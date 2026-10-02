namespace DontStarveRuneScape.Combat;

using System.Collections.Generic;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;

/// <summary>
/// CombatSystem — Monster AI (idle/patrol/chase/attack/flee on MonsterState),
/// monster attacks on the player, player attacks with gear damage, loot/XP on
/// kills, spawn-point respawn, and damage numbers.
/// </summary>
public sealed class CombatSystem
{
    public List<Monster> Monsters { get; } = [];
    public List<DamageNumber> DamageNumbers { get; } = [];

    /// <summary>Quest progress hooks (wired at boot): kills count toward
    /// kill_monster objectives.</summary>
    public NPC.QuestSystem? Quests { get; set; }

    private readonly Random _rng = new();
    private readonly List<SpawnRecord> _spawnPoints = [];
    private float _playerAttackCooldown;
    private bool _pendingPlayerDeath;

    public void Tick(float dt, Player? player = null)
    {
        if (_playerAttackCooldown > 0) _playerAttackCooldown = MathF.Max(0f, _playerAttackCooldown - dt);
        UpdateRespawns(dt, player);

        foreach (var monster in Monsters)
        {
            monster.UpdateTimers(dt);
            UpdateMonster(monster, player, dt);
        }

        // Clearing the world after the player's death (all monsters, per
        // DeathClearMonstersRadius = 0) — after the loop, the death may be
        // detected mid-iteration.
        ConsumePendingPlayerDeath();

        // Update damage numbers
        for (int i = DamageNumbers.Count - 1; i >= 0; i--)
        {
            var dn = DamageNumbers[i];
            dn.Update(dt);
            if (dn.IsExpired)
                DamageNumbers.RemoveAt(i);
        }
    }

    // ─── Spawning ──────────────────────────────────────────────────────────

    /// <summary>Spawn a runtime monster from a definition. Public so tests can
    /// place custom monsters; called by SpawnFromRegistry for world boot.</summary>
    public Monster SpawnMonster(MonsterDef def, float worldX, float worldY, string biomeId = "")
    {
        var monster = new Monster
        {
            MonsterId = def.MonsterId,
            Name = def.Name,
            WorldX = worldX,
            WorldY = worldY,
            HomeX = worldX,
            HomeY = worldY,
            Health = (int)def.Hp,
            MaxHealth = (int)def.Hp,
            Attack = def.Attack,
            Defence = def.Defence,
            Speed = def.Speed,
            AttackRange = Constants.MonsterAttackRange,
            AggroRange = def.AggressionRange,
            FleeRange = def.FleeRange,
            AttackCooldownMax = def.AttackCooldown,
            XpReward = def.XpReward,
            IsHostile = def.IsHostile,
            BiomeId = biomeId,
            SpriteKey = def.SpriteKey,
            Def = def,
        };
        Monsters.Add(monster);
        _spawnPoints.Add(new SpawnRecord { Def = def, X = worldX, Y = worldY, BiomeId = biomeId });
        return monster;
    }

    /// <summary>Populate the world at boot: for each biome with monsters, spawn
    /// MonstersPerType instances per monster type at random matching-biome
    /// tiles at least InitialMonsterSpawnRadiusTiles from the player spawn.</summary>
    public void SpawnFromRegistry(MonsterRegistry registry, World.TileMap? world, Player? player)
    {
        if (world == null) return;
        int tileSize = Constants.TileSize;

        // Matching-biome tile centers per biome id.
        var candidates = new Dictionary<string, List<(float X, float Y)>>();
        foreach (var tile in world.Tiles)
        {
            var biomeId = tile.Biome?.Id;
            if (string.IsNullOrEmpty(biomeId)) continue;
            if (!candidates.TryGetValue(biomeId, out var list))
                candidates[biomeId] = list = [];
            list.Add((tile.X * tileSize + tileSize / 2f, tile.Y * tileSize + tileSize / 2f));
        }

        float minDistSq = Constants.InitialMonsterSpawnRadiusTiles * (float)tileSize;
        minDistSq *= minDistSq;
        foreach (var (biomeId, defs) in registry.MonstersByBiome)
        {
            if (!candidates.TryGetValue(biomeId, out var list)) continue;

            List<(float X, float Y)>? valid = null;
            if (player != null)
            {
                valid = [];
                foreach (var spot in list)
                {
                    float dx = spot.X - player.WorldX;
                    float dy = spot.Y - player.WorldY;
                    if (dx * dx + dy * dy >= minDistSq)
                        valid.Add(spot);
                }
            }
            var pool = valid ?? list;
            if (pool.Count == 0) continue;

            foreach (var def in defs.Values)
            {
                for (int i = 0; i < Constants.MonstersPerType; i++)
                {
                    var spot = pool[_rng.Next(pool.Count)];
                    SpawnMonster(def, spot.X, spot.Y, biomeId);
                }
            }
        }
    }

    /// <summary>Remove every monster; their spawn points repopulate after
    /// MonsterRespawnSeconds. Used by the player-death flow.</summary>
    public void ClearMonsters()
    {
        Monsters.Clear();
        foreach (var record in _spawnPoints)
        {
            record.RespawnTimer = Constants.MonsterRespawnSeconds;
            record.Pending = true;
        }
    }

    /// <summary>Queue a monster clear for the next Tick (the player died
    /// outside this system's tick — e.g. starvation).</summary>
    public void QueuePlayerDeath() => _pendingPlayerDeath = true;

    /// <summary>Run a queued death clear immediately; Game.Update calls this
    /// so the clear lands on the next update even outside playing states.</summary>
    public void ConsumePendingPlayerDeath()
    {
        if (!_pendingPlayerDeath) return;
        _pendingPlayerDeath = false;
        ClearMonsters();
    }

    private void UpdateRespawns(float dt, Player? player)
    {
        for (int i = _spawnPoints.Count - 1; i >= 0; i--)
        {
            var record = _spawnPoints[i];
            if (!record.Pending) continue;
            record.RespawnTimer -= dt;
            if (record.RespawnTimer > 0) continue;
            record.Pending = false;
            SpawnMonster(record.Def, record.X, record.Y, record.BiomeId);
        }
    }

    // ─── Player attack ─────────────────────────────────────────────────────

    /// <summary>Attack the nearest monster in melee reach with the equipped
    /// weapon's damage (unarmed = 1): damage + attack bonus vs monster defence,
    /// min 1. Kills grant loot, attack XP and kill-quest progress.</summary>
    public CombatHitResult PlayerAttack(Player player, Inventory.Inventory inventory, Skills.SkillManager skills)
    {
        if (_playerAttackCooldown > 0)
            return new CombatHitResult { Success = false, Message = "You are not ready to attack yet." };

        Monster? target = null;
        float bestDist = Constants.PlayerAttackRange;
        foreach (var monster in Monsters)
        {
            if (!monster.IsAlive()) continue;
            float dist = player.DistanceTo(monster.WorldX, monster.WorldY);
            if (dist <= bestDist)
            {
                bestDist = dist;
                target = monster;
            }
        }
        if (target == null)
            return new CombatHitResult { Success = false, Message = "No monster in reach." };

        // Face the target so the carried weapon swings toward it.
        player.Facing = target.WorldX >= player.WorldX ? 1f : -1f;

        float weapon = player.Gear?.GetWeaponDamage() ?? 1f;
        float bonus = player.Gear?.GetTotalAttackBonus() ?? 0f;
        int damage = Math.Max(1, (int)MathF.Round(weapon + bonus - target.Defence));
        target.Health -= damage;
        DamageNumbers.Add(new DamageNumber { Value = damage, WorldX = target.WorldX, WorldY = target.WorldY });

        // Attack speed: base cooldown reduced by the gear's speed bonus.
        float speedBonus = player.Gear?.GetTotalSpeedBonus() ?? 0f;
        _playerAttackCooldown = MathF.Max(0.3f, Constants.CombatBaseAttackCooldown - speedBonus * Constants.CombatSpeedStatCooldownReduction);

        bool killed = !target.IsAlive();
        string message = killed
            ? HandleMonsterDeath(target, inventory, skills)
            : $"You hit the {target.Name} for {damage}. ({target.Health} HP left)";
        return new CombatHitResult { Success = true, Message = message, Damage = damage, Killed = killed };
    }

    private string HandleMonsterDeath(Monster monster, Inventory.Inventory inventory, Skills.SkillManager skills)
    {
        Monsters.Remove(monster);
        StartRespawnTimer(monster);

        var lines = new List<string>();
        foreach (var itemId in RollLoot(monster.Def!, _rng))
        {
            if (inventory.CanAdd(itemId, 1))
            {
                inventory.AddItem(itemId, 1);
                lines.Add($"Loot: +1 {itemId}");
            }
        }
        if (monster.XpReward > 0)
        {
            lines.Add($"+{monster.XpReward} attack xp");
            lines.AddRange(skills.AddXpWithNotification("attack", monster.XpReward));
        }
        Quests?.NotifyKill(monster.MonsterId);

        string message = $"You kill the {monster.Name}.";
        return lines.Count > 0 ? $"{message} {string.Join(" ", lines)}" : message;
    }

    /// <summary>Loot roll: each table entry drops when a random roll beats its chance.</summary>
    public static List<string> RollLoot(MonsterDef def, Random rng)
    {
        var drops = new List<string>();
        foreach (var entry in def.LootTable)
        {
            if (rng.NextDouble() < entry.Chance)
                drops.Add(entry.ItemId);
        }
        return drops;
    }

    private void StartRespawnTimer(Monster monster)
    {
        foreach (var record in _spawnPoints)
        {
            if (record.Def == monster.Def && record.X == monster.WorldX && record.Y == monster.WorldY)
            {
                record.RespawnTimer = Constants.MonsterRespawnSeconds;
                record.Pending = true;
                return;
            }
        }
    }

    // ─── Monster AI ────────────────────────────────────────────────────────

    private void UpdateMonster(Monster m, Player? player, float dt)
    {
        float dist = player?.DistanceTo(m.WorldX, m.WorldY) ?? float.MaxValue;

        // Low-health flee: run from the player while they are close; once out
        // of aggro range, calm down and resume wandering.
        if (m.State == MonsterState.Flee
            || (m.Health <= m.MaxHealth * Constants.MonsterFleeHealthFraction && dist <= m.FleeRange))
        {
            m.State = MonsterState.Flee;
            MoveAway(m, player!.WorldX, player.WorldY, dt);
            if (dist > m.AggroRange)
            {
                m.State = MonsterState.Idle;
                m.StateTimer = 0f;
                m.AggroCooldown = Constants.MonsterAggroCooldownAfterFlee;
            }
            return;
        }

        bool engaged = m.State is MonsterState.Chase or MonsterState.Attack;
        float aggroRadius = engaged ? m.AggroRange * Constants.MonsterLeashMultiplier : m.AggroRange;
        if (player?.Survival != null && !player.Survival.IsDead
            && m.IsHostile && m.AggroCooldown <= 0 && dist <= aggroRadius)
        {
            if (dist <= m.AttackRange)
            {
                m.State = MonsterState.Attack;
                if (m.AttackCooldown <= 0)
                {
                    HitPlayer(m, player);
                    m.AttackCooldown = m.AttackCooldownMax;
                }
            }
            else
            {
                m.State = MonsterState.Chase;
                MoveToward(m, player.WorldX, player.WorldY, dt);
            }
            return;
        }

        // Out of aggro: drop the chase and wander near home.
        if (engaged)
        {
            m.State = MonsterState.Idle;
            m.StateTimer = 0f;
        }
        UpdateWander(m, dt);
    }

    private void UpdateWander(Monster m, float dt)
    {
        if (m.State == MonsterState.Idle)
        {
            if (m.StateTimer >= Constants.MonsterIdleSeconds)
            {
                float angle = (float)(_rng.NextDouble() * MathF.Tau);
                float radius = (float)_rng.NextDouble() * Constants.MonsterPatrolRadius;
                m.PatrolTargetX = m.HomeX + MathF.Cos(angle) * radius;
                m.PatrolTargetY = m.HomeY + MathF.Sin(angle) * radius;
                m.State = MonsterState.Patrol;
                m.StateTimer = 0f;
            }
        }
        else if (m.State == MonsterState.Patrol)
        {
            float dx = m.PatrolTargetX - m.WorldX;
            float dy = m.PatrolTargetY - m.WorldY;
            if (dx * dx + dy * dy < 16f)
            {
                m.State = MonsterState.Idle;
                m.StateTimer = 0f;
            }
            else
            {
                MoveToward(m, m.PatrolTargetX, m.PatrolTargetY, dt);
            }
        }
    }

    private void HitPlayer(Monster m, Player player)
    {
        float defence = player.Gear?.GetTotalDefenceBonus() ?? 0f;
        int damage = Math.Max(1, (int)MathF.Round(m.Attack - defence));
        bool died = player.TakeDamage(damage);
        DamageNumbers.Add(new DamageNumber { Value = -damage, WorldX = player.WorldX, WorldY = player.WorldY });
        if (died)
        {
            // Same death path as starvation: the Survival hook runs the game's
            // death handling; the monster clear happens after this loop.
            player.Survival?.OnDeath?.Invoke();
            _pendingPlayerDeath = true;
        }
    }

    private static void MoveToward(Monster m, float targetX, float targetY, float dt)
    {
        float dx = targetX - m.WorldX;
        float dy = targetY - m.WorldY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.001f) return;
        m.WorldX += dx / length * m.Speed * dt;
        m.WorldY += dy / length * m.Speed * dt;
    }

    private static void MoveAway(Monster m, float fromX, float fromY, float dt)
    {
        float dx = m.WorldX - fromX;
        float dy = m.WorldY - fromY;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 0.001f) return;
        m.WorldX += dx / length * m.Speed * dt;
        m.WorldY += dy / length * m.Speed * dt;
    }
}

/// <summary>
/// Monster — A hostile entity in the world, with AI state and stats copied
/// from its definition at spawn.
/// </summary>
public sealed class Monster
{
    public string MonsterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public MonsterState State { get; set; } = MonsterState.Idle;
    public float AttackCooldown { get; set; } = 0f;

    // Stats copied from the definition at spawn.
    public float Attack { get; set; } = 5f;
    public float Defence { get; set; } = 0f;
    public float Speed { get; set; } = 50f;
    public float AttackRange { get; set; } = 64f;
    public float AggroRange { get; set; } = 150f;
    public float FleeRange { get; set; } = 300f;
    public float AttackCooldownMax { get; set; } = 1.5f;
    public int XpReward { get; set; } = 10;
    public bool IsHostile { get; set; } = true;
    public string BiomeId { get; set; } = string.Empty;
    public string SpriteKey { get; set; } = string.Empty;

    /// <summary>The definition this monster spawned from (loot rolls).</summary>
    public MonsterDef? Def { get; set; }

    // AI state.
    public float HomeX { get; set; }
    public float HomeY { get; set; }
    public float StateTimer { get; set; }
    public float PatrolTargetX { get; set; }
    public float PatrolTargetY { get; set; }
    public float AggroCooldown { get; set; }

    public bool IsAlive() => Health > 0;

    public void UpdateTimers(float dt)
    {
        if (AttackCooldown > 0)
            AttackCooldown -= dt;
        if (AggroCooldown > 0)
            AggroCooldown -= dt;
        StateTimer += dt;
    }
}

/// <summary>
/// Monster state enum.
/// </summary>
public enum MonsterState
{
    Idle,
    Patrol,
    Chase,
    Attack,
    Flee,
}

/// <summary>
/// DamageNumber — Floating damage text.
/// </summary>
public sealed class DamageNumber
{
    public float Value { get; set; }
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float Height { get; set; } = 0f;
    public float LifeTime { get; set; } = 1.0f;
    public float Elapsed { get; set; } = 0f;
    public bool IsExpired => Elapsed >= LifeTime;

    public void Update(float dt)
    {
        Elapsed += dt;
        Height += Constants.CombatDamageNumberRiseRate * dt; // Rise rate
    }
}

/// <summary>
/// CombatHitResult — Result of a player attack, for the notification line.
/// </summary>
public sealed class CombatHitResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Damage { get; set; }
    public bool Killed { get; set; }
}

/// <summary>
/// SpawnRecord — A monster spawn point, kept for respawn.
/// </summary>
public sealed class SpawnRecord
{
    public MonsterDef Def { get; set; } = new();
    public float X { get; set; }
    public float Y { get; set; }
    public string BiomeId { get; set; } = string.Empty;
    public float RespawnTimer { get; set; }
    public bool Pending { get; set; }
}
