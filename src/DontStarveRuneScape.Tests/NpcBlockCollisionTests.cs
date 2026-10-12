namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Vertical building, slice 9 (spec:
/// docs/superpowers/specs/2026-10-12-npc-block-collision-standing-design.md):
/// blocks are walls for every grounded mover. The player's on-foot rule
/// (a grounded step into a taller stack is a wall; mounting needs a jump)
/// extends to monsters, workers and pets: BlockStack.GroundedSlide gates
/// each axis of a beeline step (a blocked axis slides along the wall),
/// pathing occupancy goes LIVE (a removed brick frees its tile — today
/// the def slot stays set forever), and cave worlds ignore the one
/// surface BuildingSystem (pairing the cave map with surface structures
/// invents phantom walls at overlapping tile coords).
/// </summary>
public class NpcBlockCollisionTests
{
    private sealed class Harness
    {
        public TileMap World = new TileMap(8, 8);
        public BuildingSystem Buildings = MakeBuildings();
        public SkillManager Skills = new();
        public Inv Inventory = new();
        public CombatSystem Combat = new();

        public Harness()
        {
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f; // 16.5
            World.BuildCornerElevations();
            Inventory.AddItem("dirt_brick", 64);
            Inventory.AddItem("stick", 30);
            Inventory.AddItem("stone", 30);
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

        public Structure? BrickAt(int tx, int ty, int subX, int subY)
            => Buildings.Structures.FirstOrDefault(s => s.IsActive
                && s.TileX == tx && s.TileY == ty
                && s.SubX == subX && s.SubY == subY);
    }

    private static MonsterDef HostileDef() => new()
    {
        MonsterId = "test_wolf",
        Name = "Test Wolf",
        Hp = 10,
        Attack = 3,
        Defence = 2,
        Speed = 40f,
        AggressionRange = 150f,
        FleeRange = 300f,
        AttackCooldown = 0.5f,
        XpReward = 7,
        IsHostile = true,
        SpriteKey = "monster/wolf",
        LootTable = [new MonsterLootEntry { ItemId = "test_pelt", Chance = 1f }],
    };

    private static Player PlayerAt(float wx, float wy)
        => new Player(wx, wy)
        {
            Inventory = new Inv(),
            SkillManager = new SkillManager(),
            Survival = new Survival.SurvivalSystem(),
            Gear = new PlayerGear(),
        };

    private static RecruitNpc PetAt(float wx, float wy) => new()
    {
        NpcId = "test_pet",
        Name = "Test Pet",
        WorldX = wx,
        WorldY = wy,
        IsActive = true,
        Health = 50,
        MaxHealth = 50,
    };

    // ── 1. GroundedSlide (pure contracts) ─────────────────────────────────

    [Fact]
    public void GroundedSlide_FlatGround_AppliesFullStep()
    {
        var h = new Harness();
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings, 100f, 100f, 12f, 5f);
        Assert.Equal(12f, dx, 3);
        Assert.Equal(5f, dy, 3);
    }

    [Fact]
    public void GroundedSlide_FenceAhead_BlocksXKeepsY()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        // Mover west of the fence tile stepping north-east: the X sweep
        // crosses into tile 4 (a 1-unit ascent = a wall on foot), the Y
        // sweep stays on free ground.
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings, 250f, 288f, 12f, 5f);
        Assert.Equal(0f, dx, 3);
        Assert.Equal(5f, dy, 3);
    }

    [Fact]
    public void GroundedSlide_StepParallelToWall_AppliesFully()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        // Moving north along the wall's face: the mover stays west of the
        // fence tile, nothing is crossed, the step applies in full.
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings, 250f, 288f, 0f, -20f);
        Assert.Equal(0f, dx, 3);
        Assert.Equal(-20f, dy, 3);
    }

    [Fact]
    public void GroundedSlide_DescentOutOfColumn_IsFree()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        // The mover is INSIDE the fence tile (walled in / staged there):
        // every way out is a descent — free, so it is never trapped.
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings, 260f, 288f, -12f, 0f);
        Assert.Equal(-12f, dx, 3);
        Assert.Equal(0f, dy, 3);
    }

    [Fact]
    public void GroundedSlide_NullBuildings_AppliesFully()
    {
        var h = new Harness();
        var (dx, dy) = BlockStack.GroundedSlide(h.World, null, 100f, 100f, 12f, 5f);
        Assert.Equal(12f, dx, 3);
        Assert.Equal(5f, dy, 3);
    }

    [Fact]
    public void GroundedSlide_CaveWorld_IgnoresSurfaceBlocks()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 4).Success);
        // One BuildingSystem, surface-only structures: a cave world must
        // not pair with it — surface bricks at cave tile coords would
        // invent phantom walls (and phantom standing lifts).
        h.World.IsCave = true;
        var (dx, dy) = BlockStack.GroundedSlide(h.World, h.Buildings, 250f, 288f, 12f, 5f);
        Assert.Equal(12f, dx, 3);
        Assert.Equal(5f, dy, 3);
    }

    // ── 2. Cave pairing guard ─────────────────────────────────────────────

    [Fact]
    public void SubHeightAt_CaveWorld_ReturnsZeroDespiteSurfaceBlocks()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 0, 0).Success);
        h.World.IsCave = true;
        Assert.Equal(0f, BlockStack.SubHeightAt(h.World, h.Buildings, 4, 4, 0, 0));
        Assert.Equal(0f, BlockStack.BlockHeightAt(h.World, h.Buildings, 4, 4));
    }

    // ── 3. Live occupancy truth ───────────────────────────────────────────

    [Fact]
    public void CanStand_BrickTile_RefusesPathing()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 0, 0).Success);
        Assert.False(WorkerPathfinder.CanStand(h.World, 4, 4));
    }

    [Fact]
    public void CanStand_AfterRemoveStructure_FreedTileIsStandable()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 0, 0).Success);
        var brick = h.BrickAt(4, 4, 0, 0);
        Assert.NotNull(brick);
        Assert.True(h.Buildings.RemoveStructure(brick!, h.World));

        Assert.True(WorkerPathfinder.CanStand(h.World, 4, 4),
            "a removed brick must free its tile for pathing — the def slot goes stale today");
    }

    [Fact]
    public void CanStand_SiblingBrickKeepsTileOccupied()
    {
        var h = new Harness();
        Assert.True(h.PlaceBrick(4, 4, 0, 0).Success);
        Assert.True(h.PlaceBrick(4, 4, 3, 3).Success);
        var brick = h.BrickAt(4, 4, 0, 0);
        Assert.True(h.Buildings.RemoveStructure(brick!, h.World));

        Assert.False(WorkerPathfinder.CanStand(h.World, 4, 4),
            "a remaining brick on the tile keeps it occupied");
    }

    [Fact]
    public void FindPath_AfterBrickWallRemoved_RoutesThroughFreedCorridor()
    {
        var h = new Harness();
        for (int tx = 2; tx <= 6; tx++)
            Assert.True(h.PlaceBrick(tx, 4, 0, 0).Success, $"brick wall at ({tx},4)");

        // Walled: the straight route is blocked, the path detours around.
        var detour = WorkerPathfinder.FindPath(h.World, 4, 2, 4, 6);
        Assert.NotNull(detour);
        Assert.True(!detour!.Contains((4, 4)), "the walled corridor tile is not on the path");

        // Freed: removing the bricks must free the corridor tiles — the
        // stale def slot keeps them pathing-blocked today.
        foreach (var s in h.Buildings.Structures.Where(s => s.TileY == 4).ToList())
            Assert.True(h.Buildings.RemoveStructure(s, h.World));

        var straight = WorkerPathfinder.FindPath(h.World, 4, 2, 4, 6);
        Assert.NotNull(straight);
        Assert.True(straight!.Contains((4, 4)),
            "the freed corridor tile must be pathable again");
    }

    // ── 4. Monster gate ───────────────────────────────────────────────────

    [Fact]
    public void MonsterChase_BlockedByFence_FrozenXSlidesY()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(352f, 300f);
        var monster = h.Combat.SpawnMonster(HostileDef(), 250f, 352f);
        float startY = monster.WorldY;

        h.Combat.Tick(0.25f, player, null, h.World, h.Buildings);

        Assert.True(MathF.Abs(monster.WorldX - 250f) < 0.01f,
            $"the fence wall must stop the chase (WorldX {monster.WorldX})");
        Assert.True(monster.WorldY < startY,
            "the free axis still slides toward the player");
    }

    [Fact]
    public void MonsterChase_NoWall_Control_ClosesDistance()
    {
        var h = new Harness();
        var player = PlayerAt(352f, 300f);
        var monster = h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        h.Combat.Tick(0.25f, player, null, h.World, h.Buildings);

        Assert.True(monster.WorldX > 250f, "no wall: the chase closes in");
    }

    [Fact]
    public void MonsterChase_LegacyTickWithoutWorld_StillPhasesThrough()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(352f, 300f);
        var monster = h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        h.Combat.Tick(0.25f, player); // legacy signature: no world pairing

        Assert.True(monster.WorldX > 250f,
            "legacy callers (no world/buildings) keep today's walk-through");
    }

    [Fact]
    public void MonsterFlee_BlockedByFence_DoesNotRetreatThroughWall()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(500f, 300f);
        var monster = h.Combat.SpawnMonster(HostileDef(), 326f, 352f);
        monster.Health = 1; // low health → flee while the player is close

        h.Combat.Tick(0.25f, player, null, h.World, h.Buildings);

        Assert.True(MathF.Abs(monster.WorldX - 326f) < 0.01f,
            $"fleeing west must stop at the fence (WorldX {monster.WorldX})");
    }

    // ── 5. Pet gate ───────────────────────────────────────────────────────

    [Fact]
    public void PetFollow_BlockedByFence_SlidesAlongWall()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var taming = new TamingSystem { PlayerX = 352f, PlayerY = 300f };
        var pet = PetAt(250f, 352f);
        float startY = pet.WorldY;

        taming.Tick(0.25f, pet, h.World, h.Buildings);

        Assert.True(MathF.Abs(pet.WorldX - 250f) < 0.01f,
            $"the fence must stop the follower (WorldX {pet.WorldX})");
        Assert.True(pet.WorldY < startY, "the free axis slides");
    }

    [Fact]
    public void PetGuardApproach_BlockedByFence_SlidesAlongWall()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var pet = PetAt(250f, 352f);
        pet.IsColonyAssigned = true;
        var combat = new CombatSystem();
        combat.SpawnMonster(HostileDef(), 400f, 300f);
        float startY = pet.WorldY;

        var taming = new TamingSystem();
        taming.TickColonyGuard(0.25f, pet, combat, h.World, h.Buildings);

        Assert.True(MathF.Abs(pet.WorldX - 250f) < 0.01f,
            $"the fence must stop the guard pet (WorldX {pet.WorldX})");
        Assert.True(pet.WorldY < startY, "the free axis slides");
    }
}
