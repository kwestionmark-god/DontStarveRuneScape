namespace DontStarveRuneScape.Tests;

using System;
using System.IO;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using Silk.NET.Input;
using Xunit;

/// <summary>
/// Settings defaults, JSON round-trip through a temp file (PathOverride keeps
/// tests off the real per-user settings.json), clamping/validation, reset,
/// and the settings panel's cycle + display-callback wiring.
/// </summary>
public class SettingsTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(),
        $"dsr-settings-test-{Guid.NewGuid():N}.json");

    public SettingsTests()
    {
        Settings.PathOverride = _tmp;
    }

    public void Dispose()
    {
        Settings.PathOverride = null;
        if (File.Exists(_tmp)) File.Delete(_tmp);
    }

    [Fact]
    public void Defaults_MatchExpectedValues()
    {
        var s = new Settings();
        Assert.Equal(DisplayModes.Windowed, s.DisplayMode);
        Assert.Equal(1280, s.WindowWidth);
        Assert.Equal(720, s.WindowHeight);
        Assert.True(s.VSync);
        Assert.True(s.ShowVitalsWindow);
        Assert.True(s.ShowNotifications);
        Assert.True(s.ShowDamageFlash);
        Assert.Equal(1f, s.HudScale);
        Assert.Equal(300f, s.AutosaveInterval);
    }

    [Fact]
    public void JsonRoundTrip_PreservesChangedValues()
    {
        var s = new Settings
        {
            DisplayMode = DisplayModes.Fullscreen,
            WindowWidth = 1920,
            WindowHeight = 1080,
            VSync = false,
            ShowVitalsWindow = false,
            ShowNotifications = false,
            ShowDamageFlash = false,
            HudScale = 1.25f,
            AutosaveInterval = 120f,
        };
        s.Save();
        var loaded = Settings.Load();

        Assert.Equal(s.DisplayMode, loaded.DisplayMode);
        Assert.Equal(s.WindowWidth, loaded.WindowWidth);
        Assert.Equal(s.WindowHeight, loaded.WindowHeight);
        Assert.False(loaded.VSync);
        Assert.False(loaded.ShowVitalsWindow);
        Assert.False(loaded.ShowNotifications);
        Assert.False(loaded.ShowDamageFlash);
        Assert.Equal(1.25f, loaded.HudScale);
        Assert.Equal(120f, loaded.AutosaveInterval);
    }

    [Fact]
    public void InvalidValues_AreClampedOrNormalized()
    {
        var s = new Settings
        {
            DisplayMode = "bogus",
            HudScale = 99f,
            AutosaveInterval = -5f,
            WindowWidth = 1,
            WindowHeight = 100000,
        };
        Assert.Equal(DisplayModes.Windowed, s.DisplayMode);
        Assert.Equal(1.5f, s.HudScale);
        Assert.Equal(0f, s.AutosaveInterval);
        Assert.Equal(320, s.WindowWidth);
        Assert.Equal(4320, s.WindowHeight);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = Settings.Load();
        Assert.Equal(DisplayModes.Windowed, s.DisplayMode);
        Assert.True(s.VSync);
    }

    [Fact]
    public void ResetToDefaults_RestoresEveryField()
    {
        var s = new Settings
        {
            DisplayMode = DisplayModes.Borderless,
            VSync = false,
            ShowVitalsWindow = false,
            HudScale = 0.75f,
            AutosaveInterval = 0f,
        };
        s.ResetToDefaults();

        Assert.Equal(DisplayModes.Windowed, s.DisplayMode);
        Assert.True(s.VSync);
        Assert.True(s.ShowVitalsWindow);
        Assert.Equal(1f, s.HudScale);
        Assert.Equal(300f, s.AutosaveInterval);
    }

    [Fact]
    public void PausedAndSettingsStates_AreNotPanelStates_WorldStaysFrozen()
    {
        Assert.False(GameStateExtensions.IsPanelState(GameState.Paused));
        Assert.False(GameStateExtensions.IsPanelState(GameState.SettingsPanel));

        var game = new Game();
        var world = new DontStarveRuneScape.World.TileMap(4, 4);
        world.SeasonSystem = new DontStarveRuneScape.Seasons.SeasonSystem();
        var node = new DontStarveRuneScape.World.ResourceNode
        {
            ResourceDef = new DontStarveRuneScape.Data.ResourceDef { Regrow = 60, Seasons = [] },
            Density = 0.5f,
            MaxDensity = 1f,
            RegrowTime = 10f,
        };
        world.GetTile(1, 1)!.ResourceNode = node;
        game.World = world;
        game.SetState(GameState.Paused);

        game.Update(1f);

        Assert.Equal(10f, node.RegrowTime);
    }

    [Fact]
    public void PlayerDoesNotMoveWhilePaused()
    {
        var game = new Game();
        var player = new DontStarveRuneScape.Core.Player(100f, 100f);
        game.Player = player;
        game.SetState(GameState.Paused);
        game.InputManager!.InputState.MoveLeft = true;

        game.Update(0.5f);

        Assert.Equal(100f, player.WorldX);
    }

    [Fact]
    public void SettingsPanel_VSyncCycle_TogglesSavesAndRaisesDisplayCallback()
    {
        var game = new Game();
        Settings? applied = null;
        game.DisplaySettingsChanged = s => applied = s;
        game.SetState(GameState.SettingsPanel);
        Assert.True(game.SettingsPanel!.Visible);

        // Bind the panel's settings/game refs, select the VSync row (index 2), cycle.
        game.SettingsPanel.Update(new InputState(), game, 1280, 720);
        game.SettingsPanel.HandleKey(Key.Down);
        game.SettingsPanel.HandleKey(Key.Down);
        Assert.Equal(1f, game.Settings!.HudScale); // untouched rows stay put
        game.SettingsPanel.HandleKey(Key.Enter);

        Assert.False(game.Settings.VSync);
        Assert.NotNull(applied);
        Assert.True(File.Exists(_tmp));

        var reloaded = Settings.Load();
        Assert.False(reloaded.VSync);
    }

    [Fact]
    public void SettingsPanel_BackButton_ReturnsToPauseMenu()
    {
        var game = new Game();
        game.SetState(GameState.SettingsPanel);
        game.SettingsPanel!.Update(new InputState(), game, 1280, 720);

        // BACK button (bottom right; center computed from the Layout math:
        // contentY 180, note 524, buttons 538..568, x 732..892).
        var state = new InputState { MouseX = 812f, MouseY = 553f, MouseLeftClick = true };
        game.SettingsPanel.Update(state, game, 1280, 720);

        Assert.Equal(GameState.Paused, game.State);
        Assert.True(game.PauseMenu!.Visible);
        Assert.False(game.SettingsPanel.Visible);
    }
}
