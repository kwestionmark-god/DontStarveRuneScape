namespace DontStarveRuneScape.Actions;

/// <summary>
/// ActionResult — Structured result of a completed action.
///
/// Replaces string-protocol messages like "action_complete:item:qty:xp"
/// with a typed, self-documenting object.
/// </summary>
public sealed class ActionResult
{
    /// <summary>Whether the action succeeded.</summary>
    public bool Success { get; }

    /// <summary>Item produced (empty string if no item).</summary>
    public string ItemId { get; }

    /// <summary>Quantity produced (0 if no item).</summary>
    public int Quantity { get; }

    /// <summary>XP granted.</summary>
    public float Xp { get; }

    /// <summary>Human-readable feedback message.</summary>
    public string Message { get; }

    /// <summary>Rare item dredged up with the catch, if any (the fishing
    /// rare-drop table; null for a plain catch).</summary>
    public string? RareItemId { get; }

    /// <summary>
    /// Create an ActionResult.
    /// </summary>
    public ActionResult(
        bool success,
        string itemId = "",
        int quantity = 0,
        float xp = 0.0f,
        string message = "",
        string? rareItemId = null)
    {
        Success = success;
        ItemId = itemId;
        Quantity = quantity;
        Xp = xp;
        Message = message;
        RareItemId = rareItemId;
    }

    /// <summary>Create a success result with yield.</summary>
    public static ActionResult SuccessResult(string itemId, int quantity, float xp, string message,
        string? rareItemId = null)
        => new(true, itemId, quantity, xp, message, rareItemId);

    /// <summary>Create a failure result.</summary>
    public static ActionResult Failure(string message)
        => new(false, message: message);
}