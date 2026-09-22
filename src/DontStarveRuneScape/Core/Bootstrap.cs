namespace DontStarveRuneScape.Core;

using System.Text.Json;
using System.Threading;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Interactions;

/// <summary>
/// Bootstrap — Handles world generation, save loading, and subsystem initialization.
/// </summary>
public sealed class Bootstrap
{
    private readonly Game _game;
    private readonly int? _saveSlot;

    public Bootstrap(Game game, int? saveSlot)
    {
        _game = game;
        _saveSlot = saveSlot;
    }

    /// <summary>Begin asynchronous world generation.</summary>
    public void BeginWorldGen()
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                // Load data
                var dataLoader = new DataLoader();
                dataLoader.LoadAll();

                // Store DataLoader in Game for save/load
                _game.DataLoader = dataLoader;

                // Generate world
                var biomeRegistry = new BiomeRegistry(dataLoader.Biomes);
                var resourceRegistry = new ResourceRegistry(dataLoader.ResourcesData.Select(d =>
                {
                    var def = new ResourceDef();
                    foreach (var kv in d)
                    {
                        var v = kv.Value?.ToString() ?? string.Empty;
                        switch (kv.Key.ToLowerInvariant())
                        {
                            case "id": def.Id = v; break;
                            case "name": def.Name = v; break;
                            case "biome": def.Biome = v; break;
                            case "tier": def.Tier = int.TryParse(v, out var t) ? t : 1; break;
                            case "category": def.Category = v; break;
                            case "base_density": def.Density = float.TryParse(v, out var d2) ? d2 : 0.1f; break;
                            case "yield_item": def.YieldItem = v; break;
                            case "yield_quantity": def.Yield = int.TryParse(v, out var y) ? y : 1; break;
                            case "xp_reward": def.Xp = float.TryParse(v, out var xp) ? xp : 1f; break;
                            case "depletion_count": def.DepletionCount = int.TryParse(v, out var dc) ? dc : 1; break;
                            case "regrow_time": def.Regrow = float.TryParse(v, out var rt) ? rt : 0f; break;
                            case "sprite_key": def.SpriteKey = v; break;
                            case "requires_tool": def.ToolRequirement = string.IsNullOrEmpty(v) ? null : v; break;
                            case "rarity": def.Rarity = string.IsNullOrEmpty(v) ? "common" : v; break;
                            case "display_scale": def.DisplayScale = float.TryParse(v, out var ds) ? ds : 1f; break;
                            case "size_variance": def.SizeVariance = float.TryParse(v, out var sv) ? sv : 0.2f; break;
                            case "ground_decal": def.GroundDecal = v is "true" or "True" or "1"; break;
                            case "required_level": def.RequiredLevel = int.TryParse(v, out var rl) ? rl : 1; break;
                        }
                    }
                    return def;
                }));

                var tileMap = WorldGen.Generate(_game.Seed, biomeRegistry, resourceRegistry, _game.SeasonSystem, progress =>
                {
                    _game.SetLoadingProgress(progress);
                });

                // Create player at the generated spawn point (safe biome,
                // moderate elevation), not the geographic map center.
                float startX = (tileMap.SpawnX + 0.5f) * Constants.TileSize;
                float startY = (tileMap.SpawnY + 0.5f) * Constants.TileSize;
                var player = new Player(startX, startY);

                // Initialize subsystems
                InitializeSubsystems(tileMap, player, dataLoader);

                // Set result
                _game.World = tileMap;
                _game.Player = player;
                _game.ResourceRegistry = resourceRegistry;
                _game.SetWorldGenResult("success");
            }
            catch (System.Exception ex)
            {
                _game.SetWorldGenError(ex.Message);
            }
        });

        thread.Start();
        _game.SetWorldGenThread(thread);
    }

    /// <summary>Load a save file.</summary>
    public void LoadSave()
    {
        if (_saveSlot.HasValue && _game.SaveSystem != null)
        {
            var saveData = _game.SaveSystem.Load(_saveSlot.Value);
            if (saveData != null)
            {
                // Restore game state from save
                _game.SetWorldGenResult("success");
            }
            else
            {
                _game.SetWorldGenError("Save file not found.");
            }
        }
    }

    /// <summary>Called when world generation completes.</summary>
    public void OnWorldGenComplete()
    {
        // Wire up season/weather to subsystems
        if (_game.Survival != null)
        {
            _game.Survival.SeasonSystem = _game.SeasonSystem;
            _game.Survival.WeatherSystem = _game.WeatherSystem;
        }

        if (_game.Player?.ActionSystem != null)
        {
            _game.Player.ActionSystem.SeasonSystem = _game.SeasonSystem;
            _game.Player.ActionSystem.WeatherSystem = _game.WeatherSystem;
        }
    }

    private void InitializeSubsystems(TileMap tileMap, Player player, DataLoader dataLoader)
    {
        // Survival
        _game.Survival = new SurvivalSystem();
        _game.FoodRegistry = new FoodRegistry(dataLoader.ItemsData);
        _game.Survival.SeasonSystem = _game.SeasonSystem;

        // Skills
        _game.SkillManager = new SkillManager();
        _game.Woodcutting = new Skills.Woodcutting.WoodcuttingSkill(_game.SkillManager);
        _game.Mining = new Skills.Mining.MiningSkill(_game.SkillManager);
        _game.Foraging = new Skills.Foraging.ForagingSkill(_game.SkillManager);
        _game.Firemaking = new Skills.Firemaking.FiremakingSkill();
        _game.Metallurgy = new Skills.Metallurgy.MetallurgySkill(_game.SkillManager);
        _game.Intelligence = new Skills.Intelligence.IntelligenceSkill(_game.SkillManager);
        _game.Cooking = new Skills.Cooking.CookingSkill(_game.SkillManager);
        _game.Construction = new Skills.Construction.ConstructionSkill(_game.SkillManager);

        // Inventory
        _game.Inventory = new Inventory();

        // Apply starter pack
        StarterPack.ApplyStarterPack(_game.Inventory, "default");

        // Crafting
        _game.Crafting = new CraftingSystem();

        // Combat
        _game.CombatSystem = new CombatSystem();

        // Building
        _game.BuildingSystem = new BuildingSystem();

        // NPCs
        _game.NPCSystem = new NPCSystem();
        _game.TradeSystem = new TradeSystem();
        _game.RecruitmentSystem = new RecruitmentSystem();
        _game.QuestSystem = new QuestSystem();
        _game.FactionSystem = new FactionSystem();

        // Seasons & Weather
        _game.SeasonSystem = new SeasonSystem();
        _game.WeatherSystem = new WeatherSystem();

        // Render — TileRenderer and SpriteRenderer are created in Game.InitializeGraphics
        // with the GL context. Other renderers don't need GL.
        _game.ParticleSystem = new Render.ParticleSystem();
        _game.SeasonalRenderer = new Render.SeasonalRenderer();
        _game.LightingSystem = new Render.LightingSystem();

        // Camera
        _game.Camera = new Camera(1280, 720);
        _game.Camera.SetPlayer(player);
        _game.Camera.SetWorld(tileMap);

        // Smoketest hooks: DSR_CAM_PITCH/DSR_CAM_YAW/DSR_CAM_ZOOM override the
        // camera before the first rendered frame so headless captures can be
        // taken at specific angles.
        var cs = System.Globalization.CultureInfo.InvariantCulture;
        float? pitch = float.TryParse(System.Environment.GetEnvironmentVariable("DSR_CAM_PITCH"), System.Globalization.NumberStyles.Float, cs, out var p2) ? p2 : null;
        float? yaw = float.TryParse(System.Environment.GetEnvironmentVariable("DSR_CAM_YAW"), System.Globalization.NumberStyles.Float, cs, out var y2) ? y2 : null;
        float? zoom = float.TryParse(System.Environment.GetEnvironmentVariable("DSR_CAM_ZOOM"), System.Globalization.NumberStyles.Float, cs, out var z2) ? z2 : null;
        if (pitch.HasValue || yaw.HasValue || zoom.HasValue)
            _game.Camera.SetViewAngles(yaw, pitch, zoom);

        // UI
        _game.HUD = new HUD();
        _game.InventoryPanel = new InventoryPanel();
        _game.SkillPanel = new SkillPanel();
        _game.CraftingPanel = new CraftingPanel();
        _game.BuildingPanel = new BuildingPanel();
        _game.GearPanel = new GearPanel();
        _game.TradePanel = new TradePanel();
        _game.QuestPanel = new QuestPanel();
        _game.RecruitPanel = new RecruitPanel();
        _game.DiplomacyPanel = new DiplomacyPanel();
        _game.Dashboard = new DashboardPanel();
        _game.TitleScreen = new TitleScreen();
        _game.LoadingScreen = new LoadingScreen();

        // Input
        _game.InteractSystem = new Interactions.InteractSystem(_game);
        _game.FireInteraction = new Interactions.FireInteraction(_game);
        _game.InputRouter = new InputRouter(_game, _game.InteractSystem, _game.FireInteraction, new Interactions.NPCFlows(_game));

        // Player subsystems
        player.ActionSystem = new Actions.ActionSystem();
        player.ActionSystem.SetTileMap(tileMap);
        player.ActionSystem.WoodcuttingSkill = _game.Woodcutting;
        player.ActionSystem.MiningSkill = _game.Mining;
        player.ActionSystem.ForagingSkill = _game.Foraging;
        player.ActionSystem.Survival = _game.Survival;
        player.ActionSystem.SeasonSystem = _game.SeasonSystem;
        player.ActionSystem.WeatherSystem = _game.WeatherSystem;
        player.SkillManager = _game.SkillManager;
        player.Inventory = _game.Inventory;
        player.Survival = _game.Survival;
        player.WeatherSystem = _game.WeatherSystem;
        player.Gear = new Data.PlayerGear();
    }
}