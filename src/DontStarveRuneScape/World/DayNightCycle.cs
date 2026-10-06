namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Config;

/// <summary>
/// DayNightCycle — conservative day/night clock: 10 real minutes of day,
/// 10 of night. TimeOfDay runs [0,1) over one full cycle: 0 = dawn,
/// 0.25 = noon, 0.5 = dusk, 0.75 = midnight. New games start just after
/// dawn. Sleeping (at a lit fire or shelter, night only) fast-forwards the
/// clock at Constants.SleepTimeScale until dawn.
/// </summary>
public sealed class DayNightCycle
{
    /// <summary>Fraction of the full cycle elapsed. 0=dawn, 0.25=noon,
    /// 0.5=dusk, 0.75=midnight.</summary>
    public float TimeOfDay { get; private set; } = 0.05f;

    /// <summary>True while the player sleeps through the night.</summary>
    public bool Sleeping { get; private set; }

    public static float CycleLength =>
        Constants.DayLengthSeconds + Constants.NightLengthSeconds;

    /// <summary>Daylight spans the first half of the cycle.</summary>
    public bool IsDay => TimeOfDay < 0.5f;
    public bool IsNight => !IsDay;

    /// <summary>Clock hour for display; dawn maps to 06:00.</summary>
    public float HourOfDay => (6f + TimeOfDay * 24f) % 24f;

    /// <summary>Ambient light 0–1: DayAmbientLevel at noon falling to
    /// NightAmbientLevel at midnight on a smooth cosine.</summary>
    public float AmbientLevel
    {
        get
        {
            float brightness = 0.5f * (1f + MathF.Cos(MathF.Tau * (TimeOfDay - 0.25f)));
            return Constants.NightAmbientLevel +
                (Constants.DayAmbientLevel - Constants.NightAmbientLevel) * brightness;
        }
    }

    /// <summary>Begin sleeping (night only). Returns false when refused.</summary>
    public bool TryStartSleep()
    {
        if (Sleeping || IsDay) return false;
        Sleeping = true;
        return true;
    }

    public void CancelSleep() => Sleeping = false;

    /// <summary>Advance the clock. While sleeping the clock runs at
    /// SleepTimeScale and stops at dawn.</summary>
    public void Tick(float dt)
    {
        float scale = Sleeping ? Constants.SleepTimeScale : 1f;
        float prev = TimeOfDay;
        TimeOfDay = (TimeOfDay + dt * scale / CycleLength) % 1f;
        // Dawn crossed (cycle wrapped) while asleep → wake up.
        if (Sleeping && (TimeOfDay < prev || IsDay))
            Sleeping = false;
    }

    public DayNightSnapshot GetSnapshot() => new() { TimeOfDay = TimeOfDay };

    public void RestoreSnapshot(DayNightSnapshot snapshot)
    {
        TimeOfDay = MathF.Min(MathF.Max(snapshot.TimeOfDay, 0f), 0.9999f);
        Sleeping = false; // never restore mid-sleep
    }
}

/// <summary>Serializable day/night clock state. Old saves deserialize to
/// the default (just after dawn).</summary>
public sealed class DayNightSnapshot
{
    public float TimeOfDay { get; set; } = 0.05f;
}
