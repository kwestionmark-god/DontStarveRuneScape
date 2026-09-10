namespace DontStarveRuneScape.Skills.Construction;

using DontStarveRuneScape.Skills;

/// <summary>
/// ConstructionSkill — Building and structure construction.
/// </summary>
public sealed class ConstructionSkill
{
    private readonly SkillManager _skillManager;

    public ConstructionSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }
}