namespace DontStarveRuneScape.Render;

using System.Collections.Generic;
using Silk.NET.OpenGL;
using DontStarveRuneScape.Combat;

/// <summary>
/// CombatUI — Renders combat-related UI: floating damage numbers, rising from
/// the hit position. Negative values (damage to the player) render red,
/// positive values (hits on monsters) render gold.
/// </summary>
public static class CombatUI
{
    public static void RenderDamageNumbers(GL gl, List<DamageNumber> damageNumbers,
        Camera.Camera camera, PrimitiveBatch batch, TextRenderer? text)
    {
        if (text == null) return;

        foreach (var dn in damageNumbers)
        {
            var screen = camera.WorldToScreen(dn.WorldX, dn.WorldY, 0f);
            bool toPlayer = dn.Value < 0;
            byte r = toPlayer ? (byte)230 : (byte)255;
            byte g = toPlayer ? (byte)60 : (byte)230;
            byte b = toPlayer ? (byte)60 : (byte)140;
            text.DrawText(batch, ((int)MathF.Abs(dn.Value)).ToString(),
                screen.X, screen.Y - dn.Height - 10f, 15, r, g, b, bold: true);
        }
    }
}
