namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.SDL;

/// <summary>
/// TitleScreen — Main menu title screen.
/// </summary>
public sealed class TitleScreen
{
    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }

    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }

    public void Render(PrimitiveBatch batch, int screenWidth, int screenHeight, float time)
    {
        // Don't-Starve-ish dusk background
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.5f, screenWidth * 0.5f, screenHeight * 0.5f, 28, 18, 12);

        // Horizon band
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.62f, screenWidth * 0.5f, 80f, 48, 32, 22);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.78f, screenWidth * 0.5f, 140f, 36, 54, 28);

        // Fake title plate (gold)
        float pulse = 0.85f + 0.15f * MathF.Sin(time * 2.2f);
        byte gold = (byte)Math.Clamp((int)(200 * pulse), 120, 255);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.32f, 280f, 42f, gold, 168, 48);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.32f, 268f, 30f, 42, 24, 12);

        // Accent bars standing in for the wordmark
        batch.DrawScreenQuad(screenWidth * 0.5f - 90f, screenHeight * 0.32f, 70f, 10f, gold, 180, 70);
        batch.DrawScreenQuad(screenWidth * 0.5f + 90f, screenHeight * 0.32f, 70f, 10f, gold, 180, 70);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.32f, 40f, 16f, 220, 200, 120);

        // Start prompt (pulsing bar)
        byte prompt = (byte)Math.Clamp((int)(180 + 70 * MathF.Sin(time * 3.5f)), 80, 255);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.58f, 140f, 18f, prompt, 90, 40);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.58f, 128f, 10f, 20, 12, 8);
    }
}

/// <summary>
/// CharacterDefinition — Character creation data.
/// </summary>
public sealed class CharacterDefinition
{
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; } = 0;
    public int AppearanceId { get; set; } = 0;
}

/// <summary>
/// CharacterSelectPanel — Character creation/selection panel.
/// </summary>
public sealed class CharacterSelectPanel
{
    public bool Visible { get; set; } = false;
    private Action<CharacterDefinition?>? _confirmCallback;

    public void SetConfirmCallback(Action<CharacterDefinition?> callback)
    {
        _confirmCallback = callback;
    }

    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(GL gl, int screenWidth, int screenHeight) { }

    public void Render(PrimitiveBatch batch, int screenWidth, int screenHeight)
    {
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.5f, screenWidth * 0.5f, screenHeight * 0.5f, 18, 16, 22);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.45f, 220f, 140f, 50, 42, 60);
        batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.72f, 120f, 16f, 180, 140, 60);
    }
}

/// <summary>
/// LoadingScreen — World generation / save loading screen.
/// </summary>
public sealed class LoadingScreen
{
    public void HandleInput(Game game, InputState inputState) { }
    public void HandleEvent(Game game, Event evt) { }
    public void Render(Game game, GL gl, int screenWidth, int screenHeight) { }

    public void Render(PrimitiveBatch batch, Game game, int screenWidth, int screenHeight)
    {
        bool error = game.State == GameState.Error;
        if (error)
            batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.5f, screenWidth * 0.5f, screenHeight * 0.5f, 40, 12, 12);
        else
            batch.DrawScreenQuad(screenWidth * 0.5f, screenHeight * 0.5f, screenWidth * 0.5f, screenHeight * 0.5f, 16, 14, 12);

        float progress = Math.Clamp(game.LoadingProgress, 0f, 1f);
        float barWidth = 360f;
        float barHeight = 14f;
        float cx = screenWidth * 0.5f;
        float cy = screenHeight * 0.55f;

        batch.DrawScreenQuad(cx, cy, barWidth * 0.5f + 6f, barHeight * 0.5f + 6f, 60, 40, 20);
        batch.DrawScreenQuad(cx, cy, barWidth * 0.5f, barHeight * 0.5f, 24, 16, 10);

        float filled = Math.Max(4f, barWidth * progress);
        float fillCenter = cx - barWidth * 0.5f + filled * 0.5f;
        if (error)
            batch.DrawScreenQuad(fillCenter, cy, filled * 0.5f, barHeight * 0.5f - 2f, 200, 50, 40);
        else
            batch.DrawScreenQuad(fillCenter, cy, filled * 0.5f, barHeight * 0.5f - 2f, 200, 160, 50);

        // Title plate
        batch.DrawScreenQuad(cx, screenHeight * 0.32f, 200f, 22f, 180, 140, 50);
        batch.DrawScreenQuad(cx, screenHeight * 0.32f, 188f, 12f, 30, 20, 10);
    }
}
