namespace DontStarveRuneScape.Skills.Woodcutting;

using DontStarveRuneScape.Skills;

/// <summary>
/// WoodcuttingSkill — Woodcutting-specific logic for action system integration.
/// </summary>
public sealed class WoodcuttingSkill
{
    private readonly SkillManager _skillManager;

    public WoodcuttingSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }

    /// <summary>Get effective stamina cost per action (base 3.0, reduced by skill).</summary>
    public float GetEffectiveStaminaCost(float baseCost)
    {
        // Placeholder: reduce by efficiency stat
        float efficiency = _skillManager.GetEffectiveStat("woodcutting", "efficiency");
        return baseCost * (1.0f - efficiency * 0.1f);
    }

    /// <summary>Get success rate bonus in percentage points.</summary>
    public float GetSuccessRateBonus()
    {
        // Placeholder: success_rate sub-stat * 100
        return _skillManager.GetEffectiveStat("woodcutting", "success_rate") * 100.0f;
    }

    /// <summary>Calculate yield with extra resources bonus.</summary>
    public int CalculateYield(int baseYield, float extraResourcesBonus)
    {
        int quantity = baseYield;
        // Extra resources bonus is a percentage chance for +1
        var random = new System.Random();
        if (random.NextDouble() * 100.0 < extraResourcesBonus)
            quantity++;
        return quantity;
    }
}