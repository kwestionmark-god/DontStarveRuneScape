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
- **Phase 4 — worker schedules and task selection.** Roadmap line, still
  unchecked: "Give workers schedules and simple task selection, with
  sensible fallbacks." The logistics slice (task board, item
  reservations, hauling) is the concrete first cut of this line. It is
  studied, not built: no haul/task-board code is in `src/` yet.
- **Phase 4 — defense and trade wiring, remainder.** Roadmap line, still
  open: "Connect threats, caves, factions, and diplomacy to settlement
  defense and trade without making ordinary play feel like an RTS."
  Faction hostility for guards and faction merchant prices are already
  checked off; caves, broader threats, and diplomacy are not.
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

1. **Logistics slice — task board, reservations, hauling.** First cut of
   the open phase-4 schedules line, and the arc already designed.
   Pillar: MC. The design law shapes it: hauling moves stock the colony
   already owns; it does not spawn resources or replace player gathering.
2. **Structure upgrades.** Closes the last phase-3 checkbox. Pillars: RS
   (player-gated recipes and skill) and DS (materials the player helped
   gather). The design law: upgrades track player progression; shelter
   mitigates weather, it does not erase it.
3. **Worker schedules and task-selection fallbacks.** Finishes the
   phase-4 schedules line after the task board exists to schedule.
   Pillars: MC readability, DS night and season pressure. The design
   law: schedules give the settlement rhythm; they do not maximize
   output past survival pressure.
4. **Threats, caves, factions, diplomacy — the remainder.** After
   residents reliably work and rest. Pillars: DS danger, RS faction
   world. The design law, already in the roadmap wording: wire defense
   and trade without turning play into an RTS chore. The player still
   explores caves and fights.
5. **Phase 5 persistence, blocked-work feedback, cadence.** See the
   distributable track. Do the save/restore remainder before tuning
   cadence, so larger populations persist correctly.

## Distributable track

The user's stated ambition is a real distributable of this fusion, not
only more features. Phase 5 is that track:

- Persistence polish — save and restore the settlement completely, old
  saves still load.
- Blocked-work feedback — missing materials, full storage, unreachable
  jobs, and danger are visible.
- Simulation cadence and rendering — the colony stays readable as the
  population grows.

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
