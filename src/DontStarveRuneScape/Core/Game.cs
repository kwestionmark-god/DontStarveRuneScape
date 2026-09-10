namespace DontStarveRuneScape.Core;

using System.Text.Json;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using DontStarveRuneScape.Skills.Metallurgy;
using DontStarveRuneScape.Skills.Intelligence;
using DontStarveRuneScape.Skills.Cooking;
using DontStarveRuneScape.Skills.Construction;
using DontStarveRuneScape.Skills.Woodcutting;
using DontStarveRuneScape.Skills.Mining;
using DontStarveRuneScape.Skills.Foraging;
using DontStarveRuneScape.Inventory;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Camera;
using Cam = DontStarveRuneScape.Camera.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.Interactions;
using Silk.NET.Input;
using Silk.NET.SDL;
using SurvCharDef = DontStarveRuneScape.Survival.CharacterDefinition;

/// <summary>
/// Central game state. Single instance, aggregates all subsystems.
/// </summary>
public sealed class Game
{
    // Constants
    public static readonly (byte R, byte G, byte B) SuccessColor = (100, 255, 100);
    public static readonly (byte R, byte G, byte B) ErrorColor = (180, 100, 100);

    // Core state
    public int Seed { get; set; } = 42;
    public GameState State { get; private set; } = GameState.Title;
    public float Dt { get; private set; }
    public float LoadingProgress { get; set; }
    public float PlayTime { get; private set; }
    public int DeathCount { get; set; }

    // World & Player
    public TileMap? World { get; set; }
    public Player? Player { get; set; }

    // Subsystems
    public SurvivalSystem? Survival { get; set; }
    public FoodRegistry? FoodRegistry { get; set; }
    public SkillManager? SkillManager { get; set; }
    public DataLoader? DataLoader { get; set; }
    public CraftingSystem? Crafting { get; set; }
    public Inv? Inventory { get; set; }
    public Cam? Camera { get; set; }
    public HUD? HUD { get; set; }
    public SaveSystem? SaveSystem { get; set; }
    public InputManager? InputManager { get; set; }
    public InputRouter? InputRouter { get; set; }
    public FireInteraction? FireInteraction { get; set; }
    public InteractSystem? InteractSystem { get; set; }

    // Phase 2+ systems
    public CombatSystem? CombatSystem { get; set; }
    public FiremakingSkill? Firemaking { get; set; }
    public MetallurgySkill? Metallurgy { get; set; }
    public IntelligenceSkill? Intelligence { get; set; }
    public CookingSkill? Cooking { get; set; }
    public BuildingSystem? BuildingSystem { get; set; }
    public ConstructionSkill? Construction { get; set; }
    public NPCSystem? NPCSystem { get; set; }
    public TradeSystem? TradeSystem { get; set; }
    public RecruitmentSystem? RecruitmentSystem { get; set; }
    public QuestSystem? QuestSystem { get; set; }
    public FactionSystem? FactionSystem { get; set; }

    // Phase 4: Seasons & Weather
    public SeasonSystem? SeasonSystem { get; set; }
    public WeatherSystem? WeatherSystem { get; set; }

    // Phase 5: Particles & Seasonal Renderer
    public ParticleSystem? ParticleSystem { get; set; }
    public SeasonalRenderer? SeasonalRenderer { get; set; }

    // Phase 6: Lighting
    public LightingSystem? LightingSystem { get; set; }

    // Renderers
    public TileRenderer? TileRenderer { get; set; }
    public SpriteRenderer? SpriteRenderer { get; set; }

    // UI Panels
    public InventoryPanel? InventoryPanel { get; set; }
    public SkillPanel? SkillPanel { get; set; }
    public CraftingPanel? CraftingPanel { get; set; }
    public BuildingPanel? BuildingPanel { get; set; }
    public GearPanel? GearPanel { get; set; }
    public TradePanel? TradePanel { get; set; }
    public QuestPanel? QuestPanel { get; set; }
    public RecruitPanel? RecruitPanel { get; set; }
    public DiplomacyPanel? DiplomacyPanel { get; set; }
    public DashboardPanel? Dashboard { get; set; }
    public TitleScreen? TitleScreen { get; set; }
    public LoadingScreen? LoadingScreen { get; set; }
    public CharacterSelectPanel? CharacterSelectPanel { get; set; }

    // Build mode (PLAYING sub-state)
    public bool BuildMode { get; set; }
    public string? BuildingPendingId { get; set; }
    public (int X, int Y)? BuildCursor { get; set; }

    // Character selection
    public SurvCharDef? PendingCharacterDef { get; set; }

    // Bootstrap
    private Bootstrap? _bootstrap;

    // World generation thread
    private System.Threading.Thread? _worldGenThread;
    private string? _worldGenResult;
    private string? _worldGenError;

    // Autosave
    private float _autosaveTimer = 0f;

    // Flavor text for loading screen
    private string _flavorText = "";

    // Gathering skills (wired to action system)
    public WoodcuttingSkill? Woodcutting { get; set; }
    public MiningSkill? Mining { get; set; }
    public ForagingSkill? Foraging { get; set; }

    public Game(int seed = 42)
    {
        Seed = seed;
        SaveSystem = new SaveSystem();
        InputManager = new InputManager();
        _bootstrap = new Bootstrap(this, null);
    }

    /// <summary>
    /// Set game state and handle transitions.
    /// </summary>
    public void SetState(GameState newState)
    {
        if (State == newState) return;
        var oldState = State;
        State = newState;

        // Handle panel closing
        if (InputRouter != null)
        {
            if (newState == GameState.Playing)
            {
                InputRouter.CloseAllPanels();
            }
            else if (InputRouter.IsPanelState(oldState) && InputRouter.IsPanelState(newState))
            {
                InputRouter.CloseAllPanels();
            }
        }

        // Dashboard visibility
        if (oldState == GameState.DashboardOpen && newState != GameState.DashboardOpen && Dashboard != null)
        {
            Dashboard.Visible = false;
        }

        // Panel visibility
        switch (newState)
        {
            case GameState.InventoryOpen:
                if (InventoryPanel != null) InventoryPanel.Visible = true;
                break;
            case GameState.SkillPanel:
                if (SkillPanel != null) SkillPanel.Visible = true;
                break;
            case GameState.CraftingPanel:
                if (CraftingPanel != null) CraftingPanel.Visible = true;
                break;
            case GameState.BuildingPanel:
                if (BuildingPanel != null) BuildingPanel.Visible = true;
                break;
            case GameState.GearPanel:
                if (GearPanel != null) GearPanel.Visible = true;
                break;
            case GameState.TradePanel:
                if (TradePanel != null) TradePanel.Visible = true;
                break;
            case GameState.QuestPanel:
                if (QuestPanel != null) QuestPanel.Visible = true;
                break;
            case GameState.RecruitPanel:
                if (RecruitPanel != null) RecruitPanel.Visible = true;
                break;
            case GameState.DiplomacyPanel:
                if (DiplomacyPanel != null) DiplomacyPanel.Visible = true;
                break;
            case GameState.DashboardOpen:
                if (Dashboard != null) Dashboard.Visible = true;
                break;
            case GameState.CharacterSelect:
                if (CharacterSelectPanel == null)
                {
                    CharacterSelectPanel = new CharacterSelectPanel();
                    CharacterSelectPanel.SetConfirmCallback(_ => { /* handled via event */ });
                }
                CharacterSelectPanel.Visible = true;
                break;
        }

        // Hide panels for states not listed above
        if (newState != GameState.InventoryOpen && InventoryPanel != null) InventoryPanel.Visible = false;
        if (newState != GameState.SkillPanel && SkillPanel != null) SkillPanel.Visible = false;
        if (newState != GameState.CraftingPanel && CraftingPanel != null) CraftingPanel.Visible = false;
        if (newState != GameState.BuildingPanel && BuildingPanel != null) BuildingPanel.Visible = false;
        if (newState != GameState.GearPanel && GearPanel != null) GearPanel.Visible = false;
        if (newState != GameState.TradePanel && TradePanel != null) TradePanel.Visible = false;
        if (newState != GameState.QuestPanel && QuestPanel != null) QuestPanel.Visible = false;
        if (newState != GameState.RecruitPanel && RecruitPanel != null) RecruitPanel.Visible = false;
        if (newState != GameState.DiplomacyPanel && DiplomacyPanel != null) DiplomacyPanel.Visible = false;

        // Trigger world generation / save loading
        if ((oldState == GameState.Title || oldState == GameState.CharacterSelect) && newState == GameState.Loading)
        {
            _bootstrap?.BeginWorldGen();
        }
        else if (oldState == GameState.Title && newState == GameState.LoadingSave)
        {
            _bootstrap?.LoadSave();
        }
        else if (newState == GameState.Playing && (oldState == GameState.Loading || oldState == GameState.LoadingSave))
        {
            _bootstrap?.OnWorldGenComplete();
        }
    }

    /// <summary>
    /// Open the unified dashboard on a specific tab.
    /// </summary>
    public void OpenDashboard(string tab)
    {
        if (Dashboard == null) return;
        Dashboard.SetActive(tab);
        if (State != GameState.DashboardOpen)
            SetState(GameState.DashboardOpen);
    }

    /// <summary>
    /// Check if background world generation has completed.
    /// </summary>
    public void CheckWorldGenComplete()
    {
        if (State != GameState.Loading && State != GameState.LoadingSave) return;
        if (_worldGenThread != null && _worldGenThread.IsAlive) return;

        if (_worldGenError != null)
        {
            State = GameState.Error;
        }
        else if (_worldGenResult == "success")
        {
            SetState(GameState.Playing);
        }
        else
        {
            State = GameState.Error;
            _worldGenError = "World generation failed silently";
        }
    }

    /// <summary>
    /// Main update loop.
    /// </summary>
    public void Update(float dt)
    {
        Dt = dt;
        PlayTime += dt;

        // Always-tick systems (even under panels)
        Survival?.Tick(dt);

        if (Inventory != null && Player != null)
        {
            var spoilageMessages = Inventory.Tick(dt);
            foreach (var msg in spoilageMessages)
            {
                Player.ActionSystem?.AddNotification(msg, (255, 200, 50));
            }
        }

        // Autosave
        if (State == GameState.Playing && SaveSystem != null)
        {
            _autosaveTimer += dt;
            if (_autosaveTimer >= Constants.AutosaveInterval)
            {
                _autosaveTimer = 0f;
                SaveSystem.Save(this, 0);
            }
        }

        // Gameplay update (only when playing)
        if (State == GameState.Playing)
        {
            UpdatePlaying(dt);
        }

        // Clear one-shot input flags at END of frame
        InputManager?.ClearFrame();
    }

    private void UpdatePlaying(float dt)
    {
        // World update (regrowth, etc.)
        World?.Update(dt);

        // Player movement
        if (Player != null && InputManager != null)
        {
            float cameraYaw = Camera?.Yaw ?? 0f;
            Player.ApplyKeyInput(InputManager.InputState, dt, cameraYaw);
            Player.Update(dt);
        }

        // Survival tile effects
        if (World != null && Player != null && Survival != null)
        {
            var (tx, ty) = Player.GetTilePosition();
            var currentTile = World.GetTile(tx, ty);
            Survival.UpdateFromTile(currentTile);
        }

        // Action system
        if (Player != null && Inventory != null && SkillManager != null)
        {
            var actionSys = Player.ActionSystem;
            if (actionSys != null)
            {
                var result = actionSys.Update(dt);
                actionSys.ProcessCompletion(result, Inventory, SkillManager, FoodRegistry);
                actionSys.UpdateNotifications(dt);
            }
        }

        // HUD notifications
        if (HUD != null)
        {
            var actionSys = Player?.ActionSystem;
            if (actionSys != null)
            {
                HUD.SetNotifications(actionSys.Notifications);
                if (actionSys.Active != null && actionSys.Active.State == ActionState.Running)
                {
                    string skillName = actionSys.Active.ActionType == ActionType.Woodcutting ? "Woodcutting" : "Mining";
                    HUD.SetActionProgress(actionSys.Active.Progress, skillName);
                }
                else
                {
                    HUD.SetActionProgress(0f, "");
                }
                HUD.TickNotifications(dt);
            }
        }

        // Camera
        if (Camera != null && InputManager != null)
        {
            Camera.Update(dt, InputManager.InputState);
        }

        // Combat
        CombatSystem?.Tick(dt);

        // Firemaking
        Firemaking?.Tick(dt);

        // Building
        BuildingSystem?.Tick(dt, CombatSystem);

        // NPCs
        NPCSystem?.Tick(dt);
        RecruitmentSystem?.Tick(dt);
        NPCSystem?.TickFactionNPCs(dt);

        // Trade
        TradeSystem?.Tick(dt);

        // Quests
        QuestSystem?.Tick(dt, Player, World);

        // Weather
        WeatherSystem?.Tick(dt);

        // Seasons
        SeasonSystem?.Tick(dt);

        // Seasonal renderer sync
        if (SeasonalRenderer != null && SeasonSystem != null)
        {
            SeasonalRenderer.Sync(SeasonSystem.SeasonProgress,
                SeasonSystem.CurrentSeason, SeasonSystem.PreviousSeason);
        }

        // Particles
        if (ParticleSystem != null && WeatherSystem != null)
        {
            ParticleSystem.Sync(WeatherSystem.CurrentWeather);
            ParticleSystem.Update(dt);
        }

        // Lighting (Phase 6)
        if (LightingSystem != null)
        {
            if (SeasonSystem != null)
                LightingSystem.UpdateFromSeason(SeasonSystem);
            if (WeatherSystem != null)
                LightingSystem.UpdateFromWeather(WeatherSystem);
        }
    }

    /// <summary>
    /// Main render loop.
    /// </summary>
    public void Render(Silk.NET.OpenGL.GL gl, int screenWidth, int screenHeight)
    {
        switch (State)
        {
            case GameState.Title:
                TitleScreen?.Render(this, gl, screenWidth, screenHeight);
                break;
            case GameState.CharacterSelect:
                CharacterSelectPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.Loading:
            case GameState.LoadingSave:
            case GameState.Error:
                LoadingScreen?.Render(this, gl, screenWidth, screenHeight);
                break;
            default:
                RenderGame(gl, screenWidth, screenHeight);
                break;
        }
    }

    private void RenderGame(Silk.NET.OpenGL.GL gl, int screenWidth, int screenHeight)
    {
        // Clear with fog color
        var fog = Constants.FogColor;
        gl.ClearColor(fog.R / 255f, fog.G / 255f, fog.B / 255f, 1f);
        gl.Clear((uint)Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit);

        // Render terrain
        if (TileRenderer != null && Camera != null)
        {
            TileRenderer.Render(Camera);
        }

        // Collect all sprite drawables with depth sort key
        var drawables = new List<(float Depth, Action Draw)>();

        if (SpriteRenderer != null && World != null && Camera != null)
        {
            var (left, top, right, bottom) = Camera.GetViewRect();
            int xMin = Math.Max(0, (int)(left / Constants.TileSize));
            int xMax = Math.Min(World.Width, (int)(right / Constants.TileSize) + 1);
            int yMin = Math.Max(0, (int)(top / Constants.TileSize));
            int yMax = Math.Min(World.Height, (int)(bottom / Constants.TileSize) + 1);

            // Resources
            for (int x = xMin; x < xMax; x++)
            {
                for (int y = yMin; y < yMax; y++)
                {
                    var tile = World.Tiles[x, y];
                    if (tile?.ResourceNode != null)
                    {
                        float sortY = GetDepthSort(x + 0.5f, y + 0.5f, tile.Elevation * Constants.ZScale);
                        drawables.Add((Depth: sortY, Draw: new Action(() => SpriteRenderer.RenderResource(
                            tile.ResourceNode!, Camera!, (int)tile.Elevation, x, y))));
                    }
                }
            }

            // Player
            if (Player != null)
            {
                var (ptx, pty) = Player.GetTilePosition();
                var tile = World.GetTile(ptx, pty);
                float elev = (tile?.Elevation ?? 0) * Constants.ZScale;
                float sortY = GetDepthSort(Player.WorldX, Player.WorldY, elev);
                drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderPlayer(
                    Player, Camera!, elev, Dt)));
            }

            // Monsters
            if (CombatSystem != null)
            {
                foreach (var monster in CombatSystem.Monsters)
                {
                    if (monster.IsAlive())
                    {
                        float sortY = GetDepthSort(monster.WorldX, monster.WorldY, 0);
                        drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderMonster(monster, Camera!)));
                    }
                }
            }

            // NPCs
            if (NPCSystem != null && Player != null)
            {
                var nearbyNpc = NPCSystem.CheckProximity(Player);
                foreach (var npc in NPCSystem.NPCs)
                {
                    if (!npc.IsActive) continue;
                    float sortY = GetDepthSort(npc.WorldX, npc.WorldY, 0);
                    drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderNPC(npc, Camera!, 0)));

                    if (npc == nearbyNpc)
                    {
                        drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderProximityPrompt(npc, Camera!)));
                    }
                }
            }

            // Structures
            if (BuildingSystem != null)
            {
                foreach (var structure in BuildingSystem.Structures)
                {
                    if (structure.IsActive)
                    {
                        float sortY = GetDepthSort(structure.WorldX, structure.WorldY, 0);
                        drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderStructure(structure, Camera!)));
                    }
                }
            }

            // Build mode ghost preview
            if (BuildMode && Camera != null && World != null && BuildCursor.HasValue)
            {
                drawables.Add((Depth: float.MaxValue, Draw: () => RenderBuildGhost(gl)));
            }

            // Fires
            if (Firemaking != null)
            {
                foreach (var fire in Firemaking.GetActiveFires())
                {
                    float sortY = GetDepthSort(fire.WorldX, fire.WorldY, 0);
                    drawables.Add((Depth: sortY, Draw: () => SpriteRenderer.RenderFire(fire, Camera!)));
                }
            }
        }

        // Sort by depth (Y coordinate) so entities "behind" are drawn first
        drawables.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        foreach (var (_, drawFn) in drawables)
        {
            drawFn();
        }

        // Particles
        ParticleSystem?.Draw(gl);

        // Seasonal ambient overlay
        SeasonalRenderer?.DrawAmbientOverlay(gl, 20);

        // Combat damage numbers
        if (CombatSystem != null && Camera != null)
        {
            CombatUI.RenderDamageNumbers(gl, CombatSystem.DamageNumbers, Camera);
            if (CombatSystem.DamageNumbers.Count > 0)
            {
                var latest = CombatSystem.DamageNumbers[^1];
                if (latest.Value < 0 && HUD != null)
                    HUD.TriggerDamageFlash();
            }
        }

        // UI Panels
        RenderPanels(gl, screenWidth, screenHeight);

        // HUD (drawn last so overlays are never occluded)
        HUD?.Render(gl, screenWidth, screenHeight);
    }

    private float GetDepthSort(float worldX, float worldY, float elevation)
    {
        if (Camera == null) return worldY;
        float cy = MathF.Cos(Camera.Yaw);
        float sy = MathF.Sin(Camera.Yaw);
        return worldY * cy + worldX * sy + elevation * 0.5f;
    }

    private void RenderBuildGhost(Silk.NET.OpenGL.GL gl)
    {
        // TODO: Implement build ghost rendering
    }

    private void RenderPanels(Silk.NET.OpenGL.GL gl, int screenWidth, int screenHeight)
    {
        switch (State)
        {
            case GameState.InventoryOpen:
                InventoryPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.DashboardOpen:
                Dashboard?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.SkillPanel:
                SkillPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.CraftingPanel:
                CraftingPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.BuildingPanel:
                BuildingPanel?.Render(gl, screenWidth, screenHeight, BuildingSystem, SkillManager);
                break;
            case GameState.GearPanel:
                if (Player?.Gear != null && Inventory != null)
                {
                    var gearDefs = GearItem.LoadAll();
                    GearPanel?.Render(gl, screenWidth, screenHeight, Player.Gear, Inventory, gearDefs);
                }
                break;
            case GameState.TradePanel:
                TradePanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.QuestPanel:
                QuestPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.RecruitPanel:
                RecruitPanel?.Render(gl, screenWidth, screenHeight);
                break;
            case GameState.DiplomacyPanel:
                DiplomacyPanel?.Render(gl, screenWidth, screenHeight);
                break;
        }
    }

    /// <summary>
    /// Handle input event (SDL event - for future use with raw SDL input).
    /// Currently input is handled via InputManager's high-level Silk.NET API.
    /// </summary>
    public void HandleEvent(Event evt)
    {
        switch (State)
        {
            case GameState.Title:
                TitleScreen?.HandleEvent(this, evt);
                break;
            case GameState.CharacterSelect:
                CharacterSelectPanel?.HandleEvent(this, evt);
                break;
            case GameState.Loading:
            case GameState.LoadingSave:
            case GameState.Error:
                LoadingScreen?.HandleEvent(this, evt);
                break;
            default:
                // InputRouter.Handle expects Silk.NET Key, not SDL Event.
                // Input is currently routed through InputManager -> InputState -> Player/Camera.
                break;
        }
    }

    // Internal methods for bootstrap
    internal void SetWorldGenThread(System.Threading.Thread thread) => _worldGenThread = thread;
    internal void SetWorldGenResult(string result) => _worldGenResult = result;
    internal void SetWorldGenError(string error) => _worldGenError = error;
    internal string? GetWorldGenError() => _worldGenError;
    internal float GetLoadingProgress() => LoadingProgress;
    internal void SetLoadingProgress(float progress) => LoadingProgress = progress;
    internal void SetFlavorText(string text) => _flavorText = text;
    internal string GetFlavorText() => _flavorText;
}