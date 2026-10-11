# Unlock-as-event trigger + quest-unlock honesty fixes

- Date: 2026-10-11
- Status: implemented (RED d01ce2d / GREEN d625fd5, 2026-10-11)
- Slice line: a quest hands over its recipe unlocks the moment its
  objectives complete (a story beat — you learn from the hermit when you
  REACH him), with a visible "Recipe learned" line; the quest-unlock gate
  is made actually functional end-to-end, and unlocks + quest completion
  persist across saves.

## Design law

No gate that can never open and no reward that can never arrive: every
enforcement path must be reachable with real data, and display state must
match enforcement state. A learned recipe is learned — unlocks are
one-way knowledge, not a rental that save/reload or inventory churn can
revoke.

## Goal

1. **Event trigger:** while a quest with `recipe_unlocks` is active and
   all its conditions are met (the same `ConditionsMet` claim uses), the
   quest's recipe ids enter `Player.UnlockedRecipes` once, with a
   "Recipe learned: <recipe_id>" notification for ids the data actually
   gates. Claim keeps its (now idempotent) writes as a backstop.
2. **Keying fix:** the gate at all three sites
   (CraftingSystem.Craft, Panels2 CanCraft, FireMenuPanel CanCraft)
   checks `unlockedRecipes.Contains(recipe.RecipeId)` — recipe-id keyed,
   matching Claim's writer, the field name, and the panel's
   "Unlocked recipes" count. Today it reads `recipe.QuestUnlock` (a
   quest id) while the writer writes recipe ids, so the audit-B
   enforcement has never opened a recipe with real data.
3. **Data fixes:** gem_mining's dead unlock id `craft_bone_powder` (no
   such recipe; `grind_bone` is the real bone-powder recipe) →
   `grind_bone`. New data law: every `recipe_unlocks` id on every quest
   is a real recipe id (QuestChainTests only checks the 6-quest forest
   chain; gem_mining was never checked).
4. **Persistence:** quest completion (`QuestSystem._completed`) round-
   trips via the existing `QuestDataSnapshot.State` field ("completed");
   `Player.UnlockedRecipes` / `UnlockedGear` persist as new SaveData
   fields. Old saves read them empty (they never persisted anything).
5. Notification honesty: the event notifies only ids that are both
   newly added and actually quest-gated (`Recipes.GetRecipe(id)
   .QuestUnlock != null`) — "learned" something already craftable would
   be a lie, so ungated ids unlock silently.

## Non-goals

- No new quests, recipes, or gear; no expansion of the gated-recipe
  surface (the 3 flagged recipes stay the only gated ones — flagging
  more is a gameplay decision for another slice).
- Gear unlocks stay claim-time (`UnlockedGear` persists, but the event
  does not hand gear early).
- No UI rework: the display layer already reads live
  (Panels2.cs:1060 "Quest: <id>" label, FireMenuPanel.cs:294).
- Worker auto-production stays ungated (null set convention).

## Ground (verified in the tree, 2026-10-11)

- The ONLY writer today is `QuestSystem.Claim` (QuestSystem.cs:229-230)
  adding `quest.RecipeUnlocks` — recipe ids. Gates read quest ids:
  CraftingSystem.cs:39-44 (`Contains(recipe.QuestUnlock)`,
  message "Requires quest unlock: <quest_id>"), Panels2.cs:1088-1089,
  FireMenuPanel.cs:330-331. Sets can never intersect → enforcement dead
  in practice.
- Audit-B's test (ProgressionReachabilityTests:101) built the set as
  `{locked.QuestUnlock}` by hand — it pinned the gate's keying, never
  the writer's, which is why the mismatch survived.
- Real data: 5 quests carry recipe_unlocks (goblin_truce → polish_bone,
  craft_stick; coastal_defense → smelt_iron, craft_fired_brick;
  sand_storm_warning → smelt_copper, craft_cobblestone; gem_mining →
  smelt_steel, craft_bone_powder [dead id]; forest_friends →
  craft_candle). Exactly 3 recipes carry `quest_unlock` flags
  (craft_stick/goblin_truce, craft_fired_brick/coastal_defense,
  craft_cobblestone/sand_storm_warning — Skills/Construction/data/
  recipes.json:57,81,101) and each is listed by its quest (reciprocity
  holds today; no drift deadlock exists).
- `QuestSystem.Tick(dt, player, world)` runs every frame from
  Game.cs:1024; it early-returns without a world, and never receives
  the inventory — collect/deliver progress polls the inventory
  (ProgressOf, QuestSystem.cs:169-177). The event sweep needs the
  inventory, so Tick grows an optional `Inventory?` parameter (one
  caller; sprint-shared Game.cs untouched).
- `QuestDataSnapshot.State` already documents "completed" (SaveSystem.cs:
  470) but GetSnapshot only writes "active" rows (QuestSystem.cs:263)
  and RestoreSnapshot ignores State entirely — `_completed` is
  session-only today, so claimed quests re-offer after reload (XP
  re-claim dupe) and unlocks vanish.
- `Player.UnlockedRecipes`/`UnlockedGear` (Player.cs:87-88, get-only
  HashSets) are absent from SaveData (SaveSystem.cs:297-327).
- Notifications: `player.ActionSystem?.AddNotification(text, (r,g,b))`
  (Player.cs:247 pattern); gold (255,215,0) is the reward-line
  convention; tests must FlushNotifications before asserting.
- `Npc.AvailableQuests` is a settable List; `QuestGiverNpc` has a
  parameterless ctor (Panels.cs:136 uses one); `QuestDef` is init-only
  and constructible in tests. `RestoreSnapshot` accepts any quest ids —
  tests can stage an active quest directly (mirrors a mid-quest save)
  without paying acceptance-gate staging.
- `CraftingSystem.Craft(recipeId, inventory, skills, structures?,
  unlockedRecipes?)` — null set = worker path, ungated by convention.

## Design

- `QuestSystem.Tick(float dt, Player? player, TileMap? world,
  Inventory? inventory = null)`: after the player null-guard and BEFORE
  the world guard, if `inventory != null && Registry != null`, run the
  event sweep; then the existing visit_location logic unchanged. The
  sweep, per active quest with non-empty `RecipeUnlocks`:
  `ConditionsMet(quest, inventory)` → for each recipe id,
  `player.UnlockedRecipes.Add(id)`; ids that were already present are
  skipped (idempotent — the HashSet is the fire-once dedupe, so no
  fired-flag state to persist); newly added ids notify only when
  `Recipes?.GetRecipe(id)?.QuestUnlock != null` (new optional
  `RecipeRegistry? Recipes` property on QuestSystem, wired in
  Bootstrap next to the OnCrafted wire at Bootstrap.cs:239).
- Claim is unchanged — its writes become idempotent backstops
  ("claim merely surfaces the already-known provision").
- Gate fix (3 sites): `!unlockedRecipes.Contains(recipe.QuestUnlock)` →
  `!unlockedRecipes.Contains(recipe.RecipeId)`. The refusal message
  keeps the quest id ("Requires quest unlock: goblin_truce") — visible
  vocabulary, it names the quest to go do.
- Unlock set semantics: recipe ids, one-way. If conditions later drop
  (items spent below a collect threshold before claim), the recipe
  stays learned — knowledge is permanent, matching claim-time
  permanence.
- Persistence:
  - GetSnapshot appends one row per `_completed` id with
    `State = "completed"` (empty progress); RestoreSnapshot adds rows
    with State "completed" to `_completed`, everything else stays
    active-restore as today.
  - SaveData grows `List<string> UnlockedRecipes` / `UnlockedGear`;
    BuildSnapshot fills them from `game.Player`; LoadIntoGame gains a
    RestoreUnlocks step that unions them into `game.Player` (old saves:
    empty lists, no behavior change — nothing persisted before).
- Data: quests.json gem_mining `recipe_unlocks`:
  `["smelt_steel", "grind_bone"]` (was craft_bone_powder).

## Testing plan (RED first, verify each failure's reason)

New file QuestEventUnlockTests.cs:
1. EveryQuestRecipeUnlock_IsARealRecipe — all quests, all unlock ids ∈
   RecipeRegistry. RED: gem_mining's craft_bone_powder missing.
2. EveryGatedRecipe_IsUnlockableByItsQuest — flagged recipe → owning
   quest exists and lists it. Expected to pre-green legitimately (data
   reciprocity audit pin).
3. EventUnlock_FiresWhenObjectivesComplete — synthetic quest
   (recipe_unlocks ["craft_stick"], collect stick 3) on a synthetic
   QuestGiverNpc; add sticks, Tick(dt, player, null, inv) →
   UnlockedRecipes contains craft_stick; not before. RED: no sweep.
4. EventUnlock_NotifiesOnlyGatedRecipes — with Recipes wired, the
   craft_stick event posts "Recipe learned: craft_stick" once (second
   Tick: no duplicate); a synthetic quest unlocking polish_bone
   (ungated) adds silently.
5. EventUnlock_ClaimStillWorks — event fired, then Claim succeeds and
   completes the quest (idempotent writes).
6. GoblinTruce_EventUnlock_OpensCraftStick (real data end-to-end) —
   stage goblin_truce active via RestoreSnapshot (mid-quest-save
   reality; acceptance gates are already covered by QuestChainTests),
   wolf_pelt×3 + RecordNegotiation("goblins")×2, Tick →
   UnlockedRecipes ∋ craft_stick → real CraftingSystem.Craft("craft_stick",
   1 oak_log, crafting 1, player's set) succeeds. RED: unlock never
   fires AND the gate keys on the wrong id — this test pins the pair
   and stays red until both land.
7. ClaimedQuest_PersistsCompletedState — claim → GetSnapshot has a
   "completed" row. RED: only "active" rows exist.
8. RestoreSnapshot_RebuildsCompleted — snapshot with a completed row →
   IsCompleted true. RED: State ignored.
9. SaveRoundTrip_UnlocksAndCompletedSurvive — staged Games,
   BuildSnapshot → LoadIntoGame → sets + completion restored. RED:
   SaveData lacks the fields (stubs) and the wiring.

Edited: ProgressionReachabilityTests.QuestLockedRecipe_RefusesWithoutUnlock
— the with-unlock branch builds the set as `{locked.RecipeId}` (was
`{locked.QuestUnlock}`). RED: fails for the keying reason until the
gate fix lands.

Count pins that move: none (no new quests/recipes/npcs; 29 quests
stay 29).

## Open questions (resolved during clarify, on record)

- Trigger timing: at objectives-complete (user picked; notification on
  unlock).
- Keying: the clarify answers pulled writer-side ("make Claim write
  quest ids") and recipe-id-keyed set in different questions. Resolved
  recipe-id keyed / gate-side: the field name, the writer, the panel
  count, and QuestChainTests' data law all already say recipe ids — the
  gate reading quest ids is the lone outlier, and quest-id keying would
  make `UnlockedRecipes` contain quest ids while the panel counts them
  as recipes (a display lie). If the user prefers quest-id keying after
  all, the swap is confined to the three gate sites + the test set
  construction.
- Dead id: repointed at grind_bone (user picked). Note it is a data
  hygiene fix only — grind_bone carries no quest_unlock flag, so
  gem_mining's unlocks unlock nothing gated; expanding the gated
  surface is deliberately out of scope.
- Persistence: both the unlock sets and quest completion (user picked).
