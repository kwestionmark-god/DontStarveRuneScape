namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// Vertical building, slice 16 (spec:
/// docs/superpowers/specs/2026-10-13-firelight-glow-design.md):
/// fires pool warm light onto the ground tiles around them — radius
/// grows with the burn stage and the fire's WarmthRadius, alpha with
/// the ambient darkness. Pure helpers on SpriteRenderer, test-pinned.
/// </summary>
public class FirelightGlowTests
{
    [Theory]
    // Footprint = warmthRadius * 32 world-px * zoom; stage scales it.
    [InlineData(1, 3, 1f, 0.5f * 3 * 32f)]
    [InlineData(2, 3, 1f, 0.8f * 3 * 32f)]
    [InlineData(3, 3, 1f, 1.1f * 3 * 32f)]
    [InlineData(3, 6, 2f, 1.1f * 6 * 32f * 2f)] // radius + zoom scale
    public void FireGlowRadiusPx_ScalesWithStageWarmthAndZoom(
        int stage, int warmthRadius, float zoom, float expected)
    {
        Assert.Equal(expected, SpriteRenderer.FireGlowRadiusPx(stage, warmthRadius, zoom), 3);
    }

    [Fact]
    public void FireGlowRadiusPx_StageZero_IsZero()
    {
        Assert.Equal(0f, SpriteRenderer.FireGlowRadiusPx(0, 3, 1f));
    }

    [Theory]
    // Daylight (darkness ~ 0) = invisible.
    [InlineData(3, 0f, 0)]
    [InlineData(1, 0.01f, 0)]
    // Midnight, per stage: 0.35 / 0.6 / 0.8 of full 255.
    [InlineData(1, 1f, 89)]
    [InlineData(2, 1f, 153)]
    [InlineData(3, 1f, 204)]
    // Half dark scales linearly.
    [InlineData(3, 0.5f, 102)]
    public void FireGlowAlpha_StageAndDarkness(int stage, float darkness, int expected)
    {
        Assert.Equal(expected, SpriteRenderer.FireGlowAlpha(stage, darkness));
    }
}
