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
            Inventory.AddItem("stone", 30);
            Inventory.AddItem("stick", 30);
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        /// <summary>Build a mound at (tx,ty) on the flat pad; must succeed.
        /// (Struct-occupied tiles refuse a second build, slice 1.)</summary>
        public void BuildMoundAt(int tx, int ty)
        {
            var placed = Buildings.PlaceStructure("mound_block", tx, ty, World, Inventory, Skills);
            Assert.True(placed.Success, placed.Message);
        }
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
    public void Standing_OnBuiltMound_IsTerrainPlusStackHeight()
    {
        var h = new Harness();
        h.BuildMoundAt(4, 4);
        // mound stack_height = 2 levels (slice 2 data change): 16.5 + 2 = 18.5.
        float standing = BlockStack.StandingElevationAt(h.World, h.Buildings, 4.5f * 64, 4.5f * 64);
        Assert.Equal(Constants.SeaLevel + 5f + 2f, standing);
        // The neighbor tile is untouched.
        Assert.Equal(Constants.SeaLevel + 5f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, 3.5f * 64, 4.5f * 64));
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
    public void Walk_IntoMound_IsBlocked()
    {
        var h = new Harness();
        h.BuildMoundAt(4, 4);
        var p = PlayerAt(3.9f * 64, 4.5f * 64); // 249.6px — one tick crosses 256

        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX < 4f * 64,
            $"walked onto the mound tile (WorldX {p.WorldX}): wall rule failed");
    }

    [Fact]
    public void Jump_Hop_OntoMound_IsAllowedWhenWithinCap()
    {
        var h = new Harness();
        h.BuildMoundAt(4, 4);
        var p = PlayerAt(3.9f * 64, 4.5f * 64);

        Assert.True(p.TryJump()); // stamina full; starts the hop
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX >= 4f * 64,
            $"jump-climb onto the mound should advance (WorldX {p.WorldX})");
        // The standing query now puts the player on the mound top.
        Assert.Equal(Constants.SeaLevel + 5f + 2f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, p.WorldX, p.WorldY));
    }

    [Fact]
    public void Jump_TooTall_IsRefused()
    {
        var h = new Harness();
        h.BuildMoundAt(4, 4);
        // Raise the mound's terrain by 3 levels after building: top becomes
        // 21.5 vs. standing 16.5 — a 5-level delta, beyond the climb cap.
        h.World.SetElevationOverride(4, 4, Constants.SeaLevel + 8f);
        var p = PlayerAt(3.9f * 64, 4.5f * 64);

        Assert.True(p.TryJump());
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);

        Assert.True(p.WorldX < 4f * 64,
            $"hop climbed past the cap (WorldX {p.WorldX}): the wall is too tall");
    }

    [Fact]
    public void StepDown_FromMound_IsAlwaysAllowed()
    {
        var h = new Harness();
        h.BuildMoundAt(4, 4);
        // Start on top of the mound; walking DOWN to lower ground must pass.
        var p = PlayerAt(4.5f * 64, 4.5f * 64);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX != 4.5f * 64, "descent is always free — no gate on the way down");
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
    public void RenderTop_Block_LiftsByStackHeight()
    {
        var h = new Harness();
        var def = h.Buildings.Registry!.GetStructure("mound_block")!;
        var s = new Structure { StructureId = def.Id, StructureDef = def, TileX = 4, TileY = 4 };
        float tileElev = Constants.SeaLevel + 5f;
        Assert.Equal(tileElev + 2f, BlockStack.RenderTop(tileElev, s));
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
