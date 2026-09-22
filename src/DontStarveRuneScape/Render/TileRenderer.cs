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
                        // Volumetric water: a single flat surface quad tinted by
                        // depth — no seabed slab, so nothing pokes through.
                        float surface = tile.GetSurfaceElevation();
                        float wDepth = surface - tile.Elevation;
                        byte dr = (byte)Math.Clamp(82 - (int)(wDepth * 12), 28, 82);
                        byte dg = (byte)Math.Clamp(155 - (int)(wDepth * 14), 78, 155);
                        byte db = (byte)Math.Clamp(215 - (int)(wDepth * 8), 138, 215);

                        var sbl = camera.WorldToScreen(x * Constants.TileSize, y * Constants.TileSize, surface);
                        var sbr = camera.WorldToScreen((x + 1) * Constants.TileSize, y * Constants.TileSize, surface);
                        var str = camera.WorldToScreen((x + 1) * Constants.TileSize, (y + 1) * Constants.TileSize, surface);
                        var stl = camera.WorldToScreen(x * Constants.TileSize, (y + 1) * Constants.TileSize, surface);
                        batch.DrawScreenQuadCorners(sbl.X, sbl.Y, sbr.X, sbr.Y, str.X, str.Y, stl.X, stl.Y, dr, dg, db, 215);
                        uint wt = GetTerrainTexture("water");
                        if (wt != 0)
                        {
                            float phase = (x * 0.37f + y * 0.23f + time * 0.35f) % 3f;
                            float uOff = phase, vOff = (y * 0.13f + time * 0.2f) % 3f;
                            batch.DrawScreenQuadCornersTexturedUV(
                                sbl.X, sbl.Y, sbr.X, sbr.Y, str.X, str.Y, stl.X, stl.Y,
                                wt, uOff, vOff);
                        }
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

                    // Shore overlay: if any corner of this land tile dips below
                    // the level of an adjacent water surface, fill the below-
                    // water sub-region with an animated wedge (the "lap").
                    float shoreSurface = NeighborWaterSurface(world, x, y);
                    float minCorner = Math.Min(Math.Min(e00, e10), Math.Min(e11, e01));
                    if (!float.IsNaN(shoreSurface) && minCorner < shoreSurface)
                        DrawShorelineWedge(batch, camera, x, y, e00, e10, e11, e01,
                            shoreSurface, time);
                }));
            }
        }
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

    /// <summary>
    /// Draw the below-water sub-polygon of a land tile against the adjacent
    /// water surface: corners under the surface plus edge-crossing points, fan
    /// triangulated, with a light shoreline lap (alpha + tiny scale pulse).
    /// </summary>
    private static void DrawShorelineWedge(PrimitiveBatch batch, Camera camera,
        int tileX, int tileY, float e00, float e10, float e11, float e01,
        float surface, float time)
    {
        float ts = Constants.TileSize;
        // Corners in consistent order: bl(0,0), br(1,0), tr(1,1), tl(0,1).
        Span<(float Wx, float Wy, float E)> c = stackalloc (float, float, float)[4];
        c[0] = (tileX * ts, tileY * ts, e00);
        c[1] = (tileX * ts + ts, tileY * ts, e10);
        c[2] = (tileX * ts + ts, tileY * ts + ts, e11);
        c[3] = (tileX * ts, tileY * ts + ts, e01);

        var pts = new List<Silk.NET.Maths.Vector2D<float>>(6);
        for (int i = 0; i < 4; i++)
        {
            var a = c[i];
            var b2 = c[(i + 1) % 4];
            if (a.E < surface)
                pts.Add(camera.WorldToScreen(a.Wx, a.Wy, surface));
            bool aIn = a.E < surface, bIn = b2.E < surface;
            if (aIn != bIn)
            {
                float t = (surface - a.E) / (b2.E - a.E);
                float wx = a.Wx + (b2.Wx - a.Wx) * t;
                float wy = a.Wy + (b2.Wy - a.Wy) * t;
                pts.Add(camera.WorldToScreen(wx, wy, surface));
            }
        }
        if (pts.Count < 3) return;

        // Lapping: gentle alpha shimmer plus a hairline push toward the water.
        float lap = MathF.Sin(time * 1.7f + tileX * 0.9f + tileY * 0.6f);
        byte alpha = (byte)Math.Clamp(120 + (int)(38f * lap), 70, 170);
        batch.DrawScreenPolygon(pts, 52, 110, 175, alpha);
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