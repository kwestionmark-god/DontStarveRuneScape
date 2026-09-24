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
                // Water is a physical layer, not a tile class: any tile whose
                // terrain sits at/below the constant sea level carries the sea
                // plane. Water (and the bed under it) sorts strictly before
                // every dry land tile and sprite: elevation parallax projects
                // the surface plane upward onto rows BEHIND it, and only
                // "always paint land over water" keeps banks in front of the
                // waterline at any camera angle. Water tiles keep their
                // relative order among themselves.
                bool isWater = tile.HasWater;
                if (isWater) depthPx = -1e9f + depthPx * 0.001f;

                drawables.Add((depthPx, seq++, (Action)(() =>
                {
                    float minC = Math.Min(Math.Min(e00, e10), Math.Min(e11, e01));
                    float maxC = Math.Max(Math.Max(e00, e10), Math.Max(e11, e01));
                    bool straddlesSea = minC < Constants.SeaLevel && maxC > Constants.SeaLevel;
                    byte tint = (byte)Math.Clamp((int)(shade * 255f), 0, 255);
                    uint overlayTex = (biomeId != null && drawTerrainTexture) ? GetTerrainTexture(biomeId) : 0;

                    if (isWater)
                    {
                        // Bed first (it shows through the translucent shallows),
                        // then the flat sea sheet at exactly SeaLevel: per-corner
                        // depth drives both tint and opacity, and every corner of
                        // the sheet sits at the same world height with
                        // world-anchored UVs, so the body reads as one seamless
                        // continuous ocean.
                        if (straddlesSea)
                        {
                            // Part of the shore's bed pokes through the sheet —
                            // draw the bed only where it stays submerged so the
                            // clipped dry land (drawn later) meets the waterline
                            // along the exact same contour.
                            DrawLandAbovePlane(batch, camera, tile, Constants.SeaLevel, r, g, b, tint, overlayTex, keepAbovePlane: false);
                        }
                        else
                        {
                            batch.DrawScreenQuadCorners(bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y, r, g, b);
                            if (overlayTex != 0)
                                batch.DrawScreenQuadCornersTextured(
                                    bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y,
                                    overlayTex, tint, tint, tint);
                        }
                        DrawSeaSheetQuad(batch, camera, tile, Constants.SeaLevel, time);
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

                    // The sea is a flat world layer at exactly SeaLevel. A
                    // land tile straddling that plane is CLIPPED against it:
                    // the submerged portion of the terrain is never drawn, so
                    // the sea layer shows through and the visible waterline is
                    // the true terrain/plane contour at sub-tile precision —
                    // no blanket overlays, no square water edges.

                    if (straddlesSea)
                    {
                        DrawLandAbovePlane(batch, camera, tile, Constants.SeaLevel, r, g, b, tint, overlayTex);
                    }
                    else
                    {
                        // Draw colored base tile.
                        batch.DrawScreenQuadCorners(bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y, r, g, b);

                        // Overlay terrain sprite if available for this biome.
                        if (overlayTex != 0)
                        {
                            // Terrain texture tinted by the slope/elevation shading.
                            batch.DrawScreenQuadCornersTextured(
                                bl.X, bl.Y, br.X, br.Y, tr.X, tr.Y, tl.X, tl.Y,
                                overlayTex, tint, tint, tint);
                        }
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
                })));
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
            if (n == null || !n.HasWater) continue;
            float surface = Constants.SeaLevel;

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

    /// <summary>Texture repeats per tile for the sea sheet.</summary>
    private const float SheetUvScale = 0.10f;
    private const float SheetFlowU = 0.18f;
    private const float SheetFlowV = 0.11f;

    /// <summary>
    /// World-anchored animated UV for a point on the sea sheet, so the flow
    /// texture scrolls seamlessly across the whole ocean.
    /// </summary>
    private static (float U, float V) SheetUv(float wx, float wy, float time)
        => (wx / Constants.TileSize * SheetUvScale + time * SheetFlowU,
            wy / Constants.TileSize * SheetUvScale + time * SheetFlowV);

    /// <summary>
    /// One tile of the sea sheet: a flat quad at exactly the sea surface,
    /// per-corner tinted AND faded by seabed depth — shallow water lets the
    /// bed show through, deep water turns fully opaque. Carries the animated
    /// flow texture at world-anchored UVs. Shared corners sit at identical
    /// world positions, heights and UVs, so adjacent tiles join seamlessly.
    /// </summary>
    private void DrawSeaSheetQuad(PrimitiveBatch batch, Camera camera,
        Tile tile, float surface, float time)
    {
        uint tex = GetTerrainTexture("water");
        float ts = Constants.TileSize;
        float e00 = tile.CornerElevations?[0] ?? tile.Elevation;
        float e10 = tile.CornerElevations?[1] ?? tile.Elevation;
        float e11 = tile.CornerElevations?[2] ?? tile.Elevation;
        float e01 = tile.CornerElevations?[3] ?? tile.Elevation;

        var bl = camera.WorldToScreen(tile.X * ts, tile.Y * ts, surface);
        var br = camera.WorldToScreen((tile.X + 1) * ts, tile.Y * ts, surface);
        var tr = camera.WorldToScreen((tile.X + 1) * ts, (tile.Y + 1) * ts, surface);
        var tl = camera.WorldToScreen(tile.X * ts, (tile.Y + 1) * ts, surface);

        var pts = new System.Collections.Generic.List<(float X, float Y, float U, float V, byte R, byte G, byte B, byte A)>(4);
        void AddCorner(float sx, float sy, float wx, float wy, float e)
        {
            var (u, v) = SheetUv(wx, wy, time);
            float t = WaterGradientT(surface - e);
            var col = WaterGradientColor(t);
            // Opacity ramps with depth: glassy shallows over the first levels,
            // fully opaque once the bed drops away.
            byte a = (byte)Math.Clamp(168 + 87f * t, 0f, 255f);
            pts.Add((sx, sy, u, v, col.R, col.G, col.B, a));
        }
        AddCorner(bl.X, bl.Y, tile.X * ts, tile.Y * ts, e00);
        AddCorner(br.X, br.Y, (tile.X + 1) * ts, tile.Y * ts, e10);
        AddCorner(tr.X, tr.Y, (tile.X + 1) * ts, (tile.Y + 1) * ts, e11);
        AddCorner(tl.X, tl.Y, tile.X * ts, (tile.Y + 1) * ts, e01);

        if (tex != 0) batch.DrawScreenPolygonGradientTextured(pts, tex);
        else
        {
            var flat = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(4);
            foreach (var p in pts) flat.Add((p.X, p.Y, p.R, p.G, p.B, p.A));
            batch.DrawScreenPolygonGradient(flat);
        }
    }

    /// <summary>
    /// Draw the portion of a tile's terrain on one side of the flat sea plane,
    /// resolved with sub-tile marching squares: the bilinear heightfield is
    /// sampled on a (PatchSub+1)^2 grid and each sub-cell keeps either its
    /// above-surface region (keepAbovePlane: dry shoreline land, so the sea
    /// shows through with a contour-exact waterline) or its below-surface
    /// region (the submerged bed under the translucent shallows). Draws the
    /// base color and, when present, the terrain texture, both clipped to the
    /// same polygon.
    /// </summary>
    private void DrawLandAbovePlane(PrimitiveBatch batch, Camera camera, Tile tile,
        float surface, byte r, byte g, byte b, byte tint, uint tex, bool keepAbovePlane = true)
    {
        float ts = Constants.TileSize;
        const int N = PatchSub + 1;
        Span<float> g2 = stackalloc float[N * N];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                g2[j * N + i] = tile.GetElevationAt(i / (float)PatchSub, j / (float)PatchSub);

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
                bool allOut = keepAbovePlane
                    ? (e00 < surface && e10 < surface && e11 < surface && e01 < surface)
                    : (e00 >= surface && e10 >= surface && e11 >= surface && e01 >= surface);
                if (allOut) continue;

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
                    bool aUp = keepAbovePlane ? a.E >= surface : a.E < surface;
                    bool bUp = keepAbovePlane ? b2.E >= surface : b2.E < surface;
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