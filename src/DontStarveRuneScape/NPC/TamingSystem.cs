namespace DontStarveRuneScape.NPC;

using System;
using System.Collections.Generic;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Skills;

/// <summary>
/// TamingMath — the pure window math for a tame roll, kept static and
/// hook-free so the windows are testable without any world state
/// (raid/rare-drop precedent). Window: 35% at parity, ±5% per taming-level
/// delta vs the species gate, +2% per success_rate sub-stat point,
/// clamped [5%, 95%].
/// Spec: docs/superpowers/specs/2026-10-09-animal-taming-design.md
/// </summary>
public static class TamingMath
{
    public const double BaseChance = 0.35;
    public const double PerLevelDelta = 0.05;
    public const double PerSuccessRatePoint = 0.02;
    public const double MinChance = 0.05;
    public const double MaxChance = 0.95;

    public static double Chance(int tamingLevel, int speciesTameLevel, float successRatePoints)
    {
        double c = BaseChance
            + (tamingLevel - speciesTameLevel) * PerLevelDelta
            + successRatePoints * PerSuccessRatePoint;
        return Math.Clamp(c, MinChance, MaxChance);
    }

    /// <summary>True when the roll lands inside the window.</summary>
    public static bool TameChance(double roll, int tamingLevel, int speciesTameLevel, float successRatePoints)
        => roll < Chance(tamingLevel, speciesTameLevel, successRatePoints);
}

/// <summary>
/// TamingSystem — feed-to-tame runtime. Owns the player's tamed animals
/// (pets): the first pet bonds as the follower (companion slot), later
/// pets are colony-assigned (wander near the tame site / colony anchor,
/// guard hostiles inside GuardRadius). Pets are RecruitNpc-shaped with
/// RecruitBehavior "pet"; they are NOT registry NPCs, so persistence is
/// a dedicated snapshot here (registry-missing rows vanish on the NPC
/// restore path).
/// </summary>
public sealed class TamingSystem
{
    public const float AttemptXp = 4f;
    public const float SuccessXp = 25f;
    public const float GuardRadiusTiles = 6f;
    private const float WalkSpeed = 42f; // matches CompanionBehavior

    /// <summary>Roll override for tests: null = rng, "" = forced fail,
    /// any other string = forced success (raid/rare-drop pattern).</summary>
    public string? RollOverride { get; set; }

    /// <summary>Seeded rng hook; defaults to a shared instance.</summary>
    public Random? Rng { get; set; }

    /// <summary>Every animal ever tamed by this player, alive or not
    /// handled here (death is permanent + visible).</summary>
    public List<RecruitNpc> TamedAnimals { get; } = [];

    /// <summary>The bonded follower's recruit id, if any.</summary>
    public string? FollowingPetId { get; private set; }

    private int _nextSerial = 1;

    /// <summary>Result shape mirrors CombatHitResult: success + player-facing message.</summary>
    public readonly record struct TameResult(bool Success, string Message);

    /// <summary>Feed a tamable monster: consume its species food, roll,
    /// and on success replace the wild monster with a pet.</summary>
    public TameResult TryTame(Player player, Monster monster, CombatSystem combat)
    {
        var def = monster.Def;
        if (def == null || !def.Tamable || string.IsNullOrEmpty(def.TameFood))
            return new TameResult(false, $"You can't tame the {monster.Name}.");

        var skills = player.SkillManager ?? new SkillManager();
        int level = skills.GetSkill("taming")?.Level ?? 1;
        if (level < def.TameLevel)
        {
            // Gated attempts are a refusal, not a loss — food NOT consumed.
            return new TameResult(false,
                $"You need taming level {def.TameLevel} to tame a {monster.Name}.");
        }

        var inv = player.Inventory;
        if (inv == null || inv.GetItemQuantity(def.TameFood) < 1)
            return new TameResult(false,
                $"The {monster.Name} eyes you warily. It wants {def.TameFood}.");

        // Food is spent even on failure (bait-at-cast precedent: the
        // attempt is the honest cost).
        inv.RemoveItem(def.TameFood, 1);
        skills.AddXpWithNotification("taming", AttemptXp);

        bool success = RollOverride switch
        {
            null => TamingMath.TameChance(
                (Rng ?? Random.Shared).NextDouble(), level, def.TameLevel,
                skills.GetSkill("taming").SubStats.TryGetValue("success_rate", out var sr) ? sr : 0f),
            "" => false,
            _ => true,
        };

        if (!success)
            return new TameResult(false, $"The {monster.Name} snarls and backs off.");

        skills.AddXpWithNotification("taming", SuccessXp);
        combat.Monsters.Remove(monster);

        var pet = new RecruitNpc
        {
            NpcId = $"pet_{def.MonsterId}_{_nextSerial++}",
            Name = def.Name,
            IsRecruited = true,
            RecruitBehavior = "pet",
            SpeciesId = def.MonsterId,
            WorldX = monster.WorldX,
            WorldY = monster.WorldY,
            Health = monster.Health,
            MaxHealth = monster.MaxHealth,
            ColonyHunger = 100f,
            ColonyRest = 100f,
        };
        if (FollowingPetId == null)
        {
            FollowingPetId = pet.NpcId;
            pet.IsColonyAssigned = false;
        }
        else
        {
            pet.IsColonyAssigned = true;
        }
        TamedAnimals.Add(pet);
        return new TameResult(true, $"The {monster.Name} is yours now.");
    }

    /// <summary>Follower tick: close on the player at walk speed with the
    /// same 14-tile drop-off / 7-tile resume contract as CompanionBehavior.</summary>
    public void Tick(float dt, RecruitNpc pet)
    {
        if (pet.IsColonyAssigned) return; // colony pets tick via TickColonyGuard
        float dx = PlayerX - pet.WorldX, dy = PlayerY - pet.WorldY;
        TickFollow(dt, pet, dx, dy);
    }

    /// <summary>Player position for follower steering (set by the game loop
    /// or a test before Tick).</summary>
    public float PlayerX { get; set; }
    public float PlayerY { get; set; }

    private const float TetherTiles = 14f;
    private const float ResumeTiles = 7f;
    private const float FollowStopTiles = 1.5f;
    private readonly Dictionary<string, float> _gapTimers = [];

    private void TickFollow(float dt, RecruitNpc pet, float dx, float dy)
    {
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float elapsed = Math.Min(dt, 0.25f);

        if (pet.CarryStatus == "Left behind")
        {
            if (distance > ResumeTiles * 64f) { pet.VelocityX = pet.VelocityY = 0f; return; }
            pet.CarryStatus = string.Empty;
        }
        if (distance > TetherTiles * 64f)
        {
            _gapTimers.TryGetValue(pet.NpcId, out float gap);
            gap += elapsed;
            _gapTimers[pet.NpcId] = gap;
            if (gap >= 2f)
            {
                _gapTimers[pet.NpcId] = 0f;
                pet.CarryStatus = "Left behind";
                pet.VelocityX = pet.VelocityY = 0f;
                return;
            }
        }
        else _gapTimers[pet.NpcId] = 0f;

        if (distance > FollowStopTiles * 64f && distance > 0.001f)
        {
            float step = WalkSpeed * elapsed;
            pet.WorldX += dx / distance * step;
            pet.WorldY += dy / distance * step;
            pet.VelocityX = dx / distance * WalkSpeed;
            pet.VelocityY = dy / distance * WalkSpeed;
            pet.CarryStatus = "Following you";
        }
        else
        {
            pet.VelocityX = pet.VelocityY = 0f;
            pet.CarryStatus = "Beside you";
        }
    }

    /// <summary>Colony-assigned pet tick: engage the nearest live hostile
    /// inside GuardRadius of the pet's post; otherwise hold position
    /// (wander is cosmetic and deferred).</summary>
    public void TickColonyGuard(float dt, RecruitNpc pet, CombatSystem combat)
    {
        Monster? nearest = null;
        float bestSq = float.MaxValue;
        float radiusSq = MathF.Pow(GuardRadiusTiles * 64f, 2f);
        foreach (var m in combat.Monsters)
        {
            if (!m.IsAlive() || !m.IsHostile) continue;
            float dx = m.WorldX - pet.WorldX, dy = m.WorldY - pet.WorldY;
            float sq = dx * dx + dy * dy;
            if (sq <= radiusSq && sq < bestSq)
            {
                bestSq = sq;
                nearest = m;
            }
        }
        if (nearest == null)
        {
            pet.VelocityX = pet.VelocityY = 0f;
            pet.CarryStatus = "Guarding";
            return;
        }

        // Engage: close if needed, then strike at the pet's damage cadence.
        pet.CarryStatus = "Chasing";
        float dist = MathF.Sqrt(bestSq);
        if (dist > 48f)
        {
            float elapsed = Math.Min(dt, 0.25f);
            float dx = (nearest.WorldX - pet.WorldX) / dist, dy = (nearest.WorldY - pet.WorldY) / dist;
            pet.WorldX += dx * WalkSpeed * elapsed;
            pet.WorldY += dy * WalkSpeed * elapsed;
            pet.VelocityX = dx * WalkSpeed;
            pet.VelocityY = dy * WalkSpeed;
            return;
        }
        int damage = Math.Max(1, (int)MathF.Round(4f - nearest.Defence));
        nearest.Health -= damage;
        pet.CarryStatus = "Fighting!";
    }

    // ── Persistence ──────────────────────────────────────────────────

    public sealed class TamedAnimalRecord
    {
        public string NpcId { get; set; } = string.Empty;
        public string SpeciesId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public float WorldX { get; set; }
        public float WorldY { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public bool IsColonyAssigned { get; set; }
    }

    public List<TamedAnimalRecord> BuildSnapshot()
    {
        var list = new List<TamedAnimalRecord>();
        foreach (var pet in TamedAnimals)
        {
            list.Add(new TamedAnimalRecord
            {
                NpcId = pet.NpcId,
                SpeciesId = pet.SpeciesId,
                Name = pet.Name,
                WorldX = pet.WorldX,
                WorldY = pet.WorldY,
                Health = pet.Health,
                MaxHealth = pet.MaxHealth,
                IsColonyAssigned = pet.IsColonyAssigned,
            });
        }
        return list;
    }

    public void RestoreSnapshot(List<TamedAnimalRecord> snapshot, Player player)
    {
        TamedAnimals.Clear();
        FollowingPetId = null;
        foreach (var rec in snapshot)
        {
            var pet = new RecruitNpc
            {
                NpcId = rec.NpcId,
                Name = rec.Name,
                SpeciesId = rec.SpeciesId,
                IsRecruited = true,
                RecruitBehavior = "pet",
                WorldX = rec.WorldX,
                WorldY = rec.WorldY,
                Health = rec.Health,
                MaxHealth = rec.MaxHealth,
                IsColonyAssigned = rec.IsColonyAssigned,
                ColonyHunger = 100f,
                ColonyRest = 100f,
            };
            if (!pet.IsColonyAssigned && FollowingPetId == null)
                FollowingPetId = pet.NpcId;
            TamedAnimals.Add(pet);
            // Keep serials unique after restore: any numeric suffix larger
            // than _nextSerial moves the counter past it.
            int dash = pet.NpcId.LastIndexOf('_');
            if (dash >= 0 && int.TryParse(pet.NpcId[(dash + 1)..], out int n) && n >= _nextSerial)
                _nextSerial = n + 1;
        }
    }
}
