# Fire-venue handoff — 2026-10-10

For a cold session (new agent, no chat context): read this, then
`docs/superpowers/2026-10-04-development-map.md` "Ordered next work" +
frontier section. Everything below is verified against the tree as of
`c80fb8f` (clean, suite 507 total / 506 green, 1 pre-existing skip).

## How to work here (the short version)

- Skill `dsrs-colony-slice-tdd` auto-loads for this repo and has every
  pitfall hit so far — read its pitfalls before writing tests.
- Build/test (dotnet lives ONLY in the ornith-cuda toolbox):
  `toolbox run -c ornith-cuda sh -c 'cd /var/home/kwestionmark/dontstarve-runescape-c# && dotnet test src/DontStarveRuneScape.Tests/DontStarveRuneScape.Tests.csproj --nologo -v q'`
- Slice rhythm (user-approved): draft spec in
  `docs/superpowers/specs/YYYY-MM-DD-<slice>-design.md` (house style:
  Date/Status/Design law/Ground-verified/Goal/Non-goals/Design/Testing
  plan/Open questions) → ONE clarify round batching every real gameplay
  decision → RED commit (verify each failure is the RIGHT one) → GREEN
  commit → docs commit (spec status, dev-map entry, CHANGELOG).
  Explicit `git add` paths only (never `-A`).
- User style: concise, casual ("haha"), approves progressive commits,
  trusts agent judgment on defaults, TDD + spec-first, "we don't need
  to reinvent that game" — leverage existing code. Not bound to the
  inspiration titles; fusion logic of our own is welcome.

## State: the fire-venue arc is COMPLETE (one slice, 2026-10-10)

RED `20f0519` / GREEN `7d86810` / docs `c80fb8f`. The three incoherent
fire methods became one skill-owned venue:

- **AddXp root fix** — `SkillManager.AddXp` recomputes Level + pays
  points (the parked frozen-level bug died at the root);
  `AddXpWithNotification` is a thin alias. Un-froze firemaking,
  worker construction, worker mining.
- **Fuel table wired** — `FiremakingSkill.FuelTable` loads
  `Skills/Firemaking/data/fuel_table.json` (static, lazy); burn
  seconds = per-unit × qty summed (stick 15s, log/oak 30s, charcoal
  45s, planks 20s); unlisted fuel keeps flat 30s/unit
  (`DefaultFuelSeconds`); duration sub-stat (+5s/pt) stacks on top.
- **Hearths light** — `StructureDef.LightSeconds` (data key
  `light_seconds`): campfire 90s, new **firepit** 240s (stone 6 +
  stick 2, construction 3, campfire sprite reused).
  `PlaceStructure` gained an optional `firemaking` param (null = no
  light, legacy tests unchanged); instant path trains construction 10
  and lights the hearth; worker site completion lights too
  (`LightHearth` — def's flat seconds, hearth is the multiplier).
- **Relight feeds firemaking** — F near a burned-out hearth (no live
  fire) lights the hearth from carried fuel (charcoal → any log →
  stick, max 2), pays 25 firemaking XP. F away from hearths = ground
  fire as before. `FireInteraction.FindNearbyHearth` +
  `CollectFuel` are the seams.
- **Fire menu** — `GameState.FireMenu` + `FireMenuPanel` (new file,
  CraftingPanel idiom). E near a lit fire / cooking_station / furnace
  / smelter opens it (`FireInteraction.CanOpenFireMenu` →
  `Game.OpenFireMenu`). Content = every `requires_campfire` OR
  cooking recipe, single-sourced in `FireMenuPanel.IsFireRecipe`
  (menu, CraftingPanel's exclusion, and tests all read it), plus a
  Rest-until-dawn row (past-the-end index; shares the `TrySleep`
  gate). Full panel plumbing landed: enum + PanelStates +
  IsPanelState + SetState visibility + RenderPanels + Update +
  router case + Keybindings + CloseAllPanels + Bootstrap (OnRest
  wired to TrySleep).
- **Proximity honesty** — `GetAvailableStructureIds` (now internal)
  filters to structures within `CampfireSearchRadiusTiles` (3) of the
  player + the virtual campfire for lit fires. Owning a furnace
  anywhere no longer smelts anywhere.
- **Dominated recipe deleted** — "Build Campfire" craft (3 oak_logs +
  5 stone → inert item, trained crafting) removed; the items.json
  campfire def stays for old saves (inert by design, placement is the
  build panel's). This RESOLVED the parked inert-item finding.
- Count moves to know: suite 488 → 507 (19 new FireVenueTests; two
  SubStatCatalog fire pins moved to fuel-aware numbers: 2×log = 60s
  base + duration bonus).
- Data-count pins: `CraftingPipelineTests` `>= 80 recipes` survived
  (84 → 83); no NpcDataTests pins moved this slice. `recipes.json`
  count is now 83 — grep pins before any recipe data change.

## PENDING verification (honest debt from this slice)

- **Fire-menu smoketest pixel pass** — FireMenuPanel has never been
  rendered. Needs: a `DSR_TEST_FIREMENU=1`-style hook (see the
  `DSR_TEST_FISHING` precedent), `dotnet run -- --smoketest <png>`
  with headless-safe settings (the PathOverride fix in Program.cs),
  PIL pixel-diff in the container (no vision provider). CRITICAL: the
  user must NOT touch the game window during captures ("hands off
  ~75s"). Probe for a color unique to the new panel (chrome border)
  — presence proves draw, extent proves layout.

## Concurrent next steps (the menu, with my read)

Quick wins (hours, no new systems):

1. **Fire-menu smoketest pass** (above) — closes the slice's debt.
2. **Return-to-menu after a hub tab handoff** — open follow-up from
   the quest slice: NpcHubPanel tab → panel → Esc lands in Playing,
   E again reopens the hub. Small UI-flow fix; the SetState/
   CloseAllPanels ordering pitfall in the skill applies.
3. ~~**Unlock-as-event trigger**~~ — DONE 2026-10-11 (RED `d01ce2d` /
   GREEN `d625fd5`): fires at objectives-complete in the Tick sweep,
   gate keying fixed recipe-id-side at all three sites, gem_mining's
   dead id repointed, completed quests + unlock sets persist.
4. **grass_rope dup xp_reward key** — last live parked finding; tiny
   data fix (dup JSON key, second silently wins — grep
   recipes.json).
5. **WarmthRadius consumer** — `FireInstance.WarmthRadius` (3) is
   still read by nothing. Natural home: winter cold pressure near
   fires (seasons system exists). Gameplay call: does standing near
   fire slow/reverse the cold meter? Batch in a clarify round if
   bundled with 1.

Content slices (data-heavy, engine-light):

6. **Coastal/swamp side quests** — continues the quest-breadth arc
   (plains hub DONE; coastal merchant + swamp have quest-less
   boards... actually verify: coastal/swamp givers may not exist at
   all — grep npcs.json `available_quest_ids` by faction first). Pure
   data on the 7 existing condition hooks; `EveryQuest_IsOfferedBy-
   SomeNpc` + `EveryQuestSkillReward_TargetsARegisteredSkill` are the
   suite laws to satisfy; ProgressionReachabilityTests runs the
   reachability walk automatically.

Big feature slices (spec + clarify first):

7. **Vertical building + terrain leveling** — the backlog's scoped
   4-split: (1) block defs + placement + flat-pad gate, (2) jump
   landing/standing, (3) stacking + connection, (4) terrain leveling.
   Known seams banked in the dev-map backlog entry (TryJump
   Player.cs:206, CornerElevations stitching, PlaceStructure gates).
8. **Skills-arc tails** (still parked): workers reading their own
   sub-stats (gather/production math still level-only) + per-stat
   tooltips in the panel.

My recommended order for the next session: 1 (smoketest, closes
debt) → 4 (grass_rope, trivial) → 2 (hub return) → 6 (coastal/swamp
quests — momentum content) — then pick ONE of 3/5 to bundle, leaving
7 for its own session. But the user often arrives with their own
priorities — defer to theirs.

## Standing cautions (beyond the skill's pitfall list)

- `git add` explicit paths; the root accumulates harness dirt
  (package.json, node_modules).
- Terminal approval flow can block commands when the user is AFK —
  if blocked, do NOT retry; commit with the step marked PENDING and
  tell the user what to re-run.
- `python3 -c` inline scripts and root-path `rm` need approval;
  prefer the patch/read_file tools. Never pass the repo path as
  terminal workdir (`#` is a shell metachar) — `cd` inside the
  command body.
- The user hops in and out of focus — keep momentum, don't wait on
  confirmations for low-stakes picks, but surface real gameplay
  decisions (one clarify round, they answer in one click).
- Smoketest captures need the user hands-off ~75s — tell them
  before starting, or run when they're AFK-but-not-locked.
