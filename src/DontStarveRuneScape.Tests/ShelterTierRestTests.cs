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
/// Shelter-tier rest-recovery differentiation (structure-upgrades follow-up):
/// all three shelter tiers are valid rest spots and storm shelters; higher
/// tiers recover rest faster — mitigate, never nullify (fire stays king).
/// Rates: woven 0.16/s, timber 0.18/s, stone 0.20/s (fire 0.22/s).
/// </summary>
public class ShelterTierRestTests
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
                {
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
                }
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

        public Structure PlaceDirect(string id, int x, int y)
        {
            var def = Buildings.Registry!.GetStructure(id)!;
            var structure = new Structure
            {
                StructureId = id,
                StructureDef = def,
                TileX = x,
                TileY = y,
                WorldX = (x + 0.5f) * Constants.TileSize,
                WorldY = (y + 0.5f) * Constants.TileSize,
                Health = (int)def.Hp,
                MaxHealth = (int)def.Hp,
                IsActive = true,
            };
            Buildings.Structures.Add(structure);
            if (def.OccupiesTile)
                World.GetTile(x, y)!.Structure = def;
            return structure;
        }

        public RecruitNpc AddWorker(string id, int tileX, int tileY, float rest)
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
                ColonyRest = rest,
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
    }

    [Fact]
    public void RestCandidateScan_IncludesTimberAndStoneShelters()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        // Only a timber shelter — no bench, no woven shelter. If the rest
        // candidate scan misses it, the worker reports "No rest place".
        h.PlaceDirect("timber_shelter", 6, 5);
        var worker = h.AddWorker("a1", 6, 6, rest: 30f);

        h.Tick(40);

        Assert.NotEqual("No rest place", worker.ColonyRestStatus);
        Assert.True(worker.ColonyRest > 30f,
            $"rest should recover at the timber shelter (got {worker.ColonyRest})");
    }

    [Fact]
    public void TimberShelter_RecoversFasterThanWoven()
    {
        // Same geometry, same rest start; only the shelter tier differs.
        var woven = new Harness();
        woven.FoundColony(5, 5);
        woven.PlaceDirect("woven_shelter", 6, 5);
        var wovenWorker = woven.AddWorker("a1", 6, 6, rest: 50f);

        var timber = new Harness();
        timber.FoundColony(5, 5);
        timber.PlaceDirect("timber_shelter", 6, 5);
        var timberWorker = timber.AddWorker("a1", 6, 6, rest: 50f);

        const int ticks = 40;
        woven.Tick(ticks);
        timber.Tick(ticks);

        float wovenGain = wovenWorker.ColonyRest - 50f;
        float timberGain = timberWorker.ColonyRest - 50f;
        Assert.True(timberGain - wovenGain > 0.1f,
            $"timber ({timberGain:F2}) should out-recover woven ({wovenGain:F2})");
    }

    [Fact]
    public void StoneShelter_InStorm_CountsAsSheltered()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        h.PlaceDirect("stone_shelter", 6, 5);
        var worker = h.AddWorker("a1", 6, 6, rest: 30f);
        h.Weather.SetWeather("storm");

        h.Tick(40);

        // Sheltered by the stone shelter: recovery NOT halved by the storm.
        // ~0.05/tick × ~36 resting ticks ≈ 1.8; unsheltered storm would be ~0.9.
        Assert.True(worker.ColonyRest - 30f > 1.4f,
            $"stone shelter should shield rest from the storm (gain {worker.ColonyRest - 30f:F2})");
        Assert.NotEqual("No rest place", worker.ColonyRestStatus);
    }
}
