# Coastal/swamp side quests — design

- **Date:** 2026-10-11
- **Status:** Drafted (awaiting RED)
- **Slice line:** "Coastal/swamp side quests" (fire-venue handoff item 6,
  quest-breadth follow-up; user-approved next on the menu).
- **Design law:** RS = the quest web teaches the world's skills and
  factions; DS = hunger, beasts, and weather supply the verbs;
  MC = everything new is DATA on the existing condition hooks — no new
  engine systems.

## Goal

1. **Coastal side line on Maris (merchant-with-quests, first of its
   kind):** `merchant_coastal_1` "Coastal Trader Maris" gains a 3-quest
   chain, pure data — quests.json entries with `giver_npc_type:
   "merchant"`. `AcceptQuest` (QuestSystem.cs:69) already type-checks
   the def against the npc, `Npc.FromDef` (NPC.cs:90) loads quest ids for
   every type, and `NpcHubPanel.RolesFor` (Panels.cs:308) already yields
   `[quests, trade]` for a merchant with quests — E opens the hub, no
   code changes for the routing itself.
2. **Swamp merchant Fenwick + side line:** new `merchant_swamp_1`
   "Fenwick the Bog Trader" (`swamp_factions`, near Kree at (252,248)),
   carrying a 3-quest chain. This also gives swamp its FIRST shop: the
   5 swamp trade entries (swamp_toxic_reeds, swamp_peat_block,
   swamp_poison_gland, swamp_crocodile_tooth, swamp_poison_antidote)
   are dead data today — no merchant def has biome "swamp", so nobody
   can ever spawn with swamp stock.
3. **Chains mirror the plains arc** (entry gather on a lvl-1 node →
   skilled gather or hunt → deliver + trade with the shop):
   - `driftwood_scour` — collect 6 driftwood (coastal lvl-1 node,
     no tool). thr 0.3, minP 0, minI 1. XP 60 + foraging 30;
     reward shells×3.
   - `salt_run` — collect 4 salt (coastal salt_deposit, foraging 5 —
     the node gate; grinding foraging is the prep). thr 0.35,
     minP 0, minI 1. XP 70 + foraging 40; reward gold×10.
   - `crab_cove` — kill 2 crabs (non-hostile — you pick the fight,
     DS-honest RS crab-clubbing) + deliver 2 crab_meat + trade with
     Maris once. prereq salt_run. thr 0.4, minP 1, minI 2.
     XP 90 + cooking 30; reward gold×15.
   - `reed_cut` — collect 6 reed (swamp reed_bed, lvl-1 no-tool).
     thr 0.35, minP 1, minI 1. XP 60 + foraging 30; reward
     poison_antidote×1.
   - `peat_dig` — collect 4 peat (swamp peat_mound, foraging 5).
     thr 0.4, minP 1, minI 1. XP 70 + foraging 40; reward gold×10.
   - `croc_bog` — kill 2 crocodiles + deliver 2 crocodile_tooth +
     trade with Fenwick once. prereq peat_dig. thr 0.45, minP 1,
     minI 2. XP 100 + attack 50; reward gold×20.
4. **Merchant-shop infra fixes (load-bearing, found by grounding):**
   - **Authored biome on merchant defs.** Today `npc.Biome` comes from
     the def's world tile (`NPCSystem.LoadFromRegistry` :29, snapshot
     restore :164, `MerchantVisitSystem.SpawnMerchant` :212), and
     `StartNewGame` randomizes the seed — on seed 42 the forest merchant
     stands on coastal (sells coastal stock), Maris on desert, Joss on
     mountains. Stock is a seed lottery. Fix: optional `biome` key on
     merchant defs (data), preferred by all three Biome writers; def
     coords still place the body.
   - **Visiting merchants must not despawn stationary shopkeepers.**
     `MerchantVisitSystem.DepartMerchants` (:221) removes ALL
     non-recruited merchants daily at 10:00 — Maris and Fenwick would
     vanish from the world mid-session on day one. Fix: only the
     system's own spawned visitors depart (track spawned ids;
     `Game.NPCSystem.NPCs` merchants not in the visitor set stay).
5. Pure side content per the clarify: XP + gold + items only, no
   recipe/gear unlocks.

## Non-goals

- No new engine systems, no new condition types (the 7 hooks suffice).
- No new factions (swamp_factions exists; Fenwick is its merchant).
- No repeatable quests (Claim's non-repeatable path is the proven one).
- No trade economy changes (prices/stock stay as trade_items.json has
  them; Maris's coastal stock and Fenwick's swamp stock are the
  existing entries).
- No change to Kree's existing line (swamp_crossing, poison_antidote
  stay as they are — the side line is Fenwick's, not Kree's).
- No trade/sell NotifyTrade extension (sells don't count toward
  trade_at_location, matching the sea_trade precedent).

## Ground — verified in the tree

- `AcceptQuest` gates: giver's available list (:67), giver type
  match (:69), prerequisites (:76), standing (:82), int level
  (:88), commerce/persuasion (:90-93). Fresh standing = 0.5
  (DefaultStanding), fresh commerce/persuasion sub-stats = 1 — all six
  quests above are fresh-acceptable.
- `Claim` consumes deliver_item targets from inventory (:206-210) —
  that IS the delivery.
- Progress hooks: NotifyCollect on inventory add, NotifyCraft,
  NotifyKill (fired by CombatSystem on player kills), NotifyTrade
  (fired by TradeSystem.ExecuteBuy only), RecordNegotiation,
  visit_location by biome in Tick.
- `PlayerAttack` (CombatSystem.cs:195) targets the nearest ALIVE
  monster with no hostility gate — crabs (is_hostile false) are
  killable; `NotifyKill` fires on the kill path (:286).
- `RolesFor` (Panels.cs:308): quests iff AvailableQuests.Count > 0,
  trade iff MerchantNpc, so Maris + quests → `[quests, trade]` hub;
  Fenwick → `[quests, trade]` too. `OpenNpcHubTab` (Game.cs:415)
  routes both tabs to the real panels (QuestPanel.SetPlayer +
  OpenSession / TradePanel.OpenSession), transition-first.
- `GetTradeItemsForMerchant` (TradeSystem.cs:35) matches
  `merchant.Biome` — the authored-biome fix makes Maris/coastal and
  Fenwick/swamp deterministic regardless of seed.
- **Probe evidence (seed-42 world, deleted probe test):** every
  merchant def tile lands on a mismatched biome: merchant_forest_1 →
  coastal, merchant_coastal_1 → desert, merchant_plains_1 →
  mountains. The world is proc-gen from the game seed and
  `StartNewGame` (Game.cs:528) sets `Seed = Random.Shared.Next(...)`
  — every new game re-rolls every merchant's stock.
- **DepartMerchants evidence** (MerchantVisitSystem.cs:221-236 +
  UpdatePlaying :881): daily 10:00 sweep removes every non-recruited
  merchant NPC; the visit tests never stage stationary merchants so
  the sweep looked fine.
- NpcDataTests pins to bump in the SAME commit: Npcs 18→19, merchant
  count 4→5 (GetNpcsOfType), quests 23→29, spawn points stay 14 (defs
  carry their own coords; the swamp def gets no spawn point —
  spawn_points are legacy flavor, none for quest_giver_plains_1
  either).
- Trade registry: 43 entries, 5 swamp entries — pinned counts
  unchanged (no new trade entries; Fenwick reads the existing ones).
- Fresh-reachability (ProgressionReachabilityTests): driftwood, salt,
  reed, peat are node yields (grindable); crab_meat and
  crocodile_tooth are loot; gold via any sellable. All 6 targets
  pass the walk's laws without changes.
- The int-gate DAG test adds only prerequisite edges the walk already
  closes (driftwood_scour/salt_run/reed_cut/peat_dig are
  prerequisite-free).
- EveryQuest_IsOfferedBySomeNpc + EveryQuestSkillReward_
  TargetsARegisteredSkill: satisfied by adding the ids to
  merchant_coastal_1 / merchant_swamp_1 available_quest_ids and
  targeting registered skills only (foraging, cooking, attack).

## Design

### Quests (all `skill_xp_rewards` target registered skills only)

Six new quest defs in quests.json, in the shapes above. Sprite keys
reuse the quest sprite catalog (quest/herbs, quest/sea_trade,
quest/first_blood, quest/desert_trade, quest/antidote, quest/
mountain_pass — inert display data). giver_npc_type "merchant" on
all six; giver_faction coastal_merchants (coastal line) and
swamp_factions (swamp line).

### npcs.json

- `merchant_coastal_1`: add `available_quest_ids: [driftwood_scour,
  salt_run, crab_cove]` + `biome: "coastal"`.
- `merchant_swamp_1` NEW: "Fenwick the Bog Trader", merchant,
  swamp_factions, (252,248) near Kree, sprite npc/merchant_male_0,
  3 dialogue lines, available_quest_ids [reed_cut, peat_dig,
  croc_bog], biome "swamp", starting_gold 250, price_modifier 0.9,
  hostility_level 0.4, health 20.
- Authored `biome` on the other three merchants (forest, plains,
  desert) so their stock is deterministic everywhere — same fix,
  four lines of data. Also quest_giver_mountains_1/stone_guardian?
  No — biome only matters for merchants (stock matching); leave
  non-merchants as they are.
- NpcDef gains `[JsonPropertyName("biome")] string Biome` (defaults
  empty = tile-derived, old saves/defs unchanged).

### Biome resolution (the infra fix)

`Npc.FromDef` copies `def.Biome` → `npc.Biome` when non-empty. All
three tile-writers (`LoadFromRegistry` :29, snapshot restore :164,
`SpawnMerchant` :212) prefer the authored biome: `npc.Biome =
AuthoredOrTileBiome(def, tile)`. Where a def has no authored biome
(the 19 existing non-merchant defs), behavior is byte-identical to
today (tile-derived). MerchantVisitSystem.SpawnMerchant keeps its
colony-tile fallback ONLY when the def has no authored biome — the
authored biome wins so a visiting coastal merchant sells coastal
stock even at a plains colony.

### DepartMerchants (the despawn fix)

Track visitor ids in a HashSet<string> `_spawnedVisitors` (added on
SpawnMerchant, cleared on departure). DepartMerchants removes only
merchants whose NpcId is in the set. The two existing visit tests
stay green (their merchants ARE visitors). New tests pin: a
stationary merchant (Maris def loaded via LoadFromRegistry) survives
the 10:00 sweep; the visitor departs as before.

### Testing plan

RED (one commit, all failing for the RIGHT reasons):
- NpcDataTests: 18→19 Npcs, 4→5 merchants, 23→29 quests; Maris
  def carries the 3 coastal ids; Fenwick def pinned (id, faction,
  biome "swamp", board ids, first id).
- Quest-data pins: all six quest defs with gates/conditions/rewards
  as specced; every new skill_xp_rewards key registered (the law
  test already iterates the registry — verify it stays green at RED).
- EveryQuest_IsOfferedBySomeNpc: the law test fails until both
  boards list the six ids — the RIGHT failure (orphaned quests).
- Merchant-stock determinism: seed-42 worldgen + LoadFromRegistry →
  Maris.Biome == "coastal", Fenwick.Biome == "swamp", forest
  merchant == "forest", plains merchant == "plains", desert
  merchant == "desert" (RED: tile-derived mismatched values).
- MerchantVisitSystem: stationary Maris survives the 10:00 sweep
  (RED: she's removed today); visitor departs as before.
- Chain flow: coastal chain end-to-end via QuestSystem directly
  (accept → NotifyCollect/NotifyKill/NotifyTrade → claim consumes
  crab_meat) — same shape as PlainsChain test.
- Hub: RolesFor(Maris-from-def) == [quests, trade] (RED: [trade]
  today); E on Maris opens the hub (RED: TradePanel today).

GREEN: quests.json (6 defs) + npcs.json (Maris board + biome keys,
Fenwick def) + NpcDef.Biome + FromDef copy + three writer fixes +
DepartMerchants visitor tracking.

Docs: spec status, dev-map entry (coastal/swamp closed + the two
infra findings), CHANGELOG.

### Open questions (resolved)

- Giver shape: Maris takes the coastal line (merchant-with-quests,
  first of its kind — user call); new swamp merchant Fenwick (user
  call); 3-link chains mirroring plains (user call).
- Rewards: pure side content, no unlocks (user call).
- Crabs: stay non-hostile — the hunt is your aggression (user call).
- Fenwick position: (252,248), 7 tiles from Kree's (245,245) —
  near the shaman without overlapping her patrol box, inside swamp
  territory.
- The authored-biome infra fix rides this slice (it must: the new
  content is broken without it); DepartMerchants rides too (Maris
  despawning breaks the coastal line's giver).

### Revision history

- 2026-10-11: drafted from clarify answers + live-tree grounding;
  seed-42 biome probe run (probe test deleted after evidence
  collected).
