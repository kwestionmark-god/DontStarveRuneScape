namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using Xunit;

/// <summary>
/// Leap animation channels: the whole paper-doll (body quads, boots,
/// gear) rides the jump arc as one rigid figure, with a toe-off →
/// tuck → heel-strike boot profile and a body lean into the leap.
/// LeapPose is pure math — asserted directly; JumpProgress drives it.
/// </summary>
public class LeapAnimationTests
{
    private static (Player player, ActionSystem actions, SkillManager skills) Jumper()
    {
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        var skills = new SkillManager();
        player.ActionSystem = actions;
        player.SkillManager = skills;
        return (player, actions, skills);
    }

    // ─── LeapPose channels ────────────────────────────────────────────

    [Fact]
    public void LeapPose_ToeOff_AtTakeoff()
    {
        // t=0: full toe-off drag (SwingTilt −1), no tuck, no reach, no lean.
        var p = SpriteRenderer.LeapPose(0f);
        Assert.Equal(-1f, p.Tilt, 3);
        Assert.Equal(0f, p.Tuck, 3);
        Assert.Equal(0f, p.Lead, 3);
        Assert.Equal(0f, p.Lean, 3);
    }

    [Fact]
    public void LeapPose_TuckAndLean_PeakMidArc_NoReach()
    {
        // t=0.5: level boots, max tuck (knees up), max body lean, no reach.
        var p = SpriteRenderer.LeapPose(0.5f);
        Assert.Equal(0f, p.Tilt, 3);
        Assert.Equal(Constants.LeapTuckPx, p.Tuck, 3);
        Assert.Equal(0f, p.Lead, 3);
        Assert.Equal(Constants.LeapLeanPx, p.Lean, 3);
    }

    [Fact]
    public void LeapPose_HeelStrike_AtLanding()
    {
        // t=1: full heel-strike (+1), max forward reach, arc channels back to zero.
        var p = SpriteRenderer.LeapPose(1f);
        Assert.Equal(1f, p.Tilt, 3);
        Assert.Equal(Constants.LeapFootLeadPx, p.Lead, 3);
        Assert.Equal(0f, p.Tuck, 3);
        Assert.Equal(0f, p.Lean, 3);
    }

    [Fact]
    public void LeapPose_Continuous_NoChannelJumps()
    {
        // 200 samples: every channel is continuous — no pop mid-arc.
        var prev = SpriteRenderer.LeapPose(0f);
        for (int i = 1; i <= 200; i++)
        {
            float t = i / 200f;
            var p = SpriteRenderer.LeapPose(t);
            Assert.True(MathF.Abs(p.Tilt - prev.Tilt) < 0.05f);
            Assert.True(MathF.Abs(p.Tuck - prev.Tuck) < 0.2f);
            Assert.True(MathF.Abs(p.Lead - prev.Lead) < 0.2f);
            Assert.True(MathF.Abs(p.Lean - prev.Lean) < 0.2f);
            prev = p;
        }
    }

    [Fact]
    public void LiftQuad_MovesQuadUpScreen_NotDown()
    {
        // Screen Y grows DOWNWARD: a lift must SUBTRACT. Regression guard
        // for the inverted-sign leap (body sank while the boots flew).
        var q = new BillQuad { BLx = 10f, BLy = 100f, BRx = 50f, BRy = 100f, TRx = 50f, TRy = 60f, TLx = 10f, TLy = 60f, Cx = 30f, Cy = 80f };
        SpriteRenderer.LiftQuad(ref q, 26f);
        Assert.Equal(74f, q.BLy, 3);
        Assert.Equal(74f, q.BRy, 3);
        Assert.Equal(34f, q.TRy, 3);
        Assert.Equal(34f, q.TLy, 3);
        Assert.Equal(54f, q.Cy, 3);
        // X untouched — the lift is purely vertical.
        Assert.Equal(10f, q.BLx, 3);
        Assert.Equal(30f, q.Cx, 3);
    }

    // ─── JumpProgress drives it ───────────────────────────────────────

    [Fact]
    public void JumpProgress_ZeroGrounded_RisesMidJump_ResetsOnLand()
    {
        var (player, actions, _) = Jumper();

        Assert.Equal(0f, player.JumpProgress);
        Assert.True(player.TryJump());
        Assert.True(player.IsJumping);

        // Half the duration: t ≈ 0.5 — the arc peak.
        player.UpdateJump(0.5f * Constants.JumpDuration);
        Assert.InRange(player.JumpProgress, 0.45f, 0.55f);
        Assert.True(player.JumpVisualOffset > 0f);

        // Tick to landing: progress returns to 0.
        float dt = 0.016f;
        int steps = (int)(Constants.JumpDuration / dt) + 5;
        for (int i = 0; i < steps; i++)
            player.UpdateJump(dt);

        Assert.False(player.IsJumping);
        Assert.Equal(0f, player.JumpProgress, 3);
        Assert.Equal(0f, player.JumpVisualOffset, 3);
    }

    [Fact]
    public void JumpProgress_ClampedWhenFrameSpikes()
    {
        // A single huge dt must not overshoot: progress clamps to 0 on land.
        var (player, _, _) = Jumper();
        Assert.True(player.TryJump());
        player.UpdateJump(5f);
        Assert.False(player.IsJumping);
        Assert.Equal(0f, player.JumpProgress, 3);
        Assert.Equal(0f, player.JumpVisualOffset, 3);
    }
}
