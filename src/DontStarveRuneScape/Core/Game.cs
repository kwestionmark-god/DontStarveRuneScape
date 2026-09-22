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
    public ResourceRegistry? ResourceRegistry { get; set; }
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
    public PrimitiveBatch? PrimitiveBatch { get; set; }
    public TextRenderer? TextRenderer { get; set; }

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
        TitleScreen = new TitleScreen();
        LoadingScreen = new LoadingScreen();
        _bootstrap = new Bootstrap(this, null);
    }

    public void InitializeGraphics(Silk.NET.OpenGL.GL gl)
    {
        PrimitiveBatch?.Dispose();
        PrimitiveBatch = new PrimitiveBatch(gl);

        TileRenderer?.Dispose();
        TileRenderer = new TileRenderer(gl);

        SpriteRenderer?.Dispose();
        SpriteRenderer = new SpriteRenderer(gl);

        TextRenderer?.Dispose();
        TextRenderer = TextRenderer.Create(gl);

        gl.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        gl.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(Silk.NET.OpenGL.EnableCap.DepthTest);
    }

    public void DisposeGraphics()
    {
        TileRenderer?.Dispose();
        TileRenderer = null;
        SpriteRenderer?.Dispose();
        SpriteRenderer = null;
        PrimitiveBatch?.Dispose();
        PrimitiveBatch = null;
        TextRenderer?.Dispose();
        TextRenderer = null;
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
    /// <summary>
    /// When set (--smoketest &lt;path&gt;), auto-advances past the title/character
    /// screens, waits for worldgen, renders a few frames, saves a framebuffer PNG,
    /// and exits. Used for headless render verification.
    /// </summary>
    public string? SmokeTestPath { get; set; }
    private int _smokeFrames;

    /// <summary>
    /// When set (--smoketest-bench N), renders N frames after the world loads,
    /// then prints avg/p95 frame time to stdout and exits. Camera is fixed via
    /// DSR_CAM_* env vars; run with vsync off for meaningful numbers.
    /// </summary>
    public int SmokeBenchFrames { get; set; }
    private readonly List<float> _benchTimes = new();

    /// <summary>Rolling average frame time in ms (1-second window).</summary>
    public float FrameMs { get; private set; }
    /// <summary>Rolling FPS estimate.</summary>
    public float Fps { get; private set; }
    /// <summary>Debug HUD visibility (toggled with F3).</summary>
    public bool ShowDebugHud { get; private set; }
    private float _fpsAccum;
    private int _fpsFrames;

    public void Update(float dt)
    {
        if (SmokeTestPath != null)
        {
            if (State == GameState.Title) SetState(GameState.CharacterSelect);
            else if (State == GameState.CharacterSelect) SetState(GameState.Loading);
        }
        Dt = dt;
        PlayTime += dt;

        // Rolling frame-time stats (1s window)
        _fpsAccum += dt;
        _fpsFrames++;
        if (_fpsAccum >= 1.0f)
        {
            FrameMs = _fpsAccum / _fpsFrames * 1000f;
            Fps = _fpsFrames / _fpsAccum;
            _fpsAccum = 0f;
            _fpsFrames = 0;
        }

        var debugInput = InputManager?.InputState;
        if (debugInput?.ToggleDebugHud == true)
            ShowDebugHud = !ShowDebugHud;

        // Benchmark: accumulate frame times while playing, print and exit at N.
        if (SmokeBenchFrames > 0 && State == GameState.Playing)
        {
            _benchTimes.Add(dt * 1000f);
            if (_benchTimes.Count >= SmokeBenchFrames)
            {
                var sorted = _benchTimes.ToArray();
                Array.Sort(sorted);
                float avg = _benchTimes.Count > 0 ? _benchTimes.Average() : 0;
                float p95 = sorted[(int)(sorted.Length * 0.95)];
                float p50 = sorted[sorted.Length / 2];
                Console.WriteLine($"bench: frames={sorted.Length} avg={avg:F2}ms p50={p50:F2}ms p95={p95:F2}ms");
                Environment.Exit(0);
            }
        }

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

        if (State == GameState.Title)
        {
            var input = InputManager?.InputState;
            if (input != null && (input.Confirm || input.MouseLeftClick))
                SetState(GameState.Loading);
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
        if (PrimitiveBatch == null)
            InitializeGraphics(gl);

        Camera?.SetScreenSize(screenWidth, screenHeight);

        gl.Viewport(0, 0, (uint)Math.Max(1, screenWidth), (uint)Math.Max(1, screenHeight));
        gl.Enable(Silk.NET.OpenGL.EnableCap.Blend);
        gl.BlendFunc(Silk.NET.OpenGL.BlendingFactor.SrcAlpha, Silk.NET.OpenGL.BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(Silk.NET.OpenGL.EnableCap.DepthTest);

        var fog = Constants.FogColor;
        if (State == GameState.Title || State == GameState.CharacterSelect)
            gl.ClearColor(28 / 255f, 18 / 255f, 12 / 255f, 1f);
        else if (State == GameState.Error)
            gl.ClearColor(40 / 255f, 12 / 255f, 12 / 255f, 1f);
        else
            gl.ClearColor(fog.R / 255f, fog.G / 255f, fog.B / 255f, 1f);
        gl.Clear((uint)Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit);

        var batch = PrimitiveBatch;
        if (batch == null) return;

        batch.Begin(screenWidth, screenHeight);

        switch (State)
        {
            case GameState.Title:
                TitleScreen?.Render(batch, TextRenderer, screenWidth, screenHeight, PlayTime);
                break;
            case GameState.CharacterSelect:
                CharacterSelectPanel?.Render(batch, screenWidth, screenHeight);
                break;
            case GameState.Loading:
            case GameState.LoadingSave:
            case GameState.Error:
                LoadingScreen?.Render(batch, this, screenWidth, screenHeight);
                break;
            default:
                RenderGame(gl, batch, screenWidth, screenHeight);
                break;
        }

        batch.End();

        if (!string.IsNullOrEmpty(SmokeTestPath) && State == GameState.Playing && ++_smokeFrames >= 6)
        {
            CaptureFramebuffer(gl, screenWidth, screenHeight, SmokeTestPath);
            Environment.Exit(0);
        }
    }

    private static void CaptureFramebuffer(Silk.NET.OpenGL.GL gl, int width, int height, string path)
    {
        int bytes = width * height * 4;
        var pixels = new byte[bytes];
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                gl.ReadPixels(0, 0, (uint)width, (uint)height,
                    Silk.NET.OpenGL.PixelFormat.Rgba, Silk.NET.OpenGL.PixelType.UnsignedByte, ptr);
            }
        }

        // GL rows are bottom-up; flip so the top row is first.
        var flipped = new byte[bytes];
        int stride = width * 4;
        for (int y = 0; y < height; y++)
            Array.Copy(pixels, (height - 1 - y) * stride, flipped, y * stride, stride);

        var info = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888);
        // Write through a bitmap so the row flip is preserved.
        using var bitmap = new SkiaSharp.SKBitmap();
        System.Runtime.InteropServices.GCHandle handle =
            System.Runtime.InteropServices.GCHandle.Alloc(flipped, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject());
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var stream = System.IO.File.Create(path);
            data.SaveTo(stream);
        }
        finally
        {
            handle.Free();
        }
    }

    private void RenderGame(Silk.NET.OpenGL.GL gl, PrimitiveBatch batch, int screenWidth, int screenHeight)
    {
        // One shared painter's list for terrain AND sprites: identical depth
        // keys mean elevated terrain correctly occludes sprites behind it.
        var drawables = new List<(float Depth, int Seq, Action Draw)>();
        int seq = 0;

        if (TileRenderer != null && Camera != null && World != null)
            TileRenderer.Render(batch, Camera, World, drawables, ref seq, PlayTime);

        if (SpriteRenderer != null && World != null && Camera != null)
        {
            var (left, top, right, bottom) = Camera.GetViewRect();
            int xMin = Math.Max(0, (int)(left / Constants.TileSize));
            int xMax = Math.Min(World.Width, (int)(right / Constants.TileSize) + 1);
            int yMin = Math.Max(0, (int)(top / Constants.TileSize));
            int yMax = Math.Min(World.Height, (int)(bottom / Constants.TileSize) + 1);

            // Screen-space cull margin (world-space rects include tiles far
            // outside the frame, especially at shallow pitch).
            float cull = 96f * Camera.Zoom + 96f;

            for (int x = xMin; x < xMax; x++)
            {
                for (int y = yMin; y < yMax; y++)
                {
                    var tile = World.Tiles[x, y];
                    if (tile?.ResourceNode != null)
                    {
                        // Ground height at the tile center is the bilinear
                        // value of the corner heights, not tile.Elevation —
                        // on slopes those differ enough to float the sprite.
                        float elev = tile.GetElevationAt(0.5f, 0.5f);
                        float wx = (x + 0.5f) * Constants.TileSize;
                        float wy = (y + 0.5f) * Constants.TileSize;
                        var screen = Camera.WorldToScreen(wx, wy, elev);
                        if (screen.X < -cull || screen.X > screenWidth + cull ||
                            screen.Y < -cull || screen.Y > screenHeight + cull)
                            continue;
                        float sortY = GetDepthSort(wx, wy, elev);
                        var node = tile.ResourceNode;
                        int tx = x, ty = y;
                        var tTile = tile;
                        drawables.Add((sortY, seq++, () => SpriteRenderer.RenderResource(
                            node, batch, Camera, elev, tx, ty, tTile)));
                    }
                }
            }

            if (Player != null)
            {
                var (ptx, pty) = Player.GetTilePosition();
                var tile = World.GetTile(ptx, pty);
                // Bilinear ground height at the player's fractional position —
                // the same value the camera focus anchors to.
                float fx = Player.WorldX / Constants.TileSize - ptx;
                float fy = Player.WorldY / Constants.TileSize - pty;
                float elev = tile?.GetElevationAt(fx, fy) ?? 0f;
                // Wading: in water, sink to just above the surface plane.
                if (tile?.Biome?.Id == "water")
                    elev = Constants.SeaLevel - 0.8f;
                float sortY = GetDepthSort(Player.WorldX, Player.WorldY, elev);
                drawables.Add((sortY, seq++, () => SpriteRenderer.RenderPlayer(
                    Player, batch, Camera, elev, Dt)));
            }

            if (CombatSystem != null)
            {
                foreach (var monster in CombatSystem.Monsters)
                {
                    if (monster.IsAlive())
                    {
                        float sortY = GetDepthSort(monster.WorldX, monster.WorldY, 0);
                        var m = monster;
                        drawables.Add((sortY, seq++, () => SpriteRenderer.RenderMonster(m, batch, Camera)));
                    }
                }
            }

            if (NPCSystem != null && Player != null)
            {
                var nearbyNpc = NPCSystem.CheckProximity(Player);
                foreach (var npc in NPCSystem.NPCs)
                {
                    if (!npc.IsActive) continue;
                    float sortY = GetDepthSort(npc.WorldX, npc.WorldY, 0);
                    var n = npc;
                    drawables.Add((sortY, seq++, () => SpriteRenderer.RenderNPC(n, batch, Camera, 0)));

                    if (npc == nearbyNpc)
                        drawables.Add((sortY, seq++, () => SpriteRenderer.RenderProximityPrompt(n, batch, Camera)));
                }
            }

            if (BuildingSystem != null)
            {
                foreach (var structure in BuildingSystem.Structures)
                {
                    if (structure.IsActive)
                    {
                        float sortY = GetDepthSort(structure.WorldX, structure.WorldY, 0);
                        var s = structure;
                        drawables.Add((sortY, seq++, () => SpriteRenderer.RenderStructure(s, batch, Camera)));
                    }
                }
            }

            if (BuildMode && Camera != null && World != null && BuildCursor.HasValue)
                drawables.Add((float.MaxValue, seq++, () => RenderBuildGhost(gl)));

            if (Firemaking != null)
            {
                foreach (var fire in Firemaking.GetActiveFires())
                {
                    float sortY = GetDepthSort(fire.WorldX, fire.WorldY, 0);
                    var f = fire;
                    drawables.Add((sortY, seq++, () => SpriteRenderer.RenderFire(f, batch, Camera)));
                }
            }
        }

        drawables.Sort(static (a, b) =>
        {
            int c = a.Depth.CompareTo(b.Depth);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });
        foreach (var (_, _, drawFn) in drawables)
            drawFn();

        ParticleSystem?.Draw(gl);
        SeasonalRenderer?.DrawAmbientOverlay(gl, 20);

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

        RenderPanels(gl, screenWidth, screenHeight);
        HUD?.Render(batch, screenWidth, screenHeight, Survival);

        if (ShowDebugHud && TextRenderer != null)
        {
            TextRenderer.DrawText(batch,
                $"fps {Fps:F0}  frame {FrameMs:F1} ms",
                screenWidth - 110f, 24f, 14, 240, 240, 240);
        }
    }

    /// Depth key for painter's-order sorting of world sprites. All callers
    /// must pass world PIXEL positions and elevation in LEVELS — a sprite's
    /// depth is only meaningful relative to other sprites if the units match.
    private float GetDepthSort(float worldX, float worldY, float elevation)
    {
        if (Camera == null) return worldY;
        float cy = MathF.Cos(Camera.Yaw);
        float sy = MathF.Sin(Camera.Yaw);
        return worldY * cy + worldX * sy + elevation * Constants.ZScale * 0.5f;
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