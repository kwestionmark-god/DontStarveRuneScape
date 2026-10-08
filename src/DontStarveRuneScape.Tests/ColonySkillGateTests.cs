namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Colony-building skill structure: the colony never out-produces the
/// player's own progression. Stations above the player's construction
/// level are visible but produce nothing ("Awaiting builder competence"),
/// and the assistant harvest path refuses nodes whose resource def's
/// required_level exceeds the recruit's own gathering-skill level.
/// Spec: docs/superpowers/specs/2026-10-08-colony-building-skill-structure-design.md
/// </summary>
public class ColonySkillGateTests
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

        public Structure AddStructure(string id, int x, int y, bool active = true)
        {
            var def = Buildings.Registry!.GetStructure(id);
            Assert.NotNull(def);
            var structure = new Structure
            {
                StructureId = id,
                StructureDef = def!,
                TileX = x,
                TileY = y,
                WorldX = (x + 0.5f) * Constants.TileSize,
                WorldY = (y + 0.5f) * Constants.TileSize,
                IsActive = active,
            };
            Buildings.Structures.Add(structure);
            if (def!.OccupiesTile)
                World.GetTile(x, y)!.Structure = def;
            return structure;
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

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony,
                    Buildings, Crafting, Skills, null, Foods, null, null, null, null, null);
        }
    }

    [Fact]
    public void WorkOrder_AbovePlayerConstructionLevel_IsRefused()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        // Player is construction 1; smelter gate is level 10.
        var smelter = h.AddStructure("smelter", 8, 8);

        // Attempt the queue the panel flow performs — today's panel path
        // does NOT consult the player gate, so this is the expected RED.
        smelter.WorkRecipeId = "smelt_copper";
        smelter.HasManualWorkOrder = true;
        smelter.WorkStatus = "Waiting for worker";

        Assert.False(smelter.HasManualWorkOrder,
            "Above-tier station must refuse the manual work order");
        Assert.Null(smelter.WorkRecipeId);
    }

    [Fact]
    public void AutoProduction_AbovePlayerConstructionLevel_IsVisibleAwaitingBuilder()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        var smelter = h.AddStructure("smelter", 8, 8);
        h.Colony.Store("raw_copper_ore", 20); // inputs available, worker idle
        h.AddWorker("builder", 7, 7);

        h.Tick(8);
        Assert.Equal("Awaiting builder competence", smelter.WorkStatus);
        // The worker must not have claimed the station.
        Assert.Null(smelter.AssignedNpcId);
    }

    [Fact]
    public void AutoProduction_LevelUp_ResumesWork()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        var smelter = h.AddStructure("smelter", 8, 8);
        h.Colony.Store("raw_copper_ore", 20);
        h.AddWorker("builder", 7, 7);

        h.Tick(4);
        Assert.Equal("Awaiting builder competence", smelter.WorkStatus);

        // Level the player to construction 10 (smelter gate).
        h.Skills.AddXpWithNotification("construction", SkillManager.XpForLevel(10) + 1f);
        h.Tick(20);

        Assert.Equal("builder", smelter.AssignedNpcId);
        Assert.NotEqual("Awaiting builder competence", smelter.WorkStatus);
    }

    [Fact]
    public void NodeLevelGate_RefusesAboveLevel_Harvest()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        // gold_vein: required_level 30, tool pickaxe (mining), not woodcutting.
        var gold = new ResourceDef { Id = "gold_vein" };
        gold.YieldItem = "gold_ore";
        gold.Yield = 1;
        gold.RequiredLevel = 30;
        gold.ToolRequirement = "pickaxe";
        gold.Xp = 50f;
        h.World.GetTile(6, 6)!.ResourceNode = new ResourceNode("gold_vein", gold, 10f);
        h.Colony.Store("pickaxe", 1);
        var worker = h.AddWorker("miner", 6, 7);

        h.Tick(12);
        Assert.Equal("Skill too low", worker.ColonyNeedStatus);
        Assert.Equal(0, worker.Skills.GetSkillLevel("mining"));
        Assert.False(worker.CarriedQuantity > 0, "no carry: the node is never touched");
    }

    [Fact]
    public void NodeLevelGate_AllowsInLevel_Harvest()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        var copper = new ResourceDef { Id = "copper_rock" };
        copper.YieldItem = "copper_ore";
        copper.Yield = 1;
        copper.RequiredLevel = 1;
        copper.ToolRequirement = "pickaxe";
        copper.Xp = 35f;
        h.World.GetTile(6, 6)!.ResourceNode = new ResourceNode("copper_rock", copper, 10f);
        h.Colony.Store("pickaxe", 1);
        var worker = h.AddWorker("miner", 6, 7);

        h.Tick(20);
        Assert.True(worker.Skills.GetSkillLevel("mining") >= 1);
        Assert.True(h.Colony.GetItemQuantity("copper_ore") > 0 || worker.CarriedQuantity > 0);
    }
}
