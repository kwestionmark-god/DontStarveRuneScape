# Development map — Don't Starve + RuneScape + MountainCore

Status snapshot 2026-10-04, reconciled against
`docs/superpowers/2026-10-04-colony-fusion-roadmap.md` and `git log` on
`main` (HEAD `a97c4e5`). This map covers the whole project. Colony item
checklists stay in the roadmap doc; this file only sequences them.

Pillars and the balance rule are defined once, in
`## Tri-fusion design law` inside
`docs/superpowers/2026-10-04-colony-fusion-roadmap.md`. Tags below cite
that section; they do not restate it.

- **DS** — Don't Starve: player-centered survival.
- **RS** — RuneScape: skill XP and the quest/merchant/faction NPC world.
- **MC** — MountainCore: colony logistics.

## Shipped

- **Colony phases 0–2** (roadmap). Experiment, recruit work foundation,
  settlement anchor/stockpile/save snapshots. MC, with DS survival and RS
  skills left in the player's hands.
- **Colony phase 3 workplaces, mostly** (roadmap checkboxes all checked
  except structure upgrades). Staffed stations, recipe requirements, FIFO
  orders, input fetching, wheat plots, cooking/smelting, multi-stage
  dependencies, construction sites. MC logistics on top of RS crafting XP.
- **Colony phase 4 needs and factions, mostly.** Food from colony stores,
  rest with bench/fire/season, woven shelter, faction-linked guard
  threats, faction-priced trade. DS pressure plus RS faction world.
- **Caves stages 1–2.** Distance-band entrance, beacon, enter/exit prompt,
  persistent cave map, ore bands, cave troll. DS exploration and danger;
  RS mining gated by skill level.
- **Gait and dome-boot rendering.** Shared gait for player, NPCs, and
  monsters (`b68da74`, `22ef2b8`); spherical dome boots (`338a684` and
  follow-ups). Presentation of the DS/RS character, not a new loop.

## In progress

These are the roadmap's remaining open lines. Nothing else on the
roadmap is unchecked.

- **Phase 3 — structure upgrades.** The only open phase-3 checkbox:
  `- [ ] Add structure upgrades.`
- **Phase 4 — worker schedules and task selection.** Roadmap line: "Give
  workers schedules and simple task selection, with sensible fallbacks."
  Its logistic first cut shipped 2026-10-04 (`c7a9b6d`): task board with
  synchronous tile reservations, haul-home carrying, deposit reclaim,
  interruption-safe release, carried-goods persistence. Remaining on the
  line: schedules themselves (time-of-day work rhythms) and the broader
  task-selection fallbacks beyond gather/haul.
- **Phase 4 — defense and trade wiring, remainder.** Done 2026-10-07 —
  caves, broader threats, and diplomacy are now wired (see Ordered next
  work item 4). No phase-4 lines remain open.
- **Phase 5 — persistence and polish.** Three open lines: save/restore of
  anchors, stockpiles, assignments, orders, and resident state;
  blocked-work feedback; simulation cadence and rendering for larger
  populations. Settlement snapshots already save; this line is the
  remainder and the polish.

## Parked

Not next. The project-state memory topic once called cave stage 3 "the
approved next arc." The roadmap's open colony lines are the current
priority; stage 3 waits behind them. One ordered list, below.

- **Cave stage 3** — real darkness, light falloff, cave ambience. Not
  started (`dontstarve-runescape-cave-development.md`).
- **Gait sprite polish** — code and the wolf showcase sprites shipped
  (`22ef2b8`). What remains is optional cosmetic refresh (legless
  creatures such as djinn and serpents), not a blocking pass.
- **Deferred ideas** (`dontstarve-runescape-deferred-ideas.md`):
  click-to-move is dead code; carried equipped tools; torch missing from
  the gear menu.

## Ordered next work

Dependency order, then pillar balance. Each item names how the design
law shapes it.

~~1. **Logistics slice — task board, reservations, hauling.**~~ **Done
   2026-10-04 (`c7a9b6d`).** First cut of the open phase-4 schedules line.
   Pillar: MC. Per the design law, hauling moves stock the colony already
   owns; it does not spawn resources or replace player gathering.
2. **Structure upgrades** — done 2026-10-06. Closes the last phase-3
   checkbox. Pillars: RS (player-gated recipes and skill) and DS (materials
   the player helped gather). The design law held: upgrades track player
   progression via the construction skill gate; the new shelter tiers
   mitigate weather (stronger recovery, more HP) but never nullify it.
   Spec: `docs/superpowers/specs/2026-10-06-structure-upgrades-design.md`.
3. **Worker schedules and task-selection fallbacks** — done 2026-10-06.
   Finishes the phase-4 schedules line: 24-slot schedule templates (worker
   day, guard shift squads, bedtime, meals, night floor) plus fallbacks
   (FREE_TIME never dispatches work; idle WORK-slot workers wander near camp,
   labeled Idle). Pillars held: MC readability, DS night/season pressure
   shape when work is safe. Spec:
   `docs/superpowers/specs/2026-10-06-worker-schedules-design.md`.
4. **Threats, caves, factions, diplomacy — the remainder.** Done
   2026-10-07. Closes the phase-4 defense-and-trade line: daily merchant
   visits at 06:00 (standing ≥ 0.65, 4-hour window), diplomacy tab with
   live standing rows and trade click-through, daily hostile-faction raid
   roll (standing < 0.25, territory overlap, 2–4 monster party at the
   perimeter) with "Raid incoming" notifications, cave-expedition guard
   button in the colony dashboard, deterministic raid parties (map-edge
   clamp — fixed a latent RNG flake), and cave expedition save/restore
   (deterministic cave rebuild from seed + entrance). Spec:
   `docs/superpowers/specs/2026-10-06-threats-caves-factions-diplomacy-design.md`.
   Pillars held: DS danger arrives at the settlement from the RS faction
   world; the player never clicks through an RTS menu — reactions flow
   from standing the player already shaped by negotiating and trading.
5. **Phase 5 persistence, blocked-work feedback, cadence.** Done
   2026-10-07 — all three lines closed. Save/restore:
   SettlementPersistenceTests proves the full round-trip end-to-end
   (anchor, stockpile, assignments, queued orders, residents; `2246a2b`),
   and cave expeditions persist (`3cb8652`). Blocked-work feedback:
   missing materials, full storage, unreachable jobs, and paused orders
   were already visible; danger too — a hostile near a workplace pauses
   the job with visible status and the worker backs off (`9124bba`).
   Cadence/readability: the colonist list scrolls past its 3 visible
   rows and the header shows the population count (`9efe566`).
6. **New frontier — post-roadmap features (opened 2026-10-07).** The
   original arc above is done; development continues with fresh feature
   slices the user scoped 2026-10-07. Draft a spec per the house style
   (see specs/) before implementing each. Candidates in the order the
   user raised them — **per-recruit skill stats got the loudest
   enthusiasm** and is likely the best first big slice:
   - **Per-recruit skill stats** — every colony recruit carries its own
     skills/XP that grow through work, mirroring the player's
     SkillManager (gatherers level gathering, guards level combat).
   - **Static resource sprites** — draw resource nodes statically like
     the begun entity-sprite work (reuse existing art for a first pass).
     Done 2026-10-07 (`164585b`): resource nodes render as world-fixed
     crossed billboards — two static world planes on a deterministic
     per-tile facing, the entity paper-doll machinery applied to
     inanimate world dressing. Spec:
     `docs/superpowers/specs/2026-10-07-world-fixed-resource-billboards-design.md`.
   - **More characters** — new starting character defs/choices.
   - **More quests** — expand QuestSystem content.
   - **Starting companion/follower** — an early-game first follower.
   - **Colony-building skill structure** — player skills shaping what
     colonies can build and do.
   - The fusion is **not bound to the inspiration titles**: entirely
     new logic of our own is welcome and encouraged.

## Distributable track

The user's stated ambition is a real distributable of this fusion, not
only more features. Phase 5 is that track:

- Persistence polish — save and restore the settlement completely, old
  saves still load.
- Blocked-work feedback — missing materials, full storage, unreachable
  jobs, and danger are visible.
- Simulation cadence and rendering — the colony stays readable as the
  population grows.

All three landed 2026-10-07 (see Ordered next work item 5): full
round-trip proven by test, danger feedback in the dispatch, scrollable
colonist list with population counter. The colony-fusion feature arc is
feature-complete; what remains before a distributable is packaging and
play QA, not features.

Feature work (items 1–4) and this track stay distinct. Phase 5 does not
wait for parked arcs. Parked cave atmosphere and sprite polish are not
prerequisites for a first distributable build.

## Where the detail lives

- Colony checkboxes, progress log, decisions:
  `docs/superpowers/2026-10-04-colony-fusion-roadmap.md`.
- Pillars and balance rule: `## Tri-fusion design law` in that same doc.
- Caves: memory topic `dontstarve-runescape-cave-development.md`.
- Gait: memory topic `gait-for-entities-plan.md`.
- Deferred cosmetics and click-to-move:
  `dontstarve-runescape-deferred-ideas.md`.
