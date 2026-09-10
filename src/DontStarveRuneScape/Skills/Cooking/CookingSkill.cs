namespace DontStarveRuneScape.Skills.Cooking;

using DontStarveRuneScape.Skills;

/// <summary>
/// CookingSkill — Food preparation and cooking.
/// </summary>
public sealed class CookingSkill
{
    private readonly SkillManager _skillManager;

    public CookingSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }
}