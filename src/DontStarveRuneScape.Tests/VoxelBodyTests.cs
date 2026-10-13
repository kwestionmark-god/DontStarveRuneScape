namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Render;
using DontStarveRuneScape.Camera;
using Xunit;

/// <summary>
/// Voxel player body: a static classic-3D figure from projected boxes
/// standing above the dome boots (near-final user decision — the
/// crossed-billboard paper doll stays for NPCs/monsters). Geometry
/// contracts only; the look is verified by capture.
/// </summary>
public class VoxelBodyTests
{
    private static Camera Cam(float yawDeg = 45f)
    {
        var cam = new Camera(1600, 900);
        cam.SetViewAngles(yawDeg: yawDeg, pitchDeg: 30f);
        return cam;
    }

    [Fact]
    public void BuildPart_EmitsThreeFaces_TopPlusTwoCameraSides()
    {
        var faces = new VoxelBody.Face[8];
        var part = VoxelBody.PlayerModel[2]; // torso
        int n = VoxelBody.BuildPart(Cam(), 0f, 0f, 12f, 1f, 0f, part, 0, faces, 0);

        Assert.Equal(3, n);
        Assert.Equal(4, faces[0].Plane);            // top first
        Assert.Contains(faces[..3], f => f.Plane is 0 or 1); // one X side
        Assert.Contains(faces[..3], f => f.Plane is 2 or 3); // one Y side
    }

    [Fact]
    public void BuildPart_TallerSideDependsOnYaw()
    {
        var faces = new VoxelBody.Face[8];
        var part = VoxelBody.PlayerModel[2];
        VoxelBody.BuildPart(Cam(45f), 0f, 0f, 12f, 1f, 0f, part, 0, faces, 0);
        int planeXa = faces[1].Plane, planeYa = faces[2].Plane;
        // Mirrored yaw flips which side of each axis faces the camera.
        VoxelBody.BuildPart(Cam(45f + 180f), 0f, 0f, 12f, 1f, 0f, part, 0, faces, 0);
        Assert.NotEqual(planeXa, faces[1].Plane);
        Assert.NotEqual(planeYa, faces[2].Plane);
    }

    [Fact]
    public void BuildPart_TopIsAboveBase_OnScreen()
    {
        var faces = new VoxelBody.Face[8];
        var part = VoxelBody.PlayerModel[7]; // head
        VoxelBody.BuildPart(Cam(), 100f, 100f, 12f, 1f, 0f, part, 0, faces, 0);
        // Top face centroid sits clearly above the highest side base edge
        // (screen y down): the box reads as rising off the torso.
        float topY = (faces[0].BLy + faces[0].BRy + faces[0].TRy + faces[0].TLy) / 4f;
        float sideBase = (faces[1].BLy + faces[1].BRy) / 2f;
        Assert.True(topY < sideBase - 2f, $"top {topY:F1} should be above side base {sideBase:F1}");
    }

    [Fact]
    public void PlayerModel_Stacks_Contiguously()
    {
        // The figure: legs 0-10, torso 10-25, head 25-33, hair overlapping
        // the head's upper half (26) so it towers as a back-of-head mass.
        var byName = VoxelBody.PlayerModel.ToDictionary(p => p.Name);
        Assert.Equal(10f, byName["torso"].Base);
        Assert.Equal(25f, byName["head"].Base);
        Assert.Equal(26f, byName["hair"].Base);
        // Hands hang from the arm base down past the torso bottom.
        Assert.True(byName["hand_l"].Base < byName["arm_l"].Base);
    }

    [Fact]
    public void PlayerModel_StaysUnderTheBillboardNominal()
    {
        // Silhouette law: the voxel figure never towers over the old
        // paper doll's 44 world-px nominal.
        float top = VoxelBody.PlayerModel.Max(p => p.Base + p.H);
        Assert.InRange(top, 30f, 46f);
    }
}
