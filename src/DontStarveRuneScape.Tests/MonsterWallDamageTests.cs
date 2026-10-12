namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
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
/// Vertical building, slice 11 (spec:
/// docs/superpowers/specs/2026-10-12-monster-wall-damage-design.md):
/// walls buy time, not invincibility. A monster blocked by a
/// player-built wall while pursuing a target grinds at it; after
/// MonsterWallFrustrationSeconds (4 s) of sustained blocking it chips
/// the nearest built block structure in melee reach at its own attack
/// cadence (its Attack stat), until the wall breaks — then the chase
/// resumes through the gap. Wandering never accumulates. Destruction
/// notifies the player per piece (no silent losses) and frees the tile
/// for pathing immediately (the live occupancy slot).
/// Staging law: the player sits DIRECTLY east of the wall so dy = 0 —
/// no slide escape around a short wall inside the frustration window.
/// </summary>
public class MonsterWallDamageTests
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

        public Structure? FenceAt(int tx, int ty)
            => Buildings.Structures.FirstOrDefault(s => s.IsActive
                && s.TileX == tx && s.TileY == ty && s.SubX < 0);

        /// <summary>Tick combat with the full pairing (slice 9).</summary>
        public void Tick(int count, Player? player, float dt = 0.25f)
        {
            for (int i = 0; i < count; i++)
                Combat.Tick(dt, player, null, World, Buildings);
        }
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
            ActionSystem = new ActionSystem(),
        };

    // ── 1. The full break-through ─────────────────────────────────────────

    [Fact]
    public void FrustratedRaider_BreaksThroughAndNotifies()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(352f, 352f);
        var monster = h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        // 4 s frustration (16 ticks) + 4 hits at 1.5 s cadence (24 ticks)
        // + slack to resume the chase through the gap.
        h.Tick(50, player);

        Assert.True(h.FenceAt(4, 5) == null, "the fence must break under sustained attack");
        Assert.True(WorkerPathfinder.CanStand(h.World, 4, 5),
            "the destroyed wall's tile must free for pathing immediately (live occupancy)");
        player.ActionSystem!.FlushNotifications();
        Assert.Contains(player.ActionSystem.Notifications,
            n => n.Text.Contains("was destroyed"));
        Assert.True(monster.WorldX > 280f,
            $"the chase resumes through the gap (WorldX {monster.WorldX})");
    }

    // ── 2. Frustration is sustained, not instant ───────────────────────────

    [Fact]
    public void BelowFrustrationThreshold_NoDamage()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(352f, 352f);
        h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        h.Tick(8, player); // 2 s of grinding < the 4 s threshold

        var fence = h.FenceAt(4, 5);
        Assert.NotNull(fence);
        Assert.Equal(10, fence!.Health);
        player.ActionSystem!.FlushNotifications();
        Assert.DoesNotContain(player.ActionSystem.Notifications,
            n => n.Text.Contains("was destroyed"));
    }

    // ── 3. Wandering never chips walls ────────────────────────────────────

    [Fact]
    public void WanderingMonster_NeverChipsWalls()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        var player = PlayerAt(480f, 480f); // beyond aggro — no target
        h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        h.Tick(60, player); // idles, wanders, bumps the fence — never attacks it

        var fence = h.FenceAt(4, 5);
        Assert.NotNull(fence);
        Assert.Equal(10, fence!.Health);
        player.ActionSystem!.FlushNotifications();
        Assert.DoesNotContain(player.ActionSystem.Notifications,
            n => n.Text.Contains("was destroyed"));
    }

    // ── 4. Bricks chip at the monster's own cadence ────────────────────────

    [Fact]
    public void FrustratedRaider_ChipsBricks()
    {
        var h = new Harness();
        // A 64 px brick band across tile (4,5) at sub-y 2 — blocked from
        // the first tick at the monster's y band.
        Assert.True(h.PlaceBrick(4, 5, 0, 2).Success);
        Assert.True(h.PlaceBrick(4, 5, 1, 2).Success);
        Assert.True(h.PlaceBrick(4, 5, 2, 2).Success);
        Assert.True(h.PlaceBrick(4, 5, 3, 2).Success);
        var player = PlayerAt(352f, 352f);
        h.Combat.SpawnMonster(HostileDef(), 250f, 352f);

        // 4 s frustration + one cadence tick: chipping must have started,
        // but 4 bricks (hp 20 each) still stand.
        h.Tick(20, player);

        int damaged = h.Buildings.Structures.Count(s => s.IsActive && s.Health < 20);
        Assert.True(damaged > 0, "the brick band must be chipped after the frustration window");
        Assert.Equal(4, h.Buildings.Structures.Count(s => s.IsActive && s.TileX == 4));
    }

    // ── 5. DamageStructure unit contract ───────────────────────────────────

    [Fact]
    public void DamageStructure_AtZeroHealth_RemovesAndFreesTile()
    {
        var h = new Harness();
        Assert.True(h.Place("fence_block", 4, 5).Success);
        Assert.True(h.Place("fence_block", 4, 6).Success);
        var a = h.FenceAt(4, 5);
        var b = h.FenceAt(4, 6);
        Assert.NotNull(a);
        Assert.NotNull(b);

        // Partial chip: stays standing.
        Assert.False(h.Buildings.DamageStructure(a!, 3, h.World));
        Assert.Equal(7, a!.Health);
        Assert.NotNull(h.FenceAt(4, 5));

        // Lethal chip: removed, tile freed live.
        Assert.True(h.Buildings.DamageStructure(a, 7, h.World));
        Assert.True(h.FenceAt(4, 5) == null);
        Assert.True(WorkerPathfinder.CanStand(h.World, 4, 5),
            "the tile frees for pathing the moment the wall dies");
        Assert.NotNull(h.FenceAt(4, 6)); // neighbours untouched
    }
}
