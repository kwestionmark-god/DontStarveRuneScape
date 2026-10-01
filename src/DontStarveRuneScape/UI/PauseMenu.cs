namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using Silk.NET.Input;
using DontStarveRuneScape.Render;

/// <summary>
/// PauseMenu — Centered plate over the frozen world with Resume / Settings /
/// Save Game / Quit to Title buttons, Up/Down + Enter keyboard navigation, and
/// a status line. The world does not tick while paused; the last rendered
/// frame stays visible underneath.
/// </summary>
public sealed class PauseMenu
{
    private readonly Game _game;
    public bool Visible { get; set; } = false;

    private static readonly string[] ButtonLabels =
        ["RESUME", "SETTINGS", "SAVE GAME", "QUIT TO TITLE"];

    private const float ContentW = 300f;
    private const float ContentH = 194f;   // 4 buttons + gaps + status line
    private const float BtnH = 38f;
    private const float BtnGap = 6f;
    private const float StatusH = 24f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _btnX, _btnW;
    private readonly float[] _btnY = new float[ButtonLabels.Length];
    private float _statusY;
    private int _selectedIndex;
    private int _lastHover = -1;
    private string? _status;               // last save result line

    public PauseMenu(Game game)
    {
        _game = game;
    }

    /// <summary>Clear the status line and selection; called when the menu opens fresh.</summary>
    public void Reset()
    {
        _status = null;
        _selectedIndex = 0;
        _lastHover = -1;
    }

    /// <summary>Keyboard: Up/Down move the selection (Enter/Space confirm via HandleConfirm).</summary>
    public void HandleKey(Key key)
    {
        if (key == Key.Up)
            _selectedIndex = (_selectedIndex + ButtonLabels.Length - 1) % ButtonLabels.Length;
        else if (key == Key.Down)
            _selectedIndex = (_selectedIndex + 1) % ButtonLabels.Length;
    }

    /// <summary>Activate the selected button; call from InputRouter on Enter/Space.</summary>
    public void HandleConfirm() => Activate(_selectedIndex);

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, int screenW, int screenH)
    {
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        _lastHover = -1;
        for (int i = 0; i < ButtonLabels.Length; i++)
        {
            if (ui.Hovered(_btnX, _btnY[i], _btnW, BtnH))
                _lastHover = i;
            if (ui.TryClick(_btnX, _btnY[i], _btnW, BtnH))
                Activate(i);
        }
    }

    private void Activate(int i)
    {
        switch (i)
        {
            case 0: // Resume
                _game.SetState(GameState.Playing);
                break;
            case 1: // Settings
                _game.OpenSettings(GameState.Paused);
                break;
            case 2: // Save Game
                _game.SaveSystem?.Save(_game, 0);
                _status = "Game saved.";
                break;
            case 3: // Quit to Title
                _game.SetState(GameState.Title);
                break;
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenW, int screenH)
    {
        Layout(screenW, screenH);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenH, "PAUSED", ContentW, ContentH,
            out _, out _, out _, out _);

        for (int i = 0; i < ButtonLabels.Length; i++)
            RenderButton(batch, text, i, i == _selectedIndex, i == _lastHover);

        // Status line: save feedback, or a hint about the frozen world.
        if (_status != null)
            text.DrawText(batch, _status, _btnX + _btnW * 0.5f, _statusY, 13, 120, 220, 120);
        else
            text.DrawText(batch, "The world is frozen while paused.",
                _btnX + _btnW * 0.5f, _statusY, 12, 160, 150, 130);
    }

    private void RenderButton(PrimitiveBatch batch, TextRenderer text, int i, bool selected, bool hovered)
    {
        float cx = _btnX + _btnW * 0.5f;
        float cy = _btnY[i] + BtnH * 0.5f;

        if (selected || hovered)
            batch.DrawScreenQuad(cx, cy, _btnW * 0.5f, BtnH * 0.5f,
                (byte)(PanelChrome.BorderR / 4), (byte)(PanelChrome.BorderG / 4),
                (byte)(PanelChrome.BorderB / 4));

        batch.DrawScreenQuad(cx, cy, _btnW * 0.5f - 2f, BtnH * 0.5f - 2f, 60, 45, 35);

        byte br = selected ? (byte)250 : PanelChrome.BorderR;
        byte bg = selected ? (byte)225 : PanelChrome.BorderG;
        byte bb = selected ? (byte)180 : PanelChrome.BorderB;
        text.DrawText(batch, ButtonLabels[i], cx, cy, 15, br, bg, bb, bold: true);
    }

    private void Layout(int screenW, int screenH)
    {
        // Same chrome math as the other panels (Pad 16 + Title 34).
        float plateH = ContentH + 32f + 34f;
        float contentY = screenH * 0.5f - plateH * 0.5f + 34f + 16f;

        _btnW = 220f;
        _btnX = screenW * 0.5f - _btnW * 0.5f;
        for (int i = 0; i < ButtonLabels.Length; i++)
            _btnY[i] = contentY + 6f + i * (BtnH + BtnGap);
        _statusY = contentY + 6f + ButtonLabels.Length * (BtnH + BtnGap) + StatusH * 0.5f;
    }
}
