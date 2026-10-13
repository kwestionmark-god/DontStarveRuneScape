# Fire look reform — volumetric staged flames + voxel side faces (slice 14)

Date: 2026-10-12
Status: implemented — RED f98cde6 / GREEN b257d59 (2026-10-12),
suite 680 → 686 (685 green + 1 pre-existing skip);
render iterations vision-verified across 4 rounds (flat card →
units bug → starburst textures → signed-off teardrop tongues)
Slice line: vertical-building post-roadmap, slice 14 (the fire-visual
sequel: the user's "full fuel" screenshot critique)

## Design law (user's words, 2026-10-12)

"haha! the fire menu is established! however, this is what the fire
structure looks like with full fuel! it seems we need to create proper
surface area for the sides, and we need to work on the visual
designs/animations for different stages of fuel capacity!"

Two named fixes: (1) the 2x2 fire voxel's SIDE faces wear the top-ring
sprite stretched vertically — they need their own "surface" art; (2) the
flame is a flat screen-space card — it needs volume (crossed planes,
the engine's own prop idiom) and per-stage visual designs with
animation keyed to FUEL capacity (the reservoir's fill, not just the
unit's private burn cycle).

## Ground (verified in the tree)

- `SpriteRenderer.RenderStructure`'s voxel branch draws ALL faces with
  ONE texture (`tex` from `def.SpriteKey`, SpriteRenderer.cs:~1590 full
  UV per face after FillFace shading). The campfire's ring-top art on
  16px-tall side faces is the stretched-mat look in the user's shot.
  `BuildBrickVoxel` already tags faces 0-3 sides, 4 top — a per-face
  texture switch at the draw loop is all that's needed.
- `RenderFire` (SpriteRenderer.cs:1666 post-slice-13) draws flat
  screen-space quads (ember bed + body + tip) at the fire's ground
  point — screen-anchored, so orbiting shows a card, not volume.
  Crossed-plane idiom exists for structures (slice 4) and props:
  `CrossBillboardSorter` + the rig shared by RenderResource.
- Stage by reservoir fullness: slice-13's FireInteraction
  FiremakingUnitSeconds + stock math computes capacity &
  total-remaining; the render site (Game.cs:2141-2145) has
  game.Firemaking + game.BuildingSystem in scope.
- No render-time source existed; Game gains `RenderTimeSeconds
  (Update accumulates dt) and SpriteRenderer reads it for the
  flicker wobble. Deterministic on position too: fire.WorldX/Y seed
  the phase so two fires never flicker in lockstep.

## Design

### Voxel side art

- `StructureDef.SideSpriteKey` (data key `sprite_side`; default null →
  the top sprite everywhere, zero change for bricks).
- campfire gains `sprite_side: "structure/campfire_side"`;
  lined_fire_pit `sprite_side: "structure/stone_fire_ring_side"`.
- New authored textures (32 wide x 16 tall, full-bleed): a descending
  stone-course band (the pit wall seen edge-on), with a scorch gradient
  toward the top lip. The voxel draw loop picks sideTex for planes 0-3,
  the top sprite for plane 4. fp=1 behavior is unchanged (no def
  carries sprite_side today except the two fires).

### Volumetric staged flame

- `FireMenuPanel`-independent: `FiremakingSkill.StageOf(fire,
  buildings)` — the CAPACITY stage the user asked for: stage on
  (fire.Remaining + hearth stock × per-unit × rate) / (10-unit
  capacity); ground fires fall back to Remaining/MaxTime.
- `RenderFire(fire, stage, batch, camera, sprites, timeSeconds)`:
  TWO world-anchored perpendicular flame planes (X-axis + Y-axis
  through the fire's ground point) — the engine's crossed-plane idiom
  gives volume at every yaw. Per-stage geometry: embers (flat glow
  only), low (16h), burning (28h), roaring (42h x 20w). Flicker:
  ±8% height/width wobble + 1px x drift, phase =
  fire.WorldX*0.7 + fire.WorldY*1.3 + time*6 (position-seeded so fires
  never beat together).
- Authored flame sprites x4 (32x48, real alpha): fire_ember (coal
  glow cluster), fire_low (single lick), fire_mid (two licks), and
  fire_roaring (triple-lick crown) — deterministic PIL, warm palettes,
  darkest crust lowest.

## Testing plan (RED — FireFlameVisualTests.cs, ~10 pins)

1. `SideSpriteKey` parsed: campfire/lined pins + brick stays null.
2. IHDR pins: side textures 32x16 x2 exist + 4 flame textures 32x48.
3. `StageOf`: ground fire = own-fraction staging; hearth mid-burn with
   stock stages on TOTAL capacity (a 20s-left unit + 4 stocked units =
   stage 2 burning even as the unit enters embers at <7.5s — the
   "full fuel LOOKS full" law).
4. Stage machine pins: capacity fractions 0/1/2/3 (already covered by
   BurnStageOf table — reuse, no dup: StageOf's return IS
   BurnStageOf(total, capacity) — pin through StageOf with harness).
5. Smoketest staging: `DSR_TEST_STRUCTURE=campfire:lit` (the 2-part
   grammar — auto dry tile + stocked + burning; the original
   `campfire:auto:lit` colon form silently no-op'd — smoke hooks must
   never no-op silently) + DSR_SEED=42 (changed-seed A/B is invalid —
   StartNewGame randomizes the seed per boot) + DSR_TEST_HOUR=14 +
   DSR_TEST_YAW=0/22 sweeps. Renders verified by a vision-iteration loop
   (the model's own eyes on the captures): v1 flat-card flag, v2 the
   world-px-as-elevation-units bug (the pit is 0.5 units; ~18 px/unit at
   this pitch — re-scaled), v3 proportions pass but radial-starburst
   textures rejected, v4 teardrop tongues (belly sin growth + tip pinch
   + bent tips + side forks + sparks) SIGNED OFF. Suit record: the
   RenderFire dead-code and the `!diag` axis flip were both written,
   caught by reading the diff, and fixed pre-capture.

## Verification log

- Iteration 1 (axes planes, world-px heights ~42): flat starburst of
  the wrong scale; vision verdict FAIL.
- Iteration 2 (heights in units, X+diag planes): proportions right for
  the last time but planes collapsed at 45°; vision verdict FAIL.
- Iteration 3 (0.2/0.6/1.1/1.7 unit stages): height solved, silhouette
  = spiky texture (the generator's fault, not the geometry); vision
  verdict: fix the SPRITES.
- Iteration 4 (teardrop tongues + side forks + sparks): SIGNED OFF —
  upright tongues, ~2 pit-bands, yaw-shifting silhouette, stone pit
  readable, no breakage.

## Non-goals

- True per-frame flame sprite ANIMATION frames (flicker is geometric
  wobble this slice; frame-flip animation is the banked arc item).
- 3x3/4x4 footprints; WarmthRadius; walking-through damage.
