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
    public static void Main()
    {
        var options = WindowOptions.Default;
        options.Size = new Silk.NET.Maths.Vector2D<int>(1280, 720);
        options.Title = "Don't Starve RuneScape";
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new Version(3, 3));
        options.VSync = true;

        var window = Window.Create(options);
        
        var game = new Game(42);
        
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