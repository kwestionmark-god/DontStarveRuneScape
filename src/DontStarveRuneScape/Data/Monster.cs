namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Monster definition per biome.
/// </summary>
public sealed class MonsterDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("hp")]
    public float Hp { get; init; } = 10f;

    [JsonPropertyName("attack")]
    public float Attack { get; init; } = 5f;

    [JsonPropertyName("defence")]
    public float Defence { get; init; } = 0f;

    [JsonPropertyName("speed")]
    public float Speed { get; init; } = 50f;

    [JsonPropertyName("attack_range")]
    public float AttackRange { get; init; } = 1f;

    [JsonPropertyName("aggro_range")]
    public float AggroRange { get; init; } = 5f;

    [JsonPropertyName("loot_table")]
    public MonsterLootEntry[] LootTable { get; init; } = [];

    [JsonPropertyName("behavior_tags")]
    public string[] BehaviorTags { get; init; } = [];

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("xp_reward")]
    public int XpReward { get; init; } = 10;

    [JsonPropertyName("combat_level")]
    public int CombatLevel { get; init; } = 1;
}

/// <summary>
/// Monster loot entry.
/// </summary>
public sealed class MonsterLootEntry
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("min_qty")]
    public int MinQty { get; init; } = 1;

    [JsonPropertyName("max_qty")]
    public int MaxQty { get; init; } = 1;

    [JsonPropertyName("chance")]
    public float Chance { get; init; } = 1.0f;
}

/// <summary>
/// Registry of monsters by biome.
/// </summary>
public sealed class MonsterRegistry
{
    public Dictionary<string, Dictionary<string, MonsterDef>> MonstersByBiome { get; } = [];

    public MonsterRegistry() { }

    public MonsterRegistry(Dictionary<string, Dictionary<string, MonsterDef>> data)
    {
        MonstersByBiome = data;
    }

    public Dictionary<string, MonsterDef>? GetBiomeMonsters(string biomeId)
    {
        return MonstersByBiome.TryGetValue(biomeId, out var dict) ? dict : null;
    }

    public MonsterDef? GetMonster(string biomeId, string monsterId)
    {
        return GetBiomeMonsters(biomeId)?.TryGetValue(monsterId, out var m) == true ? m : null;
    }
}