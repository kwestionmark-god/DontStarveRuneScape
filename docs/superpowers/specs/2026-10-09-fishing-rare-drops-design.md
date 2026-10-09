# Fishing rare-drop table — pearl + old boot — design

Date: 2026-10-09
Status: implemented (RED `4f9acfa`; GREEN `3ae444b`)
Slice: next-arc item 1 (handoff: "fishing rare-drop table (pearl/old
boot, level-scaled) — the keep-casting OSRS hook"). The catch pipeline
gains a second, rarer reward channel: most casts land fish, some dredge
up junk, and rarely — a pearl worth real gold. Progression answer to
"why keep casting at level 40."

User decisions (clarified 2026-10-09, before drafting):

- Pearl is merchant-sellable (a coastal trade entry) — the rare drop
  is the fishing payday, not just a trophy.
- Workers roll the same table (their rares land in the colony store,
  the same lever as the bait double).
- Full inventory: no preference on what drops, but the loss must be
  VISIBLE ("let the drop be visible, thereby manageable") — never a
  silent vanish.

## Design law

- **RS**: level-scaled drop table — the pearl window grows with the
  fishing level (1% at L1 → ~5.4% at L30), so the grind compounds:
  better fisher, luckier water. The table is pure math, no new systems.
- **DS**: the grind stays honest — rares are never bought or crafted,
  only dredged; a full pack LOSES the pearl with a red message (the
  honest cost of fishing with a full bag); the old boot is vendor junk
  (the OSRS gag drop — most rares are trash, that's what makes the
  pearl real).
- **MC**: readability — the catch message names the dredge ("You
  dredged up a pearl!"), the grant gets the gold notification color,
  worker rares surface in the stock ledger (no new UI).

## Ground — verified in the tree

- Player catch pipeline: `CompleteGathering`
  (`src/DontStarveRuneScape/Actions/ActionSystem.cs:345-438`) —
  success roll `:373-375` (50% + `fishing.success_rate`), bait double
  `:421-423`, `SuccessResult` message `:426-432`. The rare roll slots
  in after the bait double, inside the success branch.
- Level at cast time: `CompleteGathering` has NO SkillManager param —
  stash the fishing level at cast start in the Fishing branch of
  `StartAction` (`:165-198`, skillManager in scope), new
  `ActiveAction.FishingLevel` (Baited at `:54-56` is the field
  precedent).
- Result carrier: `ActionResult` (`Actions/ActionResult.cs:29-45`) is
  immutable — add an optional `RareItemId` param; existing callers
  unaffected (optional).
- Grant + full-inv surface: `ProcessCompletion`
  (`ActionSystem.cs:253-274`) — `CanAdd` fail branch `:253-256`
  ("Inventory is full — nothing was gathered."), grant + gold
  notification `:259-260`, level-up gold `:266-268`, message shown
  `:273-274`.
- Worker fisher: harvest block (`NPC/RecruitmentSystem.cs:601-670`) —
  bait double `:622-632` is the placement precedent, per-recruit
  fishing level lookup `:606-611`, landing (store vs player inv)
  `:633-658`. Needs `using DontStarveRuneScape.Actions;` (not in the
  current header, `:3-11`).
- Colony store: `ColonySystem.cs:39-48` CanStore/Store,
  `:54-55` GetItemQuantity — the ledger is the worker-rare surface.
- Test-hook precedent: `MerchantVisitSystem.cs:25-29`
  (`bool? RaidRollOverride` + `Random? RaidRandom`) — mirror as
  `string? ForceRareDrop` (null = random, "" = forced none) +
  `Random? RareDropRandom` per system.
- Trade: `TradeSystem.cs:66-74` SellPriceFor matches by `ItemId` across
  ALL biomes; `ExecuteBuy :99-101` gates on stock — so a stock-0 entry
  is sell-only (never buyable). `ExecuteSell :121+` pays from the
  merchant's gold pool. TradeItemDef shape: `Data/Trade.cs:11-42`.
- Data pins: `NpcDataTests.cs:69` pins `TradeItems.Count == 42` → 43
  (a DATA reason). NO items-count pin exists (grepped — the only
  count pins are TradeItems 42 and quest counts).
- Sprites (reuse, no new art): `items/moonstone` (items.json:1099 —
  pale round stone reads pearl), `gear/leather_boots`
  (items.json:1814 — an old boot is an old boot).
- No `pearl` / `old_boot` id anywhere (grepped; only `*_boots` gear).
- Harness: FishingTests.cs:251-330 (Harness + Tick), `PumpSuccessRate`
  `:48-49`, forced-cast loop shape `:455-465`.
- No existing FishingTests assert exact full messages (all Contains) —
  an appended rare clause cannot break them; non-fishing gathers never
  roll (roll is gated on `ActionType.Fishing`).

## Goal

1. **Items**: `pearl` (stack 5, moonstone sprite) and `old_boot`
   (stack 5, leather-boots sprite) — found only, never crafted.
2. **Trade entry**: `coastal_pearl` — sell 25 gold, buy 60, stock 0
   (sell-only; the payday merchant leg).
3. **Pure table**: `FishingRareTable.Roll(roll, level)` — pearl window
   `[0, 0.01 + 0.0015·(level−1))`, then old_boot window
   `[…, +0.04 + 0.002·(level−1))`, else nothing. One roll per
   successful catch, level frozen at cast start.
4. **Player wiring**: catch message gains "You dredged up a
   <rare>!" when the roll hits; `ProcessCompletion` grants the rare
   (gold notification) or reports the visible full-inv loss.
5. **Worker wiring**: same table, recruit's own fishing level, rare
   lands where the fish lands (store ledger or player inv), roll
   gated on rod nodes only.

## Non-goals

- No pearl recipes/amulets, no boot use (junk stays junk — future
  slice), no casket/second container roll.
- No bait × rare interaction — bait doubles FISH (yield economy);
  the rare roll is one per catch regardless (level economy).
- No bonus XP for rares (XP is per-catch, unchanged).
- No new rendering/smoketest (no visual surface changes).
- Worker full-store edge: a rare that can't Store is lost (same as the
  fish's own canReceive gate); noted, not surfaced per-tick.

## Design

`Actions/FishingRareTable.cs` (new, pure):

```csharp
namespace DontStarveRuneScape.Actions;

/// <summary>
/// Fishing rare-drop table — one roll per successful catch, scaled by
/// the fisher's level at cast start. Pearl first (the payday), then
/// the old boot (the gag), then nothing. Pure: no RNG lives here.
/// </summary>
public static class FishingRareTable
{
    public const float PearlBaseChance = 0.01f;
    public const float PearlPerLevel = 0.0015f;
    public const float BootBaseChance = 0.04f;
    public const float BootPerLevel = 0.002f;

    public static string? Roll(float roll, int level)
    {
        int l = Math.Max(1, level);
        float pearl = PearlBaseChance + PearlPerLevel * (l - 1);
        if (roll < pearl) return "pearl";
        if (roll < pearl + BootBaseChance + BootPerLevel * (l - 1))
            return "old_boot";
        return null;
    }
}
```

`Actions/ActiveAction.cs` (beside Baited):

```csharp
/// <summary>Fishing level at cast start — the rare-table input
/// (frozen per cast, OSRS-style).</summary>
public int FishingLevel { get; set; } = 1;
```

`Actions/ActionResult.cs`: ctor + property `string? RareItemId = null`;
`SuccessResult` gains the optional param.

`Actions/ActionSystem.cs`:

- Hooks (RaidRollOverride precedent):

```csharp
/// <summary>Test hook: forces the next rare-roll result
/// (null = random, "" = forced no-drop).</summary>
public string? ForceRareDrop { get; set; }
/// <summary>Seeded RNG for the rare roll (tests; null = new per catch).</summary>
public System.Random? RareDropRandom { get; set; }
```

- Fishing branch of `StartAction`: `action.FishingLevel =
  skillManager.GetSkillLevel("fishing");`
- `CompleteGathering`, after the bait double:

```csharp
string? rareItemId = null;
if (action.ActionType == ActionType.Fishing)
{
    rareItemId = ForceRareDrop != null
        ? (ForceRareDrop.Length == 0 ? null : ForceRareDrop)
        : FishingRareTable.Roll(
            (RareDropRandom ?? new System.Random()).NextDouble(),
            action.FishingLevel);
}
string message = baited
    ? $"Harvested {quantity} {action.YieldItem} (bait)."
    : $"Harvested {quantity} {action.YieldItem}.";
if (rareItemId != null)
    message += $" You dredged up a {rareItemId}!";
return ActionResult.SuccessResult(
    action.YieldItem, quantity, action.XpReward, message, rareItemId);
```

- `ProcessCompletion`: full branch adds
  `AddNotification($"No room — the {result.RareItemId} slipped back into the water!", (255,100,100))`
  when a rare was incoming; success branch, after the fish grant:

```csharp
if (!string.IsNullOrEmpty(result.RareItemId))
{
    if (inventory.AddItem(result.RareItemId, 1))
        AddNotification($"+1 {result.RareItemId} — a rare find!", (255, 215, 0));
    else
        AddNotification($"No room — the {result.RareItemId} slipped back into the water!", (255, 100, 100));
}
```

`NPC/RecruitmentSystem.cs` (after the bait double, inside the
quantity>0 block; `using DontStarveRuneScape.Actions;` added):

```csharp
// Rare drop: the fisher's own fishing level scales the same table
// the player rolls — one roll per successful haul, rod nodes only.
if (resource.ResourceDef?.ToolRequirement == "fishing_rod"
    && npc is RecruitNpc rareRecruit)
{
    string? rare = ForceRareDrop != null
        ? (ForceRareDrop.Length == 0 ? null : ForceRareDrop)
        : FishingRareTable.Roll(
            (RareDropRandom ?? new Random()).NextDouble(),
            rareRecruit.Skills.GetSkillLevel("fishing"));
    if (rare != null)
    {
        if (colony is { IsFounded: true } rareColony)
            rareColony.Store(rare, 1);
        else
            player.Inventory.AddItem(rare, 1);
    }
}
```

plus the mirrored `ForceRareDrop` / `RareDropRandom` hooks.

`Data/items.json` (beside fishing_bait):

```json
{
  "id": "pearl",
  "name": "Pearl",
  "stack_size": 5,
  "is_equippable": false,
  "is_essential_tool": false,
  "is_food": false,
  "sprite_key": "items/moonstone"
},
{
  "id": "old_boot",
  "name": "Old Boot",
  "stack_size": 5,
  "is_equippable": false,
  "is_essential_tool": false,
  "is_food": false,
  "sprite_key": "gear/leather_boots"
}
```

`Data/trade_items.json` (beside the coastal entries):

```json
{
  "trade_item_id": "coastal_pearl",
  "item_id": "pearl",
  "biome": "coastal",
  "buy_price": 60,
  "sell_price": 25,
  "stock_quantity": 0,
  "max_stock": 0,
  "tier": 1,
  "is_premium": false,
  "commerce_requirement": 0
}
```

`Tests/NpcDataTests.cs`: `Assert.Equal(42, …TradeItems.Count)` → 43.

## Testing plan

New region in `FishingTests.cs`:

1. **Data shape** — pearl/old_boot items load (stack 5, sprites, not
   equippable/tool/food); `coastal_pearl` trade entry (sell 25,
   buy 60, stock 0).
2. **Pure table** — `Roll(0.005f, 1)` = pearl; `Roll(0.02f, 1)` =
   old_boot; `Roll(0.06f, 1)` = null; `Roll(0.03f, 30)` = pearl
   (level-scaling proof: same roll, low level says boot, high level
   says pearl); `Roll(0.999f, 99)` = null.
3. **Forced player pearl** — `ForceRareDrop = "pearl"`, forced
   success: `result.RareItemId == "pearl"`, message Contains
   "dredged"; after `ProcessCompletion` the pearl is in inventory and
   the gold rare notification fired (FlushNotifications first). Same
   for old_boot.
4. **Level stash** — pump fishing to a known level, StartAction,
   assert `system.Active.FishingLevel` mid-cast (pure wiring, no RNG).
5. **No-leak regression** — `ForceRareDrop = ""`: normal catch,
   `RareItemId` null, message unchanged shape.
6. **Full-inv visible loss** — fill all 20 slots (stack-1 items),
   forced pearl catch, `ProcessCompletion`: pearl NOT in inventory,
   flushed notifications contain the "slipped back into the water"
   line (red color). The loss is visible, never silent.
7. **Worker forced pearl** — harness fisher + store rod,
   `Recruits.ForceRareDrop = "pearl"`, tick 60: `pearl` lands in the
   colony store alongside the raw_fish (ledger visibility).
8. **Worker rod-gate regression** — miner on a pickaxe node with
   `ForceRareDrop = "pearl"`: no pearl ever (the roll is rod-only).
9. **Sell-only proof** — `SellPriceFor("pearl")` = 25; `ExecuteBuy` on
   `coastal_pearl` fails "Out of stock."
10. **Pin bump** — NpcDataTests TradeItems count 42 → 43.

## Open questions

Answered with defaults per standing delegation:

- Odds: pearl 1% + 0.15%/level, boot 4% + 0.2%/level — a ~5%
  "something happened" beat per catch at L1, payday odds that grow
  into the 30s. Pearl window first so the rarest reward owns the
  bottom of the range.
- Pearl sell 25 (above the 22 steel-ingot premium, below nothing —
  it IS the coastal payday); buy 60 never fires (stock 0).
- Old boot: no trade entry — junk with no market ("No market for that
  item."), the gag stays a gag.
- Level frozen at cast start (mirrors XpReward/Yield stashing; the
  roll describes the cast, not the tick).
- Worker rares bypass the haul walk (CarriedItemId is single-type;
  jewels aren't cargo) — direct Store, ledger-visible immediately.

## Revision history

- 2026-10-09 draft (post-clarify: sellable pearl, both paths,
  visible full-inv loss).
