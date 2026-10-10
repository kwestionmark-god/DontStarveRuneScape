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
    private SaveData? _pendingSaveData;

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
                            case "disappears_when_depleted": def.DisappearsWhenDepleted = v is "true" or "True" or "1"; break;
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
                if (_game.PendingCharacterDef != null)
                    player.Name = _game.PendingCharacterDef.Name;

                // Initialize subsystems
                InitializeSubsystems(tileMap, player, dataLoader);

                // Set result
                _game.World = tileMap;
                _game.Player = player;
                _game.ResourceRegistry = resourceRegistry;
                if (_pendingSaveData != null && _game.SaveSystem != null)
                {
                    _game.SaveSystem.LoadIntoGame(_pendingSaveData, _game);
                    _game.RestoreSaveMetadata(_pendingSaveData);
                    _pendingSaveData = null;
                }

                // Headless cave captures can start inside the generated cave
                // without simulating a surface walk and E-key interaction.
                // Must run after _game.World/_game.Player are set: Enter()
                // captures the live surface world to restore on exit.
                if (System.Environment.GetEnvironmentVariable("DSR_START_IN_CAVE") == "1")
                {
                    Tile? entrance = null;
                    foreach (var candidate in tileMap.Tiles)
                        if (candidate.IsCaveEntrance) { entrance = candidate; break; }
                    if (entrance != null)
                    {
                        _game.CaveWorlds.InteractWith(entrance);
                        if (float.TryParse(System.Environment.GetEnvironmentVariable("DSR_CAVE_POS_X"),
                                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cx)
                            && float.TryParse(System.Environment.GetEnvironmentVariable("DSR_CAVE_POS_Y"),
                                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var cy))
                        {
                            player.WorldX = (cx + 0.5f) * Constants.TileSize;
                            player.WorldY = (cy + 0.5f) * Constants.TileSize;
                            player.TargetX = player.WorldX;
                            player.TargetY = player.WorldY;
                            _game.Camera?.SetWorld(_game.World!);
                        }
                    }
                }
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
                _pendingSaveData = saveData;
                _game.Seed = saveData.Seed;
                BeginWorldGen();
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
        _game.Inventory.StackSizes = Inventory.StackSizesFromData(dataLoader.ItemsData);

        // Apply starter pack; the gear slots are created first so the starter
        // tool/torch sync into PlayerGear and render on the character. The
        // pack follows the creation-time background (character backgrounds
        // slice); a missing def or field falls back to the default pack —
        // the pre-slice behavior for old saves and tests.
        var starterGear = new Data.PlayerGear();
        player.Gear = starterGear;
        StarterPack.ApplyStarterPack(_game.Inventory,
            _game.PendingCharacterDef?.StarterPackId ?? "default", starterGear);

        // Crafting
        _game.Crafting = new CraftingSystem();
        _game.Crafting.Registry = new Data.RecipeRegistry();
        _game.Crafting.Registry.LoadAll();

        // Combat
        _game.CombatSystem = new CombatSystem();
        _game.MonsterRegistry = new Data.MonsterRegistry();
        _game.MonsterRegistry.LoadAll();
        _game.CombatSystem.SpawnFromRegistry(_game.MonsterRegistry, tileMap, player);
        _game.CombatSystem.Quests = _game.QuestSystem;

        // Taming (pets: feed-to-tame engine; owned by the game loop)
        _game.Taming = new NPC.TamingSystem();

        // Building
        _game.BuildingSystem = new BuildingSystem();
        _game.BuildingSystem.Registry = new Data.StructureDefRegistry();
        _game.BuildingSystem.Registry.LoadAll();

        // NPCs
        _game.NPCSystem = new NPCSystem();
        _game.ColonySystem = new NPC.ColonySystem();
        _game.TradeSystem = new TradeSystem();
        _game.RecruitmentSystem = new RecruitmentSystem();
        _game.QuestSystem = new QuestSystem();
        _game.FactionSystem = new FactionSystem();
        _game.MerchantVisitSystem = new MerchantVisitSystem(_game);

        // NPC-domain data registries + world spawning
        var npcRegistry = new Data.NpcRegistry();
        npcRegistry.LoadAll();
        _game.NpcRegistry = npcRegistry;
        _game.NPCSystem.LoadFromRegistry(npcRegistry, tileMap);

        var tradeRegistry = new Data.TradeItemRegistry();
        tradeRegistry.LoadAll();
        _game.TradeRegistry = tradeRegistry;

        var questRegistry = new Data.QuestRegistry();
        questRegistry.LoadAll();
        _game.QuestRegistry = questRegistry;

        var factionRegistry = new Data.FactionRegistry();
        factionRegistry.LoadAll();
        _game.FactionRegistry = factionRegistry;

        // Quest/trade progress hooks: quest gates read faction standings;
        // crafts and buys feed quest objectives.
        _game.QuestSystem.Factions = _game.FactionSystem;
        _game.TradeSystem.Factions = _game.FactionSystem;
        _game.Crafting.OnCrafted = item => _game.QuestSystem.NotifyCraft(item);
        _game.TradeSystem.Quests = _game.QuestSystem;

        // Soft death: the survival hook runs the game's death handling.
        _game.Survival.OnDeath = _game.HandlePlayerDeath;

        // Seasons & Weather
        _game.SeasonSystem = new SeasonSystem();
        _game.WeatherSystem = new WeatherSystem();

        // Render — TileRenderer and SpriteRenderer are created in Game.InitializeGraphics
        // with the GL context. Other renderers don't need GL.
        _game.ParticleSystem = new Render.ParticleSystem();
        _game.SeasonalRenderer = new Render.SeasonalRenderer();
        _game.LightingSystem = new Render.LightingSystem();
        _game.DayNight = new World.DayNightCycle();

        // Camera
        _game.Camera = new Camera(1280, 720);
        _game.Camera.SetPlayer(player);
        _game.Camera.SetWorld(tileMap);

        // Smoketest hooks: DSR_POS_X/DSR_POS_Y teleport the player (tile
        // coords) before the first frame so captures can target any spot —
        // e.g. a large lake — without keyboard input. DSR_POS_FRACTION="fx,fy"
        // optionally places the player at a fractional offset INSIDE the tile
        // (default 0.5,0.5 = center) to reproduce depth-sort cases that need
        // the player off-center.
        if (float.TryParse(System.Environment.GetEnvironmentVariable("DSR_POS_X"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tx)
            && float.TryParse(System.Environment.GetEnvironmentVariable("DSR_POS_Y"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ty))
        {
            float fx = 0.5f, fy = 0.5f;
            var fracEnv = System.Environment.GetEnvironmentVariable("DSR_POS_FRACTION");
            if (!string.IsNullOrWhiteSpace(fracEnv))
            {
                var parts = fracEnv.Split(',');
                if (parts.Length == 2)
                {
                    float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fx);
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fy);
                }
            }
            player.WorldX = (tx + fx) * Constants.TileSize;
            player.WorldY = (ty + fy) * Constants.TileSize;
            player.TargetX = player.WorldX;
            player.TargetY = player.WorldY;
        }

        // Smoketest hook: DSR_DUMP_HEIGHTS="cx,cy,r" prints the corner
        // elevations (order NW,NE,SE,SW) of every tile around cx,cy to
        // stdout, so headless captures can pick exact hill/occlusion spots
        // instead of eyeballing slope edges in a screenshot.
        var dumpEnv = System.Environment.GetEnvironmentVariable("DSR_DUMP_HEIGHTS");
        if (!string.IsNullOrWhiteSpace(dumpEnv))
        {
            var dp = dumpEnv.Split(',');
            if (dp.Length == 3
                && int.TryParse(dp[0], out var dcx) && int.TryParse(dp[1], out var dcy) && int.TryParse(dp[2], out var dr))
            {
                for (int dx = dcx - dr; dx <= dcx + dr; dx++)
                for (int dy = dcy - dr; dy <= dcy + dr; dy++)
                {
                    var t = tileMap.GetTile(dx, dy);
                    if (t == null) continue;
                    var c = t.CornerElevations;
                    System.Console.WriteLine(c != null && c.Length == 4
                        ? $"TILE {dx} {dy} {c[0]:F1} {c[1]:F1} {c[2]:F1} {c[3]:F1} water={t.HasWater}"
                        : $"TILE {dx} {dy} flat={t.Elevation:F1} water={t.HasWater}");
                }
            }
        }

        // Smoketest hook: DSR_TEST_MONSTER=<monster_id> spawns one monster of
        // that type right next to the player (deterministic combat captures).
        // "wolf:110" takes an optional spawn distance in world px (default 32,
        // inside attack range), and "wolf:110:270" a spawn bearing in degrees
        // (0 = east), so gait/direction captures can spawn a monster chasing
        // the player from any side.
        var monsterEnv = System.Environment.GetEnvironmentVariable("DSR_TEST_MONSTER");
        if (!string.IsNullOrWhiteSpace(monsterEnv) && _game.MonsterRegistry is { } monsterRegistry
            && _game.CombatSystem != null)
        {
            var monsterParts = monsterEnv.Trim().Split(':');
            var def = monsterRegistry.GetMonster(monsterParts[0]);
            float spawnDist = monsterParts.Length >= 2
                && float.TryParse(monsterParts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d : 32f;
            float bearingDeg = monsterParts.Length >= 3
                && float.TryParse(monsterParts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var b)
                ? b : 0f;
            if (def != null)
            {
                float rad = bearingDeg * MathF.PI / 180f;
                _game.CombatSystem.SpawnMonster(def,
                    player.WorldX + MathF.Cos(rad) * spawnDist,
                    player.WorldY + MathF.Sin(rad) * spawnDist,
                    monsterRegistry.BiomeOf(def.MonsterId));
            }
        }

        // Smoketest hook: DSR_TIME_OF_DAY=<0..1> sets the day/night clock
        // (0 = dawn, 0.25 = noon, 0.5 = dusk, 0.75 = midnight) for
        // day-vs-night lighting captures.
        var timeEnv = System.Environment.GetEnvironmentVariable("DSR_TIME_OF_DAY");
        if (!string.IsNullOrWhiteSpace(timeEnv) && _game.DayNight != null
            && float.TryParse(timeEnv, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tod))
        {
            _game.DayNight.RestoreSnapshot(new World.DayNightSnapshot { TimeOfDay = tod });
        }

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
        _game.HUD.Settings = _game.Settings;
        _game.InventoryPanel = new InventoryPanel();
        _game.SkillPanel = new SkillPanel();
        _game.CraftingPanel = new CraftingPanel();
        _game.BuildingPanel = new BuildingPanel();
        _game.BuildingPanel.BuildCallback = _game.StartPlacement;
        _game.GearPanel = new GearPanel();
        _game.TradePanel = new TradePanel();
        _game.QuestPanel = new QuestPanel();
        _game.RecruitPanel = new RecruitPanel();
        _game.DiplomacyPanel = new DiplomacyPanel();
        _game.Dashboard = new DashboardPanel { Game = _game };
        _game.TitleScreen = new TitleScreen();
        _game.LoadingScreen = new LoadingScreen();

        // Input
        var npcFlows = new Interactions.NPCFlows(_game);
        _game.TradePanel.TradeSystem.Registry = _game.TradeRegistry;
        _game.TradePanel.TradeSystem.Quests = _game.QuestSystem;
        _game.TradePanel.TradeSystem.Factions = _game.FactionSystem;
        _game.TradePanel.Player = player;
        _game.QuestPanel.SetPlayer(player);
        _game.RecruitPanel.Player = player;
        _game.RecruitPanel.OnAction = npcFlows.HandleRecruitAction;
        _game.DiplomacyPanel.Player = player;
        _game.DiplomacyPanel.Registry = _game.FactionRegistry;
        _game.DiplomacyPanel.System = _game.FactionSystem;
        _game.DiplomacyPanel.OnAction = npcFlows.HandleDiplomacyAction;
        _game.Dashboard.OnTabSelected = _game.OpenDashboardTab;
        _game.InteractSystem = new Interactions.InteractSystem(_game, npcFlows);
        _game.FireInteraction = new Interactions.FireInteraction(_game);
        _game.InputRouter = new InputRouter(_game, _game.InteractSystem, _game.FireInteraction, npcFlows);
        if (_game.InputManager != null)
            _game.InputManager.KeyEvent = _game.InputRouter.Handle;

        // Player subsystems
        player.ActionSystem = new Actions.ActionSystem();
        player.ActionSystem.SetTileMap(tileMap);
        _game.CaveWorlds = new CaveWorldSystem(_game);
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
    }
}
