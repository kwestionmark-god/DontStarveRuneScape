using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

namespace DontStarveRuneScape.Tests;

public class CombatTests
{
    // ─── Monster registry (real data) ──────────────────────────────────────

    [Fact]
    public void MonsterRegistry_LoadsRealMonstersJson()
    {
        var registry = new MonsterRegistry();
        registry.LoadAll();

        Assert.NotEmpty(registry.MonstersByBiome);
        Assert.Contains("forest", registry.MonstersByBiome.Keys);
        var wolf = registry.GetMonster("forest", "wolf");
        Assert.NotNull(wolf);
        Assert.Equal("Wolf", wolf!.Name);
        Assert.Equal(8, (int)wolf.Hp);
        Assert.Equal(3, (int)wolf.Attack);
        Assert.Equal(2, (int)wolf.Defence);
        Assert.Equal(150, (int)wolf.AggressionRange);
        Assert.Equal(1.5f, wolf.AttackCooldown);
        Assert.Equal(15, wolf.XpReward);
        Assert.True(wolf.IsHostile);
        Assert.Equal("monster/wolf", wolf.SpriteKey);
        Assert.Equal(3, wolf.LootTable.Length);
    }

    [Fact]
    public void MonsterRegistry_GetMonster_SearchesAllBiomes()
    {
        var registry = new MonsterRegistry();
        registry.LoadAll();

        var crab = registry.GetMonster("crab");

        Assert.NotNull(crab);
        Assert.Equal("coastal", registry.BiomeOf("crab"));
    }

    // ─── Player attack ─────────────────────────────────────────────────────

    [Fact]
    public void PlayerAttack_DamagesNearestMonsterInReach()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 260f, 100f); // 60px away

        var result = system.PlayerAttack(player, inv, skills);

        Assert.True(result.Success);
        var monster = Assert.Single(system.Monsters);
        Assert.Equal(9, monster.Health); // unarmed 1 + 0 bonus - 2 defence, min 1 → 10 - 1
        var dn = Assert.Single(system.DamageNumbers);
        Assert.Equal(1, (int)dn.Value);
    }

    [Fact]
    public void PlayerAttack_UsesEquippedWeaponDamage()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        player.Gear!.Equip(new GearItem
        {
            Id = "test_sword",
            Name = "Test Sword",
            Slot = "weapon",
            Damage = 5,
            AttackBonus = 2,
        });
        system.SpawnMonster(TestMonsterDef(), 260f, 100f);

        var result = system.PlayerAttack(player, inv, skills);

        Assert.True(result.Success);
        Assert.Equal(5, (int)Assert.Single(system.DamageNumbers).Value); // 5 + 2 - 2
    }

    [Fact]
    public void PlayerAttack_IgnoresMonstersOutOfRange()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 700f, 100f); // 500px away

        var result = system.PlayerAttack(player, inv, skills);

        Assert.False(result.Success);
        Assert.Equal(10, system.Monsters[0].Health);
        Assert.Empty(system.DamageNumbers);
    }

    [Fact]
    public void PlayerAttack_HasCooldown()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 260f, 100f);

        Assert.True(system.PlayerAttack(player, inv, skills).Success);
        Assert.False(system.PlayerAttack(player, inv, skills).Success); // on cooldown

        system.Tick(2f, player); // cooldown elapses
        Assert.True(system.PlayerAttack(player, inv, skills).Success);
    }

    // ─── Monster death: loot, xp, respawn ──────────────────────────────────

    [Fact]
    public void MonsterDeath_LootXpAndRespawn()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        var monster = system.SpawnMonster(TestMonsterDef(), 260f, 100f);
        monster.Health = 1;

        var result = system.PlayerAttack(player, inv, skills);

        Assert.True(result.Killed);
        Assert.Empty(system.Monsters);
        Assert.Equal(1, inv.GetItemQuantity("test_pelt")); // loot chance 1.0
        Assert.Equal(7, (int)skills.GetSkill("attack").Xp); // xp reward to the attack skill

        system.Tick(Constants.MonsterRespawnSeconds + 1f, player);
        var respawned = Assert.Single(system.Monsters);
        Assert.Equal(260f, respawned.WorldX);
        Assert.Equal(10, respawned.Health);
    }

    [Fact]
    public void RollLoot_RespectsChanceBounds()
    {
        var always = new MonsterDef
        {
            MonsterId = "a",
            LootTable = [new MonsterLootEntry { ItemId = "drop", Chance = 1f }],
        };
        var never = new MonsterDef
        {
            MonsterId = "b",
            LootTable = [new MonsterLootEntry { ItemId = "drop", Chance = 0f }],
        };

        Assert.Equal(["drop"], CombatSystem.RollLoot(always, new Random(1)));
        Assert.Empty(CombatSystem.RollLoot(never, new Random(1)));
    }

    // ─── Monster AI ────────────────────────────────────────────────────────

    [Fact]
    public void MonsterChasesPlayerInAggroRange()
    {
        var (system, player, _, _) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 320f, 100f); // 120px < aggro 150

        system.Tick(0.1f, player);

        var monster = Assert.Single(system.Monsters);
        Assert.Equal(MonsterState.Chase, monster.State);
        Assert.True(monster.WorldX < 320f, "a chasing monster should close in on the player");
    }

    [Fact]
    public void MonsterAttacksPlayerInReach()
    {
        var (system, player, _, _) = MakeCombat(200f, 100f);
        var monster = system.SpawnMonster(TestMonsterDef(), 230f, 100f); // 30px < attack range 64
        float hpBefore = player.Survival!.Hp;

        system.Tick(0.1f, player);

        Assert.Equal(MonsterState.Attack, monster.State);
        Assert.True(player.Survival.Hp < hpBefore, "a monster in reach should damage the player");
        var dn = system.DamageNumbers[^1];
        Assert.True(dn.Value < 0, "player damage spawns a negative damage number");
    }

    [Fact]
    public void MonsterLeavesChaseWhenPlayerOutOfRange()
    {
        var (system, player, _, _) = MakeCombat(200f, 100f);
        var monster = system.SpawnMonster(TestMonsterDef(), 320f, 100f);
        system.Tick(0.1f, player);
        Assert.Equal(MonsterState.Chase, monster.State);

        player.WorldX = 600f; // 280px > aggro 150 * leash 1.5
        system.Tick(0.1f, player);

        Assert.Equal(MonsterState.Idle, monster.State);
    }

    [Fact]
    public void MonsterFleesAtLowHealth()
    {
        var (system, player, _, _) = MakeCombat(200f, 100f);
        var monster = system.SpawnMonster(TestMonsterDef(), 260f, 100f);
        monster.Health = 2; // 25% of 8 < flee fraction 0.3

        system.Tick(0.1f, player);

        Assert.Equal(MonsterState.Flee, monster.State);
        Assert.True(monster.WorldX > 260f, "a fleeing monster should run from the player");
    }

    [Fact]
    public void MonsterIdlesAndPatrolsWhenPlayerAbsent()
    {
        var (system, player, _, _) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 260f, 100f);

        system.Tick(2f, player: null); // idle expires, patrol starts

        Assert.Equal(MonsterState.Patrol, system.Monsters[0].State);
    }

    // ─── Player death ──────────────────────────────────────────────────────

    [Fact]
    public void MonsterKill_TriggersDeathAndClearsMonsters()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 230f, 100f);
        var deaths = 0;
        player.Survival!.OnDeath = () => deaths++;

        player.Survival.TakeDamage(player.Survival.Hp - 1f); // leave 1 HP
        system.Tick(0.1f, player); // the wolf hits for 3 — dead

        Assert.Equal(1, deaths);
        Assert.Empty(system.Monsters); // cleared on death
        Assert.Contains(system.DamageNumbers, d => d.Value < 0);
    }

    [Fact]
    public void HandlePlayerDeath_RestoresStatsAndCounts()
    {
        var game = new Game();
        game.Survival = new SurvivalSystem();
        game.Survival.TakeDamage(game.Survival.MaxHp); // dead

        game.HandlePlayerDeath();

        Assert.Equal(1, game.DeathCount);
        Assert.False(game.Survival.IsDead);
        Assert.Equal(game.Survival.MaxHp * Constants.DeathRestoreHpFraction, game.Survival.Hp);
        Assert.Equal(game.Survival.MaxHunger * Constants.DeathRestoreHungerFraction, game.Survival.Hunger);
    }

    [Fact]
    public void PlayerDeath_QueuesMonsterClearForNextTick()
    {
        var game = new Game();
        game.Survival = new SurvivalSystem();
        game.CombatSystem = new CombatSystem();
        var player = new Player(100f, 100f)
        {
            Survival = game.Survival,
            Gear = new PlayerGear(),
        };
        game.CombatSystem.SpawnMonster(TestMonsterDef(), 400f, 100f);

        game.Survival.TakeDamage(game.Survival.MaxHp); // starvation-style death
        game.HandlePlayerDeath();
        game.Update(0.1f);

        Assert.Empty(game.CombatSystem.Monsters);
    }

    // ─── Spawning ──────────────────────────────────────────────────────────

    [Fact]
    public void SpawnFromRegistry_SpawnsPerBiomeAwayFromPlayer()
    {
        var registry = new MonsterRegistry();
        registry.LoadAll();
        var system = new CombatSystem();
        var world = new TileMap(64, 64);
        var forest = new BiomeDef { Id = "forest" };
        var swamp = new BiomeDef { Id = "swamp" };
        for (int x = 0; x < 32; x++)
            for (int y = 0; y < 64; y++)
                world.GetTile(x, y)!.Biome = forest;
        for (int x = 32; x < 64; x++)
            for (int y = 0; y < 64; y++)
                world.GetTile(x, y)!.Biome = swamp;

        var player = new Player(32 * 64f, 32 * 64f);
        system.SpawnFromRegistry(registry, world, player);

        Assert.NotEmpty(system.Monsters);
        Assert.Contains(system.Monsters, m => m.BiomeId == "forest");
        Assert.Contains(system.Monsters, m => m.BiomeId == "swamp");
        float minDist = Constants.InitialMonsterSpawnRadiusTiles * Constants.TileSize;
        Assert.All(system.Monsters,
            m => Assert.True(player.DistanceTo(m.WorldX, m.WorldY) >= minDist,
                "initial spawns keep at least the spawn radius from the player"));
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static (CombatSystem, Player, Inv, SkillManager) MakeCombat(float px, float py)
    {
        var player = new Player(px, py)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        return (new CombatSystem(), player, player.Inventory!, player.SkillManager!);
    }

    private static MonsterDef TestMonsterDef() => new()
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
}
