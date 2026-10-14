namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Cave Stage 3 flavor — drips and echoes: while the player is below, a
/// deterministic ambience scheduler occasionally whispers a flavor line
/// through the notification channel (water drips, echoes, skittering).
/// Deterministic per seed so smoketests and saves replay identically;
/// lines never fire inside the minimum interval (no spam) nor beyond the
/// maximum (the cave never feels dead-quiet for long).
/// </summary>
public class CaveAmbienceTests
{
    [Fact]
    public void Tick_StaysSilentBeforeTheMinimumInterval()
    {
        var amb = new CaveAmbience(seed: 12345);
        for (float t = 0f; t < CaveAmbience.MinIntervalSeconds - 0.1f; t += 0.1f)
            Assert.Null(amb.Tick(0.1f));
    }

    [Fact]
    public void Tick_FiresWithinTheMaximumInterval()
    {
        var amb = new CaveAmbience(seed: 12345);
        bool fired = false;
        for (float t = 0f; t < CaveAmbience.MaxIntervalSeconds + 1f; t += 0.1f)
            fired |= amb.Tick(0.1f) != null;
        Assert.True(fired, "no flavor line fired within the maximum interval");
    }

    [Fact]
    public void Tick_IsDeterministicPerSeed()
    {
        var a = Collect(new CaveAmbience(seed: 777));
        var b = Collect(new CaveAmbience(seed: 777));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Tick_RespectsTheCooldownBetweenLines()
    {
        var amb = new CaveAmbience(seed: 777);
        var fireTimes = new List<float>();
        for (float t = 0f; t < 600f; t += 0.25f)
            if (amb.Tick(0.25f) != null)
                fireTimes.Add(t);
        Assert.True(fireTimes.Count >= 3, "expected several lines over 10 minutes");
        for (int i = 1; i < fireTimes.Count; i++)
            Assert.True(fireTimes[i] - fireTimes[i - 1] >= CaveAmbience.MinIntervalSeconds - 0.3f,
                $"lines fired {fireTimes[i] - fireTimes[i - 1]:F2}s apart (min {CaveAmbience.MinIntervalSeconds}s)");
    }

    [Fact]
    public void Tick_DifferentSeedsTellDifferentStories()
    {
        Assert.NotEqual(Collect(new CaveAmbience(seed: 1)), Collect(new CaveAmbience(seed: 2)));
    }

    private static List<string> Collect(CaveAmbience amb)
    {
        var lines = new List<string>();
        for (float t = 0f; t < 600f; t += 0.25f)
        {
            var line = amb.Tick(0.25f);
            if (line != null) lines.Add($"{t:F2}:{line}");
        }
        return lines;
    }
}
