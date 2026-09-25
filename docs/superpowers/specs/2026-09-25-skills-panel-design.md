# Skills Panel — Design

Date: 2026-09-25. Status: approved in chat (user directive: quality-first, skepticism toward
existing quick-pass code, incremental commits).

This is the first interactive UI panel. It establishes the shared panel chrome and mouse
hit-testing helpers that all later panels (inventory, crafting, building, gear) reuse.

Classification: architectural (new UI sub-layer), per brainstorming skill.

## Decisions (from user)

- Visual style: OSRS-flavored grid — dark plate, gold border/title (title-screen palette),
  one row per skill: name, level, XP bar.
- Stat-point spending: mouse `[+]` buttons per sub-stat, hover highlight. First clickable
  UI in the game; hit-testing helper must be reusable.
- World keeps running while panels are open (matches existing `Always-tick systems` update
  architecture). Panel draws over the live world.
- Skill icons: drawn placeholder glyphs for now (sprite-set icons are a later assets task).

## Findings that shaped the design (existing-code audit)

1. **XP curve bug in `SkillManager.CalculateLevelFromXp`**: inner sum runs `i = 1..level`
   instead of OSRS's `1..level-1`, shifting every threshold one level (level 2 needs 174 xp
   in code vs 83 in OSRS; level 99 needs 14,391,160 vs 13,034,431). Fixed here, pinned by
   tests.
2. **`InputState.MouseLeftClick` is only consumed by the title screen**; there is no
   click-to-move, so panel clicks cannot leak into world input. No router gate needed for
   clicks, only for treating clicks as UI-consumed.
3. **Panel `Render` signatures receive only `GL`** — no `PrimitiveBatch`, no `TextRenderer` —
   so panels cannot draw text today. `Game.RenderPanels` gains `(PrimitiveBatch, TextRenderer,
   w, h)` and moves after the HUD so panels overlay it.
4. **Panels get no `Update`** (gameplay update only runs in `Playing`); a per-frame panel
   update dispatch is added so panels can poll `InputState` before `ClearFrame()`.
5. Every distinct drawn string is rasterized into a permanently cached texture
   (`TextRenderer`). Fine here; noted as a scaling concern for the future dashboard.

## Components

### `SkillManager` (logic, test-first)

- Fix `CalculateLevelFromXp` to the real OSRS table (`sum_{i=1}^{level-1}`), returning level
  with `XpForLevel(level) <= xp < XpForLevel(level+1)`; clamp to 1..99.
- New: `static float XpForLevel(int level)`, and
  `ProgressToNext(string skillId, out float into, out float needed)` for XP bars.
- New: `bool SpendPoint(string skillId, string statName)` — fails (false) on unknown skill,
  zero unallocated points, or unknown sub-stat; otherwise decrements `UnallocatedPoints` and
  increments the sub-stat. Spending is per-skill (points earned in woodcutting spend on
  woodcutting's sub-stats), matching where `UnallocatedPoints` lives.

### `UI/UiInput.cs` (new)

Minimal retained-mode mouse helper wrapping `InputState`: `bool Hovered(x, y, w, h)`,
`bool Clicked(x, y, w, h)` (hover + left-click edge), and a `Consumed` flag so one click
cannot trigger two overlapping buttons. No event system, no focus model.

### `UI/PanelChrome.cs` (new)

Static chrome: full-screen dim, centered plate (`40,24,12`), gold border (222,192,132),
title bar; returns the inner content rect via `out` so panels fill it.

### `SkillPanel` (fills the empty shell in `Panels2.cs`)

- New signature: `Render(PrimitiveBatch, TextRenderer, SkillManager, screenW, screenH)` and
  `Update(InputState)` for clicks/arrow-key selection.
- Header: total level, unallocated-points total.
- 9 rows: placeholder glyph, name, level, XP bar.
- Detail block for the selected skill: five sub-stats with values and clickable `[+]`
  (enabled only while that skill has unallocated points). Selection via click or Up/Down.

### Verification

- New `src/DontStarveRuneScape.Tests` (xunit): OSRS table pins (83 @ 2, 13,034,431 @ 99),
  `SpendPoint` success/failure paths, snapshot round-trip preserving spent points.
- `DSR_TEST_CLICK=x,y` smoketest hook: scripted click through `InputState` so a headless run
  can open the panel, select a skill, spend a point, and screenshot. Manual playtest on top.

## Out of scope

Real skill icons, level-up FX, action level-gating, other panels' content.
