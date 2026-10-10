# NPC-quest breadth — design

- **Date:** 2026-10-10
- **Status:** Implemented (RED `17b4d62` / GREEN `51be69a`; suite 488
  total / 487 green / 1 pre-existing skip)
- **Slice line:** "NPC-quest breadth" (dev-map item 4).
- **Design law:** RS = the quest web teaches the world's skills and
  factions; DS = hunger, beasts, and weather supply the verbs;
  MC = everything new is DATA on the existing condition hooks — no new
  engine systems.

## Goal

1. **Plains hub:** plains currently has a merchant (Joss, "I trade wheat
   for beast parts") and ZERO quest content. New `quest_giver` NPC at the
   plains spawn with a 3-quest chain: wheat harvest → hunt boars →
   deliver provender. Faction: `forest_villagers` (territory_biomes
   already includes `"plains"`, factions.json) — no sixth faction.
2. **Face NPCs get personal quests:** `elder_mara` (forest leader, board
   currently empty) and `stone_guardian` (mountain leader, empty) each
   gain quests on their existing boards. No new named NPCs (a new face
   would need name/dialogue/sprite/faction data — more surface, same
   depth).
3. **Multi-role NPC menu (leadership arc):** `InteractSystem.cs:64-70` —
   a faction leader with available quests opens ONLY the quest panel on
   E; diplomacy is unreachable by keyboard (L-key only). Fix by giving
   multi-role NPCs a *role menu with tabs*, the dashboard's own idiom
   (DashboardPanel: `Tabs`/`ActiveTab`/`SetActive`/`CycleTabs` +
   `OnTabSelected` routing to the real panels). An NPC whose role list
   has 1 entry opens its panel directly (every existing NPC unchanged);
   ≥2 entries opens the hub, whose tabs launch the existing
   QuestPanel / DiplomacyPanel / RecruitPanel / TradePanel. The E-fork
   dissolves: a quest-bearing leader shows quests AND diplomacy.
4. **Parked-finding fix (quest data):** 3 quests award skill_xp to
   nonexistent `"combat"` (170 XP vanishes) — retarget to `"attack"`.
   quests.json: goes with gem_mining, mountain_pass, coastal_defense.
   (Separate grindable-attack-source question stays parked.)
5. Pure side content: XP + item/gold rewards only. No `recipe_unlocks`,
   no `gear_unlocks`, no `quest_unlock` coupling. The event-unlock idea
   stays banked for its own slice.

## Non-goals

- No new engine systems, no new condition types (all 7 existing hooks
  suffice: collect/deliver/kill/visit/negotiate/trade/craft).
- No new factions, no new named NPC defs, no new sprites (quest panel
  has no icon loader — quest `sprite_key` is inert data, pin is
  catalog-only).
- No repeatable quests (Claim's non-repeatable path is the proven one).
- No change to trade/diplomacy math; the E-fork fix is routing only.
- No work on other parked findings (AddXp level recompute, grass_rope
  dup key, the "combat"→attack *source* question).

## Ground — verified in the tree

- Quest pipeline is fully data-driven: `quests.json` defs →
  `QuestSystem.AcceptQuest/Claim` (stat gates: min_total_intelligence
  = intelligence LEVEL, min_commerce/min_persuasion = sub-stat values,
  acceptance_threshold = faction standing vs `FactionSystem`, default
  0.5 neutral). Progress hooks: NotifyCollect on inventory add,
  NotifyCraft, NotifyKill, NotifyTrade, RecordNegotiation,
  visit_location by player biome in Tick. Claim polls inventory for
  collect/deliver targets (ProgressOf).
- Giver resolution: `AcceptQuest` requires the quest def's
  `giver_npc_type` to match the npc's type and the id to be in the
  npc's `AvailableQuests` (from npcs.json `available_quest_ids`).
- Quest givers today: forest (8 ids), mountains (2), desert (1).
  Leaders with quests: Grak (2), Rourke (2), Zara (1), Kree (2).
  Leaders with EMPTY boards: elder_mara, stone_guardian.
- Plains spawn point exists: `spawn_plains_1` (150,150), currently
  merchant-only. NPCs spawn from registry defs at their def tile
  (NPCSystem.LoadFromRegistry) — a new def entry is sufficient.
- Condition types in use: collect_item, deliver_item, kill_monster,
  visit_location, negotiate_faction, trade_at_location, craft_item.
- Item ids verified present in items.json for targets/rewards: wheat,
  boar_meat, boar_tusk, shells, crab_claw, gemstone, raw_meat,
  healing_herbs, gold, torch. Monster ids: boar, hawk (plains), crab
  (coastal), eagle (mountains), bear, poison_frog, crocodile.
- Rare/trade-only items to AVOID as targets (reachability walk counts
  merchant stock ≤ commerce 3, node yields, monster loot):
  check each new target against ProgressionReachabilityTests.
  `djinn_essence_trade` etc. are sell-only trade entries, not loose
  items.
- Data pins to bump in the SAME commit: NpcDataTests quests 18→23,
  NPCs 17→18, quest_giver count 3→4, spawn points 14→15 (if a new
  spawn point is added for the plains giver — or reuse
  spawn_plains_1's coord on the def; defs carry their own world_x/y,
  spawn_points are legacy/flavor — VERIFY during RED whether
  SpawnPoints count must move).
- QuestSystem tests: NpcDataTests.QuestRegistry_ReadsRealQuestsJson
  asserts sample field values; extend similarly.

## Design

### Quests (all `skill_xp_rewards` target REAL skills only)

Plains chain — new NPC `quest_giver_plains_1` "Hayward the Reaper"
(quest_giver, forest_villagers, (152,152), reused
`npc/quest_giver_male_0` sprite, 3 dialogue lines):

1. `plains_harvest` — collect 6 wheat (wild wheat node, foraging 5).
   thr 0.3, minP 0, minI 1. XP 60 + foraging 30; reward berries×3.
2. `boar_hunt` — kill 2 boars. prereq plains_harvest. thr 0.35,
   minP 0, minI 1. XP 70 + attack 40; reward healing_herbs×2.
3. `plains_provender` — deliver 2 boar_meat to Joss
   (deliver_item:boar_meat×2 + trade_at_location:merchant_plains_1×1).
   prereq boar_hunt. thr 0.4, minC 1, minP 1, minI 2. XP 90 +
   cooking 30; reward gold×15. (deliver_item consumes the items on
   claim — that IS the delivery.)

Elder Mara (forest leader, currently 0 quests — her faction's territory
spans forest+plains):

4. `herbal_remedy` — collect 3 healing_herbs (or herb — pick whichever
   the clinic flavor wants; herb is the node yield, healing_herbs is
   the trade/craft good — VERIFY at RED which reads better), deliver to
   village. Simple collect version: collect 4 herb. prereq
   timber_collection. thr 0.45, minI 2. XP 70 + intelligence 30;
   reward healing_herbs×2.

Stone Guardian (mountain leader, 0 quests; mountain quest_giver holds
mountain_pass + gem_mining — the leader gets the personal request):

5. `guardians_request` — kill 1 bear (bear = forest/mountain hostile per
   factions) + collect 1 gemstone (mined, mountains). prereq
   mountain_pass. thr 0.55, minP 2, minI 4. XP 110 + mining 40;
   reward gold×25.

(EXACT gates tuned at RED so the DAG-walk test stays green: every
prereq chain must close from prerequisite-free quests, and every
collect/deliver target must be fresh-reachable per the walk.)

### Multi-role NPC menu (tabbed hub, dashboard idiom)

New `NpcHubPanel` (UI/Panels.cs), mirroring DashboardPanel's tab
mechanics exactly:

- `List<string> Tabs` (the NPC's roles, filtered/in order:
  `quests`, `trade`, `recruit`, `diplomacy`), `ActiveTab`, `SetActive`,
  `HandleKey` (Left/Right/Up/Down cycle the strip like the dashboard),
  `HandleConfirm()` → `OnTabSelected?.Invoke(ActiveTab)`, `Close()`.
- `static List<string> RolesFor(Npc npc)` — THE single source of the
  role computation, public so tests pin it without a draw pass:
  `quests` iff `npc.AvailableQuests.Count > 0`; `diplomacy` iff
  `npc.NpcType == "faction_leader"`; `recruit` iff `npc is RecruitNpc`;
  `trade` iff `npc is MerchantNpc`.
- `OpenSession(Npc npc, IReadOnlyList<string> roles)`.

Wiring:

- `GameState.NpcHub` added to the enum, `GameStateExtensions.PanelStates`,
  `InputRouter.IsPanelState`, `CloseAllPanels`, `Game.RenderPanels`, and
  a router case (arrows → `HandleKey`, Enter/Space → `HandleConfirm`,
  else `HandleGenericPanelInput`).
- `Game.NpcHub` public panel property; `Game.OpenNpcHubTab(string tab)`
  sets state exactly like `OpenDashboardTab`: `quests` → QuestPanel
  `OpenSession(hub.Session)`; `diplomacy` → DiplomacyPanel
  `OpenSession(leader)`; `recruit` → RecruitPanel; `trade` → TradePanel.
- `NPCFlows.OpenNpcHub(Npc npc)` — opens the hub, or (roles.Count == 1)
  delegates to the existing single-panel opener so every current NPC
  keeps today's behaviour byte-for-byte.
- `InteractSystem.HandleInteract`: replace the `faction_leader` fork
  (:64-70) with `_npcFlows.OpenNpcHub(npc)` for the leader case; the
  `default` arm keeps opening the quest panel for plain `quest_giver`s
  (their role list is 1 → unchanged).

Net: leader+quests (Grak, Rourke, Zara, Kree, and the new elder_mara /
stone_guardian) → hub with [quests, diplomacy]; leader without quests →
diplomacy as today; merchant / recruit / plain giver → their panel as
today.

**Implementation note (found by the tests, not the design):**
`Game.SetState` closes every open panel on a panel-to-panel move
(`InputRouter.CloseAllPanels`), so both the hub opener and the tab
handoff must transition state FIRST and populate the panel AFTER —
doing it the other way round wipes the session/`FactionInfo` that was
just set. The menu itself closes with the handoff (press E again to
reopen it); a return-to-menu affordance is left as a follow-up.

### Parked finding: "combat" XP

quests.json `gem_mining`, `mountain_pass`, `coastal_defense` award
`skill_xp_rewards: {"combat": N}` — `"combat"` is not a registered skill
(SkillManager ctor list), so AddXpWithNotification no-ops → the XP
vanishes. Retarget all three to `"attack"` (values unchanged).
Pin: a data test asserting every skill_xp_rewards key ∈ the registered
skill id set (prevents recurrence — cheapest version of the parked fix,
does NOT touch AddXp itself).

### Testing plan

RED (one commit, all failing for the RIGHT reasons):
- NpcDataTests: 18→23 quest count, 3→4 quest_giver count, 17→18 NPC
  count, plains giver pinned (id, board contents, first id), elder_mara
  board = [herbal_remedy], stone_guardian board = [guardians_request].
- Quest-data pins: new quest ids exist with the designed
  gates/conditions/rewards; NO quest references a nonexistent skill in
  skill_xp_rewards (combat fix + recurrence test).
- Reachability walk already covers new quests automatically (it iterates
  the registry) — verify it stays GREEN on RED (new targets must all be
  reachable; if any fails at RED, the QUEST DATA is wrong, fix data not
  the walk).
- Hub (multi-role NPC menu): `NpcHubPanel.RolesFor` pinned per NPC
  shape (leader+quests → [quests, diplomacy]; leader w/o quests →
  [diplomacy]; plain giver → [quests]; merchant → [trade]; recruit →
  [recruit]); tab cycling mirrors DashboardPanel_TabSelect_FiresCallback;
  `InteractSystem` on a leaded-with-quests opens the HUB (RED: opens
  QuestPanel today); a plain giver and a quest-less leader keep their
  direct panels; `Game.OpenNpcHubTab("diplomacy")` on a leader session
  lands GameState.DiplomacyPanel scoped to that faction; end-to-end:
  hub → diplomacy tab → negotiate raises standing (through
  `flows.HandleDiplomacyAction`). Bare-Game wiring per the taming-slice
  pitfall (Game.Inventory + Game.SkillManager at the GAME level; panels
  are public settable properties — stage them like PauseFlowTests does).
- Accept/claim flow: plains chain end-to-end via QuestSystem directly
  (accept gated by prereq; progress via Notify hooks; claim consumes
  deliver items).

GREEN: quests.json + npcs.json data, the three `"combat"→"attack"`
edits, InteractSystem/Panels/router changes picked at RED.

Docs: spec status, dev-map item tick + backlog note, CHANGELOG.

### Open questions (resolved)

- Mix: all three shapes, per clarify. Resolved: plains hub + face-NPC
  quests, no new biome givers beyond plains.
- Rewards direction: pure side quests (resolved in chat: no per-quest
  reward guarantees; event-unlock is its own later slice).
- E-fork: in scope, leadership arc (clarify yes).
- New faction for plains? NO — forest_villagers already claims plains
  territory.

### Revision history

- 2026-10-10: drafted from clarify answers + live-tree grounding.
- 2026-10-10: implemented (RED `17b4d62` / GREEN `51be69a`). Design
  adjustments during RED/GREEN: the leader E-fork became a general
  multi-role NPC menu (user's call — dashboard-style tabs) instead of a
  hotkey; the panel-transition ordering fix was found by the tests; the
  parked "combat"-ghost-XP fix rode along (the new registered-skill
  law would otherwise be unenforceable); the `herbal_remedy` count was
  settled at 6 herb (matching its sibling `herb_gathering`'s economy).
