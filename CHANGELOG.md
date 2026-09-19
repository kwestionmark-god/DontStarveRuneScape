# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- **Resource sprites rendering with wrong textures**: PrimitiveBatch no longer
  batches quads across texture changes — each texture switch flushes the batch,
  so resources, terrain overlays, and text draw with their own texture.
- **Terrain elevation overwhelming biomes**: sigmoid curve on normalized
  elevation keeps lowlands clustered and peaks rare; biome thresholds respaced
  for the 0..31 elevation range (water < 3, mountains > 27, etc.)
- **Biome/resource data never reaching worldgen**: biomes now deserialize
  directly to BiomeDef (no reflection copy); ResourceRegistry is constructed
  from resources.json and passed into the resource placer, which honors each
  resource's configured density and sprite key.
- Sprite/terrain texture misses are cached to avoid per-frame filesystem stat.

### Added
- TextRenderer with bundled Liberation Mono fonts; title screen renders text.
- `--smoketest <path.png>` CLI flag: boots past the menus, generates a world,
  renders a few frames, saves the framebuffer PNG, and exits.

### Changed
- Domain-warp amplitude reduced from 12 to 4 tiles to stop biome smearing.

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