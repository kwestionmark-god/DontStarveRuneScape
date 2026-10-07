namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using Xunit;

/// <summary>
/// Sprint mode: holding Shift while moving multiplies EffectiveSpeed by
/// SprintSpeedMultiplier and continuously drains the shared gathering
/// StaminaPool (StaminaPool.Consume per frame). The existing pool semantics
/// carry the rest: empty pool → the 5s exhausted rest with a full restore,
/// 2/s regen otherwise. Sprint must not drain while standing still or while
/// the key is up, must be gated while exhausted, and must notify exactly
/// once on the sprint-to-exhausted edge.
/// </summary>
public class SprintTests
{
    private static (Player player, ActionSystem actions) SprintingPlayer()
    {
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        player.ActionSystem = actions;
        return (player, actions);
    }

    [Fact]
    public void SprintHeldWhileMoving_DrainsStaminaAndFlagsSprinting()
    {
        var (player, actions) = SprintingPlayer();

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);

        Assert.True(player.Sprinting);
        Assert.Equal(Constants.MaxStaminaBase - Constants.SprintStaminaDrainPerSecond * 0.5f,
            actions.Stamina.Current, 3);
    }

    [Fact]
    public void SprintHeldWhileStanding_DoesNotDrainOrFlag()
    {
        var (player, actions) = SprintingPlayer();

        player.UpdateSprint(sprintHeld: true, moving: false, dt: 0.5f);
        player.UpdateSprint(sprintHeld: true, moving: false, dt: 0.5f);

        Assert.False(player.Sprinting);
        Assert.Equal(Constants.MaxStaminaBase, actions.Stamina.Current, 3);
    }

    [Fact]
    public void SprintKeyReleased_MovementStopsDrainAndSprintingClears()
    {
        var (player, actions) = SprintingPlayer();

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        player.UpdateSprint(sprintHeld: false, moving: true, dt: 0.5f);

        Assert.False(player.Sprinting);
        Assert.Equal(Constants.MaxStaminaBase - 2f, actions.Stamina.Current, 3);
    }

    [Fact]
    public void SprintDrainWithoutActionSystem_NoSprintNoCrash()
    {
        var player = new Player(1000f, 1000f); // ActionSystem stays null

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);

        Assert.False(player.Sprinting);
    }

    [Fact]
    public void SprintBoostsEffectiveSpeedByMultiplier()
    {
        var (player, _) = SprintingPlayer();
        float walk = player.EffectiveSpeed;

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);

        Assert.Equal(walk * Constants.SprintSpeedMultiplier, player.EffectiveSpeed, 3);
    }

    [Fact]
    public void SprintExhaustsPool_StopsSprintingAndNotifiesOnce()
    {
        var (player, actions) = SprintingPlayer();

        // Drain until the pool trips its exhausted rest (dt=0.5 → 2 stamina
        // per call; 15 calls spend the 30-point pool, the 16th trips over).
        bool exhausted = false;
        for (int i = 0; i < 100 && !exhausted; i++)
        {
            player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
            exhausted = actions.Stamina.IsExhausted;
        }

        Assert.True(exhausted, "sprint should exhaust the pool within 100 half-second frames");
        Assert.False(player.Sprinting, "sprint must stop once the pool is exhausted");
        Assert.Equal(0f, actions.Stamina.Current, 3);

        var shown = actions.FlushNotifications();
        var winded = shown.Where(n => n.Text.Contains("exhausted", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(winded);

        // Further held-key frames while exhausted: still gated, no repeat spam.
        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        Assert.Empty(actions.FlushNotifications()
            .Where(n => n.Text.Contains("exhausted", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SprintPreExhaustedPool_IsSilentlyGated()
    {
        var (player, actions) = SprintingPlayer();
        actions.Stamina.Consume(Constants.MaxStaminaBase); // exhaust via gathering

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);

        Assert.False(player.Sprinting);
        Assert.Empty(actions.FlushNotifications());
    }

    [Fact]
    public void SprintResumesAfterExhaustionRest()
    {
        var (player, actions) = SprintingPlayer();

        bool exhausted = false;
        for (int i = 0; i < 100 && !exhausted; i++)
        {
            player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
            exhausted = actions.Stamina.IsExhausted;
        }
        actions.FlushNotifications();

        // The pool's forced rest: 5s tick restores the pool to full.
        actions.Stamina.Tick(5.0f);
        Assert.False(actions.Stamina.IsExhausted);
        Assert.Equal(actions.Stamina.MaxStamina, actions.Stamina.Current, 3);

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        Assert.True(player.Sprinting, "sprint must resume once the rest refills the pool");
        Assert.Equal(actions.Stamina.MaxStamina - 2f, actions.Stamina.Current, 3);
    }

    [Fact]
    public void GameUpdate_SprintHeldMovesPlayerFasterAndDrainsStamina()
    {
        var game = new Game();
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        player.ActionSystem = actions;
        game.Player = player;
        game.SetState(GameState.Playing);
        var st = game.InputManager!.InputState;
        st.MoveLeft = true;
        st.Sprint = true;

        game.Update(0.5f);

        // 1.5× the walk distance for the same frame time.
        float sprintDistance = 1000f - player.WorldX;
        Assert.Equal(
            Constants.PlayerMovementSpeed * 0.5f * Constants.SprintSpeedMultiplier,
            sprintDistance, 3);
        Assert.True(player.Sprinting);
        Assert.Equal(Constants.MaxStaminaBase - Constants.SprintStaminaDrainPerSecond * 0.5f,
            actions.Stamina.Current, 3);
    }

    [Fact]
    public void GameUpdate_WalkWithoutSprintCoversBaseDistance()
    {
        var game = new Game();
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        player.ActionSystem = actions;
        game.Player = player;
        game.SetState(GameState.Playing);
        var st = game.InputManager!.InputState;
        st.MoveLeft = true;

        game.Update(0.5f);

        Assert.Equal(Constants.PlayerMovementSpeed * 0.5f, 1000f - player.WorldX, 3);
        Assert.False(player.Sprinting);
        Assert.Equal(Constants.MaxStaminaBase, actions.Stamina.Current, 3);
    }
}
