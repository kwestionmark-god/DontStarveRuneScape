# Fishing cast window + animated fish spots — design

Date: 2026-10-08
Status: implemented (cast window RED `158904c`/GREEN `302c5e4`; cancel RED
`82b5ccf`/GREEN `dbf89ca`; visuals + deadlock fix `4a856cb`). Visual
pixel-diff verification PENDING — the verification run was blocked by the
command approval flow; re-run baseline/cast captures when the user is at
the keyboard.
Slice: post-roadmap frontier — user request: "animated fishspot visuals +
an animation for the player while fishing". Builds on the fishing rod
slice (`345e50e`).

## Design law

- **RS**: the cast is the progression beat — fishing takes a timed 3s
  cast per attempt (OSRS-style wait); every other gather type stays
  instant. The catch at the end is the existing XP/yield path, unchanged.
- **DS**: the grind stays honest — walking away mid-cast loses the fish
  ("You moved — the fish got away."); the wait is real time in the world
  where night/season pressure applies.
- **MC**: readability — the spot itself animates (expanding ripple rings +
  a breathing decal, per-tile deterministic phase, no RNG flicker), and
  the player gets a rod + line + bobbing bobber drawn for the cast's
  full duration. No new art assets: ripples/line/rod are primitives,
  reusing the vocabulary already on screen.

## Ground — verified in the tree

- Actions resolve **instantly**: `ActionSystem.Update` (`:269-280`) calls
  `CompleteAction` on the first tick after start; `CompleteAction`
  (`:283-298`) nulls `Resource`/`RecipeId` and returns to Idle. There is
  no "while fishing" window today — the animation needs a cast duration.
- `ActiveAction` (`Actions/ActiveAction.cs`) has no duration field; its
  State doc comment says "Running is a one-frame transient" (to update).
- `StartAction` order: already-busy guard first (`:70-72`), then tool
  check (`:74-80`), then skill gate; the Fishing branch lands ~`:163-177`
  after last slice. `CompleteAction`'s gather-trio includes Fishing
  (`:287`).
- `Game.cs:821-835` is the SOLE `ActionSystem.Update` consumer: result →
  `TriggerPlayerSwing` (only when `Gear.Weapon != null`) →
  `ProcessCompletion` → `QuestSystem.NotifyCollect` →
  `UpdateNotifications`. Workers never touch ActionSystem (own dispatch
  in RecruitmentSystem) — a player-only duration is safe.
- Interact reach is 128.0f px (`FindInteractableResource(world, 128.0f)`,
  `InteractSystem`), on 64px tiles = 2 tiles.
- `SpriteRenderer` (instance class):
  - `RenderResource` (`:43`) — fish spots draw through the ground-decal
    block (`:99-112`): flat quad via `DrawScreenQuadCornersTextured`
    alpha 220, then early return. Game.cs sorts submerged decal tiles
    into the bed pass, under the water sheet (`:1534-1538`) — decals read
    through translucent shallows (WorldGen comment `:518`).
  - `RenderPlayer` (`:637`) — advances `_playerAnimTime += dt` itself;
    visual-state pattern precedent: `TriggerPlayerSwing` sets
    `_swingRemaining/_swingTotal` (`:626-635`). Body anchor: `cx =
    screen.X`, `cy = screen.Y - half - jumpLift` (`:661-663`).
  - Per-tile deterministic hash precedent: `((tileX * 73856093) ^ (tileY *
    19349663)) & 15` (`:435`).
  - Screen-space Y grows DOWNWARD — lifts subtract (skill pitfall).
- `PrimitiveBatch` primitives available: `DrawScreenQuad` (`:71` — bobber
  dot), `DrawScreenQuadCorners` (`:142` — thin line quads, 8 floats, zero
  alloc; perfect for 12-segment ripple rings), textured variants for the
  decal quad. No line primitive needed — lines are thin corner-quads.
- `Constants.SeaLevel` is the waterline (land sits above it; harness uses
  `SeaLevel + 5f` for flat land) — the bobber floats at SeaLevel.
- Smoketest verification path (skill law — no vision provider): `--smoketest
  <png>` + `DSR_SMOKE_FRAMES` frame-count capture (`:1051-1063`), env
  hooks parsed at boot; `DSR_TEST_ACTION` block (`:1347-1367`) scans tiles
  and starts a gather action headlessly (its loop skips tool-required
  nodes, so fish_spot is never auto-picked — a dedicated hook is needed).
  Pixel-diff via PIL in the ornith-cuda container.
- Existing GREEN test `PlayerFishing_SucceedsWithCarriedRod...`
  (FishingTests) asserts instant completion — rewritten in this slice to
  the timed-cast contract (the only suite test that starts a Fishing
  action; worker tests use Recruits.Tick and are unaffected).
- No renderer clock exists for world-space ambient animation
  (`_playerAnimTime` ticks only inside RenderPlayer) — a renderer-level
  clock advanced by Game.cs is the clean seam.

## Goal

1. **Timed cast**: fishing actions hold for `Constants.
   FishingCastSeconds` (3.0) before completing; all other action types
   resolve instantly as today. Mid-cast the player is busy (existing
   guard covers re-starting; movement is handled by 2).
2. **Cancel**: `ActionSystem.CancelActive()` (idle the action, no yield);
   Game.cs cancels the cast when the player leaves 128f reach of the
   spot, notifying "You moved — the fish got away."
3. **Animated fish spots**: expanding ripple rings (2 rings, 12
   segments, phase-hashed per tile) + a subtle decal "breathing" inset
   pulse, drawn in the existing bed pass, gated on `!IsDepleted`; driven
   by a new renderer clock `AnimTime` (advanced once per update).
4. **Player cast animation**: while a Fishing action runs, draw a rod
   (primitive, wood tones) angled from the player's leading side toward
   the spot, a thin line from rod tip to a bobbing red bobber at the
   spot's waterline (SeaLevel), bob ±2px. State lives on SpriteRenderer
   (`SetFishingCast(worldX, worldY)` / `StopFishingCast()`), synced by
   Game.cs every frame — the TriggerPlayerSwing pattern, target-addressed.
5. **Smoketest hook**: `DSR_TEST_FISHING=1` — stand the player on a dry
   neighbor of the first fish_spot, grant a rod, start the cast; captures
   exercise the full visual path headlessly.

## Non-goals

- No worker cast animation (workers fish via instant dispatch; player-
  facing readability first — recorded as follow-up if wanted).
- No rod-in-weapon-slot display, no gear changes — the cast rod is drawn
  only while casting, decoupled from equipment.
- No bite minigame, no RNG escape chance (flat cancel on walk-away), no
  bait (separately scoped next), no fish species tables.
- No new art assets, no quests, no UI panel changes (skills row exists).
- No save-format changes (`ActiveAction` is transient, not persisted;
  `DurationRemaining` needs no snapshot field).

## Design

### Cycle 1 — the timed cast (xUnit RED → GREEN)

`Constants.cs` (near the agility constants):

```csharp
public const float FishingCastSeconds = 3.0f; // timed cast; other gathers instant
```

`Actions/ActiveAction.cs`:

```csharp
/// <summary>Seconds remaining for timed actions (the fishing cast);
/// zero = resolve on the first tick, as every other gather does.</summary>
public float DurationRemaining { get; set; } = 0f;
```

(State doc comment updated: Running marks a pending completion that
timed actions hold until `DurationRemaining` elapses.)

`Actions/ActionSystem.cs` — Fishing branch of `StartAction` sets
`action.DurationRemaining = Constants.FishingCastSeconds;` and queues the
cast-start notification ("You cast your line...", cool blue). `Update`
holds before completing:

```csharp
if (Active.DurationRemaining > 0f)
{
    Active.DurationRemaining -= dt;
    if (Active.DurationRemaining > 0f)
        return null;
    Active.DurationRemaining = 0f;
}
return CompleteAction();
```

Stamina cost is charged at completion (existing `CompleteGathering`) —
unchanged.

### Cycle 2 — cancel (xUnit compile-RED → GREEN)

`ActionSystem.CancelActive()`: idle the running action, clear
Resource/RecipeId/Duration, return true when something was cancelled.
Game.cs, right after the `actionSys.Update` block:

```csharp
var active = actionSys.Active;
bool casting = active.State == ActionState.Running
    && active.ActionType == ActionType.Fishing
    && active.TileXy is { } castTile;
if (casting)
{
    float fx = (castTile.X + 0.5f) * Constants.TileSize;
    float fy = (castTile.Y + 0.5f) * Constants.TileSize;
    float dx = Player.WorldX - fx, dy = Player.WorldY - fy;
    if (MathF.Sqrt(dx * dx + dy * dy) > 128f)   // interact reach, InteractSystem
    {
        actionSys.CancelActive();
        actionSys.AddNotification("You moved — the fish got away.", (255, 180, 100));
        SpriteRenderer?.StopFishingCast();
    }
    else SpriteRenderer?.SetFishingCast(fx, fy);
}
else SpriteRenderer?.StopFishingCast();
```

### Render — animated spot (SpriteRenderer.RenderResource)

Renderer clock:

```csharp
public float AnimTime { get; private set; }
public void AdvanceAnimation(float dt) => AnimTime += dt;   // Game.cs OnUpdate
```

In the ground-decal block, after the decal quad (fish_spot only,
`!resource.IsDepleted`, phase = per-tile hash `& 7`):
- decal "breathing": inset 0.08 + 0.012·sin(AnimTime·2.2 + phase·0.78)
- two ripple rings: t = (AnimTime·0.45 + k·0.5 + phase/16) % 1, radius
  (5+20·t)·zoom, alpha (byte)(100·(1−t)), each ring 12 thin
  `DrawScreenQuadCorners` segments (inner/outer radius), centered on the
  tile's screen point. Same pass as the decal (bed, under the water
  sheet) — the spot shimmers through the shallows like the decal does.

### Render — rod, line, bobber (SpriteRenderer.RenderPlayer)

State (the TriggerPlayerSwing pattern, target-addressed):

```csharp
private (float X, float Y)? _fishTarget;              // spot world coords
public void SetFishingCast(float wx, float wy) => _fishTarget = (wx, wy);
public void StopFishingCast() => _fishTarget = null;
```

At the end of `RenderPlayer` (over the figure), when `_fishTarget` is
set: project the bobber at the waterline
`camera.WorldToScreen(wx, wy, Constants.SeaLevel)`, bob =
`sin(AnimTime·3)·2.5·zoom` (screen Y down = dip); draw the rod as a
slim two-tone quad (wood 168,124,82 + grip 94,64,40) from the player's
leading side toward the target (length ~34·zoom, width ~3·zoom); draw
the line as a 1.2·zoom-wide `DrawScreenQuadCorners` quad from rod tip to
bobber (pale 230,230,220, 200 alpha); bobber dot via `DrawScreenQuad`
(2·zoom, red 230,60,60). Game.cs clears the state on completion/cancel,
so the visuals end with the action.

### Smoketest hook (Game.cs, beside DSR_TEST_ACTION)

`DSR_TEST_FISHING=1`: scan tiles for the first live `fish_spot`; stand
the player on a dry neighbor (Elevation ≥ SeaLevel + 2, else the spot
tile itself); `Inventory.AddItem("fishing_rod", 1)`;
`StartAction(ActionType.Fishing, node, SkillManager, Inventory,
tileXy:(x, y))`. Mid-cast captures (e.g. `DSR_SMOKE_FRAMES=60`) show the
rod/line/bobber/ripples.

## Testing plan

xUnit (FishingTests, new "cast window" region):

1. `PlayerFishing_CastWaitsThenCatches` — RED (today the first Update
   completes instantly): mid-cast Update(0.5f) returns null; re-start
   returns "Already performing an action."; after ≥3s of Updates the
   catch fires exactly once (raw_fish, xp 15); no repeat after.
2. Rewrite `PlayerFishing_SucceedsWithCarriedRod...` to the timed
   contract (wait out the cast, then the existing yield/XP-routing
   asserts). RED alongside 1.
3. `PlayerForaging_StillInstant` — regression guard: first Update
   returns a result (pre-greens; pipeline-reuse confirmation).
4. `PlayerFishing_CancelActive_StopsTheCast` — compile-RED
   (CS1061 CancelActive), then GREEN: cancel → not busy; later Updates
   return null forever.

Render verification (skill law — pixel-diff, not eyeballs), all same
seed via `--smoketest`:

- baseline: no DSR_TEST_FISHING, frames=60
- cast A: DSR_TEST_FISHING=1, frames=60 (mid-cast)
- cast B: DSR_TEST_FISHING=1, frames=90 (later phase — bobber bob +
  ripple phase advanced)

Diff counts via PIL in the container: A vs baseline > threshold (cast
visuals render); A vs B > 0 localized motion (animation animates).

Full suite after each cycle; smoketest captures re-run after render
lands.

## Open questions

Answered with defaults per standing delegation ("pick defaults"):

- Cast duration 3.0s (tunable constant; OSRS-feel, XP 15/catch already
  balances) — flag to user in the slice summary for veto.
- Walk-away cancel is flat (no RNG escape), threshold = interact reach
  (128f), message "You moved — the fish got away."
- Only fishing is timed; every other gather stays instant.
- Ripples gated on `!IsDepleted` (empty water doesn't shimmer).
- Rod drawn as a primitive two-tone quad only while casting — the
  elder-wood-staff texture reuses only as the ITEM sprite (inventory),
  not the in-hand render (orientation risk with rotated UVs; primitives
  are deterministic under pixel-diff).
- Worker fishing stays instant (no cast) — their dispatch is untouched.
- Cast-start notification: "You cast your line..." (cool blue).

## Revision history

- 2026-10-08 draft.
