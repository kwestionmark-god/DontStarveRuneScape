namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.UI;
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
    public void LoadAll_IncludesCraftedTools_AndTorch()
    {
        var gear = GearItem.LoadAll();
        // The crafted/smithed tool line and the starter torch are equippable:
        // the gear panel lists inventory items present in this registry, and
        // "stone_axe" satisfies an "axe" tool requirement via the suffix match.
        Assert.Equal("weapon", gear["stone_axe"].Slot);
        Assert.Equal("weapon", gear["stone_pickaxe"].Slot);
        Assert.Equal("weapon", gear["torch"].Slot);
        Assert.Equal("items/stone_axe", gear["stone_axe"].SpriteKey);
        Assert.Equal("items/stone_pickaxe", gear["stone_pickaxe"].SpriteKey);
        Assert.Equal("items/torch", gear["torch"].SpriteKey);
        Assert.True(gear["stone_axe"].Damage > 0);
        Assert.True(gear["stone_pickaxe"].Damage > 0);
    }

    [Fact]
    public void ToggleEquip_EquippingAxeRetiresTorchInTheWeaponSlot()
    {
        var inv = new Inventory();
        inv.AddItem("torch", 1);
        var gear = new PlayerGear();
        GearPanel.ToggleEquip(inv, gear, "torch");
        Assert.True(inv.Slots[0].IsEquipped);
        Assert.Equal("torch", gear.Weapon!.Id);

        // Equipping the axe replaces the torch: both sources of truth agree.
        inv.AddItem("stone_axe", 1);
        GearPanel.ToggleEquip(inv, gear, "stone_axe");
        Assert.False(inv.Slots[0].IsEquipped);      // replaced torch's flag cleared
        Assert.True(inv.Slots[1].IsEquipped);       // axe flagged
        Assert.Equal("stone_axe", gear.Weapon!.Id); // weapon slot overwritten
    }

    [Fact]
    public void ToggleEquip_UnequipClearsBothSources()
    {
        var inv = new Inventory();
        inv.AddItem("stone_axe", 1);
        var gear = new PlayerGear();
        GearPanel.ToggleEquip(inv, gear, "stone_axe");
        Assert.Equal("stone_axe", gear.Weapon!.Id);
        Assert.True(inv.Slots[0].IsEquipped);

        GearPanel.ToggleEquip(inv, gear, "stone_axe"); // toggle back off

        Assert.Null(gear.Weapon);
        Assert.False(inv.Slots[0].IsEquipped);
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
