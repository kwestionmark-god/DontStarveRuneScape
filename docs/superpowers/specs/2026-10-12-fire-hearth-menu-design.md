# Fire structure menu — fuel reservoir, burn stages, upgrade + fire art (slice 13)

Date: 2026-10-12
Status: implemented — RED b2066b2 / GREEN 023b8c2 (2026-10-12),
suite 659 → 680 (679 green + 1 pre-existing skip)
Slice line: vertical-building post-roadmap, slice 13 (fire-venue sequel + footprint arc "plain → lined" item)

## Design law (user's words, 2026-10-12)

Screenshot critique: "good progress on the fire build, now to improve its
visual quality, eh?" Then: "the fire likely needs fuel.. but do we have a
menu? currently when 'e' is pressed near the fire, it attempts to sleep..
i think we should create a proper menu when interacting with the fire
structure". Clarify round answers (verbatim):

- "lets add burn metrics, in the menu, lets add a fuel gauge, lets give
  it stages, and graphics to match.. lets also add the other fire
  related interactions to the menu, such as sleep, and structure
  upgrading."
- On fuel picking: "add a hoverable reservoir which leads to the
  inventory if empty, if it has a fuel type in reserve, display the
  item sprite and the number currently in reservoir, if there is a
  fuel type already inserted, then make engagement while hovered
  simply add another of that type to the reservoir if any exist in
  the inventory?"

## Ground (verified in the tree)

- E near a COLD campfire never opens the menu today:
  FireInteraction.CanOpenFireMenu (FireInteraction.cs:200) opens on a
  lit fire or cooking_station/furnace/smelter only; the cold hearth then
  falls through InteractSystem to TrySleep (nearShelter matches
  "campfire"/"woven_shelter") — exactly the reported "E attempts to
  sleep". E_NearColdHearth_DoesNotOpenFireMenu (FireVenueTests:368)
  pins the OLD law and is the retruth.
- A lit hearth is one flat timed FireInstance (LightHearth adds an
  instance; no stock). "Fuel is the entry fee" (CollectFuel, 1–2 units
  best-carried); hearth burns do NOT read the fuel table today
  (fire-hearth-seams.md law being REWRITTEN by this slice).
- RenderFire (SpriteRenderer.cs:1666) is a single flat 20px screen
  square (230,140,40) — no stages, no scale. FireInstance already
  carries RemainingTime + MaxTime.
- BurnSeconds (slice 12) = light_seconds x burn_rate feeds placement,
  refuel, worker completion — the variants handle this slice's
  reservoir keeps (burn_rate multiplies PER-UNIT seconds).
- Structure instance: Building/Structure.cs. StructureSnapshot:
  Core/SaveSystem.cs:429. OnRest callback wiring precedent:
  FireMenuPanel.OnRest → Bootstrap → FireInteraction.TrySleep
  (FireVenueTests:399 shows the wiring shape).
- fuel_table.json: stick 15s, (oak_)logs 30s, charcoal 45s,
  plank(s) 20s; DefaultFuelSeconds 30.
- Panel special-row idiom: rest row = selection index _sorted.Count.

## Design

### The fuel reservoir (per hearth structure)

- Building.Structure gains FuelItemId (string?, null = empty),
  FuelCount (int), FuelCapacityUnits = 10 (const). Persisted via
  StructureSnapshot (new fields FuelItemId/FuelUnits; legacy rows
  restore 0/empty — a relight, same as a burned-out hearth today).
- Feeding (FireInteraction.MenuFeedFire + panel FUEL row):
  - Empty reservoir and fed: the feeder picks the BEST carried fuel
    (the CollectFuel preference list, 1 unit), adds 1 to the stock,
    and if the fire is OUT, pops it and lights (one unit burning).
  - Reservoir holds a type: engaging adds ONE MORE of the same type
    if the inventory has it; else refusal notification (no silent
    loss / no silent no-op). Wrong-type-carried with occupied stock:
    refuse ("The fire is stocked with <type>.").
  - Cap 10; full → refusal.
  - Feed pays 5 firemaking XP per unit; LIGHT (first pop) pays 25
    (existing law).
- Burn model: the live FireInstance is the CURRENT unit burning.
  FiremakingSkill.Tick(dt, buildings) gains the auto-feed: on burnout,
  if the owner hearth (position-matched ≤4px, active, BurnSeconds > 0)
  has stock, pop one unit = FuelTable[id] x def.BurnRate (+ duration
  sub-stat bonus) — single source: LightHearth keeps its signature,
  a new LightHearthUnit(id, def, x, y, skills) does the math; else the
  fire dies. Old "entry fee -> flat light_seconds" law DIES with this
  slice (pinned retruths in FireVenueTests): F on a cold hearth now
  feeds-then-lights (same key, reservoir semantics); placement still
  auto-lights a one-unit grace charge of BurnSeconds (slice-12 pin
  Placement_LightsBurnSeconds stays green — 90s with 0 stock, then it
  needs fuel).
- Burn metrics: static GaugeInfo(structure, fires) = per-stage
  classification: stage 0 out, 1 embers (<25% of capacity burn), 2
  burning (<75%), 3 roaring. Capacity seconds = 10 x FuelTable[id or
  best-default 30] x BurnRate. The panel renders the gauge as a bar +
  the stage name + "about N min" + stock count.

### The fire menu (FireMenuPanel reform)

- CanOpenFireMenu: an ACTIVE non-construction hearth structure
  (BurnSeconds > 0: campfire, lined_fire_pit, firepit) within
  CampfireSearchRadiusTiles opens the menu EVEN COLD. Lit fires
  (ground) and stations stay openers. E near a cold campfire no longer
  falls to TrySleep — sleep near a fire lives IN the menu (already
  the rest row) and near a woven_shelter.
- Rows: [fire recipes…] [Fuel] [Upgrade →*] [Rest until dawn]. Fuel
  row index = _sorted.Count; upgrade row exists only for an
  upgradeable hearth (campfire → lined_fire_pit); rest moves past
  the added rows (existing rest-row-index pins retruthed). Detail
  pane for the fuel row: fire status label, GAUGE bar, reservoir
  sprite + count (hoverable box; engage = feed; empty stock + no
  carried fuel engages → open the inventory panel — the user's
  "leads to the inventory if empty"), itemised next-unit preview
  ("+30s per Oak Logs"). FireMenuPanel gains OnFuel / OnUpgrade
  callbacks, Bootstrap-wired like OnRest. Status + preview strings are
  SET per-frame by Game.Update from FireInteraction (panel stays
  render-only over game state).
- Upgrade verb (fire-menu row + FireInteraction.TryUpgradeHearth):
  campfire → lined_fire_pit, delta materials = lined cost minus
  campfire cost (stone 4), requires construction 2, preserves
  position/SubX/SubY + reservoir (FuelId/Count) + the LIVE fire
  (position unchanged), tile.Structure re-pointed, notification
  "Upgraded to Stone-Lined Fire Pit!". Refusals: already-lined (row
  hidden), missing stone/level. No downgrade path (non-goal).

### The fire art (visual quality)

- Rewrite campfire.png + stone_fire_ring.png (32x32, full-bleed,
  deterministic PIL): chunkier ring stones with lit-top shading,
  warm scorched interior (not void-black), two readable crossed
  logs, a real ember cluster. IHDR 32x32 pins stay green.
- RenderFire stages: the flame SIZE and palette ride the stage —
  stage 3 roaring (tall bright), 2 burning, 1 embers (small dim),
  0 draws nothing (cold ring art shows through). Procedural quads
  this slice; real flame ANIMATION stays the banked arc item.

## Testing plan (RED, one class FireHearthFuelTests + retruths)

1. Reservoir pins: feed empty+cold picks best fuel, +1 stock then
   auto-light (stock 0, one unit burning at FuelTable x rate); feed
   occupied same-type adds unit without lighting loss; wrong-type
   feed refuses with stock type named; cap refusal; no fuel carried
   → false + notification; feed XP 5/unit; cold-pop light XP 25.
2. Auto-feed: stock 2 + fire at burnout tick → still lit, stock 1,
   remaining = unit seconds; empty stock → fire dies.
3. Lined pit burn_rate applies per unit (stick 15s x 1.5 = 22.5s).
4. CanOpenFireMenu: cold campfire + cold firepit + cold lined pit all
   open; ground lit fire + station pins stay green;
   E_NearColdHearth pin REFLIPS to Opens.
5. Status/gauge strings: cold + stocked, burning stage labels,
   "about N min" rounding.
6. Panel: fuel row selection → OnFuel fires (HandleKey Down/Tab/Enter
   idiom), upgrade row only for campfire, rest row index = count +
   fuel + upgrade; Discoverable special rows render in the left list.
7. Upgrade: full flow (delta materials, level gate, StructureId swap,
   reservoir + live fire preserved, tile slot re-pointed,
   notification); refusal pins both directions.
8. Persistence: StructureSnapshot fuel fields round-trip; legacy rows
   (absent fields) restore empty.
9. Stage classification unit table (0/1/2/3 boundaries).
10. Retruth sweep (same RED commit): FireVenueTests cold-hearth pin,
    rest-row-index pins, any "entry fee"/flat-seconds hearth pins
    (grep LightHearth + CollectFuel across the suite), BuildingPipeline/
    CampfireFootprint def pins if moved by new data keys.

Suite baseline entering RED: 659 (658 green + 1 pre-existing skip).

## Non-goals

- Flame ANIMATION frames (banked footprint-arc item; stages only).
- Ground fires get no reservoir (no structure); staged flame render
  DOES apply (stage from Remaining/MaxTime fraction).
- 3x3/4x4 footprint machines; damage-on-walking-through; furnace/
  smelter menus; WarmthRadius consumer.

## Open questions — answered (quoted in Design law)

Fuel-pick UX = hoverable reservoir; feed verb = auto next-unit;
burns = staged gauge in-menu; sleep + upgrade join the menu.
