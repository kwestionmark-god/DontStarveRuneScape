namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;

/// <summary>
/// QuestSystem — Quest acceptance (stat and standing gates, prerequisites),
/// active tracking with per-objective progress, and reward claiming.
/// Collect/deliver progress polls the inventory; kill/craft/trade/visit/
/// negotiate progress comes from Notify* hooks wired by the game.
/// </summary>
public sealed class QuestSystem
{
    /// <summary>Quests this system tracks from; loaded at world boot.</summary>
    public QuestRegistry? Registry { get; set; }

    /// <summary>Faction standings (owned by FactionSystem; wired at boot) for
    /// the standing acceptance gate.</summary>
    public FactionSystem? Factions { get; set; }

    /// <summary>Faction standing a fresh relationship starts at (0–1).</summary>
    public const float DefaultStanding = 0.5f;

    private readonly List<ActiveQuest> _active = [];
    private readonly HashSet<string> _completed = [];

    public IReadOnlyList<ActiveQuest> Active => _active;
    public IReadOnlyCollection<string> Completed => _completed;

    public void Tick(float dt, Player? player, TileMap? world)
    {
        // visit_location progress: standing in the target biome completes it.
        if (player == null || world == null) return;
        var (tx, ty) = player.GetTilePosition();
        string? biome = world.GetTile(tx, ty)?.Biome?.Id;
        if (string.IsNullOrEmpty(biome)) return;

        foreach (var aq in _active)
        {
            var quest = Registry?.GetQuest(aq.QuestId);
            if (quest == null) continue;
            for (int i = 0; i < quest.Conditions.Length; i++)
            {
                var c = quest.Conditions[i];
                if (c.Type == "visit_location" && c.Target == biome)
                    aq.Progress[i] = c.RequiredCount;
            }
        }
    }

    public bool IsAccepted(string questId) => _active.Any(a => a.QuestId == questId);
    public bool IsCompleted(string questId) => _completed.Contains(questId);

    /// <summary>Accept a quest: gates are the giver's available list (caller),
    /// prerequisites, faction standing, intelligence level and its commerce/
    /// persuasion sub-stats.</summary>
    public QuestResult AcceptQuest(Player player, Npc npc, string questId)
    {
        var quest = Registry?.GetQuest(questId);
        if (quest == null)
            return new QuestResult { Success = false, Message = "Unknown quest." };
        if (!npc.AvailableQuests.Contains(questId))
            return new QuestResult { Success = false, Message = $"{npc.Name} is not offering that quest." };
        if (!string.IsNullOrEmpty(quest.GiverNpcType) && quest.GiverNpcType != npc.NpcType)
            return new QuestResult { Success = false, Message = "This quest belongs to a different kind of quest giver." };
        if (IsAccepted(questId))
            return new QuestResult { Success = false, Message = "That quest is already active." };
        if (!quest.IsRepeatable && IsCompleted(questId))
            return new QuestResult { Success = false, Message = "That quest is already completed." };

        foreach (var pre in quest.PrerequisiteQuests)
        {
            if (!IsCompleted(pre))
                return new QuestResult { Success = false, Message = $"Requires quest: {Registry?.GetQuest(pre)?.Name ?? pre}." };
        }

        if (StandingOf(quest.GiverFaction) < quest.AcceptanceThreshold)
            return new QuestResult
            {
                Success = false,
                Message = $"Requires more standing with {FactionSystem.TierName(quest.AcceptanceThreshold)}-tier factions.",
            };
        if (player.SkillManager?.GetSkillLevel("intelligence") < quest.MinTotalIntelligence)
            return new QuestResult { Success = false, Message = $"Requires intelligence level {quest.MinTotalIntelligence}." };
        if (CommerceOf(player) < quest.MinCommerce)
            return new QuestResult { Success = false, Message = $"Requires commerce {quest.MinCommerce}." };
        if (PersuasionOf(player) < quest.MinPersuasion)
            return new QuestResult { Success = false, Message = $"Requires persuasion {quest.MinPersuasion}." };

        _active.Add(new ActiveQuest { QuestId = questId });
        return new QuestResult { Success = true, Message = $"Accepted quest: {quest.Name}." };
    }

    public float StandingOf(string factionId) =>
        factionId.Length == 0 ? DefaultStanding : (Factions?.StandingOf(factionId) ?? DefaultStanding);

    public int CommerceOf(Player player) =>
        (int)(player.SkillManager?.GetEffectiveStat("intelligence", "commerce") ?? 0f);

    public int PersuasionOf(Player player) =>
        (int)(player.SkillManager?.GetEffectiveStat("intelligence", "persuasion") ?? 0f);

    // ─── Progress hooks (wired by the game) ────────────────────────────────

    /// <summary>Items added to the inventory count toward collect_item objectives.</summary>
    public void NotifyCollect(string itemId, int quantity)
    {
        foreach (var aq in _active)
        {
            var quest = Registry?.GetQuest(aq.QuestId);
            if (quest == null) continue;
            for (int i = 0; i < quest.Conditions.Length; i++)
            {
                var c = quest.Conditions[i];
                if (c.Type == "collect_item" && c.Target == itemId)
                    aq.Progress[i] = Math.Min(c.RequiredCount, aq.Progress.GetValueOrDefault(i) + quantity);
            }
        }
    }

    /// <summary>Crafted outputs count toward craft_item objectives.</summary>
    public void NotifyCraft(string outputItem)
    {
        Bump("craft_item", outputItem, 1);
    }

    /// <summary>Monster kills count toward kill_monster objectives.</summary>
    public void NotifyKill(string monsterId)
    {
        Bump("kill_monster", monsterId, 1);
    }

    /// <summary>Trades with an NPC count toward trade_at_location objectives.</summary>
    public void NotifyTrade(string npcId)
    {
        Bump("trade_at_location", npcId, 1);
    }

    /// <summary>Successful negotiations count toward negotiate_faction objectives.</summary>
    public void RecordNegotiation(string factionId)
    {
        Bump("negotiate_faction", factionId, 1);
    }

    private void Bump(string type, string target, int amount)
    {
        foreach (var aq in _active)
        {
            var quest = Registry?.GetQuest(aq.QuestId);
            if (quest == null) continue;
            for (int i = 0; i < quest.Conditions.Length; i++)
            {
                var c = quest.Conditions[i];
                if (c.Type == type && c.Target == target)
                    aq.Progress[i] = Math.Min(c.RequiredCount, aq.Progress.GetValueOrDefault(i) + amount);
            }
        }
    }

    // ─── Progress & completion ──────────────────────────────────────────────

    /// <summary>Progress of one objective: counters for hook-fed types;
    /// inventory polling for collect/deliver.</summary>
    public int ProgressOf(QuestDef quest, int conditionIndex, Inventory inventory)
    {
        var c = quest.Conditions[conditionIndex];
        if (c.Type is "collect_item" or "deliver_item")
            return Math.Min(c.RequiredCount, inventory.GetItemQuantity(c.Target));

        var aq = _active.FirstOrDefault(a => a.QuestId == quest.QuestId);
        return Math.Min(c.RequiredCount, aq?.Progress.GetValueOrDefault(conditionIndex) ?? 0);
    }

    /// <summary>All objectives complete?</summary>
    public bool ConditionsMet(QuestDef quest, Inventory inventory)
    {
        for (int i = 0; i < quest.Conditions.Length; i++)
        {
            if (ProgressOf(quest, i, inventory) < quest.Conditions[i].RequiredCount)
                return false;
        }
        return true;
    }

    /// <summary>Claim a completed quest's rewards: xp (intelligence + per-skill),
    /// items, recipe/gear unlocks; deliver_item objectives hand the goods over.
    /// Non-repeatable quests move to completed.</summary>
    public QuestResult Claim(Player player, Npc npc, string questId, Inventory inventory, SkillManager skills)
    {
        var quest = Registry?.GetQuest(questId);
        if (quest == null)
            return new QuestResult { Success = false, Message = "Unknown quest." };
        if (!IsAccepted(questId))
            return new QuestResult { Success = false, Message = "That quest is not active." };
        if (!ConditionsMet(quest, inventory))
            return new QuestResult { Success = false, Message = "Objectives not complete yet." };

        var extra = new List<string>();

        // Deliveries leave the inventory.
        foreach (var c in quest.Conditions)
        {
            if (c.Type == "deliver_item")
                inventory.RemoveItem(c.Target, c.RequiredCount);
        }

        // Rewards: flat intelligence XP, per-skill XP, items, unlocks.
        if (quest.XpReward > 0)
            extra.AddRange(skills.AddXpWithNotification("intelligence", quest.XpReward));
        foreach (var (skill, xp) in quest.SkillXpRewards)
            extra.AddRange(skills.AddXpWithNotification(skill, xp));
        foreach (var (itemId, quantity) in quest.ItemRewards)
        {
            if (inventory.CanAdd(itemId, quantity))
            {
                inventory.AddItem(itemId, quantity);
                extra.Add($"Reward: +{quantity} {itemId}");
            }
            else
            {
                extra.Add($"Reward lost (inventory full): {quantity} {itemId}");
            }
        }
        foreach (var recipe in quest.RecipeUnlocks)
            player.UnlockedRecipes.Add(recipe);
        foreach (var gear in quest.GearUnlocks)
            player.UnlockedGear.Add(gear);

        _active.RemoveAll(a => a.QuestId == questId);
        if (!quest.IsRepeatable)
            _completed.Add(questId);

        return new QuestResult
        {
            Success = true,
            Message = $"Quest complete: {quest.Name}.",
            Extra = extra,
        };
    }

    /// <summary>Record a negotiation for quest tracking (legacy alias).</summary>
    public void RecordNegotiation(string factionId, int _) => RecordNegotiation(factionId);

    /// <summary>Get snapshot for saving.</summary>
    public QuestSnapshot GetSnapshot()
    {
        var snapshot = new QuestSnapshot();
        var list = new List<QuestDataSnapshot>();
        foreach (var aq in _active)
        {
            var quest = Registry?.GetQuest(aq.QuestId);
            var progress = new Dictionary<string, int>();
            if (quest != null)
            {
                for (int i = 0; i < quest.Conditions.Length; i++)
                    progress[i.ToString()] = aq.Progress.GetValueOrDefault(i);
            }
            list.Add(new QuestDataSnapshot { QuestId = aq.QuestId, State = "active", Progress = progress });
        }
        snapshot.Quests = [.. list];
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(QuestSnapshot snapshot, DataLoader dataLoader)
    {
        _active.Clear();
        if (snapshot.Quests != null)
        {
            foreach (var q in snapshot.Quests)
            {
                var aq = new ActiveQuest { QuestId = q.QuestId };
                if (q.Progress != null)
                {
                    foreach (var (key, value) in q.Progress)
                    {
                        if (int.TryParse(key, out var index))
                            aq.Progress[index] = value;
                    }
                }
                _active.Add(aq);
            }
        }
    }
}

/// <summary>
/// ActiveQuest — An accepted quest with per-objective progress counters.
/// Collect/deliver progress polls the inventory instead of these counters.
/// </summary>
public sealed class ActiveQuest
{
    public string QuestId { get; set; } = string.Empty;
    public Dictionary<int, int> Progress { get; } = [];
}

/// <summary>
/// QuestResult — Result of a quest action.
/// </summary>
public sealed class QuestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Extra lines (rewards, level-ups), for the panel to show.</summary>
    public List<string> Extra { get; set; } = [];
}
