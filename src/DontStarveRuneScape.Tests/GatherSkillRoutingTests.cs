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
/// Cross-skill audit slice A: worker gather XP trains the skill the node
/// actually belongs to. GatherSkillFor must mirror the player's tool
/// switch (InteractSystem.cs:121-127) exactly: axe → woodcutting,
/// pickaxe → mining, fishing_rod → fishing, everything else (bucket water,
/// no-tool nodes) → foraging.
/// Spec: docs/superpowers/specs/2026-10-09-cross-skill-audit-xp-routing-design.md
/// </summary>
public class GatherSkillRoutingTests
{
    // ─── Shared fixtures ─────────────────────────────────────────────────

    private static ResourceDef TreeDef(string id = "oak_tree", int requiredLevel = 1) => new()
    {
        Id = id,
        Name = "Tree",
        Biome = "forest",
        Category = "wood",
        YieldItem = "oak_logs",
        Yield = 1,
        Xp = 25f,
        DepletionCount = 4,
        ToolRequirement = "axe",
        RequiredLevel = requiredLevel,
        Seasons = [],
    };

    private static ResourceDef WaterDef() => new()
    {
        Id = "water_source",
        Name = "Water Source",
        Category = "water",
        YieldItem = "water",
        Yield = 1,
        Xp = 0f, // real def awards no XP — routing is pinned by the table test
        DepletionCount = -1,
        ToolRequirement = "bucket",
        RequiredLevel = 1,
        Seasons = [],
    };

    // ─── 1. Routing table, data-driven over the real registry ────────────

    [Fact]
    public void GatherSkillRouting_MirrorsPlayerToolSwitch()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        var registry = new ResourceRegistry(loader.ResourcesData
            .Select(Bootstrap.BuildResourceDef));
        Assert.NotEmpty(registry.Resources);

        foreach (var def in registry.Resources.Values)
        {
            string expected = def.ToolRequirement switch
            {
                "axe" => "woodcutting",
                "pickaxe" => "mining",
                "fishing_rod" => "fishing",
                _ => "foraging",
            };
            string actual = RecruitmentSystem.GatherSkillFor(def);
            Assert.True(actual == expected,
                $"{def.Id} (tool '{def.ToolRequirement}') should train {expected}, got {actual}");
        }
    }

    // ─── 2–5. Worker path end-to-end (ColonySkillGateTests harness) ─────

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

    private static ResourceNode TreeAt(TileMap world, int x, int y,
        string id = "oak_tree", int requiredLevel = 1)
    {
        var node = new ResourceNode(id, TreeDef(id, requiredLevel), 10f);
        world.GetTile(x, y)!.ResourceNode = node;
        return node;
    }

    [Fact]
    public void WorkerChop_AxeInStore_TrainsWoodcuttingNotMining()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        TreeAt(h.World, 6, 6);
        h.Colony.Store("axe", 1);
        var worker = h.AddWorker("chopper", 5, 6);

        h.Tick(40); // walk + harvest + haul home

        // Outcome: oak_logs landed in the colony stockpile.
        int storedLogs = h.Colony.GetItemQuantity("oak_logs");
        Assert.True(storedLogs > 0, $"expected oak_logs in store, got {storedLogs}");
        // The worker trains WOODCUTTING (mirrors the player's axe switch),
        // not mining (the old any-tool-node → mining trap).
        float chopXp = worker.Skills.GetSkill("woodcutting").Xp;
        Assert.True(chopXp > 0f, $"expected woodcutting xp > 0, got {chopXp}");
        Assert.Equal(0f, worker.Skills.GetSkill("mining").Xp);
        // The axe is a gate, not a consumable — it stays.
        Assert.True(h.Colony.GetItemQuantity("axe") > 0);
    }

    [Fact]
    public void ColonyNodeGate_TreesGateOnWoodcuttingLevel()
    {
        var h = new Harness();
        h.FoundColony(5, 5);
        // Oak: required_level 1 — a fresh level-1 worker may chop.
        TreeAt(h.World, 6, 6, "oak_tree", 1);
        // Maple: required_level 20 — a fresh worker is above-gate-blocked.
        TreeAt(h.World, 7, 7, "maple_tree", 20);
        h.Colony.Store("axe", 1);
        var worker = h.AddWorker("chopper", 5, 6);

        h.Tick(20);

        // The in-level tree is claimed and worked; the above-level tree
        // never yields (the gate now reads the worker's WOODCUTTING level,
        // the skill the tree trains — the reachability law).
        Assert.True(worker.Skills.GetSkill("woodcutting").Xp > 0f,
            "in-level oak trains the woodcutting the gate reads");
        Assert.Equal(0, h.Colony.GetItemQuantity("maple_logs"));
    }
}
