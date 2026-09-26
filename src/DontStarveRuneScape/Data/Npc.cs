namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// NPC definition. Field names follow npcs.json (npc_id / npc_type /
/// dialogue_lines / available_quest_ids ...).
/// </summary>
public sealed class NpcDef : DataRecord
{
    [JsonPropertyName("npc_id")]
    public string NpcId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("npc_type")]
    public string Type { get; init; } = string.Empty; // "merchant", "quest_giver", "faction_leader", "recruit"

    [JsonPropertyName("world_x")]
    public int WorldX { get; init; } // Tile coordinates

    [JsonPropertyName("world_y")]
    public int WorldY { get; init; }

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("faction")]
    public string Faction { get; init; } = string.Empty;

    [JsonPropertyName("dialogue_lines")]
    public string[] DialogueLines { get; init; } = [];

    [JsonPropertyName("behavior")]
    public string Behavior { get; init; } = "idle";

    [JsonPropertyName("hostility_level")]
    public float HostilityLevel { get; init; } = 0f;

    [JsonPropertyName("health")]
    public int Health { get; init; } = 100;

    [JsonPropertyName("max_health")]
    public int MaxHealth { get; init; } = 100;

    [JsonPropertyName("starting_gold")]
    public int StartingGold { get; init; } = 0;

    [JsonPropertyName("price_modifier")]
    public float PriceModifier { get; init; } = 1.0f;

    [JsonPropertyName("commerce_requirement")]
    public int CommerceRequirement { get; init; } = 0;

    [JsonPropertyName("available_quest_ids")]
    public string[] AvailableQuestIds { get; init; } = [];

    [JsonPropertyName("recruit_commerce_requirement")]
    public int RecruitCommerceRequirement { get; init; } = 0;

    [JsonPropertyName("recruit_persuasion_requirement")]
    public int RecruitPersuasionRequirement { get; init; } = 0;

    [JsonPropertyName("recruit_composite_stat")]
    public int RecruitCompositeStat { get; init; } = 0;

    [JsonPropertyName("available_behaviors")]
    public string[] AvailableBehaviors { get; init; } = [];
}

/// <summary>
/// NPC spawn point definition (npcs.json spawn_points array).
/// </summary>
public sealed class NpcSpawnPoint : DataRecord
{
    [JsonPropertyName("spawn_id")]
    public string SpawnId { get; init; } = string.Empty;

    [JsonPropertyName("tile_x")]
    public int TileX { get; init; }

    [JsonPropertyName("tile_y")]
    public int TileY { get; init; }

    [JsonPropertyName("npc_types")]
    public string[] NpcTypes { get; init; } = [];

    [JsonPropertyName("faction")]
    public string? Faction { get; init; }

    [JsonPropertyName("biome")]
    public string Biome { get; init; } = string.Empty;

    [JsonPropertyName("is_safe_zone")]
    public bool IsSafeZone { get; init; }

    [JsonPropertyName("min_distance_from_player")]
    public int MinDistanceFromPlayer { get; init; } = 0;
}

/// <summary>
/// Registry of NPCs and spawn points, loaded from npcs.json.
/// </summary>
public sealed class NpcRegistry
{
    public Dictionary<string, NpcDef> Npcs { get; } = [];
    public List<NpcSpawnPoint> SpawnPoints { get; } = [];

    public void LoadAll()
    {
        try
        {
            var npcs = DataLoader.LoadJsonList<NpcDef>(Constants.NpcsFile, "npcs");
            foreach (var npc in npcs)
            {
                if (!string.IsNullOrEmpty(npc.NpcId))
                    Npcs[npc.NpcId] = npc;
            }
        }
        catch { /* missing file: empty registry */ }

        try
        {
            SpawnPoints.AddRange(DataLoader.LoadJsonList<NpcSpawnPoint>(Constants.NpcsFile, "spawn_points"));
        }
        catch { /* missing file: no spawn points */ }
    }

    public NpcDef? GetNpc(string id)
    {
        return Npcs.TryGetValue(id, out var npc) ? npc : null;
    }

    public IEnumerable<NpcDef> GetNpcsOfType(string type) =>
        Npcs.Values.Where(n => n.Type == type);

    public IEnumerable<NpcSpawnPoint> GetSpawnPointsForBiome(string biomeId)
    {
        return SpawnPoints.Where(sp => sp.Biome == biomeId);
    }
}
