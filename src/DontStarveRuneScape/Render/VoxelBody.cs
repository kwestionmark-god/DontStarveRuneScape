namespace DontStarveRuneScape.Render;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;

/// <summary>
/// Voxel-body renderer: the player's static classic-3D body built from
/// projected axis-aligned boxes (the same face idiom as the brick
/// voxels — top bright, X-sides mid, Y-sides darkest) standing above the
/// dome boots. Entirely world-space geometry; per-part face order and
/// the boots/body depth split are both handled by the caller's painter
/// order (behind-boots → body boxes → front boots).
/// </summary>
public static class VoxelBody
{
    /// <summary>One body part: an axis-aligned box — (lat, lon) center
    /// offset from the body's ground point in world px (rotating with the
    /// gait travel direction), size (w lateral, d longitudinal, h tall)
    /// in world px, base = world px above the ground point.</summary>
    public readonly record struct Part(
        string Name, float Lat, float Long, float W, float D, float H,
        float Base, byte R, byte G, byte B);

    /// <summary>The classic voxel player: dome-booted legs, green torso,
    /// green-sleeve arms with skin hands, skin head, brown hair cap.
    /// Total height ~44 world px — the crossed-billboard nominal
    /// (BodyHeightWorld 88 halves through the 30°-pitch response).</summary>
    public static readonly Part[] PlayerModel =
    [
        new("leg_l",    -2.9f, 0f, 3.2f, 4.2f, 10f, 0f,   60, 40, 30),   // brown pants
        new("leg_r",     2.9f, 0f, 3.2f, 4.2f, 10f, 0f,   60, 40, 30),
        new("torso",     0f, 0f, 13f,  6.5f, 15f, 10f,   34, 139, 34),   // green shirt
        new("arm_l",    -8.4f, 0f, 2.6f, 2.8f, 6f, 19f,   34, 120, 40),  // sleeve
        new("arm_r",     8.4f, 0f, 2.6f, 2.8f, 6f, 19f,   34, 120, 40),
        new("hand_l",   -8.4f, 0f, 2.2f, 2.4f, 3.5f, 15.5f, 235, 200, 160),
        new("hand_r",    8.4f, 0f, 2.2f, 2.4f, 3.5f, 15.5f, 235, 200, 160),
        new("head",      0f, 0f, 7f,   6f,   8f, 25f,   235, 200, 160),  // skin
        new("hair",      0f, -0.8f, 7.4f, 5.4f, 10f, 26f,  60, 40, 30),   // wraps the back of the head

        new("nose",      0f, 3.4f, 1.6f, 1.4f, 2f, 28f,   225, 190, 152), // the face tells the facing
    ];

    /// <summary>World-px heights convert to elevation units exactly like
    /// the boot domes: 1 / (ZScale * TerrainHeightScale).</summary>
    public const float WorldPxPerElevUnit =
        Constants.ZScale * Constants.TerrainHeightScale;

    /// <summary>One projected face of a box (a quadrilateral on screen)
    /// with its plane tag for shading: 4 = top, 0/1 = X-side, 2/3 = Y-side,
    /// and a view depth for back-to-front ordering across parts.</summary>
    public struct Face
    {
        public float BLx, BLy, BRx, BRy, TRx, TRy, TLx, TLy;
        public int Plane;
        public float Depth;
        public int PartIndex;
    }

    /// <summary>Project one axis-aligned box (world-px footprint centered at
    /// (wx + pxPerp*lat + dir*lon, ...), rising hWorld px from basePx) into
    /// its up-to-3 camera-visible faces (top + the two sides facing the
    /// view yaw), appended to <paramref name="faces"/>. Returns the count
    /// appended. Faces come back unordered (each box's own z-order is exact
    /// by construction; cross-box is the caller's painter problem).</summary>
    public static int BuildPart(Camera camera, float wx, float wy,
        float groundElevUnits, float dirX, float dirY, in Part part,
        int partIndex, Face[] faces, int at)
    {
        // lat is lateral (left-perp of travel), lon along travel.
        float px = -dirY, py = dirX;
        float cx = wx + px * part.Lat + dirX * part.Long;
        float cy = wy + py * part.Lat + dirY * part.Long;
        float hw = part.W * 0.5f, hd = part.D * 0.5f;
        float e0 = groundElevUnits + part.Base / WorldPxPerElevUnit;
        float e1 = e0 + part.H / WorldPxPerElevUnit;

        // Corner world positions (lateral/longitudinal frame).
        void Corner(float lat, float lon, float elev, out float sx, out float sy)
        {
            var p = camera.WorldToScreen(cx + px * lat + dirX * lon,
                cy + py * lat + dirY * lon, elev);
            sx = p.X; sy = p.Y;
        }
        // The top face's four corners (sides recompute their own).
        Corner(-hw, -hd, e1, out float tlAx, out float tlAy);
        Corner(+hw, -hd, e1, out float trAx, out float trAy);
        Corner(+hw, +hd, e1, out float trBx, out float trBy);
        Corner(-hw, +hd, e1, out float tlBx, out float tlBy);

        float sinYaw = MathF.Sin(camera.Yaw), cosYaw = MathF.Cos(camera.Yaw);
        float Depth(float lat, float lon) =>
            (px * lat + dirX * lon) * sinYaw + (py * lat + dirY * lon) * cosYaw;
        int n = 0;

        // Top face: always drawn.
        faces[at + n++] = new Face
        {
            BLx = tlAx, BLy = tlAy, BRx = trAx, BRy = trAy,
            TRx = trBx, TRy = trBy, TLx = tlBx, TLy = tlBy,
            Plane = 4, Depth = Depth(0, 0) + e1 * 0.001f, PartIndex = partIndex,
        };
        // X lateral side (perp direction): visible when facing the camera.
        float latSideSign = Depth(hw, 0) > 0f ? +1f : -1f;
        {
            float s = latSideSign;
            Corner(s * hw, -hd, e0, out var sblx, out var sbly);
            Corner(s * hw, +hd, e0, out var sbrx, out var sbry);
            Corner(s * hw, +hd, e1, out var strx, out var stry);
            Corner(s * hw, -hd, e1, out var stlx, out var stly);
            faces[at + n++] = new Face
            {
                BLx = sblx, BLy = sbly, BRx = sbrx, BRy = sbry,
                TRx = strx, TRy = stry, TLx = stlx, TLy = stly,
                Plane = s > 0 ? 0 : 1, Depth = Depth(s * hw, 0), PartIndex = partIndex,
            };
        }
        // Y longitudinal side (travel direction): visible when facing.
        float lonSideSign = Depth(0, hd) > 0f ? +1f : -1f;
        {
            float s = lonSideSign;
            Corner(-hw, s * hd, e0, out var sblx, out var sbly);
            Corner(+hw, s * hd, e0, out var sbrx, out var sbry);
            Corner(hw, s * hd, e1, out var strx, out var stry);
            Corner(-hw, s * hd, e1, out var stlx, out var stly);
            faces[at + n++] = new Face
            {
                BLx = sblx, BLy = sbly, BRx = sbrx, BRy = sbry,
                TRx = strx, TRy = stry, TLx = stlx, TLy = stly,
                Plane = s > 0 ? 2 : 3, Depth = Depth(0, s * hd), PartIndex = partIndex,
            };
        }
        return n;
    }
}
