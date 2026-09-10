namespace DontStarveRuneScape.Input;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using Silk.NET.SDL;

/// <summary>
/// Input manager - translates SDL events into InputState.
/// </summary>
public sealed class InputManager
{
    public InputState InputState { get; } = new();

    /// <summary>
    /// Process an SDL event.
    /// </summary>
    public void ProcessEvent(SDL sdl, Event evt)
    {
        if (evt.Type == EventType.KeyDown || evt.Type == EventType.KeyUp)
        {
            bool pressed = evt.Type == EventType.KeyDown;
            HandleKey(sdl, evt.Key.Key, pressed);
        }
        else if (evt.Type == EventType.MouseButtonDown || evt.Type == EventType.MouseButtonUp)
        {
            bool pressed = evt.Type == EventType.MouseButtonDown;
            HandleMouseButton(sdl, evt.Button.Button, pressed, evt.Button.X, evt.Button.Y);
        }
        else if (evt.Type == EventType.MouseMotion)
        {
            InputState.MouseX = evt.Motion.X;
            InputState.MouseY = evt.Motion.Y;
        }
        else if (evt.Type == EventType.MouseWheel)
        {
            InputState.ZoomDelta += evt.Wheel.Y > 0 ? 1f : -1f;
        }
        else if (evt.Type == EventType.TextInput)
        {
            // Text input for chat, naming, etc.
        }
    }

    private void HandleKey(SDL sdl, Key key, bool pressed)
    {
        // Movement
        if (key == Key.W || key == Key.Up) InputState.MoveUp = pressed;
        else if (key == Key.S || key == Key.Down) InputState.MoveDown = pressed;
        else if (key == Key.A || key == Key.Left) InputState.MoveLeft = pressed;
        else if (key == Key.D || key == Key.Right) InputState.MoveRight = pressed;

        // Camera orbit (arrow keys when not moving)
        // Note: Arrow keys are also used for movement in some contexts
        // We'll use separate handling for camera vs movement based on panel state
        if (key == Key.Left) InputState.OrbitCCW = pressed;
        else if (key == Key.Right) InputState.OrbitCW = pressed;
        else if (key == Key.Up) InputState.OrbitTiltUp = pressed;
        else if (key == Key.Down) InputState.OrbitTiltDown = pressed;
        else if (key == Key.PageUp) InputState.OrbitTiltUp = pressed;
        else if (key == Key.PageDown) InputState.OrbitTiltDown = pressed;

        // Actions (one-shot on key down)
        if (pressed)
        {
            if (key == Key.E) InputState.Interact = true;
            else if (key == Key.Return || key == Key.Space) InputState.Confirm = true;
            else if (key == Key.F) InputState.LightFire = true;
            else if (key == Key.J) InputState.Attack = true;
            else if (key == Key.F5) InputState.SaveGame = true;

            // Panel toggles
            else if (key == Key.C || key == Key.I) InputState.OpenInventory = true;
            else if (key == Key.Tab) InputState.OpenSkillPanel = true;
            else if (key == Key.H) InputState.OpenCrafting = true;
            else if (key == Key.B) InputState.OpenBuildingPanel = true;
            else if (key == Key.U) InputState.OpenQuestPanel = true;
            else if (key == Key.T) InputState.OpenTradePanel = true;
            else if (key == Key.K) InputState.OpenRecruitPanel = true;
            else if (key == Key.L) InputState.OpenDiplomacyPanel = true;
            else if (key == Key.G) InputState.OpenGearPanel = true;
            else if (key == Key.Escape || key == Key.Q) InputState.ClosePanel = true;

            // Hotbar
            else if (key >= Key.N1 && key <= Key.N8)
            {
                InputState.HotbarSlot = (int)key - (int)Key.N1;
            }
        }
    }

    private void HandleMouseButton(SDL sdl, MouseButton button, bool pressed, int x, int y)
    {
        InputState.MouseX = x;
        InputState.MouseY = y;

        if (pressed)
        {
            if (button == MouseButton.Left)
                InputState.MouseLeftClick = true;
            else if (button == MouseButton.Right)
                InputState.MouseRightClick = true;
        }
    }

    /// <summary>
    /// Clear one-shot flags at end of frame.
    /// </summary>
    public void ClearFrame()
    {
        InputState.ClearFrame();
    }
}