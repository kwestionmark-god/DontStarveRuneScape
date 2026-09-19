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

        // Step 3: Build corner elevations for 2.5D rendering
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
                float nx = x * Constants.NoiseScale;
                float ny = y * Constants.NoiseScale;

                // Domain warping
                var (wx, wy) = Noise.DomainWarp(nx, ny, Constants.DomainWarpScale,
                    Constants.DomainWarpOctaves, Constants.DomainWarpAmplitude);

                // Elevation: base noise + ridges
                float baseElev = Noise.PNoise2(wx + warpX, wy + warpY,
                    Constants.ElevationOctaves, 0.5f, 2.0f);
                float ridgeElev = Noise.RidgedNoise2(wx + warpX, wy + warpY,
                    Constants.RidgeOctaves, Constants.RidgePersistence, Constants.RidgeLacunarity);
                elevation[x, y] = Math.Clamp(baseElev * 0.7f + ridgeElev * 0.3f, -1f, 1f);

                // Moisture: independent noise
                float m = Noise.PNoise2((wx + warpX) * Constants.MoistureScale, (wy + warpY) * Constants.MoistureScale,
                    Constants.MoistureOctaves, 0.5f, 2.0f);
                moisture[x, y] = Math.Clamp(m * Constants.MoistureRangeMultiplier, -1f, 1f);
            }

            if (x % 64 == 0)
                progressCallback?.Invoke(0.1f + 0.3f * x / (float)width);
        }

        // Normalize to 0-1 range
        NormalizeMap(elevation);
        NormalizeMap(moisture);

        // Apply a sigmoid curve so lowlands cluster and peaks stay rare.
        // Real terrain is not uniform: most land sits at moderate-low elevation,
        // with mountains concentrated at the extremes.
        const float SigmoidK = 7.0f; // Steeper = more clustered lowlands
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float n = elevation[x, y]; // 0..1 uniform
                float s = 1.0f / (1.0f + MathF.Exp(-SigmoidK * (n - 0.5f))); // 0..1 sigmoid
                elevation[x, y] = s * (Constants.ElevationLevels - 1); // 0..31
            }
        }

        return (elevation, moisture);
    }

    /// <summary>
    /// Normalize a map to 0-1 range.
    /// </summary>
    private static void NormalizeMap(float[,] map)
    {
        int width = map.GetLength(0);
        int height = map.GetLength(1);
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float v = map[x, y];
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }

        float range = max - min;
        if (range > 0)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    map[x, y] = (map[x, y] - min) / range;
                }
            }
        }
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

                // Classify biome based on elevation/moisture thresholds.
                // Elevation is in 0..ElevationLevels-1 (0..31). After normalization
                // the distribution is roughly uniform, so thresholds are spaced to
                // give a realistic spread: lots of lowland forest/plains, a narrow
                // coastal/swamp band, scattered deserts, and mountains only at the
                // highest peaks.
                // Water: elevation < 3
                if (elev < 3.0f)
                {
                    tile.Biome = biomeRegistry.GetBiome("water");
                }
                // Mountains: only the highest peaks (elevation > 27)
                else if (elev > 27.0f)
                {
                    tile.Biome = biomeRegistry.GetBiome("mountains");
                }
                // Desert: mid-high elevation, low moisture
                else if (elev > 10.0f && elev < 18.0f && moist < 0.3f)
                {
                    tile.Biome = biomeRegistry.GetBiome("desert");
                }
                // Swamp: low elevation, high moisture
                else if (elev < 5.0f && moist > 0.7f)
                {
                    tile.Biome = biomeRegistry.GetBiome("swamp");
                }
                // Coastal: low elevation, high moisture
                else if (elev < 5.0f && moist > 0.5f)
                {
                    tile.Biome = biomeRegistry.GetBiome("coastal");
                }
                // Plains: low-mid elevation, medium moisture
                else if (elev >= 5.0f && elev < 9.0f && moist > 0.3f && moist < 0.7f)
                {
                    tile.Biome = biomeRegistry.GetBiome("plains");
                }
                // Forest: broad mid-elevation band (5–27)
                else
                {
                    tile.Biome = biomeRegistry.GetBiome("forest");
                }

                tile.Biome ??= biomeRegistry.DefaultBiome ?? biomeRegistry.GetBiome("forest")!;
            }
        }
    }

    /// <summary>
    /// Build corner elevations for 2.5D rendering (bilinear interpolation).
    /// </summary>
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

                tile.CornerElevations = [(int)e00, (int)e10, (int)e11, (int)e01];
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
    /// <summary>
    /// Place resources on the map.
    /// </summary>
    public static void Place(
        TileMap map,
        BiomeRegistry biomeRegistry,
        ResourceRegistry? resourceRegistry,
        SeasonSystem? seasonSystem,
        Random random)
    {
        string currentSeason = seasonSystem?.CurrentSeason ?? "spring";

        foreach (var biomeId in biomeRegistry.Biomes.Keys)
        {
            var biome = biomeRegistry.GetBiome(biomeId);
            if (biome == null) continue;

            foreach (var resourceId in biome.ResourceSpawns)
            {
                PlaceResource(map, resourceId, biome, resourceRegistry, currentSeason, random);
            }
        }
    }

    private static void PlaceResource(
        TileMap map,
        string resourceId,
        BiomeDef biome,
        ResourceRegistry? resourceRegistry,
        string currentSeason,
        Random random)
    {
        // Look up the resource definition so we know density and sprite.
        ResourceDef? def = resourceRegistry?.GetResource(resourceId);
        float density = def?.Density ?? 0.1f;

        // Calculate number of placements based on biome area.
        int biomeTileCount = 0;
        for (int x = 0; x < map.Width; x++)
        {
            for (int y = 0; y < map.Height; y++)
            {
                if (map.Tiles[x, y].Biome?.Id == biome.Id)
                    biomeTileCount++;
            }
        }

        int targetCount = (int)(biomeTileCount * density);
        int placed = 0;
        int attempts = 0;
        int maxAttempts = Math.Max(targetCount * 10, 50);

        while (placed < targetCount && attempts < maxAttempts)
        {
            int x = random.Next(map.Width);
            int y = random.Next(map.Height);
            attempts++;

            var tile = map.Tiles[x, y];
            if (tile.Biome?.Id != biome.Id) continue;
            if (tile.ResourceNode != null) continue;
            if (tile.Structure != null) continue;

            // Check elevation suitability
            if (biome.ElevationRange.Length >= 2)
            {
                if (tile.Elevation < biome.ElevationRange[0] || tile.Elevation > biome.ElevationRange[1])
                    continue;
            }

            tile.ResourceNode = new ResourceNode(resourceId, def, 1.0f)
            {
                GrowthStage = 2, // Start mature
            };

            placed++;
        }
    }
}