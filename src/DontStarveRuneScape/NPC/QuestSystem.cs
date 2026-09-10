namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Data;

/// <summary>
/// QuestSystem — Handles quest acceptance and tracking.
/// </summary>
public sealed class QuestSystem
{
    public void Tick(float dt, Player? player, TileMap? world) { }

    /// <summary>Accept a quest from an NPC.</summary>
    public QuestResult AcceptQuest(Player player, Npc npc, string questId)
    {
        return new QuestResult { Success = true, Message = $"Accepted quest: {questId}" };
    }

    /// <summary>Record a negotiation for quest tracking.</summary>
    public void RecordNegotiation(string factionId)
    {
        // Track negotiation for quest progress
    }

    /// <summary>Get snapshot for saving.</summary>
    public QuestSnapshot GetSnapshot()
    {
        var snapshot = new QuestSnapshot();
        // TODO: Track active quests and their progress
        snapshot.Quests = [];
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(QuestSnapshot snapshot, DataLoader dataLoader)
    {
        // TODO: Restore quest progress
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