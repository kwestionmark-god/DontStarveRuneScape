# Cross-skill audit — progression deadlocks + reachability tool (slice B) — design

Date: 2026-10-09
Status: Implemented (RED 51b88f7 / GREEN 4b56cdb)
Slice line: every entry-tier item a quest chain asks for is reachable by a
fresh character; quest-gated recipes enforce their gate; a reachability
test pins the whole class.

## Design law

A gate must be honest: a recipe that displays "Quest: X" refuses until X
is completed, and a quest condition that says "collect N <item>" has at
least one source (node, recipe, trade listing, loot, or starter pack)
reachable without the quest itself. Progression the player can't see
coming is a bug, not a secret.

## Goal (user-approved, clarify round 2026-10-09)

1. **Planks un-gate (user choice):** planks becomes craftable from day 1
   (crafting 1, 1 oak_logs → 3 planks). goblin_diplomacy keeps its other
   rewards (gear, trophy, XP). The `quest_unlock` flag leaves the planks
   def.
2. **first_flame reachable:** a new crafting-1 recipe taps tree_sap from
   oak_logs (1 log → 1 sap), so even axe-less starts can buy logs at the
   forest merchant (commerce 0, 5 gold) and craft the sap. The tutorial
   quest no longer stalls behind maple@woodcutting 20.
3. **herb_gathering offered:** attach the orphaned quest to Old Man
   Hemlock (quest_giver_forest_1) after timber_collection — its
   prerequisite chain already says that's where it belongs.
4. **quest_unlock enforcement:** the flag becomes real — `Craft()` and
   the panel's CanCraft refuse until the quest is completed. What the
   panel says and what the system does can no longer disagree (the
   user's playtest hit exactly that hole: the panel SAID "Quest:
   goblin_diplomacy" but planks crafted anyway... or the reverse read as
   a wall when it wasn't).
5. **Reachability audit test:** a data-driven test walks every quest
   condition → item source graph, and every `quest_unlock` recipe →
   quest-gate → int/commerce/persuasion reachability from a fresh
   character, failing with a named chain for anything unreachable. This
   is the backlog "progression-graph audit tool" as a test.

## Non-goals

- No new NPC-quest breadth (next arc's item 4).
- No economy rebalance beyond the approved un-gates: planks price,
  rod/bucket recipes, and trade stock stay as-is.
- The three unapproved mechanical items stay parked: "combat"→"attack"
  quest rewards, AddXp Level recompute, grass_rope duplicate key. (They
  were not picked in the clarify round; they remain open.)
- No leader-panel E-fork fix (InteractSystem.cs:63-70) — the L-key path
  works; panel UX is a separate slice.

## Ground — verified in the tree

- **quest_unlock is display-only today.** Parsed in Data/Recipe.cs:87,
  rendered as a flag line (UI/Panels2.cs:1048), and enforced NOWHERE:
  `CraftingSystem.Craft` (CraftingSystem.cs:25-105) checks skill,
  structure, campfire, ingredients — never QuestUnlock; the panel's
  `CanCraft` (Panels2.cs:1073-1089) mirrors Craft and skips it too;
  `Refresh` (Panels2.cs:946-955) lists every recipe unfiltered.
  `Player.UnlockedRecipes` (Player.cs:87) is write-only state —
  QuestSystem.Claim adds to it (QuestSystem.cs:229-230), nothing reads
  it. So the "planks deadlock" the playtest hit was the panel
  ADVERTISING a gate that didn't exist — the wall was the quest's own
  acceptance gates (commerce 3 + persuasion 5 + int 5), reached via the
  quest panel, not the recipe.
- **Planks chain:** planks def = Construction/recipes.json:4-22 (1
  oak_logs → 3, crafting 1, quest_unlock goblin_diplomacy);
  craft_fishing_rod + wood_bucket need planks×2 (Data/recipes.json:72-96,
  149-174); goblin_diplomacy's recipe_unlocks list is quests.json:76
  `["planks"]`; no trade listing sells planks (grep over trade_items:
  43 rows, none planks).
- **first_flame's tree_sap:** quest condition quests.json:444-448
  (collect tree_sap×1). tree_sap's only source is tap_maple_sap
  (Data/recipes.json:263-278, crafting 5) ← maple_logs ← maple_tree
  resources.json required_level 20 (4470 XP). No loot table drops it, no
  merchant sells it, no starter pack carries it.
- **Log availability for axe-less starts:** forest merchant sells
  oak_logs (trade_items forest_oak_log, buy 5, commerce_requirement 0,
  stock 8). Gold sources: quest item_rewards only (first_blood 15,
  settling_in 10...), plus selling items (berries sell 1, herbs 2...).
- **herb_gathering:** quests.json:35-63, prerequisite timber_collection
  (which Hemlock offers, NpcDataTests pins giver.AvailableQuestIds[0] ==
  "first_flame"). Offered by NOBODY (verified: npcs.json's only
  quest-listing NPCs are goblin_chief_grak, captain_rourke,
  quest_giver_forest_1, quest_giver_desert_1, djinn_elder_zara,
  shaman_kree, quest_giver_mountains_1 — none list herb_gathering).
- **Enforcement seam for item 4:** Craft's caller set is exactly two:
  the panel (Panels2.cs:913) and worker auto-production
  (RecruitmentSystem.cs:426). Craft takes SkillManager, not Player —
  but the panel has the Player in scope and the worker path must NOT
  be quest-gated (colony production isn't the player's unlock; the
  store-side convention is stock-visibility). So enforcement goes in
  Craft via an optional unlocked-recipes set parameter defaulting to
  null (= no gating), with the panel passing the player's set and the
  worker path passing nothing. Quest completion state (not just
  acceptance) must gate: `IsCompleted(questId)` lives on QuestSystem;
  the panel doesn't hold QuestSystem — but Player.UnlockedRecipes IS
  the post-claim state (populated by QuestSystem.Claim), so gating on
  `player.UnlockedRecipes.Contains(recipe.QuestUnlock)` matches the
  existing state model and needs no new plumbing.
- **Pins that move with this slice:** NpcDataTests pins
  giver.AvailableQuestIds[0] == "first_flame" (fine, we append
  herb_gathering, not prepend); QuestChainTests pins the 6-chain and
  timber as a side quest (fine); the 18-quest count pin stays 18; the
  43 trade rows stay 43. RecipeRegistry gains 1 recipe (sap) — no
  count pin breaks (CraftingPipelineTests asserts >= 80).
- **Data-loading APIs (per skill pitfall):** recipes load via
  RecipeRegistry.LoadAll() (typed); quests via QuestRegistry.LoadAll();
  npcs via NpcRegistry.LoadAll(); raw item ids for the source graph via
  DataLoader.LoadJsonList<ItemDef> + ItemsData rows. items.json has
  tree_sap (id 631) so no item def work needed.

## Design

### 1. Planks un-gate + quest list update

- Construction/recipes.json planks def: delete the `quest_unlock` line.
- quests.json goblin_diplomacy `recipe_unlocks`: `["planks"]` → `[]`
  (the quest keeps gear_unlocks, trophy, gold, XP — it just no longer
  pretends to own planks).
- QuestChainTests' recipe-unlock pin loop still passes (it checks every
  listed unlock EXISTS, and planks stops being listed).

### 2. Sap recipe (first_flame reachability)

- Data/recipes.json, wood_chain next to `sticks`:
  `whittle_sap`: "Tap Sap" — 1 oak_logs → 1 tree_sap, crafting 1,
  xp 4, no campfire, no structure. One recipe serves the whole
  first_flame contract; maple keeps its higher-yield niche
  (tap_maple_sap stays the bulk source at crafting 5).

### 3. herb_gathering attach

- npcs.json quest_giver_forest_1.available_quest_ids: append
  "herb_gathering" (after timber_collection).
- The quest's own gates (persuasion 2, int 3) are reachable from the
  First Steps chain's int XP (forest_friends 40, settling_in 40, plus
  flat xp_reward on every claim — int 3 = 174 XP, reachable: first_flame
  30 + hearth 40 + full_belly 50 + first_blood 60 + settling_in 120 +
  forest_friends 80 = 380 flat alone).
- Persuasion 2: persuasion starts at 1, levels with intelligence levels
  via unallocated points (3/level). int 2 needs 83 XP — one quest claim.
  So the gates are honest once the quest is offered.

### 4. quest_unlock enforcement

- `CraftingSystem.Craft` signature gains
  `IReadOnlySet<string>? unlockedRecipes = null`; when non-null and the
  recipe has a QuestUnlock not in the set, refuse:
  "Requires quest: {Name}." (visible vocabulary, red-line convention).
- Panel path: `CanCraft` + the Craft call pass
  `player.UnlockedRecipes` (panel already receives player context
  through HudWindows; verify the exact plumbing while wiring — the
  crafting panel's Render/Handle live in Panels2.cs with Player
  available via the game HUD stack).
- Worker path unchanged (null → ungated): colony auto-production
  recipes are colony-visible by the stock convention, not the player's
  quest diary.
- No enforcement on placement/building — structures have no
  quest_unlock field.

### 5. Reachability audit test

`ProgressionReachabilityTests` (new file):

- **Item-source graph (data-driven):** for every quest condition of
  type collect_item/deliver_item: the target must be reachable from a
  fresh character via (a) a resource node with required_level ≤ some
  level reachable in the skill the node trains (approximation: required
  level ≤ 10 with an entry node ≤ 2 in the same skill), (b) a recipe
  whose inputs recurse (depth-limited), (c) a trade listing with
  commerce_requirement ≤ the commerce reachable from quest flat XP, or
  (d) loot/starter. Failures print the chain, not just the id.
- **Quest-gate graph:** every quest's min_total_intelligence /
  min_commerce / min_persuasion must be satisfiable from the int XP of
  quests with no prerequisites, ordered by dependency (a fixed-point
  walk of the prerequisite DAG). goblin_truce (int 8, behind
  goblin_diplomacy) must be reachable with the XP the earlier chain
  awards.
- **quest_unlock enforcement pin:** a quest-locked recipe refuses via
  Craft when the unlock set lacks it, crafts when the set carries it.
- The three known-dead items are asserted REACHABLE post-fix:
  planks (day 1), tree_sap (whittle_sap), cooked_fish (rod →
  fish_spot → cook_fish; rod now reachable: planks day-1 + grass_rope
  crafting 1).

## Testing plan

RED first (each fails for the right reason today):

1. `Planks_RecipeNotQuestGated` — planks def has QuestUnlock == null
   AND goblin_diplomacy's recipe_unlocks no longer contain planks.
   (Fails today: flag present.)
2. `FirstFlame_TreeSapReachableAtCrafting1` — a crafting-1 recipe
   outputs tree_sap from oak_logs. (Fails today: no such recipe.)
3. `HerbGathering_OfferedByForestGiver` — Hemlock lists it, after
   timber_collection. (Fails today: not offered.)
4. `QuestLockedRecipe_RefusesWithoutUnlock` — Craft with a locked
   recipe + empty unlock set refuses; with the unlock present,
   succeeds. (Fails today: no enforcement, both succeed → assert on
   the refusal fails.)
5. Reachability walk: `EveryQuestItemTarget_HasFreshReachableSource`
   and `QuestIntGates_ReachableFromPrerequisiteFreeQuests`. (Fails
   today on tree_sap via first_flame at minimum.)

GREEN: data edits + Craft parameter + panel wiring. Full suite, docs.

## Open questions

- Worker auto-production quest-gating: deliberately OFF (stock
  convention) — confirm at review.
- The reachability test's skill-level approximation (entry ≤ 2): a
  node at required_level 5 in a skill with entry nodes trains to 5
  honestly (XP from level-1 nodes); only nodes gated ABOVE every
  entry XP source are walls. The approximation may need tuning when
  it fires false positives.

## Revision history

- 2026-10-09: drafted. Slice A (GatherSkillFor) already landed
  RED 05f9017 / GREEN 29cb967. Grounding discovered quest_unlock was
  never enforced — the "deadlock" was a display-only flag; item 4
  (enforcement) is the fix for the class, items 1-3 + the audit test
  are the approved remedies.
