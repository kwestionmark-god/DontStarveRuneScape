# World-Fixed Resource Billboards — Design Spec

- **Date:** 2026-10-07
- **Status:** implemented same day (defaults picked, user standing instruction)
- **Slice line:** dev-map frontier candidate — "static resource sprites":
  inanimate resource nodes render as world-fixed crossed billboards, the
  same 3D paper-doll applied statically to entities.
- **Design law:** tri-fusion — DS world dressing reads as objects planted
  in terrain, RS-style fixed world geometry, MC-style readability (no
  shimmer, no re-anchoring). Resources are inanimate: their planes never
  track a gait direction or the camera; they are fixed to the world lines.

## Goal

- Resource nodes draw as crossed billboards: two static world-space planes
  at 90° standing on the tile-center world line, split at the fold into
  four half-quads, painted back-to-front by view depth — identical
  machinery to the entity paper-doll, with a deterministic per-tile
  facing instead of a gait direction.
- Anchored to terrain elevation (bilinear tile-center value, unchanged).
- Same nominal size as the legacy camera-facing quad at default
  pitch/zoom; height foreshortens with camera pitch exactly like entity
  bodies (tempered response clamp 0.75–1.3).

## Non-goals

- No new art — existing sprites reused across both planes (dev-map:
  "reuse existing art for a first pass").
- Ground-decal resources (water pools, fish spots) stay flat on the tile
  footprint.
- LOD behavior unchanged: Far-tier dots, Mid-tier sub-6px dot gate.
- No entity body changes, no Game.cs changes (resources keep the
  ground-point external sort at Game.cs:1518).
- No per-resource facing field or persistence — facing is a pure hash of
  tile coords (regenerates identically, needs no save data).

## Ground — verified in the tree 2026-10-07

- `SpriteRenderer.RenderResource` (Render/SpriteRenderer.cs:43) draws a
  camera-facing screen quad (`DrawTexturedScreenQuad`, line 119) anchored
  at the projected tile center — the "player's lateral perception" this
  slice removes.
- `BuildCrossBillboard` / `ProjectBodyBillboard` (same file) already build
  the crossed paper-doll for player/monsters/NPCs: two static world planes
  on the travel axis, each split at the fold (U spans 0..0.5, 0.5..1),
  sorted by `CrossBillboardSorter` view depth.
- `Camera.WorldToScreen` is affine in world (x, y) at fixed elevation —
  the projected midpoint of a plane's base edge is exactly the projected
  ground point, so world-line anchoring is exact at every yaw.
- Projected plane width is yaw-invariant (the yaw rotation is an isometry
  on same-elevation points); the honest not-a-billboard signature is the
  two planes' horizontal extents flipping as the camera orbits.
- `InternalsVisibleTo DontStarveRuneScape.Tests` is set; `BillQuad` and
  `CrossBillboardSorter` are internal (test-reachable).

## Design

1. **Deterministic facing** — `GetDeterministicDir(tileX, tileY)`: bucket
   = (tileX·73856093 ⊕ tileY·19349663) & 15 → one of 16 orientations over
   180° (a plane's axis is a line, not a ray). Odd multipliers make the
   low nibble a bijection in each coordinate: 16 consecutive tiles hit
   all 16 buckets. Inanimate = fixed: the facing never changes between
   frames or sessions.
2. **Builder** — `BuildResourceCrossBillboard(camera, tileX, tileY,
   groundElev, halfWidthWorld, heightWorld, halves, out anchor)`:
   resolves the tile-center ground point and calls the existing
   `BuildCrossBillboard` with lean 0 and the deterministic dir; both
   planes the same width (resources are roughly symmetric sprites).
3. **Sizes** — legacy quad: half = 24·scale·zoom screen px, square.
   World sizes: halfWidthWorld = 24·scale, heightWorld = 96·scale (zoom
   lives in the projection now; 96·sin30°·zoom = 48·scale·zoom = legacy
   height at the default pitch).
4. **RenderResource wiring** — texture path draws the four sorted halves
   via `DrawScreenQuadCornersTexturedUSpan`; untextured fallback draws
   crossed colored planes (`DrawScreenQuadCorners`, stage tint kept);
   shadow / LOD / variant / decal logic unchanged.

## Testing plan

- DeterministicDir: same tile → same dir; a 16-tile row → 16 distinct
  dirs (the bijection); unit length.
- Not-a-billboard signature: the two planes' on-screen horizontal extents
  flip between yaw 0 and yaw 90 (a camera-facing quad is yaw-invariant).
- World-line anchoring: base-edge midpoint == projected tile-center
  ground point across yaws.
- Pitch: projected height rises from pitch 20° to pitch 50° (the response
  clamp keeps both readable but strictly ordered).
- Builder contract: 4 halves, ascending depth, both planes present, U
  spans folded at 0.5, anchor on the tile-center world line, identical
  across rebuilds.
- Scale: doubling display scale doubles the projected size.
- Full suite green (entity billboard math untouched — regression guard).

## Open questions (resolved — defaults)

1. Facing source: coordinate hash — no persistence, no shimmer.
2. Plane widths: both equal (symmetric-ish sprites).
3. Height: 2× half-width — legacy square-sprite parity at default pitch.
4. Depleted/young nodes: same crossed geometry, existing sprite keys
   and fallback tint.
