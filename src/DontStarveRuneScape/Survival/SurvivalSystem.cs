namespace DontStarveRuneScape.Survival;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;

/// <summary>
/// SurvivalSystem — Hunger + HP management.
///
/// Tracks and updates hunger and HP meters. Handles hunger drain,
/// starvation HP drain, food consumption, damage, and healing.
///
/// Survival continues ticking during panel states — survival tension
/// is always present once the game is in PLAYING mode.
/// </summary>
public sealed class SurvivalSystem
{
    // ─── Public State ────────────────────────────────────────────────────────

    /// <summary>Current hunger 0–100 (0 = starving, 100 = full).</summary>
    public float Hunger { get; private set; } = 100.0f;

    /// <summary>Max hunger (constant 100).</summary>
    public float MaxHunger { get; private set; } = 100.0f;

    /// <summary>Current HP 0–MaxHP (0 = dead).</summary>
    public float Hp { get; private set; }

    /// <summary>Maximum HP (starts at HpBaseMax, can increase via skills/gear).</summary>
    public float MaxHp { get; set; }

    /// <summary>Timer for starvation HP drain (accumulates while hunger == 0).</summary>
    public float StarvationTimer { get; private set; } = 0.0f;

    /// <summary>True once HP reaches 0.</summary>
    public bool IsDead { get; private set; } = false;

    /// <summary>Optional death callback (injected by Bootstrap).</summary>
    public Action? OnDeath { get; set; }

    /// <summary>Biome multiplier on survival drain (0.5 = safe, 1.0 = normal, 1.5+ = harsh).</summary>
    public float EnvironmentalPressure { get; private set; } = 1.0f;

    // ─── Seasonal / Weather Modifiers ────────────────────────────────────────

    private float _seasonHungerMod = 1.0f;
    private object? _seasonSystem;
    public object? WeatherSystem { get; set; }

    // ─── Configuration (from Constants) ──────────────────────────────────────

    private readonly float _hungerDrainInterval = Constants.HungerDrainInterval;
    private readonly float _hungerDrainRate = Constants.HungerDrainRate;
    private readonly float _starvationHpDrainRate = Constants.StarvationHpDrainRate;
    private float _lastEnvPressure = 1.0f;

    /// <summary>
    /// Initialize survival system.
    /// </summary>
    /// <param name="environmentalPressure">Biome multiplier on survival drain.</param>
    public SurvivalSystem(float environmentalPressure = 1.0f)
    {
        Hp = Constants.HpBaseMax;
        MaxHp = Constants.HpBaseMax;
        EnvironmentalPressure = environmentalPressure;
        _lastEnvPressure = environmentalPressure;
    }

    /// <summary>
    /// Called every frame. Handles hunger drain and starvation HP drain.
    /// </summary>
    /// <param name="dt">Delta time in seconds.</param>
    public void Tick(float dt)
    {
        if (IsDead) return;

        // Apply seasonal modifiers
        UpdateSeasonModifiers();

        // ── Hunger drain ──────────────────────────────────────────────────
        if (Hunger > 0)
        {
            // Drain 1 point every HUNGER_DRAIN_INTERVAL seconds,
            // scaled by environmental pressure and seasonal hunger modifier
            float drainRate = _hungerDrainRate * EnvironmentalPressure * _seasonHungerMod;
            Hunger -= drainRate * (dt / _hungerDrainInterval);
            Hunger = Math.Max(0.0f, Hunger);
        }

        // ── Starvation HP drain ──────────────────────────────────────────
        if (Hunger <= 0)
        {
            StarvationTimer += dt;
            if (StarvationTimer >= _hungerDrainInterval)
            {
                StarvationTimer -= _hungerDrainInterval;
                float hpDrain = _starvationHpDrainRate * EnvironmentalPressure;
                if (TakeDamage(hpDrain))
                {
                    // Starvation killed the player
                    OnDeath?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// Consume a FoodItem to restore hunger and optionally HP.
    /// </summary>
    /// <param name="foodItem">Food item with nutrition values.</param>
    /// <returns>Result string for player feedback.</returns>
    public string Eat(FoodItem foodItem)
    {
        if (IsDead) return "You are dead.";

        float oldHunger = Hunger;
        float oldHp = Hp;

        // Restore hunger (capped at max)
        Hunger = Math.Min(MaxHunger, Hunger + foodItem.HungerRestoration);
        float hungerGained = Hunger - oldHunger;

        // Restore/damage HP
        if (foodItem.HpRestoration != 0.0f)
        {
            Hp = Math.Max(0.0f, Math.Min(MaxHp, Hp + foodItem.HpRestoration));
        }

        // Feedback
        if (foodItem.HpRestoration > 0)
            return $"You eat it. Hunger +{hungerGained:F0}, HP +{foodItem.HpRestoration:F0}.";
        else if (foodItem.HpRestoration < 0)
            return $"You eat it. Hunger +{hungerGained:F0}, HP {foodItem.HpRestoration:F0}.";
        else
            return $"You eat it. Hunger +{hungerGained:F0}.";
    }

    /// <summary>
    /// Reduce HP by amount. Triggers death if HP <= 0.
    /// </summary>
    /// <param name="amount">Damage to deal.</param>
    /// <returns>True if the player died.</returns>
    public bool TakeDamage(float amount)
    {
        if (IsDead) return false;

        Hp -= amount;
        if (Hp <= 0)
        {
            Hp = 0.0f;
            IsDead = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Restore HP (capped at MaxHp).
    /// </summary>
    /// <param name="amount">HP to restore.</param>
    public void Heal(float amount)
    {
        if (IsDead) return;
        Hp = Math.Min(MaxHp, Hp + amount);
    }

    /// <summary>Returns hunger/max_hunger as a 0–1 ratio.</summary>
    public float GetHungerPercent() => MaxHunger <= 0 ? 0.0f : Hunger / MaxHunger;

    /// <summary>Returns hp/max_hp as a 0–1 ratio.</summary>
    public float GetHpPercent() => MaxHp <= 0 ? 0.0f : Hp / MaxHp;

    /// <summary>
    /// Update environmental pressure based on the current tile's biome.
    /// </summary>
    /// <param name="tile">The current tile the player is standing on.</param>
    public void UpdateFromTile(Tile? tile)
    {
        if (tile == null || tile.Biome == null)
        {
            if (EnvironmentalPressure != 1.0f)
            {
                EnvironmentalPressure = 1.0f;
                _lastEnvPressure = 1.0f;
            }
            return;
        }

        float newPressure = tile.Biome.EnvironmentalPressure;
        if (Math.Abs(newPressure - _lastEnvPressure) > 0.001f)
        {
            EnvironmentalPressure = newPressure;
            _lastEnvPressure = newPressure;
        }
    }

    /// <summary>Apply seasonal survival modifiers if SeasonSystem is wired.</summary>
    private void UpdateSeasonModifiers()
    {
        if (_seasonSystem == null)
        {
            _seasonHungerMod = 1.0f;
            return;
        }

        // This would call SeasonSystem.GetSurvivalModifiers() in the real implementation
        // For now, just set default
        _seasonHungerMod = 1.0f;
    }

    /// <summary>Getter for SeasonSystem (set by Bootstrap).</summary>
    public object? SeasonSystem
    {
        get => _seasonSystem;
        set => _seasonSystem = value;
    }

    /// <summary>Return current weather gameplay effects.</summary>
    public Dictionary<string, float> GetWeatherEffects()
    {
        if (WeatherSystem != null)
        {
            // This would call WeatherSystem.GetEffects() in the real implementation
            return new Dictionary<string, float>();
        }
        return new Dictionary<string, float>();
    }

    /// <summary>Return weather visibility multiplier (0.0–1.0).</summary>
    public float WeatherVisibility => GetWeatherEffects().GetValueOrDefault("visibility", 1.0f);

    /// <summary>Return weather movement speed multiplier (0.0–1.0).</summary>
    public float WeatherMovementSpeed => GetWeatherEffects().GetValueOrDefault("movement_speed", 1.0f);

    /// <summary>Return weather outdoor crafting multiplier (0.0–1.0).</summary>
    public float WeatherOutdoorCrafting => GetWeatherEffects().GetValueOrDefault("outdoor_crafting", 1.0f);

    /// <summary>Return weather spawn modifier (0.0+).</summary>
    public float WeatherSpawnMod => GetWeatherEffects().GetValueOrDefault("spawn_mod", 1.0f);

    /// <summary>Get snapshot for saving.</summary>
    public SurvivalSnapshot GetSnapshot()
    {
        return new SurvivalSnapshot
        {
            Hp = Hp,
            MaxHp = MaxHp,
            Hunger = Hunger,
            MaxHunger = MaxHunger,
            Stamina = 100f, // TODO: add stamina to SurvivalSystem
            MaxStamina = 100f
        };
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(SurvivalSnapshot snapshot)
    {
        Hp = snapshot.Hp;
        MaxHp = snapshot.MaxHp;
        Hunger = snapshot.Hunger;
        MaxHunger = snapshot.MaxHunger;
        IsDead = Hp <= 0;
    }
}