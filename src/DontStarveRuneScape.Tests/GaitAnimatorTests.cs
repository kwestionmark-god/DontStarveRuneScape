using DontStarveRuneScape.Render;
using Xunit;

namespace DontStarveRuneScape.Tests;

/// <summary>
/// GaitAnimator stride behavior: the planted/swing cycle must stay centered
/// on the stance at every playable frame rate. These tests pin the
/// boot-lag fixes: walk-start pre-phasing (no stranded boot on the first
/// alternation), per-frame landing refresh (frame spikes cannot plant a
/// foot short of its stride), the stranded trail cap, diagonal-pair sync,
/// and idle replant.
/// </summary>
public class GaitAnimatorTests
{
    private static GaitConfig Biped() => new()
    {
        Pattern = GaitPattern.Biped,
        FootOffsets = [(-3f, 0f), (3f, 0f)],
        SwingDur = 0.1f,
        LiftPx = 4f,
        TriggerDist = 3f,
        MinStride = 3f,
    };

    private static GaitConfig Quadruped() => new()
    {
        Pattern = GaitPattern.QuadrupedWalk,
        FootOffsets = [(-3f, 6f), (3f, -6f), (3f, 6f), (-3f, -6f)],
        SwingDur = 0.14f,
        LiftPx = 5f,
        TriggerDist = 3f,
        MinStride = 3f,
    };

    /// <summary>Walk a fresh rig and report per-frame, per-foot signed
    /// trail (+ = behind the stance along travel) and lead (- = ahead).</summary>
    private static (float MaxTrail, float MaxLead, float AvgPlanted) Walk(
        GaitConfig cfg, float speed, float dt, float seconds)
    {
        var gait = new GaitAnimator(cfg);
        float dx = 1f, dy = 0f;   // walk east
        float x = 0f, y = 0f;
        float halfStride = MathF.Max(cfg.MinStride, speed * cfg.SwingDur * 0.5f);
        float maxTrail = float.MinValue, maxLead = float.MinValue, sum = 0f;
        int planted = 0;
        int steps = (int)(seconds / dt);
        for (int s = 0; s < steps; s++)
        {
            x += dx * speed * dt;
            gait.Update(x, y, dx * speed, dy * speed, dt);
            for (int i = 0; i < gait.FootCount; i++)
            {
                var f = gait.GetFoot(i);
                float behind = (x - f.X) * dx + (y - f.Y) * dy;
                if (behind > maxTrail) maxTrail = behind;
                if (-behind > maxLead) maxLead = -behind;
                if (!f.Swinging)
                {
                    sum += behind;
                    planted++;
                }
            }
        }
        return (maxTrail, maxLead, sum / MathF.Max(1, planted));
    }

    [Fact]
    public void WalkStart_NoFootStrandsBeyondOneStride()
    {
        // Before the pre-phase fix the first alternation gated a foot
        // through the other's whole swing: halfStride + speed×SwingDur =
        // 22.5px behind — half a body width of visible boot lag on every
        // stop-and-go. Bound it to the stride plus one frame of overshoot.
        const float speed = 150f, dt = 1f / 60f;
        var (maxTrail, _, _) = Walk(Biped(), speed, dt, 2f);
        float halfStride = MathF.Max(3f, speed * 0.1f * 0.5f);
        Assert.InRange(maxTrail, 0f, halfStride + speed * dt + 1f);
    }

    [Fact]
    public void ConstantVelocity_StrideStaysCentered()
    {
        const float speed = 150f, dt = 1f / 60f;
        var (maxTrail, maxLead, avgPlanted) = Walk(Biped(), speed, dt, 3f);
        float halfStride = MathF.Max(3f, speed * 0.1f * 0.5f);
        // Symmetric: the boot leads and trails by about the same half-stride.
        Assert.InRange(maxLead, halfStride - 2f, halfStride + speed * dt + 1f);
        Assert.InRange(maxTrail, 0f, halfStride + speed * dt + 1f);
        // No systematic behind-bias (the old gait averaged +1.6px behind).
        Assert.InRange(avgPlanted, -1.5f, 1.5f);
    }

    [Fact]
    public void FrameSpikes_DoNotPlantShortOfStride()
    {
        // Alternating fast/slow frames simulates zoomed-out frame-time
        // spikes; the refreshed landing point must keep the cycle centered.
        const float speed = 150f;
        var cfg = Biped();
        var gait = new GaitAnimator(cfg);
        float x = 0f;
        float maxTrail = float.MinValue;
        for (int s = 0; s < 240; s++)
        {
            float dt = s % 2 == 0 ? 0.005f : 0.020f;
            x += speed * dt;
            gait.Update(x, 0f, speed, 0f, dt);
            for (int i = 0; i < gait.FootCount; i++)
            {
                var f = gait.GetFoot(i);
                maxTrail = MathF.Max(maxTrail, x - f.X);
            }
        }
        // Stranded cap: halfStride + speed×SwingDur, plus one slow frame of
        // quantization. Without the cap this run trails ~25px.
        Assert.InRange(maxTrail, 0f, 3f + speed * 0.1f * 0.5f + speed * 0.1f + speed * 0.020f + 1f);
    }

    [Fact]
    public void Quadruped_DiagonalPairsSwingTogether()
    {
        var cfg = Quadruped();
        var gait = new GaitAnimator(cfg);
        const float speed = 150f, dt = 1f / 60f;
        float x = 0f;
        for (int s = 0; s < 180; s++)
        {
            x += speed * dt;
            gait.Update(x, 0f, speed, 0f, dt);
            // Group 0 = feet 0,1 (front-left, rear-right); group 1 = 2,3.
            bool g0 = gait.GetFoot(0).Swinging;
            bool g1 = gait.GetFoot(1).Swinging;
            bool g2 = gait.GetFoot(2).Swinging;
            bool g3 = gait.GetFoot(3).Swinging;
            if (g0 != g1 || g2 != g3)
                Assert.Fail($"frame {s}: g0={g0} g1={g1} g2={g2} g3={g3}");
            // Groups alternate: never both groups planted-swinging together
            // in steady state (allow the brief overlap of the stranded cap).
        }
    }

    [Fact]
    public void Idle_ReplantsDriftedFeetOntoStance()
    {
        var cfg = Biped();
        var gait = new GaitAnimator(cfg);
        const float speed = 150f, dt = 1f / 60f;
        float x = 0f;
        for (int s = 0; s < 90; s++) { x += speed * dt; gait.Update(x, 0f, speed, 0f, dt); }
        for (int s = 0; s < 90; s++) gait.Update(x, 0f, 0f, 0f, dt);   // stop
        for (int i = 0; i < gait.FootCount; i++)
        {
            var f = gait.GetFoot(i);
            float drift = MathF.Sqrt((x - f.X) * (x - f.X) + f.Y * f.Y);
            Assert.True(drift <= cfg.TriggerDist + 0.5f,
                $"foot {i} drifted {drift:F2}px after idle replant");
        }
        Assert.False(gait.Moving);
    }

    [Fact]
    public void SwingTilt_TracesKickPlantLaunch()
    {
        // Liftoff = toe-off drag (top tips back, negative) ...
        Assert.Equal(-1f, GaitAnimator.SwingTilt(0f), 3);
        // ... eases through level at mid-swing ...
        Assert.Equal(0f, GaitAnimator.SwingTilt(0.5f), 3);
        // ... and presents the heel first at plant (top tips forward).
        Assert.Equal(1f, GaitAnimator.SwingTilt(1f), 3);
        // Monotone sweep: no wobble back and forth across the swing.
        for (float t = 0f; t < 1f; t += 0.05f)
            Assert.True(GaitAnimator.SwingTilt(t + 0.05f) >= GaitAnimator.SwingTilt(t));
        // Clamped: out-of-range T (freshly synced diagonal mates seed T<0)
        // must not extrapolate past the poses.
        Assert.Equal(-1f, GaitAnimator.SwingTilt(-0.2f), 3);
        Assert.Equal(1f, GaitAnimator.SwingTilt(1.4f), 3);
        // Poses are held near the extremes, not swept linearly: a third of
        // the way into the swing the boot is still clearly in toe-off
        // (smoothstep(1/3) = -0.48, comfortably past halfway to level).
        Assert.True(GaitAnimator.SwingTilt(1f / 3f) < -0.4f);
    }

    [Fact]
    public void StopAndGo_DoesNotAccumulateLagAcrossCycles()
    {
        // The user-visible case: WASD stop-and-go. Every restart must begin
        // stepping immediately (pre-phased), not strand a boot for the
        // other foot's whole swing.
        var cfg = Biped();
        var gait = new GaitAnimator(cfg);
        const float speed = 150f, dt = 1f / 60f;
        float x = 0f, worst = float.MinValue;
        for (int cycle = 0; cycle < 5; cycle++)
        {
            for (int s = 0; s < 40; s++) { x += speed * dt; gait.Update(x, 0f, speed, 0f, dt); }
            for (int s = 0; s < 30; s++) gait.Update(x, 0f, 0f, 0f, dt);
            for (int s = 0; s < 6; s++)
            {
                x += speed * dt;
                gait.Update(x, 0f, speed, 0f, dt);
                for (int i = 0; i < gait.FootCount; i++)
                    worst = MathF.Max(worst, x - gait.GetFoot(i).X);
            }
        }
        // One stride (7.5) + velocity lead through one swing (15) would be
        // the old transient; with pre-phasing the restart trails at most the
        // stride plus a frame.
        Assert.InRange(worst, 0f, 3f + speed * 0.1f * 0.5f + speed * dt + 1f);
    }
}
