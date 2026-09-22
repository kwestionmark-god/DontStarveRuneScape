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

        // Step 3: Water bodies (rivers/lakes from flow accumulation) —
        // water tiles override the biome classification like the Python port.
        progressCallback?.Invoke(0.45f);
        ApplyWaterBodies(map, biomeRegistry, elevationMap);

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
                    biomeId = "water";
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
    /// Water body generation ported from the Python prototype: D8 flow
    /// accumulation carves rivers, priority flood-fill fills lakes in closed
    /// depressions, coasts smooth to ocean at the map edge, and steep downhill
    /// paths connect nearby bodies into a network.
    /// </summary>
    private static void ApplyWaterBodies(
        TileMap map, BiomeRegistry biomeRegistry, float[,] elevation)
    {
        int width = map.Width, height = map.Height;
        var water = new bool[width, height];
        var waterBiome = biomeRegistry.GetBiome("water");
        if (waterBiome == null) return;

        // --- 1. D8 flow accumulation (steepest-descent neighbor) ---
        var flowTo = new sbyte[width, height]; // -1 = no downhill neighbor
        var dirs = new (int dx, int dy)[] { (-1,-1),(0,-1),(1,-1),(-1,0),(1,0),(-1,1),(0,1),(1,1) };
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float current = elevation[x, y];
                float steepest = 0f;
                int best = -1;
                for (int d = 0; d < dirs.Length; d++)
                {
                    int nx = x + dirs[d].dx, ny = y + dirs[d].dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    float slope = current - elevation[nx, ny];
                    if (slope > steepest) { steepest = slope; best = d; }
                }
                flowTo[x, y] = (sbyte)best;
            }
        }

        // Accumulate upstream tile counts, processing tiles high-to-low.
        var accum = new float[width, height];
        var byElev = new List<(float E, int X, int Y)>(width * height);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                byElev.Add((elevation[x, y], x, y));
        byElev.Sort((a, b) => b.E.CompareTo(a.E));
        foreach (var (_, x, y) in byElev)
        {
            int d = flowTo[x, y];
            if (d >= 0)
            {
                int nx = x + dirs[d].dx, ny = y + dirs[d].dy;
                accum[nx, ny] += accum[x, y] + 1f;
            }
        }

        // Rivers: any tile with enough upstream drainage.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (accum[x, y] >= Constants.RiverThreshold)
                    water[x, y] = true;

        // --- 2. Lakes: closed depressions away from the map edge ---
        var visited = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (visited[x, y]) continue;
                float basinElev = elevation[x, y];
                var basin = new List<(int X, int Y)>();
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                bool touchesEdge = false;
                float spillElev = float.MaxValue;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (visited[cx, cy]) continue;
                    visited[cx, cy] = true;
                    basin.Add((cx, cy));
                    if (cx == 0 || cy == 0 || cx == width - 1 || cy == height - 1)
                        touchesEdge = true;
                    foreach (var (dx, dy) in dirs)
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) { touchesEdge = true; continue; }
                        if (visited[nx, ny]) continue;
                        float ne = elevation[nx, ny];
                        if (ne <= basinElev + 1) stack.Push((nx, ny));
                        else if (ne < spillElev) spillElev = ne;
                    }
                }
                if (!touchesEdge && spillElev > basinElev && basinElev < 20 &&
                    basin.Count >= Constants.LakeMinSize && basin.Count <= 200)
                {
                    foreach (var (bx, by) in basin) water[bx, by] = true;
                }
            }
        }

        // --- 3. Coastal smoothing ---
        const int EdgeMargin = 4;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                bool nearEdge = x < EdgeMargin || y < EdgeMargin ||
                                x >= width - EdgeMargin || y >= height - EdgeMargin;
                if (nearEdge && elevation[x, y] <= 2) water[x, y] = true;
            }
        // Majority smoothing: 5+ water neighbors becomes water.
        var smoothed = (bool[,])water.Clone();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (water[x, y]) continue;
                int n = 0;
                foreach (var (dx, dy) in dirs)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < width && ny < height && water[nx, ny]) n++;
                }
                if (n >= 5) smoothed[x, y] = true;
            }
        water = smoothed;

        // --- 4. Connect nearby water bodies downhill (rivers to lakes/coast) ---
        foreach (var (_, sx, sy) in byElev)
        {
            if (!water[sx, sy]) continue;
            float current = elevation[sx, sy];
            int cx = sx, cy = sy;
            for (int step = 0; step < 20; step++)
            {
                float steepest = 0f; int best = -1;
                for (int d = 0; d < dirs.Length; d++)
                {
                    int nx = cx + dirs[d].dx, ny = cy + dirs[d].dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    float slope = current - elevation[nx, ny];
                    if (slope > steepest) { steepest = slope; best = d; }
                }
                if (best < 0 || steepest <= 0) break;
                cx += dirs[best].dx; cy += dirs[best].dy;
                current = elevation[cx, cy];
                if (water[cx, cy]) break; // reached existing water
                if (steepest > 0.5f) water[cx, cy] = true;
            }
        }

        // Flatten each connected water body to its lowest tile elevation so
        // water reads as one continuous surface filling a body, not stepped
        // slabs. Rivers crossing elevation get carved canyon banks instead.
        var bodyId = new int[width, height];
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++) bodyId[x, y] = -1;
        int bodyCount = 0;
        int[] bodyMin = Array.Empty<int>();
        for (int sx0 = 0; sx0 < width; sx0++)
            for (int sy0 = 0; sy0 < height; sy0++)
            {
                if (!water[sx0, sy0] || bodyId[sx0, sy0] >= 0) continue;
                int b = bodyCount++;
                Array.Resize(ref bodyMin, bodyCount);
                bodyMin[b] = 31;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((sx0, sy0));
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    if (cx < 0 || cy < 0 || cx >= width || cy >= height) continue;
                    if (!water[cx, cy] || bodyId[cx, cy] >= 0) continue;
                    bodyId[cx, cy] = b;
                    int ev = (int)map.Tiles[cx, cy].Elevation;
                    if (ev < bodyMin[b]) bodyMin[b] = ev;
                    stack.Push((cx + 1, cy)); stack.Push((cx - 1, cy));
                    stack.Push((cx, cy + 1)); stack.Push((cx, cy - 1));
                }
            }
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (bodyId[x, y] >= 0)
                    map.Tiles[x, y].Elevation = bodyMin[bodyId[x, y]];

        // Reclassify: water wins.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (water[x, y])
                    map.Tiles[x, y].Biome = waterBiome;
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