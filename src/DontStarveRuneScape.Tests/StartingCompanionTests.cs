namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Starting companion (frontier slice 5): an early-game first follower
/// who joins at npc_type "recruit" / RecruitBehavior "companion" — follows
/// the player, stops when outranged (no rubber-banding), flees hostiles
/// rather than tanking, and persists through the same snapshot path as
/// other recruits. A behavior engine (`CompanionBehavior`) on top of the
/// existing MovementSystem: no new world data, no net-new persistence.
/// Spec: docs/superpowers/specs/2026-10-07-starting-companion-design.md
/// </summary>
public sealed class StartingCompanionTests
{
    private static (CompanionBehavior Behavior, RecruitNpc Mara, Player Player) NewWorld(int guardNpcCount = 0)
    {
        var world = new TileMap(24, 24);
        for (int y = 0; y < 24; y++)
            for (int x = 0; x < 24; x++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;

        var player = new Player(200f, 200f)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new DontStarveRuneScape.Survival.SurvivalSystem(),
        };

        var mara = new RecruitNpc
        {
            NpcId = "companion_mara",
            Name = "Hunter Mara, freshly cast out",
            IsActive = true,
            IsRecruited = true,
            RecruitBehavior = "companion", // starts as companion on accept
            WorldX = 400f, WorldY = 200f,
            Health = 18, MaxHealth = 18,
            ColonyHunger = 100f, ColonyRest = 100f,
        };

        var followers = new[] { mara }.Concat(
            Enumerable.Range(0, guardNpcCount).Select(i => new RecruitNpc
            {
                NpcId = $"guard_{i}",
                Name = $"Guard {i}",
                RecruitBehavior = "guard",
                WorldX = 220f + i * 60f, WorldY = 200f,
            }).ToArray());

        var behavior = new CompanionBehavior(player);
        // Collection-equivalent: the harness passes in a follower list the
        // ColonySystem consumer also manages for schedule/claims.
        return (behavior, mara, player);
    }

    [Fact]
    public void Companion_Registry_DefExists()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();
        var def = registry.Npcs.TryGetValue("companion_mara", out var d);
        Assert.True(def, "companion_mara must be in npcs.json");
        Assert.Equal("companion", d!.Behavior);
        Assert.Contains("companion", d.AvailableBehaviors);
        Assert.Equal(0, d.RecruitCommerceRequirement);
        Assert.Equal(0, d.RecruitPersuasionRequirement);
        Assert.Equal(0, d.RecruitCompositeStat);
    }

    [Fact]
    public void Companion_FollowsPlayer_WithinTether()
    {
        var (behavior, mara, player) = NewWorld();
        // 5 tiles east, close enough to walk under tether
        player.WorldX = 260f; player.WorldY = 100f;

        for (int i = 0; i < 80; i++)
            behavior.Tick(0.25f, mara);

        // Never rubber-banding: she walks the gap closed, then stops close.
        float dx = player.WorldX - mara.WorldX;
        float dy = player.WorldY - mara.WorldY;
        Assert.True(MathF.Sqrt(dx * dx + dy * dy) <= 2f * Constants.TileSize,
            $"companion should close to within ~2 tiles; gap {MathF.Sqrt(dx * dx + dy * dy)}px");
        // Not teleporting: each tick moves the position by at most walk
        // speed (no step jumps)
    }

    [Fact]
    public void Companion_DropsOffBeyondTether_AndResumesWhenBackClose()
    {
        var (behavior, mara, player) = NewWorld();
        player.WorldX = 200f; player.WorldY = 100f; // anchor inside normal follow

        // Far away: drop the tether
        player.WorldX = 800f;
        for (int i = 0; i < 40; i++)
            behavior.Tick(0.25f, mara);
        Assert.Equal("Left behind", mara.CarryStatus);
        Assert.True(mara.VelocityX == 0f && mara.VelocityY == 0f,
            "dropped companion stops moving");

        // Come back: resume follow
        player.WorldX = 240f;
        for (int i = 0; i < 40; i++)
            behavior.Tick(0.25f, mara);
        Assert.NotEqual("Left behind", mara.CarryStatus);
    }

    [Fact]
    public void Companion_FleesHostile_AndStaysClear()
    {
        var (behavior, mara, player) = NewWorld();
        player.WorldX = 260f; player.WorldY = 100f;

        var combat = new CombatSystem();
        var wolf = combat.SpawnMonster(new MonsterDef
        {
            MonsterId = "wolf", Name = "Wolf", IsHostile = true,
            Health = 25, MaxHealth = 25, AggroRange = 150f,
        }, 400f, 100f, biomeId: "forest");
        behavior.Combat = combat; // engine reads monsters each tick

        for (int i = 0; i < 20; i++)
            behavior.Tick(0.25f, mara);

        // She flees the wolf's position, not toward it
        float dx = mara.WorldX - wolf.WorldX;
        float dy = mara.WorldY - wolf.WorldY;
        float fromWolf = MathF.Sqrt(dx * dx + dy * dy);
        Assert.True(fromWolf > 5f * Constants.TileSize,
            $"companion should stay clear of hostiles; fromWolf {fromWolf}px");
    }

    [Fact]
    public void Companion_PlayerAtRest_IdlesBesideThem()
    {
        var (behavior, mara, player) = NewWorld();
        player.WorldX = 200f; player.WorldY = 100f; // player stationary
        for (int i = 0; i < 60; i++)
            behavior.Tick(0.25f, mara);

        // She ends near the player but not on top of them
        float dx = MathF.Abs(mara.WorldX - player.WorldX);
        float dy = MathF.Abs(mara.WorldY - player.WorldY);
        Assert.True(dx >= 1f * Constants.TileSize * 0.5f && dy >= 1f * Constants.TileSize * 0.5f,
            "companion posts up beside the player, not overlapping");
    }

    // -- The engine must respect sleep need thresholds pre-agreed by colony --

    [Fact]
    public void Companion_PersistsThroughSnapshot()
    {
        var world = new TileMap(30, 30);
        for (int x = 0; x < 30; x++)
            for (int y = 0; y < 30; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;

        var player = new Player(200f, 200f) { Gear = new PlayerGear(), SkillManager = new SkillManager() };
        var mara = new RecruitNpc
        {
            NpcId = "companion_mara",
            IsRecruited = true,
            RecruitBehavior = "companion",
            WorldX = 100f, WorldY = 100f,
            ColonyHunger = 15f, ColonyRest = 25f, // worn down, must persist
        };
        // Per-recruit skills travel too (slice 1)
        mara.Skills.AddXpWithNotification("woodcutting", SkillManager.XpForLevel(3) + 1f);

        var snapshot = new NPCSystem();
        snapshot.NPCs.Add(mara);
        var snap = snapshot.GetSnapshot();
        var fresh = new NPCSystem();
        var npcRegistry = new NpcRegistry();
        npcRegistry.LoadAll();
        fresh.RestoreSnapshot(snap, npcRegistry, world);

        var restored = fresh.NPCs.OfType<RecruitNpc>().First(n => n.NpcId == "companion_mara");
        Assert.Equal("companion", restored.RecruitBehavior);
        Assert.Equal(3, restored.Skills.GetSkillLevel("woodcutting"));
        Assert.Equal(15f, restored.ColonyHunger, 0.01f);
    }

    [Fact]
    public void Companion_OneAtATime_WithPlayer() // the player can't have two companions glued
    {
        var behavior = new CompanionBehavior(new Player(0f, 0f));
        var a = new RecruitNpc { NpcId = "mara", RecruitBehavior = "companion" };
        var b = new RecruitNpc { NpcId = "second", RecruitBehavior = "companion" };
        behavior.Register(a);
        behavior.Register(b); // second lands as a follower but does NOT steal the bond
        Assert.Equal("mara", behavior.FollowingNpcId); // original companion keeps the bond
    }

    // -- One bond at a time: only one companion follows the player ---------

    [Fact]
    public void Companion_SecondBondFollows_LeavesFirstFollow() // second joins, doesn't displace
    {
        var (behavior, mara, player) = NewWorld();
        behavior.Register(mara);
        var second = new RecruitNpc
        {
            NpcId = "second", RecruitBehavior = "companion",
            WorldX = 100f, WorldY = 300f,
        };
        behavior.Register(second);

        // Only the first bond drives follow; the second companion falls
        // back to idle — it still moves via Other Movement rules.
        Assert.Equal("companion_mara", behavior.FollowingNpcId);
        behavior.Tick(0.25f, second);
        Assert.Equal(0f, second.VelocityX);
        Assert.Equal(0f, second.VelocityY);
    }
}

// ---------------------------------------------------------------------------
// Minimal engine under test/workout: the runtime target is the
// CompanionBehavior class in RecruitmentSystem.cs, spec'd here to keep
// the RED against exactly the surface the tests exercise.

/// <summary>
/// Movement legality for the companion follow/flee math: whether a walker
/// may stand at a world position. Mirrors WorkerPathfinder's usual use of
/// the TileMap — this slice's movement stays within pathing the
/// colony systems already compute (obstacle + water rules only).
/// </summary>
public static class CompanionPathing
{
    public static bool CanStandAt(TileMap world, float worldX, float worldY)
    {
        int tx = (int)(worldX / Constants.TileSize);
        int ty = (int)(worldY / Constants.TileSize);
        return WorkerPathfinder.CanStand(world, tx, ty);
    }
}
