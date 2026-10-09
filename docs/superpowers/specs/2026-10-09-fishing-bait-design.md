# Fishing bait — prepared casts and colony bait logistics — design

Date: 2026-10-09
Status: implemented (RED `514357c`; GREEN `0207b00`)
Slice: next-arc continuation — "bait" (user-scoped after the rod slice:
"whatever pieces would make a fishing skill satisfyingly grindy in a
survival sandbox with colony dynamics"). User-confirmed scope: bait item +
recipe, ×2 player baited casts, workers burn colony bait for ×2.

## Design law

- **RS**: bait is the prep economy — a cheap craftable consumable that
  doubles catches. Fishing stops being "free food node" and becomes a
  prepared activity: shells → bait → doubled hauls.
- **DS**: the grind stays honest — bait is consumed per cast (one bait =
  one doubled catch), made from player-gathered coastal ingredients
  (shells ×2 + fibers ×1, crafting level 1 — entry tier, like the rod's
  tier). Unbaited casts still work at ×1 (bait is an economy, not a wall).
- **MC**: readability — the catch notification states the bait bonus
  ("+2 raw_fish (bait)"), so the doubled yield is self-explaining; workers
  mirror the meal/stock convention (bait in store = used, silently absent
  = normal ×1 fishing, no new UI).

## Ground — verified in the tree

- `shells` yield exists: `shell_beach` node (coastal, yield_item shells,
  xp 5, required_level 1 — resources.json); `fibers` from `fiber_plant`
  (forest, xp 8). Both are level-1 gathers — entry-tier bait fits.
- `fibers` is already a recipe input (`grass_rope`: fibers ×3 — 
  recipes.json) — the crafting-consumption pipeline for it is proven.
- No `bait`/`fishing_bait` item exists anywhere (grepped items.json,
  recipes.json, src/).
- Player consumption seam: `ActionSystem.CompleteGathering`
  (`:349-365`) — stamina gate → success roll → `resource.Harvest()`.
  `Inventory.RemoveItem(itemId, qty)` (Inventory.cs:102) is the
  consumption API; `GetItemQuantity` (:129) the check. The Fishing branch
  of `StartAction` (`:163-177`) builds the action — the natural seam to
  record "this cast is baited" is `ActiveAction` (add `Baited` bool +
  consume-at-start so the bait is spent the moment the line is cast —
  honest: no refund on walk-away cancel, the bait sank with the cast).
- Worker consumption seam: `colony.RemoveItem(itemId, qty)` — established
  idiom (meals `:180`, construction materials `:262`, wheat `:1496`).
  Worker harvest path: `RecruitmentSystem :570-645` — `HarvestInterval`
  gate → `CanWorkerHarvest` → `resource.Harvest(1f)` → per-recruit level
  bonus (deterministic counter, `:604-618`) → haul home.
- The rod-gate tests (`FishingTests.cs`) are the harness model; the
  worker fisher test (`WorkerFishing_RodInColonyStore...`) already stores
  `fishing_rod` — the same test extends with bait.
- Data-count pins: none touch Items/Resources/Recipes (verified in the
  rod slice) — new bait item/recipe breaks no pins.
- Quest hook: `QuestSystem.NotifyCollect` fires on every player catch
  (Game.cs:832-833) — unaffected by quantity changes.
- Sprite: `items/worm_segment` exists (sand_worm drop) — worm-segment art
  is the natural bait stand-in (no new art; a worm IS bait, thematically
  perfect).

## Goal

1. **Bait item**: `fishing_bait` — stack 20, non-equippable, non-food,
   sprite `items/worm_segment`.
2. **Bait recipe**: `craft_fishing_bait` — shells ×2 + fibers ×1,
   crafting level 1, tier 1, ×4 output (a batch, like berries→jar; keeps
   the per-cast cost cheap without trivializing gathering).
3. **Player baited cast**: `StartAction` Fishing branch — if
   `fishing_bait` in inventory, consume 1 at cast start, set
   `action.Baited = true`; `CompleteGathering` doubles the catch quantity
   when `Baited` and the notification says "(bait)".
4. **Worker baited harvest**: worker fisher at a fish_spot — if
   `fishing_bait` in colony store, consume 1 per harvest tick and double
   the haul quantity. Silent, deterministic, colony-logistics-visible.

## Non-goals

- No unbaited-cast failure (bait is a yield economy, not a gate).
- No bait types/tiers (worm vs fly vs lure) — one bait this slice.
- No bait in trade/merchant stock, quests, or NPC defs.
- No rod-tier work (bone rod is the next slice).
- No UI changes beyond the catch notification text.

## Design

### Data changes

`Data/items.json`:

```json
{
  "id": "fishing_bait",
  "name": "Fishing Bait",
  "stack_size": 20,
  "is_equippable": false,
  "is_essential_tool": false,
  "is_food": false,
  "sprite_key": "items/worm_segment"
}
```

`Data/recipes.json` (beside craft_fishing_rod):

```json
{
  "recipe_id": "craft_fishing_bait",
  "name": "Mix Fishing Bait",
  "input_items": [["shells", 2], ["fibers", 1]],
  "output_item": "fishing_bait",
  "output_quantity": 4,
  "xp_reward": 4.0,
  "processing_chain": "wood_chain",
  "tier": 1,
  "requires_campfire": false,
  "requires_structure": "",
  "skill": null,
  "season": "all",
  "skill_affects_yield": "crafting",
  "required_skill": "crafting",
  "required_level": 1
}
```

### Code changes

**`Actions/ActiveAction.cs`** — one field:

```csharp
/// <summary>True when this cast consumed fishing bait (doubled catch).</summary>
public bool Baited { get; set; }
```

**`Actions/ActionSystem.cs`** — Fishing branch of `StartAction`, after the
duration/notification block:

```csharp
// Baited cast: one bait per cast, consumed the moment the line hits the
// water (no refund on walk-away — the bait sank with the cast).
action.Baited = inventory.RemoveItem("fishing_bait", 1);
if (action.Baited)
    AddNotification("Baited hook...", (150, 220, 160));
```

**`Actions/ActionSystem.cs`** — `CompleteGathering`, in the success path
right after `quantity` is finalized (after the skill-class yield calc,
before `ActionResult.SuccessResult`):

```csharp
// Baited fishing casts land a doubled catch.
if (action.ActionType == ActionType.Fishing && action.Baited)
    quantity *= 2;
```

And the notification: `SuccessResult` gains an optional suffix or the
message is built here — simplest: in `CompleteGathering`, when baited,
message = `$"Harvested {quantity} {action.YieldItem} (bait)."`.

**`NPC/RecruitmentSystem.cs`** — worker harvest path (`:604` area), after
the per-recruit level-bonus block, before haul-home:

```csharp
// Colony bait: a fisher burns one bait per harvest for a doubled haul —
// the same stock-consumption idiom as meals and construction materials.
if (quantity > 0 && resource.ResourceDef?.ToolRequirement == "fishing_rod"
    && colony is { IsFounded: true } baitColony
    && baitColony.RemoveItem("fishing_bait", 1))
    quantity *= 2;
```

(RemoveItem returns false when not stocked — unbaited is the silent
default; no else branch, no UI.)

### What this design does NOT change

- `CanWorkerHarvest` — bait is not a gate, just an economy.
- Rod gate, cast duration, walk-away cancel — untouched.
- `fish_spot` def — unchanged (xp 15, level 1, rod gate).
- Save format — `Baited` is transient on the running action; not
  persisted.

## Testing plan

New region in `FishingTests.cs`:

1. **Data shape** — bait ItemDef loads (stack 20, worm_segment sprite,
   non-equippable); recipe loads (shells×2 + fibers×1, crafting 1,
   output fishing_bait ×4).
2. **Player baited cast doubles the catch** — rod + 1 bait in inventory;
   cast; wait out the window; result.Quantity == 2; inventory has 0 bait
   left; message contains "(bait)".
3. **Player unbaited cast unchanged** — rod, no bait → quantity 1; 
   regression leg (pre-greens; pipeline confirmation).
4. **Bait consumed at cast start, not on catch** — rod + 1 bait; cast;
   IMMEDIATELY RemoveItem-check: bait gone before the window closes.
   Then walk-away-cancel semantics: no refund (bait stays gone after
   CancelActive).
5. **Worker baited harvest doubles the haul** — harness: colony founded,
   fish_spot, `fishing_rod` + `fishing_bait` ×2 in store; tick 40;
   raw_fish in store ≥ bait count used (with 2 bait, ≥4 fish across two
   harvests — actually assert: store raw_fish ≥ 2·(bait consumed) is
   hard to pin deterministically with depletion_count 2; assert the
   simpler invariant: raw_fish ≥ 2 with bait vs the no-bait baseline
   test's ≥ 1, plus bait stock DECREASED).
6. **Worker no-bait regression** — existing
   `WorkerFishing_RodInColonyStore_HarvestsAndTrainsFishing` already
   covers unbaited ×1 (store only rod) — unchanged, still green.

## Open questions

Answered with defaults per user-confirmed scope:

- Bait consumed at cast START (sinks with the cast; no refund on cancel)
  — honest-grind reading of the DS law.
- Output ×4 per craft (shells×2+fibers×1) — a batch keeps per-cast cost
  cheap (shells are xp-5 trash-tier) without trivializing coastal
  gathering.
- Worker bait: silent consumption, doubled haul, no per-harvest
  notification (the stock ledger is the feedback, per the meal
  convention).
- Sprite: `items/worm_segment` (thematic: a worm is bait).
- No trade/quest hooks this slice.

## Revision history

- 2026-10-09 draft.
