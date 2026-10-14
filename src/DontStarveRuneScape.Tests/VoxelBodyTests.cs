namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Render;
using DontStarveRuneScape.Camera;
using Xunit;

/// <summary>
/// Voxel player body: body.png projected on the FRONT face of a voxel slab
/// (user decision 2026-10-13 — the sprite's exact art reads from the front,
/// with generated side/back views carrying the other surfaces). Geometry
/// contracts only; the look is verified by capture.
/// </summary>
public class VoxelBodyTests
{
    private static readonly Dictionary<char, (byte R, byte G, byte B)> Palette = new()
    {
        ['h'] = (60, 40, 30),     // hair
        ['b'] = (139, 90, 43),    // brown
        ['s'] = (255, 220, 180),  // skin
        ['g'] = (34, 139, 34),    // tunic
    };

    private static Camera Cam(float yawDeg = 45f)
    {
        var cam = new Camera(1600, 900);
        // A player anchor switches WorldToScreen on (identity without one)
        // and pins the orbit's pan to a known world point.
        cam.SetPlayer(new DontStarveRuneScape.Core.Player(100f, 100f));
        cam.SetViewAngles(yawDeg: yawDeg, pitchDeg: 30f);
        cam.SetOrbit(cam.Yaw, cam.Pitch);   // refresh pan onto the player
        return cam;
    }

    /// <summary>A 16x16 front view: green torso rows 10-12 at columns 6-9.</summary>
    private static VoxelBody.Art Torso() => VoxelBody.Art.FromRows(Rows(
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "......gggg......",
        "......gggg......",
        "......gggg......",
        "................",
        "................",
        "................"), Palette);

    private static string[] Rows(params string[] rows) => rows;

    /// <summary>A side view whose run at rows 10-12 is 6 cells wide (depth 6).</summary>
    private static VoxelBody.Art Side(int firstCol = 5, int lastCol = 10)
    {
        var rows = new string[16];
        for (int i = 0; i < 16; i++)
            rows[i] = new string('.', 16);
        for (int y = 10; y <= 12; y++)
        {
            var chars = rows[y].ToCharArray();
            for (int x = firstCol; x <= lastCol; x++) chars[x] = x == firstCol ? 's' : 'h';
            rows[y] = new string(chars);
        }
        return VoxelBody.Art.FromRows(rows, Palette);
    }

    /// <summary>A back view: the same torso rows, brown instead of green.</summary>
    private static VoxelBody.Art Back() => VoxelBody.Art.FromRows(Rows(
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "......bbbb......",
        "......bbbb......",
        "......bbbb......",
        "................",
        "................",
        "................"), Palette);

    [Fact]
    public void BuildSlab_ProjectsTheArtOnTheFrontPlane()
    {
        var faces = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var fronts = faces.Where(f => f.Plane == 0).ToArray();

        // Every front face sits on one flat plane (the art is coplanar).
        Assert.All(fronts, f => Assert.Equal(f.Lon0, f.Lon1));
        Assert.Single(fronts.Select(f => f.Lon0).Distinct());

        // The green torso covers exactly columns 6-9, rows 10-12, in the
        // art's own colour: 4 art px wide, 3 rows tall.
        Assert.All(fronts, f => Assert.Equal((34, (byte)139, (byte)34), (f.R, f.G, f.B)));
        float left = fronts.Min(f => f.Lat0), right = fronts.Max(f => f.Lat1);
        Assert.Equal(4 * VoxelBody.ArtPxLat, right - left, 3);
        Assert.Equal(-2 * VoxelBody.ArtPxLat, left, 3);   // columns 6-9 around the centre
        Assert.Equal(2 * VoxelBody.ArtPxLat, right, 3);

        float bot = fronts.Min(f => f.H0), top = fronts.Max(f => f.H1);
        Assert.Equal(3 * VoxelBody.ArtPxVert, top - bot, 3);
        Assert.Equal(3 * VoxelBody.ArtPxVert, bot, 3);   // art row 12 is 3 rows up
    }

    [Fact]
    public void BuildSlab_DepthComesFromTheSideView()
    {
        // The side view spans columns 5..10 = 6 art px deep.
        var faces = VoxelBody.BuildSlab(Torso(), Side(), Back());
        float front = faces.First(f => f.Plane == 0).Lon0;
        float back = faces.First(f => f.Plane == 1).Lon0;
        Assert.Equal(6 * VoxelBody.ArtPxLat, front - back, 3);

        // A shallower side view reads as a shallower body.
        var slim = VoxelBody.BuildSlab(Torso(), Side(6, 8), Back());
        float slimFront = slim.First(f => f.Plane == 0).Lon0;
        float slimBack = slim.First(f => f.Plane == 1).Lon0;
        Assert.Equal(3 * VoxelBody.ArtPxLat, slimFront - slimBack, 3);

        // Missing side art still extrudes one cell: never a paper sheet.
        var none = VoxelBody.BuildSlab(Torso(),
            VoxelBody.Art.FromRows(new string[16].Select(_ => new string('.', 16)).ToArray(), Palette),
            Back());
        Assert.Equal(VoxelBody.ArtPxLat,
            none.First(f => f.Plane == 0).Lon0 - none.First(f => f.Plane == 1).Lon0, 3);
    }

    [Fact]
    public void BuildSlab_SidesWearTheSideViewColours()
    {
        var faces = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var sides = faces.Where(f => f.Plane is 2 or 3).ToArray();

        // Both lateral faces of the torso rows run the full depth in the
        // side view's colours: skin at the front plane, hair behind — two
        // same-colour runs for each of the three torso rows.
        Assert.Equal(6, sides.Count(f => f.Plane == 2));
        Assert.Equal(6, sides.Count(f => f.Plane == 3));
        Assert.Contains(sides, f => (f.R, f.G, f.B) == (255, 220, 180));
        Assert.Contains(sides, f => (f.R, f.G, f.B) == (60, 40, 30));

        // The skin cell is one cell deep; hair covers the remaining five.
        float front = faces.First(f => f.Plane == 0).Lon0;
        var skin = sides.First(f => (f.R, f.G, f.B) == (255, 220, 180));
        Assert.Equal(VoxelBody.ArtPxLat, skin.Lon0 - skin.Lon1, 3);
        Assert.Equal(front, skin.Lon0, 3);
    }

    [Fact]
    public void BuildSlab_BackFaceWearsTheBackView()
    {
        var faces = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var backs = faces.Where(f => f.Plane == 1).ToArray();
        Assert.All(backs, f => Assert.Equal(((byte)139, (byte)90, (byte)43), (f.R, f.G, f.B)));
        // Same lateral extent as the front: the body is as wide as it is drawn.
        Assert.Equal(faces.Where(f => f.Plane == 0).Min(f => f.Lat0), backs.Min(f => f.Lat0), 3);
        Assert.Equal(faces.Where(f => f.Plane == 0).Max(f => f.Lat1), backs.Max(f => f.Lat1), 3);
    }

    [Fact]
    public void BuildSlab_TopFaceOnlyWhereTheCellAboveIsEmpty()
    {
        // A single torso row: nothing above it, so its top is exposed.
        var solo = VoxelBody.Art.FromRows(Rows(
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "......gggg......",
            "................",
            "................",
            "................",
            "................",
            "................"), Palette);
        var one = VoxelBody.BuildSlab(solo, Side(), Back());
        Assert.Contains(one, f => f.Plane == 4);

        // Stacked rows: the lower rows' tops are covered by the row above
        // (the side view is no deeper there), so only the top row shows one.
        var stack = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var tops = stack.Where(f => f.Plane == 4).ToArray();
        Assert.Single(tops);
        Assert.Equal(6 * VoxelBody.ArtPxVert, tops[0].H0, 3);   // art row 10's top edge
        Assert.Equal(4 * VoxelBody.ArtPxLat, tops[0].Lat1 - tops[0].Lat0, 3);
    }

    [Fact]
    public void BuildSlab_TopFaceCoversTheRearBand_WhenTheRowAboveIsShallower()
    {
        // Row 12 is 6 deep, row 11 only 3: the rear 3 cells of row 12's top
        // face are exposed behind the row above.
        var side = VoxelBody.Art.FromRows(new string[16].Select((_, i) =>
        {
            if (i is not (11 or 12)) return new string('.', 16);
            var chars = new string('.', 16).ToCharArray();
            for (int x = 5; x <= (i == 12 ? 10 : 7); x++) chars[x] = 'h';
            return new string(chars);
        }).ToArray(), Palette);
        var front = VoxelBody.Art.FromRows(Rows(
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "......gggg......",
            "......gggg......",
            "................",
            "................",
            "................"), Palette);

        var faces = VoxelBody.BuildSlab(front, side, Back());
        // Row 11 tops the stack (nothing above it) — its full 3-deep top
        // shows. Row 12's top shows only the rear 3-cell band its wider
        // side view exposes behind row 11.
        var top11 = faces.Single(f => f is { Plane: 4 } && f.H0 == (16 - 11) * VoxelBody.ArtPxVert);
        var top12 = faces.Single(f => f is { Plane: 4 } && f.H0 == (16 - 12) * VoxelBody.ArtPxVert);
        Assert.Equal(3 * VoxelBody.ArtPxLat, top12.Lon0 - top12.Lon1, 3);   // exposed band only
        float back12 = faces.Where(f => f.Plane == 1).Min(f => f.Lon0);     // row 12 is deepest
        Assert.Equal(back12, top12.Lon1, 3);
    }

    [Fact]
    public void BuildSlab_Height_StaysUnderTheBillboardNominal()
    {
        // body.png reaches art row 1, so the figure tops out at 15 art px:
        // 82.5 world px — the same silhouette height the paper doll showed.
        var art = VoxelBody.Art.FromRows(Rows(
            "................",
            ".......hh.......",
            "......hhhh......",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "................",
            "......gggg......",
            "......gggg......",
            "......gggg......",
            "................",
            "................",
            "................"), Palette);
        var faces = VoxelBody.BuildSlab(art, Side(), Back());
        float top = faces.Max(f => f.H1);
        Assert.Equal(15 * VoxelBody.ArtPxVert, top, 3);
        Assert.InRange(top, 30f, 88f);
    }

    [Fact]
    public void Project_FaceTowardTheCamera_ShowsTheArt_FacingAwayShowsTheBack()
    {
        var model = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var buffer = new VoxelBody.Face[model.Length];

        // Travel 45° off the view axis (driving dead AT the camera makes
        // both the front/back and lateral faces edge-on, and none draw —
        // the honest 3D read).
        var cam = Cam(45f);
        int nf = VoxelBody.Project(cam, 0f, 0f, 12f, 1f, 0f, model, buffer);
        Assert.Contains(buffer[..nf], f => f.Plane == 0);
        Assert.DoesNotContain(buffer[..nf], f => f.Plane == 1);
        // ...and only one lateral side is ever camera-facing.
        Assert.Single(buffer[..nf].Where(f => f.Plane is 2 or 3).Select(f => f.Plane).Distinct());

        // Away from the camera: the back view replaces the art.
        nf = VoxelBody.Project(cam, 0f, 0f, 12f, -1f, 0f, model, buffer);
        Assert.DoesNotContain(buffer[..nf], f => f.Plane == 0);
        Assert.Contains(buffer[..nf], f => f.Plane == 1);
        Assert.Single(buffer[..nf].Where(f => f.Plane is 2 or 3).Select(f => f.Plane).Distinct());
    }

    [Fact]
    public void Project_TopFacesAboveTheirRow_OnScreen()
    {
        var model = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var buffer = new VoxelBody.Face[model.Length];
        var cam = Cam();
        int nf = VoxelBody.Project(cam, 100f, 100f, 12f, 1f, 0f, model, buffer);

        var top = buffer[..nf].First(f => f.Plane == 4);
        var side = buffer[..nf].First(f => f.Plane is 2 or 3);
        float topY = (top.BLy + top.TLy) * 0.5f, sideTopY = (side.TLy + side.TRy) * 0.5f;
        // Screen y grows downward: the top face rides the silhouette's summit.
        Assert.True(topY < sideTopY, $"top {topY:F1} should sit above the side top {sideTopY:F1}");
    }

    [Fact]
    public void Project_SortsBackToFront()
    {
        var model = VoxelBody.BuildSlab(Torso(), Side(), Back());
        var buffer = new VoxelBody.Face[model.Length];
        var cam = Cam();
        float towardX = MathF.Sin(cam.Yaw), towardY = MathF.Cos(cam.Yaw);
        int nf = VoxelBody.Project(cam, 0f, 0f, 12f, towardX, towardY, model, buffer);
        Array.Sort(buffer, 0, nf);
        for (int i = 1; i < nf; i++)
            Assert.True(buffer[i].Depth >= buffer[i - 1].Depth, "faces must ascend by view depth");
        // The art's front plane is nearest, so it paints last.
        Assert.Equal(0, buffer[nf - 1].Plane);
    }
}
