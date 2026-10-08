# Water gathering — bucket mechanics — design

Date: 2026-10-08
Status: draft (RED next)
Slice: post-roadmap frontier, next-arc item 1 — "water gathering (bucket
mechanics)" (user-approved next arc start-of-session state).

## Design law

- **RS**: progression gates everything — water is no longer free. The
  `water_source` node gains a `requires_tool: "bucket"` gate; a bucket is a
  craftable, equippable tool with durability. The player must craft one
  before the infinite node yields.
- **DS**: the grind stays honest — the bucket is placed with player-fetched
  materials through the existing crafting system (`Data/recipes.json`,
  required_skill crafting level 2); no free water from an infinite node.
  The existing "You need a bucket." refusal (`ActionSystem.cs:79`) explains
  the gate in the DS honesty tradition.
- **MC**: readability — the water node keeps its existing `world/water`
  ground-decal look; no new art. The refusal is the existing
  tool-requirement vocabulary; workers mirror the cave-mining convention
  (visible in stock, silently skipped when not).

## Ground — verified in the tree

- `water_source` def exists (`Data/resources.json:168`): plains, ground
  decal, `depletion_count: -1` (infinite), yields `water` (stack 10, is_food,
  hunger_restore 5), `requires_tool: null` — the gate is the gap.
- The full tool pipeline is live and used by axe/pickaxe: player harvest
  via `ActionSystem.StartAction` tool check (`ActionSystem.cs:74-80`) +
  `FindEquippedTool` (`:376-407`, exact-id or `_suffix` match, equipped
  slots first, then carried); interact routing in `InteractSystem.cs:88-95`
  routes any non-axe/pickaxe tool req to `ActionType.Foraging` (comment:
  "water, etc.").
- Worker dispatch: `CanWorkerHarvest` (`RecruitmentSystem.cs:760-772`) —
  surface tool nodes are NEVER worker-harvestable (`if (!inCave) return
  false`); caves consult player inventory + colony store. The colony-gather
  skill gate is live (`RecruitmentSystem.cs:497-503`, `GatherSkillFor`
  `:685-690`).
- Worker harvest is RNG-free (`RecruitmentSystem.cs:601` deterministic
  counter bonus), so tests are stable.
- `water` is an input to `brew_mead` (`Data/recipes.json:357-378`, cooking
  12) — water has a real sink. Player-side `StartAction` success roll is
  RNG but `PumpSuccessRate(sm, 1000f)` makes it deterministic
  (`ActionSystemTests.cs:33-36`).
- Data-count pins: `NpcDataTests` pins Npcs=17, TradeItems=42, Quests=18;
  **no** test pins Items/Resources/Recipes counts — adding a bucket item,
  recipe, or flipping `requires_tool` on water_source breaks no pins.
- No thirst stat exists; per clarifying defaults this slice is the bucket
  gate ONLY (thirst deferred; see Open questions).
- No bucket sprite exists — per skill pitfall, reuse an existing sprite
  (`berry_jar` is the natural pick, glass/container-shaped).

## Goal

1. **Bucket item**: new `ItemDef` — id `bucket`, equippable, essential tool,
   tool_type `bucket`, durability 100, sprite `items/berry_jar`.
2. **Node gate**: `water_source.requires_tool` null → `"bucket"`.
3. **Player path**: interacts to water_source → Foraging route (existing
   comment already anticipates water) → `StartAction` tool check refuses
   without a bucket ("You need a bucket."); with a bucket it harvests as
   today.
4. **Worker path**: `CanWorkerHarvest` surface carve-out — buckets join
   pickaxes as harvestable-if-available, mirroring the cave convention:
   tool in player inventory OR colony store → harvestable on the surface
   too. Water keeps training foraging (GatherSkillFor unchanged).

## Non-goals

- No new thirst/survival stat (deferred; see Open questions).
- No fishing rod/bait/catches — that is next-arc item 2 (fishing skill).
- No bucket durability wear-on-harvest, refilling, or spilling — the
  bucket is a gate token; durability exists for UI consistency with other
  tools.
- No new art (sprite reuse), no quests, no NPC changes, no trade entries.
- No change to cave tool-node dispatch (the surface carve-out extends the
  cave behavior to a new tool category, not vice versa).

## Design

### Data changes

`Data/items.json` — new entry beside axe/pickaxe:

```json
{
  "id": "bucket",
  "name": "Bucket",
  "stack_size": 1,
  "is_equippable": true,
  "is_essential_tool": true,
  "is_food": false,
  "tool_type": "bucket",
  "durability": 100,
  "sprite_key": "items/berry_jar"
}
```

`Data/resources.json` — `water_source.requires_tool: null` →
`"bucket"`.

`Data/recipes.json` — new recipe beside grass_rope (crafting level 2):

```json
{
  "recipe_id": "wood_bucket",
  "name": "Carve Wooden Bucket",
  "input_items": [["planks", 2], ["grass_rope", 1]],
  "output_item": "bucket",
  "output_quantity": 1,
  "xp_reward": 6.0,
  "processing_chain": "wood_chain",
  "tier": 1,
  "requires_campfire": false,
  "requires_structure": "",
  "skill": null,
  "season": "all",
  "skill_affects_yield": "crafting",
  "required_skill": "crafting",
  "required_level": 2
}
```

(tier 1 matches the planks/grass_rope house convention in the same file.)

### Code changes

**Player** — none. `InteractSystem.cs:88-95` already routes bucket →
Foraging; `StartAction`'s tool check + `FindEquippedTool`'s suffix match
handle the refusal and recognition with zero code change. This is the
"leverage existing logic" law: the pipeline was built for exactly this.

**Worker** — one seam: `RecruitmentSystem.CanWorkerHarvest`
(`:760-772`). Current: tool nodes are cave-only. New: the cave branch's
  inventory/store check becomes the general tool-available check, and the
  surface carve-out admits it when the tool is present (mirroring the
  user-approved cave convention on the surface for the same reason:
  visible stock, silent skip when not).

```csharp
private static bool CanWorkerHarvest(ResourceNode node,
    Inv inventory, ColonySystem? colony, bool inCave)
{
    if (!node.RequiresTool) return true;
    string required = node.ResourceDef?.ToolRequirement ?? "";
    bool inPlayerInventory = inventory.Slots.Any(...);  // existing match logic
    bool inColonyStore = colony != null && (colony.GetItemQuantity(required) > 0
        || colony.GetItemQuantity("stone_" + required) > 0);
    return inPlayerInventory || inColonyStore;
}
```

For buckets the store check is `colony.GetItemQuantity("bucket") > 0`.
(Cave dispatch (`:648`) keeps `skills?.AddXp("mining", xp)` — unchanged.)

For surface tool nodes generally, this change makes `NodeLevelGate_AllowsInLevel_Harvest` (ColonySkillGateTests.cs:182) live instead of vacuous — the level-gate test still passes because its level-1 copper is in-level; `RefusesAboveLevel` still passes because dispatch never claims. No test updates needed.

### UI

None. The build menu shows recipes from the same registry the recipe is added to; the crafting panel lists `wood_bucket` like any other recipe. Tool refusal message text comes from `ActionSystem.cs:79`.

## Testing plan

New file `WaterBucketTests.cs`, harness-free player-side + harness worker-side mix:

1. **RED compile+data** — `ItemCatalog` has `bucket` with
   `ToolType == "bucket"`, durability 100; recipe `wood_bucket` loads with
   inputs planks×2 + grass_rope×1, crafting level 2. (Data-shape test in the
   NpcDataTests style, `registry.LoadAll()`.)
2. **Player refused without bucket** — `ActionSystem.StartAction` on a
   water_source node with no bucket in inventory returns "You need a
   bucket."; Active is not busy.
3. **Player harvests with bucket** — same node, bucket in inventory via
   plain AddItem (carried, not equipped — exercises FindEquippedTool's
   second pass), `PumpSuccessRate(sm, 1000f)`; Update succeeds; result is
   water ×1, xp 0 (def has xp_reward 0.0). Second Update returns null (no
   repeat).
4. **Worker refused without bucket** — colony founded, water_source node
   placed via NodeAt-style helper, no bucket anywhere → worker never
   carries (CarriedQuantity stays 0 after 12 ticks, the proven outcome-
   assert idiom).
5. **Worker harvests with bucket in store** — colony.Store("bucket", 1);
   same node; tick 40; assert water deposited in colony stockpile
   (water_count > 0) — outcome, not per-tick status.
6. **Worker surface pickaxe regression** — `NodeLevelGate_AllowsInLevel_Harvest`
   shape: surface copper + colony pickaxe → mining XP lands. This
   un-vacuums the existing test and guards the carve-out.

## Open questions

Answered with defaults per standing delegation (clarify timed out; user
said "pick defaults"):

- Thirst stat: NOT this slice — bucket gate only, thirst deferred.
- Worker surface-tool harvest: YES via cave-convention mirror (tool in
  player inventory or colony store).
- Bucket craftable: YES — planks×2 + grass_rope×1, crafting level 2.
- Water trains foraging for workers (GatherSkillFor unchanged) — water
  xp_reward is 0.0 so no XP drift; player-side foraging unaffected.
- Sprite: reuse `items/berry_jar` for the bucket (no new art this slice).

Deferred to fishing slice (next-arc item 2): rod/plank tech, bait, fish
catch tables, fishing skill.

## Revision history

- 2026-10-08 draft.
