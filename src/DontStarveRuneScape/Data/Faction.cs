namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// Faction definition (factions.json). No color field in the data; HUD colors
/// fall back to white via GetFactionColor.
/// </summary>
public sealed class FactionDef : DataRecord
{
    [JsonPropertyName("faction_id")]
    public string FactionId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("leader_npc_id")]
    public string LeaderNpcId { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("territory_biomes")]
    public string[] TerritoryBiomes { get; init; } = [];

    [JsonPropertyName("base_hostility")]
    public float BaseHostility { get; init; } = 0f; // 0 = peaceful, 1 = hostile

    [JsonPropertyName("hostile_monster_types")]
    public string[] HostileMonsterTypes { get; init; } = [];

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;
}

/// <summary>
/// Registry of factions, loaded from factions.json.
/// </summary>
public sealed class FactionRegistry
{
    public Dictionary<string, FactionDef> Factions { get; } = [];

    public void LoadAll()
    {
        try
        {
            var factions = DataLoader.LoadJsonList<FactionDef>(Constants.FactionsFile, "factions");
            foreach (var faction in factions)
            {
                if (!string.IsNullOrEmpty(faction.FactionId))
                    Factions[faction.FactionId] = faction;
            }
        }
        catch { /* missing file: empty registry */ }
    }

    public FactionDef? GetFaction(string id)
    {
        return Factions.TryGetValue(id, out var f) ? f : null;
    }

    public (byte R, byte G, byte B) GetFactionColor(string id)
    {
        return ((byte)255, (byte)255, (byte)255);
    }

    public IEnumerable<FactionDef> GetFactionsForBiome(string biomeId)
    {
        return Factions.Values.Where(f => f.TerritoryBiomes.Contains(biomeId));
    }
}
