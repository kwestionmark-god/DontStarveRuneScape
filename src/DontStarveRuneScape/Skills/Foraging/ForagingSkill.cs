namespace DontStarveRuneScape.Skills.Foraging;

using DontStarveRuneScape.Skills;

/// <summary>
/// ForagingSkill — Foraging-specific logic for action system integration.
/// </summary>
public sealed class ForagingSkill
{
    private readonly SkillManager _skillManager;

    public ForagingSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }

    /// <summary>Get stamina reduction (0.0 to 0.5).</summary>
    public float GetStaminaReduction()
    {
        return _skillManager.GetEffectiveStat("foraging", "stamina_reduction") * 0.5f;
    }

    /// <summary>Get success rate (0.0 to 1.0).</summary>
    public float GetSuccessRate()
    {
        return _skillManager.GetEffectiveStat("foraging", "success_rate");
    }

    /// <summary>Calculate harvest yield. The harvest_boost bonus (raw
    /// invested points) is a percentage chance for +1 — same shape as the
    /// woodcutting/mining yield arms; it was stashed but dead until the
    /// individual-stat-menus slice wired it.</summary>
    public int CalculateHarvest(int baseYield, float extraResourcesBonus = 0f)
    {
        if (extraResourcesBonus > 0f && new System.Random().NextDouble() * 100.0 < extraResourcesBonus)
            return baseYield + 1;
        return baseYield;
    }
}