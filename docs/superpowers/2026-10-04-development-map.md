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
       LEVELING tool/method ships with it. Known seams: Player.TryJump
       (`Player.cs:206`) and UpdateJump's arc
       (JumpVisualOffset/JumpProgress) are visual-only — landing
       checks are new; Tile.CornerElevations is the
       min-corner-stitch convention (TileStitchingTests pins it);
       Structure placement rides BuildingSystem.PlaceStructure's
       biome/skill/material gates; structures occupy tiles but have no
       vertical extent yet. Likely split: (1) block defs + placement +
       flat-pad gate, (2) jump landing/standing on blocks, (3) stacking
       + connection, (4) terrain leveling.
     - **Progression accounting (user playtest finding, 2026-10-09).**
       The fishing rod is entry-tier by design but hangs on a deadlock:
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
       names a real skill). Follow-ups left open: coastal/swamp side
       quests, and a panels-opened-from-the-menu get no return-to-menu
       (E again reopens the hub).
       Pillars held: RS (a chain that walks you through plains
       foraging → combat → cooking), DS (wheat, boars and hunger supply
       the verbs), MC (pure DATA on the 7 existing condition hooks — no
       engine work; the one code change is UI routing).
     - **Unlock-as-event (banked idea, 2026-10-10):** a quest-bound
       craftable needn't be a completion REWARD — unlock it mid-quest as
       a story beat (learn the recipe from the hermit when you REACH him,
       not when you turn the quest in). Verified shape today:
       quest_unlock recipes hide until the named VALUE is in
       `Player.UnlockedRecipes`; the ONLY writer is
       `QuestSystem.Claim` (QuestSystem.cs:230) adding the entries of
       `quest.recipe_unlocks` (recipe ids), i.e. recipe-id keyed and
       already decoupled from quest ids. Claim also handles
       `gear_unlocks` → UnlockedGear (:231). So an event-unlock is CHEAP:
       the storage is already right; only the write needs a new trigger
       fired at step completion (or on prerequisite chaining) instead of
       at claim. Claim then merely surfaces the already-known provision.
       The display layer needs no fix-up — both Panels2.cs:2417 and
       Craft consult UnlockedRecipes live. Banked for a later slice;
       audit-B's enforced quest_unlock is the gate this reads from.
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
       Game.cs:1979). Two honest fixes — route an `is_structure` craft
       output into placement mode on craft, or drop the recipe. Parked
       for a gameplay call.
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
