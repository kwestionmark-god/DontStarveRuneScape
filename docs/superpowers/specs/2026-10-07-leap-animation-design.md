# Leap Animation — Design Spec

- **Date:** 2026-10-07
- **Status:** implemented same day (defaults picked, user standing instruction)
- **Slice line:** polish follow-up on the agility slice (249b005): the
  jump hop gets its animation — the whole figure leaps, grounded in the
  terrain-aware boot/gait machinery.
- **Design law:** tri-fusion — DS moment-to-moment movement feel, RS
  agility flavor (the hop reads as a practiced leap), MC readability
  (shadow stays on the ground; landing reconnects to terrain). Reuse the
  gait/boot machinery; no new screens, no gameplay change (still
  cosmetic — the agility spec's non-goals hold).

## Goal

- While airborne, the entire paper-doll rides the arc as ONE rigid
  figure: body quads, dome boots, carried gear, cape, waterline all
  lift together (today the crossed-billboard body never lifts at all —
  `jumpLift` reached only the legacy single-sprite fallback).
- Boots animate the leap: toe-off tilt at takeoff, tuck mid-flight,
  heel-strike + a small forward reach at landing, then settle back onto
  terrain through the existing idle-replant.
- Body pitches into the leap: top edge shears forward along travel,
  peaking mid-arc (the sprint-lean machinery, driven by the jump arc).
- Shadow shrinks and fades with height — the groundedness cue.

## Non-goals

- No ledge/platforming logic, no collision change — jump stays cosmetic.
- No airborne steering, no double jump (already blocked).
- No new art or particles (ParticleSystem is a stub; skipped).
- NPC/monster rigs untouched (they don't jump).

## Ground — verified in the tree 2026-10-07

- `Player.TryJump/UpdateJump` (Core/Player.cs:196-223) drives
  `JumpVisualOffset = sin(t·π)·JumpHeightPx`; t is local — not exposed.
- `RenderPlayer` computes `jumpLift` (Render/SpriteRenderer.cs:563) but
  applies it only to the legacy single-sprite path (line 566); the
  crossed-billboard body (BuildCrossBillboard, line 598) ignores it, and
  `DrawBootDomes` samples `BootElevation` so boots stay on the terrain.
- The projection has no perspective divide: a uniform screen-space Y
  shift IS a rigid lift of the whole figure (same trick as the boot
  swing `lift` and `ShearQuadTop`).
- `GaitAnimator.PinToStance` exists (swimming) and cancels swings;
  `DrawBootDomes` already threads a per-foot screen-px `lift` and
  `SwingTilt(t)` = −1 toe-off → 0 → +1 heel-strike — the exact leap
  tilt profile, free.
- Sprint forward-lean block (line 608) shows the top-shear pattern along
  the on-screen travel direction.
- Smoketest harness: `DSR_TEST_KEYS=Space` one-shots a jump at first
  Playing frame; `DSR_SMOKE_FRAMES=n` captures frame n headlessly.

## Design

1. **Player** (surgical, sprint-shared file): expose
   `JumpProgress` (0..1 across the arc, 0 while grounded) — set in
   `UpdateJump`'s t, reset on land and in `TryJump`.
2. **Constants**: `LeapTuckPx 3.5` (boot tuck bump, world px),
   `LeapFootLeadPx 5` (landing reach, world px),
   `LeapLeanPx 4.5` (body top-shear at arc peak).
3. **SpriteRenderer.LeapPose(t)** (internal static, pure): the four
   animation channels — Tilt = SwingTilt(t); Tuck = LeapTuckPx·sin(t·π);
   Lead = LeapFootLeadPx·max(0,(t−0.75)/0.25)²; Lean = LeapLeanPx·sin(t·π).
4. **LiftQuad(q, dy)**: shifts all corners + center of a BillQuad in
   screen Y. Applied to bodyQuad and every half while airborne.
5. **Body**: after the sprint-lean block, if airborne: LiftQuad all
   quads by jumpLift, then ShearQuadTop along travel by Lean. lcx/lcy,
   gear, cape, waterline ride automatically (they read bodyQuad after).
6. **Boots**: while airborne skip gait.Update, `PinToStance` at the body
   plus the Lead offset (feet reach ahead for landing); draw domes with
   `baseLift = jumpLift + Tuck·zoom` and `baseTilt = Tilt` — two new
   optional params on DrawBootDomes threaded into its existing
   lift/tilt channels. On landing the gait resumes; feet sit a touch
   ahead of stance and the idle replant (TriggerDist 3) settles them —
   the terrain reconnect.
7. **Shadow**: `DrawShadow` at the un-lifted ground point with
   half·(1−0.45·airFrac) and alpha 220·(1−0.55·airFrac),
   airFrac = JumpVisualOffset/JumpHeightPx.
8. **Swimming + jump** (allowed per agility spec): swim-branch boots
   subtract jumpLift like everything else; body lift applies via the
   shared path.

## Testing plan

- LeapPose phases: t=0 → tilt −1 (toe-off), zero tuck/lead/lean;
  t=0.5 → tilt 0, max tuck, max lean, no lead; t=1 → tilt +1
  (heel-strike), max lead, zero tuck/lean.
- LeapPose continuity: 200 samples, no channel jumps.
- JumpProgress: 0 before TryJump, ~0.5 at half-duration, back to 0
  after landing; stays 0 while grounded.
- Full suite green (AgilityJumpTests semantics unchanged).
- Live headless check: DSR_TEST_KEYS=Space + DSR_SMOKE_FRAMES sweeps,
  pixel-diff vs standing baseline — the airborne frame shows the figure
  lifted (player-cluster diff), landed frame close to baseline.

## Open questions (resolved — defaults)

1. Tuck shape: sine bump (knees-up read) — 3.5 world px.
2. Landing reach: last quarter, quadratic ease-in, 5 world px ahead.
3. Lean: same magnitude family as sprint lean (4.5 vs 5.5), sine-shaped.
4. Mid-air stepping: none — feet pinned (stepping while airborne would
   read as running on air).
