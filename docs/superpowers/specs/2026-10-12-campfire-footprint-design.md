# Campfire footprint reform — the first multi-sub-cell structure (vertical building slice 12)

Date: 2026-10-12
Status: implemented — RED d4149f6 / GREEN b71c4e5 (2026-10-12),
suite 648 → 659 (658 green + 1 pre-existing skip)
Slice line: vertical-building post-roadmap, slice 12 (the banked
"structure footprint reform" arc, user-picked start: campfires)

## Design law

User ideation (banked verbatim 2026-10-12): "for example, for a
campfire, lets explore a 2x2 structure with fire animations? add
collision.. perhaps even damage upon collision or while standing
on... variants of campfires, like lined pits that could cost more, but
also not cause damage upon collision... a burn rate, etc.. which can
play meaningfully into the variants framework."

Structures live on the 4x4 grid with real footprints: a 2x2 campfire
is a squat solid in the brick world — grounded movers cannot pass,
the player CAN jump onto it, and standing on a lit one hurts. Variants
are sibling defs whose cost buys safety and efficiency.

User decisions 2026-10-12 (all recommended defaults): 2 HP/s standing
damage while LIT; squat voxel look with authored 32x32 full-bleed
texture; Stone-Lined Fire Pit at stone 6 + stick 2, construction 2,
zero contact damage, 1.5x burn (135 s); the campfire def upgrades IN
PLACE (saves migrate to a centered footprint).

## Goal

1. **Def-level footprint.** `footprint` (sub-cells per side, square;
   parsed to `StructureDef.Footprint`, default 1). Footprint > 1
   structures place at a sub-cell ANCHOR with the footprint extending
   +x/+y; the tile-level `PlaceStructure` overload auto-centers a
   footprint def (anchor (1,1) for 2x2 — its center equals the tile
   center, so every existing id-based call site and save keeps
   working). Anchors that would spill the tile refuse.
2. **Campfire reform.** `campfire` becomes `is_block: true`,
   `stack_height: 0.5`, `footprint: 2`, `occupies_tile: true`,
   `contact_damage: 2`, `burn_rate: 1.0`. Collision rides the block
   machinery for free: SubHeightAt 0.5 at the four covered cells —
   grounded movers are walled everywhere (GroundedSlide/CanStep/
   CanStand), the player hops on (climb cap 2.05 > 0.5) — where the
   fire bites. Occupies_tile true keeps tile-level pathing
   conservative (the established granularity for anything solid).
   Consequence (accepted): block defs gate on the flat pad — campfires
   now need level ground, consistent with the brick world.
3. **Authored 32x32 sprite.** `structure/campfire.png` re-authored
   full-bleed (stone ring, charred logs, ember glow — deterministic
   PIL art, every pixel painted, the slice-8 law); the voxel branch
   renders the footprint rect (32 world px) with full-UV faces = 1:1.
   The flame keeps animating via the existing FireInstance render
   (placement auto-lights it — BuildingSystem.cs:258).
4. **Standing damage.** `contact_damage` (HP/s, default 0): while a
   fire is LIVE at the structure's position, the PLAYER standing
   inside the footprint takes the damage — 2 HP/s lit-only, applied
   as whole HP with an accumulator, "You're standing in the fire!"
   notification on the first bite (the no-silent-losses rule).
   Home: `FiremakingSkill.TickContactDamage(player, buildings, dt)`
   (firemaking owns fire state), called from the game loop beside
   `Firemaking?.Tick(dt)`.
5. **Burn rate.** `burn_rate` (multiplier, default 1.0) →
   `StructureDef.BurnSeconds => LightSeconds * BurnRate`. All three
   light call sites read BurnSeconds (placement auto-light
   BuildingSystem.cs:258, F-relight FireInteraction.cs:56, worker
   site-light RecruitmentSystem.cs:340) — variants tune burn without
   touching code.
6. **Stone-Lined Fire Pit variant.** New def `lined_fire_pit`: stone 6
   + stick 2, construction 2, hp 20, `footprint: 2`, `stack_height:
   0.5`, `occupies_tile: true`, `contact_damage: 0`, `burn_rate: 1.5`
   (135 s), sprite `structure/stone_fire_ring` (authored 32x32),
   light_seconds 90. The deluxe safe hearth.
7. **Legacy migration.** RestoreSnapshot: a tile-level row of a
   Footprint>1 def migrates to the centered footprint — and MUST be
   checked BEFORE the mound→32-brick blow-up (BuildingSystem.cs:398),
   which a now-IsBlock campfire would otherwise hit.

## Non-goals

- No standing damage for NPCs/monsters (grounded movers cannot be on
  the footprint; only the player can jump up there).
- No footprint generalization to every structure yet (campfire +
  lined pit are the arc's first vehicles; the machinery is def-driven
  so the next structures are data).
- No cross-tile footprints (anchors spill-refuse), no non-square
  footprints, no stacking on footprint structures, no repair.
- No fire-menu/proximity changes (id-keyed, unaffected — verified:
  GetAvailableStructureIds is id-based).

## Ground (verified in the tree 2026-10-12)

- `campfire` def: occupies_tile FALSE today (no collision at all),
  burnable, light_seconds 90, sprite structure/campfire, materials
  stick 2 + stone 2, levels 1 (structures.json:36-66).
- Placement auto-lights: BuildingSystem.cs:258-259
  `LightHearth(def.LightSeconds, …)`; F-relight FireInteraction.cs:56;
  worker site-light RecruitmentSystem.cs:340.
- Placement flow: UpdatePlacement (Game.cs:582-619) routes
  `IsBlock && id != fence_block` to the 5-arg sub-cell call — the
  campfire rides it automatically once IsBlock; the ghost grid
  (Game.cs:2358-2374) already renders 4x4 cells for IsBlock defs with
  the hovered cell solid — the 2x2 highlight extends it.
- Brick occupancy partition: BuildingSystem.cs:166-168 (refuse unless
  every occupant is a block) — needs footprint overlap added (a brick
  at a FREE sub-cell of a campfire tile places; an overlapping cell
  refuses).
- The voxel branch: RenderStructure (SpriteRenderer.cs:1508-1562) —
  SubX >= 0 → BuildBrickVoxel over the 16-px sub-cell rect; the
  footprint rect is the extension point; SubNeighbourTop wraps
  sub-cells across tiles (:1530-1537). BuildBrickVoxel takes an
  EXPLICIT rect (SpriteRenderer.cs:315-320) ✓.
- Sub-collision truth: SubHeightAt (BlockStack.cs:25-48) counts
  IsBlock structures at (SubX,SubY) or tile-wide when SubX < 0 — a
  2x2 campfire reads as 0.5 lift on exactly its four cells; the
  GroundedSlide/CanStep/CanStand machinery (slices 2, 9) needs zero
  changes.
- Migration hazard: RestoreSnapshot's mound blow-up
  (BuildingSystem.cs:398-424) triggers on `IsBlock && SubX < 0` —
  post-reform legacy campfire rows must take a footprint branch first.
- FireInstance { WorldX, WorldY, RemainingTime, MaxTime };
  GetActiveFires(); LightHearth(seconds, x, y, …) (FiremakingSkill.cs).
- Parser: Structure.cs:129-137 (GetStr/GetFloat/GetBool row) — the
  three new fields parse there with defaults.
- Pins that re-truth (data-reason changes, restaged with a genuine
  non-block fixture so their INTENT survives): VerticalLandingTests
  `Standing_NonBlockStructure_AddsNothing` (:112-119) and
  VerticalStackingTests `Brick_TileWithCampfire_KeepsLegacyRefusal`
  (:220-229) — both used the campfire as an inert prop; the campfire
  is now a solid block. StructureBillboardTests stages a campfire as
  a crossed-plane fixture (:194, :280) — restage with a crossed-plane
  def. No structure-registry count pins exist (grepped) — the new
  lined_fire_pit def shifts nothing.

## Design

### 1. Def fields (Structure.cs parser + structures.json)

`footprint` → `Footprint` (int, default 1) · `contact_damage` →
`ContactDamage` (float HP/s, default 0) · `burn_rate` → `BurnRate`
(float, default 1) · computed `BurnSeconds => LightSeconds * BurnRate`.

### 2. Placement (BuildingSystem.PlaceStructure)

Footprint branch (before the brick path): gates as ever (flat-pad,
water, biome, skill), anchor must fit (`subX + Footprint <= 4`,
same for Y — else "The footprint doesn't fit there."), every covered
cell must hold no ACTIVE structure of any kind ("That spot is
occupied."), no layer/stacking, Layer 0, tile slot set when
OccupiesTile, +10 construction XP, auto-light BurnSeconds.
Tile-level call with a Footprint>1 def auto-centers the anchor.

### 3. Occupancy partition (brick path)

A brick places on a tile whose occupants are all blocks AND whose
footprints do not cover the brick's target sub-cell. A footprint
placement refuses any covered-cell overlap with anything.

### 4. Collision — zero new code

SubHeightAt already counts IsBlock structures per sub-cell: the
campfire's 0.5 lift walls grounded movers and admits airborne hops.

### 5. Render (RenderStructure voxel branch)

Footprint rect: `[SubX·16, (SubX+Footprint)·16)` × same for Y (within
the tile). Face-neighbour tops = MAX over the span's adjacent cells
(conservative whole-face cull; campfires sit on flat pads). Full-UV
faces carry the authored 32x32 sprite at literal 1:1; the tinted
underlay follows GetSpriteBaseColor as ever.

### 6. Ghost (RenderBuildGhost)

IsBlock footprint-2 defs: the hovered cell plus its footprint
siblings render at the solid alpha; anchors that spill the tile tint
red (valid=false).

### 7. Standing damage (FiremakingSkill)

`TickContactDamage(Player? player, BuildingSystem? buildings, float
dt)`: for each ACTIVE footprint structure with ContactDamage > 0,
if a live fire sits at its position (within half a cell) and the
player's sub-cell lies inside the footprint — accumulate
ContactDamage·dt into a whole-HP bite (player.TakeDamage), notify on
the first bite per burn. Unlit or lined-pit (0 damage): nothing.
Game loop calls it beside Firemaking?.Tick(dt).

### 8. Migration (RestoreSnapshot)

`if (structureDef.Footprint > 1 && s.SubX < 0)` → restore as the
centered footprint (SubX 1, SubY 1, Layer 0, WorldX/Y at tile+32),
BEFORE the mound blow-up branch. Sites and non-footprint blocks fall
through unchanged.

## Testing plan

RED (new `CampfireFootprintTests`; parser stubs land behavior-free —
fields parse with defaults, structures.json UNCHANGED so the retruthed
fixtures stay green):

1. `CampfireDef_IsTheFootprintBlock` — pin: is_block, stack 0.5,
   footprint 2, occupies_tile, contact_damage 2, burn_rate 1.0,
   sprite structure/campfire; the authored png exists at 32x32
   (IHDR). lined_fire_pit: footprint 2, contact 0, burn_rate 1.5,
   materials stone 6 + stick 2, skill level 2, BurnSeconds 135.
2. `Campfire_PlacesAtAnchor_2x2Solid` — 5-arg placement at (1,1):
   SubX/SubY 1, WorldX = tile+32; SubHeightAt 0.5 on the four covered
   cells, 0 on the uncovered.
3. `Campfire_TileLevelCall_AutoCenters` — 4-arg call anchors (1,1).
4. `Campfire_GroundedMover_Blocked_AirborneHopAllowed` —
   GroundedSlide refuses a step crossing the footprint; CanStep
   airborne (climb 0.5 ≤ cap) passes — the player can jump on.
5. `Campfire_SpillingAnchor_Refused` — anchor (3,3) refuses with the
   footprint-fit message.
6. `Brick_FootprintCoexistence` — a brick at a free sub-cell of a
   campfire tile places; an overlapping cell refuses; after
   RemoveStructure the overlapped cell frees (live occupancy).
7. `Placement_LightsBurnSeconds` — campfire placement with a
   FiremakingSkill lights RemainingTime ≈ 90 (1.0x); lined_fire_pit
   ≈ 135 (1.5x).
8. `TickContactDamage_LitFireOnly_BitesPlayer` — lit campfire +
   player on a covered cell: HP falls after the accumulator bites,
   notification present; unlit: no damage; lined_fire_pit: no
   damage.
9. `LegacyCampfireRow_MigratesToCenteredFootprint` — snapshot row
   (campfire, SubX -1) restores as ONE structure anchored (1,1) with
   live collision — NOT 32 copies (the mound blow-up must not fire).
10. Retruthed pins: `Standing_NonBlockStructure_AddsNothing` and
    `Brick_TileWithCampfire_KeepsLegacyRefusal` restage their fixture
    as a genuine non-block (crossed-plane) def — intent preserved,
    data-reason change recorded here.

GREEN: flip the campfire def + add lined_fire_pit + authored art;
implement placement/collision/render/ghost/damage/migration; rewire
the three light call sites to BurnSeconds.

Pixel verification: same-camera pre/post — the campfire's new squat
box + 32x32 texture render at the staged tile (palette-probe the new
stone-ring colors, extent = the 2x2 footprint projected ≈ 40x45 fb
px); a control capture without the campfire for the delta; the
standing-damage path is state-asserted (no render).

Full suite 648 → 648+N green, 1 pre-existing skip.

## Open questions

None — damage rate (2 HP/s lit-only), the squat-voxel look, the
lined-pit bundle (stone 6 + stick 2 / level 2 / safe / 1.5x), and the
in-place def upgrade with centered migration were all decided by the
user 2026-10-12.
