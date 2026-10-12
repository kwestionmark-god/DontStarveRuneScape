# Development map — Don't Starve + RuneScape + MountainCore

Status snapshot 2026-10-04, reconciled against
`docs/superpowers/2026-10-04-colony-fusion-roadmap.md` and `git log` on
`main` (HEAD `a97c4e5`). This map covers the whole project. Colony item
checklists stay in the roadmap doc; this file only sequences them.

Pillars and the balance rule are defined once, in
`## Tri-fusion design law` inside
`docs/superpowers/2026-10-04-colony-fusion-roadmap.md`. Tags below cite
that section; they do not restate it.

- **DS** — Don't Starve: player-centered survival.
- **RS** — RuneScape: skill XP and the quest/merchant/faction NPC world.
- **MC** — MountainCore: colony logistics.

## Shipped

- **Colony phases 0–2** (roadmap). Experiment, recruit work foundation,
  settlement anchor/stockpile/save snapshots. MC, with DS survival and RS
  skills left in the player's hands.
- **Colony phase 3 workplaces, mostly** (roadmap checkboxes all checked
  except structure upgrades). Staffed stations, recipe requirements, FIFO
  orders, input fetching, wheat plots, cooking/smelting, multi-stage
  dependencies, construction sites. MC logistics on top of RS crafting XP.
- **Colony phase 4 needs and factions, mostly.** Food from colony stores,
  rest with bench/fire/season, woven shelter, faction-linked guard
  threats, faction-priced trade. DS pressure plus RS faction world.
- **Caves stages 1–2.** Distance-band entrance, beacon, enter/exit prompt,
  persistent cave map, ore bands, cave troll. DS exploration and danger;
  RS mining gated by skill level.
- **Gait and dome-boot rendering.** Shared gait for player, NPCs, and
  monsters (`b68da74`, `22ef2b8`); spherical dome boots (`338a684` and
  follow-ups). Presentation of the DS/RS character, not a new loop.

## In progress

These are the roadmap's remaining open lines. Nothing else on the
roadmap is unchecked.

- **Phase 3 — structure upgrades.** The only open phase-3 checkbox:
  `- [ ] Add structure upgrades.`
- **Phase 4 — worker schedules and task selection.** Roadmap line: "Give
  workers schedules and simple task selection, with sensible fallbacks."
  Its logistic first cut shipped 2026-10-04 (`c7a9b6d`): task board with
  synchronous tile reservations, haul-home carrying, deposit reclaim,
  interruption-safe release, carried-goods persistence. Remaining on the
  line: schedules themselves (time-of-day work rhythms) and the broader
  task-selection fallbacks beyond gather/haul.
- **Phase 4 — defense and trade wiring, remainder.** Done 2026-10-07 —
  caves, broader threats, and diplomacy are now wired (see Ordered next
  work item 4). No phase-4 lines remain open.
- **Phase 5 — persistence and polish.** Three open lines: save/restore of
  anchors, stockpiles, assignments, orders, and resident state;
  blocked-work feedback; simulation cadence and rendering for larger
  populations. Settlement snapshots already save; this line is the
  remainder and the polish.

## Parked

Not next. The project-state memory topic once called cave stage 3 "the
approved next arc." The roadmap's open colony lines are the current
priority; stage 3 waits behind them. One ordered list, below.

- **Cave stage 3** — real darkness, light falloff, cave ambience. Not
  started (`dontstarve-runescape-cave-development.md`).
- **Gait sprite polish** — code and the wolf showcase sprites shipped
  (`22ef2b8`). What remains is optional cosmetic refresh (legless
  creatures such as djinn and serpents), not a blocking pass.
- **Deferred ideas** (`dontstarve-runescape-deferred-ideas.md`):
  click-to-move is dead code; carried equipped tools; torch missing from
  the gear menu.

## Ordered next work

Dependency order, then pillar balance. Each item names how the design
law shapes it.

~~1. **Logistics slice — task board, reservations, hauling.**~~ **Done
   2026-10-04 (`c7a9b6d`).** First cut of the open phase-4 schedules line.
   Pillar: MC. Per the design law, hauling moves stock the colony already
   owns; it does not spawn resources or replace player gathering.
2. **Structure upgrades** — done 2026-10-06. Closes the last phase-3
   checkbox. Pillars: RS (player-gated recipes and skill) and DS (materials
   the player helped gather). The design law held: upgrades track player
   progression via the construction skill gate; the new shelter tiers
   mitigate weather (stronger recovery, more HP) but never nullify it.
   Spec: `docs/superpowers/specs/2026-10-06-structure-upgrades-design.md`.
3. **Worker schedules and task-selection fallbacks** — done 2026-10-06.
   Finishes the phase-4 schedules line: 24-slot schedule templates (worker
   day, guard shift squads, bedtime, meals, night floor) plus fallbacks
   (FREE_TIME never dispatches work; idle WORK-slot workers wander near camp,
   labeled Idle). Pillars held: MC readability, DS night/season pressure
   shape when work is safe. Spec:
   `docs/superpowers/specs/2026-10-06-worker-schedules-design.md`.
4. **Threats, caves, factions, diplomacy — the remainder.** Done
   2026-10-07. Closes the phase-4 defense-and-trade line: daily merchant
   visits at 06:00 (standing ≥ 0.65, 4-hour window), diplomacy tab with
   live standing rows and trade click-through, daily hostile-faction raid
   roll (standing < 0.25, territory overlap, 2–4 monster party at the
   perimeter) with "Raid incoming" notifications, cave-expedition guard
   button in the colony dashboard, deterministic raid parties (map-edge
   clamp — fixed a latent RNG flake), and cave expedition save/restore
   (deterministic cave rebuild from seed + entrance). Spec:
   `docs/superpowers/specs/2026-10-06-threats-caves-factions-diplomacy-design.md`.
   Pillars held: DS danger arrives at the settlement from the RS faction
   world; the player never clicks through an RTS menu — reactions flow
   from standing the player already shaped by negotiating and trading.
5. **Phase 5 persistence, blocked-work feedback, cadence.** Done
   2026-10-07 — all three lines closed. Save/restore:
   SettlementPersistenceTests proves the full round-trip end-to-end
   (anchor, stockpile, assignments, queued orders, residents; `2246a2b`),
   and cave expeditions persist (`3cb8652`). Blocked-work feedback:
   missing materials, full storage, unreachable jobs, and paused orders
   were already visible; danger too — a hostile near a workplace pauses
   the job with visible status and the worker backs off (`9124bba`).
   Cadence/readability: the colonist list scrolls past its 3 visible
   rows and the header shows the population count (`9efe566`).
6. **New frontier — post-roadmap features (opened 2026-10-07).** The
   original arc above is done; development continues with fresh feature
   slices the user scoped 2026-10-07. Draft a spec per the house style
   (see specs/) before implementing each. Candidates in the order the
   user raised them — **per-recruit skill stats got the loudest
   enthusiasm** and is likely the best first big slice:
   - **Per-recruit skill stats** — **done 2026-10-07 (`ef1f8b4`).** Every
     recruit carries its own SkillManager on the player's OSRS curve;
     harvests train the node's gathering skill, construction trains
     construction, and gatherers earn a doubled harvest every
     max(1, 20−2·level) intervals (deterministic). Skills persist through
     NPCDataSnapshot; pre-slice saves load a fresh level-1 manager.
     Guard attack-XP is recorded as a deferred hook for the combat
     scaling slice. Spec:
     `docs/superpowers/specs/2026-10-07-per-recruit-skill-stats-design.md`.
     Pillars held: RS progression (levels earned through real dispatched
     work, never on the clock), DS pressure (recruits start weak at
     level 1), MC readability (levels read like characters).
   - **Static resource sprites** — draw resource nodes statically like
     the begun entity-sprite work (reuse existing art for a first pass).
     Done 2026-10-07 (`164585b`): resource nodes render as world-fixed
     crossed billboards — two static world planes on a deterministic
     per-tile facing, the entity paper-doll machinery applied to
     inanimate world dressing. Spec:
     `docs/superpowers/specs/2026-10-07-world-fixed-resource-billboards-design.md`.
   - **More characters** — **done 2026-10-07 (character backgrounds).**
     Creation offers Wanderer/Forester/Prospector/Scavenger, mapped to
     the three starter packs that already existed but were unreachable;
     mouse + arrow-key selection, the choice flows panel → StartNewGame
     → PendingCharacterDef → Bootstrap's ApplyStarterPack, and persists
     as SaveData.CharacterBackground (missing field = wanderer — old
     saves load unchanged). Playstyle, not power: one tool, one torch,
     one food, no stat bonuses. Spec:
     `docs/superpowers/specs/2026-10-07-character-backgrounds-design.md`.
     Pillars held: RS (the start shapes the grind), DS (every background
     starts vulnerable), MC (wired the existing packs, no new systems).
   - **More quests** — **done 2026-10-07 (First Steps chain).** A six-quest
     early-game chain (first_flame → hearth_and_home → full_belly →
     first_blood → settling_in → forest_friends) from the forest quest
     giver: pure data on the existing condition hooks (collect/craft/kill/
     visit/negotiate), prerequisite-chained, ends pointing at the colony
     and commerce arc; timber_collection kept as a side quest. Data-
     integrity and chain-order tests included. Spec:
     `docs/superpowers/specs/2026-10-07-first-steps-quest-chain-design.md`.
     Pillars held: RS (guided grind teaches one skill per step), DS
     (survival-first objectives — fire, food, roof), MC (zero new systems).
   - **Starting companion/follower** — **done 2026-10-07.** Hunter Mara
     (`companion_mara`) joins with zero stat gates as the early-game
     first follower: one bonded companion per player, walks to close the
     gap (never teleports), posts up beside the player, drops off with a
     visible "Left behind" status after a sustained 14-tile gap until
     the player returns within 7 tiles, and flees hostiles near the pair
     instead of tanking. CompanionBehavior steers inside
     RecruitmentSystem.Tick as a sibling to the guard branch — companions
     bypass schedules and the task board, while hunger/rest needs and
     NPCDataSnapshot persistence (per-recruit skills included) ride the
     existing colony paths. Spec:
     `docs/superpowers/specs/2026-10-07-starting-companion-design.md`.
     Pillars held: RS (a companion grows with you via slice-1 skills), DS
     (she is a liability — frail at 18 HP, needs food and rest), MC (one
     new behavior on the existing recruit machinery, zero new systems).
   - **Colony-building skill structure** — **done 2026-10-08.** Colony
     capability follows the player's construction skill ("the colony never
     out-produces its founder"): above-tier stations are never claimed and
     read "Awaiting builder competence" on the dashboard, and the assistant
     harvest path now filters by the recruit's own gathering level against
     the node's required_level (closes the gold_vein-by-level-1 hole).
     Spec: `docs/superpowers/specs/2026-10-08-colony-building-skill-structure-design.md`.
     Pillars held: RS (your level is the colony's roof — nothing high-tier
     runs without you), DS (the grind is honest for workers too now), MC
     (one gate revealed by status, zero new systems).
   - **Water gathering — bucket mechanics** — **done 2026-10-08.** Water is
     no longer free: `water_source` requires a bucket, a craftable
     equippable tool (planks×2 + grass_rope×1, crafting level 2, sprite
     reused). The player gate rides the existing axe/pickaxe tool
     pipeline verbatim (zero player-path code); worker dispatch's
     surface blanket-ban on tool nodes became the same stock check caves
     use (player inventory OR colony store) — visible in stock, silent
     skip without, and the surface pickaxe path stays guarded by test.
     Spec:
     `docs/superpowers/specs/2026-10-08-water-gathering-bucket-mechanics-design.md`.
     Pillars held: RS (progression gates the infinite node), DS (the
     grind stays honest — water costs a crafted tool), MC (the gate is
     stock-visibility, not a new UI).
   - **Fishing — rod mechanics + fishing skill** — **done 2026-10-08.**
     Fish are no longer free: `fish_spot` requires a fishing_rod, a
     craftable equippable tool (planks×2 + grass_rope×1, crafting level 2,
     sprite reused); `required_level` dropped 5 → 1 so the only fishing XP
     source isn't stranded behind its own gate. Catches now train a real
     `fishing` skill (net-new; XP previously leaked into foraging for the
     player, mining for workers). Two landmines the design dodged: a
     Foraging-route XP leak (CompleteAction nulls Active.Resource before
     ProcessCompletion maps the skill — only the new `ActionType.Fishing`
     enum survives the reset) and the worker any-tool-node → mining map
     (GatherSkillFor gained a fishing branch before the tool check). Player
     gate rides the existing tool pipeline verbatim; workers fish when the
     rod is in stock (bucket convention). Skills panel gained a fishing
     row. Spec:
     `docs/superpowers/specs/2026-10-08-fishing-rod-skill-design.md`.
     Pillars held: RS (progression gates the node AND names the skill it
     trains), DS (the grind stays honest — fish cost a crafted rod),
     MC (readability — existing refusal vocabulary, existing decal art).
   - **Fishing — cast window + animated visuals** — **done 2026-10-08.**
     Fishing is now the one timed gather: a 3s cast (Constants.
     FishingCastSeconds) holds the catch, giving the animation window —
     walking out of interact reach cancels it ("You moved — the fish got
     away.", CancelActive). Fish spots animate in place: decal breathing
     + two expanding ripple rings (per-tile phase hash, no RNG) drawn in
     the existing seabed pass. While casting, the player renders a
     primitive rod angled toward the spot, a thin line, and a bobbing
     bobber at the waterline — all primitives, no new art. New
     SpriteRenderer.AnimTime world clock (advanced by Game.cs once per
     update) and DSR_TEST_FISHING=1 smoketest hook. Found and fixed a
     real harness bug along the way: Settings.PathOverride was set
     AFTER the Game ctor loaded settings, so smoketest runs silently
     used the user's real Borderless+VSync window — on a locked/sleeping
     session the swap present deadlocked after ~2 frames; the override
     now applies before the ctor with headless-safe defaults.
     Pixel-diff verification still pending (approval-flow block; re-run
     when the user is at the keyboard). Spec:
     `docs/superpowers/specs/2026-10-08-fishing-cast-animations-design.md`.
     Pillars held: RS (the cast is the OSRS-style progression beat),
     DS (walking away loses the fish — the wait is real time),
     MC (the spot itself telegraphs "fish here"; the cast reads at a
     glance).
   - **Fishing — bait economy** — **done 2026-10-09.** Fishing is now a
     prepared activity: `craft_fishing_bait` (shells×2 + fibers×1,
     crafting 1, ×4 per batch, worm-segment sprite reused) turns coastal
     trash into catch-doubling consumables. Player casts consume one bait
     at cast START — the bait sinks with the cast, so walk-away cancels
     get no refund (honest grind) — and baited catches land ×2 with
     "(bait)" in the message. Worker fishers burn one colony-store bait
     per harvest for a doubled haul via the meals/materials
     stock-consumption idiom; unbaited fishing stays the silent ×1
     default, visible in the stock ledger. Unbaited casts still work —
     bait is an economy, not a wall. Spec:
     `docs/superpowers/specs/2026-10-09-fishing-bait-design.md`.
     Pillars held: RS (the prep loop — shells → bait → doubled catches),
     DS (per-cast consumption, no refund; entry-tier but real
     ingredients), MC (the doubled catch names itself; workers mirror
     the stock-visibility convention).
   - **Fishing — rod tiers (bone rod)** — **done 2026-10-09.** The cast
     window is the tier lever: the carved rod (crafting 2) casts in 3s,
     the bone rod (wolf_bone×2 + grass_rope×1, crafting 4, bone_wolf
     sprite) in 2s — tier buys speed, never yield (yield stays the
     bait economy's knob, both stay relevant). Player path free via the
     FindEquippedTool suffix match (the tool-check's matched id is now
     hoisted for the Fishing branch); the cast notification names the
     rod. Found and fixed a worker gap while grounding: the colony-store
     tool check only matched exact + `stone_` ids, so a store-side bone
     rod would have silently failed the gate — `CanWorkerHarvest`
     gained the `bone_` arm (stone_axe convention extended). Spec:
     `docs/superpowers/specs/2026-10-09-rod-tiers-bone-rod-design.md`.
     Pillars held: RS (tool progression on one node — entry vs
     upgrade), DS (the upgrade costs combat drops, not wood; the base
     rod never stops working), MC (the notification names the tier;
     no new systems).
   - **Fishing — rare-drop table (pearl + old boot)** — **done
     2026-10-09.** The keep-casting hook: every successful catch
     rolls a second, level-scaled table — pearl (1% + 0.15%/fishing
     level, the payday: coastal merchants pay 25 gold, sell-only via
     the stock-0 entry) then the old boot (4% + 0.2%/level, vendor
     junk with no market). The roll uses the level frozen at cast
     start (`ActiveAction.FishingLevel`, OSRS-style). Player catches
     name the dredge ("You dredged up a pearl!") and the grant posts
     the gold rare-find notification; a full inventory loses the
     rare VISIBLY ("No room — the pearl slipped back into the
     water!") — never a silent vanish. Worker fishers roll the same
     table on their own fishing level (rod nodes only) and rares land
     directly in the stockpile ledger. Test hooks follow the
     RaidRollOverride convention (`ForceRareDrop` string + seeded
     `RareDropRandom` on both systems). Spec:
     `docs/superpowers/specs/2026-10-09-fishing-rare-drops-design.md`.
     Pillars held: RS (the drop table compounds the grind — better
     fisher, luckier water), DS (rares are dredged, never bought; the
     full-bag loss is honest and visible), MC (the catch message
     names the dredge; workers mirror the stock-ledger convention).
   - **Animal taming (feed-to-tame + pets)** — **done 2026-10-09.** The
     ten `tamable` species in monsters.json are now feedable: E on a
     live tamable monster within reach consumes its species food
     (`tame_food`) and rolls a tame window — 35% at parity, ±5% per
     taming-level delta vs the species `tame_level` gate, +2% per
     success_rate point, clamped [5%, 95%] (`TamingMath`, pure/static,
     hook-free tests; `RollOverride` string + seeded `Rng` for the
     probabilistic paths, raid convention). Attempts cost the food
     even on failure and award 4 XP to the new top-level `taming`
     skill; successes award 25 more, remove the wild monster, and
     stage a pet (RecruitNpc, RecruitBehavior "pet", SpeciesId —
     rendered via RenderNPC with the species `monster/` sprite).
     First pet bonds as the follower (companion tether contract:
     follow, 14-tile drop-off, 7-tile resume); later pets are
     colony-assigned — approach-and-strike any hostile inside a 6-tile
     guard radius of their post, hold position otherwise. Level-gated
     attempts refuse visibly and consume NOTHING (a gate is not a
     loss); wrong/missing food tells the player what the animal wants.
     Pets persist via `SaveData.TamedAnimals` (not the NPC registry —
     the restore path skips registry-missing rows; old saves read an
     empty list). Player path: InteractSystem E-branch between NPC
     panels and resource gather; game loop ticks followers and guards
     surface-side only. Spec:
     `docs/superpowers/specs/2026-10-09-animal-taming-design.md`.
     Pillars held: RS (a real taming skill with per-species level
     gates — wolf 1, boar 2, scorpion 3, snake 3, eagle 4, croc 4,
     bear 5), DS (the food is spent on the attempt — grind honesty;
     the level gate refuses instead of silently eating), MC (the
     refusal/wants-food messages reuse the notification vocabulary;
     pets render as their species, no new art).
   - **Individual stat menus (per-skill sub-stat catalogs)** — **done
     2026-10-09.** The skills panel's one-size-fits-all five sub-stats
     are gone: every skill now has its own menu, single-sourced in
     `SkillManager.SubStatCatalog`, and every key on every menu has a
     live consumer. Attack finally appears in the panel (12 rows; its
     levels were already banking invisible points) with power (+flat
     damage) and speed (−0.12s/pt attack cooldown, gear floor). New
     wired stats: fishing rare_luck (widens the pearl/boot windows,
     stashed at cast start like the level), agility sprint_cost /
     jump_cost (−5%/pt, cap 75%), firemaking duration (+5s/pt) and
     fuel_saver (4%/pt fuel rebate), craft-seam harvest_boost (+1
     output chance) and efficiency (input save) for cooking/crafting/
     metallurgy, construction build_speed (+5%/pt on colony site
     ticks — the founder speeds the colony's builds). Honesty fixes
     while in there: fishing success_rate normalized to the family
     ×100 scale (was ×1), fishing and foraging harvest_boost were
     stashed but DEAD — both now have real yield arms. Old saves
     respec: snapshot keys a skill's new menu drops return as that
     skill's unallocated points (visible, never lost). All new
     consumers read RAW invested points (`GetSubStatPoints`) — zero
     points keeps every legacy number bit-identical. Spec:
     `docs/superpowers/specs/2026-10-09-individual-stat-menus-design.md`.
     Pillars held: RS (every point buys something real — a stat ships
     only with a live consumer), DS (no free power — new knobs are
     invested-points-only), MC (menus render from the same catalog
     SpendPoint admits; respec surfaces as the green counter).
   - Parked sub-slice: refused-messaging on the work-order PANEL itself
     (the gate exists; the UI hint is follow-up).
   - **Backlog (user-raised 2026-10-09, unsequenced — the ideas pool for
     future slices)**:
     - **Vertical building + terrain leveling.** The jump exists
       (agility slice, Space); give it something to land on.
       Wall/workbench BLOCKS at jump height, stackable, connectable;
       jump must land on a placed block; building needs a FLAT pad or
       the corner-stitched heightmap breaks stacking — so a terrain
       LEVELING tool/method ships with it. **Slice 1 DONE 2026-10-11
       (spec 2026-10-11-vertical-building-foundations-design.md; RED
       8b53a0c / GREEN 20965a3):** StructureDef gains `is_block` +
       `stack_height` (parser + data pins — every block def must occupy
       its tile and carry a positive stack height); first def
       `mound_block` (Dirt Mound, structure/stone_wall sprite, cheap
       2 stone + 1 stick). `PlaceStructure` gates block defs on a flat
       pad — the tile's four corner-stitch elevations must spread ≤
       `Constants.FlatPadMaxSpread` (0.01) — with the honest refusal
       "Needs flat ground — level this tile with a shovel", plus a
       block-only water refusal; non-block defs keep every legacy gate
       bit-identical on slopes. The remedy ships in the same slice:
       new `shovel` tool (gear.json, craft_shovel recipe at crafting 1,
       2 stick + 1 stone) and `ActionType.Flattening` — a V-keyed 3 s
       timed action on the player's tile that sets its elevation to the
       MEDIAN of the 4 corner-source tiles (self + 3 forward neighbors —
       the agreement neighbors already hold, where a mean lands between
       levels and stays sloped), pays 10 construction XP, refreshes the
       corner stitch, and refuses — with notification, never a silent
       loss — on water, structure, resource-node, no-shovel, and any
       target that would sink to the sea. Edits persist: TileMap records
       them and SaveData.World.TerrainEdits round-trips through the
       seeded rebuild (RestoreWorld applies them after regen, same as
       depleted nodes). Suite 540 → 553 (13 new tests).
       **Slice 2 DONE 2026-10-11 (spec
       2026-10-11-vertical-building-jump-landing-design.md; RED 9a5e249 /
       GREEN 83e045f; 11 new tests):** blocks are now walls and
       trampoline tops: `Building/BlockStack.cs` holds the pure rules —
       `BlockHeightAt` (summed stack heights of ACTIVE, BUILT blocks on
       a tile; sites aren't solid), `StandingElevationAt` (tile center
       elevation + block lift), `CanStep` (descents and pure-terrain
       ascents stay legacy-free; stepping into a TALLER stack is a wall
       on foot, a hop of ≤ `Constants.BlockClimbCap` = 2.05 levels
       airborne — one mound = one hop), `RenderTop` (blocks draw at
       their top height). `Player.ApplyKeyInput` gains optional
       world/buildings params that apply the gate per-axis (axis-slide,
       no bounce) — Game.cs:902 passes them; the render loop feeds the
       player sprite and boot sampler the block-lifted elevation and
       block sprites render at their top. mound_block stack_height
       8 → 2 levels so one mound is exactly one jump (a wall of 8 was
       ≈102 px vs the 26 px arc). Suite 553 → 564.
       Known seams: Player.TryJump
       (`Player.cs:206`) and UpdateJump's arc now MEAN something —
       airborne + cap is the climb. **Slice 3 DONE 2026-10-11** (spec
       2026-10-11-vertical-building-stacking-design.md; RED b4fc9b7 /
       GREEN 462248b): blocks STACK into per-tile columns (PlaceStructure
       partition: block-onto-block allowed, non-block occupancy refusal
       bit-identical) with a 4-unit budget (`BlockColumnMaxHeight` — two
       mounds = the first true unclimbable wall); new `fence_block` def
       (1 unit, 2 sticks, structure/wooden_gate art) is a half-hop per
       the standing jump rule; `BlockStack.ConnectionMask` is the pure
       N/E/S/W fence-link seam (sites aren't solid); `RenderTop` is now
       cumulative and the Game.cs render site reads it; columns persist
       via the existing BuildingSnapshot. Suite 564 → 581.
       **Slice 4 DONE 2026-10-11** (spec
       2026-10-11-vertical-building-render-design.md; RED c6794eb /
       GREEN a67d2f4; user pivot: structures were 2D, yaw-fixed,
       floating): `RenderStructure` now draws world-anchored CROSSED
       PLANES (same paper-doll as resources: base planted on the ground
       line, deterministic world facing — blocks instead face their
       `FenceFacingDir` connection axis from `ConnectionMask`).
       `BlockStack.RenderBase` is the cumulative column-base seam
       (RenderTop delegates: base + own stack); the Game call site
       passes the BASE, not the top — no more float. Blocks draw
       `StructureHeightWorld` = stack_height × 12.8 world px, so a
       column reads as one contiguous stack. Fences draw plank-brown
       rails (`FenceRails` center→edge-midpoint per masked bit) toward
       neighbors. Pixel pass: rails VERIFIED connected-vs-not
       (1128 vs 132 brown px in the same-camera strip; extent covers
       the neighbor edge), grounding/height pinned by projection-exact
       unit tests (base corners ≤0.6 px off the ground line at 5 yaws,
       extents flip with yaw). New hook `DSR_TEST_STRUCTURE=id:tx:ty`.
       Suite 581 → 595.
     - **Vertical building, slice 5 — true voxel blocks (user critique of
       the slice-4 render, 2026-10-11: "two mounds side by side don't
       JOIN — go true voxel, same image projected as surface area, and
       stitch joined blocks") **DONE 2026-10-11** (spec
       2026-10-11-block-voxel-render-design.md; RED 1ecd53b / GREEN
       17cf450): a block now renders as a world-space BOX —
       `SpriteRenderer.BuildBlockVoxel` builds a top face + the
       camera-facing side faces from the tile's real world corners × its
       column base/top elevations, all through `WorldToScreen`; the
       sprite maps as surface area (full-UV per face) over a solid fill
       tinted with the sprite's average opaque colour (billboard glyphs
       carry wide transparent margins — the underlay kills the gaps).
       STITCHING: a side face culls the span its neighbour column
       already fills, so adjacent equal-height blocks share their top
       edge pixel-for-pixel and render as one continuous volume;
       shorter neighbours expose exactly the height-step strip.
       Fences keep their crossed planes + rails (user decision).
       Pixel-VERIFIED via DSR_TEST_STRUCTURE: two joined mounds read as
       a single 2×1 box. Suite 601 → 601 (6 new voxel-contract tests).
     - **Vertical building, slice 6 — sprite-proportioned walls (user
       critique: "the tile size makes chunky walls — use the normal
       sprite size, extend them to join naturally") **DONE 2026-10-11**
       (spec 2026-10-11-block-voxel-walls-design.md; RED b1d86df / GREEN
       90cadc8): `BuildBlockVoxel` gains `(connectionMask, halfThickness)`
       — the box footprint is center ±16 px on unconnected sides and
       EXTENDS to the tile edge on each connected side, so joined blocks
       BUTT at the shared boundary into one continuous slender wall
       instead of overlapping fat 64-px chunks; isolated blocks render
       as compact 32×32 slabs. Slice-5 strips/culling/sort unchanged.
       Gameplay untouched — blocks still OWN the full tile for
       stacking/movement; only the drawn footprint slimmed. Pixel-
       VERIFIED: 3-block row = one seamless wall. Suite 601 → 602.
     - **Vertical building, slice 7 — brick-by-brick building grid (user
       pivot: "instead of stretchy joining, really go brick by brick —
       granulate the building grid, each tile holds maybe 9 bricks, they
       can all just have their fixed sizes") **DONE 2026-10-11** (spec
       2026-10-11-block-voxel-bricks-design.md; 7a RED 69132fd / GREEN
       28e2a97; 7b RED e20dacb / GREEN 89d220a): the building grid
       granulates to 4x4 sub-cells of 16 px per tile (texel-exact 1:1
       with the 16x16 sprite — Terraria's cell size; user pick over
       3x3). `mound_block` rebrands to "Dirt Brick" (stack_height 1.25 =
       16 px at 12.8 px/unit; id kept), placed per sub-cell via the new
       `PlaceStructure(id, tx, ty, subX, subY, …)`; repeated placement
       at one sub-cell stacks courses within the new 5.0-unit column
       budget (4 bricks = a 64 px wall; user pick). Placement consumes
       the craftable `dirt_brick` item (craft_dirt_brick: 2 stone + 1
       stick → 1, crafting 1 — net economy unchanged; user: "keep it as
       it is"); bricks bypass the colony-site flow (player-built).
       Standing/stepping/jump-landing resolve per sub-cell
       (`BlockStack.SubHeightAt`, swept `CanStep` — a 37.5 px tick
       sweeps 8 px steps so a 16 px column can't be stepped over
       between ticks); 1 brick (1.25) is a hop, 2 (2.5) a wall.
       `BlockHeightAt` is now MAX over sub-cell columns, never a sum.
       Render: `BuildBrickVoxel` draws each brick as a fixed 16 px box
       over its sub-cell (shared `BuildBoxVoxel` core with the slice-6
       slabs; neighbour tops wrap across tile edges) — the slice-6
       mask/halfThickness stretch games are dead for bricks; fences
       stay tile-level crossed planes + rails. Build mode goes
       open-ended for bricks (place many, Esc/Q closes) and shows the
       hovered tile's 4x4 ghost grid with the hovered sub-cell solidly
       highlighted (`BuildGhostSubCells`). Legacy tile-level mound
       saves migrate to full 4x4x2 brick blocks. Pixel-VERIFIED:
       3-brick same-camera delta = 3502 px in-region, 0 noise in
       controls, flat pixel-art palette (1:1, no stretch). Suite
       602 → 616.
     - **Vertical building, slice 8 — brick face cover + bricks-only
       occlusion (user critique of the slice-7 render, 2026-10-11: "the
       textures still dont fill the surface area of a single brick.. i
       notice that bricks are also iffy about visibility when adjacent to
       varying elevations..") **DONE 2026-10-11** (spec
       2026-10-11-brick-face-cover-design.md; RED 5f9abdd / GREEN
       0108152): `mound_block` points at its own authored 16x16
       `structure/dirt_brick.png` — all 256 px painted (3 seeded brick
       courses + 1px dark mortar, 10 colours, zero partial alpha) instead
       of the old `stone_wall` glyph (16x16 but only 80/256 px opaque =
       31%, a glyph floating in an underlay field). New pure query
       `BlockStack.BrickColumnTopAt(world, buildings, tx, ty, subX, subY)`
       = the sub-cell's brick/block column top, or null beside bare
       terrain; RenderStructure's brick branch feeds `NegativeInfinity`
       for null, so a neighbour's raw elevation can no longer cull a
       brick face — only a neighbouring brick column (or a tile-level
       block, e.g. a fence) strips it. Accepted consequence (user
       decision 2026-10-11): a brick against a steep slope keeps its face
       and may poke through the hillside slightly. Render-only — the
       slice-5/6 tile-slab voxel branch and every movement/standing query
       are untouched. Pixel-VERIFIED by a same-camera pre/post A/B (the
       pre-slice-8 occluder re-landed temporarily, then reverted): flat
       ground renders BIT-IDENTICAL pre/post (0 changed px, every control
       box 0 — the fix is a no-op for level neighbours); raising the south
       neighbour just past the brick top (16.5 → 18.0 vs the brick's
       17.75) strips the WHOLE south face pre-fix (brick texels 1590 →
       1000, the face rows 643-655 going to zero) and keeps it post-fix
       (1336; the lowest ~8 rows are legitimately under the raised
       ground); a neighbouring BRICK column still strips the shared face
       (350 px changed inside the brick box at bit-identical terrain), so
       occlusion was narrowed, not disabled. Suite 616 → 621 (5 new
       contracts).
     - **Vertical building, slice 9 — NPC/monster block collision +
       standing truth (the parked "NPC standing on block tops" item,
       widened to the honest scope: every grounded mover respects the
       brick world). DONE 2026-10-12** (spec
       2026-10-12-npc-block-collision-standing-design.md; RED f191e24 /
       GREEN f21e294): blocks are walls for every grounded mover. New
       pure `BlockStack.GroundedSlide` gates each axis of a step
       (blocked axis zeroes, free axis applies — the player's own
       per-axis law; grounded rule: any block ascent is a wall,
       descents stay free, so nothing is ever trapped inside a column).
       Wired at all 7 movement sites: monster chase/patrol/flee
       (CombatSystem.Tick + UpdateMonster + UpdateWander thread
       world/buildings — legacy callers keep today's walk-through),
       worker path-walk + goal + guard steps (RecruitmentSystem reads a
       tick-scoped `_groundedBuildings` set from the Tick buildings
       param; MoveAlongPath covers wander too), pet follower +
       colony-guard approach (TamingSystem). Occupancy truth goes LIVE:
       `RemoveStructure(structure, world)` recomputes the tile's
       Structure slot from remaining occupants — before, a removed
       brick left its tile pathing-blocked forever while BlockStack
       said empty (two truths diverged). IsCave guard at the
       SubHeightAt/BlockHeightAt roots: the ONE surface BuildingSystem
       never pairs with a cave map (that pairing invented phantom walls
       at overlapping tile coords — a latent bug the player already
       carried into caves; fixed with the same guard). Render truth:
       the monster/NPC/pet render sites add the sub-cell lift (the
       player's Game.cs idiom) — an entity overlapping a column
       renders ON it and depth-sorts there; cave passes stay raw.
       User decisions 2026-10-12 (all recommended defaults accepted):
       any block = a wall for all grounded movers (no NPC climbing —
       the player must JUMP to mount, grounded movers can't), blocked
       movers axis-slide (Minecraft-zombie grind, never a dead stop),
       monster-vs-wall damage PARKED (walls are safe until its own
       slice; cheesing a raider behind 2 bricks works for now),
       monsters stay beeline (no pathfinding — they pile and grind).
       CompanionBehavior needed NO change: it already slides per-axis
       on the CanStand tile truth, which the live-slot fix heals.
       Pixel-verified same-camera pre/post (new
       `DSR_TEST_MONSTER=id:tx:ty` hook, aggro-frozen for deterministic
       captures): off-column control BIT-IDENTICAL (2415-px wolf mask,
       centroid equal), in-column wolf sprite top edge y501 → 483 =
       18 px up (predicted 1.25 units × 12.8 world px × sin(pitch) ×
       1.25 zoom), brick reveal 74 → 132 px (rows y542-560 uncovered by
       the lifted sprite), brick-only baseline identical pre/post;
       known run-to-run noise: animated-water phase + one unseeded
       patrol rng, both localized away from the verified region.
       Suite 621 → 638 (17 new contracts; 8 RED as assertion failures
       with verified reasons).
     - **Vertical building, slice 10 — structure sort honesty (user
       critique, 2026-10-12: "the structures are very sensitive to
       yaw/pitch panning.. id like any player built structures to be
       statically visible, the only time they should have reduced
       visibility, is when something is actually in the way"). DONE
       2026-10-12** (spec
       2026-10-12-structure-sort-honesty-design.md; RED e3705b0 /
       GREEN 79cb827): dirt bricks were sinking/burying into the ground
       at some orbit angles and coming back — the painter's-order tie
       with their own tile, not a geometry bug. The structure loop keyed
       the RAW tile elevation with no standing boost while terrain quads
       key the bilinear tile center (TileRenderer.cs:134): a brick's
       sub-cell center sits up to ±24 px from the tile center, and at
       the yaws where that offset lands on the far side of the view axis
       the owning tile quad keys nearer and paints over the build.
       Confirmed empirically before speccing (new
       `Camera.SetOrbit(yaw, pitch?)` + `DSR_TEST_YAW`/`DSR_TEST_PITCH`
       exact-angle staging hooks — the orbit keys can't land an exact
       angle): brick sub(1,1) on a flat pad, yaw 45° = fully buried
       (0 palette px at its predicted screen box), yaw 225° = fully
       visible (1877 px); corner sub-cells bury half-way (the box pokes
       above the tile's screen top edge). Prop-style structures buried
       on SLOPED tiles at every yaw (raw elevation vs the stitched
       bilinear center gap, measured −7 px), and ground fires keyed at
       elevation 0 — below every land tile (−66 px). FIX: the entity law
       the player/monsters already follow, extended to builds —
       `Game.StructureSortKey` / `Game.FireSortKey` key at
       `SortElevation` (the tile's highest corner) composed with
       RenderBase, plus `StandingBoost()` (≥ any in-tile positional
       deficit); `TileRenderer.TileSortKey` extracted so the tests read
       the identical terrain formula. Genuine occlusion survives: a
       raised tile genuinely in front wins on its elevation term
       (pinned by test across the sin ≥ 0.5 half-sweep; the lateral
       sliver outside that is the entity precedent's own trade — the
       player keys with the same boost today). Pixel-verified pre/post:
       bury yaw 45° — PRE 0 px (absent), POST 1865 px at the predicted
       position; mirror yaw 225° — identical pre/post (1877 px, same
       bbox), a clean no-op where the order already favored the brick.
       Suite 638 → 643 (5 contracts: 3 RED with exact predicted
       magnitudes — −11.31 px = 8·√2 offset, −7 stitch gap, −66
       elevation-0 gap — plus 2 both-states pins).
     - **Vertical building, slice 11 — monster-vs-structure wall damage
       (the parked "blocked raiders chip walls" follow-up, un-parked by
       the user 2026-10-12: "yes, we can move to your parked
       objectives"). DONE 2026-10-12** (spec
       2026-10-12-monster-wall-damage-design.md; RED 95bb0bb / GREEN
       4603bb2): walls buy time, not invincibility. MoveToward/MoveAway
       now report any-axis blocking (the GroundedSlide applied-vs-
       desired delta); Monster.WallFrustration accumulates on chase/flee
       blocks ONLY (wander never calls the accumulator — a wandering
       wolf grinds past); at Constants.MonsterWallFrustrationSeconds
       (4 s) the monster attacks the nearest BUILT, ACTIVE block
       structure within its AttackRange — damage = its Attack stat at
       its own shared melee cadence (wall hits and target hits
       alternate honestly), so a wolf needs ~10 s per brick (hp 20)
       and a 4-raider party ~2.5 s. BuildingSystem.DamageStructure
       chips Health and removes through the slice-9 LIVE occupancy
       recompute at zero — the tile is pathable the moment the wall
       dies, the destruction notifies the player per piece
       ("Your Dirt Brick was destroyed!", the no-silent-losses rule,
       no refund), and frustration resets so the chase resumes through
       the gap (the beeline takes over; workers re-path through the
       freed tile). User decisions 2026-10-12 (all recommended
       defaults): 4 s frustration timer, monster Attack at own
       cadence, notify-no-refund, chase/flee-driven only. Verified by
       state contracts (no render changes): a frustrated raider breaks
       a fence in ~10 s, frees the tile live, notifies, and resumes the
       chase (WorldX > 280); below-threshold and wandering pins stay
       green; the brick band chips at cadence; DamageStructure unit
       (partial chip stands, lethal chip removes + frees, neighbours
       untouched). Suite 643 → 648.
    - **Progression accounting (user playtest finding, 2026-10-09).** The
      fishing rod is entry-tier by design but hangs on a deadlock:
       rod → planks → `goblin_diplomacy` (goblin CHIEF's quest, not
       Shaman Kree's) → intelligence level 5 (388 XP) + commerce 3 +
       persuasion 5. Intelligence XP exists ONLY as quest rewards
       (`QuestSystem.cs:214` — no action/craft/activity trains it);
       the whole catalog awards ~470 int XP, with goblin_truce's 60
       circular behind diplomacy itself. Net: reaching the rod's gate
       needs nearly every faction's quest line first. Related UI
       finds: `InteractSystem.cs:63-70` — a leader with listed quests
       NEVER opens the diplomacy panel via E (L-key is the only path
       to negotiation); the quest panel lists unmeetable quests with
       only a one-line status (the "incomplete menu" report); quest
       ownership is undiscoverable (user attributed the chief's quest
       to Kree). Candidate remedies when the slice is drafted:
       un-gate planks for entry-tier tools vs. add a grindable
       intelligence XP source vs. both; plus the leader-panel fork
       fix. A progression-graph audit TOOL (recipe → quest → stat →
       XP-source reachability) would prevent this class permanently —
       natural extension of the cross-skill audit item.
     - **Cross-skill audit** — **done 2026-10-09 (two slices).** The audit
       walked every XP entry point and gate chain in the tree and fixed
       what it found, test-first. Slice A (RED `05f9017` / GREEN
       `29cb967`): `GatherSkillFor` now mirrors the player's tool switch
       exactly — workers chopping the 8 axe-tree species train
       woodcutting (they silently trained MINING since the per-recruit
       skills slice; the wood-yield branch was dead code matching no
       real yield id), bucket water trains foraging, and the colony
       node-gate consequently reads the skill the node actually trains.
       Slice B (RED `51b88f7` / GREEN `4b56cdb`): the known rod→planks
       deadlock dissolved — `quest_unlock` turned out to be DISPLAY-ONLY
       (parsed, rendered as "Quest: …", enforced nowhere), so the fix was
       honesty both ways: planks is day-1 craftable (flag removed from
       the def and from goblin_diplomacy's rewards, which keep gear/
       trophy/gold), while the remaining quest-locked recipes now
       actually refuse in `Craft` + the panel until claimed (worker
       auto-production stays ungated, stock convention). first_flame's
       tree_sap got a crafting-1 recipe (`whittle_sap`, 1 oak_logs → 1
       sap — logs buyable at the forest merchant, so axe-less starts
       finish the tutorial quest), herb_gathering (defined, offered by
       NOBODY) attached to Hemlock after timber_collection, and
       `ProgressionReachabilityTests` now walks the whole item-source
       graph (node yield + loot + trade + recipe closure) and the quest
       prerequisite DAG every suite run — the backlog's
       "progression-graph audit tool", landed as a test. Specs:
       `2026-10-09-cross-skill-audit-xp-routing-design.md`,
       `2026-10-09-cross-skill-audit-deadlocks-design.md`.
       Pillars held: RS (XP lands in the skill whose grind you're doing —
       player and worker agree), DS (the tutorial fire is craftable from
       a fresh start with no axe), MC (the panel's quest flag and the
       craft system's gate are the same fact now).
       Audit findings NOT yet fixed (parked, unapproved in the clarify
       round): raw `AddXp` never recomputes Level (construction/
       firemaking/cave-mining levels frozen mid-session; acute for
       player construction, whose only in-session source is raw) and the
       grass_rope recipe's duplicate xp_reward keys. FIXED since, in the
       NPC-quest breadth slice: the "combat" ghost skill (3 quests
       awarded XP to a skill that doesn't exist — retargeted to `attack`)
       and the leader-panel E-fork (leaders with quests could never open
       diplomacy via E — replaced by the multi-role NPC role menu).
     - **NPC-quest breadth** — **done 2026-10-10 (RED `17b4d62` /
       GREEN `51be69a`).** quests.json 18 → 23 and npcs.json 17 → 18.
       The plains hub opens: new `quest_giver_plains_1` ("Hayward the
       Reaper", forest_villagers — their territory already includes
       plains) carries a chain plains_harvest → boar_hunt →
       plains_provender (wheat → boars → delivering the hunt to Joss at
       the plains camp, which also gives the plains merchant a reason to
       exist). elder_mara and stone_guardian each get their FIRST quest
       (herbal_remedy chained off timber_collection; guardians_request
       off mountain_pass) — both are faction leaders, so they now carry
       the multi-role menu. `EveryQuest_IsOfferedBySomeNpc` and
       `EveryQuestSkillReward_TargetsARegisteredSkill` pin the two laws
       this slice depends on (every quest has a giver; every XP reward
       names a real skill). Both follow-ups have since closed:
       coastal/swamp side quests (2026-10-11 — the entry below), and
       panels-opened-from-the-menu get no return-to-menu (2026-10-11 —
       the hub-tab return slice).
       Pillars held: RS (a chain that walks you through plains
       foraging → combat → cooking), DS (wheat, boars and hunger supply
       the verbs), MC (pure DATA on the 7 existing condition hooks — no
       engine work; the one code change is UI routing).
     - **Coastal/swamp side quests — done 2026-10-11 (RED `75f690c` /
       GREEN `9a2ccb6`).** quests.json 23 → 29, npcs.json 18 → 19.
       Maris (`merchant_coastal_1`) becomes the first MERCHANT
       quest giver: her 3-link chain driftwood_scour → salt_run →
       crab_cove (driftwood → salt → crab-kill + meat delivery + a
       trade with her; crabs are non-hostile, so the hunt is your
       aggression — RS crab-clubbing). New swamp merchant Fenwick the
       Bog Trader (`merchant_swamp_1`, swamp_factions, near Kree)
       carries reed_cut → peat_dig → croc_bog AND gives swamp its
       first shop — the five swamp trade entries were dead data (no
       merchant def claimed biome swamp). Two load-bearing infra
       fixes found by grounding rode along: (1) merchant stock was a
       per-seed lottery — npc.Biome came from the def's world tile and
       the game seed is randomized (on seed 42 the forest merchant
       stood on coastal and sold coastal stock); all five merchant
       defs now carry an authored `biome` key that all three Biome
       writers prefer (NpcDef.Biome + FromDef copy +
       LoadFromRegistry/RestoreSnapshot/SpawnMerchant). (2)
       DepartMerchants removed ALL non-recruited merchants daily at
       10:00 — stationary shopkeepers like Maris vanished mid-session;
       the sweep now removes only the visitors MerchantVisitSystem
       itself spawned. Suite 517 → 531.
       Spec:
       `2026-10-11-coastal-swamp-side-quests-design.md`.
       Pillars held: RS (each chain teaches its biome's gather →
       hunt → trade loop; a merchant can hold a quest board like any
       RS shopkeeper), DS (tide-washed driftwood, salt deposits,
       bog peat and non-hostile crabs supply the verbs; the crab
       hunt is your choice), MC (both lines are pure DATA on the 7
       condition hooks; the code changes are the two infra
       honesty fixes the content needs to function).
     - **Unlock-as-event — done 2026-10-11 (RED `d01ce2d` / GREEN
       `d625fd5`).** Quest recipe unlocks fire the tick objectives
       complete (`QuestSystem.Tick` gains the inventory; HashSet =
       fire-once; gold "Recipe learned" only for newly gated ids);
       the three quest_unlock gate sites are now recipe-id keyed (the
       quest-id keying was dead enforcement — writer wrote recipe ids);
       gem_mining's dead `craft_bone_powder` → `grind_bone`; completed
       quests + both unlock sets persist across saves. Suite 531 → 540
       (539 green / 1 pre-existing skip). Spec:
       `2026-10-11-unlock-as-event-design.md`.
     - **Fire venue / firemaking narrowing — done 2026-10-10 (RED
       `20f0519` / GREEN `7d86810`).** The three incoherent fire
       methods are now one skill-owned venue. Every path feeds its
       owning skill: `SkillManager.AddXp` ROOT-FIXED to recompute Level
       + pay points (the parked frozen-level bug died here —
       firemaking/construction/mine XP all level up now;
       AddXpWithNotification is an alias), fuel_table.json WIRED
       (burn seconds = per-unit × qty summed, unlisted fuel keeps the
       flat 30s/unit, duration sub-stat stacks on top), placement
       auto-lights hearths + trains construction 10 (worker site
       completion lights too), new `firepit` structure (stone 6 +
       stick 2, construction 3, `light_seconds` 240 vs campfire 90 —
       the construction firepit the user specced: pay stones once,
       burn 4× longer), F relights a burned-out hearth from inventory
       fuel → firemaking XP (hearth def's flat seconds, not the fuel
       table — the hearth is the multiplier), and E near a lit fire or
       heat station opens the new `FireMenuPanel` (GameState.FireMenu:
       every `requires_campfire` + cooking recipe, single-sourced
       `IsFireRecipe`, plus rest-until-dawn sharing the TrySleep gate;
       the crafting panel drops those recipes, and
       `GetAvailableStructureIds` is now PROXIMITY-filtered — a
       station enables crafts only within 3 tiles, owning one
       anywhere is not a global unlock). The dominated "Build
       Campfire" craft recipe (3 oak_logs + 5 stone → inert item,
       trained crafting) is DELETED — resolving the parked inert-item
       finding by the "drop the recipe" fix; the items.json campfire
       def stays for old-save tolerance. Spec:
       `2026-10-10-firemaking-narrowing-fire-venue-design.md`.
       Follow-ups left open: `WarmthRadius` still has no consumer,
       `skill_affects_yield` still dead data (both noted in the
       spec's non-goals), and heat-station menus of their own.
       SMOKETEST DEBT CLEARED 2026-10-11 (RED `bd06fb8` / GREEN
       `07b97c8`): `DSR_TEST_FIREMENU=1` stages the player on the
       first clear dry tile (`Game.FindFireMenuStageTile` — no cave
       collar in its 3x3, no NPC within 128px, no live monster within
       96px, so scripted E reaches the fire menu) and grants 2
       oak_logs; `DSR_TEST_KEYS="F,E"` then lights a real ground fire
       and opens the real panel. Pixel pass: chrome border bbox within
       1px of the geometry predicted from ContentW/H + Pad + TitleH,
       plate count 33x baseline, panel-rect mean diff 97.8 —
       FireMenuPanel is rendered and layout-verified. Spec:
       `2026-10-11-fire-menu-smoketest-design.md`.
     - **grass_rope "duplicate xp_reward key" — PHANTOM, resolved
       2026-10-11 (docs-only).** Full-history audit: a
       dup-detecting JSON parse (object_pairs_hook) of every commit
       that ever touched recipes.json, plus items/quests/npcs json,
       shows zero duplicate keys or ids at any committed state — the
       finding likely came from a transient parallel-session
       working-tree artifact that never committed. The grass_rope
       ITEM is alive and load-bearing: Twist Rope recipe output
       (3 fibers, crafting 1, +4 xp) and the input both rod recipes
       consume (FishingTests pins both). No data change; the
       dev-map's earlier "parked" mention above is closed by this
       entry.
     - **Hub-tab return — done 2026-10-11 (RED `ad29921` / GREEN
       `171c721`).** The quest slice's open follow-up closed: Esc
       from a role panel the NPC hub launched (OpenNpcHubTab
       handoff) now returns to the hub menu with the session and
       the active tab restored, instead of dumping the player into
       Playing. The Game captures `HubReturnNpc` + `HubReturnTab`
       before the SetState wipe (the dashboard-return idiom, third
       use), `TryReturnToNpcHub` restores them (transition first,
       populate after), and SetState clears both on any world
       transition (Playing/Paused/Settings — same rule as
       `DashboardReturnAvailable`). Panels opened directly
       (single-role E, T/U/K/L hotkeys, dashboard tabs) still
       close straight to Playing. Spec:
       `2026-10-11-hub-tab-return-design.md`.
     - **Crafted campfire item is inert (found 2026-10-10, verified in
       the tree).** `items.json` marks the campfire item
       `is_structure: true` and the crafting recipe
       (`Skills/Construction/data/recipes.json`, "campfire": 3 oak_logs
       + 5 stone) outputs it — but NOTHING reads `Item.IsStructure`
       (grep finds only the declaration in `Data/Item.cs`). Placement
       flows solely from the building panel (`BuildingPanel.BuildCallback`
       → `Game.StartPlacement` → `BuildingSystem.PlaceStructure`), which
       spends the structures.json def's own materials (2 stick + 2
       stone), never the crafted item. So the crafted campfire can
       neither be placed nor satisfy `requires_campfire`: the cooking
       gate reads active placed structures plus a virtual "campfire"
       within 3 tiles of a LIT fire (`Game.GetAvailableStructureIds`,
       Game.cs:1979). RESOLVED 2026-10-10 in the fire-venue slice by
       the "drop the recipe" fix (see item above); the item def stays
       for old-save tolerance and remains inert by design — placement
       comes from the building panel.
     - **Structure footprint reform — every structure on the 4x4 grid
      (user ideation, 2026-10-12, to design when picked up).** User's
      words: "for many other structures, the per tile grid logic
      doesnt exist yet.. this leaves tons of very exciting
      implications though, such as redesigning machines, etc.. since
      our tiles each consist of 4x4 grids, id like to reform
      structures to have variable designs based within the 4x4
      possibility space.. for example, for a campfire, lets explore a
      2x2 structure with fire animations? add collision.. perhaps
      even damage upon collision or while standing on... this gives us
      opportunities to have variants of campfires, like lined pits
      that could cost more, but also not cause damage upon collision,
      etc... another feature id like to add to campfires is a burn
      rate, etc.. which can play meaningfully into the variants
      framework." Grounded seams (verified in the tree 2026-10-12):
      the sub-cell machinery exists but is BLOCK-DEF-ONLY —
      `Structure.SubX/SubY/Layer` (slice 7) with
      `PlaceStructure(id, tx, ty, subX, subY, …)` refusing non-brick
      defs ("Pick a spot on the brick grid."), `SubHeightAt`/swept
      `CanStep`/`GroundedSlide`/`BuildBoxVoxel(explicit rect)` all
      ready to serve wider footprints. A 2x2 campfire needs: a
      def-level footprint (sub-cell w×h), per-sub-cell occupancy truth
      for `CanStand`/pathing (today tile-level for non-blocks),
      collision via the existing grounded-mover gates reading the
      footprint, standing-on damage as a per-tick sub-cell check
      (player + entities; the `HasHostileNear` radius-check precedent
      and fire proximity (`GetAvailableStructureIds`, 3-tile reach)
      are the patterns), fire ANIMATION via the existing
      `RenderFire`/`FireInstance` machinery, and a per-def burn rate
      feeding the firemaking fuel economy (light_seconds + fuel_table
      seams). Variants = sibling defs (campfire vs lined pit) with
      different materials/footprints/damage — the def registry and
      materials already vary cost; adding defs shifts the NpcDataTests
      count pins (the data-count law). Open questions for the spec:
      footprint field shape, build-mode UI for non-brick grid
      placement (the 4x4 ghost today is bricks-only), whether the
      craft-then-place economy carries over, damage cadence while
      standing on a lit fire, and how variants unlock (construction
      level gates are the precedent).
    - **Skills-arc tails** (still parked): workers reading their own
       sub-stats (RecruitNpc.Skills inherits catalogs; gather/production
       math still level-only) and per-stat tooltips in the panel.
     - The fusion is **not bound to the inspiration titles**: entirely
       new logic of our own is welcome and encouraged.

## Distributable track

The user's stated ambition is a real distributable of this fusion, not
only more features. Phase 5 is that track:

- Persistence polish — save and restore the settlement completely, old
  saves still load.
- Blocked-work feedback — missing materials, full storage, unreachable
  jobs, and danger are visible.
- Simulation cadence and rendering — the colony stays readable as the
  population grows.

All three landed 2026-10-07 (see Ordered next work item 5): full
round-trip proven by test, danger feedback in the dispatch, scrollable
colonist list with population counter. The colony-fusion feature arc is
feature-complete; what remains before a distributable is packaging and
play QA, not features.

Feature work (items 1–4) and this track stay distinct. Phase 5 does not
wait for parked arcs. Parked cave atmosphere and sprite polish are not
prerequisites for a first distributable build.

## Where the detail lives

- Colony checkboxes, progress log, decisions:
  `docs/superpowers/2026-10-04-colony-fusion-roadmap.md`.
- Pillars and balance rule: `## Tri-fusion design law` in that same doc.
- Caves: memory topic `dontstarve-runescape-cave-development.md`.
- Gait: memory topic `gait-for-entities-plan.md`.
- Deferred cosmetics and click-to-move:
  `dontstarve-runescape-deferred-ideas.md`.
