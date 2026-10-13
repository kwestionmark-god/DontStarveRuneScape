namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Render;
using Xunit;

/// <summary>
/// Cave Stage 3 (memory: real darkness, light sources with radial
/// falloff — parked behind the development map; s16 built the fire-glow
/// machinery and s18's walls closed the edge): the cave's flat violet
/// overlay stays, but light now POOLS — the carried torch lights the
/// player's surroundings, fires pool warm light (s16), and gemstone
/// deposits glow cool blue as lures in the dark.
/// </summary>
public class CaveLightTests
{
    [Theory]
    // The torch pool: lit only while a torch is equipped.
    [InlineData(true, 2.5f)]
    [InlineData(false, 0f)]
    public void TorchGlowRadius_LitWhenEquipped(bool equipped, float expected)
    {
        Assert.Equal(expected, SpriteRenderer.TorchGlowRadiusTiles(equipped));
    }

    [Theory]
    // Pool alpha follows the ambient darkness: day does nothing, full cave
    // dark strong. Torch reads brighter than the fire pools (it moves).
    [InlineData(0f, 0)]
    [InlineData(1f, 191)] // 0.75 * 255
    [InlineData(0.5f, 95)]
    public void TorchGlowAlpha_FollowsDarkness(float darkness, int expected)
    {
        Assert.Equal(expected, SpriteRenderer.TorchGlowAlpha(darkness));
    }

    [Theory]
    // Gemstone crystals glow faintly cool: half the torch's strength.
    [InlineData(0f, 0)]
    [InlineData(1f, 102)] // 0.40 * 255
    public void CrystalGlowAlpha_FollowsDarkness(float darkness, int expected)
    {
        Assert.Equal(expected, SpriteRenderer.CrystalGlowAlpha(darkness));
    }
}
