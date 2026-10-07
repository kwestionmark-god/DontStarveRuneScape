namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Per-recruit skill stats: every colony recruit carries its own
/// SkillManager; recruited workers gain XP through the work they perform
/// (gathering trains the node's skill, construction trains construction),
/// levels rise via the OSRS curve, the state persists through
/// NPCSystem snapshots, and gathering level yields extra harvests.
/// Spec: docs/superpowers/specs/2026-10-07-per-recruit-skill-stats-design.md
/// </summary>
public sealed class RecruitSkillTests
{
    private static Harness NewColony()
    {
        var h = new Harness();
        // Anchor at tile (10,10) — FoundAt takes world pixels.
        Assert.True(h.Colony.FoundAt(
            10.5f * Constants.TileSize, 10.5f * Constants.TileSize, h.World));
        return h;
    }

    // -- 1. Gather XP accrues to the working recruit ----------------------

    [Fact]
    public void Worker_GainsForagingXp_FromGatheringResources()
    {
        var h = NewColony();
        var def = new ResourceDef
        {
            Id = "test_berry_bush",
            Name = "Test Berry Bush",
            YieldItem = "berry",
            Yield = 2,
            Xp = 7f,
            DepletionCount = 1000,
        };
        h.PlaceNode(12, 11, def, 1000f);

        var worker = h.AddWorker("forager", 12, 10, "assistant");
        h.Tick(80);

        Assert.True(worker.Skills.GetSkill("foraging").Xp >= def.Xp,
            $"Expected foraging xp >= {def.Xp}, got {worker.Skills.GetSkill("foraging").Xp}");
    }

    // -- 2. Construction completion trains the builder --------------------

    [Fact]
    public void Builder_GainsConstructionXp_OnCompletion()
    {
        var h = NewColony();
        var worker = h.AddWorker("builder", 12, 10, "assistant");
        var site = h.AddStructure("campfire", 14, 10);
        site.IsUnderConstruction = true;
        site.ConstructionMaterialsPaid = true;

        h.Tick(200); // 80 ticks of build progress + approach + buffer

        Assert.False(site.IsUnderConstruction);
        Assert.True(worker.Skills.GetSkill("construction").Xp >= 10f,
            $"Expected construction xp >= 10, got {worker.Skills.GetSkill("construction").Xp}");
    }

    // -- 3. Guard attack XP (deferred hook, lands with combat scaling) ----

    [Fact(Skip = "guard XP hook lands in the combat path; covered by a later slice")]
    public void Guard_GainsAttackXp_InCombat() { }

    // -- 4. Levels follow the OSRS curve ----------------------------------

    [Fact]
    public void Recruit_LevelsUp_WithEnoughXp()
    {
        var h = NewColony();
        var worker = h.AddWorker("lvl", 12, 10, "assistant");

        worker.Skills.AddXpWithNotification("foraging", SkillManager.XpForLevel(4) + 1f);

        Assert.Equal(4, worker.Skills.GetSkillLevel("foraging"));
    }

    // -- 5. Persistence round-trip ----------------------------------------

    [Fact]
    public void Recruit_Skills_Persist_ThroughSnapshot()
    {
        var h = NewColony();
        var worker = h.AddRegistryWorker(12, 10, "assistant");
        worker.Skills.AddXpWithNotification("woodcutting", SkillManager.XpForLevel(5) + 1f);

        var snapshot = h.Npcs.GetSnapshot();
        var fresh = new NPCSystem();
        fresh.RestoreSnapshot(snapshot, h.NpcRegistry, h.World);

        var restored = fresh.NPCs.OfType<RecruitNpc>()
            .First(n => n.NpcId == worker.NpcId);
        Assert.Equal(5, restored.Skills.GetSkillLevel("woodcutting"));
    }

    // -- 6. Old saves load to a fresh level-1 manager ---------------------

    [Fact]
    public void OldSave_WithoutSkills_LoadsFresh()
    {
        var h = NewColony();
        var worker = h.AddRegistryWorker(12, 10, "assistant");
        var snapshot = h.Npcs.GetSnapshot();
        foreach (var row in snapshot.NPCs)
            row.Skills = null; // simulate a pre-slice save

        var fresh = new NPCSystem();
        fresh.RestoreSnapshot(snapshot, h.NpcRegistry, h.World);

        var restored = fresh.NPCs.OfType<RecruitNpc>()
            .First(n => n.NpcId == worker.NpcId);
        Assert.Equal(1, restored.Skills.GetSkillLevel("foraging"));
        Assert.Equal(0f, restored.Skills.GetSkill("foraging").Xp);
    }

    // -- 7. Yield feedback: higher level gathers more ---------------------

    [Fact]
    public void HigherLevelGatherer_YieldsMore()
    {
        var h = NewColony();
        var def = new ResourceDef
        {
            Id = "test_berry_bush2",
            Name = "Test Berry Bush",
            YieldItem = "berry",
            Yield = 1,
            Xp = 0f,
            DepletionCount = 100000,
        };

        var low = h.AddWorker("low", 12, 8, "assistant");
        var high = h.AddWorker("high", 12, 12, "assistant");
        high.Skills.AddXpWithNotification("foraging", SkillManager.XpForLevel(12) + 1f);

        // Mirror-symmetric around the anchor: identical haul distances so
        // only the skill level differs between the two gatherers.
        h.PlaceNode(12, 9, def, 100000f);
        h.PlaceNode(12, 11, def, 100000f);

        h.Tick(600);

        Assert.True(high.TotalGathered > low.TotalGathered,
            $"level-12 gatherer ({high.TotalGathered}) should out-yield level-1 ({low.TotalGathered})");
        Assert.True(low.TotalGathered > 0, "low gatherer should still produce");
    }

    // -- 8. Dashboard surface: headline skill on the colonist row ----------

    [Fact]
    public void Dashboard_RowShowsRecruitHeadlineSkill()
    {
        // Game-shaped harness (the dashboard reads from Game, not bare systems).
        var game = new Game();
        var world = new TileMap(24, 24);
        for (int tx = 0; tx < 24; tx++)
            for (int ty = 0; ty < 24; ty++)
            {
                world.Tiles[tx, ty].Elevation = Constants.SeaLevel + 5f;
                world.Tiles[tx, ty].Biome = new BiomeDef { Id = "plains", Name = "Plains" };
            }
        game.World = world;
        game.Player = new Player(100f, 100f)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        game.NPCSystem = new NPCSystem();
        game.ColonySystem = new ColonySystem();
        game.BuildingSystem = new BuildingSystem { Registry = new StructureDefRegistry() };
        Assert.True(game.ColonySystem.FoundAt(100f, 100f, world));

        var guard = new RecruitNpc
        {
            NpcId = "g1", Name = "Guard", IsRecruited = true, IsActive = true,
            RecruitBehavior = "guard",
        };
        guard.Skills.AddXpWithNotification("attack", SkillManager.XpForLevel(6) + 1f);
        game.NPCSystem.NPCs.Add(guard);
        var forager = new RecruitNpc
        {
            NpcId = "f1", Name = "Forager", IsRecruited = true, IsActive = true,
            RecruitBehavior = "assistant",
        };
        forager.Skills.AddXpWithNotification("foraging", SkillManager.XpForLevel(3) + 1f);
        game.NPCSystem.NPCs.Add(forager);
        var fresh = new RecruitNpc
        {
            NpcId = "n1", Name = "Newcomer", IsRecruited = true, IsActive = true,
            RecruitBehavior = "assistant",
        };
        game.NPCSystem.NPCs.Add(fresh);

        var panel = new DashboardPanel { Game = game };

        Assert.Equal("attack 6", DashboardPanel.ColonistSkillLabel(guard));
        Assert.Equal("foraging 3", DashboardPanel.ColonistSkillLabel(forager));
        Assert.Equal("untrained", DashboardPanel.ColonistSkillLabel(fresh));
    }

    // -- Harness -----------------------------------------------------------

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
        public FoodRegistry Foods = MakeFoods();
        public RecruitmentSystem Recruits = new();
        public NpcRegistry NpcRegistry = MakeNpcRegistry();

        private static NpcRegistry MakeNpcRegistry()
        {
            var registry = new NpcRegistry();
            registry.LoadAll();
            return registry;
        }

        private static TileMap MakeWorld(int size = 30)
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

        public void PlaceNode(int tx, int ty, ResourceDef def, float reserve)
        {
            var tile = World.GetTile(tx, ty)!;
            tile.ResourceNode = new ResourceNode(def.Id, def, reserve);
        }

        public Structure AddStructure(string id, int x, int y)
        {
            var def = Buildings.Registry.GetStructure(id);
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

        public RecruitNpc AddWorker(string id, int tileX, int tileY, string behavior)
        {
            var npc = new RecruitNpc
            {
                NpcId = id,
                WorldX = (tileX + 0.5f) * Constants.TileSize,
                WorldY = (tileY + 0.5f) * Constants.TileSize,
                IsActive = true,
                IsRecruited = true,
                RecruitBehavior = behavior,
                ColonyHunger = 100f,
                ColonyRest = 100f,
                Health = 100,
                MaxHealth = 100,
            };
            Npcs.NPCs.Add(npc);
            return npc;
        }

        /// <summary>A recruit built from the real npcs.json registry — the
        /// path persistence restore takes (it skips NPCs with no def). Keeps
        /// the def's NpcId so the registry lookup on restore succeeds.</summary>
        public RecruitNpc AddRegistryWorker(int tileX, int tileY, string behavior)
        {
            var def = NpcRegistry.Npcs["recruit_forest_assistant"];
            var npc = (RecruitNpc)Npc.FromDef(def);
            npc.IsRecruited = true;
            npc.RecruitBehavior = behavior;
            npc.WorldX = (tileX + 0.5f) * Constants.TileSize;
            npc.WorldY = (tileY + 0.5f) * Constants.TileSize;
            npc.ColonyHunger = 100f;
            npc.ColonyRest = 100f;
            Npcs.NPCs.Add(npc);
            return npc;
        }

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony, Buildings,
                    Crafting, Player.SkillManager, null, Foods);
        }
    }
}
