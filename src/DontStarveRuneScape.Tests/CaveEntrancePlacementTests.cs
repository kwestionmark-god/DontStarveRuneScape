namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// The cave entrance is the surface map's single landmark and the only route
/// into the cave layer: every seed must place exactly one, inside the
/// reachable exploration band from spawn, on dry walkable ground. The old
/// placement maximized distance from spawn and buried the entrance at the
/// map edge where players never went.
/// </summary>
public class CaveEntrancePlacementTests
{
    private readonly ITestOutputHelper _output;

    public CaveEntrancePlacementTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(12345)]
    [InlineData(777)]
    [InlineData(-42)]
    public void Entrance_ExistsOnce_InReachableBand_OnWalkableGround(int seed)
    {
        var map = GenerateSurface(seed);
        var entrance = FindEntrance(map);
        Assert.NotNull(entrance);

        float dx = entrance.X - map.SpawnX;
        float dy = entrance.Y - map.SpawnY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        _output.WriteLine(
            $"seed {seed}: spawn ({map.SpawnX},{map.SpawnY}) entrance ({entrance.X},{entrance.Y}) distance {distance:F1} tiles");
        Assert.InRange(distance, 40f, 80f);

        // Walkable: dry, above the sea plane, no resource rooted on it.
        Assert.False(entrance.HasWater);
        Assert.True(entrance.Elevation >= Constants.SeaLevel + 2f);
        Assert.Null(entrance.ResourceNode);

        // The 3x3 rock collar (the terrain-backed entrance signature) stays
        // clear of rooted resources so nothing grows out of the stone.
        for (int cdx = -1; cdx <= 1; cdx++)
        for (int cdy = -1; cdy <= 1; cdy++)
        {
            var collarTile = map.GetTile(entrance.X + cdx, entrance.Y + cdy);
            Assert.NotNull(collarTile);
            Assert.Null(collarTile.ResourceNode);
        }

        int count = 0;
        foreach (var tile in map.Tiles)
            if (tile.IsCaveEntrance)
                count++;
        Assert.Equal(1, count);
    }

    /// <summary>Same generation path as Bootstrap: real biomes, resources
    /// irrelevant to entrance placement (the placer clears the 3x3 node
    /// patch around the entrance), no season system needed for the terrain
    /// pipeline.</summary>
    private static TileMap GenerateSurface(int seed)
    {
        var dataLoader = new DataLoader();
        dataLoader.LoadAll();
        Assert.NotEmpty(dataLoader.Biomes); // without real biomes this test proves nothing
        var biomeRegistry = new BiomeRegistry(dataLoader.Biomes);
        return WorldGen.Generate(seed, biomeRegistry, null, null);
    }

    private static Tile? FindEntrance(TileMap map)
    {
        foreach (var tile in map.Tiles)
            if (tile.IsCaveEntrance)
                return tile;
        return null;
    }
}
