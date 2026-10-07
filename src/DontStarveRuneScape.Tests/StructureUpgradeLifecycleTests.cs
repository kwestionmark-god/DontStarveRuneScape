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
/// Structure-upgrades slice, cycle 3: the worker-side upgrade lifecycle.
/// A worker dispatched to an upgrading structure charges the successor's
/// materials from the stockpile, builds, and the def swaps in place —
/// position, tile occupancy, and assignment semantics preserved.
/// </summary>
public class StructureUpgradeLifecycleTests
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
        public SkillManager Skills = LeveledConstruction(8);
        public CombatSystem Combat = new();
        public FoodRegistry Foods = MakeFoods();
        public DayNightCycle Clock = new();
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

        private static SkillManager LeveledConstruction(int level)
        {
            var skills = new SkillManager();
            skills.AddXpWithNotification("construction", SkillManager.XpForLevel(level) + 1f);
            return skills;
        }

        public Structure PlaceBuilt(string id, int x, int y)
        {
            var inventory = new Inv();
            var def = Buildings.Registry!.GetStructure(id)!;
            foreach (var material in def.Materials)
                inventory.AddItem(material.ItemId, material.Quantity + 5);
            var (ok, message) = Buildings.PlaceStructure(id, x, y, World, inventory, Skills);
            Assert.True(ok, message);
            var placed = Buildings.Structures.Single(s => s.TileX == x && s.TileY == y);
            placed.IsUnderConstruction = false; // simulate a finished build
            return placed;
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
                    null, null, null, null, Clock);
        }
    }

    [Fact]
    public void WorkerChargesSuccessorMaterials_FromStockpile()
    {
        var h = new Harness();
        h.FoundColony(8, 8);
        var gate = h.PlaceBuilt("wooden_gate", 8, 8);
        var worker = h.AddWorker("a1", 8, 9);
        Assert.True(h.Buildings.UpgradeStructure(gate, "iron_gate", h.Skills, h.Colony).Success);

        // Successor materials: 4 smelted_iron_ingot + 2 stick
        h.Colony.Store("smelted_iron_ingot", 4);
        h.Colony.Store("stick", 2);

        h.Tick(40); // enough to walk + charge, not enough to finish (80+ ticks)

        Assert.True(gate.ConstructionMaterialsPaid,
            "worker should have charged successor materials");
        Assert.Equal(0, h.Colony.GetItemQuantity("smelted_iron_ingot"));
        Assert.Equal(0, h.Colony.GetItemQuantity("stick"));
        Assert.True(gate.IsUnderConstruction); // still mid-upgrade
        Assert.Contains("Upgrading", gate.WorkStatus);
    }

    [Fact]
    public void UpgradeCompletes_StructureSwapsToSuccessorDefInPlace()
    {
        var h = new Harness();
        h.FoundColony(8, 8);
        var gate = h.PlaceBuilt("wooden_gate", 8, 8);
        var worker = h.AddWorker("a1", 8, 9);
        Assert.True(h.Buildings.UpgradeStructure(gate, "iron_gate", h.Skills, h.Colony).Success);

        h.Colony.Store("smelted_iron_ingot", 4);
        h.Colony.Store("stick", 2);

        h.Tick(400); // enough steps to walk + build 20s progress at dt=0.25

        Assert.False(gate.IsUnderConstruction);
        Assert.Null(gate.UpgradingToId);
        Assert.Equal("iron_gate", gate.StructureId);
        Assert.Equal("Iron Gate", gate.StructureDef.Name);
        Assert.Equal(60, gate.MaxHealth);
        Assert.Equal(8, gate.TileX);   // position preserved
        Assert.Equal(8, gate.TileY);
        Assert.Equal("structure/iron_gate", gate.StructureDef.SpriteKey);
        // Tile occupancy follows the successor def
        Assert.NotNull(h.World.GetTile(8, 8)!.Structure);
        Assert.Equal("iron_gate", h.World.GetTile(8, 8)!.Structure!.Id);
    }

    [Fact]
    public void Upgrade_WithoutMaterials_WorkerWaits_DoesNotCharge()
    {
        var h = new Harness();
        h.FoundColony(8, 8);
        var gate = h.PlaceBuilt("wooden_gate", 8, 8);
        var worker = h.AddWorker("a1", 8, 9);
        Assert.True(h.Buildings.UpgradeStructure(gate, "iron_gate", h.Skills, h.Colony).Success);

        // No iron in stockpile
        h.Tick(100);

        Assert.False(gate.ConstructionMaterialsPaid);
        Assert.False(gate.IsUnderConstruction == false); // still upgrading
        Assert.True(gate.IsUnderConstruction);
        Assert.Contains("materials", gate.WorkStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletedUpgrade_SnapshotRoundTripsAsSuccessor()
    {
        var h = new Harness();
        h.FoundColony(8, 8);
        var gate = h.PlaceBuilt("wooden_gate", 8, 8);
        var worker = h.AddWorker("a1", 8, 9);
        Assert.True(h.Buildings.UpgradeStructure(gate, "iron_gate", h.Skills, h.Colony).Success);
        h.Colony.Store("smelted_iron_ingot", 4);
        h.Colony.Store("stick", 2);
        h.Tick(400);

        // Save
        var snapshot = h.Buildings.GetSnapshot();
        var restored = new BuildingSystem { Registry = h.Buildings.Registry };
        restored.RestoreSnapshot(snapshot, h.World);

        var restoredGate = restored.Structures.Single(s => s.TileX == 8 && s.TileY == 8);
        Assert.Equal("iron_gate", restoredGate.StructureId);
        Assert.False(restoredGate.IsUnderConstruction);
        Assert.Null(restoredGate.UpgradingToId);
    }

    [Fact]
    public void MidUpgrade_SaveRestore_PreservesUpgradeState()
    {
        var h = new Harness();
        h.FoundColony(8, 8);
        var gate = h.PlaceBuilt("wooden_gate", 8, 8);
        var worker = h.AddWorker("a1", 8, 9);
        Assert.True(h.Buildings.UpgradeStructure(gate, "iron_gate", h.Skills, h.Colony).Success);
        // No materials stocked: stays mid-upgrade

        var snapshot = h.Buildings.GetSnapshot();
        var restored = new BuildingSystem { Registry = h.Buildings.Registry };
        restored.RestoreSnapshot(snapshot, h.World);

        var restoredGate = restored.Structures.Single(s => s.TileX == 8 && s.TileY == 8);
        Assert.Equal("wooden_gate", restoredGate.StructureId);
        Assert.True(restoredGate.IsUnderConstruction);
        Assert.Equal("iron_gate", restoredGate.UpgradingToId);
        Assert.False(restoredGate.ConstructionMaterialsPaid);
    }
}
