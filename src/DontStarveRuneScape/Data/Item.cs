namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Item definition.
/// </summary>
public sealed class ItemDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("stack_size")]
    public int StackSize { get; init; } = 1;

    [JsonPropertyName("is_equippable")]
    public bool IsEquippable { get; init; }

    [JsonPropertyName("is_essential_tool")]
    public bool IsEssentialTool { get; init; }

    [JsonPropertyName("is_food")]
    public bool IsFood { get; init; }

    [JsonPropertyName("is_raw")]
    public bool IsRaw { get; init; }

    [JsonPropertyName("spoilage_seconds")]
    public float SpoilageSeconds { get; init; }

    [JsonPropertyName("hunger_restore")]
    public int HungerRestore { get; init; }

    [JsonPropertyName("hp_restore")]
    public int HpRestore { get; init; }

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("tool_type")]
    public string? ToolType { get; init; }

    [JsonPropertyName("durability")]
    public int Durability { get; init; }

    [JsonPropertyName("is_structure")]
    public bool IsStructure { get; init; }

    [JsonPropertyName("is_currency")]
    public bool IsCurrency { get; init; }

    [JsonPropertyName("cooking_base_item")]
    public string? CookingBaseItem { get; init; }
}

/// <summary>
/// Registry of all items.
/// </summary>
public sealed class ItemRegistry
{
    public Dictionary<string, ItemDef> Items { get; } = [];

    public ItemRegistry() { }

    public ItemRegistry(IEnumerable<ItemDef> items)
    {
        foreach (var item in items)
            Items[item.Id] = item;
    }

    public ItemDef? GetItem(string id)
    {
        return Items.TryGetValue(id, out var item) ? item : null;
    }

    public int GetStackSize(string id)
    {
        return GetItem(id)?.StackSize ?? 1;
    }
}