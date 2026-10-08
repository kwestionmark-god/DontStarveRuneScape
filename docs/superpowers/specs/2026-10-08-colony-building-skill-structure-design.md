# Colony-building skill structure — design

Date: 2026-10-08
Status: implemented (main `4c855f2`; RED `a439669`)
Slice: post-roadmap frontier, item 6 — the last standing frontier line
(dev-map "Ordered next work" item 6): "Colony-building skill structure —
player skills shaping what colonies can build and do."

## Design law

- **RS**: progression gates everything — the colony never out-builds the
  player's construction skill. Structures above the player's level are
  visible but unplaceable, un-upgradeable, and un-workable until the
  player levels up or recruits/skills around them.
- **DS**: the grind stays honest — structures are still placed with
  player-fetched materials through the existing construction skill gates
  (structure-upgrades slice); the colony extends the player's craft,
  never replaces it.
- **MC**: readability — the build menu already grays out gated entries
  (`Panels2.1335`); this slice extends the same "Requires construction
  Lv N" vocabulary to colony work orders so blocked work is visible and
  explains itself.

## Goal

Colony capability follows the player's progression: the structures the
colony can work depend on what the player can build. Four rules:

1. **Colony recruiters**: compat — no change. Recruitment gates already
   exist against commerce/persuasion; untouched.
2. **Work orders gated by structure tier**: the normal work-order panel
   shows contested entries with the structure's `RequiresSkillLevel` as
   the gate — a Work order on a structure the player couldn't build is
   refused ("Colonel needs construction level N"), not queued.
3. **Colony-built structures**: the player may place a colony-funded
   blueprint above their level (a build order, not a personal build) —
   workers construct it, but produce nothing until the player reaches
   the def's level. The colony dashboard shows "Awaiting builder
   competence" on such stations: visible, not silent.
4. **Grow-from**: the colony's meal/garden/order UI never queues work
   the player couldn't personally take at their level (same gate as the
   work-order panel). Everything else (NPC levels, deliveries, raids,
   companion) is untouched.

## Scope note — resource-node levels

While this slice is in dispatch code it absorbs a pre-existing hole:
the assistant harvest path never checks `ResourceDef.RequiredLevel`
against a recruit's own gathering level (`GatherSkillFor` mapping), so
a level-1 recruit could strip-mine `gold_vein`. The node-selection loop
in `RecruitmentSystem.Tick` now refuses any node whose
`RequiredLevel > recruit.Skills.GetSkillLevel(GatherSkillFor(def))` —
visibility rides `ColonyNeedStatus = "Skill too low"` per tick so the
colonist's row isn't silent. This is the same "the grind stays honest"
law as rule 3; it is in scope only because the dispatch code is the
touch point and the fix is one filter + one status.

## Non-goals

- Per-structure worker skill requirements beyond the place gate
  (assistants at level 1 work any station whose blueprint the player
  could place — the colony extension of the player's craft).
- A separate colony-wide construction level, XP, or tech tree — the
  player's own skill is the single queue.
- Changing the build-menu gating already live (structures above the
  player's level stay grayed out with the existing "Lv N" row).

## Design

### The gate helper (one place both sides read)

New static helper on `BuildingSystem.Registry` def data:

```csharp
bool PlayerCanOperate(SkillManager skills, StructureDef def)
    => skills.GetSkillLevel("construction") >= def.RequiresSkillLevel;
```

### Placement / upgrade: unchanged

`BuildingSystem.PlaceStructure` and `UpgradeStructure` already gate on
the player (`BuildingSystem.cs:56`, `:150`).

### Colony work orders (Panels2.cs:2203-2228 + structure dispatch)

The "set as active order" and "queue order" buttons consult
`PlayerCanOperate(player.Skills, structure.StructureDef)`. Refusal:
`_colonyStatus = "Colony work gated — requires construction level N"`,
and the order is NOT set (WorkRecipeId/WorkOrdersPaused untouched).
Already-queued orders on a structure that later "outlevels" (a
hypothetical downgrade) are NOT switched off — skills only go up.

### Automation (RecruitmentSystem.Tick line ~335: `workplace.WorkStatus = "Worker en route"`)

The per-tick assignment claim in the main worker loop consults the
gate: if the player couldn't place the station, the worker doesn't bind
(`claimedWorkplaces.Add` never runs) and the structure shows
`"Awaiting builder competence"` — visible on the colonist's dashboard.
Compensation: the station is skipped only once per tick; on the next
tick a different same-structure station may claim if viable. On player
level-up the stations re-enter autonomously (recomputed each tick, no
migration step).

### Data — no new fields

`Structure.Def.RequiresSkillLevel` exists; persistence rides the
existing settlement/NPC snapshot.

## Testing plan

New file `ColonySkillGateTests.cs` in the established Harness shape
(ColonyWorkerBrainTests pattern), `skills.AddXpWithNotification("construction", XpForLevel(n)+1f)`,
clock-free ticks:

1. **Manual work order refused**: colony + staffed furnace (level-8
   gate), player at construction 1 — queueing "semi_smelt_smelting"
   refuses with the gate message, WorkStatus unchanged.
2. **Auto-production skips above-tier**: colony with logs, a smelter
   (level 10), an assistant at level 1 in dispatch — with the player at
   construction 1 the smelter shows "Awaiting builder competence" and
   the worker gathers instead.
3. **Level-up unlocks**: same setup; the player reaches construction 10
   via `AddXpWithNotification`; next tick the smelter works.
4. **Placed-below-always-works**: a campfire-gated station with the
   player at level 1 works normally.
5. **Persistence**: a founded colony with an above-tier smelter survives
   a save/load; the status still reports "Awaiting builder competence".

## Open questions

Answered with defaults per standing delegation:

- "Above-level" NPCs (a recruited elder blacksmith): not this slice —
  the gate is the player's, not the worker's.
- Refugee/acquired stations above the player's level: same rule — the
  colony shows the gate, waits for the player.
- Traps/defensive structures: same gate as stations (uniform rule).
