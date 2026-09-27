namespace DontStarveRuneScape;

using System;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.SDL;
using DontStarveRuneScape.Core;

using Version = Silk.NET.Windowing.APIVersion;
using Window = Silk.NET.Windowing.Window;
using DontStarveRuneScape.Config;

/// <summary>
/// Main entry point for the DontStarveRuneScape game.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        // --smoketest <path.png>: boot straight into a fresh world, render a few
        // frames, save the framebuffer to the path, and exit.
        string? smokeTestPath = null;
        int smokeBenchFrames = 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--smoketest")
                smokeTestPath = args[i + 1];
            else if (args[i] == "--smoketest-bench")
                smokeBenchFrames = int.Parse(args[i + 1]);
        }

        var game = new Game(42);
        if (smokeTestPath != null)
        {
            game.SmokeTestPath = smokeTestPath;
            // The harness mutates settings via scripted clicks/hooks; keep
            // those writes off the real per-user settings.json.
            Settings.PathOverride = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsr-smoketest-settings.json");
        }
        if (smokeBenchFrames > 0)
            game.SmokeBenchFrames = smokeBenchFrames;
        // Benchmark runs also auto-advance past the menus.
        if (smokeBenchFrames > 0 && smokeTestPath == null)
            game.SmokeTestPath = "";

        // Window options come from the user settings (resolution, mode, vsync).
        var settings = game.Settings!;
        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(settings.WindowWidth, settings.WindowHeight);
        options.Title = "Don't Starve RuneScape";
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new Version(3, 3));
        options.VSync = smokeBenchFrames == 0 && settings.VSync; // benchmarks need uncapped frametimes
        options.WindowState = settings.DisplayMode switch
        {
            DisplayModes.Fullscreen => WindowState.Fullscreen,
            DisplayModes.Borderless => WindowState.Normal, // maximize needs a WM; border hidden below
            _ => WindowState.Normal,
        };
        if (settings.DisplayMode == DisplayModes.Borderless)
            options.WindowBorder = WindowBorder.Hidden;

        var window = Window.Create(options);
        game.DisplaySettingsChanged = s => ApplyDisplay(window, s);

        window.Load += () =>
        {
            var gl = window.CreateOpenGL();
            game.InputManager!.Initialize(window);
            game.InitializeGraphics(gl);
        };
        
        window.Render += (dt) =>
        {
            var gl = window.CreateOpenGL();
            game.Update((float)dt);
            game.Render(gl, window.Size.X, window.Size.Y);
        };
        
        window.Update += (dt) =>
        {
            game.CheckWorldGenComplete();
        };
        
        window.Closing += () =>
        {
            game.DisposeGraphics();
            game.InputManager?.Dispose();
        };
        
        // Handle input events
        window.Run();
    }

    /// <summary>Apply user display settings to the live window (startup and
    /// whenever the settings panel changes a video row). Size is set before
    /// the state change and redundant state sets are skipped: SDL processes
    /// these asynchronously and a Restore after a resize reverts the size.
    /// Borderless uses a hidden border at the chosen resolution (maximize
    /// needs a window manager; SetWindowBordered does not).</summary>
    private static void ApplyDisplay(IWindow window, Settings s)
    {
        window.VSync = s.VSync;
        switch (s.DisplayMode)
        {
            case DisplayModes.Fullscreen:
                if (window.WindowState != WindowState.Fullscreen)
                    window.WindowState = WindowState.Fullscreen;
                window.Size = new Silk.NET.Maths.Vector2D<int>(s.WindowWidth, s.WindowHeight);
                break;
            case DisplayModes.Borderless:
                if (window.WindowBorder != WindowBorder.Hidden)
                    window.WindowBorder = WindowBorder.Hidden;
                if (window.WindowState != WindowState.Normal)
                    window.WindowState = WindowState.Normal;
                window.Size = new Silk.NET.Maths.Vector2D<int>(s.WindowWidth, s.WindowHeight);
                break;
            default: // Windowed
                if (window.WindowBorder != WindowBorder.Resizable)
                    window.WindowBorder = WindowBorder.Resizable;
                if (window.WindowState != WindowState.Normal)
                    window.WindowState = WindowState.Normal;
                window.Size = new Silk.NET.Maths.Vector2D<int>(s.WindowWidth, s.WindowHeight);
                break;
        }
    }
}