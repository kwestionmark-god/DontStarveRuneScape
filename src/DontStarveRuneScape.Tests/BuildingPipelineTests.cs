namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Structure data loading from the real structures.json nested envelope and
/// the BuildingSystem placement flow: material gate, biome gate, consume and
/// place.
/// </summary>
public class BuildingPipelineTests
{
    [Fact]
    public void LoadAll_ReadsRealStructuresJson()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        Assert.True(registry.Structures.Count >= 25, $"expected 25+ structures, got {registry.Structures.Count}");

        var campfire = registry.GetStructure("campfire");
        Assert.NotNull(campfire);
        Assert.Equal("Campfire", campfire!.Name);
        Assert.Equal(2, campfire.Materials.Length);
        Assert.Equal(("stick", 2), (campfire.Materials[0].ItemId, campfire.Materials[0].Quantity));
        Assert.Equal(("stone", 2), (campfire.Materials[1].ItemId, campfire.Materials[1].Quantity));
        Assert.False(campfire.OccupiesTile);
        Assert.True(campfire.Burnable);
        Assert.Equal("common", campfire.SubStat);
        Assert.Contains("forest", campfire.BiomeCompatibility);

        var ballista = registry.GetStructure("ballista");
        Assert.NotNull(ballista);
        Assert.Equal("offensive", ballista!.SubStat);
        Assert.True(ballista.Damage > 0);
    }

    [Fact]
    public void PlaceStructure_ConsumesMaterials_AndPlaces()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var inventory = new Inventory();
        inventory.AddItem("stick", 5);
        inventory.AddItem("stone", 5);
        var world = new TileMap(8, 8);

        var result = system.PlaceStructure("campfire", 3, 3, world, inventory, new SkillManager());
        Assert.True(result.Success, result.Message);

        var structure = Assert.Single(system.Structures);
        Assert.Equal("campfire", structure.StructureId);
        Assert.Equal(3, structure.TileX);
        Assert.Equal(3, structure.TileY);
        Assert.Equal(3, inventory.GetItemQuantity("stick"));
        Assert.Equal(3, inventory.GetItemQuantity("stone"));
    }

    [Fact]
    public void PlaceStructure_FailsWithoutMaterials_AndDoesNotConsume()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var inventory = new Inventory();
        var world = new TileMap(8, 8);

        var result = system.PlaceStructure("campfire", 3, 3, world, inventory, new SkillManager());
        Assert.False(result.Success);
        Assert.Empty(system.Structures);
    }

    [Fact]
    public void PlaceStructure_FailsInIncompatibleBiome()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var inventory = new Inventory();
        inventory.AddItem("stick", 5);
        inventory.AddItem("stone", 5);
        var world = new TileMap(8, 8);
        world.GetTile(3, 3)!.Biome = new BiomeDef { Id = "ocean" };

        var result = system.PlaceStructure("campfire", 3, 3, world, inventory, new SkillManager());
        Assert.False(result.Success);
        Assert.Empty(system.Structures);
    }
}
