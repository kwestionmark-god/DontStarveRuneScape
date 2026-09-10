namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Quest definition.
/// </summary>
public sealed class QuestDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("giver_faction")]
    public string GiverFaction { get; init; } = string.Empty;

    [JsonPropertyName("required_faction_standing")]
    public int RequiredFactionStanding { get; init; } = 0;

    [JsonPropertyName("conditions")]
    public QuestCondition[] Conditions { get; init; } = [];

    [JsonPropertyName("fail_conditions")]
    public QuestCondition[] FailConditions { get; init; } = [];

    [JsonPropertyName("rewards")]
    public QuestReward[] Rewards { get; init; } = [];

    [JsonPropertyName("is_repeatable")]
    public bool IsRepeatable { get; init; } = false;
}

/// <summary>
/// Quest condition (objective).
/// </summary>
public sealed class QuestCondition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "kill", "collect", "deliver", "craft", "reach_level", "faction_attack"

    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty; // monster_id, item_id, npc_id, skill_id, faction_id

    [JsonPropertyName("count")]
    public int Count { get; init; } = 1;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Quest reward.
/// </summary>
public sealed class QuestReward
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty; // "item", "xp", "gold", "faction_standing", "recipe", "stat_point"

    [JsonPropertyName("target")]
    public string Target { get; init; } = string.Empty; // item_id, skill_id, faction_id

    [JsonPropertyName("amount")]
    public int Amount { get; init; } = 1;
}

/// <summary>
/// Registry of quests.
/// </summary>
public sealed class QuestRegistry
{
    public Dictionary<string, QuestDef> Quests { get; } = [];

    public QuestRegistry() { }

    public QuestRegistry(IEnumerable<QuestDef> quests)
    {
        foreach (var q in quests)
            Quests[q.Id] = q;
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