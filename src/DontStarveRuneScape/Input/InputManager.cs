namespace DontStarveRuneScape.Input;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using Silk.NET.Input;
using Silk.NET.Windowing;
using System.Numerics;

/// <summary>
/// Input manager - uses Silk.NET high-level input abstractions.
/// </summary>
public sealed class InputManager
{
    public InputState InputState { get; } = new();
    private IInputContext? _inputContext;

    /// <summary>Raised on every key-down event; routed by InputRouter based on GameState.</summary>
    public Action<Key>? KeyEvent;

    /// <summary>
    /// Initialize with the window.
    /// </summary>
    public void Initialize(IWindow window)
    {
        _inputContext = window.CreateInput();

        // Subscribe to keyboard events
        foreach (var keyboard in _inputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
            keyboard.KeyChar += OnKeyChar;
        }

        // Subscribe to mouse events
        foreach (var mouse in _inputContext.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnScroll;
        }

        // Handle device connections
        _inputContext.ConnectionChanged += OnConnectionChanged;
    }

    private void OnConnectionChanged(IInputDevice device, bool connected)
    {
        if (connected)
        {
            if (device is IKeyboard keyboard)
            {
                keyboard.KeyDown += OnKeyDown;
                keyboard.KeyUp += OnKeyUp;
                keyboard.KeyChar += OnKeyChar;
            }
            else if (device is IMouse mouse)
            {
                mouse.MouseDown += OnMouseDown;
                mouse.MouseUp += OnMouseUp;
                mouse.MouseMove += OnMouseMove;
                mouse.Scroll += OnScroll;
            }
        }
        else
        {
            if (device is IKeyboard keyboard)
            {
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
                keyboard.KeyChar -= OnKeyChar;
            }
            else if (device is IMouse mouse)
            {
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.MouseMove -= OnMouseMove;
                mouse.Scroll -= OnScroll;
            }
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int arg3)
    {
        HandleKey(key, true);
        KeyEvent?.Invoke(key);
    }

    private void OnKeyUp(IKeyboard keyboard, Key key, int arg3)
    {
        HandleKey(key, false);
    }

    private void OnKeyChar(IKeyboard keyboard, char c)
    {
        // Text input for chat, naming, etc.
    }

    private void OnMouseDown(IMouse mouse, MouseButton button)
    {
        InputState.MouseX = mouse.Position.X;
        InputState.MouseY = mouse.Position.Y;
        HandleMouseButton(button, true);
    }

    private void OnMouseUp(IMouse mouse, MouseButton button)
    {
        InputState.MouseX = mouse.Position.X;
        InputState.MouseY = mouse.Position.Y;
        HandleMouseButton(button, false);
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        InputState.MouseX = position.X;
        InputState.MouseY = position.Y;
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        InputState.ZoomDelta += wheel.Y > 0 ? 1f : -1f;
    }

    private void HandleKey(Key key, bool pressed)
    {
        // Movement (held) — WASD only; arrow keys reserved for camera
        if (key == Key.W) InputState.MoveUp = pressed;
        else if (key == Key.S) InputState.MoveDown = pressed;
        else if (key == Key.A) InputState.MoveLeft = pressed;
        else if (key == Key.D) InputState.MoveRight = pressed;

        // Camera orbit (held)
        if (key == Key.Left) InputState.OrbitCCW = pressed;
        else if (key == Key.Right) InputState.OrbitCW = pressed;
        // Up/Down arrows control pitch (tilt)
        else if (key == Key.Up) InputState.OrbitTiltUp = pressed;
        else if (key == Key.Down) InputState.OrbitTiltDown = pressed;

        // Actions (one-shot on key down)
        if (pressed)
        {
            if (key == Key.E) InputState.Interact = true;
            else if (key == Key.Enter || key == Key.Space) InputState.Confirm = true;
            else if (key == Key.F) InputState.LightFire = true;
            else if (key == Key.J) InputState.Attack = true;
            else if (key == Key.F5) InputState.SaveGame = true;
            else if (key == Key.F3) InputState.ToggleDebugHud = true;

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

            // Hotbar (1-8)
            else if (key >= Key.Number1 && key <= Key.Number8)
            {
                InputState.HotbarSlot = (int)key - (int)Key.Number1;
            }
        }
    }

    private void HandleMouseButton(MouseButton button, bool pressed)
    {
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

    /// <summary>
    /// Dispose input resources.
    /// </summary>
    public void Dispose()
    {
        if (_inputContext != null)
        {
            foreach (var keyboard in _inputContext.Keyboards)
            {
                keyboard.KeyDown -= OnKeyDown;
                keyboard.KeyUp -= OnKeyUp;
                keyboard.KeyChar -= OnKeyChar;
            }
            foreach (var mouse in _inputContext.Mice)
            {
                mouse.MouseDown -= OnMouseDown;
                mouse.MouseUp -= OnMouseUp;
                mouse.MouseMove -= OnMouseMove;
                mouse.Scroll -= OnScroll;
            }
            _inputContext.ConnectionChanged -= OnConnectionChanged;
            _inputContext = null;
        }
    }
}