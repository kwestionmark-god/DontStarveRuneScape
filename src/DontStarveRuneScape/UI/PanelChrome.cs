namespace DontStarveRuneScape.UI;

using DontStarveRuneScape.Render;

/// <summary>
/// PanelChrome — Shared frame for all UI panels: dims the live world behind a
/// centered dark plate with a gold border and a gold title, in the title screen's
/// palette. Callers receive the inner content rect and fill only that.
/// </summary>
public static class PanelChrome
{
    public const byte PlateR = 40, PlateG = 24, PlateB = 12;
    public const byte BorderR = 222, BorderG = 192, BorderB = 132;
    public const byte TextR = 230, TextG = 220, TextB = 200;

    /// Overhang of plate and border around the content rect.
    private const float Pad = 16f;
    private const float TitleH = 34f;
    private const float Border = 3f;

    /// <summary>Draw dim + plate + border + title centered on screen; outputs the
    /// content rect below the title.</summary>
    public static void Draw(PrimitiveBatch batch, TextRenderer? text,
        int screenW, int screenH, string title, float contentW, float contentH,
        out float contentX, out float contentY, out float outW, out float outH)
    {
        // Dim the world (drawn under the plate).
        batch.DrawScreenQuad(screenW * 0.5f, screenH * 0.5f, screenW * 0.5f, screenH * 0.5f,
            0, 0, 0, 150);

        float plateW = contentW + Pad * 2f;
        float plateH = contentH + Pad * 2f + TitleH;
        float cx = screenW * 0.5f;
        float cy = screenH * 0.5f;

        // Border ring, then plate interior.
        batch.DrawScreenQuad(cx, cy, plateW * 0.5f + Border, plateH * 0.5f + Border,
            BorderR, BorderG, BorderB);
        batch.DrawScreenQuad(cx, cy, plateW * 0.5f, plateH * 0.5f, PlateR, PlateG, PlateB);

        float plateTop = cy - plateH * 0.5f;
        text?.DrawText(batch, title, cx, plateTop + TitleH * 0.5f, 22,
            BorderR, BorderG, BorderB, bold: true);
        // Hairline under the title.
        batch.DrawScreenQuad(cx, plateTop + TitleH, plateW * 0.5f - Pad * 0.5f, 0.5f,
            BorderR, BorderG, BorderB, 120);

        contentX = cx - plateW * 0.5f + Pad;
        contentY = plateTop + TitleH + Pad;
        outW = contentW;
        outH = contentH;
    }

    /// <summary>Draw a minimal plate for a panel that has no content yet, so no
    /// panel state is invisible: shared chrome, the panel title, and a note.</summary>
    public static void DrawPlaceholder(PrimitiveBatch batch, TextRenderer? text,
        int screenW, int screenH, string title)
    {
        Draw(batch, text, screenW, screenH, title, 360f, 80f,
            out float contentX, out float contentY, out float contentW, out float contentH);
        if (text == null) return;
        text.DrawText(batch, "Not yet implemented (Esc to close)",
            contentX + contentW * 0.5f, contentY + contentH * 0.5f, 15, 160, 150, 130);
    }
}
