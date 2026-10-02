namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Input;
using Silk.NET.Input;
using DontStarveRuneScape.Render;

/// <summary>
/// SettingsPanel — Video / HUD / Gameplay sections of label + value rows.
/// Clicking a row's value control (or Left/Right/Enter on the selected row)
/// cycles the option; changes apply immediately (display through the Game
/// callback, HUD on the next render, autosave per tick) and persist to
/// settings.json. BACK/Escape returns to the pause menu.
/// </summary>
public sealed class SettingsPanel
{
    public bool Visible { get; set; } = false;
    public int SelectedIndex { get; private set; }
    public GameState ReturnState { get; set; } = GameState.Paused;

    private const string VideoSection = "VIDEO";

    /// <summary>One settings row: an indexed option list with the read/write
    /// delegates into Settings and a value formatter.</summary>
    private sealed class Row(string section, string label, int optionCount,
        Func<Settings, int> index, Action<Settings, int> setIndex,
        Func<Settings, int, string> format)
    {
        public readonly string Section = section;
        public readonly string Label = label;
        public readonly int OptionCount = optionCount;
        private readonly Func<Settings, int> _index = index;
        private readonly Action<Settings, int> _setIndex = setIndex;
        private readonly Func<Settings, int, string> _format = format;

        public string Value(Settings s)
        {
            int idx = _index(s);
            return idx >= 0 && idx < OptionCount ? _format(s, idx) : "?";
        }

        public void Cycle(Settings s, int dir)
        {
            int idx = _index(s);
            if (idx < 0 || idx >= OptionCount) idx = 0;
            _setIndex(s, ((idx + dir) % OptionCount + OptionCount) % OptionCount);
        }
    }

    private static readonly float[] HudScaleOptions = [0.75f, 1f, 1.25f, 1.5f];
    private static readonly float[] AutosaveOptions = [0f, 120f, 300f, 600f];

    private static readonly Row[] Rows =
    [
        new("VIDEO", "Display mode", DisplayModes.All.Length,
            s => Array.IndexOf(DisplayModes.All, s.DisplayMode),
            (s, i) => s.DisplayMode = DisplayModes.All[i],
            (s, i) => DisplayModes.All[i]),
        new("VIDEO", "Resolution", DisplayModes.Resolutions.Length,
            s => ResIndex(s.WindowWidth, s.WindowHeight),
            (s, i) => {
                s.WindowWidth = DisplayModes.Resolutions[i].Width;
                s.WindowHeight = DisplayModes.Resolutions[i].Height;
            },
            (s, i) => $"{DisplayModes.Resolutions[i].Width}×{DisplayModes.Resolutions[i].Height}"),
        new("VIDEO", "VSync", 2,
            s => s.VSync ? 1 : 0, (s, i) => s.VSync = i == 1,
            (s, i) => i == 1 ? "ON" : "OFF"),
        new("HUD", "Vitals window", 2,
            s => s.ShowVitalsWindow ? 1 : 0, (s, i) => s.ShowVitalsWindow = i == 1,
            (s, i) => i == 1 ? "ON" : "OFF"),
        new("HUD", "Notifications", 2,
            s => s.ShowNotifications ? 1 : 0, (s, i) => s.ShowNotifications = i == 1,
            (s, i) => i == 1 ? "ON" : "OFF"),
        new("HUD", "Damage flash", 2,
            s => s.ShowDamageFlash ? 1 : 0, (s, i) => s.ShowDamageFlash = i == 1,
            (s, i) => i == 1 ? "ON" : "OFF"),
        new("HUD", "HUD scale", HudScaleOptions.Length,
            s => Array.IndexOf(HudScaleOptions, s.HudScale),
            (s, i) => s.HudScale = HudScaleOptions[i],
            (s, i) => $"{(int)MathF.Round(HudScaleOptions[i] * 100f)}%"),
        new("GAMEPLAY", "Autosave", AutosaveOptions.Length,
            s => Array.IndexOf(AutosaveOptions, s.AutosaveInterval),
            (s, i) => s.AutosaveInterval = AutosaveOptions[i],
            (s, i) => AutosaveOptions[i] <= 0f ? "OFF" : $"{(int)(AutosaveOptions[i] / 60f)} min"),
    ];

    private static int ResIndex(int width, int height)
    {
        var res = DisplayModes.Resolutions;
        for (int i = 0; i < res.Length; i++)
        {
            if (res[i].Width == width && res[i].Height == height) return i;
        }
        return -1;
    }

    private const float ContentW = 520f;
    private const float ContentH = 394f;   // headers + rows + note + buttons
    private const float SectionHeaderH = 30f;
    private const float RowH = 30f;
    private const float BtnW = 160f, BtnH = 30f;
    private const float CtrlW = 190f;      // clickable value-control width

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _rowX, _rowW;
    private readonly float[] _rowY = new float[Rows.Length];
    private readonly float[] _headerY = new float[Rows.Length];   // band top; -1 = no header
    private float _ctrlX;
    private float _noteY, _btnY;
    private float _resetX, _backX;
    private int _lastHover = -1;

    private Game? _game;                   // for the display-apply callback
    private Settings? _settings;

    /// <summary>Keyboard: Up/Down move the selection; Left/Right cycle backward/
    /// forward; Enter/Space cycle forward.</summary>
    public void HandleKey(Key key)
    {
        if (key == Key.Up)
            SelectedIndex = (SelectedIndex + Rows.Length - 1) % Rows.Length;
        else if (key == Key.Down)
            SelectedIndex = (SelectedIndex + 1) % Rows.Length;
        else if (key == Key.Left || key == Key.Right || key == Key.Enter || key == Key.Space)
            ApplyRow(SelectedIndex, key == Key.Left ? -1 : 1);
    }

    /// <summary>Mouse handling + option cycling; call once per frame while open.</summary>
    public void Update(InputState input, Game game, int screenW, int screenH)
    {
        _game = game;
        _settings = game.Settings;
        if (_settings == null) return;

        Layout(screenW, screenH);
        var ui = new UiInput(input);

        _lastHover = -1;
        for (int i = 0; i < Rows.Length; i++)
        {
            // Click the value control to cycle, the label side to select only.
            if (ui.Hovered(_rowX, _rowY[i], _rowW, RowH))
                _lastHover = i;
            if (ui.TryClick(_ctrlX, _rowY[i], CtrlW, RowH))
                ApplyRow(i, 1);
            else if (ui.TryClick(_rowX, _rowY[i], _rowW - CtrlW, RowH))
                SelectedIndex = i;
        }

        if (ui.Hovered(_resetX, _btnY, BtnW, BtnH))
            _lastHover = Rows.Length;
        else if (ui.Hovered(_backX, _btnY, BtnW, BtnH))
            _lastHover = Rows.Length + 1;

        if (ui.TryClick(_resetX, _btnY, BtnW, BtnH))
        {
            _settings.ResetToDefaults();
            _game?.ApplyDisplaySettings();
            _settings.Save();
        }
        else if (ui.TryClick(_backX, _btnY, BtnW, BtnH))
        {
            _game?.SetState(ReturnState);
        }
    }

    private void ApplyRow(int index, int dir)
    {
        if (_settings == null || index < 0 || index >= Rows.Length) return;
        Rows[index].Cycle(_settings, dir);
        if (Rows[index].Section == VideoSection)
            _game?.ApplyDisplaySettings();
        _settings.Save();
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenW, int screenH)
    {
        Layout(screenW, screenH);
        if (text == null || _settings == null) return;

        PanelChrome.Draw(batch, text, screenW, screenH, "SETTINGS", ContentW, ContentH,
            out _, out _, out _, out _);

        for (int i = 0; i < Rows.Length; i++)
        {
            if (_headerY[i] >= 0f)
            {
                // Section band: label above a hairline at the band's bottom.
                batch.DrawScreenQuad(_cx, _headerY[i] + SectionHeaderH - 4f,
                    ContentW * 0.5f - 8f, 0.5f,
                    PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 120);
                DrawLeft(batch, text, Rows[i].Section, _cx + 4f,
                    _headerY[i] + SectionHeaderH * 0.5f - 5f, 13,
                    PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
            }
            RenderRow(batch, text, i, i == SelectedIndex, i == _lastHover);
        }

        // Centered, matching the pause menu's status line.
        text.DrawText(batch, "Changes apply immediately and are saved.",
            _cx + ContentW * 0.5f, _noteY, 12, 160, 150, 130);
        RenderButton(batch, text, "RESET DEFAULTS", _resetX, selected: false,
            hover: _lastHover == Rows.Length);
        RenderButton(batch, text, "BACK", _backX, selected: false,
            hover: _lastHover == Rows.Length + 1);
    }

    private void RenderRow(PrimitiveBatch batch, TextRenderer text, int i, bool selected, bool hovered)
    {
        var row = Rows[i];
        float y = _rowY[i];
        float cy = y + RowH * 0.5f;

        if (selected || hovered)
            batch.DrawScreenQuad(_rowX + _rowW * 0.5f, cy, _rowW * 0.5f, RowH * 0.5f,
                (byte)(PanelChrome.BorderR / 4), (byte)(PanelChrome.BorderG / 4),
                (byte)(PanelChrome.BorderB / 4));

        DrawLeft(batch, text, row.Label, _rowX + 4f, cy, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: selected);

        // Value control: < > chips at the sides, value centered between them.
        batch.DrawScreenQuad(_ctrlX + CtrlW * 0.5f, cy, CtrlW * 0.5f, (RowH - 6f) * 0.5f, 60, 45, 35);
        byte chipR = selected || hovered ? (byte)250 : (byte)222;
        batch.DrawScreenQuad(_ctrlX + 12f, cy, 9f, 9f, chipR, (byte)(chipR - 28), 150);
        batch.DrawScreenQuad(_ctrlX + CtrlW - 12f, cy, 9f, 9f, chipR, (byte)(chipR - 28), 150);
        text.DrawText(batch, "<", _ctrlX + 12f, cy, 12, 25, 18, 15, bold: true);
        text.DrawText(batch, ">", _ctrlX + CtrlW - 12f, cy, 12, 25, 18, 15, bold: true);
        DrawLeft(batch, text, row.Value(_settings!), _ctrlX + CtrlW * 0.5f, cy, 14,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
    }

    private void RenderButton(PrimitiveBatch batch, TextRenderer text, string label,
        float x, bool selected, bool hover)
    {
        float cx = x + BtnW * 0.5f;
        float cy = _btnY + BtnH * 0.5f;
        if (selected || hover)
            batch.DrawScreenQuad(cx, cy, BtnW * 0.5f, BtnH * 0.5f,
                (byte)(PanelChrome.BorderR / 4), (byte)(PanelChrome.BorderG / 4),
                (byte)(PanelChrome.BorderB / 4));
        batch.DrawScreenQuad(cx, cy, BtnW * 0.5f - 2f, BtnH * 0.5f - 2f, 60, 45, 35);
        text.DrawText(batch, label, cx, cy, 13, PanelChrome.BorderR,
            PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
    }

    private void Layout(int screenW, int screenH)
    {
        // Same chrome math as the other panels (Pad 16 + Title 34).
        float plateH = ContentH + 32f + 34f;
        float cy0 = screenH * 0.5f - plateH * 0.5f;
        _cx = screenW * 0.5f - (ContentW + 32f) * 0.5f + 16f;
        float contentY = cy0 + 34f + 16f;

        _rowX = _cx + 4f;
        _rowW = ContentW - 8f;
        _ctrlX = _rowX + _rowW - CtrlW - 4f;

        float y = contentY + 6f;
        string lastSection = "";
        for (int i = 0; i < Rows.Length; i++)
        {
            if (Rows[i].Section != lastSection)
            {
                _headerY[i] = y;
                y += SectionHeaderH;
                lastSection = Rows[i].Section;
            }
            else
            {
                _headerY[i] = -1f;
            }
            _rowY[i] = y;
            y += RowH;
        }

        // Note line, then the two buttons side by side at the bottom.
        _noteY = y + 8f;
        _btnY = _noteY + 14f;
        _resetX = _rowX + 4f;
        _backX = _rowX + _rowW - BtnW - 4f;
    }

    private static void DrawLeft(PrimitiveBatch batch, TextRenderer text, string s,
        float left, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, left + tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }
}
