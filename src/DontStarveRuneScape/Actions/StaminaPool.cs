namespace DontStarveRuneScape.Actions;

using DontStarveRuneScape.Config;

/// <summary>
/// StaminaPool — Tracks stamina for woodcutting/mining actions.
/// </summary>
public sealed class StaminaPool
{
    /// <summary>Current stamina (0 = exhausted).</summary>
    public float Current { get; private set; }

    /// <summary>Maximum stamina (from config).</summary>
    public float MaxStamina { get; private set; }

    /// <summary>Cooldown when exhausted (seconds remaining).</summary>
    public float ExhaustionTimer { get; private set; } = 0.0f;

    /// <summary>True when stamina is 0 and resting.</summary>
    public bool IsExhausted { get; private set; } = false;

    /// <summary>
    /// Create a StaminaPool.
    /// </summary>
    public StaminaPool()
    {
        Current = Constants.MaxStaminaBase;
        MaxStamina = Constants.MaxStaminaBase;
    }

    /// <summary>
    /// Try to consume stamina. Returns false if exhausted.
    /// </summary>
    /// <param name="amount">Stamina to consume.</param>
    /// <returns>True if stamina was consumed, false if exhausted.</returns>
    public bool Consume(float amount)
    {
        if (IsExhausted) return false;
        if (Current < amount)
        {
            Current = 0.0f;
            IsExhausted = true;
            ExhaustionTimer = 5.0f; // 5s rest when exhausted
            return false;
        }
        Current -= amount;
        return true;
    }

    /// <summary>
    /// Update stamina pool each frame.
    /// Regenerates stamina when not exhausted. Rests when exhausted.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    public void Tick(float dt)
    {
        if (IsExhausted)
        {
            ExhaustionTimer -= dt;
            if (ExhaustionTimer <= 0)
            {
                IsExhausted = false;
                Current = MaxStamina;
            }
        }
        else
        {
            // Regenerate stamina slowly
            Current = Math.Min(MaxStamina, Current + dt * 2.0f);
        }
    }
}