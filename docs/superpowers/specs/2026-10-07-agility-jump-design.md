# Agility Skill + Jump Action — Design Spec

- **Date:** 2026-10-07
- **Status:** implemented same day (defaults picked, user standing
  instruction); leap animation follow-up done 2026-10-07 (d20e4d2) — see
  `2026-10-07-leap-animation-design.md`.
- **Slice line:** new post-roadmap feature — RS pillar skill (agility) with
  DS-flavored movement play (sprint/jump), OSRS XP curve.
- **Design law:** tri-fusion — RS skill progression (OSRS XP table, levels
  1–99), DS moment-to-moment movement (stamina pool), MC-style readability.
  No new screens; jump is a movement verb, not a platformer.

## Goal

- Agility skill (11th skill) that levels from sprinting and jumping.
- A jump action (Space, Playing state): stamina-cost hop with a visual arc.
- XP accrues **very slowly** (OSRS curve) so long-term levels feel earned:
  ~1 XP per second of actual sprinting, 2 XP per successful jump.
  Level 2 ≈ 83 sprint-seconds; level 10 ≈ 19 min; level 50 ≈ 28 hours.

## Non-goals

- No obstacle courses, rooftops, or platforming — jump is cosmetic + XP.
- No new panels; agility appears in the existing SkillPanel grid.
- No changes to worker/NPC movement.

## Ground — verified in the tree 2026-10-07

- `SkillManager` (Skills/SkillManager.cs): skill id list in the ctor;
  OSRS `XpForLevel`; `AddXpWithNotification` raises level + points;
  snapshot iterates `_skills` (registration auto-persists).
- `Player.UpdateSprint` gates sprinting on the shared `StaminaPool`
  (4 stamina/s drain, 5s exhausted rest) — the natural XP hook.
- `StaminaPool.Consume` returns false when exhausted — jump gate.
- Space in the Playing state currently does nothing (Confirm is only
  consumed by panels; `HandlePlayingInput` ignores it).
- `SpriteRenderer` anchors the body billboard bottom-center at the
  ground screen point — a screen-space Y offset gives the hop arc.
- `SkillPanel.Skills` row array drives layout; 10 rows fit ContentH 470.

## Design

1. **Constants** (Config/Constants.cs): `JumpStaminaCost 5f`,
   `JumpDuration 0.45s`, `JumpHeightPx 26f`, `AgilityXpPerSprintSecond 1f`,
   `AgilityXpPerJump 2f`, `AgilitySprintSpeedBonusPerLevel 0.01f`.
2. **SkillManager**: add `"agility"` to the skill id list (level 1, generic
   five sub-stats; snapshot/restore + death penalty pick it up for free).
3. **Player**: `TryJump()` — gated on not-already-jumping and
   `StaminaPool.Consume(JumpStaminaCost)`; sets `JumpTimeRemaining`,
   awards `AgilityXpPerJump` via `AddXpWithNotification`, forwards level-up
   messages to the ActionSystem notification channel.
   `UpdateJump(dt)` ticks the timer; `JumpVisualOffset` is a sine arc
   (0 → JumpHeightPx → 0) while jumping. Sprint XP: while `Sprinting`,
   `AddXpWithNotification("agility", AgilityXpPerSprintSecond * dt)`
   per frame (float XP, no rounding loss).
4. **EffectiveSpeed**: agility grants +1% movement speed per level above
   1 (level 1 = +0%; level 10 = +9%) — the long-term earned bonus.
5. **Input**: Space keydown in Playing → `Player.TryJump()`
   (InputRouter.HandlePlayingInput; one-shot, no held flag).
6. **Render**: SpriteRenderer lifts the body billboard by
   `JumpVisualOffset * zoom` while jumping (feet leave the ground).
7. **UI**: SkillPanel grid gains an Agility row (glyph "Ag", cyan).

## Testing plan

- Fresh SkillManager registers agility (AddXpWithNotification levels it).
- Sprinting grants ~1 XP/s; standing with Shift grants none.
- Jump consumes stamina, starts the arc, awards 2 XP; exhausted pool fails
  the jump with no XP.
- Arc rises then lands (offset > 0 mid-jump, 0 after).
- EffectiveSpeed scales +1%/agility-level above 1.
- Full suite stays green (SprintTests semantics unchanged).

## Open questions (resolved — defaults)

1. Jump key: Space (free in Playing state). 
2. Jump while swimming: allowed — purely cosmetic, no gameplay edge.
3. XP rates: 1 XP/s sprinting, 2 XP/jump (OSRS curve makes late levels
   take hours-to-days of movement; "very slowly, earned").
4. Bonus shape: +1%/level sprint+walk speed via EffectiveSpeed.
