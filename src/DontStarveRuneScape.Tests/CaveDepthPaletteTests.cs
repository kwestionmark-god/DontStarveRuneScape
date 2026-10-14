namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// Cave Stage 3 flavor — depth palette: the cave's darkness overlay is no
/// longer a flat violet wash. As the player pushes deeper (x-distance from
/// the west-edge exit, the basin's depth axis) the overlay darkens and
/// cools — near the exit a faint violet gloom, at the far wall a deep
/// blue-black. Pure math pinned here; Game.RenderGame feeds it the
/// player's tile x.
/// </summary>
public class CaveDepthPaletteTests
{
    [Theory]
    // Depth is the normalized distance from the cave exit (west edge)
    // toward the far wall. At the exit: 0; at the far wall: 1.
    [InlineData(6f, 6, 64, 0f)]
    [InlineData(63f, 6, 64, 1f)]
    [InlineData(34.5f, 6, 64, 0.5f)]
    public void CaveDepth01_NormalizesExitToFarWall(float tileX, int exitX, int width, float expected)
    {
        Assert.Equal(expected, SpriteRenderer.CaveDepth01(tileX, exitX, width), 3);
    }

    [Theory]
    // Out-of-range positions clamp: never darker than the far wall, never
    // lighter than the exit mouth (and walking past the exit tile — the
    // player spawns AT x=6 — must not go negative).
    [InlineData(0f, 6, 64, 0f)]
    [InlineData(100f, 6, 64, 1f)]
    public void CaveDepth01_ClampsOutsideTheBasin(float tileX, int exitX, int width, float expected)
    {
        Assert.Equal(expected, SpriteRenderer.CaveDepth01(tileX, exitX, width), 3);
    }

    [Theory]
    // Overlay alpha deepens with depth: a gloomy 60 at the mouth, an
    // oppressive 120 at the far wall (still under the surface night's
    // NightOverlayMaxAlpha so cave dark never out-darks a real midnight).
    [InlineData(0f, 60)]
    [InlineData(1f, 120)]
    [InlineData(0.5f, 90)]
    public void CaveOverlayAlpha_DeepensWithDepth(float depth01, int expected)
    {
        Assert.Equal(expected, SpriteRenderer.CaveOverlayAlpha(depth01));
    }

    [Theory]
    // The tint cools as it darkens: today's violet (13,12,21) at the
    // mouth, a deep blue-black (6,8,26) at depth — blue rises while red
    // and green fall.
    [InlineData(0f, 13, 12, 21)]
    [InlineData(1f, 6, 8, 26)]
    public void CaveOverlayColor_CoolsWithDepth(float depth01, int r, int g, int b)
    {
        var (R, G, B) = SpriteRenderer.CaveOverlayColor(depth01);
        Assert.Equal(r, R);
        Assert.Equal(g, G);
        Assert.Equal(b, B);
    }

    [Fact]
    public void CaveOverlayColor_BlueRisesRedFalls_Monotonically()
    {
        var shallow = SpriteRenderer.CaveOverlayColor(0.25f);
        var deep = SpriteRenderer.CaveOverlayColor(0.75f);
        Assert.True(deep.B > shallow.B, "blue must rise with depth");
        Assert.True(deep.R < shallow.R, "red must fall with depth");
        Assert.True(deep.G < shallow.G, "green must fall with depth");
    }
}
