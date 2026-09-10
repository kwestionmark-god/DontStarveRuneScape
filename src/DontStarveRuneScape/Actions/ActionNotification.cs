namespace DontStarveRuneScape.Actions;

/// <summary>
/// ActionNotification — A brief notification shown to the player.
/// Used for level-ups, action results, spoilage warnings, etc.
/// </summary>
public sealed class ActionNotification
{
    /// <summary>Message text.</summary>
    public string Text { get; }

    /// <summary>RGB color for rendering.</summary>
    public (byte R, byte G, byte B) Color { get; }

    /// <summary>Duration in seconds.</summary>
    public float Duration { get; }

    /// <summary>Time elapsed since creation.</summary>
    public float Elapsed { get; set; } = 0.0f;

    /// <summary>
    /// Create an ActionNotification.
    /// </summary>
    public ActionNotification(
        string text,
        (byte R, byte G, byte B) color = default,
        float duration = 2.0f)
    {
        Text = text;
        Color = color == default ? (255, 255, 255) : color;
        Duration = duration;
    }

    /// <summary>True if the notification has expired.</summary>
    public bool IsExpired => Elapsed >= Duration;
}