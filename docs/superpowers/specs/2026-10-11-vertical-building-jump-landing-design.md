# Vertical building, slice 2 — blocks are walls, jumps land on tops

- Date: 2026-10-11
- Status: implemented (RED 9a5e249 / GREEN 83e045f, 2026-10-11)
- Slice line: blocks stop your feet and carry your body — walking into a
  block taller than what you stand on is a wall, a jump hop of up to 2
  elevation levels lands you ON its top, walking off any edge drops you
  down, and blocks + the player render at their true top height (a block
  you can't see is unteachable). Slice 1 shipped block defs, the flat-pad
  gate, and the shovel flatten; this slice makes them matter to movement.

## Design law

A block is ground you paid for: if you can't put your feet on it, it's
a wall (visual + mechanical); if you can, it's a floor. No invisible
colliders — the climb cap is the same number the art shows (2 levels =
one mound = JumpHeightPx-range hop), and no silent stepping: terrain
slopes stay legacy-free (worldgen hills walk as before), only PLACED
OURSELEVATION (blocks) gates movement in this slice.

## Decisions (clarify round 2026-10-11, answered)

1. Walking into the side of a block taller than your footing is REFUSED
   (obstacle). Getting on top is the jump's job. Jumping or walking DOWN
   off any edge always works.
2. Climb capability per hop: flat 2 levels (clean TDD seam). Agility
   scaling is a later slice. (User note banked: blocks could be
   wall-shaped workbench/fence-like connectors — goes to slice 3 as
   "fence-style connecting wall segments, Minecraft-fashion".)
3. Blocks RENDER lifted to their top height in this slice — the landing
   target must be visible to be learnable.
4. mound_block `stack_height` becomes 2 levels (was 8 — an 8-level mound
   is ≈102 screen px, far beyond the 26 px jump peak; 2 levels ≈ 25.6 px
   ≈ one hop = one mound. Stacking is the progression).

## Goal

1. **Standing model.** New pure statics (Building namespace, e.g.
   `BlockStack`): `BlockHeightAt(world, buildingSystem, tx, ty)` — the
   sum of `StackHeight` for ACTIVE, BUILT (not under-construction-site)
   block structures on a tile; and
   `StandingElevationAt(world, buildings, worldX, worldY)` = the tile's
   `GetElevationAt(u,v)` interpolated terrain + that tile's block lift.
   No state: standing is a query of position.
2. **Movement gate.** `Player.ApplyKeyInput` gains optional
   `TileMap? world = null, BuildingSystem? buildings = null` (appended —
   existing callers compile untouched, per the overload-visibility trap
   the solution is default params not a second overload). Per-axis: if
   the candidate position's standing top sits ABOVE the current standing
   top, the step is allowed only when it isn't a block ascent... rule:
   descent/equal → allow; ascent where the destination carries more
   block-height than the source → allow only if airborne AND the top
   delta ≤ `Constants.BlockClimbCap` (2.0f + epsilon, levels); pure-
   terrain ascents stay free (legacy). Blocked axis = don't advance on
   it this frame (axis-slide, no bounce).
3. **Landing.** Jumping while moving into a block tile with top-delta ≤
   cap is legal per the gate; when the arc ends the standing query
   immediately puts the player on top (no snap — the arc's own descent
   lands there visually). No fall damage, no fall state: stepping off a
   top edge just puts you at the lower standing.
4. **Render lift.** Player: Game.cs's render loop (Game.cs:1843) feeds
   `RenderPlayer(..., elev, ...)` the STANDING elevation (terrain +
   block top), and the per-boot `BootElevation` sampler samples the same
   standing query (boots ride the top, not float at terrain). Blocks:
   the structure render loop (Game.cs:1941-1947) lifts block sprites by
   their block lift (single block per tile this slice — stackable
   columns are slice 3, so the lift equals `def.StackHeight`); sort key
   uses the lifted elevation so tops occlude correctly. Camera focus
   stays terrain-focus (judder noted, later polish).
5. **Data change.** structures.json `mound_block.stack_height` 8 → 2.
   New data law pin: every is_block def's stack_height must be ≤ the
   climb cap... NO — future defensive tiers may exceed it by design;
   the law stays "stack_height > 0" (slice 1). mound_block's value is
   the reachable tier.

## Non-goals (slices 3-4 + parked)

- No block stacking on blocks (OccupiesTile still refuses; the column
  container and fence-style neighbor connection visuals = slice 3,
  with the user's fence-note banked).
- No NPC/pet/monster standing heights — player only (spec contract #4
  from slice 1).
- No fall damage / falling animation; drops are steps.
- No wall-shaped defs; mound stays 1-tile.
- No smoke/pixel-diff verification plumbing this slice (there is no
  accepted key-hook for movement-based smoke yet; that hook + the
  render pixel check is parked as slice-4 debt, unlocked here as an
  API shape).

## Ground (verified in the tree, 2026-10-11)

- Standing ground today: Game.cs:1843 `elev = tile.GetElevationAt(fx, fy)`
  feeds RenderPlayer; boot sampler at Game.cs:1846-1855 re-derives it.
  Renderer's WorldToScreen (Camera/Camera.cs:131) takes elevation, scale
  = ZScale 8 × TerrainHeightScale 1.6 = 12.8 px per level (Constants.cs:
  24-25); JumpHeightPx = 26 px, JumpDuration 0.45 s (Constants.cs:145-146).
- Structures render at tile elevation (Game.cs:1941-1949): watercheck →
  tile.Elevation → GetDepthSort → RenderStructure. Blocks need the top-
  lift injected at exactly that spot.
- Movement is free-form WASD: Player.ApplyKeyInput (Player.cs:120) adds
  velocity, clamps to map bounds; there is NO tile-crossing gate in the
  codebase (grep: no CanWalk/IsWalkable) — the block-wall rule is the
  first movement gate.
- Jump is visual-only: TryJump (Player.cs:205) starts a sine arc,
  UpdateJump (Player.cs:224) drives JumpVisualOffset/JumpProgress;
  position is untouched by it. `IsJumping` exists for the airborne flag.
- BuildingSystem.Structures carries active/built state (IsActive,
  IsUnderConstruction); block defs parsed is_block/stack_height (slice
  1). Blocks can't be on water (flat-pad gate refuses water).
- InputState is a settable flags object (Input/InputState.cs:7) —
  movement tests construct it directly.
- Persistence: structures already round-trip via BuildingSnapshot; the
  standing model is derived → nothing new to save (no SaveData change).

## Testing plan (TDD)

RED (new VerticalLandingTests):
1. Standing query: flat world at SeaLevel+5; mound built at (4,4) →
   StandingElevationAt((4.5,4.5)·64) == 18.5; a neighboring tile reads
   16.5; an under-construction mound contributes ZERO (site isn't solid);
   a tile with a campfire (non-block) contributes zero.
2. Wall rule: player beside a mound walking into it does NOT advance
   (WorldX unchanged across a tick); the same walk with `IsJumping`
   true and delta = 2 (== cap) advances; delta ABOVE cap (stack the
   mound to 4 via two adjacent... no — stack test is slice 3; simulate
   tall by placing on a raised tile: mound on an 18.5 tile → top 20.5
   vs standing 16.5 = 4 > cap → refused even airborne).
3. Step-down: player standing on the mound (start on top) walks onto
   terrain — advances (descent always allowed).
4. Terrain-only ascent preserved: a 3-level terrain step with no blocks
   is walkable as before (legacy freedom pinned).
5. Render math: the Game render loop's structure-elevation expression
   is factored into a pure helper (e.g. `BlockStack.RenderTop(tile,
   structure)` — block → tile elev + StackHeight; non-block → tile
   elev) and pinned; an end-to-end Game-draw smoke with pixel diff is
   slice-4 debt (declared, not hidden).
6. Regression: existing jump tests unchanged; standing on a flattened
   (SeaLevel+6) edited tile across save/load keeps the standing right
   (delegates to slice-1 persistence + derived query).

GREEN: minimal implementation in that order: BlockStack statics →
ApplyKeyInput params + gate → render-loop elevation exchanges → data
8→2. Run the whole suite (mound_block stack_height moves; check the
slice-1 data-law tests still pass — they read "> 0", safe).

## Open questions

None for slice 2. Slice 3 enters with concrete sourced notes: stacking
container per tile, visual fence-connection for adjacent block tops,
stack-height > cap defs as defensive walls.
