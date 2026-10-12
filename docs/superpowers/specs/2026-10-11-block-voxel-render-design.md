# Block voxel rendering (texture-as-surface + neighbour stitching)

- **Date:** 2026-10-11
- **Status:** IMPLEMENTED (RED 1ecd53b / GREEN 17cf450; suite 601 total /
  600 green / 1 pre-existing skip; stitching pixel-VERIFIED via
  DSR_TEST_STRUCTURE smoketest)
- **Slice line:** vertical-building arc, slice 5 (follow-up after the arc's
  close — user critique of the slice-4 output: "two dirt mounds side by
  side don't JOIN; go true voxel rather than projecting a static image in
  2 directions; same image, projected as surface area; joined blocks
  visually stitch")
- **Design law:** a block is a world-space BOX: one top face + four side
  faces built from the tile's real world corners and its column
  base/top elevations, all through `Camera.WorldToScreen`. The sprite
  texture maps as surface area (full-UV on each face) instead of the
  crossed-billboard projection. Shared faces between joined blocks are
  culled, so two adjacent equal-height blocks render as ONE continuous
  volume (their top faces literally share a projected edge).
- **Goal:**
  - `SpriteRenderer.BuildBlockVoxel` (pure static, testable): fills up to
    5 `BillQuad` faces (top + the camera-facing sides), tagged via
    `Plane` (0=E, 1=S, 2=W, 3=N, 4=Top), depth-sorted back-to-front.
  - Stitching: a side face is drawn only where EXPOSED — the strip from
    `max(base, neighbourColumnTop)` to own top; culled entirely when the
    neighbour column reaches/passes own top. Two equal-height adjacent
    mounds → no shared face, seamless top.
  - Camera-facing culling: a side face draws only when its outward
    normal has positive dot with the view direction
    (nx·sinYaw + ny·cosYaw > 0) — at most 2 side faces of an
    axis-aligned box are ever visible, orbit rotates the set.
  - `BlockStack.ColumnTop(world, buildings, tx, ty)` seam: tile elevation
    + summed block heights (absolute elevation for neighbour strips).
  - `RenderStructure`: block defs go through the voxel path (top +
    sides textured with the same sprite per face); non-blocks keep the
    slice-4 crossed planes bit-identical; `fence_block` keeps crossed
    planes + rails (user decision).
- **Non-goals:** no new 3D mesh pipeline in PrimitiveBatch (user decision —
  hand-rolled world-projected quads on the existing batch, like the boot
  domes); no per-face art (same mound sprite on every face); NPC standing
  on block tops stays parked; terrain slopes under pads unchanged.
- **Ground — verified in the tree:**
  - Current block render is `RenderStructure` → `BuildStructureHalves` →
    `BuildCrossBillboard`, two static planes × 2 halves, sprite U-span
    mapped per half (SpriteRenderer.cs:1364-1376, 458-480). Blocks face
    `FenceFacingDir(mask)` from `BlockStack.ConnectionMask`.
  - Column math already exists: `BlockStack.RenderBase` (cumulative,
    Game.cs:1991 call site), `RenderTop`, `BlockHeightAt` (sums built,
    active blocks; sites count for nothing), `ConnectionMask` N=1/E=2/S=4/W=8.
  - Textured arbitrary quads: `PrimitiveBatch.DrawScreenQuadCornersTextured`
    (full-UV 4-corner, PrimitiveBatch.cs:192) — the exact surface-area
    mapper; no batch changes needed.
  - Depth sort: `CrossBillboardSorter.SortBackToFront(BillQuad[])` sorts a
    whole array ascending by `Depth`; `ViewDepth(wx, wy, yaw)` is the
    face-camera test source (CrossBillboard.cs:33,53).
  - Elev ↔ world px: `ElevPerWorldPx = 1/12.8`; a stack unit lifts
    ≈12.75 px. `WORLD` footprint: `Constants.TileSize = 64`.
  - Pixel verification path: `DSR_TEST_STRUCTURE` hook + same-camera
    delta (slice 4's proven pattern — probe the delta, not absolute
    colours).
- **Testing plan (RED):**
  1. Two adjacent equal-height blocks: block A's east top edge projects
     pixel-for-pixel to block B's west top edge (shared-corner stitch
     contract), and NEITHER draws its shared side face (culling).
  2. Neighbour one stack unit shorter: own side face survives as the
     exposed strip — bottom corners sit at the neighbour's top elevation,
     top corners at own top.
  3. Neighbour taller (≥ own top): no shared face at all.
  4. Camera-facing culling: at yaw 45° exactly the +X/+Y (E,S) pair
     draws; at yaw 225° exactly the W,N pair — the visible set flips with
     the camera, never the geometry.
  5. Face set is top + ≤2 sides, sorted back-to-front by depth
     (last face painted has the largest Depth).
  6. Non-block structures still route to crossed planes; fences keep rails
     (pin the slice-4 contracts against regression).
- **Open questions:** (answered pre-RED in one round) scope = blocks only,
  fences keep rails; projection = hand-rolled quads on PrimitiveBatch.
