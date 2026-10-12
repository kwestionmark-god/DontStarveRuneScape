namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 4 (spec:
/// docs/superpowers/specs/2026-10-11-vertical-building-render-design.md):
/// structures render as world-anchored crossed planes (not camera-fixed
/// floating quads), bases plant at the cumulative column base, fence
/// facing/rails follow the slice-3 connection mask.
/// </summary>
public class StructureBillboardTests
{
    private static Camera MakeCamera(float yawDeg, float pitchDeg = 30f, float zoom = 1f)
    {
        var cam = new Camera(1280, 720);
        cam.SetPlayer(new Player(0f, 0f));
        cam.SetViewAngles(yawDeg: yawDeg, pitchDeg: pitchDeg, zoom: zoom);
        return cam;
    }

    // ── 1. World-fixed planes, not camera-facing ───────────────────────────

    [Fact]
    public void StructurePlanes_HorizontalExtent_FlipsWithYaw()
    {
        // Mirror of the resource-billboard contract: with facing (0.6, 0.8),
        // one plane is wider at yaw 0 and the cross plane wider at yaw 90 —
        // a camera-facing billboard could never change extent.
        var cam0 = MakeCamera(0f);
        var cam90 = MakeCamera(90f);
        var halves0 = new BillQuad[4];
        var halves90 = new BillQuad[4];

        SpriteRenderer.BuildStructureHalves(cam0, 0f, 0f, 0f, 24f, 96f, 0.6f, 0.8f, halves0, out _);
        SpriteRenderer.BuildStructureHalves(cam90, 0f, 0f, 0f, 24f, 96f, 0.6f, 0.8f, halves90, out _);

        float spanDir0 = MathF.Max(halves0[0].HalfW, halves0[1].HalfW) * 2f;
        float spanCross0 = MathF.Max(halves0[2].HalfW, halves0[3].HalfW) * 2f;
        float spanDir90 = MathF.Max(halves90[0].HalfW, halves90[1].HalfW) * 2f;
        float spanCross90 = MathF.Max(halves90[2].HalfW, halves90[3].HalfW) * 2f;

        Assert.True(spanCross0 > spanDir0,
            $"yaw 0: cross plane should dominate (got cross {spanCross0} vs dir {spanDir0})");
        Assert.True(spanDir90 > spanCross90,
            $"yaw 90: the extents must flip (got dir {spanDir90} vs cross {spanCross90})");
    }

    [Fact]
    public void StructurePlanes_BaseEdge_PlantsOnGround_AtAnyYaw()
    {
        const float wx = 100f, wy = 200f, elev = 3f, halfW = 24f, height = 96f;
        const float dirX = 0.6f, dirY = 0.8f;

        foreach (var yaw in new[] { 0f, 45f, 90f, 135f, 200f })
        {
            var cam = MakeCamera(yaw);
            var halves = new BillQuad[4];
            SpriteRenderer.BuildStructureHalves(cam, wx, wy, elev, halfW, height, dirX, dirY, halves, out _);

            // halves 0,1 = dir plane spans (-1,0),(0,1); 2,3 = cross plane.
            (float ax, float ay, float s0, float s1)[] spans =
            [
                (dirX, dirY, -1f, 0f), (dirX, dirY, 0f, 1f),
                (-dirY, dirX, -1f, 0f), (-dirY, dirX, 0f, 1f),
            ];
            for (int i = 0; i < 4; i++)
            {
                var (ax, ay, s0, s1) = spans[i];
                var bl = cam.WorldToScreen(wx + ax * halfW * s0, wy + ay * halfW * s0, elev);
                var br = cam.WorldToScreen(wx + ax * halfW * s1, wy + ay * halfW * s1, elev);
                Assert.True(MathF.Abs(halves[i].BLx - bl.X) < 0.6f &&
                            MathF.Abs(halves[i].BLy - bl.Y) < 0.6f,
                    $"yaw {yaw} half {i}: base-left corner off the ground line");
                Assert.True(MathF.Abs(halves[i].BRx - br.X) < 0.6f &&
                            MathF.Abs(halves[i].BRy - br.Y) < 0.6f,
                    $"yaw {yaw} half {i}: base-right corner off the ground line");
            }
        }
    }

    // ── 2. Column bases (pure seams in BlockStack) ──────────────────────────

    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public Skills.SkillManager Skills = new();
        public Inv Inventory = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            World.BuildCornerElevations();
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("stick", 30);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        public Structure Place(string id, int tx, int ty)
        {
            var placed = Buildings.PlaceStructure(id, tx, ty, World, Inventory, Skills);
            Assert.True(placed.Success, placed.Message);
            return Buildings.Structures[^1];
        }
    }

    [Fact]
    public void RenderBase_StackedMound_SecondSitsOnFirstTop()
    {
        var h = new Harness();
        var first = h.Place("mound_block", 4, 4);
        var second = h.Place("mound_block", 4, 4);
        float tile = Constants.SeaLevel + 5f;

        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, first, tile));
        Assert.Equal(tile + 2f, BlockStack.RenderBase(h.World, h.Buildings, second, tile));
    }

    [Fact]
    public void RenderBase_FenceAtopMound_SitsOnMoundTop()
    {
        var h = new Harness();
        h.Place("mound_block", 4, 4);
        var fence = h.Place("fence_block", 4, 4);
        float tile = Constants.SeaLevel + 5f;

        Assert.Equal(tile + 2f, BlockStack.RenderBase(h.World, h.Buildings, fence, tile));
    }

    [Fact]
    public void RenderBase_LoneFence_IsTile()
    {
        var h = new Harness();
        var fence = h.Place("fence_block", 4, 4);
        float tile = Constants.SeaLevel + 5f;
        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, fence, tile));
    }

    [Fact]
    public void RenderBase_NonBlock_IsTile()
    {
        var h = new Harness();
        var fire = h.Place("campfire", 4, 4);
        float tile = Constants.SeaLevel + 5f;
        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, fire, tile));
        // campfire has no stack height: StructureDef non-null, IsBlock false.
        Assert.True(fire.StructureDef?.IsBlock != true);
    }

    [Fact]
    public void RenderTop_Delegates_IE_BasePlusOwnStack()
    {
        var h = new Harness();
        h.Place("mound_block", 4, 4);
        var fence = h.Place("fence_block", 4, 4);
        float tile = Constants.SeaLevel + 5f;

        float baseElevation = BlockStack.RenderBase(h.World, h.Buildings, fence, tile);
        float top = BlockStack.RenderTop(h.World, h.Buildings, fence, tile);
        Assert.Equal(baseElevation + fence.StructureDef!.StackHeight, top);
    }

    // ── 3. Fence facing + rails from the connection mask ────────────────────

    [Fact]
    public void FenceFacing_EastWest_GivesXAxis()
    {
        Assert.Equal((1f, 0f), BlockStack.FenceFacingDir(2 | 8));
    }

    [Fact]
    public void FenceFacing_NorthSouth_GivesYAxis()
    {
        Assert.Equal((0f, 1f), BlockStack.FenceFacingDir(1 | 4));
    }

    [Fact]
    public void FenceFacing_NoneOrFull_KeepsCross()
    {
        Assert.Null(BlockStack.FenceFacingDir(0));
        Assert.Null(BlockStack.FenceFacingDir(15));
    }

    [Fact]
    public void FenceRails_East_SpansCenterToEastEdge()
    {
        var rails = BlockStack.FenceRails(2, 4, 4);
        Assert.Single(rails);
        var r = rails[0];
        // Center (4.5·64, 4.5·64) → east edge midpoint (5·64, 4.5·64).
        Assert.Equal(4.5f * 64, r.X0);
        Assert.Equal(4.5f * 64, r.Y0);
        Assert.Equal(5f * 64, r.X1);
        Assert.Equal(4.5f * 64, r.Y1);
    }

    [Fact]
    public void FenceRails_EastAndWest_TwoRails()
    {
        Assert.Equal(2, BlockStack.FenceRails(2 | 8, 4, 4).Count);
        Assert.Empty(BlockStack.FenceRails(0, 4, 4));
    }

    // ── 4. Height contract ──────────────────────────────────────────────────

    [Fact]
    public void StructureHeight_Mound_IsStackUnitsInWorldPx()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var mound = new Structure
        {
            StructureId = "mound_block",
            StructureDef = registry.GetStructure("mound_block"),
        };
        // 2 units × 12.8 px/unit (ZScale 8 × TerrainHeightScale 1.6).
        Assert.Equal(2f * Constants.ZScale * Constants.TerrainHeightScale,
            SpriteRenderer.StructureHeightWorld(mound));
    }

    [Fact]
    public void StructureHeight_NonBlock_KeepsLegacy32()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var fire = new Structure
        {
            StructureId = "campfire",
            StructureDef = registry.GetStructure("campfire"),
        };
        Assert.Equal(32f, SpriteRenderer.StructureHeightWorld(fire));
    }
}
