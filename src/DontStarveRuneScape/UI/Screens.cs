namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Survival;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.SDL;

/// <summary>Main menu and ten-slot save browser.</summary>
public sealed class TitleScreen
{
    private enum Page { Main, Load }
    private static readonly string[] MainChoices = ["NEW GAME", "CONTINUE", "LOAD GAME", "SETTINGS", "QUIT"];
    private static readonly string[] Flavors =
    [
        "The forest keeps its own counsel.",
        "Gather what you need. Leave enough for tomorrow.",
        "The nights are long beyond the campfire.",
        "Every world begins with a single footprint.",
    ];

    private Page _page;
    private int _selected;
    private int _hover = -1;
    private SaveSlotInfo[] _slots = [];

    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }

    public void HandleKey(Game game, Key key)
    {
        if (_page == Page.Main)
        {
            if (key == Key.Up) MoveSelection(-1, game);
            else if (key == Key.Down) MoveSelection(1, game);
        }
        else
        {
            int count = _slots.Length + 1; // final row is BACK
            if (key == Key.Up) _selected = (_selected + count - 1) % count;
            else if (key == Key.Down) _selected = (_selected + 1) % count;
        }
    }

    public void HandleConfirm(Game game)
    {
        if (_page == Page.Main)
        {
            if (IsEnabled(_selected, game)) Activate(game, _selected);
            return;
        }

        if (_selected == _slots.Length)
        {
            _page = Page.Main;
            _selected = 0;
        }
        else if (_selected >= 0 && _selected < _slots.Length && _slots[_selected].HasData)
        {
            game.StartLoadingSave(_slots[_selected].Slot);
        }
    }

    public void HandleBack(Game game)
    {
        if (_page == Page.Load)
        {
            _page = Page.Main;
            _selected = 0;
        }
        else
        {
            game.RequestExit?.Invoke();
        }
    }

    /// <summary>Mouse hit testing; call once per frame while the title is visible.</summary>
    public void Update(Game game, InputState input, int screenWidth, int screenHeight)
    {
        var ui = new UiInput(input);
        _hover = -1;
        if (_page == Page.Main)
        {
            GetMainLayout(screenHeight, out float top, out float rowH, out float gap);
            for (int i = 0; i < MainChoices.Length; i++)
            {
                float y = top + i * (rowH + gap);
                if (ui.Hovered(screenWidth * .5f - 160f, y, 320f, rowH)) _hover = i;
                if (ui.TryClick(screenWidth * .5f - 160f, y, 320f, rowH))
                {
                    _selected = i;
                    if (IsEnabled(i, game)) Activate(game, i);
                    input.MouseLeftClick = false;
                    return;
                }
            }
        }
        else
        {
            GetLoadLayout(screenWidth, screenHeight, out float x, out float top, out float width, out float rowH, out float gap);
            int rowCount = _slots.Length + 1;
            for (int i = 0; i < rowCount; i++)
            {
                float y = top + i * (rowH + gap);
                if (ui.Hovered(x, y, width, rowH)) _hover = i;
                if (!ui.TryClick(x, y, width, rowH)) continue;
                _selected = i;
                if (i < _slots.Length && _slots[i].HasData)
                    game.StartLoadingSave(_slots[i].Slot);
                else if (i == _slots.Length)
                    HandleConfirm(game);
                input.MouseLeftClick = false;
                return;
            }
        }

        // Title/menu clicks must not leak through to another screen opened this frame.
        if (input.MouseLeftClick) input.MouseLeftClick = false;
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight, float time, Game game)
    {
        RenderBackdrop(batch, screenWidth, screenHeight, time);
        if (text == null) return;
        if (_page == Page.Load)
        {
            RenderLoadPage(batch, text, screenWidth, screenHeight, game);
            return;
        }

        float titleY = screenHeight * .20f + MathF.Sin(time * 1.2f) * 2f;
        batch.DrawScreenQuad(screenWidth * .5f, titleY, 355, 35, 40, 24, 12);
        batch.DrawScreenQuad(screenWidth * .5f, titleY, 346, 29, 195, 151, 48);
        text.DrawText(batch, "Don't Starve RuneScape", screenWidth * .5f + 2, titleY + 1, 38,
            38, 24, 12, bold: true);
        text.DrawText(batch, "A SURVIVAL ADVENTURE IN A PROCEDURALLY GENERATED WORLD",
            screenWidth * .5f, titleY + 55, 13, 174, 153, 115, italic: true);

        GetMainLayout(screenHeight, out float top, out float rowH, out float gap);
        for (int i = 0; i < MainChoices.Length; i++)
        {
            bool enabled = IsEnabled(i, game);
            DrawButton(batch, text, screenWidth * .5f - 160, top + i * (rowH + gap), 320, rowH,
                MainChoices[i], i == _selected, i == _hover, enabled);
        }

        RenderSaveSummary(batch, text, screenWidth, screenHeight, game);
        text.DrawText(batch, "↑ / ↓ select     ENTER open     Click to choose",
            screenWidth * .5f, screenHeight - 70, 13, 175, 165, 145);
        text.DrawText(batch, "ESC quit", screenWidth * .5f, screenHeight - 43, 11, 112, 104, 88);
        text.DrawText(batch, Flavors[(int)(time / 8f) % Flavors.Length],
            screenWidth * .5f, screenHeight - 105, 13, 140, 129, 105, italic: true);
    }

    public static void RenderBackdrop(PrimitiveBatch batch, int w, int h, float time)
    {
        const int step = 5;
        for (int y = 0; y < h; y += step)
        {
            float t = y / (float)Math.Max(1, h);
            batch.DrawScreenQuad(w * .5f, y + step * .5f, w * .5f, step,
                (byte)(24 + 24 * t), (byte)(17 + 29 * t), (byte)(13 + 16 * t));
        }
        DrawEmbers(batch, w, h, time);
        DrawCornerBrackets(batch, w, h, time);
    }

    private void RenderSaveSummary(PrimitiveBatch batch, TextRenderer text, int w, int h, Game game)
    {
        float cx = w * .79f, cy = h * .58f;
        batch.DrawScreenQuad(cx, cy, 150, 119, 54, 37, 22, 220);
        batch.DrawScreenQuad(cx, cy - 119, 150, 2, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 150);
        DrawLeft(batch, text, "YOUR LAST SAVE", cx - 128, cy - 82, 12,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
        if (game.SaveSystem?.HasSave(0) == true)
        {
            var info = game.SaveSystem.ListSlots().FirstOrDefault(slot => slot.Slot == 0);
            DrawLeft(batch, text, string.IsNullOrWhiteSpace(info?.CharacterName) ? "Survivor" : info.CharacterName,
                cx - 128, cy - 48, 16, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, true);
            DrawLeft(batch, text, $"Seed {info?.Seed ?? 0}", cx - 128, cy - 17, 12);
            DrawLeft(batch, text, $"Played {FormatTime(info?.PlayTime ?? 0)}", cx - 128, cy + 7, 12);
            DrawLeft(batch, text, $"Deaths {info?.DeathCount ?? 0}", cx - 128, cy + 31, 12);
            DrawLeft(batch, text, "Continue resumes slot 1.", cx - 128, cy + 76, 11, 170, 158, 133);
        }
        else
        {
            DrawLeft(batch, text, "No save file yet.", cx - 128, cy - 39, 15);
            DrawLeft(batch, text, "Start a new world to begin.", cx - 128, cy - 10, 12, 170, 158, 133);
            DrawLeft(batch, text, "Autosaves use slot 1.", cx - 128, cy + 28, 12, 170, 158, 133);
        }
    }

    private void RenderLoadPage(PrimitiveBatch batch, TextRenderer text, int w, int h, Game game)
    {
        text.DrawText(batch, "LOAD WORLD", w * .5f, h * .14f, 30,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
        text.DrawText(batch, "Select a save to continue. Empty slots are available for future saves.",
            w * .5f, h * .19f, 13, 170, 160, 139);
        GetLoadLayout(w, h, out float x, out float top, out float width, out float rowH, out float gap);
        for (int i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            float y = top + i * (rowH + gap);
            DrawSlot(batch, text, x, y, width, rowH, slot, i == _selected, i == _hover);
        }
        float backY = top + _slots.Length * (rowH + gap);
        DrawButton(batch, text, x, backY, width, rowH, "BACK TO MENU",
            _selected == _slots.Length, _hover == _slots.Length, true);
        text.DrawText(batch, "↑ / ↓ select     ENTER load     ESC back",
            w * .5f, h - 34, 12, 155, 145, 123);
    }

    private static void DrawSlot(PrimitiveBatch batch, TextRenderer text, float x, float y, float w, float h,
        SaveSlotInfo slot, bool selected, bool hovered)
    {
        byte r = selected || hovered ? (byte)85 : (byte)37;
        byte g = selected || hovered ? (byte)63 : (byte)27;
        byte b = selected || hovered ? (byte)34 : (byte)18;
        batch.DrawScreenQuad(x + w * .5f, y + h * .5f, w * .5f, h * .5f - 1f, r, g, b, 230);
        string name = string.IsNullOrWhiteSpace(slot.CharacterName) ? "Survivor" : slot.CharacterName;
        string left = slot.HasData ? $"SLOT {slot.Slot + 1}   {name}" : $"SLOT {slot.Slot + 1}   EMPTY";
        string right = slot.HasData
            ? $"{FormatTime(slot.PlayTime)}  ·  Seed {slot.Seed}  ·  {slot.LastModified.ToLocalTime():g}"
            : "No saved world";
        DrawLeft(batch, text, left, x + 14, y + h * .5f, 13,
            slot.HasData ? PanelChrome.TextR : (byte)128,
            slot.HasData ? PanelChrome.TextG : (byte)120,
            slot.HasData ? PanelChrome.TextB : (byte)105, selected);
        var (rw, _) = text.Measure(right, 11);
        text.DrawText(batch, right, x + w - rw * .5f - 12, y + h * .5f, 11, 168, 157, 133);
    }

    private static void GetMainLayout(int h, out float top, out float rowH, out float gap)
    {
        rowH = 44f;
        gap = 9f;
        top = h * .43f;
    }

    private static void GetLoadLayout(int w, int h, out float x, out float top, out float width,
        out float rowH, out float gap)
    {
        width = Math.Min(900f, w * .72f);
        x = (w - width) * .5f;
        rowH = Math.Min(42f, h * .06f);
        gap = 4f;
        top = h * .235f;
    }

    private bool IsEnabled(int index, Game game) => index != 1 || game.SaveSystem?.HasSave(0) == true;

    private void MoveSelection(int direction, Game game)
    {
        for (int tries = 0; tries < MainChoices.Length; tries++)
        {
            _selected = (_selected + direction + MainChoices.Length) % MainChoices.Length;
            if (IsEnabled(_selected, game)) return;
        }
    }

    private void Activate(Game game, int choice)
    {
        switch (choice)
        {
            case 0:
                game.SetState(GameState.CharacterSelect);
                break;
            case 1:
                game.StartLoadingSave(0);
                break;
            case 2:
                _slots = game.SaveSystem?.ListSlots().ToArray() ?? [];
                _page = Page.Load;
                _selected = 0;
                break;
            case 3:
                game.OpenSettings(GameState.Title);
                break;
            case 4:
                game.RequestExit?.Invoke();
                break;
        }
    }

    private static void DrawButton(PrimitiveBatch batch, TextRenderer text, float x, float y, float w, float h,
        string label, bool selected, bool hovered, bool enabled)
    {
        byte r = !enabled ? (byte)40 : selected || hovered ? (byte)105 : (byte)66;
        byte g = !enabled ? (byte)48 : selected || hovered ? (byte)80 : (byte)54;
        byte b = !enabled ? (byte)52 : selected || hovered ? (byte)45 : (byte)28;
        batch.DrawScreenQuad(x + w * .5f, y + h * .5f, w * .5f, h * .5f, 205, 174, 113, selected || hovered ? (byte)235 : (byte)145);
        batch.DrawScreenQuad(x + w * .5f, y + h * .5f, w * .5f - 3f, h * .5f - 3f, r, g, b, 245);
        text.DrawText(batch, label, x + w * .5f, y + h * .5f, 14,
            enabled ? PanelChrome.TextR : (byte)128,
            enabled ? PanelChrome.TextG : (byte)120,
            enabled ? PanelChrome.TextB : (byte)105, bold: selected || hovered);
    }

    private static void DrawLeft(PrimitiveBatch batch, TextRenderer text, string value,
        float left, float y, int size, byte r = PanelChrome.TextR,
        byte g = PanelChrome.TextG, byte b = PanelChrome.TextB, bool bold = false)
    {
        var (width, _) = text.Measure(value, size, bold);
        text.DrawText(batch, value, left + width * .5f, y, size, r, g, b, bold: bold);
    }

    private static void DrawEmbers(PrimitiveBatch batch, int w, int h, float time)
    {
        for (int i = 0; i < 26; i++)
        {
            float cycle = (time * (9 + i % 4 * 2) + i * 71.3f) % (h + 70f);
            float x = (i * 113.7f + 47) % w;
            float y = h - cycle;
            byte alpha = (byte)Math.Clamp(90 * MathF.Sin(cycle / (h + 70f) * MathF.PI), 0, 90);
            if (alpha > 0) batch.DrawScreenQuad(x, y, 2f + i % 3, 2f + i % 3, 220, 158, 72, alpha);
        }
    }

    private static void DrawCornerBrackets(PrimitiveBatch batch, int w, int h, float time)
    {
        byte alpha = (byte)(45 + 25 * (.5f + .5f * MathF.Sin(time)));
        const float pad = 28f, len = 38f, thick = 3f;
        foreach (float x in new[] { pad, w - pad })
        foreach (float y in new[] { pad, h - pad })
        {
            float dirX = x < w * .5f ? 1f : -1f;
            float dirY = y < h * .5f ? 1f : -1f;
            batch.DrawScreenQuad(x + dirX * len * .5f, y, len, thick, 180, 140, 80, alpha);
            batch.DrawScreenQuad(x, y + dirY * len * .5f, thick, len, 180, 140, 80, alpha);
        }
    }

    private static string FormatTime(float seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours:D2}:{time.Minutes:D2}" : $"{time.Minutes:D2}:{time.Seconds:D2}";
    }
}

/// <summary>Simple name-only character setup, ready for later appearance/class options.</summary>
public sealed class CharacterDefinition
{
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public int AppearanceId { get; set; }
    /// <summary>Creation-time background id (character backgrounds slice);
    /// defaults to wanderer — the no-choice behavior predates the slice.</summary>
    public string Background { get; set; } = "wanderer";
}

public sealed class CharacterSelectPanel
{
    public bool Visible { get; set; }
    public string Name { get; private set; } = "";
    /// <summary>Currently highlighted background id (character backgrounds
    /// slice); public so tests assert selection without a draw pass.</summary>
    public string SelectedBackgroundId { get; private set; } = "wanderer";
    private Action<CharacterDefinition?>? _confirmCallback;
    private int _hover;

    // Background option row geometry (character backgrounds slice): four
    // cells spanning the panel width under the name box.
    private const float RowInsetX = 240f;
    private const float CellH = 42f;
    private const float RowRelY = 140f;

    public void SetConfirmCallback(Action<CharacterDefinition?> callback) => _confirmCallback = callback;
    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(GL gl, int screenWidth, int screenHeight) { }

    public void HandleTextInput(char character)
    {
        if (!char.IsControl(character) && Name.Length < 18)
            Name += character;
    }

    public void HandleKey(Game game, Key key)
    {
        if (key == Key.Escape)
        {
            game.SetState(GameState.Title);
        }
        else if (key == Key.Backspace && Name.Length > 0)
        {
            Name = Name[..^1];
        }
        else if (key is Key.Right or Key.Down)
        {
            CycleBackground(1);
        }
        else if (key is Key.Left or Key.Up)
        {
            CycleBackground(-1);
        }
        else if (key == Key.Enter)
        {
            Confirm();
        }
    }

    private void CycleBackground(int delta)
    {
        var all = Backgrounds.All;
        int idx = Array.FindIndex(all, b => b.Id == SelectedBackgroundId);
        SelectedBackgroundId = all[((idx < 0 ? 0 : idx) + delta + all.Length) % all.Length].Id;
    }

    public void Update(Game game, InputState input, int screenWidth, int screenHeight)
    {
        float cx = screenWidth * .5f;
        float top = screenHeight * .5f - 124f;
        var ui = new UiInput(input);

        // Background option cells (character backgrounds slice): the row
        // spans the panel width under the name box, four equal cells.
        var backgrounds = Backgrounds.All;
        float cellW = (RowInsetX * 2f) / backgrounds.Length;
        int hoverBackground = -1;
        for (int i = 0; i < backgrounds.Length; i++)
        {
            if (ui.TryClick(cx - RowInsetX + cellW * i, top + RowRelY, cellW, CellH))
            {
                SelectedBackgroundId = backgrounds[i].Id;
                hoverBackground = i;
            }
            else if (ui.Hovered(cx - RowInsetX + cellW * i, top + RowRelY, cellW, CellH))
            {
                hoverBackground = i;
            }
        }

        _hover = ui.Hovered(cx - 240, top + 78, 480, 48) ? 0
            : ui.Hovered(cx - 132, top + 210, 124, 42) ? 1
            : ui.Hovered(cx + 8, top + 210, 124, 42) ? 2 : -1;
        if (ui.TryClick(cx - 132, top + 210, 124, 42)) Confirm();
        else if (ui.TryClick(cx + 8, top + 210, 124, 42)) game.SetState(GameState.Title);
        input.MouseLeftClick = false;
        _hoverBackground = hoverBackground;
    }

    private int _hoverBackground;

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight, float time)
    {
        TitleScreen.RenderBackdrop(batch, screenWidth, screenHeight, time);
        if (text == null) return;
        text.DrawText(batch, "CREATE CHARACTER", screenWidth * .5f, screenHeight * .5f - 194f,
            25, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
        text.DrawText(batch, "Choose a name. You can customize your survivor further later.",
            screenWidth * .5f, screenHeight * .5f - 164f, 13, 174, 160, 140);
        float cx = screenWidth * .5f, top = screenHeight * .5f - 124f;
        batch.DrawScreenQuad(cx, top + 101f, 244, 26, _hover == 0 ? (byte)125 : (byte)83, 66, 37);
        batch.DrawScreenQuad(cx, top + 101f, 240, 22, 30, 21, 14);
        string shownName = Name.Length == 0 ? "Type a name..." : Name;
        text.DrawText(batch, shownName + (Name.Length < 18 ? "_" : ""), cx, top + 101f, 16,
            Name.Length == 0 ? (byte)145 : PanelChrome.TextR,
            Name.Length == 0 ? (byte)135 : PanelChrome.TextG,
            Name.Length == 0 ? (byte)120 : PanelChrome.TextB);
        DrawButton(batch, text, cx - 132, top + 210, 124, 42, "BEGIN", _hover == 1, _hover == 1);
        DrawButton(batch, text, cx + 8, top + 210, 124, 42, "BACK", _hover == 2, _hover == 2);

        // Background option row (character backgrounds slice): four equal
        // cells under the name box; the selected cell highlights and the
        // hovered one brightens.
        var backgrounds = Backgrounds.All;
        float cellW = (RowInsetX * 2f) / backgrounds.Length;
        for (int i = 0; i < backgrounds.Length; i++)
        {
            float bx = cx - RowInsetX + cellW * i;
            bool selected = backgrounds[i].Id == SelectedBackgroundId;
            bool hovered = _hoverBackground == i;
            batch.DrawScreenQuad(bx + cellW * .5f, top + RowRelY + CellH * .5f,
                cellW * .5f - 2f, CellH * .5f - 2f,
                selected ? (byte)205 : hovered ? (byte)170 : (byte)150, 124, 67);
            batch.DrawScreenQuad(bx + cellW * .5f, top + RowRelY + CellH * .5f,
                cellW * .5f - 5f, CellH * .5f - 5f, 54, 39, 24);
            text.DrawText(batch, backgrounds[i].Name, bx + cellW * .5f,
                top + RowRelY + CellH * .5f - 7f, 12,
                selected ? (byte)240 : (byte)200, selected ? (byte)225 : (byte)190,
                selected ? (byte)205 : (byte)165);
            text.DrawText(batch, backgrounds[i].Hint, bx + cellW * .5f,
                top + RowRelY + CellH * .5f + 8f, 8, 165, 150, 120);
        }

        text.DrawText(batch, "Type to enter a name  ·  Arrows pick a background  ·  Enter begin  ·  Esc back",
            cx, top + 272, 12, 158, 148, 127);
    }

    private void Confirm()
    {
        string clean = Name.Trim();
        _confirmCallback?.Invoke(new CharacterDefinition
        {
            Name = string.IsNullOrWhiteSpace(clean) ? "Survivor" : clean,
            Background = SelectedBackgroundId,
        });
    }

    private static void DrawButton(PrimitiveBatch batch, TextRenderer text, float x, float y,
        float w, float h, string label, bool selected, bool hovered)
    {
        float cx = x + w * .5f, cy = y + h * .5f;
        batch.DrawScreenQuad(cx, cy, w * .5f, h * .5f, selected || hovered ? (byte)205 : (byte)150, 124, 67);
        batch.DrawScreenQuad(cx, cy, w * .5f - 3, h * .5f - 3, 54, 39, 24);
        text.DrawText(batch, label, cx, cy, 13, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
    }
}

/// <summary>Progress, current generation phase, seed, and useful tips.</summary>
public sealed class LoadingScreen
{
    private static readonly string[] Tips =
    [
        "Press E near a person or resource to interact.",
        "Food restores hunger, and some meals restore health too.",
        "Build a shelter before nightfall.",
        "Press O for your player dashboard.",
    ];

    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }

    public void Render(PrimitiveBatch batch, TextRenderer? text, Game game, int screenWidth, int screenHeight)
    {
        TitleScreen.RenderBackdrop(batch, screenWidth, screenHeight, game.PlayTime);
        if (text == null) return;
        bool error = game.State == GameState.Error;
        bool loadingSave = game.State == GameState.LoadingSave;
        float cx = screenWidth * .5f;
        float cy = screenHeight * .5f;
        float cardW = Math.Min(680f, screenWidth * .78f);
        batch.DrawScreenQuad(cx, cy, cardW * .5f, 185f, 52, 35, 22, 235);
        batch.DrawScreenQuad(cx, cy - 185, cardW * .5f, 2, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 180);

        string heading = error ? "WORLD COULD NOT START" : loadingSave ? "RESTORING YOUR WORLD" : "PREPARING A NEW WORLD";
        text.DrawText(batch, heading, cx, cy - 135, 23,
            error ? (byte)230 : PanelChrome.BorderR,
            error ? (byte)105 : PanelChrome.BorderG,
            error ? (byte)82 : PanelChrome.BorderB, bold: true);
        text.DrawText(batch, $"Seed  {game.Seed}", cx, cy - 92, 13, 175, 160, 140);

        float barW = Math.Min(560f, screenWidth * .64f);
        float barY = cy - 22f;
        float progress = error ? 1f : Math.Clamp(game.LoadingProgress, 0f, 1f);
        batch.DrawScreenQuad(cx, barY, barW * .5f + 5, 15, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB);
        batch.DrawScreenQuad(cx, barY, barW * .5f, 10, 22, 15, 10);
        if (progress > 0)
            batch.DrawScreenQuad(cx - barW * .5f + barW * progress * .5f, barY,
                barW * progress * .5f, 7, error ? (byte)218 : (byte)214, error ? (byte)83 : (byte)164, 64);
        text.DrawText(batch, error ? "ERROR" : $"{progress:P0}", cx, barY + 32, 12,
            error ? (byte)225 : (byte)205, error ? (byte)110 : (byte)187, 128, bold: true);

        if (error)
        {
            string message = game.GetWorldGenError() ?? "An unknown error occurred.";
            text.DrawText(batch, message, cx, cy + 67, 13, 225, 185, 165);
            text.DrawText(batch, "Press ENTER or ESC to return to the title screen.", cx, cy + 112, 12, 165, 151, 133);
        }
        else
        {
            text.DrawText(batch, Phase(progress, loadingSave), cx, cy + 21, 14, 230, 220, 192);
            text.DrawText(batch, Tips[(int)(game.PlayTime / 5f) % Tips.Length], cx, cy + 100, 13, 170, 160, 140, italic: true);
            text.DrawText(batch, "Your world is being built. This can take a little while.", cx, cy + 144, 11, 127, 117, 99);
        }
    }

    private static string Phase(float progress, bool restoring) => restoring
        ? progress < .85f ? "Recreating the saved world..." : "Restoring your progress..."
        : progress < .1f ? "Preparing game data..."
        : progress < .4f ? "Generating the land..."
        : progress < .6f ? "Shaping biomes and resources..."
        : progress < .9f ? "Populating the world..."
        : "Finishing the world...";
}
