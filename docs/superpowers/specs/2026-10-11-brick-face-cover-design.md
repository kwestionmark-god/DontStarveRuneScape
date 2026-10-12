# Brick face cover + elevation-adjacent faces (vertical building slice 8)

Date: 2026-10-11
Status: draft
Slice line: vertical-building post-roadmap, slice 8 (brick render polish)

## Design law

User critique, verbatim (drives this slice, overrides any generic
backlog text):

> "the textures still dont fill the surface area of a single brick..
> i notice that bricks are also iffy about visibility when adjacent
> to varying elevations.."

A brick face must read as a BRICK FACE: the texture covers the whole
face (no icon-in-a-field), and a brick never loses a side to bare
terrain — only a neighbouring brick column can occlude it.

## Goal

1. **Full-bleed brick texture.** New authored 16x16
   `structure/dirt_brick.png`: every pixel painted, pixel-art brick
   courses with mortar lines, dirt palette. `mound_block`'s
   `sprite_key` points at it; `stone_wall` stays for other callers.
   User decision 2026-10-11 (over UV-crop and procedural options).
2. **Bricks-only occlusion.** A brick's side face is culled/stripped
   only by a neighbouring BRICK (or tile-level block) column, never by
   raw terrain elevation. Consequence the user accepted: a brick against
   a steep slope keeps its face and may poke through the hillside
   slightly. User decision 2026-10-11.

## Non-goals

- No change to the tile-slab voxel branch (slice-5/6 contracts stay
  bit-identical; `NeighbourTop` there is untouched).
- No change to movement/standing math (CanStep, SubHeightAt, BlockHeightAt
  all keep their current elevation semantics — this is RENDER-ONLY).
- No new brick materials, no drag-paint, no NPC block-top standing
  (parked follow-ups, later slices).

## Ground (verified in the tree 2026-10-11)

- Brick render branch: `src/DontStarveRuneScape/Render/SpriteRenderer.cs:1514-1542`.
  `SubNeighbourTop` (:1522-1527) returns
  `neighbourTile.Elevation + BlockStack.SubHeightAt(...)` — the raw
  neighbour elevation is baked into the "occluder top", so a bare taller
  tile culls or strips faces.
- The cull lives in `BuildBoxVoxel.Edge` (:344-345):
  `lo = max(base, neighbourTop); if (lo >= top) return;` — with
  neighbour elevation ≥ brick top, the face dies entirely.
- The tile-slab branch (:1546-1559) has the same elevation-inclusion,
  but no built tile-slab def exists any more (mound_block is a brick);
  it stays untouched per non-goals.
- Texture path: textured faces draw the sprite at full UV
  (:1580-1588) on top of the tinted underlay. The sprite is
  `structure/stone_wall.png`: measured 2026-10-11 with PIL — 16x16,
  80/256 px opaque (**31%**), bbox x3..12, y8..15. So ~69% of every face
  is bare underlay colour even at the slice-7b 1:1 mapping.
- `mound_block.sprite_key == "structure/stone_wall"`
  (`src/DontStarveRuneScape/Data/structures.json:19`).
- `GetSpriteBaseColor` (SpriteRenderer.cs:1726) averages opaque pixels
  at load — the underlay auto-follows the new texture, no change needed.
- Sprite key → file: `Assets/sprites/<key>.png` under the main project
  (verified: `src/DontStarveRuneScape/Assets/sprites/structure/stone_wall.png`).
- IHDR header parse gives PNG width/height at bytes 16-23 (big-endian)
  without a decoder — the asset test can pin 16x16 cheaply.

## Design

### Brick-column occluder query (BlockStack)

New pure static alongside the other vertical queries (BlockStack is the
single query seam):

```csharp
/// <summary>Occluder top for brick face culling (slice 8): the brick
/// column top at a sub-cell — neighbour tile elevation + its sub-cell
/// brick height — or null when the sub-cell holds no bricks and no
/// tile-level block. NEVER raw terrain elevation: bare ground does not
/// occlude a brick face.</summary>
internal static float? BrickColumnTopAt(TileMap world, BuildingSystem buildings,
    int tx, int ty, int subX, int subY);
```

Returns null when `SubHeightAt` at that sub-cell is 0 (no bricks, no
tile-level lift). Where it returns a value the existing
`max(base, top)` strip logic in `BuildBoxVoxel` is reused unchanged.

`RenderStructure`'s brick branch calls it for the four wrapped
sub-cell neighbours, substituting `float.NegativeInfinity` for null
when calling `BuildBrickVoxel`. Note `Edge` treats any value below
`base` as "no strip lost", so `NegativeInfinity` is the no-occluder
value; the face then paints base→top in full.

### Texture asset

`dirt_brick.png` authored 16x16, full-bleed: 3 courses of bricks
(5px/5px/4px rows + 1px mortar = 16), offset joints, dark mortar
(~60,42,26), dirt-brick faces in 2-3 close browns (~122,88,54 /
132,96,60 / 112,80,48) so the face reads as masonry at 1:1. Written
deterministically (seeded shade pick per brick cell) so the art is
reproducible. `structures.json` mound_block sprite_key becomes
`structure/dirt_brick`.

## Testing plan

RED (assertion failures, stubs compile):

1. **Asset pin** (new `BrickFaceCoverTests` or into
   VerticalBuildingTests): mound_block sprite_key is
   `structure/dirt_brick`; the file exists under the main project's
   `Assets/sprites/structure/dirt_brick.png`; IHDR reads 16x16.
   RED on all three (file absent at RED time).
2. **Occluder query** (BlockStack.BrickColumnTopAt — land as a stub
   returning a value so RED compiles):
   - neighbour tile ELEVATED (e.g. +12 elevation) with NO bricks ⇒
     null (RED: current behaviour would bake the elevation in — the
     stub mimics it or the method is absent-but-stubbed; assert null).
   - neighbour with a brick column ⇒ tile elev + column height.
   - neighbour tile-level fence ⇒ at least the fence lift (SubHeightAt
     floor), i.e. non-null.
3. Slice-5/6 tile-slab voxel contracts and slice-7 brick geometry
   (BlockVoxelTests / brick ghost tests) must stay GREEN at RED time
   (compile-only interaction).

GREEN: author png (toolbox PIL), repoint the def, implement
BrickColumnTopAt + wire it into RenderStructure's SubNeighbourTop.

Pixel verification (headless smoketest, same-camera A/B):

- A/B texture: stage 1 brick before/after the texture change (stash
  baseline) — count DISTINCT colours inside the brick's top-face
  region; old render ≈ 2 (underlay + glyph fleck), new render ≥ 4
  (bricks + mortar). Palette pulled from the actual PNG.
- A/B elevation: stage capture A = brick alone on flat ground;
  capture B = same brick with the NEIGHBOUR tile raised +12
  (SetElevationOverride on its stitch tiles) — camera shared via the
  anchor entry. Assert brick face-region pixel mass in B ≥ ~90% of A
  (pre-fix B culls a face and mass collapses).

Full suite must stay 616/615 green + 1 skip (plus new tests).

## Open questions

None — texture route (authored full-bleed), occlusion rule
(bricks-only), and session scope (this slice then straight into the
next building item) were answered 2026-10-11.
