namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 1 (spec:
/// docs/superpowers/specs/2026-10-11-vertical-building-foundations-design.md):
/// block-typed structure defs place on flat pads only, and the shovel's
/// timed flatten action makes a sloped tile level — training construction.
/// Terrain edits persist as WorldSnapshot.TerrainEdits.
/// </summary>
public class VerticalBuildingTests
{
    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public ActionSystem Actions = new();

        public Harness()
        {
            // Flat land above the sea; corner quads derive from neighbors.
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            World.BuildCornerElevations();
            Actions.SetTileMap(World);
            // RED carries both wallets until GREEN flips the def to dirt_brick.
            Inventory.AddItem("stone", 20);
            Inventory.AddItem("stick", 20);
            Inventory.AddItem("dirt_brick", 20);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public (bool Success, string Message) PlaceBrick(int tx, int ty, int subX, int subY)
            => Buildings.PlaceStructure("mound_block", tx, ty, subX, subY, World, Inventory, Skills);

        /// <summary>Turn (4,4) into a slope: three neighbors one level up,
        /// giving (4,4) a corner spread of 1.0 and a 17.25-mean target of 17.</summary>
        public void MakeSlopeAt44()
        {
            World.Tiles[5, 4].Elevation = Constants.SeaLevel + 6f;
            World.Tiles[5, 5].Elevation = Constants.SeaLevel + 6f;
            World.Tiles[4, 5].Elevation = Constants.SeaLevel + 6f;
            World.BuildCornerElevations();
        }

        public ActionResult? RunTimedAction()
        {
            float remaining = Constants.FlattenSeconds;
            while (remaining > 0.25f)
            {
                Assert.Null(Actions.Update(0.25f));
                remaining -= 0.25f;
            }
            return Actions.Update(0.25f);
        }
    }

    // ── 1. Data laws ────────────────────────────────────────────────────────

    [Fact]
    public void BlockDef_ParsesAsBrickWithFixedUnitHeight()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var block = registry.GetStructure("mound_block");
        Assert.NotNull(block);
        Assert.True(block!.IsBlock);
        // The texel-exact brick: 16 px at 12.8 px/unit = 1.25 units.
        Assert.Equal(1.25f, block.StackHeight);
        Assert.Equal("Dirt Brick", block.Name);
        Assert.True(block.OccupiesTile);
        Assert.Equal(1, block.RequiresSkillLevel);
    }

    [Fact]
    public void BlockLaw_HoldsAcrossAllDefs()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        foreach (var def in registry.Structures.Values)
        {
            if (!def.IsBlock) continue;
            Assert.True(def.StackHeight > 0f, $"{def.Id}: block def has no stack_height");
            Assert.True(def.OccupiesTile, $"{def.Id}: block must occupy its tile");
        }
    }

    // ── 2. Flat-pad gate ────────────────────────────────────────────────────

    [Fact]
    public void Block_PlacesOnFlatPad()
    {
        var h = new Harness();

        var result = h.PlaceBrick(4, 4, 2, 2);

        Assert.True(result.Success, result.Message);
        // Placement consumes one crafted brick; stone/stick are untouched
        // (they were spent at the crafting bench, not here).
        Assert.Equal(19, h.Inventory.GetItemQuantity("dirt_brick"));
        Assert.Equal(20, h.Inventory.GetItemQuantity("stone"));
        Assert.Equal(20, h.Inventory.GetItemQuantity("stick"));
        Assert.NotNull(h.World.GetTile(4, 4)!.Structure);
    }

    [Fact]
    public void Block_RefusesSlope_WithHonestMessage()
    {
        var h = new Harness();
        h.MakeSlopeAt44();

        var result = h.PlaceBrick(4, 4, 2, 2);

        Assert.False(result.Success);
        Assert.Contains("level", result.Message);
        // Refusal consumed nothing, occupied nothing.
        Assert.Equal(20, h.Inventory.GetItemQuantity("dirt_brick"));
        Assert.Empty(h.Buildings.Structures);
    }

    [Fact]
    public void NonBlock_StillPlacesOnSlope_NoNewGateForOldDefs()
    {
        var h = new Harness();
        h.MakeSlopeAt44();

        var result = h.Buildings.PlaceStructure("campfire", 4, 4, h.World, h.Inventory, h.Skills);

        Assert.True(result.Success, result.Message);
    }

    // ── 3. Flatten action ───────────────────────────────────────────────────

    [Fact]
    public void Flatten_ShovelAction_LevelsSlope_AndBlockThenPlaces()
    {
        var h = new Harness();
        h.MakeSlopeAt44();
        Assert.True(h.Inventory.AddItem("shovel", 1));
        float before = h.World.GetTile(4, 4)!.Elevation;

        var err = h.Actions.StartAction(ActionType.Flattening, null, h.Skills, h.Inventory, tileXy: (4, 4));
        Assert.Null(err); // RED: no flatten branch yet — "No valid target."
        var result = h.RunTimedAction();
        Assert.NotNull(result);
        h.Actions.ProcessCompletion(result, h.Inventory, h.Skills);

        // Target: median of the corner sources (the agreement the 3 raised
        // neighbors already have) = 17.5f.
        Assert.Equal(17.5f, h.World.GetTile(4, 4)!.Elevation);
        Assert.NotEqual(before, h.World.GetTile(4, 4)!.Elevation);

        // Corner quads refreshed: (4,4)'s four quad corners now all agree
        // with the neighbors — the pad reads flat to the placement gate.
        Assert.All(h.World.GetTile(4, 4)!.CornerElevations!, c => Assert.Equal(17.5f, c));

        // The now-levelled pad accepts the brick.
        var place = h.PlaceBrick(4, 4, 2, 2);
        Assert.True(place.Success, place.Message);

        // Construction XP landed in construction, not a gather skill.
        h.Actions.FlushNotifications();
        Assert.Contains(h.Actions.Notifications, n => n.Text.Contains("levelled"));
        var snapshot = h.Skills.GetSnapshot();
        Assert.True(snapshot.Skills.TryGetValue("construction", out var construction), "construction skill snapshot missing");
        Assert.True(construction!.Xp > 0f, $"construction XP expected > 0, got {construction.Xp}");
    }

    // ── 4. Refusals (no silent loss, all notified) ───────────────────────────

    private static string? TryFlatten(Harness h, int x, int y)
    {
        Assert.True(h.Inventory.AddItem("shovel", 1));
        return h.Actions.StartAction(ActionType.Flattening, null, h.Skills, h.Inventory, tileXy: (x, y));
    }

    [Fact]
    public void Flatten_Refuses_NoShovel()
    {
        var h = new Harness();
        var err = h.Actions.StartAction(ActionType.Flattening, null, h.Skills, h.Inventory, tileXy: (4, 4));
        Assert.Equal("Needs a shovel.", err);
    }

    [Fact]
    public void Flatten_Refuses_WaterTile()
    {
        var h = new Harness();
        h.World.Tiles[4, 4].Elevation = Constants.SeaLevel - 2f; // water
        var err = TryFlatten(h, 4, 4);
        Assert.Equal("Can't level water.", err);
    }

    [Fact]
    public void Flatten_Refuses_OccupiedTile()
    {
        var h = new Harness();
        var place = h.PlaceBrick(4, 4, 2, 2);
        // RED note: once the brick exists the tile is occupied; flatten must refuse.
        Assert.True(place.Success, place.Message);
        var err = TryFlatten(h, 4, 4);
        Assert.Equal("Clear the structure first.", err);
    }

    [Fact]
    public void Flatten_Refuses_ResourceNodeTile()
    {
        var h = new Harness();
        h.World.Tiles[4, 4].ResourceNode = new ResourceNode("oak_tree", null!, 1f);
        var err = TryFlatten(h, 4, 4);
        Assert.Equal("Clear the resource node first.", err);
    }

    [Fact]
    public void Flatten_Refuses_SinkBelowSea()
    {
        var h = new Harness();
        // Land tile above a deep well: corner sources [16.5,4,4,4] — median
        // 4 would sink the tile underwater.
        h.World.Tiles[5, 4].Elevation = 4f;
        h.World.Tiles[5, 5].Elevation = 4f;
        h.World.Tiles[4, 5].Elevation = 4f;
        var err = TryFlatten(h, 4, 4);
        Assert.Equal("That would flood.", err);
        Assert.Equal(Constants.SeaLevel + 5f, h.World.GetTile(4, 4)!.Elevation); // untouched
    }

    // ── 5. XP routing wiring ────────────────────────────────────────────────

    [Fact]
    public void GetSkillId_Flattening_IsConstruction()
    {
        Assert.Equal("construction", ActionType.Flattening.GetSkillId());
    }

    // ── 6. Persistence: terrain edits survive save/reload ───────────────────

    private sealed class GameHarness
    {
        public Game Game = new();
        public TileMap Surface = MakeSurface();
        public Player Player = new(4.5f * Constants.TileSize, 4.5f * Constants.TileSize)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();

        private static TileMap MakeSurface(int size = 8)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            world.BuildCornerElevations();
            return world;
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public void SetupGame()
        {
            Game.World = Surface;
            Game.Player = Player;
            Game.BuildingSystem = Buildings;
            Game.SkillManager = Skills;
            Game.NPCSystem = new NPC.NPCSystem();
            Game.Crafting = new Crafting.CraftingSystem();
            Game.CombatSystem = new Combat.CombatSystem();
            var loader = new DataLoader();
            loader.LoadAll();
            Game.DataLoader = loader;
            Game.FoodRegistry = new FoodRegistry(loader.ItemsData);
        }
    }

    [Fact]
    public void TerrainEdit_AndBlock_SurviveSaveLoad()
    {
        var h = new GameHarness();
        h.SetupGame();

        // A flattened tile (edit recorded) with a brick on it.
        h.Surface.SetElevationOverride(1, 1, Constants.SeaLevel + 6f);
        h.Player.Inventory!.AddItem("dirt_brick", 10);
        var place = h.Buildings.PlaceStructure("mound_block", 2, 2, 1, 1,
            h.Surface, h.Player.Inventory, h.Skills);
        Assert.True(place.Success, place.Message);
        var edits = h.Surface.GetTerrainEdits();
        Assert.Single(edits);
        Assert.Equal((1, 1, Constants.SeaLevel + 6f), (edits[0].TileX, edits[0].TileY, edits[0].Elevation));

        // Save, then load into a fresh game whose surface is the pre-edit
        // flat pad (the seeded-regen analogue).
        var saveDir = Path.Combine(Path.GetTempPath(), $"dsr-vb-{Guid.NewGuid():N}");
        var save = new SaveSystem(saveDir);
        save.Save(h.Game, 0);

        var h2 = new GameHarness();
        h2.SetupGame();
        var data = save.Load(0);
        Assert.NotNull(data);
        save.LoadIntoGame(data!, h2.Game);

        // The edit lands after the world rebuild and re-stitches corners.
        Assert.Equal(Constants.SeaLevel + 6f, h2.Surface.GetTile(1, 1)!.Elevation);
        Assert.Contains(Constants.SeaLevel + 6f, h2.Surface.GetTile(1, 1)!.CornerElevations!);

        // The brick rides the structure snapshot — sub-cell and all.
        var restored = h2.Buildings.Structures
            .Single(s => s.StructureId == "mound_block" && s.TileX == 2 && s.TileY == 2);
        Assert.Equal(1, restored.SubX);
        Assert.Equal(1, restored.SubY);
        Assert.Equal(0, restored.Layer);
    }
}

