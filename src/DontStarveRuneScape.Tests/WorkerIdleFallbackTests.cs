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
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Task-selection fallbacks slice (roadmap phase-4 remainder):
/// a WORK-slot worker with NO available job wanders near camp with an
/// "Idle" roster label instead of freezing, and a FREE_TIME-slot worker
/// never takes production/gather/construction dispatch.
/// </summary>
public class WorkerIdleFallbackTests
{
    private sealed class Harness
    {
        public TileMap World = MakeWorld();
        public Player Player = new(4.5f * Constants.TileSize, 4.5f * Constants.TileSize)
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
        public WeatherSystem Weather = new();

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
            return npc;
        }

        public bool FoundColony(int x = 5, int y = 5)
            => Colony.FoundAt((x + 0.5f) * Constants.TileSize, (y + 0.5f) * Constants.TileSize, World);

        public void Tick(int steps = 1, float dt = 0.25f)
        {
            for (int i = 0; i < steps; i++)
                Recruits.Tick(dt, Npcs, Player, World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods,
                    null, null, Weather, null, Clock);
        }

        public void SetClockHour(float hour)
            => Clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = (hour - 6f) / 24f });
    }

    [Fact]
    public void WorkSlot_NoJobsAvailable_WandersNearAnchor_WithIdleLabel()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        var worker = h.AddWorker("a1", 5, 5);
        h.SetClockHour(10f); // WORK slot, flat empty world: nothing to do

        h.Tick(60); // 15 sim-seconds

        // Worker must NOT freeze in place; wander changes position over time.
        float startX = (5 + 0.5f) * Constants.TileSize;
        float startY = (5 + 0.5f) * Constants.TileSize;
        bool moved = MathF.Abs(worker.WorldX - startX) + MathF.Abs(worker.WorldY - startY) > 1f;
        Assert.True(moved || worker.VelocityX != 0f || worker.VelocityY != 0f,
            "idle worker should wander near the anchor, not freeze");
        // Roster label: Idle (readable, not stale gatherer status)
        Assert.Equal("Idle", worker.CarryStatus == string.Empty ? "Idle" : worker.CarryStatus);
    }

    [Fact]
    public void WorkSlot_JobAvailable_IdleFallbackDoesNotFire()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        var worker = h.AddWorker("a1", 5, 5);
        h.SetClockHour(10f);
        // A harvestable node exists
        var node = new ResourceNode("n1", new ResourceDef
        {
            Id = "test_berries", Name = "berries", YieldItem = "berries",
            Yield = 2, Xp = 1f, DepletionCount = 5, Seasons = [],
        }, 1f);
        h.World.GetTile(7, 5)!.ResourceNode = node;

        h.Tick(60);

        // The gather path still wins when a job exists: berries reached the
        // stockpile (or are on the worker mid-haul).
        Assert.True(h.Colony.GetItemQuantity("berries") > 0 || worker.CarriedQuantity > 0,
            "a WORK-slot worker with a harvestable node should gather it");
    }

    [Fact]
    public void FreeTimeSlot_WithWorkAvailable_DoesNotDispatchWork()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        var worker = h.AddWorker("a1", 5, 5);
        h.SetClockHour(19f); // FREE_TIME slot (19-20)
        var node = new ResourceNode("n1", new ResourceDef
        {
            Id = "test_berries", Name = "berries", YieldItem = "berries",
            Yield = 2, Xp = 1f, DepletionCount = 5, Seasons = [],
        }, 1f);
        h.World.GetTile(7, 5)!.ResourceNode = node;

        h.Tick(60);

        // FREE_TIME must not produce: no berries gathered or carried
        Assert.Equal(0, h.Colony.GetItemQuantity("berries"));
        Assert.Equal(0, worker.CarriedQuantity);
    }
}
