# Structure sort honesty — statically visible player builds (vertical building slice 10)

Date: 2026-10-12
Status: implemented — RED e3705b0 / GREEN 79cb827; suite 643 total /
642 green / 1 pre-existing skip; bury/no-op pixel A/B verified (numbers
in the dev-map slice-10 entry)
Slice line: vertical-building post-roadmap, slice 10 (user critique of the
slice 7-9 brick world, 2026-10-12)

## Design law

User critique, verbatim (drives this slice):

> "the structures are very sensitive to yaw/pitch panning.. id like any
> player built structures to be statically visible, the only time they
> should have reduced visibility, is when something is actually in the
> way.. and even then they should be statically initialised, only not
> visible when there is something closer to the camera in front of them.."

Clarified 2026-10-12: the symptom is DIRT BRICKS sinking/burying into
the ground at some camera angles and coming back while panning, mainly
on the yaw orbit.

A structure must never lose the painter's-order tie with its own
terrain tile. Reduced visibility comes only from something genuinely
nearer the camera — a hill, a raised neighbour — never from the owning
tile painting over the build that stands on it.

## Goal

1. **Structure sort key = the entity law.** Structures key with
   `SortElevation` (the tile's highest corner — at or above the tile
   quad's corner-average key) composed with `RenderBase`, plus
   `StandingBoost()` (the largest in-tile positional deficit). Today
   the structure loop keys the RAW tile elevation with no boost
   (Game.cs:2097), while terrain quads key the bilinear tile center
   (TileRenderer.cs:134-135) — a brick on the far side of its tile
   (sub-cell offset up to ±24 px along the view axis) keys BELOW its
   own tile and the tile quad paints over it. Empirically confirmed
   2026-10-12: brick at sub-cell (1,1), flat pad, yaw 45° — fully
   buried (0 px); yaw 225° — fully visible (1877 px).
2. **Ground-fire sort key gets the same law.** The fire loop keys
   `GetDepthSort(fire.WorldX, fire.WorldY, 0)` — elevation 0 is below
   every land tile's key; fires rely on luck and sprite height. Key
   with the fire tile's `SortElevation` + boost.
3. **Genuine occlusion preserved.** A raised tile genuinely between
   the camera and a structure still keys nearer (its elevation term
   dominates the boost) — the fix only wins the own-tile tie. The
   slice-8 contract "the lowest rows are legitimately under the raised
   ground" keeps holding.

## Non-goals

- No geometry changes: brick boxes are proper 3D voxels (back-face
  culling shows 2 sides + top at any yaw — the box never vanishes
  geometrically); crossed-plane structures already carry the full
  sprite per plane and the tempered pitch response
  (ProjectBodyBillboard clamps 0.75-1.3), so prop-style structures
  (campfires, workbenches) are already size-stable — the user has
  only tested bricks and the bug is pure draw ORDER.
- No change to entity keys (player/monsters/NPCs/pets already follow
  the law — this slice brings structures and fires up to it).
- No change to resource-node keys (tile-centered, out of scope).
- No structure-vs-structure reorder work: mutual occlusion between
  builds follows from the same key once the own-tile tie is honest.

## Ground (verified in the tree 2026-10-12)

- The bury mechanism: `Game.cs:2097` —
  `GetDepthSort(structure.WorldX, structure.WorldY, elev)` where
  `structure.WorldX/Y` is the SUB-CELL center for bricks (up to
  ±24,±24 px from the tile center, BuildingSystem.cs:125-126) and
  `elev = RenderBase(raw tile.Elevation)` — no `SortElevation`, no
  `StandingBoost`.
- Terrain quads key at the tile center with the bilinear center
  elevation: TileRenderer.cs:133-135 (`depthPx = ((y+0.5)·cy +
  (x+0.5)·sy)·TileSize + GetElevationAt(0.5,0.5)·ZScale·0.5`), added
  to the SAME painter's list as sprites (Game.cs:1840, sorted at
  :2129 larger-key-draws-later with the seq tie-break favouring
  sprites, which enter after tiles).
- The entity law already exists verbatim for movers: player
  Game.cs:2008-2009 (`SortElevation(tile, elev), StandingBoost()`),
  monsters :2027-2028. `SortElevation` = the tile's highest corner
  (Game.cs:2200-2207, "the max keeps the sprite's key at or above its
  own tile's on any slope"); `StandingBoost` = 0.5·TileSize·(|cos|+
  |sin|) (Game.cs:2188-2193, "largest depth-key deficit a moving
  sprite can have against its own tile" — covers ANY point inside the
  tile, sub-cell centers included).
- Fires key elevation 0: Game.cs:2121-2125.
- Empirical confirmation (same-camera captures, flat staged pad,
  `DSR_TEST_STRUCTURE="mound_block:12:12:1:1"`):
  - yaw 45° (offset·viewAxis = −11.3 px): brick FULLY BURIED — no
    brick-palette cluster at its predicted screen position.
  - yaw 225° (+11.3 px): brick fully visible, 1877 palette px.
  - corner sub-cells ((0,0)/(3,3), ±24 px deficits) bury half-way —
    the box pokes above the tile's screen top edge (partial bury).
  - a near-side brick (+offset) is always visible (control).
- Camera staging for exact angles: `Camera.SetOrbit(yaw, pitch?)` +
  `DSR_TEST_YAW`/`DSR_TEST_PITCH` (degrees) hooks — new this slice
  (the orbit keys cannot land an exact angle).
- Pitch's role is secondary: pitch changes how much of a buried box
  pokes above the owning tile's screen footprint (the box's up-screen
  extent is fixed by elevation; the tile quad's screen height shrinks
  with cosPitch). Fixing the key kills the whole symptom at any
  pitch.

## Design

### 1. Sort-key helpers on Game (the testable seam)

```csharp
/// <summary>Sort key for a placed structure (slice 10): the entity
/// law — the tile's highest corner composed with RenderBase, plus the
/// standing boost — so a structure never loses the painter's-order tie
/// with its own terrain tile. A brick keys at its sub-cell center (up
/// to ±24 px from the tile center); without the boost the owning tile
/// quad keys nearer at the yaws where that offset lands on the far
/// side of the view axis and paints over the build — bricks "sink
/// into the ground" while orbiting, then come back.</summary>
internal float StructureSortKey(Structure structure, TileMap? world,
    BuildingSystem? buildings);

/// <summary>Sort key for a ground fire (slice 10): the fire tile's
/// highest corner + the standing boost. The old key (elevation 0) sat
/// below every land tile's key — fires sorted behind their own
/// ground.</summary>
internal float FireSortKey(float worldX, float worldY, TileMap? world);
```

RED stubs reproduce today's math exactly (RenderBase(raw elev), no
SortElevation, no boost; fires elevation 0). GREEN fills in
SortElevation + StandingBoost.

### 2. TileRenderer.TileSortKey extraction

The inline terrain key formula (TileRenderer.cs:134-135) moves to
`internal static float TileSortKey(TileMap world, int tx, int ty,
float cy, float sy)` — one source for the render loop AND the tests
(no parallel formula drift).

### 3. Call sites (GREEN)

Game.cs structure loop: `sortY = StructureSortKey(structure, World,
BuildingSystem)`. Fire loop: `sortY = FireSortKey(fire.WorldX,
fire.WorldY, World)`.

## Testing plan

RED (new `StructureVisibilityTests`; Game + Camera staged, yaw swept
in π/16 steps):

1. `BrickSortKey_FarSideSubCell_NeverBelowOwnTile` — flat pad, bricks
   at sub-cells (1,1) and (0,0): StructureSortKey ≥ TileSortKey(own
   tile) at every sweep angle. RED fails ~half the sweep (the far
   sides).
2. `CampfireSortKey_SlopedTile_NeverBelowOwnTile` — non-block on a
   sloped tile (stitch the neighbors to separate the raw elevation
   from the bilinear center): same sweep contract. RED fails.
3. `FireSortKey_NeverBelowOwnTile` — staged fire: same contract. RED
   fails (elevation-0 key).
4. `StructureSortKey_RaisedNeighbourStillOccludes` — the honesty pin:
   a +6-level raised tile on the camera side keys NEARER than the
   structure at every sweep angle (the boost must not lift a build
   above genuine terrain). Passes at RED and must stay green at GREEN.
5. Control pins: near-side sub-cell bricks key above their tile
   (passes at RED).

GREEN: helpers get the law; call sites wired. Full suite 638 → 638+N
green, 1 pre-existing skip.

Pixel verification (same-camera pre/post A/B, `DSR_TEST_YAW` staging):
brick at sub-cell (1,1) — yaw 45°: pre = buried (0 palette px, the
confirmed capture), post = visible (~1877 px); yaw 225° control:
identical staging, pre/post both visible (the key change is a no-op
where the order already favored the brick). Slice-8 raised-neighbour
contract re-checked: the raised neighbour still covers the brick's
low rows.

## Open questions

None — the symptom (bricks sinking while orbiting), the mechanism
(own-tile painter's-order deficit), and the fix (the existing entity
law extended to structures and fires) were all confirmed against the
tree and by same-camera captures on 2026-10-12.
