namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using Xunit;

/// <summary>
/// Click-to-move wiring (deferred-idea, user-reported 2026-10-03: "click
/// to move doesnt even work for me" — Player.MoveTo had zero callers).
/// Laws: a ground left-click in Playing state walks to the clicked world
/// point; any WASD key CANCELS the click-walk immediately; the walk
/// stops on arrival.
/// </summary>
public class ClickToMoveTests
{
    private static Player PlayerAt(float wx, float wy)
        => new Player(wx, wy)
        {
            Inventory = new DontStarveRuneScape.Inventory.Inventory(),
            SkillManager = new DontStarveRuneScape.Skills.SkillManager(),
            Survival = new Survival.SurvivalSystem(),
            Gear = new PlayerGear(),
            ActionSystem = new ActionSystem(),
        };

    [Fact]
    public void MoveTo_ThenUpdate_WalksToTargetAndStops()
    {
        var player = PlayerAt(100f, 100f);
        player.MoveTo(160f, 100f);
        Assert.True(player.Moving);

        for (int i = 0; i < 200 && player.Moving; i++)
            player.Update(1f / 60f);

        Assert.False(player.Moving);
        Assert.Equal(160f, player.WorldX, 1);
    }

    [Fact]
    public void WasdKey_CancelsClickWalk_Immediately()
    {
        var player = PlayerAt(100f, 100f);
        player.MoveTo(300f, 100f);
        Assert.True(player.Moving);

        var input = new InputState { MoveRight = true };
        player.ApplyKeyInput(input, 1f / 60f);

        Assert.False(player.Moving, "a held WASD key cancels the click-walk");
        Assert.True(player.WorldX > 100f, "the key moves the player this frame");
        // The target stays cancelled: further keyless updates don't resume.
        var x0 = player.WorldX;
        player.Update(1f / 60f);
        Assert.Equal(x0, player.WorldX, 3);
    }

    [Fact]
    public void KeylessFrame_LeavesClickWalkRunning()
    {
        var player = PlayerAt(100f, 100f);
        player.MoveTo(300f, 100f);
        player.ApplyKeyInput(new InputState(), 1f / 60f);
        Assert.True(player.Moving, "no keys held = the click-walk continues");
    }
}
