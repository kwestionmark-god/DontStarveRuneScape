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
    // Crossed-billboard half-planes, sorted by view depth every frame.
    private readonly BillQuad[] _bodyHalves = new BillQuad[4];
    private readonly TurnLean _playerLean = new();

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

    // ── Spherical dome boots ─────────────────────────────────────────────
    // Each boot is a tessellated half-ellipsoid dome with a slight toe bias
    // (geometry in <see cref="BootDome"/>) — round, in place of the old
    // stacked shoe boxes. Every grid vertex lives in world space and passes
    // through the camera projection, so the boot reads as a curved 3D
    // object at any yaw. Vertices drape onto the terrain individually, so
    // boots sit flush on slopes; the swing phase rocks the whole dome
    // rigidly on its toe/heel pivot (kick/plant/launch) and rolls it
    // laterally mid-swing; per-vertex lighting shades the curvature
    // smoothly; and a slightly scaled dark dome drawn behind provides the
    // silhouette rim.

    // Dome-boot dims per entity come from its GaitConfig (DomeBoots);
    // heights are world px — the 3D projection compresses them by sinPitch,
    // so the domes read a touch chunkier on screen than the raw numbers
    // suggest.

    // Silhouette rim: the dark backing dome is the boot scaled about its
    // ground point by this factor and drawn first, so it peeks a hair
    // beyond the shaded surface all around.
    private const float BootRimScale = 1.16f;

    // Fixed world-space light for the dome shading (xy + up component).
    private const float LightX = 0.45f, LightY = 0.30f, LightZ = 1.00f;

    // WorldToScreen's elevation channel carries elevation units (ZScale ×
    // TerrainHeightScale screen px per unit), while boot dims are world px.
    // Convert before adding height, or the boots render as stilts (×12.8).
    private const float ElevPerWorldPx = 1f / (Constants.ZScale * Constants.TerrainHeightScale);

    // Body billboard height in world px: 88 reads as the legacy 44·zoom
    // screen px at the default ~30° pitch (the projection foreshortens
    // verticals by sinPitch). The foreshortening is tempered by
    // BodyPitchResponse below rather than applied raw: every other sprite
    // in the world (trees, NPCs, resources) keeps a constant screen size,
    // so a fully honest billboard flattens to a pancake at near-horizon
    // pitch; a 0.75–1.3 clamp gives the 3D read without breaking
    // readability.
    private const float BodyHeightWorld = 88f;
    private const float BodyPitchResponseMin = 0.75f;
    private const float BodyPitchResponseMax = 1.30f;
    // Reference pitch = the camera's default (~30°).
    private const float BodyPitchResponseRef = 0.5f; // sin(30°)

    // Boot trail (world px) — SPRINT-ONLY subtlety: stance targets sit this
    // far behind the body's centerline along the travel direction, so the
    // boots read as trailing (tucked under) the leaning sprite at speed.
    // Zero at normal walk; on stop the idle replant squares the feet.
    private const float BootTrailPx = 2.4f;
    // Sprint envelope: 0..1, ramped with a slow rise while sprinting and a
    // quicker release after, so the sprint extras (deeper lean, boot trail)
    // ease IN over ~0.5s, HOLD for the whole sprint, and ease out on the
    // return to walking — they never ride the turn-lean's fast decay.
    private float _sprintEnv;
    private const float SprintEnvRisePerSec = 2.0f;   // full lean-in ≈ 0.5s
    private const float SprintEnvFallPerSec = 3.5f;  // back to walk ≈ 0.3s
    private const float SprintLeanBoost = 1.35f;     // bank multiplier at env=1
    // Sprint forward lean (world px at env=1): the body billboard's top
    // edge shears this far along the on-screen travel direction — a held
    // "pitched into the run" read for the whole sprint, easing in and out
    // with the envelope. The boots' trail gives the counter-read under it.
    private const float SprintForwardLeanPx = 5.5f;

    // Pooled cell buffers (every visible cell redraws them each frame).
    private readonly System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)> _facePts = new(4);
    private readonly System.Collections.Generic.List<Silk.NET.Maths.Vector2D<float>> _outlinePts = new(4);

    // Pooled dome-grid cache, reused for every foot each frame: BootDome's
    // local points, the projected + shaded main-dome vertices, the rim
    // dome's screen points, and one visibility bit per surface cell.
    private struct DomeVert
    {
        public float NLon, NLat, NH;   // local-frame normal, for cell culling
        public byte R, G, B;          // shaded base color, for the gradient fill
        public float SX, SY;          // projected screen point (lift applied)
    }
    private readonly BootDome.Point[] _domePts = new BootDome.Point[(BootDome.Rings + 1) * BootDome.Segments];
    private readonly DomeVert[] _domeGrid = new DomeVert[(BootDome.Rings + 1) * BootDome.Segments];
    private readonly (float SX, float SY)[] _domeRim = new (float SX, float SY)[(BootDome.Rings + 1) * BootDome.Segments];
    private readonly bool[] _domeCellVis = new bool[BootDome.Rings * BootDome.Segments];

    /// <summary>Draw one entity's feet as projected spherical domes.
    /// <paramref name="inFront"/> selects which side of the body to draw
    /// for: a foot is in front when it is closer to the camera than the
    /// body's billboard plane — i.e. its horizontal offset from the ground
    /// point (<paramref name="bodyWX"/>/Y) along the view-yaw direction is
    /// positive — and behind otherwise, so a far-side foot never paints
    /// over the torso. (The body is a vertical yaw-facing billboard, so the
    /// side-of-plane test is that horizontal dot product; elevation does
    /// not enter it.) Uses the gait's per-foot positions, swing phase
    /// (tilt + roll + lift), and travel direction.</summary>
    private void DrawBootDomes(GaitAnimator gait, GaitConfig cfg, in FootDomeDims dims,
        PrimitiveBatch batch, Camera camera, float elevation, float bodyWX, float bodyWY, bool inFront,
        byte baseR, byte baseG, byte baseB)
    {
        var (dirX, dirY) = gait.Dir;
        float latX = -dirY, latY = dirX;
        float sinYaw = MathF.Sin(camera.Yaw), cosYaw = MathF.Cos(camera.Yaw);

        for (int i = 0; i < gait.FootCount; i++)
        {
            ref var foot = ref gait.GetFoot(i);
            // In front of the body plane ⇔ toward the camera along the yaw.
            float depth = (foot.X - bodyWX) * sinYaw + (foot.Y - bodyWY) * cosYaw;
            if ((depth > 0f) != inFront)
                continue;
            float lift = foot.Swinging ? MathF.Sin(foot.T * MathF.PI) * cfg.LiftPx * camera.Zoom : 0f;
            float tilt = foot.Swinging ? GaitAnimator.SwingTilt(foot.T) : 0f;
            // Roll toward the foot's outside (sideSigned by its stance
            // offset), swelling mid-swing and settling at plant.
            float sideSign = MathF.Sign(cfg.FootOffsets[i].Item1);
            float roll = foot.Swinging
                ? MathF.Sin(foot.T * MathF.PI) * sideSign
                : 0f;

            DrawBootDome(batch, camera, dims, foot.X, foot.Y, dirX, dirY, latX, latY,
                elevation, tilt, roll, lift, baseR, baseG, baseB);
        }
    }

    /// <summary>Project one plane of the body as a world-space vertical
    /// billboard quad standing on the entity's ground point, spanning
    /// ±<paramref name="halfWidthWorld"/> world px along the world axis
    /// (<paramref name="ax"/>, <paramref name="ay"/>) and
    /// <paramref name="heightWorld"/> world px up from the ground. Corners
    /// pass through <see cref="Camera.WorldToScreen"/> — the same projection
    /// the boot domes use — so the body foreshortens with pitch and anchors
    /// to terrain elevation. The plane is STATIC: it keeps its world
    /// orientation while the camera orbits (a quad seen edge-on collapses
    /// to zero width, which is the classic crossed-billboard look). All
    /// four corners then rotate about the projected ground point by
    /// <paramref name="lean"/> (the turn-lean pivot at the feet).</summary>
    /// <summary>Project a span of a body plane: <paramref name="span0"/>/
    /// <paramref name="span1"/> are fractions along the plane's axis in
    /// [-1, +1] (the full plane is −1..+1; the crossed billboards split each
    /// plane at the fold into halves). The UV range follows the span so the
    /// sprite's matching strip maps onto the half-quad.
    /// <paramref name="Depth"/> is the quad center's view depth
    /// (along view yaw: larger = closer) for back-to-front sorting.</summary>
    private static BillQuad ProjectBodyBillboard(Camera camera, float wx, float wy,
        float groundElev, float halfWidthWorld, float heightWorld, float lean,
        float ax, float ay, float span0 = -1f, float span1 = 1f)
    {
        // Tempered pitch foreshortening: nominal screen height is 44·zoom at
        // the reference pitch; tilting stretches/squashes within a clamp so
        // the sprite reads 3D against the terrain but never pancakes (see
        // BodyPitchResponseMin/Max). heightWorld is the nominal value at the
        // reference pitch; divide the foreshortening back out of it.
        float response = Math.Clamp(MathF.Sin(camera.Pitch) / BodyPitchResponseRef,
            BodyPitchResponseMin, BodyPitchResponseMax);
        heightWorld *= response / (MathF.Sin(camera.Pitch) / BodyPitchResponseRef);
        float topElev = groundElev + heightWorld * ElevPerWorldPx;
        var q = new BillQuad();
        q.U0 = (span0 + 1f) * 0.5f;
        q.U1 = (span1 + 1f) * 0.5f;
        var p = camera.WorldToScreen(wx + ax * halfWidthWorld * span0, wy + ay * halfWidthWorld * span0, groundElev);
        (q.BLx, q.BLy) = (p.X, p.Y);
        p = camera.WorldToScreen(wx + ax * halfWidthWorld * span1, wy + ay * halfWidthWorld * span1, groundElev);
        (q.BRx, q.BRy) = (p.X, p.Y);
        p = camera.WorldToScreen(wx + ax * halfWidthWorld * span1, wy + ay * halfWidthWorld * span1, topElev);
        (q.TRx, q.TRy) = (p.X, p.Y);
        p = camera.WorldToScreen(wx + ax * halfWidthWorld * span0, wy + ay * halfWidthWorld * span0, topElev);
        (q.TLx, q.TLy) = (p.X, p.Y);

        // Turn lean pivots the whole sprite about the projected ground
        // point (the midpoint of the base edge).
        float gx = (q.BLx + q.BRx) * 0.5f, gy = (q.BLy + q.BRy) * 0.5f;
        float lc = MathF.Cos(lean), ls = MathF.Sin(lean);
        (float, float) Lean(float x, float y) =>
            (gx + (x - gx) * lc - (y - gy) * ls, gy + (x - gx) * ls + (y - gy) * lc);
        (q.BLx, q.BLy) = Lean(q.BLx, q.BLy);
        (q.BRx, q.BRy) = Lean(q.BRx, q.BRy);
        (q.TRx, q.TRy) = Lean(q.TRx, q.TRy);
        (q.TLx, q.TLy) = Lean(q.TLx, q.TLy);

        q.Cx = (q.BLx + q.BRx + q.TRx + q.TLx) * 0.25f;
        q.Cy = (q.BLy + q.BRy + q.TRy + q.TLy) * 0.25f;
        q.HalfW = (MathF.Abs(q.BRx - q.BLx) + MathF.Abs(q.TRx - q.TLx)) * 0.25f;
        q.HalfH = (MathF.Abs(q.BLy - q.TLy) + MathF.Abs(q.BRy - q.TRy)) * 0.25f;

        // View depth of the quad center (view yaw direction in world
        // coords, matching DrawBootDomes' sorting): larger = nearer the
        // camera = paints later.
        q.Depth = CrossBillboardSorter.HalfDepth(wx, wy, ax, ay,
            halfWidthWorld, span0, span1, camera.Yaw);
        return q;
    }

    /// <summary>Build the crossed-billboard "paper doll" for any entity:
    /// two static world-space planes at 90° riding the gait travel axis
    /// (side view along dir, front/back across it), each split at the fold
    /// into half-quads, the four halves sorted back-to-front by view depth
    /// into <paramref name="halves"/> (length ≥ 4). The more camera-facing
    /// full quad is returned in <paramref name="anchorQuad"/> for anchored
    /// overlays (carried gear, waterline). Entity-agnostic — NPCs and
    /// monsters share this with the player.</summary>
    private static void BuildCrossBillboard(Camera camera, float wx, float wy,
        float groundElev, float halfWidthWorld, float heightWorld, float lean,
        float dirX, float dirY, BillQuad[] halves, out BillQuad anchorQuad)
    {
        var quadFull = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, dirX, dirY);
        var quadFull2 = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, -dirY, dirX);
        anchorQuad = quadFull.HalfW >= quadFull2.HalfW ? quadFull : quadFull2;
        halves[0] = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, dirX, dirY, -1f, 0f);
        halves[1] = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, dirX, dirY, 0f, 1f);
        halves[2] = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, -dirY, dirX, -1f, 0f);
        halves[3] = ProjectBodyBillboard(camera, wx, wy,
            groundElev, halfWidthWorld, heightWorld, lean, -dirY, dirX, 0f, 1f);
        CrossBillboardSorter.SortBackToFront(halves);
    }

    /// <summary>Shift a projected body quad's TOP edge in screen space — the
    /// sprint forward lean pivots the silhouette visually about the base,
    /// so only the top corners move.</summary>
    private static void ShearQuadTop(ref BillQuad q, float dx, float dy)
    {
        q.TLx += dx; q.TLy += dy;
        q.TRx += dx; q.TRy += dy;
        q.Cx += dx * 0.5f; q.Cy += dy * 0.5f;
    }

    /// <summary>Project and draw one foot's dome. Each grid vertex samples
    /// the terrain at its own world position and lifts to its dome height
    /// above it, so the base ring sits flush on slopes. The rim pass
    /// re-projects the same grid scaled by <see cref="BootRimScale"/> for
    /// the dark silhouette drawn behind. Surface cells facing away from
    /// the camera cull via <see cref="BootDome.FacesCamera"/>; the dome is
    /// convex, so the surviving cells never overlap and painter order is
    /// free. Heights are world px converted to elevation units via
    /// <see cref="ElevPerWorldPx"/> before reaching the projection.</summary>
    private void DrawBootDome(PrimitiveBatch batch, Camera camera, in FootDomeDims dims,
        float cx, float cy, float dirX, float dirY, float latX, float latY, float fallbackElev,
        float tilt, float roll, float lift, byte baseR, byte baseG, byte baseB)
    {
        float yaw = camera.Yaw, pitch = camera.Pitch;
        const int segs = BootDome.Segments, rings = BootDome.Rings;
        float lightLen = MathF.Sqrt(LightX * LightX + LightY * LightY + LightZ * LightZ);

        // Vertex pass: geometry → terrain drape → projection → shading.
        // Brightness is the fixed-light shade of the vertex's own normal
        // (smooth across shared cell edges), plus a gentle height gradient
        // — dark at the ground, brighter at the apex — so the dome reads
        // solid.
        for (int ring = 0; ring <= rings; ring++)
            for (int seg = 0; seg < segs; seg++)
            {
                int idx = ring * segs + seg;
                BootDome.Vertex(ring, seg, dims, tilt, roll, out _domePts[idx]);
                var v = _domePts[idx];
                float wx = cx + dirX * v.Lon + latX * v.Lat;
                float wy = cy + dirY * v.Lon + latY * v.Lat;
                float e = (BootElevation?.Invoke(wx, wy) ?? fallbackElev) + v.H * ElevPerWorldPx;
                var s = camera.WorldToScreen(wx, wy, e);
                float nx = v.NLon * dirX + v.NLat * latX;
                float ny = v.NLon * dirY + v.NLat * latY;
                float d = (nx * LightX + ny * LightY + v.NH * LightZ) / lightLen;
                float bright = (0.50f + 0.55f * MathF.Max(0f, d))
                    * (0.92f + 0.14f * (v.H / dims.Height));
                _domeGrid[idx] = new DomeVert
                {
                    NLon = v.NLon, NLat = v.NLat, NH = v.NH,
                    R = (byte)Math.Clamp(baseR * bright, 0, 255),
                    G = (byte)Math.Clamp(baseG * bright, 0, 255),
                    B = (byte)Math.Clamp(baseB * bright, 0, 255),
                    SX = s.X, SY = s.Y - lift,
                };
            }

        // Rim pass: the same grid scaled about the ground point. Uniform
        // scaling leaves the normals untouched, so the rim shares the
        // surface cells' visibility.
        for (int ring = 0; ring <= rings; ring++)
            for (int seg = 0; seg < segs; seg++)
            {
                int idx = ring * segs + seg;
                var v = _domePts[idx];
                float wx = cx + dirX * (v.Lon * BootRimScale) + latX * (v.Lat * BootRimScale);
                float wy = cy + dirY * (v.Lon * BootRimScale) + latY * (v.Lat * BootRimScale);
                float e = (BootElevation?.Invoke(wx, wy) ?? fallbackElev)
                    + v.H * BootRimScale * ElevPerWorldPx;
                var s = camera.WorldToScreen(wx, wy, e);
                _domeRim[idx] = (s.X, s.Y - lift);
            }

        // Cell visibility once, from the averaged cell normal.
        for (int ring = 0; ring < rings; ring++)
            for (int seg = 0; seg < segs; seg++)
            {
                int i00 = ring * segs + seg;
                int i01 = ring * segs + (seg + 1) % segs;
                int i10 = i00 + segs, i11 = i01 + segs;
                float nLon = (_domeGrid[i00].NLon + _domeGrid[i01].NLon + _domeGrid[i10].NLon + _domeGrid[i11].NLon) * 0.25f;
                float nLat = (_domeGrid[i00].NLat + _domeGrid[i01].NLat + _domeGrid[i10].NLat + _domeGrid[i11].NLat) * 0.25f;
                float nH = (_domeGrid[i00].NH + _domeGrid[i01].NH + _domeGrid[i10].NH + _domeGrid[i11].NH) * 0.25f;
                _domeCellVis[i00] = BootDome.FacesCamera(nLon, nLat, nH,
                    dirX, dirY, latX, latY, yaw, pitch);
            }

        // Silhouette rim first (flat dark); the shaded surface paints over
        // its interior, leaving the dark rim peeking around the silhouette.
        for (int ring = 0; ring < rings; ring++)
            for (int seg = 0; seg < segs; seg++)
            {
                int i00 = ring * segs + seg;
                if (!_domeCellVis[i00]) continue;
                int i01 = ring * segs + (seg + 1) % segs;
                int i10 = i00 + segs, i11 = i01 + segs;
                _outlinePts.Clear();
                _outlinePts.Add(new(_domeRim[i00].SX, _domeRim[i00].SY));
                _outlinePts.Add(new(_domeRim[i01].SX, _domeRim[i01].SY));
                _outlinePts.Add(new(_domeRim[i11].SX, _domeRim[i11].SY));
                _outlinePts.Add(new(_domeRim[i10].SX, _domeRim[i10].SY));
                batch.DrawScreenPolygon(_outlinePts, 42, 27, 13, 255);
            }

        // Shaded surface cells; per-vertex colors make the tessellation
        // read as one smooth curved dome.
        for (int ring = 0; ring < rings; ring++)
            for (int seg = 0; seg < segs; seg++)
            {
                int i00 = ring * segs + seg;
                if (!_domeCellVis[i00]) continue;
                int i01 = ring * segs + (seg + 1) % segs;
                int i10 = i00 + segs, i11 = i01 + segs;
                _facePts.Clear();
                AddDomeFacePt(i00);
                AddDomeFacePt(i01);
                AddDomeFacePt(i11);
                AddDomeFacePt(i10);
                batch.DrawScreenPolygonGradient(_facePts);
            }
    }

    /// <summary>Append one dome-grid vertex to the shared gradient-cell
    /// buffer (screen point + shaded base color).</summary>
    private void AddDomeFacePt(int idx)
    {
        ref var v = ref _domeGrid[idx];
        _facePts.Add((v.SX, v.SY, v.R, v.G, v.B, 255));
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

        // Bank into left/right turns while running: the body and its carried
        // gear pivot about the ground point, so the torso leans into the turn;
        // the gait's stance also shifts laterally with the lean (passed to
        // _playerGait below), so new foot plants land a bit toward the bank —
        // feet "planted under the lean". Swimming or standing holds it
        // upright. lean is a screen-space angle; positive tilts clockwise.
        bool swimming = waterDepth >= SwimDepth && bootTex != 0;
        float lean = swimming ? _playerLean.Update(0f, 0f, dt)
                              : _playerLean.Update(velX, velY, dt);
        // Sprint envelope: eases in over ~0.5s on sprint start, holds while
        // sprinting, eases out on the return to walk. Drives the deeper
        // bank and the boot trail below, so both persist for the sprint's
        // duration instead of following the turn-lean's quick decay.
        float envTarget = (player.Sprinting && !swimming) ? 1f : 0f;
        float envRate = envTarget > _sprintEnv ? SprintEnvRisePerSec : SprintEnvFallPerSec;
        _sprintEnv += Math.Clamp(envTarget - _sprintEnv, -envRate * dt, envRate * dt);
        lean *= 1f + (SprintLeanBoost - 1f) * _sprintEnv;

        // The body is a classic crossed billboard riding the travel axis:
        // two static planes at 90° to each other — one ALONG the current
        // gait direction (the side view), one ACROSS it (the front/back
        // view) — intersecting on the vertical line above the ground point
        // and turning with the character like the boots do. Both pass
        // through the same camera projection as the boot domes, so the
        // whole figure is one rigid 3D paper-doll. Each plane is split at
        // the fold into its two halves, and the four half-quads are painted
        // strictly back-to-front by view depth — neither plane ever wins
        // over the other; the camera decides which half-arm is nearest.
        // The more camera-facing full quad anchors gear and the waterline.
        var (gdirX, gdirY) = _playerGait.Dir;
        BuildCrossBillboard(camera, player.WorldX, player.WorldY,
            elevation, HalfWidth, BodyHeightWorld, lean, gdirX, gdirY,
            _bodyHalves, out var bodyQuad);

        // Sprint forward lean: while the envelope is up, shear the TOP edge
        // of every body quad along the on-screen travel direction. Held for
        // the whole sprint (unlike the turn bank, which settles) and eased
        // back out with the envelope on the return to walking. (The uniform
        // shear does not move the base edge or the half extents, so the
        // anchor-quad choice and the depth sort both still hold.)
        if (_sprintEnv > 0.001f && (velX != 0f || velY != 0f))
        {
            var g0 = camera.WorldToScreen(player.WorldX, player.WorldY, elevation);
            var g1 = camera.WorldToScreen(player.WorldX + gdirX, player.WorldY + gdirY, elevation);
            float dx = g1.X - g0.X, dy = g1.Y - g0.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.01f)
            {
                float amt = SprintForwardLeanPx * _sprintEnv * camera.Zoom / len;
                ShearQuadTop(ref bodyQuad, dx * amt, dy * amt);
                for (int i = 0; i < _bodyHalves.Length; i++)
                    ShearQuadTop(ref _bodyHalves[i], dx * amt, dy * amt);
            }
        }
        float lcx = bodyQuad.Cx, lcy = bodyQuad.Cy;

        // Carried cape renders behind the body.
        var cape = player.Gear?.GetEquipped("cape");
        if (cape != null)
            RenderCarried(batch, cape, lcx, lcy, bodyQuad.HalfW,
                player.Facing, behind: true, swing: 0f, lean, halfV: bodyQuad.HalfH);

        DrawShadow(batch, screen.X, screen.Y, half, 220);

        if (bodyTex != 0)
        {
            // Procedural stepping: each boot plants at a fixed world spot while
            // the body moves; once left behind it swings forward, arcs up, and
            // lands at its own sampled terrain elevation. At rest both feet
            // plant on the heightmap, contouring slopes. Far-side feet draw
            // before the body quad, near-side feet after, so a boot on the far
            // side never paints over the torso. The player's boots are drawn
            // as projected world-space spherical domes (terrain-draped,
            // phase-tilted, smoothly shaded) — see DrawBootDomes.
            // (swimming was hoisted above with the turn-lean state.)
            float bootHalf = half * (4f / 32f);
            if (!swimming)
            {
                _playerGait.Update(player.WorldX, player.WorldY, velX, velY, dt,
                    lean * TurnLean.StanceShiftPerRad,
                    trail: BootTrailPx * _sprintEnv);
                // Boot base color matches the player/boot.png leather tone.
                DrawBootDomes(_playerGait, GaitConfigs.Player, GaitConfigs.Player.DomeBoots,
                    batch, camera, elevation, player.WorldX, player.WorldY,
                    inFront: false, 139, 90, 43);
            }

            // Draw the cross: four half-quads back-to-front by view depth.
            // Halves only meet at the fold line, so painter order is exact
            // and stable under every yaw — no flicker as planes cross.
            foreach (var hq in _bodyHalves)
                batch.DrawScreenQuadCornersTexturedUSpan(
                    hq.BLx, hq.BLy, hq.BRx, hq.BRy,
                    hq.TRx, hq.TRy, hq.TLx, hq.TLy,
                    bodyTex, 255, 255, 255, 255, hq.U0, hq.U1);

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
                DrawBootDomes(_playerGait, GaitConfigs.Player, GaitConfigs.Player.DomeBoots,
                    batch, camera, elevation, player.WorldX, player.WorldY,
                    inFront: true, 139, 90, 43);
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
        // Rides the projected billboard center so gear banks with the torso.
        var gear = player.Gear;
        if (gear != null)
        {
            if (gear.Weapon != null)
                RenderCarried(batch, gear.Weapon, lcx, lcy, bodyQuad.HalfW,
                    player.Facing, behind: false, swing, lean, halfV: bodyQuad.HalfH);
            foreach (var slotName in CarriedSlots)
            {
                var item = gear.GetEquipped(slotName);
                if (item != null)
                    RenderCarried(batch, item, lcx, lcy, bodyQuad.HalfW,
                        player.Facing, behind: false, swing: 0f, lean, halfV: bodyQuad.HalfH);
            }
        }

        // Waterline overlay: the submerged lower body sits under a translucent
        // band of sea-colored water, taller the deeper the water.
        if (waterDepth > 0.2f)
        {
            float frac = Math.Clamp(waterDepth / 2.6f, 0f, 0.45f);
            float stripHalf = bodyQuad.HalfH * frac;
            byte a = (byte)Math.Clamp(45 + waterDepth * 22f, 0f, 100f);
            batch.DrawScreenQuad(lcx, lcy + bodyQuad.HalfH - stripHalf,
                bodyQuad.HalfW * 0.6f, stripHalf, 70, 130, 195, a);
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
        float facing, bool behind, float swing, float lean = 0f, float halfV = 0f)
    {
        // halfV is the billboard's vertical half extent (pitch-foreshortened),
        // used for dy mounts; horizontal mounts and item size use half.
        if (halfV <= 0f) halfV = half;
        // Sprite: catalog display key first, then the gear.json key, then the
        // item id — GetSpriteTexture caches misses, so fallbacks are free.
        var display = Data.ItemCatalog.Get(item.Id);
        uint tex = GetSpriteTexture(display?.SpriteKey ?? item.SpriteKey);
        if (tex == 0 && item.SpriteKey != item.Id)
            tex = GetSpriteTexture(item.Id);
        if (tex == 0) return;

        var (dx, dy, size, tilt) = MountOf(item.Slot.ToLowerInvariant());
        float itemHalf = size * half;

        // cx/cy is the leaned body center; mount offsets rotate with the
        // same lean so items stay glued to the torso while it banks.
        float leanCos = MathF.Cos(lean), leanSin = MathF.Sin(lean);
        (float X, float Y) At(float lx, float ly) =>
            (cx + lx * leanCos - ly * leanSin, cy + lx * leanSin + ly * leanCos);

        if (behind)
        {
            // Cape: hangs off the trailing side, mirrored with the facing.
            var (bx, by) = At(-facing * dx * half, dy * halfV);
            DrawRotatedTexturedQuad(batch, bx, by, itemHalf, itemHalf, lean, tex,
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
            // The swing arc mirrors with the facing; the turn lean adds on
            // top in screen space (never mirrored).
            float angle = angleDeg * (MathF.PI / 180f) * facing + lean;
            var (pivotX, pivotY) = At(facing * dx * half, dy * halfV);
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

        // Armor overlays: fixed slot position, mirrored with the facing,
        // banking with the body's turn lean.
        var (px, py) = At(facing * dx * half, dy * halfV);
        DrawRotatedTexturedQuad(batch, px, py, itemHalf, itemHalf, lean, tex,
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
        GaitAnimator? npcGait = null;
        if (tex != 0)
        {
            npcGait = GetGaitRig(npc, GaitConfigs.Npc);
            npcGait.Update(npc.WorldX, npc.WorldY, npc.VelocityX, npc.VelocityY, dt);
        }

        if (!npc.IsRecruited)
            DrawShadow(batch, screen.X, screen.Y, half, 200);
        if (npcGait != null)
            DrawBootDomes(npcGait, GaitConfigs.Npc, GaitConfigs.Npc.DomeBoots,
                batch, camera, elevation, npc.WorldX, npc.WorldY,
                inFront: false, 139, 90, 43);
        if (tex != 0)
        {
            // World-space crossed billboard, same paper-doll as the player:
            // two static planes at 90° riding the gait axis, four half-quads
            // back-to-front by view depth. NPCs have a single sprite, so
            // both planes share the one texture. Height follows the player's
            // ratio convention (BodyHeightWorld = 4× half width: 60 world px
            // reads as the legacy 30·zoom screen px at the default pitch).
            var (gdirX, gdirY) = npcGait!.Dir;
            BuildCrossBillboard(camera, npc.WorldX, npc.WorldY, elevation,
                15f, 60f, 0f, gdirX, gdirY, _bodyHalves, out _);
            foreach (var hq in _bodyHalves)
                batch.DrawScreenQuadCornersTexturedUSpan(
                    hq.BLx, hq.BLy, hq.BRx, hq.BRy,
                    hq.TRx, hq.TRy, hq.TLx, hq.TLy,
                    tex, 255, 255, 255, 255, hq.U0, hq.U1);
        }
        else
        {
            batch.DrawScreenQuad(cx, cy, half, half, 70, 180, 100);
        }
        if (npcGait != null)
            DrawBootDomes(npcGait, GaitConfigs.Npc, GaitConfigs.Npc.DomeBoots,
                batch, camera, elevation, npc.WorldX, npc.WorldY,
                inFront: true, 139, 90, 43);
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
        if (structure.StructureId == "woven_shelter")
        {
            // Procedural woven hut: stacked browns, doorway shadow, roof pole.
            byte shelterAlpha = structure.IsUnderConstruction ? (byte)150 : (byte)255;
            batch.DrawScreenQuad(screen.X, screen.Y + half * 0.35f, half * 0.95f, half * 0.38f,
                51, 37, 25, shelterAlpha);
            batch.DrawScreenQuad(screen.X, screen.Y + half * 0.12f, half * 0.8f, half * 0.28f,
                113, 72, 38, shelterAlpha);
            batch.DrawScreenQuad(screen.X, screen.Y - half * 0.12f, half * 0.62f, half * 0.25f,
                139, 92, 48, shelterAlpha);
            batch.DrawScreenQuad(screen.X, screen.Y - half * 0.34f, half * 0.4f, half * 0.22f,
                164, 118, 66, shelterAlpha);
            batch.DrawScreenQuad(screen.X, screen.Y - half * 0.52f, half * 0.18f, half * 0.18f,
                126, 82, 45, shelterAlpha);
            batch.DrawScreenQuad(screen.X, screen.Y + half * 0.44f, half * 0.09f, half * 0.3f,
                82, 54, 31, shelterAlpha);
            return;
        }
        // Real structure sprite (structure/*.png), gray quad fallback.
        uint tex = GetSpriteTexture(structure.StructureDef.SpriteKey);
        // Blueprint look while under construction: washed-out tint + alpha
        // and a progress bar under the site (WorkProgress fills 20s).
        byte alpha = structure.IsUnderConstruction ? (byte)145 : (byte)255;
        if (tex != 0)
            batch.DrawTexturedScreenQuad(screen.X, screen.Y, half * 0.95f, half * 0.95f, tex,
                structure.IsUnderConstruction ? (byte)190 : (byte)255,
                structure.IsUnderConstruction ? (byte)175 : (byte)255,
                structure.IsUnderConstruction ? (byte)125 : (byte)255, alpha);
        else
            batch.DrawScreenQuad(screen.X, screen.Y, half, half,
                structure.IsUnderConstruction ? (byte)185 : (byte)150,
                structure.IsUnderConstruction ? (byte)140 : (byte)150,
                structure.IsUnderConstruction ? (byte)80 : (byte)160, alpha);
        if (structure.IsUnderConstruction)
        {
            float progress = Math.Clamp(structure.WorkProgress / 20f, 0f, 1f);
            batch.DrawScreenQuad(screen.X, screen.Y + half + 3f * camera.Zoom,
                half * 0.7f, 2f * camera.Zoom, 45, 30, 18, 200);
            if (progress > 0f)
                batch.DrawScreenQuad(screen.X - half * 0.7f * (1f - progress),
                    screen.Y + half + 3f * camera.Zoom,
                    half * 0.7f * progress, 2f * camera.Zoom, 230, 174, 65, 230);
        }
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
