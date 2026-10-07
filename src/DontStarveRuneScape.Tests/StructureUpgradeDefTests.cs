namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using Xunit;

/// <summary>
/// Structure-upgrades slice, cycle 1: def parsing of the upgrades_to chain
/// field, and the def-level facts the lifecycle tests rely on.
/// </summary>
public class StructureUpgradeDefTests
{
    [Fact]
    public void WovenShelter_UpgradesToTimberShelter_ChainContinuesToStone()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var shelter = registry.GetStructure("woven_shelter");
        Assert.NotNull(shelter);
        Assert.Equal("timber_shelter", shelter!.UpgradesTo);
        var timber = registry.GetStructure("timber_shelter");
        Assert.NotNull(timber);
        Assert.Equal("stone_shelter", timber!.UpgradesTo);
    }

    [Fact]
    public void WoodenGate_UpgradesToIronGate_ParsedFromData()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var gate = registry.GetStructure("wooden_gate");
        Assert.NotNull(gate);
        Assert.Equal("iron_gate", gate!.UpgradesTo);
    }

    [Fact]
    public void UpgradeChain_IsLinear_TerminalHasNullSuccessor()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        Assert.Null(registry.GetStructure("iron_gate")!.UpgradesTo);
        Assert.Null(registry.GetStructure("iron_trap")!.UpgradesTo);
    }

    [Fact]
    public void SuccessorDefs_ExistInRegistry()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        foreach (var id in new[] { "iron_gate", "iron_trap", "timber_shelter", "stone_shelter" })
        {
            Assert.NotNull(registry.GetStructure(id));
        }
    }
}
