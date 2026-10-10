namespace DontStarveRuneScape.Skills;

using System.Collections.Generic;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;

/// <summary>
/// SkillManager — Manages all skills with OSRS XP formula and sub-stats.
/// </summary>
public sealed class SkillManager
{
    private readonly Dictionary<string, SkillData> _skills = new();

    /// <summary>Per-skill spendable sub-stats — the single source the
    /// panel renders and SpendPoint admits. A key appears on a skill only
    /// if a live seam consumes it (spec:
    /// 2026-10-09-individual-stat-menus-design.md).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> SubStatCatalog = new Dictionary<string, string[]>
    {
        ["attack"] = ["power", "speed"],
        ["woodcutting"] = ["success_rate", "harvest_boost", "efficiency"],
        ["mining"] = ["success_rate", "extra_resources", "efficiency"],
        ["foraging"] = ["success_rate", "harvest_boost", "stamina_reduction"],
        ["fishing"] = ["success_rate", "harvest_boost", "rare_luck"],
        ["cooking"] = ["harvest_boost", "efficiency"],
        ["firemaking"] = ["fuel_saver", "duration"],
        ["crafting"] = ["harvest_boost", "efficiency"],
        ["metallurgy"] = ["harvest_boost", "efficiency"],
        ["construction"] = ["build_speed", "efficiency"],
        ["intelligence"] = ["commerce", "persuasion"],
        ["agility"] = ["sprint_cost", "jump_cost"],
    };

    /// <summary>Display names shared across skills (keys are reused by
    /// design — one name per mechanic).</summary>
    public static readonly IReadOnlyDictionary<string, string> SubStatNames = new Dictionary<string, string>
    {
        ["success_rate"] = "Success rate",
        ["harvest_boost"] = "Harvest boost",
        ["extra_resources"] = "Extra resources",
        ["efficiency"] = "Efficiency",
        ["stamina_reduction"] = "Stamina reduction",
        ["power"] = "Power",
        ["speed"] = "Attack speed",
        ["rare_luck"] = "Rare luck",
        ["fuel_saver"] = "Fuel saver",
        ["duration"] = "Fire duration",
        ["build_speed"] = "Build speed",
        ["commerce"] = "Commerce",
        ["persuasion"] = "Persuasion",
        ["sprint_cost"] = "Sprint cost",
        ["jump_cost"] = "Jump cost",
    };

    public SkillManager()
    {
        var skillIds = new[]
        {
            "attack", "woodcutting", "mining", "foraging", "fishing", "cooking",
            "firemaking", "crafting", "metallurgy", "construction", "intelligence",
            "agility"
        };

        foreach (var id in skillIds)
        {
            var data = new SkillData { Id = id };
            // The skill's menu comes from the catalog — logic and panel
            // can never disagree.
            if (SubStatCatalog.TryGetValue(id, out var stats))
                foreach (var key in stats)
                    data.SubStats[key] = 0f;
            _skills[id] = data;
        }

        // Intelligence also carries the commerce/persuasion sub-stats that
        // gate quests, trade stock, and recruitment. They start at 1 (like
        // levels) so the first gates are reachable from a fresh character.
        _skills["intelligence"].SubStats["commerce"] = 1f;
        _skills["intelligence"].SubStats["persuasion"] = 1f;
    }

    /// <summary>Get skill level for a skill ID.</summary>
    public int GetSkillLevel(string skillId)
    {
        if (_skills.TryGetValue(skillId, out var skill))
            return skill.Level;
        return 1;
    }

    /// <summary>Alias for GetSkillLevel.</summary>
    public int GetLevel(string skillId) => GetSkillLevel(skillId);

    /// <summary>Check if a quest is completed (placeholder - would check quest system).</summary>
    public bool IsQuestCompleted(string questId)
    {
        // TODO: Integrate with QuestSystem
        return false;
    }

    /// <summary>Get effective stat value (base + level bonuses + gear).</summary>
    public float GetEffectiveStat(string skillId, string statName)
    {
        if (!_skills.TryGetValue(skillId, out var skill))
            return 0f;

        // Base stat from sub-stats
        if (skill.SubStats.TryGetValue(statName, out var baseValue))
        {
            // Add level scaling
            float levelBonus = skill.Level * 0.01f; // 1% per level
            return baseValue + levelBonus;
        }
        return 0f;
    }

    /// <summary>Add XP to a skill and return level-up messages.</summary>
    public List<string> AddXpWithNotification(string skillId, float xp)
    {
        var messages = new List<string>();
        if (!_skills.TryGetValue(skillId, out var skill))
            return messages;

        skill.Xp += xp;
        int oldLevel = skill.Level;

        // OSRS XP formula: XP = floor(level + 300 * 2^(level/7)) / 4
        // Reverse: find level from XP
        int newLevel = CalculateLevelFromXp(skill.Xp);
        if (newLevel > oldLevel)
        {
            // One message + points per level gained; a single big XP award (e.g. quest
            // reward) must grant points for every level crossed.
            for (int lvl = oldLevel + 1; lvl <= newLevel; lvl++)
            {
                messages.Add($"Congratulations! Your {skillId} level is now {lvl}!");
                skill.UnallocatedPoints += Constants.StatPointsPerLevel;
            }
            skill.Level = newLevel;
        }

        return messages;
    }

    /// <summary>Add raw XP to a skill.</summary>
    public void AddXp(string skillId, float xp)
    {
        if (_skills.TryGetValue(skillId, out var skill))
            skill.Xp += xp;
    }

    /// <summary>Soft-death XP penalty: every skill loses the given fraction of
    /// its XP; levels re-derive from the reduced XP (no points are refunded).</summary>
    public void ApplyDeathPenalty(float fraction)
    {
        foreach (var skill in _skills.Values)
        {
            if (skill.Xp <= 0) continue;
            skill.Xp -= skill.Xp * fraction;
            skill.Level = CalculateLevelFromXp(skill.Xp);
        }
    }

    /// <summary>Spend an unallocated stat point on a sub-stat. Returns false for unknown
    /// skill, unknown sub-stat, or no points available.</summary>
    public bool SpendPoint(string skillId, string statName)
    {
        if (!_skills.TryGetValue(skillId, out var skill)) return false;
        if (skill.UnallocatedPoints <= 0) return false;
        if (!skill.SubStats.ContainsKey(statName)) return false;
        skill.UnallocatedPoints--;
        skill.SubStats[statName]++;
        return true;
    }

    /// <summary>Raw invested points in a sub-stat — no level scaling, no
    /// gear. New consumers read this so zero-point characters keep exact
    /// legacy math.</summary>
    public float GetSubStatPoints(string skillId, string statName)
    {
        return _skills.TryGetValue(skillId, out var skill)
            && skill.SubStats.TryGetValue(statName, out float value) ? value : 0f;
    }

    /// <summary>Progress within the current level: xp earned into the level and xp needed
    /// to reach the next.</summary>
    public void ProgressToNext(string skillId, out float into, out float needed)
    {
        into = 0f; needed = 1f;
        if (!_skills.TryGetValue(skillId, out var skill)) return;
        float floor = XpForLevel(skill.Level);
        float ceil = skill.Level >= 99 ? floor : XpForLevel(skill.Level + 1);
        into = skill.Xp - floor;
        needed = ceil - floor;
    }

    /// <summary>Cumulative XP required to be at the given level (OSRS table; level 1 = 0).</summary>
    public static float XpForLevel(int level)
    {
        if (level <= 1) return 0f;
        double sum = 0;
        for (int i = 1; i <= level - 1; i++)
            sum += System.Math.Floor(i + 300 * System.Math.Pow(2, i / 7.0));
        return (float)System.Math.Floor(sum / 4.0);
    }

    /// <summary>Calculate level from XP using the OSRS formula.</summary>
    private static int CalculateLevelFromXp(float xp)
    {
        for (int level = 1; level <= 99; level++)
        {
            if (xp < XpForLevel(level + 1))
                return level;
        }
        return 99;
    }

    /// <summary>Get skill data for UI display.</summary>
    public SkillData GetSkill(string skillId)
    {
        return _skills.GetValueOrDefault(skillId, new SkillData { Id = skillId });
    }

    /// <summary>Get snapshot for saving.</summary>
    public SkillSnapshot GetSnapshot()
    {
        var snapshot = new SkillSnapshot();
        foreach (var kvp in _skills)
        {
            snapshot.Skills[kvp.Key] = new SkillDataSnapshot
            {
                Level = kvp.Value.Level,
                Xp = kvp.Value.Xp,
                StatPoints = kvp.Value.UnallocatedPoints,
                SubStats = kvp.Value.SubStats.ToDictionary(k => k.Key, v => (int)v.Value)
            };
        }
        return snapshot;
    }

    /// <summary>Restore from snapshot. Sub-stat keys the skill's menu no
    /// longer carries are RESPEC'D: their points return to that skill's
    /// unallocated pool (never a silent loss). Returns the total number
    /// of refunded points.</summary>
    public int RestoreSnapshot(SkillSnapshot snapshot)
    {
        int refunded = 0;
        foreach (var kvp in snapshot.Skills)
        {
            if (!_skills.TryGetValue(kvp.Key, out var skill))
                continue;
            skill.Level = kvp.Value.Level;
            skill.Xp = kvp.Value.Xp;
            skill.UnallocatedPoints = kvp.Value.StatPoints;
            foreach (var statKvp in kvp.Value.SubStats)
            {
                if (skill.SubStats.ContainsKey(statKvp.Key))
                    skill.SubStats[statKvp.Key] = statKvp.Value;
                else
                {
                    // Dead key on this skill's new menu: refund the
                    // invested points, visibly spendable again.
                    skill.UnallocatedPoints += statKvp.Value;
                    refunded += statKvp.Value;
                }
            }
        }
        return refunded;
    }
}

/// <summary>
/// SkillData — Holds XP, level, and sub-stats for a single skill.
/// </summary>
public sealed class SkillData
{
    public string Id { get; set; } = string.Empty;
    public float Xp { get; set; } = 0f;
    public int Level { get; set; } = 1;
    public int UnallocatedPoints { get; set; } = 0;
    public Dictionary<string, float> SubStats { get; } = new();
}