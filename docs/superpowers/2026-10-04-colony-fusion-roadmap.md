# Colony Fusion Roadmap

## Objective

Grow the existing explorer and survival world into a colony simulation. Keep the
player's current gathering, skills, combat, quests, and exploration loop, while
making recruited people useful, visible members of a settlement. Borrow the
simulation depth and readable worker behavior associated with Mountaincore; use
this project's terrain, sprites, seasons, caves, factions, and crafting systems
as the identity of the fusion.

## Guardrails

- Work only in `/tmp/dontstarve-runescape-mountaincore-experiment`.
- Preserve the current player-centered survival loop and existing save files.
- Add simulation in small vertical slices that connect data, behavior, world
  state, and visible feedback.
- Prefer existing resource, inventory, building, season, faction, and rendering
  systems over parallel implementations.
- Keep the colony optional: a player can explore and survive without recruiting.

## Tri-fusion design law (2026-10-04)

This project fuses three games. Each contributes a pillar that future work must
keep load-bearing:

- **Don't Starve → player-centered survival.** Hunger/health pressure, seasons
  and weather, darkness, and exploration are the core moment-to-moment loop.
  The player personally gathers, crafts, and gets into danger.
- **RuneScape → skill-XP progression in a lived-in world.** Gathering and
  crafting grant skill XP; quest givers, merchants, and faction-flavored NPCs
  make the world feel populated; long-tail leveling is the progression spine.
- **MountainCore → colony logistics.** Recruited workers, hauling, stockpiles,
  workplaces, and task assignment give the settlement a simulated life of its
  own.

**Balance rule:** No single lineage's systems may subsume the others' core
loops. Logistics and colony features must *augment* the player-facing survival
and skill loops — workers feed, supply, and guard the player's expeditionary
play, and never replace it. Before building any colony feature, ask: what does
the player still do themselves, what XP do they still earn, and what survival
pressure still applies? If a proposed feature automates away a player loop
(e.g. workers gather so the player never needs to), reshape it — cap worker
throughput, require player-supplied materials or recipes, or gate the feature
behind player progression — rather than ship it.

Applying the rule to the pending work:

- **Logistics slice (task board, reservations, hauling).** Hauling moves what
  the colony already owns; it does not spawn resources. Raw-material gathering
  stays player-led or worker-assisted at capped rates, and worker crafting
  still consumes player-earned recipes and settlement stock. Player gathering
  and crafting keep granting the player their skill XP; workers accrue their
  own resident XP instead of siphoning the player's.
- **Structure upgrades.** Upgrades consume gathered materials and player-gated
  recipes, so better buildings track the player's progression instead of
  running ahead of it. Seasonal and weather pressure keeps applying outdoors
  (the woven-shelter precedent: shelter mitigates, never nullifies).
- **Worker schedules.** Schedules make residents readable and alive (Don't
  Starve-flavored night danger and Don't Starve-style seasons keep shaping
  when work is safe); they exist to give the settlement rhythm, not to
  maximize output. Guard duty and rest tie schedules back into survival and
  faction-threat systems.

## Roadmap

### 0. Establish the experiment — complete

- Create an isolated copy based on the current local `main` checkpoint.
- Carry the uncommitted `SpriteRenderer.cs` work forward.
- Exclude the unrelated Redis `dump.rdb` cache.
- Keep the original project untouched.

### 1. Give recruits useful work — foundation delivered; broader jobs in progress

- [x] Persist recruited status and behavior.
- [x] Gather nearby tool-free resources with assistants; respect season, node,
  and storage constraints.
- [x] Keep assistant and guard assignments distinct.
- [x] Give guards a real defense/patrol behavior around the settlement.
- [x] Let hostile monsters attack and injure recruited residents.
- [x] Pause work cleanly when the world, player, or inventory is unavailable.

Recruited `assistant` NPCs walk to nearby tool-free resource nodes and harvest
them into player inventory (or the settlement stockpile after one is founded).
They respect season availability, node depletion, storage capacity, and avoid
targeting the same node as another assistant during a simulation step. Assigned
guards patrol around the settlement anchor and intercept hostile monsters in a
six-tile defense radius. Their strikes use the combat system's death/loot/XP/
quest path. Hostile monsters can target recruited residents; attacks reduce
their saved health, and residents reaching zero health leave the active colony.

Recruitment status and behavior are now included in NPC snapshots, so assistants
remain recruited after loading. Existing saves without these fields deserialize
as unrecruited, preserving their prior behavior.

### 2. Establish a settlement core — implementation delivered

A dry surface tile can be founded as the settlement anchor. The
colony tracks a ten-tile work radius and a 500-unit stockpile; assistants use the
anchor and deliver there. The dashboard's Colony tab lists colonists and stores,
allows eligible recruits to switch between assistant and guard, and transfers
items to/from the player's inventory. Anchor, stockpile, and job assignment are
saved. Stockpile item rows are scrollable.

- [x] Define a settlement anchor and radius instead of treating the player as
  the only center of activity.
- [x] Add stockpile storage, capacity, and clear transfer rules between player
  and colony inventories.
- [x] Add a compact colony panel for population, jobs, and stores.
- [x] Let players assign and reassign eligible workers from the colony view.

### 3. Turn buildings into workplaces

In progress: assistants staff compatible built stations when the colony stockpile
contains a feasible recipe. They walk to the structure and use the unified
crafting pipeline to consume inputs, produce output, grant skill XP, and notify
crafting quests. Recipe station requirements now load from `requires_structure`;
heat recipes can run at campfires, cooking stations, furnaces, or smelters. The
selected recipe, assigned worker, progress, and status persist on the placed
structure. The first available lowest-tier recipe becomes that station's repeat
order. Players can also assign seasonal wheat farming through garden plots.
The unified registry includes cooking and metallurgy recipes; assistants can
run those orders at heat-enabled workplaces and fetch their actual inputs.
Automatic lower-tier recipe dependencies now queue across compatible idle
workplaces, including materials needed by settlement construction sites.
Structure upgrades remain future work.

- [x] Connect placed recipe-compatible structures to production and workers.
- [x] Use recipe station, skill, material, and campfire requirements.
- [x] Add player-controlled station work orders and FIFO recipe queues.
- [x] Make assistants fetch the missing resource inputs for queued recipes.
- [x] Add seasonal wheat production to garden plots.
- [x] Run cooking and smelting recipes as staffed workplace orders.
- [x] Add automatic multi-stage recipe dependencies across workplaces.
- [x] Turn settlement placements into saved construction sites supplied from
  the colony stockpile and completed by assistant workers.
- [x] Plan lower-tier material production for construction sites.
- [ ] Add structure upgrades.

### 4. Simulate colony life and pressure

- [x] Add resident food needs that consume real food items from colony stores.
- [x] Add a persistent rest need with bench/fire recovery affected by season and
  weather.
- [x] Add a buildable shelter that protects residents from harsh weather.
- Give workers schedules and simple task selection, with sensible fallbacks.
- Connect threats, caves, factions, and diplomacy to settlement defense and
  trade without making ordinary play feel like an RTS management chore.
- [x] Let faction hostility and diplomatic standing affect which faction-linked
  monsters guards treat as settlement threats.
- [x] Apply faction standing to merchant buy and sell prices.

### 5. Persistence and polish

- Save and restore settlement anchors, stockpiles, job assignments, work orders,
  and resident state with backwards-compatible defaults.
- Add feedback for blocked work, missing materials, full storage, and danger.
- Tune simulation cadence and rendering for larger populations.

## Progress log

### 2026-10-04

- Confirmed the experiment copy contains the local `SpriteRenderer.cs` change
  and does not contain `dump.rdb`.
- Inspected current NPC/recruitment/building seams: recruitment ticks and NPC AI
  are empty; structure assignment currently only reports success.
- Roadmap created and first implementation slice delivered: assistant resource
  work using the existing resource nodes, season checks, and inventory capacity.
- Implemented the assistant gathering loop in `RecruitmentSystem` and wired it
  to the game tick. Work stays within a ten-tile recruitment-site radius until a
  settlement is founded, then uses the settlement anchor. Worker targets do not
  overlap within a tick, and seasonal/depleted/tool-gated nodes are skipped.
- Added recruited status/behavior to NPC save snapshots and rebuild the player's
  recruited-NPC list on restore.
- Added `ColonySystem`: foundable dry-surface anchor, bounded work radius,
  500-unit item stockpile, capacity-aware deposits/withdrawals, and a
  backwards-compatible snapshot in the normal save data.
- Added the Colony dashboard tab: found a settlement at the current tile, inspect
  colonists and job labels, reassign eligible workers, and transfer items between
  the stockpile and player inventory. Assistant workers relocate to the anchor
  and store their output there once it exists.
- The dashboard stockpile list scrolls through item types instead of hiding items
  after the first six.
- Added `IItemStorage`, implemented by both player inventory and colony stores,
  so the existing `CraftingSystem` can operate on either. Recipe parsing now
  retains `requires_structure`; player crafting checks available stations, and
  assistant workers run matching recipes at built, staffed workplaces.
- Structures persist their assigned worker, selected recipe, work progress, and
  current status. Colony dashboard rows report worker activity and staffed
  workplace counts.
- Added a work-order view beside the stockpile view: select a built workplace,
  browse compatible recipes, set its repeat order, or clear it. Orders use the
  existing saved `WorkRecipeId` field and show the worker/status state.
- `dotnet build src/DontStarveRuneScape/DontStarveRuneScape.csproj --no-restore`
  succeeds with 0 errors and 27 compiler warnings after adding work-order
  controls. `git diff --check` is clean. Tests were not run.
- The structure-assignment path now writes to the actual placed building,
  releases a previous assignment, rejects unavailable buildings, and releases
  assignments when their recruit is dismissed or removed.
- Garden plots now act as seasonal workplaces. An assistant spends one stored
  wheat as seed, grows a three-wheat harvest over 30 simulation seconds, pauses
  growth in winter, and reports blocked storage/seed states. Crop progress uses
  the saved structure work-progress field, so it resumes after loading without
  a parallel crop-save format.
- Guards now patrol four points around their home and intercept hostile monsters
  near the colony. Guard hits go through `CombatSystem`, including monster
  respawn, loot delivery, attack XP, damage numbers, and kill-quest progress.
  Switching a recruit's role also releases their former workplace assignment.
- `dotnet build src/DontStarveRuneScape/DontStarveRuneScape.csproj --no-restore`
  succeeds with 0 errors and 27 compiler warnings after the guard implementation.
  Tests were not run.
- Recruited residents now have persistent hunger that drains while the colony is
  founded. At low hunger they consume the best registered stockpile food (cooked
  foods before raw); assistants at zero hunger forage only for food resources,
  avoiding a starvation softlock. Starving assistants pause workplace production
  and the dashboard reports fed/hungry/starving states. Old saves default to fed.
- `dotnet build src/DontStarveRuneScape/DontStarveRuneScape.csproj --no-restore`
  succeeds with 0 errors and 27 compiler warnings after the resident hunger
  changes. `git diff --check` is clean. Tests were not run.
- Guard threat selection now consults `FactionRegistry` and `FactionSystem`.
  Faction-linked monsters are treated as settlement threats when their effective
  hostility (base faction hostility adjusted by diplomacy standing) is at least
  0.5; unassociated hostile wildlife remains a threat independently.
- The experiment builds after faction-aware threat selection with 0 errors and
  27 compiler warnings. `git diff --check` is clean; tests were not run.
- Assistants with queued workplace orders now claim the station even when its
  ingredients are missing, then target nearby resource nodes yielding only the
  missing recipe inputs. They return to the station when the stockpile meets the
  recipe quantities. The build succeeds with 0 errors and 27 warnings; diff
  whitespace checks are clean. Tests were not run.
- Normalized recipe inputs now include dedicated metallurgy `base_fuel_cost`
  and `extra_input` metadata. This fixes undercharging steel fuel and ensures
  stockpile checks and assistant gathering use the same full input list.
- `dotnet build src/DontStarveRuneScape/DontStarveRuneScape.csproj --no-restore`
  succeeds with 0 errors and 27 compiler warnings after recipe cost
  normalization. Tests were not run.
- Workstations now support a saved FIFO queue of one-shot recipe orders. The
  Colony dashboard can set the active recipe, append a recipe, or clear and
  pause the station; assistants advance through the queue and release the
  workplace after the final order. Empty manual queues no longer fall back into
  automatic recipe selection. The experiment builds with 0 errors and 27
  warnings; `git diff --check` is clean. Tests were not run.
- Added A* worker routing for resource trips, workplace approaches, and guard
  patrol/intercept movement. Water and occupied structure tiles block routes;
  diagonal moves cannot cut across blocked corners. Unreachable resource nodes
  are skipped for 30 seconds before retry. The build succeeds with 0 errors and
  27 compiler warnings; `git diff --check` is clean. Tests were not run.
- `dotnet build src/DontStarveRuneScape/DontStarveRuneScape.csproj --no-restore`
  succeeds with 0 errors and 27 compiler warnings after the workplace changes.
  Tests were not run.
- Worker A* routes now use the shared cliff threshold to reject steep steps,
  include elevation change in their movement cost, and check both parts of a
  diagonal move for valid slope transitions. Build succeeds with 0 errors and
  27 compiler warnings; `git diff --check` is clean. Tests were not run.
- Recruited workers now accompany the player into a cave, mine tool-gated veins
  when a pickaxe is available in the colony stockpile or player inventory,
  deposit through the existing storage path, and restore their surface positions
  on exit. Cave mining awards the existing Mining XP. Build succeeds with 0
  errors and 27 compiler warnings; `git diff --check` is clean. Tests were not run.
- Work-order planning now finds lower-tier recipes that produce missing inputs,
  assigns them to compatible idle workplaces, queues enough runs for the
  shortage, and recursively plans their ingredients to a depth of eight. This
  uses existing recipe, skill, station, stockpile, and FIFO queue rules. Build
  succeeds with 0 errors and 28 compiler warnings; `git diff --check` is clean.
  Tests were not run.
- Built structures can now consume materials from the colony stockpile as well
  as player inventory. The build panel includes both sources in its available
  counts, and placement validates the combined total before atomically charging
  the two stores. Construction skill and biome checks remain in the existing
  building pipeline. Build succeeds with 0 errors and 27 compiler warnings;
  `git diff --check` is clean. Tests were not run.
- Monster aggro now considers living recruited residents alongside the player.
  Attacks reduce persistent resident health, display damage numbers, and
  deactivate a resident at zero health; existing snapshot handling removes dead
  residents on the next save. The Colony roster displays resident health beside
  hunger state. Build succeeds with 0 errors and 27 compiler warnings;
  `git diff --check` is clean. Tests were not run.
- Merchant buy and sell prices now use the merchant faction's saved standing:
  low standing raises purchase prices and reduces sale payouts, while high
  standing does the reverse. Non-faction merchants retain their existing prices.
  Build succeeds with 0 errors and 27 compiler warnings; `git diff --check` is
  clean. Tests were not run.
- Worker A* now stores costs, parents, and visited state only for discovered
  tiles rather than allocating four full-map arrays for each route query. This
  bounds route-search memory by explored area on the 512×512 surface map. Build
  succeeds with 0 errors and 27 compiler warnings; `git diff --check` is clean.
  Tests were not run.
- Placing a structure inside a founded settlement now creates a saved blueprint
  site. Assistants gather missing resource materials into the stockpile, reserve
  and consume a complete bill of materials once, then build over 20 simulation
  seconds and earn Construction XP. Sites show their progress and do not count
  as crafting stations before completion; builds outside the colony retain the
  immediate construction path. Saved occupied sites restore their tile
  collision, and unreachable approach results are retried instead of cached
  forever. Build succeeds with 0 errors and 27 compiler warnings;
  `git diff --check` is clean. Tests were not run.
- Construction sites now enter the same recursive dependency planner as recipe
  orders. Compatible workplaces can produce intermediate materials and queue
  lower-tier ingredients up to the existing eight-level planning limit; raw
  gathering remains the fallback where no producer recipe exists. Build succeeds
  with 0 errors and 27 compiler warnings; `git diff --check` is clean. Tests were
  not run.
- Recruited residents now carry a saved rest reserve. Work drains it; at low
  reserve they route to a nearby bench, active fire, or settlement anchor and
  pause their current task until recovered. Fire improves recovery, while winter
  and harsh weather reduce outdoor recovery and increase fatigue. Nearby threats
  keep guards on duty. The Colony roster shows the rest state and reserve bar.
  Build succeeds with 0 errors and 27 compiler warnings; `git diff --check` is
  clean. Tests were not run.
- Added the Woven Shelter as a construction-site structure using planks and
  sticks. Residents prefer it as a rest location; its radius suppresses rain,
  storm, and snow fatigue penalties and preserves outdoor recovery rates.
  Construction, save/restore, and blueprint rendering use the shared building
  pipeline. Build succeeds with 0 errors and 27 compiler warnings;
  `git diff --check` is clean. Tests were not run.

## Decisions and open questions

- Until the player founds a settlement, assistant output goes into the existing
  player inventory. Founding switches their delivery to the colony stockpile.
- Worker paths account for elevation changes and reject steps above the shared
  cliff threshold. Cave expeditions use the cave's local map and ore; settlement
  buildings and routes between the surface and cave are not modeled yet.

- 2026-10-04: Logistics slice (first cut of the phase-4 schedules line):
  ColonyTaskBoard with synchronous gather-tile reservations (no two workers
  on one node), haul-home — workers carry harvested goods to the stockpile
  instead of teleporting, deposit reclaims space incrementally, rest
  interruption releases claims without leaking, carried goods persist in NPC
  snapshots, dashboard roster shows hauling status. 226/226 tests.

- 2026-10-06: Day/night cycle (commit 5cdc4e7) — prerequisite for the
  phase-4 worker-schedules line. Conservative per user direction: 10 min
  day + 10 min night, cosine ambient curve driving a full-screen darkness
  overlay, HUD clock, and sleep-at-fire/shelter (E at night) that
  fast-forwards the clock 30x until dawn with reduced hunger drain and
  slow HP regen. Persisted via DayNightSnapshot; DSR_TIME_OF_DAY smoketest
  hook; 8 new tests (249/249 green); day/night captures verified. Worker
  schedule integration (MountainCore's per-hour WORK/SLEEP/NOURISHMENT
  categories, day/night guard shifts) is the intended next slice on top.
