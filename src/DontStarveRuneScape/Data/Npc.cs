namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// NPC definition.
/// </summary>
public sealed class NpcDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "merchant", "quest_giver", "faction_leader", "recruitable"

    [JsonPropertyName("faction")]
    public string Faction { get; init; } = string.Empty;

    [JsonPropertyName("dialogue")]
    public string[] Dialogue { get; init; } = [];

    [JsonPropertyName("behavior")]
    public string Behavior { get; init; } = "idle"; // "idle", "wander", "patrol", "guard"

    [JsonPropertyName("health")]
    public float Health { get; init; } = 100f;

    [JsonPropertyName("price_modifier")]
    public float PriceModifier { get; init; } = 1.0f;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("trade_items")]
    public string[] TradeItems { get; init; } = [];

    [JsonPropertyName("quest_ids")]
    public string[] QuestIds { get; init; } = [];

    [JsonPropertyName("recruitable")]
    public bool Recruitable { get; init; }

    [JsonPropertyName("recruit_cost")]
    public int RecruitCost { get; init; } = 0;

    [JsonPropertyName("recruit_requirements")]
    public RecruitRequirement[] RecruitRequirements { get; init; } = [];
}

/// <summary>
/// Requirement for recruiting an NPC.
/// </summary>
public sealed class RecruitRequirement
{
    [JsonPropertyName("skill")]
    public string Skill { get; init; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; init; } = 1;
}

/// <summary>
/// NPC spawn point definition.
/// </summary>
public sealed class NpcSpawnPoint
{
    [JsonPropertyName("npc_id")]
    public string NpcId { get; init; } = string.Empty;

    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }

    [JsonPropertyName("biome")]
    public string Biome { get; init; } = string.Empty;
}

/// <summary>
/// Registry of NPCs and spawn points.
/// </summary>
public sealed class NpcRegistry
{
    public Dictionary<string, NpcDef> Npcs { get; } = [];
    public List<NpcSpawnPoint> SpawnPoints { get; } = [];

    public NpcRegistry() { }

    public NpcDef? GetNpc(string id)
    {
        return Npcs.TryGetValue(id, out var npc) ? npc : null;
    }

    public IEnumerable<NpcSpawnPoint> GetSpawnPointsForBiome(string biomeId)
    {
        return SpawnPoints.Where(sp => sp.Biome == biomeId);
    }
}