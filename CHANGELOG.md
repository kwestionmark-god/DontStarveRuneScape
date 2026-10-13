# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **Vertical building, slice 12 — structure footprints + the campfire
  family** — the first multi-sub-cell structure. The campfire is now a
  2x2 footprint block (0.5 units squat) on the 4x4 brick grid: it walls
  grounded movers, you can hop onto it, and standing on a LIT one
  bites 2 HP/s (with a first-bite notification — no silent losses).
  New Stone-Lined Fire Pit variant: stone 6 + stick 2, construction 2 —
  safe to stand on (zero contact damage) and burns 1.5x longer
  (135 s vs 90 s). New def fields drive the variants framework:
  `footprint`, `contact_damage`, `burn_rate` (→ `BurnSeconds` feeds
  every fire-lighting call site). Bricks share a footprint tile's
  FREE cells; build ghost highlights the whole anchor extent; legacy
  tile-level campfires auto-migrate to the centered footprint on load.

### Changed
- **Vertical building, slice 11 — monsters break walls** — walls buy
  time, not invincibility. A monster blocked by a player-built wall
  while chasing (or fleeing) grinds at it, and after 4 seconds of
  sustained blocking it starts attacking the nearest wall piece in
  melee reach — its own attack stat at its own attack cadence (a wolf
  needs ~10 s per Dirt Brick; a raid party much less). A destroyed
  piece frees its tile for pathing immediately, notifies you ("Your
  Dirt Brick was destroyed!"), and the chase resumes through the gap.
  Wandering monsters never attack walls — only target-driven ones.
  Bricks (hp 20) outlast fences (hp 10); thick walls buy more time.
- **Vertical building, slice 10 — structure sort honesty** — player
  builds no longer sink into the ground while orbiting the camera.
  Structures (and ground fires) now follow the same painter's-order
  law the player and monsters always had: they key at their tile's
  highest corner plus the standing boost, so the owning terrain quad
  can never paint over a build that stands on it. Before, a brick on
  the far side of its tile keyed below its own tile at the yaws where
  the sub-cell offset lands against the view axis — it buried
  completely and came back as the orbit continued (fires keyed at
  elevation 0, below every land tile). Genuine occlusion is untouched:
  a raised tile genuinely between the camera and a build still hides
  it.
- **Vertical building, slice 9 — NPC/monster block collision +
  standing truth** — blocks are walls for every grounded mover now:
  monsters (chase, patrol, flee, raids), worker walk steps and pets
  (follower, colony guard) refuse block ascents and slide along the
  wall axis-wise instead of phasing through (the player's own
  per-axis law; no NPC climbing — mounting is a jump, and grounded
  movers can't jump). A brick column a raider can't pass is actual
  defense now. Occupancy goes live: removing a structure recomputes
  its tile's occupancy slot, so a removed brick frees the tile for
  pathing instead of blocking it forever. Cave worlds no longer pair
  with the surface building list — that pairing invented phantom
  walls at overlapping tile coords (the player carried this latent
  bug into caves; fixed with the same guard). Monsters, NPCs and
  pets overlapping a brick column render standing ON it (the
  player's lift idiom) instead of buried inside it.
- **Vertical building, slice 8 — brick face cover** — Dirt Bricks carry
  their own authored 16x16 sprite (seeded brick courses with dark
  mortar, dirt palette, every pixel painted) instead of a
  mostly-transparent wall glyph with a wide empty margin, so a brick
  face reads as masonry at 1:1. Bricks also keep their faces next to
  sloped ground: only a neighbouring brick column occludes a face,
  never raw terrain elevation, so a brick beside a rise no longer
  loses its side to the hill (it may poke through the slope slightly
  instead).
- **Vertical building, slice 7 — brick-by-brick building grid** — the
  building grid granulates to 4x4 sub-cells of 16 px per tile. The
  Dirt Brick (formerly Dirt Mound, id `mound_block` kept) is a fixed
  16 px unit placed at a hovered sub-cell: the sprite maps 1:1 onto
  every face (no stretching ever), joined bricks butt and cull their
  shared faces, and build mode presents the hovered tile's 4x4 grid
  with the hovered cell solidly highlighted. Bricks stack into
  per-sub-cell columns up to 4 high (a 64 px wall); standing, stepping
  and jump-landing resolve at the 16 px sub-cell (one brick is a hop,
  two are a wall); placement consumes a crafted dirt_brick item
  (2 stone + 1 stick at the bench — net cost unchanged) and build mode
  stays open for brick-laying. Legacy mound saves migrate to full
  brick blocks. Fences stay tile-level.
- **Vertical building, slice 6 — sprite-proportioned block walls** — a
  block's drawn footprint is now a slender slab (32 px thick, sprite
  scale) centered in its tile instead of a full 64×64 chunk, extending
  to the tile edge wherever a connected neighbour exists, so joined
  blocks butt into one continuous natural-looking wall. Isolated blocks
  render as compact 32×32 slabs. Movement/stacking rules unchanged —
  blocks still own their full tile; this is purely visual.

### Added
- **Vertical building, slice 5 — true voxel blocks with stitching** — a
  block no longer renders as two crossed billboard planes: it is a
  world-space box (top + camera-facing side faces projected through
  `WorldToScreen` from the tile's real corners and column base/top
  elevations) with the sprite mapped as surface area over a solid fill
  tinted the sprite's average opaque colour. Neighbour-shared faces
  cull the covered span, so joined blocks stitch into one continuous
  volume (two mounds = a seamless 2×1 box) and height steps expose
  exactly the uncovered strip. Fences keep crossed planes + rails.

### Added
- **Vertical building, slice 4 — grounded world-plane structures** —
  structures no longer draw as flat quads center-anchored to the camera's
  yaw (which made them float and billboard): every sprite structure now
  renders on the same world-anchored crossed planes as resources and
  characters — base edge planted on the ground line, fixed world facing,
  planes' extents visibly flipping as the camera orbits. Blocks stand at
  their cumulative column base (new `BlockStack.RenderBase` seam; a
  stacked column reads as one contiguous stack, `stack_height` × 12.8
  world px tall) and fences face along their connection axis, drawing
  plank-brown rails toward each connected neighbor (Minecraft-fashion,
  from the slice-3 `ConnectionMask`). woven_shelter keeps its hand-tuned
  procedural look but plants on the ground; construction sites keep the
  blueprint tint and the screen-space progress bar. New smoketest hook
  `DSR_TEST_STRUCTURE=id:tx:ty` for staged render captures.

### Added
- **Vertical building, slice 3 — stacking columns + fence connections** —
  blocks now STACK: targeting a tile holding only your own block structures
  piles the next block on top (non-block occupancy refusal is bit-identical),
  up to the 4-unit column budget (`Constants.BlockColumnMaxHeight`) — two
  dirt mounds make the game's first true unclimbable wall. New entry-tier
  `fence_block` (Wooden Fence, 2 sticks, 1 stack unit) is a deliberate
  half-hop: it walls off walking but a jump clears it. Fences auto-connect
  to neighboring blocks via the pure `BlockStack.ConnectionMask` N/E/S/W
  seam (construction sites aren't solid; the renderer consumes the mask in
  slice 4). Stacked blocks render at their cumulative top — column order is
  placement order — and columns survive save/load on the existing structure
  snapshot. Over-budget placement refuses honestly ("This block column is
  already at full height.") and consumes nothing.

### Added
- **Vertical building, slice 2 — blocks are walls, jumps land on tops** —
  walking into a block taller than your footing now REFUSES the step
  (axis-slide, no bounce); a jump hop of up to 2.05 elevation levels
  (`Constants.BlockClimbCap`) lands ON the block's top, and stepping or
  jumping down from any edge is always free. Standing is a pure query —
  `BlockStack.BlockHeightAt` sums the stack heights of BUILT blocks on a
  tile (construction sites aren't solid), `StandingElevationAt` adds it
  to the terrain — so the player sprite, the boot sampler, and the
  movement gate all read one truth. Blocks render lifted to their top
  (a block you can't see is unteachable) and pure-terrain slopes keep
  legacy free movement (worldgen hills unchanged). mound_block becomes
  2 levels = exactly one hop (an 8-level mound was ≈102 px vs the 26 px
  jump peak — unclimbable forever). Suite 553 → 564.
- **Vertical building, slice 1 — blocks, flat pads, and the shovel** —
  structures gain vertical extent data: `StructureDef` carries
  `is_block` + `stack_height` (the first def is entry-tier `mound_block`)
  and block placement is gated on a FLAT pad — the tile's four
  corner-stitch elevations must spread ≤ 0.01 levels, with an honest
  "level this tile with a shovel" refusal; old defs keep byte-identical
  gates on slopes (no retroactive refusals). The remedy ships beside
  the gate: a craftable tier-1 `shovel` and the `ActionType.Flattening`
  verb (V key) — a 3 s timed action that levels the tile to the median
  of its corner-source neighbors (the level agreeing neighbors already
  hold, where a mean lands between levels and stays sloped), pays
  construction XP, re-stitches the corner quads, and refuses — always
  with a notification — on water, occupied/resource tiles, and targets
  that would sink below the sea. Terrain edits persist:
  `WorldSnapshot.TerrainEdits` re-applies after the seeded world rebuild,
  same convention as depleted nodes. Jump landing and stacking are
  slices 2–4 (spec banked). Suite 540 → 553.
- **Unlock-as-event trigger + quest-unlock honesty** — a quest with
  `recipe_unlocks` now teaches its recipes the tick its objectives
  complete (the story beat — learn from the hermit when you REACH
  him), posting a gold "Recipe learned: <id>" line only for newly
  learned, actually gated ids; claim keeps its now-idempotent writes
  as the backstop and knowledge is permanent. The quest-unlock gate
  was dead in practice: all three enforcement sites (CraftingSystem,
  crafting panel, fire menu) keyed on the quest id while the only
  writer wrote recipe ids — the sets could never intersect. Now
  recipe-id keyed end-to-end, gem_mining's dead `craft_bone_powder`
  unlock repointed at the real `grind_bone`, completed quests
  round-trip through the save (no more post-reload re-offer / XP
  re-claim), and the unlock sets persist. Suite 531 → 540.
- **Coastal/swamp side quests — the two biome lines live** — Maris
  (`merchant_coastal_1`) becomes the first merchant quest giver: her
  chain walks the coastal loop driftwood_scour → salt_run → crab_cove
  (tide-washed driftwood → salt deposits → crabs — non-hostile, so the
  hunt is your aggression — plus a crab-meat delivery and a trade with
  her). New swamp merchant Fenwick the Bog Trader
  (`merchant_swamp_1`) carries reed_cut → peat_dig → croc_bog and
  gives swamp its first shop (the five swamp trade entries were dead
  data — no merchant claimed the biome). quests.json 23 → 29,
  npcs.json 18 → 19. Suite 517 → 531.
- **Merchant shop honesty, two root fixes found by grounding** —
  merchant trade stock was a per-seed lottery: `npc.Biome` came from
  the def's world tile and every new game randomizes the seed (on seed
  42 the forest merchant stood on coastal and sold coastal stock).
  All five merchant defs now carry an authored `biome` key that every
  Biome writer prefers over the tile (boot, save-restore, and daily
  visitor spawns). And the daily 10:00 depart sweep removed ALL
  non-recruited merchants — stationary shopkeepers like Maris vanished
  mid-session on day one; it now removes only the visitors the visit
  system itself spawned.
- **Hub-tab return** — Esc (or Q) from a role panel the NPC hub
  launched now returns to that NPC's role menu with the session and
  active tab restored, instead of dropping the player into the world
  mid-conversation. Mirrors the dashboard-return idiom: the Game
  holds the hub npc + tab across the panel handoff, and any world
  transition (Playing/Paused/Settings) clears it. Panels opened
  directly keep their close-to-Playing behavior. Suite 513 → 517.
- **Fire-menu smoketest — the fire-venue debt closed** — FireMenuPanel
  had never been rendered outside unit tests. New
  `DSR_TEST_FIREMENU=1` staging hook (the fifth smoketest env hook):
  stands the player on the first clear dry tile — no cave collar in
  its 3x3, no NPC within the 128px interact band, no live monster
  within the 96px taming band (`Game.FindFireMenuStageTile`, pinned
  by 6 new tests) — and grants 2 oak_logs, so a scripted `F,E`
  lights a real ground fire and opens the real menu through the real
  input router. Pixel-verified: chrome border bbox within 1px of the
  geometry predicted from the panel constants, plate fill 33x
  baseline, panel rect mean-diff 97.8. Suite 507 → 513.
- **Firemaking narrowing — the fire venue** — every fire method now
  feeds its owning skill, and cooking has its own home. A new fire
  menu (E near a lit fire, cooking station, furnace or smelter) holds
  every recipe that needs fire — cooking, burn-to-ash, charcoal,
  rendering, refining — plus rest-until-dawn; the crafting panel drops
  those recipes. The partition is single-sourced
  (`FireMenuPanel.IsFireRecipe`) so menu, panel and tests can't
  disagree. Hearths now actually burn: placing a campfire or the new
  firepit auto-lights it (worker-built sites light on completion), and
  F relights a burned-out hearth from carried fuel, paying firemaking
  XP. The new firepit structure (construction 3, stone 6 + stick 2)
  burns 240s vs the campfire's 90s — masonry buys you a longer fire.
  Fuel identity is real at last: Skills/Firemaking/data/fuel_table.json
  now drives burn duration (sticks 15s, logs 30s, charcoal 45s per
  unit, summed; unknown fuels keep the flat legacy rate), with the
  duration sub-stat stacking on top.
- **Skill XP root fix** — `SkillManager.AddXp` now recomputes the
  level and grants stat points, exactly like the notification variant
  (which is now a thin alias). Raw-XP paths — the F-fire, hearth
  placement, worker construction and mining — no longer accrue XP
  into a frozen level; every XP writer levels up. This closes the
  parked "frozen mid-session levels" bug at the root.
- **Station gates are proximity-honest** — a built station (furnace,
  smelter, cooking station) now only enables its crafts while you
  stand near it; owning one anywhere no longer unlocks its recipes
  globally. The fire-menu rest row shares the sleep gate, refusing
  visibly during the day.
- **NPC-quest breadth — the plains hub and two leader boards** —
  quests.json 18 → 23, npcs.json 17 → 18. A new plains quest giver,
  Hayward the Reaper (forest_villagers — their territory already
  includes plains), carries a three-quest chain: Plains Harvest (gather
  6 wild wheat), Boar Hunt (kill 2 boars), Plains Provender (deliver 2
  boar meat to Joss and trade at his camp — which finally gives the
  plains merchant a reason to exist). Elder Mara and the Stone Guardian
  each gain their first quest (Herbal Remedy, behind Timber Collection;
  The Guardian's Request, behind Mountain Pass), so the two quest-less
  faction leaders now have boards. All five are pure side quests — XP,
  items and gold only, no recipe or gear gating. Two new suite laws pin
  the slice's invariants: every quest is offered by some NPC, and every
  quest's skill XP names a skill the game actually registers.
- **Multi-role NPC menu (the leader E-fork fix)** — an NPC offering more
  than one interaction now opens a role menu with tabs (the dashboard's
  own idiom) instead of being hard-routed into a single panel: E on a
  faction leader with quests shows Quests AND Diplomacy, so the
  negotiation their own quest line demands is reachable by keyboard
  again. Single-role NPCs — merchants, recruits, plain quest givers,
  quest-less leaders — open their panel directly, exactly as before.
  The tabs come from what the NPC actually offers
  (`NpcHubPanel.RolesFor`): one source for the menu, the E routing and
  the tests.
- **Cross-skill audit — progression reachability test** — a
  data-driven suite guard (`ProgressionReachabilityTests`) that walks
  every quest's collect/deliver targets against the full item-source
  graph (any resource node yield, any monster loot, merchant stock
  within early commerce reach, starter packs, and the recipe closure
  over those) and walks the quest prerequisite DAG to a fixed point —
  a quest asking for an item nothing produces, or a quest chain with a
  dead-end, now fails the suite with a named chain instead of being
  discovered by playtest.
- **Tap Sap recipe** — tree_sap is craftable at crafting level 1 from
  one oak_log (`whittle_sap`), so First Flame (the tutorial quest) is
  completable by any background: axe-less starts buy logs at the
  forest merchant (commerce 0) or chop them as the forester. The
  maple tap (crafting 5) remains the bulk sap source.
- **herb_gathering quest is offered** — the orphaned quest (defined
  since Phase 3, listed by no NPC, so its 40 intelligence XP and
  rewards were unreachable) now sits on Old Man Hemlock's board after
  Timber Collection, matching its prerequisite chain.

### Changed
- **"Build Campfire" craft recipe removed** — it was dominated on every
  axis: 3 oak_logs + 5 stone (vs the building panel's 2 stick +
  2 stone), trained crafting instead of firemaking or construction,
  and produced an item nothing in the game reads (`is_structure:`
  true has no consumer — placement comes from the building panel).
  The campfire items.json def stays so old saves keep loading; the
  recipe's deletion resolves the parked inert-item finding.
- **Worker gather XP trains the right skill** — `GatherSkillFor` now
  mirrors the player's tool switch exactly: workers chopping the eight
  axe-tree species train woodcutting (they silently trained MINING —
  the any-tool-node→mining mapping predates per-recruit skills, and
  the woodcutting branch it was supposed to fall through to was dead
  code matching no real yield id), bucket-water hauling trains
  foraging (matching the player's water gather), pickaxe nodes keep
  training mining, rod nodes keep training fishing. The colony
  node-level gate calls the same function, so a maple_tree's level-20
  gate now reads the worker's woodcutting — the skill the tree itself
  trains — instead of testing mining and waving everything through.
- **Planks are craftable from day 1** — the `quest_unlock:
  goblin_diplomacy` flag is removed from the planks recipe and from
  the quest's reward list (goblin_diplomacy keeps its gear unlocks,
  trophy, gold, and XP). The fishing rod and wooden bucket chains no
  longer inherit the quest's commerce-3/persuasion-5/intelligence-5
  acceptance gates through their shared planks input.
- **`quest_unlock` is now enforced** — the flag used to be
  display-only: the crafting panel rendered "Quest: <id>" on the
  recipe while `CraftingSystem.Craft` and the panel's craftability
  check ignored it entirely, and `Player.UnlockedRecipes` was
  write-only state. Both the system and the panel now refuse a
  quest-locked recipe until the owning quest's claim has added it to
  the player's unlock set ("Requires quest unlock: <id>."); worker
  colony auto-production passes no unlock set and stays ungated
  (colony stock-visibility convention). What the panel says and what
  the craft system does are the same fact.

### Fixed
- **Quests paid XP into a skill that doesn't exist** — goblin_diplomacy,
  coastal_defense and mountain_pass awarded 50/80/40 skill XP to
  `"combat"`, which is not a registered skill (the game registers
  `attack`), so 170 XP silently vanished at claim. Retargeted to
  `attack`; `EveryQuestSkillReward_TargetsARegisteredSkill` now fails
  the suite on any future ghost skill.
- **A panel opened from another panel lost its data** — `SetState`
  closes every open panel on a panel-to-panel move (`CloseAllPanels`),
  so the role menu's tab handoff wiped the target panel's freshly-set
  state. State transitions now happen first and the target panel is
  populated after.

### Added (fishing arc — 2026-10-08/09, consolidated)
- **Fishing rare-drop table — pearl + old boot** — every successful
  catch now rolls a second, level-scaled table: a pearl (1% +
  0.15%/fishing level — the payday: coastal merchants pay 25 gold,
  sell-only stock-0 entry) or the old boot (4% + 0.2%/level — vendor
  junk with no market, the OSRS gag). The roll uses the fishing level
  frozen at cast start. The catch message names the dredge ("You
  dredged up a pearl!") and the grant posts the gold rare-find
  notification; a full inventory loses the rare visibly ("No room —
  the pearl slipped back into the water!") — never a silent vanish.
  Worker fishers roll the same table on their own fishing level (rod
  nodes only) and their rares land in the colony stockpile ledger.
- **Fishing rod tiers — bone rod** — the cast window is the tier lever:
  Carve Bone Fishing Rod (wolf_bone×2 + grass_rope×1, crafting 4) casts
  in 2s where the carved plank rod takes 3s ("You cast your bone
  rod..."). Tier buys speed, never yield — yield stays the bait
  economy's knob. The player path recognizes the bone rod free via the
  tool-suffix match; the colony-store tool check gained the `bone_`
  arm so a store-side bone rod satisfies the fishing gate (extends the
  stone_axe convention).
- **Fishing bait economy** — fishing is now a prepared activity: Mix
  Fishing Bait (shells×2 + fibers×1, crafting level 1, ×4 per batch)
  turns coastal trash into catch-doubling consumables. Baited player
  casts land ×2 catches ("Baited hook..." → "Harvested 2 raw_fish
  (bait)."); one bait is consumed at cast start — walk-away cancels get
  no refund, the bait sank with the cast. Colony fishers burn one
  bait from the store per harvest for a doubled haul (meals/materials
  convention); unbaited fishing stays the silent ×1 default.
- **Fishing cast window + animated visuals** — fishing is now the one
  timed gather: a 3-second cast holds the catch, walking away cancels it
  ("You moved — the fish got away."). Fish-spot decals breathe and carry
  expanding ripple rings (deterministic per-tile phases); while casting
  the player draws a rod, line, and bobbing bobber at the waterline —
  all primitive-rendered, no new art. New DSR_TEST_FISHING=1 headless
  smoketest hook, and a fixed harness bug: smoketest runs now apply the
  settings override (windowed, no vsync) BEFORE the Game ctor, so the
  framebuffer present no longer deadlocks on a locked desktop session.
- **Fishing skill + rod gate** — fish are no longer free: `fish_spot` now
  requires a craftable `fishing_rod` (planks×2 + grass_rope×1, crafting
  level 2, elder-wood-staff sprite reused). Catches train a real `fishing`
  skill — new `ActionType.Fishing` routes XP correctly for the player
  (previously leaked into foraging), and `GatherSkillFor` gained a fishing
  branch so rod-gated nodes train workers' fishing (previously mining).
  `fish_spot.required_level` dropped 5 → 1 (the only fishing XP source
  can't be stranded behind its own gate). Workers fish when the rod is in
  stock (bucket convention); skills panel gained a fishing row.
- **Dashboard hub (O)** — live player/survival, location, inventory, quest,
  crafting, and faction summaries; choose destinations by mouse or keyboard,
  then return from dashboard-launched panels with O or the on-screen button.
  Quest and faction views expose a global journal and faction overview while
  NPC-scoped interactions remain unchanged.
- **Skills panel (Tab)** — first interactive UI panel: OSRS-style grid with one
  row per skill (glyph, name, level, XP bar) and a detail block with clickable
  `[+]` buttons for per-skill stat-point spending. Establishes the shared panel
  chrome (`PanelChrome`) and reusable mouse hit-testing (`UiInput`) that the
  inventory/crafting/building/gear panels will reuse; panels update per frame
  while open and draw over the live world.
- Headless UI test hooks: `DSR_TEST_KEYS`, `DSR_TEST_XP`, `DSR_TEST_CLICK`
  script panel flows through the `--smoketest` capture.
- xunit test project pinning the OSRS XP table and stat-point spending.
- **Inventory panel (C/I)** — 20-slot grid (4x5) with a detail block for the
  selected slot: real item sprites via `SpriteRenderer.GetSpriteTexture`
  (glyph-chip fallback), quantity vs stack size, equipped marker, food stats,
  and a spoilage bar; click and arrow-key selection. `ItemCatalog` parses item
  display info (name/sprite key/food stats) from items.json with
  JsonElement-safe parsing; new `DSR_TEST_ITEMS="oak_logs:10"` smoketest hook.
- **Crafting panel (H)** — recipe list (82 recipes from all five recipe files,
  sorted by tier/name, scrolled) with a detail block: output sprite, 
  ingredients with have/need counts, skill gate, XP, campfire/food/quest
  flags, and a clickable CRAFT button with a status line. `CraftingSystem.Craft`
  now really crafts: skill gate, ingredient gate, all-or-nothing consume/
  produce, XP grant. `RecipeRegistry` parses the actual recipes.json shape
  (recipe_id / input_items pairs / output_item) that nothing loaded before —
  the old RecipeDef model was written for a different shape and was unused.
- **Gear panel (G)** — nine equipment slots (Weapon/Head/Chest/Legs/Boots/
  Gloves/Cape/Ammo/Shield) with the equipped item, damage/attack totals, and
  the inventory's equippable items; click an equipped slot to unequip, click
  an equippable row (or Up/Down + click) to equip/unequip — keeping the
  inventory slot flag and PlayerGear in sync. `GearItem.LoadAll` now really
  loads Data/gear.json (weapons/armor/tools sections, keyed envelope,
  JsonElement-safe; equip slot derived from the item id, which the data
  lacks; cached; tools get a Durability field). New `Inventory.UnequipItem`.
- **Building panel (B) + placement mode** — 25 structures from structures.json
  (doubly nested envelope, JsonElement-safe; the old StructureDef model was
  written for a different shape and was unused): list sorted by sub-stat/name,
  materials with have/need counts, construction skill gate, hp/type/burnable
  flags, biome list, and a BUILD button that enters placement mode — the panel
  closes, a ghost preview follows the hovered tile (gold where valid, red
  where not), and the next world click places; Esc/Q cancels.
  `BuildingSystem.PlaceStructure` really builds: biome/skill/material gates,
  all-or-nothing consume, tile occupancy. `RestoreSnapshot` now looks the def
  up in the registry — the raw "id"/"max_hp" lookups never matched the data,
  so save/restore of placed structures was silently broken. Structures render
  with their real sprite at the tile's elevation (was a gray elevation-0
  quad). `DSR_TEST_CLICK` accepts multiple clicks ("x1,y1;x2,y2") applied one
  per frame.
- **Water gathering — bucket mechanics** — water is no longer free.
  `water_source` nodes require a bucket: a new craftable equippable tool
  (Carve Wooden Bucket — planks×2 + grass_rope×1, crafting level 2,
  durability 100, sprite reused from berry_jar). The player-side gate
  rides the existing tool pipeline ("You need a bucket." refusal, exact/
  suffix tool match); colony workers harvest tool-gated nodes on the
  surface when the tool sits in the player's inventory or the colony
  store — the same stock check caves already used — instead of the old
  surface blanket-ban. Workers without the tool skip the node silently
  (visible in stock). 7-test slice; surface pickaxe dispatch guarded by
  a regression test.

### Fixed
- **Notifications never rendered**: `ActionSystem.AddNotification` queued into
  `_pendingNotifications`, and `FlushNotifications` — the only bridge to the
  display queue the HUD reads — had zero callers; the HUD's render also drew
  only the bars. The flush now runs at the end of each frame (same-frame
  messages render immediately) and the HUD draws notifications under the bars,
  fading over their last second.

### Fixed
- **Stack sizes ignored item data**: `Inventory.GetStackSize` used hardcoded
  defaults (logs 20) while items.json says 28; stack sizes now come from
  `Inventory.StackSizes` (built from items.json at world load) with the old
  defaults as fallback for unknown ids. `AddItem`/`CanAdd` stack to the data
  value (e.g. oak_logs 28, torch 5).

### Fixed
- **Panel states were invisible**: every panel except skills (Tab) was an empty
  shell class — `Render(...) { }` drew nothing, so pressing C/I/H/B/G (or NPC
  dialogue opening trade/quest/recruit/diplomacy) switched state, stopped
  movement, and left a frozen-looking world with no UI. All shells now draw a
  shared "not yet implemented" plate (`PanelChrome.DrawPlaceholder`) in the
  title-screen palette, with the usual Esc/Q to close.

### Fixed
- **OSRS XP curve was shifted one level**: `CalculateLevelFromXp` summed
  thresholds over `1..level` instead of `1..level-1` (level 2 needed 174 xp
  instead of 83; level 99 needed 14,391,160 instead of 13,034,431).

### Changed
- **Sea rendering is now one continuous water layer**: a single flat mesh at
  SeaLevel spans the visible map each frame (painter class between submerged
  beds and dry land), per-vertex tinted and faded by the smoothed bed depth —
  replacing all per-tile water quads, so tile-grid seams, waffling, and
  yaw-dependent artifacts are gone. Land that dips below the plane is clipped
  against it (marching squares), so waterline contours come from one terrain
  field at every yaw/pitch, and terrain dips fill with water as natural
  inlets. Foam crests ride the same contour. Perf ~8.3ms at a busy shore.

### Changed
- **Sea level raised to a permanent 11.5** (was 6.5): the old flood system was
  manufacturing ~25% water coverage from a heightmap that almost never dips
  below 6.5; at 11.5 about a quarter of the world is genuinely below the sea
  plane (deepest beds ~7 levels down). Spawn search now requires dry ground
  above the sea.
- **Land is never clipped — water is a contact layer**: dry tiles always draw
  whole (fixing the missing wedges at shorelines); where a tile's terrain dips
  below the sea plane, the sheet is painted over the dipped region via sub-tile
  marching squares (DrawSeaSheetPatch) with world-anchored UVs and
  depth-ramped tint and opacity, so water visibly meets land along the true
  contour. Perf at a shoreline spot: 5.39ms avg / 6.49ms p95.
- **Water is now a physical sea-level layer, not a tile class**: worldgen no
  longer converts low tiles to a water biome; any terrain at or below the
  constant `SeaLevel` simply intersects the flat sea plane. The renderer draws
  each submerged tile as bed terrain plus a flat sheet quad at exactly
  SeaLevel whose per-corner tint and opacity ramp with depth (glassy shallows,
  opaque deeps), with world-anchored scrolling flow texture so the ocean reads
  as one seamless body. Dry land tiles straddling the plane are clipped
  against it with sub-tile marching squares, so the visible waterline is the
  true terrain/plane contour from every camera yaw — no tile-stepped shores.
  Corner elevations are floats now (was int-terraced), so slopes are smooth
  ramps and contours curve. Removed the priority-flood pool system,
  per-tile pool quads, drop faces, and the banded lapping-blanket overlays.
- Sea flow texture is Repeat-wrapped and its contrast is flattened toward the
  mean at load, replacing the harsh white speckle with a gentle current.

### Fixed
- **Flattened world / mountain-only biomes**: Perlin2D simplex attenuation used
  t^2 instead of t^4, pushing noise to ±6; combined with min-max normalization
  and a steep sigmoid, 60% of tiles pinned to elevation max. Kernel fixed,
  ridged noise switched to the Python `1 - 2|n|` form, and elevation/moisture
  shaping ported from the Python generator.
- **Resources only spawning on water**: ResourcePlacer rejected tiles outside
  biome `elevation_range` (legacy 0-7 scale vs 0-31 elevation). The gate was
  removed and the placer rewritten to the Python per-tile model with
  rarity-clamped densities and a 40% occupancy cap.
- **Bottom half of screen unpainted**: tile culling did not account for
  elevation displacement; the view rect is now padded by the max terrain
  displacement in tiles.
- **Painter order breaking under camera yaw**: tiles drew row-major and sprite
  depth keys mixed tile-index units with world-pixel units. Tiles are now
  depth-sorted per frame and all sprite depth keys use world-pixel coordinates.
- **Resource sprites sliding across tiles when panning the camera**: resources
  were screen-center anchored; they are now bottom-anchored billboards at the
  tile center, matching the player sprite.
- **Sprites floating above low-elevation ground**: render/depth anchoring used
  `tile.Elevation` instead of the bilinear corner height at the actual position.
- **Camera centering drifted with pitch/zoom**: the camera anchored the
  elevation-0 plane. The focus now anchors to the player's bilinear ground
  elevation, and the vertical anchor is pitch-adaptive (lower at shallow pitch,
  centered near top-down).

### Added
- TextRenderer with bundled Liberation Mono fonts; title screen renders text.
- `--smoketest <path.png>` CLI flag: boots past the menus, generates a world,
  renders a few frames, saves the framebuffer PNG, and exits.
- `DSR_CAM_PITCH`/`DSR_CAM_YAW`/`DSR_CAM_ZOOM` env vars override the camera for
  headless captures.
- Player sprite animation (idle/walk frame sets) with bottom-center anchoring;
  small pixel-art sprites are pre-upscaled with nearest-neighbor sampling.

### Changed
- Domain-warp amplitude reduced from 12 to 4 tiles to stop biome smearing.
- Camera pitch range widened to 10°–80°.
- The biome classifier is a direct port of the Python six-band
  elevation/moisture tree.
- Terrain textures receive the slope/elevation shading as a grayscale tint.

## [0.1.1] - 2026-09-10

### Fixed
- **Compilation Errors**: Fixed all remaining build errors (~20) across core systems
  - SurvivalSystem: Added setter to MaxHunger property for snapshot restoration
  - Program.cs: Resolved ambiguous Version/Window references with alias imports
  - WorldGen.cs: Fixed float-to-int conversions for corner elevations
  - NPCFlows.cs: Fixed duplicate variable `qty`, unassigned variable, and static `Game.ErrorColor` access
  - Bootstrap.cs: Replaced `JsonElement.Deserialize` with `JsonSerializer.Deserialize`; made `BiomeRegistry.DefaultBiome` setter public
  - Game.cs: Fixed tuple conversion errors in render drawables with named tuple syntax; added `HandleEvent` methods to TitleScreen/CharacterSelectPanel/LoadingScreen; removed unused InputRouter.Handle call with SDL Event
  - TileMap.cs: Fixed `RegrowTimer` → `RegrowTime` references; replaced read-only `IsDepleted` assignments with `Density = 0f` pattern
  - Program.cs: Removed non-existent `Initialize()` calls from renderers and UI panels

### Changed
- Simplified Program.cs Load event handler - renderers/panels now initialize on first use

## [0.1.0] - 2026-09-09

### Added
- **Project Setup**: C# .NET 8 project with Silk.NET (SDL2, OpenGL, Maths, Windowing), System.Text.Json, MathNet.Numerics
- **Configuration System**: Constants.cs and Keybindings.cs ported from Python config
- **Core Game Architecture**:
  - GameState.cs - Discrete state machine (Title, Loading, Playing, Panel states)
  - Player.cs - Player entity with stats, inventory, skills, position
  - SaveSystem.cs - JSON-based save/load with slot support
  - Game.cs - Main game loop aggregator
  - Bootstrap.cs - Data loading and initialization sequence
- **Data System**:
  - DataLoader.cs - Instance-based JSON data loading (refactored from static utility)
  - Data classes: Biome, Item, Resource, Monster, Gear, Structure, NPC, Quest, Faction, Trade, Recipe
  - All JSON data files: biomes, items, resources, monsters, gear, structures, NPCs, quests, factions, trade items, recipes
  - Skill-specific data: construction recipes, cooking recipes, firemaking fuel table, intelligence enchantments/recipes, metallurgy recipes
- **World Generation**:
  - Noise.cs - Perlin noise implementation using MathNet.Numerics
  - Tile.cs - Tile data structure with biome, elevation, resources
  - TileMap.cs - 2D tile grid with spatial queries
  - WorldGen.cs - Procedural world generation with biome classification and resource placement
- **Camera System**: Camera.cs - Orbital camera with yaw, pitch, zoom, pan controls
- **Input System**:
  - InputState.cs - Keyboard, mouse, gamepad state tracking
  - InputManager.cs - SDL2 input polling and event handling
  - InputRouter.cs - Context-aware input routing (game, UI, panels)
- **Survival System**:
  - SurvivalSystem.cs - Hunger, health, stamina with seasonal/weather modifiers
  - FoodItem.cs / FoodRegistry.cs - Food data with nutrition, spoilage
  - StarterPack.cs - Starting equipment and supplies
- **Action System**:
  - ActionType.cs, ActionState.cs, ActionResult.cs - Action framework
  - ActiveAction.cs - Running action with progress tracking
  - StaminaPool.cs - Stamina resource management
  - ActionNotification.cs - User feedback for action events
  - ActionSystem.cs - Woodcutting, mining, cooking, foraging with stamina costs
- **Interaction System**:
  - InteractSystem.cs - Generic interaction framework
  - FireInteraction.cs - Campfire cooking, warmth
  - NPCFlows.cs - NPC dialogue and interaction flows
- **Building System**:
  - Structure.cs - Building data with requirements, outputs, upgrades
  - BuildingSystem.cs - Placement, construction, validation, grid snapping
- **NPC System**:
  - NPC.cs - NPC entities with AI, factions, schedules
  - NPCSystem.cs - Spawning, updating, despawning
  - TradeSystem.cs - Player-NPC trading with dynamic prices
  - QuestSystem.cs - Quest tracking, objectives, rewards
  - RecruitmentSystem.cs - Follower recruitment and management
  - FactionSystem.cs - Faction reputation, diplomacy, territory
- **Skills System** (8 skills with OSRS XP formula and sub-stats):
  - SkillManager.cs - Central skill registry, XP calculation, level ups
  - WoodcuttingSkill.cs - Tree felling, log processing
  - MiningSkill.cs - Ore extraction, gem finding
  - ForagingSkill.cs - Plant gathering, herb identification
  - CookingSkill.cs - Food preparation, recipe discovery
  - FiremakingSkill.cs - Fire lighting, fuel efficiency
  - CraftingSkill.cs - Item crafting, material processing
  - MetallurgySkill.cs - Smelting, alloy creation
  - ConstructionSkill.cs - Building construction, blueprints
  - IntelligenceSkill.cs - Enchanting, research, mana
- **Combat System**:
  - CombatSystem.cs - Turn-based and real-time hybrid combat
  - Monster.cs - Monster AI, stats, loot tables
  - DamageNumber.cs - Floating damage text
- **Render System**:
  - TileRenderer.cs - 2.5D terrain rendering with biome transitions
  - SpriteRenderer.cs - Animated sprite batching
  - CombatUI.cs - Health bars, targeting indicators
  - ParticleSystem.cs - Environmental and combat particles
  - SeasonalRenderer.cs - Seasonal visual transitions
  - LightingSystem.cs - Dynamic lighting with day/night cycle
- **Season/Weather System**:
  - SeasonSystem.cs - Four seasons with gameplay effects
  - WeatherSystem.cs - Rain, snow, storms with visual/audio
- **Inventory System**: Inventory.cs - 20-slot grid with spoilage, stacking, categories
- **Crafting System**: CraftingSystem.cs - Recipe-based crafting with stations
- **UI System**:
  - HUD.cs - Health, hunger, stamina, skills, minimap
  - Panels.cs - Trade, Quest, Recruitment, Diplomacy, Faction Info panels
  - Panels2.cs - Inventory, Skill, Crafting, Building, Gear, Dashboard (5 tabs)
  - Screens.cs - Title, Character Select, Loading screens
- **Assets**: 300+ procedural PNG sprites for terrain, trees, rocks, items, gear, monsters, NPCs, structures, UI
- **License**: MIT License

### Changed
- Refactored DataLoader from static utility to instance class with loaded data fields for Bootstrap consumption
- Fixed project naming from "Don'tStarveRuneScape" to "DontStarveRuneScape" to avoid apostrophe issues

### Infrastructure
- Git repository initialized with .gitignore for .NET projects
- GitHub repository created at https://github.com/kwestionmark-god/DontStarveRuneScape
- Initial commit with full C# port (451 files, 17,000+ lines)
- CI/CD ready project structure
