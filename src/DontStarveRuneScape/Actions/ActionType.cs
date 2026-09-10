namespace DontStarveRuneScape.Actions;

using DontStarveRuneScape.World;

/// <summary>
/// ActionType — Types of resource interaction actions.
/// </summary>
public enum ActionType
{
    Woodcutting,
    Mining,
    Cooking,
    Foraging,
}

/// <summary>
/// Extension methods for ActionType.
/// </summary>
public static class ActionTypeExtensions
{
    /// <summary>
    /// Map an ActionType + resource to a skill ID string.
    /// </summary>
    /// <param name="actionType">The ActionType enum value.</param>
    /// <param name="resource">Optional ResourceNode (used to disambiguate).</param>
    /// <returns>Skill ID string: "woodcutting", "mining", etc.</returns>
    public static string GetSkillId(this ActionType actionType, ResourceNode? resource = null)
    {
        return actionType switch
        {
            ActionType.Woodcutting => "woodcutting",
            ActionType.Mining => "mining",
            ActionType.Cooking => "cooking",
            ActionType.Foraging => "foraging",
            _ => throw new System.ArgumentOutOfRangeException(nameof(actionType), $"Unknown ActionType: {actionType}"),
        };
    }
}