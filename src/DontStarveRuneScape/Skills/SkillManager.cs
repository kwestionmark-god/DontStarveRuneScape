namespace DontStarveRuneScape.Skills;

using System.Collections.Generic;

/// <summary>
/// SkillManager — Manages all 8 skills with OSRS XP formula and sub-stats.
/// </summary>
public sealed class SkillManager
{
    private readonly Dictionary<string, SkillData> _skills = new();

    public SkillManager()
    {
        // Initialize 8 skills
        var skillIds = new[]
        {
            "woodcutting", "mining", "foraging", "cooking",
            "firemaking", "crafting", "metallurgy", "construction", "intelligence"
        };

        foreach (var id in skillIds)
        {
            _skills[id] = new SkillData { Id = id };
        }
    }

    /// <summary>Get skill level for a skill ID.</summary>
    public int GetSkillLevel(string skillId)
    {
        if (_skills.TryGetValue(skillId, out var skill))
            return skill.Level;
        return 1;
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
            skill.Level = newLevel;
            messages.Add($"Congratulations! Your {skillId} level is now {newLevel}!");
            // Grant stat points
            skill.UnallocatedPoints += Constants.StatPointsPerLevel;
        }

        return messages;
    }

    /// <summary>Add raw XP to a skill.</summary>
    public void AddXp(string skillId, float xp)
    {
        if (_skills.TryGetValue(skillId, out var skill))
            skill.Xp += xp;
    }

    /// <summary>Calculate level from XP using OSRS formula.</summary>
    private int CalculateLevelFromXp(float xp)
    {
        // Simplified OSRS formula
        for (int level = 1; level <= 99; level++)
        {
            float xpForLevel = 0;
            for (int i = 1; i <= level; i++)
            {
                xpForLevel += (float)System.Math.Floor(i + 300 * System.Math.Pow(2, i / 7.0));
            }
            xpForLevel /= 4;
            if (xp < xpForLevel)
                return level - 1;
        }
        return 99;
    }

    /// <summary>Get skill data for UI display.</summary>
    public SkillData GetSkill(string skillId)
    {
        return _skills.GetValueOrDefault(skillId, new SkillData { Id = skillId });
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
    public Dictionary<string, float> SubStats { get; } = new()
    {
        ["success_rate"] = 0f,
        ["harvest_boost"] = 0f,
        ["extra_resources"] = 0f,
        ["efficiency"] = 0f,
        ["stamina_reduction"] = 0f,
    };
}