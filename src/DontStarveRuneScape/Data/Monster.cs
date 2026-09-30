namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// Monster definition per biome. Shape matches monsters.json:
/// monsters.json nests `monsters: { biome: { monster_id: {...} } }`.
/// </summary>
public sealed class MonsterDef : DataRecord
{
    [JsonPropertyName("monster_id")]
    public string MonsterId { get; init; } = string.Empty;

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

    [JsonPropertyName("aggression_range")]
    public float AggressionRange { get; init; } = 150f;

    [JsonPropertyName("flee_range")]
    public float FleeRange { get; init; } = 300f;

    [JsonPropertyName("attack_cooldown")]
    public float AttackCooldown { get; init; } = 1.5f;

    [JsonPropertyName("xp_reward")]
    public int XpReward { get; init; } = 10;

    [JsonPropertyName("is_hostile")]
    public bool IsHostile { get; init; } = true;

    [JsonPropertyName("tamable")]
    public bool Tamable { get; init; } = false;

    [JsonPropertyName("faction_owned")]
    public bool FactionOwned { get; init; } = false;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("loot_table")]
    public MonsterLootEntry[] LootTable { get; init; } = [];
}

/// <summary>
/// Monster loot entry.
/// </summary>
public sealed class MonsterLootEntry
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("chance")]
    public float Chance { get; init; } = 1.0f;
}

/// <summary>
/// Registry of monsters by biome, loaded from monsters.json.
/// </summary>
public sealed class MonsterRegistry
{
    public Dictionary<string, Dictionary<string, MonsterDef>> MonstersByBiome { get; } = [];

    public MonsterRegistry() { }

    public MonsterRegistry(Dictionary<string, Dictionary<string, MonsterDef>> data)
    {
        MonstersByBiome = data;
    }

    /// <summary>Load the doubly-nested envelope: monsters → biome → monster id → def.</summary>
    public void LoadAll()
    {
        try
        {
            var root = DataLoader.LoadJson(Constants.MonstersFile);
            if (!root.TryGetProperty("monsters", out var biomes)) return;
            foreach (var biome in biomes.EnumerateObject())
            {
                var defs = new Dictionary<string, MonsterDef>();
                foreach (var entry in biome.Value.EnumerateObject())
                {
                    var def = entry.Value.Deserialize<MonsterDef>();
                    if (def != null && !string.IsNullOrEmpty(def.MonsterId))
                        defs[def.MonsterId] = def;
                }
                if (defs.Count > 0)
                    MonstersByBiome[biome.Name] = defs;
            }
        }
        catch { /* missing file: empty registry */ }
    }

    public Dictionary<string, MonsterDef>? GetBiomeMonsters(string biomeId)
    {
        return MonstersByBiome.TryGetValue(biomeId, out var dict) ? dict : null;
    }

    public MonsterDef? GetMonster(string biomeId, string monsterId)
    {
        return GetBiomeMonsters(biomeId)?.TryGetValue(monsterId, out var m) == true ? m : null;
    }

    /// <summary>Look a monster up across all biomes (ids are unique in practice).</summary>
    public MonsterDef? GetMonster(string monsterId)
    {
        foreach (var defs in MonstersByBiome.Values)
        {
            if (defs.TryGetValue(monsterId, out var m))
                return m;
        }
        return null;
    }

    /// <summary>The biome id a monster id lives in (empty when unknown).</summary>
    public string BiomeOf(string monsterId)
    {
        foreach (var (biomeId, defs) in MonstersByBiome)
        {
            if (defs.ContainsKey(monsterId))
                return biomeId;
        }
        return string.Empty;
    }
}
