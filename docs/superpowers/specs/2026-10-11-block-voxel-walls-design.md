# Block voxel walls — sprite-proportioned thickness, join-by-extension

- **Date:** 2026-10-11
- **Status:** IMPLEMENTED (RED b1d86df / GREEN 90cadc8; suite 602 total /
  601 green / 1 pre-existing skip; seamless 3-block wall pixel-VERIFIED via
  DSR_TEST_STRUCTURE smoketest)
- **Slice line:** vertical-building arc, slice 6 (user critique of slice-5
  render: "the tile size makes for chunky walls — use the normal sprite
  size, but extend them to join naturally")
- **Design law:** a block's box footprint is NOT the whole tile. Its
  thickness across the unconnected sides is sprite-scale (half-thickness
  16 world px = the slice-4 crossed-plane glyph width), and each
  CONNECTED side (slice-3 `ConnectionMask` bit) extends the box to that
  tile EDGE — so two joined blocks butt at the shared boundary and read
  as one continuous slender wall. Isolated blocks render as a compact
  32×32 box centered in the tile.
- **Goal:**
  - `BuildBlockVoxel` gains `(int connectionMask, float halfThickness)`:
    footprint = for each axis, [tile edge if the mask bit set, else
    center ∓ halfThickness]. Face construction, strips, culling, sorting
    from slice 5 run on that footprint unchanged.
  - The shared topping-edge identity of joined blocks now pins at the
    slab edge (the boundary BUTT line), not the tile edge of a fat box.
  - `RenderStructure` passes the slice-3 `ConnectionMask` (fence_block
    keeps its own path).
- **Non-goals:** gameplay/collision untouched (blocks still occupy the
  whole 64-px tile for movement/stacking); height unchanged (stack ×
  12.8 px ≈ sprite height — user said "normal sprite SIZE" for the
  footprint, heights already read right); no corner/merge autotiling
  beyond mask extension; fences unchanged.
- **Ground — verified in the tree:**
  - Slice 5 voxel path: `BuildBlockVoxel` (SpriteRenderer.cs ~:283)
    derives the footprint from tile ints (x0 = tx·64 …); tests in
    BlockVoxelTests.cs pin FULL-TILE corners — those contracts move to
    mask-driven extents in this slice and get rewritten.
  - `BlockStack.ConnectionMask` (N=1/E=2/S=4/W=8) already computed in
    RenderStructure as `mask`; `FenceFacingDir` is the axis precedent.
  - halfWidth 16 px is the slice-4 structure glyph width
    (RenderStructure passes 16f to BuildStructureHalves).
- **Testing plan (RED):**
  1. Isolated block (mask 0): top-face corners pin to center ±16 px,
     NOT the tile corners.
  2. E-connected block: footprint spans tile-center−16..tile-east-edge
     along X? No — spans west-center…east EDGE along X (east bit set),
     center ±16 along Y.
  3. E|W pair of blocks: A east extent == tile boundary == B west
     extent (butt-join, no gap, no overlap); shared side faces still
     culled via neighbour heights (slice-5 logic intact).
  4. Exposed-strip + taller-neighbour culling formulas keep working on
     the slender footprint.
  5. Visible-sides flip with yaw + back-to-front sort keep working.
- **Open questions:** none — proportions (16 px half-thickness), height,
  and collar behavior all resolved from the user's note + slice-4/5
  ground truth.
