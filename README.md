# Don't Starve RuneScape

A survival RPG hybrid — Don't Starve's survival pressure meets RuneScape's
skills, combat, and gear progression — written in C# on .NET 8. Rendered
with Silk.NET (SDL2 + OpenGL) and SkiaSharp, with procedural sprites and a
data-driven design (JSON catalogs for items, monsters, recipes, quests,
biomes, and more).

## Features

- **Survival core** — hunger, food items, cooking, firemaking, and a
  starter pack to keep you alive through the first nights.
- **Skills** — Woodcutting, Mining, Foraging, Cooking, Firemaking,
  Crafting, Construction, Metallurgy, and Intelligence, each with its own
  data-driven recipes and tables.
- **Combat & gear** — melee combat against a bestiary of monsters (wolves,
  bears, scorpions, cave trolls, and more), with craftable weapons and
  armor across material tiers from wood to steel.
- **Colony sim** — recruit NPCs, found colonies, assign workplaces, and
  let worker brains and a logistics task board haul and craft for you.
- **Factions, quests & trade** — faction diplomacy, quest givers, and
  merchant trading.
- **A living world** — procedural biomes, seasons, weather, day/night
  lighting, and a cave system with deterministic ore pockets and
  cave-local monsters.
- **Character rendering** — crossed-billboard "paper doll" bodies with
  procedural walking gaits, dome boots, turn lean, and sprint flavor,
  shared across the player, NPCs, and monsters.

## Requirements

- .NET 8 SDK
- Linux (primary target; SDL2 handles windowing and input)

## Build & run

```bash
dotnet build src/DontStarveRuneScape
dotnet run --project src/DontStarveRuneScape
```

## Tests

```bash
dotnet test src/DontStarveRuneScape.Tests
```

The suite (xUnit) covers combat, crafting, colony logistics, skills,
gait animation, panel behavior, and more.

## Controls

| Key | Action |
| --- | --- |
| W A S D | Move |
| Shift | Sprint (drains stamina) |
| E | Interact |
| J | Attack |
| F | Light fire |
| C / I | Inventory |
| Tab | Skill panel |
| H | Crafting |
| B | Building panel |
| G | Gear |
| U | Quests |
| T | Trade |
| K | Recruit |
| L | Diplomacy |
| O | Dashboard |
| 1–8 | Hotbar |
| Arrow keys / PgUp / PgDn | Orbit and tilt camera |
| F5 | Save game |
| Esc | Close panel / pause menu |

## Headless smoketest & visual verification

The game can render a fixed number of frames headlessly and dump a
screenshot, which is how rendering changes are verified:

```bash
DSR_SEED=12345 dotnet run --project src/DontStarveRuneScape --no-build -- --smoketest out.png
```

Useful environment variables:

- `DSR_SEED` — world seed
- `DSR_POS_X` / `DSR_POS_Y` — spawn position
- `DSR_CAM_ZOOM` / `DSR_CAM_PITCH` / `DSR_CAM_YAW` — camera setup (degrees)
- `DSR_TEST_MOVE` — scripted movement
- `DSR_TEST_MONSTER` — spawn a test monster (`<id>` or
  `<id>:spawnDist:bearingDeg`)
- `DSR_TEST_NPC` — teleport next to an NPC of a type
- `DSR_START_IN_CAVE` — start inside the cave system
- `DSR_SMOKE_FRAMES` — frames to render before capturing

## Project layout

```
src/DontStarveRuneScape/
  Actions/      action queue and stamina
  Building/     structure placement
  Camera/       orbit camera
  Combat/       combat system
  Config/       constants, keybindings, settings
  Core/         bootstrap, game loop, player, saves
  Data/         JSON catalogs (items, monsters, recipes, quests, ...)
  Input/        input manager and router
  Interactions/ NPC flows, fire interaction
  Inventory/    inventory and item storage
  NPC/          colonies, factions, quests, trade, recruitment
  Render/       sprites, billboards, gait, lighting, particles, tiles
  Seasons/      seasons and weather
  Skills/       skill systems with data-driven recipes
  Survival/     food, hunger, starter pack
  UI/           HUD, panels, menus
  World/        world generation, tile map, caves
src/DontStarveRuneScape.Tests/   xUnit test suite
tools/                           sprite generation scripts
docs/                            design docs and roadmaps
```

## License

MIT
