# Handoff — Apply the player's visual updates to NPCs and monsters

Date: 2026-10-05
Repo: `kwestionmark-god/DontStarveRuneScape`, main @ `4f3b89a` (players' crossed-billboard body `b2578d2`, sprint-flavor lean/trail `4f3b89a`)

## Context — what the player has and where it lives

All in `src/DontStarveRuneScape/Render/` unless noted:

1. **Crossed-billboard body** (`b2578d2`): `SpriteRenderer.ProjectBodyBillboard` projects a
   vertical world-space plane through `Camera.WorldToScreen` (tempered pitch response,
   clamp 0.75–1.30 so it never pancakes). `RenderPlayer` builds a classic "paper doll":
   two static planes at 90° riding the gait travel axis (side view along dir, front/back
   across it), each split at the fold into half-quads painted back-to-front by view depth
   (`BillQuad.Depth`), drawn via `PrimitiveBatch.DrawScreenQuadCornersTexturedUSpan`
   (per-half UVs). Boots sort against the body by world depth (`DrawBootDomes` takes
   `bodyWX/bodyWY`, in-front test = `Δ·(sinYaw, cosYaw) > 0`).
2. **Sprint-flavor visuals** (`4f3b89a`): sprint-only envelope `_sprintEnv` (rise 2.0/s,
   fall 3.5/s) drives (a) top-edge shear of the body along travel (`ShearQuadTop`,
   `SprintForwardLeanPx`), (b) turn-lean bank multiplier `SprintLeanBoost`, (c) backward
   boot trail via `GaitAnimator.Update(..., trail:)` (`BootTrailPx`). Walk path untouched.
3. **What NPCs/monsters have today**: they use the OLD screen-space body
   (`DrawTexturedScreenQuad`) with `DrawGaitFeet` flat sprite-quad feet (split far/near
   by *screen-Y* vs the ground point). They do NOT get the crossed billboard and do NOT
   get dome boots. Quadrupeds have directional sprite variants and mirror handling
   baked into `RenderMonster`'s quad draw.
4. **Harness/verify**: headless captures — env vars `DSR_SEED`, `DSR_POS_X/Y`,
   `DSR_CAM_ZOOM`, `DSR_CAM_PITCH`, `DSR_CAM_YAW`, `DSR_TEST_MOVE`
   (commas/phases `;`, keys incl. `sprint`, `orbit_cw/ccw`), `DSR_SMOKE_FRAMES`;
   run `dotnet run --project src/DontStarveRuneScape --no-build -- --smoketest out.png`.
   A wandering monster spawns near (100,102) on seed 12345. Pixel checks from
   `gait_debug_cycle.py` patterns (shirt pixel bbox, boot vs base-row deltas).
   **rule**: pass visual checks only by viewing or measuring the pixels yourself,
   with a per-image reflection (one question + answer record per image).
5. **Verification commands**: `dotnet build src/DontStarveRuneScape` (0 errors) and
   `dotnet test src/DontStarveRuneScape.Tests` (236 passing at handoff). Build via
   `toolbox run -c ornith-cuda` if the host's Wayland/XDG differs. `git checkout -- src/`
   resets only source.

## Goal

NPCs and monsters stand IN the world like the player: world-space crossed-billboard
bodies with correct depth-sorted feet, so orbiting the camera pans the doll with the
terrain instead of sliding a screen-aligned decal across it. Monsters get dome-feet
parity; NPCs get dome boots. Conserve resources: reuse `ProjectBodyBillboard`,
`BillQuad`, `ShearQuadTop`, `DrawBootDomes`, and the per-entity `ConditionalWeakTable`
gait rigs — write no parallel projection or painter sort.

## Work plan (single PR, small hunks)

1. **Generalize the anchors** — `ProjectBodyBillboard` is static and already
   entity-agnostic (takes world pos + axis). Extract the "two planes + 4 sorted half
   quads + anchor quad selection" block from `RenderPlayer` into a helper
   `BuildCrossBillboard(Camera, wx, wy, elev, halfWidthWorld, heightWorld, lean, dirX,
   dirY, out anchorQuad)` returning the 4 sorted halves; `RenderPlayer` keeps its
   sprint shear on top of the result.
2. **NPCs** (`RenderNPC`): replace the `DrawTexturedScreenQuad` body with the cross
   (height ≈ 2×halfWorld nominal at ref pitch, same 88px ratio convention), switch
   `DrawGaitFeet` → `DrawBootDomes` with the NPC gait rig and `(bodyWX, bodyWY)`
   depth test. No directional/mirror handling changes.
3. **Monsters** (`RenderMonster`): same body replacement; feet per `GaitConfig`
   (`PlayerBoot` becomes `DrawBootDomes` dims from `cfg`'s offsets/scale — reuse,
   don't add sprite textures). Preserve the quadruped directional variant + mirror
   logic by picking the plane-aligned texture per plane (front/back/side) and
   mirroring half-quads via reversed U span when needed — keep it simple: side view
   on the travel-aligned plane, front/back on the cross plane, mirrored via U swap.
4. **Skip** the sprint envelope/lean for NPCs/monsters (player-only flavor).
5. **Verify**: build+tests green (add `GaitAnimator`-style pure-math tests if depth
   ordering logic moves anywhere testable — e.g. a `CrossBillboardSorter` static);
   captures: NPC at (100,102) seed 12345 yaw 0/45/90 vs before; a monster mid-walk
   at yaw 45 with feet under/over correct; zoom 1/2.5; pitch 10/70. Compare against
   pre-change captures (capture BEFORE first, same env).

## Boundaries

- Do not touch: the sprint feature commit tree (uncommitted sprint files belong to a
  parallel session), cave system items, or `DrawGaitFeet` call sites until 3 works.
- Keep pixel-equivalence where the camera is default (pitch 30°): bodies must render
  the same size as today at default pitch (that's what the 88px/tempered clamp is for).
- If mirroring half-quads by U-swap, mind the fold: left half = U 0..0.5, right = 0.5..1;
  a mirrored plane swaps span endpoints, not draw order (depth sort is fold-aware).

## Handoff prompt (paste into the new session)

```
Task: apply the player's world-space visual updates to NPCs and monsters in this repo
(dontstarve-runescape-c#, main @ 4f3b89a). Read
docs/superpowers/2026-10-05-npc-monster-visual-updates-handoff.md first — it has the
context, work plan, boundaries, and verification harness.

Rules of engagement:
- Follow the plan's 5 hunks in order; one git commit per hunk; run
  `dotnet build src/DontStarveRuneScape` + `dotnet test src/DontStarveRuneScape.Tests`
  after each (236 tests must stay green).
- REUSE SpriteRenderer.ProjectBodyBillboard / BillQuad / DrawBootDomes / gait rigs.
  No parallel painters, no new sprite textures; dome-boot dims come from GaitConfig.
- NPCs: no directional sprites (single texture both planes). Monsters: preserve
  quadruped front/back/side variants — side view on the travel plane, front/back on
  the cross plane, mirror via U-span swap, never by draw-order games.
- No sprint envelope/lean for NPCs/monsters. Default-pitch pixel-equivalence is the
  invariant (tempered pitch clamp is load-bearing).
- Verify visually with the headless capture harness in the doc; capture BEFORE shots
  first; per image, answer one specific question and record the reflections, and pass
  only on direct pixel evidence (crop around the entity; use the shirt-pixel /
  base-row-delta recipes from gait_debug_cycle.py patterns).
- Keep the uncommitted sprint-feature files (Constants.cs, Player.cs, Game.cs, ...)
  untouched; commit only the files each hunk needs. `dump.rdb` is unrelated, ignore it.
- If a hunk needs a new pure function (e.g. fold-aware depth sort), land it with xUnit
  tests first, then wire it.
```
