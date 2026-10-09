# Fishing rod tiers — bone rod — design

Date: 2026-10-09
Status: drafted
Slice: next-arc continuation — "rod TIERS" (user: "whatever makes fishing
satisfyingly grindy in a survival sandbox with colony dynamics"). The
cast window (3178c94) becomes the tier lever: better rod, shorter cast.

## Design law

- **RS**: tool progression — the carved plank rod (crafting 2) is
  entry-tier; the bone rod (crafting 4, monster-drop ingredients) is the
  upgrade. Same node, faster grind: 3s cast → 2s cast.
- **DS**: the grind stays honest — the upgrade costs wolf bones (combat
  drops), not just wood; the faster cast is earned, not bought. The
  base rod never stops working (tiers are economy, not walls — the
  bait precedent).
- **MC**: readability — the cast-start notification names the rod tier
  ("You cast your bone rod..."), one new item/recipe, no new systems.

## Ground — verified in the tree

- `FindEquippedTool` suffix match (`ActionSystem.cs:376-407`): exact id OR
  `_suffix` — `bone_fishing_rod` passes the player gate for free, zero
  code change on the player path.
- **Worker store gap (the find):** `CanWorkerHarvest`
  (`RecruitmentSystem.cs:772-776`) checks `GetItemQuantity(required) > 0
  || GetItemQuantity("stone_" + required) > 0` — exact + stone-prefix
  only. A colony-store `bone_fishing_rod` does NOT satisfy
  `fishing_rod`; needs a `bone_` arm (mirrors the stone_axe store
  convention).
- `wolf_bone` item exists (`items.json:495`, monster drop — wolf).
  `grass_rope` proven input. No `bone_fishing_rod` anywhere (grepped).
- Cast duration seam: the Fishing branch of `StartAction`
  (`:163-186`) sets `DurationRemaining = Constants.FishingCastSeconds`.
  The tool check at `:77` already calls `FindEquippedTool` but discards
  the id — hoist and reuse it in the Fishing branch to pick the tier's
  cast length. `ActiveAction` needs no new field (duration is already
  the lever).
- Crafting gate is data-only (`CraftingSystem.cs:32`: RequiredSkill +
  RequiredLevel from the recipe). Existing rod recipe is crafting 2;
  bone rod at crafting 4 sits above the bucket (2) and below
  metallurgy-tier gear.
- Test harness: FishingTests is current (bait region freshest);
  `PlayerFishing_*` tests call `system.Update(0.25f)` loops with
  `Constants.FishingCastSeconds` — the tier test asserts against 2.0f
  explicitly.
- Data-count pins: none touch Items/Recipes (verified twice this arc).

## Goal

1. **Bone rod item**: `bone_fishing_rod` — equippable, essential tool,
   tool_type `fishing_rod`, durability 100, sprite `items/bone_wolf`
   (bone art reuse; a rod carved from bone).
2. **Bone rod recipe**: `craft_bone_fishing_rod` — wolf_bone×2 +
   grass_rope×1, crafting level 4, tier 1, wood_chain.
3. **Player tier lever**: casting with the bone rod holds 2s
   (Constants.BoneFishingCastSeconds) vs 3s with the base rod;
   notification names the rod ("You cast your bone rod...").
4. **Worker store arm**: `CanWorkerHarvest` gains `bone_` prefix —
   a colony-store bone rod satisfies the gate.

## Non-goals

- No rod durability wear (gate tokens, bucket/rod precedent).
- No third tier, no rare-drop table (next slice), no bait interactions
  with tier.
- No UI changes beyond the cast notification text.

## Design

`Data/items.json` (beside fishing_rod):

```json
{
  "id": "bone_fishing_rod",
  "name": "Bone Fishing Rod",
  "stack_size": 1,
  "is_equippable": true,
  "is_essential_tool": true,
  "is_food": false,
  "tool_type": "fishing_rod",
  "durability": 100,
  "sprite_key": "items/bone_wolf"
}
```

`Data/recipes.json` (beside craft_fishing_rod):

```json
{
  "recipe_id": "craft_bone_fishing_rod",
  "name": "Carve Bone Fishing Rod",
  "input_items": [["wolf_bone", 2], ["grass_rope", 1]],
  "output_item": "bone_fishing_rod",
  "output_quantity": 1,
  "xp_reward": 10.0,
  "processing_chain": "wood_chain",
  "tier": 1,
  "requires_campfire": false,
  "requires_structure": "",
  "skill": null,
  "season": "all",
  "skill_affects_yield": "crafting",
  "required_skill": "crafting",
  "required_level": 4
}
```

`Config/Constants.cs` (beside FishingCastSeconds):

```csharp
// Tier-2 rod (bone): a faster cast is the upgrade — same spot, less wait.
public const float BoneFishingCastSeconds = 2.0f;
```

`Actions/ActionSystem.cs`:

- Hoist the tool-check result: `string? matchedTool = null;` before
  `:74-80`; in the check `matchedTool = FindEquippedTool(...)`.
- Fishing branch: `bool boneRod = matchedTool == "bone_fishing_rod";
  action.DurationRemaining = boneRod ? Constants.BoneFishingCastSeconds
  : Constants.FishingCastSeconds;` and the cast notification picks
  "You cast your bone rod..." vs "You cast your line...".

`NPC/RecruitmentSystem.cs` `CanWorkerHarvest` store check:

```csharp
bool inColonyStore = colony != null && (
    colony.GetItemQuantity(required) > 0
    || colony.GetItemQuantity("stone_" + required) > 0
    || colony.GetItemQuantity("bone_" + required) > 0);
```

## Testing plan

New region in `FishingTests.cs`:

1. **Data shape** — bone rod item (tool_type fishing_rod, equippable,
   durability 100, bone_wolf sprite) + recipe (wolf_bone×2 +
   grass_rope×1, crafting 4, output bone_fishing_rod).
2. **Bone rod passes the player gate + casts faster** — only
   `bone_fishing_rod` in inventory (no base rod): StartAction succeeds
   (suffix match); mid-cast Update(1.0f) null, Update(1.0f) null
   (2.0s elapsed exactly → the closing-tick-is-catch-tick rule:
   loop `while (remaining > 0.25f)` over 2.0f then the final Update
   catches); message contains "bone rod" wording; quantity/xp
   unchanged (tier buys speed, not yield).
3. **Base rod still 3s** — regression: existing 3s tests already pin
   this (PlayerFishing_MidCastIsBusyAndHoldsSilence loops 2.9s and
   stays busy); no new test needed.
4. **Worker store bone rod satisfies the gate** — harness: fish_spot,
   `h.Colony.Store("bone_fishing_rod", 1)` (NO plain fishing_rod); tick
   40; raw_fish lands in store — proves the `bone_` store arm.

## Open questions

Answered with defaults per standing delegation:

- Tier effect = cast speed (2s), NOT yield or success — the window is
  the fishing-specific knob; yield is the bait economy's knob (clean
  separation, both economies stay relevant).
- Bone rod crafting 4 — above bucket (2), below metallurgy gear.
- Sprite: `items/bone_wolf` (bone-carved look; wolf bone is literally
  the ingredient).
- Player-path suffix match covers carried bone rods with zero code
  change; only the store arm + duration hook are code.

## Revision history

- 2026-10-09 draft.
