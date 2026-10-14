namespace DontStarveRuneScape.Render;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using SkiaSharp;

/// <summary>
/// Voxel body: the player's figure as the sprite art projected on the front
/// face of a voxel slab, so body.png reads EXACTLY as drawn from the front
/// while the body still turns in 3D. One art row is one box: the row's
/// opaque cells from body.png are its width, the matching row of
/// body_side.png is its depth (read front-to-back), and body_back.png
/// paints the back. Lateral and back surfaces therefore wear generated art
/// rather than a shade of the front colour; top faces are the front colour
/// lit brighter (the brick-voxel shading law).
///
/// Scale: one art pixel is <see cref="ArtPxLat"/> world px across (the
/// crossed billboard's own body.png scale: 44 world px over 16 art px) and
/// <see cref="ArtPxVert"/> world px tall. The vertical axis projects at half
/// the screen scale of a ground offset — the 2:1 the billboard applied by
/// stretching body.png over an 88 world-px quad — so an art pixel stays
/// square on screen. Model rows count up from art row 15, i.e. body.png's
/// own floor: the belt floats the same 3 art px above the ground it always
/// did, and the dome boots stay where the gait plants them.
/// </summary>
public static class VoxelBody
{
    /// <summary>Sprite art grid is 16x16 for every player view.</summary>
    public const int ArtSize = 16;

    /// <summary>Art pixel → world px across the body and into its depth.</summary>
    public const float ArtPxLat = 2.75f;

    /// <summary>Art pixel → world px up the body (2:1: a world px of height
    /// projects at half the screen scale of a world px of ground offset).</summary>
    public const float ArtPxVert = 5.5f;

    /// <summary>Art grid top edge in world px — the sprint shear normalises
    /// each face's top edge height against it.</summary>
    public const float ModelTopPx = ArtSize * ArtPxVert;

    /// <summary>World px per elevation unit: the boot domes' own conversion.</summary>
    public const float WorldPxPerElevUnit = Constants.ZScale * Constants.TerrainHeightScale;

    // ── Art ───────────────────────────────────────────────────────────────

    /// <summary>One 16x16 RGBA view of the player, row 0 at the top (PNG
    /// order). Transparent cells are empty space, never geometry.</summary>
    public sealed class Art
    {
        private readonly uint[] _px = new uint[ArtSize * ArtSize];

        /// <summary>Paint one opaque cell (ARGB 0xFF...).</summary>
        public void Set(int x, int y, byte r, byte g, byte b) =>
            _px[y * ArtSize + x] = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;

        public bool Opaque(int x, int y) => (_px[y * ArtSize + x] & 0xFF000000u) != 0u;

        public (byte R, byte G, byte B) ColorAt(int x, int y)
        {
            uint p = _px[y * ArtSize + x];
            return ((byte)(p >> 16), (byte)(p >> 8), (byte)p);
        }

        /// <summary>Decode a player view PNG; null when it is missing or
        /// unreadable (the caller falls back to another view).</summary>
        public static Art? FromPng(string path)
        {
            if (!System.IO.File.Exists(path)) return null;
            using var decoded = SKBitmap.Decode(path);
            if (decoded == null) return null;
            using var rgba = new SKBitmap(decoded.Width, decoded.Height,
                SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using (var canvas = new SKCanvas(rgba)) canvas.DrawBitmap(decoded, 0, 0);

            var art = new Art();
            Span<byte> span = rgba.GetPixelSpan();
            int w = Math.Min(ArtSize, rgba.Width), h = Math.Min(ArtSize, rgba.Height);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * rgba.Width + x) * 4;
                    if (span[i + 3] == 0) continue;
                    art.Set(x, y, span[i], span[i + 1], span[i + 2]);
                }
            return art;
        }

        /// <summary>Build a view from palette-character rows ('.' = empty).
        /// Fixture helper for tests and art experiments.</summary>
        public static Art FromRows(string[] rows,
            IReadOnlyDictionary<char, (byte R, byte G, byte B)> palette)
        {
            var art = new Art();
            for (int y = 0; y < Math.Min(ArtSize, rows.Length); y++)
                for (int x = 0; x < Math.Min(ArtSize, rows[y].Length); x++)
                {
                    char c = rows[y][x];
                    if (c != '.' && palette.TryGetValue(c, out var rgb))
                        art.Set(x, y, rgb.R, rgb.G, rgb.B);
                }
            return art;
        }
    }

    // ── Model ─────────────────────────────────────────────────────────────

    /// <summary>One face of the slab in model-local space: world px lateral
    /// (lat, + = the body's left as seen on screen when it faces the camera,
    /// so body.png's own column order projects unmirrored), along travel
    /// (lon, + = forward, so the front plane sits at max depth ahead) and
    /// above the ground (h). Each face varies in exactly two of the three
    /// axes; <see cref="Plane"/> says which pair (0 front, 1 back, 2 low-lat
    /// side, 3 high-lat side, 4 top).</summary>
    public readonly record struct LocalFace(
        float Lat0, float Lat1, float Lon0, float Lon1, float H0, float H1,
        int Plane, byte R, byte G, byte B);

    /// <summary>A projected, camera-facing face: four screen corners
    /// (bottom-left, bottom-right, top-right, top-left), the plane tag for
    /// shading, the view depth for back-to-front sorting, and the top edge
    /// height for the sprint shear.</summary>
    public struct Face : IComparable<Face>
    {
        public float BLx, BLy, BRx, BRy, TRx, TRy, TLx, TLy;
        public int Plane;
        public float Depth;
        public float TopPx;
        public byte R, G, B;

        /// <summary>Back-to-front (ascending view depth): the nearest face
        /// paints last.</summary>
        public readonly int CompareTo(Face other) => Depth.CompareTo(other.Depth);
    }

    /// <summary>Load the player's three art views and build the slab. A
    /// missing side view falls back to the front art (square depth, front
    /// colours on the sides); a missing back view likewise.</summary>
    public static LocalFace[] BuildPlayerSlab(string spritesDir)
    {
        string dir = System.IO.Path.Combine(spritesDir, "player");
        var front = Art.FromPng(System.IO.Path.Combine(dir, "body.png")) ?? new Art();
        var side = Art.FromPng(System.IO.Path.Combine(dir, "body_side.png")) ?? front;
        var back = Art.FromPng(System.IO.Path.Combine(dir, "body_back.png")) ?? front;
        return BuildSlab(front, side, back);
    }

    /// <summary>Build the slab's faces from three art views: the front
    /// silhouette and colours come from <paramref name="front"/>, each row's
    /// depth and lateral colours from <paramref name="side"/>, the back face
    /// colours from <paramref name="back"/>.</summary>
    public static LocalFace[] BuildSlab(Art front, Art side, Art back)
    {
        int n = ArtSize;
        var faces = new List<LocalFace>(128);

        // Row metrics: body.png's opaque run, and the side view's run (its
        // first cell is the body's front plane, so the run length IS the
        // row's depth). Art the side view does not cover still extrudes one
        // cell deep — the body never collapses to a sheet.
        var fx0 = new int[n];
        var fx1 = new int[n];
        var sx0 = new int[n];
        var depth = new int[n];
        for (int y = 0; y < n; y++)
        {
            fx0[y] = fx1[y] = -1;
            for (int x = 0; x < n; x++)
                if (front.Opaque(x, y))
                {
                    if (fx0[y] < 0) fx0[y] = x;
                    fx1[y] = x;
                }

            sx0[y] = -1;
            int sx1 = -1;
            for (int x = 0; x < n; x++)
                if (side.Opaque(x, y))
                {
                    if (sx0[y] < 0) sx0[y] = x;
                    sx1 = x;
                }
            depth[y] = fx0[y] < 0 ? 0 : Math.Max(1, sx1 - sx0[y] + 1);
        }
        int maxDepth = 0;
        for (int y = 0; y < n; y++) maxDepth = Math.Max(maxDepth, depth[y]);

        // A flat front plane at the deepest row, the slab extruding back.
        float frontLon = maxDepth * ArtPxLat * 0.5f;
        // Lateral edge of an art cell. lat is a left-perp offset, which maps
        // art cell order to the screen right: cell 8 is the body's axis.
        float LatOf(float cell) => (n * 0.5f - cell) * ArtPxLat;

        for (int y = 0; y < n; y++)
        {
            if (fx0[y] < 0) continue;
            float hb = (n - 1 - y) * ArtPxVert;
            float ht = hb + ArtPxVert;
            float lonBack = frontLon - depth[y] * ArtPxLat;

            // Front face — body.png's own colours, in same-colour runs so no
            // seam can appear inside one flat region of the art.
            Func<int, (byte R, byte G, byte B)> frontColor = x => front.ColorAt(x, y);
            for (int x = fx0[y]; x <= fx1[y]; x = RunEnd(x, y, frontColor, front) + 1)
            {
                int xe = RunEnd(x, y, frontColor, front);
                AddSpan(faces, LatOf(x), LatOf(xe + 1), frontLon, frontLon,
                    hb, ht, 0, front.ColorAt(x, y));
            }

            // Back face — body_back.png, falling back to the front colour
            // wherever the back view draws nothing.
            Func<int, (byte R, byte G, byte B)> backColor = x =>
                back.Opaque(x, y) ? back.ColorAt(x, y) : front.ColorAt(x, y);
            for (int x = fx0[y]; x <= fx1[y]; x = RunEnd(x, y, backColor, front) + 1)
            {
                int xe = RunEnd(x, y, backColor, front);
                AddSpan(faces, LatOf(x), LatOf(xe + 1), lonBack, lonBack,
                    hb, ht, 1, backColor(x));
            }

            // Lateral faces — one flat quad per same-colour depth run, at the
            // row's own silhouette edge (its neighbour cell is empty by
            // definition, so the face is exposed).
            for (int s = 0; s < 2; s++)
            {
                // LatOf decreases along the art's columns: the low-lat edge
                // is the far side of the row's LAST opaque cell.
                bool low = s == 0;
                int edgeCell = low ? fx1[y] : fx0[y];
                float lat = LatOf(edgeCell + (low ? 1 : 0));
                var edge = front.ColorAt(edgeCell, y);
                (byte R, byte G, byte B) SideColor(int k) =>
                    sx0[y] >= 0 && side.Opaque(sx0[y] + k, y) ? side.ColorAt(sx0[y] + k, y) : edge;
                int k = 0;
                while (k < depth[y])
                {
                    var c = SideColor(k);
                    int ke = k;
                    while (ke + 1 < depth[y] && SideColor(ke + 1) == c) ke++;
                    faces.Add(new LocalFace(lat, lat, frontLon - k * ArtPxLat,
                        frontLon - (ke + 1) * ArtPxLat, hb, ht, low ? 2 : 3, c.R, c.G, c.B));
                    k = ke + 1;
                }
            }

            // Top faces — only where the cell above is empty, i.e. beyond the
            // row above's silhouette (full depth exposed) or behind its depth
            // (the rear band exposed). A row that nothing sits on top of gets
            // one top face per colour run.
            int above = y - 1;
            bool covered = above >= 0 && fx0[above] >= 0;
            int cov0 = covered ? fx0[above] : n;
            int cov1 = covered ? fx1[above] : -1;
            int dAbove = covered ? depth[above] : 0;
            for (int x = fx0[y]; x <= fx1[y]; x = RunEnd(x, y, frontColor, front) + 1)
            {
                int xe = RunEnd(x, y, frontColor, front);
                var c = front.ColorAt(x, y);
                void Top(float a0, float a1, float lonA, float lonB) =>
                    AddSpan(faces, a0, a1, lonA, lonB, ht, ht, 4, c);

                if (!covered)
                {
                    Top(LatOf(x), LatOf(xe + 1), frontLon, lonBack);
                    continue;
                }
                if (x < cov0) Top(LatOf(x), LatOf(Math.Min(xe, cov0 - 1) + 1), frontLon, lonBack);
                if (xe > cov1) Top(LatOf(Math.Max(x, cov1 + 1)), LatOf(xe + 1), frontLon, lonBack);
                if (depth[y] > dAbove)
                {
                    int m0 = Math.Max(x, cov0), m1 = Math.Min(xe, cov1);
                    if (m0 <= m1)
                        Top(LatOf(m0), LatOf(m1 + 1), frontLon - dAbove * ArtPxLat, lonBack);
                }
            }
        }
        return faces.ToArray();
    }

    /// <summary>Last cell of the same-colour opaque run starting at
    /// <paramref name="x"/> in <paramref name="front"/>'s row.</summary>
    private static int RunEnd(int x, int y,
        Func<int, (byte R, byte G, byte B)> colorAt, Art front)
    {
        if (!front.Opaque(x, y)) return x;
        var c = colorAt(x);
        int xe = x;
        while (xe + 1 < ArtSize && front.Opaque(xe + 1, y) && colorAt(xe + 1) == c) xe++;
        return xe;
    }

    /// <summary>Emit one slab face with the lat span ordered low..high.</summary>
    private static void AddSpan(List<LocalFace> faces, float latA, float latB,
        float lonA, float lonB, float h0, float h1, int plane, (byte R, byte G, byte B) c)
    {
        if (latA > latB) (latA, latB) = (latB, latA);
        if (lonA < lonB) (lonA, lonB) = (lonB, lonA);
        faces.Add(new LocalFace(latA, latB, lonA, lonB, h0, h1, plane, c.R, c.G, c.B));
    }

    // ── Projection ────────────────────────────────────────────────────────

    /// <summary>Project the slab into <paramref name="faces"/> for one frame
    /// and return how many faces are camera-facing. Only faces whose outward
    /// normal points at the camera survive (the brick-voxel culling law):
    /// the body's front when it faces the camera, its back when it walks
    /// away, one of the two lateral sides, plus the top faces.</summary>
    public static int Project(Camera camera, float wx, float wy, float groundElev,
        float dirX, float dirY, LocalFace[] model, Face[] faces)
    {
        float sinYaw = MathF.Sin(camera.Yaw), cosYaw = MathF.Cos(camera.Yaw);
        float px = -dirY, py = dirX;                    // left-perpendicular
        // Outward-normal view depths (positive = toward the camera). lon+
        // is travel-forward, so the front face looks along travel.
        float dFwd = dirX * sinYaw + dirY * cosYaw;
        float dLat = px * sinYaw + py * cosYaw;         // the +lat side's normal

        void Corner(float lat, float lon, float h, out float sx, out float sy)
        {
            var p = camera.WorldToScreen(wx + px * lat + dirX * lon,
                wy + py * lat + dirY * lon, groundElev + h / WorldPxPerElevUnit);
            sx = p.X;
            sy = p.Y;
        }

        int nf = 0;
        for (int i = 0; i < model.Length; i++)
        {
            var f = model[i];
            bool visible = f.Plane switch
            {
                0 => dFwd > 0f,
                1 => dFwd < 0f,
                2 => dLat < 0f,
                3 => dLat > 0f,
                _ => true,
            };
            if (!visible) continue;

            // Quad corners in the painter's order (bl, br, tr, tl).
            float a = f.Lat0, b = f.Lat1, l0 = f.Lon0, l1 = f.Lon1, h0 = f.H0, h1 = f.H1;
            if (f.Plane is 0 or 1)
            {
                Corner(a, l0, h0, out var x0, out var y0);
                Corner(b, l0, h0, out var x1, out var y1);
                Corner(b, l0, h1, out var x2, out var y2);
                Corner(a, l0, h1, out var x3, out var y3);
                faces[nf] = new Face
                {
                    BLx = x0, BLy = y0, BRx = x1, BRy = y1, TRx = x2, TRy = y2, TLx = x3, TLy = y3,
                };
            }
            else if (f.Plane is 2 or 3)
            {
                Corner(a, l0, h0, out var x0, out var y0);
                Corner(a, l1, h0, out var x1, out var y1);
                Corner(a, l1, h1, out var x2, out var y2);
                Corner(a, l0, h1, out var x3, out var y3);
                faces[nf] = new Face
                {
                    BLx = x0, BLy = y0, BRx = x1, BRy = y1, TRx = x2, TRy = y2, TLx = x3, TLy = y3,
                };
            }
            else
            {
                Corner(a, l0, h1, out var x0, out var y0);
                Corner(b, l0, h1, out var x1, out var y1);
                Corner(b, l1, h1, out var x2, out var y2);
                Corner(a, l1, h1, out var x3, out var y3);
                faces[nf] = new Face
                {
                    BLx = x0, BLy = y0, BRx = x1, BRy = y1, TRx = x2, TRy = y2, TLx = x3, TLy = y3,
                };
            }

            // Sort key: the face centre's view depth, measured from the
            // ground point (a common shift cancels out of the ordering).
            float cLat = (a + b) * 0.5f, cLon = (l0 + l1) * 0.5f;
            ref var outFace = ref faces[nf];
            outFace.Plane = f.Plane;
            outFace.TopPx = h1;
            outFace.Depth = (px * cLat + dirX * cLon) * sinYaw + (py * cLat + dirY * cLon) * cosYaw;
            outFace.R = f.R;
            outFace.G = f.G;
            outFace.B = f.B;
            nf++;
        }
        return nf;
    }
}
