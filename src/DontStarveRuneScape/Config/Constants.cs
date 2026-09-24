namespace DontStarveRuneScape.Config;

/// <summary>
/// Global constants and tuning parameters.
/// All magic numbers are defined here. No subsystem should hardcode
/// values that appear in this table; instead, import from Constants.
/// </summary>
public static class Constants
{
    // ─── Window & Rendering ───────────────────────────────────────────────

    public const int WindowWidth = 1280;
    public const int WindowHeight = 720;
    public const string WindowTitle = "Don't Starve RuneScape";
    public const int TargetFps = 60;

    // ─── Tile & Terrain ───────────────────────────────────────────────────

    public const int TileSize = 64;                 // Pixel size of one tile
    public const int TileSubdivisions = 2;          // Half-tile rendering (2×2 sub-tiles)
    public const int MapWidth = 512;                // Tiles wide
    public const int MapHeight = 512;               // Tiles tall
    public const int ElevationLevels = 32;          // 0–31 elevation levels
    public const int ZScale = 8;                    // Pixels of vertical displacement per elevation unit
    public const float TerrainHeightScale = 1.6f;   // Render-only multiplier on ZScale

    // ─── Proc-Gen Noise Configuration ─────────────────────────────────────

    public const float NoiseScale = 0.015f;              // Controls "zoom" of noise features
    public const int ElevationOctaves = 5;               // Number of noise layers for detail
    public const int MoistureOctaves = 3;
    public const float MoistureScale = 0.008f;           // Independent scale for moisture
    public const float MoistureRangeMultiplier = 2.5f;   // Scale factor to expand moisture noise range

    // Ridgeline noise parameters (for sharp ridges/valleys)
    public const float RidgeNoiseScale = 0.023f;         // Higher frequency for ridges
    public const int RidgeOctaves = 4;                   // More octaves for sharp detail
    public const float RidgeLacunarity = 2.1f;           // Non-integer lacunarity to break periodicity
    public const float RidgePersistence = 0.5f;

    // Domain warping parameters (for geological distortion)
    public const float DomainWarpScale = 0.008f;
    public const int DomainWarpOctaves = 3;
    public const float DomainWarpAmplitude = 4.0f;      // Max warp distance in tiles

    // Biome-specific noise overrides (can be overridden per biome)
    public static readonly Dictionary<string, BiomeNoiseParams> BiomeNoiseParams = new()
    {
        ["mountains"] = new BiomeNoiseParams
        {
            Scale = 0.018f,
            Octaves = 5,
            Lacunarity = 2.1f,
            Persistence = 0.5f,
            RidgeWeight = 0.4f,
            DomainWarp = 15.0f,
        },
        ["plains"] = new BiomeNoiseParams
        {
            Scale = 0.01f,
            Octaves = 3,
            Lacunarity = 2.1f,
            Persistence = 0.6f,
            RidgeWeight = 0.05f,
            DomainWarp = 5.0f,
        },
        ["forest"] = new BiomeNoiseParams
        {
            Scale = 0.012f,
            Octaves = 4,
            Lacunarity = 2.1f,
            Persistence = 0.5f,
            RidgeWeight = 0.15f,
            DomainWarp = 10.0f,
        },
        ["swamp"] = new BiomeNoiseParams
        {
            Scale = 0.006f,
            Octaves = 2,
            Lacunarity = 2.1f,
            Persistence = 0.7f,
            RidgeWeight = 0.0f,
            DomainWarp = 3.0f,
        },
        ["desert"] = new BiomeNoiseParams
        {
            Scale = 0.014f,
            Octaves = 4,
            Lacunarity = 2.1f,
            Persistence = 0.4f,
            RidgeWeight = 0.2f,
            DomainWarp = 8.0f,
        },
        ["coastal"] = new BiomeNoiseParams
        {
            Scale = 0.01f,
            Octaves = 3,
            Lacunarity = 2.1f,
            Persistence = 0.6f,
            RidgeWeight = 0.05f,
            DomainWarp = 5.0f,
        },
    };

    // Erosion simulation parameters
    public const int ErosionIterations = 5;
    public const float ErosionRainAmount = 0.01f;
    public const float ErosionSolubility = 0.05f;
    public const float ErosionEvaporation = 0.01f;
    public const float ErosionGravity = 4.0f;
    public const float ErosionSedimentCapacityFactor = 0.1f;

    // Cliff detection threshold
    public const float CliffThreshold = 2.0f;         // Elevation delta > this = cliff

    // Water body generation
    public const float RiverThreshold = 10.0f;        // Flow accumulation threshold for rivers
    public const int LakeMinSize = 5;                 // Minimum tiles for a lake

    /// <summary>World sea level: any tile at or below this elevation is water.</summary>
    /// <summary>
    /// The permanent sea level: a flat world-water plane at this elevation.
    /// Set from the elevation histogram (seed 42) so ~20-25% of the world is
    /// reliably underwater — anything at or below this height IS the sea.
    /// </summary>
    public const float SeaLevel = 11.5f;

    // ─── Survival ─────────────────────────────────────────────────────────

    public const float HungerDrainInterval = 30.0f;   // Seconds per hunger point drain
    public const float HungerDrainRate = 1.0f;        // Hunger points per interval
    public const float StarvationHpDrainRate = 0.5f;  // HP drain per interval when hunger = 0
    public const float HpBaseMax = 20.0f;             // Starting max HP
    public const float MaxStaminaBase = 30.0f;        // Max stamina for Woodcutting/Mining actions

    // ─── Player ───────────────────────────────────────────────────────────

    public const float PlayerMovementSpeed = 150.0f;  // Pixels per second

    // ─── XP & Progression ────────────────────────────────────────────────

    public const float XpScaleFactor = 4.0f;          // Denominator in OSRS XP formula
    public const int StatPointsPerLevel = 3;          // Stat points granted per skill level
    public const int WildcardInterval = 5;            // Every N total skill levels for wildcard point

    // ─── Camera ───────────────────────────────────────────────────────────

    public const float CameraZoomMin = 1.0f;         // No far tier during normal play
    public const float CameraZoomMax = 2.5f;
    public const float CameraZoomDefault = 1.8f;       // Closer, more intimate aerial view
    public const float CameraOrbitSpeed = 90.0f;      // Degrees per second for yaw
    public const float CameraTiltSpeed = 60.0f;       // Degrees per second for pitch
    public const float CameraPitchMin = 10.0f;        // Near-horizon view
    public const float CameraPitchMax = 80.0f;        // Near top-down view

    // LOD tier zoom thresholds (hysteresis of ±5% applied at runtime)
    public const float LodNearZoom = 1.2f;            // zoom >= this: full detail
    public const float LodFarZoom = 0.75f;            // zoom < this: cheapest terrain, dots/culling

    // ─── Terrain Shading ──────────────────────────────────────────────────

    public const float ShadingStrength = 0.20f;       // ±brightness on slopes
    public static readonly (int X, int Y) LightDirection = (-1, -1);  // North-west light

    // ─── Fog & LOD ─────────────────────────────────────────────────────────

    public const float FogNearDistance = 500.0f;      // Pixels from player where fog begins to fade
    public const float FogFarDistance = 1400.0f;      // Pixels from player where fog reaches maximum opacity
    public const int FogMaxAlpha = 215;               // Fog caps below 255 so far terrain stays faintly readable
    public const float FogCullDistance = 1700.0f;     // Pixels beyond which sprites are not rendered
    public static readonly (byte R, byte G, byte B) FogColor = (135, 206, 235);  // Matches sky colour

    /// <summary>
    /// Return fog alpha (0..FogMaxAlpha) for a world-space distance.
    /// Smoothstep ramp so fog eases in gently near the player.
    /// Returns -1 beyond the cull distance so callers can skip drawing entirely.
    /// </summary>
    public static int FogAlphaForDistance(float dist)
    {
        if (dist > FogCullDistance) return -1;
        if (dist <= FogNearDistance) return 0;
        if (dist >= FogFarDistance) return FogMaxAlpha;
        float t = (dist - FogNearDistance) / (FogFarDistance - FogNearDistance);
        t = t * t * (3.0f - 2.0f * t);  // smoothstep
        return (int)(FogMaxAlpha * t);
    }

    // ─── Phase 6: Advanced Shading & Lighting ────────────────────────────

    // Time-of-day
    public const float DayDurationSeconds = 1200.0f;        // Full day/night cycle (20 min)
    public const float SunriseHour = 6.0f;                  // Time-of-day 0.25
    public const float SunsetHour = 18.0f;                  // Time-of-day 0.75
    public const float SunAltitudeMin = -30.0f;             // Degrees at midnight
    public const float SunAltitudeMax = 30.0f;              // Degrees at noon
    public const float SunAzimuth = 45.0f;                  // Degrees from north. Matches NW light.
    public const float NightAmbientLevel = 0.15f;           // Minimum ambient at night (0–1)
    public const float DayAmbientLevel = 0.45f;             // Maximum ambient at noon (0–1)

    // Ambient occlusion
    public const float AoStrength = 0.35f;                  // Max AO darkening (0–1)
    public const int AoBlurRadius = 1;                      // Tiles to blur AO across (0=sharp)

    // Rim lighting / fresnel
    public const float RimStrength = 0.25f;                 // Max rim brightening (0–1)
    public const float RimExponent = 3.0f;                  // Fresnel sharpness

    // Height fog / volumetric
    public const float FogScaleHeight = 8.0f;               // Elevation scale for density falloff
    public static readonly List<(float Height, (byte R, byte G, byte B) Color, float DensityMult)> FogHeightPlanes = [];

    // Weather lighting
    public const float CloudShadowStrength = 0.20f;         // Max cloud shadow darkening
    public const int LightningFlashDurationMs = 50;         // Screen flash duration
    public const int LightningFlashAlpha = 180;             // 0–255

    // Seasonal lighting profiles (per-biome ambient/sun/fog tint)
    public static readonly Dictionary<string, Dictionary<string, (byte R, byte G, byte B)>> SeasonLighting = new()
    {
        ["spring"] = new() { ["ambient"] = (230, 240, 220), ["sun_color"] = (255, 240, 200), ["fog_tint"] = (180, 210, 190) },
        ["summer"] = new() { ["ambient"] = (245, 235, 210), ["sun_color"] = (255, 245, 220), ["fog_tint"] = (200, 190, 160) },
        ["autumn"] = new() { ["ambient"] = (235, 220, 190), ["sun_color"] = (255, 200, 140), ["fog_tint"] = (180, 160, 130) },
        ["winter"] = new() { ["ambient"] = (200, 220, 240), ["sun_color"] = (220, 235, 255), ["fog_tint"] = (160, 180, 200) },
    };

    // Shading presets
    public static readonly Dictionary<string, ShadingPreset> ShadingPresets = new()
    {
        ["stylized"] = new ShadingPreset
        {
            SlopeShading = true, Ao = false, Rim = false, MultiLight = false,
            VolumetricFog = false, HeightFog = false, WeatherLighting = false, Lightning = false,
            ParticleCap = 500, FogCullDistance = 1700, AmbientOverlayAlpha = 20,
            SkyMode = "solid", TerrainStyle = "textured", TerrainSubdiv = 2,
            TerrainLod = true, TerrainWarp = true, BiomeBlend = true,
        },
        ["realistic"] = new ShadingPreset
        {
            SlopeShading = true, Ao = true, Rim = true, MultiLight = true,
            VolumetricFog = true, HeightFog = true, WeatherLighting = true, Lightning = true,
            ParticleCap = 500, FogCullDistance = 1700, AmbientOverlayAlpha = 40,
            SkyMode = "gradient", TerrainStyle = "flat", TerrainSubdiv = 4,
            TerrainLod = true, TerrainWarp = true, BiomeBlend = true,
        },
        ["performance"] = new ShadingPreset
        {
            SlopeShading = false, Ao = false, Rim = false, MultiLight = false,
            VolumetricFog = false, HeightFog = false, WeatherLighting = false, Lightning = false,
            ParticleCap = 150, FogCullDistance = 1000, AmbientOverlayAlpha = 0,
            SkyMode = "solid", TerrainStyle = "textured", TerrainSubdiv = 2,
            TerrainLod = true, TerrainWarp = false, BiomeBlend = false,
        },
    };
    public const string DefaultShadingPreset = "stylized";

    // ─── State Machine ────────────────────────────────────────────────────

    public const float LoadingScreenDuration = 3.0f;  // Minimum seconds for loading screen
    public const int FlavorTextCount = 10;            // Number of loading screen messages

    public static readonly string[] FlavorTexts =
    [
        "Assembling campfire from existential dread...",
        "Teaching wolves to share resources...",
        "Polishing pickaxe with hope and determination...",
        "Convincing trees to donate logs...",
        "Balancing hunger against hope...",
        "Crafting planks from pure optimism...",
        "Mining stones for future prosperity...",
        "Cooking fish over a dream of warmth...",
        "Gathering herbs to heal existential wounds...",
        "Exploring biomes of endless possibility...",
    ];

    // ─── Combat ──────────────────────────────────────────────────────────

    public const float PlayerHpPerCombatLevel = 1.0f;   // HP gain per Combat level (stub)
    public const float CombatBaseAttackCooldown = 1.5f; // Base attack cooldown in seconds (unarmed)
    public const float CombatSpeedStatCooldownReduction = 0.3f; // Cooldown reduction per speed stat point
    public const float CombatDamageNumberRiseRate = 30.0f; // Damage number rise rate (px/s)
    public const float DeathClearMonstersRadius = 0.0f; // Radius in tiles to clear monsters on death (0 = all)

    // ─── Firemaking ──────────────────────────────────────────────────────

    public const int FireInteractionRadiusTiles = 2;  // Tiles radius for fire interaction check
    public const int CampfireSearchRadiusTiles = 3;   // Tiles radius for campfire search

    // ─── Monster Spawning ────────────────────────────────────────────────

    public const int InitialMonsterSpawnRadiusTiles = 20; // Spawn radius in tiles

    // ─── Save System ──────────────────────────────────────────────────────

    public const int SaveSlotCount = 10;                // Number of save slots
    public const float AutosaveInterval = 300.0f;       // Seconds between autosaves (5 min)
    public const float DeathRestoreHpFraction = 0.5f;   // HP fraction restored on soft death
    public const float DeathRestoreHungerFraction = 0.5f; // Hunger fraction restored on soft death
    public const float DeathXpPenaltyFraction = 0.01f;  // XP lost on soft death (1%)

    // ─── Data File Paths ──────────────────────────────────────────────────

    public const string DataDir = "Data";
    public const string BiomesFile = "Data/biomes.json";
    public const string ResourcesFile = "Data/resources.json";
    public const string ItemsFile = "Data/items.json";

    // ─── Seasons & Weather ──────────────────────────────────────────────────

    public const float SeasonDurationSeconds = 600.0f;      // 10 minutes per season
    public const float SeasonTransitionSeconds = 30.0f;     // Visual/logical transition window
    public static readonly string[] SeasonOrder = ["spring", "summer", "autumn", "winter"];
    public static readonly Dictionary<string, string> SeasonTransitions = new()
    {
        ["spring"] = "summer",
        ["summer"] = "autumn",
        ["autumn"] = "winter",
        ["winter"] = "spring",
    };

    // Weather weights by season → (clear, rain, snow, storm, fog)
    public static readonly Dictionary<string, (float Clear, float Rain, float Snow, float Storm, float Fog)> WeatherWeights = new()
    {
        ["spring"] = (40.0f, 35.0f, 0.0f, 10.0f, 15.0f),
        ["summer"] = (50.0f, 30.0f, 0.0f, 15.0f, 5.0f),
        ["autumn"] = (45.0f, 30.0f, 0.0f, 15.0f, 10.0f),
        ["winter"] = (40.0f, 15.0f, 30.0f, 10.0f, 5.0f),
    };
    public const float WeatherChangeInterval = 120.0f;     // Seconds between weather rolls

    // ─── Proc-Gen Rarity System ────────────────────────────────────────────

    private const float GlobalDensityScale = 0.35f;

    public static readonly Dictionary<string, (float Min, float Max)> RarityDensityRange = new()
    {
        ["ubiquitous"] = (0.15f * GlobalDensityScale, 0.30f * GlobalDensityScale),   // water, grass
        ["common"]     = (0.08f * GlobalDensityScale, 0.15f * GlobalDensityScale),   // oak, iron, fiber, berry, birch, maple, pine, reed
        ["uncommon"]   = (0.03f * GlobalDensityScale, 0.08f * GlobalDensityScale),   // copper, gold, gem, tin, spruce, willow, dead_tree
        ["rare"]       = (0.01f * GlobalDensityScale, 0.03f * GlobalDensityScale),   // void_crystal, obsidian, silk, elder_wood, moonstone
        ["epic"]       = (0.003f * GlobalDensityScale, 0.01f * GlobalDensityScale),  // mithril, ghost_iron, dragonbone, void_essence, phoenix
        ["legendary"]  = (0.001f * GlobalDensityScale, 0.003f * GlobalDensityScale), // star_metal, ancient_rune, celestial_crystal
    };
}

/// <summary>
/// Biome-specific noise parameters.
/// </summary>
public sealed class BiomeNoiseParams
{
    public float Scale { get; set; }
    public int Octaves { get; set; }
    public float Lacunarity { get; set; }
    public float Persistence { get; set; }
    public float RidgeWeight { get; set; }
    public float DomainWarp { get; set; }
}

/// <summary>
/// Shading preset configuration.
/// </summary>
public sealed class ShadingPreset
{
    public bool SlopeShading { get; set; }
    public bool Ao { get; set; }
    public bool Rim { get; set; }
    public bool MultiLight { get; set; }
    public bool VolumetricFog { get; set; }
    public bool HeightFog { get; set; }
    public bool WeatherLighting { get; set; }
    public bool Lightning { get; set; }
    public int ParticleCap { get; set; }
    public int FogCullDistance { get; set; }
    public int AmbientOverlayAlpha { get; set; }
    public string SkyMode { get; set; } = "solid";
    public string TerrainStyle { get; set; } = "textured";
    public int TerrainSubdiv { get; set; }
    public bool TerrainLod { get; set; }
    public bool TerrainWarp { get; set; }
    public bool BiomeBlend { get; set; }
}