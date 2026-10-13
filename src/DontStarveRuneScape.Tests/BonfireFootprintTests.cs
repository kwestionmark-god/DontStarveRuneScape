namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 18: the BONFIRE — the first 3x3 footprint
/// structure and the third hearth tier (campfire → lined pit → bonfire).
/// Bigger open flame: more contact damage, longer burn per unit, and a
/// warmth_radius def field the hearth fire inherits (slice 17's consumer
/// grows with the tier). Covers the odd-footprint centering law.
/// </summary>
public class BonfireFootprintTests
{
    private static void AssertIhdr(string path, int w, int h)
    {
        Assert.True(File.Exists(path), $"expected texture at {path}");
        var bytes = File.ReadAllBytes(path);
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        Assert.Equal(w, width);
        Assert.Equal(h, height);
    }

    [Fact]
    public void BonfireDef_IsThe3x3ThirdTier()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var bonfire = registry.GetStructure("bonfire")!;

        Assert.True(bonfire.IsBlock);
        Assert.Equal(3, bonfire.Footprint);
        Assert.Equal(0.75f, bonfire.StackHeight);
        Assert.Equal(3f, bonfire.ContactDamage);
        Assert.Equal(180f, bonfire.BurnSeconds); // 90 light_seconds x 2.0
        Assert.Equal(30f, bonfire.Hp);
        Assert.Equal(3, bonfire.RequiresSkillLevel);
        Assert.Equal(5, bonfire.WarmthRadius);   // bigger fire warms wider
        Assert.True(bonfire.OccupiesTile);
        Assert.Equal("structure/bonfire", bonfire.SpriteKey);
        Assert.Equal("structure/bonfire_side", bonfire.SideSpriteKey);
        // The tier chain terminates here.
        Assert.Equal("bonfire", registry.GetStructure("lined_fire_pit")!.UpgradesTo);
        Assert.Null(bonfire.UpgradesTo);
    }

    [Fact]
    public void BonfireTextures_Ship()
    {
        // The 3x3 face spans 48 world-px; art authored at 1px per world-px.
        AssertIhdr("Assets/sprites/structure/bonfire.png", 48, 48);
        AssertIhdr("Assets/sprites/structure/bonfire_side.png", 48, 24);
    }

    [Fact]
    public void OtherHearths_KeepDefaultWarmthRadius()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        Assert.Equal(3, registry.GetStructure("campfire")!.WarmthRadius);
        Assert.Equal(3, registry.GetStructure("lined_fire_pit")!.WarmthRadius);
        Assert.Equal(3, registry.GetStructure("mound_block")!.WarmthRadius);
    }

    private sealed class Harness
    {
        public TileMap World = new(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public Skills.Firemaking.FiremakingSkill Firemaking = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            World.BuildCornerElevations();
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("stone", 30);
            Skills.AddXp("construction", 1000f); // construction >= 3
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public (bool Success, string Message) PlaceAt(string id, int tx, int ty, int subX, int subY)
            => Buildings.PlaceStructure(id, tx, ty, subX, subY, World, Inventory, Skills);
    }

    [Fact]
    public void BonfirePlacement_Covers3x3_OddCentering()
    {
        var h = new Harness();
        var placed = h.PlaceAt("bonfire", 1, 1, 0, 0);
        Assert.True(placed.Success, placed.Message);

        // The odd 3x3 anchored at sub (0,0) covers cells 0..2 per axis;
        // all report the squat 0.75 block height.
        for (int sx = 0; sx <= 2; sx++)
        for (int sy = 0; sy <= 2; sy++)
            Assert.Equal(0.75f,
                BlockStack.SubHeightAt(h.World, h.Buildings, 1, 1, sx, sy));
        // The free sub-column/row stays clear.
        Assert.Equal(0f, BlockStack.SubHeightAt(h.World, h.Buildings, 1, 1, 3, 0));
        Assert.Equal(0f, BlockStack.SubHeightAt(h.World, h.Buildings, 1, 1, 0, 3));
    }

    [Fact]
    public void BonfireHearthFire_InheritsWarmthRadius()
    {
        var def = new StructureDef
        {
            Name = "Bonfire", Footprint = 3, WarmthRadius = 5,
            LightSeconds = 90f, BurnRate = 2f,
        };
        var firemaking = new Skills.Firemaking.FiremakingSkill();
        firemaking.LightHearthUnit("oak_logs", def, 100f, 100f);
        Assert.Equal(5, firemaking.GetActiveFires()[0].WarmthRadius);
    }
}
