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

    /// <summary>Calculate harvest yield.</summary>
    public int CalculateHarvest(int baseYield)
    {
        // Foraging might have different yield logic
        return baseYield;
    }
}