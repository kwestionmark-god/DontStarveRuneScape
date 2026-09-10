namespace DontStarveRuneScape.Survival;

using System.Collections.Generic;

/// <summary>
/// FoodItem — Definition of a food item (holds only food-specific data).
/// Loaded from items.json via FoodRegistry.
/// </summary>
public sealed class FoodItem
{
    /// <summary>Item ID from items.json.</summary>
    public string ItemId { get; }

    /// <summary>How much hunger it restores (0–100).</summary>
    public float HungerRestoration { get; }

    /// <summary>How much HP it restores (negative = damage from spoiled food).</summary>
    public float HpRestoration { get; }

    /// <summary>Seconds until the item spoils in inventory (0 = never spoils).</summary>
    public float SpoilageRate { get; }

    /// <summary>Whether the food is raw (affects cooking skill).</summary>
    public bool IsRaw { get; }

    /// <summary>ID of raw food this is cooked from (empty if not cooked).</summary>
    public string CookingBaseItem { get; }

    /// <summary>
    /// Create a FoodItem.
    /// </summary>
    public FoodItem(
        string itemId,
        float hungerRestoration,
        float hpRestoration,
        float spoilageRate,
        bool isRaw,
        string cookingBaseItem)
    {
        ItemId = itemId;
        HungerRestoration = hungerRestoration;
        HpRestoration = hpRestoration;
        SpoilageRate = spoilageRate;
        IsRaw = isRaw;
        CookingBaseItem = cookingBaseItem;
    }

    /// <summary>Get max stack size from item data.</summary>
    public int GetStackSize(Dictionary<string, Dictionary<string, object>> itemsData)
    {
        if (itemsData.TryGetValue(ItemId, out var item))
        {
            if (item.TryGetValue("stack_size", out var stackObj) && stackObj is int stack)
                return stack;
        }
        return 10;
    }

    /// <summary>Get sprite key from item data.</summary>
    public string GetSpriteKey(Dictionary<string, Dictionary<string, object>> itemsData)
    {
        if (itemsData.TryGetValue(ItemId, out var item))
        {
            if (item.TryGetValue("sprite_key", out var spriteObj) && spriteObj is string sprite)
                return sprite;
        }
        return string.Empty;
    }
}