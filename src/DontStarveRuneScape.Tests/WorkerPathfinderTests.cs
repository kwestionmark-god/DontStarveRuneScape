namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Worker A* routing from slice B of the colony fusion: dry/occupied tile
/// rules, water detours, cliff rejection, elevation cost, and the no-corner-
/// cutting diagonal rule.
/// </summary>
public class WorkerPathfinderTests
{
    private const float Dry = 15f;
    private const float Water = 0f;

    private static TileMap FlatWorld(int size)
    {
        var world = new TileMap(size, size);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                world.Tiles[x, y].Elevation = Dry;
        return world;
    }

    private static void Raise(TileMap world, int x, int y, float elevation)
        => world.Tiles[x, y].Elevation = elevation;

    [Fact]
    public void CanStand_RequiresDryUnoccupiedTile()
    {
        var world = FlatWorld(4);
        Assert.True(WorkerPathfinder.CanStand(world, 1, 1));

        Raise(world, 1, 1, Water);
        Assert.False(WorkerPathfinder.CanStand(world, 1, 1));

        Raise(world, 1, 1, Dry);
        world.Tiles[1, 1].Structure = new StructureDef { OccupiesTile = true };
        Assert.False(WorkerPathfinder.CanStand(world, 1, 1));
    }

    [Fact]
    public void FindPath_SameTile_ReturnsEmptyPath()
    {
        var world = FlatWorld(4);
        Assert.NotNull(WorkerPathfinder.FindPath(world, 2, 2, 2, 2));
        Assert.Empty(WorkerPathfinder.FindPath(world, 2, 2, 2, 2)!);
    }

    [Fact]
    public void FindPath_AdjacentTarget_IsOneStep()
    {
        var world = FlatWorld(4);
        var path = WorkerPathfinder.FindPath(world, 1, 1, 2, 1);
        Assert.NotNull(path);
        Assert.Equal([(2, 1)], path!);
    }

    [Fact]
    public void FindPath_AllowsDiagonal_WhenBothOrthogonalsAreFree()
    {
        var world = FlatWorld(4);
        var path = WorkerPathfinder.FindPath(world, 1, 1, 2, 2);
        Assert.NotNull(path);
        Assert.Equal([(2, 2)], path!);
    }

    [Fact]
    public void FindPath_NeverCutsBlockedCorners()
    {
        // (1,1) -> (2,2) with both orthogonal neighbors underwater: the
        // direct diagonal is illegal, so the route must detour around.
        var world = FlatWorld(6);
        Raise(world, 2, 1, Water);
        Raise(world, 1, 2, Water);

        var path = WorkerPathfinder.FindPath(world, 1, 1, 2, 2);

        Assert.NotNull(path);
        Assert.True(path!.Count > 1, "diagonal shortcut through blocked orthogonals must not be used");
        foreach (var (x, y) in path)
            Assert.True(WorkerPathfinder.CanStand(world, x, y), $"path steps on blocked tile ({x},{y})");
        Assert.Equal((2, 2), path[^1]);
    }

    [Fact]
    public void FindPath_DetoursAroundWaterWall_ThroughTheGap()
    {
        var world = FlatWorld(8);
        for (int y = 0; y < 8; y++)
            if (y != 6)
                Raise(world, 3, y, Water); // wall at x=3 with a single gap at y=6

        var path = WorkerPathfinder.FindPath(world, 1, 1, 6, 1);

        Assert.NotNull(path);
        Assert.Contains((3, 6), path!); // crossed through the only gap
        Assert.Equal((6, 1), path![^1]);
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenTargetUnreachable()
    {
        var world = FlatWorld(8);
        for (int y = 0; y < 8; y++)
            Raise(world, 3, y, Water); // solid water wall, no gap

        Assert.Null(WorkerPathfinder.FindPath(world, 1, 1, 6, 1));
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenTargetIsBlockedOrWet()
    {
        var world = FlatWorld(6);
        Raise(world, 5, 5, Water);
        Assert.Null(WorkerPathfinder.FindPath(world, 1, 1, 5, 5));

        Raise(world, 5, 5, Dry);
        world.Tiles[5, 5].Structure = new StructureDef { OccupiesTile = true };
        Assert.Null(WorkerPathfinder.FindPath(world, 1, 1, 5, 5));
    }

    [Fact]
    public void FindPath_RejectsStepsAboveCliffThreshold()
    {
        // Plateau at elevation 30 surrounded by elevation 15: no ramp means
        // no route (every approach step exceeds the 2.0 cliff threshold).
        var world = FlatWorld(6);
        Raise(world, 4, 4, 30f);
        Assert.Null(WorkerPathfinder.FindPath(world, 1, 1, 4, 4));
    }

    [Fact]
    public void FindPath_ClimbsOnlyThroughSlopeWithinThreshold()
    {
        // Plateau at (6,2) elevation 20 with a two-step ramp at (4,2)/(5,2);
        // every other approach stays a >2.0 cliff step.
        var world = FlatWorld(8);
        Raise(world, 4, 2, 16f);
        Raise(world, 5, 2, 18f);
        Raise(world, 6, 2, 20f);

        var path = WorkerPathfinder.FindPath(world, 1, 2, 6, 2);

        Assert.NotNull(path);
        Assert.Equal((6, 2), path![^1]);
        Assert.Contains((5, 2), path!); // climbed the ramp
        // Every step respects the cliff threshold.
        for (int i = 0; i < path.Count; i++)
        {
            var (x, y) = path[i];
            Assert.True(world.Tiles[x, y].Elevation <= 20f);
            Assert.True(WorkerPathfinder.CanStand(world, x, y));
        }
    }
}
