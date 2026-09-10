namespace DontStarveRuneScape.Skills.Metallurgy;

using DontStarveRuneScape.Skills;

/// <summary>
/// MetallurgySkill — Smelting and metalworking.
/// </summary>
public sealed class MetallurgySkill
{
    private readonly SkillManager _skillManager;

    public MetallurgySkill(SkillManager skillManager)
    {
        _skillManager = skillManager;
    }
}