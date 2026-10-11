namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 3 (spec:
/// docs/superpowers/specs/2026-10-11-vertical-building-stacking-design.md):
/// blocks stack into per-tile columns up to BlockColumnMaxHeight; the new
/// 1-unit fence_block is a half-hop; fences connect to neighbors through a
/// pure mask seam; stacked blocks draw at their cumulative top.
/// </summary>
public class VerticalStackingTests
{
    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f; // 16.5
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

        public (bool Success, string Message) Place(string id, int tx, int ty)
            => Buildings.PlaceStructure(id, tx, ty, World, Inventory, Skills);

        public int StructuresOnTile(int tx, int ty)
            => Buildings.Structures.Count(s => s.IsActive && s.TileX == tx && s.TileY == ty);
    }

    private static Player PlayerAt(float wx, float wy)
        => new Player(wx, wy)
        {
            Inventory = new Inv(),
            SkillManager = new SkillManager(),
            Survival = new Survival.SurvivalSystem(),
            Gear = new PlayerGear(),
            ActionSystem = new ActionSystem(),
        };

    private static readonly InputState WalkRight = new() { MoveRight = true };

    // ── 1. Data laws ────────────────────────────────────────────────────────

    [Fact]
    public void FenceDef_ParsesAsOneUnitBlock()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var fence = registry.GetStructure("fence_block");
        Assert.NotNull(fence);
        Assert.True(fence!.IsBlock);
        Assert.Equal(1f, fence.StackHeight);
        Assert.True(fence.OccupiesTile);
        Assert.Equal(1, fence.RequiresSkillLevel);
    }

    // ── 2. Stacking placement ───────────────────────────────────────────────

    [Fact]
    public void Stack_SecondMound_OnOwnBlockTile_Succeeds()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        var second = h.Place("mound_block", 4, 4);

        Assert.True(second.Success, second.Message);
        Assert.Equal(2, h.StructuresOnTile(4, 4));
        Assert.Equal(26, h.Inventory.GetItemQuantity("stone")); // paid twice
    }

    [Fact]
    public void Stack_ThirdMound_RefusesAtColumnBudget()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);

        var third = h.Place("mound_block", 4, 4);

        Assert.False(third.Success);
        Assert.Equal("This block column is already at full height.", third.Message);
        // Refusal consumed nothing, occupied nothing more.
        Assert.Equal(26, h.Inventory.GetItemQuantity("stone"));
        Assert.Equal(2, h.StructuresOnTile(4, 4));
    }

    [Fact]
    public void Stack_MixedColumn_ReachesBudgetExactly()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);   // 2
        Assert.True(h.Place("fence_block", 4, 4).Success);   // 3
        Assert.True(h.Place("fence_block", 4, 4).Success);   // 4 = budget

        var over = h.Place("fence_block", 4, 4);
        Assert.False(over.Success);
        Assert.Equal("This block column is already at full height.", over.Message);
        Assert.Equal(3, h.StructuresOnTile(4, 4));
    }

    [Fact]
    public void Stack_TileWithNonBlock_KeepsLegacyRefusal()
    {
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);

        var second = h.Place("mound_block", 4, 4);

        Assert.False(second.Success);
        Assert.Equal("That tile already has a structure.", second.Message);
    }

    [Fact]
    public void Stack_SecondBlock_StillNeedsFlatPad()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        // Slope the pad AFTER the first build: the second placement re-checks.
        h.World.Tiles[5, 4].Elevation = Constants.SeaLevel + 6f;
        h.World.Tiles[5, 5].Elevation = Constants.SeaLevel + 6f;
        h.World.Tiles[4, 5].Elevation = Constants.SeaLevel + 6f;
        h.World.BuildCornerElevations();

        var second = h.Place("mound_block", 4, 4);

        Assert.False(second.Success);
        Assert.Equal("Needs flat ground — level this tile with a shovel.", second.Message);
    }

    // ── 3. Column truth: standing + movement ────────────────────────────────

    [Fact]
    public void Standing_OnTwoMoundColumn_IsTerrainPlus4()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);

        Assert.Equal(Constants.SeaLevel + 5f + 4f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, 4.5f * 64, 4.5f * 64));
    }

    [Fact]
    public void ColumnFour_IsWall_GroundedAndAirborne()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);

        // Grounded: the wall refuses.
        var p = PlayerAt(3.9f * 64, 4.5f * 64);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX < 4f * 64,
            $"walked onto a 4-high column (WorldX {p.WorldX}): wall rule failed");

        // Airborne: 4 > BlockClimbCap (2.05), so even a jump can't climb it.
        var p2 = PlayerAt(3.9f * 64, 4.5f * 64);
        Assert.True(p2.TryJump());
        p2.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p2.WorldX < 4f * 64,
            $"jump climbed a 4-high column (WorldX {p2.WorldX}): beyond the cap");
    }

    [Fact]
    public void StepDown_FromColumnFour_IsAlwaysAllowed()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);

        var p = PlayerAt(4.5f * 64, 4.5f * 64);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX != 4.5f * 64, "descent off a column is always free");
    }

    [Fact]
    public void Fence_IsHalfHop_AirborneLandsGroundedRefuses()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);

        // Grounded: a fence is still a wall (1 > 0 delta on foot).
        var p = PlayerAt(3.9f * 64, 4.5f * 64);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX < 4f * 64,
            $"walked onto a fence (WorldX {p.WorldX}): grounded wall rule failed");

        // Airborne: 1 ≤ 2.05 — the half-hop lands on the fence top.
        var p2 = PlayerAt(3.9f * 64, 4.5f * 64);
        Assert.True(p2.TryJump());
        p2.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p2.WorldX >= 4f * 64,
            $"half-hop onto a fence refused (WorldX {p2.WorldX}): hop should land");
        Assert.Equal(Constants.SeaLevel + 5f + 1f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, p2.WorldX, p2.WorldY));
    }

    // ── 4. Fence connection mask ────────────────────────────────────────────

    [Fact]
    public void ConnectionMask_LoneFence_IsZero()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.Equal(0, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void ConnectionMask_FenceEastOfMound_HasEastBit()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 5, 4).Success);
        // N=1, E=2, S=4, W=8 — fences connect to ANY block, not just fences.
        Assert.Equal(2, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void ConnectionMask_FullCross_Is15()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 3).Success); // N
        Assert.True(h.Place("mound_block", 5, 4).Success); // E
        Assert.True(h.Place("fence_block", 4, 5).Success); // S
        Assert.True(h.Place("fence_block", 3, 4).Success); // W
        Assert.Equal(15, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void ConnectionMask_SiteNeighbor_SetsNoBit()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        var def = h.Buildings.Registry!.GetStructure("mound_block")!;
        h.Buildings.Structures.Add(new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 5, TileY = 4,
            WorldX = 5.5f * 64, WorldY = 4.5f * 64,
            IsUnderConstruction = true,
        });
        // Sites are not solid (slice-2 contract): no connection to a site.
        Assert.Equal(0, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    // ── 5. Cumulative render top ────────────────────────────────────────────

    [Fact]
    public void RenderTop_SecondMound_DrawsAtCumulativeTop()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);
        float tileElev = Constants.SeaLevel + 5f;
        var column = h.Buildings.Structures.Where(s => s.TileX == 4 && s.TileY == 4).ToList();
        Assert.Equal(2, column.Count);

        // First block draws at tile + 2; the stacked block rides on its top: tile + 2 + 2.
        Assert.Equal(tileElev + 2f, BlockStack.RenderTop(h.World, h.Buildings, column[0], tileElev));
        Assert.Equal(tileElev + 4f, BlockStack.RenderTop(h.World, h.Buildings, column[1], tileElev));
    }

    [Fact]
    public void RenderTop_FenceAtopMound_DrawsAtTilePlus3()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("fence_block", 4, 4).Success);
        float tileElev = Constants.SeaLevel + 5f;
        var fence = h.Buildings.Structures.Single(s => s.StructureId == "fence_block");

        Assert.Equal(tileElev + 3f, BlockStack.RenderTop(h.World, h.Buildings, fence, tileElev));
    }

    // ── 6. Persistence: columns ride the existing structure snapshot ────────

    [Fact]
    public void Snapshot_StackedColumn_RoundTrips()
    {
        var h = new Harness();
        Assert.True(h.Place("mound_block", 4, 4).Success);
        Assert.True(h.Place("mound_block", 4, 4).Success);

        var snapshot = h.Buildings.GetSnapshot();
        var restored = new BuildingSystem { Registry = h.Buildings.Registry };
        restored.RestoreSnapshot(snapshot, h.World);

        Assert.Equal(2, restored.Structures.Count(s => s.TileX == 4 && s.TileY == 4));
        Assert.Equal(4f, BlockStack.BlockHeightAt(h.World, restored, 4, 4));
    }
}
