using DontStarveRuneScape.Render;
using Xunit;

namespace DontStarveRuneScape.Tests;

/// <summary>
/// BootDome geometry: the dome-cap grid must sit exactly on its
/// half-ellipsoid footprint, stay continuous across the toe/heel kink,
/// rock rigidly on the swing pivots, and cull cells with the exact
/// screen-facing test for the camera's affine projection.
/// </summary>
public class BootDomeTests
{
    private static readonly FootDomeDims Dims = new(toe: 3f, heel: 2f, halfWidth: 2.5f, height: 3.5f);

    [Fact]
    public void GridPinsFootprintExtremes()
    {
        // Apex sits straight above the origin at full height.
        BootDome.Vertex(0, 0, Dims, 0f, 0f, out var apex);
        Assert.Equal(0f, apex.Lon, 5);
        Assert.Equal(0f, apex.Lat, 5);
        Assert.Equal(Dims.Height, apex.H, 5);

        // Base ring: toe forward, heel back, ±lat at the quarter turns.
        BootDome.Vertex(BootDome.Rings, 0, Dims, 0f, 0f, out var toe);
        Assert.Equal(Dims.Toe, toe.Lon, 5);
        Assert.Equal(0f, toe.Lat, 5);
        Assert.Equal(0f, toe.H, 5);

        BootDome.Vertex(BootDome.Rings, BootDome.Segments / 2, Dims, 0f, 0f, out var heel);
        Assert.Equal(-Dims.Heel, heel.Lon, 5);
        Assert.Equal(0f, heel.Lat, 5);
        Assert.Equal(0f, heel.H, 5);

        BootDome.Vertex(BootDome.Rings, BootDome.Segments / 4, Dims, 0f, 0f, out var side);
        Assert.Equal(0f, side.Lon, 5);
        Assert.Equal(Dims.HalfWidth, side.Lat, 5);
        Assert.Equal(0f, side.H, 5);
    }

    [Fact]
    public void SurfaceContinuousAcrossToeHeelKink()
    {
        // The footprint kink between the toe and heel halves sits at
        // lon = 0; the surface there must not jump in lat or height.
        for (int ring = 0; ring <= BootDome.Rings; ring++)
        {
            BootDome.Vertex(ring, BootDome.Segments / 4, Dims, 0f, 0f, out var seam);
            Assert.Equal(0f, seam.Lon, 5);          // seam is exactly at lon 0

            // Mirror segments just either side of the kink share lat/height.
            BootDome.Vertex(ring, BootDome.Segments / 4 - 1, Dims, 0f, 0f, out var toeSide);
            BootDome.Vertex(ring, BootDome.Segments / 4 + 1, Dims, 0f, 0f, out var heelSide);
            Assert.Equal(toeSide.Lat, heelSide.Lat, 5);
            Assert.Equal(toeSide.H, heelSide.H, 5);
            Assert.Equal(seam.H, toeSide.H, 4);     // height runs flat across the seam
        }
    }

    [Fact]
    public void HeightDescendsFromApexToBase()
    {
        float prev = float.MaxValue;
        for (int ring = 0; ring <= BootDome.Rings; ring++)
        {
            BootDome.Vertex(ring, 0, Dims, 0f, 0f, out var v);
            Assert.True(v.H < prev, $"ring {ring} height {v.H} not below ring {ring - 1} ({prev})");
            prev = v.H;
        }
        Assert.Equal(0f, prev, 5);
    }

    [Fact]
    public void NormalsAreUnitOutwardAndHorizontalAtTheBase()
    {
        for (int ring = 0; ring <= BootDome.Rings; ring++)
            for (int seg = 0; seg < BootDome.Segments; seg++)
            {
                BootDome.Vertex(ring, seg, Dims, 0f, 0f, out var v);
                float len = MathF.Sqrt(v.NLon * v.NLon + v.NLat * v.NLat + v.NH * v.NH);
                Assert.Equal(1f, len, 4);

                // Outward: the normal's lon component follows the point's.
                if (MathF.Abs(v.Lon) > 1e-4f)
                    Assert.Equal(MathF.Sign(v.Lon), MathF.Sign(v.NLon));

                if (ring == BootDome.Rings)
                {
                    Assert.Equal(0f, v.NH, 4);      // base ring normals are horizontal
                    Assert.Equal(0f, v.H, 5);
                }
            }

        // Apex normal points straight up.
        BootDome.Vertex(0, 0, Dims, 0f, 0f, out var apex);
        Assert.Equal(0f, apex.NLon, 5);
        Assert.Equal(0f, apex.NLat, 5);
        Assert.Equal(1f, apex.NH, 5);
    }

    [Fact]
    public void HeelStrikePivotsOnTheHeelAndLiftsTheToe()
    {
        // tilt +1 (heel-strike): the heel base vertex stays planted, the
        // toe base vertex rises.
        BootDome.Vertex(BootDome.Rings, BootDome.Segments / 2, Dims, 1f, 0f, out var heel);
        Assert.Equal(0f, heel.H, 5);
        BootDome.Vertex(BootDome.Rings, 0, Dims, 1f, 0f, out var toe);
        Assert.True(toe.H > 0.5f, $"toe should lift at heel-strike, got h={toe.H}");
        // ... and the toe slid back toward the pivot, not forward.
        Assert.True(toe.Lon < Dims.Toe, "toe should rotate about the heel pivot");
    }

    [Fact]
    public void ToeOffPivotsOnTheToeAndLiftsTheHeel()
    {
        // tilt −1 (toe-off drag): the toe stays planted, the heel rises.
        BootDome.Vertex(BootDome.Rings, 0, Dims, -1f, 0f, out var toe);
        Assert.Equal(0f, toe.H, 5);
        BootDome.Vertex(BootDome.Rings, BootDome.Segments / 2, Dims, -1f, 0f, out var heel);
        Assert.True(heel.H > 0.5f, $"heel should lift at toe-off, got h={heel.H}");
    }

    [Fact]
    public void RollLeansTheApexTowardItsSide()
    {
        BootDome.Vertex(0, 0, Dims, 0f, 1f, out var leanRight);
        Assert.True(leanRight.Lat > 0.1f, $"apex should lean toward +lat, got lat={leanRight.Lat}");
        Assert.True(leanRight.H < Dims.Height, "leaning apex drops below the upright height");

        BootDome.Vertex(0, 0, Dims, 0f, -1f, out var leanLeft);
        Assert.True(leanLeft.Lat < -0.1f, $"apex should lean toward −lat, got lat={leanLeft.Lat}");
    }

    [Fact]
    public void TiltAndRollKeepPivotAndNormalsRigid()
    {
        // Rotations preserve normal length at every cell of the grid.
        for (int ring = 0; ring <= BootDome.Rings; ring++)
            for (int seg = 0; seg < BootDome.Segments; seg++)
            {
                BootDome.Vertex(ring, seg, Dims, 0.75f, -0.5f, out var v);
                float len = MathF.Sqrt(v.NLon * v.NLon + v.NLat * v.NLat + v.NH * v.NH);
                Assert.Equal(1f, len, 4);
            }
    }

    [Fact]
    public void FacesCamera_MatchesGroundPlaneYawTestForVerticalFaces()
    {
        // dir = world +y: a cell facing +y (local lon normal 1,0,0 → world
        // (0,1,0)) must mirror the old frontVisible/latVisible rule —
        // visible when the camera yaw's view vector points its way.
        float dirX = 0f, dirY = 1f, latX = -1f, latY = 0f;
        Assert.True(BootDome.FacesCamera(1f, 0f, 0f, dirX, dirY, latX, latY, yaw: 0f, pitch: 0.5f));
        Assert.False(BootDome.FacesCamera(1f, 0f, 0f, dirX, dirY, latX, latY, yaw: MathF.PI, pitch: 0.5f));
        Assert.True(BootDome.FacesCamera(0f, -1f, 0f, dirX, dirY, latX, latY, yaw: MathF.PI / 2f, pitch: 0.5f));
        Assert.False(BootDome.FacesCamera(0f, -1f, 0f, dirX, dirY, latX, latY, yaw: -MathF.PI / 2f, pitch: 0.5f));
    }

    [Fact]
    public void FacesCamera_TopsVisibleAtEveryPitch()
    {
        // Up-facing cells stay visible from any yaw at any play pitch…
        Assert.True(BootDome.FacesCamera(0f, 0f, 1f, 0f, -1f, 1f, 0f, yaw: 2.3f, pitch: 0.52f));
        Assert.True(BootDome.FacesCamera(0f, 0f, 1f, 1f, 0f, 0f, 1f, yaw: -1.1f, pitch: 1.2f));
        // …even edge-on at pitch 0 the up cell is the only thing visible.
        Assert.True(BootDome.FacesCamera(0f, 0f, 1f, 0f, 1f, -1f, 0f, yaw: 0.7f, pitch: 0f));
    }

    [Fact]
    public void FacesCamera_PitchGatesHowFarOverTheFarSideIsSeen()
    {
        // A far-side cell tilted 45° up: visible from a shallower camera
        // (30°, looking across the dome) but culled from steeper (60°,
        // looking down). World normal (0, −sin45, cos45) with dir = +y.
        float s = MathF.Sin(MathF.PI / 4f), c = MathF.Cos(MathF.PI / 4f);
        float nLon = -s, nH = c;                     // world (0, −s, c)
        Assert.True(BootDome.FacesCamera(nLon, 0f, nH, 0f, 1f, -1f, 0f, yaw: 0f, pitch: MathF.PI / 6f));
        Assert.False(BootDome.FacesCamera(nLon, 0f, nH, 0f, 1f, -1f, 0f, yaw: 0f, pitch: MathF.PI / 3f));
    }
}
