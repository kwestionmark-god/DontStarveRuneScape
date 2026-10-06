namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;

/// <summary>
/// HUD — Heads-up display: OpenTTD-style draggable/collapsible windows (vitals
/// with the stamina bar that gates gathering), floating notifications, and the
/// damage flash. The user settings gate each element and scale the windows.
/// </summary>
public sealed class HUD
{
    private List<ActionNotification> _notifications = [];
    private readonly HudWindowManager _windows = new();
    private readonly VitalsHudWindow _vitals;
    private float _damageFlash;
    private DayNightCycle? _clock;

    /// <summary>User settings; null leaves every element visible at scale 1.</summary>
    public Settings? Settings { get; set; }

    public HUD()
    {
        _vitals = new VitalsHudWindow(18f, 18f);
        _windows.Add(_vitals);
    }

    public T? Find<T>() where T : HudWindow => _windows.Find<T>();

    public void SetNotifications(List<ActionNotification> notifications)
    {
        _notifications = notifications;
    }

    public void SetVitals(SurvivalSystem? survival, StaminaPool? stamina)
    {
        _vitals.SetData(survival, stamina);
    }

    /// <summary>Day/night clock shown top-right, with the sleep indicator.</summary>
    public void SetClock(DayNightCycle? clock) => _clock = clock;

    /// <summary>Retained-mode input for the windows (drag, collapse, raise).</summary>
    public void UpdateInput(InputState? state, int screenW, int screenH)
    {
        if (state == null) return;
        _windows.Update(new UiInput(state), state.MouseLeftDown, screenW, screenH);
    }

    public void Tick(float dt)
    {
        for (int i = _notifications.Count - 1; i >= 0; i--)
        {
            _notifications[i].Elapsed += dt;
            if (_notifications[i].IsExpired)
                _notifications.RemoveAt(i);
        }
        // Damage flash decays over ~1.25s regardless of framerate.
        _damageFlash = Math.Max(0f, _damageFlash - dt * 0.8f);
    }

    public void TriggerDamageFlash() => _damageFlash = 1f;

    public void Render(PrimitiveBatch batch, TextRenderer? text, int screenWidth, int screenHeight)
    {
        // Settings gate each element and scale the windows.
        float scale = Settings?.HudScale ?? 1f;
        _vitals.Scale = scale;
        _vitals.Visible = Settings?.ShowVitalsWindow ?? true;

        _windows.Render(batch, text);

        // Damage flash: red edge vignette, decaying over ~2s.
        if (_damageFlash > 0f && (Settings?.ShowDamageFlash ?? true))
        {
            byte a = (byte)(130 * Math.Clamp(_damageFlash, 0f, 1f));
            const float t = 40f;
            batch.DrawScreenQuad(screenWidth * 0.5f, t * 0.5f, screenWidth * 0.5f, t * 0.5f, 200, 30, 30, a);
            batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight - t * 0.5f, screenWidth * 0.5f, t * 0.5f, 200, 30, 30, a);
            batch.DrawScreenQuad(t * 0.5f, screenHeight * 0.5f, t * 0.5f, screenHeight * 0.5f, 200, 30, 30, a);
            batch.DrawScreenQuad(screenWidth - t * 0.5f, screenHeight * 0.5f, t * 0.5f, screenHeight * 0.5f, 200, 30, 30, a);
        }

        if (text == null) return;

        // Day/night clock, top-right; "Sleeping..." sits under it.
        if (_clock != null)
        {
            int hour = (int)_clock.HourOfDay;
            int minute = (int)((_clock.HourOfDay - hour) * 60f);
            int hour12 = hour % 12 == 0 ? 12 : hour % 12;
            string label = $"{(_clock.IsDay ? "Day" : "Night")} {hour12}:{minute:D2} {(hour < 12 ? "AM" : "PM")}";
            var (cw, _) = text.Measure(label, 13, false);
            float cx = screenWidth - 18f - cw * 0.5f;
            batch.DrawScreenQuad(cx, 22f, cw * 0.5f + 5f, 9f, 12, 10, 8, 170);
            text.DrawText(batch, label, cx, 22f, 13, 235, 225, 200);
            if (_clock.Sleeping)
            {
                const string sleeping = "Sleeping...";
                var (sw, _) = text.Measure(sleeping, 13, false);
                float sx = screenWidth - 18f - sw * 0.5f;
                batch.DrawScreenQuad(sx, 41f, sw * 0.5f + 5f, 9f, 12, 10, 8, 170);
                text.DrawText(batch, sleeping, sx, 41f, 13, 150, 200, 255);
            }
        }

        // Notifications under the vitals window, fading over their last second.
        if (Settings?.ShowNotifications == false) return;
        float ny = 112f;
        for (int i = 0; i < _notifications.Count && i < 6; i++)
        {
            var n = _notifications[i];
            float fade = Math.Clamp((n.Duration - n.Elapsed) / 1.0f, 0f, 1f);
            byte r = (byte)(n.Color.R * fade), g = (byte)(n.Color.G * fade), b = (byte)(n.Color.B * fade);
            var (tw, _) = text.Measure(n.Text, 13, false);
            float cx = 18f + 4f + tw * 0.5f;
            batch.DrawScreenQuad(cx, ny, tw * 0.5f + 5f, 9f, 12, 10, 8, 170);
            text.DrawText(batch, n.Text, cx, ny, 13, r, g, b);
            ny += 19f;
        }
    }
}
