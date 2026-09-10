namespace DontStarveRuneScape.Core;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.World;

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
    // Movement state
    public float WorldX { get; private set; }
    public float WorldY { get; private set; }
    public float Speed { get; set; } = Constants.PlayerMovementSpeed;  // Pixels per second
    public float TargetX { get; private set; }
    public float TargetY { get; private set; }
    public bool Moving { get; private set; }

    // Subsystem references (wired by Bootstrap)
    public ActionSystem? ActionSystem { get; set; }
    public PlayerGear? Gear { get; set; }
    public SkillManager? SkillManager { get; set; }
    public Inventory.Inventory? Inventory { get; set; }
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

        // Clamp to map bounds
        float maxX = Constants.MapWidth * Constants.TileSize;
        float maxY = Constants.MapHeight * Constants.TileSize;
        WorldX = Math.Clamp(WorldX, 0.0f, maxX);
        WorldY = Math.Clamp(WorldY, 0.0f, maxY);
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

        // Clamp to map bounds
        float maxX = Constants.MapWidth * Constants.TileSize;
        float maxY = Constants.MapHeight * Constants.TileSize;
        WorldX = Math.Clamp(WorldX, 0.0f, maxX);
        WorldY = Math.Clamp(WorldY, 0.0f, maxY);
    }

    /// <summary>
    /// Return movement speed with weather modifier applied.
    /// </summary>
    public float EffectiveSpeed
    {
        get
        {
            float baseSpeed = Speed;
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
                if (node.IsDepleted && node.RegrowTime <= 0) continue;

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