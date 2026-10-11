# Firemaking narrowing — the fire venue — design

Date: 2026-10-10
Status: drafted (clarify round done 2026-10-10; RED pending)
Slice: skill-separation arc — user: "narrow firemaking's components to
firemaking… a full, clean separation, for the sake of keeping our skills
progression feeling necessary… move cooking out of crafting, and to a menu
that can only be accessed at a fire", plus "a construction based firepit…
cheaper or longer duration fires".

## Design law

- **RS**: every skill owns its verbs. Firemaking owns *lighting fires*
  (F-key, relight); construction owns *raising hearths* (campfire,
  firepit); crafting stops producing structures entirely (the
  dominated "Build Campfire" recipe dies). Fuel identity is data
  (fuel_table.json), not a flat constant.
- **DS**: a campfire that never burns is a lie — placement auto-lights
  (and worker-built sites light on completion). A burned-out hearth is
  a ring of cold stones until relit: it enables nothing (no cooking, no
  rest) until fire lives in it again. Sleep near a fire moves into the
  fire menu so the venue is the single honest surface.
- **MC**: one new panel (fire menu) cloned from the proven
  CraftingPanel idiom, one new structure def (firepit, reusing the
  campfire sprite), zero new systems. The XP bug class dies at the root
  (AddXp recomputes Level), not per-call-site.

## Ground — verified in the tree (2026-10-10)

- Three fire methods existed with incoherent outcomes:
  1. F-key (`FireInteraction.HandleLightFire` :18 →
     `FiremakingSkill.LightFire`) — transient `FireInstance`, 30s flat,
     25 firemaking XP paid via raw `AddXp` (:71) whose Level never
     recomputes (`SkillManager.cs:146` — the parked frozen-level bug).
  2. Build panel (`BuildingSystem.PlaceStructure` :38) — 2 stick +
     2 stone, permanent structure, ZERO XP of any kind, never lights
     (`RenderFire` only draws `FireInstance`s; no fire is created).
  3. Craft "Build Campfire" (`Skills/Construction/data/recipes.json:62`)
     — 3 oak_logs + 5 stone, trains **crafting** 30 XP, outputs the
     inert `campfire` item (`items.json:351`, `is_structure: true`,
     nothing reads `Item.IsStructure` — the parked finding). Dominated
     by method 2 on every axis.
- `fuel_table.json` (stick 15s, log/oak_logs 30s, charcoal 45s,
  planks 20s) has ZERO consumers; `LightFire` uses flat 30s.
  `HandleLightFire`'s preferred list names "log" and "charcoal" first —
  "log" is not an items.json id.
- `GetAvailableStructureIds` (Game.cs:2023) adds every active structure
  globally (distance-blind) + a virtual "campfire" when a lit fire is
  within `CampfireSearchRadiusTiles` (3). Worker rest reads lit fires
  only (`RecruitmentSystem.cs:928-940`); workers cannot rest at
  structure campfires today.
- Cooking recipes live in the generic CraftingPanel
  (`Refresh`, Panels2.cs:951-960 lists ALL registry recipes): 7
  cooking-skill recipes (cook_chicken/fish/meat, render_tallow,
  refine_salt, filter_honey, brew_mead) plus 4 non-cooking
  `requires_campfire` recipes from other skills (burn_ash firemaking;
  charcoal, refine_resin, make_pitch crafting; craft_fired_brick
  construction).
- E near a lit fire is fully consumed by `TrySleep`
  (InteractSystem.cs:97, FireInteraction.cs:84 — returns true even on
  daytime refusal near a valid spot).
- Placement XP: none on the instant path; worker completion pays
  `AddXp("construction", 10)` (RecruitmentSystem.cs:330) via the same
  frozen-Level API.
- `FireSnapshot` round-trips `FireInstance`s (SaveSystem.cs:423);
  structures restore from their defs, so a new def field travels free.
- Data pins that move: `CraftingPipelineTests.cs:86` (>= 80 recipes,
  84 exist — one dies, pin holds); `SubStatCatalogTests.cs:306-350`
  pin the flat-30s fire contract (2×log → 30s base) and move to
  fuel-aware numbers. No quest targets the campfire item or recipe
  (quests.json grep: description text only); NpcDataTests pins
  untouched.
- Panel plumbing checklist (NpcHub precedent): GameState member +
  PanelStates + IsPanelState + RenderPanels case + router case +
  Keybindings panel actions; `SetState` closes panels on any
  panel→panel move.

## Goal

1. **Kill the dominated craft recipe** — remove "Build Campfire" from
   `Skills/Construction/data/recipes.json`. The `campfire` items.json
   def stays for old-save tolerance (inert either way). Crafting no
   longer produces structures.
2. **Root-fix the frozen-level bug** — `SkillManager.AddXp` recomputes
   Level, grants unallocated points per level crossed, and returns the
   level-up message list (AddXpWithNotification becomes a thin alias).
   Un-freezes firemaking (F-fire), worker construction, worker mining.
3. **Wire the fuel table** — `LightFire` sums `burn_seconds` × quantity
   from `Skills/Firemaking/data/fuel_table.json` (fallback 30s per
   unknown fuel unit); the duration sub-stat bonus adds on top.
   `HandleLightFire`'s fuel preference list becomes charcoal →
   oak_logs → birch_logs → maple_logs → stick (real item ids only).
4. **Hearths are construction; lighting is firemaking** —
   `structures.json` campfire gains `light_seconds: 90`; new
   `firepit` def (common group, stone 6 + stick 2, construction
   level 3, `light_seconds: 240`, reuses `structure/campfire` sprite).
   `PlaceStructure` (instant path) pays `AddXp("construction", 10)`
   and auto-lights a `FireInstance` at the structure via an optional
   `FiremakingSkill` param (null = no light, legacy tests unchanged);
   worker site completion lights too (Tick already receives
   firemaking).
5. **Relight feeds firemaking** — F near a burned-out campfire/firepit
   structure (and no live fire nearby) lights the hearth for
   `def.LightSeconds`, consuming inventory fuel, paying firemaking XP.
   F away from hearths behaves as today (ground fire from fuel).
6. **The fire menu** — new `GameState.FireMenu` + `FireMenuPanel`
   (new file, CraftingPanel idiom) opened by E near a lit fire (3
   tiles) or an active cooking_station/furnace/smelter. Contains every
   recipe with `requires_campfire == true` OR `required_skill ==
   "cooking"`, plus a final "Rest until dawn" row (same
   `TryStartSleep` gate, visible daytime refusal). CraftingPanel drops
   those recipes from its list. CraftingSystem.Craft is reused
   unchanged (same gates); stations count via the menu's own
   proximity rule.
7. **Proximity honesty** — `GetAvailableStructureIds` keeps only
   structures within 3 tiles of the player (plus the virtual
   campfire rule for lit fires); smelting/crafting gates become
   "stand at your station" instead of "own one anywhere".

## Non-goals

- `WarmthRadius` consumer (stays declared, still unread).
- `burnable` structure mechanics (fire spread), cooking_station fuel,
  furnace/smelter menus of their own.
- Worker access to the fire menu (worker auto-production at heat
  stations stays ungated — established convention).
- `skill_affects_yield` consumers (still dead data; noted, not fixed).
- fuel_table `heat_multiplier` (parsed shape reserved, unused).

## Design

- **XP ownership after the slice**: F ground fire → firemaking 25;
  F relight at hearth → firemaking 25; place campfire/firepit →
  construction 10 (instant path; worker path unchanged at 10);
  burn_ash → firemaking (existing recipe); cook_* → cooking;
  craft a candle → crafting. No path pays two skills; no skill pays
  into another's verb.
- **FireMenu recipe partition** single-sources in a static helper
  (`FireMenuPanel.IsFireRecipe(recipe)`): `RequiresCampfire ||
  RequiredSkill == "cooking"`. Menu, CraftingPanel filter, and tests
  all read it (the RolesFor precedent).
- **E-key contract preserved**: near a lit fire, E opens the menu
  (still consumed, still never falls through to gathering). Near a
  shelter with no fire, TrySleep fires as today. The menu's Rest row
  handles night-only sleep with a visible refusal.
- **Old saves**: FireSnapshot unchanged; structure light_seconds comes
  from the registry def on restore (no migration). A pre-slice
  inventory "campfire" item still loads (def kept), remains inert.

## Testing plan

New `FireVenueTests.cs` (RED first, right failure per test):

1. `AddXp_RecomputesLevel_And_GrantsPoints` — raw AddXp past
   XpForLevel(2) raises Level and unallocated points.
2. `FuelTable_Charcoal_BurnsLonger` — 2×charcoal → 90s MaxTime;
   2×stick → 30s; duration sub-stat stacks on top (SubStatCatalog
   fire tests move to fuel-aware expectations: 2×log 60s base).
3. `PlaceStructure_Campfire_Lights_And_TrainsConstruction` — instant
   placement (with firemaking passed) creates a FireInstance at the
   structure position, pays 10 construction XP, Level recomputes.
4. `PlaceStructure_NullFiremaking_NoFire` — legacy harness shape keeps
   passing (pre-green expected; verify reason).
5. `Firepit_DefLightsLonger` — firepit exists, light_seconds 240 >
   campfire 90.
6. `Relight_AtBurnedOutHearth` — F near a campfire structure with no
   live fire: fire spawns at the structure, fuel consumed, XP paid.
7. `E_NearLitFire_OpensFireMenu` / `E_NearColdHearth_DoesNot` —
   InteractSystem routing.
8. `FireMenuPartition_SingleSource` — every requires_campfire or
   cooking recipe is menu content; CraftingPanel list excludes them.
9. `Campfire_CraftRecipe_Removed` — registry lacks recipe id "campfire";
   structure def "campfire" still present.
10. `WorkerCompletion_LightsSiteFire` — colony site finish with
    firemaking passed spawns the fire.
11. `GetAvailableStructureIds_IsProximityFiltered` — distant furnace
    no longer satisfies smelting; near one does.

## Open questions (defaults chosen, flag on review)

- Relight consumes 1–2 units of best fuel and burns `LightSeconds`
  flat (hearth as multiplier) — not fuel-table-scaled.
- Placement auto-light is "free" (materials are the fuel narrative).
- Construction XP flat 10 (matches worker completion).
- Menu enablers include furnace/smelter (built kitchens) even though
  their recipes stay in the crafting panel (station proximity is
  shared infrastructure).
