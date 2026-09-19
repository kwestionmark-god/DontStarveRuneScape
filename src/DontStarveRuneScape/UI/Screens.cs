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

    /// <summary>
    /// Title screen: dusk gradient, ambient embers, title plate with wordmark,
    /// subtitle, pulsing start prompt, and a flavor-text footer.
    /// </summary>
    public void Render(PrimitiveBatch batch, TextRenderer text, int screenWidth, int screenHeight, float time)
    {
        DrawGradientBackground(batch, screenWidth, screenHeight);
        DrawEmbers(batch, screenWidth, screenHeight, time);

        // Decorative corner brackets that pulse gently.
        DrawCornerBrackets(batch, screenWidth, screenHeight, time);

        float bob = MathF.Sin(time * 1.5f) * 4f;
        float titleY = screenHeight * 0.29f + bob;

        // Title plate (behind the wordmark).
        float platePulse = 0.85f + 0.15f * MathF.Sin(time * 2.2f);
        int plateGold = Math.Clamp((int)(205 * platePulse), 140, 255);
        batch.DrawScreenQuad(screenWidth * 0.5f, titleY, 340f, 54f, 40, 24, 12);
        batch.DrawScreenQuad(screenWidth * 0.5f, titleY, 330f, 46f, (byte)plateGold, 168, 50);

        // Wordmark: dark shadow, then gold glyph, then a light highlight on top.
        string title = "Don't Starve RuneScape";
        const int titleSize = 50;
        if (text != null)
        {
            text.DrawText(batch, title, screenWidth * 0.5f + 3f, titleY + 3f, titleSize, 24, 15, 8);
            text.DrawText(batch, title, screenWidth * 0.5f, titleY, titleSize, 222, 192, 132, bold: true);
            text.DrawText(batch, title, screenWidth * 0.5f, titleY - 2f, titleSize, 255, 234, 186,
                a: 150, bold: true);
        }

        // Subtitle / tagline.
        if (text != null)
        {
            float subAlpha = Math.Clamp(140 + (int)(60 * MathF.Sin(time * 2f)), 120, 255);
            text.DrawText(batch, "A survival adventure in a procedurally generated world",
                screenWidth * 0.5f, titleY + 74f, 18, 170, 150, 110, (byte)subAlpha, italic: true);
        }

        // Menu button: NEW GAME.
        DrawMenuButton(batch, text, screenWidth, time, 430, "NEW GAME",
            (70, 110, 160), (240, 240, 255), 22, true);

        // Start prompt.
        float promptPulse = 0.5f + 0.5f * MathF.Sin(time * 3f);
        if (text != null)
            text.DrawText(batch, "Press ENTER or SPACE to start",
                screenWidth * 0.5f, screenHeight * 0.52f, 20,
                (byte)(150 + (int)(50 * promptPulse)), (byte)(175 + (int)(40 * promptPulse)), 215, 255);

        // Quit hint.
        if (text != null)
            text.DrawText(batch, "Press ESC to quit",
                screenWidth * 0.5f, screenHeight * 0.57f, 14, 120, 110, 100);

        // Footer: version + flavor text.
        if (text != null)
        {
            text.DrawText(batch, "v0.1.0  |  Don't Starve RuneScape Fan Project",
                screenWidth * 0.5f, screenHeight - 40f, 12, 95, 88, 78);

            string flavor = FlavorFor(time);
            text.DrawText(batch, flavor, screenWidth * 0.5f, screenHeight - 62f, 13, 110, 100, 85, italic: true);
        }
    }

    private static void DrawGradientBackground(PrimitiveBatch batch, int w, int h)
    {
        // Dark dusk gradient: warm-brown sky fading to a green forest floor.
        const int step = 4;
        for (int y = 0; y < h; y += step)
        {
            float ratio = (float)y / h;
            // Sky (warm brown) to mid (brown) to ground (dark green).
            int r = Math.Clamp((int)(26 + ratio * 30), 0, 255);
            int g = Math.Clamp((int)(18 + ratio * 40), 0, 255);
            int b = Math.Clamp((int)(12 + ratio * 22), 0, 255);
            batch.DrawScreenQuad(w * 0.5f, y + step * 0.5f, w * 0.5f, step + 1f, (byte)r, (byte)g, (byte)b);
        }
    }

    private static void DrawEmbers(PrimitiveBatch batch, int w, int h, float time)
    {
        // Deterministic rising embers (no per-frame state needed).
        const int count = 34;
        for (int i = 0; i < count; i++)
        {
            float speed = 8f + (i % 5) * 3f;
            float phase = i * 47.3f;
            float cycle = (time * speed + phase) % (float)(h + 60);
            float x = (i * 97.3f) % (float)w;
            float y = (float)h - cycle;

            // Fade in near the ground, fade out near the top.
            float life = cycle / (float)(h + 60);
            float alpha = Math.Clamp(120 * MathF.Sin(life * MathF.PI), 0, 140);
            if (alpha <= 0f) continue;

            int baseR = 200 + (i % 3) * 20;
            int baseG = 150 + (i % 4) * 12;
            int baseB = 70 + (i % 2) * 25;
            float size = 2f + (i % 3) * 1.2f;
            batch.DrawScreenQuad(x, y, size, size, (byte)baseR, (byte)baseG, (byte)baseB, (byte)alpha);
        }
    }

    private static void DrawCornerBrackets(PrimitiveBatch batch, int w, int h, float time)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(time * 0.8f);
        byte a = (byte)Math.Clamp(50 + (int)(40 * pulse), 0, 255);
        byte c = 180, g = 140, b = 80;
        const int len = 40, thick = 4, pad = 30;

        // Corners drawn as thin quads (L-brackets).
        // Top-left horizontal + vertical
        batch.DrawScreenQuad(pad + len * 0.5f, pad, len, thick, c, g, b, a);
        batch.DrawScreenQuad(pad, pad + len * 0.5f, thick, len, c, g, b, a);
        // Top-right
        batch.DrawScreenQuad(w - pad - len * 0.5f, pad, len, thick, c, g, b, a);
        batch.DrawScreenQuad(w - pad, pad + len * 0.5f, thick, len, c, g, b, a);
        // Bottom-left
        batch.DrawScreenQuad(pad + len * 0.5f, h - pad, len, thick, c, g, b, a);
        batch.DrawScreenQuad(pad, h - pad - len * 0.5f, thick, len, c, g, b, a);
        // Bottom-right
        batch.DrawScreenQuad(w - pad - len * 0.5f, h - pad, len, thick, c, g, b, a);
        batch.DrawScreenQuad(w - pad, h - pad - len * 0.5f, thick, len, c, g, b, a);
    }

    private static void DrawMenuButton(PrimitiveBatch batch, TextRenderer? text, int w, float time,
        int y, string label, (int r, int g, int b) baseColor, (int r, int g, int b) textColor,
        int size, bool bold)
    {
        float pulse = 0.5f + 0.5f * MathF.Sin(time * 3f);
        int r = Math.Clamp(baseColor.r + (int)(30 * pulse), 0, 255);
        int gg = Math.Clamp(baseColor.g + (int)(30 * pulse), 0, 255);
        int b = Math.Clamp(baseColor.b + (int)(30 * pulse), 0, 255);

        int textW = 160, textH = 26;
        batch.DrawScreenQuad(w * 0.5f, y, textW * 0.5f + 28, textH * 0.5f + 12, (byte)r, (byte)gg, (byte)b);
        batch.DrawScreenQuad(w * 0.5f, y, textW * 0.5f + 22, textH * 0.5f + 8, (byte)Math.Min(255, r + 40),
            (byte)Math.Min(255, gg + 55), (byte)Math.Min(255, b + 70));

        if (label != null && text != null)
            text.DrawText(batch, label, w * 0.5f, y, size, (byte)textColor.r, (byte)textColor.g, (byte)textColor.b, bold: bold);
    }

    private static string FlavorFor(float time)
    {
        string[] flavors =
        {
            "\"They did not die; they were buried while alive.\"",
            "\"The moon watches. It has always watched.\"",
            "\"Do not trust the trees after dark.\"",
            "\"Hunt. Craft. Survive. Repeat.\"",
            "\"The web remembers every visitor.\"",
        };
        int index = (int)(time / 6f) % flavors.Length;
        if (index < 0) index += flavors.Length;
        return flavors[index];
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
