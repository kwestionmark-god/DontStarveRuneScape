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
                // Water is a physical layer, not a tile class. Paint classes:
                //   1. submerged beds (anything at/below the sea plane),
                //   2. THE WATER LAYER — one continuous flat mesh at SeaLevel
                //      enqueued once per frame after this loop,
                //   3. dry land (clipped against the plane where straddling) and
                //      sprites. Elevation parallax projects the plane upward
                //      onto rows BEHIND it, so land must always paint over
                //      water at any yaw.
                bool isWater = tile.HasWater;
                if (isWater) depthPx = -2e9f + depthPx * 0.001f;

                float minC = Math.Min(Math.Min(e00, e10), Math.Min(e11, e01));
                float maxC = Math.Max(Math.Max(e00, e10), Math.Max(e11, e01));
                bool straddlesSea = minC < Constants.SeaLevel + 0.05f && maxC > Constants.SeaLevel - 0.05f;
                byte tint = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
                uint overlayTex = (biomeId != null && drawTerrainTexture) ? GetTerrainTexture(biomeId) : 0;

                // Shoreline blend: land within two levels of the plane warms
                // toward sand so banks read as natural beaches.
                if (!isWater && tile.Elevation <= Constants.SeaLevel + 2f && TileTouchesWater(world, x, y))
                {
                    r = (byte)Math.Clamp((int)(r * 0.55f + 205 * 0.45f), 0, 255);
                    g = (byte)Math.Clamp((int)(g * 0.55f + 200 * 0.45f), 0, 255);
                    b = (byte)Math.Clamp((int)(b * 0.55f + 160 * 0.45f), 0, 255);
                }

                drawables.Add((depthPx, seq++, (Action)(() =>
                {
                    if (isWater)
                    {
                        // Submerged bed only — depth-graded per vertex over the
                        // smoothed bed grid (continuous across tiles); the
                        // water itself is the single layer mesh drawn after.
                        DrawBedQuad(batch, camera, world, tile, bl, br, tr, tl);
                        if (straddlesSea)
                            // Bed pokes through the plane: the above-surface
                            // part is dry land, drawn in a later pass class so
                            // it sits over the water layer.
                            DrawLandAbovePlane(batch, camera, tile, Constants.SeaLevel, r, g, b, tint, overlayTex);
                        return;
                    }

                    // Dry land. Where this tile's terrain dips below the flat
                    // sea plane it is clipped against it (marching squares):
                    // the continuous water layer underneath shows through with
                    // a contour-exact waterline — one field, one contour, no
                    // per-tile negotiation. Everything else draws whole.
                    if (straddlesSea)
                    {
                        DrawLandAbovePlane(batch, camera, tile, Constants.SeaLevel, r, g, b, tint, overlayTex);
                    }
                    else
                    {
                        batch.DrawScreenQuadCorners(bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y, r, g, b);
                        if (overlayTex != 0)
                        {
                            // Terrain texture tinted by slope/elevation shading.
                            batch.DrawScreenQuadCornersTextured(
                                bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y,
                                overlayTex, tint, tint, tint);
                        }
                    }
                })));
            }
        }

        // Paint class 2: the water layer — one drawable, one continuous flat
        // sheet mesh at exactly SeaLevel spanning the visible range, tinted and
        // faded per vertex from the smoothed bed depth, then foam crests along
        // every straddling tile's true waterline contour.
        int mx0 = xMin, mx1 = xMax, my0 = yMin, my1 = yMax; // capture
        drawables.Add((-1.5e9f, seq++, (Action)(() => DrawWaterLayer(batch, camera, world, mx0, my0, mx1, my1, time))));
    }

    /// <summary>
    /// The water layer: a continuous flat sheet mesh at exactly the sea level
    /// over the visible tile range (subdivided 2x2 per tile), per-vertex
    /// tinted and faded by smoothed bed depth so shallows turn glassy and the
    /// sheet gracefully vanishes where terrain rises through it. Followed, in
    /// the same drawable, by foam crests riding the true waterline contour of
    /// every straddling tile.
    /// </summary>
    private void DrawWaterLayer(PrimitiveBatch batch, Camera camera, TileMap world,
        int xMin, int yMin, int xMax, int yMax, float time)
    {
        uint tex = GetTerrainTexture("water");
        float ts = Constants.TileSize;
        const int SUB = 2; // sheet cells per tile per axis
        var pts = new System.Collections.Generic.List<(float X, float Y, float U, float V, byte R, byte G, byte B, byte A)>(4);

        for (int ty = yMin; ty < yMax; ty++)
        for (int tx = xMin; tx < xMax; tx++)
        {
            for (int j = 0; j < SUB; j++)
            for (int i = 0; i < SUB; i++)
            {
                float gx0 = tx + i / (float)SUB, gy0 = ty + j / (float)SUB;
                // Skip where all four corners stand dry — dry land is painted
                // over the layer anyway, this just saves fill rate.
                float s00 = SmoothBed(world, gx0, gy0, 99f);
                float s10 = SmoothBed(world, gx0 + 1f / SUB, gy0, 99f);
                float s11 = SmoothBed(world, gx0 + 1f / SUB, gy0 + 1f / SUB, 99f);
                float s01 = SmoothBed(world, gx0, gy0 + 1f / SUB, 99f);
                float minS = Math.Min(Math.Min(s00, s10), Math.Min(s11, s01));
                // Skip where the smoothed bed is surely above the plane —
                // dry land is painted over the layer anyway.
                if (minS > Constants.SeaLevel + 0.1f) continue;

                pts.Clear();
                void V(float gx, float gy, float s)
                {
                    float wx = gx * ts, wy = gy * ts;
                    var p = camera.WorldToScreen(wx, wy, Constants.SeaLevel);
                    var (u, v) = SheetUv(wx, wy, time);
                    var (col, a) = SheetColor(Constants.SeaLevel - s);
                    pts.Add((p.X, p.Y, u, v, col.R, col.G, col.B, a));
                }
                V(gx0, gy0, s00);
                V(gx0 + 1f / SUB, gy0, s10);
                V(gx0 + 1f / SUB, gy0 + 1f / SUB, s11);
                V(gx0, gy0 + 1f / SUB, s01);
                if (tex != 0) batch.DrawScreenPolygonGradientTextured(pts, tex);
                else
                {
                    var flat = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(4);
                    foreach (var p in pts) flat.Add((p.X, p.Y, p.R, p.G, p.B, p.A));
                    batch.DrawScreenPolygonGradient(flat);
                }
            }
        }

        // Foam crests along the true waterline of every straddling tile.
        for (int ty = yMin; ty < yMax; ty++)
        for (int tx = xMin; tx < xMax; tx++)
        {
            var tile = world.Tiles[tx, ty];
            if (tile == null) continue;
            DrawShoreFoam(batch, camera, world, tile, Constants.SeaLevel, time);
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
        return ((byte)(46 + (35 - 46) * t),
                (byte)(120 + (90 - 120) * t),
                (byte)(182 + (150 - 182) * t));
    }

    /// <summary>
    /// The sea's depth grading, shared by the full sheet quads and the shore
    /// contact patches so they meet invisibly: tint eases pale-teal → deep
    /// blue and opacity eases glassy → fully opaque over the first few levels
    /// of depth below the sea plane.
    /// </summary>
    private static ((byte R, byte G, byte B) Col, byte A) SheetColor(float bedDepth)
    {
        float t = WaterGradientT(bedDepth);
        var col = WaterGradientColor(t);
        // Opacity eases from fully clear exactly at the waterline (wet sand),
        // through glassy shallows, to opaque in the deeps.
        float fade = Math.Clamp(bedDepth / 0.9f, 0f, 1f);
        fade = fade * fade * (3f - 2f * fade);
        byte a = (byte)Math.Clamp((110 + 145f * t) * fade, 0f, 255f);
        return (col, a);
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

    /// <summary>Sub-cell resolution of the clipped water patch.</summary>
    private const int PatchSub = 4;

    /// <summary>Texture repeats per tile for the sea sheet — small, so each
    /// portion of the flow texture spans many tiles and the ocean reads as
    /// broad currents instead of tile-sized streaks.</summary>
    private const float SheetUvScale = 0.045f;
    private const float SheetFlowU = 0.07f;
    private const float SheetFlowV = 0.045f;

    /// <summary>
    /// World-anchored animated UV for a point on the sea sheet, so the flow
    /// texture scrolls seamlessly across the whole ocean.
    /// </summary>
    private static (float U, float V) SheetUv(float wx, float wy, float time)
        => (wx / Constants.TileSize * SheetUvScale + time * SheetFlowU,
            wy / Constants.TileSize * SheetUvScale + time * SheetFlowV);

    /// <summary>
    /// Draw only the portion of a tile's terrain lying ABOVE the sea plane:
    /// the bilinear heightfield is sampled on a (PatchSub+1)^2 grid and each
    /// sub-cell keeps its above-surface region (marching squares). The
    /// continuous water layer underneath fills the clipped-away part, giving a
    /// contour-exact waterline. Draws the base color and, when present, the
    /// terrain texture, both clipped to the same polygon.
    /// </summary>
    private void DrawLandAbovePlane(PrimitiveBatch batch, Camera camera, Tile tile,
        float surface, byte r, byte g, byte b, byte tint, uint tex)
    {
        float ts = Constants.TileSize;
        const int N = PatchSub + 1;
        Span<float> g2 = stackalloc float[N * N];
        bool anyAbove = false;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float e = tile.GetElevationAt(i / (float)PatchSub, j / (float)PatchSub);
                g2[j * N + i] = e;
                if (e >= surface) anyAbove = true;
            }
        if (!anyAbove) return;

        var flat = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(6);
        var text = new System.Collections.Generic.List<(float X, float Y, float U, float V, byte R, byte G, byte B, byte A)>(6);

        void Vertex(float wx, float wy, float e)
        {
            var p = camera.WorldToScreen(wx, wy, e);
            // Local tile UV (v flipped to match the terrain overlay mapping).
            float u = wx / ts - tile.X;
            float v = 1f - (wy / ts - tile.Y);
            flat.Add((p.X, p.Y, r, g, b, (byte)255));
            text.Add((p.X, p.Y, u, v, tint, tint, tint, (byte)255));
        }

        float cell = 1f / PatchSub;
        Span<(float Wx, float Wy, float E)> c = stackalloc (float, float, float)[4];
        for (int j = 0; j < PatchSub; j++)
            for (int i = 0; i < PatchSub; i++)
            {
                float e00 = g2[j * N + i], e10 = g2[j * N + i + 1], e11 = g2[(j + 1) * N + i + 1], e01 = g2[(j + 1) * N + i];
                if (e00 < surface && e10 < surface && e11 < surface && e01 < surface) continue;

                float wx0 = (tile.X + i * cell) * ts, wy0 = (tile.Y + j * cell) * ts;
                float wx1 = wx0 + cell * ts, wy1 = wy0 + cell * ts;
                c[0] = (wx0, wy0, e00);
                c[1] = (wx1, wy0, e10);
                c[2] = (wx1, wy1, e11);
                c[3] = (wx0, wy1, e01);

                flat.Clear(); text.Clear();
                for (int k = 0; k < 4; k++)
                {
                    var a = c[k];
                    var b2 = c[(k + 1) % 4];
                    bool aUp = a.E >= surface, bUp = b2.E >= surface;
                    if (aUp) Vertex(a.Wx, a.Wy, a.E);
                    if (aUp != bUp)
                    {
                        float t = (surface - a.E) / (b2.E - a.E);
                        Vertex(a.Wx + (b2.Wx - a.Wx) * t, a.Wy + (b2.Wy - a.Wy) * t, surface);
                    }
                }
                if (flat.Count >= 3)
                {
                    batch.DrawScreenPolygonGradient(flat);
                    if (tex != 0) batch.DrawScreenPolygonGradientTextured(text, tex);
                }
            }
    }

    /// <summary>
    /// Extract the waterline contour (marching-squares crossings at the sea
    /// plane) of a tile and lay foam crests along it.
    /// </summary>
    private void DrawShoreFoam(PrimitiveBatch batch, Camera camera, TileMap world,
        Tile tile, float surface, float time)
    {
        float ts = Constants.TileSize;
        const int N = PatchSub + 1;
        Span<float> g = stackalloc float[N * N];
        bool straddle = false;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float e = tile.GetElevationAt(i / (float)PatchSub, j / (float)PatchSub);
                g[j * N + i] = e;
                // Foam rides a hair offshore of the clip contour.
                if (e < surface + 0.15f && e > surface - 0.4f) straddle = true;
            }
        if (!straddle) return;

        const float clip = Constants.SeaLevel + 0.15f;
        var crossings = new System.Collections.Generic.List<(float X1, float Y1, float X2, float Y2)>(8);
        float cell = 1f / PatchSub;
        for (int j = 0; j < PatchSub; j++)
            for (int i = 0; i < PatchSub; i++)
            {
                float e00 = g[j * N + i], e10 = g[j * N + i + 1], e11 = g[(j + 1) * N + i + 1], e01 = g[(j + 1) * N + i];
                int mask = (e00 < clip ? 1 : 0) | (e10 < clip ? 2 : 0) | (e11 < clip ? 4 : 0) | (e01 < clip ? 8 : 0);
                if (mask == 0 || mask == 15) continue;

                float wx0 = (tile.X + i * cell) * ts, wy0 = (tile.Y + j * cell) * ts;
                float wx1 = wx0 + cell * ts, wy1 = wy0 + cell * ts;
                float wx = 0, wy = 0; int nCross = 0;
                float px = 0, py = 0;
                void EdgeCross(float ex0, float ey0, float ea, float ex1, float ey1, float eb)
                {
                    bool aIn = ea < clip, bIn = eb < clip;
                    if (aIn == bIn) return;
                    float t = (clip - ea) / (eb - ea);
                    float cx = ex0 + (ex1 - ex0) * t, cy = ey0 + (ey1 - ey0) * t;
                    if (nCross == 0) { px = cx; py = cy; }
                    else if (nCross == 1) crossings.Add((px, py, cx, cy));
                    wx = cx; wy = cy; nCross++;
                }
                EdgeCross(wx0, wy0, e00, wx1, wy0, e10);
                EdgeCross(wx1, wy0, e10, wx1, wy1, e11);
                EdgeCross(wx1, wy1, e11, wx0, wy1, e01);
                EdgeCross(wx0, wy1, e01, wx0, wy0, e00);
            }

        DrawContourFoam(batch, camera, tile, crossings, surface, time, cameraZoom: camera.Zoom);
    }

    /// per-corner tinted AND faded by seabed depth — shallow water lets the
    /// bed show through, deep water turns fully opaque. Carries the animated
    /// flow texture at world-anchored UVs. Shared corners sit at identical
    /// world positions, heights and UVs, so adjacent tiles join seamlessly.
    /// </summary>
    /// <summary>
    /// Smoothed bed elevation at a fractional grid position (bilinear over the
    /// vertex-averaged bed grid). Falls back to the raw value when unavailable.
    /// </summary>
    private static float SmoothBed(TileMap world, float gx, float gy, float fallback)
    {
        var bed = world.SmoothedBed;
        if (bed == null) return fallback;
        int x0 = Math.Clamp((int)MathF.Floor(gx), 0, world.Width - 1);
        int y0 = Math.Clamp((int)MathF.Floor(gy), 0, world.Height - 1);
        float fx = gx - x0, fy = gy - y0;
        int x1 = Math.Min(x0 + 1, world.Width), y1 = Math.Min(y0 + 1, world.Height);
        float a = bed[x0, y0] * (1 - fx) + bed[x1, y0] * fx;
        float b = bed[x0, y1] * (1 - fx) + bed[x1, y1] * fx;
        return a * (1 - fy) + b * fy;
    }

    /// <summary>
    /// Underwater ground quad: per-vertex depth-graded color (sand → slate)
    /// over the smoothed bed grid, so tiles blend into one continuous seabed
    /// and nothing tile-aligned ever shows through the translucent shallows.
    /// </summary>
    private static void DrawBedQuad(PrimitiveBatch batch, Camera camera,
        TileMap world, Tile tile,
        Silk.NET.Maths.Vector2D<float> bl, Silk.NET.Maths.Vector2D<float> br,
        Silk.NET.Maths.Vector2D<float> tr, Silk.NET.Maths.Vector2D<float> tl)
    {
        float ts = Constants.TileSize;
        var pts = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(4);
        void Corner(float sx, float sy, float gx, float gy)
        {
            float t = WaterGradientT(Constants.SeaLevel - SmoothBed(world, gx, gy, Constants.SeaLevel));
            pts.Add((sx, sy,
                (byte)(196 + (35 - 196) * t),
                (byte)(182 + (90 - 182) * t),
                (byte)(146 + (150 - 146) * t),
                (byte)255));
        }
        Corner(bl.X, bl.Y, tile.X, tile.Y);
        Corner(br.X, br.Y, tile.X + 1, tile.Y);
        Corner(tr.X, tr.Y, tile.X + 1, tile.Y + 1);
        Corner(tl.X, tl.Y, tile.X, tile.Y + 1);
        batch.DrawScreenPolygonGradient(pts);
    }

    /// <summary>
    /// Foam crests along the true waterline contour: small oriented bands laid
    /// on the marching-squares crossing segments of a straddling tile, so the
    /// foam follows the curving shore instead of stair-stepping tile edges.
    /// </summary>
    private void DrawContourFoam(PrimitiveBatch batch, Camera camera, Tile tile,
        System.Collections.Generic.List<(float X1, float Y1, float X2, float Y2)> crossings,
        float surface, float time, float cameraZoom)
    {
        if (cameraZoom < 0.75f || crossings.Count == 0) return;
        uint foam = GetWaveTexture();
        float ts = Constants.TileSize;
        for (int i = 0; i < crossings.Count; i++)
        {
            var (x1, y1, x2, y2) = crossings[i];
            uint hash = (uint)(tile.X * 73856093 ^ tile.Y * 19349663 ^ i * 83492791);
            if ((hash & 1) == 0) continue; // thin the foam out along the contour
            float phase = hash * 0.00061f;
            float s = (MathF.Sin(time * 2.0f + phase) + 1f) * 0.5f;
            byte alpha = (byte)(25 + 95 * s * s);

            // World-space perpendicular of the contour segment.
            float dx = x2 - x1, dy = y2 - y1;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1f) continue;
            float nx = -dy / len, ny = dx / len;
            float w = ts * (0.10f + 0.06f * s);

            var p1 = camera.WorldToScreen(x1, y1, surface);
            var p2 = camera.WorldToScreen(x2, y2, surface);
            var p3 = camera.WorldToScreen(x2 + nx * w * 2, y2 + ny * w * 2, surface);
            var p4 = camera.WorldToScreen(x1 + nx * w * 2, y1 + ny * w * 2, surface);

            batch.DrawScreenPolygonGradientTextured(new()
            {
                (p1.X, p1.Y, 0f, 0f, (byte)255, (byte)255, (byte)255, alpha),
                (p2.X, p2.Y, 1f, 0f, (byte)255, (byte)255, (byte)255, alpha),
                (p3.X, p3.Y, 1f, 1f, (byte)255, (byte)255, (byte)255, alpha),
                (p4.X, p4.Y, 0f, 1f, (byte)255, (byte)255, (byte)255, alpha),
            }, foam);
        }
    }

    /// <summary>Does this land tile share an edge or corner with water?</summary>
    private static bool TileTouchesWater(TileMap world, int x, int y)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (world.GetTile(x + dx, y + dy)?.HasWater == true) return true;
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

        if (biomeId == "water")
        {
            // Calm the sea: blend every texel toward the texture mean so the
            // speckle reads as gentle current instead of harsh white dashes.
            Span<byte> wp = rgba.GetPixelSpan();
            long sr = 0, sg = 0, sb = 0;
            int count = w * h;
            for (int i = 0; i + 2 < wp.Length; i += 4) { sr += wp[i]; sg += wp[i + 1]; sb += wp[i + 2]; }
            byte mr = (byte)(sr / count), mg = (byte)(sg / count), mb = (byte)(sb / count);
            const float Keep = 0.42f; // fraction of original contrast retained
            for (int i = 0; i + 2 < wp.Length; i += 4)
            {
                wp[i]     = (byte)Math.Clamp(mr + (wp[i]     - mr) * Keep, 0f, 255f);
                wp[i + 1] = (byte)Math.Clamp(mg + (wp[i + 1] - mg) * Keep, 0f, 255f);
                wp[i + 2] = (byte)Math.Clamp(mb + (wp[i + 2] - mb) * Keep, 0f, 255f);
            }
        }

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
        // Water UVs are world-anchored and unbounded so the sea sheet flows
        // seamlessly across tiles — it must wrap; other terrain textures clamp.
        var wrap = biomeId == "water" ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge;
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);

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