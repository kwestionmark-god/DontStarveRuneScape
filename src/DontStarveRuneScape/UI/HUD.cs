namespace DontStarveRuneScape.UI;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Actions;

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
}