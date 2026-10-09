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
/// Fishing slice: rod gate + fishing skill (spec:
/// docs/superpowers/specs/2026-10-08-fishing-rod-skill-design.md).
/// Player-side tests are harness-free (ActionSystem shape); worker-side
/// tests use the WaterBucketTests harness shape.
/// </summary>
public class FishingTests
{
    // ─── Shared fixtures ─────────────────────────────────────────────────

    /// <summary>The real fish_spot def, exactly as resources.json loads it
    /// post-slice (rod-gated, level 1).</summary>
    private static ResourceDef FishSpotDef() => new()
    {
        Id = "fish_spot",
        Name = "Fish Spot",
        Biome = "coastal",
        Category = "special",
        YieldItem = "raw_fish",
        Yield = 1,
        Xp = 15f,
        DepletionCount = 2,
        Regrow = 60f,
        ToolRequirement = "fishing_rod",  // the gate under test
        RequiredLevel = 1,
        Seasons = [],
    };

    private static ResourceNode FishSpot() =>
        new("fish_spot", FishSpotDef(), 10f);

    private static void PumpSuccessRate(SkillManager sm, float value)
        => sm.GetSkill("fishing").SubStats["success_rate"] = value;

    // ─── 1. Data shape ──────────────────────────────────────────────────

    [Fact]
    public void FishingRodData_ItemAndRecipe_LoadFromJson()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var rod = items.FirstOrDefault(i => i.Id == "fishing_rod");
        Assert.NotNull(rod);
        Assert.Equal("fishing_rod", rod!.ToolType);
        Assert.True(rod.IsEquippable);
        Assert.True(rod.IsEssentialTool);
        Assert.Equal(100, rod.Durability);
        Assert.Equal("items/elder_wood_staff", rod.SpriteKey);

        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var recipe = recipes.GetRecipe("craft_fishing_rod");
        Assert.NotNull(recipe);
        Assert.Equal("fishing_rod", recipe!.OutputItem);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(2, recipe.RequiredLevel);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "planks" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "grass_rope" && i.Quantity == 1);
    }

    [Fact]
    public void FishSpotDef_RequiresRodGate()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        // Raw parsed row — the same rows Bootstrap feeds its ResourceDef
        // conversion (Bootstrap.cs:53-83), asserted directly.
        var row = loader.ResourcesData.FirstOrDefault(r =>
            r.TryGetValue("id", out var id) && id.ToString() == "fish_spot");
        Assert.NotNull(row);
        Assert.True(row!.TryGetValue("requires_tool", out var tool));
        // RED until the gate lands: today requires_tool is JSON null.
        Assert.Equal("fishing_rod", tool?.ToString());
        Assert.True(row.TryGetValue("required_level", out var level));
        // Level 5 would strand the only fishing XP source behind a
        // fishing-level gate nothing else can train. Entry-level, rod-gated.
        Assert.Equal("1", level?.ToString());
    }

    [Fact]
    public void FishingSkill_IsRegisteredInSkillManager()
    {
        var sm = new SkillManager();
        // Registration is what makes XP real: AddXpWithNotification
        // no-ops for unknown ids (SkillManager.cs:73).
        var messages = sm.AddXpWithNotification("fishing", 50f);
        Assert.Equal(50f, sm.GetSkill("fishing").Xp);
        Assert.Equal(1, sm.GetSkillLevel("fishing"));
    }

    // ─── 2–3. Player path ───────────────────────────────────────────────

    [Fact]
    public void PlayerFishing_RefusedWithoutRod()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();

        var error = system.StartAction(ActionType.Fishing, node, sm, inv);
        Assert.Equal("You need a fishing_rod.", error);
        Assert.False(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerFishing_SucceedsWithCarriedRod_TrainsFishingNotForaging()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f); // deterministic success roll
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1)); // carried, not equipped

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));
        Assert.True(system.Active.IsBusy);

        // The cast is timed (Constants.FishingCastSeconds): no catch mid-cast…
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }

        // …the tick that closes the window is the catch tick.
        var result = system.Update(0.25f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("raw_fish", result.ItemId);
        Assert.Equal(1, result.Quantity);
        // fish_spot xp_reward is 15 — and it must route to FISHING, not
        // foraging (the ActionTypeToSkillId landmine: CompleteAction nulls
        // Active.Resource, so only the enum survives to tell the skill).
        Assert.Equal(15f, result.Xp);

        // No repeated completion on later ticks.
        Assert.Null(system.Update(0.016f));

        // ProcessCompletion lands the XP in fishing — and nowhere else.
        system.ProcessCompletion(result, inv, sm);
        Assert.Equal(15f, sm.GetSkill("fishing").Xp);
        Assert.Equal(0f, sm.GetSkill("foraging").Xp);
        Assert.True(inv.Slots.Any(s => s?.ItemId == "raw_fish"),
            "raw_fish should be in the player inventory after a catch");
    }

    [Fact]
    public void PlayerFishing_MidCastIsBusyAndHoldsSilence()
    {
        var system = new ActionSystem();
        var node = FishSpot();
        var sm = new SkillManager();
        var inv = new Inv();
        Assert.True(inv.AddItem("fishing_rod", 1));

        Assert.Null(system.StartAction(ActionType.Fishing, node, sm, inv));

        // Mid-cast the player is busy: a second start is refused…
        Assert.Equal("Already performing an action.",
            system.StartAction(ActionType.Fishing, node, sm, inv));
        // …and partial-time Updates return null (no early catch, no leak).
        Assert.Null(system.Update(0.5f));
        Assert.Null(system.Update(1.0f));
        Assert.True(system.Active.IsBusy);
        Assert.Null(system.Update(1.4f));
        // Total elapsed 2.9s < 3.0s cast: still busy.
        Assert.True(system.Active.IsBusy);
    }

    [Fact]
    public void PlayerForaging_StillInstant_RegressionGuard()
    {
        var system = new ActionSystem();
        var sm = new SkillManager();
        sm.GetSkill("foraging").SubStats["success_rate"] = 1000f;
        var inv = new Inv();
        var berries = new ResourceDef
        {
            Id = "berry_bush",
            Name = "Berry Bush",
            YieldItem = "berries",
            Yield = 1,
            Xp = 5f,
            DepletionCount = 3,
            RequiredLevel = 1,
            Seasons = [],
        };
        var node = new ResourceNode("berry_bush", berries, 3f);

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        // Every gather EXCEPT fishing stays instant: the very first Update
        // completes it (no DurationRemaining was ever set).
        var result = system.Update(0.016f);
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("berries", result.ItemId);
    }

    // ─── 5–7. Worker path (WaterBucketTests harness shape) ──────────────

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

    private static ResourceNode FishSpotAt(TileMap world, int x, int y)
    {
        var node = new ResourceNode("fish_spot", FishSpotDef(), 10f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    [Fact]
    public void WorkerFishing_NoRod_NeverCarries()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(12);

        // No rod anywhere (player inv empty, colony store empty) → the
        // node is never claimed, nothing is ever carried.
        Assert.Equal(0, worker.CarriedQuantity);
        Assert.True(string.IsNullOrEmpty(worker.CarriedItemId));
    }

    [Fact]
    public void WorkerFishing_RodInColonyStore_HarvestsAndTrainsFishing()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        FishSpotAt(h.World, 6, 6);
        h.Colony.Store("fishing_rod", 1);
        var worker = h.AddWorker("fisher", 5, 6);

        h.Tick(40); // walk + harvest + haul home

        // Outcome assert: raw_fish landed in the colony stockpile.
        Assert.True(h.Colony.GetItemQuantity("raw_fish") > 0,
            $"expected raw_fish in store, got {h.Colony.GetItemQuantity("raw_fish")}");
        // The rod is a gate, not a consumable — it stays.
        Assert.True(h.Colony.GetItemQuantity("fishing_rod") > 0);
        // The worker trains FISHING (GatherSkillFor branch), not mining
        // (the old any-tool-node → mining trap) and not foraging.
        Assert.True(worker.Skills.GetSkill("fishing").Xp > 0f,
            $"expected fishing xp > 0, got {worker.Skills.GetSkill("fishing").Xp}");
        Assert.Equal(0f, worker.Skills.GetSkill("mining").Xp);
    }

    [Fact]
    public void WorkerPickaxeNodes_StillTrainMining()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        // Surface copper + colony pickaxe — the existing surface-tool
        // carve-out must keep working (regression guard for the
        // GatherSkillFor re-order).
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

        Assert.True(miner.Skills.GetSkill("mining").Xp > 0f,
            "surface pickaxe harvest still trains mining");
        Assert.True(h.Colony.GetItemQuantity("copper_ore") > 0);
    }
}
