namespace DontStarveRuneScape.Render;

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Skills.Firemaking;
using SkiaSharp;
using Silk.NET.OpenGL;

/// <summary>
/// SpriteRenderer — Loads sprite textures from Assets/sprites/ and renders
/// resources, player, monsters, NPCs, structures, and fires on the world.
/// Each unique sprite key is loaded once and cached; repeated draws reuse the
/// uploaded OpenGL texture.
/// </summary>
public sealed class SpriteRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly string _spritesDir;

    // textureId -> (width, height) in pixels
    private readonly Dictionary<string, (uint Texture, int W, int H)> _cache = new();

    private bool _disposed;

    public SpriteRenderer(GL gl)
    {
        _gl = gl;
        string baseDir = AppContext.BaseDirectory;
        _spritesDir = System.IO.Path.Combine(baseDir, "Assets", "sprites");
        if (!System.IO.Directory.Exists(_spritesDir))
            _spritesDir = baseDir;
    }

    // ─── Resource rendering ───────────────────────────────────────────────

    public void RenderResource(ResourceNode resource, PrimitiveBatch batch, Camera camera, float elevation, int tileX, int tileY, World.Tile? tile = null)
    {
        if (resource.IsDepleted && resource.ResourceDef?.DisappearsWhenDepleted == true)
            return;

        float half = 24f
            * (resource.ResourceDef?.DisplayScale > 0 ? resource.ResourceDef.DisplayScale : FallbackScale(resource))
            * resource.SizeScale
            * camera.Zoom;

        // Ground point at tile center — bottom-anchored billboard (same scheme
        // as the player sprite) so the resource keeps its tile placement under
        // any camera yaw/pitch instead of sliding around the tile center.
        var screen = camera.WorldToScreen(
            tileX * Constants.TileSize + Constants.TileSize * 0.5f,
            tileY * Constants.TileSize + Constants.TileSize * 0.5f,
            elevation);
        float cx = screen.X;
        float cy = screen.Y - half;

        var tier = camera.Tier;
        if (tier == Camera.LodTier.Far)
        {
            // Far: no textures, no texture binds — a single tiny dot carries
            // the tile's occupancy. 'Ubiquitous' filler isn't worth drawing.
            if (resource.ResourceDef?.Rarity == "ubiquitous") return;
            float dot = 3f * camera.Zoom;
            batch.DrawScreenQuad(cx, screen.Y, dot, dot, 200, 210, 160);
            return;
        }

        string spriteKey = resource.GetSpriteKey();
        uint tex = 0;
        // Deterministic per-tile variant: try "key_v0..v2" sprites (if artist
        // files ever exist); the negative cache makes misses free after first check.
        if (resource.ResourceDef != null && !resource.IsDepleted)
        {
            int variant = (tileX * 73856093 ^ tileY * 19349663) & 0x7fffffff % 3;
            uint vt = GetSpriteTexture($"{spriteKey}_v{variant}");
            if (vt != 0) { spriteKey = $"{spriteKey}_v{variant}"; tex = vt; }
        }
        if (tex == 0)
            tex = GetSpriteTexture(spriteKey);
        // Some resource families do not have a bespoke depleted/young image.
        // Reuse the intact node artwork instead of drawing the colored fallback.
        if (tex == 0 && resource.ResourceDef is { } resourceDef && spriteKey != resourceDef.SpriteKey)
            tex = GetSpriteTexture(resourceDef.SpriteKey);

        // Ground-decal resources (water pools etc.) are drawn flat on their
        // tile's projected footprint — never as billboards.
        if (resource.ResourceDef?.GroundDecal == true && tile != null && tex != 0)
        {
            float ts = Constants.TileSize;
            const float inset = 0.08f; // keep the sprite just inside the tile
            var c00 = camera.WorldToScreen((tileX + inset) * ts, (tileY + inset) * ts, tile.CornerElevations?[0] ?? tile.Elevation);
            var c10 = camera.WorldToScreen((tileX + 1 - inset) * ts, (tileY + inset) * ts, tile.CornerElevations?[1] ?? tile.Elevation);
            var c11 = camera.WorldToScreen((tileX + 1 - inset) * ts, (tileY + 1 - inset) * ts, tile.CornerElevations?[2] ?? tile.Elevation);
            var c01 = camera.WorldToScreen((tileX + inset) * ts, (tileY + 1 - inset) * ts, tile.CornerElevations?[3] ?? tile.Elevation);
            batch.DrawScreenQuadCornersTextured(c00.X, c00.Y, c10.X, c10.Y, c11.X, c11.Y, c01.X, c01.Y, tex, 255, 255, 255, (byte)220);
            return;
        }

        // Soft shadow grounds every sprite (Nearest only — dots don't cast,
        // and neither does a flat ground decal).
        if (tier == Camera.LodTier.Nearest && !resource.IsDepleted)
            DrawShadow(batch, cx, screen.Y, half, 200);
        if (tex != 0)
        {
            // Mid: sprites smaller than ~6 px half-size degrade to dots —
            // the texture fetch costs more than the detail is worth.
            if (tier == Camera.LodTier.Mid && half < 6f)
            {
                batch.DrawScreenQuad(cx, screen.Y, 3f * camera.Zoom, 3f * camera.Zoom, 200, 210, 160);
                return;
            }
            // Straight-alpha white texture; tint via the batch shader.
            batch.DrawTexturedScreenQuad(cx, cy, half, half, tex, 255, 255, 255);
        }
        else
        {
            // Fallback colored quad.
            (byte r, byte g, byte b) color = resource.IsDepleted
                ? ((byte)120, (byte)120, (byte)120)
                : resource.GrowthStage == 1
                    ? ((byte)150, (byte)220, (byte)120)
                    : ((byte)90, (byte)160, (byte)70);
            batch.DrawScreenQuad(cx, cy, half, half, color.r, color.g, color.b);
        }
    }

    /// <summary>
    /// Fallback size scale by sprite family when the JSON has no display_scale.
    /// Trees read larger than shrubs and smalls.
    /// </summary>
    private static float FallbackScale(ResourceNode resource)
    {
        string key = resource.ResourceDef?.SpriteKey ?? string.Empty;
        if (key.StartsWith("trees/")) return 2.2f;
        if (key.StartsWith("rocks/")) return 1.25f;
        if (key.StartsWith("world/")) return 0.75f;
        return 1f;
    }

    // ─── Player / monster / NPC / structure / fire ────────────────────────

    private float _playerAnimTime;

    /// <summary>Optional terrain-height sampler (world px → elevation) used to
    /// anchor each foot at its own ground height. Set by Game each frame.</summary>
    public Func<float, float, float>? BootElevation { get; set; }

    // ── Procedural stepping: planted/swing state machine per foot ────────
    // The gait lives in GaitAnimator (reusable, config-driven: biped /
    // quadruped diagonal pairs / none); SpriteRenderer holds one rig per
    // entity and draws its foot quads. See GaitConfigs for the per-entity
    // layouts and tunables.
    private const float SwimDepth = 1.2f;   // waterDepth at which swimming replaces stepping
    private const float KickPx = 3f;        // swim flutter-kick amplitude, screen px
    private readonly ConditionalWeakTable<object, GaitAnimator> _gaitRigs = new();
    private readonly GaitAnimator _playerGait = new(GaitConfigs.Player);

    /// <summary>The gait rig attached to an entity (player, monster, NPC):
    /// created on first use and kept alive while the entity lives.</summary>
    private GaitAnimator GetGaitRig(object entity, GaitConfig cfg) =>
        _gaitRigs.GetValue(entity, _ => new GaitAnimator(cfg));

    /// <summary>Draw an entity's foot quads at their gait positions: each foot
    /// samples its own terrain elevation and lifts on the sine of its swing
    /// arc. <paramref name="inFront"/> selects which side of the body to draw
    /// for: feet projecting at or below the body ground point are in front
    /// (drawn after the body), feet above it are behind (drawn before) — so a
    /// foot on the far side never paints over the torso.</summary>
    private void DrawGaitFeet(GaitAnimator gait, GaitConfig cfg, float footHalf, uint footTex,
        PrimitiveBatch batch, Camera camera, float elevation, float groundScreenY, bool inFront)
    {
        for (int i = 0; i < gait.FootCount; i++)
        {
            ref var foot = ref gait.GetFoot(i);
            float fe = BootElevation?.Invoke(foot.X, foot.Y) ?? elevation;
            var fs = camera.WorldToScreen(foot.X, foot.Y, fe);
            // Screen Y grows downward (toward the camera): in front = at or
            // below the ground point. Compare before applying the lift arc.
            if ((fs.Y >= groundScreenY) != inFront)
                continue;
            float lift = foot.Swinging ? MathF.Sin(foot.T * MathF.PI) * cfg.LiftPx * camera.Zoom : 0f;
            batch.DrawTexturedScreenQuad(fs.X, fs.Y - footHalf - lift,
                footHalf, footHalf, footTex, 255, 255, 255);
        }
    }

    // ── Next-level player boots: tapered world-space shoe boxes ──────────
    // Each boot is two stacked boxes (a wide foot base + a narrower ankle
    // stump — the taper). Every corner lives in world space and passes
    // through the camera projection, so the boot reads as a real 3D object
    // at any yaw. The base box's bottom corners sample the terrain
    // individually, so the sole lies flush on slopes. The swing phase shears
    // the tops along travel (toe-off drag → level → heel-strike) for the
    // kick/plant/launch feel, and each visible face is lit by a fixed
    // world-space light with a darker outline behind it.

    /// <summary>Foot-box dimensions in world px (per entity type later;
    /// player values for now).</summary>
    private readonly struct FootBoxDims
    {
        public readonly float Toe, Heel, HalfWidth, BaseHeight, AnkleToe, AnkleHeel, AnkleHalfWidth, AnkleHeight;
        public FootBoxDims(float toe, float heel, float halfWidth, float baseHeight,
            float ankleToe, float ankleHeel, float ankleHalfWidth, float ankleHeight)
        {
            Toe = toe; Heel = heel; HalfWidth = halfWidth; BaseHeight = baseHeight;
            AnkleToe = ankleToe; AnkleHeel = ankleHeel; AnkleHalfWidth = ankleHalfWidth; AnkleHeight = ankleHeight;
        }
    }

    // Player boot: short wide sole, boot toe biased forward of the ankle.
    private static readonly FootBoxDims PlayerBoot = new(
        toe: 3.4f, heel: 2.2f, halfWidth: 2.3f, baseHeight: 1.8f,
        ankleToe: 1.8f, ankleHeel: 1.6f, ankleHalfWidth: 1.7f, ankleHeight: 3.2f);

    // Swing tilt: max top-edge shear, as a tangent. ±~20° at the ankle top.
    private const float BootTiltTan = 0.36f;
    // Fixed world-space light for the face shading (xy + up component).
    private const float LightX = 0.45f, LightY = 0.30f, LightZ = 1.00f;

    // Pooled face buffers (drawn many times per frame).
    private readonly System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)> _facePts = new(4);
    private readonly System.Collections.Generic.List<Silk.NET.Maths.Vector2D<float>> _outlinePts = new(4);

    /// <summary>Draw one entity's feet as projected shoe boxes. Same depth
    /// contract as <see cref="DrawGaitFeet"/>: <paramref name="inFront"/>
    /// selects feet whose ground contact is at/below (true) or above
    /// (false) the body's ground point, so far feet paint under the torso.
    /// Uses the gait's per-foot positions, swing phase (tilt + lift), and
    /// travel direction.</summary>
    private void DrawFootBoxes(GaitAnimator gait, GaitConfig cfg, in FootBoxDims dims,
        PrimitiveBatch batch, Camera camera, float elevation, float groundScreenY, bool inFront,
        byte baseR, byte baseG, byte baseB)
    {
        var (dirX, dirY) = gait.Dir;
        float latX = -dirY, latY = dirX;
        // World direction that maps to screen-down = toward the viewer for
        // ground-plane faces: a side face is visible when its outward
        // normal has a positive dot with it.
        float viewX = MathF.Sin(camera.Yaw), viewY = MathF.Cos(camera.Yaw);
        bool frontVisible = dirX * viewX + dirY * viewY > 0f;
        bool latVisible = latX * viewX + latY * viewY > 0f;

        // Face brightness from the fixed light: sides dot into the light's
        // xy component, the top face into its dominant up component.
        float lightLen = MathF.Sqrt(LightX * LightX + LightY * LightY + LightZ * LightZ);
        float Shade(float nx, float ny, float nz)
        {
            float d = (nx * LightX + ny * LightY + nz * LightZ) / lightLen;
            return 0.50f + 0.55f * MathF.Max(0f, d);
        }

        for (int i = 0; i < gait.FootCount; i++)
        {
            ref var foot = ref gait.GetFoot(i);
            float fe = BootElevation?.Invoke(foot.X, foot.Y) ?? elevation;
            var ground = camera.WorldToScreen(foot.X, foot.Y, fe);
            if ((ground.Y >= groundScreenY) != inFront)
                continue;
            float lift = foot.Swinging ? MathF.Sin(foot.T * MathF.PI) * cfg.LiftPx * camera.Zoom : 0f;
            float tilt = foot.Swinging ? GaitAnimator.SwingTilt(foot.T) : 0f;

            // Sole box: bottom corners at their own terrain heights.
            DrawShoeBox(batch, camera, dims.Toe, dims.Heel, dims.HalfWidth, dims.BaseHeight,
                dims.AnkleToe, dims.AnkleHeel, dims.AnkleHalfWidth, dims.AnkleHeight,
                foot.X, foot.Y, dirX, dirY, latX, latY, elevation,
                tilt, lift, frontVisible, latVisible, Shade,
                baseR, baseG, baseB);
        }
    }

    /// <summary>Project and draw one foot's two stacked boxes. The sole's
    /// bottom corners sample the terrain at their own positions (sole lies
    /// flush on the slope); the ankle sits on the sole's top corners. Tops
    /// shear by tilt×BootTiltTan×corner-height along the travel direction.
    /// Visible faces: two camera-facing sides + top, back-face culled by
    /// the precomputed visibility flags.</summary>
    private void DrawShoeBox(PrimitiveBatch batch, Camera camera,
        float toe, float heel, float halfW, float baseH,
        float ankToe, float ankHeel, float ankHalfW, float ankH,
        float cx, float cy, float dirX, float dirY, float latX, float latY, float fallbackElev,
        float tilt, float lift, bool frontVisible, bool latVisible,
        Func<float, float, float, float> shade, byte baseR, byte baseG, byte baseB)
    {
        // Perimeter order (clockwise on the ground plane):
        // 0: +toe +lat | 1: +toe −lat | 2: −heel −lat | 3: −heel +lat.
        Span<float> lon = stackalloc float[4] { toe, toe, -heel, -heel };
        Span<float> lat = stackalloc float[4] { halfW, -halfW, -halfW, halfW };

        // Project all bottom + top corners of both boxes. Ground elevation
        // per bottom corner makes the sole follow the tilt of the terrain.
        Span<float> bx = stackalloc float[4], by = stackalloc float[4];
        Span<float> groundElev = stackalloc float[4];
        for (int c = 0; c < 4; c++)
        {
            bx[c] = cx + dirX * lon[c] + latX * lat[c];
            by[c] = cy + dirY * lon[c] + latY * lat[c];
            groundElev[c] = BootElevation?.Invoke(bx[c], by[c]) ?? fallbackElev;
        }

        // screen corners: sBase[k] = sole top, sBtm[k] = sole bottom,
        // sAnk[k] = ankle top. Shear grows with corner height; lift applies
        // to every projected point of this foot.
        var sBtm = new (float X, float Y)[4];
        var sBase = new (float X, float Y)[4];
        var sAnk = new (float X, float Y)[4];
        for (int c = 0; c < 4; c++)
        {
            var pb = camera.WorldToScreen(bx[c], by[c], groundElev[c]);
            sBtm[c] = (pb.X, pb.Y - lift);
            float baseShear = tilt * BootTiltTan * baseH;
            var pt = camera.WorldToScreen(bx[c] + dirX * baseShear, by[c] + dirY * baseShear,
                groundElev[c] + baseH);
            sBase[c] = (pt.X, pt.Y - lift);
            float ankShear = tilt * BootTiltTan * (baseH + ankH);
            var pa = camera.WorldToScreen(bx[c] + dirX * ankShear, by[c] + dirY * ankShear,
                groundElev[c] + baseH + ankH);
            sAnk[c] = (pa.X, pa.Y - lift);
        }

        // Faces: two visible sides + top, in painter order (sides then top).
        // Sole box sides: indices pair (c, c+1 mod 4) with the right winding
        // from the perimeter; bottom corners first so the face gradient
        // darkens toward the ground.
        for (int s = 0; s < 2; s++)
        {
            int c0, c1;         // perimeter edge for this face
            float nx, ny;
            bool visible;
            if (s == 0)
            {
                // +lon (toe) or −lon (heel) face: perimeter edges 0-1 / 2-3.
                if (frontVisible) { c0 = 0; c1 = 1; nx = dirX; ny = dirY; }
                else { c0 = 2; c1 = 3; nx = -dirX; ny = -dirY; }
                visible = true;
            }
            else
            {
                // +lat or −lat face: perimeter edges 1-2 / 3-0.
                if (latVisible) { c0 = 3; c1 = 0; nx = latX; ny = latY; }
                else { c0 = 1; c1 = 2; nx = -latX; ny = -latY; }
                visible = true;
            }
            float bright = shade(nx, ny, 0f);
            // Sole side face spans bottom → sole top.
            DrawBoxFace(batch, sBtm[c0], sBtm[c1], sBase[c1], sBase[c0], bright, baseR, baseG, baseB);
            // Ankle side face spans sole top → ankle top (slightly inset
            // footprint, but the same edge directions — the taper reads as
            // the ankle's narrower silhouette overhanging nothing).
            DrawBoxFace(batch, sBase[c0], sBase[c1], sAnk[c1], sAnk[c0], bright, baseR, baseG, baseB);
        }

        // Top faces last (they overlap the sides' upper edges).
        float topBright = shade(0f, 0f, 1f);
        DrawBoxFace(batch, sBase[0], sBase[1], sBase[2], sBase[3], topBright, baseR, baseG, baseB);
        DrawBoxFace(batch, sAnk[0], sAnk[1], sAnk[2], sAnk[3], topBright, baseR, baseG, baseB);
    }

    /// <summary>Draw one quad face: an expanded dark outline behind, then a
    /// vertical gradient (dark toward the bottom edge, bright toward the
    /// top) tinted from the boot base color by the face brightness.</summary>
    private void DrawBoxFace(PrimitiveBatch batch,
        (float X, float Y) b0, (float X, float Y) b1,
        (float X, float Y) t1, (float X, float Y) t0,
        float bright, byte baseR, byte baseG, byte baseB)
    {
        // Outline: same quad scaled ~30% about its centroid, flat dark.
        float mx = (b0.X + b1.X + t1.X + t0.X) * 0.25f;
        float my = (b0.Y + b1.Y + t1.Y + t0.Y) * 0.25f;
        const float expand = 1.30f;
        _outlinePts.Clear();
        _outlinePts.Add(new(b0.X + (b0.X - mx) * expand, b0.Y + (b0.Y - my) * expand));
        _outlinePts.Add(new(b1.X + (b1.X - mx) * expand, b1.Y + (b1.Y - my) * expand));
        _outlinePts.Add(new(t1.X + (t1.X - mx) * expand, t1.Y + (t1.Y - my) * expand));
        _outlinePts.Add(new(t0.X + (t0.X - mx) * expand, t0.Y + (t0.Y - my) * expand));
        batch.DrawScreenPolygon(_outlinePts, 42, 27, 13, 255);

        // Gradient face: bottom corners dimmer, top corners brighter.
        byte rLo = (byte)Math.Clamp(baseR * bright * 0.92f, 0, 255);
        byte gLo = (byte)Math.Clamp(baseG * bright * 0.92f, 0, 255);
        byte bLo = (byte)Math.Clamp(baseB * bright * 0.92f, 0, 255);
        byte rHi = (byte)Math.Clamp(baseR * bright * 1.06f, 0, 255);
        byte gHi = (byte)Math.Clamp(baseG * bright * 1.06f, 0, 255);
        byte bHi = (byte)Math.Clamp(baseB * bright * 1.06f, 0, 255);
        _facePts.Clear();
        _facePts.Add((b0.X, b0.Y, rLo, gLo, bLo, 255));
        _facePts.Add((b1.X, b1.Y, rLo, gLo, bLo, 255));
        _facePts.Add((t1.X, t1.Y, rHi, gHi, bHi, 255));
        _facePts.Add((t0.X, t0.Y, rHi, gHi, bHi, 255));
        batch.DrawScreenPolygonGradient(_facePts);
    }

    // Swing arc for the carried weapon-slot item (attack/chop/mine). Purely
    // visual state: TriggerPlayerSwing starts the arc, RenderPlayer animates
    // it over the duration.
    private float _swingRemaining;
    private float _swingTotal = 1f;

    public void TriggerPlayerSwing(float duration = 0.3f)
    {
        _swingRemaining = duration;
        _swingTotal = duration;
    }

    public void RenderPlayer(Player player, PrimitiveBatch batch, Camera camera, float elevation, float dt, float waterDepth = 0f)
    {
        _playerAnimTime += dt;
        _swingRemaining = MathF.Max(0f, _swingRemaining - dt);
        // Arc progress 0→1 across the swing; 0 when resting.
        float swing = _swingRemaining > 0f ? 1f - _swingRemaining / _swingTotal : 0f;
        // The gait reacts to actual net velocity (WASD + click-to-move combined),
        // so feet track real body motion and plant when the body is still.
        float velX = player.VelocityX, velY = player.VelocityY;
        float speed = MathF.Sqrt(velX * velX + velY * velY);
        bool moving = speed > 1f;
        // Walk cycles run a bit faster than the idle bob.
        float frameDuration = moving ? 0.12f : 0.3f;
        int frame = (int)(_playerAnimTime / frameDuration) % 4;

        // 8-directional sprite selection based on movement angle.
        // Directions (clockwise from South): 0=S, 1=SE, 2=E, 3=NE, 4=N, 5=NW, 6=W, 7=SW
        var (mdx, mdy) = player.LastMoveDir;

        uint bodyTex = GetSpriteTexture("player/body");
        uint bootTex = GetSpriteTexture("player/boot");
        // Legacy single-sprite fallback: idle_/walk_ frames.
        uint tex = bodyTex != 0 ? 0 : GetSpriteTexture((moving ? "player/walk_" : "player/idle_") + frame);
        // Ground point: the world position is the player's feet.
        var screen = camera.WorldToScreen(player.WorldX, player.WorldY, elevation);
        const float HalfWidth = 22f;
        float half = HalfWidth * camera.Zoom;
        // Anchor bottom-center: feet at the ground point at any zoom/pitch.
        float cx = screen.X;
        float cy = screen.Y - half;

        // Carried cape renders behind the body.
        var cape = player.Gear?.GetEquipped("cape");
        if (cape != null)
            RenderCarried(batch, cape, cx, cy, half, player.Facing, behind: true, swing: 0f);

        DrawShadow(batch, screen.X, screen.Y, half, 220);

        if (bodyTex != 0)
        {
            // Procedural stepping: each boot plants at a fixed world spot while
            // the body moves; once left behind it swings forward, arcs up, and
            // lands at its own sampled terrain elevation. At rest both feet
            // plant on the heightmap, contouring slopes. Far-side feet draw
            // before the body quad, near-side feet after, so a boot on the far
            // side never paints over the torso. The player's boots are drawn
            // as projected world-space shoe boxes (tapered, terrain-tilted,
            // phase-tilted, face-shaded) — see DrawFootBoxes.
            bool swimming = waterDepth >= SwimDepth && bootTex != 0;
            float bootHalf = half * (4f / 32f);
            if (!swimming)
            {
                _playerGait.Update(player.WorldX, player.WorldY, velX, velY, dt);
                // Boot base color matches the player/boot.png leather tone.
                DrawFootBoxes(_playerGait, GaitConfigs.Player, PlayerBoot,
                    batch, camera, elevation, screen.Y, inFront: false, 139, 90, 43);
            }

            batch.DrawTexturedScreenQuad(cx, cy, half, half, bodyTex, 255, 255, 255);

            if (bootTex != 0 && swimming)
            {
                // Swimming: the feet can't reach the bottom, so the gait
                // stops. Boots dangle under the body at the body's own
                // elevation and flutter-kick while moving; pinning them to
                // the stance keeps the gait sane when back on land.
                float pa = mdx == 0f && mdy == 0f ? MathF.PI / 2f : MathF.Atan2(mdy, mdx);
                float px = -MathF.Sin(pa), py = MathF.Cos(pa);  // left-perpendicular
                float kick = moving
                    ? MathF.Sin(_playerAnimTime * 10f) * KickPx * camera.Zoom
                    : 0f;
                _playerGait.PinToStance(player.WorldX, player.WorldY, px, py);
                for (int i = 0; i < _playerGait.FootCount; i++)
                {
                    ref var foot = ref _playerGait.GetFoot(i);
                    var fos = camera.WorldToScreen(foot.X, foot.Y, elevation);
                    // Left/right boots kick opposite (index 0 = left).
                    float side = i == 0 ? -1f : 1f;
                    batch.DrawTexturedScreenQuad(fos.X, fos.Y - bootHalf - kick * side,
                        bootHalf, bootHalf, bootTex, 255, 255, 255);
                }
            }
            else if (!swimming)
            {
                DrawFootBoxes(_playerGait, GaitConfigs.Player, PlayerBoot,
                    batch, camera, elevation, screen.Y, inFront: true, 139, 90, 43);
            }
        }
        else if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(cx, cy, half, half, tex, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(cx, cy, half, half, 60, 120, 220);
        }

        // Carried equipment in front of the body: the weapon-slot item at the
        // leading hand (swinging during actions), armor overlays by slot.
        var gear = player.Gear;
        if (gear != null)
        {
            if (gear.Weapon != null)
                RenderCarried(batch, gear.Weapon, cx, cy, half, player.Facing, behind: false, swing);
            foreach (var slotName in CarriedSlots)
            {
                var item = gear.GetEquipped(slotName);
                if (item != null)
                    RenderCarried(batch, item, cx, cy, half, player.Facing, behind: false, swing: 0f);
            }
        }

        // Waterline overlay: the submerged lower body sits under a translucent
        // band of sea-colored water, taller the deeper the water.
        if (waterDepth > 0.2f)
        {
            float frac = Math.Clamp(waterDepth / 2.6f, 0f, 0.45f);
            float stripHalf = half * frac;
            byte a = (byte)Math.Clamp(45 + waterDepth * 22f, 0f, 100f);
            batch.DrawScreenQuad(cx, cy + half - stripHalf, half * 0.6f, stripHalf, 70, 130, 195, a);
        }
    }

    private static readonly string[] CarriedSlots =
        { "head", "chest", "legs", "boots", "gloves", "shield", "ammo" };

    // Mount offsets per slot, relative to the sprite center (cx, cy) at half
    // size: (dx, dy, size, baseTiltDeg). dx faces right; mirrored for facing
    // left. Hand items ride the leading hand tilted up-forward, armor overlays
    // the body, the cape hangs off the trailing side, ammo rides the back.
    private static (float Dx, float Dy, float Size, float Tilt) MountOf(string slot) => slot switch
    {
        "weapon" => (0.50f, 0.10f, 0.60f, 30f),
        "head"   => (0f,    -0.55f, 0.55f, 0f),
        "chest"  => (0f,     0.08f, 0.85f, 0f),
        "legs"   => (0f,     0.42f, 0.60f, 0f),
        "boots"  => (0f,     0.72f, 0.55f, 0f),
        "gloves" => (0.45f,  0.18f, 0.40f, 0f),
        "shield" => (-0.55f, 0.10f, 0.50f, 0f),
        "ammo"   => (-0.30f, -0.15f, 0.40f, 0f),
        _        => (0f,     0f,    0.50f, 0f),
    };

    private void RenderCarried(PrimitiveBatch batch, Data.GearItem item, float cx, float cy, float half,
        float facing, bool behind, float swing)
    {
        // Sprite: catalog display key first, then the gear.json key, then the
        // item id — GetSpriteTexture caches misses, so fallbacks are free.
        var display = Data.ItemCatalog.Get(item.Id);
        uint tex = GetSpriteTexture(display?.SpriteKey ?? item.SpriteKey);
        if (tex == 0 && item.SpriteKey != item.Id)
            tex = GetSpriteTexture(item.Id);
        if (tex == 0) return;

        var (dx, dy, size, tilt) = MountOf(item.Slot.ToLowerInvariant());
        float itemHalf = size * half;

        if (behind)
        {
            // Cape: hangs off the trailing side, mirrored with the facing.
            float bx = cx - facing * dx * half;
            DrawRotatedTexturedQuad(batch, bx, cy + dy * half, itemHalf, itemHalf, 0f, tex,
                mirror: facing < 0);
            return;
        }

        if (item.Slot.ToLowerInvariant() == "weapon")
        {
            // Hand items pivot at the grip: the sprite center sits a lever
            // arm above the hand along the item's tilted axis. The swing arc
            // sweeps the item down-forward through the target and back.
            const float SwingArcDeg = 75f;
            float angleDeg = tilt + SwingArcDeg * MathF.Sin(MathF.PI * swing);
            float angle = angleDeg * (MathF.PI / 180f) * facing;
            float pivotX = cx + facing * dx * half;
            float pivotY = cy + dy * half;
            float lever = itemHalf * 0.45f;
            float itemX = pivotX + lever * MathF.Sin(angle);
            float itemY = pivotY - lever * MathF.Cos(angle);
            if (item.Id == "torch")
            {
                // Warm, flickering halo under the flame end of the torch,
                // drawn before the item so the sprite sits on top of it.
                uint glowTex = GetSpriteTexture("fx/torch_glow");
                if (glowTex != 0)
                {
                    float flameX = itemX + itemHalf * 0.55f * MathF.Sin(angle);
                    float flameY = itemY - itemHalf * 0.55f * MathF.Cos(angle);
                    byte glowAlpha = (byte)Math.Clamp(
                        105f + 25f * MathF.Sin(_playerAnimTime * 9.3f)
                             + 8f * MathF.Sin(_playerAnimTime * 23.7f),
                        40f, 160f);
                    DrawRotatedTexturedQuad(batch, flameX, flameY,
                        itemHalf * 1.6f, itemHalf * 1.6f, 0f, glowTex, alpha: glowAlpha);
                }
            }
            DrawRotatedTexturedQuad(batch, itemX, itemY, itemHalf, itemHalf, angle, tex,
                mirror: facing < 0);
            return;
        }

        // Armor overlays: fixed slot position, mirrored with the facing.
        DrawRotatedTexturedQuad(batch, cx + facing * dx * half, cy + dy * half, itemHalf, itemHalf, 0f, tex,
            mirror: facing < 0);
    }

    /// <summary>Draw a texture as a rotated (and optionally horizontally
    /// mirrored) quad. Angle is radians in screen space (y down, positive
    /// rotates clockwise visually). Mirroring swaps the corner order so the
    /// image flips without a new shader path.</summary>
    private static void DrawRotatedTexturedQuad(PrimitiveBatch batch, float cx, float cy,
        float halfW, float halfH, float angle, uint tex, bool mirror = false, byte alpha = 255)
    {
        float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        (float X, float Y) Rot(float lx, float ly) =>
            (cx + lx * cos - ly * sin, cy + lx * sin + ly * cos);

        var bl = Rot(-halfW, halfH);
        var br = Rot(halfW, halfH);
        var tr = Rot(halfW, -halfH);
        var tl = Rot(-halfW, -halfH);

        if (mirror)
            batch.DrawScreenQuadCornersTextured(br.X, br.Y, bl.X, bl.Y, tl.X, tl.Y, tr.X, tr.Y,
                tex, 255, 255, 255, alpha);
        else
            batch.DrawScreenQuadCornersTextured(bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y,
                tex, 255, 255, 255, alpha);
    }

    public void RenderMonster(Monster monster, PrimitiveBatch batch, Camera camera,
        float elevation = 0f, float dt = 0f)
    {
        // Ground point: the world position is the monster's feet.
        var screen = camera.WorldToScreen(monster.WorldX, monster.WorldY, elevation);
        float half = 16f * camera.Zoom;
        // Gait config: quadruped side-on bodies render wider (24×16 canvas =
        // 1.5× the half); legless bodies (snake, djinn, ...) draw without feet.
        var cfg = GaitConfigs.ForMonster(monster.SpriteKey);

        // Update the gait first: its travel direction selects the sprite
        // variant and the mirror, and feet draw after the update.
        GaitAnimator? gait = null;
        uint footTex = 0;
        if (cfg != null)
        {
            gait = GetGaitRig(monster, cfg);
            gait.Update(monster.WorldX, monster.WorldY,
                monster.VelocityX, monster.VelocityY, dt);
            footTex = GetSpriteTexture(cfg.FootTextureKey);
        }

        // Directional sprite selection for quadrupeds: walking away (north)
        // shows the rear view, walking toward (south) the front view, and the
        // side-on view mirrors when facing left. Missing variants fall back to
        // the base side-on sprite.
        bool quadruped = cfg is { Pattern: GaitPattern.QuadrupedWalk };
        bool mirror = false;
        float bodyHalfW = quadruped ? 1.5f * half : half;
        uint tex = GetSpriteTexture(monster.SpriteKey);
        if (quadruped && gait != null)
        {
            var (ddx, ddy) = gait.Dir;
            // World -y is up-screen: ddy > 0 walks toward the camera (front),
            // ddy < 0 walks away (rear).
            if (ddy < -0.45f)
            {
                uint t = GetSpriteTexture(monster.SpriteKey + "_back");
                if (t != 0) { tex = t; bodyHalfW = half; }
            }
            else if (ddy > 0.45f)
            {
                uint t = GetSpriteTexture(monster.SpriteKey + "_front");
                if (t != 0) { tex = t; bodyHalfW = half; }
            }
            else if (ddx < 0f)
            {
                mirror = true;
            }
        }

        // Anchor bottom-center: feet at the ground point at any zoom/pitch.
        float cx = screen.X;
        float cy = screen.Y - half;

        DrawShadow(batch, screen.X, screen.Y, bodyHalfW, 180);

        // Far-side feet draw under the body; near-side feet after it.
        if (gait != null && footTex != 0)
            DrawGaitFeet(gait, cfg!, half * cfg!.FootSizeFrac, footTex,
                batch, camera, elevation, screen.Y, inFront: false);

        if (tex != 0)
            batch.DrawTexturedScreenQuad(cx, cy, bodyHalfW, half, tex, 255, 255, 255, 255,
                mirrorX: mirror);
        else
            batch.DrawScreenQuad(cx, cy, bodyHalfW, half, 200, 60, 60);

        if (gait != null && footTex != 0)
            DrawGaitFeet(gait, cfg!, half * cfg!.FootSizeFrac, footTex,
                batch, camera, elevation, screen.Y, inFront: true);
    }

    public void RenderNPC(Npc npc, PrimitiveBatch batch, Camera camera,
        float elevation, float dt = 0f)
    {
        // Real NPC sprite by type (npcs/*.png), colored quad fallback.
        string spriteKey = npc.NpcType switch
        {
            "merchant" => "npcs/merchant",
            "quest_giver" => "npcs/quest_giver",
            "recruit" => "npcs/recruit",
            "faction_leader" => "npcs/faction_leader",
            _ => "npcs/quest_giver",
        };
        uint tex = GetSpriteTexture(spriteKey);

        var screen = camera.WorldToScreen(npc.WorldX, npc.WorldY, elevation);
        float half = 15f * camera.Zoom;
        // Anchor bottom-center: feet at the ground point at any zoom/pitch.
        float cx = screen.X;
        float cy = screen.Y - half;

        // Gait feet: same planted/swing stepping as the player, split into a
        // far-side pass under the body and a near-side pass over it. NPCs are
        // stationary today, so feet stay planted on the heightmap (terrain
        // contouring); velocity plumbing is in place for future wanderers.
        uint bootTex = GetSpriteTexture("player/boot");
        GaitAnimator? npcGait = null;
        if (tex != 0 && bootTex != 0)
        {
            npcGait = GetGaitRig(npc, GaitConfigs.Npc);
            npcGait.Update(npc.WorldX, npc.WorldY, npc.VelocityX, npc.VelocityY, dt);
        }

        if (!npc.IsRecruited)
            DrawShadow(batch, screen.X, screen.Y, half, 200);
        if (npcGait != null)
            DrawGaitFeet(npcGait, GaitConfigs.Npc, half * GaitConfigs.Npc.FootSizeFrac,
                bootTex, batch, camera, elevation, screen.Y, inFront: false);
        if (tex != 0)
            batch.DrawTexturedScreenQuad(cx, cy, half, half, tex, 255, 255, 255, 255);
        else
            batch.DrawScreenQuad(cx, cy, half, half, 70, 180, 100);
        if (npcGait != null)
            DrawGaitFeet(npcGait, GaitConfigs.Npc, half * GaitConfigs.Npc.FootSizeFrac,
                bootTex, batch, camera, elevation, screen.Y, inFront: true);
    }

    public void RenderProximityPrompt(Npc npc, PrimitiveBatch batch, Camera camera,
        float elevation, TextRenderer? text)
    {
        // Ground point at the same elevation the sprite renders at, so the
        // name/[E] sit directly above it.
        var screen = camera.WorldToScreen(npc.WorldX, npc.WorldY, elevation);
        float half = 15f * camera.Zoom;
        // Name above the sprite, [E] prompt above the name.
        float nameY = screen.Y - half * 2f - 12f * camera.Zoom;
        if (text != null)
        {
            text.DrawText(batch, npc.Name, screen.X, nameY, 13, 240, 230, 200, bold: true);
            text.DrawText(batch, "[E]", screen.X, nameY - 16f * camera.Zoom, 13, 255, 215, 0, bold: true);
        }
        else
        {
            batch.DrawScreenQuad(screen.X, nameY, 3f * camera.Zoom, 3f * camera.Zoom, 255, 255, 120);
        }
    }

    public void RenderStructure(Structure structure, PrimitiveBatch batch, Camera camera, float elevation)
    {
        var screen = camera.WorldToScreen(structure.WorldX, structure.WorldY, elevation);
        float half = 16f * camera.Zoom;
        // Real structure sprite (structure/*.png), gray quad fallback.
        uint tex = GetSpriteTexture(structure.StructureDef.SpriteKey);
        if (tex != 0)
            batch.DrawTexturedScreenQuad(screen.X, screen.Y, half * 0.95f, half * 0.95f, tex, 255, 255, 255, 255);
        else
            batch.DrawScreenQuad(screen.X, screen.Y, half, half, 150, 150, 160);
    }

    public void RenderFire(FireInstance fire, PrimitiveBatch batch, Camera camera)
    {
        var screen = camera.WorldToScreen(fire.WorldX, fire.WorldY, 0f);
        float half = 10f * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, 230, 140, 40);
    }

    // ─── Soft ground shadow ───────────────────────────────────────────────

    private uint _shadowTex;

    private uint GetShadowTexture()
    {
        if (_shadowTex != 0) return _shadowTex;

        const int Size = 64;
        var px = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float nx = (x + 0.5f) / Size * 2f - 1f;
                float ny = (y + 0.5f) / Size * 2f - 1f;
                float d = MathF.Sqrt(nx * nx + ny * ny);
                float t = Math.Clamp(1f - d, 0f, 1f);
                byte a = (byte)(t * t * 200f); // quadratic falloff
                int i = (y * Size + x) * 4;
                px[i] = 20; px[i + 1] = 20; px[i + 2] = 25; px[i + 3] = a;
            }
        }

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        unsafe
        {
            fixed (byte* ptr = px)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, Size, Size, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }
        _shadowTex = tex;
        return _shadowTex;
    }

    /// <summary>
    /// Soft ellipse shadow under a sprite standing at (cx, groundY). Kept
    /// flattened and tightly inside the sprite's own tile band — anything
    /// taller would get buried by nearer tile quads in the painter order.
    /// </summary>
    public void DrawShadow(PrimitiveBatch batch, float cx, float groundY, float half, byte alphaScale = 255)
    {
        uint tex = GetShadowTexture();
        batch.DrawTexturedScreenQuad(
            cx + half * 0.10f, groundY - half * 0.10f,
            half * 0.85f, half * 0.22f,
            tex, 255, 255, 255, alphaScale);
    }

    // ─── Sprite texture loading / caching ─────────────────────────────────

    /// <summary>Texture handle for a sprite key (e.g. "items/logs_oak"); 0 when
    /// the sprite is missing. Panels use this to draw real item sprites.</summary>
    public uint GetSpriteTexture(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
                return 0;
        }
        if (_cache.TryGetValue(key, out var entry))
        {
            return entry.Texture;
        }

        string path = System.IO.Path.Combine(_spritesDir, key + ".png");
        if (!System.IO.File.Exists(path))
        {
            // Cache the miss so we don't stat the filesystem every frame.
            _cache[key] = (0, 0, 0);
            return 0;
        }

        using var decoded = SKBitmap.Decode(path);
        if (decoded == null)
        {
            _cache[key] = (0, 0, 0);
            return 0;
        }

        // Pixel-art sprites (<=32px) are re-sampled 2-4x with nearest neighbor
        // so they stay crisp under the batcher's Linear filtering at higher zooms.
        SKBitmap bitmap = decoded;
        int upscale = decoded.Width <= 16 ? 4 : decoded.Width <= 32 ? 2 : 1;
        if (upscale > 1)
        {
            var resized = decoded.Resize(
                new SKImageInfo(decoded.Width * upscale, decoded.Height * upscale),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)) ?? decoded;
            bitmap = resized;
        }

        int w = bitmap.Width;
        int h = bitmap.Height;

        // Convert to RGBA8888 unpremultiplied so the batch shader tints cleanly.
        using var rgba = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(rgba);
        canvas.DrawBitmap(bitmap, 0, 0);

        // Convert to straight alpha: multiply RGB by alpha so the batch shader
        // can tint cleanly without double-multiplied alpha.
        Span<byte> span = rgba.GetPixelSpan();
        byte[] px = new byte[span.Length];
        span.CopyTo(px);
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a > 0 && a < 255)
            {
                // Straighten premultiplied alpha: scale RGB up by 255/a
                px[i]     = (byte)Math.Clamp(px[i]     * 255 / a, 0, 255);
                px[i + 1] = (byte)Math.Clamp(px[i + 1] * 255 / a, 0, 255);
                px[i + 2] = (byte)Math.Clamp(px[i + 2] * 255 / a, 0, 255);
            }
        }

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        unsafe
        {
            fixed (byte* ptr = px)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)w, (uint)h, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        _cache[key] = (tex, w, h);
        return tex;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kv in _cache)
            _gl.DeleteTexture(kv.Value.Texture);
        _cache.Clear();
    }
}
