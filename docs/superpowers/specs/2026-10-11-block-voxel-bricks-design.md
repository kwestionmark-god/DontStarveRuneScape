# Brick-by-brick building grid (slice 7)

- **Date:** 2026-10-11
- **Status:** draft → RED this session

## Slice

vertical-building / brick-grid — user pivot: "instead of stretchy joining, really go
brick by brick... granulate our building grid — each tile could contain maybe 9
bricks... the user can just join the damn bricks, and they can all just have their
fixed sizes." Resolution: 4x4 sub-grid of 16px bricks per 64px tile (texel-exact
1:1 with the 16x16 sprite; Terraria's cell size), player places bricks per
sub-cell, every brick is a fixed-size box — no stretching, no masks, no butt-join
geometry. Full building-logic refactor in one arc, staged as two TDD slices:

- **7a (this spec):** brick data model + placement + per-sub-cell collision.
- **7b:** per-brick render + build-mode 4x4 grid ghost (follow-up spec).

## Design law

Each brick is a fixed 16x16 world-px unit placed on a 4x4 sub-grid within the
tile; joining is adjacency, not geometry math. Build mode stays open while
building (closable when finished).

## Decisions (user-answered this session; recorded)

1. **Grid:** 4x4 sub-cells of 16px per tile — 1:1 with the 16x16 stone_wall sprite.
2. **Scope:** full refactor now — player places individual bricks via build mode;
   hovering a tile shows the highlighted 4x4 grid, hovered sub-cell solid.
3. **Collision:** per-sub-cell — standing, stepping, jump-landing resolve at 16px.
4. **Column budget:** 4 bricks high — `BlockColumnMaxHeight` 4.0 → 5.0
   (4 × 1.25 = 5.0 exactly; the 5th refuses).
5. **Economy:** unchanged net cost — a craftable `dirt_brick` item (2 stone +
   1 stick → 1 brick, crafting 1) is consumed by placement (craft-then-place,
   Terraria inventory-block style). SHIPPED DEFAULT, explicitly vetoable.
6. **Build mode open-ended:** brick placement keeps build mode active (place many);
   Esc/Q closes. Non-block defs keep place-once-and-exit.
7. Fences stay tile-level (not granulated) — crossed planes + rails unchanged.

## Goal

- `Structure` gains `SubX`/`SubY` (0-3) and `Layer` (0-based course); bricks are
  plain Structures flowing the existing list/machinery.
- `PlaceStructure` gains a sub-cell overload; brick gates: flat pad + dry land
  (tile-level, unchanged), sub-cell empty, support rule (layer 0 = ground;
  layer L>0 requires a brick at the same sub-cell, layer L-1), per-sub-cell
  column budget 5.0.
- `BlockStack` granulates: `SubHeightAt(world, buildings, tx, ty, subX, subY)` =
  max(tile-level block lift, brick column top at that sub-cell);
  `BlockHeightAt` (per-tile) = max over sub-cells (fence seams keep working);
  `StandingElevationAt`/`CanStep` resolve the 16px sub-cell from pixel coords;
  per-axis slide preserved; hop cap `BlockClimbCap` 2.05 unchanged — 1 brick
  (1.25) is a hop, 2 bricks (2.5) a wall.
- Per-brick render base = ground + Layer × 1.25 (list-order cumulation replaced
  by stored Layer — simpler, order-independent).
- Persistence: `StructureSnapshot` gains SubX/SubY/Layer; legacy tile-level
  `mound_block` restores materialized as a full 4x4 × 2-course brick block
  (footprint and height preserved); fences restore unchanged.
- `mound_block` id KEPT (pinned across ~40 test call sites); rebranded name
  "Dirt Brick", stack_height 2 → 1.25, materials → 1 dirt_brick.
- Bricks bypass the colony-site branch (player-built; no 20s worker site per
  brick) — SHIPPED DEFAULT, vetoable.

## Non-goals

- Fence/other-def behavior, render slice (7b: per-brick voxels + ghost grid).
- Drag-paint placement (click per brick v1).
- Sub-cell collision for NPC pathing (player only, as today).

## Ground — verified in the tree

- `Game.UpdatePlacement` (Game.cs:580-611): mouse → tile via
  `Camera.ScreenToWorld`; sub-cell = `(wx/16) % 4` on the same path.
  `StartPlacement` (Game.cs:560); `RenderBuildGhost` (Game.cs:2175).
- `BuildingSystem.PlaceStructure` (BuildingSystem.cs:39-150): occupancy
  partition at :50-62 (all-blocks stack + budget), flat-pad/water gates at
  :67-78, colony-site branch at :100-118, material loop :120-135,
  construction XP at :145.
- `BlockStack` (BlockStack.cs): `BlockHeightAt` :18 sums per-tile stack heights
  (becomes max over sub-cell columns); `StandingElevationAt` :34;
  `CanStep` :45 (pixel-args — sub-cell drops in); `RenderBase`/`RenderTop`
  :72-117 (list-order cumulation → Layer-based); `ConnectionMask` :88 reads
  tile lift (fence seams survive on the max-over-sub-cells tile query).
- `Structure` model (Structure.cs:8-39); `StructureSnapshot` (SaveSystem.cs:429).
- `mound_block` def (Data/structures.json:6-20, stack_height 2, 2 stone + 1
  stick, occupies_tile, sprite structure/stone_wall). `StackHeight` parses as
  float (Data/Structure.cs:135) — 1.25 is legal json data.
- Height math: 1 elevation unit = 12.8 world px (`ElevPerWorldPx`,
  SpriteRenderer.cs:271); 16px brick = 1.25 units exactly; jump arc ≈ 26px ≈
  2 units — 1 brick hop-able, 2 bricks a wall, `BlockClimbCap` 2.05
  (Constants.cs:154-157 with `FlatPadMaxSpread`, `BlockColumnMaxHeight`).
- Data-count pins are floors (`Structures.Count >= 25`, `Recipes.Count >= 80`) —
  new defs need no pin bumps.

## Testing plan

- RED: VerticalStackingTests REWRITTEN to brick contracts — sub-cell placement,
  occupancy per sub-cell, support rule, budget 5.0, standing/step per
  sub-cell, brick-item economy, snapshot round-trip + legacy migration;
  fence contracts preserved untouched.
- VerticalBuildingTests / VerticalLandingTests / StructureBillboardTests
  brick placements move to the sub-cell overload.
- GREEN: full suite; then 7b renders and pixel-verifies.
