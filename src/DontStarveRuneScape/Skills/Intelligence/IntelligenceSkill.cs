namespace DontStarveRuneScape.Skills.Intelligence;

using DontStarveRuneScape.Skills;

/// <summary>
/// IntelligenceSkill — Magic, research, and knowledge.
/// </summary>
public sealed class IntelligenceSkill
{
    private readonly SkillManager _skillManager;

    public IntelligenceSkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }
}