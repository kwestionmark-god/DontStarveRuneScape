namespace DontStarveRuneScape.UI;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Survival;

/// <summary>
/// HUD — Heads-up display showing health, hunger, stamina, notifications, action progress.
/// </summary>
public sealed class HUD
{
    private List<ActionNotification> _notifications = [];
    private float _actionProgress = 0f;
    private string _actionSkillName = "";

    public void SetNotifications(List<ActionNotification> notifications)
    {
        _notifications = notifications;
    }

    public void SetActionProgress(float progress, string skillName)
    {
        _actionProgress = progress;
        _actionSkillName = skillName;
    }

    public void TickNotifications(float dt)
    {
        for (int i = _notifications.Count - 1; i >= 0; i--)
        {
            _notifications[i].Elapsed += dt;
            if (_notifications[i].IsExpired)
                _notifications.RemoveAt(i);
        }
    }

    public void TriggerDamageFlash() { }

    public void Render(GL gl, int screenWidth, int screenHeight) { }

    public void Render(PrimitiveBatch batch, int screenWidth, int screenHeight, SurvivalSystem? survival)
    {
        float x = 18f;
        float y = 18f;
        float w = 180f;
        float h = 10f;

        float hp = survival == null || survival.MaxHp <= 0 ? 1f : Math.Clamp(survival.Hp / survival.MaxHp, 0f, 1f);
        float hunger = survival == null ? 1f : Math.Clamp(survival.GetHungerPercent(), 0f, 1f);
        float action = Math.Clamp(_actionProgress, 0f, 1f);

        DrawBar(batch, x, y, w, h, hp, 180, 50, 50);
        DrawBar(batch, x, y + 16f, w, h, hunger, 200, 140, 40);
        if (action > 0f)
            DrawBar(batch, x, y + 32f, w, h, action, 90, 170, 220);
    }

    private static void DrawBar(PrimitiveBatch batch, float x, float y, float w, float h, float fill, byte r, byte g, byte b)
    {
        float cx = x + w * 0.5f;
        float cy = y + h * 0.5f;
        batch.DrawScreenQuad(cx, cy, w * 0.5f + 2f, h * 0.5f + 2f, 12, 10, 8);
        batch.DrawScreenQuad(cx, cy, w * 0.5f, h * 0.5f, 30, 24, 18);
        float filled = Math.Max(2f, w * fill);
        batch.DrawScreenQuad(x + filled * 0.5f, cy, filled * 0.5f, h * 0.5f - 1f, r, g, b);
    }
}