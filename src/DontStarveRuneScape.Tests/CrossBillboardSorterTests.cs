using DontStarveRuneScape.Render;
using Xunit;

namespace DontStarveRuneScape.Tests;

public class CrossBillboardSorterTests
{
    private const float Pi = MathF.PI;

    [Fact]
    public void ViewDepth_YawZero_IsWorldY()
    {
        Assert.Equal(7f, CrossBillboardSorter.ViewDepth(3f, 7f, 0f), 4);
    }

    [Fact]
    public void ViewDepth_Yaw90_IsWorldX()
    {
        Assert.Equal(3f, CrossBillboardSorter.ViewDepth(3f, 7f, Pi / 2f), 4);
    }

    [Fact]
    public void HalfDepth_RespectsSpanMidpoint()
    {
        // Plane axis +x, view looking along +x (yaw = 90°): the half nearer
        // the camera (span 0..1, mid +0.5) has the larger depth.
        float near = CrossBillboardSorter.HalfDepth(0f, 0f, 1f, 0f, 10f, 0f, 1f, Pi / 2f);
        float far = CrossBillboardSorter.HalfDepth(0f, 0f, 1f, 0f, 10f, -1f, 0f, Pi / 2f);
        Assert.Equal(5f, near, 4);
        Assert.Equal(-5f, far, 4);
        Assert.True(near > far);
    }

    [Fact]
    public void HalfDepth_MirroredSpan_KeepsDepth()
    {
        // Mirroring swaps the span endpoints (U 0..0.5 ↔ 0.5..1); the
        // midpoint — and so the sort depth — is unchanged.
        float a = CrossBillboardSorter.HalfDepth(2f, 3f, 0.6f, 0.8f, 12f, -1f, 0f, 0.7f);
        float b = CrossBillboardSorter.HalfDepth(2f, 3f, 0.6f, 0.8f, 12f, 0f, -1f, 0.7f);
        Assert.Equal(a, b, 5);
    }

    [Fact]
    public void SortBackToFront_OrdersAscendingDepth()
    {
        var halves = new BillQuad[4];
        halves[0].Depth = 3f;
        halves[1].Depth = -2f;
        halves[2].Depth = 5f;
        halves[3].Depth = 0f;
        CrossBillboardSorter.SortBackToFront(halves);
        Assert.Equal(new[] { -2f, 0f, 3f, 5f },
            new[] { halves[0].Depth, halves[1].Depth, halves[2].Depth, halves[3].Depth });
    }
}
