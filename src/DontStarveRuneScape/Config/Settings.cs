namespace DontStarveRuneScape.Config;

using System.IO;
using System.Text.Json;

/// <summary>
/// User settings with JSON persistence: display, HUD, and gameplay options.
/// Stored next to the saves (%APPDATA%/DontStarveRuneScape/settings.json);
/// a missing or invalid file falls back to defaults.
/// </summary>
public sealed class Settings
{
    // ─── Video ─────────────────────────────────────────────────────────────
    /// <summary>Window mode; see DisplayModes. Invalid values normalize to Windowed.</summary>
    public string DisplayMode
    {
        get => _displayMode;
        set => _displayMode = DisplayModes.All.Contains(value) ? value : DisplayModes.Windowed;
    }
    private string _displayMode = DisplayModes.Windowed;

    /// <summary>Windowed resolution (also the requested video mode for fullscreen).</summary>
    public int WindowWidth
    {
        get => _windowWidth;
        set => _windowWidth = Math.Clamp(value, 320, 7680);
    }
    private int _windowWidth = 1280;

    public int WindowHeight
    {
        get => _windowHeight;
        set => _windowHeight = Math.Clamp(value, 240, 4320);
    }
    private int _windowHeight = 720;

    public bool VSync { get; set; } = true;

    // ─── HUD ───────────────────────────────────────────────────────────────
    public bool ShowVitalsWindow { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool ShowDamageFlash { get; set; } = true;

    /// <summary>Scale multiplier for the HUD widget windows (0.75–1.5).</summary>
    public float HudScale
    {
        get => _hudScale;
        set => _hudScale = Math.Clamp(value, 0.75f, 1.5f);
    }
    private float _hudScale = 1f;

    // ─── Gameplay ──────────────────────────────────────────────────────────
    /// <summary>Seconds between autosaves; 0 disables autosaving.</summary>
    public float AutosaveInterval
    {
        get => _autosaveInterval;
        set => _autosaveInterval = Math.Max(0f, value);
    }
    private float _autosaveInterval = Constants.AutosaveInterval;

    /// <summary>Test seam: when set, Load/Save use this path instead of the
    /// default per-user settings.json.</summary>
    public static string? PathOverride { get; set; }

    private static string SettingsPath => PathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DontStarveRuneScape", "settings.json");

    /// <summary>Load from disk, or defaults when the file is missing or invalid.</summary>
    public static Settings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new Settings();
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Restore every field to its default value.</summary>
    public void ResetToDefaults()
    {
        var d = new Settings();
        DisplayMode = d.DisplayMode;
        WindowWidth = d.WindowWidth;
        WindowHeight = d.WindowHeight;
        VSync = d.VSync;
        ShowVitalsWindow = d.ShowVitalsWindow;
        ShowNotifications = d.ShowNotifications;
        ShowDamageFlash = d.ShowDamageFlash;
        HudScale = d.HudScale;
        AutosaveInterval = d.AutosaveInterval;
    }
}

/// <summary>
/// Display-mode names and the resolution choices offered in the settings panel.
/// </summary>
public static class DisplayModes
{
    public const string Windowed = "Windowed";
    public const string Borderless = "Borderless";
    public const string Fullscreen = "Fullscreen";

    public static readonly string[] All = [Windowed, Borderless, Fullscreen];

    public static readonly (int Width, int Height)[] Resolutions =
    [
        (1280, 720),
        (1366, 768),
        (1600, 900),
        (1920, 1080),
        (2560, 1440),
    ];
}
