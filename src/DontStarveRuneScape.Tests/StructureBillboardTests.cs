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

        // BuildCrossBillboard sorts halves by depth — group by .Plane
        // (0 = dir plane, 1 = cross plane), never by index order.
        static float Span(BillQuad[] halves, int plane)
            => halves.Where(h => h.Plane == plane).Max(h => h.HalfW) * 2f;

        float spanDir0 = Span(halves0, 0), spanCross0 = Span(halves0, 1);
        float spanDir90 = Span(halves90, 0), spanCross90 = Span(halves90, 1);

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

            // Halves are depth-sorted (index order is NOT span order), so
            // match by candidate corner sets: each half of a plane wraps one
            // of (E-, E-mid) / (E-mid, E+) along that plane's axis, corners
            // possibly assigned to BL/BR either way around.
            (float ax, float ay)[] axes = [(dirX, dirY), (-dirY, dirX)];
            for (int i = 0; i < 4; i++)
            {
                var (ax, ay) = axes[halves[i].Plane];
                var eMinus = cam.WorldToScreen(wx - ax * halfW, wy - ay * halfW, elev);
                var eMid = cam.WorldToScreen(wx, wy, elev);
                var ePlus = cam.WorldToScreen(wx + ax * halfW, wy + ay * halfW, elev);
                // Vector2D<float> points — compare fields, not tuples.
                bool CornerOnLine(float cx, float cy)
                {
                    foreach (var e in new[] { eMinus, eMid, ePlus })
                        if (MathF.Abs(cx - e.X) < 0.6f && MathF.Abs(cy - e.Y) < 0.6f)
                            return true;
                    return false;
                }
                Assert.True(CornerOnLine(halves[i].BLx, halves[i].BLy),
                    $"yaw {yaw} half {i}: base-left corner off the ground line");
                Assert.True(CornerOnLine(halves[i].BRx, halves[i].BRy),
                    $"yaw {yaw} half {i}: base-right corner off the ground line");
                // And the two corners must be DISTINCT span points (a planted
                // plane segment, not a degenerate stub).
                float cornerDist = MathF.Sqrt(
                    MathF.Pow(halves[i].BRx - halves[i].BLx, 2) +
                    MathF.Pow(halves[i].BRy - halves[i].BLy, 2));
                Assert.True(cornerDist > 1f,
                    $"yaw {yaw} half {i}: base edge collapsed ({cornerDist}px)");
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
            // RED carries both wallets until GREEN flips the def to dirt_brick.
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("dirt_brick", 64);
            Inventory.AddItem("planks", 8);
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

        /// <summary>Place a brick at a sub-cell of (tx,ty); must succeed.</summary>
        public Structure PlaceBrick(int tx, int ty, int subX, int subY)
        {
            var placed = Buildings.PlaceStructure("mound_block", tx, ty, subX, subY,
                World, Inventory, Skills);
            Assert.True(placed.Success, placed.Message);
            return Buildings.Structures[^1];
        }
    }

    [Fact]
    public void RenderBase_StackedBricks_LayerSitsOnBrickTop_OrderIndependent()
    {
        var h = new Harness();
        var first = h.PlaceBrick(4, 4, 2, 2);    // layer 0
        var second = h.PlaceBrick(4, 4, 2, 2);   // layer 1
        float tile = Constants.SeaLevel + 5f;

        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, first, tile));
        Assert.Equal(tile + 1.25f, BlockStack.RenderBase(h.World, h.Buildings, second, tile));

        // The stored Layer makes the base list-order-independent.
        h.Buildings.Structures.Reverse();
        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, first, tile));
        Assert.Equal(tile + 1.25f, BlockStack.RenderBase(h.World, h.Buildings, second, tile));
    }

    [Fact]
    public void RenderBase_FenceOnBrickTile_PlantsAtGround()
    {
        var h = new Harness();
        h.PlaceBrick(4, 4, 2, 2);
        // All-blocks partition lets a fence share the tile; the fence is
        // tile-level and plants on the ground, never on brick tops.
        var fence = h.Place("fence_block", 4, 4);
        float tile = Constants.SeaLevel + 5f;

        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, fence, tile));
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
        // Slice 12 retruth: the campfire is a footprint BLOCK now — the
        // non-block fixture is the chest.
        var prop = h.Place("chest", 4, 4);
        float tile = Constants.SeaLevel + 5f;
        Assert.Equal(tile, BlockStack.RenderBase(h.World, h.Buildings, prop, tile));
        // chest has no stack height: StructureDef non-null, IsBlock false.
        Assert.True(prop.StructureDef?.IsBlock != true);
    }

    [Fact]
    public void RenderTop_Delegates_IE_BasePlusOwnStack()
    {
        var h = new Harness();
        h.PlaceBrick(4, 4, 2, 2);
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
    public void StructureHeight_Brick_IsSixteenWorldPx_TexelExact()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var brick = new Structure
        {
            StructureId = "mound_block",
            StructureDef = registry.GetStructure("mound_block"),
        };
        // 1.25 units × 12.8 px/unit (ZScale 8 × TerrainHeightScale 1.6) = the
        // 16px sprite 1:1 — the fixed-size brick law.
        Assert.Equal(1.25f * Constants.ZScale * Constants.TerrainHeightScale,
            SpriteRenderer.StructureHeightWorld(brick));
    }

    [Fact]
    public void StructureHeight_NonBlock_KeepsLegacy32()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        // Slice 12 retruth: the campfire is a footprint BLOCK now — the
        // non-block fixture is the chest.
        var prop = new Structure
        {
            StructureId = "chest",
            StructureDef = registry.GetStructure("chest"),
        };
        Assert.Equal(32f, SpriteRenderer.StructureHeightWorld(prop));
    }
}
