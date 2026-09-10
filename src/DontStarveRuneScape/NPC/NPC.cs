namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Data;

/// <summary>
/// Npc — Base NPC class.
/// </summary>
public abstract class Npc
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
    public string? RecruitedBy { get; set; }

    public virtual void AssignBehavior(string behavior) { }

    /// <summary>Create an NPC instance from a definition dictionary.</summary>
    public static Npc CreateFromDef(Dictionary<string, object> def)
    {
        string type = def.TryGetValue("type", out var typeObj) && typeObj is string t ? t : "npc";
        Npc npc = type switch
        {
            "merchant" => new MerchantNpc(),
            "recruit" => new RecruitNpc(),
            "faction_leader" => new FactionLeaderNpc(),
            "quest_giver" => new QuestGiverNpc(),
            _ => new QuestGiverNpc(), // default
        };

        if (def.TryGetValue("id", out var id) && id is string idStr) npc.NpcId = idStr;
        if (def.TryGetValue("name", out var name) && name is string nameStr) npc.Name = nameStr;
        npc.NpcType = type;
        if (def.TryGetValue("health", out var hp) && hp is int hpInt) npc.Health = hpInt;
        if (def.TryGetValue("max_health", out var maxHp) && maxHp is int maxHpInt) npc.MaxHealth = maxHpInt;
        
        return npc;
    }
}

/// <summary>
/// MerchantNpc — NPC that can trade.
/// </summary>
public sealed class MerchantNpc : Npc
{
    public MerchantNpc()
    {
        NpcType = "merchant";
    }
}

/// <summary>
/// RecruitNpc — NPC that can be recruited.
/// </summary>
public sealed class RecruitNpc : Npc
{
    public RecruitNpc()
    {
        NpcType = "recruit";
    }
}

/// <summary>
/// FactionLeaderNpc — NPC that leads a faction.
/// </summary>
public sealed class FactionLeaderNpc : Npc
{
    public string FactionId { get; set; } = string.Empty;

    public FactionLeaderNpc()
    {
        NpcType = "faction_leader";
    }
}

/// <summary>
/// QuestGiverNpc — NPC that gives quests.
/// </summary>
public sealed class QuestGiverNpc : Npc
{
    public QuestGiverNpc()
    {
        NpcType = "quest_giver";
    }
}