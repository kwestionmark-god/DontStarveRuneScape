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
        ResourcePlacer.Place(map, biomeRegistry, seasonSystem, random);

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

        // Scale elevation to 0-ElevationLevels
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                elevation[x, y] = elevation[x, y] * (Constants.ElevationLevels - 1);
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

                // Classify biome based on elevation/moisture thresholds
                // Water: elevation < 1
                if (elev < 1.0f)
                {
                    tile.Biome = biomeRegistry.GetBiome("water");
                }
                // Mountains: elevation > 4
                else if (elev > 4.0f)
                {
                    tile.Biome = biomeRegistry.GetBiome("mountains");
                }
                // Swamp: low elevation, high moisture
                else if (elev < 2.0f && moist > 0.7f)
                {
                    tile.Biome = biomeRegistry.GetBiome("swamp");
                }
                // Desert: medium elevation, low moisture
                else if (elev > 2.0f && elev < 4.0f && moist < 0.3f)
                {
                    tile.Biome = biomeRegistry.GetBiome("desert");
                }
                // Coastal: low elevation, near water
                else if (elev < 2.0f && moist > 0.5f)
                {
                    tile.Biome = biomeRegistry.GetBiome("coastal");
                }
                // Plains: medium elevation, medium moisture
                else if (elev > 1.5f && elev < 3.5f && moist > 0.3f && moist < 0.7f)
                {
                    tile.Biome = biomeRegistry.GetBiome("plains");
                }
                // Forest: everything else
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

                    // Check if biome is safe and elevation is reasonable
                    if (safeBiomes.Contains(tile.Biome?.Id ?? "") &&
                        tile.Elevation >= 1.0f && tile.Elevation <= 3.0f &&
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
        SeasonSystem? seasonSystem,
        Random random)
    {
        string currentSeason = seasonSystem?.CurrentSeason ?? "spring";

        foreach (var biomeId in biomeRegistry.Biomes.Keys)
        {
            var biome = biomeRegistry.GetBiome(biomeId);
            if (biome == null) continue;

            // Get resources for this biome
            var resources = biomeRegistry.Biomes.Values
                .SelectMany(b => b.ResourceSpawns)
                .Distinct()
                .Select(id => biomeRegistry.GetBiome(biomeId)?.ResourceSpawns.Contains(id) == true ? id : null)
                .Where(id => id != null)
                .Select(id => biomeRegistry.Biomes.Values.FirstOrDefault(b => b.ResourceSpawns.Contains(id!))?.Id)
                .Where(id => id != null)
                .Cast<string>();

            // Actually get resource defs from registry (would need ResourceRegistry)
            // For now, place based on biome's resource_spawns list
            foreach (var resourceId in biome.ResourceSpawns)
            {
                PlaceResource(map, resourceId, biome, currentSeason, random);
            }
        }
    }

    private static void PlaceResource(
        TileMap map,
        string resourceId,
        BiomeDef biome,
        string currentSeason,
        Random random)
    {
        // Find resource definition (would come from ResourceRegistry)
        // For now, use density from config
        float density = 0.1f; // Default

        // Calculate number of placements based on biome area
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
        int maxAttempts = targetCount * 10;

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

            // Create resource node (would use ResourceRegistry in full implementation)
            tile.ResourceNode = new ResourceNode
            {
                ResourceId = resourceId,
                // ResourceDef would be set from registry
                Density = 1.0f,
                MaxDensity = 1.0f,
                GrowthStage = 2, // Start mature
            };

            placed++;
        }
    }
}