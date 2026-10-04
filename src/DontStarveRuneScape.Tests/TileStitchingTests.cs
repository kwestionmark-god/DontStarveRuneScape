namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// 2.5D terrain quads only stitch when every tile bordering a grid vertex
/// stores the identical corner height there. The shared convention: the
/// height at vertex (x, y) is the elevation of the tile whose min-corner
/// sits on that vertex. A drifted per-tile variant (the cave averaging each
/// corner with its own elevation) makes neighbors disagree, pulls adjacent
/// quads apart at every tile edge, and lets the sky-blue clear color bleed
/// through as glowing seams — the "gridlines" seen in cave captures on
/// 2026-10-03. These tests pin the convention for every world layer.
/// </summary>
public class TileStitchingTests
{
    [Fact]
    public void Cave_CornerHeights_AgreeAtEverySharedVertex()
    {
        var cave = CaveWorldSystem.Generate(12345, null, null);
        Assert.NotNull(cave);
        AssertSharedVertices(cave);
    }

    [Fact]
    public void Surface_CornerHeights_AgreeAtEverySharedVertex()
    {
        var dataLoader = new DataLoader();
        dataLoader.LoadAll();
        Assert.NotEmpty(dataLoader.Biomes); // without real biomes this test proves nothing
        var surface = WorldGen.Generate(12345, new BiomeRegistry(dataLoader.Biomes), null, null);
        AssertSharedVertices(surface);
    }

    [Fact]
    public void BuildCornerElevations_UsesMinCornerTileElevation()
    {
        var map = new TileMap(3, 3);
        // Distinct elevation per tile so a wrong pairing cannot cancel out.
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                map.Tiles[x, y].Elevation = 10 + x * 3 + y * 7;

        map.BuildCornerElevations();

        // Corner order [c00, c10, c11, c01] at (x,y) (x+1,y) (x+1,y+1) (x,y+1):
        // every vertex reads the elevation of its min-corner tile.
        // Elev(x,y) = 10 + 3x + 7y.
        Assert.Equal(10, map.Tiles[0, 0].CornerElevations![0]);
        Assert.Equal(13, map.Tiles[0, 0].CornerElevations![1]); // vertex (1,0) -> tile (1,0)
        Assert.Equal(20, map.Tiles[0, 0].CornerElevations![2]); // vertex (1,1) -> tile (1,1)
        Assert.Equal(17, map.Tiles[0, 0].CornerElevations![3]); // vertex (0,1) -> tile (0,1)
        // Out-of-bounds neighbors clamp to the tile's own elevation.
        Assert.Equal(16, map.Tiles[2, 0].CornerElevations![1]);
        Assert.Equal(30, map.Tiles[2, 2].CornerElevations![2]);
    }

    /// <summary>
    /// Interior grid vertices are shared by four tiles; all four must store
    /// the identical height there or the rendered quads pull apart.
    /// </summary>
    private static void AssertSharedVertices(TileMap map)
    {
        for (int x = 1; x < map.Width; x++)
        for (int y = 1; y < map.Height; y++)
        {
            Assert.NotNull(map.Tiles[x, y].CornerElevations);
            Assert.NotNull(map.Tiles[x - 1, y].CornerElevations);
            Assert.NotNull(map.Tiles[x - 1, y - 1].CornerElevations);
            Assert.NotNull(map.Tiles[x, y - 1].CornerElevations);

            float v = map.Tiles[x, y].CornerElevations![0];
            Assert.Equal(v, map.Tiles[x - 1, y].CornerElevations![1]);
            Assert.Equal(v, map.Tiles[x - 1, y - 1].CornerElevations![2]);
            Assert.Equal(v, map.Tiles[x, y - 1].CornerElevations![3]);
        }
    }
}
