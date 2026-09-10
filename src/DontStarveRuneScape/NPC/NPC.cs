namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Data;

/// <summary>
/// NPC — Base NPC class.
/// </summary>
public abstract class NPC
{
    public string NpcId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NpcType { get; set; } = string.Empty;
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRecruited { get; set; } = false;
    public string? RecruitBehavior { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public List<string> AvailableQuests { get; set; } = [];

    public virtual void AssignBehavior(string behavior) { }
}

/// <summary>
/// MerchantNPC — NPC that can trade.
/// </summary>
public sealed class MerchantNPC : NPC
{
    public MerchantNPC()
    {
        NpcType = "merchant";
    }
}

/// <summary>
/// RecruitNPC — NPC that can be recruited.
/// </summary>
public sealed class RecruitNPC : NPC
{
    public RecruitNPC()
    {
        NpcType = "recruit";
    }
}

/// <summary>
/// FactionLeaderNPC — NPC that leads a faction.
/// </summary>
public sealed class FactionLeaderNPC : NPC
{
    public string FactionId { get; set; } = string.Empty;

    public FactionLeaderNPC()
    {
        NpcType = "faction_leader";
    }
}

/// <summary>
/// QuestGiverNPC — NPC that gives quests.
/// </summary>
public sealed class QuestGiverNPC : NPC
{
    public QuestGiverNPC()
    {
        NpcType = "quest_giver";
    }
}