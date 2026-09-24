namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Seasons;

/// <summary>
/// Procedural world generation.
/// Pipeline: noise → elevation/moisture → biome classification → tile grid → resource placement → spawn point.
/// </summary>
public static class WorldGen
{
    /// <summary>
    /// Generate a world from seed.
    /// </summary>
    public static TileMap Generate(
        int seed,
        BiomeRegistry biomeRegistry,
        ResourceRegistry? resourceRegistry,
        SeasonSystem? seasonSystem,
        Action<float>? progressCallback = null)
    {
        var random = new Random(seed);
        var map = new TileMap(Constants.MapWidth, Constants.MapHeight);
        map.BiomeRegistry = biomeRegistry;
        map.SeasonSystem = seasonSystem;

        // Step 1: Generate noise maps
        progressCallback?.Invoke(0.1f);
        var (elevationMap, moistureMap) = GenerateNoiseMaps(seed, progressCallback);

        // Step 2: Classify biomes
        progressCallback?.Invoke(0.4f);
        ClassifyBiomes(map, biomeRegistry, elevationMap, moistureMap);

        // Step 3: Water as a substance with a sea level. Anything below
        // sea level is water; inland dips become lakes. Rivers as terrain
        // channels below sea level come along for free.
        progressCallback?.Invoke(0.45f);
        ApplySeaLevel(map, biomeRegistry, elevationMap);

        // Step 4: Build corner elevations for 2.5D rendering
        progressCallback?.Invoke(0.5f);
        BuildCornerElevations(map);

        // Step 4: Place resources
        progressCallback?.Invoke(0.6f);
        ResourcePlacer.Place(map, biomeRegistry, resourceRegistry, seasonSystem, random);

        // Step 5: Find spawn point (safe biome, moderate elevation)
        progressCallback?.Invoke(0.9f);
        int spawnX = 0, spawnY = 0;
        FindSpawnPoint(map, biomeRegistry, out spawnX, out spawnY);
        map.SpawnX = spawnX;
        map.SpawnY = spawnY;

        progressCallback?.Invoke(1.0f);
        return map;
    }

    /// <summary>
    /// Generate elevation and moisture noise maps.
    /// </summary>
    private static (float[,] Elevation, float[,] Moisture) GenerateNoiseMaps(
        int seed,
        Action<float>? progressCallback)
    {
        int width = Constants.MapWidth;
        int height = Constants.MapHeight;
        var elevation = new float[width, height];
        var moisture = new float[width, height];

        // Domain warp offsets for variation
        float warpX = seed * 1000.0f;
        float warpY = seed * 2000.0f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Domain warping operates on raw tile coordinates, like the
                // Python version (xs/ys in tile units, scales applied at
                // sampling time).
                var (wx, wy) = Noise.DomainWarp(x, y, Constants.DomainWarpScale,
                    Constants.DomainWarpOctaves, Constants.DomainWarpAmplitude);

                // Elevation: base + ridge, mixed 0.85/0.15 with a -0.2 bias —
                // mirrors the Python generator. Ridge noise uses its own
                // (higher-frequency) sampling scale.
                float baseElev = Noise.PNoise2((wx + warpX) * Constants.NoiseScale,
                    (wy + warpY) * Constants.NoiseScale,
                    Constants.ElevationOctaves, 0.5f, 2.0f);
                float ridgeElev = Noise.RidgedNoise2((wx + warpX) * Constants.RidgeNoiseScale,
                    (wy + warpY) * Constants.RidgeNoiseScale,
                    Constants.RidgeOctaves, Constants.RidgePersistence, Constants.RidgeLacunarity);
                // -0.1 bias (Python uses -0.2 with a wider-spread noise lib;
                // our normalized octaves need the smaller bias to center the
                // distribution on the same ~15/31 median).
                float combined = baseElev * 0.85f + ridgeElev * 0.15f - 0.1f;
                float e = (combined + 1.0f) * 0.5f * Constants.ElevationLevels;
                elevation[x, y] = Math.Clamp(MathF.Floor(e), 0, Constants.ElevationLevels - 1);

                // Moisture: independent noise, +100 coordinate offset like the
                // Python version so it doesn't correlate with elevation.
                float m = Noise.PNoise2((x + 100) * Constants.MoistureScale + warpX,
                    (y + 100) * Constants.MoistureScale + warpY,
                    Constants.MoistureOctaves, 0.5f, 2.0f);
                // Multiplier 0.7 shrinks our octave sum to the Python-noise
                // library's spread; with the raw 2.5 multiplier ~24% of tiles
                // clamped to exactly 0/1.
                moisture[x, y] = Math.Clamp((m * Constants.MoistureRangeMultiplier * 0.7f + 1f) * 0.5f, 0f, 1f);
            }

            if (x % 64 == 0)
                progressCallback?.Invoke(0.1f + 0.3f * x / (float)width);
        }

        return (elevation, moisture);
    }

    /// <summary>
    /// Classify biomes based on elevation and moisture.
    /// </summary>
    private static void ClassifyBiomes(
        TileMap map,
        BiomeRegistry biomeRegistry,
        float[,] elevation,
        float[,] moisture)
    {
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var tile = map.Tiles[x, y];
                float elev = elevation[x, y];
                float moist = moisture[x, y];

                tile.Elevation = elev;
                tile.Moisture = moist;

                // Port of the Python classifier: six elevation bands with
                // moisture sub-bands. (Water currently comes only from the
                // shallowest lowlands; rivers/lakes are a separate port.)
                string biomeId;
                if (elev < 3.0f)
                    biomeId = "coastal"; // below sea level gets overridden to water next
                else if (elev >= 26f)
                    biomeId = "mountains";
                else if (elev >= 20f)
                    biomeId = moist < 0.15f ? "desert" : moist < 0.35f ? "plains" : "mountains";
                else if (elev >= 14f)
                    biomeId = moist < 0.15f ? "desert" : moist < 0.3f ? "plains"
                        : moist < 0.6f ? "forest" : moist < 0.8f ? "mountains" : "swamp";
                else if (elev >= 8f)
                    biomeId = moist < 0.2f ? "desert" : moist < 0.35f ? "plains"
                        : moist < 0.55f ? "forest" : "swamp";
                else if (elev >= 4f)
                    biomeId = moist < 0.2f ? "desert" : moist < 0.35f ? "plains"
                        : moist < 0.4f ? "forest" : "coastal";
                else
                    biomeId = moist < 0.25f ? "desert" : moist < 0.4f ? "plains" : "coastal";

                tile.Biome = biomeRegistry.GetBiome(biomeId)
                             ?? biomeRegistry.DefaultBiome
                             ?? biomeRegistry.GetBiome("forest")!;
            }
        }
    }

    /// <summary>
    /// Build corner elevations for 2.5D rendering (bilinear interpolation).
    /// </summary>
    /// <summary>
    /// Volumetric water: any tile at or below sea level becomes water. The
    /// noise naturally carves oceans, inlets and lakes; the render layer draws
    /// one continuous animated plane and terrain overcliffs it.
    /// </summary>
    private static void ApplySeaLevel(
        TileMap map, BiomeRegistry biomeRegistry, float[,] elevation)
    {
        // Water is no longer a tile classification: it is a physical layer at
        // the constant SeaLevel. Any tile at or below SeaLevel is underwater
        // (Tile.HasWater); the renderer draws the flat sea plane through them
        // and clips straddling land tiles against it. This pass only derives
        // the shore fields the renderer needs (distance/gradient + beaches).
        int width = map.Width, height = map.Height;

        // Shoreline ring: land tiles within two steps of the waterline and
        // close to sea level become coastal — beaches around every edge.
        var coastalBiome = biomeRegistry.GetBiome("coastal");
        if (coastalBiome != null)
        {
            var toCoastal = new List<(int X, int Y)>();
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var t = map.Tiles[x, y];
                    if (t.HasWater) continue;
                    // Coastal when near submerged terrain at sea-level reach.
                    bool touchesWater = false;
                    for (int dx = -2; dx <= 2 && !touchesWater; dx++)
                        for (int dy = -2; dy <= 2 && !touchesWater; dy++)
                        {
                            var w = map.GetTile(x + dx, y + dy);
                            if (w != null && w.HasWater && t.Elevation <= w.GetSurfaceElevation() + 2.5f)
                                touchesWater = true;
                        }
                    if (touchesWater) toCoastal.Add((x, y));
                }
            }
            foreach (var (x, y) in toCoastal)
                map.Tiles[x, y].Biome = coastalBiome;
        }

        // Shore-distance field: multi-source BFS over submerged tiles starting
        // from those bordering dry land. Rendered as the shallow→deep gradient.
        var distQ = new Queue<(int X, int Y)>();
        var distD = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (!map.Tiles[x, y].HasWater) continue;
                bool borderLand = false;
                for (int dx = -1; dx <= 1 && !borderLand; dx++)
                    for (int dy = -1; dy <= 1 && !borderLand; dy++)
                    {
                        var n = map.GetTile(x + dx, y + dy);
                        if (n == null || !n.HasWater)
                            borderLand = true;
                    }
                if (borderLand) { distQ.Enqueue((x, y)); distD[x, y] = 0; }
            }
        var distSeen = new bool[width, height];
        foreach (var s in distQ) distSeen[s.X, s.Y] = true;
        while (distQ.Count > 0)
        {
            var c = distQ.Dequeue();
            map.Tiles[c.X, c.Y].ShoreDistance = distD[c.X, c.Y];
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = c.X + dx, ny = c.Y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                if (distSeen[nx, ny] || !map.Tiles[nx, ny].HasWater) continue;
                distSeen[nx, ny] = true;
                distD[nx, ny] = distD[c.X, c.Y] + 1;
                distQ.Enqueue((nx, ny));
            }
        }

        // Land shore field: BFS outward from the waterline over land, capped
        // at ring 8 and blocked by banks rising more than a couple of levels
        // above the sea surface.
        const int LandShoreCap = 8;
        const float ShoreRiseCap = 2.0f;
        var lQ = new Queue<(int X, int Y)>();
        var landSeen = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var t = map.Tiles[x, y];
                if (!t.HasWater) continue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        var lt = map.Tiles[nx, ny];
                        if (landSeen[nx, ny] || lt.HasWater) continue;
                        float surf0 = t.GetSurfaceElevation();
                        if (lt.Elevation > surf0 + ShoreRiseCap) continue;
                        landSeen[nx, ny] = true;
                        lt.LandDistToWater = 1;
                        lt.ShoreSurface = surf0;
                        lQ.Enqueue((nx, ny));
                    }
            }
        while (lQ.Count > 0)
        {
            var c = lQ.Dequeue();
            int d = map.Tiles[c.X, c.Y].LandDistToWater;
            if (d >= LandShoreCap) continue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = c.X + dx, ny = c.Y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var lt = map.Tiles[nx, ny];
                    if (landSeen[nx, ny] || lt.HasWater) continue;
                    float surf = map.Tiles[c.X, c.Y].ShoreSurface;
                    if (lt.Elevation > surf + ShoreRiseCap) continue;
                    landSeen[nx, ny] = true;
                    lt.LandDistToWater = d + 1;
                    lt.ShoreSurface = surf;
                    lQ.Enqueue((nx, ny));
                }
        }
    }

    private static void BuildCornerElevations(TileMap map)
    {
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                var tile = map.Tiles[x, y];

                // Get 4 corners (with clamping at edges)
                float e00 = map.GetTile(x, y)?.Elevation ?? tile.Elevation;
                float e10 = map.GetTile(x + 1, y)?.Elevation ?? tile.Elevation;
                float e11 = map.GetTile(x + 1, y + 1)?.Elevation ?? tile.Elevation;
                float e01 = map.GetTile(x, y + 1)?.Elevation ?? tile.Elevation;

                // Float corner heights: smooth slopes mean the sea plane /
                // terrain intersection is a curving contour, never a terraced
                // straight line.
                tile.CornerElevations = [e00, e10, e11, e01];
            }
        }
    }

    /// <summary>
    /// Find a suitable spawn point.
    /// </summary>
    private static void FindSpawnPoint(TileMap map, BiomeRegistry biomeRegistry, out int spawnX, out int spawnY)
    {
        // Prefer starting safety biomes (forest, plains, coastal)
        var safeBiomes = new[] { "forest", "plains", "coastal" };

        // Search from center outward
        int cx = map.Width / 2;
        int cy = map.Height / 2;

        for (int radius = 0; radius < Math.Max(map.Width, map.Height); radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;

                    int x = cx + dx;
                    int y = cy + dy;

                    var tile = map.GetTile(x, y);
                    if (tile == null) continue;

                    // Check if biome is safe and elevation is reasonable.
                    // Forest spans 5–27; spawn in the lowland fringe.
                    if (safeBiomes.Contains(tile.Biome?.Id ?? "") &&
                        tile.Elevation >= 4.0f && tile.Elevation <= 7.0f &&
                        tile.ResourceNode == null) // Don't spawn on resource
                    {
                        spawnX = x;
                        spawnY = y;
                        return;
                    }
                }
            }
        }

        // Fallback: center of map
        spawnX = cx;
        spawnY = cy;
    }
}

/// <summary>
/// Resource placement on the generated world.
/// </summary>
public static class ResourcePlacer
{
    // Rarity density ranges from the Python version (RARITY_DENSITY_RANGE,
    // 0.35 global scale) with an additional world-feel dial: fewer, larger
    // landmarks instead of a dense scatter.
    private const float DensityScaleAtmospheric = 0.7f;

    private static readonly Dictionary<string, (float Min, float Max)> RarityDensityRange = new()
    {
        ["ubiquitous"] = (0.15f * 0.35f, 0.30f * 0.35f),
        ["common"] = (0.08f * 0.35f, 0.15f * 0.35f),
        ["uncommon"] = (0.03f * 0.35f, 0.08f * 0.35f),
        ["rare"] = (0.01f * 0.35f, 0.03f * 0.35f),
        ["epic"] = (0.003f * 0.35f, 0.01f * 0.35f),
        ["legendary"] = (0.001f * 0.35f, 0.003f * 0.35f),
    };

    // Never let resources saturate more than this fraction of the map.
    private const float MaxTileOccupancy = 0.40f;

    /// <summary>
    /// Place resources on the map, one pass over tiles (matches the Python
    /// placer's per-tile probability model).
    /// </summary>
    public static void Place(
        TileMap map,
        BiomeRegistry biomeRegistry,
        ResourceRegistry? resourceRegistry,
        SeasonSystem? seasonSystem,
        Random random)
    {
        int occupancyLimit = (int)(map.Width * map.Height * MaxTileOccupancy);
        int occupied = 0;

        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                if (occupied >= occupancyLimit) return;

                var tile = map.Tiles[x, y];
                if (tile.ResourceNode != null) continue;
                if (tile.Structure != null) continue;
                if (tile.Biome == null) continue;

                // Never root a resource at or below the sea plane — the tile
                // would sit submerged under the water layer.
                if (tile.Elevation < Constants.SeaLevel) continue;
                bool submerged = false;
                for (int dx = -1; dx <= 1 && !submerged; dx++)
                    for (int dy = -1; dy <= 1 && !submerged; dy++)
                    {
                        var n = map.GetTile(x + dx, y + dy);
                        if (n != null && n.HasWater && n.GetSurfaceElevation() > tile.Elevation + 0.05f)
                            submerged = true;
                    }
                if (submerged) continue;

                foreach (var resourceId in tile.Biome.ResourceSpawns)
                {
                    ResourceDef? def = resourceRegistry?.GetResource(resourceId);
                    if (def == null) continue;

                    // Effective density: base_density clamped to its rarity band.
                    float density = def.Density;
                    if (RarityDensityRange.TryGetValue(def.Rarity, out var band))
                        density = Math.Clamp(density, band.Min, band.Max);
                    density *= DensityScaleAtmospheric;

                    // Generative size: range driven by the resource's own
                    // size_variance — trees swing wide, pebbles stay uniform.
                    // Bigger nodes consume more visual space, so their density
                    // is divided by area (scale^2) — one big tree instead of
                    // three same-sized small ones.
                    float v = def.SizeVariance;
                    float scale = (1f - 0.40f * v) + MathF.Pow(random.NextSingle(), 2.2f) * (2.8f * v);
                    float scaledDensity = density / (scale * scale);

                    if (random.NextSingle() < scaledDensity)
                    {
                        tile.ResourceNode = new ResourceNode(resourceId, def, 1.0f)
                        {
                            GrowthStage = 2, // Start mature
                            SizeScale = scale,
                        };
                        occupied++;
                        break; // First successful placement wins; tile occupied.
                    }
                }
            }
        }
    }
}