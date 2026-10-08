namespace DontStarveRuneScape.Tests;

using System.Text.Json;
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
/// Phase-5, Cycle 2: danger feedback for working residents. When a hostile
/// monster lurks near a claimed workplace, the worker pauses the job
/// (WorkStatus "Danger nearby — work paused"), retreats toward the anchor,
/// and resumes work once the threat clears. Guards are unaffected — they
/// engage threats; workers get out of the way (the MC pattern).
/// </summary>
public class DangerFeedbackTests
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

        public void SetClockHour(float hour)
            => Clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = (hour - 6f) / 24f });

        public void Tick(int steps = 1, float dt = 0.25f)
        {
            for (int i = 0; i < steps; i++)
                Recruits.Tick(dt, Npcs, Player, World,
                    Colony.IsFounded ? Colony : null, Buildings, Crafting, Skills, Combat, Foods,
                    null, null, null, null, Clock);
        }
    }

    // ─── Tests ───────────────────────────────────────────────────────────

    [Fact]
    public void HostileNearWorkplace_WorkPauses_ResumeWhenClear()
    {
        var h = new Harness();
        h.FoundColony();
        h.SetClockHour(9f); // a WORK slot, daytime
        // Design law: the player can build the stations the colony runs.
        h.Skills.AddXpWithNotification("construction", SkillManager.XpForLevel(5) + 1f);
        var worker = h.AddWorker("a1", 6, 5, "assistant");
        var bench = h.AddStructure("crafting_station", 7, 5);
        bench.WorkRecipeId = "carve";
        h.Colony.Store("stick", 10);

        // Add the carve recipe (crafting_station structure, stick inputs)
        var row = new Dictionary<string, object>
        {
            ["recipe_id"] = JsonSerializer.SerializeToElement("carve"),
            ["output_item"] = JsonSerializer.SerializeToElement("carved_staff"),
            ["input_items"] = JsonSerializer.SerializeToElement(
                new object[] { new object[] { "stick", 2 } }),
            ["required_skill"] = JsonSerializer.SerializeToElement("crafting"),
            ["required_level"] = JsonSerializer.SerializeToElement(1),
            ["requires_structure"] = JsonSerializer.SerializeToElement("crafting_station"),
        };
        var recipes = RecipeRegistry.FromData([row]);
        foreach (var recipe in recipes.Values)
            h.Crafting.Registry!.Recipes[recipe.RecipeId] = recipe;

        // Worker reaches the bench and starts working
        bool working = false;
        for (int i = 0; i < 20 && !working; i++)
        {
            h.Tick(1);
            working = bench.WorkStatus.StartsWith("Working", StringComparison.Ordinal);
        }
        Assert.True(working, $"worker should be working, got '{bench.WorkStatus}'");

        // A hostile spawns 2 tiles from the bench
        h.Combat.SpawnMonster(HostileDef(), 9.5f * Constants.TileSize, 5.5f * Constants.TileSize);
        h.Tick(4);

        // Work paused with visible feedback; worker backed off (not at bench)
        Assert.Equal("Danger nearby — work paused", bench.WorkStatus);
        float distToBench = MathF.Abs(worker.WorldX - bench.WorldX)
                          + MathF.Abs(worker.WorldY - bench.WorldY);
        Assert.True(distToBench > Constants.TileSize, $"worker should back off, dist {distToBench}");

        // Threat removed → work resumes
        h.Combat.Monsters.Clear();
        h.Tick(6);
        Assert.StartsWith("Working", bench.WorkStatus, StringComparison.Ordinal);
    }

    private static MonsterDef HostileDef() => new()
    {
        MonsterId = "test_wolf",
        Name = "Test Wolf",
        Hp = 30,
        Attack = 5,
        Speed = 50,
        IsHostile = true,
    };
}
