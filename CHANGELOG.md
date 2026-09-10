# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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