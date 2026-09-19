namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Biome definition for world generation.
/// </summary>
public sealed class BiomeDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("mega_cluster")]
    public string MegaCluster { get; set; } = string.Empty;

    [JsonPropertyName("environmental_pressure")]
    public float EnvironmentalPressure { get; set; }

    [JsonPropertyName("starting_safety")]
    public bool StartingSafety { get; set; }

    [JsonPropertyName("elevation_range")]
    public int[] ElevationRange { get; set; } = [0, 7];

    [JsonPropertyName("terrain_colors")]
    public Dictionary<string, int[]> TerrainColors { get; set; } = [];

    [JsonPropertyName("resource_spawns")]
    public string[] ResourceSpawns { get; set; } = [];

    /// <summary>
    /// Get terrain color for a specific elevation level.
    /// </summary>
    public (byte R, byte G, byte B) GetTerrainColor(int elevation)
    {
        string key = elevation.ToString();
        if (TerrainColors.TryGetValue(key, out var color) && color.Length >= 3)
            return ((byte)color[0], (byte)color[1], (byte)color[2]);

        // Fallback to nearest available
        if (TerrainColors.Count > 0)
        {
            var nearest = TerrainColors.OrderBy(kvp => Math.Abs(int.Parse(kvp.Key) - elevation)).First();
            if (nearest.Value.Length >= 3)
                return ((byte)nearest.Value[0], (byte)nearest.Value[1], (byte)nearest.Value[2]);
        }

        return (128, 128, 128); // Gray fallback
    }
}

/// <summary>
/// Registry of all biomes.
/// </summary>
public sealed class BiomeRegistry
{
    public Dictionary<string, BiomeDef> Biomes { get; } = [];
    public BiomeDef? DefaultBiome { get; set; }

    public BiomeRegistry() { }

    public BiomeRegistry(IEnumerable<BiomeDef> biomes)
    {
        foreach (var biome in biomes)
            Biomes[biome.Id] = biome;

        // Find starting safety biome as default
        DefaultBiome = Biomes.Values.FirstOrDefault(b => b.StartingSafety)
                       ?? Biomes.Values.FirstOrDefault();
    }

    public BiomeDef? GetBiome(string id)
    {
        return Biomes.TryGetValue(id, out var biome) ? biome : DefaultBiome;
    }

    public BiomeDef GetBiomeForElevation(float elevation)
    {
        // Find biome whose elevation range contains the given elevation
        foreach (var biome in Biomes.Values)
        {
            if (biome.ElevationRange.Length >= 2 &&
                elevation >= biome.ElevationRange[0] &&
                elevation <= biome.ElevationRange[1])
                return biome;
        }
        return DefaultBiome ?? Biomes.Values.First();
    }
}