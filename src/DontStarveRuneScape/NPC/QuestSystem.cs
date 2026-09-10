namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.World;

/// <summary>
/// QuestSystem — Handles quest acceptance and tracking.
/// </summary>
public sealed class QuestSystem
{
    public void Tick(float dt, Player? player, TileMap? world) { }

    /// <summary>Accept a quest from an NPC.</summary>
    public QuestResult AcceptQuest(Player player, NPC npc, string questId)
    {
        return new QuestResult { Success = true, Message = $"Accepted quest: {questId}" };
    }

    /// <summary>Record a negotiation for quest tracking.</summary>
    public void RecordNegotiation(string factionId)
    {
        // Track negotiation for quest progress
    }
}

/// <summary>
/// QuestResult — Result of a quest action.
/// </summary>
public sealed class QuestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}