namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Resource node definition for world generation.
/// Field names match the JSON envelope in resources.json.
/// </summary>
public sealed class ResourceDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("biome")]
    public string Biome { get; set; } = string.Empty;

    [JsonPropertyName("tier")]
    public int Tier { get; set; } = 1;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("base_density")]
    public float Density { get; set; } = 0.1f;

    [JsonPropertyName("yield_item")]
    public string YieldItem { get; set; } = string.Empty;

    [JsonPropertyName("yield_quantity")]
    public int Yield { get; set; } = 1;

    [JsonPropertyName("xp_reward")]
    public float Xp { get; set; } = 1f;

    [JsonPropertyName("depletion_count")]
    public int DepletionCount { get; set; } = 1;

    [JsonPropertyName("regrow_time")]
    public float Regrow { get; set; } = 0f;

    [JsonPropertyName("sprite_key")]
    public string SpriteKey { get; set; } = string.Empty;

    [JsonPropertyName("requires_tool")]
    public string? ToolRequirement { get; set; }

    [JsonPropertyName("seasons")]
    public string[] Seasons { get; set; } = [];

    [JsonPropertyName("rarity")]
    public string Rarity { get; set; } = "common";

    [JsonPropertyName("required_level")]
    public int RequiredLevel { get; set; } = 1;

    /// <summary>Draw-size multiplier for this resource's sprite (1 = 48px box).</summary>
    [JsonPropertyName("display_scale")]
    public float DisplayScale { get; set; } = 1f;

    /// <summary>Whether this resource requires a tool to harvest.</summary>
    public bool RequiresTool => !string.IsNullOrEmpty(ToolRequirement);
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