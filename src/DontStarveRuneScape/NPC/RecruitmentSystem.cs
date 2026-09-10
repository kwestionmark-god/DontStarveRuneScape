namespace DontStarveRuneScape.NPC;

/// <summary>
/// RecruitmentSystem — Handles NPC recruitment and dismissal.
/// </summary>
public sealed class RecruitmentSystem
{
    public void Tick(float dt) { }

    /// <summary>Called when an NPC is recruited.</summary>
    public void OnRecruit(string npcId, string behavior) { }

    /// <summary>Called when an NPC is dismissed.</summary>
    public void OnDismiss(string npcId) { }
}