namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Input;

/// <summary>
/// UiInput — Minimal retained-mode mouse helper for panels. Wraps one frame's
/// InputState (mouse position is in screen pixels, y-down, matching DrawScreenQuad).
/// One click can activate at most one widget: TryClick consumes it.
/// Panels poll this from their Update pass; clicks are edge-triggered by InputManager
/// and cleared at the end of Game.Update, so they are invisible by render time.
/// </summary>
public sealed class UiInput
{
    public float MouseX { get; }
    public float MouseY { get; }
    private readonly bool _click;
    private bool _consumed;

    public UiInput(InputState state)
    {
        MouseX = state.MouseX;
        MouseY = state.MouseY;
        _click = state.MouseLeftClick;
    }

    public bool Hovered(float x, float y, float w, float h)
        => MouseX >= x && MouseX <= x + w && MouseY >= y && MouseY <= y + h;

    /// <summary>True once per click for the first hovered rect queried.</summary>
    public bool TryClick(float x, float y, float w, float h)
    {
        if (_consumed || !_click || !Hovered(x, y, w, h)) return false;
        _consumed = true;
        return true;
    }
}
