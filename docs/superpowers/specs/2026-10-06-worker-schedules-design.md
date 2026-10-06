# Worker Schedules — Design Spec

- **Date:** 2026-10-06
- **Status:** approved in discussion; awaiting implementation plan
- **Slice line:** colony-fusion roadmap phase 4 ("Give workers schedules and simple
  task selection, with sensible fallbacks")
- **Design law:** tri-fusion balance rule — schedules give the settlement rhythm
  and readability; they must not maximize output or automate away player loops.

## Goal

Recruited workers live on a daily schedule keyed to the day/night clock:

- Non-guard workers work daylight hours, take meals at meal hours, spend a
  short evening at camp, and sleep at night at the fire/bench.
- Guards split into DAYTIME/NIGHTTIME shift squads so the settlement keeps a
  watch during both halves of the cycle.
- Urgent needs (starving, exhausted, nearby threat) override schedules.
- Outdoor colony work refuses dispatch at night for non-guards (hard safety
  floor), independent of what any schedule says.

## Non-goals (this slice)

- No schedule-editing UI. The per-NPC 24-slot data shape keeps the door open.
- No per-NPC schedule overrides.
- No darkness damage for anyone (night safety = floor + fire, not damage).
- No cave schedules — cave workers keep today's always-work behavior
  (expedition mode; the cave map has no surface clock).
- No new needs (no thirst etc.), no changes to decay/recovery rates.

## Ground — verified in the tree 2026-10-06

All integration points below were read in the live code before writing this.

- **Worker brain:** `RecruitmentSystem.Tick` (`NPC/RecruitmentSystem.cs`, ~1231
  lines) is the single dispatcher. Per recruited NPC with behavior
  `assistant`/`guard`, the order today is: `TickResidentRest` (returns true
  while seeking/resting → skips everything else) → guard branch →
  `needsFood` (`ColonyHunger <= 0`) → workplace inputs → garden plot →
  construction site → haul-home if carrying → gather via task-board
  reservation. The brain has **no clock access today**.
- **Food:** `TickColonyNeeds` drains `ColonyHunger` at 0.04/s (clamped dt
  ≤ 0.25) and auto-eats from colony stores when hunger ≤ 20 — prefers cooked
  (`IsRaw` ascending), then best `HungerRestoration`. Statuses: Fed /
  Hungry (≤35) / Starving (≤15).
- **Rest:** `TickResidentRest` drains `ColonyRest` at 0.03/s (×1.25 winter,
  ×1.25 harsh weather unsheltered), rests once ≤ 60, recovers 0.22/s at a
  fire, 0.16/s at bench/shelter, 0.08/s bare (halved in harsh weather,
  ×0.75 winter without fire), wakes at ≥ 90. A hostile monster within
  3 tiles aborts rest. Rest places: `stone_bench`, `woven_shelter`, active
  fires, home tile.
- **Claims:** `ColonyTaskBoard` holds gather-tile reservations and
  haul-home tasks; `ReleaseAllFor(npcId)` is the single cleanup path — the
  existing rest interruption already uses it and keeps carried goods.
- **Clock:** `World/DayNightCycle` — `TimeOfDay` 0..1 with 0 = dawn 06:00,
  `HourOfDay`, `IsDay` (< 0.5); one game hour = one real minute
  (`DayLengthSeconds`/`NightLengthSeconds` = 720). Persisted via
  `DayNightSnapshot`. Player sleep fast-forwards the clock 30x.
- **Construction:** `Bootstrap.cs:234` constructs `RecruitmentSystem`
  (Bootstrap is free of sprint-session hunks). The clock instance is owned
  by `Game` and is public.
- **Snapshots:** the worker snapshot record lives in `Core/SaveSystem.cs`
  (~line 422, e.g. `CarriedItemId`); `NPCSystem.cs` maps fields both ways
  (~lines 140/176).
- **Sprint-shared files (parallel session owns uncommitted hunks):**
  `Config/Constants.cs`, `Config/Keybindings.cs`, `Core/Game.cs`,
  `Core/Player.cs`, `Input/InputManager.cs`, `Input/InputState.cs`,
  `UI/HUD.cs`, `UI/HudWindows.cs`. Any edit inside these uses the
  established partial-staging pattern (filtered patch + `git apply
  --cached`); this slice needs it only for `UI/HudWindows.cs`.
- **Roster:** only 3 recruit-type NPCs exist (forest assistant, coastal
  trader, mountain guard); the brain runs behaviors `assistant` and `guard`.

## Design

### 1. New unit — `NPC/WorkerSchedule.cs`

```csharp
enum ScheduleCategory { Work, Sleep, Nourishment, FreeTime, MilitaryDuty }
enum WorkShift { Daytime, Nighttime }

sealed class WorkerSchedule   // 24 slots, one per hour 0..23
{
    ScheduleCategory SlotFor(float hourOfDay);   // floor to int hour
    // factories:
    static WorkerSchedule WorkerDay();
    static WorkerSchedule GuardShift(WorkShift shift);
}

static class ScheduleTemplates  // who gets which schedule
{
    // "guard"        -> GuardShift(worker's shift)
    // "assistant"    -> WorkerDay()
    // anything else  -> null (NPC not brain-driven; untouched)
}
```

Display labels: WORK / SLEEP / NOURISHMENT / FREE TIME / MILITARY DUTY.

### 2. Templates

Dawn 06:00, dusk 18:00 (matches `HourOfDay`).

| Hour | Worker day | Guard — day squad | Guard — night squad |
|------|-----------|-------------------|---------------------|
| 06   | NOURISHMENT | NOURISHMENT     | NOURISHMENT        |
| 07–08 | WORK      | MILITARY_DUTY    | FREE_TIME          |
| 09–11 | WORK      | MILITARY_DUTY    | SLEEP              |
| 12   | NOURISHMENT | NOURISHMENT     | SLEEP              |
| 13–17 | WORK      | MILITARY_DUTY    | SLEEP              |
| 18   | NOURISHMENT | NOURISHMENT     | NOURISHMENT        |
| 19–20 | FREE_TIME | FREE_TIME       | MILITARY_DUTY     |
| 21–05 | SLEEP     | SLEEP           | MILITARY_DUTY     |

Squad rule: a fresh guard's shift is the alternation complement of the most
recently recruited living guard — Daytime when the settlement has none, so
guard #1 is day, #2 night, #3 day, and so on. A lone guard takes the day
shift: the player meets the settlement guarded on day one, and night safety
comes from the floor plus the fire. Persisted shifts always win over
re-derivation, so dismissing one guard never silently flips a survivor's
shift.

### 3. Dispatcher gate — schedule in front of the existing chain

Priority stack, top wins:

1. **Urgent rest** — existing `TickResidentRest` threshold behavior first
   (unchanged).
2. **Urgent hunger** — `needsFood` (`ColonyHunger <= 0`) unchanged.
3. **Threat** — the existing 3-tile hostile check aborts rest and wins over
   any category.
4. **Schedule category** for the current hour (clock null → category is
   always `Work`, i.e. today's exact behavior — backwards compatibility and
   old-save safety with zero migration):
   - `Sleep` → bedtime: run the rest routine regardless of the meter, and do
     not wake at rest ≥ 90 — wake only when the slot ends (see §4).
   - `Nourishment` → eat at the casual threshold, then loiter near camp
     (see §4).
   - `MilitaryDuty` → the existing `TickGuard` patrol, now shift-gated
     instead of 24/7. Defensive: non-guards landing in a duty slot route to
     the work chain.
   - `FreeTime` → wander near the anchor (see §6).
   - `Work` → today's chain untouched, **plus the hard floor**: if
     `clock.IsNight` and not in a cave, refuse outdoor dispatch and fall
     back to the free-time wander. Guards' own templates have no night WORK
     slots; the floor exists so any future override is safe by default.
5. **Category change** (tracked per NpcId): `TaskBoard.ReleaseAllFor` +
   drop the gather target + reset wander state. Carried goods persist and
   workplace assignments persist — stations stay owned by their worker
   overnight and resume at dawn, which is the desired rhythm.

### 4. Needs integration

- **Bedtime sleep:** `TickResidentRest` gains a `bool bedtime` parameter
  (default false). When true: skip the `> 60 → keep working` gate (bedtime
  sends a full-meter worker to the fire) and skip the `≥ 90 → wake` early
  exit (stay resting until the slot ends). Decay/recovery rates, rest-place
  preference, and the threat abort stay exactly as they are.
- **Casual meals:** `TickColonyNeeds` gains the current category. During a
  NOURISHMENT slot the auto-eat threshold rises from 20 (urgent) to
  `CasualHungerThreshold` (45); all other slots keep the urgent threshold.
  Meal choice logic unchanged. The visible behavior: workers drift near the
  anchor during meal hours and consume from stores.

### 5. Night safety floor (user decision, 2026-10-06)

Outdoor work dispatch refuses at night on the surface for non-guards,
regardless of template — encoding "night is dangerous" as law, not
suggestion. Guards (MILITARY_DUTY) and cave workers are exempt. Workers
refused at the floor fall back to free-time wander near camp.

### 6. Free-time wander

Small new behavior, kept deliberately cheap: every 2–4 s pick a random
standable, path-reachable tile within `WanderRadiusTiles` (3) of home, walk
there, pause. Reuses `MoveAlongPath`. Used by FREE_TIME slots, refused night
work, and NOURISHMENT loitering above the casual threshold. If no such tile
exists, stand at home.

### 7. Visibility

Colony dashboard roster (renders in `UI/Panels2.cs` ~line 2008, verified —
NOT sprint-shared, so no partial staging): each worker's line gains the
current category — `Sleeping`, `Eating`, `On duty (day/night shift)`,
`Free time`, or the existing work/haul status during WORK. Guard entries
show their squad.

### 8. Clock wiring

`RecruitmentSystem.SetClock(DayNightCycle?)` called from `Bootstrap.cs`
right after construction — no `Game.cs` edits. Null clock (unit tests,
partial boot) leaves schedules inert (category = Work always). If Bootstrap
cannot reach the clock instance at wiring time, the fallback is a clock
parameter on `Tick` at the `Game.cs` call site via partial staging.

### 9. Persistence & save compatibility

- `WorkShift` persisted on the worker snapshot record in `SaveSystem.cs`
  (nullable string; `NPCSystem.cs` maps it both ways like `CarriedItemId`).
  Runtime home: a `WorkShift` property on `Npc` (`NPC/NPC.cs` — not
  sprint-shared).
- Old saves: `WorkShift == null` → derive by the alternation rule among
  guards in registry order (Daytime first). No dismissal can silently flip a
  living guard's shift because the loaded value wins.
- Schedules themselves are derived (behavior + shift) → nothing else new in
  saves. `DayNightSnapshot` already persists time-of-day.

### 10. Constants placement

New tunables live in `WorkerSchedule.cs` (not `Config/Constants.cs`, which
is sprint-shared): `CasualHungerThreshold = 45`, `WanderRadiusTiles = 3`,
wander repick interval. They can migrate to Config after the sprint session
lands.

## Testing plan (TDD)

New `src/DontStarveRuneScape.Tests/WorkerScheduleTests.cs`:

- `SlotFor` maps every boundary hour correctly for all three templates
  (06/07/12/18/19/21/05 for workers; night-squad duty at 19, 00, 05; sleep
  at 12).
- Shift alternation by recruit order, Daytime first.
- Null clock → category resolves to Work (inert fallback).

New dispatcher tests (step-ticking per the known straddle gotcha — break at
the transition, never rely on fixed tick counts across hour boundaries):

- Dusk mid-gather: worker holds a gather reservation at 20:59; tick past
  21:00 → claim released, worker enters bedtime rest, task board no longer
  holds their reservation.
- Night floor: category Work at 23:00 (template forced for the test) → no
  outdoor dispatch, worker wanders near anchor.
- Bedtime: full `ColonyRest` (100) at a SLEEP slot → worker seeks the fire
  and rests; slot ends at 06:00 → worker resumes work.
- Casual meal: hunger 40 during NOURISHMENT → store count drops by one meal;
  hunger 40 during WORK → no consumption.
- Urgent overrides: hunger 0 during SLEEP → eating path wins; nearby
  hostile during SLEEP → rest aborts (existing branch).
- Guard shifts: day-squad guard patrols at 12:00 and sleeps at 23:00;
  night-squad guard patrols at 23:00.

All existing tests must pass unmodified (null-clock inertness guarantees
this for tests that never wire a clock).

## Smoketest & visual verification

- `DSR_TEST_COLONY=1 DSR_SEED=12345 DSR_SMOKE_FRAMES=N` with
  `DSR_TIME_OF_DAY=0.75` (midnight): workers resting at the campfire, night
  guard moving (frame diff proves patrol movement, workers stationary).
- Same with `DSR_TIME_OF_DAY=0.25` (noon): workers out gathering (movement
  diff), day guard patrolling.
- Captures checked via the reflections Q&A protocol (one question per image
  read). Roster labels confirmed by opening the colony dashboard.

## Risks

- `UI/HudWindows.cs` is sprint-shared → the roster hunk is partial-staged
  (proven pattern; keep the hunk small and keyword-distinct).
- `RecruitmentSystem.cs` is verified clean of sprint hunks — safe to edit.
- Bedtime threading must not change urgent-rest behavior: the `bedtime`
  parameter only relaxes the work gate and the wake gate, nothing else.
- Fixed tick counts straddle hour boundaries (known repo gotcha) — tests
  tick stepwise and break at transitions.
- Player sleep (30x clock) compresses schedules to ~2 real seconds per game
  hour; workers will transition rapidly during player sleep. Acceptable:
  the world is supposed to be asleep.

## Deferred (recorded, not this slice)

Per-NPC schedule overrides + dashboard editor; cave expedition schedules;
darkness damage; DRINK need; squad UI management. Roadmap doc gets its
progress-log entry when the slice lands.
