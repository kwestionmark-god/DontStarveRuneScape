namespace DontStarveRuneScape.UI;

using System.Linq;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using Silk.NET.Input;

/// <summary>
/// FireMenuPanel — the fire venue. Every recipe that needs fire lives here
/// (cooking, heat work) plus rest-until-dawn; opened by E near a lit fire or
/// heat station. Content is single-sourced by IsFireRecipe — the crafting
/// panel's exclusion and tests read the same predicate.
/// </summary>
public sealed class FireMenuPanel
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

    private readonly List<CraftRecipe> _sorted = [];
    private int _scroll;
    private string? _status;
    private bool _statusOk;
    private readonly List<string> _statusExtra = [];

    // Selection mode: 0 = recipe list, 1 = CRAFT/FUEL/UPGRADE/REST button.
    private int _selectionMode = 0;
    private bool _pendingCraft;
    private bool _pendingRest;
    private bool _pendingFuel;
    private bool _pendingUpgrade;

    // Slice 13: special rows live past the recipe list — the Fuel row
    // always, the Upgrade row when the target hearth has a next tier,
    // Rest last. A bare ground fire (FireTargetDef null) has no upgrade.
    internal int FuelIndex => _sorted.Count;
    internal int UpgradeIndex => FireTargetDef?.UpgradesTo != null ? _sorted.Count + 1 : -1;
    internal int RestIndex => _sorted.Count + (UpgradeIndex > 0 ? 2 : 1);

    /// <summary>Single source of the fire-menu partition: a recipe belongs
    /// here when it requires a campfire/heat station or trains cooking.</summary>
    public static bool IsFireRecipe(CraftRecipe recipe)
        => recipe.RequiresCampfire || recipe.RequiredSkill == "cooking";

    /// <summary>Recipes currently listed (post-partition); test seam mirroring
    /// CraftingPanel.ListedRecipes.</summary>
    internal IReadOnlyList<CraftRecipe> ListedRecipes => _sorted;

    /// <summary>Rest seam: called with true when the player selects Rest
    /// until dawn; wired by Bootstrap to the sleep flow.</summary>
    public Action<bool>? OnRest { get; set; }

    /// <summary>Slice 13: the Fuel row — feed the hearth's reservoir.
    /// Wired by Bootstrap to FireInteraction.MenuFeedFire.</summary>
    public Action<bool>? OnFuel { get; set; }

    /// <summary>Slice 13: the Upgrade row — kick off the hearth's
    /// upgrade chain step (campfire -> lined fire pit).</summary>
    public Action<bool>? OnUpgrade { get; set; }

    /// <summary>Slice 13: the hearth def the menu opened on (null = a bare
    /// ground fire / station). Set by Game when opening the menu.</summary>
    public StructureDef? FireTargetDef { get; set; }

    /// <summary>Slice 13: status label + fuel preview text, refreshed
    /// per-frame by Game.Update from FireInteraction.</summary>
    public string FireStatusText { get; set; } = string.Empty;
    public string FuelPreviewLine { get; set; } = string.Empty;

    /// <summary>Slice 13: the reservoir gauge contents — burn fraction,
    /// the stocked item + its unit count (null id = empty).</summary>
    public float FireGaugeFraction { get; set; }
    public string? ReservoirItemId { get; set; }
    public int ReservoirCount { get; set; }

    public void HandleKey(Key key)
    {
        if (_selectionMode == 0)
        {
            if (key == Key.Up && SelectedIndex > 0)
                SelectedIndex--;
            else if (key == Key.Down && SelectedIndex < RestIndex)
                SelectedIndex++;
            else if (key == Key.Right || key == Key.Tab)
                _selectionMode = 1;
            else
                return;
        }
        else
        {
            if (key == Key.Left || key == Key.Tab)
                _selectionMode = 0;
            else if (key == Key.Enter || key == Key.Space)
            {
                if (SelectedIndex < _sorted.Count)
                    _pendingCraft = true;
                else if (SelectedIndex == FuelIndex)
                    _pendingFuel = true;
                else if (SelectedIndex == UpgradeIndex)
                    _pendingUpgrade = true;
                else
                    _pendingRest = true; // past the special rows = Rest until dawn
            }
            else
                return;
        }
        _status = null;
        _statusExtra.Clear();
        KeepSelectionInView();
    }

    private void HandleKeyRepeats(InputState input)
    {
        if (input.KeyRepeatUp && _selectionMode == 0 && SelectedIndex > 0)
            SelectedIndex--;
        else if (input.KeyRepeatDown && _selectionMode == 0 && SelectedIndex < RestIndex)
            SelectedIndex++;
        else if (input.KeyRepeatRight && _selectionMode == 0)
            _selectionMode = 1;
        else if (input.KeyRepeatLeft && _selectionMode == 1)
            _selectionMode = 0;
        KeepSelectionInView();
    }

    private void KeepSelectionInView()
    {
        // Slice 13: the special rows (fuel/upgrade/rest) extend the list.
        int maxIndex = RestIndex;
        if (SelectedIndex > maxIndex) SelectedIndex = maxIndex;
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
        if (_scroll < 0) _scroll = 0;
    }

    /// <summary>Mouse handling + craft/rest action; call once per frame from
    /// Game.Update while open. availableStructures: the proximity-filtered
    /// station ids (the fire itself is the venue).</summary>
    public void Update(InputState input, CraftingSystem crafting, Inventory inventory,
        SkillManager skills, int screenW, int screenH,
        IReadOnlySet<string>? availableStructures = null,
        IReadOnlySet<string>? unlockedRecipes = null)
    {
        HandleKeyRepeats(input);
        Refresh(crafting);
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        float scroll = ui.GetScroll();
        if (scroll != 0f)
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, RestIndex - VisibleRows + 1));

        int visibleTop = Math.Min(_scroll + VisibleRows, RestIndex + 1);
        for (int i = 0; _scroll + i < visibleTop; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                int idx = _scroll + i;
                bool reselectSpecial = idx >= _sorted.Count && idx == SelectedIndex;
                SelectedIndex = idx;
                _selectionMode = 0;
                _status = null;
                _statusExtra.Clear();
                if (reselectSpecial)
                {
                    // Clicking an already-selected special row ACTIVATES it.
                    if (idx == FuelIndex) _pendingFuel = true;
                    else if (idx == UpgradeIndex) _pendingUpgrade = true;
                    else _pendingRest = true;
                }
            }
        }

        bool craftHover = ui.Hovered(_craftX, _craftY, CraftW, CraftH);
        if (craftHover) _selectionMode = 1;

        bool wantsCraft = ui.TryClick(_craftX, _craftY, CraftW, CraftH) || _pendingCraft;
        bool wantsFuel = _pendingFuel;
        bool wantsUpgrade = _pendingUpgrade;
        bool wantsRest = _pendingRest;
        if (SelectedIndex < _sorted.Count && wantsCraft)
        {
            var result = crafting.Craft(_sorted[SelectedIndex].RecipeId, inventory, skills,
                availableStructures, unlockedRecipes);
            _status = result.Message;
            _statusOk = result.Success;
            _statusExtra.Clear();
            _statusExtra.AddRange(result.LevelUpMessages);
        }
        else if (SelectedIndex == FuelIndex && (wantsFuel || wantsCraft))
        {
            OnFuel?.Invoke(true);
        }
        else if (SelectedIndex == UpgradeIndex && (wantsUpgrade || wantsCraft))
        {
            OnUpgrade?.Invoke(true);
        }
        else if (SelectedIndex == RestIndex && (wantsRest || wantsCraft || wantsFuel || wantsUpgrade))
        {
            OnRest?.Invoke(true);
        }
        _pendingCraft = false;
        _pendingFuel = false;
        _pendingUpgrade = false;
        _pendingRest = false;
    }

    private void Refresh(CraftingSystem crafting)
    {
        _sorted.Clear();
        if (crafting?.Registry == null) return;
        foreach (var recipe in crafting.Registry.Recipes.Values
            .Where(IsFireRecipe)
            .OrderBy(r => r.Tier).ThenBy(r => r.Name, StringComparer.Ordinal))
            _sorted.Add(recipe);
        if (SelectedIndex > RestIndex)
            SelectedIndex = RestIndex;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, RestIndex - VisibleRows + 1));
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        CraftingSystem crafting, Inventory inventory, SkillManager skills,
        int screenW, int screenHeight, IReadOnlySet<string>? availableStructures = null,
        IReadOnlySet<string>? unlockedRecipes = null)
    {
        Refresh(crafting);
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "FIRE", ContentW, ContentH,
            out _, out _, out _, out _);

        int craftable = _sorted.Count(r => CanCraft(r, inventory, skills, availableStructures, unlockedRecipes));
        float headerY = _cy - HeaderH + 13f;
        DrawLeft(batch, text, $"Recipes: {_sorted.Count}", _cx, headerY, 15,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        DrawRight(batch, text, $"Craftable now: {craftable}", _cx + ContentW, headerY, 15,
            150, 200, 120, bold: true);

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
            RenderRow(batch, text, sprites, _sorted[_scroll + i], _scroll + i, inventory, skills,
                availableStructures, unlockedRecipes);
        // Slice 13: the special rows (fuel / upgrade / rest) render in the
        // left list too — the fire verbs must be DISCOVERABLE.
        for (int idx = Math.Max(_sorted.Count, _scroll); idx <= RestIndex; idx++)
        {
            int slot = idx - _scroll;
            if (slot >= VisibleRows) break;
            float y = _rowY[slot];
            bool selected = idx == SelectedIndex;
            if (selected)
                batch.DrawScreenQuad(_rowX + _rowW * 0.5f, y + RowH * 0.5f,
                    _rowW * 0.5f, RowH * 0.5f, 70, 52, 30);
            string label = idx == FuelIndex ? "Feed the fire"
                : idx == UpgradeIndex ? $"Upgrade: {FireTargetDef?.UpgradesTo ?? ""}"
                : "Rest until dawn";
            text.DrawText(batch, label, _rowX + 18f, y + RowH * 0.5f, 14,
                (byte)220, (byte)190, (byte)120, bold: selected);
        }

        RenderDetail(batch, text, sprites, inventory, skills, availableStructures, unlockedRecipes);
    }

    private void RenderRow(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        CraftRecipe recipe, int index, Inventory inventory, SkillManager skills,
        IReadOnlySet<string>? availableStructures, IReadOnlySet<string>? unlockedRecipes)
    {
        float y = _rowY[index - _scroll];
        float cx = _rowX + _rowW * 0.5f, cy = y + RowH * 0.5f;
        bool selected = index == SelectedIndex;
        bool craftable = CanCraft(recipe, inventory, skills, availableStructures, unlockedRecipes);

        if (selected)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 70, 52, 30);
        else if (craftable)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 34, 26, 20);

        var display = ItemCatalog.Get(recipe.OutputItem);
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

        byte r = craftable ? (byte)150 : (byte)140;
        byte g = craftable ? (byte)210 : (byte)130;
        byte b = craftable ? (byte)110 : (byte)120;
        text.DrawText(batch, recipe.Name, _rowX + 42f, cy, 14, r, g, b,
            bold: selected);
        DrawRight(batch, text, $"Lv {recipe.RequiredLevel}   +{(int)recipe.XpReward}xp",
            _rowX + _rowW - 8f, cy, 12, 130, 125, 115);
    }

    private void RenderDetail(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Inventory inventory, SkillManager skills,
        IReadOnlySet<string>? availableStructures, IReadOnlySet<string>? unlockedRecipes)
    {
        float dividerX = _detailX - 10f;
        batch.DrawScreenQuad(dividerX, _detailY + 200f, 0.5f, 200f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        if (SelectedIndex == FuelIndex)
        {
            // Slice 13: the fuel reservoir — burn metrics, gauge, stock.
            DrawLeft(batch, text, FireStatusText.Length > 0 ? FireStatusText : "Fire",
                _detailX, _detailY + 8f, 18,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            // Gauge: the reservoir's total burn against a full stock.
            float barY = _detailY + 52f;
            batch.DrawScreenQuad(_detailX + 110f, barY, 110f, 7f, 30, 24, 18);
            float frac = Math.Clamp(FireGaugeFraction, 0f, 1f);
            if (frac > 0f)
            {
                byte fr = 250, fg = (byte)(frac > 0.25f ? 150 : 90);
                batch.DrawScreenQuad(_detailX + 110f * frac, barY, 110f * frac, 7f,
                    fr, fg, 40, 220);
            }
            // The reservoir box: stocked item + count (click to add one).
            float boxX = _detailX + 30f, boxY = _detailY + 100f;
            uint rtex = ReservoirItemId != null
                ? sprites?.GetSpriteTexture(ItemCatalog.Get(ReservoirItemId)?.SpriteKey ?? ReservoirItemId) ?? 0
                : 0;
            batch.DrawScreenQuad(boxX + 20f, boxY + 20f, 20f, 20f, 40, 30, 22);
            if (rtex != 0)
                batch.DrawTexturedScreenQuad(boxX + 20f, boxY + 20f, 18f, 18f,
                    rtex, 255, 255, 255, 255);
            else
                text.DrawText(batch, "?", boxX + 20f, boxY + 20f, 14, 150, 140, 130, bold: true);
            if (ReservoirItemId != null && ReservoirCount > 0)
                DrawLeft(batch, text, $"x{ReservoirCount}", boxX + 48f, boxY + 20f, 14,
                    PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            DrawLeft(batch, text,
                FuelPreviewLine.Length > 0 ? FuelPreviewLine : "Feed from your inventory.",
                _detailX, _detailY + 156f, 13, 150, 140, 130);
            DrawButton(batch, text, "FEED", _selectionMode == 1);
            return;
        }

        if (SelectedIndex == UpgradeIndex && FireTargetDef is { UpgradesTo: { } targetId })
        {
            DrawLeft(batch, text, $"Upgrade to {targetId}", _detailX, _detailY + 8f, 18,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            DrawLeft(batch, text, "A colony crew rebuilds the hearth at its next tier.",
                _detailX, _detailY + 64f, 13, 150, 140, 130);
            DrawLeft(batch, text, "The fire and its reservoir carry over.",
                _detailX, _detailY + 84f, 13, 150, 140, 130);
            DrawButton(batch, text, "UPGRADE", _selectionMode == 1);
            return;
        }

        if (SelectedIndex >= _sorted.Count)
        {
            // The Rest row: one past the recipe list.
            bool restSelected = SelectedIndex == _sorted.Count;
            DrawLeft(batch, text, "Rest until dawn", _detailX, _detailY + 8f, 18,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            DrawLeft(batch, text, "Sleep by the fire until morning.",
                _detailX, _detailY + 64f, 13, 150, 140, 130);
            DrawButton(batch, text, "REST", restSelected);
            return;
        }

        var recipe = _sorted[SelectedIndex];
        var display = ItemCatalog.Get(recipe.OutputItem);

        DrawLeft(batch, text, recipe.Name, _detailX, _detailY + 8f, 18,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);

        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? recipe.OutputItem) ?? 0;
        if (tex != 0)
            batch.DrawTexturedScreenQuad(_detailX + 30f, _detailY + 64f, 28f, 28f, tex, 255, 255, 255, 255);
        DrawLeft(batch, text, $"{recipe.OutputQuantity} x {display?.Name ?? recipe.OutputItem}",
            _detailX + 70f, _detailY + 64f, 14, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        DrawLeft(batch, text, "Ingredients", _detailX, _detailY + 100f, 13, 150, 140, 130);
        int line = 0;
        foreach (var (itemId, need) in recipe.Inputs)
        {
            int have = inventory.GetItemQuantity(itemId);
            bool ok = have >= need;
            var ingDisplay = ItemCatalog.Get(itemId);
            DrawLeft(batch, text, ingDisplay?.Name ?? itemId, _detailX, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            DrawRight(batch, text, $"{have}/{need}", _detailX + _detailW, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            line++;
        }

        DrawLeft(batch, text, $"Requires {recipe.RequiredSkill} Lv {recipe.RequiredLevel}   +{(int)recipe.XpReward} xp",
            _detailX, _detailY + 208f, 13, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        string flags =
            (!string.IsNullOrEmpty(recipe.RequiresStructure) ? $"Requires {recipe.RequiresStructure}   " : "") +
            (recipe.QuestUnlock != null ? $"Quest: {recipe.QuestUnlock}" : "");
        if (flags.Length > 0)
            DrawLeft(batch, text, flags.TrimEnd(), _detailX, _detailY + 230f, 13, 200, 170, 60);

        bool craftable = CanCraft(recipe, inventory, skills, availableStructures, unlockedRecipes);
        bool craftSelected = _selectionMode == 1;
        byte br = craftable ? (craftSelected ? (byte)250 : PanelChrome.BorderR) : (byte)110;
        byte bg = craftable ? (craftSelected ? (byte)225 : PanelChrome.BorderG) : (byte)95;
        byte bb = craftable ? (craftSelected ? (byte)180 : PanelChrome.BorderB) : (byte)70;
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f + 2f, CraftH * 0.5f + 2f, br, bg, bb);
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f, CraftH * 0.5f, 30, 20, 10);
        text.DrawText(batch, "COOK", _craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, 15, br, bg, bb, bold: true);

        float statusY = _craftY + CraftH + 18f;
        if (_status != null)
            DrawLeft(batch, text, _status, _detailX, statusY, 13,
                _statusOk ? (byte)150 : (byte)210, _statusOk ? (byte)210 : (byte)110, _statusOk ? (byte)120 : (byte)100);
        for (int i = 0; i < _statusExtra.Count; i++)
            DrawLeft(batch, text, _statusExtra[i], _detailX, statusY + 20f + i * 18f, 13, 255, 215, 0);
    }

    private void DrawButton(PrimitiveBatch batch, TextRenderer text, string label, bool selected)
    {
        byte br = selected ? (byte)250 : PanelChrome.BorderR;
        byte bg = selected ? (byte)225 : PanelChrome.BorderG;
        byte bb = selected ? (byte)180 : PanelChrome.BorderB;
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f + 2f, CraftH * 0.5f + 2f, br, bg, bb);
        batch.DrawScreenQuad(_craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, CraftW * 0.5f, CraftH * 0.5f, 30, 20, 10);
        text.DrawText(batch, label, _craftX + CraftW * 0.5f, _craftY + CraftH * 0.5f, 15, br, bg, bb, bold: true);
    }

    // Panel-side craftable check; matches CraftingSystem's gates (the venue
    // is already verified by the open predicate — stations count live).
    private static bool CanCraft(CraftRecipe recipe, Inventory inventory, SkillManager skills,
        IReadOnlySet<string>? availableStructures, IReadOnlySet<string>? unlockedRecipes)
    {
        if (unlockedRecipes != null && recipe.QuestUnlock != null
            && !unlockedRecipes.Contains(recipe.RecipeId)) return false;
        if (skills.GetSkillLevel(recipe.RequiredSkill) < recipe.RequiredLevel) return false;
        if (availableStructures != null && !string.IsNullOrEmpty(recipe.RequiresStructure)
            && !availableStructures.Contains(recipe.RequiresStructure)) return false;
        if (availableStructures != null && recipe.RequiresCampfire
            && !availableStructures.Contains("campfire")
            && !availableStructures.Contains("cooking_station")
            && !availableStructures.Contains("furnace")
            && !availableStructures.Contains("smelter")) return false;
        foreach (var group in recipe.Inputs.GroupBy(input => input.ItemId, StringComparer.Ordinal))
        {
            if (inventory.GetItemQuantity(group.Key) < group.Sum(input => input.Quantity)) return false;
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
