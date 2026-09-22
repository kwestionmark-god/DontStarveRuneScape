namespace DontStarveRuneScape;

using System;
using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using Silk.NET.SDL;
using DontStarveRuneScape.Core;

using Version = Silk.NET.Windowing.APIVersion;
using Window = Silk.NET.Windowing.Window;

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

        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(1280, 720);
        options.Title = "Don't Starve RuneScape";
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new Version(3, 3));
        options.VSync = smokeBenchFrames == 0; // benchmarks need uncapped frametimes

        var window = Window.Create(options);
        
        var game = new Game(42);
        if (smokeTestPath != null)
            game.SmokeTestPath = smokeTestPath;
        if (smokeBenchFrames > 0)
            game.SmokeBenchFrames = smokeBenchFrames;
        // Benchmark runs also auto-advance past the menus.
        if (smokeBenchFrames > 0 && smokeTestPath == null)
            game.SmokeTestPath = "";

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
}