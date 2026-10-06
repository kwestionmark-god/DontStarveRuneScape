namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;
using Xunit;

public class DayNightCycleTests
{
    [Fact]
    public void NewGame_StartsJustAfterDawn_InDaylight()
    {
        var clock = new DayNightCycle();
        Assert.True(clock.IsDay);
        Assert.False(clock.IsNight);
        Assert.False(clock.Sleeping);
        Assert.InRange(clock.HourOfDay, 6f, 8f);
    }

    [Fact]
    public void Ambient_PeaksAtNoon_BottomsAtMidnight()
    {
        var clock = new DayNightCycle();
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.25f });
        Assert.Equal(Constants.DayAmbientLevel, clock.AmbientLevel, 3);

        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.75f });
        Assert.Equal(Constants.NightAmbientLevel, clock.AmbientLevel, 3);
    }

    [Fact]
    public void Ambient_AtDawnAndDusk_IsMidway()
    {
        var clock = new DayNightCycle();
        float mid = (Constants.DayAmbientLevel + Constants.NightAmbientLevel) * 0.5f;
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.0f });
        Assert.Equal(mid, clock.AmbientLevel, 3);
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.5f });
        Assert.Equal(mid, clock.AmbientLevel, 3);
    }

    [Fact]
    public void Tick_WrapsAfterFullCycle()
    {
        var clock = new DayNightCycle();
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.9f });
        clock.Tick(DayNightCycle.CycleLength * 0.2f);
        Assert.InRange(clock.TimeOfDay, 0f, 0.15f);
        Assert.True(clock.IsDay);
    }

    [Fact]
    public void TryStartSleep_RefusedDuringDay()
    {
        var clock = new DayNightCycle(); // dawn, day
        Assert.False(clock.TryStartSleep());
        Assert.False(clock.Sleeping);
    }

    [Fact]
    public void Sleep_FastForwardsAndWakesAtDawn()
    {
        var clock = new DayNightCycle();
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.6f }); // early night
        Assert.True(clock.TryStartSleep());

        // 20 real seconds at 30x = 600 game seconds = half a cycle → dawn.
        for (int i = 0; i < 200 && clock.Sleeping; i++)
            clock.Tick(0.1f);

        Assert.False(clock.Sleeping);
        Assert.True(clock.IsDay);
        Assert.InRange(clock.TimeOfDay, 0f, 0.05f);
    }

    [Fact]
    public void CancelSleep_StopsAcceleration()
    {
        var clock = new DayNightCycle();
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.75f });
        Assert.True(clock.TryStartSleep());
        clock.CancelSleep();
        Assert.False(clock.Sleeping);
        float before = clock.TimeOfDay;
        clock.Tick(1f);
        Assert.Equal(before + 1f / DayNightCycle.CycleLength, clock.TimeOfDay, 4);
    }

    [Fact]
    public void Snapshot_RoundTrips_AndNeverRestoresSleeping()
    {
        var clock = new DayNightCycle();
        clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = 0.75f });
        clock.TryStartSleep();

        var restored = new DayNightCycle();
        restored.RestoreSnapshot(clock.GetSnapshot());

        Assert.Equal(0.75f, restored.TimeOfDay, 4);
        Assert.False(restored.Sleeping);
    }
}
