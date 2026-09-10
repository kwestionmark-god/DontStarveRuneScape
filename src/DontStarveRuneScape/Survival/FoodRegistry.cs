namespace DontStarveRuneScape.Survival;

using System.Collections.Generic;

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
            if (item.TryGetValue("id", out var idObj) && idObj is string id)
            {
                _itemsData[id] = item;
            }
        }

        foreach (var data in itemsData)
        {
            if (data.TryGetValue("is_food", out var isFoodObj) && isFoodObj is bool isFood && isFood)
            {
                string itemId = data.TryGetValue("id", out var id2) && id2 is string s2 ? s2 : "";
                float hungerRestore = data.TryGetValue("hunger_restore", out var hr) && hr is float f1 ? f1 : 0;
                float hpRestore = data.TryGetValue("hp_restore", out var hr2) && hr2 is float f2 ? f2 : 0;
                float spoilageSeconds = data.TryGetValue("spoilage_seconds", out var ss) && ss is float f3 ? f3 : 0;
                bool isRaw = data.TryGetValue("is_raw", out var ir) && ir is bool b1 && b1;
                string cookingBaseItem = data.TryGetValue("cooking_base_item", out var cb) && cb is string s3 ? s3 : "";

                if (!string.IsNullOrEmpty(itemId))
                {
                    _foods[itemId] = new FoodItem(itemId, hungerRestore, hpRestore, spoilageSeconds, isRaw, cookingBaseItem);
                }
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
            if (item.TryGetValue("stack_size", out var stackObj) && stackObj is int stack)
                return stack;
        }
        return 10;
    }

    /// <summary>Get sprite key for an item (from item data).</summary>
    public string GetSpriteKey(string itemId)
    {
        if (_itemsData.TryGetValue(itemId, out var item))
        {
            if (item.TryGetValue("sprite_key", out var spriteObj) && spriteObj is string sprite)
                return sprite;
        }
        return string.Empty;
    }
}