namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 7a (spec:
/// docs/superpowers/specs/2026-10-11-block-voxel-bricks-design.md): the
/// building grid granulates to 4x4 sub-cells of 16 world px per 64 px tile.
/// mound_block becomes a fixed 16 px brick (stack_height 1.25 — 16 px at
/// 12.8 px per elevation unit, texel-exact with the 16x16 sprite) placed at
/// a sub-cell; repeated placement at the same sub-cell stacks a column
/// within BlockColumnMaxHeight (5.0 = exactly 4 bricks). Placement consumes
/// the crafted dirt_brick item. Standing and stepping resolve per sub-cell;
/// fences stay tile-level and keep their slice-3/4 contracts.
///
/// Sub-cell (sx, sy) of tile (tx, ty) spans world px
/// [tx*64 + sx*16, +16) x [ty*64 + sy*16, +16).
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

        public (bool Success, string Message) PlaceBrick(int tx, int ty, int subX, int subY)
            => Buildings.PlaceStructure("mound_block", tx, ty, subX, subY,
                World, Inventory, Skills);

        public (bool Success, string Message) Place(string id, int tx, int ty)
            => Buildings.PlaceStructure(id, tx, ty, World, Inventory, Skills);

        public int BricksAt(int tx, int ty, int subX, int subY)
            => Buildings.Structures.Count(s => s.IsActive && s.TileX == tx && s.TileY == ty
                && s.SubX == subX && s.SubY == subY);
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

    /// <summary>Center world px of a sub-cell (tile 4 defaults keep the old
    /// 4.5*64 sample inside sub-cell (2,2) of the tile).</summary>
    private static float SubCenter(int tile, int sub) => tile * 64 + sub * 16 + 8f;

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

    [Fact]
    public void BrickDef_IsAFixedSixteenPxUnit()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var brick = registry.GetStructure("mound_block");
        Assert.NotNull(brick);
        Assert.True(brick!.IsBlock);
        // 16 px / 12.8 px-per-unit = 1.25 exactly — the texel-exact brick.
        Assert.Equal(1.25f, brick.StackHeight);
        Assert.Equal("Dirt Brick", brick.Name);
        Assert.True(brick.OccupiesTile); // tile.Structure gates flatten/E-use
        var material = Assert.Single(brick.Materials);
        Assert.Equal("dirt_brick", material.ItemId);
        Assert.Equal(1, material.Quantity);
    }

    [Fact]
    public void BrickRecipe_CraftsFromStoneAndStick()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();

        var recipe = recipes.GetRecipe("craft_dirt_brick");
        Assert.NotNull(recipe);
        // Net economy unchanged from the old mound: 2 stone + 1 stick.
        Assert.Equal("dirt_brick", recipe!.OutputItem);
        Assert.Equal(1, recipe.OutputQuantity);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "stone" && i.Quantity == 2);
        Assert.Contains(recipe.Inputs, i => i.ItemId == "stick" && i.Quantity == 1);
    }

    [Fact]
    public void BrickItem_DefExists()
    {
        var items = DataLoader.LoadJsonList<ItemDef>(Constants.ItemsFile, "items");
        var brick = items.FirstOrDefault(i => i.Id == "dirt_brick");
        Assert.NotNull(brick);
    }

    // ── 2. Sub-cell placement ───────────────────────────────────────────────

    [Fact]
    public void Brick_PlacesAtSubCell_ConsumesOneBrickItem()
    {
        var h = new Harness();

        var placed = h.PlaceBrick(4, 4, 2, 2);

        Assert.True(placed.Success, placed.Message);
        Assert.Equal(63, h.Inventory.GetItemQuantity("dirt_brick"));
        var brick = Assert.Single(h.Buildings.Structures);
        Assert.Equal(4, brick.TileX);
        Assert.Equal(4, brick.TileY);
        Assert.Equal(2, brick.SubX);
        Assert.Equal(2, brick.SubY);
        Assert.Equal(0, brick.Layer);
    }

    [Fact]
    public void Brick_SecondAtSameSubCell_StacksToLayer1()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);

        var second = h.PlaceBrick(4, 4, 2, 2);

        Assert.True(second.Success, second.Message);
        Assert.Equal(2, h.BricksAt(4, 4, 2, 2));
        var top = h.Buildings.Structures.Single(s => s.Layer > 0);
        Assert.Equal(1, top.Layer);
        Assert.Equal(2.5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 2));
    }

    [Fact]
    public void Brick_AtDifferentSubCell_SitsSideBySide()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 4, 3, 2).Success);

        // Two neighbors, both layer 0 — a side-by-side join, not a stack.
        Assert.Equal(2, h.Buildings.Structures.Count(s => s.Layer == 0));
        Assert.Equal(1.25f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 2));
        Assert.Equal(1.25f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 3, 2));
        // Tile height is the MAX over sub-cells, never the sum.
        Assert.Equal(1.25f, BlockStack.BlockHeightAt(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void Brick_FourHigh_Okay_FifthRefusesAtBudget()
    {
        var h = new Harness();
        for (int i = 0; i < 4; i++)
            Assert.True(h.PlaceBrick(4, 4, 2, 2).Success, $"brick {i} failed");

        Assert.Equal(4, h.BricksAt(4, 4, 2, 2));
        Assert.Equal(5f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 2, 2));

        var fifth = h.PlaceBrick(4, 4, 2, 2);
        Assert.False(fifth.Success);
        Assert.Equal("This block column is already at full height.", fifth.Message);
        // Refusal consumed nothing and placed nothing.
        Assert.Equal(60, h.Inventory.GetItemQuantity("dirt_brick"));
        Assert.Equal(4, h.BricksAt(4, 4, 2, 2));
    }

    [Fact]
    public void Brick_TileLevelCall_IsRefused()
    {
        var h = new Harness();
        var legacy = h.Place("mound_block", 4, 4);
        Assert.False(legacy.Success);
        Assert.Equal("Pick a spot on the brick grid.", legacy.Message);
    }

    [Fact]
    public void Brick_TileWithCampfire_KeepsLegacyRefusal()
    {
        var h = new Harness();
        Assert.True(h.Place("campfire", 4, 4).Success);

        var brick = h.PlaceBrick(4, 4, 2, 2);

        Assert.False(brick.Success);
        Assert.Equal("That tile already has a structure.", brick.Message);
    }

    [Fact]
    public void Brick_SecondPlacement_StillNeedsFlatPad()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        // Slope the pad AFTER the first brick: the next placement re-checks.
        h.World.Tiles[5, 4].Elevation = Constants.SeaLevel + 6f;
        h.World.Tiles[5, 5].Elevation = Constants.SeaLevel + 6f;
        h.World.Tiles[4, 5].Elevation = Constants.SeaLevel + 6f;
        h.World.BuildCornerElevations();

        var second = h.PlaceBrick(4, 4, 3, 2);

        Assert.False(second.Success);
        Assert.Equal("Needs flat ground — level this tile with a shovel.", second.Message);
    }

    [Fact]
    public void Brick_InsideColony_BypassesTheSiteFlow()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var buildings = new BuildingSystem { Registry = registry };
        var world = new TileMap(12, 12);
        for (int x = 0; x < 12; x++)
            for (int y = 0; y < 12; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        world.BuildCornerElevations();
        var colony = new ColonySystem();
        Assert.True(colony.FoundAt(4.5f * Constants.TileSize, 4.5f * Constants.TileSize, world));
        var inventory = new Inv();
        inventory.AddItem("dirt_brick", 8);

        // Shipped default: bricks are player-built on the spot — a 20-second
        // worker site per 16 px brick would be absurd. Inside the colony the
        // brick still builds directly and consumes its item.
        var placed = buildings.PlaceStructure("mound_block", 5, 4, 1, 2,
            world, inventory, new SkillManager(), colony);

        Assert.True(placed.Success, placed.Message);
        var brick = Assert.Single(buildings.Structures);
        Assert.False(brick.IsUnderConstruction);
        Assert.Equal(7, inventory.GetItemQuantity("dirt_brick"));
    }

    // ── 3. Per-sub-cell standing + movement truth ────────────────────────────

    [Fact]
    public void Standing_ResolvesPerSubCell_OnAndOffTheColumn()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success); // layer 1 → 2.5

        float onColumn = BlockStack.StandingElevationAt(
            h.World, h.Buildings, SubCenter(4, 2), SubCenter(4, 2));
        float besideIt = BlockStack.StandingElevationAt(
            h.World, h.Buildings, SubCenter(4, 0), SubCenter(4, 0));
        Assert.Equal(Constants.SeaLevel + 5f + 2.5f, onColumn);
        Assert.Equal(Constants.SeaLevel + 5f, besideIt);
    }

    [Fact]
    public void Step_IntoBrickColumn_GroundedRefused_AirborneHopsOneBrick()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success); // one brick = 1.25

        // Direct seam: grounded into the column sub-cell is a wall.
        Assert.False(BlockStack.CanStep(h.World, h.Buildings,
            SubCenter(4, 1), SubCenter(4, 2), SubCenter(4, 1), SubCenter(4, 2), false));
        // Airborne: 1.25 <= BlockClimbCap — the hop lands.
        Assert.True(BlockStack.CanStep(h.World, h.Buildings,
            SubCenter(4, 1), SubCenter(4, 2), SubCenter(4, 1), SubCenter(4, 2), true));

        // Player-level: walking into the column's left edge stops there.
        var p = PlayerAt(SubCenter(4, 0), SubCenter(4, 2));
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX < 4 * 64 + 2 * 16,
            $"walked into the brick column (WorldX {p.WorldX}): wall rule failed");
    }

    [Fact]
    public void Step_TwoBrickColumn_WallEvenAirborne()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success); // 2.5 > BlockClimbCap

        Assert.False(BlockStack.CanStep(h.World, h.Buildings,
            SubCenter(4, 1), SubCenter(4, 2), SubCenter(4, 1), SubCenter(4, 2), true));
    }

    [Fact]
    public void StepDown_FromBrickTop_IsAlwaysAllowed()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);

        var p = PlayerAt(SubCenter(4, 2), SubCenter(4, 2));
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX != SubCenter(4, 2), "descent off a brick is always free");
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
            TileX = 4, TileY = 4, SubX = 2, SubY = 2,
            WorldX = SubCenter(4, 2), WorldY = SubCenter(4, 2),
            IsUnderConstruction = true,
        });
        Assert.Equal(Constants.SeaLevel + 5f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, SubCenter(4, 2), SubCenter(4, 2)));
    }

    // ── 4. Fence contracts survive the granulation (tile-level) ─────────────

    [Fact]
    public void Fence_IsHalfHop_AirborneLandsGroundedRefuses()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);

        var p = PlayerAt(3.9f * 64, 4.5f * 64);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        p.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p.WorldX < 4f * 64,
            $"walked onto a fence (WorldX {p.WorldX}): grounded wall rule failed");

        var p2 = PlayerAt(3.9f * 64, 4.5f * 64);
        Assert.True(p2.TryJump());
        p2.ApplyKeyInput(WalkRight, 0.25f, 0f, h.World, h.Buildings);
        Assert.True(p2.WorldX >= 4f * 64,
            $"half-hop onto a fence refused (WorldX {p2.WorldX}): hop should land");
        Assert.Equal(Constants.SeaLevel + 5f + 1f,
            BlockStack.StandingElevationAt(h.World, h.Buildings, p2.WorldX, p2.WorldY));
    }

    [Fact]
    public void ConnectionMask_LoneFence_IsZero()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.Equal(0, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void ConnectionMask_BricksEastOfFence_HasEastBit()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.True(h.PlaceBrick(5, 4, 1, 1).Success);
        // N=1, E=2, S=4, W=8 — fences connect to ANY block, bricks included.
        Assert.Equal(2, BlockStack.ConnectionMask(h.World, h.Buildings, 4, 4));
    }

    [Fact]
    public void ConnectionMask_FullCross_Is15()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        Assert.True(h.PlaceBrick(4, 3, 1, 1).Success); // N
        Assert.True(h.PlaceBrick(5, 4, 1, 1).Success); // E
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

    // ── 5. Layer-based render tops (order-independent) ──────────────────────

    [Fact]
    public void RenderTop_LayeredBricks_OrderIndependent()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);   // layer 0
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);   // layer 1
        float tileElev = Constants.SeaLevel + 5f;
        var column = h.Buildings.Structures.Where(s => s.SubX == 2 && s.SubY == 2).ToList();
        Assert.Equal(2, column.Count);

        Assert.Equal(tileElev,
            BlockStack.RenderBase(h.World, h.Buildings, column[0], tileElev));
        Assert.Equal(tileElev + 1.25f,
            BlockStack.RenderBase(h.World, h.Buildings, column[1], tileElev));
        Assert.Equal(tileElev + 2.5f,
            BlockStack.RenderTop(h.World, h.Buildings, column[1], tileElev));

        // The stored Layer makes the draw base list-order-independent.
        h.Buildings.Structures.Reverse();
        Assert.Equal(tileElev,
            BlockStack.RenderBase(h.World, h.Buildings, column[1], tileElev));
        Assert.Equal(tileElev + 1.25f,
            BlockStack.RenderBase(h.World, h.Buildings, column[0], tileElev));
    }

    [Fact]
    public void RenderBase_FenceOnBrickTile_IsStillGround()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        // All-blocks partition: a fence may share a brick tile — but it is a
        // tile-level structure and plants on the ground, not on brick tops.
        Assert.True(h.Place("fence_block", 4, 4).Success);
        float tileElev = Constants.SeaLevel + 5f;
        var fence = h.Buildings.Structures.Single(s => s.StructureId == "fence_block");
        Assert.Equal(tileElev, BlockStack.RenderBase(h.World, h.Buildings, fence, tileElev));
    }

    // ── 6. Persistence: sub-cells ride the structure snapshot ───────────────

    [Fact]
    public void Snapshot_BrickColumn_RoundTripsSubCell()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 4, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 4, 3, 2).Success);

        var snapshot = h.Buildings.GetSnapshot();
        var restored = new BuildingSystem { Registry = h.Buildings.Registry };
        restored.RestoreSnapshot(snapshot, h.World);

        Assert.Equal(2, restored.Structures.Count(s =>
            s.SubX == 2 && s.SubY == 2 && s.TileX == 4 && s.TileY == 4));
        Assert.Equal(2.5f, BlockStack.SubHeightAt(h.World, restored, 4, 4, 2, 2));
        Assert.Equal(1.25f, BlockStack.SubHeightAt(h.World, restored, 4, 4, 3, 2));
        // Sub-cell placement still works after restore (Layer derives from
        // the restored column, not from list accident).
        var inv = new Inv();
        inv.AddItem("dirt_brick", 1);
        var stack = restored.PlaceStructure("mound_block", 4, 4, 3, 2,
            h.World, inv, new SkillManager());
        Assert.True(stack.Success, stack.Message);
    }

    [Fact]
    public void Snapshot_LegacyTileLevelMound_MigratesToBrickBlock()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var buildings = new BuildingSystem { Registry = registry };
        var world = new TileMap(8, 8);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        world.BuildCornerElevations();

        // A pre-brick save: a tile-level mound with no sub-cell (SubX -1).
        var snapshot = new BuildingSnapshot
        {
            Structures =
            [
                new StructureSnapshot
                {
                    StructureId = "mound_block",
                    TileX = 4, TileY = 4,
                    Hp = 20,
                    WorkStatus = "Idle",
                },
            ],
        };

        buildings.RestoreSnapshot(snapshot, world);

        // The legacy mound materializes as a full 4x4 x 2-course brick block:
        // same tile footprint, same wall read (2 courses = 2.5 units).
        Assert.Equal(32, buildings.Structures.Count);
        for (int sx = 0; sx < 4; sx++)
            for (int sy = 0; sy < 4; sy++)
            {
                Assert.Equal(2, buildings.Structures.Count(s =>
                    s.SubX == sx && s.SubY == sy));
                Assert.Equal(2.5f,
                    BlockStack.SubHeightAt(world, buildings, 4, 4, sx, sy));
            }
    }
}
