# Structure Upgrades — Design Spec

- **Date:** 2026-10-06
- **Status:** implemented (cycles 1–3 green, 304/304 suite); open questions
  resolved in discussion — stockpile-only materials, three-tier shelter chain
  added, recipe queues cleared on tier swap by the dependency planner re-pick.
  Follow-up shipped same day: shelter tiers differentiate — all three tiers
  are rest candidates and storm shelters; recovery 0.16/0.18/0.20/s (fire
  stays 0.22). Suite 307/307.
- **Slice line:** development-map ordered next work #2 — "closes the last
  phase-3 checkbox" (`Add structure upgrades.` unchecked in the roadmap)
- **Design law:** tri-fusion balance rule — upgrades consume gathered
  materials and player-gated progression, so better buildings track the
  player's advancement instead of running ahead of it. Seasonal and weather
  pressure keeps applying outdoors (the woven-shelter precedent: shelter
  mitigates, never nullifies).

## Goal

Placed structures can be upgraded in place along declared chains:

- An upgrade is a new structure def (e.g. `wooden_gate` → `iron_gate`),
  built on top of an existing placed structure — no demolish/replace dance.
- Upgrading consumes stockpile materials and takes worker construction time,
  exactly like a blueprint build site (reuse the existing
  `IsUnderConstruction` / `ConstructionMaterialsPaid` flow).
- Gating is player-facing: `RequiresStructureLevel` and `RequiresSkillLevel`
  already exist on `StructureDef`; the upgrade path enforces them against
  the player's construction skill, the same check the build menu uses.
- Upgraded structures grant their stated benefit tier (more HP for
  walls/gates/traps, more damage for ballista-class, etc.) — the def fields
  already carry `Hp`/`Damage`; upgraded stats apply in place.
- Shelter precedent holds: a sturdier shelter extends mitigation radius or
  harsh-weather protection strength but never removes weather pressure.

## Non-goals (this slice)

- No automatic upgrade (a worker never starts one unprompted — the player
  enqueues each upgrade, same as they place each blueprint today).
- No multi-stage visual tiers / new sprites. Reuse existing sprite keys;
  an upgrade is `StructureId` swap + materials + progress.
- No upgrade refunds or downgrade path.
- No changes to structure *placement* (new building) rules — upgrades only
  act on already-built, active, non-blueprint structures.
- No UI panel in this slice beyond the build/inspect surface wiring the
  upgrade action; a dedicated upgrade designer UI is future work.

## Ground — verified in the tree 2026-10-06

- **Defs:** `Data/Structure.cs` — `StructureDef` has `RequiresStructureLevel`,
  `RequiresSkillLevel`, `Materials`, `Hp`, `Damage`, `SpriteKey`.
  `structures.json` already encodes natural upgrade pairs: `wooden_gate`
  → `iron_gate`, `wooden_trap` → `iron_trap`, plus tiered cooking/crafting
  stations (campfire/cooking_station, crafting_station/anvil…). Level/skill
  gates are enforced at placement today.
- **Placed structure:** `Building/Structure.cs` — one instance per placed
  building with `StructureId`, `StructureDef`, `IsUnderConstruction`,
  `ConstructionMaterialsPaid`, `WorkProgress`, `Health`/`MaxHealth`.
- **Construction flow:** `RecruitmentSystem` construction dispatcher (the
  `FindConstructionSite` path) charges materials from the colony stockpile,
  marks `ConstructionMaterialsPaid`, sets `WorkStatus` phases, and a worker
  walks to the site and accumulates progress. An upgrade is the same flow
  pointed at an existing structure instead of an empty blueprint.
- **Build gating:** the build path checks `RequiresStructureLevel` /
  `RequiresSkillLevel` against the player's construction skill (the exact
  check to mirror lives in the build/place code — confirm at implementation
  planning time; Candidate: `Core/Game.cs` build action or
  `UI/HudWindows.cs` build panel).
- **Sleep/weather:** rest recovery at `woven_shelter` is a code constant
  table in `RecruitmentSystem.TickResidentRest` (0.16/s bench/shelter).
  Any shelter-upgrade benefit rides through the same table keyed by
  structure id, keeping "mitigates, never nullifies."

## Design

### Upgrade chains (data, not code)

Add an optional `upgrades_to` field to `StructureDef` (single successor id,
keeping it a def-level fact). Seed from existing pairs:

- `wooden_gate` → `iron_gate`
- `wooden_trap` → `iron_trap`
- (further pairs chosen at implementation time from `structures.json` tiers)

Chains are linear; a def with no `upgrades_to` is a terminal tier.

### Upgrade lifecycle

1. Player targets a placed, active structure that has `upgrades_to` set.
2. Validation: successor def exists; player construction skill meets the
   successor's `RequiresSkillLevel`; structure is not under construction;
   colony (or player inventory, mirroring the build menu's rule) can pay
   the successor's `Materials`.
3. Structure flips to `IsUnderConstruction = true` and becomes a
   construction job like a blueprint: workers fetch materials and build.
4. On completion: `StructureId`/`StructureDef`/`MaxHealth`/`Health`/
   `SpriteKey` swap to the successor, `IsUnderConstruction = false`. Position,
   assignment, and work queues survive (recipe queues keep their meaning only
   where the successor accepts the same recipes — otherwise clear and let
   the dependency planner re-pick).

### What stays put

- Damage/repair rules, seasonal pressure, night floor, schedule dispatch:
  untouched. An upgraded wall has more HP; it does not change when workers
  work.
- Material bookkeeping: upgrade materials are charged from the stockpile via
  the same `ColonySystem.RemoveItem` path blueprints use.

## Testing plan

- **Def parsing:** a fixture def with `upgrades_to` loads the successor id;
  missing/absent field → null.
- **Validation:** can't upgrade past skill gate; can't upgrade a mid-build
  blueprint; can't upgrade a terminal tier.
- **Lifecycle:** enqueue upgrade → materials reserved from stockpile →
  worker builds → successor def/HP/sprite applied, position and assignment
  retained → rest/save-load snapshot roundtrips the upgraded structure.
- **No-regression:** an unchanged structure behaves exactly as today
  (construction, work dispatch, rest).

## Open questions for review

1. Material source: colony stockpile only (current blueprint behavior), or
   allow player-inventory top-up the way the build menu does?
2. Shelter benefit shape: is there a second shelter tier in `structures.json`
   intended for the woven_shelter line, or does "shelter mitigates" stay
   single-tier this slice?
3. Workplace recipe compatibility on upgrade: clear the queue on tier swap,
   or keep recipes that exist on both defs?
