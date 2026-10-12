namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// Vertical building, slice 6 (spec:
/// docs/superpowers/specs/2026-10-11-block-voxel-walls-design.md): blocks
/// render as sprite-proportioned slabs (half-thickness 16 world px,
/// centered in the tile) that EXTEND to the tile edge along each
/// connected-mask side — joined blocks butt at the boundary into one
/// continuous slender wall. Slice-5 culling contracts (strips, taller
/// neighbours), yaw flip and depth sort carry over.
///
/// Plane tags: 0=E(+X), 1=S(+Y), 2=W(−X), 3=N(−Y), 4=Top.
/// Faces are depth-sorted back-to-front; group by .Plane, never by index.
/// Mask bits: N=1, E=2, S=4, W=8 (BlockStack.ConnectionMask).
/// </summary>
public class BlockVoxelTests
{
    private static Camera MakeCamera(float yawDeg, float pitchDeg = 30f, float zoom = 1f)
    {
        var cam = new Camera(1280, 720);
        cam.SetPlayer(new Player(0f, 0f));
        cam.SetViewAngles(yawDeg: yawDeg, pitchDeg: pitchDeg, zoom: zoom);
        return cam;
    }

    private const float Base = 16.5f;  // SeaLevel + 5, the harness line
    private const float Stack = 2f;
    private const float Thick = 16f;
    private const float Ts = Constants.TileSize;

    private static int Voxel(Camera cam, int tx, int ty, int mask, BillQuad[] faces,
        float n = Base, float e = Base, float s = Base, float w = Base)
        => SpriteRenderer.BuildBlockVoxel(cam, tx, ty, Base, Stack,
            mask, Thick, n, e, s, w, faces);

    private static bool CornerMatches(BillQuad q, float sx, float sy, float tol = 0.6f)
        => (MathF.Abs(q.BLx - sx) < tol && MathF.Abs(q.BLy - sy) < tol)
        || (MathF.Abs(q.BRx - sx) < tol && MathF.Abs(q.BRy - sy) < tol)
        || (MathF.Abs(q.TRx - sx) < tol && MathF.Abs(q.TRy - sy) < tol)
        || (MathF.Abs(q.TLx - sx) < tol && MathF.Abs(q.TLy - sy) < tol);

    // ── 1. Slab footprints follow the mask ────────────────────────────────

    [Fact]
    public void Voxel_IsolatedBlock_IsSpriteSizedSlab_NotFullTile()
    {
        var cam = MakeCamera(45f);
        var faces = new BillQuad[5];
        int count = Voxel(cam, 10, 10, 0, faces);

        var top = faces.Take(count).Where(f => f.Plane == 4).ToArray();
        Assert.True(top.Length == 1, $"exactly one top face (got {top.Length} of {count})");

        // Footprint centered on the tile: center ±16 in BOTH axes —
        // a 32x32 slab, not the 64x64 chunk.
        float cx = 10.5f * Ts, cy = 10.5f * Ts, topElev = Base + Stack;
        foreach (var (wx, wy) in new[]
        {
            (cx - Thick, cy - Thick), (cx + Thick, cy - Thick),
            (cx + Thick, cy + Thick), (cx - Thick, cy + Thick),
        })
        {
            var p = cam.WorldToScreen(wx, wy, topElev);
            Assert.True(CornerMatches(top[0], p.X, p.Y),
                $"slab corner ({wx},{wy}) missing from the top face");
        }
        // And NOT the fat tile corners.
        var fat = cam.WorldToScreen(10 * Ts, 10 * Ts, topElev);
        Assert.False(CornerMatches(top[0], fat.X, fat.Y, 2f),
            "mask 0 still draws a full-tile box — the slab contract is missing");
    }

    [Fact]
    public void Voxel_ConnectedSide_ExtendsToTileEdge()
    {
        var cam = MakeCamera(45f);
        var faces = new BillQuad[5];
        int count = Voxel(cam, 10, 10, 2, faces); // E connected only
        var top = faces.Take(count).Single(f => f.Plane == 4);

        float cx = 10.5f * Ts, cy = 10.5f * Ts, topElev = Base + Stack;
        // East edge reaches the tile boundary; west stays slab.
        var eastA = cam.WorldToScreen(11 * Ts, cy - Thick, topElev);
        var eastB = cam.WorldToScreen(11 * Ts, cy + Thick, topElev);
        Assert.True(CornerMatches(top, eastA.X, eastA.Y), "east extent must reach the tile edge");
        Assert.True(CornerMatches(top, eastB.X, eastB.Y), "east extent must reach the tile edge");
        var westA = cam.WorldToScreen(cx - Thick, cy - Thick, topElev);
        Assert.True(CornerMatches(top, westA.X, westA.Y), "unconnected west side stays slab-thick");
        var fatWest = cam.WorldToScreen(10 * Ts, cy - Thick, topElev);
        Assert.False(CornerMatches(top, fatWest.X, fatWest.Y, 2f),
            "west side bled to the tile edge with no W connection");
        // Perpendicular axis stays slab: no S connection → south = center+16.
        var southA = cam.WorldToScreen(cx - Thick, cy + Thick, topElev);
        Assert.True(CornerMatches(top, southA.X, southA.Y), "south side should stay slab-thick");
    }

    [Fact]
    public void Voxel_JoinedBlocks_ButtAtBoundary_AndDrawNoSharedFace()
    {
        // A (E-connected) and B (W-connected): A's east extent IS B's west
        // extent at the shared boundary — a butt join, pixel-for-pixel —
        // and neither draws the shared side face.
        var cam = MakeCamera(45f);
        var a = new BillQuad[5];
        var b = new BillQuad[5];
        int na = Voxel(cam, 10, 10, 2, a, e: Base + Stack);
        int nb = Voxel(cam, 11, 10, 8, b, w: Base + Stack);

        Assert.DoesNotContain(a.Take(na), f => f.Plane == 0);
        Assert.DoesNotContain(b.Take(nb), f => f.Plane == 2);

        var topA = a.Take(na).First(f => f.Plane == 4);
        var topB = b.Take(nb).First(f => f.Plane == 4);
        float cy = 10.5f * Ts, topElev = Base + Stack;
        foreach (var wy in new[] { cy - Thick, cy + Thick })
        {
            var bound = cam.WorldToScreen(11 * Ts, wy, topElev);
            Assert.True(CornerMatches(topA, bound.X, bound.Y),
                "A's east slab edge must sit on the shared boundary");
            Assert.True(CornerMatches(topB, bound.X, bound.Y),
                "B's west slab edge must sit on the same boundary (butt join)");
        }
    }

    // ── 2. Slice-5 culling contracts survive on the slab footprint ────────

    [Fact]
    public void Voxel_SideFace_ExposedStrip_StartsAtNeighbourTop()
    {
        var cam = MakeCamera(45f);
        float neighbourTop = Base + Stack - 1f;
        var faces = new BillQuad[5];
        int count = Voxel(cam, 10, 10, 2, faces, e: neighbourTop); // E, 1 shorter

        var east = faces.Take(count).Single(f => f.Plane == 0);
        float cy = 10.5f * Ts;
        foreach (var (wy, elev) in new[]
        {
            (cy - Thick, neighbourTop), (cy + Thick, neighbourTop),
            (cy - Thick, Base + Stack), (cy + Thick, Base + Stack),
        })
        {
            var p = cam.WorldToScreen(11 * Ts, wy, elev);
            Assert.True(CornerMatches(east, p.X, p.Y),
                $"strip corner (edge,{wy})@{elev} missing from the exposed E face");
        }
        var below = cam.WorldToScreen(11 * Ts, cy - Thick, neighbourTop - 0.5f);
        Assert.False(
            MathF.Abs(east.BLy - below.Y) < 0.6f || MathF.Abs(east.BRy - below.Y) < 0.6f
            || MathF.Abs(east.TRy - below.Y) < 0.6f || MathF.Abs(east.TLy - below.Y) < 0.6f,
            "face dips below the neighbour top — the strip is not culled");
    }

    [Fact]
    public void Voxel_TallerNeighbour_FullyCullsTheSide()
    {
        var cam = MakeCamera(45f);
        var faces = new BillQuad[5];
        int count = Voxel(cam, 10, 10, 2, faces, e: Base + Stack + 2f); // E towers
        Assert.Contains(faces.Take(count), f => f.Plane == 4); // the box exists…
        Assert.DoesNotContain(faces.Take(count), f => f.Plane == 0); // …but no E face
    }

    // ── 3. Camera-facing culling + depth order ────────────────────────────

    [Fact]
    public void Voxel_VisibleSides_FlipWithCameraYaw()
    {
        var faces = new BillQuad[5];
        var cam45 = MakeCamera(45f);
        int n45 = Voxel(cam45, 10, 10, 0, faces);
        var p45 = faces.Take(n45).Select(f => f.Plane).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { 0, 1, 4 }, p45); // view grows along (+,+): E and S near

        var cam225 = MakeCamera(225f);
        int n225 = Voxel(cam225, 10, 10, 0, faces);
        var p225 = faces.Take(n225).Select(f => f.Plane).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { 2, 3, 4 }, p225);
    }

    [Fact]
    public void Voxel_Faces_AreSortedBackToFront()
    {
        foreach (var yaw in new[] { 0f, 30f, 90f, 160f, 260f })
        {
            var cam = MakeCamera(yaw);
            var faces = new BillQuad[5];
            int count = Voxel(cam, 10, 10, 0, faces);
            Assert.True(count >= 2, $"yaw {yaw}: at least top + one side ({count})");
            for (int i = 1; i < count; i++)
                Assert.True(faces[i].Depth >= faces[i - 1].Depth - 0.001f,
                    $"yaw {yaw}: face {i} out of depth order");
        }
    }
}
