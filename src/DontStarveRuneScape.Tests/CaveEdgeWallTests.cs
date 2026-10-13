namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Cave Stage 2¾ (user-approved 2026-10-03, never started): visual cave
/// WALLS at the map edge — the 64x64 cave ends abruptly in the sky-blue
/// clear color. The fix builds a two-ring stepped rock cliff from the
/// per-corner elevation machinery: ring 2 (x or y == 1 or size-2) lifts
/// to 28, ring 1 (x or y == 0 or size-1) to 31 — both past the floor's
/// 13..26 band so the mass reads as enclosure and the CanStep height
/// gate refuses walking into it. The walls run BEFORE ore veins are
/// placed so the wall ring stays free of nodes.
/// </summary>
public class CaveEdgeWallTests
{
    private static TileMap Gen(int seed) =>
        CaveWorldSystem.Generate(seed, CaveInnardsTests.RealResources(), null);

    [Theory]
    [InlineData(12345)]
    [InlineData(777)]
    public void OuterTwoRings_AreWall(int seed)
    {
        var map = Gen(seed);
        for (int x = 0; x < 64; x++)
        for (int y = 0; y < 64; y++)
        {
            var t = map.Tiles[x, y];
            int ring = Math.Min(Math.Min(x, 63 - x), Math.Min(y, 63 - y));
            if (ring == 0)
                Assert.True(t.Elevation >= 31f, $"({x},{y}) ring-1 lip should top out the wall, elev {t.Elevation}");
            else if (ring == 1)
                Assert.True(t.Elevation >= 28f, $"({x},{y}) ring-2 shoulder should lift, elev {t.Elevation}");
            else
                Assert.True(t.Elevation <= 26f, $"({x},{y}) interior floor must stay in 13..26, elev {t.Elevation}");
        }
    }

    [Fact]
    public void Walls_StayStitched_ToTheFloor()
    {
        var map = Gen(12345);
        // Shared-vertex law from the 5660368 stitch fix: every corner reads
        // the min-corner tile's elevation. A wall must share edges exactly
        // with its neighbor — no glowing seams at the cliff crease.
        for (int x = 0; x < 63; x++)
        for (int y = 0; y < 63; y++)
        {
            var t = map.Tiles[x, y];
            Assert.Equal(t.Elevation, t.CornerElevations![0]);
        }
    }

    [Fact]
    public void WallRing_HasNoOreNodes()
    {
        var map = Gen(12345);
        for (int i = 0; i < 64; i++)
        foreach (var (x, y) in new[] { (0, i), (1, i), (i, 0), (i, 1), (63, i), (62, i), (i, 63), (i, 62) })
            Assert.Null(map.Tiles[x, y].ResourceNode);
        // Spawn (the cave exit) is NOT on the wall.
        Assert.True(map.Tiles[map.SpawnX, map.SpawnY].IsCaveExit);
        Assert.True(map.Tiles[map.SpawnX, map.SpawnY].Elevation <= 26f);
    }
}
