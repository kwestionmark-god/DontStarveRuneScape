# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **Sea level raised to a permanent 11.5** (was 6.5): the old flood system was
  manufacturing ~25% water coverage from a heightmap that almost never dips
  below 6.5; at 11.5 about a quarter of the world is genuinely below the sea
  plane (deepest beds ~7 levels down). Spawn search now requires dry ground
  above the sea.
- **Land is never clipped — water is a contact layer**: dry tiles always draw
  whole (fixing the missing wedges at shorelines); where a tile's terrain dips
  below the sea plane, the sheet is painted over the dipped region via sub-tile
  marching squares (DrawSeaSheetPatch) with world-anchored UVs and
  depth-ramped tint and opacity, so water visibly meets land along the true
  contour. Perf at a shoreline spot: 5.39ms avg / 6.49ms p95.
- **Water is now a physical sea-level layer, not a tile class**: worldgen no
  longer converts low tiles to a water biome; any terrain at or below the
  constant `SeaLevel` simply intersects the flat sea plane. The renderer draws
  each submerged tile as bed terrain plus a flat sheet quad at exactly
  SeaLevel whose per-corner tint and opacity ramp with depth (glassy shallows,
  opaque deeps), with world-anchored scrolling flow texture so the ocean reads
  as one seamless body. Dry land tiles straddling the plane are clipped
  against it with sub-tile marching squares, so the visible waterline is the
  true terrain/plane contour from every camera yaw — no tile-stepped shores.
  Corner elevations are floats now (was int-terraced), so slopes are smooth
  ramps and contours curve. Removed the priority-flood pool system,
  per-tile pool quads, drop faces, and the banded lapping-blanket overlays.
- Sea flow texture is Repeat-wrapped and its contrast is flattened toward the
  mean at load, replacing the harsh white speckle with a gentle current.

### Fixed
- **Flattened world / mountain-only biomes**: Perlin2D simplex attenuation used
  t^2 instead of t^4, pushing noise to ±6; combined with min-max normalization
  and a steep sigmoid, 60% of tiles pinned to elevation max. Kernel fixed,
  ridged noise switched to the Python `1 - 2|n|` form, and elevation/moisture
  shaping ported from the Python generator.
- **Resources only spawning on water**: ResourcePlacer rejected tiles outside
  biome `elevation_range` (legacy 0-7 scale vs 0-31 elevation). The gate was
  removed and the placer rewritten to the Python per-tile model with
  rarity-clamped densities and a 40% occupancy cap.
- **Bottom half of screen unpainted**: tile culling did not account for
  elevation displacement; the view rect is now padded by the max terrain
  displacement in tiles.
- **Painter order breaking under camera yaw**: tiles drew row-major and sprite
  depth keys mixed tile-index units with world-pixel units. Tiles are now
  depth-sorted per frame and all sprite depth keys use world-pixel coordinates.
- **Resource sprites sliding across tiles when panning the camera**: resources
  were screen-center anchored; they are now bottom-anchored billboards at the
  tile center, matching the player sprite.
- **Sprites floating above low-elevation ground**: render/depth anchoring used
  `tile.Elevation` instead of the bilinear corner height at the actual position.
- **Camera centering drifted with pitch/zoom**: the camera anchored the
  elevation-0 plane. The focus now anchors to the player's bilinear ground
  elevation, and the vertical anchor is pitch-adaptive (lower at shallow pitch,
  centered near top-down).

### Added
- TextRenderer with bundled Liberation Mono fonts; title screen renders text.
- `--smoketest <path.png>` CLI flag: boots past the menus, generates a world,
  renders a few frames, saves the framebuffer PNG, and exits.
- `DSR_CAM_PITCH`/`DSR_CAM_YAW`/`DSR_CAM_ZOOM` env vars override the camera for
  headless captures.
- Player sprite animation (idle/walk frame sets) with bottom-center anchoring;
  small pixel-art sprites are pre-upscaled with nearest-neighbor sampling.

### Changed
- Domain-warp amplitude reduced from 12 to 4 tiles to stop biome smearing.
- Camera pitch range widened to 10°–80°.
- The biome classifier is a direct port of the Python six-band
  elevation/moisture tree.
- Terrain textures receive the slope/elevation shading as a grayscale tint.

## [0.1.1] - 2026-09-10

### Fixed
- **Compilation Errors**: Fixed all remaining build errors (~20) across core systems
  - SurvivalSystem: Added setter to MaxHunger property for snapshot restoration
  - Program.cs: Resolved ambiguous Version/Window references with alias imports
  - WorldGen.cs: Fixed float-to-int conversions for corner elevations
  - NPCFlows.cs: Fixed duplicate variable `qty`, unassigned variable, and static `Game.ErrorColor` access
  - Bootstrap.cs: Replaced `JsonElement.Deserialize` with `JsonSerializer.Deserialize`; made `BiomeRegistry.DefaultBiome` setter public
  - Game.cs: Fixed tuple conversion errors in render drawables with named tuple syntax; added `HandleEvent` methods to TitleScreen/CharacterSelectPanel/LoadingScreen; removed unused InputRouter.Handle call with SDL Event
  - TileMap.cs: Fixed `RegrowTimer` → `RegrowTime` references; replaced read-only `IsDepleted` assignments with `Density = 0f` pattern
  - Program.cs: Removed non-existent `Initialize()` calls from renderers and UI panels

### Changed
- Simplified Program.cs Load event handler - renderers/panels now initialize on first use

## [0.1.0] - 2026-09-09

### Added
- **Project Setup**: C# .NET 8 project with Silk.NET (SDL2, OpenGL, Maths, Windowing), System.Text.Json, MathNet.Numerics
- **Configuration System**: Constants.cs and Keybindings.cs ported from Python config
- **Core Game Architecture**:
  - GameState.cs - Discrete state machine (Title, Loading, Playing, Panel states)
  - Player.cs - Player entity with stats, inventory, skills, position
  - SaveSystem.cs - JSON-based save/load with slot support
  - Game.cs - Main game loop aggregator
  - Bootstrap.cs - Data loading and initialization sequence
- **Data System**:
  - DataLoader.cs - Instance-based JSON data loading (refactored from static utility)
  - Data classes: Biome, Item, Resource, Monster, Gear, Structure, NPC, Quest, Faction, Trade, Recipe
  - All JSON data files: biomes, items, resources, monsters, gear, structures, NPCs, quests, factions, trade items, recipes
  - Skill-specific data: construction recipes, cooking recipes, firemaking fuel table, intelligence enchantments/recipes, metallurgy recipes
- **World Generation**:
  - Noise.cs - Perlin noise implementation using MathNet.Numerics
  - Tile.cs - Tile data structure with biome, elevation, resources
  - TileMap.cs - 2D tile grid with spatial queries
  - WorldGen.cs - Procedural world generation with biome classification and resource placement
- **Camera System**: Camera.cs - Orbital camera with yaw, pitch, zoom, pan controls
- **Input System**:
  - InputState.cs - Keyboard, mouse, gamepad state tracking
  - InputManager.cs - SDL2 input polling and event handling
  - InputRouter.cs - Context-aware input routing (game, UI, panels)
- **Survival System**:
  - SurvivalSystem.cs - Hunger, health, stamina with seasonal/weather modifiers
  - FoodItem.cs / FoodRegistry.cs - Food data with nutrition, spoilage
  - StarterPack.cs - Starting equipment and supplies
- **Action System**:
  - ActionType.cs, ActionState.cs, ActionResult.cs - Action framework
  - ActiveAction.cs - Running action with progress tracking
  - StaminaPool.cs - Stamina resource management
  - ActionNotification.cs - User feedback for action events
  - ActionSystem.cs - Woodcutting, mining, cooking, foraging with stamina costs
- **Interaction System**:
  - InteractSystem.cs - Generic interaction framework
  - FireInteraction.cs - Campfire cooking, warmth
  - NPCFlows.cs - NPC dialogue and interaction flows
- **Building System**:
  - Structure.cs - Building data with requirements, outputs, upgrades
  - BuildingSystem.cs - Placement, construction, validation, grid snapping
- **NPC System**:
  - NPC.cs - NPC entities with AI, factions, schedules
  - NPCSystem.cs - Spawning, updating, despawning
  - TradeSystem.cs - Player-NPC trading with dynamic prices
  - QuestSystem.cs - Quest tracking, objectives, rewards
  - RecruitmentSystem.cs - Follower recruitment and management
  - FactionSystem.cs - Faction reputation, diplomacy, territory
- **Skills System** (8 skills with OSRS XP formula and sub-stats):
  - SkillManager.cs - Central skill registry, XP calculation, level ups
  - WoodcuttingSkill.cs - Tree felling, log processing
  - MiningSkill.cs - Ore extraction, gem finding
  - ForagingSkill.cs - Plant gathering, herb identification
  - CookingSkill.cs - Food preparation, recipe discovery
  - FiremakingSkill.cs - Fire lighting, fuel efficiency
  - CraftingSkill.cs - Item crafting, material processing
  - MetallurgySkill.cs - Smelting, alloy creation
  - ConstructionSkill.cs - Building construction, blueprints
  - IntelligenceSkill.cs - Enchanting, research, mana
- **Combat System**:
  - CombatSystem.cs - Turn-based and real-time hybrid combat
  - Monster.cs - Monster AI, stats, loot tables
  - DamageNumber.cs - Floating damage text
- **Render System**:
  - TileRenderer.cs - 2.5D terrain rendering with biome transitions
  - SpriteRenderer.cs - Animated sprite batching
  - CombatUI.cs - Health bars, targeting indicators
  - ParticleSystem.cs - Environmental and combat particles
  - SeasonalRenderer.cs - Seasonal visual transitions
  - LightingSystem.cs - Dynamic lighting with day/night cycle
- **Season/Weather System**:
  - SeasonSystem.cs - Four seasons with gameplay effects
  - WeatherSystem.cs - Rain, snow, storms with visual/audio
- **Inventory System**: Inventory.cs - 20-slot grid with spoilage, stacking, categories
- **Crafting System**: CraftingSystem.cs - Recipe-based crafting with stations
- **UI System**:
  - HUD.cs - Health, hunger, stamina, skills, minimap
  - Panels.cs - Trade, Quest, Recruitment, Diplomacy, Faction Info panels
  - Panels2.cs - Inventory, Skill, Crafting, Building, Gear, Dashboard (5 tabs)
  - Screens.cs - Title, Character Select, Loading screens
- **Assets**: 300+ procedural PNG sprites for terrain, trees, rocks, items, gear, monsters, NPCs, structures, UI
- **License**: MIT License

### Changed
- Refactored DataLoader from static utility to instance class with loaded data fields for Bootstrap consumption
- Fixed project naming from "Don'tStarveRuneScape" to "DontStarveRuneScape" to avoid apostrophe issues

### Infrastructure
- Git repository initialized with .gitignore for .NET projects
- GitHub repository created at https://github.com/kwestionmark-god/DontStarveRuneScape
- Initial commit with full C# port (451 files, 17,000+ lines)
- CI/CD ready project structure