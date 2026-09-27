namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;

/// <summary>
/// FactionSystem — Faction standing (0–1, starts neutral at QuestSystem's
/// DefaultStanding) and negotiation: standing rises by a base gain plus a
/// persuasion-scaled bonus, capped at 1.
/// </summary>
public sealed class FactionSystem
{
    // Standing per faction; unknown factions start neutral.
    private readonly Dictionary<string, float> _standings = [];

    /// <summary>The faction's current standing (0–1).</summary>
    public float StandingOf(string factionId) =>
        _standings.TryGetValue(factionId, out var s) ? s : QuestSystem.DefaultStanding;

    /// <summary>Standing tier name for display.</summary>
    public static string TierName(float standing) => standing switch
    {
        >= 0.85f => "allied",
        >= 0.65f => "friendly",
        >= 0.5f => "neutral",
        >= 0.25f => "suspicious",
        _ => "hostile",
    };

    /// <summary>Negotiate with a faction: raises standing by a base gain plus
    /// a persuasion-scaled bonus (intelligence sub-stat), capped at 1.</summary>
    public FactionNegotiationResult Negotiate(Player player, string factionId)
    {
        if (string.IsNullOrEmpty(factionId))
            return new FactionNegotiationResult { Success = false, Message = "No faction to negotiate with." };

        float persuasion = player.SkillManager?.GetEffectiveStat("intelligence", "persuasion") ?? 0f;
        float gain = 0.08f + persuasion * 0.03f;

        float old = StandingOf(factionId);
        float now = Math.Min(1f, old + gain);
        _standings[factionId] = now;

        if (now >= 1f)
            return new FactionNegotiationResult
            {
                Success = true,
                Message = $"Fully allied with {factionId} ({now:P0} standing).",
                NewStanding = now,
            };
        return new FactionNegotiationResult
        {
            Success = true,
            Message = $"Negotiated with {factionId}: standing now {TierName(now)} ({now:P0}).",
            NewStanding = now,
        };
    }

    /// <summary>Get snapshot for saving.</summary>
    public FactionSnapshot GetSnapshot()
    {
        var snapshot = new FactionSnapshot();
        snapshot.Factions = [.. _standings.Select(kv => new FactionDataSnapshot
        {
            FactionId = kv.Key,
            Standing = (int)MathF.Round(kv.Value * 100f), // stored as percent
        })];
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(FactionSnapshot snapshot, DataLoader dataLoader)
    {
        _standings.Clear();
        if (snapshot.Factions != null)
        {
            foreach (var f in snapshot.Factions)
                _standings[f.FactionId] = f.Standing / 100f;
        }
    }
}

/// <summary>
/// FactionNegotiationResult — Result of a faction negotiation.
/// </summary>
public sealed class FactionNegotiationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public float NewStanding { get; set; }
}
