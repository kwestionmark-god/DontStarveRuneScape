namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Water-gathering bucket-mechanics slice (spec:
/// docs/superpowers/specs/2026-10-08-water-gathering-bucket-mechanics-design.md).
/// Player-side tests are harness-free (ActionSystem shape); worker-side
/// tests use the ColonyWorkerBrainTests harness shape.
/// </summary>
public class WaterBucketTests
{
    // ─── Shared fixtures ─────────────────────────────────────────────────

    /// <summary>The real water_source def, exactly as resources.json loads it.</summary>
    private static ResourceDef WaterSourceDef() => new()
    {
        Id = "water_source",
        Name = "Water Source",
        Biome = "plains",
        Category = "water",
        YieldItem = "water",
        Yield = 1,
        Xp = 0f,
        DepletionCount = -1,          // infinite
        Regrow = 0f,
        ToolRequirement = "bucket",    // the gate under test
        RequiredLevel = 1,
        Seasons = [],
    };

    private static ResourceNode WaterNode() =>
        new("water_source", WaterSourceDef(), 1f);

    private static void PumpSuccessRate(SkillManager sm, float value)
        => sm.GetSkill("foraging").SubStats["success_rate"] = value;

    // ─── 1. Data shape ──────────────────────────────────────────────────

    [Fact]
    public void BucketData_ItemAndRecipe_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var bucket = items.FirstOrDefault(i => i.Id == "bucket");
        Assert.NotNull(bucket);
        Assert.Equal("bucket", bucket!.ToolType);
        Assert.True(bucket.IsEquippable);
        Assert.True(bucket.IsEssentialTool);
        Assert.Equal(100, bucket.Durability);

        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var recipe = recipes.GetRecipe("wood_bucket");
        Assert.NotNull(recipe);
        Assert.Equal("bucket", recipe!.OutputItem);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(2, recipe.RequiredLevel);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "planks" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "grass_rope" && i.Quantity == 1);
    }

    [Fact]
    public void WaterSourceDef_RequiresBucketGate()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        // Raw parsed row — the same rows Bootstrap feeds its ResourceDef
        // conversion (Bootstrap.cs:53-83), asserted directly.
        var row = loader.ResourcesData.FirstOrDefault(r =>
            r.TryGetValue("id", out var id) && id.ToString() == "water_source");
        Assert.NotNull(row);
        Assert.True(row!.TryGetValue("requires_tool", out var tool));
        // RED until the gate lands: today requires_tool is JSON null.
        Assert.Equal("bucket", tool?.ToString());
    }

    // ─── 2–3. Player path ───────────────────────────────────────────────

    [Fact]
    public void PlayerWaterHarvest_RefusedWithoutBucket()
    {
        var system = new ActionSystem();
        var node = WaterNode();
        var sm = new SkillManager();
        var inv = new Inv();

        var error = system.StartAction(ActionType.Foraging, node, sm, inv);
        Assert.Equal("You need a bucket.", error);
        Assert.False(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerWaterHarvest_SucceedsWithCarriedBucket()
    {
        var system = new ActionSystem();
        var node = WaterNode();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f); // deterministic success roll
        var inv = new Inv();
        Assert.True(inv.AddItem("bucket", 1)); // carried, not equipped

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        Assert.True(system.Active.IsBusy);

        var result = system.Update(0.016f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("water", result.ItemId);
        Assert.Equal(1, result.Quantity);
        // water_source xp_reward is 0.0 — a bucket run is a chore, not grind
        Assert.Equal(0f, result.Xp);

        // No repeated completion on later ticks.
        Assert.Null(system.Update(0.016f));
    }

    // ─── 4–6. Worker path (ColonySkillGateTests harness shape) ──────────

    private sealed class Harness
    {
        public TileMap World = MakeWorld();
        public Player Player = new(3.5f * Constants.TileSize, 3.5f * Constants.TileSize)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        public NPCSystem Npcs = new();
        public ColonySystem Colony = new();
        public BuildingSystem Buildings = MakeBuildings();
        public CraftingSystem Crafting = MakeCrafting();
        public SkillManager Skills = new();
        public FoodRegistry Foods = MakeFoods();
        public RecruitmentSystem Recruits = new();

        private static TileMap MakeWorld(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            return world;
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        private static CraftingSystem MakeCrafting()
        {
            var recipes = new RecipeRegistry();
            recipes.LoadAll();
            return new CraftingSystem { Registry = recipes };
        }

        private static FoodRegistry MakeFoods()
        {
            var loader = new DataLoader();
            loader.LoadAll();
            return new FoodRegistry(loader.ItemsData);
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY)
        {
            var npc = new RecruitNpc
            {
                NpcId = id,
                Name = id,
                IsRecruited = true,
                RecruitBehavior = "assistant",
                WorldX = (tileX + 0.5f) * Constants.TileSize,
                WorldY = (tileY + 0.5f) * Constants.TileSize,
                Health = 100,
                MaxHealth = 100,
                IsActive = true,
                ColonyHunger = 100f,
                ColonyRest = 100f,
            };
            Npcs.NPCs.Add(npc);
            Recruits.OnRecruit(id, "assistant");
            return npc;
        }

        public bool FoundColony(int x = 5, int y = 5)
            => Colony.FoundAt((x + 0.5f) * Constants.TileSize,
                (y + 0.5f) * Constants.TileSize, World);

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony,
                    Buildings, Crafting, Skills, null, Foods, null, null, null, null, null);
        }
    }

    private static ResourceNode WaterSourceAt(TileMap world, int x, int y)
    {
        var node = new ResourceNode("water_source", WaterSourceDef(), 1f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    [Fact]
    public void WorkerWater_NoBucket_NeverCarries()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        WaterSourceAt(h.World, 6, 6);
        var worker = h.AddWorker("hauler", 5, 6);

        h.Tick(12);

        // No bucket anywhere (player inv empty, colony store empty) → the
        // node is never claimed, nothing is ever carried.
        Assert.Equal(0, worker.CarriedQuantity);
        Assert.True(string.IsNullOrEmpty(worker.CarriedItemId));
    }

    [Fact]
    public void WorkerWater_BucketInColonyStore_HarvestsAndDeposits()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        WaterSourceAt(h.World, 6, 6);
        h.Colony.Store("bucket", 1);
        var worker = h.AddWorker("hauler", 5, 6);

        h.Tick(40); // walk + harvest + haul home

        // Outcome assert: water landed in the colony stockpile.
        Assert.True(h.Colony.GetItemQuantity("water") > 0,
            $"expected water in store, got {h.Colony.GetItemQuantity("water")}");
        // The bucket is a gate, not a consumable — it stays.
        Assert.True(h.Colony.GetItemQuantity("bucket") > 0);
    }

    [Fact]
    public void WorkerSurfaceMine_BucketCarveOutKeepsPickaxeWorking()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        // Surface copper + colony pickaxe — the existing surface-tool
        // carve-out must keep working (regression guard for the change).
        var copper = new ResourceDef
        {
            Id = "copper_rock",
            Name = "Copper Rock",
            YieldItem = "copper_ore",
            Yield = 1,
            RequiredLevel = 1,
            ToolRequirement = "pickaxe",
            Xp = 35f,
            DepletionCount = 5,
            Seasons = [],
        };
        h.World.GetTile(6, 6)!.ResourceNode =
            new ResourceNode("copper_rock", copper, 10f);
        h.Colony.Store("pickaxe", 1);
        var miner = h.AddWorker("miner", 6, 7);

        h.Tick(40);

        Assert.True(miner.Skills.GetSkillLevel("mining") >= 1,
            "surface pickaxe harvest still trains mining");
        Assert.True(h.Colony.GetItemQuantity("copper_ore") > 0);
    }
}
