# Fishing — rod mechanics and the fishing skill — design

Date: 2026-10-08
Status: implemented (RED `4bcf55e`; GREEN `345e50e`)
Slice: next-arc item 2 — "fishing as a real skill (catches/bait/rod)" (per
user-approved scope: skill + rod now, bait next).

## Design law

- **RS**: progression gates everything — fish are no longer free. The
  `fish_spot` node gains a `requires_tool: "fishing_rod"` gate; a rod is a
  craftable, equippable tool. The player must craft one before fish yield,
  and catches train a real `fishing` skill (net-new: XP today trains
  foraging).
- **DS**: the grind stays honest — the rod is placed with player-fetched
  materials through the existing crafting system (`Data/recipes.json`,
  crafting level 2, same tier as the bucket: planks + grass_rope). The
  existing "You need a fishing_rod." refusal (`ActionSystem.cs:79`) explains
  the gate in the DS honesty tradition.
- **MC**: readability — fish spots keep their existing `world/fish` decal
  look (no new art); rod sprite reuses an existing asset; refusal and
  skill display reuse existing UI vocabulary.

## Ground — verified in the tree

- `fish_spot` def (`Data/resources.json:250-273`): coastal, ground decal in
  shallow water (WorldGen `:518` keeps it in-sea), yields `raw_fish` (stack
  10, `items.json:140`), `yield_quantity: 1`, `xp_reward: 15.0`,
  `depletion_count: 2`, `regrow_time: 60`, `requires_tool: null`,
  `required_level: 5`, `sprite_key: world/fish` — the gate is the gap.
- The full tool pipeline is live (proven by the bucket): player harvest via
  `ActionSystem.StartAction` tool check (`:74-80`) + `FindEquippedTool`
  (`:376-407`, exact/suffix match, equipped-first); `InteractSystem.cs:92-95`
  routes any non-axe/pickaxe tool req to `ActionType.Foraging`.
- **XP-routing landmine (why a Foraging route fails):**
  `ActionSystem.CompleteAction` (`:268-283`) nulls `action.Resource` before
  `ProcessCompletion` (`:227-230`) maps skill via
  `ActionTypeToSkillId` → `GetSkillId(actionType, resource)`. A fishing XP
  routed through Foraging lands in foraging.
- **Worker landmine:** `GatherSkillFor` (`RecruitmentSystem.cs:685-690`)
  maps ANY `RequiresTool` node to "mining" — a rod-gated fish_spot would
  train recruits' mining; needs a fishing branch BEFORE the tool check.
- `SkillManager` has NO fishing skill (11 skills; `SkillManager.cs:16-21`).
  Adding to the array registers it everywhere: XP/level (`AddXpWithNotification`),
  snapshots (`GetSnapshot`/`RestoreSnapshot` keys match `_skills`), death
  penalty, UI skills table (`Panels2.cs:486-496` row needed).
- `SkillManager.GetSkill("fishing")` on the player path always returns a
  non-null `SkillData` (fresh instance if absent, `:162-165`) — BUT
  `AddXpWithNotification`/`GetSkillLevel` silently no-op for unknown ids
  (`:73`, `:39-40`); registering the skill is what makes player fishing XP
  real.
- `CanWorkerHarvest` (`RecruitmentSystem.cs:758-776`): surface tool nodes
  harvestable when tool is in player inventory or colony store (bucket
  carve-out, extended) — rod joins by name, zero code change.
- Rod-craftable pattern proven by `wood_bucket` (`recipes.json:71-91`):
  planks×2 + grass_rope×1, crafting 2, tier 1, wood_chain.
- Data-count pins: `NpcDataTests` pins Npcs=17, TradeItems=42, Quests=18;
  **no** test pins Items/Resources/Recipes counts — new rod item/recipe and
  the fish_spot edits break no pins.
- No `fishing_rod` item, recipe, tool_type, or sprite exists anywhere
  (verified by grep across src/).
- Test-harness shape: `WaterBucketTests.cs` is the copy-verbatim model —
  harness-free player tests + `Harness` worker tests, `PumpSuccessRate`
  (foraging `success_rate` substat, `:47-48`), outcome asserts
  (`CarriedQuantity`/`GetItemQuantity`, never per-tick status).
- No fishing sprite exists. `items/elder_wood_staff` is the natural rod
  stand-in (long thin wood, appropriate tier flavor).

## Goal

1. **Fishing skill**: register `fishing` in `SkillManager` (array position:
  after "foraging" — grouping gather skills together).
2. **Rod item**: new `ItemDef` — id `fishing_rod`, equippable, essential
  tool, tool_type `fishing_rod`, durability 100, sprite
  `items/elder_wood_staff`.
3. **Rod recipe**: `craft_fishing_rod` — planks×2 + grass_rope×1,
  crafting level 2, tier 1, wood_chain (mirrors `wood_bucket`).
4. **Node gate**: `fish_spot.requires_tool` null → `"fishing_rod"`;
  `required_level` 5 → 1 (see Open questions).
5. **Player path**: interact near fish_spot → InteractSystem routes rod →
  **new `ActionType.Fishing`** → `StartAction` Fishing branch (yield from
  def; success from foraging-style substats → see Design) → rod refusal
  without a rod → completion grants raw_fish + fishing XP.
6. **Worker path**: `GatherSkillFor` fishing branch (rod nodes → "fishing"
  BEFORE the tool check) — recruits train fishing on rod-gated nodes;
  `CanWorkerHarvest` unchanged (rod joins bucket/pickaxe by name).
7. **UI**: skills-panel row for fishing (Panels2.cs table + full name).

## Non-goals

- No bait (next slice; see Open questions).
- No catch tables / junk items / multiple fish species — `raw_fish` is the
  single catch this slice.
- No rod durability wear-on-harvest (gate token, same decision as bucket).
- No new art (sprite reuse), no quests, no NPC def changes, no trade
  entries.
- No fishing schedule slot — workers fish via the existing gather dispatch
  during work slots.
- No changes to cave dispatch or `CanWorkerHarvest` logic.

## Design

### Data changes

`Data/items.json` — new entry beside axe/pickaxe/bucket:

```json
{
  "id": "fishing_rod",
  "name": "Fishing Rod",
  "stack_size": 1,
  "is_equippable": true,
  "is_essential_tool": true,
  "is_food": false,
  "tool_type": "fishing_rod",
  "durability": 100,
  "sprite_key": "items/elder_wood_staff"
}
```

`Data/recipes.json` — beside `wood_bucket` (tier 1, crafting level 2):

```json
{
  "recipe_id": "craft_fishing_rod",
  "name": "Carve Fishing Rod",
  "input_items": [["planks", 2], ["grass_rope", 1]],
  "output_item": "fishing_rod",
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

`Data/resources.json` — `fish_spot`: `requires_tool: null` →
`"fishing_rod"`, `required_level: 5` → `1`.

### Code changes

**`Actions/ActionType.cs`** — add enum member + skill-id mapping:

```csharp
public enum ActionType { Woodcutting, Mining, Cooking, Foraging, Fishing }

public static string GetSkillId(this ActionType actionType, ResourceNode? resource = null)
    => actionType switch
    {
        ActionType.Woodcutting => "woodcutting",
        ActionType.Mining => "mining",
        ActionType.Cooking => "cooking",
        ActionType.Foraging => "foraging",
        ActionType.Fishing => "fishing",
        _ => throw ...
    };
```

**`Actions/ActionSystem.cs`** — new branch in `StartAction` for
`ActionType.Fishing` (resource null-checked like the others): sets
Resource/XpReward/YieldItem/YieldQuantity/RequiredTool, stamina 2.0f,
success-rate bonus from the `fishing` substat `success_rate` (the same
substat key `PumpSuccessRate` pumps in WaterBucketTests — verified:
`SkillManager.GetEffectiveStat` reads from `GetSkill("fishing").SubStats`
for any registered skill, and the default `SkillData` SubStats dict ships
all five keys). Include fishing in the `CompleteAction` gather-trio
(`:272`) so completion routes through `CompleteGathering` and resets
cleanly.

**`Interactions/InteractSystem.cs:92-95`** — one arm:

```csharp
"fishing_rod" => ActionType.Fishing,
```

**`NPC/RecruitmentSystem.cs` `GatherSkillFor`** — fishing branch BEFORE
the tool check:

```csharp
if (def.ToolRequirement == "fishing_rod") return "fishing";
if (def.RequiresTool) return "mining";
```

**`Skills/SkillManager.cs:16-21`** — add `"fishing"` to the array (after
"foraging").

**`UI/Panels2.cs:486-496`** — add row `("fishing", "Fishing", "Fi", 66, 134, 244)`.

### What this design does NOT change (and why)

- `CanWorkerHarvest` — rod nodes already harvestable when rod is in stock
  (bucket carve-out, by tool name; zero code change).
- Save/restore — `SkillSnapshot` serializes whatever `_skills` holds;
  registration adds the fishing row automatically on save; old saves
  without a fishing key restore as level 1 / 0 XP (missing key → key absent
  from snapshot → default SkillData).
- `NpcDataTests` count pins — none touch Items/Resources/Recipes.
- WorldGen fish_spot placement (in-sea shallow-water rule unchanged).
- `Game.cs:1361` DSR_TEST_ACTION foraging fallback — skips tool-required
  nodes already (`:1363`).

### UI

None beyond the skills-panel row. The build menu lists `craft_fishing_rod`
from the same recipe registry. Refusal message comes from
`ActionSystem.cs:79` (`"You need a fishing_rod."`).

## Testing plan

New file `FishingTests.cs` (WaterBucketTests shape):

1. **Data shape** — rod ItemDef loads (tool_type, equippable, durability
  100, sprite elder_wood_staff); `craft_fishing_rod` recipe loads (inputs,
  crafting 2, output fishing_rod); fish_spot raw row: `requires_tool ==
  "fishing_rod"`, `required_level == 1`; fishing skill registered
  (GetSkillLevel("fishing") == 1 fresh).
2. **Player refused without rod** — StartAction Fishing on fish_spot, no
  rod → "You need a fishing_rod."; not busy.
3. **Player harvest with rod + XP** — rod carried; PumpSuccessRate-style
  (fishing success_rate); Update → Success, raw_fish ×1, **Xp == 15**
  (def's xp_reward — the XP routing works); second Update null (no
  repeat); `ProcessCompletion` lands XP in `fishing` (sm.GetSkill("fishing").Xp
  == 15) and NOT foraging (0).
4. **Interact routing** — InteractSystem maps `fishing_rod` →
  ActionType.Fishing (light reflection-free check via the switch table if
  accessible; else verified by the StartAction path in 2-3).
   — covered by 2/3 exercising the full path; no separate test.
5. **Worker refused without rod** — colony founded, fish_spot node, no rod
  anywhere → CarriedQuantity stays 0 after 12 ticks.
6. **Worker harvests with rod in store, trains fishing** — colony.Store
  ("fishing_rod", 1); tick 40; raw_fish lands in store; worker's
  `Skills.GetSkillLevel("fishing") >= 1` after XP (GatherSkillFor branch;
  XP > 0 → level check via `GetSkill("fishing").Xp > 0`).
7. **Worker regression: pickaxe nodes still train mining** — surface copper
  + colony pickaxe → mining XP > 0 (guards the GatherSkillFor re-order).

## Open questions

Answered with defaults per standing delegation (user confirmed skill + rod
this slice, bait next):

- Scope: skill + rod + gate this slice. Bait/catch tables deferred.
- `required_level` 5 → 1: OSRS shrimp-net convention — fishing is
  entry-level via cheap rod; level-5 gate on a fishing-only node is a dead
  end (no other fishing XP source exists to reach 5 with). Rod is the gate.
- Worker fishing: yes via CanWorkerHarvest stock-check (rod in player
  inventory or colony store) — bucket convention.
- XP routing: new `ActionType.Fishing` (enum survives the
  CompleteAction null-out; Foraging route would leak XP into foraging —
  verified landmine, see Ground).
- Sprite: reuse `items/elder_wood_staff` (no new art).
- fish_spot XP 15/catch stays (raw_fish restores 12 hunger; decent but not
  trivial early food — good first-fishing reward).
- Fishing workers use existing gather dispatch; no new schedule slot.

## Revision history

- 2026-10-08 draft.
