namespace DontStarveRuneScape.Inventory;

using System.Text.Json;
using System.Collections.Generic;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Core;

/// <summary>
/// InventorySlot — A single slot in the inventory grid.
/// </summary>
public sealed class InventorySlot
{
    public string? ItemId { get; set; }
    public int Quantity { get; set; } = 0;
    public float SpoilageTimer { get; set; } = 0f;
    public float MaxSpoilageTime { get; set; } = 0f;
    public bool IsEquipped { get; set; } = false;
}

/// <summary>
/// Inventory — 20-slot grid inventory with spoilage tracking.
/// </summary>
public sealed class Inventory
{
    private const int SlotCount = 20;
    public List<InventorySlot> Slots { get; } = new(SlotCount);

    public Inventory()
    {
        for (int i = 0; i < SlotCount; i++)
            Slots.Add(new InventorySlot());
    }

    /// <summary>Add an item to the inventory.</summary>
    public bool AddItem(string itemId, int quantity, float? spoilageSeconds = null)
    {
        if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            return false;

        // Try to stack first
        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId && slot.Quantity < GetStackSize(itemId))
            {
                int canAdd = GetStackSize(itemId) - slot.Quantity;
                int toAdd = Math.Min(canAdd, quantity);
                slot.Quantity += toAdd;
                quantity -= toAdd;
                if (quantity == 0) return true;
            }
        }

        // Find empty slot
        foreach (var slot in Slots)
        {
            if (slot.ItemId == null)
            {
                slot.ItemId = itemId;
                int toAdd = Math.Min(GetStackSize(itemId), quantity);
                slot.Quantity = toAdd;
                quantity -= toAdd;
                if (spoilageSeconds.HasValue)
                {
                    slot.MaxSpoilageTime = spoilageSeconds.Value;
                    slot.SpoilageTimer = 0f;
                }
                if (quantity == 0) return true;
            }
        }

        return quantity == 0;
    }

    /// <summary>Check if inventory can add an item.</summary>
    public bool CanAdd(string itemId, int quantity)
    {
        if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            return false;

        int stackSize = GetStackSize(itemId);
        int availableSpace = 0;

        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId)
            {
                availableSpace += stackSize - slot.Quantity;
            }
            else if (slot.ItemId == null)
            {
                availableSpace += stackSize;
            }
        }

        return availableSpace >= quantity;
    }

    /// <summary>Remove an item from the inventory.</summary>
    public bool RemoveItem(string itemId, int quantity)
    {
        if (string.IsNullOrEmpty(itemId) || quantity <= 0)
            return false;

        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId)
            {
                if (slot.Quantity >= quantity)
                {
                    slot.Quantity -= quantity;
                    if (slot.Quantity == 0)
                    {
                        slot.ItemId = null;
                        slot.SpoilageTimer = 0f;
                        slot.MaxSpoilageTime = 0f;
                    }
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Get quantity of an item in inventory.</summary>
    public int GetItemQuantity(string itemId)
    {
        int total = 0;
        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId)
                total += slot.Quantity;
        }
        return total;
    }

    /// <summary>Equip an item (mark as equipped).</summary>
    public void EquipItem(string itemId)
    {
        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId)
            {
                // Unequip other items of same type
                foreach (var otherSlot in Slots)
                {
                    if (otherSlot.IsEquipped && otherSlot.ItemId?.EndsWith("_" + GetToolType(itemId)) == true)
                        otherSlot.IsEquipped = false;
                }
                slot.IsEquipped = true;
                break;
            }
        }
    }

    /// <summary>Unequip every stack of an item (clears the equipped flag).</summary>
    public void UnequipItem(string itemId)
    {
        foreach (var slot in Slots)
        {
            if (slot.ItemId == itemId)
                slot.IsEquipped = false;
        }
    }

    private string GetToolType(string itemId)
    {
        if (itemId.EndsWith("_axe")) return "axe";
        if (itemId.EndsWith("_pickaxe")) return "pickaxe";
        return itemId;
    }

    /// <summary>Per-item stack-size overrides from item data (id -> stack size);
    /// GetStackSize falls back to the built-in defaults for unknown ids.</summary>
    public Dictionary<string, int>? StackSizes { get; set; }

    /// <summary>Stack size for an item: data override first, then built-in defaults.</summary>
    public int StackSizeOf(string itemId) => GetStackSize(itemId);

    /// <summary>Build stack-size overrides from raw items data (id/stack_size rows).
    /// Values may be plain primitives or JsonElement (DataLoader deserializes
    /// Dictionary<string, object> values as JsonElement); invalid rows are skipped.</summary>
    public static Dictionary<string, int> StackSizesFromData(List<Dictionary<string, object>> itemsData)
    {
        var sizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in itemsData)
        {
            string? id = entry.TryGetValue("id", out var idObj) ? idObj switch
            {
                string s => s,
                JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
                _ => null,
            } : null;
            if (string.IsNullOrEmpty(id)) continue;

            int size = entry.TryGetValue("stack_size", out var sizeObj) ? sizeObj switch
            {
                JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
                int i => i,
                _ => -1,
            } : -1;
            if (size > 0) sizes[id] = size;
        }
        return sizes;
    }

    private int GetStackSize(string itemId)
    {
        // Data override (items.json stack_size) first; built-in defaults fallback.
        if (StackSizes != null && StackSizes.TryGetValue(itemId, out int fromData))
            return fromData;

        return itemId switch
        {
            var id when id.EndsWith("_logs") => 20,
            var id when id.EndsWith("_ore") => 20,
            var id when id.EndsWith("_bars") => 20,
            "berries" => 20,
            "raw_meat" => 10,
            "raw_fish" => 10,
            "charcoal" => 20,
            "log" => 20,
            "oak_logs" => 20,
            _ => 10,
        };
    }

    /// <summary>Tick inventory for spoilage.</summary>
    public List<string> Tick(float dt)
    {
        var messages = new List<string>();
        foreach (var slot in Slots)
        {
            if (slot.ItemId != null && slot.MaxSpoilageTime > 0)
            {
                slot.SpoilageTimer += dt;
                if (slot.SpoilageTimer >= slot.MaxSpoilageTime)
                {
                    // Item spoiled - convert to spoiled version
                    string spoiledId = slot.ItemId + "_spoiled";
                    messages.Add($"{slot.ItemId} has spoiled!");
                    slot.ItemId = spoiledId;
                    slot.MaxSpoilageTime = 0f;
                    slot.SpoilageTimer = 0f;
                }
            }
        }
        return messages;
    }

    /// <summary>Get snapshot for saving.</summary>
    public InventorySnapshot GetSnapshot()
    {
        var snapshot = new InventorySnapshot();
        snapshot.Slots = new InventorySlotSnapshot[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = Slots[i];
            snapshot.Slots[i] = new InventorySlotSnapshot
            {
                ItemId = slot.ItemId ?? string.Empty,
                Quantity = slot.Quantity,
                SpoilageRemaining = slot.MaxSpoilageTime > 0 ? Math.Max(0, slot.MaxSpoilageTime - slot.SpoilageTimer) : 0f,
                IsEquipped = slot.IsEquipped
            };
        }
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(InventorySnapshot snapshot)
    {
        for (int i = 0; i < SlotCount && i < snapshot.Slots.Length; i++)
        {
            var slotData = snapshot.Slots[i];
            var slot = Slots[i];
            slot.ItemId = string.IsNullOrEmpty(slotData.ItemId) ? null : slotData.ItemId;
            slot.Quantity = slotData.Quantity;
            slot.SpoilageTimer = 0f;
            slot.MaxSpoilageTime = slotData.SpoilageRemaining;
            slot.IsEquipped = slotData.IsEquipped;
        }
    }
}