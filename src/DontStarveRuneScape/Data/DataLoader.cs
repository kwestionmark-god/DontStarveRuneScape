namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using DontStarveRuneScape.Config;

/// <summary>
/// Shared data loading utilities.
/// All JSON files use a metadata + keyed-records envelope
/// (_description/_note stripped by consumers).
/// </summary>
public sealed class DataLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Loaded data
    public List<Dictionary<string, object>> ItemsData { get; private set; } = [];
    public List<BiomeDef> Biomes { get; private set; } = [];
    public List<Dictionary<string, object>> ResourcesData { get; private set; } = [];
    public List<Dictionary<string, object>> MonstersData { get; private set; } = [];
    public List<Dictionary<string, object>> GearData { get; private set; } = [];
    public List<Dictionary<string, object>> StructuresData { get; private set; } = [];
    public List<Dictionary<string, object>> NPCsData { get; private set; } = [];
    public List<Dictionary<string, object>> QuestsData { get; private set; } = [];
    public List<Dictionary<string, object>> FactionsData { get; private set; } = [];
    public List<Dictionary<string, object>> TradeData { get; private set; } = [];
    public List<Dictionary<string, object>> RecipesData { get; private set; } = [];

    /// <summary>
    /// Load a JSON file and return the root element.
    /// </summary>
    public static JsonElement LoadJson(string relativePath)
    {
        string fullPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (!File.Exists(fullPath))
        {
            // Try relative to working directory
            fullPath = Path.GetFullPath(relativePath);
        }
        string json = File.ReadAllText(fullPath);
        return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }).RootElement;
    }

    /// <summary>
    /// Load a JSON file and return the typed object.
    /// </summary>
    public static T? LoadJson<T>(string relativePath) where T : class
    {
        var element = LoadJson(relativePath);
        return element.Deserialize<T>(JsonOptions);
    }

    /// <summary>
    /// Load a JSON list from a keyed-records envelope.
    /// e.g. { "items": [ {...}, {...} ] } -> returns the array under "items"
    /// </summary>
    public static JsonElement LoadJsonList(string relativePath, string key)
    {
        var root = LoadJson(relativePath);
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var list))
            return list;
        return root; // fallback: assume root is the array
    }

    /// <summary>
    /// Load a typed list from a keyed-records envelope.
    /// </summary>
    public static List<T> LoadJsonList<T>(string relativePath, string key) where T : class
    {
        var listElement = LoadJsonList(relativePath, key);
        return listElement.Deserialize<List<T>>(JsonOptions) ?? [];
    }

    /// <summary>
    /// Load a dictionary from a keyed-records envelope where the value is an object.
    /// </summary>
    public static Dictionary<string, T> LoadJsonDict<T>(string relativePath, string key) where T : class
    {
        var list = LoadJsonList<T>(relativePath, key);
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            // Try to get an "id" property for the key
            var json = JsonSerializer.SerializeToElement(item, JsonOptions);
            if (json.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                dict[idProp.GetString()!] = item;
            }
        }
        return dict;
    }

    /// <summary>
    /// Load all game data from JSON files.
    /// </summary>
    public void LoadAll()
    {
        try
        {
            ItemsData = LoadJsonList<Dictionary<string, object>>(Constants.ItemsFile, "items");
        }
        catch { ItemsData = []; }

        try
        {
            Biomes = LoadJsonList<BiomeDef>(Constants.BiomesFile, "biomes");
        }
        catch { Biomes = []; }

        try
        {
            ResourcesData = LoadJsonList<Dictionary<string, object>>(Constants.ResourcesFile, "resources");
        }
        catch { ResourcesData = []; }
    }
}

/// <summary>
/// Base class for data records with an ID.
/// </summary>
public abstract class DataRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}