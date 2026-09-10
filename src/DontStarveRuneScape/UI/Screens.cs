namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.SDL;

/// <summary>
/// TitleScreen — Main menu title screen.
/// </summary>
public sealed class TitleScreen
{
    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// CharacterDefinition — Character creation data.
/// </summary>
public sealed class CharacterDefinition
{
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; } = 0;
    public int AppearanceId { get; set; } = 0;
}

/// <summary>
/// CharacterSelectPanel — Character creation/selection panel.
/// </summary>
public sealed class CharacterSelectPanel
{
    public bool Visible { get; set; } = false;
    private Action<CharacterDefinition?>? _confirmCallback;

    public void SetConfirmCallback(Action<CharacterDefinition?> callback)
    {
        _confirmCallback = callback;
    }

    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// LoadingScreen — World generation / save loading screen.
/// </summary>
public sealed class LoadingScreen
{
    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }
}