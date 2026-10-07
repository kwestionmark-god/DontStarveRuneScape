# Character Backgrounds — design

Date: 2026-10-07
Status: draft
Slice: post-roadmap frontier, item 3 (dev-map "Ordered next work" item 6)

## Design law

RS pillar: a character choice should matter early — the forester starts
with an axe (woodcutting lives), the prospector with a pickaxe (mining
lives), the scavenger with food (foraging lives). This mirrors OSRS's
"your start shapes your grind" without hard classes: all skills remain
open to everyone.
DS pillar: every background still starts vulnerable — one tool, one
torch, one food; no stat boosts, no gear advantages. The choice is
playstyle, not power.
MC pillar: re-use what exists — three starter packs are already defined
in StarterPack.cs but only "default" is reachable. This slice wires them
into the character-creation flow instead of inventing a class system.

## Goal

Character creation gains a background choice: Wanderer (the current
default), Forester, Prospector, or Scavenger. The selection flows through
the existing `CharacterSelectPanel` → `StartNewGame` →
`PendingCharacterDef` → `Bootstrap` path, so the chosen background
applies its starter pack (already defined in `StarterPack.StarterPacks`)
at world build. The panel shows the four options with a one-line play
hint each; keyboard (Up/Down/Left/Right + Enter) and mouse both select.
The chosen background persists in the save (`CharacterName` slot gains
`CharacterBackground`), so the load screen and the save-slot list can
read it.

## Non-goals

- Starting stat allocations (`CharacterDefinition.StartingStats` stays
  unused — no power creep this slice).
- Appearance options, gender, sprites per background.
- New items or recipes — strictly existing pack contents.
- Mid-game re-selection; background is a creation-time fact.

## Ground — verified in the tree

- `StarterPack.StarterPacks` already defines forester (axe, torch,
  berries), prospector (pickaxe, torch, raw_meat), scavenger (torch,
  berries, raw_fish), default (torch, berries, raw_fish)
  (StarterPack.cs:32-38); `ApplyStarterPack(inventory, packId, gear)`
  equips tool + torch into gear (StarterPack.cs:58-77).
- Bootstrap applies the hardcoded "default" pack at world build
  (Bootstrap.cs:211) and reads `PendingCharacterDef.Name` (line 96).
- `Survival.CharacterDefinition` already has `Background` and
  `StarterPackId` fields (StarterPack.cs:10-23) — currently unused.
- The UI panel (`Screens.cs:343-431`) is name-only: an input box, BEGIN
  and BACK buttons, `_confirmCallback` invoked with
  `CharacterDefinition { Name }` (UI CharacterDefinition is a separate
  3-field class at Screens.cs:343; the confirm callback in Game.cs:290
  only forwards `def?.Name`).
- `StartNewGame(string name)` (Game.cs:422) sets
  `PendingCharacterDef = new SurvCharDef { Name = name }`.
- Save slots persist `CharacterName` (SaveSystem.cs:290, 322);
  `SaveData.CharacterName` (line 290) round-trips through
  `Save(game)`/`LoadIntoGame` (Game.cs:443 restores the name).
- Test shape for panel flows: PauseFlowTests drives `Game` +
  `InputState` mouse coordinates into panel `Update` and asserts state
  (PauseFlowTests.cs:100-135) — the same approach applies to
  `CharacterSelectPanel.Update(game, input, w, h)`.

## Design

### Backgrounds data

New static catalog `Backgrounds` in Survival/StarterPack.cs (beside the
packs it maps to):

- `Wanderer` — pack "default"; hint "A blank slate. Torch, berries, fish."
- `Forester` — pack "forester"; hint "Starts with an axe — woodcutting first."
- `Prospector` — pack "prospector"; hint "Starts with a pickaxe — mining first."
- `Scavenger` — pack "scavenger"; hint "Extra food — foraging first."

Each entry: (Id, Name, PackId, Hint). `Backgrounds.ById(id)` resolves
with a Wanderer fallback for unknown ids (old saves, corrupt data).

### Panel

`CharacterSelectPanel` gains a background row: four option buttons
under the name box, rendered with the existing DrawButton helper; the
selected one highlights. Selection state persists in the panel
(`SelectedBackgroundId`, default "wanderer"). Confirm passes both name
and `SelectedBackgroundId`.

### Flow

- `SetConfirmCallback` signature stays
  `Action<CharacterDefinition?>`; the UI CharacterDefinition gains
  `BackgroundId` (Screens.cs:343 class, additive).
- `StartNewGame(string name)` → `StartNewGame(string name, string
  backgroundId)`. Game.cs:290's callback forwards both. The old
  single-arg overload keeps existing callers (tests) working, defaulting
  to wanderer.
- `PendingCharacterDef` gets `Background = backgroundId`,
  `StarterPackId = Backgrounds.ById(id).PackId`.
- Bootstrap reads `PendingCharacterDef.StarterPackId` (fallback
  "default" when the def or field is absent — old saves) and passes it
  to `ApplyStarterPack`.
- Save/load: `SaveData` gains `CharacterBackground`; `Save()` writes
  `game.PendingCharacterDef?.Background ?? "wanderer"`; load restores it
  into `PendingCharacterDef` (so a resumed session keeps its identity).
  The slot list (Screens.cs:222 area) shows the background name beside
  the character name.

## Testing plan

New file `CharacterBackgroundTests.cs`:

1. `Backgrounds_ById_ResolvesPacks` — each id maps to the expected pack
   (forester→axe etc.); unknown id falls back to wanderer.
2. `Panel_SelectsBackground_ByClick` — headless panel Update with
   InputState clicking each option region sets `SelectedBackgroundId`;
   Confirm invokes the callback with the chosen id.
3. `StartNewGame_AppliesBackgroundPack` — a Game with
   `StartNewGame("Riri", "forester")` run far enough into bootstrap to
   inspect `_game.Inventory` (or via PendingCharacterDef assertions if
   bootstrap is async: assert `PendingCharacterDef.StarterPackId ==
   "forester"`) — the concrete inventory assertion lives in a
   Bootstrap-level test where the world-gen path is reachable
   headless; otherwise assert through `ApplyStarterPack` directly.
4. `SaveRoundTrip_KeepsBackground` — SaveData written with background
   "prospector" loads back with the same value.
5. Keyboard: Up/Down or Left/Right cycles the selection; Enter confirms.

## Open questions

Answered with defaults per standing delegation:

- Four options incl. Wanderer (not just the three packs) — keeps the
  current no-choice behavior reachable and gives new players an obvious
  neutral pick.
- No stat bonuses — playstyle only, per the DS pillar (no power creep).
- Save-format: additive field `CharacterBackground`; missing field
  reads as wanderer — old saves load unchanged.
