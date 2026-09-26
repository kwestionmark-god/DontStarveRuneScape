namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// Quest definition (quests.json). Field names follow the data (quest_id /
/// giver_npc_type / conditions[].condition_type / item reward pairs ...).
/// </summary>
public sealed class QuestDef : DataRecord
{
    [JsonPropertyName("quest_id")]
    public string QuestId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("giver_npc_type")]
    public string GiverNpcType { get; init; } = string.Empty; // "quest_giver", "faction_leader"

    [JsonPropertyName("giver_faction")]
    public string GiverFaction { get; init; } = string.Empty;

    [JsonPropertyName("min_commerce")]
    public int MinCommerce { get; init; } = 0; // Intelligence commerce sub-stat

    [JsonPropertyName("min_persuasion")]
    public int MinPersuasion { get; init; } = 0; // Intelligence persuasion sub-stat

    [JsonPropertyName("min_total_intelligence")]
    public int MinTotalIntelligence { get; init; } = 0; // Intelligence level

    [JsonPropertyName("acceptance_threshold")]
    public float AcceptanceThreshold { get; init; } = 0f; // Faction standing required (0–1)

    [JsonPropertyName("required_faction_status")]
    public string? RequiredFactionStatus { get; init; } // Display name of the standing tier

    [JsonPropertyName("prerequisite_quests")]
    public string[] PrerequisiteQuests { get; init; } = [];

    [JsonPropertyName("xp_reward")]
    public float XpReward { get; init; } = 0f; // Flat intelligence XP

    [JsonPropertyName("skill_xp_rewards")]
    public Dictionary<string, float> SkillXpRewards { get; init; } = []; // Per-skill XP

    [JsonPropertyName("item_rewards")]
    [JsonConverter(typeof(ItemRewardPairListConverter))]
    public List<(string ItemId, int Quantity)> ItemRewards { get; init; } = [];

    [JsonPropertyName("recipe_unlocks")]
    public string[] RecipeUnlocks { get; init; } = [];

    [JsonPropertyName("gear_unlocks")]
    public string[] GearUnlocks { get; init; } = [];

    [JsonPropertyName("conditions")]
    public QuestCondition[] Conditions { get; init; } = [];

    [JsonPropertyName("fail_conditions")]
    public QuestCondition[] FailConditions { get; init; } = [];

    [JsonPropertyName("repeatable")]
    public bool IsRepeatable { get; init; } = false;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;
}

/// <summary>
/// Quest condition (objective). Types seen in the data: collect_item,
/// deliver_item, craft_item, kill_monster, visit_location, negotiate_faction,
/// trade_at_location.
/// </summary>
public sealed class QuestCondition
{
    [JsonPropertyName("condition_type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty; // item_id, monster_id, biome, npc_id, faction_id

    [JsonPropertyName("required_count")]
    public int RequiredCount { get; init; } = 1;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Deserializes item reward pairs like ["healing_herbs", 2] into
/// (item_id, quantity); tolerates malformed entries.
/// </summary>
public sealed class ItemRewardPairListConverter : JsonConverter<List<(string ItemId, int Quantity)>>
{
    public override List<(string ItemId, int Quantity)> Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<(string, int)>();
        using var doc = JsonDocument.ParseValue(ref reader);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

        foreach (var pair in doc.RootElement.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array) continue;
            string? itemId = null;
            int quantity = 1;
            int index = 0;
            foreach (var element in pair.EnumerateArray())
            {
                if (index == 0 && element.ValueKind == JsonValueKind.String)
                    itemId = element.GetString();
                else if (index == 1 && element.ValueKind == JsonValueKind.Number)
                    quantity = element.GetInt32();
                index++;
            }
            if (!string.IsNullOrEmpty(itemId))
                list.Add((itemId, quantity));
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<(string ItemId, int Quantity)> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var (itemId, quantity) in value)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(itemId);
            writer.WriteNumberValue(quantity);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// Registry of quests, loaded from quests.json.
/// </summary>
public sealed class QuestRegistry
{
    public Dictionary<string, QuestDef> Quests { get; } = [];

    public void LoadAll()
    {
        try
        {
            var quests = DataLoader.LoadJsonList<QuestDef>(Constants.QuestsFile, "quests");
            foreach (var quest in quests)
            {
                if (!string.IsNullOrEmpty(quest.QuestId))
                    Quests[quest.QuestId] = quest;
            }
        }
        catch { /* missing file: empty registry */ }
    }

    public QuestDef? GetQuest(string id)
    {
        return Quests.TryGetValue(id, out var q) ? q : null;
    }

    public IEnumerable<QuestDef> GetQuestsForFaction(string factionId)
    {
        return Quests.Values.Where(q => q.GiverFaction == factionId);
    }
}
