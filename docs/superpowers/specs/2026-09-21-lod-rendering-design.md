# LOD Rendering & Distance-Relative Sprite Sizing — Design

**Date:** 2026-09-21
**Status:** Approved (chat approval) — Stage 1 implementation
**Scope:** Render pipeline (`Render/`, `Camera/`, `Core/Game.cs`), `Config/Constants.cs`

## Goal

Two goals, one pass:

1. **Performance scales with camera distance.** Zooming out (or a future
   larger map) must not tank the framerate; today every visible tile draws a
   textured quad and every visible resource a sprite, and the whole visible
   tile set is depth-sorted every frame.
2. **Sprites size realistically with distance.** Tree > rock > stick, and all
   sprites shrink smoothly with zoom rather than staying a hardcoded size.

## Measurement first (Stage 1)

No optimization without numbers:

- HUD debug line: FPS + average frame ms (rolling 1 s), toggled by F3.
- `--smoketest-bench <N>` CLI flag: renders N frames with fixed camera
  (env `DSR_CAM_PITCH/YAW/ZOOM`), then prints avg / p95 frame ms and exits.
  Baseline recorded before any LOD change.

## LOD model (Stage 2)

A single derived value drives everything: `LodTier = Nearest | Mid | Far`,
computed from camera zoom each frame (thresholds in Constants, tunable
on the wire via env for smoketest captures).

| Surface | Nearest | Mid | Far |
|---------|---------|-----|-----|
| Terrain overlay texture | on, tinted | on | off (color quad only) |
| Tile depth sort | full | full | coarse (bucketed, stable) |
| Resource sprites | full | sprites; tiny ones (<8 px) become dots | dots or culled by rarity |
| Particles / AA extras | on | reduced | off |

Terrain texture-off at Far is the biggest saving: it removes the per-tile
textured pass and its batch flushes. Sprite dots reuse the existing colored
quad path (no texture bind at all). Rarity-based culling at Far uses the
`ResourceDef.Rarity` already on nodes: `uncommon+` keep dots, `common`
dot, `ubiquitous` culled.

The tier switch must read identically at default zoom (Nearest) to the
current build's output — validated by smoketest capture diff.

## Sprite size realism (Stage 3)

- `ResourceDef.DisplayScale` (new JSON field, default 1.0): multiplies the
  draw size, decoupled from the 24px constant. Species data gets sensible
  values (oak 1.6, birch 1.4, shrubs 0.8, sticks 0.6).
- `WorldScale` semantics: `half = 24 * DisplayScale * camera.Zoom` — zoom
  linearly moves sprite size, so zooming out genuinely shrinks things.
- Seeds for future variants: `GetSpriteKey()` may append a deterministic
  per-tile variant suffix (`_v0.._v3`); missing variant files fall back to
  the base key (misses are already cached).
- Existing tint/shading behavior unchanged.

## Explicitly out of scope (now)

- Chunked terrain baking (bigger win, needs framebuffer churn) — revisit
  after Stage 2 numbers.
- 3D object models for resources.
- Sprite atlases / GPU instancing (only if benchmark shows batch-flush
  pain after Stage 2).

## Risks

- Tier switching must not pop: hysteresis band around thresholds (±5% zoom)
  before the tier changes.
- Depth sort every frame is O(n log n) over visible tiles; at Far, bucketing
  per 4-tile row is acceptable but must not interleave sprites wrongly — the
  sprites keep full-precision depth keys regardless.
- `DisplayScale` > 1 sprites may overlap deeper rows — painter ordering uses
  the tile ground point, which stays correct regardless of sprite size.

## Testing

- `--smoketest-bench 600` numbers recorded in this doc per stage.
- Smoketest captures at pitch 10/30/60/80, zoom 0.5/1.8/3.0, yaw 0/90/180,
  eyeballed for: coverage (no unpainted bands), painter correctness
  (nearer never overdrawn by farther beyond a tile edge), anchoring
  (sprites pinned to tiles).
