namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// Vertical building, slice 5 (spec:
/// docs/superpowers/specs/2026-10-11-block-voxel-render-design.md): blocks
/// render as world-space boxes — top + camera-facing side faces through
/// WorldToScreen, sprite mapped as surface area, shared faces culled so
/// joined blocks stitch into one volume.
///
/// Plane tags: 0=E(+X), 1=S(+Y), 2=W(−X), 3=N(−Y), 4=Top.
/// Faces are depth-sorted back-to-front; group by .Plane, never by index.
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

    private static bool CornerMatches(BillQuad q, float sx, float sy, float tol = 0.6f)
        => (MathF.Abs(q.BLx - sx) < tol && MathF.Abs(q.BLy - sy) < tol)
        || (MathF.Abs(q.BRx - sx) < tol && MathF.Abs(q.BRy - sy) < tol)
        || (MathF.Abs(q.TRx - sx) < tol && MathF.Abs(q.TRy - sy) < tol)
        || (MathF.Abs(q.TLx - sx) < tol && MathF.Abs(q.TLy - sy) < tol);

    // ── 1. Top face is the TRUE tile top (the stitch contract) ────────────

    [Fact]
    public void Voxel_TopFace_CornersAreProjectedTileCorners()
    {
        var cam = MakeCamera(45f);
        var faces = new BillQuad[5];
        int count = SpriteRenderer.BuildBlockVoxel(cam, 10, 10, Base, Stack,
            Base, Base, Base, Base, faces);

        var top = faces.Take(count).Where(f => f.Plane == 4).ToArray();
        Assert.True(top.Length == 1, $"exactly one top face (got {top.Length} of {count})");

        float ts = Constants.TileSize;
        float topElev = Base + Stack;
        foreach (var (wx, wy) in new[]
        {
            (10 * ts, 10 * ts), (11 * ts, 10 * ts), (11 * ts, 11 * ts), (10 * ts, 11 * ts),
        })
        {
            var p = cam.WorldToScreen(wx, wy, topElev);
            Assert.True(CornerMatches(top[0], p.X, p.Y),
                $"tile corner ({wx},{wy}) missing from the top face");
        }
    }

    [Fact]
    public void Voxel_JoinedBlocks_ShareProjectedTopEdge_AndDrawNoSharedFace()
    {
        // Two equal-height neighbours east-west: A's east top edge must BE
        // B's west top edge pixel-for-pixel, and neither draws the shared
        // side face — two mounds stitch to one volume.
        var cam = MakeCamera(45f);
        float ts = Constants.TileSize;
        var a = new BillQuad[5];
        var b = new BillQuad[5];
        int na = SpriteRenderer.BuildBlockVoxel(cam, 10, 10, Base, Stack,
            Base, Base + Stack, Base, Base, a); // east neighbour covers
        int nb = SpriteRenderer.BuildBlockVoxel(cam, 11, 10, Base, Stack,
            Base, Base, Base, Base + Stack, b); // west neighbour covers

        Assert.DoesNotContain(a.Take(na), f => f.Plane == 0); // A east face gone
        Assert.DoesNotContain(b.Take(nb), f => f.Plane == 2); // B west face gone

        var topA = a.Take(na).First(f => f.Plane == 4);
        float topElev = Base + Stack;
        var e0 = cam.WorldToScreen(11 * ts, 10 * ts, topElev);
        var e1 = cam.WorldToScreen(11 * ts, 11 * ts, topElev);
        Assert.True(CornerMatches(topA, e0.X, e0.Y) && CornerMatches(topA, e1.X, e1.Y),
            "A's east top edge must sit exactly on the shared tile boundary");
    }

    // ── 2. Exposed strips and taller-neighbour culling ────────────────────

    [Fact]
    public void Voxel_SideFace_ExposedStrip_StartsAtNeighbourTop()
    {
        // East neighbour one stack unit shorter: the E face survives only
        // as the strip from neighbourTop to own top.
        var cam = MakeCamera(45f);
        float ts = Constants.TileSize;
        float neighbourTop = Base + Stack - 1f;
        var faces = new BillQuad[5];
        int count = SpriteRenderer.BuildBlockVoxel(cam, 10, 10, Base, Stack,
            Base, neighbourTop, Base, Base, faces);

        var east = faces.Take(count).Single(f => f.Plane == 0);
        foreach (var (wx, wy, elev) in new[]
        {
            (11 * ts, 10 * ts, neighbourTop), (11 * ts, 11 * ts, neighbourTop),
            (11 * ts, 10 * ts, Base + Stack), (11 * ts, 11 * ts, Base + Stack),
        })
        {
            var p = cam.WorldToScreen(wx, wy, elev);
            Assert.True(CornerMatches(east, p.X, p.Y),
                $"strip corner ({wx},{wy})@{elev} missing from the exposed E face");
        }
        // And no corner may dip BELOW the neighbour top.
        var below = cam.WorldToScreen(11 * ts, 10 * ts, neighbourTop - 0.5f);
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
        int count = SpriteRenderer.BuildBlockVoxel(cam, 10, 10, Base, Stack,
            Base, Base + Stack + 2f, Base, Base, faces); // east neighbour towers
        Assert.Contains(faces.Take(count), f => f.Plane == 4); // the box exists…
        Assert.DoesNotContain(faces.Take(count), f => f.Plane == 0); // …but no E face
    }

    // ── 3. Camera-facing culling + depth order ────────────────────────────

    [Fact]
    public void Voxel_VisibleSides_FlipWithCameraYaw()
    {
        var faces = new BillQuad[5];
        var cam45 = MakeCamera(45f);
        int n45 = SpriteRenderer.BuildBlockVoxel(cam45, 10, 10, Base, Stack,
            Base, Base, Base, Base, faces);
        // View depth grows along (sin 45°, cos 45°) = (+, +): E and S near.
        var p45 = faces.Take(n45).Select(f => f.Plane).OrderBy(p => p).ToArray();
        Assert.Equal(new[] { 0, 1, 4 }, p45);

        var cam225 = MakeCamera(225f);
        int n225 = SpriteRenderer.BuildBlockVoxel(cam225, 10, 10, Base, Stack,
            Base, Base, Base, Base, faces);
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
            int count = SpriteRenderer.BuildBlockVoxel(cam, 10, 10, Base, Stack,
                Base, Base, Base, Base, faces);
            Assert.True(count >= 2, $"yaw {yaw}: at least top + one side ({count})");
            for (int i = 1; i < count; i++)
                Assert.True(faces[i].Depth >= faces[i - 1].Depth - 0.001f,
                    $"yaw {yaw}: face {i} out of depth order");
        }
    }
}
