namespace DontStarveRuneScape.Render;

/// <summary>
/// Banks the player's body into left/right turns while running. Each
/// frame's heading change feeds a smoothed turn rate, and the body leans
/// proportionally to it — lerped in, capped, and eased back out — so an
/// instant WASD direction change reads as a brief bank into the turn.
/// Pure state math with no GL types, unit-testable in isolation.
/// <para>
/// Conventions: the heading comes from the player's world velocity, where
/// +y grows downward on screen (matching <c>Camera.WorldToScreen</c>), so
/// a positive heading delta is a right turn as the viewer sees it, and a
/// positive <see cref="Lean"/> tilts the top of the sprite clockwise
/// (toward screen right) — the same screen-space angle convention as
/// <c>SpriteRenderer.DrawRotatedTexturedQuad</c>.
/// </para>
/// </summary>
public sealed class TurnLean
{
    /// <summary>Speed in px/s below which the player counts as stopped:
    /// the lean settles upright and heading tracking resets (also used to
    /// hold the body upright while swimming).</summary>
    public const float MinSpeed = 40f;

    /// <summary>Low-pass rate on the turn rate (per second). Sets how long
    /// a bank holds after an instant direction change: a one-frame heading
    /// jump of Δθ lifts the smoothed rate to about Δθ × this rate,
    /// independent of the frame time.</summary>
    public const float TurnRateLerp = 5f;

    /// <summary>Output lerp rate toward the target bank (per second) — the
    /// visible ramp into and out of the lean.</summary>
    public const float LeanLerp = 9f;

    /// <summary>Bank radians per rad/s of smoothed turn rate. Tuned so a
    /// held camera orbit while running forward (circling at
    /// <c>CameraOrbitSpeed</c>) holds a visible ~3° bank, and a 90° WASD
    /// direction change peaks near 6°. Sprinting widens this downstream
    /// (SpriteRenderer scales the lean up while the sprint flag is set) —
    /// the base walk bank stays here.</summary>
    public const float Gain = 0.032f;

    /// <summary>Hard cap on the base bank angle (~8.6°) — keeps rapid
    /// double-tap direction changes from tipping the sprite over. The
    /// sprint boost multiplies past this by design.</summary>
    public const float MaxLean = 0.15f;

    /// <summary>Lateral stance shift in world px per radian of bank: while
    /// the body leans into a turn, the feet plant shifted toward the lean
    /// side ("planted under the bank") — ~1.5px while circling under a
    /// held camera orbit, up to ~4.5px at the cap.</summary>
    public const float StanceShiftPerRad = 30f;

    private float _prevHeading;
    private bool _hasHeading;
    private float _turnRate;   // smoothed turn rate, rad/s
    private float _lean;      // current bank, rad

    /// <summary>Current bank in radians, screen space: positive tilts the
    /// top of the sprite clockwise (toward screen right).</summary>
    public float Lean => _lean;

    /// <summary>Advance the lean one frame. <paramref name="velX"/>/
    /// <paramref name="velY"/> is the player's net world velocity;
    /// <paramref name="dt"/> is the frame time in seconds. Passing a
    /// below-<see cref="MinSpeed"/> velocity (or zero, e.g. while
    /// swimming) settles the body upright. Returns <see cref="Lean"/>.</summary>
    public float Update(float velX, float velY, float dt)
    {
        if (dt <= 0f) return _lean;

        float speed = MathF.Sqrt(velX * velX + velY * velY);
        if (speed < MinSpeed)
        {
            // Stopped: settle upright and forget the old heading so the
            // next run start doesn't bank from a stale direction.
            _hasHeading = false;
            _turnRate = Step(_turnRate, 0f, dt, TurnRateLerp);
            _lean = Step(_lean, 0f, dt, LeanLerp);
            return _lean;
        }

        float heading = MathF.Atan2(velY, velX);
        float delta = 0f;
        if (_hasHeading)
        {
            delta = WrapPi(heading - _prevHeading);
            // Only left/right turning banks the body: past ±90° the change
            // shades toward a reversal, whose "inside" is ambiguous, fading
            // to no lean at a full 180°.
            float lateral = Math.Clamp((MathF.PI - MathF.Abs(delta)) / (MathF.PI * 0.5f), 0f, 1f);
            delta *= lateral;
        }
        else
        {
            _hasHeading = true;   // first moving frame: adopt, don't turn
        }
        _prevHeading = heading;

        // The per-frame delta over dt is the instantaneous turn rate; the
        // low-pass spreads a one-frame keyboard turn into a ~0.2s bank.
        _turnRate = Step(_turnRate, delta / dt, dt, TurnRateLerp);
        float target = Math.Clamp(Gain * _turnRate, -MaxLean, MaxLean);
        _lean = Step(_lean, target, dt, LeanLerp);
        return _lean;
    }

    /// <summary>Frame-rate-robust exponential follow: reaches 63% of the
    /// gap in 1/rate seconds regardless of the frame time.</summary>
    private static float Step(float from, float to, float dt, float rate) =>
        from + (to - from) * (1f - MathF.Exp(-rate * dt));

    /// <summary>Wrap an angle to (−π, π].</summary>
    private static float WrapPi(float a)
    {
        a = MathF.IEEERemainder(a, 2f * MathF.PI);
        if (a <= -MathF.PI) a += 2f * MathF.PI;
        if (a > MathF.PI) a -= 2f * MathF.PI;
        return a;
    }
}
