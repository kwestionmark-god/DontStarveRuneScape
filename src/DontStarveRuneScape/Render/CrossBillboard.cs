namespace DontStarveRuneScape.Render;

/// <summary>An axis-described projected quad: four screen corners
/// (bottom-left, bottom-right, top-right, top-left) plus its center and
/// half extents, for anchored overlays (carried gear, waterline).</summary>
internal struct BillQuad
{
    public float BLx, BLy, BRx, BRy, TRx, TRy, TLx, TLy;
    public float Cx, Cy, HalfW, HalfH;
    // UV horizontal range (for half-planes): full quad is 0..1.
    public float U0 = 0f, U1 = 1f;
    // View depth of the quad's center, for back-to-front sorting of the
    // crossed halves (larger = closer to the camera = drawn later).
    public float Depth;

    public BillQuad() { }
}

/// <summary>Fold-aware view-depth math for the crossed-billboard bodies:
/// each plane splits at the fold into two half-quads whose sort key is the
/// view depth of the half's CENTER along the plane axis, so mirroring a
/// plane by swapping its U span never changes the depth ordering.</summary>
internal static class CrossBillboardSorter
{
    /// <summary>View depth of a world point along the camera yaw: larger =
    /// nearer the camera = paints later. Matches the boot-dome in-front
    /// test in SpriteRenderer.DrawBootDomes.</summary>
    public static float ViewDepth(float wx, float wy, float yawRad) =>
        wx * MathF.Sin(yawRad) + wy * MathF.Cos(yawRad);

    /// <summary>View depth of a half-plane's center: the midpoint of the
    /// world span [<paramref name="span0"/>, <paramref name="span1"/>]
    /// (fractions along the plane axis in [-1, +1]). Swapping the span
    /// endpoints (mirror) leaves the midpoint — and so the depth —
    /// unchanged: mirroring is a UV concern, never a draw-order one.</summary>
    public static float HalfDepth(float wx, float wy, float ax, float ay,
        float halfWidthWorld, float span0, float span1, float yawRad)
    {
        float mid = (span0 + span1) * 0.5f;
        return ViewDepth(wx + ax * halfWidthWorld * mid,
                         wy + ay * halfWidthWorld * mid, yawRad);
    }

    /// <summary>Sort crossed-billboard halves back-to-front (ascending
    /// depth) so the nearest half-arm paints last. Halves only meet at the
    /// fold line, so painter order is exact under every yaw; equal depths
    /// (an edge-on, zero-width plane) are invisible either way.</summary>
    public static void SortBackToFront(BillQuad[] halves) =>
        Array.Sort(halves, static (a, b) => a.Depth.CompareTo(b.Depth));
}
