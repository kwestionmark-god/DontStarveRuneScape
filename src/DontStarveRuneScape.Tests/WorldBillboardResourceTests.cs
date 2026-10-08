namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// World-fixed resource billboards: inanimate resources stand on static
/// world planes — the same crossed-billboard paper-doll the entities use —
/// instead of camera-facing screen quads. The not-a-billboard signature is
/// the crossed planes' horizontal extents swapping as the camera orbits
/// (the anchor flips), while every quad's base edge stays anchored to the
/// projected world line through the tile center at any yaw.
/// </summary>
public class WorldBillboardResourceTests
{
    private static Camera MakeCamera(float yawDeg, float pitchDeg = 30f, float zoom = 1f)
    {
        var cam = new Camera(1280, 720);
        // WorldToScreen is identity without a player — set one so the
        // tests exercise the real yaw/pitch/zoom projection.
        cam.SetPlayer(new Player(0f, 0f));
        cam.SetViewAngles(yawDeg: yawDeg, pitchDeg: pitchDeg, zoom: zoom);
        return cam;
    }

    [Fact]
    public void DeterministicDir_SameTile_SameDir()
    {
        var a = SpriteRenderer.GetDeterministicDir(12, 34);
        var b = SpriteRenderer.GetDeterministicDir(12, 34);
        Assert.Equal(a, b);
    }

    [Fact]
    public void DeterministicDir_SixteenTileRow_SixteenDistinctDirs_AndUnit()
    {
        // Odd multipliers make the hash's low nibble a bijection in the
        // tile x: 16 consecutive tiles cover all 16 orientation buckets.
        var dirs = new (float X, float Y)[16];
        for (int x = 0; x < 16; x++)
            dirs[x] = SpriteRenderer.GetDeterministicDir(x, 34);
        Assert.Equal(16, dirs.Distinct().Count());
        var d = dirs[0];
        Assert.Equal(1f, MathF.Sqrt(d.X * d.X + d.Y * d.Y), 4);
    }

    [Fact]
    public void ResourcePlanes_HorizontalExtent_FlipsWithYaw()
    {
        // A world-fixed plane's on-screen horizontal extent follows its
        // world axis; a camera-facing billboard never changes. With the
        // plane axis at (0.6, 0.8), the cross plane is wider at yaw 0 and
        // the travel plane wider at yaw 90 — the crossed-billboard flip.
        const float wx = 0f, wy = 0f, elev = 0f, halfW = 24f, height = 96f;
        const float dirX = 0.6f, dirY = 0.8f, perpX = -0.8f, perpY = 0.6f;

        var cam0 = MakeCamera(0f);
        var qDir0 = SpriteRenderer.ProjectBodyBillboard(cam0, wx, wy, elev, halfW, height, 0f, dirX, dirY);
        var qPerp0 = SpriteRenderer.ProjectBodyBillboard(cam0, wx, wy, elev, halfW, height, 0f, perpX, perpY);
        Assert.True(qDir0.HalfW < qPerp0.HalfW);

        var cam90 = MakeCamera(90f);
        var qDir90 = SpriteRenderer.ProjectBodyBillboard(cam90, wx, wy, elev, halfW, height, 0f, dirX, dirY);
        var qPerp90 = SpriteRenderer.ProjectBodyBillboard(cam90, wx, wy, elev, halfW, height, 0f, perpX, perpY);
        Assert.True(qDir90.HalfW > qPerp90.HalfW);
    }

    [Fact]
    public void ResourceBillboard_BaseCorners_StayOnWorldLine_WhenCameraRotates()
    {
        // The base edge spans the tile-center world line along the plane
        // axis; its projected midpoint is the projected ground point at
        // every yaw — the sprite never re-anchors to the viewer.
        const float wx = 3.5f * 64f, wy = 9.5f * 64f, elev = 0f;
        const float halfW = 24f, height = 96f, dirX = 0.6f, dirY = 0.8f;

        foreach (float yawDeg in new[] { 0f, 40f, 90f })
        {
            var cam = MakeCamera(yawDeg);
            var q = SpriteRenderer.ProjectBodyBillboard(cam, wx, wy, elev, halfW, height, 0f, dirX, dirY);
            var ground = cam.WorldToScreen(wx, wy, elev);
            Assert.Equal(ground.X, (q.BLx + q.BRx) * 0.5f, 1);
            Assert.Equal(ground.Y, (q.BLy + q.BRy) * 0.5f, 1);
        }
    }

    [Fact]
    public void ResourceBillboard_TopEdge_RisesWithPitch()
    {
        // World-fixed verticals foreshorten with pitch (tempered by the
        // body response clamp): a steeper camera sees a taller sprite.
        const float wx = 0f, wy = 0f, elev = 0f, halfW = 24f, height = 96f;
        const float dirX = 0.6f, dirY = 0.8f;

        var shallow = MakeCamera(0f, pitchDeg: 20f);
        var qs = SpriteRenderer.ProjectBodyBillboard(shallow, wx, wy, elev, halfW, height, 0f, dirX, dirY);
        var steep = MakeCamera(0f, pitchDeg: 50f);
        var qt = SpriteRenderer.ProjectBodyBillboard(steep, wx, wy, elev, halfW, height, 0f, dirX, dirY);

        Assert.True(qs.HalfH < qt.HalfH);
    }

    [Fact]
    public void ResourceCross_FourSortedHalves_AnchorOnTileCenter()
    {
        var cam = MakeCamera(35f);
        var halves = new BillQuad[4];
        SpriteRenderer.BuildResourceCrossBillboard(cam, 3, 9, 5f, 24f, 96f, halves, out var anchor);

        Assert.Equal(4, halves.Length);
        // Back-to-front: ascending view depth.
        for (int i = 1; i < halves.Length; i++)
            Assert.True(halves[i - 1].Depth <= halves[i].Depth);
        // Both planes present, each split at the fold into its two U spans.
        Assert.Equal(2, halves.Count(h => h.Plane == 0));
        Assert.Equal(2, halves.Count(h => h.Plane == 1));
        Assert.Equal(2, halves.Count(h => MathF.Abs(h.U0) < 0.01f));
        Assert.Equal(2, halves.Count(h => MathF.Abs(h.U0 - 0.5f) < 0.01f));
        // The anchor stands on the projected tile-center world line.
        var ground = cam.WorldToScreen(3.5f * 64f, 9.5f * 64f, 5f);
        Assert.Equal(ground.X, (anchor.BLx + anchor.BRx) * 0.5f, 1);
        Assert.Equal(ground.Y, (anchor.BLy + anchor.BRy) * 0.5f, 1);
        // Deterministic: same tiles build identical quads (inanimate —
        // the facing never shimmers between frames).
        var halves2 = new BillQuad[4];
        SpriteRenderer.BuildResourceCrossBillboard(cam, 3, 9, 5f, 24f, 96f, halves2, out var anchor2);
        Assert.Equal(anchor.BLx, anchor2.BLx);
        Assert.Equal(halves[3].Depth, halves2[3].Depth);
    }

    [Fact]
    public void ResourceCross_SizeScalesWithDisplayScale()
    {
        var cam = MakeCamera(35f);
        SpriteRenderer.BuildResourceCrossBillboard(cam, 1, 1, 0f, 24f, 96f, new BillQuad[4], out var small);
        SpriteRenderer.BuildResourceCrossBillboard(cam, 1, 1, 0f, 48f, 192f, new BillQuad[4], out var big);
        Assert.Equal(2f * small.HalfH, big.HalfH, 1);
        Assert.Equal(2f * small.HalfW, big.HalfW, 1);
    }
}
