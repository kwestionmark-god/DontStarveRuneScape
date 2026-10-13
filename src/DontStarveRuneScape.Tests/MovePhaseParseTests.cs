namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Core;
using Xunit;

/// <summary>
/// DSR_TEST_MOVE grammar fix: the phased form ("right:20;up:40") was
/// gated on Contains(';'), so a SINGLE timed phase ("right:999") fell
/// through to the legacy whole-run branch where the token "right:999"
/// parsed to no keys at all — a silent no-op (the harness's own law:
/// smoke hooks must never no-op silently). Live symptom: single-phase
/// smoketest movement does nothing. Game.ParseMovePhases is the single
/// parser for every form; each phase is keys[:frames], a frames-less
/// final phase holds to the end.
/// </summary>
public class MovePhaseParseTests
{
    [Fact]
    public void SingleTimedPhase_Parses()
    {
        var phases = Game.ParseMovePhases("right:999");
        var ph = Assert.Single(phases);
        Assert.True(ph.Right);
        Assert.Equal(999, ph.EndFrame);
    }

    [Fact]
    public void MultiPhase_AccumulatesEndFrames()
    {
        var phases = Game.ParseMovePhases("right:20;up+orbit_cw:40");
        Assert.Equal(2, phases.Count);
        Assert.True(phases[0].Right);
        Assert.Equal(20, phases[0].EndFrame);
        Assert.True(phases[1].Up && phases[1].OrbitCW);
        Assert.Equal(60, phases[1].EndFrame);
    }

    [Fact]
    public void LegacyHold_ParsesAsUnboundedPhase()
    {
        var phases = Game.ParseMovePhases("up,left");
        var ph = Assert.Single(phases);
        Assert.True(ph.Up && ph.Left);
        Assert.Equal(int.MaxValue, ph.EndFrame);
    }

    [Fact]
    public void TrailingFrameslessPhase_HoldsToEnd()
    {
        var phases = Game.ParseMovePhases("up:30;sprint");
        Assert.Equal(30, phases[0].EndFrame);
        Assert.Equal(int.MaxValue, phases[1].EndFrame);
        Assert.True(phases[1].Sprint);
    }
}
