namespace DontStarveRuneScape.Data;

using System.Text.Json;

/// <summary>
/// DataValues — JsonElement-safe extraction from DataLoader rows. DataLoader
/// loads JSON as List<Dictionary<string, object>>, which boxes every value
/// as JsonElement, so direct casts (is string / is float / is bool) silently
/// fail. All consumers should read values through these helpers, which accept
/// both the boxed JsonElement shape and plain primitives (hand-built rows).
/// </summary>
public static class DataValues
{
    public static string? GetString(object? value) => value switch
    {
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
        _ => null,
    };

    public static bool GetBool(object? value, bool fallback = false) => value switch
    {
        bool b => b,
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.False } => false,
        _ => fallback,
    };

    public static float GetFloat(object? value, float fallback = 0f) => value switch
    {
        float f => f,
        int i => i,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.GetSingle(),
        _ => fallback,
    };

    public static int GetInt(object? value, int fallback = 0) => value switch
    {
        int i => i,
        float f => (int)f,
        JsonElement { ValueKind: JsonValueKind.Number } e => e.GetInt32(),
        _ => fallback,
    };
}
