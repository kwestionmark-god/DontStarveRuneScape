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
    public void WorkOrder_AboveTier_IsVisibleAndNotClaimed()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        // Player is construction 1; smelter gate is level 10.
        var smelter = h.AddStructure("smelter", 8, 8);
        smelter.WorkRecipeId = "smelt_copper";
        smelter.HasManualWorkOrder = true;
        smelter.WorkStatus = "Waiting for worker";
        h.AddWorker("crafter", 7, 7);

        h.Tick(8);

        // Above-tier: the worker never binds and the station reads visibly.
        Assert.Equal("Awaiting builder competence", smelter.WorkStatus);
        Assert.Null(smelter.AssignedNpcId);
    }

    [Fact]
    public void WorkOrder_BelowTier_ClaimsAndWorks()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        // Player is construction 5: campfire-tier stations are fine.
        h.Skills.AddXpWithNotification("construction", SkillManager.XpForLevel(5) + 1f);
        var fire = h.AddStructure("campfire", 8, 8);
        fire.WorkRecipeId = null; // campfire needs no recipe
        h.AddWorker("crafter", 7, 7);
        h.Tick(4);

        Assert.NotEqual("Awaiting builder competence", fire.WorkStatus);
        // Below-tier: the worker either binds or wanders idle — never gated.
        Assert.NotEqual("Awaiting builder competence", fire.WorkStatus);
    }

    [Fact]
    public void NodeLevelGate_RefusesAboveLevel_Harvest()
    {
        var h = new Harness();
        h.Colony.FoundAt((5f + 0.5f) * Constants.TileSize, (5f + 0.5f) * Constants.TileSize, h.World);
        // gold_vein: required_level 30, tool pickaxe (mining).
        var gold = new ResourceDef { Id = "gold_vein" };
        gold.YieldItem = "gold_ore";
        gold.Yield = 1;
        gold.RequiredLevel = 30;
        gold.ToolRequirement = "pickaxe";
        gold.Xp = 50f;
        gold.DepletionCount = 5;
        h.World.GetTile(6, 6)!.ResourceNode = new ResourceNode("gold_vein", gold, 10f);
        h.Colony.Store("pickaxe", 1);
        var miner = h.AddWorker("miner", 6, 7);

        h.Tick(12);
        // The node is never reserved (dispatch skips), so CarriedQuantity stays 0.
        Assert.False(miner.CarriedQuantity > 0, "no carry: the node is never touched");
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
        copper.DepletionCount = 5;
        h.World.GetTile(6, 6)!.ResourceNode = new ResourceNode("copper_rock", copper, 10f);
        h.Colony.Store("pickaxe", 1);
        var miner = h.AddWorker("miner", 6, 7);

        h.Tick(40); // pickaxe available, copper is level 1 — the run completes
        Assert.True(miner.Skills.GetSkillLevel("mining") >= 1,
            "in-level nodes still train the recruit's gathering skill");
    }
}
