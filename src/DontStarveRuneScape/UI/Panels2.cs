namespace DontStarveRuneScape.UI;

using System.Linq;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;

/// <summary>
/// InventoryPanel — 20-slot inventory grid (4 columns x 5 rows) with a detail
/// block for the selected slot: real item sprite (glyph-chip fallback),
/// quantity vs stack size, equipped marker, gear stats, food stats, and a
/// spoilage bar. Food items get an EAT button; equippable items (present in
/// gear.json) get an EQUIP/UNEQUIP button (and Enter toggles) that drive the
/// shared equip path, keeping the inventory flag and PlayerGear in sync.
/// Layout is computed in Layout() from the screen size alone, so Update (hit-testing)
/// and Render stay consistent.
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
    private const float EatW = 150f, EatH = 30f;
    private const float EquipW = 150f, EquipH = 30f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;                    // content rect origin
    private readonly float[] _slotX = new float[SlotCount];
    private readonly float[] _slotY = new float[SlotCount];
    private float _detailX, _detailY;
    private float _eatX, _eatY;
    private float _equipX, _equipY;

    // Detail button selection: 0 = grid, 1 = EAT, 2 = EQUIP
    private int _detailSelection = 0;

    // Last eat result line, cleared when the selection changes.
    private string? _status;
    private bool _statusOk;

    public void HandleKey(Key key, Inventory inventory, PlayerGear? gear)
    {
        if (_detailSelection == 0)
        {
            // Navigating the grid
            if (key == Key.Up)
                SelectedIndex = (SelectedIndex + SlotCount - Cols) % SlotCount;
            else if (key == Key.Down)
                SelectedIndex = (SelectedIndex + Cols) % SlotCount;
            else if (key == Key.Left)
                SelectedIndex = (SelectedIndex + SlotCount - 1) % SlotCount;
            else if (key == Key.Right)
                SelectedIndex = (SelectedIndex + 1) % SlotCount;
            else if (key == Key.Tab)
            {
                // Move to first available detail button
                var slot = inventory.Slots[SelectedIndex];
                if (slot.ItemId != null && slot.Quantity > 0)
                {
                    var display = Data.ItemCatalog.Get(slot.ItemId);
                    if (display?.IsFood == true)
                        _detailSelection = 1; // EAT
                    else if (Data.GearItem.LoadAll().ContainsKey(slot.ItemId))
                        _detailSelection = 2; // EQUIP
                }
            }
            else if (key == Key.Enter)
            {
                // Enter toggles the selected item's equipped state (gear items only),
                // through the same path the EQUIP button and the gear panel use.
                if (inventory.Slots[SelectedIndex] is { ItemId: not null, Quantity: > 0 } slot
                    && Data.GearItem.LoadAll().ContainsKey(slot.ItemId))
                {
                    bool wasEquipped = slot.IsEquipped;
                    GearPanel.ToggleEquip(inventory, gear, slot.ItemId);
                    _status = wasEquipped ? $"Unequipped {NameOf(slot.ItemId)}."
                                          : $"Equipped {NameOf(slot.ItemId)}.";
                    _statusOk = true;
                }
            }
            else
                return;
        }
        else if (_detailSelection == 1)
        {
            // On EAT button
            if (key == Key.Tab || key == Key.Left)
                _detailSelection = 0; // Back to grid
            else if (key == Key.Right)
            {
                // Move to EQUIP if available
                var slot = inventory.Slots[SelectedIndex];
                if (slot.ItemId != null && slot.Quantity > 0 && Data.GearItem.LoadAll().ContainsKey(slot.ItemId))
                    _detailSelection = 2;
            }
            else if (key == Key.Enter || key == Key.Space)
            {
                // Trigger EAT
                var slot = inventory.Slots[SelectedIndex];
                if (slot.ItemId != null && slot.Quantity > 0)
                {
                    var food = _foods?.Get(slot.ItemId);
                    if (food != null && _survival != null)
                    {
                        inventory.RemoveItem(slot.ItemId, 1);
                        _status = _survival.Eat(food);
                        _statusOk = true;
                    }
                    else
                    {
                        _status = "Nothing to eat here.";
                        _statusOk = false;
                    }
                }
            }
            else
                return;
        }
        else if (_detailSelection == 2)
        {
            // On EQUIP button
            if (key == Key.Tab || key == Key.Left)
            {
                var slot = inventory.Slots[SelectedIndex];
                if (slot.ItemId != null && slot.Quantity > 0)
                {
                    var display = Data.ItemCatalog.Get(slot.ItemId);
                    if (display?.IsFood == true)
                        _detailSelection = 1; // Back to EAT
                    else
                        _detailSelection = 0; // Back to grid
                }
                else
                    _detailSelection = 0;
            }
            else if (key == Key.Enter || key == Key.Space)
            {
                // Trigger EQUIP
                var slot = inventory.Slots[SelectedIndex];
                if (slot.ItemId != null && slot.Quantity > 0 && Data.GearItem.LoadAll().ContainsKey(slot.ItemId))
                {
                    bool wasEquipped = slot.IsEquipped;
                    GearPanel.ToggleEquip(inventory, gear, slot.ItemId);
                    _status = wasEquipped ? $"Unequipped {NameOf(slot.ItemId)}."
                                          : $"Equipped {NameOf(slot.ItemId)}.";
                    _statusOk = true;
                }
            }
            else
                return;
        }
        if (key != Key.Enter && key != Key.Space)
            _status = null;
    }

    // Need to store foods and survival for HandleKey EAT action
    private Survival.FoodRegistry? _foods;
    private Survival.SurvivalSystem? _survival;

    /// <summary>Handle accelerated key repeats for UI navigation.</summary>
    private void HandleKeyRepeats(InputState input)
    {
        if (_detailSelection == 0)
        {
            // Navigating the grid
            if (input.KeyRepeatUp)
                SelectedIndex = (SelectedIndex + SlotCount - Cols) % SlotCount;
            else if (input.KeyRepeatDown)
                SelectedIndex = (SelectedIndex + Cols) % SlotCount;
            else if (input.KeyRepeatLeft)
                SelectedIndex = (SelectedIndex + SlotCount - 1) % SlotCount;
            else if (input.KeyRepeatRight)
                SelectedIndex = (SelectedIndex + 1) % SlotCount;
        }
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, Inventory inventory, Survival.SurvivalSystem? survival,
        Survival.FoodRegistry? foods, int screenW, int screenH, PlayerGear? gear = null)
    {
        HandleKeyRepeats(input);
        _foods = foods;
        _survival = survival;
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        for (int i = 0; i < inventory.Slots.Count; i++)
        {
            if (ui.TryClick(_slotX[i], _slotY[i], SlotSize, SlotSize))
            {
                if (SelectedIndex != i)
                    _status = null;
                SelectedIndex = i;
                _detailSelection = 0;
            }
        }

        // EAT: consume one of the selected food item and apply its values.
        bool eatHover = false;
        if (ui.Hovered(_eatX, _eatY, EatW, EatH))
            eatHover = true;

        if (ui.TryClick(_eatX, _eatY, EatW, EatH)
            && inventory.Slots[SelectedIndex] is { ItemId: not null, Quantity: > 0 } slot)
        {
            var food = foods?.Get(slot.ItemId);
            if (food == null || survival == null)
            {
                _status = "Nothing to eat here.";
                _statusOk = false;
            }
            else
            {
                inventory.RemoveItem(slot.ItemId, 1);
                _status = survival.Eat(food);
                _statusOk = true;
            }
        }

        // EQUIP/UNEQUIP: toggle the selected item's equipped state (gear items
        // only), keeping the inventory flag and the PlayerGear slot in sync.
        bool equipHover = false;
        if (ui.Hovered(_equipX, _equipY, EquipW, EquipH))
            equipHover = true;

        if (ui.TryClick(_equipX, _equipY, EquipW, EquipH)
            && inventory.Slots[SelectedIndex] is { ItemId: not null, Quantity: > 0 } equipSlot
            && Data.GearItem.LoadAll().ContainsKey(equipSlot.ItemId))
        {
            bool wasEquipped = equipSlot.IsEquipped;
            GearPanel.ToggleEquip(inventory, gear, equipSlot.ItemId);
            _status = wasEquipped ? $"Unequipped {NameOf(equipSlot.ItemId)}."
                                  : $"Equipped {NameOf(equipSlot.ItemId)}.";
            _statusOk = true;
        }

        // Update detail selection from hover
        if (eatHover) _detailSelection = 1;
        else if (equipHover) _detailSelection = 2;
        else if (_detailSelection != 0 && !eatHover && !equipHover)
        {
            // If mouse left the buttons, go back to grid
            // But only if not actively using keyboard navigation
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

        // Gear stats for equippable items (present in gear.json).
        var gearItem = Data.GearItem.LoadAll().TryGetValue(id, out var gi) ? gi : null;
        if (gearItem != null)
        {
            string stats = gearItem.Slot == "weapon"
                ? $"Damage {gearItem.Damage:0}, Atk +{gearItem.AttackBonus:0}"
                : $"Defence +{gearItem.DefenceBonus:0}";
            DrawLeft(batch, text, stats, _detailX, _detailY + 172f, 13, 200, 190, 160);
        }

        if (display is { IsFood: true })
        {
            string foodLine = $"Food: +{(int)display.HungerRestore} hunger, {(int)display.HpRestore} hp";
            DrawLeft(batch, text, foodLine, _detailX, _detailY + 176f, 14, 150, 200, 120);

            // EAT button: gold for food, only drawn for food items.
            bool eatSelected = _detailSelection == 1;
            byte eatR = eatSelected ? (byte)250 : PanelChrome.BorderR;
            byte eatG = eatSelected ? (byte)225 : PanelChrome.BorderG;
            byte eatB = eatSelected ? (byte)180 : PanelChrome.BorderB;
            batch.DrawScreenQuad(_eatX + EatW * 0.5f, _eatY + EatH * 0.5f, EatW * 0.5f + 2f, EatH * 0.5f + 2f, eatR, eatG, eatB);
            batch.DrawScreenQuad(_eatX + EatW * 0.5f, _eatY + EatH * 0.5f, EatW * 0.5f, EatH * 0.5f, 30, 20, 10);
            text.DrawText(batch, "EAT", _eatX + EatW * 0.5f, _eatY + EatH * 0.5f, 15, eatR, eatG, eatB, bold: true);
        }

        // EQUIP/UNEQUIP button for equippable items (present in gear.json);
        // toggles through the shared path so PlayerGear stays in sync.
        if (gearItem != null)
        {
            string equipLabel = slot.IsEquipped ? "UNEQUIP" : "EQUIP";
            bool equipSelected = _detailSelection == 2;
            byte equipR = equipSelected ? (byte)250 : PanelChrome.BorderR;
            byte equipG = equipSelected ? (byte)225 : PanelChrome.BorderG;
            byte equipB = equipSelected ? (byte)180 : PanelChrome.BorderB;
            batch.DrawScreenQuad(_equipX + EquipW * 0.5f, _equipY + EquipH * 0.5f, EquipW * 0.5f + 2f, EquipH * 0.5f + 2f, equipR, equipG, equipB);
            batch.DrawScreenQuad(_equipX + EquipW * 0.5f, _equipY + EquipH * 0.5f, EquipW * 0.5f, EquipH * 0.5f, 30, 20, 10);
            text.DrawText(batch, equipLabel, _equipX + EquipW * 0.5f, _equipY + EquipH * 0.5f, 14, equipR, equipG, equipB, bold: true);
        }

        // Status line: last eat result.
        if (_status != null)
            DrawLeft(batch, text, _status, _detailX, _eatY + EatH + 16f, 13,
                _statusOk ? (byte)150 : (byte)210, _statusOk ? (byte)210 : (byte)110,
                _statusOk ? (byte)120 : (byte)100);

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

    private static string NameOf(string id) =>
        Data.ItemCatalog.Get(id)?.Name ?? PrettifyId(id);

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

        _eatX = _detailX;
        _eatY = _detailY + 258f;
        _equipX = _eatX + EatW + 12f;
        _equipY = _eatY;
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
        ("fishing",      "Fishing",      "Fi",  66, 134, 244),
        ("cooking",      "Cooking",      "Ck", 208, 132,  70),
        ("firemaking",   "Firemaking",   "Fm", 220,  96,  48),
        ("crafting",     "Crafting",     "Cr", 186, 152,  96),
        ("metallurgy",   "Metallurgy",   "Mt", 176, 116,  74),
        ("construction", "Construction", "Cs", 168, 124,  82),
        ("agility",      "Agility",      "Ag",  90, 200, 190),
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

    // Intelligence-only sub-stats (gate quests, trade stock, recruitment).
    private static readonly (string Key, string Name)[] IntelExtraStats =
    {
        ("commerce",   "Commerce"),
        ("persuasion", "Persuasion"),
    };

    // Sub-stats for the selected skill: the generic five, plus the
    // intelligence extras when intelligence is selected.
    private static (string Key, string Name)[] StatsFor(string skillId) =>
        skillId == "intelligence" ? [.. SubStats, .. IntelExtraStats] : SubStats;

    private const float ContentW = 780f;
    private const float ContentH = 520f; // 10 skill rows (RowH 42 + gap 4) + header
    private const float RowH = 42f;
    private const float RowGap = 4f;
    private const float HeaderH = 26f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;                    // content rect origin
    private readonly float[] _rowY = new float[Skills.Length];
    private float _rowX, _rowW;
    private float _detailX, _detailY, _detailW;
    private readonly float[] _statY = new float[SubStats.Length + IntelExtraStats.Length];
    private float _plusX, _plusY0;             // [+] button column

    // Selection mode: 0 = skill list (left), 1 = [+] buttons (right)
    private int _selectionMode = 0;
    private int _plusSelected = 0; // which [+] button is selected

    public void HandleKey(Key key)
    {
        if (_selectionMode == 0)
        {
            // Navigating skill list
            if (key == Key.Up)
                SelectedIndex = (SelectedIndex + Skills.Length - 1) % Skills.Length;
            else if (key == Key.Down)
                SelectedIndex = (SelectedIndex + 1) % Skills.Length;
            else if (key == Key.Right || key == Key.Tab)
            {
                var skill = skills.GetSkill(Skills[SelectedIndex].Id);
                if (skill.UnallocatedPoints > 0)
                {
                    _selectionMode = 1;
                    _plusSelected = 0;
                }
            }
        }
        else
        {
            // Navigating [+] buttons
            var skill = skills.GetSkill(Skills[SelectedIndex].Id);
            var statList = StatsFor(skill.Id);
            if (key == Key.Up && _plusSelected > 0)
                _plusSelected--;
            else if (key == Key.Down && _plusSelected < statList.Length - 1)
                _plusSelected++;
            else if (key == Key.Left || key == Key.Tab)
                _selectionMode = 0;
            else if (key == Key.Enter || key == Key.Space)
            {
                if (skill.UnallocatedPoints > 0 && _plusSelected < statList.Length)
                    skills.SpendPoint(Skills[SelectedIndex].Id, statList[_plusSelected].Key);
            }
        }
    }

    /// <summary>Handle accelerated key repeats for UI navigation.</summary>
    private void HandleKeyRepeats(InputState input)
    {
        if (_selectionMode == 0)
        {
            // Navigating skill list
            if (input.KeyRepeatUp)
                SelectedIndex = (SelectedIndex + Skills.Length - 1) % Skills.Length;
            else if (input.KeyRepeatDown)
                SelectedIndex = (SelectedIndex + 1) % Skills.Length;
            else if (input.KeyRepeatRight)
            {
                var skill = skills.GetSkill(Skills[SelectedIndex].Id);
                if (skill.UnallocatedPoints > 0)
                {
                    _selectionMode = 1;
                    _plusSelected = 0;
                }
            }
        }
        else
        {
            // Navigating [+] buttons
            var skill = skills.GetSkill(Skills[SelectedIndex].Id);
            var statList = StatsFor(skill.Id);
            if (input.KeyRepeatUp && _plusSelected > 0)
                _plusSelected--;
            else if (input.KeyRepeatDown && _plusSelected < statList.Length - 1)
                _plusSelected++;
            else if (input.KeyRepeatLeft)
                _selectionMode = 0;
        }
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, SkillManager skills, int screenW, int screenH)
    {
        HandleKeyRepeats(input);
        this.skills = skills;
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        for (int i = 0; i < Skills.Length; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                SelectedIndex = i;
                _selectionMode = 0;
            }
        }

        var skill = skills.GetSkill(Skills[SelectedIndex].Id);
        _lastMouseHoverPlus = -1;
        if (skill.UnallocatedPoints <= 0) return;
        var statList = StatsFor(skill.Id);
        for (int s = 0; s < statList.Length; s++)
        {
            if (ui.Hovered(_plusX, _statY[s] - 11f, 22f, 22f))
            {
                _lastMouseHoverPlus = s;
                _selectionMode = 1;
                _plusSelected = s;
            }
            if (ui.TryClick(_plusX, _statY[s] - 11f, 22f, 22f))
                skills.SpendPoint(Skills[SelectedIndex].Id, statList[s].Key);
        }
    }

    private SkillManager? skills;

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

        var statList = StatsFor(id);
        for (int s = 0; s < statList.Length; s++)
        {
            float y = _statY[s];
            float value = data.SubStats[statList[s].Key];
            DrawLeft(batch, text, statList[s].Name, _detailX + 8f, y, 14,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
            DrawRight(batch, text, $"+{(int)value}", _plusX - 34f, y, 14, 200, 190, 160);

            if (data.UnallocatedPoints > 0)
            {
                bool selected = (_selectionMode == 1) && (_plusSelected == s);
                bool hover = _lastMouseHoverPlus == s;
                byte br = (selected || hover) ? (byte)250 : (byte)222;
                byte bg = (selected || hover) ? (byte)225 : (byte)192;
                byte bb = (selected || hover) ? (byte)180 : (byte)132;
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
        for (int s = 0; s < _statY.Length; s++)
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

    // Selection mode: 0 = recipe list, 1 = CRAFT button
    private int _selectionMode = 0;

    public void HandleKey(Key key)
    {
        if (_selectionMode == 0)
        {
            // In recipe list
            if (key == Key.Up && SelectedIndex > 0)
                SelectedIndex--;
            else if (key == Key.Down && SelectedIndex < _sorted.Count - 1)
                SelectedIndex++;
            else if (key == Key.Right || key == Key.Tab)
            {
                if (_sorted.Count > 0) _selectionMode = 1; // Move to CRAFT button
            }
            else
                return;
        }
        else
        {
            // On CRAFT button
            if (key == Key.Left || key == Key.Tab)
                _selectionMode = 0; // Back to list
            else if (key == Key.Enter || key == Key.Space)
            {
                if (SelectedIndex < _sorted.Count)
                    _pendingCraft = true;
            }
            else
                return;
        }
        _status = null;
        _statusExtra.Clear();
        // Keep the selection inside the visible window.
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
    }

    private bool _pendingCraft = false;

    /// <summary>Handle accelerated key repeats for UI navigation.</summary>
    private void HandleKeyRepeats(InputState input)
    {
        if (input.KeyRepeatUp && _selectionMode == 0 && SelectedIndex > 0)
            SelectedIndex--;
        else if (input.KeyRepeatDown && _selectionMode == 0 && SelectedIndex < _sorted.Count - 1)
            SelectedIndex++;
        else if (input.KeyRepeatRight && _selectionMode == 0 && _sorted.Count > 0)
            _selectionMode = 1;
        else if (input.KeyRepeatLeft && _selectionMode == 1)
            _selectionMode = 0;

        // Keep selection in view
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
    }

    /// <summary>Mouse handling + craft action; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, CraftingSystem crafting, Inventory inventory,
        SkillManager skills, int screenW, int screenH,
        IReadOnlySet<string>? availableStructures = null)
    {
        HandleKeyRepeats(input);
        Refresh(crafting);
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        // Mouse wheel scrolls the recipe list
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, _sorted.Count - VisibleRows));
        }

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                SelectedIndex = _scroll + i;
                _selectionMode = 0;
                _status = null;
                _statusExtra.Clear();
            }
        }

        bool craftHover = ui.Hovered(_craftX, _craftY, CraftW, CraftH);
        if (craftHover) _selectionMode = 1;

        if (SelectedIndex < _sorted.Count && (ui.TryClick(_craftX, _craftY, CraftW, CraftH) || _pendingCraft))
        {
            var result = crafting.Craft(_sorted[SelectedIndex].RecipeId, inventory, skills, availableStructures);
            _status = result.Message;
            _statusOk = result.Success;
            _statusExtra.Clear();
            _statusExtra.AddRange(result.LevelUpMessages);
            _pendingCraft = false;
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        CraftingSystem crafting, Inventory inventory, SkillManager skills,
        int screenW, int screenHeight, IReadOnlySet<string>? availableStructures = null)
    {
        Refresh(crafting);
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "CRAFTING", ContentW, ContentH,
            out _, out _, out _, out _);

        int craftable = _sorted.Count(r => CanCraft(r, inventory, skills, availableStructures));
        float headerY = _cy - HeaderH + 13f;
        DrawLeft(batch, text, $"Recipes: {_sorted.Count}", _cx, headerY, 15,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        DrawRight(batch, text, $"Craftable now: {craftable}", _cx + ContentW, headerY, 15,
            150, 200, 120, bold: true);

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
            RenderRow(batch, text, sprites, _sorted[_scroll + i], _scroll + i, inventory, skills, availableStructures);

        RenderDetail(batch, text, sprites, inventory, skills, availableStructures);
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
        Data.CraftRecipe recipe, int index, Inventory inventory, SkillManager skills,
        IReadOnlySet<string>? availableStructures)
    {
        float y = _rowY[index - _scroll];
        float cx = _rowX + _rowW * 0.5f, cy = y + RowH * 0.5f;
        bool selected = index == SelectedIndex;
        bool craftable = CanCraft(recipe, inventory, skills, availableStructures);

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
        Inventory inventory, SkillManager skills, IReadOnlySet<string>? availableStructures)
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
            (!string.IsNullOrEmpty(recipe.RequiresStructure) ? $"Requires {recipe.RequiresStructure}   " : "") +
            (recipe.RequiresCampfire ? "Requires campfire   " : "") +
            (recipe.IsFood ? "Food   " : "") +
            (recipe.QuestUnlock != null ? $"Quest: {recipe.QuestUnlock}" : "");
        if (flags.Length > 0)
            DrawLeft(batch, text, flags.TrimEnd(), _detailX, _detailY + 230f, 13, 200, 170, 60);

        // CRAFT button: gold when craftable, dim when gated.
        bool craftable = CanCraft(recipe, inventory, skills, availableStructures);
        bool craftSelected = _selectionMode == 1;
        byte br = craftable ? (craftSelected ? (byte)250 : PanelChrome.BorderR) : (byte)110;
        byte bg = craftable ? (craftSelected ? (byte)225 : PanelChrome.BorderG) : (byte)95;
        byte bb = craftable ? (craftSelected ? (byte)180 : PanelChrome.BorderB) : (byte)70;
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

    // Panel-side craftable check for coloring; matches CraftingSystem's skill,
    // ingredient, and available-station gates.
    private static bool CanCraft(Data.CraftRecipe recipe, Inventory inventory, SkillManager skills,
        IReadOnlySet<string>? availableStructures)
    {
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

/// <summary>
/// BuildingPanel — Buildable structures (from the loaded registry, sorted by
/// sub-stat then name, scrolled) with a detail block: structure sprite,
/// materials with have/need counts, construction skill gate, hp/type/burnable
/// flags, biome list, and a BUILD button that hands the structure id to the
/// game's placement mode (next world click places it).
/// </summary>
public sealed class BuildingPanel
{
    public bool Visible { get; set; } = false;
    public int SelectedIndex { get; private set; }

    private const float ContentW = 780f;
    private const float ContentH = 470f;
    private const float HeaderH = 26f;
    private const float RowH = 40f;
    private const float RowGap = 2f;
    private const int VisibleRows = 10;
    private const float BuildW = 150f, BuildH = 30f;

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;
    private float _rowX, _rowW;
    private readonly float[] _rowY = new float[VisibleRows];
    private float _detailX, _detailW, _detailY;
    private float _buildX, _buildY;

    private readonly List<Data.StructureDef> _sorted = [];
    private int _scroll;
    private int _selectionMode = 0; // 0 = structure list, 1 = BUILD button
    private bool _pendingBuild = false;

    /// <summary>Called with the selected structure id when BUILD is clicked;
    /// the game enters placement mode.</summary>
    public Action<string>? BuildCallback { get; set; }

    public void HandleKey(Key key)
    {
        if (_selectionMode == 0)
        {
            // In structure list
            if (key == Key.Up && SelectedIndex > 0)
                SelectedIndex--;
            else if (key == Key.Down && SelectedIndex < _sorted.Count - 1)
                SelectedIndex++;
            else if (key == Key.Right || key == Key.Tab)
            {
                if (_sorted.Count > 0) _selectionMode = 1; // Move to BUILD button
            }
            else
                return;
        }
        else
        {
            // On BUILD button
            if (key == Key.Left || key == Key.Tab)
                _selectionMode = 0; // Back to list
            else if (key == Key.Enter || key == Key.Space)
            {
                if (SelectedIndex < _sorted.Count)
                    _pendingBuild = true;
            }
            else
                return;
        }
        // Keep the selection inside the visible window.
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
    }

    /// <summary>Handle accelerated key repeats for UI navigation.</summary>
    private void HandleKeyRepeats(InputState input)
    {
        if (input.KeyRepeatUp && _selectionMode == 0 && SelectedIndex > 0)
            SelectedIndex--;
        else if (input.KeyRepeatDown && _selectionMode == 0 && SelectedIndex < _sorted.Count - 1)
            SelectedIndex++;
        else if (input.KeyRepeatRight && _selectionMode == 0 && _sorted.Count > 0)
            _selectionMode = 1;
        else if (input.KeyRepeatLeft && _selectionMode == 1)
            _selectionMode = 0;

        // Keep selection in view
        if (SelectedIndex < _scroll) _scroll = SelectedIndex;
        if (SelectedIndex >= _scroll + VisibleRows) _scroll = SelectedIndex - VisibleRows + 1;
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, BuildingSystem building, Inventory inventory,
        SkillManager skills, int screenW, int screenH, ColonySystem? colony = null)
    {
        HandleKeyRepeats(input);
        Refresh(building);
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        // Mouse wheel scrolls the structure list
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, _sorted.Count - VisibleRows));
        }

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
        {
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                SelectedIndex = _scroll + i;
                _selectionMode = 0;
            }
        }

        bool buildHover = ui.Hovered(_buildX, _buildY, BuildW, BuildH);
        if (buildHover) _selectionMode = 1;

        if (SelectedIndex < _sorted.Count && (ui.TryClick(_buildX, _buildY, BuildW, BuildH) || _pendingBuild))
        {
            BuildCallback?.Invoke(_sorted[SelectedIndex].Id);
            _pendingBuild = false;
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        BuildingSystem building, Inventory inventory, SkillManager skills,
        int screenW, int screenHeight, ColonySystem? colony = null)
    {
        Refresh(building);
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "BUILDING", ContentW, ContentH,
            out _, out _, out _, out _);

        int buildable = _sorted.Count(d => CanBuild(d, inventory, skills, colony));
        float headerY = _cy - HeaderH + 13f;
        DrawLeft(batch, text, $"Structures: {_sorted.Count}", _cx, headerY, 15,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        DrawRight(batch, text, $"Materials ready: {buildable}", _cx + ContentW, headerY, 15,
            150, 200, 120, bold: true);

        for (int i = 0; i < VisibleRows && _scroll + i < _sorted.Count; i++)
            RenderRow(batch, text, sprites, _sorted[_scroll + i], _scroll + i, inventory, skills, colony);

        RenderDetail(batch, text, sprites, inventory, skills, colony);
    }

    private void Refresh(BuildingSystem? building)
    {
        _sorted.Clear();
        if (building?.Registry == null) return;
        foreach (var def in building.Registry.Structures.Values.OrderBy(d => d.SubStat).ThenBy(d => d.Name))
            _sorted.Add(def);
        if (SelectedIndex >= _sorted.Count)
            SelectedIndex = Math.Max(0, _sorted.Count - 1);
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _sorted.Count - VisibleRows));
    }

    private void RenderRow(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Data.StructureDef def, int index, Inventory inventory, SkillManager skills, ColonySystem? colony)
    {
        float y = _rowY[index - _scroll];
        float cx = _rowX + _rowW * 0.5f, cy = y + RowH * 0.5f;
        bool selected = index == SelectedIndex;
        bool buildable = CanBuild(def, inventory, skills, colony);

        // Row well: warm wash for buildable, darker for gated; selection on top.
        if (selected)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 70, 52, 30);
        else if (buildable)
            batch.DrawScreenQuad(cx, cy, _rowW * 0.5f, RowH * 0.5f, 34, 26, 20);

        uint tex = sprites?.GetSpriteTexture(def.SpriteKey) ?? 0;
        float chipX = _rowX + 18f;
        if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(chipX, cy, 14f, 14f, tex, 255, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(chipX, cy, 13f, 13f, 60, 48, 36);
            text.DrawText(batch, GlyphOf(def.Name), chipX, cy, 11,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }

        // Name: green when buildable, dim when gated.
        byte r = buildable ? (byte)150 : (byte)140;
        byte g = buildable ? (byte)210 : (byte)130;
        byte b = buildable ? (byte)110 : (byte)120;
        DrawLeft(batch, text, def.Name, _rowX + 42f, cy, 15, r, g, b);

        DrawRight(batch, text, def.SubStat, _rowX + _rowW - 84f, cy, 12, 150, 140, 130);
        bool levelOk = skills.GetSkillLevel("construction") >= def.RequiresSkillLevel;
        DrawRight(batch, text, $"Lv {def.RequiresSkillLevel}", _rowX + _rowW - 12f, cy, 13,
            levelOk ? (byte)150 : (byte)200, levelOk ? (byte)140 : (byte)120, levelOk ? (byte)130 : (byte)110);
    }

    private void RenderDetail(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        Inventory inventory, SkillManager skills, ColonySystem? colony)
    {
        float dividerX = _detailX - 10f;
        batch.DrawScreenQuad(dividerX, _detailY + 200f, 0.5f, 200f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        if (SelectedIndex >= _sorted.Count)
        {
            DrawLeft(batch, text, "No structures", _detailX, _detailY + 8f, 16, 150, 140, 130);
            return;
        }

        var def = _sorted[SelectedIndex];

        DrawLeft(batch, text, def.Name, _detailX, _detailY + 8f, 18,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);

        // Structure sprite under the name.
        uint tex = sprites?.GetSpriteTexture(def.SpriteKey) ?? 0;
        if (tex != 0)
            batch.DrawTexturedScreenQuad(_detailX + 30f, _detailY + 64f, 28f, 28f, tex, 255, 255, 255, 255);

        // Materials: have/need per requirement, red when short.
        DrawLeft(batch, text, "Materials", _detailX, _detailY + 100f, 13, 150, 140, 130);
        int line = 0;
        foreach (var material in def.Materials)
        {
            int have = inventory.GetItemQuantity(material.ItemId)
                + (colony?.GetItemQuantity(material.ItemId) ?? 0);
            bool ok = have >= material.Quantity;
            var display = Data.ItemCatalog.Get(material.ItemId);
            DrawLeft(batch, text, display?.Name ?? material.ItemId, _detailX, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            DrawRight(batch, text, $"{have}/{material.Quantity}", _detailX + _detailW, _detailY + 122f + line * 20f, 13,
                ok ? PanelChrome.TextR : (byte)210, ok ? PanelChrome.TextG : (byte)110, ok ? PanelChrome.TextB : (byte)100);
            line++;
        }

        // Gate and structure summary.
        DrawLeft(batch, text, $"Requires construction Lv {def.RequiresSkillLevel}   Hp {def.Hp:0}",
            _detailX, _detailY + 208f, 13, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        // Flags: type / burnable / biomes.
        string biomes = def.BiomeCompatibility.Length > 0
            ? string.Join(", ", def.BiomeCompatibility.Take(3)) + (def.BiomeCompatibility.Length > 3 ? "…" : "")
            : "any biome";
        DrawLeft(batch, text, $"{def.StructureType}{(def.Burnable ? "   burnable" : "")}   {biomes}",
            _detailX, _detailY + 230f, 13, 200, 170, 60);

        // BUILD button: gold when buildable, dim when gated.
        bool buildable = CanBuild(def, inventory, skills, colony);
        bool buildSelected = _selectionMode == 1;
        byte br = buildable ? (buildSelected ? (byte)250 : PanelChrome.BorderR) : (byte)110;
        byte bg = buildable ? (buildSelected ? (byte)225 : PanelChrome.BorderG) : (byte)95;
        byte bb = buildable ? (buildSelected ? (byte)180 : PanelChrome.BorderB) : (byte)70;
        batch.DrawScreenQuad(_buildX + BuildW * 0.5f, _buildY + BuildH * 0.5f, BuildW * 0.5f + 2f, BuildH * 0.5f + 2f, br, bg, bb);
        batch.DrawScreenQuad(_buildX + BuildW * 0.5f, _buildY + BuildH * 0.5f, BuildW * 0.5f, BuildH * 0.5f, 30, 20, 10);
        text.DrawText(batch, colony?.IsFounded == true ? "PLACE SITE" : "BUILD",
            _buildX + BuildW * 0.5f, _buildY + BuildH * 0.5f, 15, br, bg, bb, bold: true);

        DrawLeft(batch, text, "Click a tile in the world to place", _buildX + BuildW + 14f,
            _buildY + BuildH * 0.5f, 12, 150, 140, 130);
    }

    // Panel-side buildable check for coloring; matches PlaceStructure's gates
    // (skill level + materials; the biome gate is per-tile and checked at
    // placement time).
    private static bool CanBuild(Data.StructureDef def, Inventory inventory, SkillManager skills,
        ColonySystem? colony)
    {
        if (skills.GetSkillLevel("construction") < def.RequiresSkillLevel) return false;
        foreach (var material in def.Materials)
        {
            if (inventory.GetItemQuantity(material.ItemId)
                + (colony?.GetItemQuantity(material.ItemId) ?? 0) < material.Quantity) return false;
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

        _buildX = _detailX;
        _buildY = _detailY + 258f;
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
/// GearPanel — Equipment view: nine slots (Weapon/Head/Chest/Legs/Boots/Gloves/
/// Cape/Ammo/Shield) with the equipped item, plus totals and the inventory's
/// equippable items (items present in gear.json). Click an equipped slot to
/// unequip; click an equippable row (or Up/Down + click) to equip/unequip.
/// Equipping updates both the inventory slot flag and PlayerGear.
/// </summary>
public sealed class GearPanel
{
    public bool Visible { get; set; } = false;

    private const float ContentW = 780f;
    private const float ContentH = 470f;
    private const float RowH = 44f;
    private const float RowGap = 2f;
    private const int EquippableRows = 15;
    private const float EquippableRowH = 20f;

    private static readonly string[] SlotNames =
        { "weapon", "head", "chest", "legs", "boots", "gloves", "cape", "ammo", "shield" };
    private static readonly string[] SlotLabels =
        { "Weapon", "Head", "Chest", "Legs", "Boots", "Gloves", "Cape", "Ammo", "Shield" };

    // Cached absolute rects (set by Layout, which Update and Render both call).
    private float _cx, _cy;
    private float _rowX, _rowW;
    private readonly float[] _rowY = new float[SlotNames.Length];
    private float _detailX, _detailW, _detailY;
    private float _listY;

    private int _selected;
    private int _equippableCount;            // set by Update; bounds HandleKey
    private int _equippableScroll = 0;       // scroll offset for equippable list
    private int _selectionMode = 0; // 0 = equipped slots (left), 1 = equippable list (right)
    private int _equippedSelected = 0; // which equipped slot is selected
    private PlayerGear? _gear;               // stored for HandleKey
    private Inventory? _inventory;           // stored for HandleKey

    public void HandleKey(Key key)
    {
        if (_selectionMode == 0)
        {
            // Navigating equipped slots (left side)
            if (key == Key.Up && _equippedSelected > 0)
                _equippedSelected--;
            else if (key == Key.Down && _equippedSelected < SlotNames.Length - 1)
                _equippedSelected++;
            else if (key == Key.Right || key == Key.Tab)
            {
                if (_equippableCount > 0) _selectionMode = 1; // Move to equippable list
            }
            else if (key == Key.Enter || key == Key.Space)
            {
                // Unequip the selected equipped slot
                var equipped = _gear?.GetEquipped(SlotNames[_equippedSelected]);
                if (equipped != null && _gear != null && _inventory != null)
                {
                    _inventory.UnequipItem(equipped.Id);
                    _gear.Unequip(SlotNames[_equippedSelected]);
                }
            }
            else
                return;
        }
        else
        {
            // Navigating equippable list (right side)
            if (key == Key.Up && _selected > 0)
                _selected--;
            else if (key == Key.Down && _selected < _equippableCount - 1)
                _selected++;
            else if (key == Key.Left || key == Key.Tab)
                _selectionMode = 0; // Back to equipped slots
            else if (key == Key.Enter || key == Key.Space)
            {
                // Equip/unequip the selected equippable item
                var equippable = EquippableSlots(_inventory!);
                if (_selected < equippable.Count && _gear != null && _inventory != null)
                    ToggleEquip(_inventory, _gear, equippable[_selected].ItemId!);
            }
            else
                return;
        }
    }

    /// <summary>Handle accelerated key repeats for UI navigation.</summary>
    private void HandleKeyRepeats(InputState input)
    {
        if (_selectionMode == 0)
        {
            // Navigating equipped slots (left side)
            if (input.KeyRepeatUp && _equippedSelected > 0)
                _equippedSelected--;
            else if (input.KeyRepeatDown && _equippedSelected < SlotNames.Length - 1)
                _equippedSelected++;
            else if (input.KeyRepeatRight && _equippableCount > 0)
                _selectionMode = 1; // Move to equippable list
        }
        else
        {
            // Navigating equippable list (right side)
            if (input.KeyRepeatUp && _selected > 0)
                _selected--;
            else if (input.KeyRepeatDown && _selected < _equippableCount - 1)
                _selected++;
            else if (input.KeyRepeatLeft)
                _selectionMode = 0; // Back to equipped slots
        }
    }

    /// <summary>Mouse handling; call once per frame from Game.Update while open.</summary>
    public void Update(InputState input, PlayerGear? gear, Inventory inventory, int screenW, int screenH)
    {
        HandleKeyRepeats(input);
        _gear = gear;
        _inventory = inventory;
        Layout(screenW, screenH);
        var ui = new UiInput(input);

        var equippable = EquippableSlots(inventory);
        _equippableCount = equippable.Count;

        // Mouse wheel scrolls the equippable list
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _equippableScroll = Math.Clamp(_equippableScroll - (int)scroll, 0, Math.Max(0, _equippableCount - EquippableRows));
        }

        for (int i = 0; i < EquippableRows && _equippableScroll + i < equippable.Count; i++)
        {
            if (ui.TryClick(_detailX, _listY + i * EquippableRowH, _detailW, EquippableRowH))
            {
                _selected = _equippableScroll + i;
                _selectionMode = 1;
                ToggleEquip(inventory, gear, equippable[_selected].ItemId!);
            }
        }

        // Click an equipped slot row to unequip it.
        for (int i = 0; i < SlotNames.Length; i++)
        {
            var equipped = gear?.GetEquipped(SlotNames[i]);
            if (equipped == null) continue;
            if (ui.TryClick(_rowX, _rowY[i], _rowW, RowH))
            {
                _equippedSelected = i;
                _selectionMode = 0;
                inventory.UnequipItem(equipped.Id);
                gear!.Unequip(SlotNames[i]);
            }
        }

        // Hover updates selection mode
        for (int i = 0; i < EquippableRows && _equippableScroll + i < equippable.Count; i++)
        {
            if (ui.Hovered(_detailX, _listY + i * EquippableRowH, _detailW, EquippableRowH))
                _selectionMode = 1;
        }
        for (int i = 0; i < SlotNames.Length; i++)
        {
            var equipped = gear?.GetEquipped(SlotNames[i]);
            if (equipped == null) continue;
            if (ui.Hovered(_rowX, _rowY[i], _rowW, RowH))
                _selectionMode = 0;
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, SpriteRenderer? sprites,
        PlayerGear? gear, Inventory inventory, int screenW, int screenHeight)
    {
        Layout(screenW, screenHeight);
        if (text == null) return;

        PanelChrome.Draw(batch, text, screenW, screenHeight, "GEAR", ContentW, ContentH,
            out _, out _, out _, out _);

        for (int i = 0; i < SlotNames.Length; i++)
            RenderSlotRow(batch, text, sprites, gear, SlotNames[i], SlotLabels[i], i);

        RenderRight(batch, text, sprites, gear, inventory);
    }

    private void RenderSlotRow(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        PlayerGear? gear, string slotName, string label, int i)
    {
        float y = _rowY[i];
        float cy = y + RowH * 0.5f;
        var equipped = gear?.GetEquipped(slotName);

        // Row well.
        bool equippedSelected = (_selectionMode == 0) && (_equippedSelected == i);
        byte wellR = equippedSelected ? (byte)70 : (byte)26;
        byte wellG = equippedSelected ? (byte)52 : (byte)18;
        byte wellB = equippedSelected ? (byte)30 : (byte)12;
        batch.DrawScreenQuad(_rowX + _rowW * 0.5f, cy, _rowW * 0.5f, RowH * 0.5f, wellR, wellG, wellB);
        DrawLeft(batch, text, label, _rowX + 10f, cy, 13, 150, 140, 130);

        if (equipped == null)
        {
            DrawRight(batch, text, "empty", _rowX + _rowW - 10f, cy, 13, 110, 100, 90);
            return;
        }

        var display = Data.ItemCatalog.Get(equipped.Id);
        uint tex = sprites?.GetSpriteTexture(display?.SpriteKey ?? equipped.SpriteKey) ?? 0;
        float chipX = _rowX + 96f;
        if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(chipX, cy, 15f, 15f, tex, 255, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(chipX, cy, 14f, 14f, 60, 48, 36);
            text.DrawText(batch, GlyphOf(display?.Name ?? equipped.Name), chipX, cy, 11,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }

        DrawLeft(batch, text, equipped.Name, _rowX + 122f, cy, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        DrawRight(batch, text, $"Dmg {equipped.Damage:0}  Atk +{equipped.AttackBonus:0}",
            _rowX + _rowW - 10f, cy, 13, 200, 190, 160);
    }

    private void RenderRight(PrimitiveBatch batch, TextRenderer text, SpriteRenderer? sprites,
        PlayerGear? gear, Inventory inventory)
    {
        float dividerX = _detailX - 10f;
        batch.DrawScreenQuad(dividerX, _detailY + 205f, 0.5f, 205f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 90);

        // Totals.
        DrawLeft(batch, text, "Totals", _detailX, _detailY + 6f, 13, 150, 140, 130);
        float damage = gear?.Weapon?.Damage ?? 0f;
        DrawLeft(batch, text, $"Damage {damage:0}", _detailX, _detailY + 30f, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
        DrawLeft(batch, text, $"Attack +{gear?.GetTotalAttackBonus() ?? 0f:0}", _detailX, _detailY + 52f, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
        DrawLeft(batch, text, $"Defence +{gear?.GetTotalDefenceBonus() ?? 0f:0}", _detailX, _detailY + 74f, 14,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);

        // Equippable inventory items (present in gear.json).
        DrawLeft(batch, text, "Equippable", _detailX, _detailY + 104f, 13, 150, 140, 130);
        var equippable = EquippableSlots(inventory);
        for (int i = 0; i < EquippableRows && _equippableScroll + i < equippable.Count; i++)
        {
            var slot = equippable[_equippableScroll + i];
            float y = _listY + i * EquippableRowH + EquippableRowH * 0.5f;
            bool selected = (_selectionMode == 1) && (_equippableScroll + i == _selected);
            bool isEquipped = slot.IsEquipped;
            if (selected)
                batch.DrawScreenQuad(_detailX + _detailW * 0.5f, y, _detailW * 0.5f, EquippableRowH * 0.5f, 34, 26, 20);

            var display = Data.ItemCatalog.Get(slot.ItemId!);
            DrawLeft(batch, text, display?.Name ?? slot.ItemId!, _detailX + 8f, y, 13,
                isEquipped ? (byte)200 : (selected ? (byte)200 : (byte)170),
                isEquipped ? (byte)170 : (selected ? (byte)220 : (byte)160),
                isEquipped ? (byte)60 : (selected ? (byte)140 : (byte)140));
            if (isEquipped)
                DrawRight(batch, text, "Wielded", _detailX + _detailW - 8f, y, 12, 200, 170, 60, bold: true);
        }
    }

    private static List<InventorySlot> EquippableSlots(Inventory inventory)
    {
        var gear = Data.GearItem.LoadAll();
        var slots = new List<InventorySlot>();
        foreach (var slot in inventory.Slots)
        {
            if (slot.ItemId != null && gear.ContainsKey(slot.ItemId))
                slots.Add(slot);
        }
        return slots;
    }

    // Equip/unequip keeps both sources of truth in sync: the inventory slot
    // flag and the PlayerGear slot. Shared with InventoryPanel so both panels
    // drive the same path. Internal for tests.
    internal static void ToggleEquip(Inventory inventory, PlayerGear? gear, string itemId)
    {
        if (gear == null) return;
        if (!Data.GearItem.LoadAll().TryGetValue(itemId, out var gearItem)) return;

        bool isEquipped = false;
        foreach (var slot in inventory.Slots)
        {
            if (slot.ItemId == itemId && slot.IsEquipped)
            {
                isEquipped = true;
                break;
            }
        }

        if (isEquipped)
        {
            inventory.UnequipItem(itemId);
            gear.Unequip(gearItem.Slot);
        }
        else
        {
            inventory.EquipItem(itemId);
            gear.Equip(gearItem);
            // Equipping into a slot retires the previous occupant: clear the
            // replaced item's equipped flag (e.g. an axe replaces a torch in
            // the weapon slot) so the flag agrees with PlayerGear.
            var registry = Data.GearItem.LoadAll();
            foreach (var slot in inventory.Slots)
            {
                if (!slot.IsEquipped || slot.ItemId == null || slot.ItemId == itemId) continue;
                if (registry.TryGetValue(slot.ItemId, out var other) && other.Slot == gearItem.Slot)
                    slot.IsEquipped = false;
            }
        }
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
        _cy = cy + 34f + 16f;

        _rowX = _cx + 4f;
        _rowW = ContentW * 0.52f;
        for (int i = 0; i < SlotNames.Length; i++)
            _rowY[i] = _cy + 4f + i * (RowH + RowGap);

        _detailX = _cx + ContentW * 0.58f;
        _detailW = ContentW - ContentW * 0.58f - 4f;
        _detailY = _cy + 4f;
        _listY = _detailY + 126f;
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
} // GearPanel

/// <summary>
/// DashboardPanel — Unified player and colony dashboard.
/// </summary>
public sealed class DashboardPanel
{
    private bool _showWorkOrders;
    private string? _selectedWorkplaceKey;
    private int _workRecipeIndex;
    private string? _selectedColonyItemId;
    private string? _selectedColonistId;
    private int _colonyItemScroll;
    private int _colonistScroll;
    private string _colonyStatus = "Select a stockpile item, then transfer it.";
    public bool Visible { get; set; } = false;
    public Game? Game { get; set; }
    public string[] Tabs { get; } = ["inventory", "skills", "crafting", "quests", "diplomacy", "colony"];
    public string ActiveTab { get; private set; } = "inventory";
    /// <summary>Currently selected colonist id (read-only view for tests/HUD).</summary>
    public string? SelectedColonistId => _selectedColonistId;
    /// <summary>Visible-row scroll offset for the colonist list.</summary>
    public int ColonistScroll => _colonistScroll;

    /// <summary>Headline skill for a colonist row (per-recruit skill stats
    /// slice): guards read their attack level, everyone else their highest
    /// trained work skill; a recruit with no training reads "untrained".
    /// Public so tests assert the label without a draw pass.</summary>
    public static string ColonistSkillLabel(RecruitNpc recruit)
    {
        if (recruit.RecruitBehavior == "guard")
        {
            int attack = recruit.Skills.GetSkillLevel("attack");
            return attack > 1 ? $"attack {attack}" : "untrained";
        }
        string[] workSkills = ["foraging", "woodcutting", "mining", "construction"];
        string best = "";
        int bestLevel = 1;
        foreach (var skill in workSkills)
        {
            int level = recruit.Skills.GetSkillLevel(skill);
            if (level > bestLevel)
            {
                bestLevel = level;
                best = skill;
            }
        }
        return best.Length > 0 ? $"{best} {bestLevel}" : "untrained";
    }
    public Action<string>? OnTabSelected { get; set; }
    public void SetActive(string tab) { if (Tabs.Contains(tab)) ActiveTab = tab; }
    public void HandleKey(Key key)
    {
        int i = Array.IndexOf(Tabs, ActiveTab);
        if (key == Key.Left || key == Key.Up) SetActive(Tabs[(i + Tabs.Length - 1) % Tabs.Length]);
        else if (key == Key.Right || key == Key.Down) SetActive(Tabs[(i + 1) % Tabs.Length]);
    }
    public void HandleConfirm() => OnTabSelected?.Invoke(ActiveTab);
    public void Update(InputState input, int screenWidth, int screenHeight)
    {
        var ui = new UiInput(input);
        float x = screenWidth * 0.5f - 325f;
        float y = screenHeight * 0.5f - 158f;
        float tabWidth = 650f / Tabs.Length;
        for (int i = 0; i < Tabs.Length; i++)
        {
            float buttonX = x + tabWidth * i + 3f;
            if (ui.TryClick(buttonX, y + 25f, tabWidth - 6f, 40f))
            {
                SetActive(Tabs[i]);
                HandleConfirm();
                return;
            }
        }

        // Diplomacy tab: merchant visit status readout
        if (ActiveTab == "diplomacy")
        {
            var activeMerchant = Game?.MerchantVisitSystem?.ActiveMerchant;
            if (activeMerchant != null
                && ui.TryClick(x + 292f, y + 230f, 240f, 22f))
            {
                // Open the existing trade panel via the canonical NPC flow
                if (Game != null)
                {
                    var flows = new Interactions.NPCFlows(Game);
                    flows.OpenTradePanel(activeMerchant);
                    _colonyStatus = $"Trading with {activeMerchant.Name}.";
                }
            }
            return;
        }

        if (ActiveTab != "colony" || Game?.ColonySystem == null) return;
        var colony = Game.ColonySystem;
        var inventory = Game.Player?.Inventory ?? Game.Inventory;
        if (!colony.IsFounded)
        {
            if (Game.Player != null && ui.TryClick(x + 333f, y + 242f, 284f, 40f))
            {
                bool founded = colony.FoundAt(Game.Player.WorldX, Game.Player.WorldY, Game.World);
                _colonyStatus = founded
                    ? "Settlement founded. Assistants will gather within its work radius."
                    : "Choose a dry surface tile to found your settlement.";
            }
            return;
        }

        if (ui.TryClick(x + 480f, y + 101f, 145f, 22f))
        {
            _showWorkOrders = !_showWorkOrders;
            return;
        }

        if (_showWorkOrders)
        {
            UpdateColonyWorkOrders(ui, x, y);
            return;
        }

        var recruits = GetRecruits();
        if (_selectedColonistId == null || recruits.All(n => n.NpcId != _selectedColonistId))
            _selectedColonistId = recruits.FirstOrDefault()?.NpcId;
        // Colonist list scroll (mouse wheel): 3 visible rows, keep the
        // selection in view when the population outgrows the panel.
        _colonistScroll = Math.Clamp(_colonistScroll - Math.Sign(ui.GetScroll()), 0,
            Math.Max(0, recruits.Length - 3));
        for (int i = 0; i < Math.Min(3, recruits.Length - _colonistScroll); i++)
        {
            if (ui.TryClick(x + 16f, y + 215f + i * 22f, 250f, 21f))
            {
                _selectedColonistId = recruits[_colonistScroll + i].NpcId;
                return;
            }
        }
        if (_selectedColonistId != null && ui.TryClick(x + 20f, y + 284f, 112f, 30f))
            AssignSelectedRecruit("assistant");
        else if (_selectedColonistId != null && ui.TryClick(x + 148f, y + 284f, 112f, 30f))
            AssignSelectedRecruit("guard");
        else if (_selectedColonistId != null && Game?.CaveWorlds?.IsInside == true
            && ui.TryClick(x + 276f, y + 284f, 118f, 30f))
        {
            // Cave expedition: move the selected guard underground with the player
            var recruit = GetRecruits().FirstOrDefault(npc => npc.NpcId == _selectedColonistId);
            if (recruit != null && recruit.RecruitBehavior != "guard")
                _colonyStatus = $"{recruit.Name} must be a guard to join the expedition.";
            else if (recruit != null && Game!.CaveWorlds!.BringGuard(recruit.NpcId))
                _colonyStatus = $"{recruit.Name} joined the cave expedition.";
            else if (recruit != null)
                _colonyStatus = $"{recruit.Name} is already on the expedition.";
        }

        var itemIds = GetColonyItemIds(colony, inventory);
        _colonyItemScroll = Math.Clamp(_colonyItemScroll - Math.Sign(ui.GetScroll()), 0,
            Math.Max(0, itemIds.Length - 6));
        if (_selectedColonyItemId == null || !itemIds.Contains(_selectedColonyItemId))
            _selectedColonyItemId = itemIds.Skip(_colonyItemScroll).FirstOrDefault();
        for (int i = 0; i < Math.Min(6, itemIds.Length - _colonyItemScroll); i++)
        {
            if (ui.TryClick(x + 292f, y + 124f + i * 25f, 348f, 24f))
            {
                _selectedColonyItemId = itemIds[_colonyItemScroll + i];
                return;
            }
        }

        if (_selectedColonyItemId == null) return;
        if (ui.TryClick(x + 365f, y + 286f, 118f, 30f))
        {
            int moved = colony.Deposit(_selectedColonyItemId, inventory);
            _colonyStatus = moved > 0 ? $"Stored {moved} {_selectedColonyItemId}." : "Nothing moved; check storage capacity.";
        }
        else if (ui.TryClick(x + 500f, y + 286f, 118f, 30f))
        {
            int moved = colony.Withdraw(_selectedColonyItemId, inventory);
            _colonyStatus = moved > 0 ? $"Withdrew {moved} {_selectedColonyItemId}." : "Nothing moved; check inventory space.";
        }
    }

    public bool UpdateReturnButton(InputState input, int screenWidth)
    {
        var ui = new UiInput(input);
        return ui.TryClick(screenWidth - 142f, 14f, 128f, 34f);
    }

    public void RenderReturnButton(PrimitiveBatch batch, TextRenderer? text, int screenWidth)
    {
        float x = screenWidth - 78f;
        batch.DrawScreenQuad(x, 31f, 64f, 17f, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 220);
        batch.DrawScreenQuad(x, 31f, 61f, 14f, PanelChrome.PlateR, PanelChrome.PlateG, PanelChrome.PlateB, 255);
        text?.DrawText(batch, "O  DASHBOARD", x, 31f, 11, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight)
    {
        PanelChrome.Draw(batch, text, screenWidth, screenHeight, "DASHBOARD", 650, 350,
            out float x, out float y, out _, out _);
        if (text == null) return;
        for (int i = 0; i < Tabs.Length; i++)
        {
            float cx = x + (650f / Tabs.Length) * (i + 0.5f);
            bool active = Tabs[i] == ActiveTab;
            batch.DrawScreenQuad(cx, y + 45, 48, 20, active ? (byte)80 : (byte)30, 50, 25);
            text.DrawText(batch, Tabs[i].ToUpperInvariant(), cx, y + 45, 12,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: active);
        }

        if (ActiveTab == "colony") RenderColony(batch, text, x, y);
        else RenderOverview(batch, text, x, y);
        if (ActiveTab == "colony")
            text.DrawText(batch, _colonyStatus, x + 325, y + 330, 11, 190, 178, 145);
        else
            text.DrawText(batch, "Choose a tab · Enter or click to open · O / Esc closes",
                x + 325, y + 330, 12, 160, 150, 130);
    }

    private void RenderColony(PrimitiveBatch batch, TextRenderer text, float x, float y)
    {
        var game = Game;
        var colony = game?.ColonySystem;
        var player = game?.Player;
        var inventory = player?.Inventory ?? game?.Inventory;
        if (colony == null) return;

        batch.DrawScreenQuad(x + 127, y + 194, 122, 116, 24, 17, 11, 210);
        Left(batch, text, "SETTLEMENT", x + 16, y + 105, 12,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
        if (!colony.IsFounded)
        {
            Left(batch, text, "No settlement founded", x + 20, y + 141, 16);
            Left(batch, text, "Found one at your current dry surface tile.", x + 20, y + 175, 12);
            Left(batch, text, "Assistants will work around its anchor.", x + 20, y + 197, 12);
            Left(batch, text, _colonyStatus, x + 16, y + 222, 11, 190, 178, 145);
            batch.DrawScreenQuad(x + 475, y + 262, 142, 20, 74, 55, 24);
            text.DrawText(batch, "FOUND SETTLEMENT", x + 475, y + 262, 12,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }
        else
        {
            Left(batch, text, $"Anchor  {colony.AnchorTileX}, {colony.AnchorTileY}", x + 16, y + 138, 13);
            Left(batch, text, $"Work radius  {colony.WorkRadiusTiles} tiles", x + 16, y + 164, 13);
            Left(batch, text, $"Stores  {colony.StoredUnits} / {colony.StorageCapacity} units", x + 16, y + 190, 12);
            var recruits = GetRecruits();
            Left(batch, text, $"COLONISTS · {recruits.Length} residents · select one, then assign a job",
                x + 16, y + 211, 10,
                PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
            for (int i = 0; i < Math.Min(3, recruits.Length - _colonistScroll); i++)
            {
                var npc = recruits[_colonistScroll + i];
                float rowY = y + 225 + i * 22;
                bool selected = npc.NpcId == _selectedColonistId;
                if (selected) batch.DrawScreenQuad(x + 131, rowY, 115, 10, 68, 48, 23, 210);
                Left(batch, text, npc.Name, x + 20, rowY, 11,
                    selected ? PanelChrome.BorderR : PanelChrome.TextR,
                    selected ? PanelChrome.BorderG : PanelChrome.TextG,
                    selected ? PanelChrome.BorderB : PanelChrome.TextB);
                var workplace = game?.BuildingSystem?.Structures
                    .FirstOrDefault(structure => structure.AssignedNpcId == npc.NpcId);
                string jobLabel = workplace == null
                    ? npc.RecruitBehavior ?? "unassigned"
                    : ColonyWorkLabel(workplace.WorkStatus);
                if (npc.ColonyRestStatus is "Resting" or "Seeking rest")
                    jobLabel = npc.ColonyRestStatus == "Resting" ? "resting" : "seeking rest";
                else if (npc.ColonyRest <= 25f)
                    jobLabel = "exhausted";
                else if (npc.CarriedQuantity > 0 && !string.IsNullOrEmpty(npc.CarriedItemId))
                    jobLabel = $"hauling {npc.CarriedQuantity} {npc.CarriedItemId}";
                byte needColor = npc.ColonyNeedStatus == "Starving" ? (byte)220
                    : npc.ColonyNeedStatus == "Hungry" ? (byte)210 : (byte)150;
                string needAndHealth = $"{(npc.ColonyNeedStatus == "Fed" ? "fed" : npc.ColonyNeedStatus.ToLowerInvariant())} {npc.Health}hp";
                text.DrawText(batch, needAndHealth, x + 164, rowY, 9, needColor, 150, 120);
                // Per-recruit skills: headline skill rides the row, right of
                // the need/health readout, dim so the job label stays primary.
                if (npc is RecruitNpc recruitNpc)
                    text.DrawText(batch, ColonistSkillLabel(recruitNpc), x + 236, rowY, 9,
                        165, 150, 120);
                byte restColor = npc.ColonyRest <= 25f ? (byte)210
                    : npc.ColonyRest <= 50f ? (byte)190
                    : npc.ColonyRest <= 75f ? (byte)165 : (byte)110;
                batch.DrawScreenQuad(x + 164, rowY + 8, 16, 1.5f, 42, 34, 26, 220);
                float restWidth = 16f * Math.Clamp(npc.ColonyRest / 100f, 0f, 1f);
                if (restWidth > 0f)
                    batch.DrawScreenQuad(x + 148 + restWidth, rowY + 8, restWidth, 1.5f,
                        restColor, (byte)Math.Min(220, (int)restColor), 95, 235);
                text.DrawText(batch, jobLabel, x + 198, rowY, 9,
                    PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
            }
            if (recruits.Length == 0)
                Left(batch, text, "Recruit people to staff the settlement.", x + 20, y + 237, 11, 180, 170, 150);
            batch.DrawScreenQuad(x + 76, y + 299, 56, 14, 74, 55, 24);
            batch.DrawScreenQuad(x + 204, y + 299, 56, 14, 74, 55, 24);
            text.DrawText(batch, "ASSISTANT", x + 76, y + 299, 10,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            text.DrawText(batch, "GUARD", x + 204, y + 299, 10,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            if (Game?.CaveWorlds?.IsInside == true)
            {
                // Cave expedition button — visible only while inside a cave
                batch.DrawScreenQuad(x + 335, y + 299, 80, 14, 74, 55, 24);
                text.DrawText(batch, "CAVE EXPEDITION", x + 335, y + 299, 9,
                    (byte)200, (byte)160, (byte)90, bold: true);
            }
        }

        batch.DrawScreenQuad(x + 464, y + 194, 180, 116, 24, 17, 11, 210);
        batch.DrawScreenQuad(x + 564, y + 105, 73, 12, 74, 55, 24);
        text.DrawText(batch, _showWorkOrders ? "STORES" : "WORK", x + 564, y + 105, 10,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        Left(batch, text, _showWorkOrders ? "WORK ORDERS" : "STOCKPILE", x + 292, y + 105, 12,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
        if (colony.IsFounded)
        {
            var workplaces = game?.BuildingSystem?.Structures.Where(s => s.IsActive && s.AssignedNpcId != null).ToArray() ?? [];
            if (_showWorkOrders)
            {
                RenderColonyWorkOrders(batch, text, game, x, y);
                return;
            }
            int working = workplaces.Count(s => s.WorkStatus.StartsWith("Working", StringComparison.Ordinal));
            Left(batch, text, $"Workplaces  {working} active / {workplaces.Length} staffed", x + 292, y + 120, 10,
                190, 178, 145);
            var itemIds = GetColonyItemIds(colony, inventory);
            _colonyItemScroll = Math.Clamp(_colonyItemScroll, 0, Math.Max(0, itemIds.Length - 6));
            for (int i = 0; i < Math.Min(6, itemIds.Length - _colonyItemScroll); i++)
            {
                string itemId = itemIds[_colonyItemScroll + i];
                float rowY = y + 137 + i * 25;
                bool selected = itemId == _selectedColonyItemId;
                if (selected)
                    batch.DrawScreenQuad(x + 466, rowY, 174, 12, 68, 48, 23, 210);
                int stored = colony.Stockpile.TryGetValue(itemId, out int quantity) ? quantity : 0;
                int carried = inventory?.GetItemQuantity(itemId) ?? 0;
                Left(batch, text, itemId, x + 300, rowY, 11,
                    selected ? PanelChrome.BorderR : PanelChrome.TextR,
                    selected ? PanelChrome.BorderG : PanelChrome.TextG,
                    selected ? PanelChrome.BorderB : PanelChrome.TextB);
                text.DrawText(batch, $"{stored} stored  ·  {carried} carried", x + 570, rowY, 10,
                    PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
            }
            if (itemIds.Length > 6)
                text.DrawText(batch, $"{_colonyItemScroll + 1}–{Math.Min(itemIds.Length, _colonyItemScroll + 6)} / {itemIds.Length}  ·  scroll",
                    x + 575, y + 118, 9, 180, 170, 150);
            batch.DrawScreenQuad(x + 410, y + 301, 59, 15, 74, 55, 24);
            batch.DrawScreenQuad(x + 545, y + 301, 59, 15, 74, 55, 24);
            text.DrawText(batch, "DEPOSIT", x + 410, y + 301, 11,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
            text.DrawText(batch, "WITHDRAW", x + 545, y + 301, 11,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }
    }

    private void UpdateColonyWorkOrders(UiInput ui, float x, float y)
    {
        var structures = Game?.BuildingSystem?.Structures.Where(s => s.IsActive)
            .OrderBy(s => s.TileY).ThenBy(s => s.TileX).ToArray() ?? [];
        if (_selectedWorkplaceKey == null || structures.All(s => WorkplaceKey(s) != _selectedWorkplaceKey))
            _selectedWorkplaceKey = structures.Length > 0 ? WorkplaceKey(structures[0]) : null;
        for (int i = 0; i < Math.Min(4, structures.Length); i++)
        {
            if (ui.TryClick(x + 294f, y + 137f + i * 25f, 340f, 24f))
            {
                _selectedWorkplaceKey = WorkplaceKey(structures[i]);
                _workRecipeIndex = 0;
                return;
            }
        }
        var selected = structures.FirstOrDefault(s => WorkplaceKey(s) == _selectedWorkplaceKey);
        var recipes = CompatibleWorkRecipes(selected).ToArray();
        if (selected == null) return;

        // Structure-upgrades slice: enqueue the successor-tier upgrade as a
        // worker construction job (materials charged from the stockpile).
        if (selected.StructureDef.UpgradesTo != null
            && ui.TryClick(x + 300f, y + 320f, 240f, 16f))
        {
            var (ok, message) = Game?.BuildingSystem != null && Game.Player != null
                ? Game.BuildingSystem.UpgradeStructure(
                    selected, selected.StructureDef.UpgradesTo,
                    Game.Player.SkillManager, Game.ColonySystem)
                : (false, "Building system unavailable.");
            _colonyStatus = message;
            return;
        }
        if (recipes.Length == 0) return;
        if (ui.TryClick(x + 300f, y + 292f, 32f, 16f)) _workRecipeIndex = (_workRecipeIndex + recipes.Length - 1) % recipes.Length;
        else if (ui.TryClick(x + 566f, y + 292f, 32f, 16f)) _workRecipeIndex = (_workRecipeIndex + 1) % recipes.Length;
        else if (ui.TryClick(x + 338f, y + 292f, 70f, 16f))
        {
            selected.WorkRecipeId = recipes[_workRecipeIndex].RecipeId;
            selected.WorkRecipeQueue.Clear();
            selected.WorkOrdersPaused = false;
            selected.HasManualWorkOrder = true;
            selected.WorkProgress = 0;
            selected.WorkStatus = selected.AssignedNpcId == null ? "Waiting for worker" : "Waiting for materials";
            _colonyStatus = $"{selected.StructureId}: set {recipes[_workRecipeIndex].Name} as the active order.";
        }
        else if (ui.TryClick(x + 414f, y + 292f, 70f, 16f))
        {
            if (selected.WorkOrdersPaused || selected.WorkRecipeId == null)
            {
                selected.WorkRecipeId = recipes[_workRecipeIndex].RecipeId;
                selected.WorkRecipeQueue.Clear();
                selected.WorkOrdersPaused = false;
                selected.WorkProgress = 0;
            }
            else
            {
                selected.WorkRecipeQueue.Add(recipes[_workRecipeIndex].RecipeId);
            }
            selected.HasManualWorkOrder = true;
            selected.WorkStatus = "Waiting for materials";
            _colonyStatus = $"Queued {recipes[_workRecipeIndex].Name} at {selected.StructureId}.";
        }
        else if (ui.TryClick(x + 490f, y + 292f, 70f, 16f))
        {
            selected.WorkRecipeId = null;
            selected.WorkRecipeQueue.Clear();
            selected.WorkOrdersPaused = true;
            selected.HasManualWorkOrder = true;
            selected.WorkProgress = 0;
            selected.AssignedNpcId = null;
            selected.WorkStatus = "Work orders paused";
            _colonyStatus = $"Cleared and paused orders at {selected.StructureId}.";
        }
        else if (selected.StructureDef.UpgradesTo != null
            && ui.TryClick(x + 300f, y + 320f, 90f, 16f))
        {
            // Structure-upgrades slice: enqueue the successor-tier upgrade as
            // a worker construction job (materials charged from the stockpile).
            var (ok, message) = Game!.BuildingSystem!.UpgradeStructure(
                selected, selected.StructureDef.UpgradesTo,
                Game.Player.SkillManager, Game.ColonySystem);
            _colonyStatus = message;
        }
    }

    private void RenderColonyWorkOrders(PrimitiveBatch batch, TextRenderer text, Game? game, float x, float y)
    {
        var structures = game?.BuildingSystem?.Structures.Where(s => s.IsActive)
            .OrderBy(s => s.TileY).ThenBy(s => s.TileX).ToArray() ?? [];
        Left(batch, text, "BUILT WORKPLACES · select one", x + 300, y + 124, 10, 190, 178, 145);
        for (int i = 0; i < Math.Min(4, structures.Length); i++)
        {
            var structure = structures[i];
            float rowY = y + 140 + i * 25;
            if (WorkplaceKey(structure) == _selectedWorkplaceKey)
                batch.DrawScreenQuad(x + 466, rowY, 174, 12, 68, 48, 23, 210);
            string worker = structure.AssignedNpcId == null ? "unstaffed" : "staffed";
            Left(batch, text, $"{structure.StructureId} · {worker}", x + 300, rowY, 10);
            string order = structure.WorkRecipeId ?? (structure.WorkOrdersPaused ? "paused" : "automatic");
            if (structure.WorkRecipeQueue.Count > 0) order += $" +{structure.WorkRecipeQueue.Count}";
            text.DrawText(batch, order, x + 570, rowY, 9, 180, 170, 150);
        }
        var selected = structures.FirstOrDefault(s => WorkplaceKey(s) == _selectedWorkplaceKey);
        var recipes = CompatibleWorkRecipes(selected).ToArray();
        if (selected == null)
        {
            Left(batch, text, "Build a production station to create work orders.", x + 300, y + 257, 10, 180, 170, 150);
            return;
        }
        // Structure-upgrades slice: show the successor-tier upgrade action.
        if (selected.StructureDef.UpgradesTo is { } upgradeTarget)
        {
            var targetDef = game?.BuildingSystem?.Registry?.GetStructure(upgradeTarget);
            bool midUpgrade = selected.IsUnderConstruction;
            string label = midUpgrade ? $"UPGRADING · {selected.WorkStatus}"
                : $"UPGRADE TO {(targetDef?.Name ?? upgradeTarget).ToUpperInvariant()}";
            batch.DrawScreenQuad(x + 300, y + 320, 240, 16, 74, 55, 24);
            text.DrawText(batch, label, x + 420, y + 320, 9,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }
        if (recipes.Length == 0)
        {
            Left(batch, text, $"No recipes use {selected.StructureId}.", x + 300, y + 257, 10, 180, 170, 150);
            return;
        }
        _workRecipeIndex = Math.Clamp(_workRecipeIndex, 0, recipes.Length - 1);
        var recipe = recipes[_workRecipeIndex];
        Left(batch, text, $"Order: {recipe.Name}  ·  {recipe.OutputQuantity} {recipe.OutputItem}", x + 300, y + 249, 10);
        string activeOrder = selected.WorkRecipeId ?? (selected.WorkOrdersPaused ? "paused" : "automatic");
        Left(batch, text, $"Active: {activeOrder}  ·  {selected.WorkStatus}", x + 300, y + 263, 9, 180, 170, 150);
        string queuePreview = string.Join(", ", selected.WorkRecipeQueue.Take(2));
        if (selected.WorkRecipeQueue.Count > 2) queuePreview += $" +{selected.WorkRecipeQueue.Count - 2}";
        Left(batch, text, $"Next: {(queuePreview.Length == 0 ? "none" : queuePreview)}", x + 300, y + 276, 9, 180, 170, 150);
        foreach (var (label, bx, width) in new[] { ("<", 300f, 32f), ("SET", 338f, 70f), ("QUEUE", 414f, 70f), ("CLEAR", 490f, 70f), (">", 566f, 32f) })
        {
            batch.DrawScreenQuad(x + bx, y + 292, width, 16, 74, 55, 24);
            text.DrawText(batch, label, x + bx + width / 2, y + 292, 9,
                PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB, bold: true);
        }
    }

    private IEnumerable<CraftRecipe> CompatibleWorkRecipes(Structure? structure)
    {
        var registry = Game?.Crafting?.Registry;
        if (structure == null || registry == null) return [];
        return registry.Recipes.Values.Where(recipe =>
                (!string.IsNullOrWhiteSpace(recipe.RequiresStructure)
                    && recipe.RequiresStructure == structure.StructureId)
                || (recipe.RequiresCampfire
                    && structure.StructureId is "campfire" or "cooking_station" or "furnace" or "smelter"))
            .OrderBy(recipe => recipe.Tier).ThenBy(recipe => recipe.Name, StringComparer.Ordinal);
    }

    private static string WorkplaceKey(Structure structure) => $"{structure.StructureId}:{structure.TileX}:{structure.TileY}";

    private static string[] GetColonyItemIds(ColonySystem colony, Inventory? inventory)
        => colony.Stockpile.Keys
            .Concat(inventory?.Slots.Where(slot => slot.ItemId != null && slot.Quantity > 0)
                .Select(slot => slot.ItemId!) ?? [])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

    private Npc[] GetRecruits()
        => Game?.NPCSystem?.NPCs
            .Where(npc => npc.IsActive && npc.IsRecruited)
            .OrderBy(npc => npc.Name, StringComparer.Ordinal)
            .ToArray() ?? [];

    private void AssignSelectedRecruit(string behavior)
    {
        var recruit = GetRecruits().FirstOrDefault(npc => npc.NpcId == _selectedColonistId);
        if (recruit == null) return;
        if (!recruit.AvailableBehaviors.Contains(behavior))
        {
            _colonyStatus = $"{recruit.Name} cannot take the {behavior} job.";
            return;
        }

        recruit.RecruitBehavior = behavior;
        foreach (var structure in Game?.BuildingSystem?.Structures.Where(s => s.AssignedNpcId == recruit.NpcId) ?? [])
        {
            structure.AssignedNpcId = null;
            structure.WorkStatus = structure.WorkRecipeId == null ? "Idle" : "Waiting for worker";
        }
        recruit.AssignBehavior(behavior);
        Game?.RecruitmentSystem?.OnRecruit(recruit.NpcId, behavior);
        _colonyStatus = $"{recruit.Name} assigned as {behavior}.";
    }

    private static string ColonyWorkLabel(string status)
    {
        if (status.StartsWith("Working", StringComparison.Ordinal)) return "working";
        if (status == "Worker en route") return "travelling";
        if (status == "Waiting for materials") return "gathering inputs";
        if (status.StartsWith("Growing wheat", StringComparison.Ordinal)) return "farming";
        if (status == "Fallow (winter)" || status == "Crop dormant (winter)") return "winter rest";
        if (status == "Waiting for stockpile space") return "stores full";
        if (status.StartsWith("No food", StringComparison.Ordinal)) return "needs food";
        if (status.StartsWith("Produced", StringComparison.Ordinal)) return "work done";
        if (status.StartsWith("Danger nearby", StringComparison.Ordinal)) return "danger — paused";
        return "workplace idle";
    }

    private void RenderOverview(PrimitiveBatch batch, TextRenderer text, float x, float y)
    {
        var game = Game;
        var player = game?.Player;
        var inventory = player?.Inventory ?? game?.Inventory;
        var skills = player?.SkillManager ?? game?.SkillManager;
        var activeQuests = game?.QuestSystem?.Active.Count ?? 0;
        var completedQuests = game?.QuestSystem?.Completed.Count ?? 0;
        var items = inventory?.Slots.Count(slot => slot.ItemId != null && slot.Quantity > 0) ?? 0;
        var recruits = player?.RecruitedNpcs.Count ?? 0;
        var season = game?.SeasonSystem?.CurrentSeason ?? "unknown";
        string biome = "unknown";
        if (player != null && game?.World != null)
        {
            var (tx, ty) = player.GetTilePosition();
            biome = game.World.GetTile(tx, ty)?.Biome?.Id ?? biome;
        }

        // Left column: immediate player and world state.
        batch.DrawScreenQuad(x + 127, y + 194, 122, 111, 24, 17, 11, 210);
        Left(batch, text, "CURRENT STATUS", x + 16, y + 105, 12, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
        Left(batch, text, $"Season  {Pretty(season)}", x + 16, y + 132, 13);
        Left(batch, text, $"Biome   {Pretty(biome)}", x + 16, y + 154, 13);
        Left(batch, text, $"HP      {Value(game?.Survival?.Hp)} / {Value(game?.Survival?.MaxHp)}", x + 16, y + 176, 13);
        Left(batch, text, $"Hunger  {Value(game?.Survival?.Hunger)} / {Value(game?.Survival?.MaxHunger)}", x + 16, y + 198, 13);
        Left(batch, text, $"Items   {items} / {inventory?.Slots.Count ?? 0} slots", x + 16, y + 220, 13);
        Left(batch, text, $"Recruits {recruits}   Deaths {game?.DeathCount ?? 0}", x + 16, y + 242, 12);

        // Right column: actionable counts tied to the selected destination.
        batch.DrawScreenQuad(x + 464, y + 194, 180, 111, 24, 17, 11, 210);
        Left(batch, text, "AT A GLANCE", x + 292, y + 105, 12, PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
        switch (ActiveTab)
        {
            case "inventory":
                Left(batch, text, $"Gold  {inventory?.GetItemQuantity(TradeSystem.GoldItemId) ?? 0}", x + 292, y + 137, 14);
                Left(batch, text, $"Occupied slots  {items}", x + 292, y + 162, 14);
                Left(batch, text, $"Equipped weapon  {player?.Gear?.Weapon?.Name ?? "None"}", x + 292, y + 187, 13);
                Left(batch, text, "Open inventory to eat or inspect items.", x + 292, y + 226, 12, 180, 170, 150);
                break;
            case "skills":
                Left(batch, text, $"Attack {skills?.GetSkillLevel("attack") ?? 1}   Intelligence {skills?.GetSkillLevel("intelligence") ?? 1}", x + 292, y + 137, 13);
                Left(batch, text, $"Woodcutting {skills?.GetSkillLevel("woodcutting") ?? 1}   Mining {skills?.GetSkillLevel("mining") ?? 1}", x + 292, y + 162, 12);
                Left(batch, text, $"Active quests  {activeQuests}", x + 292, y + 187, 13);
                Left(batch, text, "Spend skill points in the skills panel.", x + 292, y + 226, 12, 180, 170, 150);
                break;
            case "crafting":
                Left(batch, text, $"Known recipes  {game?.Crafting?.Registry?.Recipes.Count ?? 0}", x + 292, y + 137, 14);
                Left(batch, text, $"Unlocked recipes  {player?.UnlockedRecipes.Count ?? 0}", x + 292, y + 162, 14);
                Left(batch, text, $"Crafting level  {skills?.GetSkillLevel("crafting") ?? 1}", x + 292, y + 187, 14);
                Left(batch, text, "Check ingredients and gates before crafting.", x + 292, y + 226, 12, 180, 170, 150);
                break;
            case "quests":
                Left(batch, text, $"Active  {activeQuests}", x + 292, y + 137, 14);
                Left(batch, text, $"Completed  {completedQuests}", x + 292, y + 162, 14);
                Left(batch, text, "Accept new work by speaking with quest givers.", x + 292, y + 201, 12, 180, 170, 150);
                break;
            case "diplomacy":
                Left(batch, text, $"Known factions  {game?.FactionRegistry?.Factions.Count ?? 0}", x + 292, y + 137, 14);
                Left(batch, text, $"Recruitable companions  {recruits}", x + 292, y + 162, 14);

                // Per-faction standing rows (up to 4 shown)
                var factions = game?.FactionRegistry?.Factions.Values.ToArray() ?? [];
                float rowY = y + 192f;
                int shown = 0;
                foreach (var f in factions)
                {
                    if (shown >= 4) break;
                    float standing = game?.FactionSystem?.StandingOf(f.FactionId) ?? 0.5f;
                    string tier = FactionSystem.TierName(standing);
                    Left(batch, text, $"{f.Name}  {tier} ({standing:P0})", x + 292, rowY, 12,
                        standing >= 0.65f ? (byte)120 : standing < 0.25f ? (byte)200 : PanelChrome.TextR,
                        standing >= 0.65f ? (byte)220 : standing < 0.25f ? (byte)90 : PanelChrome.TextG,
                        standing >= 0.65f ? (byte)120 : standing < 0.25f ? (byte)90 : PanelChrome.TextB);
                    rowY += 17;
                    shown++;
                }

                // Merchant visit UI
                var activeMerchant = game?.MerchantVisitSystem?.ActiveMerchant;
                if (activeMerchant != null)
                {
                    Left(batch, text, $"Visiting Merchant: {activeMerchant.Name}", x + 292, y + 276, 13, 200, 180, 90);
                    Left(batch, text, $"Faction: {activeMerchant.FactionId} · Departs at 10:00 · click to trade", x + 292, y + 294, 11, 180, 170, 150);
                }
                else
                {
                    Left(batch, text, "No merchant visiting today.", x + 292, y + 276, 12, 180, 170, 150);
                }
                break;
        }
        Left(batch, text, ActiveTab.ToUpperInvariant(), x + 292, y + 265, 12,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, true);
    }

    private static string Pretty(string value) => string.IsNullOrEmpty(value)
        ? "Unknown"
        : string.Join(' ', value.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static string Value(float? value) => value.HasValue ? MathF.Round(value.Value).ToString("0") : "—";

    private static void Left(PrimitiveBatch batch, TextRenderer text, string value,
        float left, float y, int size, byte r = PanelChrome.TextR, byte g = PanelChrome.TextG,
        byte b = PanelChrome.TextB, bool bold = false)
    {
        var (width, _) = text.Measure(value, size, bold);
        text.DrawText(batch, value, left + width * 0.5f, y, size, r, g, b, bold: bold);
    }
} // DashboardPanel
