namespace DontStarveRuneScape.NPC;

/// <summary>One day of planned activity: 24 slots, one per clock hour.</summary>
public enum ScheduleCategory
{
    Work,
    Sleep,
    Nourishment,
    FreeTime,
    MilitaryDuty,
}

/// <summary>Which half of the cycle a guard keeps watch over.</summary>
public enum WorkShift
{
    Daytime,
    Nighttime,
}

/// <summary>
/// A worker's daily schedule — 24 slots keyed to the day/night clock's hour
/// (dawn 06:00, dusk 18:00). Schedules are derived per worker from their
/// behavior and shift, never edited in this slice; the per-NPC override
/// seam exists for tests and a future editor.
/// </summary>
public sealed class WorkerSchedule
{
    /// <summary>Auto-eat threshold during NOURISHMENT slots (urgent is 20).</summary>
    public const float CasualHungerThreshold = 45f;

    /// <summary>Free-time wander picks tiles within this radius of home.</summary>
    public const int WanderRadiusTiles = 3;

    /// <summary>Seconds a wandering worker pauses before picking a new tile.</summary>
    public const float WanderRepickMinSeconds = 2f;
    public const float WanderRepickMaxSeconds = 4f;

    private readonly ScheduleCategory[] _hourly;

    internal WorkerSchedule(ScheduleCategory[] hourly) => _hourly = hourly;

    /// <summary>Category for a clock hour (fractional hours floor).</summary>
    public ScheduleCategory SlotFor(float hourOfDay)
        => _hourly[((int)MathF.Floor(hourOfDay) % 24 + 24) % 24];

    /// <summary>Assistant template: meals at 06/12/18, work in daylight,
    /// a short evening at camp, sleep 21:00–05:00.</summary>
    public static WorkerSchedule WorkerDay()
    {
        var hourly = new ScheduleCategory[24];
        for (int hour = 0; hour < 24; hour++)
            hourly[hour] = hour switch
            {
                6 or 12 or 18 => ScheduleCategory.Nourishment,
                >= 7 and < 12 => ScheduleCategory.Work,
                >= 13 and < 18 => ScheduleCategory.Work,
                19 or 20 => ScheduleCategory.FreeTime,
                _ => ScheduleCategory.Sleep,
            };
        return new WorkerSchedule(hourly);
    }

    /// <summary>Guard template: MILITARY_DUTY covers the shift's watch half;
    /// the other half mirrors a worker's off hours. Day squad watches
    /// 07–17 (meals at 06/12/18), night squad takes 19–05.</summary>
    public static WorkerSchedule GuardShift(WorkShift shift)
    {
        var hourly = new ScheduleCategory[24];
        for (int hour = 0; hour < 24; hour++)
        {
            bool meal = hour is 6 or 12 or 18;
            bool daytimeWatch = hour is >= 7 and <= 17;
            bool nightWatch = hour is >= 19 or <= 5;
            hourly[hour] = shift switch
            {
                WorkShift.Daytime => meal ? ScheduleCategory.Nourishment
                    : daytimeWatch ? ScheduleCategory.MilitaryDuty
                    : hour is 19 or 20 ? ScheduleCategory.FreeTime
                    : ScheduleCategory.Sleep,
                _ => hour is 6 or 18 ? ScheduleCategory.Nourishment
                    : nightWatch ? ScheduleCategory.MilitaryDuty
                    : hour is 7 or 8 ? ScheduleCategory.FreeTime
                    : ScheduleCategory.Sleep,
            };
        }
        return new WorkerSchedule(hourly);
    }
}

/// <summary>Maps a worker's behavior (and shift, for guards) to a schedule.
/// Non-brain behaviors get null and stay untouched.</summary>
public static class ScheduleTemplates
{
    public static WorkerSchedule? For(string? recruitBehavior, WorkShift? shift)
        => recruitBehavior switch
        {
            "assistant" => WorkerSchedule.WorkerDay(),
            "guard" => WorkerSchedule.GuardShift(shift ?? WorkShift.Daytime),
            _ => null,
        };
}
