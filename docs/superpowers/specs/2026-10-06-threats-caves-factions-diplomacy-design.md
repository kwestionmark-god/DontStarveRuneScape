# Threats, Caves, Factions, Diplomacy — Design Spec

- **Date:** 2026-10-06
- **Status:** draft; awaiting review before implementation plan
- **Slice line:** roadmap phase-4 final open line — "Connect threats, caves,
  factions, and diplomacy to settlement defense and trade without making
  ordinary play feel like an RTS management chore."
- **Design law:** tri-fusion balance rule — the settlement reacts to the
  world; the player explores and fights. Automation supports, never
  replaces, the player's choices.

## Goal

Wire the existing threat/faction/cave systems into a living settlement
loop that:

- Guards and workers respond to **faction standing changes** (friendly
  merchants visit, hostile monsters raid) without the player clicking
  through menus.
- The **cave expedition** is a player choice with consequences: leaving
  the surface unguarded attracts raids, but bringing guards underground
  weakens the home defense.
- **Diplomacy is visible** — standing shifts trigger world reactions
  (trade prices, merchant visits, raid likelihood) and are readable in
  the colony dashboard.
- **Trade at the settlement** — faction merchants arrive periodically
  with biome-appropriate stock; the player buys/sells without leaving
  the colony anchor.

## Non-goals (this slice)

- No RTS unit-control (no "select guard, right-click monster").
- No cave settlement / underground colony building — cave remains an
  expedition map for the player.
- No new combat mechanics — existing CombatSystem handles fights.
- No multiplayer / diplomacy with other player factions.

## Ground — verified in the tree 2026-10-06

- **FactionSystem** (`NPC/FactionSystem.cs`) — standing 0..1 per faction,
  negotiation raises standing via intelligence/persuasion; snapshot
  persists standings as percents.
- **TradeSystem** (`NPC/TradeSystem.cs`) — prices modified by
  `MerchantStanding` (faction standing scales buy/sell ±50%); gold
  currency item; per-merchant stock pools; commerce sub-stat gates
  premium stock.
- **QuestSystem** (`NPC/QuestSystem.cs`) — acceptance gates on faction
  standing, intelligence level, commerce/persuasion sub-stats; hooks
  `NotifyCollect/Craft/Kill/Trade/Negotiate` wired by game.
- **Threat filter** (`RecruitmentSystem.IsSettlementThreat`) — hostile
  monsters are settlement threats if their type matches a faction with
  `BaseHostility + (DefaultStanding - Standing) >= 0.5`. Already used
  in guard target selection.
- **Guards** (`TickGuard`) — patrol 4 points around the anchor, intercept
  hostile monsters in a 6-tile (colony) / 4-tile (no colony) radius.
- **CaveWorldSystem** (`World/CaveWorldSystem.cs`) — player enters/exits
  cave; workers teleport to cave spawn on enter, return to surface
  positions on exit; cave has its own CombatSystem and MonsterRegistry.
- **Settlement clock** — `DayNightCycle` (already used by schedules).
- **Colony dashboard** — `Panels2.cs` tabs (inventory/skills/crafting/
  quests/diplomacy) with `_colonyStatus` message line.
- **Worker schedules** — assistants have FREE_TIME/NOURISHMENT/SLEEP
  slots; guards split into day/night shift squads.

## Design

### 1. Cave-expedition consequence: surface unguarded

When the player enters a cave, surface workers are teleported to the
cave spawn but guards **stay on the surface** (they defend the colony).
If the player brings guards into the cave (new: "assign guard to
expedition" action in the colony dashboard), the surface defense radius
shrinks. A global "surface under threat" timer ticks while the cave
is active and no guards are on the surface; every 5 minutes it rolls
a raid chance based on nearby hostile faction standing.

Implementation: `CaveWorldSystem.Enter` — don't teleport RecruitBehavior
== "guard" unless a new `BringGuards` flag is set. Add a `SurfaceUnguardedTimer`
that increments each tick while `IsInside && !HasSurfaceGuards()`; on
threshold, spawn a raid squad at colony perimeter (reuse `CombatSystem.
SpawnMonster` with faction-matched types).

### 2. Faction-diplomacy → world reactions

Standing changes already modify `TradeSystem` prices and `IsSettlementThreat`.
Add three visible hooks:

- **Merchant visits**: every in-game day (06:00), for each faction with
  standing ≥ friendly (0.65) and a merchant defined, a `MerchantNpc`
  spawns at the colony anchor with a 4-hour visit window. They use the
  existing `TradeSystem` rows for that faction/biome. Despawn at window
  end or when player trades with them.
- **Raid probability**: daily at 06:00, for each hostile faction
  (standing < 0.25) with territory overlapping the colony biome,
  roll `0.15 * (0.25 - standing)` chance to spawn a raid party at
  colony edge. Raid party = 2–4 monsters from the faction's
  `HostileMonsterTypes`, path to the anchor.
- **Status message**: `_colonyStatus` in the dashboard shows "Merchants
  from X arrived" / "Raid incoming" / "Tensions with X rising" on
  standing change events.

### 3. Settlement merchant UI (no new screen)

The colony dashboard's diplomacy tab already lists factions and standing.
Add a sub-row per faction: if a merchant is visiting, show a "Visit
Merchant" button that opens the existing trade panel (via the same
`TradeSystem` path used by static merchants).

### 4. Guard assignment to cave expedition

In the colony dashboard's worker list, for each guard show a "Cave
Expedition" button (only when `CaveWorldSystem.IsInside` is true).
Clicking it moves that guard to the cave CombatSystem (with the player)
and marks `OnExpedition = true`. The surface timer activates. On cave
exit, the guard returns to surface position. If the guard dies, the
expedition slot frees.

### 5. Worker-schedule integration (already there)

Guards on night shift still patrol surface while player is in cave.
Assistants keep their schedules. Night floor (no outdoor work at night)
already applies on the surface; cave has no clock so cave workers
(only the player, typically) keep expedition behavior.

### 6. Save/restore (no new formats)

- `FactionSystem` snapshots standings (already).
- `CaveWorldSystem` needs a snapshot: `IsInside`, `_entranceX/Y`,
  `_surfaceWorkerPositions`, `OnExpedition` guard list, surface timer.
- Merchant visits are runtime-only (reseed on load from standing).

## Testing plan

- **Guard stays on surface by default** — enter cave, verify guards
  remain on surface map, workers teleport.
- **Bring-guards action** — click "Cave Expedition" on a guard, verify
  guard moves to cave CombatSystem; surface unguarded timer starts.
- **Merchant visit spawn** — set standing ≥ 0.65, tick to 06:00, verify
  MerchantNpc at anchor with correct TradeSystem stock.
- **Raid roll** — set standing < 0.25, tick to 06:00, verify raid party
  spawns at perimeter.
- **Diplomacy tab buttons** — "Visit Merchant" opens trade; "Cave
  Expedition" moves guard.
- **Save/load** — save mid-expedition with guard underground, load,
  verify guard in cave and surface timer restored.

## Open questions for review (resolved — defaults picked)

1. **Merchant spawn location**: colony anchor (safe/simple).
2. **Raid party AI**: generic path-to-anchor attack (reuse existing
   `IsSettlementThreat` filter; guards already intercept).
3. **Expedition guard cap**: one guard per expedition — if ≥2 guards
   recruited, at least one stays on surface; if only 1, player chooses
   surface or cave.
4. **Surface timer cadence**: 5 minutes real-time (≈5 game hours at the
   12-min day cycle).
5. **Cave guard behavior**: follow-and-protect — guard stays within
   4 tiles of the player and intercepts hostile monsters in that radius
   (reuse `IsSettlementThreat`).