# Individual stat menus — per-skill allocation catalogs — design

Date: 2026-10-09
Status: draft (decisions clarified 2026-10-09) → implemented
Slice: user-directed 2026-10-09 ("the stat point allocation menu is one size
fits all, that doesn't actually fit — individualise these allocation menus
based on integrative individualised parameters"). The skills panel currently
offers the same five sub-stats to every skill; most of them are dead for
most skills, seven skills have no wired sub-stat at all, and `attack` is not
even in the panel. This slice gives every skill its own honest, wired menu.

User decisions (clarified 2026-10-09, before drafting):

- **Scope: full breadth** — every skill gets its own wired menu in one
  slice; gatherers pruned to their real stats, attack gains a panel row +
  power/speed, production skills via the Craft seam, firemaking via the
  fire seams, agility via sprint/jump stamina.
- **Migration: auto-respec on load** — snapshot keys a skill's new menu
  drops return as unallocated points on that skill; nothing earned is
  lost; the migration is visible.
- **Attack: power + speed** — power adds flat damage, speed cuts the
  attack cooldown, mirroring the existing gear bonus seams; no crit.
- **Workers: player-only this slice** — worker math keeps its level-based
  loops; worker reads of their own sub-stats are a follow-up. (Player-stat
  effects that flow through shared seams are in scope — see Design law.)
- **Fishing third knob: rare luck** — boosts the pearl/boot rare-drop
  roll; no bait retention (the bait sink stays honest).

## Design law

- **RS**: every point buys something real — a stat appears on a skill's
  menu only if a live seam consumes it. Attack finally shows in the panel
  (levels were already banking invisible points). Menus are per-skill
  catalogs, single-sourced data the panel renders.
- **DS**: the grind stays honest — no free power: new sub-stats read
  *invested points only* (no per-level scaling on new knobs), so a fresh
  character's math is bit-identical to today; legacy keys keep their
  established consumers unchanged.
- **MC**: readability — the panel detail block lists each skill's own
  stats with human names; respec'd points surface as the green "stat
  points to spend" counter (plus a one-time load notification when the
  call site allows it).

## Ground — verified in the tree

- Shared five: `SkillData.SubStats` initializer
  (`src/DontStarveRuneScape/Skills/SkillManager.cs:213-220`) hardcodes
  success_rate / harvest_boost / extra_resources / efficiency /
  stamina_reduction for every skill. Panel shows all five plus
  intelligence extras via `StatsFor`
  (`src/DontStarveRuneScape/UI/Panels2.cs:499-518`).
- Panel rows 11 vs manager skills 12: `Panels2.cs:484-497` (no attack
  row) vs `SkillManager.cs:16-21`. Attack levels already grant points
  (`AddXpWithNotification` grants 3/level, `SkillManager.cs:86-90`) —
  invisible and unspendable today.
- Real consumers per key (grepped):
  - success_rate: woodcutting/mining/foraging via skill classes at ×100
    (`Actions/ActionSystem.cs:121,142,163`), **fishing directly at ×1**
    (`ActionSystem.cs:185`) — one point buys +100% threshold for the
    siblings but +1% for fishing.
  - harvest_boost: stashed for wc/foraging/fishing
    (`ActionSystem.cs:122,128,164,170,186`); consumed only for
    woodcutting/mining via `CalculateYield`
    (`Skills/Woodcutting/WoodcuttingSkill.cs:33-41`,
    `Skills/Mining/MiningSkill.cs:31-38`). **Dead for foraging**
    (`ForagingSkill.CalculateHarvest` ignores its bonus,
    `Skills/Foraging/ForagingSkill.cs:30-34`) and **dead for fishing**
    (yield branch list `ActionSystem.cs:417-428` has no Fishing arm).
  - extra_resources: mining only (`ActionSystem.cs:143,149`).
  - efficiency: wc/mining stamina cost
    (`WoodcuttingSkill.cs:18-23`, `MiningSkill.cs:18-23`).
  - stamina_reduction: foraging only (`ForagingSkill.cs:17-21`).
  - commerce/persuasion: trade/quest/recruit gates
    (`NPC/TradeSystem.cs:96`, `NPC/QuestSystem.cs:103-106`,
    `UI/Panels.cs:186`, `NPC/FactionSystem.cs:38`); seeded at 1
    (`SkillManager.cs:31-32`).
  - No consumers at all: attack, cooking, firemaking, crafting,
    metallurgy, construction, agility.
- Combat seams: `PlayerAttack` (`Combat/CombatSystem.cs:195-233`) —
  damage `weapon + bonus − defence` at `:218-220`; cooldown
  `max(0.3, 1.5 − speedBonus·0.3)` at `:226`
  (`Constants.CombatBaseAttackCooldown = 1.5` `Config/Constants.cs:313`).
- Sprint/jump seams: `Player.UpdateSprint` drain
  `Core/Player.cs:182`, `TryJump` cost `Player.cs:205`.
- Craft seam: `CraftingSystem.Craft` (`Crafting/CraftingSystem.cs:25-91`)
  — skill gate `:32`, grouped input consume `:76-77`, output `:79`, XP
  `:82`. Called by the panel (`Panels2.cs:924`) AND by worker stations
  (`NPC/RecruitmentSystem.cs:421`) — both pass the **player's**
  SkillManager, so a player-stat read inside Craft is a player-stat
  effect on the shared seam (in scope; workers' own stats stay unread).
- Fire seams: `FiremakingSkill.LightFire`
  (`Skills/Firemaking/FiremakingSkill.cs:41-69`) — fuel consume
  `:47-50`, duration flat 30s `:53`; caller `FireInteraction.cs:18-77`
  has `game.SkillManager` in scope (`:69`).
- Construction site seam: worker site ticks advance
  `WorkProgress += min(dt, 0.25)` and complete at ≥20
  (`NPC/RecruitmentSystem.cs:291-296`); the Tick already receives the
  player's `skills`.
- Rare-roll seam: `FishingRareTable.Roll(roll, level)` pure
  (`Actions/FishingRareTable.cs:28-36`); player call
  `ActionSystem.cs:456-464`; level stashed at cast start
  (`ActionSystem.cs:190`, `ActiveAction.FishingLevel` — the stash
  precedent for a luck twin).
- Snapshot: `GetSnapshot` saves current SubStats (`SkillManager.cs:168-182`);
  `RestoreSnapshot` copies only keys the skill knows and **silently drops
  the rest today** (`:194-198`) — the respec makes that loss visible and
  refundable.
- Tests touching the seam (must stay green or be updated in the same RED):
  `SkillManagerTests.cs:64-102` (SpendPoint paths — mining keeps
  efficiency; cooking "efficiency" spend fails on points, not on key),
  `SprintTests.cs:36` (exact stamina math — raw-0 must be bit-identical),
  `FishingTests.cs:49` (PumpSuccessRate), `ActionSystemTests.cs:35`.
- `skill_affects_yield` exists in `Data/recipes.json` (e.g. `:439`) with
  ZERO C# consumers (grepped) — dead data, do not reuse; craft stat reads
  key on `recipe.RequiredSkill` instead.
- Real recipe ids per skill live in `Data/recipes.json` (crafting,
  cooking `:437-481`, metallurgy `:518+`) — grep exact ids before writing
  fixtures (house pitfall).
- No Data/*.json changes in this slice → no `NpcDataTests` count-pin
  bumps (the pins are data-driven).
- Panel geometry: `Panels2.cs:520-531` (ContentH 520, RowH 42,
  `_statY` sized 7) and `Layout` `:764-782`.

## Goal

1. **Catalog (single source of truth)**: `SkillManager.SubStatCatalog`
   maps every skill id to its ordered stat keys;
   `SkillManager.SubStatNames` maps every key to a display name.
   `SkillData.SubStats` is populated from the catalog (empty initializer;
   the manager fills it), so logic and panel can never disagree.

   ```
   attack:       power, speed
   woodcutting:  success_rate, harvest_boost, efficiency
   mining:       success_rate, extra_resources, efficiency
   foraging:     success_rate, harvest_boost, stamina_reduction
   fishing:      success_rate, harvest_boost, rare_luck
   cooking:      harvest_boost, efficiency
   firemaking:   fuel_saver, duration
   crafting:     harvest_boost, efficiency
   metallurgy:   harvest_boost, efficiency
   construction: build_speed, efficiency
   intelligence: commerce, persuasion
   agility:      sprint_cost, jump_cost
   ```

   Pruned everywhere they were dead: extra_resources/stamina_reduction
   off non-consumers; the generic five off intelligence (commerce and
   persuasion stay, still seeded at 1).

2. **Raw-point reads for new knobs**: `SkillManager.GetSubStatPoints(skillId, statName)` returns invested points with no level scaling. Every NEW consumer uses it, so 0-point characters keep exact current math (no flake shifts in pinned tests). Legacy keys keep their `GetEffectiveStat` consumers unchanged.

3. **Auto-respec migration**: `RestoreSnapshot` returns the number of
   points it refunded — dead snapshot keys add their value to that
   skill's `UnallocatedPoints` instead of vanishing. The player-load call
   site posts one notification when > 0 (ground the exact caller in RED;
   if the site has no notification channel, the panel's green counter is
   the visible surface).

4. **Attack wired + visible**: panel gains an Attack row (12 rows,
   ContentH grows accordingly); power adds flat damage, speed cuts the
   attack cooldown (−0.12s/pt, same 0.3s floor as gear) in `PlayerAttack`.

5. **Agility wired**: sprint_cost cuts sprint drain (−5%/pt, cap 75%),
   jump_cost cuts the jump cost (−5%/pt, cap 75%) — raw reads inside
   `UpdateSprint`/`TryJump`.

6. **Production wired via the Craft seam**: harvest_boost = chance of
   +1 output (1%/pt, raw) on any recipe; efficiency = chance to save one
   unit of the recipe's largest input (1%/pt, raw). Both inside
   `CraftingSystem.Craft`, keyed on `recipe.RequiredSkill` — so player
   cooking/crafting/metallurgy points also lift worker-station output
   through the shared seam (player-stat driven; founder competence shapes
   the colony, consistent with the colony-skill-structure slice).

7. **Firemaking wired**: duration adds +5s/pt to the 30s fire;
   fuel_saver gives a 4%/pt chance to refund one unit of the first
   consumed fuel. `LightFire` gains an optional `SkillManager?` param
   (null = legacy behavior); `FireInteraction` passes it.

8. **Construction wired**: build_speed multiplies worker site progress
   (+5%/pt, raw) in the `RecruitmentSystem` site tick — the founder's
   competence speeds the colony's builds. efficiency = 1%/pt chance to
   save one material unit on direct (non-colony-site) placement in
   `BuildingSystem.PlaceStructure`.

9. **Gatherer honesty fixes**: fishing success_rate normalizes to the
   family ×100 scale (`ActionSystem.cs:185`); fishing harvest_boost —
   dead since the fishing slice — gets its yield arm (raw stash);
   foraging harvest_boost — set but never consumed — now flows through
   `ForagingSkill.CalculateHarvest(base, bonus)` mirroring CalculateYield
   (stash repointed to raw).

10. **Rare luck**: `FishingRareTable.Roll(roll, level, luck = 0f)` — luck
    widens the pearl and boot windows (+0.1%/pt each); the player's
    fishing rare_luck is stashed at cast start beside `FishingLevel`
    (`ActiveAction.FishingLuck`) and passed at the roll; the worker roll
    keeps default 0.

## Non-goals

- No worker reads of the workers' OWN sub-stats (follow-up slice).
- No renames of legacy keys (save compatibility trumps tidiness).
- No respec UI, no un-spend/refund beyond the one-time load migration.
- No crit (declined), no bait retention (declined — the bait sink stays).
- No tooltips/descriptions per stat (follow-up).
- The ×1 fallback paths for null skill-class instances
  (`ActionSystem.cs:127,148,169`) keep their current scale (open
  question below), and wc/mining's historical 1%-at-level-1 yield leak
  stays as-is — a balance pass is out of scope.
- No new art, sprites, or data JSON.

## Design

`Skills/SkillManager.cs`:

```csharp
/// <summary>Per-skill spendable sub-stats — the single source the panel
/// renders and SpendPoint admits. A key appears only if a live seam
/// consumes it.</summary>
public static readonly IReadOnlyDictionary<string, string[]> SubStatCatalog = new Dictionary<string, string[]>
{
    ["attack"] = ["power", "speed"],
    ["woodcutting"] = ["success_rate", "harvest_boost", "efficiency"],
    ["mining"] = ["success_rate", "extra_resources", "efficiency"],
    ["foraging"] = ["success_rate", "harvest_boost", "stamina_reduction"],
    ["fishing"] = ["success_rate", "harvest_boost", "rare_luck"],
    ["cooking"] = ["harvest_boost", "efficiency"],
    ["firemaking"] = ["fuel_saver", "duration"],
    ["crafting"] = ["harvest_boost", "efficiency"],
    ["metallurgy"] = ["harvest_boost", "efficiency"],
    ["construction"] = ["build_speed", "efficiency"],
    ["intelligence"] = ["commerce", "persuasion"],
    ["agility"] = ["sprint_cost", "jump_cost"],
};

/// <summary>Display names shared by all skills (keys are reused across
/// skills by design — one name per mechanic).</summary>
public static readonly IReadOnlyDictionary<string, string> SubStatNames = new Dictionary<string, string>
{
    ["success_rate"] = "Success rate",      ["harvest_boost"] = "Harvest boost",
    ["extra_resources"] = "Extra resources",["efficiency"] = "Efficiency",
    ["stamina_reduction"] = "Stamina reduction",
    ["power"] = "Power",                    ["speed"] = "Attack speed",
    ["rare_luck"] = "Rare luck",
    ["fuel_saver"] = "Fuel saver",          ["duration"] = "Fire duration",
    ["build_speed"] = "Build speed",
    ["commerce"] = "Commerce",              ["persuasion"] = "Persuasion",
    ["sprint_cost"] = "Sprint cost",        ["jump_cost"] = "Jump cost",
};
```

- Ctor: after `_skills[id] = new SkillData { Id = id };` populate
  `SubStats` from the catalog (`foreach (var k in SubStatCatalog[id]) data.SubStats[k] = 0f;`), then keep the intelligence seed (`:31-32`).
- `SkillData.SubStats` initializer (`:213-220`) becomes `= new();`.
- New raw reader:

```csharp
/// <summary>Raw invested points in a sub-stat — no level scaling. New
/// consumers read this so zero-point characters keep exact legacy math.</summary>
public float GetSubStatPoints(string skillId, string statName)
{
    return _skills.TryGetValue(skillId, out var skill)
        && skill.SubStats.TryGetValue(statName, out float v) ? v : 0f;
}
```

- `RestoreSnapshot` returns `int` (respec'd points): inside the SubStats
  copy loop, an unknown key adds its value to
  `skill.UnallocatedPoints` and counts; known keys copy as today.
  Callers ignoring the return still compile.

`UI/Panels2.cs` (SkillPanel):

- `Skills` array gains `("attack", "Attack", "At", 200, 64, 56)` first
  (12 rows); `ContentH` 520 → 580; `_statY` sized
  `SkillManager.SubStatCatalog.Values.Max(v => v.Length)` (3);
  `SubStats`/`IntelExtraStats`/`StatsFor` replaced by a catalog lookup:
  `StatsFor(id)` returns `(Key, Name)` pairs from
  `SubStatCatalog[id]` + `SubStatNames`. HandleKey/Update/RenderDetail
  keep their current shapes (they already call `StatsFor`).

`Combat/CombatSystem.cs` (`PlayerAttack`):

```csharp
float powerPts = skills.GetSubStatPoints("attack", "power");
float speedPts = skills.GetSubStatPoints("attack", "speed");
int damage = Math.Max(1, (int)MathF.Round(weapon + bonus + powerPts - target.Defence));
...
_playerAttackCooldown = MathF.Max(0.3f, Constants.CombatBaseAttackCooldown
    - speedBonus * Constants.CombatSpeedStatCooldownReduction - speedPts * 0.12f);
```

`Core/Player.cs`:

```csharp
// UpdateSprint
float sprintCut = Math.Min(0.75f, SkillManager?.GetSubStatPoints("agility", "sprint_cost") * 0.05f ?? 0f);
... pool.Consume(Constants.SprintStaminaDrainPerSecond * dt * (1f - sprintCut))
// TryJump
float jumpCut = Math.Min(0.75f, SkillManager?.GetSubStatPoints("agility", "jump_cost") * 0.05f ?? 0f);
if (pool == null || !pool.Consume(Constants.JumpStaminaCost * (1f - jumpCut))) ...
```

(0 points → multiplier exactly `1f` → bit-identical drain, keeping
SprintTests' exact Equal pins green.)

`Crafting/CraftingSystem.cs` (`Craft`, after gates, before consume):

```csharp
float yieldBonus = skillManager.GetSubStatPoints(recipe.RequiredSkill, "harvest_boost");
int extra = yieldBonus > 0f && new Random().NextDouble() * 100f < yieldBonus ? 1 : 0;
float saveBonus = skillManager.GetSubStatPoints(recipe.RequiredSkill, "efficiency");
// consume loop: for the largest input group, save one unit on a hit
```

`CanAdd`/`AddItem`/message use `recipe.OutputQuantity + extra`.

`Skills/Firemaking/FiremakingSkill.cs`:

```csharp
public FireResult LightFire(List<(string, int)> fuelQueue, Inventory inventory,
    float x, float y, Skills.SkillManager? skills = null)
{
    ... consume as today ...
    float durBonus = (skills?.GetSubStatPoints("firemaking", "duration") ?? 0f) * 5f;
    float duration = 30f + durBonus;
    ... 
    // fuel_saver: 4%/pt chance to refund 1 unit of the first fuel
    float save = skills?.GetSubStatPoints("firemaking", "fuel_saver") ?? 0f;
    if (save > 0f && new Random().NextDouble() * 100f < save * 4f)
        inventory.AddItem(fuelQueue[0].ItemId, 1);
}
```

`Interactions/FireInteraction.cs:62` passes `game.SkillManager`.

`Skills/Foraging/ForagingSkill.cs`:

```csharp
public int CalculateHarvest(int baseYield, float extraResourcesBonus)
{
    if (extraResourcesBonus > 0f && new Random().NextDouble() * 100f < extraResourcesBonus)
        return baseYield + 1;
    return baseYield;
}
```

`Actions/ActionSystem.cs`:

- `:164` foraging stash and `:186` fishing stash repoint:
  `GetSubStatPoints(...)` (both were dead-consumer lines; raw makes
  quantity math deterministic at 0 points).
- `:185` fishing success `* 1.0f` → `* 100.0f` (family scale).
- `:425-428` yield arms gain:
  `else if (action.ActionType == ActionType.Fishing) quantity = ForagingStyleRoll(action.ExtraResourcesBonus, quantity)` — concretely the same +1 roll as the sibling arms, inline.
- Fishing branch of `StartAction` stashes
  `action.FishingLuck = skillManager.GetSubStatPoints("fishing", "rare_luck");`
  beside `FishingLevel`; the roll call passes it.

`Actions/FishingRareTable.cs`:

```csharp
public const float PearlPerLuck = 0.001f;
public const float BootPerLuck = 0.001f;

public static string? Roll(float roll, int level, float luck = 0f)
{
    int l = Math.Max(1, level);
    float pearl = PearlBaseChance + PearlPerLevel * (l - 1) + PearlPerLuck * luck;
    if (roll < pearl) return "pearl";
    if (roll < pearl + BootBaseChance + BootPerLevel * (l - 1) + BootPerLuck * luck)
        return "old_boot";
    return null;
}
```

`NPC/RecruitmentSystem.cs` (`:291`): site tick gains

```csharp
float buildMult = 1f + skills.GetSubStatPoints("construction", "build_speed") * 0.05f;
constructionSite.WorkProgress += Math.Min(dt, 0.25f) * buildMult;
```

(raw → 0 points keeps 80-tick timing bit-identical for every existing test).

`Building/BuildingSystem.cs` (`PlaceStructure`, direct consume loop
`:100-107`): efficiency roll (1%/pt, raw) saves one unit of the first
material after the loop — the colony-site transfer path (`:76-84`) is
untouched (materials there are stored, not consumed).

`Actions/ActiveAction.cs`: `public float FishingLuck { get; set; } = 0f;`
beside `FishingLevel`.

## Testing plan

New `SubStatCatalogTests.cs` (RED first):

1. **Catalog completeness** — every SkillManager skill has a catalog
   entry; every catalog key has a display name; intelligence is exactly
   commerce+persuasion; attack is exactly power+speed.
2. **SkillData inits from catalog** — mining keys ==
   {success_rate, extra_resources, efficiency}; firemaking ==
   {fuel_saver, duration}; `stamina_reduction` absent from mining.
3. **SpendPoint rejects pruned keys** — firemaking/success_rate fails;
   attack/charisma fails.
4. **RestoreSnapshot respecs dead keys** — snapshot mining
   {stamina_reduction: 2, efficiency: 1}, StatPoints 5 → efficiency 1
   kept, stamina_reduction gone, UnallocatedPoints 7, returns 2.
5. **No dead keys → returns 0**, points untouched.
6. **Round-trip keeps new keys** — cooking harvest_boost 3 survives.
7. **Intelligence seed survives** — fresh manager: commerce == 1.

Domain additions (same RED batch):

8. **CombatTests**: power 5 pts → damage +5 (same weapon/monster);
   speed 10 pts → second attack lands after Tick(0.4s) where 0 pts
   still reads "not ready".
9. **AgilityJumpTests/SprintTests**: sprint_cost 10 pts → dt 0.5 drains
   1.0 (half); 0 pts → exact legacy 2.0 (existing pins stay green);
   jump_cost 10 pts → TryJump consumes 2.5.
10. **CraftingPipelineTests**: harvest_boost 200 on the recipe's skill →
    output +1; 0 pts → exact quantity; efficiency 100 → one input unit
    survives. (Grep real recipe ids first.)
11. **Firemaking**: duration 4 pts → fire MaxTime 50; fuel_saver 25 →
    one fuel unit refunded (inventory math).
12. **ColonyWorkerBrainTests-shape harness**: player construction
    build_speed 10 → site completes by tick 60; 0 pts → still
    incomplete at tick 60 (baseline needs 80).
13. **FishingTests**: `Roll(0.0105f, 1, 0)` = old_boot vs
    `Roll(0.0105f, 1, 1f)` = pearl (luck proof);
    `Roll(0.999f, 99, 50f)` = null; `Active.FishingLuck` stashed at cast
    start; fishing harvest_boost 200 → catch quantity 2, 0 pts → 1;
    success_rate 10 pts → 20 consecutive catches (normalization proof —
    flaky-fails at the old ×1 scale).
14. **ActionSystemTests**: foraging harvest_boost 200 → forage +1.
15. **BuildingPipelineTests-shape**: construction efficiency 100 → one
    material unit stays in inventory on direct placement.

Panel: smoketest screenshot of the skills panel before/after (capture
the baseline at HEAD first), pixel-diff — 12 rows, pruned detail block.

## Open questions

Answered with defaults per standing delegation:

- New knobs read raw points (no level scaling) — keeps every pinned
  exact-math test green and honest ("no free power"); the five legacy
  keys keep GetEffectiveStat's level leak where they already had it.
- Speed −0.12s/pt (10 pts hits the 0.3s floor); sprint/jump −5%/pt cap
  75%; build_speed +5%/pt; fire duration +5s/pt; fuel_saver 4%/pt; craft
  yield/save 1%/pt — all small enough that a point is a real but modest
  buy (3/level), matching the OSRS "many small choices" feel.
- Respec visibility: panel green counter always; one-time load
  notification if the load call site has a notification channel (ground
  in RED).
- The ×1 null-skill-class fallbacks stay — they only fire in harnesses
  without skill-class wiring; normalizing them buys no player-facing
  honesty.

## Revision history

- 2026-10-09 draft (post-clarify: full breadth, auto-respec, power+speed,
  player-only, rare luck).
