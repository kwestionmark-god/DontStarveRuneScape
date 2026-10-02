namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.UI;
using Silk.NET.Input;
using Xunit;

/// <summary>
/// InventoryPanel keyboard path: arrow-key selection and the Enter toggle
/// that equips/unequips through the same shared path as the EQUIP button
/// and the gear panel.
/// </summary>
public class InventoryPanelTests
{
    [Fact]
    public void Enter_TogglesEquipThroughTheSharedPath()
    {
        var inv = new Inventory();
        inv.AddItem("stone_axe", 1);
        var gear = new PlayerGear();
        var panel = new InventoryPanel(); // SelectedIndex starts at slot 0

        panel.HandleKey(Key.Enter, inv, gear);

        Assert.True(inv.Slots[0].IsEquipped);
        Assert.Equal("stone_axe", gear.Weapon!.Id);

        panel.HandleKey(Key.Enter, inv, gear);

        Assert.False(inv.Slots[0].IsEquipped);
        Assert.Null(gear.Weapon);
    }

    [Fact]
    public void Enter_OnNonGearItem_DoesNothing()
    {
        var inv = new Inventory();
        inv.AddItem("berries", 1);
        var gear = new PlayerGear();
        var panel = new InventoryPanel();

        panel.HandleKey(Key.Enter, inv, gear);

        Assert.False(inv.Slots[0].IsEquipped);
        Assert.Null(gear.Weapon);
    }

    [Fact]
    public void Enter_OnEmptySlot_DoesNothing()
    {
        var inv = new Inventory();
        var gear = new PlayerGear();
        var panel = new InventoryPanel();

        panel.HandleKey(Key.Enter, inv, gear);

        Assert.Null(gear.Weapon);
        Assert.False(inv.Slots[0].IsEquipped);
    }

    [Fact]
    public void ArrowKeys_MoveSelection()
    {
        var inv = new Inventory();
        var gear = new PlayerGear();
        var panel = new InventoryPanel();

        panel.HandleKey(Key.Right, inv, gear);
        panel.HandleKey(Key.Down, inv, gear);

        Assert.Equal(5, panel.SelectedIndex); // 1 right, then down a row of 4 columns
    }
}
