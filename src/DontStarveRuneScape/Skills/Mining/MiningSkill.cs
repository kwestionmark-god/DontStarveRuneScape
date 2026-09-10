namespace DontStarveRuneScape.Skills.Mining;

using DontStarveRuneScape.Skills;

/// <summary>
/// MiningSkill — Mining-specific logic for action system integration.
/// </summary>
public sealed class MiningSkill
{
    private readonly SkillManager _skillManager;

    public MiningSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }

    /// <summary>Get effective stamina cost per action (base 3.0, reduced by skill).</summary>
    public float GetEffectiveStaminaCost(float baseCost)
    {
        float efficiency = _skillManager.GetEffectiveStat("mining", "efficiency");
        return baseCost * (1.0f - efficiency * 0.1f);
    }

    /// <summary>Get success rate bonus in percentage points.</summary>
    public float GetSuccessRateBonus()
    {
        return _skillManager.GetEffectiveStat("mining", "success_rate") * 100.0f;
    }

    /// <summary>Calculate yield with extra resources bonus.</summary>
    public int CalculateYield(int baseYield, float extraResourcesBonus)
    {
        int quantity = baseYield;
        var random = new System.Random();
        if (random.NextDouble() * 100.0 < extraResourcesBonus)
            quantity++;
        return quantity;
    }
}