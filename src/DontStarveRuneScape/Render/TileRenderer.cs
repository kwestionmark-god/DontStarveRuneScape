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
                        float shoreDist = tile.ShoreDistance;
                        float eShore = Math.Max(0f, shoreDist - 1f);
                        Span<float> cornerT =
                        [
                            WaterGradientT(surface - e00, CornerShore(world, x, y, -1, -1, shoreDist, eShore)),
                            WaterGradientT(surface - e10, CornerShore(world, x, y, 1, -1, shoreDist, eShore)),
                            WaterGradientT(surface - e11, CornerShore(world, x, y, 1, 1, shoreDist, eShore)),
                            WaterGradientT(surface - e01, CornerShore(world, x, y, -1, 1, shoreDist, eShore)),
                        ];

                        float ts2 = Constants.TileSize;
                        var sbl = camera.WorldToScreen(x * ts2, y * ts2, surface);
                        var sbr = camera.WorldToScreen((x + 1) * ts2, y * ts2, surface);
                        var str = camera.WorldToScreen((x + 1) * ts2, (y + 1) * ts2, surface);
                        var stl = camera.WorldToScreen(x * ts2, (y + 1) * ts2, surface);
                        DrawWaterPatch(batch, camera, x, y, surface,
                            e00, e10, e11, e01, cornerT, time);
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

                    // Shore overlay: fill the below-water sub-region of this
                    // land tile against the adjacent water surface — the exact
                    // geographic waterline, feathered at the shore.
                    float shoreSurface = NeighborWaterSurface(world, x, y);
                    float minCorner = Math.Min(Math.Min(e00, e10), Math.Min(e11, e01));
                    if (!float.IsNaN(shoreSurface) && minCorner < shoreSurface)
                    {
                        Span<float> landT =
                        [
                            WaterGradientT(shoreSurface - e00, 0f),
                            WaterGradientT(shoreSurface - e10, 0f),
                            WaterGradientT(shoreSurface - e11, 0f),
                            WaterGradientT(shoreSurface - e01, 0f),
                        ];
                        DrawWaterPatch(batch, camera, x, y, shoreSurface,
                            e00, e10, e11, e01, landT, time);
                    }
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
        return ((byte)(82 + (28 - 82) * t),
                (byte)(155 + (78 - 155) * t),
                (byte)(215 + (138 - 215) * t));
    }

    /// <summary>
    /// Blend factor for a water tile corner: driven by both the bed depth at
    /// that corner and the tile's BFS distance from shore, so the gradient
    /// spans several tiles even where the bed plunges immediately.
    /// </summary>
    private static float WaterGradientT(float bedDepth, float shoreDistance)
    {
        float depthT = Math.Clamp(bedDepth / 4.5f, 0f, 1f);
        float distT = Math.Clamp(shoreDistance / 5f, 0f, 1f);
        return Math.Max(depthT, distT);
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

    /// <summary>
    /// Draw the portion of a tile that lies below a water surface: submerged
    /// corners plus the interpolated points where tile edges cross the
    /// surface, fan-triangulated at surface height. Because crossings are
    /// computed on shared edges, adjacent tiles stitch one continuous
    /// waterline that follows the terrain contour — no aprons, no gaps.
    /// Per-vertex gradient factor t (0 = waterline pale teal, 1 = deep) is
    /// interpolated along crossing edges; alpha feathers at the waterline.
    /// </summary>
    private static void DrawWaterPatch(PrimitiveBatch batch, Camera camera,
        int tileX, int tileY, float surface,
        float e00, float e10, float e11, float e01,
        ReadOnlySpan<float> cornerT, float time)
    {
        float ts = Constants.TileSize;
        // Corners in consistent order: bl(0,0), br(1,0), tr(1,1), tl(0,1).
        Span<(float Wx, float Wy, float E, float T)> c = stackalloc (float, float, float, float)[4];
        c[0] = (tileX * ts, tileY * ts, e00, cornerT[0]);
        c[1] = (tileX * ts + ts, tileY * ts, e10, cornerT[1]);
        c[2] = (tileX * ts + ts, tileY * ts + ts, e11, cornerT[2]);
        c[3] = (tileX * ts, tileY * ts + ts, e01, cornerT[3]);

        // Gentle lapping shimmer on the waterline feather.
        float lap = MathF.Sin(time * 1.7f + tileX * 0.9f + tileY * 0.6f);
        byte shoreAlpha = (byte)Math.Clamp(150 + (int)(28f * lap), 105, 185);
        const byte deepAlpha = 235;

        var pts = new System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)>(6);
        for (int i = 0; i < 4; i++)
        {
            var a = c[i];
            var b = c[(i + 1) % 4];
            bool aIn = a.E < surface, bIn = b.E < surface;
            if (aIn)
            {
                var p = camera.WorldToScreen(a.Wx, a.Wy, surface);
                var col = WaterGradientColor(a.T);
                pts.Add((p.X, p.Y, col.R, col.G, col.B, deepAlpha));
            }
            if (aIn != bIn)
            {
                float t = (surface - a.E) / (b.E - a.E);
                float wx = a.Wx + (b.Wx - a.Wx) * t;
                float wy = a.Wy + (b.Wy - a.Wy) * t;
                float tt = a.T + (b.T - a.T) * t;
                var p = camera.WorldToScreen(wx, wy, surface);
                var col = WaterGradientColor(tt);
                pts.Add((p.X, p.Y, col.R, col.G, col.B, shoreAlpha));
            }
        }
        if (pts.Count < 3) return;
        batch.DrawScreenPolygonGradient(pts);
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

    /// <summary>Max surface elevation of any water tile adjacent to (x, y).</summary>
    private static float NeighborWaterSurface(TileMap world, int x, int y)
    {
        float best = float.NaN;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = world.GetTile(x + dx, y + dy);
                if (n?.Biome?.Id == "water")
                {
                    float s = n.GetSurfaceElevation();
                    if (float.IsNaN(best) || s > best) best = s;
                }
            }
        return best;
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
        foreach (var kv in _terrainTextures)
            _gl.DeleteTexture(kv.Value);
        _terrainTextures.Clear();
    }
}