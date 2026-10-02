namespace DontStarveRuneScape.Actions;

using DontStarveRuneScape.World;

/// <summary>
/// ActiveAction — Tracks the currently running action on the player.
/// </summary>
public sealed class ActiveAction
{
    /// <summary>What kind of action (woodcutting/mining/cooking).</summary>
    public ActionType ActionType { get; set; }

    /// <summary>IDLE, RUNNING. Running is a one-frame transient: actions
    /// resolve instantly on the next tick, so the state only marks a pending
    /// completion.</summary>
    public ActionState State { get; set; } = ActionState.Idle;

    /// <summary>The resource node being interacted with (None for cooking).</summary>
    public ResourceNode? Resource { get; set; }

    /// <summary>The recipe being cooked (None for woodcutting/mining).</summary>
    public string? RecipeId { get; set; }

    /// <summary>Stamina consumed per action, charged at completion. The pool
    /// gates the next action: acting is possible while it covers the cost.</summary>
    public float StaminaCost { get; set; } = 3.0f;

    /// <summary>XP granted on success.</summary>
    public float XpReward { get; set; } = 0.0f;

    /// <summary>Item produced on success.</summary>
    public string YieldItem { get; set; } = string.Empty;

    /// <summary>How many items produced.</summary>
    public int YieldQuantity { get; set; } = 1;

    /// <summary>Bonus from skill sub-stat (percentage points).</summary>
    public float SuccessRateBonus { get; set; } = 0.0f;

    /// <summary>Bonus from skill sub-stat (extra roll chance).</summary>
    public float ExtraResourcesBonus { get; set; } = 0.0f;

    /// <summary>Tool type needed ("axe", "pickaxe", or None).</summary>
    public string? RequiredTool { get; set; }

    /// <summary>Tile coords for regrow tracking.</summary>
    public (int X, int Y)? TileXy { get; set; }

    /// <summary>
    /// Create an ActiveAction.
    /// </summary>
    public ActiveAction(ActionType actionType = ActionType.Woodcutting)
    {
        ActionType = actionType;
    }

    /// <summary>True while the player cannot perform other actions.</summary>
    public bool IsBusy => State == ActionState.Running;
}