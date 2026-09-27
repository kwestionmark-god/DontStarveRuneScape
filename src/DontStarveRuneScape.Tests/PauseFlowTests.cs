namespace DontStarveRuneScape.Tests;

using System;
using System.IO;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Interactions;
using DontStarveRuneScape.Survival;
using Silk.NET.Input;
using Xunit;

/// <summary>
/// Pause flow: Escape pauses/resumes (via InputRouter), placement keeps its
/// cancel behavior, panels close on pause, and the pause menu buttons act.
/// Save targets a temp directory (injected SaveSystem) so tests never touch
/// the real saves.
/// </summary>
public class PauseFlowTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(),
        $"dsr-pause-flow-{Guid.NewGuid():N}.json");

    public PauseFlowTests()
    {
        // Fresh (missing) settings file per test: Load returns defaults, and
        // the settings panel's Save writes here, off the real per-user file.
        Settings.PathOverride = _tmp;
    }

    public void Dispose()
    {
        Settings.PathOverride = null;
        if (File.Exists(_tmp)) File.Delete(_tmp);
    }

    private static (Game game, InputRouter router) MakeGame()
    {
        var game = new Game();
        var router = new InputRouter(game, new InteractSystem(game),
            new FireInteraction(game), new NPCFlows(game));
        return (game, router);
    }

    [Fact]
    public void EscapeKey_PausesThenResumes()
    {
        var (game, router) = MakeGame();
        game.SetState(GameState.Playing);

        router.Handle(Key.Escape);
        Assert.Equal(GameState.Paused, game.State);
        Assert.True(game.PauseMenu!.Visible);

        router.Handle(Key.Escape);
        Assert.Equal(GameState.Playing, game.State);
        Assert.False(game.PauseMenu.Visible);
    }

    [Fact]
    public void EscapeKey_DuringPlacement_CancelsPlacementWithoutPausing()
    {
        var (game, router) = MakeGame();
        game.SetState(GameState.Playing);
        game.BuildMode = true;
        game.BuildingPendingId = "campfire";

        router.Handle(Key.Escape);
        Assert.Equal(GameState.Playing, game.State);

        // A real Escape also raises ClosePanel (InputManager does this alongside
        // the router event); Game.Update's placement path consumes it.
        game.InputManager!.InputState.ClosePanel = true;
        game.Update(0.016f);
        Assert.False(game.BuildMode);
        Assert.Null(game.BuildingPendingId);
    }

    [Fact]
    public void EnteringPaused_ClosesGameplayPanels()
    {
        var game = new Game();
        game.InventoryPanel = new DontStarveRuneScape.UI.InventoryPanel();
        game.SetState(GameState.InventoryOpen);

        game.SetState(GameState.Paused);

        Assert.False(game.InventoryPanel!.Visible);
        Assert.True(game.PauseMenu!.Visible);
    }

    [Fact]
    public void PauseMenu_SettingsButton_OpensSettingsPanel()
    {
        var (game, _) = MakeGame();
        game.SetState(GameState.Paused);

        // SETTINGS is the second button (y 330..368, x 530..750 at 1280x720).
        var state = new InputState { MouseX = 640f, MouseY = 349f, MouseLeftClick = true };
        game.PauseMenu!.Update(state, 1280, 720);

        Assert.Equal(GameState.SettingsPanel, game.State);
        Assert.True(game.SettingsPanel!.Visible);
        Assert.False(game.PauseMenu.Visible);
    }

    [Fact]
    public void PauseMenu_SaveButton_WritesInjectedSaveSlot()
    {
        var (game, _) = MakeGame();
        var saveDir = Path.Combine(Path.GetTempPath(), $"dsr-pause-save-test-{Guid.NewGuid():N}");
        game.SaveSystem = new SaveSystem(saveDir);
        game.SetState(GameState.Paused);

        // SAVE GAME is the third button (y 374..412, x 530..750 at 1280x720).
        var state = new InputState { MouseX = 640f, MouseY = 393f, MouseLeftClick = true };
        game.PauseMenu!.Update(state, 1280, 720);

        Assert.True(File.Exists(Path.Combine(saveDir, "slot_0.json")));
        Assert.Equal(GameState.Paused, game.State); // stays open with the status line
    }

    [Fact]
    public void PauseMenu_QuitToTitle_ReturnsToTitleScreen()
    {
        var (game, _) = MakeGame();
        game.SetState(GameState.Paused);

        // QUIT TO TITLE is the fourth button (y 418..456, x 530..750).
        var state = new InputState { MouseX = 640f, MouseY = 437f, MouseLeftClick = true };
        game.PauseMenu!.Update(state, 1280, 720);

        Assert.Equal(GameState.Title, game.State);
        Assert.False(game.PauseMenu.Visible);
    }

    [Fact]
    public void PauseMenu_KeyboardNavigation_MovesSelectionAndConfirms()
    {
        var (game, _) = MakeGame();
        var saveDir = Path.Combine(Path.GetTempPath(), $"dsr-pause-kb-test-{Guid.NewGuid():N}");
        game.SaveSystem = new SaveSystem(saveDir);
        game.SetState(GameState.Paused);

        // Down twice lands on SAVE GAME; Enter/Space confirms it.
        game.PauseMenu!.HandleKey(Key.Down);
        game.PauseMenu.HandleKey(Key.Down);
        game.PauseMenu.HandleConfirm();

        Assert.True(File.Exists(Path.Combine(saveDir, "slot_0.json")));
    }

    [Fact]
    public void PausedState_FreezesSurvivalTicks()
    {
        var (game, _) = MakeGame();
        // World generation runs on a thread; inject Survival directly.
        game.Survival = new SurvivalSystem();
        game.SetState(GameState.Playing);

        game.Update(1f);
        float playingHunger = game.Survival.Hunger;

        game.SetState(GameState.Paused);
        game.Update(1f);
        game.Update(1f);

        // Playing decays hunger; the pause menu freezes it (no system ticks).
        Assert.True(game.Survival.Hunger < 100f);
        Assert.Equal(playingHunger, game.Survival.Hunger);
    }

    [Fact]
    public void SettingsPanel_DisplayModeRow_CyclesBothDirectionsAndApplies()
    {
        var (game, _) = MakeGame();
        int applied = 0;
        game.DisplaySettingsChanged = _ => applied++;
        game.SetState(GameState.SettingsPanel);
        game.SettingsPanel!.Update(new InputState(), game, 1280, 720);

        // Row 0 is Display mode (Windowed): Right → Borderless, Right → Fullscreen.
        game.SettingsPanel.HandleKey(Key.Right);
        Assert.Equal(DisplayModes.Borderless, game.Settings!.DisplayMode);
        game.SettingsPanel.HandleKey(Key.Right);
        Assert.Equal(DisplayModes.Fullscreen, game.Settings!.DisplayMode);
        // Left cycles backward.
        game.SettingsPanel.HandleKey(Key.Left);
        Assert.Equal(DisplayModes.Borderless, game.Settings!.DisplayMode);
        Assert.Equal(3, applied);

        // Resolution row (index 1) cycles into the list.
        game.SettingsPanel.HandleKey(Key.Down);
        game.SettingsPanel.HandleKey(Key.Enter);
        Assert.Equal(1366, game.Settings!.WindowWidth);
        Assert.Equal(768, game.Settings!.WindowHeight);
        Assert.Equal(4, applied);
    }
}
