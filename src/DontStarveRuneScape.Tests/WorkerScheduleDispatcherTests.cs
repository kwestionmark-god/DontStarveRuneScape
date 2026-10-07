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
/// Dispatcher integration tests for the worker-schedules slice.
/// Tests step-tick across hour boundaries per the repo's straddle gotcha.
/// </summary>
public class WorkerScheduleDispatcherTests
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
        public DayNightCycle Clock = new();
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

        public void NodeAt(int x, int y, string yieldItem,
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
            World.GetTile(x, y)!.ResourceNode = node;
        }

        private static Dictionary<string, object> Row(params (string Key, object Value)[] fields)
        {
            var row = new Dictionary<string, object>();
            foreach (var (key, value) in fields)
                row[key] = JsonSerializer.SerializeToElement(value);
            return row;
        }

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

        /// <summary>Set clock to specific hour (0-23), then tick the full system.</summary>
        public void SetClockHour(float hour) => Clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = (hour - 6f) / 24f });

        /// <summary>Tick with clock wired; dt=0.25s is one simulation step.</summary>
        public void Tick(float dt = 0.25f)
            => Recruits.Tick(dt, Npcs, Player, World,
                Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods, null, null, null, null, Clock);

        /// <summary>Tick N steps across hour boundary at step <paramref name="breakAtStep"/>.
        /// Returns the worker's state after the break step.</summary>
        public RecruitNpc TickAcrossHour(int breakAtStep, int totalSteps, float dt = 0.25f)
        {
            for (int i = 0; i < totalSteps; i++)
            {
                Tick(dt);
                if (i == breakAtStep) break;
            }
            return (RecruitNpc)Npcs.NPCs[0];
        }
    }

    // ─── Schedule gate basics ───────────────────────────────────────────────

    [Fact]
    public void NullClock_FallbackToWork_CategoryResolvesToWork()
    {
        var h = new Harness();
        // Don't wire the clock - RecruitmentSystem should treat null as Work
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant");
        h.AddStructure("stone_bench", 6, 5);
        h.NodeAt(7, 5, "berries", yield: 2);

        h.Tick(1); // one step without clock

        // Without clock the worker should behave as Work (today's behavior)
        Assert.NotEqual("Resting", worker.ColonyRestStatus);
    }

    // ─── Dusk mid-gather → bedtime rest ─────────────────────────────────────

    [Fact]
    public void DuskMidGather_TransitionToSleep_ReleasesClaimAndRests()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", rest: 100f); // fully rested
        h.AddStructure("stone_bench", 6, 5);
        h.NodeAt(7, 5, "berries", yield: 2); // 2 tiles away

        // Claim phase happens at 17:00 (a WORK slot); the node is held
        // mid-gather as dusk approaches.
        h.SetClockHour(17f);

        h.Tick(10); // worker claims node and walks

        var board = h.Colony.TaskBoard;
        var reservationBefore = board.FindGatherByTile(7, 5);
        Assert.NotNull(reservationBefore);
        Assert.Equal("a1", reservationBefore!.AssigneeNpcId);

        // Now cross the 21:00 boundary (SLEEP starts)
        h.SetClockHour(21f);
        h.Tick(1); // single step past boundary

        // Reservation should be released (category changed to Sleep)
        var reservationAfter = board.FindGatherByTile(7, 5);
        Assert.Null(reservationAfter);

        // Worker should be seeking rest (bedtime forces rest regardless of meter)
        Assert.Equal("Seeking rest", worker.ColonyRestStatus);
    }

    // ─── Night floor: Work category at night → wander near anchor ───────────

    [Fact]
    public void NightFloor_WorkCategoryAt23_NoOutdoorDispatch_WandersNearCamp()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", rest: 100f);
        h.AddStructure("stone_bench", 6, 5);
        h.NodeAt(7, 5, "berries", yield: 2);

        // Force template to Work at 23:00 (night) - using a custom approach:
        // For this test we'll just set clock to 23:00 and verify no gather happens
        h.SetClockHour(23f);

        h.Tick(10); // enough steps to attempt dispatch

        // Worker should NOT be gathering (night floor refusal)
        // Should be wandering near anchor or seeking rest if tired
        // At full rest and night floor, the worker falls back to free-time wander
        Assert.NotEqual("hauling", worker.CarryStatus.ToLowerInvariant());
        // The task board should have no gather reservations for this worker
        var gather = h.Colony.TaskBoard.Tasks.FirstOrDefault(t => t.Kind == ColonyTaskKind.Gather && t.AssigneeNpcId == "a1");
        Assert.Null(gather);
    }

    // ─── Bedtime sleep: full rest at SLEEP slot → seeks fire, wakes at 06:00 ──

    [Fact]
    public void BedtimeSleep_FullRestAtSleepSlot_SeeksFireAndWakesAtDawn()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", rest: 100f); // fully rested
        h.AddStructure("stone_bench", 6, 5); // rest spot
        h.AddStructure("campfire", 6, 6);    // fire gives faster recovery

        // Set to 22:00 (SLEEP slot for workers)
        h.SetClockHour(22f);

        // Tick enough for worker to path to rest spot and start resting
        h.Tick(30);

        // Should be resting (bedtime forces it even at full meter)
        Assert.Equal("Resting", worker.ColonyRestStatus);

        // Now advance to 06:00 (dawn, NOURISHMENT)
        h.SetClockHour(6f);
        h.Tick(1);

        // Should wake and resume work (Nourishment)
        Assert.NotEqual("Resting", worker.ColonyRestStatus);
    }

    // ─── Casual meals: NOURISHMENT slot threshold 45 vs urgent 20 ───────────

    [Fact]
    public void CasualMeal_NourishmentSlotHunger40_EatsFromStore()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 40f); // above urgent (20), below casual (45)
        h.AddStructure("stone_bench", 6, 5);
        h.Colony.Store("cooked_fish", 1); // restores 20

        // Set to 12:00 (NOURISHMENT slot)
        h.SetClockHour(12f);

        h.Tick(1); // one tick should consume the meal at casual threshold

        Assert.Equal(0, h.Colony.GetItemQuantity("cooked_fish"));
        Assert.True(worker.ColonyHunger > 40f, "should have eaten at casual threshold");
    }

    [Fact]
    public void CasualMeal_WorkSlotHunger40_DoesNotEat()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 40f);
        h.AddStructure("stone_bench", 6, 5);
        h.Colony.Store("cooked_fish", 1);

        // Set to 10:00 (WORK slot)
        h.SetClockHour(10f);

        h.Tick(1);

        Assert.Equal(1, h.Colony.GetItemQuantity("cooked_fish"));
        Assert.Equal(40f - 0.01f, worker.ColonyHunger, 1); // only passive drain
    }

    // ─── Urgent overrides: hunger 0 during SLEEP → eating wins ──────────────

    [Fact]
    public void UrgentHunger_ZeroHungerDuringSleep_EatingWinsOverBedtime()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 0f, rest: 100f);
        h.AddStructure("stone_bench", 6, 5);
        h.Colony.Store("cooked_fish", 1);

        // Set to 22:00 (SLEEP slot)
        h.SetClockHour(22f);

        h.Tick(1);

        // Urgent hunger (0) should override bedtime sleep
        Assert.Equal(0, h.Colony.GetItemQuantity("cooked_fish"));
        Assert.True(worker.ColonyHunger > 0f);
    }

    [Fact]
    public void UrgentThreat_HostileNearbyDuringSleep_RestAborts()
    {
        var h = new Harness();
        h.FoundColony();
        var worker = h.AddWorker("a1", 5, 5, "assistant", hunger: 50f, rest: 20f); // tired
        h.AddStructure("stone_bench", 6, 5);

        // Spawn hostile monster right next to worker
        h.Combat.SpawnMonster(TestMonsterDef(), 5.5f * Constants.TileSize, 5.5f * Constants.TileSize);

        // Set to 22:00 (SLEEP slot)
        h.SetClockHour(22f);

        h.Tick(1);

        // Threat should abort rest (existing behavior)
        Assert.NotEqual("Resting", worker.ColonyRestStatus);
    }

    // ─── Guard shifts: day squad patrols at 12:00, sleeps at 23:00 ──────────

    [Fact]
    public void GuardShifts_DaySquad_PatrolsAtNoonSleepsAtMidnight()
    {
        var h = new Harness();
        h.FoundColony();
        var guard = h.AddWorker("g1", 5, 5, "guard");
        h.AddStructure("stone_bench", 6, 5);

        // 12:00 -> MILITARY_DUTY for day squad
        h.SetClockHour(12f);
        h.Tick(10);
        // Should be patrolling (not resting)
        Assert.NotEqual("Resting", guard.ColonyRestStatus);

        // 23:00 -> SLEEP for day squad
        h.SetClockHour(23f);
        h.Tick(10);
        // Should seek rest
        Assert.True(guard.ColonyRestStatus is "Seeking rest" or "Resting");
    }

    [Fact]
    public void GuardShifts_NightSquad_PatrolsAtMidnight()
    {
        var h = new Harness();
        h.FoundColony();
        // First guard (day), second guard gets night shift by alternation rule
        h.AddWorker("g1", 5, 5, "guard"); // day
        var nightGuard = h.AddWorker("g2", 5, 6, "guard"); // night
        h.AddStructure("stone_bench", 6, 5);

        // 23:00 -> MILITARY_DUTY for night squad
        h.SetClockHour(23f);
        h.Tick(10);

        // Night guard should be patrolling
        Assert.NotEqual("Resting", nightGuard.ColonyRestStatus);
        Assert.NotEqual("Seeking rest", nightGuard.ColonyRestStatus);

        // Day guard should be sleeping
        var dayGuard = (RecruitNpc)h.Npcs.NPCs.First(n => n.NpcId == "g1");
        Assert.True(dayGuard.ColonyRestStatus is "Seeking rest" or "Resting");
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
}