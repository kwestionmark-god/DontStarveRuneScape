namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Faction definition.
/// </summary>
public sealed class FactionDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("leader_npc")]
    public string LeaderNpc { get; init; } = string.Empty;

    [JsonPropertyName("territory_biomes")]
    public string[] TerritoryBiomes { get; init; } = [];

    [JsonPropertyName("hostility")]
    public float Hostility { get; init; } = 0f; // 0 = peaceful, 1 = hostile

    [JsonPropertyName("hostile_monster_types")]
    public string[] HostileMonsterTypes { get; init; } = [];

    [JsonPropertyName("color")]
    public int[] Color { get; init; } = [255, 255, 255]; // RGB for HUD

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Registry of factions.
/// </summary>
public sealed class FactionRegistry
{
    public Dictionary<string, FactionDef> Factions { get; } = [];

    public FactionRegistry() { }

    public FactionRegistry(IEnumerable<FactionDef> factions)
    {
        foreach (var f in factions)
            Factions[f.Id] = f;
    }

    public FactionDef? GetFaction(string id)
    {
        return Factions.TryGetValue(id, out var f) ? f : null;
    }

    public (byte R, byte G, byte B) GetFactionColor(string id)
    {
        return GetFaction(id) is { Color.Length: >= 3 } f
            ? ((byte)f.Color[0], (byte)f.Color[1], (byte)f.Color[2])
            : (255, 255, 255);
    }

    public IEnumerable<FactionDef> GetFactionsForBiome(string biomeId)
    {
        return Factions.Values.Where(f => f.TerritoryBiomes.Contains(biomeId));
    }
}