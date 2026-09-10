namespace DontStarveRuneScape.Core;

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
        var thread = new Thread(() =>
        {
            try
            {
                // Load data
                var dataLoader = new DataLoader();
                dataLoader.LoadAll();

                // Generate world
                var worldGen = new WorldGen(_game.Seed);
                var tileMap = worldGen.Generate();

                // Create player at center
                float startX = Constants.MapWidth * Constants.TileSize / 2f;
                float startY = Constants.MapHeight * Constants.TileSize / 2f;
                var player = new Player(startX, startY);

                // Initialize subsystems
                InitializeSubsystems(tileMap, player, dataLoader);

                // Set result
                _game.World = tileMap;
                _game.Player = player;
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

        // Render
        _game.TileRenderer = new Render.TileRenderer();
        _game.SpriteRenderer = new Render.SpriteRenderer();
        _game.ParticleSystem = new Render.ParticleSystem();
        _game.SeasonalRenderer = new Render.SeasonalRenderer();
        _game.LightingSystem = new Render.LightingSystem();

        // Camera
        _game.Camera = new Camera(1280, 720);
        _game.Camera.SetPlayer(player);

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