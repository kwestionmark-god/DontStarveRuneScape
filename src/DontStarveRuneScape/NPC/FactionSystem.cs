namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;

/// <summary>
/// FactionSystem — Handles faction relationships and diplomacy.
/// </summary>
public sealed class FactionSystem
{
    /// <summary>Negotiate with a faction.</summary>
    public FactionNegotiationResult Negotiate(Player player, string factionId)
    {
        return new FactionNegotiationResult
        {
            Success = true,
            Message = $"Negotiation with {factionId} successful."
        };
    }

    /// <summary>Get snapshot for saving.</summary>
    public FactionSnapshot GetSnapshot()
    {
        var snapshot = new FactionSnapshot();
        // TODO: Track faction standings
        snapshot.Factions = [];
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(FactionSnapshot snapshot, DataLoader dataLoader)
    {
        // TODO: Restore faction standings
    }
}

/// <summary>
/// FactionNegotiationResult — Result of a faction negotiation.
/// </summary>
public sealed class FactionNegotiationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}