# Vertical Building Slice 3 — Stacking Columns + Fence Connections — Design

Date: 2026-10-11
Status: implemented 2026-10-11 (RED b4fc9b7 / GREEN 462248b; spec 817b75b)
Slice: Vertical building slice 3 (follows foundations 8b53a0c/20965a3 and
jump-landing 9a5e249/83e045f; seam map:
`references/vertical-building-seams.md` in the dsrs skill)
Design law: a block is ground with vertical extent — its top is floor,
its side is a wall, and the player's jump arc (≈2 units) is the ruler
every height decision is measured against.

## Goal

1. **Stacking placement.** `PlaceStructure` no longer flat-refuses a tile
   holding ONLY the player's block structures when the incoming def is a
   block: the new block stacks onto the column. A tile holding a non-block
   structure keeps the hard "already has a structure" refusal bit-identical.
2. **Column height budget.** A tile's column may hold at most
   `Constants.BlockColumnMaxHeight` (4 units) of summed `StackHeight` —
   TWO mounds make the first true unclimbable wall (4 > BlockClimbCap
   2.05). Over-budget placement refuses with
   "This block column is already at full height." (user decision 1) and
   consumes nothing. The budget counts ALL active block structures on the
   tile, sites included (no over-committing a column through concurrent
   sites).
3. **Stack from anywhere.** Targeting a tile with your own block(s) stacks
   the next block on top; no standing-on-the-column requirement (user
   decision 2). The slice-2 NPC-standing follow-up stays parked.
4. **Fence block def.** `fence_block` (Wooden Fence): `is_block`,
   `stack_height` 1, `occupies_tile` true, `materials` 2 stick,
   `requires_skill_level` 1, `construction_sub_stat` "common",
   `sprite_key` `structure/wooden_gate` (existing art). Entry-tier
   reachability: sticks are whittled from any log at crafting 1.
5. **Fence auto-connection seam.** `BlockStack.ConnectionMask(world,
   buildings, tx, ty)` — pure static, 4-bit N/E/S/W mask: a bit sets where
   the edge-neighbor tile holds any built, active block (fences connect to
   fences AND solid blocks, Minecraft-fashion). Center-tile model (user
   decision 3): the fence occupies its tile center; the mask is data the
   renderer consumes in slice 4 — this slice ships the seam + pins.
6. **Fence hop rule.** A 1-unit fence is a half-hop: the standing CanStep
   math (airborne + delta ≤ BlockClimbCap) hops it with no fence-specific
   code (user decision 4). Fence on terrain, fence on fence (2 units, one
   full hop), fence on mound (3 units, wall) all fall out of the rule.
7. **Cumulative render top.** `BlockStack.RenderTop` learns the column: a
   stacked block's draw elevation = tile elevation + sum of stack heights
   of the built, active blocks PLACED BEFORE it on the tile + its own
   stack. Placement order in `Structures` is the column order. Slice-2
   single-block behavior is the degenerate case and stays bit-identical.
8. **Persistence rides the existing list.** Stacked columns survive
   save/load because `BuildingSnapshot.Structures` already round-trips
   every structure regardless of tile sharing (verified in the seam map:
   BuildingSystem.cs:200-232) — pin it with a round-trip test; no new
   WorldSnapshot list this slice.

## Non-goals

- NPC standing on block tops (slice-2 follow-up, still parked).
- Fence sprite VARIANTS by connection mask in the renderer (slice 4).
- Gates/doors — fences are hoppable by design, no gate needed yet.
- Rotating or edge-mounted fence geometry (rejected center vs edge in
  the clarify round).
- Demolishing/removing individual stacked blocks (no removal verb exists
  for any structure yet; out of scope).

## Ground — verified in the tree

- Hard occupancy refusal is a two-clause check at
  `Building/BuildingSystem.cs:50-52`: `tile.Structure?.OccupiesTile` OR
  any active `Structures` row on the tile → "That tile already has a
  structure." The staking admission amends clause 2 for block-def targets.
- `BlockStack` (Building/BlockStack.cs) is the pure-static single query:
  `BlockHeightAt` already SUMS the stack heights of built+active blocks on
  a tile (:18-30) — a stacked column's standing elevation and wall math
  need no change. `RenderTop` (:72-75) reads ONE structure's stack height
  and must go cumulative.
- `Structures` preserves insertion order (List<Structure>), so "placed
  before" is list order.
- Count-pin risk: `BuildingPipelineTests.cs:22` pins `Structures.Count
  >= 25` — tolerant, adding fence_block cannot break it.
- Scale law from the seam map: 1 stack unit ≈ 12.75 px, jump peak
  ≈ 26 px ≈ 2.05 cap. 2-unit mound = one hop (shipped); 4-unit column =
  first permanent wall (this slice's budget); 1-unit fence = half-hop.
- Refusals are player notifications, never silent (no-silent-losses rule):
  the budget refusal must notify through the PlaceStructure message path
  the panel already surfaces.
- `fence_block` data mirrors `mound_block` (structures.json common group,
  `:6-19`): is_block parser + data-pin coverage already exists in
  VerticalBuildingTests.

## Design

New constant: `Constants.BlockColumnMaxHeight = 4f`.

`BuildingSystem.PlaceStructure` — replace the blanket tile-occupancy
refusal with a partition:
- Occupant set = active `Structures` on the tile.
- If the incoming def is a block AND every occupant `StructureDef?.IsBlock
  == true`: stacking candidate. Column height = sum of occupant
  `StackHeight` (sites included); if column height + incoming `StackHeight`
  > `BlockColumnMaxHeight` → refuse "This block column is already at full
  height." Otherwise fall through to the normal gates (flat pad, water,
  biome, skill, materials) UNCHANGED — a stacked block still needs a flat
  pad under it and still pays full materials + 10 construction XP.
- Otherwise: existing refusal, bit-identical.

`BlockStack.RenderTop(TileMap world, BuildingSystem buildings, Structure
structure, float tileElevation)` — cumulative overload: tile elevation +
sum of `StackHeight` over built, active blocks on the tile appearing BEFORE
`structure` in `Structures` order + own `StackHeight`. The legacy 2-arg
form delegates for single-block callers (the Game.cs render site moves to
the overload).

`BlockStack.ConnectionMask(TileMap, BuildingSystem, int tx, int ty)` —
pure static; bit i set when the edge-neighbor in direction i
(N=1, E=2, S=4, W=8) holds ≥1 built, active block. Fences connect to any
block (Minecraft rule), not just fences.

Structures.json: `fence_block` record in the `common` group beside
`mound_block` (shape above). Sprite `structure/wooden_gate` — verify the
key exists in the atlas before RED.

## Testing plan

New `VerticalStackingTests.cs` (harness copied from VerticalBuildingTests /
VerticalLandingTests):
1. Second mound stacks onto an occupied block tile: success, two active
   structures share the tile, materials consumed for the second block.
2. Three mounds = 6 > 4: third refuses with the exact budget message,
   consumes nothing, Structures count on tile stays 2.
3. Mixed column: mound (2) + fence (1) + fence (1) = exactly 4 succeeds;
   budget refusal at +1 more.
4. Tile occupied by a NON-block structure still refuses any second build
   with the legacy message (partition carve-out).
5. Stacked blocks pay the SAME gates: sloped pad refusal still fires for
   the second block (flat-pad law applies per placement).
6. Standing elevation: player on a 2-mound column stands at tile + 4
   (`StandingElevationAt`/`BlockHeightAt` sums — pin the existing sum).
7. Movement: grounded step from column 4 to terrain is a free descent;
   grounded step INTO a 4-column is a wall; airborne step onto a 4-column
   from terrain is beyond the cap = wall even mid-jump.
8. Fence hop: airborne step onto a 1-unit fence from terrain succeeds;
   grounded is refused (the half-hop contract).
9. `ConnectionMask`: lone fence = 0; fence E of a mound = W bit; full
   cross of blocks = 15; construction-site neighbor sets NO bit (sites
   aren't solid).
10. Cumulative `RenderTop`: second mound draws at tile + 2 + 2; fence
    atop mound at tile + 2 + 1.
11. Save → restore round-trip: stacked column survives
    `GetSnapshot`/`RestoreSnapshot` with both structures on the tile.
Data pins: fence_block parses (is_block, stack_height 1, occupies_tile).

## Open questions (answered 2026-10-11)

- Height budget? → 4 units total; two mounds = first true wall.
- Stacking placement? → from anywhere targeting your own block tile.
- Fence model? → center-tile fence_block, 1 unit, render links later.
- Fence hoppable? → yes, half-hop from the standing jump rule.
