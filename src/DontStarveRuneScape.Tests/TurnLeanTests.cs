using DontStarveRuneScape.Render;
using Xunit;

namespace DontStarveRuneScape.Tests;

/// <summary>
/// TurnLean banking: the body must bank into left/right turns while
/// running (positive lean = clockwise on screen = right turn, since world
/// +y grows downward on screen), stay upright on straight runs and full
/// reversals, settle after the turn, and behave identically at different
/// frame rates. Expected values mirror the offline tuning sim
/// (90° turn ≈ 0.106 rad peak at Gain 0.032).
/// </summary>
public class TurnLeanTests
{
    private const float RunSpeed = 150f;   // Constants.PlayerMovementSpeed

    /// <summary>Drive TurnLean at fps for total seconds; heading(t) is the
    /// travel heading in radians (velocity = speed·(cos, sin)). Returns all
    /// lean samples.</summary>
    private static List<float> Simulate(TurnLean lean, float fps, float totalSeconds,
        Func<float, float> heading)
    {
        float dt = 1f / fps;
        var samples = new List<float>();
        for (float t = 0f; t < totalSeconds; t += dt)
        {
            float h = heading(t);
            samples.Add(lean.Update(RunSpeed * MathF.Cos(h), RunSpeed * MathF.Sin(h), dt));
        }
        return samples;
    }

    // Stand still briefly, run straight, then one instant heading change —
    // the canonical WASD turn scenario.
    private static List<float> RunThenTurn(TurnLean lean, float fps, float turnHeadingRad,
        float runAfterTurnSeconds)
    {
        return Simulate(lean, fps, 0.5f + runAfterTurnSeconds, t => t < 0.5f ? 0f : turnHeadingRad);
    }

    [Fact]
    public void StraightRunStaysUpright()
    {
        var lean = new TurnLean();
        foreach (var v in Simulate(lean, 60f, 2f, _ => 0f))
            Assert.True(MathF.Abs(v) < 1e-4f, $"lean {v} on a straight run");

        // Zero dt is a no-op.
        Assert.Equal(lean.Lean, lean.Update(RunSpeed, 0f, 0f), 6);
    }

    [Fact]
    public void BanksIntoRightTurn()
    {
        // East → south is a right turn on screen (world +y points down), so
        // the lean must be positive (top of the sprite tilts clockwise).
        var samples = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 1f);
        float peak = samples.Select(MathF.Abs).Max();
        Assert.True(peak > 0.06f, $"peak {peak} too small to read as a lean");
        Assert.True(peak <= TurnLean.MaxLean + 1e-4f, $"peak {peak} exceeds the cap");
        Assert.True(samples.Max() > 0.05f, "right turn must lean positive");
    }

    [Fact]
    public void BanksIntoLeftTurnMirror()
    {
        // East → north is a left turn: the lean must mirror the right turn.
        var right = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 0.5f);
        var left = RunThenTurn(new TurnLean(), 60f, -MathF.PI / 2f, 0.5f);
        Assert.Equal(-right.Max(), left.Min(), 2);
        Assert.Equal(-right.Min(), left.Max(), 2);
    }

    [Fact]
    public void SmallerTurnBanksProportionally()
    {
        // A 45° turn banks about half of a 90° turn.
        var q90 = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 0.4f).Select(MathF.Abs).Max();
        var q45 = RunThenTurn(new TurnLean(), 60f, MathF.PI / 4f, 0.4f).Select(MathF.Abs).Max();
        Assert.InRange(q45 / q90, 0.35f, 0.65f);
    }

    [Fact]
    public void FullReversalDoesNotBank()
    {
        // A 180° reversal has an ambiguous inside, so it must stay upright.
        var samples = RunThenTurn(new TurnLean(), 60f, MathF.PI, 1f);
        Assert.True(samples.Select(MathF.Abs).Max() < 0.01f,
            $"reversal banked {samples.Select(MathF.Abs).Max()} rad");
    }

    [Fact]
    public void SettlesAfterTheTurn()
    {
        var samples = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 1.4f);
        // The last 0.2s of straight running in the new direction: upright again.
        Assert.True(MathF.Abs(samples[^1]) < 0.02f, $"lean settled at {samples[^1]}");
        Assert.True(samples.Select(MathF.Abs).Max() > 0.05f, "should have banked mid-window");
    }

    [Fact]
    public void FrameRateRobust()
    {
        // The same 90° turn at 60 and 30 fps banks to nearly the same peak.
        float peak60 = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 0.5f).Select(MathF.Abs).Max();
        float peak30 = RunThenTurn(new TurnLean(), 30f, MathF.PI / 2f, 0.5f).Select(MathF.Abs).Max();
        Assert.InRange(peak30 / peak60, 0.9f, 1.1f);
    }

    [Fact]
    public void StoppingSettlesAndResetsHeading()
    {
        var lean = new TurnLean();
        RunThenTurn(lean, 60f, MathF.PI / 2f, 0.1f);
        // Stop: settles upright.
        for (int i = 0; i < 60; i++)
            lean.Update(0f, 0f, 1f / 60f);
        Assert.True(MathF.Abs(lean.Lean) < 0.02f, $"lean {lean.Lean} after stopping");

        // Restart straight in a new direction: no bank from the stale
        // heading — the first moving frame adopts it instead of turning.
        var samples = Simulate(lean, 60f, 0.5f, _ => MathF.PI);
        Assert.True(samples.Select(MathF.Abs).Max() < 0.01f,
            "restart straight banked from a stale heading");
    }

    [Fact]
    public void RapidDoubleTurnBanksHarderButCapped()
    {
        // Two 90° right taps a few frames apart bank harder than one turn
        // (the smoothed turn rate is still elevated) but never past the cap.
        var lean = new TurnLean();
        var samples = Simulate(lean, 60f, 1.2f,
            t => t switch { < 0.5f => 0f, < 0.55f => MathF.PI / 2f, _ => MathF.PI });
        float peak = samples.Select(MathF.Abs).Max();
        float single = RunThenTurn(new TurnLean(), 60f, MathF.PI / 2f, 0.5f).Select(MathF.Abs).Max();
        Assert.True(peak > single, "double turn should bank harder than a single one");
        Assert.True(peak <= TurnLean.MaxLean + 1e-4f, $"peak {peak} exceeds the cap");
    }
}
