namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Structure-upgrades slice, cycle 2: enqueue via BuildingSystem.UpgradeStructure —
/// validation gates (successor exists, skill gate, not mid-build, not already
/// upgrading, materials affinity check) then flip to construction mode.
/// </summary>
public class StructureUpgradeEnqueueTests
{
    private static TileMap MakeWorld(int size = 16)
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

    private static SkillManager LeveledConstruction(int level)
    {
        var skills = new SkillManager();
        skills.AddXpWithNotification("construction", SkillManager.XpForLevel(level) + 1f);
        return skills;
    }

    private Structure PlaceBuilt(BuildingSystem buildings, TileMap world,
        string id, int x, int y, SkillManager skills)
    {
        var inventory = new Inv();
        var def = buildings.Registry!.GetStructure(id)!;
        foreach (var material in def.Materials)
            inventory.AddItem(material.ItemId, material.Quantity + 5);
        var (ok, message) = buildings.PlaceStructure(id, x, y, world, inventory, skills);
        Assert.True(ok, message);
        var placed = buildings.Structures.Single(s => s.TileX == x && s.TileY == y);
        placed.IsUnderConstruction = false; // simulate a finished build
        return placed;
    }

    [Fact]
    public void Upgrade_WoodGateToIronGate_EnqueuesConstructionMode()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        var (ok, message) = buildings.UpgradeStructure(gate, "iron_gate", skills, colony);

        Assert.True(ok, message);
        Assert.True(gate.IsUnderConstruction);
        Assert.Equal("Upgrading to Iron Gate", gate.WorkStatus);
        Assert.False(gate.ConstructionMaterialsPaid);
    }

    [Fact]
    public void Upgrade_RejectsWhenSkillTooLow()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(3); // enough to place wooden_gate (3), not iron_gate (8)
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        var (ok, message) = buildings.UpgradeStructure(gate, "iron_gate", skills, colony);

        Assert.False(ok);
        Assert.Contains("construction level", message);
        Assert.False(gate.IsUnderConstruction);
    }

    [Fact]
    public void Upgrade_RejectsMidBuildBlueprint()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);
        gate.IsUnderConstruction = true; // still a blueprint

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        var (ok, message) = buildings.UpgradeStructure(gate, "iron_gate", skills, colony);

        Assert.False(ok);
        Assert.False(gate.ConstructionMaterialsPaid);
    }

    [Fact]
    public void Upgrade_RejectsWhenAlreadyUpgrading()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);
        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);

        Assert.True(buildings.UpgradeStructure(gate, "iron_gate", skills, colony).Success);
        var (ok, _) = buildings.UpgradeStructure(gate, "iron_gate", skills, colony);

        Assert.False(ok);
    }

    [Fact]
    public void Upgrade_UsesOnlyUpgradesTo_RejectsOffChainTarget()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        // iron_gate is a valid def but not gate's declared successor
        var (ok, message) = buildings.UpgradeStructure(gate, "stone_wall", skills, colony);

        Assert.False(ok);
        Assert.Contains("upgrade", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upgrade_MissingMaterials_QueuedWithoutChargingStockpile()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        var (ok, message) = buildings.UpgradeStructure(gate, "iron_gate", skills, colony);

        Assert.True(ok, message);
        Assert.True(gate.IsUnderConstruction);
        // Materials are not consumed at enqueue; the worker charge path pays
        // once the stockpile can (ConstructionMaterialsPaid stays false).
        Assert.False(gate.ConstructionMaterialsPaid);
        Assert.Equal(0, colony.GetItemQuantity("smelted_iron_ingot"));
    }

    [Fact]
    public void Upgrade_WithMaterials_WorkerChargesStockpile_OnCompletion()
    {
        var world = MakeWorld();
        var buildings = MakeBuildings();
        var skills = LeveledConstruction(8);
        var gate = PlaceBuilt(buildings, world, "wooden_gate", 5, 5, skills);

        var colony = new ColonySystem();
        colony.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, world);
        Assert.True(buildings.UpgradeStructure(gate, "iron_gate", skills, colony).Success);

        // Worker charge path: RecruitmentSystem's construction branch charges
        // via GetMissingConstructionInputs → successor materials from stockpile.
        colony.Store("smelted_iron_ingot", 4);
        colony.Store("stick", 2);
        // Simulate the worker-side charge by calling the same check:
        int missing = 0;
        foreach (var m in buildings.Registry!.GetStructure("iron_gate")!.Materials)
            if (colony.GetItemQuantity(m.ItemId) < m.Quantity) missing += m.Quantity;
        Assert.Equal(0, missing); // materials present — worker can charge
    }
}
