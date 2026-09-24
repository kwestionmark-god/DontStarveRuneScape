namespace DontStarveRuneScape.Render;

using System.Collections.Generic;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using SkiaSharp;
using Silk.NET.OpenGL;

/// <summary>
/// TileRenderer — Renders terrain tiles as shaded, biome-colored quads with
/// optional terrain-sprite texture overlays.
/// </summary>
public sealed class TileRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly string _spritesDir;

    // biomeId -> OpenGL texture id
    private readonly Dictionary<string, uint> _terrainTextures = new();
    // Reused per-frame painter-order scratch (avoids per-frame allocation),
    private readonly List<(float Depth, int X, int Y)> _order = new();
    private bool _disposed;

    public TileRenderer(GL gl)
    {
        _gl = gl;
        string baseDir = AppContext.BaseDirectory;
        _spritesDir = System.IO.Path.Combine(baseDir, "Assets", "sprites", "terrain");
        if (!System.IO.Directory.Exists(_spritesDir))
            _spritesDir = baseDir;
    }

    /// <summary>
    /// Terrian draw item for the shared painter's list: tiles and sprites get
    /// unified depth keys so elevated terrain occludes sprites behind it.
    /// </summary>
    /// <param name="drawables">Shared (depth, seq, draw) list; sort by depth then seq.</param>
    /// <summary>
    /// The volumetric sea: one fullscreen animated plane drawn FIRST, so all
    /// terrain and sprites layer over it. Camera pan scrolls the flow texture
    /// so the water reads as rooted in the world; time scrolls the current.
    /// </summary>
    public void DrawSeaSurface(PrimitiveBatch batch, Camera camera, int screenWidth, int screenHeight, float time)
    {
        uint tex = GetTerrainTexture("water");
        if (tex == 0)
        {
            batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.5f,
                screenWidth * 0.5f, screenHeight * 0.5f, 35, 90, 150);
            return;
        }
        float uOff = (camera.PanX / Constants.TileSize) * 0.25f + time * 0.18f;
        float vOff = (camera.PanY / Constants.TileSize) * 0.25f + time * 0.11f;
        batch.DrawScreenQuadCornersTexturedUV(
            0, screenHeight, screenWidth, screenHeight, screenWidth, 0, 0, 0,
            tex, uOff, vOff);
    }

    public void Render(PrimitiveBatch batch, Camera camera, TileMap world,
        List<(float Depth, int Seq, Action Draw)> drawables, ref int seq,
        float time = 0f)
    {
        var (left, top, right, bottom) = camera.GetViewRect();

        // Pad beyond the view rect: high-elevation tiles project upward, so
        // rows below the elevation-0 bottom edge can still be visible, and the
        // top rows underneath tall terrain must not pop out.
        int padTiles = 1 + (int)(Constants.ElevationLevels * Constants.ZScale * Constants.TerrainHeightScale / Constants.TileSize);
        int xMin = Math.Max(0, (int)(left / Constants.TileSize) - padTiles);
        int xMax = Math.Min(world.Width - 1, (int)(right / Constants.TileSize) + padTiles);
        int yMin = Math.Max(0, (int)(top / Constants.TileSize) - padTiles);
        int yMax = Math.Min(world.Height - 1, (int)(bottom / Constants.TileSize) + padTiles);

        var tier = camera.Tier;
        bool drawTerrainTexture = tier != Camera.LodTier.Far;

        // Painter's order: sort tiles back-to-front along the camera's view axis
        // so the draw order stays correct at any yaw (row-major loops only work
        // when yaw == 0).
        float cy = MathF.Cos(camera.Yaw);
        float sy = MathF.Sin(camera.Yaw);
        _order.Clear();
        if (_order.Capacity < (xMax - xMin) * (yMax - yMin))
            _order.Capacity = (xMax - xMin) * (yMax - yMin);
        var order = _order;
        for (int yy = yMin; yy < yMax; yy++)
        {
            for (int xx = xMin; xx < xMax; xx++)
            {
                var t = world.Tiles[xx, yy];
                if (t == null) continue;
                float depth = (yy + 0.5f) * cy + (xx + 0.5f) * sy
                              + t.GetElevationAt(0.5f, 0.5f) * 0.5f;
                order.Add((depth, xx, yy));
            }
        }
        order.Sort((a, b) => a.Depth.CompareTo(b.Depth));

        foreach (var (_, x, y) in order)
        {
            {
                var tile = world.Tiles[x, y];
                if (tile == null) continue;
                int tileX = x, tileY = y;

                int elevation = (int)MathF.Round(tile.Elevation);
                var color = tile.Biome?.GetTerrainColor(elevation) ?? (128, 128, 128);
                if (color == default) color = (128, 128, 128);

                // Slope-shaded with a light source toward the north-east.
                float slope = tile.GetSlopeShading(0.6f, 0.35f);
                float shade = Math.Clamp(0.75f + slope * 0.30f - elevation / (float)Constants.ElevationLevels * 0.25f, 0.35f, 1.15f);

                byte r = (byte)Math.Clamp((int)(color.R * shade), 0, 255);
                byte g = (byte)Math.Clamp((int)(color.G * shade), 0, 255);
                byte b = (byte)Math.Clamp((int)(color.B * shade), 0, 255);

                // Project all four corners of the tile to screen space so adjacent
                // tiles share edges and there are no gaps when the camera is tilted.
                float e00 = tile.CornerElevations?[0] ?? tile.Elevation;
                float e10 = tile.CornerElevations?[1] ?? tile.Elevation;
                float e11 = tile.CornerElevations?[2] ?? tile.Elevation;
                float e01 = tile.CornerElevations?[3] ?? tile.Elevation;

                var bl = camera.WorldToScreen(x * Constants.TileSize, y * Constants.TileSize, e00);
                var br = camera.WorldToScreen((x + 1) * Constants.TileSize, y * Constants.TileSize, e10);
                var tr = camera.WorldToScreen((x + 1) * Constants.TileSize, (y + 1) * Constants.TileSize, e11);
                var tl = camera.WorldToScreen(x * Constants.TileSize, (y + 1) * Constants.TileSize, e01);

                // Depth key in the same world-pixel units the sprite pass uses.
                float depthPx = ((y + 0.5f) * cy + (x + 0.5f) * sy) * Constants.TileSize
                                + tile.GetElevationAt(0.5f, 0.5f) * Constants.ZScale * 0.5f;

                string? biomeId = tile.Biome?.Id;
                bool isWater = biomeId == "water";
                // Water sorts strictly before every land tile and sprite:
                // elevation parallax projects the surface plane upward onto
                // rows BEHIND it, and only "always paint land over water"
                // keeps banks in front of the waterline at any camera angle.
                // Water-vs-water order is preserved among themselves.
                if (isWater) depthPx = -1e9f + depthPx * 0.001f;

                drawables.Add((depthPx, seq++, () =>
                {
                    if (isWater)
                    {
                        // Volumetric water: the tile's intersection with its
                        // own surface plane (here the full quad), tinted per
                        // vertex by bed depth and shore distance. Shared tile
                        // edges clip at identical world points, so neighboring
                        // land wedges continue the same waterline seamlessly.
                        float surface = tile.GetSurfaceElevation();
                        // The fullscreen animated sea plane underlies all
                        // deep open water. Per-tile quads only add value near
                        // land (shore gradient, texture phase) or for pools
                        // above sea level — elsewhere they'd just fight the
                        // plane with a mismatched tile boundary.
                        // Sea-level water is the fullscreen animated sea plane
                        // alone — no per-tile quads, so the body of water has
                        // one continuous texture and zero tile-boundary seams.
                        // Per-tile quads are only painted for pooled water
                        // (above sea level), where the plane can't reach.
                        bool isPool = !float.IsNaN(tile.WaterLevel);
                        if (!isPool) return;
                        float ts2 = Constants.TileSize;
                        var sbl = camera.WorldToScreen(x * ts2, y * ts2, surface);
                        var sbr = camera.WorldToScreen((x + 1) * ts2, y * ts2, surface);
                        var str = camera.WorldToScreen((x + 1) * ts2, (y + 1) * ts2, surface);
                        var stl = camera.WorldToScreen(x * ts2, (y + 1) * ts2, surface);
                        DrawWaterPatch(batch, camera, tile, surface, time);
                        uint wt = GetTerrainTexture("water");
                        if (wt != 0)
                        {
                            float phase = (x * 0.37f + y * 0.23f + time * 0.35f) % 3f;
                            float uOff = phase, vOff = (y * 0.13f + time * 0.2f) % 3f;
                            batch.DrawScreenQuadCornersTexturedUV(
                                sbl.X, sbl.Y, sbr.X, sbr.Y, str.X, str.Y, stl.X, stl.Y,
                                wt, uOff, vOff);
                        }

                        // Waterfall face: where the neighboring water tile's
                        // surface sits lower than ours, close the vertical
                        // slice between the two surface heights so sky never
                        // shows through the seam (reads as a small spill).
                        if (IsWaterAt(world, x, y - 1)) DrawWaterDrop(batch, camera, x, y, x + 1, y, surface, world.GetTile(x, y - 1)!.GetSurfaceElevation());
                        if (IsWaterAt(world, x, y + 1)) DrawWaterDrop(batch, camera, x, y + 1, x + 1, y + 1, surface, world.GetTile(x, y + 1)!.GetSurfaceElevation());
                        if (IsWaterAt(world, x - 1, y)) DrawWaterDrop(batch, camera, x, y, x, y + 1, surface, world.GetTile(x - 1, y)!.GetSurfaceElevation());
                        if (IsWaterAt(world, x + 1, y)) DrawWaterDrop(batch, camera, x + 1, y, x + 1, y + 1, surface, world.GetTile(x + 1, y)!.GetSurfaceElevation());

                        // Bank face: toward a LAND neighbor while our surface
                        // sits above the shared bank edge (pool held by its
                        // rim), draw the front bank wall down to the bank so
                        // the surface never reads as a floating slab; the land
                        // tile itself still overlaps on top.
                        if (!IsWaterAt(world, x, y - 1)) DrawWaterDropToBank(batch, camera, x, y, x + 1, y, surface, Math.Max(e00, e10));
                        if (!IsWaterAt(world, x, y + 1)) DrawWaterDropToBank(batch, camera, x, y + 1, x + 1, y + 1, surface, Math.Max(e11, e01));
                        if (!IsWaterAt(world, x - 1, y)) DrawWaterDropToBank(batch, camera, x, y, x, y + 1, surface, Math.Max(e00, e01));
                        if (!IsWaterAt(world, x + 1, y)) DrawWaterDropToBank(batch, camera, x + 1, y, x + 1, y + 1, surface, Math.Max(e10, e11));
                        return;
                    }

                    // Shoreline blend: land tiles within two levels of sea level
                    // warm toward sand so banks read as natural beaches.
                    if (tile.Elevation <= Constants.SeaLevel + 2f && TileTouchesWater(world, x, y))
                    {
                        r = (byte)Math.Clamp((int)(r * 0.55f + 205 * 0.45f), 0, 255);
                        g = (byte)Math.Clamp((int)(g * 0.55f + 200 * 0.45f), 0, 255);
                        b = (byte)Math.Clamp((int)(b * 0.55f + 160 * 0.45f), 0, 255);
                    }

                    // Draw colored base tile.
                    batch.DrawScreenQuadCorners(bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y, r, g, b);

                    // Overlay terrain sprite if available for this biome.
                    if (biomeId != null && drawTerrainTexture)
                    {
                        uint tex = GetTerrainTexture(biomeId);
                        if (tex != 0)
                        {
                            // Terrain texture tinted by the slope/elevation shading.
                            byte t = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
                            batch.DrawScreenQuadCornersTextured(
                                bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y,
                                tex, t, t, t);
                        }
                    }

                    // Lapping sea blanket over low land: layered translucent
                    // patches at slightly raised virtual sea levels. Each layer
                    // is contour-exact (marching squares), so the shallows and
                    // tide edge hug the terrain's actual shape — never a square.
                    // Translucent everywhere, so dipped terrain shows through.
                    float minC = Math.Min(Math.Min(e00, e10), Math.Min(e11, e01));
                    if (tile.LandDistToWater > 0 && tile.LandDistToWater <= 4
                        && !float.IsNaN(tile.ShoreSurface)
                        && tile.ShoreSurface <= Constants.SeaLevel + 0.05f
                        && minC < Constants.SeaLevel + 0.9f)
                    {
                        DrawWaterPatch(batch, camera, tile, Constants.SeaLevel, time, alphaMul: 0.62f);
                        DrawWaterPatch(batch, camera, tile, Constants.SeaLevel + 0.45f, time, alphaMul: 0.30f);
                        DrawWaterPatch(batch, camera, tile, Constants.SeaLevel + 0.9f, time, alphaMul: 0.13f);
                    }
                    // Wave crests ride every land tile edge that faces water,
                    // projected at that water's surface — the waterline is the
                    // tile boundary (terrain occludes the water there), so the
                    // crests sit right on the visible shore.
                    if (tile.LandDistToWater == 1)
                        DrawEdgeWaves(batch, camera, x, y, world, time);

                    // (The old ring-band wash was removed: per-tile quads made
                    // the shore read as square steps. The layered blanket above
                    // supplies the wet-slope gradient contour-exactly.)
                }));
            }
        }
    }

    /// <summary>
    /// Water tint as a shallow→deep gradient. t = 0 is shore water (pale
    /// teal), t = 1 is deep offshore water (dark blue).
    /// </summary>
    private static (byte R, byte G, byte B) WaterGradientColor(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        // Shallow: saturated teal (reads as water, not haze). Deep endpoint is
        // exactly the fullscreen sea-plane fallback color so per-tile water
        // meets the surrounding sea seamlessly.
        return ((byte)(80 + (35 - 80) * t),
                (byte)(150 + (90 - 150) * t),
                (byte)(205 + (150 - 205) * t));
    }

    /// <summary>
    /// Blend factor from water depth below the surface, smoothstepped over
    /// ~4.5 levels so shallow teal eases into deep blue without banding.
    /// </summary>
    private static float WaterGradientT(float bedDepth)
    {
        float t = Math.Clamp(bedDepth / 5f, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>
    /// Shore distance for a water tile corner: pulls the corner value toward
    /// the land-facing neighbor's distance so the gradient hugs the shoreline.
    /// </summary>
    private static float CornerShore(TileMap world, int x, int y, int dirX, int dirY, float shoreDist, float edgeShore)
    {
        // Sample the two water neighbors straddling this corner, if any.
        float best = edgeShore;
        var nx = world.GetTile(x + dirX, y);
        var ny = world.GetTile(x, y + dirY);
        if (nx?.Biome?.Id == "water") best = Math.Min(best, Math.Max(0f, nx.ShoreDistance - 0.5f));
        if (ny?.Biome?.Id == "water") best = Math.Min(best, Math.Max(0f, ny.ShoreDistance - 0.5f));
        return best;
    }

    private static bool IsWaterAt(TileMap world, int x, int y)
        => world.GetTile(x, y)?.Biome?.Id == "water";

    private uint _waveTex;

    /// <summary>
    /// Generated wave-crest sprite: a soft white foam band, bright in the
    /// middle and feathered at every edge so crests can tile along the
    /// waterline. Slight per-pixel mottling breaks the band into foam.
    /// </summary>
    private uint GetWaveTexture()
    {
        if (_waveTex != 0) return _waveTex;
        const int W = 96, H = 48;
        var px = new byte[W * H * 4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)(W - 1);
                float v = y / (float)(H - 1);
                // Vertical gaussian band with a lower bias (crest face).
                float dy = (v - 0.42f) / 0.30f;
                float band = MathF.Exp(-dy * dy);
                // Fade the horizontal tips so crests merge end-to-end.
                float tip = MathF.Sin(u * MathF.PI);
                // Mottle for foam: deterministic ridge noise.
                float n = MathF.Sin(x * 0.71f) * MathF.Sin(y * 1.13f)
                        + MathF.Sin((x + y) * 0.37f);
                float mottle = 0.75f + 0.25f * Math.Clamp(n * 0.5f + 0.5f, 0f, 1f);
                byte a = (byte)Math.Clamp(band * tip * mottle * 235f, 0f, 255f);
                int i = (y * W + x) * 4;
                px[i] = 235; px[i + 1] = 246; px[i + 2] = 250; px[i + 3] = a;
            }

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        unsafe
        {
            fixed (byte* ptr = px)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, W, H, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }
        _waveTex = tex;
        return _waveTex;
    }

    /// <summary>
    /// Oscillating wave-crest billboards on every edge of this land tile that
    /// borders water: the crest sits at the shared edge midpoint, projected at
    /// the water surface, pulsing in size and alpha with a per-edge phase so
    /// crests wash in staggered along the visible shoreline.
    /// </summary>
    private void DrawEdgeWaves(PrimitiveBatch batch, Camera camera,
        int tileX, int tileY, TileMap world, float time)
    {
        if (camera.Zoom < 0.75f) return; // far LOD: no waves
        uint tex = GetWaveTexture();
        if (tex == 0) return;

        float ts = Constants.TileSize;
        for (int k = 0; k < 4; k++)
        {
            int dx = k == 0 ? 0 : k == 1 ? 1 : k == 2 ? 0 : -1;
            int dy = k == 0 ? -1 : k == 1 ? 0 : k == 2 ? 1 : 0;
            var n = world.GetTile(tileX + dx, tileY + dy);
            if (n?.Biome?.Id != "water") continue;
            float surface = n.GetSurfaceElevation();

            // Edge midpoint in world coords.
            float mx = (tileX + 0.5f + dx * 0.5f) * ts;
            float my = (tileY + 0.5f + dy * 0.5f) * ts;
            float phase = ((tileX * 73856093) ^ (tileY * 19349663) ^ (k * 83492791)) * 0.00061f;
            float s = (MathF.Sin(time * 2.0f + phase) + 1f) * 0.5f;
            byte alpha = (byte)(45 + 185 * s * s);
            var m = camera.WorldToScreen(mx, my, surface);
            float halfW = ts * camera.Zoom * (0.34f + 0.22f * s);
            float halfH = halfW * 0.42f;
            batch.DrawTexturedScreenQuad(m.X, m.Y - halfH * (0.5f + 0.5f * s),
                halfW, halfH, tex, 255, 255, 255, alpha);
        }
    }

    /// <summary>Sub-cell resolution of the clipped water patch.</summary>
    private const int PatchSub = 4;

    /// <summary>
    /// Draw the portion of a tile lying below a water surface, resolved with
    /// sub-tile marching squares: the tile's bilinear heightfield is sampled
    /// on a (PatchSub+1)^2 grid and each sub-cell is clipped against the
    /// surface plane. The waterline therefore follows the terrain contour at
    /// quarter-tile precision instead of flipping whole tiles in or out.
    /// Per-vertex color comes from depth below the surface (smoothstepped),
    /// alpha feathers at crossing points with a gentle lapping shimmer.
    /// </summary>
    private static void DrawWaterPatch(PrimitiveBatch batch, Camera camera,
        Tile tile, float surface, float time, float alphaMul = 1f)
    {
        float ts = Constants.TileSize;
        const int N = PatchSub + 1;
        Span<float> g = stackalloc float[N * N];
        bool anyBelow = false, anyAbove = false;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float e = tile.GetElevationAt(i / (float)PatchSub, j / (float)PatchSub);
                g[j * N + i] = e;
                if (e < surface) anyBelow = true; else anyAbove = true;
            }
        if (!anyBelow) return;

        float lap = MathF.Sin(time * 1.7f + tile.X * 0.9f + tile.Y * 0.6f);
        byte shoreAlpha = (byte)(Math.Clamp(150 + (int)(28f * lap), 105, 185) * alphaMul);
        byte deepAlpha = (byte)(235f * alphaMul);

        var pts = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(6);
        float cell = 1f / PatchSub;
        for (int j = 0; j < PatchSub; j++)
            for (int i = 0; i < PatchSub; i++)
            {
                float e00 = g[j * N + i], e10 = g[j * N + i + 1], e11 = g[(j + 1) * N + i + 1], e01 = g[(j + 1) * N + i];
                bool below00 = e00 < surface, below10 = e10 < surface, below11 = e11 < surface, below01 = e01 < surface;
                if (!below00 && !below10 && !below11 && !below01) continue;

                float wx0 = (tile.X + i * cell) * ts, wy0 = (tile.Y + j * cell) * ts;
                float wx1 = wx0 + cell * ts, wy1 = wy0 + cell * ts;
                Span<(float Wx, float Wy, float E)> c = stackalloc (float, float, float)[4];
                c[0] = (wx0, wy0, e00);
                c[1] = (wx1, wy0, e10);
                c[2] = (wx1, wy1, e11);
                c[3] = (wx0, wy1, e01);

                pts.Clear();
                for (int k = 0; k < 4; k++)
                {
                    var a = c[k];
                    var b = c[(k + 1) % 4];
                    bool aIn = a.E < surface, bIn = b.E < surface;
                    if (aIn)
                    {
                        var p = camera.WorldToScreen(a.Wx, a.Wy, surface);
                        var col = WaterGradientColor(WaterGradientT(surface - a.E));
                        pts.Add((p.X, p.Y, col.R, col.G, col.B, deepAlpha));
                    }
                    if (aIn != bIn)
                    {
                        float t = (surface - a.E) / (b.E - a.E);
                        float wx = a.Wx + (b.Wx - a.Wx) * t;
                        float wy = a.Wy + (b.Wy - a.Wy) * t;
                        var p = camera.WorldToScreen(wx, wy, surface);
                        var col = WaterGradientColor(WaterGradientT(surface - (a.E + (b.E - a.E) * t)));
                        pts.Add((p.X, p.Y, col.R, col.G, col.B, shoreAlpha));
                    }
                }
                if (pts.Count >= 3)
                    batch.DrawScreenPolygonGradient(pts);
            }
    }

    /// <summary>
    /// Draw the vertical bank wall from a water tile's surface down to the
    /// bank edge elevation of an adjacent land tile. Skipped when the bank
    /// is at or above the surface (normal shoreline handled by the patch).
    /// </summary>
    private static void DrawWaterDropToBank(PrimitiveBatch batch, Camera camera,
        float ax, float ay, float bx, float by, float surface, float bank)
    {
        if (bank >= surface - 0.05f) return;
        DrawWaterDrop(batch, camera, ax, ay, bx, by, surface, bank);
    }

    /// <summary>
    /// Draw the vertical water face between this tile's edge and a lower
    /// neighboring water surface. (ax ay)-(bx by) is the shared edge in tile
    /// coords. Both sides are drawn fully tinted since nothing lies between.
    /// </summary>
    private static void DrawWaterDrop(PrimitiveBatch batch, Camera camera,
        float ax, float ay, float bx, float by, float surface, float neighborSurface)
    {
        if (neighborSurface >= surface - 0.05f) return;
        float ts = Constants.TileSize;
        var top = WaterGradientColor(0.45f);
        var bot = WaterGradientColor(0.15f);
        var th0 = camera.WorldToScreen(ax * ts, ay * ts, surface);
        var th1 = camera.WorldToScreen(bx * ts, by * ts, surface);
        var bh0 = camera.WorldToScreen(ax * ts, ay * ts, neighborSurface);
        var bh1 = camera.WorldToScreen(bx * ts, by * ts, neighborSurface);
        batch.DrawScreenPolygonGradient(new()
        {
            (th0.X, th0.Y, top.R, top.G, top.B, (byte)235),
            (th1.X, th1.Y, top.R, top.G, top.B, (byte)235),
            (bh1.X, bh1.Y, bot.R, bot.G, bot.B, (byte)235),
            (bh0.X, bh0.Y, bot.R, bot.G, bot.B, (byte)235),
        });
    }

    /// <summary>Does this land tile share an edge or corner with water?</summary>
    private static bool TileTouchesWater(TileMap world, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (world.GetTile(x + dx, y + dy)?.Biome?.Id == "water") return true;
            }
        return false;
    }

    private uint GetTerrainTexture(string biomeId)
    {
        if (string.IsNullOrEmpty(biomeId)) return 0;
        if (_terrainTextures.TryGetValue(biomeId, out var tex))
            return tex;

        string path = System.IO.Path.Combine(_spritesDir, biomeId + ".png");
        if (!System.IO.File.Exists(path))
        {
            _terrainTextures[biomeId] = 0;
            return 0;
        }

        using var bitmap = SKBitmap.Decode(path);
        if (bitmap == null)
        {
            _terrainTextures[biomeId] = 0;
            return 0;
        }

        int w = bitmap.Width;
        int h = bitmap.Height;

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

        uint tid = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tid);
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

        _terrainTextures[biomeId] = tid;
        return tid;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_waveTex != 0) _gl.DeleteTexture(_waveTex);
        foreach (var kv in _terrainTextures)
            _gl.DeleteTexture(kv.Value);
        _terrainTextures.Clear();
    }
}