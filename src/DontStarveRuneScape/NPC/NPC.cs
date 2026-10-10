namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;

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
    /// <summary>Net velocity last frame (world px per second), derived from
    /// position deltas — the same approach the player's gait uses. Zero while
    /// NPCs are stationary.</summary>
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsRecruited { get; set; } = false;
    public string? RecruitBehavior { get; set; }
    /// <summary>Settlement food reserve, from 0 (starving) to 100 (sated).</summary>
    public float ColonyHunger { get; set; } = 100f;
    public string ColonyNeedStatus { get; set; } = "Fed";
    /// <summary>Rest reserve from 0 (exhausted) to 100 (rested).</summary>
    public float ColonyRest { get; set; } = 100f;
    public string ColonyRestStatus { get; set; } = "Rested";
    /// <summary>Goods a worker is hauling to the settlement stockpile.
    /// Persisted so a mid-haul save loses nothing.</summary>
    public string? CarriedItemId { get; set; }
    public int CarriedQuantity { get; set; }
    public string CarryStatus { get; set; } = "";
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public List<string> AvailableQuests { get; set; } = [];
    public string? RecruitedBy { get; set; }

    /// <summary>Biome of the tile the NPC stands on (set at spawn); used to
    /// match merchants to their trade stock.</summary>
    public string Biome { get; set; } = string.Empty;

    /// <summary>Faction id for all NPC types (loaded from the definition).</summary>
    public string FactionId { get; set; } = string.Empty;

    /// <summary>Flavor dialogue lines (loaded from the definition).</summary>
    public List<string> DialogueLines { get; } = [];

    /// <summary>Merchant's starting gold (loaded from the definition); the
    /// TradeSystem tracks it per merchant at runtime.</summary>
    public int StartingGold { get; set; }

    /// <summary>Merchant's price modifier (loaded from the definition).</summary>
    public float PriceModifier { get; set; } = 1.0f;

    /// <summary>Recruitment gates (loaded from the definition): intelligence
    /// commerce/persuasion sub-stats and their composite sum.</summary>
    public int RecruitCommerce { get; set; }
    public int RecruitPersuasion { get; set; }
    public int RecruitComposite { get; set; }
    public List<string> AvailableBehaviors { get; } = [];

    public virtual void AssignBehavior(string behavior) { }

    /// <summary>Create an NPC instance from a typed definition.</summary>
    public static Npc FromDef(NpcDef def)
    {
        Npc npc = def.Type switch
        {
            "merchant" => new MerchantNpc(),
            "recruit" => new RecruitNpc(),
            "faction_leader" => new FactionLeaderNpc(),
            "quest_giver" => new QuestGiverNpc(),
            _ => new QuestGiverNpc(), // default
        };

        npc.NpcId = def.NpcId;
        npc.Name = def.Name;
        npc.NpcType = def.Type;
        npc.Health = def.Health;
        npc.MaxHealth = def.MaxHealth;
        npc.FactionId = def.Faction;
        npc.StartingGold = def.StartingGold;
        npc.PriceModifier = def.PriceModifier;
        npc.RecruitCommerce = def.RecruitCommerceRequirement;
        npc.RecruitPersuasion = def.RecruitPersuasionRequirement;
        npc.RecruitComposite = def.RecruitCompositeStat;
        npc.AvailableQuests.AddRange(def.AvailableQuestIds);
        npc.AvailableBehaviors.AddRange(def.AvailableBehaviors);
        foreach (var line in def.DialogueLines)
            npc.DialogueLines.Add(line);
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
/// RecruitNpc — NPC that can be recruited into the colony.
/// Per-recruit skill stats: carries its own SkillManager, mirroring the
/// player's (same OSRS XP curve), so a recruit reads like a character.
/// </summary>
public sealed class RecruitNpc : Npc
{
    /// <summary>Per-recruit skill progression — gatherers level the node's
    /// gathering skill, builders level construction, guards level attack.
    /// Same SkillManager class the player uses.</summary>
    public SkillManager Skills { get; } = new();

    /// <summary>Total units gathered by this recruit as a colony worker over
    /// its life — the yardstick the yield-feedback loop measures. Not
    /// persisted; a save keeps the skills, not the odometer.</summary>
    public int TotalGathered { get; set; }

    /// <summary>Harvest intervals since this recruit last earned a bonus
    /// yield — the deterministic counter for the gather-level feedback.
    /// Runtime-only; a save/load resets the cadence, not the skill.</summary>
    public int GatherIntervalsSinceBonus { get; set; }

    /// <summary>Taming: the monster def id this pet was tamed from
    /// ("" for non-pet recruits).</summary>
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Taming: true when this pet is colony-assigned (wander/guard)
    /// rather than the bonded follower.</summary>
    public bool IsColonyAssigned { get; set; }

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
