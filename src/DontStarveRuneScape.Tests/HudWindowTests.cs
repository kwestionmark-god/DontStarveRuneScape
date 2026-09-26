namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Input;
using DontStarveRuneScape.UI;
using Xunit;

/// <summary>
/// HudWindow logic (no rendering): collapse toggle, title-bar drag with grab
/// offset and screen clamping, raise-to-top on click, and hidden-window gating.
/// </summary>
public class HudWindowTests
{
    private static (UiInput ui, bool held) Frame(InputState state)
    {
        state.MouseLeftClick = true;
        return (new UiInput(state), true);
    }

    private static (UiInput ui, bool held) Frame(InputState state, float mx, float my, bool held, bool click = false)
    {
        state.MouseX = mx;
        state.MouseY = my;
        state.MouseLeftClick = click;
        return (new UiInput(state), held);
    }

    [Fact]
    public void CollapseButton_ClickTogglesCollapsed()
    {
        var w = new VitalsHudWindow(18f, 18f);
        var state = new InputState();
        Assert.False(w.Collapsed);

        // Collapse button sits inside the title bar's right edge.
        var (ui, held) = Frame(state, 192f, 28f, true, click: true);
        w.Update(ui, held, 1280, 720);

        Assert.True(w.Collapsed);
        Assert.Equal(20f + 4f, w.PlateH); // title bar + collapsed pad only
    }

    [Fact]
    public void TitleDrag_FollowsMouseWithGrabOffset()
    {
        var w = new VitalsHudWindow(18f, 18f);
        var state = new InputState();

        // Click the title bar (clear of the collapse button) and hold.
        var (ui, held) = Frame(state, 100f, 30f, true, click: true);
        w.Update(ui, held, 1280, 720);

        // Still held: window follows the mouse, keeping the grab offset.
        var (ui2, held2) = Frame(state, 150f, 60f, true);
        w.Update(ui2, held2, 1280, 720);
        Assert.Equal(68f, w.X); // 150 - (100 - 18)
        Assert.Equal(48f, w.Y); // 60 - (30 - 18)

        // Release: drag ends, position stays.
        var (ui3, held3) = Frame(state, 200f, 90f, false);
        w.Update(ui3, held3, 1280, 720);
        Assert.Equal(68f, w.X);
        Assert.Equal(48f, w.Y);
    }

    [Fact]
    public void TitleDrag_ClampsToScreen()
    {
        var w = new VitalsHudWindow(18f, 18f);
        var state = new InputState();

        var (ui, held) = Frame(state, 100f, 30f, true, click: true);
        w.Update(ui, held, 1280, 720);

        var (ui2, held2) = Frame(state, 10000f, -50f, true);
        w.Update(ui2, held2, 1280, 720);

        Assert.Equal(1280f - w.PlateW, w.X);
        Assert.Equal(0f, w.Y);
    }

    [Fact]
    public void Manager_ClickRaisesWindowToTop()
    {
        var manager = new HudWindowManager();
        var a = new VitalsHudWindow(18f, 18f);
        var b = new ActionHudWindow(300f, 300f);
        manager.Add(a);
        manager.Add(b);
        Assert.Equal(new HudWindow[] { a, b }, manager.Windows);

        // Clicking the top window keeps it on top.
        var state = new InputState();
        var (ui, held) = Frame(state, 392f, 310f, true, click: true);
        manager.Update(ui, held, 1280, 720);
        Assert.Equal(new HudWindow[] { a, b }, manager.Windows);

        // Clicking the bottom window raises it above the other.
        var (ui2, held2) = Frame(state, 100f, 30f, true, click: true);
        manager.Update(ui2, held2, 1280, 720);
        Assert.Equal(new HudWindow[] { b, a }, manager.Windows);
    }

    [Fact]
    public void ActionWindow_ShowsWhileRunningHidesWhenIdle()
    {
        var hud = new HUD();
        var action = hud.Find<ActionHudWindow>();
        Assert.NotNull(action);
        Assert.False(action.Visible);

        hud.SetActionProgress(0.5f, "Woodcutting", 1280, 720);
        Assert.True(action.Visible);
        Assert.Equal("WOODCUTTING", action.Title);

        hud.SetActionProgress(0f, "", 1280, 720);
        Assert.False(action.Visible);
    }
}
