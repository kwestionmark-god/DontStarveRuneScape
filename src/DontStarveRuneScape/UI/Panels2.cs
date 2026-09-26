namespace DontStarveRuneScape.UI;

using System.Linq;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Data;

/// <summary>
/// InventoryPanel — 20-slot inventory grid (4 columns x 5 rows) with a detail
/// block for the selected slot: real item sprite (glyph-chip fallback),
/// quantity vs stack size, equipped marker, food stats, and a spoilage bar.
/// Layout is computed in Layout() from the screen size alone, so Update
/// (hit-testing) and Render stay consistent.
/// </summary>
public sealed class InventoryPanel
{
    public bool Visible { get; set; } = false;
    public int SelectedIndex { get; private set; }

    private const int SlotCount = 20;
    private const int Cols = 4;
    private const float ContentW = 700f;
    private const float ContentH = 368f;
    private const float SlotSize = 64f;
    private const float SlotGap = 8f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;                    // content rect origin
    private readonly float[] _slotX = new float[SlotCount];
    private readonly float[] _slotY = new float[SlotCount];
    private float _detailX, _detailY;

    public void HandleKey(Key key)
    {
        if (key == Key.Up)
            SelectedIndex = (SelectedIndex + SlotCount - Cols) % SlotCount;
        else if (key == Key.Down)
            SelectedIndex = (SelectedIndex + Cols) % SlotCount;
        else if (key == Key.Left)
            SelectedIndex = (SelectedIndex + SlotCount - 1) % SlotCount;
        else if (key == Key.Right)
            SelectedIndex = (SelectedIndex + 1) % SlotCount;
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, Inventory inventory, int screenW, int screenH)
    {
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            if (ui.TryClick(_slotX[i], _slotY[i], SlotSize, SlotSize))
                SelectedIndex = i;
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        Inventory inventory, int screenW, int screenHeight)
    {
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "INVENTORY", ContentW, ContentH,
            out _, out _, out _, out _);

        for (int i = 0; i < inventory.Slots.Count; i++)
            RenderSlot(batch, text, sprites, inventory, i);

        RenderDetail(batch, text, sprites, inventory);
    }

    private void RenderSlot(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Inventory inventory, int i)
    {
        var slot = inventory.Slots[i];
        float x = _slotX[i], y = _slotY[i];
        float cx = x + SlotSize * 0.5f, cy = y + SlotSize * 0.5f;
        float half = SlotSize * 0.5f;
        bool hasItem = slot.ItemId != null && slot.Quantity > 0;

        // Well: faint warm hairline for filled slots, plain for empty.
        if (hasItem)
            batch.DrawScreenQuad(cx, cy, half + 1f, half + 1f, 70, 56, 34);
        batch.DrawScreenQuad(cx, cy, half, half, 22, 14, 8);

        // Rings draw even for empty slots, selection on top.
        if (slot.IsEquipped)
            DrawRing(batch, cx, cy, half + 2.5f, half + 2.5f, 2f, 200, 170, 60);
        if (i == SelectedIndex)
            DrawRing(batch, cx, cy, half + 5f, half + 5f, 2f,
                PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB);

        if (!hasItem)
            return;
        string id = slot.ItemId!;
        var display = Data.ItemCatalog.Get(id);

        // Item sprite: real sprite when available, glyph chip fallback.
        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? id) ?? 0;
        if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(cx, cy, half * 0.72f, half * 0.72f, tex, 255, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(cx, cy, 16f, 16f, 60, 48, 36);
            text.DrawText(batch, GlyphOf(display?.Name ?? id), cx, cy, 13,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }

        // Quantity in the slot's bottom-right corner.
        if (slot.Quantity > 1)
            text.DrawText(batch, slot.Quantity.ToString(), x + SlotSize - 7f, y + SlotSize - 8f, 12,
                255, 240, 200, bold: true);
    }

    private void RenderDetail(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Inventory inventory)
    {
        var slot = inventory.Slots[SelectedIndex];

        float dividerX = _detailX - 10f;
        batch.DrawScreenQuad(dividerX, _detailY + 176f, 0.5f, 176f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        if (slot.ItemId == null || slot.Quantity <= 0)
        {
            DrawLeft(batch, text, "Empty slot", _detailX, _detailY + 8f, 16, 150, 140, 130);
            return;
        }

        string id = slot.ItemId;
        var display = Data.ItemCatalog.Get(id);
        string name = display?.Name ?? PrettifyId(id);

        DrawLeft(batch, text, name, _detailX, _detailY + 8f, 19,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);

        // Larger sprite under the name.
        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? id) ?? 0;
        if (tex != 0)
            batch.DrawTexturedScreenQuad(_detailX + 34f, _detailY + 74f, 34f, 34f, tex, 255, 255, 255, 255);

        DrawLeft(batch, text, $"Quantity {slot.Quantity} / {inventory.StackSizeOf(id)}",
            _detailX, _detailY + 124f, 14, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        if (slot.IsEquipped)
            DrawLeft(batch, text, "Equipped", _detailX, _detailY + 148f, 14, 200, 170, 60, bold: true);

        if (display is { IsFood: true })
        {
            string foodLine = $"Food: +{(int)display.HungerRestore} hunger, {(int)display.HpRestore} hp";
            DrawLeft(batch, text, foodLine, _detailX, _detailY + 176f, 14, 150, 200, 120);
        }

        if (slot.MaxSpoilageTime > 0)
        {
            DrawLeft(batch, text, "Freshness", _detailX, _detailY + 208f, 14,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
            float remaining = Math.Clamp((slot.MaxSpoilageTime - slot.SpoilageTimer) / slot.MaxSpoilageTime, 0f, 1f);
            const float barW = 180f, barH = 7f;
            float barX = _detailX + barW * 0.5f;
            batch.DrawScreenQuad(barX, _detailY + 226f, barW * 0.5f, barH * 0.5f, 22, 16, 10);
            if (remaining > 0f)
            {
                byte r = (byte)(120 + 70 * (1f - remaining));
                batch.DrawScreenQuad(barX + barW * remaining * 0.5f, _detailY + 226f,
                    barW * remaining * 0.5f, barH * 0.5f - 1f, r, 190, 90);
            }
        }
    }

    // Gold ring from four thin quads; used for equipped and selected slots.
    private static void DrawRing(PrimitiveBatch batch, float cx, float cy,
        float halfW, float halfH, float t, byte r, byte g, byte b)
    {
        batch.DrawScreenQuad(cx, cy - halfH + t * 0.5f, halfW, t * 0.5f, r, g, b);
        batch.DrawScreenQuad(cx, cy + halfH - t * 0.5f, halfW, t * 0.5f, r, g, b);
        batch.DrawScreenQuad(cx - halfW + t * 0.5f, cy, t * 0.5f, halfH, r, g, b);
        batch.DrawScreenQuad(cx + halfW - t * 0.5f, cy, t * 0.5f, halfH, r, g, b);
    }

    private static string GlyphOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
    }

    private static string PrettifyId(string id) =>
        id.Replace('_', ' ');

    private void Layout(int screenW, int screenH)
    {
        float plateH = ContentH + 32f + 34f;
        float cy = screenH * 0.5f - plateH * 0.5f;
        _cx = screenW * 0.5f - (ContentW + 32f) * 0.5f + 16f;
        _cy = cy + 34f + 16f;

        for (int i = 0; i < SlotCount; i++)
        {
            int col = i % Cols;
            int row = i / Cols;
            _slotX[i] = _cx + col * (SlotSize + SlotGap);
            _slotY[i] = _cy + row * (SlotSize + SlotGap);
        }

        _detailX = _cx + Cols * SlotSize + (Cols - 1) * SlotGap + 20f;
        _detailY = _cy;
    }

    private static void DrawLeft(PrimitiveBatch batch, TextRenderer text, string s,
        float left, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, left + tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }
}

/// <summary>
/// SkillPanel — OSRS-flavored skills grid: one row per skill (glyph, name, level,
/// XP bar), plus a detail block for the selected skill with clickable [+] buttons
/// to spend that skill's unallocated stat points.
/// Layout is computed in Layout() from the screen size alone, so Update (hit-testing)
/// and Render stay consistent.
/// </summary>
public sealed class SkillPanel
{
    public bool Visible { get; set; } = false;
    public int SelectedIndex { get; private set; }

    private static readonly (string Id, string Name, string Glyph, byte R, byte G, byte B)[] Skills =
    {
        ("woodcutting",  "Woodcutting",  "Wc",  96, 160,  84),
        ("mining",       "Mining",       "Mn", 150, 146, 128),
        ("foraging",     "Foraging",     "Fo", 118, 186, 110),
        ("cooking",      "Cooking",      "Ck", 208, 132,  70),
        ("firemaking",   "Firemaking",   "Fm", 220,  96,  48),
        ("crafting",     "Crafting",     "Cr", 186, 152,  96),
        ("metallurgy",   "Metallurgy",   "Mt", 176, 116,  74),
        ("construction", "Construction", "Cs", 168, 124,  82),
        ("intelligence", "Intelligence", "In", 122, 148, 196),
    };

    private static readonly (string Key, string Name)[] SubStats =
    {
        ("success_rate",      "Success rate"),
        ("harvest_boost",     "Harvest boost"),
        ("extra_resources",   "Extra resources"),
        ("efficiency",        "Efficiency"),
        ("stamina_reduction", "Stamina reduction"),
    };

    private const float ContentW = 780f;
    private const float ContentH = 470f;
    private const float RowH = 42f;
    private const float RowGap = 4f;
    private const float HeaderH = 26f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;                    // content rect origin
    private readonly float[] _rowY = new float[Skills.Length];
    private float _rowX, _rowW;
    private float _detailX, _detailY, _detailW;
    private readonly float[] _statY = new float[SubStats.Length];
    private float _plusX, _plusY0;             // [+] button column

    public void HandleKey(Key key)
    {
        if (key == Key.Up)
            SelectedIndex = (SelectedIndex + Skills.Length - 1) % Skills.Length;
        else if (key == Key.Down)
            SelectedIndex = (SelectedIndex + 1) % Skills.Length;
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, SkillManager skills, int screenW, int screenH)
    {
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        for (int i = 0; i < Skills.Length; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
                SelectedIndex = i;
        }

        var skill = skills.GetSkill(Skills[SelectedIndex].Id);
        _lastMouseHoverPlus = -1;
        if (skill.UnallocatedPoints <= 0) return;
        for (int s = 0; s < SubStats.Length; s++)
        {
            if (ui.Hovered(_plusX, _statY[s] - 11f, 22f, 22f))
                _lastMouseHoverPlus = s;
            if (ui.TryClick(_plusX, _statY[s] - 11f, 22f, 22f))
                skills.SpendPoint(Skills[SelectedIndex].Id, SubStats[s].Key);
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SkillManager skills,
        int screenW, int screenHeight)
    {
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "SKILLS", ContentW, ContentH,
            out _, out _, out _, out _);

        int totalLevel = 0, totalPoints = 0;
        foreach (var s in Skills)
        {
            var d = skills.GetSkill(s.Id);
            totalLevel += d.Level;
            totalPoints += d.UnallocatedPoints;
        }
        // Header sits in its own band above the row list and detail block.
        float headerY = _cy - HeaderH + 12f;
        DrawLeft(batch, text, $"Total level: {totalLevel}", _cx + 4f, headerY, 15,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
        string pointsLabel = totalPoints > 0 ? $"Stat points to spend: {totalPoints}" : "No stat points to spend";
        DrawRight(batch, text, pointsLabel, _cx + ContentW - 4f, headerY, 15,
            totalPoints > 0 ? (byte)120 : (byte)140, totalPoints > 0 ? (byte)220 : (byte)130,
            totalPoints > 0 ? (byte)120 : (byte)120);

        for (int i = 0; i < Skills.Length; i++)
            RenderRow(batch, text, skills, i);

        RenderDetail(batch, text, skills);
    }

    private void RenderRow(PrimitiveBatch batch, TextRenderer text, SkillManager skills, int i)
    {
        var (id, name, glyph, r, g, b) = Skills[i];
        var data = skills.GetSkill(id);
        float y = _rowY[i];
        float cy = y + RowH * 0.5f;
        bool selected = i == SelectedIndex;

        if (selected)
            batch.DrawScreenQuad(_rowX + _rowW * 0.5f, cy, _rowW * 0.5f, RowH * 0.5f,
                (byte)(PanelChrome.BorderR / 4), (byte)(PanelChrome.BorderG / 4),
                (byte)(PanelChrome.BorderB / 4));

        // Glyph chip: colored square with a two-letter tag.
        batch.DrawScreenQuad(_rowX + 15f, cy, 13f, 13f, r, g, b);
        text.DrawText(batch, glyph, _rowX + 15f, cy, 12, 20, 14, 8, bold: true);

        DrawLeft(batch, text, name, _rowX + 38f, cy, 15,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: selected);

        // XP bar at the right end of the row.
        const float barW = 130f, barH = 7f;
        float barX = _rowX + _rowW - barW - 54f;
        skills.ProgressToNext(id, out float into, out float needed);
        float fill = needed > 0 ? Math.Clamp(into / needed, 0f, 1f) : 1f;
        batch.DrawScreenQuad(barX + barW * 0.5f, cy, barW * 0.5f, barH * 0.5f, 22, 16, 10);
        if (fill > 0f)
            batch.DrawScreenQuad(barX + barW * fill * 0.5f, cy, barW * fill * 0.5f,
                barH * 0.5f - 1f, r, g, b);

        DrawRight(batch, text, $"Lv {data.Level}", _rowX + _rowW - 4f, cy, 15,
            (byte)Math.Min(255, r + 80), (byte)Math.Min(255, g + 80),
            (byte)Math.Min(255, b + 80), bold: true);
    }

    private void RenderDetail(PrimitiveBatch batch, TextRenderer text, SkillManager skills)
    {
        var (id, name, _, r, g, b) = Skills[SelectedIndex];
        var data = skills.GetSkill(id);

        float dividerX = _detailX - 18f;
        batch.DrawScreenQuad(dividerX, _detailY + 210f, 0.5f, 210f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        DrawLeft(batch, text, name, _detailX, _detailY + 4f, 19, r, g, b, bold: true);

        skills.ProgressToNext(id, out float into, out float needed);
        string xpLine = data.Level >= 99
            ? $"Level {data.Level}  (max)"
            : $"Level {data.Level}   xp {(int)data.Xp:#,0} / {(int)(SkillManager.XpForLevel(data.Level + 1)):#,0}";
        DrawLeft(batch, text, xpLine, _detailX, _detailY + 30f, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        string pts = data.UnallocatedPoints > 0
            ? $"{data.UnallocatedPoints} stat point{(data.UnallocatedPoints == 1 ? "" : "s")} to spend"
            : "No unallocated points";
        DrawLeft(batch, text, pts, _detailX, _detailY + 52f, 14,
            data.UnallocatedPoints > 0 ? (byte)120 : (byte)150,
            data.UnallocatedPoints > 0 ? (byte)220 : (byte)140,
            data.UnallocatedPoints > 0 ? (byte)120 : (byte)130);

        for (int s = 0; s < SubStats.Length; s++)
        {
            float y = _statY[s];
            float value = data.SubStats[SubStats[s].Key];
            DrawLeft(batch, text, SubStats[s].Name, _detailX + 8f, y, 14,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
            DrawRight(batch, text, $"+{(int)value}", _plusX - 34f, y, 14, 200, 190, 160);

            if (data.UnallocatedPoints > 0)
            {
                bool hover = _lastMouseHoverPlus == s;
                byte br = hover ? (byte)250 : (byte)222;
                byte bg = hover ? (byte)225 : (byte)192;
                byte bb = hover ? (byte)180 : (byte)132;
                batch.DrawScreenQuad(_plusX + 11f, y, 11f, 11f, br, bg, bb);
                text.DrawText(batch, "+", _plusX + 11f, y, 16, PanelChrome.PlateR,
                    PanelChrome.PlateG, PanelChrome.PlateB, bold: true);
            }
        }
    }

    // Updated in Update so RenderDetail can hover-highlight the [+] under the mouse.
    private int _lastMouseHoverPlus = -1;

    private void Layout(int screenW, int screenH)
    {
        float plateH = ContentH + 32f + 34f;
        float cy = screenH * 0.5f - plateH * 0.5f;
        _cx = screenW * 0.5f - (ContentW + 32f) * 0.5f + 16f;
        _cy = cy + 34f + 16f + HeaderH;

        _rowX = _cx + 4f;
        _rowW = ContentW * 0.52f;
        for (int i = 0; i < Skills.Length; i++)
            _rowY[i] = _cy + 8f + i * (RowH + RowGap);

        _detailX = _cx + ContentW * 0.58f;
        _detailY = _cy + 10f;
        _detailW = ContentW * 0.40f;
        for (int s = 0; s < SubStats.Length; s++)
            _statY[s] = _detailY + 92f + s * 30f;
        _plusX = _detailX + _detailW - 60f;
    }

    private static void DrawLeft(PrimitiveBatch batch, TextRenderer text, string s,
        float left, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, left + tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }

    private static void DrawRight(PrimitiveBatch batch, TextRenderer text, string s,
        float right, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, right - tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }
}

/// <summary>
/// CraftingPanel — Recipe list (sorted by tier then name, scrolled) with a
/// detail block for the selected recipe: output sprite/name, ingredients with
/// have/need counts, skill/level gate, XP, campfire/food/quest flags, and a
/// clickable CRAFT button with a status line. Layout is computed in Layout()
/// from the screen size alone, so Update (hit-testing) and Render stay
/// consistent.
/// </summary>
public sealed class CraftingPanel
{
    public bool Visible { get; set; } = false;
    public int SelectedIndex { get; private set; }

    private const float ContentW = 780f;
    private const float ContentH = 470f;
    private const float HeaderH = 26f;
    private const float RowH = 40f;
    private const float RowGap = 2f;
    private const int VisibleRows = 10;
    private const float CraftW = 150f, CraftH = 30f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;
    private float _rowX, _rowW;
    private readonly float[] _rowY = new float[VisibleRows];
    private float _detailX, _detailW, _detailY;
    private float _craftX, _craftY;

    private readonly List<Data.CraftRecipe> _sorted = [];
    private int _scroll;
    private string? _status;                 // last craft result line
    private bool _statusOk;
    private readonly List<string> _statusExtra = []; // level-up lines

    public void HandleKey(Key key)
    {
        if (key == Key.Up && SelectedIndex > 0)
            SelectedIndex--;
        else if (key == Key.Down && SelectedIndex < _sorted.Count - 1)
            SelectedIndex++;
        else
            return;
        _status = null;
        _statusExtra.Clear();
        // Keep the selection inside the visible window.
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
    }

    /// <summary>Mouse handling + craft action; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, CraftingSystem crafting, Inventory inventory,
        SkillManager skills, int screenW, int screenH)
    {
        Refresh(crafting);
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                SelectedIndex = _scroll + i;
                _status = null;
                _statusExtra.Clear();
            }
        }

        if (SelectedIndex < _sorted.Count && ui.TryClick(_craftX, _craftY, CraftW, CraftH))
        {
            var result = crafting.Craft(_sorted[SelectedIndex].RecipeId, inventory, skills);
            _status = result.Message;
            _statusOk = result.Success;
            _statusExtra.Clear();
            _statusExtra.AddRange(result.LevelUpMessages);
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        CraftingSystem crafting, Inventory inventory, SkillManager skills,
        int screenW, int screenHeight)
    {
        Refresh(crafting);
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "CRAFTING", ContentW, ContentH,
            out _, out _, out _, out _);

        int craftable = _sorted.Count(r => CanCraft(r, inventory, skills));
        float headerY = _cy - HeaderH + 13f;
        DrawLeft(batch, text, $"Recipes: {_sorted.Count}", _cx, headerY, 15,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        DrawRight(batch, text, $"Craftable now: {craftable}", _cx + ContentW, headerY, 15,
            150, 200, 120, bold: true);

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
            RenderRow(batch, text, sprites, _sorted[_scroll + i], _scroll + i, inventory, skills);

        RenderDetail(batch, text, sprites, inventory, skills);
    }

    private void Refresh(CraftingSystem? crafting)
    {
        _sorted.Clear();
        if (crafting?.Registry == null) return;
        foreach (var recipe in crafting.Registry.Recipes.Values.OrderBy(r => r.Tier).ThenBy(r => r.Name))
            _sorted.Add(recipe);
        if (SelectedIndex >= _sorted.Count)
            SelectedIndex = Math.Max(0, _sorted.Count - 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _sorted.Count - VisibleRows));
    }

    private void RenderRow(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Data.CraftRecipe recipe, int index, Inventory inventory, SkillManager skills)
    {
        float y = _rowY[index - _scroll];
        float cx = _rowX + _rowW * 0.5f, cy = y + RowH * 0.5f;
        bool selected = index == SelectedIndex;
        bool craftable = CanCraft(recipe, inventory, skills);

        // Row well: warm wash for craftable, darker for gated; selection on top.
        if (selected)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 70, 52, 30);
        else if (craftable)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 34, 26, 20);

        var display = Data.ItemCatalog.Get(recipe.OutputItem);
        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? recipe.OutputItem) ?? 0;
        float chipX = _rowX + 18f;
        if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(chipX, cy, 14f, 14f, tex, 255, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(chipX, cy, 13f, 13f, 60, 48, 36);
            text.DrawText(batch, GlyphOf(display?.Name ?? recipe.OutputItem), chipX, cy, 11,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }

        // Name: green when craftable, dim when gated.
        byte r = craftable ? (byte)150 : (byte)140;
        byte g = craftable ? (byte)210 : (byte)130;
        byte b = craftable ? (byte)110 : (byte)120;
        DrawLeft(batch, text, recipe.Name, _rowX + 42f, cy, 15, r, g, b);

        DrawRight(batch, text, $"T{recipe.Tier}", _rowX + _rowW - 70f, cy, 13, 150, 140, 130);
        bool levelOk = skills.GetSkillLevel(recipe.RequiredSkill) >= recipe.RequiredLevel;
        DrawRight(batch, text, $"Lv {recipe.RequiredLevel}", _rowX + _rowW - 12f, cy, 13,
            levelOk ? (byte)150 : (byte)200, levelOk ? (byte)140 : (byte)120, levelOk ? (byte)130 : (byte)110);
    }

    private void RenderDetail(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Inventory inventory, SkillManager skills)
    {
        float dividerX = _detailX - 10f;
        batch.DrawScreenQuad(dividerX, _detailY + 200f, 0.5f, 200f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        if (SelectedIndex >= _sorted.Count)
        {
            DrawLeft(batch, text, "No recipes", _detailX, _detailY + 8f, 16, 150, 140, 130);
            return;
        }

        var recipe = _sorted[SelectedIndex];
        var display = Data.ItemCatalog.Get(recipe.OutputItem);

        DrawLeft(batch, text, recipe.Name, _detailX, _detailY + 8f, 18,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);

        // Output: sprite and quantity x name.
        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? recipe.OutputItem) ?? 0;
        if (tex != 0)
            batch.DrawTexturedScreenQuad(_detailX + 30f, _detailY + 64f, 28f, 28f, tex, 255, 255, 255, 255);
        DrawLeft(batch, text, $"{recipe.OutputQuantity} x {display?.Name ?? recipe.OutputItem}",
            _detailX + 70f, _detailY + 64f, 14, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        // Ingredients: have/need per input, red when short.
        DrawLeft(batch, text, "Ingredients", _detailX, _detailY + 100f, 13, 150, 140, 130);
        int line = 0;
        foreach (var (itemId, need) in recipe.Inputs)
        {
            int have = inventory.GetItemQuantity(itemId);
            bool ok = have >= need;
            var ingDisplay = Data.ItemCatalog.Get(itemId);
            DrawLeft(batch, text, ingDisplay?.Name ?? itemId, _detailX, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            DrawRight(batch, text, $"{have}/{need}", _detailX + _detailW, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            line++;
        }

        // Gate and reward summary.
        DrawLeft(batch, text, $"Requires {recipe.RequiredSkill} Lv {recipe.RequiredLevel}   +{(int)recipe.XpReward} xp",
            _detailX, _detailY + 208f, 13, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        // Flags: campfire / food / quest unlock.
        string flags =
            (recipe.RequiresCampfire ? "Requires campfire   " : "") +
            (recipe.IsFood ? "Food   " : "") +
            (recipe.QuestUnlock != null ? $"Quest: {recipe.QuestUnlock}" : "");
        if (flags.Length > 0)
            DrawLeft(batch, text, flags.TrimEnd(), _detailX, _detailY + 230f, 13, 200, 170, 60);

        // CRAFT button: gold when craftable, dim when gated.
        bool craftable = CanCraft(recipe, inventory, skills);
        byte br = craftable ? PanelChrome.BorderR : (byte)110, bg = craftable ? PanelChrome.BorderG : (byte)95, bb = craftable ? PanelChrome.BorderB : (byte)70;
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f + 2f, CraftH * 0.5f + 2f, br, bg, bb);
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f, CraftH * 0.5f, 30, 20, 10);
        text.DrawText(batch, "CRAFT", _craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, 15, br, bg, bb, bold: true);

        // Status line: last craft result + level-ups.
        float statusY = _craftY + CraftH + 18f;
        if (_status != null)
            DrawLeft(batch, text, _status, _detailX, statusY, 13,
                _statusOk ? (byte)150 : (byte)210, _statusOk ? (byte)210 : (byte)110, _statusOk ? (byte)120 : (byte)100);
        for (int i = 0; i < _statusExtra.Count; i++)
            DrawLeft(batch, text, _statusExtra[i], _detailX, statusY + 20f + i * 18f, 13, 255, 215, 0);
    }

    // Panel-side craftable check for coloring; matches CraftingSystem's gates
    // (skill level + ingredients; campfire is shown as a flag, not enforced).
    private static bool CanCraft(Data.CraftRecipe recipe, Inventory inventory, SkillManager skills)
    {
        if (skills.GetSkillLevel(recipe.RequiredSkill) < recipe.RequiredLevel) return false;
        foreach (var (itemId, quantity) in recipe.Inputs)
        {
            if (inventory.GetItemQuantity(itemId) < quantity) return false;
        }
        return true;
    }

    private static string GlyphOf(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}"
            : name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
    }

    private void Layout(int screenW, int screenH)
    {
        float plateH = ContentH + 32f + 34f;
        float cy = screenH * 0.5f - plateH * 0.5f;
        _cx = screenW * 0.5f - (ContentW + 32f) * 0.5f + 16f;
        _cy = cy + 34f + 16f + HeaderH;

        _rowX = _cx + 4f;
        _rowW = ContentW * 0.52f;
        for (int i = 0; i < VisibleRows; i++)
            _rowY[i] = _cy + 4f + i * (RowH + RowGap);

        _detailX = _cx + ContentW * 0.58f;
        _detailW = ContentW - ContentW * 0.58f - 4f;
        _detailY = _cy + 8f;

        _craftX = _detailX;
        _craftY = _detailY + 258f;
    }

    private static void DrawLeft(PrimitiveBatch batch, TextRenderer text, string s,
        float left, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, left + tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }

    private static void DrawRight(PrimitiveBatch batch, TextRenderer text, string s,
        float right, float centerY, int size, byte r, byte g, byte b, bool bold = false)
    {
        var (tw, _) = text.Measure(s, size, bold);
        text.DrawText(batch, s, right - tw * 0.5f, centerY, size, r, g, b, bold: bold);
    }
}

/// <summary>
/// BuildingPanel — Building/construction UI panel (placeholder until it gets content).
/// </summary>
public sealed class BuildingPanel
{
    public bool Visible { get; set; } = false;
    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight)
        => PanelChrome.DrawPlaceholder(batch, text, screenWidth, screenHeight, "BUILDING");
}

/// <summary>
/// GearPanel — Equipment/gear UI panel (placeholder until it gets content).
/// </summary>
public sealed class GearPanel
{
    public bool Visible { get; set; } = false;
    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight)
        => PanelChrome.DrawPlaceholder(batch, text, screenWidth, screenHeight, "GEAR");
}

/// <summary>
/// DashboardPanel — Unified dashboard with 5 tabs (placeholder until it gets content).
/// </summary>
public sealed class DashboardPanel
{
    public bool Visible { get; set; } = false;
    public void SetActive(string tab) { }
    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight)
        => PanelChrome.DrawPlaceholder(batch, text, screenWidth, screenHeight, "DASHBOARD");
}