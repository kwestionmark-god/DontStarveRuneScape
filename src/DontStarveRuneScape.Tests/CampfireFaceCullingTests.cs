namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>Campfire side-face culling (slice 12 follow-up): a 2x2
/// footprint brick must expose its N and W walls the same as S and E.
/// The face-cull neighbour scan originally stepped ALONG the face normal
/// instead of perpendicular, so the N/W faces sampled the footprint's own
/// interior sub-cells, saw the campfire's own column top, and self-culled
/// — only two of the four sides ever rendered (S/E visible, N/W flat).</summary>
public class CampfireFaceCullingTests
{
    private static (TileMap World, BuildingSystem Buildings) PlaceCampfireAt(int tx, int ty)
    {
        var world = new TileMap(8, 8);
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var buildings = new BuildingSystem { Registry = registry };
        var skills = new DontStarveRuneScape.Skills.SkillManager();
        var inventory = new Inv();
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        world.BuildCornerElevations();
        inventory.AddItem("stick", 30);
        inventory.AddItem("stone", 10);
        inventory.AddItem("dirt_brick", 64);
        var placed = buildings.PlaceStructure("campfire", tx, ty, 1, 1, world, inventory, skills);
        Assert.True(placed.Success, placed.Message);
        return (world, buildings);
    }

    [Fact]
    public void Campfire_Isolated_AllFourFaceNeighboursEmpty()
    {
        var (world, buildings) = PlaceCampfireAt(3, 3);
        // fp=2 at sub (1,1): every face's neighbour span is outside the
        // footprint, so no face may see a column top (self-cull bug: N/W
        // faces used to step into the footprint's own cells).
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, 0, -1)); // N
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 2, 1, 2, 1, 0));  // E
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 2, 2, 0, 1));  // S
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, -1, 0)); // W
    }

    [Fact]
    public void Campfire_BrickNorthOfPit_CullsOnlyNorthFace()
    {
        var (world, buildings) = PlaceCampfireAt(3, 3);
        var skills = new DontStarveRuneScape.Skills.SkillManager();
        var inventory = new Inv();
        inventory.AddItem("dirt_brick", 4);
        // Brick directly north of the pit's N-face row (sub (2,0)).
        Assert.True(buildings.PlaceStructure("mound_block", 3, 3, 2, 0, world, inventory, skills).Success);
        var def = buildings.Registry!.GetStructure("mound_block")!;
        float brickTop = Constants.SeaLevel + 5f + def.StackHeight;

        Assert.Equal(brickTop, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, 0, -1)); // N sees the brick
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, -1, 0)); // W does NOT
    }

    [Fact]
    public void Campfire_BrickWestOfPit_CullsOnlyWestFace()
    {
        var (world, buildings) = PlaceCampfireAt(3, 3);
        var inventory = new Inv();
        inventory.AddItem("dirt_brick", 4);
        // Brick directly west of the pit's W-face column (sub (0,1)).
        Assert.True(buildings.PlaceStructure("mound_block", 3, 3, 0, 1, world, inventory, new DontStarveRuneScape.Skills.SkillManager()).Success);
        var def = buildings.Registry!.GetStructure("mound_block")!;
        float brickTop = Constants.SeaLevel + 5f + def.StackHeight;

        Assert.Equal(brickTop, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, -1, 0)); // W sees the brick
        Assert.Equal(float.NegativeInfinity, SpriteRenderer.FootprintFaceTop(world, buildings, 3, 3, 1, 1, 2, 0, -1)); // N does NOT
    }
}
