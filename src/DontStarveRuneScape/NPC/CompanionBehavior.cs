namespace DontStarveRuneScape.NPC;

using System;
using System.Collections.Generic;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.World;

/// <summary>
/// CompanionBehavior — the "companion" recruit behavior engine: at most one
/// bonded follower per player. Follows at walking pace (never teleports),
/// drops off with a visible "Left behind" status when the player outruns
/// the tether for a sustained gap (no rubber-banding across the map), and
/// flees when a hostile gets close to the pair instead of engaging.
/// Straight-line steering with standability gating; follow bypasses the
/// task board entirely — companions never claim colony work.
/// Spec: docs/superpowers/specs/2026-10-07-starting-companion-design.md
/// </summary>
public sealed class CompanionBehavior
{
    private const float WalkSpeed = 42f;        // matches RecruitmentSystem workers
    private const float FleeSpeed = WalkSpeed + 6f; // a frightened burst
    private const float TetherTiles = 14f;       // give-up distance
    private const float ResumeTiles = 7f;        // re-follow distance
    private const float FleeTriggerTiles = 4f;   // hostile near the companion OR the player
    private const float FollowStopTiles = 1.5f; // posts up this close, no overlap
    private const float GiveUpSeconds = 2f;      // sustained gap before dropping off

    private readonly Player _player;
    private readonly Dictionary<string, float> _gapTimers = [];

    /// <summary>The one companion currently bonded to the player; first
    /// registered wins, later companions idle until the bond clears.</summary>
    public string? FollowingNpcId { get; private set; }

    /// <summary>Hostile population the flee response reads (test-optional).</summary>
    public CombatSystem? Combat { get; set; }

    /// <summary>World used for standability gating (test-optional).</summary>
    public TileMap? World { get; set; }

    public CompanionBehavior(Player player)
    {
        _player = player;
    }

    /// <summary>Identity check: is this engine bound to the given player?</summary>
    public bool BoundTo(Player player) => ReferenceEquals(_player, player);

    /// <summary>Explicit re-bond (recruitment flow): the recruited companion
    /// takes the bond, replacing any previous holder.</summary>
    public void Bond(string npcId) => FollowingNpcId = npcId;

    /// <summary>Bond request: the first companion registered keeps the bond;
    /// a second companion never steals it.</summary>
    public void Register(RecruitNpc npc)
    {
        if (npc.RecruitBehavior != "companion") return;
        if (FollowingNpcId == null) FollowingNpcId = npc.NpcId;
    }

    /// <summary>Clear the bond when its holder is dismissed or re-rolled.</summary>
    public void Release(string npcId)
    {
        if (FollowingNpcId == npcId) FollowingNpcId = null;
    }

    /// <summary>One sim step for a companion recruit.</summary>
    public void Tick(float dt, RecruitNpc npc)
    {
        if (npc.RecruitBehavior != "companion")
        {
            npc.VelocityX = npc.VelocityY = 0f;
            return;
        }
        if (FollowingNpcId == null) FollowingNpcId = npc.NpcId; // direct-tick auto-bond
        if (npc.NpcId != FollowingNpcId)
        {
            // Not the bonded companion: idle, no follow stealing.
            npc.VelocityX = npc.VelocityY = 0f;
            npc.CarryStatus = "Idle";
            return;
        }

        float elapsed = Math.Min(dt, 0.25f);
        float dx = _player.WorldX - npc.WorldX;
        float dy = _player.WorldY - npc.WorldY;
        float distance = MathF.Sqrt(dx * dx + dy * dy);

        // Dropped off: stand still until the player comes back inside resume range.
        if (npc.CarryStatus == "Left behind")
        {
            if (distance > ResumeTiles * Constants.TileSize)
            {
                npc.VelocityX = npc.VelocityY = 0f;
                return;
            }
            npc.CarryStatus = string.Empty;
        }

        // A sustained out-of-tether gap gives up the chase.
        if (distance > TetherTiles * Constants.TileSize)
        {
            _gapTimers.TryGetValue(npc.NpcId, out float gap);
            gap += elapsed;
            _gapTimers[npc.NpcId] = gap;
            if (gap >= GiveUpSeconds)
            {
                _gapTimers[npc.NpcId] = 0f;
                npc.CarryStatus = "Left behind";
                npc.VelocityX = npc.VelocityY = 0f;
                return;
            }
        }
        else
        {
            _gapTimers[npc.NpcId] = 0f;
        }

        // Flee: the nearest live hostile close to the companion OR the player.
        Monster? threat = null;
        float threatDistSq = float.MaxValue;
        float triggerSq = MathF.Pow(FleeTriggerTiles * Constants.TileSize, 2f);
        if (Combat != null)
        {
            foreach (var monster in Combat.Monsters)
            {
                if (!monster.IsAlive() || !monster.IsHostile) continue;
                float selfDx = monster.WorldX - npc.WorldX;
                float selfDy = monster.WorldY - npc.WorldY;
                float selfSq = selfDx * selfDx + selfDy * selfDy;
                float playerDx = monster.WorldX - _player.WorldX;
                float playerDy = monster.WorldY - _player.WorldY;
                float playerSq = playerDx * playerDx + playerDy * playerDy;
                bool triggered = selfSq <= triggerSq || playerSq <= triggerSq;
                float nearer = Math.Min(selfSq, playerSq);
                if (triggered && nearer < threatDistSq)
                {
                    threatDistSq = nearer;
                    threat = monster;
                }
            }
        }
        if (threat != null)
        {
            float awayX = npc.WorldX - threat.WorldX;
            float awayY = npc.WorldY - threat.WorldY;
            float away = MathF.Sqrt(awayX * awayX + awayY * awayY);
            if (away > 0.001f && Step(npc, awayX / away, awayY / away, FleeSpeed, elapsed))
                npc.CarryStatus = "Fleeing!";
            return;
        }

        // Follow: close the gap to the player, then post up beside them.
        if (distance > FollowStopTiles * Constants.TileSize && distance > 0.001f)
        {
            if (Step(npc, dx / distance, dy / distance, WalkSpeed, elapsed))
                npc.CarryStatus = "Following you";
        }
        else
        {
            npc.VelocityX = npc.VelocityY = 0f;
            npc.CarryStatus = "Beside you";
        }
    }

    /// <summary>Straight-line step with standability gating; returns false
    /// when fully blocked (the caller keeps any prior status).</summary>
    private bool Step(Npc npc, float dirX, float dirY, float speed, float dt)
    {
        float step = speed * dt;
        float nextX = npc.WorldX + dirX * step;
        float nextY = npc.WorldY + dirY * step;
        if (World != null && !WorkerPathfinder.CanStand(World,
                (int)(nextX / Constants.TileSize), (int)(nextY / Constants.TileSize)))
        {
            // Blocked straight ahead — try sliding along one axis instead.
            bool xFree = WorkerPathfinder.CanStand(World,
                (int)(nextX / Constants.TileSize), (int)(npc.WorldY / Constants.TileSize));
            bool yFree = WorkerPathfinder.CanStand(World,
                (int)(npc.WorldX / Constants.TileSize), (int)(nextY / Constants.TileSize));
            if (xFree)
            {
                npc.WorldX = nextX;
                dirY = 0f;
            }
            else if (yFree)
            {
                npc.WorldY = nextY;
                dirX = 0f;
            }
            else
            {
                npc.VelocityX = npc.VelocityY = 0f;
                return false;
            }
        }
        else
        {
            npc.WorldX = nextX;
            npc.WorldY = nextY;
        }
        npc.VelocityX = dirX * speed;
        npc.VelocityY = dirY * speed;
        return true;
    }
}
