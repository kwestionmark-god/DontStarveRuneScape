namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using System.Text.Json;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// The recruited-worker brain from slice D of the colony fusion: gathering
/// into player storage or the settlement stockpile, hunger and rest needs,
/// guard patrol/intercept, workplace production with FIFO orders,
/// construction sites, garden plots, and faction-priced trade.
/// </summary>
public class ColonyWorkerBrainTests
{
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
        public CombatSystem Combat = new();
        public FoodRegistry Foods = MakeFoods();

        public RecruitmentSystem Recruits = new();
        public StructureDefRegistry StructureDefs { get; } = MakeStructureDefs();

        private static TileMap MakeWorld(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            return world;
        }

        private static StructureDefRegistry MakeStructureDefs()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return registry;
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

        public Structure AddStructure(string id, int x, int y)
        {
            var def = StructureDefs.GetStructure(id);
            Assert.NotNull(def);
            var structure = new Structure
            {
                StructureId = id,
                StructureDef = def!,
                TileX = x,
                TileY = y,
                WorldX = (x + 0.5f) * Constants.TileSize,
                WorldY = (y + 0.5f) * Constants.TileSize,
                IsActive = true,
            };
            Buildings.Structures.Add(structure);
            if (def!.OccupiesTile)
                World.GetTile(x, y)!.Structure = def;
            return structure;
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY, string behavior,
            float hunger = 100f, float rest = 100f)
        {
            var npc = new RecruitNpc
            {
                NpcId = id,
                Name = id,
                IsRecruited = true,
                RecruitBehavior = behavior,
                WorldX = (tileX + 0.5f) * Constants.TileSize,
                WorldY = (tileY + 0.5f) * Constants.TileSize,
                Health = 100,
                MaxHealth = 100,
                IsActive = true,
                ColonyHunger = hunger,
                ColonyRest = rest,
            };
            Npcs.NPCs.Add(npc);
            return npc;
        }

        public bool FoundColony(int x = 5, int y = 5)
            => Colony.FoundAt((x + 0.5f) * Constants.TileSize, (y + 0.5f) * Constants.TileSize, World);

        public void Tick(int count, float dt = 0.25f)
        {
            for (int i = 0; i < count; i++)
                Recruits.Tick(dt, Npcs, Player, World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods);
        }
    }

    private static ResourceNode NodeAt(TileMap world, int x, int y, string yieldItem,
        int yield = 2, string[]? seasons = null)
    {
        var node = new ResourceNode("test_" + yieldItem + $"_{x}_{y}", new ResourceDef
        {
            Id = "test_" + yieldItem,
            Name = yieldItem,
            YieldItem = yieldItem,
            Yield = yield,
            Xp = 1f,
            DepletionCount = 1,
            Seasons = seasons ?? [],
        }, 1f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    private static MonsterDef TestMonsterDef(int hp = 10) => new()
    {
        MonsterId = "test_wolf",
        Name = "Test Wolf",
        Hp = hp,
        Attack = 3,
        Defence = 2,
        Speed = 40f,
        AggressionRange = 150f,
        FleeRange = 300f,
        AttackCooldown = 0.5f,
        XpReward = 7,
        IsHostile = true,
        SpriteKey = "monster/wolf",
        LootTable = [new MonsterLootEntry { ItemId = "test_pelt", Chance = 1f }],
    };

    private static Dictionary<string, object> Row(params (string Key, object Value)[] fields)
    {
        var row = new Dictionary<string, object>();
        foreach (var (key, value) in fields)
            row[key] = JsonSerializer.SerializeToElement(value);
        return row;
    }

    // Station work only accepts recipes that declare requires_structure or
    // requires_campfire — plain recipes like "sticks" never qualify.
    private static void AddCarveRecipe(CraftingSystem crafting)
    {
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "carve"), ("output_item", "carved_staff"),
            ("input_items", new object[] { new object[] { "stick", 2 } }),
            ("required_skill", "crafting"), ("required_level", 1),
            ("requires_structure", "crafting_station"))]);
        foreach (var recipe in recipes.Values)
            crafting.Registry!.Recipes[recipe.RecipeId] = recipe;
    }

    // ─── Gathering ────────────────────────────────────────────────────────

    [Fact]
    public void Assistant_GathersIntoPlayerInventory_WithoutAColony()
    {
        var h = new Harness();
        var worker = h.AddWorker("a1", 5, 5, "assistant");
        NodeAt(h.World, 7, 5, "berries", yield: 2);

        h.Tick(80);

        Assert.Equal(2, h.Player.Inventory!.GetItemQuantity("berries"));
    }

    [Fact]
    public void Assistant_GathersIntoStockpile_WhenColonyFounded()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 6, "assistant");
        NodeAt(h.World, 7, 5, "berries", yield: 2);

        h.Tick(80);

        Assert.Equal(2, h.Colony.GetItemQuantity("berries"));
        Assert.Equal(0, h.Player.Inventory!.GetItemQuantity("berries"));
    }

    [Fact]
    public void TwoAssistants_Share_NoNodeIsDoubleHarvested()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 6, "assistant");
        h.AddWorker("a2", 5, 4, "assistant");
        NodeAt(h.World, 6, 5, "berries", yield: 2);

        h.Tick(80);

        // The single node yields exactly once — claimed targets prevent a
        // second worker from harvesting the same tile in the same pass.
        Assert.Equal(2, h.Colony.GetItemQuantity("berries"));
        Assert.Equal(0, h.Player.Inventory!.GetItemQuantity("berries"));
    }

    [Fact]
    public void SeasonGatedNodes_AreSkippedOutOfSeason()
    {
        var h = new Harness();
        h.World.SeasonSystem = new SeasonSystem(); // spring
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 5, "assistant");
        NodeAt(h.World, 6, 5, "berries", yield: 2, seasons: ["winter"]);

        h.Tick(80);

        Assert.Equal(0, h.Colony.GetItemQuantity("berries"));
    }

    [Fact]
    public void StarvingAssistant_ForagesOnlyFoodNodes_AndEatsWhatItGathers()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 0f);
        NodeAt(h.World, 6, 5, "stone", yield: 2);      // nearer, inedible
        NodeAt(h.World, 8, 5, "berries", yield: 2);    // farther, edible

        // Step to the moment the foraged berries are eaten: until then the
        // starving worker must ignore the nearer stone node entirely. Past
        // that moment the fed worker may gather stone like anyone else.
        int ticks = 0;
        while (worker.ColonyHunger <= 0f && ticks < 80)
        {
            h.Tick(1);
            ticks++;
        }

        Assert.True(worker.ColonyHunger > 0f,
            $"starving worker should eat the berries it foraged, hunger was {worker.ColonyHunger}");
        Assert.Equal(1, h.Colony.GetItemQuantity("berries")); // one serving eaten on rising
        Assert.Equal(0, h.Colony.GetItemQuantity("stone"));   // stone untouched while starving
    }

    // ─── Hunger ───────────────────────────────────────────────────────────

    [Fact]
    public void HungryResidents_EatBestStockpileFood_CookedFirst()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 20f);
        h.Colony.Store("berries", 1);       // raw, restores 8
        h.Colony.Store("cooked_fish", 1);   // cooked, restores 20

        h.Tick(1);

        // Cooked before raw, largest restoration first: the fish is eaten,
        // the berries stay for later. (One tick's 0.01 hunger drain applies.)
        Assert.Equal(0, h.Colony.GetItemQuantity("cooked_fish"));
        Assert.Equal(1, h.Colony.GetItemQuantity("berries"));
        Assert.InRange(worker.ColonyHunger, 39f, 40f);
        Assert.Equal("Fed", worker.ColonyNeedStatus);
    }

    [Fact]
    public void HungerDrains_WhileColonyIsFounded()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 50f);

        h.Tick(10);

        Assert.True(worker.ColonyHunger < 50f, $"expected drain, got {worker.ColonyHunger}");
    }

    // ─── Rest ─────────────────────────────────────────────────────────────

    [Fact]
    public void TiredWorkers_SeekBench_AndPauseWork()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var worker = h.AddWorker("a1", 6, 6, "assistant", rest: 50f);
        h.AddStructure("stone_bench", 6, 5); // adjacent tile (6,6) is the approach
        NodeAt(h.World, 8, 6, "berries", yield: 2);

        h.Tick(20);

        Assert.True(worker.ColonyRest > 50f, $"expected recovery at the bench, got {worker.ColonyRest}");
        Assert.Equal("Resting", worker.ColonyRestStatus);
        Assert.Equal(0, h.Colony.GetItemQuantity("berries")); // work paused while resting
    }

    [Fact]
    public void Threats_KeepTiredGuards_OnDuty()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var guard = h.AddWorker("g1", 5, 6, "guard", rest: 10f);
        h.Combat.SpawnMonster(TestMonsterDef(), 6.5f * Constants.TileSize, 6.5f * Constants.TileSize);

        // Observe at the kill moment: afterwards the exhausted guard is
        // free to rest, so a fixed tick count may straddle into "Resting".
        int ticks = 0;
        while (h.Combat.Monsters.Count > 0 && ticks < 120)
        {
            h.Tick(1);
            ticks++;
        }

        Assert.Empty(h.Combat.Monsters); // guard killed the wolf
        Assert.NotEqual("Resting", guard.ColonyRestStatus); // kept on duty by the threat
        Assert.True(h.Skills.GetSkill("attack").Xp > 0);
    }

    // ─── Guards ───────────────────────────────────────────────────────────

    [Fact]
    public void Guard_PatrolsAroundHome_WithNoThreat()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var guard = h.AddWorker("g1", 5, 5, "guard");
        float startX = guard.WorldX, startY = guard.WorldY;

        h.Tick(120); // 30s: several patrol point switches

        var moved = MathF.Sqrt(MathF.Pow(guard.WorldX - startX, 2) + MathF.Pow(guard.WorldY - startY, 2));
        Assert.True(moved > 8f, $"expected patrol movement, moved {moved}px");
    }

    [Fact]
    public void Guard_InterceptsAndKillsThreat_NearColony()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        var guard = h.AddWorker("g1", 5, 5, "guard");
        h.Combat.SpawnMonster(TestMonsterDef(), 7.5f * Constants.TileSize, 5.5f * Constants.TileSize);

        h.Tick(60); // two tiles away, inside the 6-tile defense radius

        Assert.Empty(h.Combat.Monsters);
        Assert.Equal(1, h.Player.Inventory!.GetItemQuantity("test_pelt"));
        Assert.True(h.Skills.GetSkill("attack").Xp > 0);
    }

    // ─── Workplaces and FIFO orders ────────────────────────────────────────

    [Fact]
    public void StaffedStation_ProducesFromStockpile()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 5, "assistant");
        AddCarveRecipe(h.Crafting);
        var station = h.AddStructure("crafting_station", 6, 5);
        station.WorkRecipeId = "carve"; // carve: 2 stick -> 1 carved_staff
        h.Colony.Store("stick", 4);     // exactly two production cycles

        h.Tick(120); // walk + two 5s production cycles

        Assert.Equal(2, h.Colony.GetItemQuantity("carved_staff"));
        Assert.Equal(0, h.Colony.GetItemQuantity("stick"));
        // The stockpile drained, so the station parks waiting for materials.
        Assert.Equal("Waiting for materials", station.WorkStatus);
    }

    [Fact]
    public void ManualFifoQueue_AdvancesThenPauses()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 5, "assistant");
        AddCarveRecipe(h.Crafting);
        var station = h.AddStructure("crafting_station", 6, 5);
        station.WorkRecipeId = "carve";
        station.WorkRecipeQueue.Add("carve");
        station.HasManualWorkOrder = true;
        h.Colony.Store("stick", 4); // exactly two queued cycles

        h.Tick(240); // walk + two 5s production cycles

        Assert.Equal(2, h.Colony.GetItemQuantity("carved_staff"));
        Assert.Equal(0, h.Colony.GetItemQuantity("stick"));
        Assert.Null(station.WorkRecipeId);
        Assert.True(station.WorkOrdersPaused);
        Assert.Equal("Orders complete", station.WorkStatus);
    }

    // ─── Construction and farming ─────────────────────────────────────────

    [Fact]
    public void ConstructionSite_ChargesBillOnce_AndCompletesWithXp()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 5, "assistant");
        var site = h.AddStructure("campfire", 6, 5);
        site.IsUnderConstruction = true;
        site.ConstructionMaterialsPaid = false;
        h.Colony.Store("stick", 2);
        h.Colony.Store("stone", 2);

        h.Tick(120); // claim + charge + walk + 20s build

        Assert.False(site.IsUnderConstruction);
        Assert.Equal("Construction complete", site.WorkStatus);
        Assert.Equal(0, h.Colony.GetItemQuantity("stick")); // charged exactly once
        Assert.Equal(0, h.Colony.GetItemQuantity("stone"));
        Assert.True(h.Skills.GetSkill("construction").Xp > 0);
    }

    [Fact]
    public void GardenPlot_SowsGrowsAndHarvestsWheat()
    {
        var h = new Harness();
        Assert.True(h.FoundColony());
        h.AddWorker("a1", 5, 5, "assistant");
        var plot = h.AddStructure("garden_plot", 6, 5);
        h.Colony.Store("wheat", 1); // the seed

        // Break at the harvest moment: past it, the plot re-sows and spends
        // another seed, so a fixed tick count can straddle the cycle.
        int ticks = 0;
        while (ticks < 220 && plot.WorkStatus != "Harvested wheat")
        {
            h.Tick(1);
            ticks++;
        }

        Assert.Equal("Harvested wheat", plot.WorkStatus);
        Assert.Equal(3, h.Colony.GetItemQuantity("wheat")); // seed spent, 3 harvested
    }

    // ─── Re-recruitment home ──────────────────────────────────────────────

    [Fact]
    public void OnRecruit_RehomesWorker_AtCurrentPosition()
    {
        var h = new Harness();
        var worker = h.AddWorker("a1", 5, 5, "assistant");
        NodeAt(h.World, 5, 6, "berries", yield: 2);
        h.Tick(1); // pins the home at (5,5)

        // Worker relocates far from the old home; a node near the new
        // position is outside the stale home's work radius...
        worker.WorldX = 15.5f * Constants.TileSize;
        worker.WorldY = 15.5f * Constants.TileSize;
        NodeAt(h.World, 16, 15, "berries", yield: 2);
        h.Tick(40);
        Assert.Equal(0, h.Player.Inventory!.GetItemQuantity("berries"));

        // ...until re-recruitment clears the saved home.
        h.Recruits.OnRecruit("a1", "assistant");
        h.Tick(80);
        Assert.Equal(2, h.Player.Inventory!.GetItemQuantity("berries"));
    }

    // ─── Faction-priced trade ─────────────────────────────────────────────

    private static TradeSystem MakeTrade(int standingPercent)
    {
        var registry = new TradeItemRegistry();
        registry.TradeItems["berries_trade"] = new TradeItemDef
        {
            Id = "berries_trade",
            ItemId = "berries",
            Biome = "forest",
            BuyPrice = 100,
            SellPrice = 60,
        };
        var factions = new FactionSystem();
        factions.RestoreSnapshot(new FactionSnapshot
        {
            Factions = [new FactionDataSnapshot { FactionId = "forest_villagers", Standing = standingPercent }],
        }, new DataLoader());
        return new TradeSystem { Registry = registry, Factions = factions };
    }

    private static MerchantNpc Merchant() => new()
    {
        NpcId = "m1",
        Name = "Merchant",
        Biome = "forest",
        FactionId = "forest_villagers",
        PriceModifier = 1f,
        StartingGold = 1000,
    };

    [Fact]
    public void TradePrices_FollowFactionStanding()
    {
        var def = MakeTrade(20).Registry!.TradeItems["berries_trade"];
        Assert.NotNull(def);

        // Low standing (20%): buy costs more, sell pays less.
        var unfriendly = MakeTrade(20);
        Assert.Equal(115, unfriendly.BuyPriceFor(def!, Merchant()));
        Assert.Equal(51, unfriendly.SellPriceFor("berries", Merchant()));

        // High standing (80%): the reverse.
        var friendly = MakeTrade(80);
        Assert.Equal(85, friendly.BuyPriceFor(def!, Merchant()));
        Assert.Equal(69, friendly.SellPriceFor("berries", Merchant()));

        // No faction link: base prices regardless of diplomacy.
        var neutral = MakeTrade(80);
        var loner = Merchant();
        loner.FactionId = "";
        Assert.Equal(100, neutral.BuyPriceFor(def!, loner));
        Assert.Equal(60, neutral.SellPriceFor("berries", loner));
    }
}
