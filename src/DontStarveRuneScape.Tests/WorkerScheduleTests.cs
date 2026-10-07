namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.NPC;
using Xunit;

/// <summary>
/// Worker-schedules slice: 24-slot behavior templates keyed to the day/night
/// clock (dawn 06:00, dusk 18:00), template selection by behavior/shift,
/// and the inert null-clock fallback.
/// </summary>
public class WorkerScheduleTests
{
    // Worker day: 06 NOURISHMENT · 07–08 WORK · 09–11 WORK · 12 NOURISHMENT
    // · 13–17 WORK · 18 NOURISHMENT · 19–20 FREE_TIME · 21–05 SLEEP.
    [Theory]
    [InlineData(0f, ScheduleCategory.Sleep)]
    [InlineData(3f, ScheduleCategory.Sleep)]
    [InlineData(5.99f, ScheduleCategory.Sleep)]
    [InlineData(6f, ScheduleCategory.Nourishment)]
    [InlineData(7f, ScheduleCategory.Work)]
    [InlineData(11.5f, ScheduleCategory.Work)]
    [InlineData(12f, ScheduleCategory.Nourishment)]
    [InlineData(13f, ScheduleCategory.Work)]
    [InlineData(17.99f, ScheduleCategory.Work)]
    [InlineData(18f, ScheduleCategory.Nourishment)]
    [InlineData(19f, ScheduleCategory.FreeTime)]
    [InlineData(20.99f, ScheduleCategory.FreeTime)]
    [InlineData(21f, ScheduleCategory.Sleep)]
    [InlineData(23f, ScheduleCategory.Sleep)]
    public void WorkerDay_MapsEveryBoundaryHour(float hour, ScheduleCategory expected)
        => Assert.Equal(expected, WorkerSchedule.WorkerDay().SlotFor(hour));

    [Theory]
    [InlineData(WorkShift.Daytime, 7f, ScheduleCategory.MilitaryDuty)]
    [InlineData(WorkShift.Daytime, 12f, ScheduleCategory.Nourishment)]
    [InlineData(WorkShift.Daytime, 17f, ScheduleCategory.MilitaryDuty)]
    [InlineData(WorkShift.Daytime, 19f, ScheduleCategory.FreeTime)]
    [InlineData(WorkShift.Daytime, 23f, ScheduleCategory.Sleep)]
    [InlineData(WorkShift.Daytime, 2f, ScheduleCategory.Sleep)]
    [InlineData(WorkShift.Nighttime, 7f, ScheduleCategory.FreeTime)]
    [InlineData(WorkShift.Nighttime, 9f, ScheduleCategory.Sleep)]
    [InlineData(WorkShift.Nighttime, 12f, ScheduleCategory.Sleep)]
    [InlineData(WorkShift.Nighttime, 13f, ScheduleCategory.Sleep)]
    [InlineData(WorkShift.Nighttime, 18f, ScheduleCategory.Nourishment)]
    [InlineData(WorkShift.Nighttime, 19f, ScheduleCategory.MilitaryDuty)]
    [InlineData(WorkShift.Nighttime, 0f, ScheduleCategory.MilitaryDuty)]
    [InlineData(WorkShift.Nighttime, 5f, ScheduleCategory.MilitaryDuty)]
    public void GuardShift_MapsWatchAndOffHours(WorkShift shift, float hour, ScheduleCategory expected)
        => Assert.Equal(expected, WorkerSchedule.GuardShift(shift).SlotFor(hour));

    [Fact]
    public void Templates_ForAssistantAndGuard_ByBehaviorAndShift()
    {
        var worker = ScheduleTemplates.For("assistant", null);
        Assert.NotNull(worker);
        Assert.Equal(ScheduleCategory.Work, worker!.SlotFor(9f));

        var dayGuard = ScheduleTemplates.For("guard", WorkShift.Daytime);
        Assert.NotNull(dayGuard);
        Assert.Equal(ScheduleCategory.MilitaryDuty, dayGuard!.SlotFor(9f));

        var nightGuard = ScheduleTemplates.For("guard", WorkShift.Nighttime);
        Assert.NotNull(nightGuard);
        Assert.Equal(ScheduleCategory.Sleep, nightGuard!.SlotFor(9f));

        Assert.Null(ScheduleTemplates.For("merchant", null));
        Assert.Null(ScheduleTemplates.For(null, null));
    }
}
