# Hub-tab return — Esc from a role panel returns to the NPC's menu — design

Date: 2026-10-11
Status: draft
Slice: quest-slice follow-up — "NpcHubPanel tab → panel → Esc lands in
Playing; E again reopens the hub". A leader with quests + diplomacy opens
the role menu; choosing a tab hands off to that panel; closing the panel
should return to the menu, not the world.

## Design law

- MC: one small seam, cloned from the proven dashboard-return idiom
  (`DashboardReturnAvailable` + `Game.TryReturnToDashboard`, consumed by
  `InputRouter.Handle`'s O-key head, Game.cs:435-442 / InputRouter.cs:30).
  Zero new systems.
- The hub panel itself stays the single source of role routing
  (`RolesFor`/`OpenSession`); the return only restores the session it
  captured at handoff time.
- RS: an NPC conversation is one continuous interaction — walking out of
  the quest board should land you back at the NPC's menu, not teleport
  you out of the conversation.

## Ground — verified in the tree (2026-10-11)

- `Game.OpenNpcHubTab(tab)` (Game.cs:400-433) hands off to
  Quest/Diplomacy/Recruit/Trade panels. The doc comment claims "The menu
  itself closes with the handoff; press E again to return" — but E in the
  tab panel routes `HandleInteract`, and `NPCFlows.OpenNpcHub` re-opens
  via `NpcHub.OpenSession` only when the hub is gone... actually the hub
  CAN re-open by E (CheckProximity re-fires), but Esc/Q in the tab panel
  (`HandleGenericPanelInput`, InputRouter.cs:240-247) goes straight to
  Playing — the hub session and its remembered tab are lost, and the
  player must walk out of the interaction and re-E.
- `SetState` on a panel→panel move calls `InputRouter.CloseAllPanels()`
  (Game.cs:239-241) → `NpcHub.Close()` (Panels.cs:343) → `Session = null`,
  `Tabs = []` — so the hub cannot be restored from itself after the
  handoff. The npc reference must be held by the Game, like
  `DashboardReturnAvailable` is.
- The dashboard precedent: `TryReturnToDashboard` checks
  `DashboardReturnAvailable && Dashboard != null && State is not
  DashboardOpen/Playing`, then `SetState(DashboardOpen)`. Consumed at the
  HEAD of `InputRouter.Handle` (before the state switch) for the O key.
  `SetState` flips `DashboardReturnAvailable = false` when entering
  Playing/Paused/SettingsPanel (Game.cs:228-229) so the flag never leaks
  into the world.
- Esc in tab panels: every tab-panel router case funnels non-arrow keys
  to `HandleGenericPanelInput`, whose Esc/Q arm does
  `CloseAllPanels(); SetState(Playing)`. That arm is the single place to
  intercept a hub return, and it fires for Trade/Quest/Recruit/Diplomacy
  panels alike (InputRouter.cs:54-85).
- Single-role NPCs (plain giver, merchant, recruit) never pass through
  `OpenNpcHubTab` — they open their panel directly via
  `OpenSingleRole`/`OpenQuestPanel` etc. (NPCFlows.cs:56,69-86), so a
  return-to-hub flag set only by `OpenNpcHubTab` cannot affect them.
  The flag must NOT be set on that path.

## Goal

1. Esc or Q in a panel that was handed off from the NPC hub returns to
   the hub menu, with the session and the remembered tab restored.
2. Esc in a panel opened directly (single-role E, T/U/K/L keys,
   dashboard tabs) still lands in Playing — unchanged.
3. The hub return does not survive a real world transition: entering
   Playing/Paused/Settings from anywhere clears the flag (same rule as
   the dashboard's).

## Non-goals

- No new panels, no hub visual changes, no per-tab state caching beyond
   the active tab id.
- No change to single-role NPC E-routing (the quest-slice behavior
   stands).
- Mouse paths out of scope (keyboard is the test surface; panels' own
   confirm buttons route through the same panels).

## Design

- `Game.HubReturnNpc` (internal Npc?, default null): set to
  `NpcHub.Session` at the top of `OpenNpcHubTab` (before `SetState`,
  which wipes the hub), plus the hub's `ActiveTab` remembered in
  `Game.HubReturnTab` (string, default empty). Cleared by `SetState`
  alongside `DashboardReturnAvailable` (same three-state rule: entering
  Playing/Paused/SettingsPanel nulls it).
- `Game.TryReturnToNpcHub()`: returns false when `HubReturnNpc == null`,
  `NpcHub == null`, or `State` is NpcHub/Playing; else re-opens the hub
  session (`SetState(NpcHub)` then `NpcHub.OpenSession(npc, RolesFor(npc))`
  with `SetActive(rememberedTab)`) and returns true. Transition first,
  populate after — the SetState/CloseAllPanels ordering pitfall.
- `InputRouter.HandleGenericPanelInput`: the Esc/Q arm tries
  `TryReturnToNpcHub()` before falling back to Playing.
  The O-key head (`key == Key.O && TryReturnToDashboard()`) is
  untouched.

## Testing plan

- RED: new tests in a `HubReturnTests` class (the E-routing tests in
  NpcQuestBreadthTests are the harness shape — bare Game + staged
  Player/panels, no Bootstrap):
  1. `OpenNpcHubTab` then Esc in the tab panel → State NpcHub, hub
     Session restored, ActiveTab remembered.
  2. Opening a panel directly (single-role path) then Esc → still
     Playing (flag never set).
  3. `SetState(Playing)` clears the flag (a second Esc from a later
     panel goes to Playing, not the hub).
  4. The return respects roles: hub re-opens with the same roles list
     (a leader with quests+diplomacy shows both tabs again).
- GREEN: implementation as designed; suite 513 → 517 expected.
- No smoketest: keyboard-only UI flow, the router is the real surface
  and unit tests exercise it end to end.

## Open questions → answered (one clarify round, 2026-10-11)

- Continue down the handoff menu after the smoketest? → Yes: hub-tab
  return now, coastal/swamp quests if we're rolling.
