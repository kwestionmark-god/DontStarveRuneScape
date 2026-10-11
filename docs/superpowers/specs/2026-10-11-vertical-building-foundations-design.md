# Vertical building, slice 1 — block defs + flat-pad gate + terrain flattening

- Date: 2026-10-11
- Status: implemented — slice 1 (RED 8b53a0c / GREEN 20965a3, 2026-10-11)
- Slice line: structures gain real vertical extent — block-typed defs in
  structures.json place on flat land only, and a shovel-driven "level the
  ground" timed action makes that flatness earnable, training construction.
  All edits persist across save/load as terrain overrides applied after
  worldgen rebuild. Later slices: jump landing on block tops, stacking /
  connecting, NPC pathing on tops.

## Design law

No gate without a way to open it, no way that lies: if a sloped tile
refuses a block, the refusal says so and the fix (level it) is a real
tool in the player's hand. Terrain edits are world edits — they survive
reload exactly like depleted nodes do, and a tile carrying a structure
or resource node cannot be leveled out from under them (no silent loss;
a block placed on your ground stands).

## Decisions (clarify round 2026-10-11, answered)

1. Scope: slice 1 = block defs + placement + flat-pad gate + the flatten
   tool. Jump landing / stacking / NPC tops pathing are slices 2–4
   (the dev-map 4-way split).
2. Block data model: extend structures.json / StructureDef with
   `is_block` + `stack_height` — the existing placement gates,
   colony-site path, and structure persistence carry the new type; no
   parallel voxel pipeline.
3. Flatten: a shovel-class tool action. Tile corners average to a target
   elevation, gated on construction, refuses water/occupied/resource
   tiles, and posts a visible notification on every outcome (no silent
   loss, no silent loss-prevention either).
4. Standing rule (forward contract for slices 2–4): standing elevation =
   terrain elevation + stack height of blocks under the stander; player
   first, NPC pathing on tops later.

## Goal

1. **Block types in structures.json.** StructureDef gains `IsBlock`
   (data key `is_block`) and `StackHeight` (data key `stack_height`,
   elevation levels per block, > 0 required when is_block). One entry-
   tier def ships with the slice: `dirt_mound`(working name
   `mound_block`) — 1×1 footprint, occupies_tile, cheap dirt-tier
   materials, construction level 1. Blocks reuse every existing gate
   (biome, skill, materials, colony-site staging).
2. **Flat-pad gate.** `BuildingSystem.PlaceStructure` refuses a block
   def on a non-flat tile: the tile's four corner elevations
   (neighbor-stitch quad, computed fresh from `Elevation`-of-neighbors)
   must spread ≤ `Constants.FlatPadMaxSpread` (default 0.01 levels —
   slope tiles carry a 1-level corner spread). Refusal message names the
   remedy: "Needs flat ground — level this tile with a shovel."
   Blocks also refuse on water (existing tile gate covers the rest).
   Non-block structures keep today's gates bit-identical (zero-spread
   requirement would retro-refuse every existing def on real terrain).
3. **Flatten tool + timed action.** New gear def `shovel` in gear.json
   `tools` (tier 1, durability, sprite_key reuse of an existing tool
   sprite — no new art), craftable entry-tier. New `ActionType.Flattening`
   (member + `GetSkillId` → "construction" + StartAction branch — the
   XP-routing minefield rule): holding/with the shovel equipped, the
   player runs a timed action on a target tile (the tile they stand on /
   face). Completion levels the tile: `tile.Elevation` := the MEDIAN of
   the 4 corner-source elevations (self + the 3 forward neighbors that
   BuildCornerElevations pulls from, TileMap.cs:74); the median is the
   value agreeing neighbors already hold, so a one-level step genuinely
   becomes a flat pad, where a mean would land between levels and still
   read sloped. Then the corner-stitch refresh
   (`TileMap.BuildCornerElevations()`) re-derives the quads. Pays construction XP (10, the structureal
   placement rate) and notifies "Ground levelled."
4. **Refusals, all notified:** water tile ("Can't level water."),
   tile with a structure or construction site ("Clear the structure
   first."), tile with a resource node ("Clear the resource node
   first."), target elevation would sink to/below sea level
   ("That would flood."), no shovel equipped ("Needs a shovel.").
   Materials are never consumed on a refused action.
5. **Persistence.** TileMap tracks elevation edits in a write set;
   SaveData gains `WorldSnapshot.TerrainEdits` (list of x,y,elevation),
   written at BuildSnapshot, applied after worldgen rebuild at load
   (RestoreWorld runs after the seeded regen — same convention as
   depleted nodes). Old saves read the list empty. Corner quads are
   rebuilt after overrides apply.

## Non-goals (slices 2–4)

- No jump landing/collision on block tops; `TryJump` stays visual-only.
- No block stacking atop blocks (OccupiesTile still refuses a second
  structure on a block's tile — the stack container is slice 3).
- No renderer lift of placed blocks to their top height; blocks draw at
  terrain level like every structure (slice 2 owns the lift).
- No worker/NPC terrain standing heights; no colony work-order for
  leveling (dispatch wiring is its own slice).
- No leveling kit structure, no free auto-level on placement (rejected
  options — the mechanic stays visible).

## Ground (verified in the tree, 2026-10-11)

- Terrain: `Tile.Elevation` (Tile.cs:164) is 0–31 integer levels
  (Constants.ElevationLevels=32, WorldGen.cs:167 floors noise into
  levels); `HasWater = Elevation <= SeaLevel` (Tile.cs:181).
  `CornerElevations` (Tile.cs:174) are built by
  `TileMap.BuildCornerElevations()` (TileMap.cs:74): each corner is the
  NEIGHBOR tile's center elevation — a flat pad is a tile whose four
  quad corners share its own elevation; a one-level step reads as a
  corner spread of 1.0.
- Placement gates: `BuildingSystem.PlaceStructure` (BuildingSystem.cs:39)
  checks in order — unknown def → tile null → occupied (Structure or
  active site) → biome → construction level → materials consume →
  Structure add with `tile.Structure = def` when OccupiesTile; hearth
  defs light via the firemaking param. No elevation check anywhere.
  `Structure` persists via BuildingSnapshot already.
- StructureDef (Data/Structure.cs:12) parses the doubly-nested
  structures.json envelope; a new def field = one init property + one
  parser line (+ JSON). `OccupiesTile`/`Hp`/`Materials` defaults already
  carry a block-shaped def.
- Actions: ActionType (Actions/ActionType.cs:8) has 5 members;
  `GetSkillId` throws on an unmapped member — the new member needs its
  switch arm ("construction") and a StartAction branch in the same
  commit, or XP routing lands in the wrong skill (the fishing-through-
  foraging bug class).
- Gear/tools: gear.json "tools" group carries axe/pickaxe/stone_*/
  torch with gear_type "tool", tier, durability — no shovel anywhere
  (grep zero). Tools gate player actions by equipped gear already.
- Persistence: restores rebuild the world from `Seed`; RestoreWorld
  (SaveSystem.cs:297) today only restores depleted nodes — terrain
  elevation edits are lost on reload without an override list.
- Notifications: player-visible lines via
  `Player.ActionSystem.AddNotification(text, rgb)` + FlushNotifications
  in tests.

## Testing plan (TDD)

RED (new VerticalBuildingTests + edits):
1. Data law: every `is_block` def has `stack_height > 0`,
   `occupies_tile: true`, and parses through LoadAll; the new
   mound_block def exists with construction level 1.
2. Flat-pad gate: block placement succeeds on a flat tile; refused on a
   slope tile (corner spread 1.0) with the "level this tile" message;
   non-block def on the same slope still places (no regression).
3. Flatten happy path: shovel-equipped timed action on a slope tile
   levels it (Elevation := corner-source median — the value agreeing
   neighbors already hold), corner quads refresh,
   a block then places on it; construction XP lands in construction,
   not elsewhere.
4. Refusals: water, structure-occupied, resource-node, sink-below-sea,
   no-shovel — each refuses with its exact message, consumes nothing,
   mutates no elevation.
5. Persistence: an edited tile + a placed block round-trip through
   SaveSystem BuildSnapshot/Restore (seeded regen + overrides); second
   restore keeps edit (overrides apply after regen, before BuildCorner-
   Elevations).
6. ActionType wiring: `GetSkillId(ActionType.Flattening)` ==
   "construction"; an equality pin that the enum stays mapped.

GREEN: minimal implementation in the order the tests read.

## Open questions

None for slice 1 — the four decisions above are on record. Slice-2
candidate already banked in the dev-map entry (jump landing/standing
rule, now contract #4).
