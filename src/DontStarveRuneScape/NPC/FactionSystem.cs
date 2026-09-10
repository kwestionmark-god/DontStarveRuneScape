namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.Core;

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
}

/// <summary>
/// FactionNegotiationResult — Result of a faction negotiation.
/// </summary>
public sealed class FactionNegotiationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}