namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Collections.Generic;
using DontStarveRuneScape.Config;

/// <summary>
/// ItemCatalog — Display info for inventory/panel UI: name, sprite key, and
/// food stats per item id, parsed once from items.json. Unknown ids and a
/// missing file degrade to null so panels can fall back to id-derived text.
/// </summary>
public static class ItemCatalog
{
    public sealed record ItemDisplay(
        string Name,
        string SpriteKey,
        bool IsFood,
        float HungerRestore,
        float HpRestore,
        float SpoilageSeconds);

    private static Dictionary<string, ItemDisplay>? _byId;

    public static ItemDisplay? Get(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        if (_byId == null) Load();
        return _byId!.TryGetValue(itemId, out var display) ? display : null;
    }

    private static void Load()
    {
        List<Dictionary<string, object>> rows;
        try
        {
            rows = DataLoader.LoadJsonList<Dictionary<string, object>>(Constants.ItemsFile, "items");
        }
        catch { rows = []; }
        _byId = FromData(rows);
    }

    /// <summary>Parse display info from raw items data rows. Values may be plain
    /// primitives or JsonElement (DataLoader deserializes Dictionary<string, object>
    /// values as JsonElement); rows without an id are skipped.</summary>
    public static Dictionary<string, ItemDisplay> FromData(List<Dictionary<string, object>> itemsData)
    {
        var byId = new Dictionary<string, ItemDisplay>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in itemsData)
        {
            string? id = GetString(entry, "id");
            if (string.IsNullOrEmpty(id)) continue;
            byId[id] = new ItemDisplay(
                GetString(entry, "name") ?? id,
                GetString(entry, "sprite_key") ?? id,
                GetBool(entry, "is_food"),
                GetFloat(entry, "hunger_restore"),
                GetFloat(entry, "hp_restore"),
                GetFloat(entry, "spoilage_seconds"));
        }
        return byId;
    }

    private static string? GetString(Dictionary<string, object> entry, string key) =>
        entry.TryGetValue(key, out var v) ? v switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            _ => null,
        } : null;

    private static bool GetBool(Dictionary<string, object> entry, string key) =>
        entry.TryGetValue(key, out var v) && v switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            JsonElement { ValueKind: JsonValueKind.False } => false,
            _ => false,
        };

    private static float GetFloat(Dictionary<string, object> entry, string key) =>
        entry.TryGetValue(key, out var v) ? v switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetSingle(),
            float f => f,
            int i => i,
            _ => 0f,
        } : 0f;
}
