namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Structure definition for building system.
/// </summary>
public sealed class StructureDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty; // "storage", "crafting", "defense", "utility", "decoration"

    [JsonPropertyName("sub_stat")]
    public string SubStat { get; init; } = string.Empty; // Construction sub-stat required

    [JsonPropertyName("level_req")]
    public int LevelReq { get; init; } = 1;

    [JsonPropertyName("materials")]
    public StructureMaterial[] Materials { get; init; } = [];

    [JsonPropertyName("biome_compatibility")]
    public string[] BiomeCompatibility { get; init; } = [];

    [JsonPropertyName("occupies_tile")]
    public bool OccupiesTile { get; init; } = true;

    [JsonPropertyName("hp")]
    public float Hp { get; init; } = 100f;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("is_offensive")]
    public bool IsOffensive { get; init; }

    [JsonPropertyName("attack_range")]
    public float AttackRange { get; init; }

    [JsonPropertyName("attack_damage")]
    public float AttackDamage { get; init; }

    [JsonPropertyName("attack_cooldown")]
    public float AttackCooldown { get; init; }
}

/// <summary>
/// Material requirement for a structure.
/// </summary>
public sealed class StructureMaterial
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; } = 1;
}

/// <summary>
/// Registry of all structures.
/// </summary>
public sealed class StructureDefRegistry
{
    public Dictionary<string, StructureDef> Structures { get; } = [];

    public StructureDefRegistry() { }

    public StructureDefRegistry(IEnumerable<StructureDef> structures)
    {
        foreach (var s in structures)
            Structures[s.Id] = s;
    }

    public void LoadAll()
    {
        // Will be populated from JSON at runtime
    }

    public StructureDef? GetStructure(string id)
    {
        return Structures.TryGetValue(id, out var s) ? s : null;
    }

    public IEnumerable<StructureDef> GetStructuresForSubStat(string subStat)
    {
        return Structures.Values.Where(s => s.SubStat == subStat);
    }

    public IEnumerable<StructureDef> GetStructuresForBiome(string biomeId)
    {
        return Structures.Values.Where(s =>
            s.BiomeCompatibility.Length == 0 || s.BiomeCompatibility.Contains(biomeId));
    }
}