namespace DontStarveRuneScape.Survival;

using System.Collections.Generic;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;

/// <summary>
/// CharacterDefinition — Player character definition for future background selection.
/// </summary>
public sealed class CharacterDefinition
{
    /// <summary>Player's chosen name (empty = default).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Character background (e.g. "forester", "prospector").</summary>
    public string Background { get; set; } = string.Empty;

    /// <summary>Initial stat allocations (skill_id → {sub_stat: points}).</summary>
    public Dictionary<string, Dictionary<string, int>> StartingStats { get; set; } = new();

    /// <summary>Which starter pack to use.</summary>
    public string StarterPackId { get; set; } = "default";
}

/// <summary>
/// StarterPack — Predefined equipment bundles and application logic.
/// </summary>
public static class StarterPack
{
    // Starter pack definitions: (tool_item, torch_item, food_item)
    // 3 variants per design doc
    private static readonly Dictionary<string, (string Tool, string Torch, string Food)> StarterPacks = new()
    {
        ["forester"] = ("axe", "torch", "berries"),
        ["prospector"] = ("pickaxe", "torch", "raw_meat"),
        ["scavenger"] = ("torch", "berries", "raw_fish"),
        ["default"] = ("torch", "berries", "raw_fish"),
    };

    /// <summary>
    /// Get the starter pack items for a given pack ID.
    /// </summary>
    /// <param name="packId">One of "forester", "prospector", "scavenger", "default".</param>
    /// <returns>(tool_item, torch_item, food_item) tuple.</returns>
    public static (string Tool, string Torch, string Food) GetStarterPack(string packId)
    {
        return StarterPacks.TryGetValue(packId, out var pack) ? pack : StarterPacks["default"];
    }

    /// <summary>
    /// Apply a starter pack to the player's inventory.
    /// </summary>
    /// <param name="inventory">The player's inventory to populate.</param>
    /// <param name="packId">Which starter pack to apply.</param>
    /// <param name="gear">Optional gear slots to sync the starter tool/torch
    /// into, so the equipped starter item renders on the character.</param>
    /// <returns>True if all items were added successfully.</returns>
    public static bool ApplyStarterPack(Inventory inventory, string packId = "default", Data.PlayerGear? gear = null)
    {
        var (toolItem, torchItem, foodItem) = GetStarterPack(packId);

        bool success = true;
        success = success && inventory.AddItem(toolItem, 1);
        success = success && inventory.AddItem(torchItem, 1);
        success = success && inventory.AddItem(foodItem, 1);

        // Auto-equip the tool (and the torch when it differs) — both the
        // inventory flag and the PlayerGear slot. The tool syncs last so it
        // wins the shared weapon slot (e.g. the forester's axe over the torch).
        if (!string.IsNullOrEmpty(toolItem))
            inventory.EquipItem(toolItem);
        if (!string.IsNullOrEmpty(torchItem) && torchItem != toolItem)
            EquipIntoGear(gear, torchItem);
        EquipIntoGear(gear, toolItem);

        return success;
    }

    private static void EquipIntoGear(Data.PlayerGear? gear, string itemId)
    {
        if (gear == null || string.IsNullOrEmpty(itemId)) return;
        if (GearItem.LoadAll().TryGetValue(itemId, out var item))
            gear.Equip(item);
    }
}