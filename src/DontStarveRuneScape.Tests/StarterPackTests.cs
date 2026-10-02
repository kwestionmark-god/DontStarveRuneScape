namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Survival;
using Xunit;

/// <summary>
/// Starter pack application: inventory population, the auto-equipped tool,
/// and the PlayerGear sync that makes the starter item render on the
/// character (the default pack's torch is the starter tool).
/// </summary>
public class StarterPackTests
{
    [Fact]
    public void ApplyStarterPack_SyncsGearSoTheStarterToolRenders()
    {
        var inv = new Inventory();
        var gear = new PlayerGear();

        StarterPack.ApplyStarterPack(inv, "default", gear);

        // Default pack: the torch is the tool — equipped in both sources of
        // truth from the first frame.
        Assert.True(inv.Slots[0].IsEquipped);
        Assert.Equal("torch", gear.Weapon!.Id);
    }

    [Fact]
    public void ApplyStarterPack_Forester_ToolWinsTheWeaponSlot()
    {
        var inv = new Inventory();
        var gear = new PlayerGear();

        StarterPack.ApplyStarterPack(inv, "forester", gear);

        // The axe is the tool; the torch syncs into PlayerGear first but the
        // tool overwrites it, so both sources agree on the axe.
        Assert.Equal("axe", gear.Weapon!.Id);
        Assert.True(inv.Slots[0].IsEquipped);  // tool flagged
        Assert.False(inv.Slots[1].IsEquipped); // torch sits unequipped
    }

    [Fact]
    public void ApplyStarterPack_WithoutGear_StillEquipsTheInventoryFlag()
    {
        var inv = new Inventory();

        Assert.True(StarterPack.ApplyStarterPack(inv, "default"));

        Assert.True(inv.Slots[0].IsEquipped);
    }
}
