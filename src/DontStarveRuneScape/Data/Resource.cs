namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Resource node definition for world generation.
/// </summary>
public sealed class ResourceDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("biome")]
    public string Biome { get; init; } = string.Empty;

    [JsonPropertyName("tier")]
    public int Tier { get; init; } = 1;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("density")]
    public float Density { get; init; } = 0.1f;

    [JsonPropertyName("yield")]
    public int Yield { get; init; } = 1;

    [JsonPropertyName("xp")]
    public int Xp { get; init; } = 1;

    [JsonPropertyName("depletion")]
    public float Depletion { get; init; } = 1.0f;

    [JsonPropertyName("regrow")]
    public float Regrow { get; init; } = 0.0f;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; init; } = string.Empty;

    [JsonPropertyName("tool_requirement")]
    public string? ToolRequirement { get; init; }

    [JsonPropertyName("seasons")]
    public string[] Seasons { get; init; } = [];

    [JsonPropertyName("rarity")]
    public string Rarity { get; init; } = "common";
}

/// <summary>
/// Registry of all resources.
/// </summary>
public sealed class ResourceRegistry
{
    public Dictionary<string, ResourceDef> Resources { get; } = [];

    public ResourceRegistry() { }

    public ResourceRegistry(IEnumerable<ResourceDef> resources)
    {
        foreach (var resource in resources)
            Resources[resource.Id] = resource;
    }

    public ResourceDef? GetResource(string id)
    {
        return Resources.TryGetValue(id, out var resource) ? resource : null;
    }

    public IEnumerable<ResourceDef> GetResourcesForBiome(string biomeId)
    {
        return Resources.Values.Where(r => r.Biome == biomeId);
    }

    public IEnumerable<ResourceDef> GetResourcesForSeason(string season)
    {
        return Resources.Values.Where(r => r.Seasons.Length == 0 || r.Seasons.Contains(season));
    }
}