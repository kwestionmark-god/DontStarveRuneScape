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
                var biomeRegistry = new BiomeRegistry();
                // Convert BiomesData to BiomeDef objects
                foreach (var biomeDict in dataLoader.BiomesData)
                {
                    if (biomeDict.TryGetValue("id", out var id) && id is string idStr)
                    {
                        var biomeDef = new BiomeDef();
                        // Populate biomeDef from dictionary
                        if (biomeDict.TryGetValue("name", out var name) && name is string n) biomeDef.GetType().GetProperty("Name")?.SetValue(biomeDef, n);
                        if (biomeDict.TryGetValue("mega_cluster", out var mc) && mc is string mcs) biomeDef.GetType().GetProperty("MegaCluster")?.SetValue(biomeDef, mcs);
                        if (biomeDict.TryGetValue("environmental_pressure", out var ep) && ep is float epf) biomeDef.GetType().GetProperty("EnvironmentalPressure")?.SetValue(biomeDef, epf);
                        if (biomeDict.TryGetValue("starting_safety", out var ss) && ss is bool ssb) biomeDef.GetType().GetProperty("StartingSafety")?.SetValue(biomeDef, ssb);
                        if (biomeDict.TryGetValue("elevation_range", out var er) && er is System.Text.Json.JsonElement ere)
                        {
                            var range = JsonSerializer.Deserialize<int[]>(ere);
                            if (range != null) biomeDef.GetType().GetProperty("ElevationRange")?.SetValue(biomeDef, range);
                        }
                        if (biomeDict.TryGetValue("terrain_colors", out var tc) && tc is System.Text.Json.JsonElement tce)
                        {
                            var colors = JsonSerializer.Deserialize<Dictionary<string, int[]>>(tce);
                            if (colors != null) biomeDef.GetType().GetProperty("TerrainColors")?.SetValue(biomeDef, colors);
                        }
                        if (biomeDict.TryGetValue("resource_spawns", out var rs) && rs is System.Text.Json.JsonElement rse)
                        {
                            var spawns = JsonSerializer.Deserialize<string[]>(rse);
                            if (spawns != null) biomeDef.GetType().GetProperty("ResourceSpawns")?.SetValue(biomeDef, spawns);
                        }
                        biomeRegistry.Biomes[idStr] = biomeDef;
                    }
                }
                // Find starting safety biome as default
                biomeRegistry.DefaultBiome = biomeRegistry.Biomes.Values.FirstOrDefault(b => b.StartingSafety)
                                           ?? biomeRegistry.Biomes.Values.FirstOrDefault();

                var tileMap = WorldGen.Generate(_game.Seed, biomeRegistry, _game.SeasonSystem, progress => 
                {
                    _game.SetLoadingProgress(progress);
                });

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