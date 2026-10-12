# Monster-vs-structure wall damage (vertical building slice 11)

Date: 2026-10-12
Status: implemented — RED 95bb0bb / GREEN 4603bb2; suite 648 total /
647 green / 1 pre-existing skip (state-asserted, no render changes)
Slice line: vertical-building post-roadmap, slice 11 (the parked
"monster-vs-structure damage" follow-up from the slice-9 clarify
round, now un-parked by the user 2026-10-12)

## Design law

Walls buy time, not invincibility. A monster blocked by a player-built
wall while pursuing a target grinds at it, and sustained frustration
turns to violence: it chips the nearest wall piece at its own attack
cadence until the wall breaks — then the chase resumes through the
gap. A wandering monster that merely bumps a wall grinds past it and
moves on.

## Goal

1. **Frustration timer.** A monster accumulates wall frustration while
   a chase/flee step is blocked on any axis (the slice-9 axis-slide
   report); 4 seconds of sustained blocking (Constants
   `MonsterWallFrustrationSeconds`) triggers wall attacks. Wandering
   never accumulates. User decision 2026-10-12.
2. **Wall attack.** At the frustration threshold the monster chips
   the nearest BUILT, ACTIVE block structure within melee reach
   (its `AttackRange`), damage = its Attack stat at its own
   `AttackCooldownMax` cadence (the shared melee cooldown — a monster
   alternates wall hits and target hits honestly). Bricks hp 20,
   fences hp 10: one wolf (attack 3) needs ~10 s per brick; a 4-raider
   party ~2.5 s. User decision 2026-10-12.
3. **Destruction is honest.** `BuildingSystem.DamageStructure` chips
   Health; at zero the structure is removed through the slice-9 LIVE
   occupancy recompute (the tile immediately becomes pathable again),
   and the player is notified per piece — "Your Dirt Brick was
   destroyed!" — no refund, per the no-silent-losses rule. User
   decision 2026-10-12.
4. **Chase resumes.** Frustration resets on destruction; the existing
   beeline takes the monster through the gap (and the freed tile lets
   workers re-path through it too).

## Non-goals

- No monster pathfinding around walls (still parked — beeline +
  grind + now break-through).
- No wall retaliation AI, no guards/pets wall-specific behavior
  (guards already engage hostiles in range; nothing new needed).
- No player-caused structure damage (a player demolish feature can
  reuse `DamageStructure` later).
- No structure repair this slice (a follow-up can add it on the same
  seam).
- No render changes: the wall vanishing is `RemoveStructure` doing
  its job; verified by state asserts, not pixels.

## Ground (verified in the tree 2026-10-12)

- Blocked-step reporting: `CombatSystem.MoveToward`/`MoveAway` apply
  `BlockStack.GroundedSlide` when world/buildings are passed (slice 9,
  CombatSystem.cs:454-481) but return void — they need a
  `bool` return (any axis blocked) for the frustration counter. Both
  are private static; call sites are the chase branch
  (UpdateMonster ~:371) and flee branch (~:340) — target-driven
  movement only; patrol (~:410) is the wander path and must NOT
  accumulate.
- Chase branch shape: `UpdateMonster` (:320+) — engaged check, melee
  attack at `dist <= m.AttackRange`, else `MoveToward`; the flee
  branch mirrors it with `MoveAway`. `m.AttackCooldown` is consumed by
  the melee hit and decremented in `UpdateTimers` (monster.cs —
  UpdateTimers(dt) per tick, Game.cs:2036).
- Melee cadence fields on Monster: `Attack`, `AttackRange` (default
  64), `AttackCooldown`, `AttackCooldownMax` (default 1.5 s)
  (CombatSystem.cs:489-495).
- Damage target: block structures carry `Health`/`MaxHealth` ints
  (`mound_block` hp 20, `fence_block` hp 10 — structures.json:13/:28;
  set at placement BuildingSystem.cs:127-128). Nothing damages them
  today (grep `\.Health =` over BuildingSystem — only placement).
- Removal with live truth: `RemoveStructure(structure, world)` +
  the tile-slot recompute (slice 9, BuildingSystem.cs:264-283) — the
  tile frees for `WorkerPathfinder.CanStand` immediately.
- Notifications: systems notify via
  `player?.ActionSystem?.AddNotification(text, (r, g, b))`
  (MerchantVisitSystem.Notify :182-184); tests `FlushNotifications()`
  before asserting (the pending-queue law). `UpdateMonster` already
  receives the player — no new wiring.
- Reach: a grinding monster sits at the wall face; the blocking
  structure's center is well inside `AttackRange` (64 px) — a fence
  tile center is ~38 px from a monster at the face (verified geometry:
  monster (250,352), fence tile (4,5) center (288,352)).
- Persistence: frustration is transient (resets on load — a raid that
  loads mid-siege starts its 4 s again; acceptable and invisible).

## Design

### 1. Blocked-step report (CombatSystem)

`MoveToward`/`MoveAway` return `true` when ANY axis component of the
desired step was refused by `GroundedSlide` (world != null only).

### 2. Frustration + wall attack (UpdateMonster)

```csharp
public float WallFrustration; // on Monster — transient, seconds
```

Chase branch (and the flee branch, same accumulation):

```csharp
bool blocked = MoveToward(m, targetX, targetY, dt, world, buildings);
AccumulateWallFrustration(m, blocked, dt, buildings, world, player);
```

Shared helper (chase/flee only — the wander path never calls it):

```csharp
/// <summary>Sustained blocking turns to violence (slice 11): after
/// MonsterWallFrustrationSeconds of grinding, chip the nearest built
/// block structure in melee reach at the monster's own cadence. A
/// broken wall resets frustration — the chase resumes through the
/// gap; the freed tile is immediately pathable (live occupancy).</summary>
private void AccumulateWallFrustration(Monster m, bool blocked, float dt,
    BuildingSystem? buildings, World.TileMap? world, Player? player)
```

- `blocked ? m.WallFrustration += dt : m.WallFrustration = 0` (a free
  tick always resets — the threshold means SUSTAINED grinding).
- Below `Constants.MonsterWallFrustrationSeconds`: return.
- Nearest target: ACTIVE, BUILT (`!IsUnderConstruction`),
  `StructureDef.IsBlock` structures with
  `dist² ≤ m.AttackRange²` — nearest wins.
- Cadence: `if (m.AttackCooldown > 0) return;` — then
  `buildings.DamageStructure(best, Math.Max(1, (int)MathF.Round(m.Attack)), world)`,
  `m.AttackCooldown = m.AttackCooldownMax`.
- On destroy: `m.WallFrustration = 0` +
  `player?.ActionSystem?.AddNotification($"Your {best.Name} was destroyed!", (255, 90, 90))`.

### 3. DamageStructure (BuildingSystem)

```csharp
/// <summary>Chip a built structure (slice 11: wall-attacking
/// monsters). Health -= amount; at zero the structure is removed with
/// the live occupancy recompute. Returns true when destroyed.</summary>
public bool DamageStructure(Structure structure, int amount, TileMap? world = null)
```

### 4. Constant

`MonsterWallFrustrationSeconds = 4f` (Constants, beside
MonsterIdleSeconds).

## Testing plan

RED (new `MonsterWallDamageTests`, harness copied from
NpcBlockCollisionTests — flat 8x8 pad, fence/brick staging,
HostileDef (attack 3, range 64, cooldown 1.5 s), CombatSystem.Tick
with world+buildings; staging law: player DIRECTLY east of the wall so
dy = 0 — no slide escape around a short wall inside the frustration
window):

1. `FrustratedRaider_BreaksThroughAndNotifies` — fence at (4,5),
   monster (250,352) west, player (352,352) east; 60 ticks:
   fence destroyed and removed, `WorkerPathfinder.CanStand(4,5)` true
   (live slot), the notification list (after FlushNotifications)
   contains "was destroyed", and the monster moved east of its start
   (chase resumed through the gap). RED: fence still standing.
2. `BelowFrustrationThreshold_NoDamage` — same staging, 8 ticks
   (2 s): fence Health still 10 and no notification. Pin (passes at
   RED; must stay green — frustration is not instant).
3. `WanderingMonster_NeverChipsWalls` — no player in aggro range,
   fence in the wander area, 60 ticks: Health still 10. Pin (passes
   at RED; wander must never accumulate).
4. `FrustratedRaider_ChipsBricksAtOwnCadence` — mound_block hp 20
   instead of the fence; tick past the frustration window + one
   cadence: Health < 20 (chipping observed) but the brick still
   standing (7 hits needed). RED: Health still 20.
5. `DamageStructure_AtZeroHealth_RemovesAndFreesTile` — direct unit:
   DamageStructure(fence, 10, world) → true + structure gone +
   CanStand true; partial damage returns false and keeps Health.
   RED: no such method — compile stub (behavior-free: return false).

GREEN: implement 1-4. Full suite 643 → 643+N green, 1 pre-existing
skip.

No pixel verification: no render-path changes; the wall's
disappearance is RemoveStructure state, asserted above.

## Open questions

None — frustration timing (4 s), damage source (monster Attack at own
cadence), destruction feedback (notification, no refund), and scope
(chase/flee only) all decided by the user 2026-10-12 (all recommended
defaults accepted).
