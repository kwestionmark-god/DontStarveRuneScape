namespace DontStarveRuneScape.UI;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Core;

/// <summary>
/// TitleScreen — Main menu title screen.
/// </summary>
public sealed class TitleScreen
{
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }
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

    public void HandleEvent(Game game, Event evt) { }
    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// LoadingScreen — World generation / save loading screen.
/// </summary>
public sealed class LoadingScreen
{
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }
}