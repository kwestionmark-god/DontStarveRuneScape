# Fire-menu smoketest — DSR_TEST_FIREMENU staging hook — design

Date: 2026-10-11
Status: implemented + pixel-verified (RED `bd06fb8`; GREEN `07b97c8`;
captures + PIL probes 2026-10-11 — border bbox (289,111)-(1310,788) vs
predicted (289,111)-(1311,789), plate count 621k px, panel-rect mean
abs diff 97.8 vs baseline)
Slice: fire-venue debt closure — FireMenuPanel has never been rendered
(the fire-venue handoff's PENDING verification item). One env-gated
staging hook plus the pixel pass that retires the debt.

## Design law

- The capture must exercise the REAL paths end to end: real Bootstrap
  wiring, real F (`FireInteraction.HandleLightFire`), real E
  (`InteractSystem.HandleInteract`), real `SetState(GameState.FireMenu)`,
  real `FireMenuPanel.Render`. The hook only stages the world — position
  + fuel; `DSR_TEST_KEYS="F,E"` drives the actual game, the same way the
  DSR_TEST_FISHING hook stages a cast and lets the render path run.
- MC: zero new systems, zero gameplay changes. One static helper + one
  env-gated block; without `DSR_TEST_FIREMENU` behavior is identical.

## Ground — verified in the tree (2026-10-11)

- E-order blockers in `InteractSystem.HandleInteract`, all checked
  BEFORE the fire-menu branch (:99): a 3x3 cave collar around the player
  (:41-48), any active NPC within 128px (`NPCSystem.FindNearby`,
  NPCSystem.cs:52-70, comparator `< 128*128`), any live tamable monster
  within 96px (:68-93, comparator `<= 96*96`). A staged tile clear of all
  three guarantees E reaches `CanOpenFireMenu`.
- `LightFire` is synchronous (FiremakingSkill.cs:92-132): F then E in
  the same inject pass works — the fire exists before E routes.
- The smoke-capture condition `State == Playing || _smokeFrames > 0`
  (Game.cs:1124) keeps counting frames after E flips state to FireMenu;
  default DSR_SMOKE_FRAMES=6 leaves 5 frames of panel render.
- `DSR_TEST_KEYS` routes at Game.cs:1385, BEFORE `DSR_TEST_ITEMS`
  (:1437) — the hook must grant its own fuel (the fishing hook grants
  its own rod for the same reason).
- `HandleLightFire` caps fuel at 2 of the best type (`CollectFuel`,
  FireInteraction.cs:123-139); 2 oak_logs = 60s burn (fuel_table 30s x 2)
  — outlasts the 6-frame capture.
- Panel geometry (FireMenuPanel.cs:23-29, Layout :355-373): plate
  780+32 wide x 470+32+34 tall centered on the framebuffer;
  `PanelChrome` plate (40,24,12), border (222,192,132) — the probe
  colors. No other chrome renders in a plain Playing capture, so
  plate+border presence proves the panel drew, their bbox proves the
  layout.
- Env-gated hooks have no unit-test seam by convention
  (DSR_TEST_FISHING has none) — the stage-tile selection is the
  testable core; the hook itself is verified by the pixel pass.
- Carried finding resolved here: the parked "grass_rope duplicate
  xp_reward key" is a phantom — a dup-detecting JSON parse of every
  commit that ever touched recipes.json (plus items.json/quests.json/
  npcs.json) shows zero duplicate keys or ids at any point in history;
  the grass_rope ITEM is alive (Twist Rope recipe output, consumed by
  both rod recipes, FishingTests.cs:73/:539). Docs-only resolution.

## Goal

1. `DSR_TEST_FIREMENU=1` stages the player on the first clear dry tile
   (no cave collar / active NPC / live monster in the E-bands) and
   grants 2 oak_logs; with `DSR_TEST_KEYS="F,E"` a 6-frame capture opens
   the real fire menu.
2. Pixel pass: plate + border present in the expected extent, plus a
   panel-rect mean diff against a no-env baseline. Spec Status →
   verified; the handoff debt is closed.
3. grass_rope phantom resolution lands in the docs commit (dev-map +
   skill pitfall note), no data change.

## Non-goals

- No gameplay or panel-behavior changes. If the capture reveals a
  FireMenuPanel layout bug, it gets recorded as a finding — not
  silently fixed in this slice.
- No new smoketest framework — this is the next env hook on the
  established pattern (fifth hook: RESOURCE, KEYS, FISHING, COLONY,
   FIREMENU).

## Design

- `internal static (int X, int Y)? Game.FindFireMenuStageTile(
  TileMap world, NPCSystem? npcs, CombatSystem? combat)` — first tile
  in a deterministic scan (y outer, x inner, matching the fishing
  hook) that is dry (`Elevation >= SeaLevel + 2f`), has no cave
  collar in its 3x3, no active NPC within 128px (distSq < 128²,
  matching FindNearby's comparator), no live monster within 96px
  (distSq <= 96², matching the taming band). Null when nothing
  qualifies. `InternalsVisibleTo` makes it the test seam.
- Staging block in `InjectTestHooks`, inserted after the
  DSR_TEST_COLONY block and BEFORE the DSR_TEST_KEYS block (order
  matters: staging precedes key routing in the same frame): teleport,
  clear move targets, `Inventory.AddItem("oak_logs", 2)`.
- Captures: baseline (no env) + firemenu (`DSR_TEST_FIREMENU=1
  DSR_TEST_KEYS="F,E"`), default 6 frames each; PIL (in the ornith-cuda
  container) probes plate/border counts and bboxes and diffs the
  panel rect against the baseline.

## Testing plan

- RED: new `FireMenuSmoketestTests` against a null-returning stub —
  clear flat map returns the first dry tile; all-wet map with one dry
  island finds the island; NPC near origin pushes the pick past the
  128px band; live monster pushes past 96px (dead does not); a cave
  collar at (1,1) pushes past its 3x3; a 2x2 map fully covered by one
  NPC returns null (pre-greens against the stub — expected, noted).
- GREEN: real helper + hook; suite 507 → 514 expected (513 green, 1
  pre-existing skip).
- Pixel pass: both captures land and the probes pass; docs commit
  flips Status to verified, ticks the dev-map debt, CHANGELOG entry.

## Open questions → answered (one clarify round, 2026-10-11)

- When do captures run? → Immediately after GREEN; user hands-off
  ~3 min total.
- grass_rope: user asked whether the item exists — it does (both rod
  recipes consume it); the dup-key claim matches no committed state →
  docs-only phantom resolution.
- Continue down the handoff menu after? → Yes: hub-tab return-to-menu
  next, coastal/swamp quests if rolling.
