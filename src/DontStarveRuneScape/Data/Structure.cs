namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// Structure definition for the building system, parsed from the actual
/// structures.json shape: a doubly nested envelope (construction sub-stat
/// group -> structure name -> fields) with material item/quantity pairs.
/// </summary>
public sealed class StructureDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>"portable", "fixed", etc. (data key: structure_type).</summary>
    public string StructureType { get; init; } = string.Empty;

    /// <summary>Construction sub-stat category (data key: construction_sub_stat).</summary>
    public string SubStat { get; init; } = string.Empty;

    public int RequiresStructureLevel { get; init; } = 1;
    public int RequiresSkillLevel { get; init; } = 1;

    public StructureMaterial[] Materials { get; init; } = [];

    /// <summary>Biome ids this structure can be built on; empty = anywhere.</summary>
    public string[] BiomeCompatibility { get; init; } = [];

    public bool OccupiesTile { get; init; } = true;
    public bool Burnable { get; init; }

    public float Hp { get; init; } = 100f;

    /// <summary>Offensive structures only (data key: damage).</summary>
    public float Damage { get; init; }

    public string SpriteKey { get; init; } = string.Empty;
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
/// Registry of all structures, loaded from Data/structures.json.
/// </summary>
public sealed class StructureDefRegistry
{
    public Dictionary<string, StructureDef> Structures { get; } = [];

    public StructureDefRegistry() { }

    public void LoadAll()
    {
        try
        {
            var root = DataLoader.LoadJson(Constants.StructuresFile);
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("structures", out var groups))
            {
                foreach (var group in groups.EnumerateObject())
                {
                    if (group.Value.ValueKind != JsonValueKind.Object) continue;
                    foreach (var entry in group.Value.EnumerateObject())
                    {
                        if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                        var def = ParseStructure(entry.Value, group.Name, entry.Name);
                        if (def != null) Structures[def.Id] = def;
                    }
                }
            }
        }
        catch { /* missing file: empty registry, panels fall back to ids */ }
    }

    public StructureDef? GetStructure(string id) =>
        Structures.TryGetValue(id, out var s) ? s : null;

    public IEnumerable<StructureDef> GetStructuresForSubStat(string subStat) =>
        Structures.Values.Where(s => s.SubStat == subStat);

    public IEnumerable<StructureDef> GetStructuresForBiome(string biomeId) =>
        Structures.Values.Where(s =>
            s.BiomeCompatibility.Length == 0 || s.BiomeCompatibility.Contains(biomeId));

    private static StructureDef? ParseStructure(JsonElement e, string group, string entryName)
    {
        // The data keys by structure name and repeats the id as structure_id.
        string? id = e.TryGetProperty("structure_id", out var idProp) &&
            idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : null;
        if (string.IsNullOrEmpty(id)) id = entryName;

        float hp = e.TryGetProperty("hp", out var hpProp) && hpProp.ValueKind == JsonValueKind.Number
            ? hpProp.GetSingle() : 0f;

        return new StructureDef
        {
            Id = id,
            Name = GetStr(e, "name") ?? id,
            StructureType = GetStr(e, "structure_type") ?? string.Empty,
            SubStat = GetStr(e, "construction_sub_stat") ?? group,
            RequiresStructureLevel = GetInt(e, "requires_structure_level") ?? 1,
            RequiresSkillLevel = GetInt(e, "requires_skill_level") ?? 1,
            Materials = ParseMaterials(e),
            BiomeCompatibility = ParseStrings(e, "biome_compatible"),
            OccupiesTile = GetBool(e, "occupies_tile"),
            Burnable = GetBool(e, "burnable"),
            Hp = hp > 0f ? hp : 100f,
            Damage = GetFloatOrPrefix(e, "damage"),
            SpriteKey = GetStr(e, "sprite_key") ?? id,
        };
    }

    // materials shape: [["stick", 2], ...] — item/quantity pairs.
    private static StructureMaterial[] ParseMaterials(JsonElement e)
    {
        if (!e.TryGetProperty("materials", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var materials = new List<StructureMaterial>();
        foreach (var pair in arr.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array) continue;
            string? itemId = null;
            int quantity = 0;
            int index = 0;
            foreach (var value in pair.EnumerateArray())
            {
                if (index == 0 && value.ValueKind == JsonValueKind.String)
                    itemId = value.GetString();
                else if (index == 1 && value.ValueKind == JsonValueKind.Number)
                    quantity = value.GetInt32();
                index++;
            }
            if (!string.IsNullOrEmpty(itemId) && quantity > 0)
                materials.Add(new StructureMaterial { ItemId = itemId, Quantity = quantity });
        }
        return materials.ToArray();
    }

    private static string[] ParseStrings(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        var values = new List<string>();
        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                values.Add(s);
        }
        return values.ToArray();
    }

    private static string? GetStr(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static float GetFloat(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : 0f;

    // damage may be a formula string like "5 + offensive_stat × 2"; the numeric
    // prefix is the flat base (stat scaling is runtime behavior, not data).
    private static float GetFloatOrPrefix(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v)) return 0f;
        if (v.ValueKind == JsonValueKind.Number) return v.GetSingle();
        if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
        {
            int end = 0;
            while (end < s.Length && (char.IsAsciiDigit(s[end]) || s[end] == '.' || (end == 0 && s[end] == '-')))
                end++;
            if (end > 0 && float.TryParse(s[..end], System.Globalization.CultureInfo.InvariantCulture, out var f))
                return f;
        }
        return 0f;
    }

    private static int? GetInt(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
