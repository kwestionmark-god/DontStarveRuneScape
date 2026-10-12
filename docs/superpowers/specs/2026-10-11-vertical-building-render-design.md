# Vertical Building Slice 4 — Grounded World-Plane Structures — Design

Date: 2026-10-11
Status: implemented 2026-10-11 (spec 560f44f / RED c6794eb / GREEN a67d2f4)
(defaults from the timed-out clarify round shipped: all sprite structures,
full column bases, procedural fence rails this slice — user flagged the
floating/yaw-fixed render before the round; the fix targets exactly that)
Slice: Vertical building slice 4 (closes the arc; follows stacking-slice
b4fc9b7/462248b; seam map: references/vertical-building-seams.md)
Design law: a structure stands IN the world — its base edge plants on the
terrain it occupies, its planes hold a fixed world facing, and only the
camera moves. Screen Y grows DOWNWARD: lifts subtract (standing rule).

## Goal

1. **Structures join the world plane system.** `RenderStructure` drops the
   flat camera-facing screen quad for the SAME crossed-billboard paper-doll
   the resources and entities already use (`BuildResourceCrossBillboard` /
   `ProjectBodyBillboard`, SpriteRenderer.cs:381-498): two static
   world-space half-plane pairs, base edge anchored at the structure's
   ground elevation, sprite texture drawn as U-strips per half
   (`DrawScreenQuadCornersTexturedUSpan`), colored crossed planes as the
   no-texture fallback. The not-a-billboard signature (plane extents flip
   as the camera orbits) is the test contract, mirrored from
   WorldBillboardResourceTests.
2. **No more floating (the user's finding).** The legacy call site anchored
   the quad's SCREEN CENTER at the projected point — the sprite hung in
   mid-air and yaw-tracked. The crossed planes anchor by their BASE EDGE on
   the projected world line; construction sites keep the blueprint tint/alpha
   and the progress bar (the bar stays screen-space under the anchor point,
   like every other overlay).
3. **Blocks draw as the column they are.** A block's base plants at its own
   CUMULATIVE column base — tile elevation + summed stack of the built
   blocks below it (new `BlockStack.RenderBase` seam; `RenderTop` stays =
   base + own height) — and its height is `StackHeight ×
   1/ElevPerWorldPx` world px (≈12.8 px/unit; a 2-unit mound ≈ 25.6 px).
   A stacked column reads as one contiguous stack, base on the ground.
   Game.cs's structure drawable passes the base elevation, not the top.
4. **Fences face and connect (slice-3 mask spent).** Blocks use
   world-AXIS facing instead of the deterministic per-tile hash: a fence
   (or mound row) with masked neighbors lies along the connection axis —
   `BlockStack.ConnectionMask` bits E|W → plane pair axis (1,0), N|S →
   (0,1), both or neither → keep the symmetric cross. Connected sides draw
   a thin RAIL quad from the fence center toward each connected
   tile-edge midpoint at rail height (fence top − 2): projected
   corner-quads, texture-free, unique plank-brown (RGB 122,86,50 — no
   terrain color matches) so pixel probes can prove extent.
5. **woven_shelter stays procedural but plants.** Its layered quads anchor
   by the bottom edge (screen.Y becomes the base, not the center). It has
   bespoke hand-tuned art; rewriting it as planes is polish-on-polish.
6. **Pixel-diff verification (declared slice-4 debt).** Smoketest staging
   hook `DSR_TEST_STRUCTURE=<id>:tx:ty` (authored BEFORE the KEYS block in
   InjectTestHooks — the fire-menu pitfall) places structures on flat
   authored ground; PIL probes in the toolbox assert: plane extents flip
   between two yaws; the mound base sits at the terrain line (no gap);
   the rail brown's pixel extent grows when a second fence is connected.

## Non-goals

- Rewriting woven_shelter as crossed planes (planted instead; see 5).
- Per-sprite hitboxes, shadows per plane (structures keep the existing
  single shadow or none — matched to whatever the resource path does).
- Camera focus riding block tops (was on the dev-map line: descoped —
  the depth-sort already handles occlusion; a focus lift is a UX
  experiment, not render correctness).
- NPC standing on block tops (slice-2 follow-up, still parked).

## Ground — verified in the tree

- `RenderStructure` (SpriteRenderer.cs:1304): single
  `DrawTexturedScreenQuad(screen.X, screen.Y, …)` — screen-aligned
  (yaw-fixed) and CENTERED at the projected point (floats). Game.cs:1952
  passes `RenderTop`-lifted elevation, worsening the float.
- Resources render the target shape already: `BuildResourceCrossBillboard`
  anchors base corners at `WorldToScreen(wx±, wy±, groundElev)` —
  bottom corners sit ON the ground line (SpriteRenderer.cs:397-400),
  drawn via `DrawScreenQuadCornersTexturedUSpan` with U strips (:175-179).
  `GetDeterministicDir` buckets per-tile facing over 180°.
- `ElevPerWorldPx = 1/(ZScale·TerrainHeightScale) = 1/12.8`
  (SpriteRenderer.cs:268; Constants.cs:24-25) — elevation units ↔ world px.
- LOD precedent: Mid-tier structures whose projected height < 6 px
  degrade to dots (resource path :167-170) — structures inherit the rule
  through the same draw path.
- Depth sort: Game's structure drawable uses
  `GetDepthSort(WorldX, WorldY, elev)`; passing base instead of top
  changes nothing topologically (same tile, monotone offset).
- Smoketest pipeline: `--smoketest <png>`, `DSR_TEST_KEYS`,
  `DSR_TEST_ITEMS`, `DSR_TEST_FIREMENU` precedent — hooks stage BEFORE the
  KEYS block in InjectTestHooks; framebuffer/logical scale 1.25 on this
  display; PIL in the toolbox does the probes (no vision provider).
- Suite baseline entering slice 4: 581 total / 580 green / 1 pre-existing
  skip.

## Design

`BlockStack.RenderBase(world, buildings, structure, tileElevation)`
(new pure static — the column-base half of cumulative RenderTop;
RenderTop 4-arg delegates: RenderTop = RenderBase + own StackHeight for
blocks; non-blocks stay tile elevation).

`SpriteRenderer.RenderStructure` rewrite (default scope = ALL sprite
structures; APPROVED-BY-DEFAULT pending the user's return):
- Compute base elevation: blocks → `BlockStack.RenderBase`; non-blocks →
  tile elevation as today. The `elevation` parameter's MEANING changes
  from center-anchor to base; the Game call site passes base.
- Facing: blocks → axis from `ConnectionMask` (needs TileMap +
  BuildingSystem in scope — RenderStructure gains the resolve via
  Game-provided optional world/buildings params, defaulting null =
  deterministic hash facing, keeping legacy callers compiling);
  non-blocks → `GetDeterministicDir(tileX, tileY)`.
- Geometry: `BuildCrossBillboard(camera, wx, wy, baseElev, halfW,
  heightWorld, 0f, dirX, dirY, halves, out anchor)`; heightWorld =
  StackHeight/ElevPerWorldPx for blocks, 32f default otherwise.
- Texture: same U-strip draw as resources; blueprint tint/alpha when
  under construction; progress bar unchanged (screen-space, below
  anchor).
- Fallback (tex == 0): crossed gray planes, same as resources.
- Fence rails: for each set bit in the mask, project a rail quad —
  endpoints (tile center, tile-edge midpoint toward that neighbor) at
  rail height → two projected points → thin corner-quad (rail color
  122/86/50). Rails draw AFTER the fence's own halves.

woven_shelter: keep the procedural stack, re-anchor to base edge.

`DSR_TEST_STRUCTURE` hook: parse "id:tx:ty[,id:tx:ty...]", place with
full materials granted (like DSR_TEST_ITEMS) on flat authored ground —
must run BEFORE the KEYS block.

## Testing plan

New `StructureBillboardTests.cs` (shape copied from
WorldBillboardResourceTests + VerticalLandingTests):
1. Structure planes' horizontal extents flip with yaw (world-fixed,
   not camera-facing) — campfire at (0,0) sprite path, orbits 0°/90°.
2. Base anchored at ANY yaw: each half's base edge midpoint lies on the
   WorldToScreen line of (wx, wy, baseElev) ±0.6 px ("no float" pin).
3. Column math: `RenderBase` of stacked mound #2 = tile + 2; of a fence
   atop mound = tile + 2; a lone fence = tile + 0. `RenderTop` 4-arg =
   RenderBase + own stack (delegation contract).
4. Fence axis selection: mask E|W → dir (1,0); N|S → (0,1); 0 or 15 →
   symmetric cross (both halves present, equal width class).
5. Non-block height default 32 world px: campfire halves' world height
   maps to ≈32 px projection at zoom 1 pitch 30 (within the pitch
   response band — assert against the projection's own math).
6. Block height: a mound (2 units) projects ≈25.6·pitchResponse px.
7. Rail endpoints: mask E|W returns two rail segments, each spanning
   center→edge-midpoint at rail height (pure-function pin).
8. Marshaled depth: structure drawable still sorts by its base point
   (GetDepthSort unaffected by the base/top swap on the same tile).
9. Legacy callers: RenderStructure without world/buildings (null) keeps
   crossed planes with deterministic facing — the API-signature carve-out.
Pixel pass (manual, toolbox): fence rail brown grows with a connected
neighbor; mound base hugs terrain; plane extents flip by yaw sweep via
DSR_TEST_KEYS rotation.

## Open questions (defaults chosen — user away at clarify time)

- Scope (all vs blocks-only)? → ALL sprite structures.
- Column drawing? → full column, cumulative bases.
- Fence rails this slice? → yes, procedural rails from the mask.
(woven_shelter carve-out and camera-focus descope are documented
in Goal/Non-goals.)
