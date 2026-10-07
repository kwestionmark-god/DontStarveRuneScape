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
/// CharacterBackground — One selectable creation-time background.
/// </summary>
public sealed class CharacterBackground
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PackId { get; set; } = string.Empty;
    public string Hint { get; set; } = string.Empty;
}

/// <summary>
/// Backgrounds — The creation-time background catalog (character
/// backgrounds slice). Each maps to an existing starter pack: the
/// choice is playstyle, not power — one tool, one torch, one food.
/// </summary>
public static class Backgrounds
{
    public sealed record Entry(string Id, string Name, string PackId, string Hint);

    public static readonly Entry Wanderer =
        new("wanderer", "Wanderer", "default", "A blank slate. Torch, berries, fish.");

    /// <summary>All backgrounds in display order.</summary>
    public static readonly Entry[] All =
    [
        Wanderer,
        new("forester", "Forester", "forester", "Starts with an axe — woodcutting first."),
        new("prospector", "Prospector", "prospector", "Starts with a pickaxe — mining first."),
        new("scavenger", "Scavenger", "scavenger", "Extra food — foraging first."),
    ];

    /// <summary>Resolve by id with a Wanderer fallback (old saves, corrupt
    /// data, typos). Never null.</summary>
    public static Entry ById(string? id) =>
        All.FirstOrDefault(b => b.Id == id) ?? Wanderer;
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