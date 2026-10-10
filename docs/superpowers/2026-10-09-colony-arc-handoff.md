# Colony arc handoff — 2026-10-09

For a cold session (new agent, no chat context): read this, then
`docs/superpowers/2026-10-04-development-map.md` "Ordered next work" /
frontier section. Everything below is verified against the tree as of
`2d09c33` (clean, suite 411 total / 410 green, 1 pre-existing skip).

## How to work here (the short version)

- Skill `dsrs-colony-slice-tdd` auto-loads for this repo's tasks and has
  every pitfall hit so far — read its pitfalls before writing tests.
- Build/test (dotnet lives ONLY in the ornith-cuda toolbox):
  `toolbox run -c ornith-cuda sh -c 'cd /var/home/kwestionmark/dontstarve-runescape-c# && dotnet test src/DontStarveRuneScape.Tests/DontStarveRuneScape.Tests.csproj --nologo -v q'`
- Slice rhythm (user-approved): draft spec in
  `docs/superpowers/specs/YYYY-MM-DD-<slice>-design.md` (house style:
  Date/Status/Design law/Ground-verified/Goal/Non-goals/Design/Testing
  plan/Open questions/Revision history) → RED commit (verify the failure
  is the RIGHT one) → GREEN commit → docs commit (spec status, dev-map
  entry with pillars, CHANGELOG). Explicit `git add` paths only (never
  `-A`). The user approves progressive commits and wants to be shown
  the plan when stakes are real; defaults are ours to pick otherwise.
- User's lens for this arc: "whatever makes fishing satisfyingly
  grindy in a survival sandbox with colony dynamics" — RS-style
  progression + honest grind + readable systems. "Not bound to the
  inspiration titles — fusion logic of our own welcome."

## State: the fishing arc is COMPLETE (four slices)

1. **Skill + rod** (`0e25d54`): `fishing` skill registered;
   `fish_spot` requires `fishing_rod` (craftable: planks×2 +
   grass_rope×1, crafting 2; required_level 5→1). New
   `ActionType.Fishing` routes player XP (Foraging would leak —
   `CompleteAction` nulls `Active.Resource` before
   `ProcessCompletion` maps the skill; only the enum survives).
   `GatherSkillFor` has a fishing branch BEFORE the tool check (else
   workers train mining on rod nodes).
2. **Cast window + visuals** (`3178c94`, pixel-verified `4208d09`):
   fishing is the one TIMED gather — 3s cast
   (`Constants.FishingCastSeconds`), walk-away >128px cancels
   ("You moved — the fish got away.", `ActionSystem.CancelActive`).
   Fish spots: breathing decal + two expanding ripple rings (per-tile
   phase hash, bed pass, driven by `SpriteRenderer.AnimTime`, advanced
   once per update by Game.cs). Casting overlay: primitive rod + line +
   bobbing bobber via `SetFishingCast`/`StopFishingCast` (Game.cs
   syncs every frame). `DSR_TEST_FISHING=1` smoketest hook.
3. **Bait economy** (`0207b00`): `fishing_bait` (shells×2 + fibers×1,
   crafting 1, ×4 per batch, worm_segment sprite). Player: bait
   consumed at cast START (`ActiveAction.Baited`), no refund on cancel,
   ×2 catch with "(bait)" in the message. Workers: one colony-store
   bait per harvest → ×2 haul (`RecruitmentSystem` ~:622, the
   meals/materials `RemoveItem` idiom).
4. **Rod tiers** (`a03597d`): `bone_fishing_rod` (wolf_bone×2 +
   grass_rope×1, crafting 4, bone_wolf sprite). Tier lever = cast
   speed: 2s (`Constants.BoneFishingCastSeconds`) vs 3s. The tool
   check's matched id is hoisted (`matchedTool` in StartAction) so the
   Fishing branch picks the tier; notification says "You cast your
   bone rod...". Colony-store tool check (`CanWorkerHarvest`) matches
   exact + `stone_` + `bone_` prefixes now.

## Live seam map (where the next slice will cut)

- **Player catch pipeline**: `InteractSystem.cs:92-96` routes
  `fishing_rod` → `ActionType.Fishing` → `ActionSystem.StartAction`
  Fishing branch (~`:163-200`) sets duration/bait → `Update` holds
  `DurationRemaining` → `CompleteGathering` (~`:380-430`) rolls success
  (50% + `fishing.success_rate` substat; tests pump it to 1000),
  harvests, doubles if baited, message, then `ProcessCompletion`
  routes XP via the enum.
- **Worker fisher**: gather dispatch in `RecruitmentSystem`
  (~`:488-650`): claim → walk → `HarvestInterval` 4s → `Harvest` →
  level bonus (deterministic counter) → bait double (~`:622`) → haul.
  `CanWorkerHarvest` (~`:780-792`) = tool availability (player inv
  suffix-match OR store exact/stone_/bone_).
- **Rare-drop candidates for the next slice**: drops belong in
  `CompleteGathering`'s success path (player) — a level-scaled roll
  (pearl / old boot; seed a `Random` hook or deterministic counter for
  tests — see the RaidRollOverride pitfall in the skill). Worker side:
  same spot as the bait double. Items `pearl` may not exist yet — grep
  items.json first (the classic RED-compile trap).
- **Smoketest verification**: `dotnet run -- --smoketest <png>` +
  `DSR_SMOKE_FRAMES=<n>`; `DSR_TEST_FISHING=1` stages a mid-cast
  capture. Pixel-diff via PIL in the container (no vision provider in
  the default profile). CRITICAL: the user must NOT touch the game
  window during captures (their movement cancels casts/moves the
  camera) — tell them "hands off ~75s". The Settings.PathOverride fix
  (`Program.cs`, applied BEFORE the Game ctor, VSync=false defaults)
  unblocked captures that used to deadlock on a locked session.

## Taming seam map (added 2026-10-09, slice e8aed14)

- **Core engine**: `NPC/TamingSystem.cs` — `TamingMath` (pure window
  math: 35% base, ±5%/level delta, +2%/success_rate point, clamp
  [5%,95%]), `TryTame(player, monster, combat)` (gates: Tamable flag →
  taming level vs TameLevel → food in inventory; consumes food on
  attempt, 4 XP; success +25 XP, removes the monster, stages a
  RecruitNpc pet with RecruitBehavior "pet" + SpeciesId). `RollOverride`
  (string?: null=rng, ""=forced fail, else forced success) + `Rng`
  seed for tests — the RaidRollOverride convention.
- **Player entry**: `InteractSystem.HandleInteract` — monster branch
  between the NPC-panel branch and the fire-sleep check; 96px reach,
  nearest live tamable wins; success green / failure red
  notifications via the standard AddNotification idiom.
- **Game loop**: `Game.Update` after RecruitmentSystem.Tick —
  follower pets via `Taming.Tick` (PlayerX/PlayerY set first),
  colony pets via `Taming.TickColonyGuard`; surface only
  (`World is { IsCave: false }`).
- **Rendering**: `Game` render pass draws TamedAnimals through
  `RenderNPC` (depth-sorted with everything else);
  `SpriteRenderer.RenderNPC` picks `monster/<SpeciesId>` when the
  recruit is a pet — no new art, species sprites already exist.
- **Persistence**: `SaveData.TamedAnimals`
  (List<TamingSystem.TamedAnimalRecord>) — BuildSnapshot/Restore via
  `RestoreTamedAnimals`; pets are NOT in NPCSystem (the registry
  restore path drops unknown ids), so this is the only round-trip.
- **Data**: monsters.json — `tame_food` + `tame_level` on all ten
  tamable species (wolf 1/raw_meat, boar 2/berries, poison_frog 2 +
  scorpion 3/worm_segment, snake 3 + crocodile 4 + crab 2/raw_fish,
  eagle 4/raw_meat, hawk 1/grass, bear 5/honeycomb); untamable
  species carry neither field (defaults: "" / 1).
- **Skill**: `taming` in SkillManager's id list + SubStatCatalog
  `["taming"] = ["success_rate"]` — lands with its consumer per the
  stat-menus law. TamedAnimalTests pins the catalog entry.


## Next-arc candidates (user-approved order)

1. **Fishing rare-drop table** — **done 2026-10-09 (RED `4f9acfa` /
   GREEN `3ae444b`)** — pearl (sell-only coastal trade entry, 25 gold)
   + old boot (junk, no market), level-scaled windows, one roll per
   successful catch on BOTH player and worker paths, visible
   full-inventory loss. Spec:
   `docs/superpowers/specs/2026-10-09-fishing-rare-drops-design.md`.
2. **Animal taming** — **done 2026-10-09 (RED `7615462` / GREEN
   `2b47599` / wiring `e8aed14`).** Feed-to-tame via E on the ten
   tamable species; new `taming` skill (attempt 4 XP, success 25 XP,
   success_rate sub-stat); first pet bonds as follower (companion
   tether contract), later pets colony-guard (approach-and-strike in a
   6-tile radius). Full seam notes above. Spec:
   `docs/superpowers/specs/2026-10-09-animal-taming-design.md`.
3. **Cross-skill audit** — **done 2026-10-09 (slices A+B: RED
   `05f9017`/GREEN `29cb967`, RED `51b88f7`/GREEN `4b56cdb`).**
   GatherSkillFor mirrors the player tool switch (workers chop →
   woodcutting, water → foraging; the colony node-gate reads the
   trained skill); planks day-1 + quest_unlock ENFORCED in Craft and
   the panel (worker auto-production ungated); whittle_sap unblocks
   first_flame for axe-less starts; herb_gathering attached to
   Hemlock; ProgressionReachabilityTests walks item sources + the
   quest DAG every run. Parked findings (clarify-round
   non-picks): "combat"→attack (170 XP vanishing), AddXp Level
   recompute, grass_rope dup key, leader-panel E-fork.
4. **NPC-quest breadth** — **done 2026-10-10 (RED `17b4d62` / GREEN
   `51be69a`).** quests.json 18→23, npcs.json 17→18: the plains hub
   (Hayward the Reaper + plains_harvest → boar_hunt → plains_provender),
   first boards for elder_mara and stone_guardian, the ghost `"combat"`
   skill retargeted to `attack`, and the leader E-fork replaced by a
   role menu with tabs (`NpcHubPanel` — E on a multi-role NPC shows
   Quests AND Diplomacy; single-role NPCs unchanged). Open follow-ups:
   coastal/swamp side quests; return-to-menu after a tab handoff; the
   banked unlock-as-event trigger.

## Standing cautions (beyond the skill's pitfall list)

- `git add` explicit paths; the root accumulates harness dirt
  (package.json etc.).
- Terminal approval flow can block commands when the user is AFK — if
  blocked, do NOT retry; commit with the step marked PENDING and tell
  the user what to re-run.
- `python3 -c` inline scripts and root-path `rm` need approval in this
  environment; prefer the patch/read_file tools.
- The user hops in and out of focus — keep momentum, don't wait on
  confirmations for low-stakes picks, but surface real gameplay
  decisions (they answered the bait-scope clarify in one click).
