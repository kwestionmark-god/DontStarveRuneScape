namespace DontStarveRuneScape.World;

/// <summary>
/// Cave Stage 3 flavor: a deterministic drip/echo scheduler. While the
/// player is below, Tick occasionally surfaces a flavor line (drips,
/// echoes, skittering) for the notification channel. Seeded with the cave
/// seed so smoketests and save/reload replay the same story. Lines arrive
/// 20–45s apart — never spammy, never dead-quiet.
/// </summary>
public sealed class CaveAmbience
{
    public const float MinIntervalSeconds = 20f;
    public const float MaxIntervalSeconds = 45f;

    private static readonly string[] Lines =
    {
        "Water drips somewhere in the dark.",
        "A faint echo rolls through the cavern.",
        "Something skitters between the rocks.",
        "Cold air breathes up from the deep.",
        "Drip... drip... drip...",
    };

    private readonly Random _rng;
    private float _timer;
    private float _nextFire;

    public CaveAmbience(int seed)
    {
        _rng = new Random(seed);
        _nextFire = RollInterval();
    }

    /// <summary>Advance the clock while inside the cave. Returns the
    /// flavor line when one fires, else null.</summary>
    public string? Tick(float dt)
    {
        _timer += dt;
        if (_timer < _nextFire) return null;
        _timer = 0f;
        _nextFire = RollInterval();
        return Lines[_rng.Next(Lines.Length)];
    }

    private float RollInterval()
        => MinIntervalSeconds + (float)_rng.NextDouble() * (MaxIntervalSeconds - MinIntervalSeconds);
}
