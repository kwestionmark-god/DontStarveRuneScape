# Per-Recruit Skill Stats — design

Date: 2026-10-07
Status: draft
Slice: post-roadmap frontier, item 1 (dev-map "Ordered next work" item 6)

## Design law

RS pillar: every recruit carries personal skill progression like the
player's — work trains the relevant skill, levels make the recruit
measurably better at that work, and the player's high-level recruits are
an asset built over time, not hired pre-made.
DS pillar: recruits start weak and grow through surviving seasons of
work; nothing here removes pressure — XP arrives only through real
dispatched labour, never passively on the clock.
MC pillar: a recruit's skills are legible from existing colony surfaces —
the dashboard shows the headline level and the work the recruit is doing
feeds it.

## Goal

Every colony recruit (`RecruitNpc`) carries its own `SkillManager`,
persisted with the rest of the NPC state. Successful work outcomes in
`RecruitmentSystem` award XP to the working recruit (in addition to the
player, where today the player alone gets it): gatherers train the
gathering skill of the node they harvest, builders train construction,
guards train attack through combat, crafters train the recipe's skill.
Recruit level feeds back into work at exactly one place this slice:
gatherers gain increased yield (extra harvests per interval) as their
gathering skill rises, matching how the player's gathering sub-stats
already improve output. Everything else stays cosmetic/visible-only for
this slice.

## Non-goals

- Recruits spending stat points or having sub-stats managed by the
  player (levels are the entire interface this slice).
- Combat-skill-driven damage scaling for guards (attack XP is recorded;
  scaling is a later slice).
- Skill gates on work orders (recruits do jobs at any level; level only
  improves yield).
- Leveling recipes' dynamic dependency changes (crafting dispatch keys
  off the player's skills, unchanged).
- A new UI panel — dashboard text only.

## Ground — verified in the tree

- Every `RecruitNpc` that `IsRecruited` with behavior
  "assistant"/"guard" ticks through `RecruitmentSystem.Tick`
  (`RecruitmentSystem.cs:100-110`).
- Worker success points that already award the **player** XP:
  - Construction/upgrade completion: `skills?.AddXp("construction", 10f)`
    (`RecruitmentSystem.cs:301`).
  - Cave mining with a tool: `skills?.AddXp("mining", xp)`
    (`RecruitmentSystem.cs:583`).
  - Harvest: `resource.Harvest(1f, world.SeasonSystem)` returns
    `(itemId, quantity, xp)` — today that `xp` is dropped for surface
    gathers (`RecruitmentSystem.cs:560`).
  - Recipe production: `crafting.Craft(recipeId, colony, skills, ...)`
    (`RecruitmentSystem.cs:383`) — skills is the player's manager.
  - Guards: `TickGuard(... skills ...)` already receives the player's
    SkillManager; combat outcomes flow through `CombatSystem`.
- `SkillManager.AddXp` alone does not raise `Level` — levels move only
  via `AddXpWithNotification` (established harness pitfall).
- NPC persistence: `NPCSystem.GetSnapshot` (NPCSystem.cs:122) and
  `RestoreSnapshot` (NPCSystem.cs:150) already round-trip recruit
  behavior via `NPCDataSnapshot` / `NPCSnapshot`
  (`SaveSystem.cs:419-440`).
- Harvest interval is a constant cadence (`HarvestInterval`, advanced by
  min(dt, 0.25) in `_workTimers`, line 113); a gather yields once per
  interval — the natural unit for a yield-boost hook is an extra yield
  every N intervals, seeded deterministically per recruit.

## Design

### Skills manager per recruit

`RecruitNpc` gains a `Skills` property: a lazily-constructed
`SkillManager` instance. Construction is lazy so existing tests that
build recruits directly do not change shape. Guards and assistants use
the same manager — selection of which skill receives XP is per action,
not per behavior class.

### XP awards

All recruit XP uses `AddXpWithNotification` internally but the messages
are discarded (recruits have no notification channel); level-ups are
surfaced through the dashboard line instead of chat spam.

| Action                          | Skill         | Amount                          |
| ------------------------------- | ------------- | ------------------------------- |
| Gather yield delivered/loaded   | node's skill  | the `xp` the node already pays  |
| Construction site completes     | construction  | 10 (matches player award)       |
| Recipe production succeeds      | recipe skill  | recipe's own XP value (if any;  |
|                                 |               | fallback 5)                     |
| Guard combat hit landed         | attack        | damage dealt (rounded, min 1)   |

Gather-skill mapping reuses the existing convention: nodes that
`RequiresTool` train mining, woodcutting-yield nodes train woodcutting,
everything else trains foraging. (The tree already distinguishes cave
miners by `RequiresTool`; extend the same rule on the surface.)

### Yield scaling (the one functional feedback)

A gatherer with gathering/woodcutting/mining level L yields one extra
harvest unit every `max(1, 20 - 2 * L)` intervals, deterministic per
recruit (counter in the recruit's state, no RNG). At level 1: 1 extra
per 18 (~5.5% boost); level 10: every interval yields double. This is
the recruit-side mirror of the player's harvesting sub-stats.

### Persistence

`NPCDataSnapshot` gains a `SkillSnapshot Skills` field
(`SaveSystem.cs`); `NPCSystem.RestoreSnapshot` restores it when the NPC
is a `RecruitNpc`, and old saves without the field load fine (field
null → fresh manager).

### Dashboard surface

The colony dashboard's colonist row gains the recruit's
highest-non-attack... — actually simpler: the row shows the skill
relevant to the recruit's current job: guards show attack level,
assistants on a gather job show that node's skill level, builders show
construction. Fallback for idle: highest skill. Rendering stays in the
existing scrollable list (Panels2 colony dashboard) — no new panel.

## Testing plan

New file `RecruitSkillTests.cs` in the established Harness shape
(TileMap, Player, NPCSystem, ColonySystem, Registry/LoadAll, etc.):

1. Recruited worker gathers a forage node to depletion — the recruit's
   foraging XP equals the summed node XP; the recruit's mining XP stays
   0.
2. Construction worker completes a small structure — recruit
   construction XP == 10, and leveling crosses when enough completions
   are simulated (posited explicit `XpForLevel` math in the test).
3. Guard attack XP: a guard fighting a spawned monster gains attack XP
   proportionate to damage dealt; an idle guard gains none.
4. Yield feedback: a level-9 forager (set up via
   `recruit.Skills.AddXpWithNotification` per the harness pitfall)
   yields strictly more units over a fixed depletion window than a
   level-1 forager working the same node type.
5. Persistence round-trip: recruit XP/level survives
   `NPCSystem.GetSnapshot`/`RestoreSnapshot`; an old-shape snapshot
   (Skills absent) restores to a fresh level-1 manager.
6. Player XP unchanged: the player's awards from the same actions are
   exactly what the 332-baseline suite already asserts (regression).

## Open questions

Answered with defaults per standing delegation:

- Guard attack XP per hit (flat 2) vs per damage — chose per damage;
  mirrors OSRS XP-per-damage.
- Recipe XP value — recipes don't currently carry an XP field; this
  slice uses flat 5 and notes the field as a future tweak in the
  structures/recipes data.
- Whether assistants gather at player-level or recruit-level — recruit
  level only; the player's level never gates a recruit's work (the
  player-authored structure does the gating).
