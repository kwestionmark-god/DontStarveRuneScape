namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Render;

/// <summary>
/// HudWindow — OpenTTD-style HUD widget window: draggable by its title bar,
/// collapsible to just the title bar, with the panels' gold chrome. The window
/// owns a small content rect the subclass fills; everything else is chrome.
/// </summary>
public abstract class HudWindow
{
    public string Title { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public bool Collapsed { get; set; }
    public bool Visible { get; set; } = true;

    protected readonly float ContentW;
    protected readonly float ContentH;

    private const float Pad = 8f;
    private const float TitleH = 20f;
    private const float Border = 2f;
    private const float CollapseW = 14f;

    private bool _dragging;
    private float _dragDx, _dragDy;

    /// <summary>Invoked when the window is clicked (raise to top of the stack).</summary>
    public Action? BringToFront { get; set; }

    protected HudWindow(string title, float x, float y, float contentW, float contentH)
    {
        Title = title;
        X = x;
        Y = y;
        ContentW = contentW;
        ContentH = contentH;
    }

    /// <summary>Plate width.</summary>
    public float PlateW => ContentW + Pad * 2f;
    /// <summary>Plate height (collapsed = title bar only).</summary>
    public float PlateH => TitleH + (Collapsed ? Pad * 0.5f : ContentH + Pad * 2f);

    /// <summary>Retained-mode input: collapse toggle, title drag, clamp to screen.</summary>
    public void Update(UiInput ui, bool mouseHeld, int screenW, int screenH)
    {
        if (!Visible) return;

        // Collapse button sits inside the title bar; query it before the title
        // rect so the drag start cannot consume its click.
        float btnX = X + PlateW - CollapseW - 3f;
        if (ui.TryClick(btnX, Y + 3f, CollapseW, TitleH - 6f))
        {
            Collapsed = !Collapsed;
            return;
        }

        if (ui.TryClick(X, Y, PlateW, TitleH))
        {
            _dragging = true;
            _dragDx = ui.MouseX - X;
            _dragDy = ui.MouseY - Y;
            BringToFront?.Invoke();
        }

        if (!_dragging) return;
        if (mouseHeld)
        {
            X = Math.Clamp(ui.MouseX - _dragDx, 0f, Math.Max(0f, screenW - PlateW));
            Y = Math.Clamp(ui.MouseY - _dragDy, 0f, Math.Max(0f, screenH - PlateH));
        }
        else
        {
            _dragging = false;
        }
    }

    /// <summary>Draw plate + border + title + collapse arrow; content when expanded.</summary>
    public void Render(PrimitiveBatch batch, TextRenderer? text)
    {
        if (!Visible) return;

        float cx = X + PlateW * 0.5f;
        float cy = Y + PlateH * 0.5f;

        batch.DrawScreenQuad(cx, cy, PlateW * 0.5f + Border, PlateH * 0.5f + Border,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB);
        batch.DrawScreenQuad(cx, cy, PlateW * 0.5f, PlateH * 0.5f,
            PanelChrome.PlateR, PanelChrome.PlateG, PanelChrome.PlateB);

        text?.DrawText(batch, Title, cx - CollapseW * 0.5f, Y + TitleH * 0.5f, 13,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, bold: true);
        batch.DrawScreenQuad(cx, Y + TitleH, PlateW * 0.5f - Border, 0.5f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB, 120);

        // Collapse indicator: − when expanded, + when collapsed.
        float btnX = X + PlateW - CollapseW - 3f;
        float btnCx = btnX + CollapseW * 0.5f;
        float btnCy = Y + TitleH * 0.5f;
        batch.DrawScreenQuad(btnCx, btnCy, CollapseW * 0.5f, (TitleH - 6f) * 0.5f, 60, 45, 35);
        batch.DrawScreenQuad(btnCx, btnCy, CollapseW * 0.25f, 0.75f,
            PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB);
        if (Collapsed)
            batch.DrawScreenQuad(btnCx, btnCy, 0.75f, CollapseW * 0.25f,
                PanelChrome.BorderR, PanelChrome.BorderG, PanelChrome.BorderB);

        if (!Collapsed)
            RenderContent(batch, text, X + Pad, Y + TitleH + Pad);
    }

    /// <summary>Fill the content rect (top-left anchored, expanded only).</summary>
    protected abstract void RenderContent(PrimitiveBatch batch, TextRenderer? text,
        float contentX, float contentY);
}

/// <summary>
/// HudWindowManager — Stack of HudWindows: input runs top-down (topmost first),
/// rendering back-to-front; a click raises the clicked window to the top.
/// </summary>
public sealed class HudWindowManager
{
    private readonly List<HudWindow> _windows = [];

    public void Add(HudWindow window)
    {
        window.BringToFront = () =>
        {
            _windows.Remove(window);
            _windows.Add(window);
        };
        _windows.Add(window);
    }

    public T? Find<T>() where T : HudWindow => _windows.OfType<T>().FirstOrDefault();

    /// <summary>Windows back-to-front (index 0 renders first, underneath).</summary>
    public IReadOnlyList<HudWindow> Windows => _windows;

    public void Update(UiInput ui, bool mouseHeld, int screenW, int screenH)
    {
        for (int i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].Visible)
                _windows[i].Update(ui, mouseHeld, screenW, screenH);
        }
    }

    public void Render(PrimitiveBatch batch, TextRenderer? text)
    {
        for (int i = 0; i < _windows.Count; i++)
        {
            if (_windows[i].Visible)
                _windows[i].Render(batch, text);
        }
    }
}

/// <summary>
/// VitalsHudWindow — HP / hunger / stamina bars with labels, numeric values,
/// and quarter ticks, in the panels' chrome.
/// </summary>
public sealed class VitalsHudWindow : HudWindow
{
    private SurvivalSystem? _survival;
    private StaminaPool? _stamina;

    private const float BarH = 9f;
    private const float RowH = 16f;

    public VitalsHudWindow(float x, float y) : base("VITALS", x, y, 168f, RowH * 3f + 2f) { }

    public void SetData(SurvivalSystem? survival, StaminaPool? stamina)
    {
        _survival = survival;
        _stamina = stamina;
    }

    protected override void RenderContent(PrimitiveBatch batch, TextRenderer? text,
        float contentX, float contentY)
    {
        float hp = _survival == null || _survival.MaxHp <= 0
            ? 1f : Math.Clamp(_survival.Hp / _survival.MaxHp, 0f, 1f);
        float hunger = _survival == null ? 1f : Math.Clamp(_survival.GetHungerPercent(), 0f, 1f);
        float stamina = _stamina == null || _stamina.MaxStamina <= 0
            ? 1f : Math.Clamp(_stamina.Current / _stamina.MaxStamina, 0f, 1f);

        DrawRow(batch, text, contentX, contentY, "HP", hp, _survival?.Hp ?? 0f, _survival?.MaxHp ?? 0f, 180, 50, 50);
        DrawRow(batch, text, contentX, contentY + RowH, "Hunger", hunger, _survival?.Hunger ?? 0f, _survival?.MaxHunger ?? 0f, 200, 140, 40);
        DrawRow(batch, text, contentX, contentY + RowH * 2f, "Stamina", stamina, _stamina?.Current ?? 0f, _stamina?.MaxStamina ?? 0f, 100, 190, 90);
    }

    private void DrawRow(PrimitiveBatch batch, TextRenderer? text, float x, float y,
        string label, float fill, float value, float max, byte r, byte g, byte b)
    {
        // Label left, bar in the middle, numeric value right.
        float barX = x + 56f;
        float barW = ContentW - 56f - 40f;
        float barCy = y + RowH * 0.5f;

        batch.DrawScreenQuad(barX + barW * 0.5f, barCy, barW * 0.5f + 1f, BarH * 0.5f + 1f, 12, 10, 8);
        batch.DrawScreenQuad(barX + barW * 0.5f, barCy, barW * 0.5f, BarH * 0.5f, 30, 24, 18);
        // Quarter ticks.
        for (int i = 1; i < 4; i++)
            batch.DrawScreenQuad(barX + barW * i / 4f, barCy, 0.5f, BarH * 0.5f, 12, 10, 8);
        float filled = Math.Max(1.5f, barW * fill);
        batch.DrawScreenQuad(barX + filled * 0.5f, barCy, filled * 0.5f, BarH * 0.5f - 1f, r, g, b);

        if (text == null) return;
        text.DrawText(batch, label, x + 4f, barCy, 11, PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
        string valueText = max > 0f ? $"{(int)MathF.Ceiling(value)}/{(int)MathF.Ceiling(max)}" : $"{(int)MathF.Ceiling(value)}";
        var (vw, _) = text.Measure(valueText, 10);
        text.DrawText(batch, valueText, x + 4f + 56f + barW + (40f - vw) * 0.5f, barCy, 10,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
    }
}

/// <summary>
/// ActionHudWindow — Progress of the running gather action; the game shows it
/// while an action runs and hides it when idle.
/// </summary>
public sealed class ActionHudWindow : HudWindow
{
    private float _progress;

    private const float BarH = 10f;

    public ActionHudWindow(float x, float y) : base("ACTION", x, y, 168f, BarH + 6f) { }

    public void SetProgress(float progress, string skillName)
    {
        _progress = Math.Clamp(progress, 0f, 1f);
        if (!string.IsNullOrEmpty(skillName))
            Title = skillName.ToUpperInvariant();
    }

    protected override void RenderContent(PrimitiveBatch batch, TextRenderer? text,
        float contentX, float contentY)
    {
        float barCy = contentY + BarH * 0.5f;
        batch.DrawScreenQuad(contentX + ContentW * 0.5f, barCy, ContentW * 0.5f + 1f, BarH * 0.5f + 1f, 12, 10, 8);
        batch.DrawScreenQuad(contentX + ContentW * 0.5f, barCy, ContentW * 0.5f, BarH * 0.5f, 30, 24, 18);
        float filled = Math.Max(1.5f, ContentW * _progress);
        batch.DrawScreenQuad(contentX + filled * 0.5f, barCy, filled * 0.5f, BarH * 0.5f - 1f, 90, 170, 220);

        if (text == null) return;
        string pct = $"{(int)MathF.Round(_progress * 100f)}%";
        var (pw, _) = text.Measure(pct, 10);
        text.DrawText(batch, pct, contentX + ContentW - pw * 0.5f - 2f, barCy, 10,
            PanelChrome.TextR, PanelChrome.TextG, PanelChrome.TextB);
    }
}
