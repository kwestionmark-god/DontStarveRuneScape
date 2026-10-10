namespace DontStarveRuneScape.Core;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Data;

/// <summary>
/// Player entity with smooth movement and sub-tile precision.
///
/// Movement modes:
/// - WASD: Continuous movement in pressed direction
/// - Mouse click: Move to clicked world position (stops when reached)
/// - Both modes can be used simultaneously (WASD overrides click target)
///
/// Camera panning:
/// - Arrow keys: Pan the camera view (left/right = horizontal, up/down = vertical)
/// </summary>
public sealed class Player
{
    public string Name { get; set; } = "Survivor";
    // Movement state
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float Speed { get; set; } = Constants.PlayerMovementSpeed;  // Pixels per second
    public float TargetX { get; set; }
    public float TargetY { get; set; }
    public bool Moving { get; set; }

    /// <summary>True while sprinting this frame (Shift held + moving + the
    /// stamina pool is paying for it). Owned by <see cref="UpdateSprint"/>;
    /// EffectiveSpeed applies the boost while this is set.</summary>
    public bool Sprinting { get; private set; }

    /// <summary>True while airborne in a jump hop. Owned by <see cref="UpdateJump"/>.</summary>
    public bool IsJumping => _jumpTimeRemaining > 0f;

    /// <summary>Jump arc progress 0..1 across the jump duration (0 while
    /// grounded). Drives the leap animation channels (toe-off tilt, boot
    /// tuck, body lean, landing reach) in the renderer.</summary>
    public float JumpProgress { get; private set; }

    /// <summary>Visual arc height this frame (0 on the ground, peak
    /// <see cref="Constants.JumpHeightPx"/> mid-jump). Screen-space pixels;
    /// the renderer lifts the body billboard by this amount.</summary>
    public float JumpVisualOffset { get; private set; }

    private float _jumpTimeRemaining;

    /// <summary>Horizontal facing for the carried-equipment visual:
    /// +1 right, -1 left. Follows the last horizontal move direction and the
    /// attack target.</summary>
    public float Facing { get; set; } = 1f;

    /// <summary>Last movement direction (normalized dx, dy). Used by the renderer
    /// to select the correct directional sprite (side-view vs back-view).</summary>
    public (float Dx, float Dy) LastMoveDir { get; set; } = (0f, -1f);

    /// <summary>Actual net velocity this frame (world px per second), computed
    /// by Game after all movement paths run. Drives the stepping gait.</summary>
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }

    // Subsystem references (wired by Bootstrap)
    public ActionSystem? ActionSystem { get; set; }
    public PlayerGear? Gear { get; set; }
    public SkillManager? SkillManager { get; set; }
    public Inv? Inventory { get; set; }
    public SurvivalSystem? Survival { get; set; }
    public WeatherSystem? WeatherSystem { get; set; }

    // Recruitment
    public List<string> RecruitedNpcs { get; } = [];

    // Base position for recruit behavior tracking
    public float? BaseX { get; private set; }
    public float? BaseY { get; private set; }

    // Progression
    public HashSet<string> UnlockedRecipes { get; } = [];
    public HashSet<string> UnlockedGear { get; } = [];

    /// <summary>
    /// Initialize player at world position.
    /// </summary>
    public Player(float worldX, float worldY)
    {
        WorldX = worldX;
        WorldY = worldY;
        TargetX = worldX;
        TargetY = worldY;
    }

    /// <summary>
    /// Set a click-to-move target.
    /// </summary>
    public void MoveTo(float worldX, float worldY)
    {
        TargetX = worldX;
        TargetY = worldY;
        Moving = true;
    }

    /// <summary>
    /// Handle WASD keyboard movement.
    /// Normalizes diagonal movement to maintain constant speed.
    /// Movement is relative to the camera's yaw — WASD directions
    /// rotate with the camera so "forward" always matches the
    /// camera's viewing direction.
    /// Clamps to map bounds. Arrow keys control camera rotation
    /// (left/right = orbit, up/down = tilt).
    /// </summary>
    public void ApplyKeyInput(InputState inputState, float dt, float cameraYaw = 0.0f)
    {
        float dx = 0.0f, dy = 0.0f;
        if (inputState.MoveUp) dy -= 1.0f;
        if (inputState.MoveDown) dy += 1.0f;
        if (inputState.MoveLeft) dx -= 1.0f;
        if (inputState.MoveRight) dx += 1.0f;

        // Normalize diagonal movement
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length > 0)
        {
            dx /= length;
            dy /= length;
        }

        // Rotate movement vector by camera yaw so WASD is camera-relative.
        // The camera's yaw rotates the *view* around the player; to move
        // "forward" in camera space we rotate the input vector by -yaw
        // (the inverse rotation) so the player moves in the direction the
        // camera is looking, not in screen-space directions.
        if (cameraYaw != 0.0f)
        {
            float cy = MathF.Cos(-cameraYaw);
            float sy = MathF.Sin(-cameraYaw);
            float rotatedX = dx * cy - dy * sy;
            float rotatedY = dx * sy + dy * cy;
            dx = rotatedX;
            dy = rotatedY;
        }

        WorldX += dx * EffectiveSpeed * dt;
        WorldY += dy * EffectiveSpeed * dt;

        // Facing follows the last horizontal move direction.
        if (dx > 0.001f) Facing = 1f;
        else if (dx < -0.001f) Facing = -1f;
        // Track movement direction for directional sprites
        if (length > 0)
            LastMoveDir = (dx, dy);

        // Clamp to map bounds
        float maxX = Constants.MapWidth * Constants.TileSize;
        float maxY = Constants.MapHeight * Constants.TileSize;
        WorldX = Math.Clamp(WorldX, 0.0f, maxX);
        WorldY = Math.Clamp(WorldY, 0.0f, maxY);
    }

    /// <summary>
    /// Sprint gate — call once per frame BEFORE ApplyKeyInput so movement
    /// this frame already carries the boost. Holding Shift while moving
    /// drains the shared gathering StaminaPool continuously for a
    /// SprintSpeedMultiplier speed boost; an empty pool trips the pool's
    /// exhausted rest, which gates sprinting until it lifts. Standing still
    /// or releasing the key costs nothing.
    /// </summary>
    public void UpdateSprint(bool sprintHeld, bool moving, float dt)
    {
        var pool = ActionSystem?.Stamina;
        bool wasSprinting = Sprinting;
        // agility.sprint_cost: invested points cut the drain −5%/pt,
        // capped at 75% (raw read — 0 points = exact legacy drain).
        float sprintCut = Math.Min(0.75f,
            (SkillManager?.GetSubStatPoints("agility", "sprint_cost") ?? 0f) * 0.05f);
        Sprinting = sprintHeld && moving && dt > 0f
            && pool is { IsExhausted: false }
            && pool.Consume(Constants.SprintStaminaDrainPerSecond * dt * (1f - sprintCut));
        // The sprint ran the pool dry → forced rest. Fire once on the
        // sprinting→exhausted edge only, so a held key does not spam it
        // every frame.
        if (wasSprinting && !Sprinting && pool is { IsExhausted: true })
            ActionSystem!.AddNotification("You are too exhausted to sprint. Rest a moment.",
                (255, 170, 60));

        // Agility XP trickles in per second of actual sprinting (RS-style
        // movement skilling; the OSRS curve keeps early levels quick and
        // late levels a long-haul earned climb).
        if (Sprinting)
            AwardAgilityXp(Constants.AgilityXpPerSprintSecond * dt);
    }

    /// <summary>Try a jump hop: pays <see cref="Constants.JumpStaminaCost"/>
    /// from the shared stamina pool, starts the visual arc, and awards agility
    /// XP. Fails (no cost, no XP) while already airborne or when the pool is
    /// exhausted.</summary>
    public bool TryJump()
    {
        if (IsJumping) return false;
        var pool = ActionSystem?.Stamina;
        // agility.jump_cost: invested points cut the cost −5%/pt, capped
        // at 75% (raw read — 0 points = the exact legacy cost).
        float jumpCut = Math.Min(0.75f,
            (SkillManager?.GetSubStatPoints("agility", "jump_cost") ?? 0f) * 0.05f);
        if (pool == null || !pool.Consume(Constants.JumpStaminaCost * (1f - jumpCut)))
            return false;

        _jumpTimeRemaining = Constants.JumpDuration;
        JumpVisualOffset = 0f;
        JumpProgress = 0f;
        AwardAgilityXp(Constants.AgilityXpPerJump);
        return true;
    }

    /// <summary>Advance the jump arc. Call once per frame while playing.</summary>
    public void UpdateJump(float dt)
    {
        if (!IsJumping) return;
        _jumpTimeRemaining -= dt;
        if (_jumpTimeRemaining <= 0f)
        {
            _jumpTimeRemaining = 0f;
            JumpVisualOffset = 0f;
            JumpProgress = 0f;
            return;
        }
        // Sine arc: 0 → JumpHeightPx → 0 across the jump duration.
        float t = 1f - _jumpTimeRemaining / Constants.JumpDuration; // 0..1
        JumpVisualOffset = MathF.Sin(t * MathF.PI) * Constants.JumpHeightPx;
        JumpProgress = t;
    }

    /// <summary>Forward agility XP through the leveling path and push any
    /// level-up messages to the notification channel.</summary>
    private void AwardAgilityXp(float xp)
    {
        if (SkillManager == null || xp <= 0f) return;
        foreach (var message in SkillManager.AddXpWithNotification("agility", xp))
            ActionSystem?.AddNotification(message, (255, 215, 0));
    }

    /// <summary>
    /// Move toward click-to-move target position.
    /// Called every frame. Does nothing if not moving.
    /// Stops when within 2px of target.
    /// </summary>
    public void Update(float dt)
    {
        if (!Moving) return;

        float dx = TargetX - WorldX;
        float dy = TargetY - WorldY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        // Facing follows the click-to-move direction.
        if (dx > 0.001f) Facing = 1f;
        else if (dx < -0.001f) Facing = -1f;

        if (distance < 2.0f)  // Reached target (2px tolerance)
        {
            Moving = false;
            return;
        }

        // Move toward target at full speed
        dx /= distance;
        dy /= distance;
        WorldX += dx * EffectiveSpeed * dt;
        WorldY += dy * EffectiveSpeed * dt;

        // Track movement direction for directional sprites
        LastMoveDir = (dx, dy);

        // Clamp to map bounds
        float maxX = Constants.MapWidth * Constants.TileSize;
        float maxY = Constants.MapHeight * Constants.TileSize;
        WorldX = Math.Clamp(WorldX, 0.0f, maxX);
        WorldY = Math.Clamp(WorldY, 0.0f, maxY);
    }

    /// <summary>
    /// Return movement speed with the sprint boost and weather modifier applied.
    /// </summary>
    public float EffectiveSpeed
    {
        get
        {
            float baseSpeed = Speed;
            if (Sprinting)
                baseSpeed *= Constants.SprintSpeedMultiplier;
            // Agility: +1% movement speed per level above 1 — the
            // long-term earned bonus for all the sprinting and jumping.
            int agility = SkillManager?.GetSkillLevel("agility") ?? 1;
            if (agility > 1)
                baseSpeed *= 1f + (agility - 1) * Constants.AgilitySprintSpeedBonusPerLevel;
            if (WeatherSystem != null)
            {
                var effects = WeatherSystem.GetEffects();
                if (effects.TryGetValue("movement_speed", out var mult))
                    baseSpeed *= mult;
            }
            return baseSpeed;
        }
    }

    /// <summary>
    /// Get the current tile position (grid coordinates).
    /// </summary>
    public (int TileX, int TileY) GetTilePosition()
    {
        int tileSize = Constants.TileSize;
        return ((int)(WorldX / tileSize), (int)(WorldY / tileSize));
    }

    /// <summary>
    /// Calculate distance to a world position.
    /// </summary>
    public float DistanceTo(float otherX, float otherY)
    {
        float dx = WorldX - otherX;
        float dy = WorldY - otherY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Check if player is within radius of a world position.
    /// </summary>
    public bool IsNear(float otherX, float otherY, float radius)
    {
        return DistanceTo(otherX, otherY) <= radius;
    }

    /// <summary>
    /// Apply damage to the player via the survival system.
    /// </summary>
    public bool TakeDamage(float amount)
    {
        return Survival?.TakeDamage(amount) ?? false;
    }

    /// <summary>
    /// Add an NPC to the player's recruited crew.
    /// </summary>
    public void AddRecruit(string npcId)
    {
        if (!RecruitedNpcs.Contains(npcId))
            RecruitedNpcs.Add(npcId);
    }

    /// <summary>
    /// Remove an NPC from the player's recruited crew.
    /// </summary>
    public void RemoveRecruit(string npcId)
    {
        RecruitedNpcs.Remove(npcId);
    }

    /// <summary>
    /// Set the player's base position for recruit behavior tracking.
    /// </summary>
    public void SetBasePosition(float x, float y)
    {
        BaseX = x;
        BaseY = y;
    }

    /// <summary>
    /// Find the nearest interactable resource node near the player.
    /// Searches the visible tile area for resource nodes that the player
    /// can interact with (not depleted, within range).
    /// </summary>
    public (ResourceNode? Node, int TileX, int TileY, float NodeWorldX, float NodeWorldY)
        FindInteractableResource(TileMap world, float radius = 128.0f)
    {
        if (world == null)
            return (null, 0, 0, 0.0f, 0.0f);

        var (tx, ty) = GetTilePosition();
        (ResourceNode? Node, int TileX, int TileY, float NodeWorldX, float NodeWorldY)? best = null;
        float bestDist = radius;
        int tileSize = Constants.TileSize;

        // Search in a small radius around the player
        int searchRadiusTiles = (int)(radius / tileSize) + 2;

        for (int dx = -searchRadiusTiles; dx <= searchRadiusTiles; dx++)
        {
            for (int dy = -searchRadiusTiles; dy <= searchRadiusTiles; dy++)
            {
                int nx = tx + dx;
                int ny = ty + dy;
                var tile = world.GetTile(nx, ny);
                if (tile == null) continue;

                var node = tile.ResourceNode;
                if (node == null) continue;
                if (node.IsDepleted) continue;

                // Calculate center of tile
                float nodeX = nx * tileSize + tileSize / 2f;
                float nodeY = ny * tileSize + tileSize / 2f;
                float dist = DistanceTo(nodeX, nodeY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = (node, nx, ny, nodeX, nodeY);
                }
            }
        }

        return best ?? (null, 0, 0, 0.0f, 0.0f);
    }
}
