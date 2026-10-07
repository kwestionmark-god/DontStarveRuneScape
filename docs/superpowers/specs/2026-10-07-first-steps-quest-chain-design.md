# More Quests — First Steps chain — design

Date: 2026-10-07
Status: implemented (GREEN 364/365; six chain quests live in quests.json
+ forest giver wiring)
Slice: post-roadmap frontier, item 4 (dev-map "Ordered next work" item 6)

## Design law

RS pillar: quests are the guided grind — each new quest teaches one
skill through its objective and pays in that skill's XP, so the chain
walks a new survivor up the early curve the way OSRS's tutorial land
walks a fresh account.
DS pillar: the chain is survival-first — fire, food, and a roof over
gathered wood before any commerce; the nights stay dangerous and the
objectives are what a player would need anyway.
MC pillar: zero new systems — the quest machinery is fully data-driven
(condition hooks all live: collect/deliver/craft/kill/trade/negotiate/
visit), so this slice is content plus quest-giver wiring, reusing every
existing surface (QuestPanel, snapshot persistence, prerequisite gates).

## Goal

Add an early-game quest chain (five quests, plus one bridging quest into
the mid game) to quests.json, focused on the first hour: fire, food,
crafting, first kill, and founding the settlement. All objectives use
existing condition types against existing items/recipes/monsters/biomes.
The forest quest giver (quest_giver_forest_1) offers the chain in
prerequisite order, so a new character meets a coherent tutorial arc
that ends pointing at the colony feature and the merchant economy.

Chain (forest_villagers, giver quest_giver_forest_1):

1. **first_flame** (no prereqs, persuasion 0) — craft 1 candle? No:
   craft_item candle is tier-2. First flame = collect 3 stick +
   visit forest. Teaches gathering + the firemaking toolchain.
   Reward: firemaking XP 40, 2 torch, intelligence 30.
   — Simplified: conditions collect_item stick ×3 + collect_item
   tree_sap ×1 (the campfire recipe inputs), reward torches so the
   player can light a fire before owning the campfire recipe.
2. **hearth_and_home** (prereq first_flame) — craft_item campfire ×1
   via... campfire is a structure, not a recipe output. Use
   collect_item: birch_logs ×4 + stone ×3 — the actual campfire build
   cost — and keep the fiction "gather the materials for a campfire".
   Reward: construction XP 40, healing_herbs ×2.
3. **full_belly** (prereq hearth_and_home) — collect_item cooked_fish
   ×2 + collect_item berries ×5. Teaches cooking + foraging; reward
   cooking XP 50, raw_meat ×2.
4. **first_blood** (prereq full_belly) — kill_monster wolf ×1.
   Reward: attack XP 60, wolf_pelt ×1, gold 15.
5. **settling_in** (prereq first_blood) — visit_location plains ×1 +
   collect_item gold ×10 (the commerce seed). Points the player at
   founding the colony and meeting merchants. Reward: intelligence
   XP 120, persuasion-flavored intelligence 40.
6. **bridge: forest_friends** (prereq settling_in) —
   negotiate_faction forest_villagers ×1 + trade_at_location
   merchant... trade hook records NPC id. Use negotiate only, target
   forest_villagers. Reward: recipe unlock (craft_candle),
   intelligence 80.

## Non-goals

- New condition types, UI, or systems — data only, plus npcs.json
  quest-list wiring.
- Balancing the full quest roster beyond the new chain.
- Quest-giver placement changes (the forest giver already spawns).

## Ground — verified in the tree

- QuestSystem hooks all fire live: NotifyCollect (Game.cs:834,
  TradeSystem.cs:115), NotifyCraft (Bootstrap.cs:265 →
  CraftingSystem.OnCrafted), NotifyKill (CombatSystem.cs:280),
  NotifyTrade (TradeSystem.cs:114), RecordNegotiation (NPCFlows.cs:240),
  visit_location via TickBiomeVisit polling (QuestSystem.cs:50).
- quests.json has 12 quests; quest_giver_forest_1 currently offers
  only timber_collection (npcs.json:62-64). The panel lists whatever
  AvailableQuests the NPC carries (NpcPanelTests.QuestPanel_AcceptAndClaim).
- Prerequisites gate acceptance (QuestSystem prerequisite check;
  NpcPanelTests.QuestPanel_Accept_GatesPrerequisites).
- Real targets verified: items stick/tree_sap/birch_logs/stone/
  cooked_fish/berries/torch/healing_herbs/raw_meat/wolf_pelt/gold/
  candle; monster wolf; biome plains; faction forest_villagers;
  recipe craft_candle; reward recipe planks exists.
- QuestSnapshot round-trips ActiveQuest progress (QuestSystem.cs:250).
- sprite_key convention: "quest/<slug>" — existing quests use
  quest/timber, quest/herbs; new quests reuse the nearest existing art
  key (campfire, wolf, etc.) — new defs need a sprite_key, reuse
  existing sprites when no new art (skill rule).

## Design

Pure data: six QuestDef entries appended to quests.json, and the forest
quest giver's available_quest_ids extended with the six ids (keeping
timber_collection — it becomes an optional side quest). Difficulty
curve: persuasion 0 → 2 across the chain; acceptance thresholds stay
low (0.3–0.5) so a fresh character can climb it. Every quest is
non-repeatable with 1–2 conditions. Rewards scale 30→120 intelligence
XP plus the thematic skill XP.

sprite_key assignments: first_flame→"quest/timber" family absent — use
"items/torch"; hearth_and_home→"items/campfire"... quest sprite keys
load through GetSpriteKey with negative cache for misses, so a missing
sprite falls back harmlessly; keep the "quest/..." convention with
nearest-fit names (quest/flame, quest/hearth, quest/meal, quest/blood,
quest/settle, quest/friends) — misses are cached-free fallbacks, no
crash, same as any not-yet-drawn quest art.

## Testing plan

Extend NpcPanelTests-style coverage in a new QuestChainTests.cs:

1. **Chain loads and references resolve** — registry loads all 18
   quests; every new quest's conditions/rewards reference ids that
   exist (item ids in items.json, monster in monsters.json, biome in
   biomes.json, faction in factions.json, recipe in recipes.json) —
   a self-validating data-integrity test over the real JSON files.
2. **Chain order is enforced by prerequisites** — each quest lists the
   previous; accepting step N+1 without completing step N fails via
   the existing gate.
3. **Forest giver offers the full chain** —
   QuestGiverNpc.FromDef(registry def) → AvailableQuests contains all
   six new ids + timber_collection.
4. **A fresh character can accept step 1** — AcceptQuest with a
   persuasion-1, standing-0.4 player succeeds for first_flame.
5. **Walk-through** — accept first_flame, NotifyCollect(stick, 3) +
   NotifyCollect(tree_sap, 1) → ConditionsMet true; Claim delivers
   torch ×2 and the XP. (Borrow the NpcPanelTests harness shape.)

## Open questions

Answered with defaults per standing delegation:

- Chain length 5 + 1 bridge (six data-only quests) — one focused
  tutorial arc, not a content dump; the mid/late game keeps the
  existing 12-quest roster.
- teach-then-test: each quest's objective is something the player
  needs anyway (fire inputs, campfire inputs, food, first kill,
  colony/commerce seed) — no fetch-quest filler.
- No new art: sprite keys follow the quest/ convention; misses fall
  back through the existing negative-cache path.
