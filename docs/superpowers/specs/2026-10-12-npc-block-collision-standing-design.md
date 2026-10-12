# NPC/monster block collision + standing truth (vertical building slice 9)

Date: 2026-10-12
Status: drafted — awaiting RED
Slice line: vertical-building post-roadmap, slice 9 (the parked
"NPC standing on block tops" follow-up, widened to the honest scope:
every grounded mover respects the brick world)

## Design law

Blocks are walls for grounded movers. The player's own on-foot rule
(slice 2: only a GROUNDED step into a taller stack is a wall; mounting
requires a jump) extends verbatim to every NPC, monster and pet: they
cannot jump, so any block ascent is a wall, descents stay free, and a
blocked axis SLIDES along the wall face (the player's per-axis law —
never a bounce, never a dead stop). What you build now stops raids and
routes workers.

## Goal

1. **Monster gate.** `CombatSystem` beeline movement (`MoveToward` —
   chase, patrol, raid parties; `MoveAway` — flee) refuses block
   ascents and slides along the wall axis-wise. User decision
   2026-10-12: beeline + axis-slide, NO pathfinding for monsters.
2. **Worker/NPC walk gate.** The walk steps in `RecruitmentSystem`
   (`MoveAlongPath` node + goal steps, `MoveGuard` final approach)
   apply the same grounded gate; pathfinding itself already refuses
   block tiles and keeps doing so.
3. **Pet gate.** Follower close-on-player and colony-guard approach in
   `TamingSystem` apply the same grounded gate.
4. **Live occupancy truth.** `BuildingSystem.RemoveStructure`
   recomputes the tile's `Structure` occupancy slot from the remaining
   structures — today a removed brick leaves its tile pathing-blocked
   forever.
5. **Standing/render truth.** The NPC, monster and pet render sites
   read the same sub-cell lift the player does (Game.cs:1936-1942): an
   entity overlapping a brick column renders ON it, not buried inside
   it, and depth-sorts with the lifted elevation.
6. **Cave phantom-wall kill.** `BlockStack`'s (world, buildings)
   queries ignore buildings on cave worlds: cave entry swaps
   `Game.World` to the cave map (CaveWorldSystem.cs:146) while the one
   `BuildingSystem` (Bootstrap.cs:204) stays surface, so pairing them
   invents phantom walls at overlapping tile coords — a latent bug the
   player already carries into caves; every new gate must not.

## Non-goals

- No NPC/monster climbing or block-top standing by choice: any block
  ascent is a wall (user decision 2026-10-12). Entities that END UP
  inside a column (walled in, staged) walk OUT freely — descents are
  free — and render lifted meanwhile.
- No monster-vs-structure damage: walls are safe for now; a blocked
  raider grinds at the wall. Structure combat is its own parked slice
  (user decision 2026-10-12).
- No monster pathfinding (user decision 2026-10-12).
- No water collision for monsters (the beeline still crosses water —
  separate gameplay question, untouched).
- No non-block structure collision: campfires and wells stay
  pass-through for everyone, player included — blocks-only is the
  game-wide standard (slice 2).
- No change to player movement math (`CanStep` semantics untouched;
  the cave guard only stops pairing surface structures with cave
  maps).

## Ground (verified in the tree 2026-10-12)

Every position-mutating movement site (7 total; grep
`.(WorldX|WorldY) +=` over src):

- `Combat/CombatSystem.cs:447-455` `MoveToward` — beeline; called by
  chase (:371) and patrol (:410). `:457-464` `MoveAway` — flee. Static,
  no world/buildings in scope.
- `CombatSystem.Tick(float dt, Player?, NPCSystem?)` (:28) — no world
  param. Game call site Game.cs:1007 passes only player + npcSystem;
  World + BuildingSystem are in scope there.
- `NPC/RecruitmentSystem.cs:1062` `MoveAlongPath(npc, world, targetX,
  targetY, dt, speed)` — path-node step (:1108-1113) and final goal
  step (:1123-1127) both move straight; world in scope, buildings not.
  `:1268` `MoveGuard` — MoveAlongPath first, then a beeline finish
  (:1273-1281).
- `NPC/TamingSystem.cs:144` `Tick(dt, pet)` + `TickFollow` step
  (:186-193); `:205` `TickColonyGuard(dt, pet, combat)` approach step
  (:235-236). No world in scope. Game call sites Game.cs:1031-1032.
- Worker pathfinding: `NPC/WorkerPathfinder.cs:15-19` `CanStand` reads
  `tile.Structure?.OccupiesTile != true` — tile-level, and ALREADY
  refuses block tiles because placement writes the slot
  (BuildingSystem.cs:131-132 bricks, :227-228 sites). `FindPath`
  already forbids diagonal corner-cuts (:51-58) and cliffs
  (:48-50). The walk step itself has NO gate — the bug.
- `BuildingSystem.RemoveStructure` (:265-268) is
  `Structures.Remove(structure)` — never clears the tile slot: a
  removed brick leaves its tile pathing-blocked forever while
  `BlockStack.BlockHeightAt` correctly says empty. Two truths diverge.
- Pure seam: `Building/BlockStack.cs` — `SubHeightAt` (:25-48),
  `BlockHeightAt` (:67-84), `StandingElevationAt` (:88-95),
  `CanStep` (:103-141: swept 8px, descents free, pure-terrain ascents
  free, block ascent = wall grounded / hop ≤ BlockClimbCap airborne).
  `SubHeightAt`/`BlockHeightAt` scan `buildings.Structures` and add
  elevations from the PAIRED world — the phantom-wall hazard.
- Render: monsters Game.cs:1982-1990, NPCs :2001-2011, pets :2026-2033
  all read raw terrain (`GetElevationAt(0.5f, 0.5f)` / water surface);
  structures do NOT render in caves (:2036 `!World.IsCave` guard) but
  nothing stops `SubHeightAt` pairing surface bricks with cave tiles
  today.
- Both block defs occupy: `mound_block` (structures.json:14) and
  `fence_block` (:29) — `fence_block` is the cheap tile-wide test wall
  (SubX -1, applies to every sub-cell of its tile).
- Test staging idiom: VerticalStackingTests.cs `Harness` — 8x8 flat
  map at `SeaLevel + 5f`, `PlaceBrick(tx,ty,subX,subY)` /
  `Place(id,tx,ty)` via `PlaceStructure`.

## Design

### 1. Cave guard at the pure roots (BlockStack)

`SubHeightAt` and `BlockHeightAt` treat buildings as absent when the
paired world is a cave: `if (buildings == null || world.IsCave)
return 0f;`. Every derived query (`StandingElevationAt`, `CanStep`,
`BrickColumnTopAt`, `ConnectionMask`) inherits. Surface behaviour is
bit-identical; cave behaviour stops inventing walls (and stops
phantom-lifting the player mid-cave).

### 2. Grounded slide (BlockStack — the single pure seam)

```csharp
/// <summary>Grounded-mover step gate (slice 9): applies whichever
/// axis components of the desired step clear CanStep(airborne: false)
/// — a blocked axis slides along the wall (the player's per-axis law),
/// descents stay free, pure-terrain ascents stay legacy-free. Returns
/// the applied (dx, dy).</summary>
public static (float Dx, float Dy) GroundedSlide(TileMap world,
    BuildingSystem? buildings, float fromX, float fromY, float dx, float dy);
```

Per-axis: the X move is gated by `CanStep(world, buildings, from,
(from.x + dx, from.y), airborne: false)`; the Y move likewise from the
ORIGINAL position. Both clear → full step; one blocked → the other
axis applies (the slide); both blocked → no move. The swept 8px
`CanStep` does the sub-cell resolution.

### 3. Movers (optional-param seams, legacy callers keep compiling)

- `CombatSystem.Tick` gains `TileMap? world = null, BuildingSystem?
  buildings = null`; `MoveToward`/`MoveAway` gain the same pair and
  run the desired step through `GroundedSlide` when `world != null`.
  Game.cs:1007 passes `World, BuildingSystem` (in caves the pure guard
  nulls buildings — cave monsters keep today's behaviour).
- `RecruitmentSystem.MoveAlongPath` gains a `BuildingSystem? buildings
  = null` param threaded from the Recruits.Tick call chain (which
  already receives buildings); node step, goal step and `MoveGuard`'s
  finish run through `GroundedSlide`. Path replans keep working: a
  brick landing on a path node fails `CanStand` at the next plan check
  and reroutes.
- `TamingSystem.Tick`/`TickColonyGuard` gain optional
  `(TileMap? world, BuildingSystem? buildings)`; `TickFollow` and the
  guard approach step run through `GroundedSlide`. Consequence
  accepted: a follower whose owner stands beyond a wall grinds at the
  wall face until the owner returns — pets are grounded movers.

### 4. Live occupancy slot (BuildingSystem.RemoveStructure)

After `Structures.Remove`, recompute the tile's `Structure` slot: any
remaining ACTIVE structure on that tile whose def `OccupiesTile` wins
(sites included — a cancelled site's tile stays reserved for a
remaining sibling), else null. One writer path, no staleness.

### 5. Render truth (Game.cs)

The three entity render sites add the player's lift idiom
(Game.cs:1936-1942): `blockLift = BlockStack.SubHeightAt(World,
BuildingSystem, tx, ty, SubCellOf(wx), SubCellOf(wy))` added to the
site's existing elevation expression (which keeps its exact
`GetElevationAt(0.5f,0.5f)`/water-surface form so the depth-sort tie
contracts stay bit-identical on bare ground). Depth-sort keys flow
through the same variable.

## Testing plan

RED (new `NpcBlockCollisionTests`, harness copied from
VerticalStackingTests; stubs compile, failures are assertion
failures):

1. **GroundedSlide contracts (pure):** flat step applies fully;
   a fence east of the mover blocks dx and keeps dy (NE → (0, dy));
   a step parallel to a wall face applies fully; a descent out of a
   brick column applies fully; a pure-terrain ascent applies;
   null buildings applies fully (legacy); a cave world with a
   surface-paired brick applies fully (phantom kill).
2. **Cave guard:** `SubHeightAt`/`BlockHeightAt` on a cave-paired
   world with real bricks in the buildings list → 0.
3. **Live occupancy:** `CanStand` false on a brick tile (pins
   existing truth); after `RemoveStructure` of the tile's only brick
   → true (RED: stale slot); with a second brick remaining → still
   false.
4. **Monster gate:** monster chasing the player across a fence wall:
   X distance frozen, Y slides (both asserted); no-wall control
   closes; `Tick` without world/buildings (legacy signature) still
   walks through (compat pin); patrol covered by the same
   `MoveToward`.
5. **Live-truth pathing:** `FindPath` routes THROUGH a tile whose
   bricks were removed (the freed corridor) — at RED the stale slot
   forces the detour. The per-step walk gate in `MoveAlongPath`/
   `MoveGuard` is an invariant pinned indirectly by the GroundedSlide
   contracts: tile-level path truth plus the existing replan check
   already keep plans off block tiles, so no reachable step-level RED
   exists — the gate lands in GREEN as defense against mid-tick
   placement races and spawned-inside drift.
6. **Pet gate:** `TickFollow` with a fence between pet and
   `PlayerX/Y` slides instead of phasing.
7. Slice 1-8 contracts (VerticalStackingTests, BlockVoxelTests) stay
   GREEN at RED time.

GREEN: implement 1-5. Full suite 621 → 621+N green, 1 pre-existing
skip.

Pixel verification (same-camera causal A/B, slice-8 recipe): stage a
monster INSIDE a brick region (new `DSR_TEST_MONSTER=id:tx:ty` staging
hook in InjectTestHooks if no monster hook exists) — post-fix capture
must show the sprite centroid above the brick-top projection where
the pre-fix capture buries it; flat-ground no-blocks capture
bit-identical (render change is a no-op without blocks).

## Open questions

None — all four gameplay decisions answered 2026-10-12: any block = a
wall for every grounded mover (no NPC climbing), blocked movers
axis-slide, monster-vs-wall damage is parked for a later slice, and
monsters stay beeline (no pathfinding).
