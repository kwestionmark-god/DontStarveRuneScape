namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using Xunit;

/// <summary>
/// Gear data loading from the real gear.json keyed envelope (weapons/armor/
/// tools sections), slot derivation from item ids, cache behavior, and the
/// inventory equip flag handling.
/// </summary>
public class GearLoadTests
{
    [Fact]
    public void LoadAll_ReadsRealGearJson_AndDerivesSlots()
    {
        var gear = GearItem.LoadAll();
        Assert.True(gear.Count >= 20, $"expected 20+ gear defs, got {gear.Count}");

        // Tools section -> hand weapons.
        var axe = gear["axe"];
        Assert.Equal("Axe", axe.Name);
        Assert.Equal("weapon", axe.Slot);

        // Armor slots derived from the item id suffix.
        Assert.Equal("head", gear["iron_helmet"].Slot);
        Assert.Equal("chest", gear["leather_armor"].Slot);
        Assert.Equal("chest", gear["iron_chestplate"].Slot);
        Assert.Equal("boots", gear["iron_boots"].Slot);

        // Weapons.
        Assert.Equal("weapon", gear["wooden_sword"].Slot);
        Assert.Equal("weapon", gear["steel_spear"].Slot);
    }

    [Fact]
    public void LoadAll_ParsesStatsAndSprite()
    {
        var gear = GearItem.LoadAll();
        var sword = gear["wooden_sword"];
        Assert.Equal(2, sword.Damage);
        Assert.Equal(1, sword.RequiredLevel);
        Assert.Equal("gear/wooden_sword", sword.SpriteKey);
    }

    [Fact]
    public void LoadAll_IsCached()
    {
        Assert.Same(GearItem.LoadAll(), GearItem.LoadAll());
    }

    [Fact]
    public void UnequipItem_ClearsEquippedFlag()
    {
        var inv = new Inventory();
        inv.AddItem("axe", 1);
        inv.EquipItem("axe");
        Assert.True(inv.Slots[0].IsEquipped);
        inv.UnequipItem("axe");
        Assert.False(inv.Slots[0].IsEquipped);
    }
}
