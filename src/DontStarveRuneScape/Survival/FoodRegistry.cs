namespace DontStarveRuneScape.Survival;

using System.Collections.Generic;
using DontStarveRuneScape.Data;

/// <summary>
/// FoodRegistry — Registry of all food items, loaded from JSON data.
/// Provides nutrition values and spoilage info for each food item.
/// </summary>
public sealed class FoodRegistry
{
    private readonly Dictionary<string, Dictionary<string, object>> _itemsData;
    private readonly Dictionary<string, FoodItem> _foods = new();

    /// <summary>
    /// Initialize from JSON data.
    /// </summary>
    /// <param name="itemsData">Parsed list of item dicts from items.json.</param>
    public FoodRegistry(List<Dictionary<string, object>> itemsData)
    {
        _itemsData = new Dictionary<string, Dictionary<string, object>>();
        foreach (var item in itemsData)
        {
            string? id = DataValues.GetString(item.TryGetValue("id", out var idObj) ? idObj : null);
            if (!string.IsNullOrEmpty(id))
            {
                _itemsData[id] = item;
            }
        }

        foreach (var data in itemsData)
        {
            bool isFood = DataValues.GetBool(data.TryGetValue("is_food", out var isFoodObj) ? isFoodObj : null);
            if (!isFood) continue;

            string itemId = DataValues.GetString(data.TryGetValue("id", out var id2) ? id2 : null) ?? "";
            float hungerRestore = DataValues.GetFloat(data.TryGetValue("hunger_restore", out var hr) ? hr : null);
            float hpRestore = DataValues.GetFloat(data.TryGetValue("hp_restore", out var hr2) ? hr2 : null);
            float spoilageSeconds = DataValues.GetFloat(data.TryGetValue("spoilage_seconds", out var ss) ? ss : null);
            bool isRaw = DataValues.GetBool(data.TryGetValue("is_raw", out var ir) ? ir : null);
            string cookingBaseItem = DataValues.GetString(data.TryGetValue("cooking_base_item", out var cb) ? cb : null) ?? "";

            if (!string.IsNullOrEmpty(itemId))
            {
                _foods[itemId] = new FoodItem(itemId, hungerRestore, hpRestore, spoilageSeconds, isRaw, cookingBaseItem);
            }
        }
    }

    /// <summary>Look up a food item by ID.</summary>
    public FoodItem? Get(string itemId)
    {
        _foods.TryGetValue(itemId, out var food);
        return food;
    }

    /// <summary>Return all registered food items.</summary>
    public List<FoodItem> All() => new(_foods.Values);

    /// <summary>Check if an item_id refers to raw food.</summary>
    public bool IsRaw(string itemId)
    {
        return _foods.TryGetValue(itemId, out var food) && food.IsRaw;
    }

    /// <summary>Get the raw food ID that this cooked item comes from.</summary>
    public string CookedFrom(string itemId)
    {
        return _foods.TryGetValue(itemId, out var food) ? food.CookingBaseItem : string.Empty;
    }

    /// <summary>Get max stack size for an item (from item data).</summary>
    public int GetStackSize(string itemId)
    {
        if (_itemsData.TryGetValue(itemId, out var item))
        {
            if (item.TryGetValue("stack_size", out var stackObj))
                return DataValues.GetInt(stackObj, 10);
        }
        return 10;
    }

    /// <summary>Get sprite key for an item (from item data).</summary>
    public string GetSpriteKey(string itemId)
    {
        if (_itemsData.TryGetValue(itemId, out var item))
        {
            if (item.TryGetValue("sprite_key", out var spriteObj))
                return DataValues.GetString(spriteObj) ?? string.Empty;
        }
        return string.Empty;
    }
}
