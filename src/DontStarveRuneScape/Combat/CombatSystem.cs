namespace DontStarveRuneScape.Combat;

using System.Collections.Generic;
using DontStarveRuneScape.Core;

/// <summary>
/// CombatSystem — Handles combat mechanics, monsters, and damage numbers.
/// </summary>
public sealed class CombatSystem
{
    public List<Monster> Monsters { get; } = [];
    public List<DamageNumber> DamageNumbers { get; } = [];

    public void Tick(float dt)
    {
        // Update monsters, AI, combat logic
        foreach (var monster in Monsters)
        {
            monster.Update(dt);
        }

        // Update damage numbers
        for (int i = DamageNumbers.Count - 1; i >= 0; i--)
        {
            var dn = DamageNumbers[i];
            dn.Update(dt);
            if (dn.IsExpired)
                DamageNumbers.RemoveAt(i);
        }
    }
}

/// <summary>
/// Monster — A hostile entity in the world.
/// </summary>
public sealed class Monster
{
    public string MonsterId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100
    public bool IsActive { get; set; } = true;
    public MonsterState State { get; set; } = MonsterState.Idle;
    public float AttackCooldown { get; set; } = 0f;

    public bool IsAlive() => Health > 0;

    public void Update(float dt)
    {
        if (AttackCooldown > 0)
            AttackCooldown -= dt;
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
        Height += 30f * dt; // Rise rate
    }
}