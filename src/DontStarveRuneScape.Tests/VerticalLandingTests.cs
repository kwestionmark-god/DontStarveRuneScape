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
/// Vertical building, slice 2 (spec:
/// docs/superpowers/specs/2026-10-11-vertical-building-jump-landing-design.md):
/// blocks are walls a jump climbs onto. Standing elevation = tile center
/// elevation + summed stack heights of the tile's built blocks; walking into
/// a taller stack refuses, a hop of up to BlockClimbCap levels lands on top,
/// descents are always free; blocks and the player render at the true top.
/// </summary>
public class VerticalLandingTests
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
            // RED carries both wallets: the stub still charges the legacy
            // stone/stick materials until GREEN flips the def to dirt_brick.
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("dirt_brick", 64);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        /// <summary>Build a brick column at a sub-cell of (tx,ty); must succeed.
        /// (Campfire tiles refuse bricks — the slice-1 occupancy gate.)</summary>
        public void BuildBrickColumnAt(int tx, int ty, int subX, int subY, int bricks = 1)
        {
            for (int i = 0; i < bricks; i++)
            {
                var placed = Buildings.PlaceStructure("mound_block", tx, ty, subX, subY,
                    World, Inventory, Skills);
                Assert.True(placed.Success, placed.Message);
            }
        }

        /// <summary>Center world px of a tile's sub-cell (tile 4 sub 2 spans
        /// [288, 304) — the old 4.5*64 sample sits on its left edge).</summary>
        public static float SubCenter(int tile, int sub) => tile * 64 + sub * 16 + 8f;
    }

    // ── 1. Standing query ───────────────────────────────────────────────────

    [Fact]
    public void Standing_IgnoresBlocks_TerrainOnly()
    {
        var h = new Harness();
        // RED stub returns bare terrain: 16.5 everywhere.
        float standing = BlockStack.StandingElevationAt(h.World, h.Buildings, 4.5f * 64, 4.5f * 64);
        Assert.Equal(Constants.SeaLevel + 5f, standing);
    }

    [Fact]
    public void Standing_OnBrickColumn_IsTerrainPlusCourses_PerSubCell()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 2);
        // Two courses of 1.25: 16.5 + 2.5 — ON the column's sub-cell only.
        float onColumn = BlockStack.StandingElevationAt(h.World, h.Buildings,
            Harness.SubCenter(4, 2), Harness.SubCenter(4, 2));
        Assert.Equal(Constants.SeaLevel + 5f + 2.5f, onColumn);
        // The same tile's open sub-cell is untouched ground.
        Assert.Equal(Constants.SeaLevel + 5f,
            BlockStack.StandingElevationAt(h.World, h.Buildings,
                Harness.SubCenter(4, 0), Harness.SubCenter(4, 0)));
    }

    [Fact]
    public void Standing_ConstructionSite_IsNotSolid()
    {
        var h = new Harness();
        var def = h.Buildings.Registry!.GetStructure("mound_block")!;
        h.Buildings.Structures.Add(new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 4, TileY = 4,
            WorldX = 4.5f * 64, WorldY = 4.5f * 64,
            IsUnderConstruction = true,
        });
        Assert.Equal(Constants.SeaLevel + 5f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, 4.5f * 64, 4.5f * 64));
    }

    [Fact]
    public void Standing_NonBlockStructure_AddsNothing()
    {
        var h = new Harness();
        var placed = h.Buildings.PlaceStructure("campfire", 4, 4, h.World, h.Inventory, h.Skills);
        Assert.True(placed.Success, placed.Message);
        Assert.Equal(Constants.SeaLevel + 5f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, 4.5f * 64, 4.5f * 64));
    }

    // ── 2. Movement: blocks are walls, jumps climb ──────────────────────────

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

    [Fact]
    public void Walk_IntoBrickColumn_IsBlocked()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 2);
        // Walking east along the sub 2 row: the column blocks at its edge
        // (288 = sub-cell (2,2)'s left boundary), not at the tile boundary.
        var p = PlayerAt(3.9f * 64, Harness.SubCenter(4, 2));

        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX < 4f * 64 + 2 * 16,
            $"walked into the brick column (WorldX {p.WorldX}): wall rule failed");
    }

    [Fact]
    public void Walk_IntoSingleBrick_IsStillAWallOnFoot()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 1);
        var p = PlayerAt(3.9f * 64, Harness.SubCenter(4, 2));

        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX < 4f * 64 + 2 * 16,
            $"walked onto a single brick (WorldX {p.WorldX}): grounded wall rule failed");
    }

    [Fact]
    public void Jump_Hop_OntoSingleBrick_IsAllowedWhenWithinCap()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 1); // 1.25 <= 2.05: a hop
        // Start inside the tile's open sub-cells (276 + 37.5 = 313.5 — one
        // tick clears the column's 288-304 span).
        var p = PlayerAt(4f * 64 + 20f, Harness.SubCenter(4, 2));

        Assert.True(p.TryJump()); // stamina full; starts the hop
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX >= 4f * 64 + 2 * 16,
            $"jump over/onto the single brick should advance (WorldX {p.WorldX})");
    }

    [Fact]
    public void Jump_TooTall_IsRefused()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 4); // 5.0 > BlockClimbCap
        var p = PlayerAt(4f * 64 + 20f, Harness.SubCenter(4, 2));

        Assert.True(p.TryJump());
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX < 4f * 64 + 2 * 16,
            $"hop climbed past the cap (WorldX {p.WorldX}): the wall is too tall");
    }

    [Fact]
    public void StepDown_FromBrickTop_IsAlwaysAllowed()
    {
        var h = new Harness();
        h.BuildBrickColumnAt(4, 4, 2, 2, bricks: 1);
        // Start on top of the brick; walking DOWN to lower ground must pass.
        var p = PlayerAt(Harness.SubCenter(4, 2), Harness.SubCenter(4, 2));
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX != Harness.SubCenter(4, 2), "descent is always free — no gate on the way down");
    }

    [Fact]
    public void Walk_PureTerrainSlope_StaysLegacyFree()
    {
        var h = new Harness();
        // A 3-level terrain step, no blocks: historical movement is unchanged.
        h.World.SetElevationOverride(4, 4, Constants.SeaLevel + 8f);
        var p = PlayerAt(3.9f * 64, 4.5f * 64);

        for (int i = 0; i < 3; i++)
            p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX >= 4f * 64,
            $"legacy terrain ascent blocked (WorldX {p.WorldX}) — the wall rule must only bite on blocks");
    }

    // ── 3. Render lift math (pixel diff is slice-4 debt, declared) ──────────

    [Fact]
    public void RenderTop_Brick_LiftsByBrickHeight()
    {
        var h = new Harness();
        var def = h.Buildings.Registry!.GetStructure("mound_block")!;
        var s = new Structure { StructureId = def.Id, StructureDef = def, TileX = 4, TileY = 4 };
        float tileElev = Constants.SeaLevel + 5f;
        Assert.Equal(tileElev + 1.25f, BlockStack.RenderTop(tileElev, s));
    }

    [Fact]
    public void RenderTop_NonBlock_StaysAtTileElevation()
    {
        var h = new Harness();
        var def = h.Buildings.Registry!.GetStructure("campfire")!;
        var s = new Structure { StructureId = def.Id, StructureDef = def, TileX = 4, TileY = 4 };
        float tileElev = Constants.SeaLevel + 5f;
        Assert.Equal(tileElev, BlockStack.RenderTop(tileElev, s));
    }
}
