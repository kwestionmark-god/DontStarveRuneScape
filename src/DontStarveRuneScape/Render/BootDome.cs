namespace DontStarveRuneScape.Render;

/// <summary>
/// Dome-cap boot dimensions in world px, in the boot's local frame:
/// lon runs along the travel direction (positive = toe), lat runs
/// laterally, and the dome's height is measured from the ground.
/// </summary>
public readonly struct FootDomeDims
{
    /// <summary>Footprint radius forward of the ankle (the toe).</summary>
    public readonly float Toe;
    /// <summary>Footprint radius behind the ankle (the heel).</summary>
    public readonly float Heel;
    /// <summary>Lateral half-width of the footprint.</summary>
    public readonly float HalfWidth;
    /// <summary>Dome height at the apex, above the ground.</summary>
    public readonly float Height;

    public FootDomeDims(float toe, float heel, float halfWidth, float height)
    {
        Toe = toe; Heel = heel; HalfWidth = halfWidth; Height = height;
    }
}

/// <summary>
/// Pure geometry for the spherical dome boots: a latitude/longitude grid on
/// a half-ellipsoid cap with a toe bias. The toe half of the footprint uses
/// <see cref="FootDomeDims.Toe"/>, the heel half uses
/// <see cref="FootDomeDims.Heel"/> (a kinked footprint whose surface stays
/// continuous at lon = 0). Grid vertices can be rocked rigidly by the
/// swing phase — tilting about a toe/heel pivot and rolling about the
/// centerline — and the class provides the exact screen-facing test for
/// the camera's affine projection so back-facing surface cells cull.
/// No GL types here, so the math is unit-testable in isolation.
/// </summary>
public static class BootDome
{
    /// <summary>Segments around the footprint. Multiple of 4 so the
    /// toe/heel kink lands exactly on a cell boundary.</summary>
    public const int Segments = 16;
    /// <summary>Bands from the apex (ring 0) down to the base ring.</summary>
    public const int Rings = 6;

    /// <summary>Max swing pitch as a tangent: the dome rocks ±~20° at
    /// toe-off (−1) and heel-strike (+1). Matches the shoe-box boots.</summary>
    public const float TiltTan = 0.36f;
    /// <summary>Max swing roll as a tangent: the dome top leans laterally
    /// ~12° at mid-swing, settling flat at liftoff and plant.</summary>
    public const float RollTan = 0.22f;

    /// <summary>One grid vertex: position plus the outward surface normal,
    /// both in boot-local coordinates (lon along travel, lat lateral,
    /// h up; all world px).</summary>
    public readonly struct Point
    {
        public readonly float Lon, Lat, H, NLon, NLat, NH;
        public Point(float lon, float lat, float h, float nLon, float nLat, float nH)
        {
            Lon = lon; Lat = lat; H = h; NLon = nLon; NLat = nLat; NH = nH;
        }
    }

    /// <summary>Grid vertex at <paramref name="ring"/> (0 = apex,
    /// <see cref="Rings"/> = base) and <paramref name="seg"/> (0 = toe,
    /// running clockwise: +lat at a quarter turn, heel at half). The
    /// swing tilt/roll rock the whole dome rigidly:
    /// tilt −1 pivots on the toe (toe-off drag), +1 on the heel
    /// (heel-strike), and roll leans the top toward +lat.</summary>
    public static void Vertex(int ring, int seg, in FootDomeDims dims, float tilt, float roll, out Point p)
    {
        // Half-ellipsoid cap: rings walk the polar angle from the apex to
        // the equator, segments walk the footprint angle around it.
        float phi = (float)ring / Rings * (MathF.PI * 0.5f);
        float alpha = (float)seg / Segments * (MathF.PI * 2f);
        float sp = MathF.Sin(phi), cp = MathF.Cos(phi);
        float ca = MathF.Cos(alpha), sa = MathF.Sin(alpha);
        float a = ca >= 0f ? dims.Toe : dims.Heel;

        float lon = a * sp * ca;
        float lat = dims.HalfWidth * sp * sa;
        float h = dims.Height * cp;

        // Outward normal of the ellipsoid cap ∝ (lon/a², lat/b², h/H²).
        float nl = sp * ca / a;
        float nt = sp * sa / dims.HalfWidth;
        float nu = cp / dims.Height;

        if (tilt != 0f)
        {
            // Rigid rotation about the lateral axis through the pivot:
            // the planted toe (tilt −1) or heel (tilt +1) stays put while
            // the rest of the dome rocks over it.
            float pivot = (dims.Toe * (1f - tilt) - dims.Heel * (1f + tilt)) * 0.5f;
            float ang = tilt * TiltTan;
            float s = MathF.Sin(ang), c = MathF.Cos(ang);
            float rel = lon - pivot;
            lon = pivot + rel * c - h * s;
            h = rel * s + h * c;
            float t = nl;
            nl = nl * c - nu * s;
            nu = t * s + nu * c;
        }

        if (roll != 0f)
        {
            // Rigid rotation about the travel axis through the centerline.
            float ang = roll * RollTan;
            float s = MathF.Sin(ang), c = MathF.Cos(ang);
            float t = lat;
            lat = lat * c + h * s;
            h = -t * s + h * c;
            t = nt;
            nt = nt * c + nu * s;
            nu = -t * s + nu * c;
        }

        float inv = 1f / MathF.Sqrt(nl * nl + nt * nt + nu * nu);
        p = new Point(lon, lat, h, nl * inv, nt * inv, nu * inv);
    }

    /// <summary>True when a surface cell with this (local-frame) normal
    /// faces the camera. The camera's WorldToScreen is affine, so its view
    /// axis — the kernel of the projection, pointing out of the screen —
    /// is (sinYaw·sinPitch, cosYaw·sinPitch, cosPitch); a normal faces the
    /// camera iff its dot with that axis is positive. For pure vertical
    /// faces this reduces to the old ground-plane yaw test, and dome tops
    /// stay visible at every pitch below 90°.</summary>
    public static bool FacesCamera(float nLon, float nLat, float nH,
        float dirX, float dirY, float latX, float latY, float yaw, float pitch)
    {
        // Local frame → world (the up component maps directly: boot dims
        // and the projection's elevation channel share world-px units).
        float wx = nLon * dirX + nLat * latX;
        float wy = nLon * dirY + nLat * latY;
        float dx = MathF.Sin(yaw) * MathF.Sin(pitch);
        float dy = MathF.Cos(yaw) * MathF.Sin(pitch);
        float dz = MathF.Cos(pitch);
        return wx * dx + wy * dy + nH * dz > 0f;
    }
}
