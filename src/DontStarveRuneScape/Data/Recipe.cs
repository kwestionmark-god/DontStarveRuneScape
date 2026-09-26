namespace DontStarveRuneScape.Data;

using System.Text.Json;
using System.Collections.Generic;
using DontStarveRuneScape.Config;

/// <summary>
/// CraftRecipe — One recipe from the recipe data files (general plus the
/// per-skill files): input item/quantity pairs converted into an output item,
/// with the skill gate and campfire/food/quest flags the panel and
/// CraftingSystem use. Parsed from the actual recipes.json shape
/// (recipe_id / input_items pairs / output_item), not the aspirational one.
/// </summary>
public sealed class CraftRecipe
{
    public string RecipeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public (string ItemId, int Quantity)[] Inputs { get; init; } = [];
    public string OutputItem { get; init; } = string.Empty;
    public int OutputQuantity { get; init; } = 1;
    public float XpReward { get; init; }
    public string RequiredSkill { get; init; } = string.Empty;
    public int RequiredLevel { get; init; } = 1;
    public int Tier { get; init; } = 1;
    public bool RequiresCampfire { get; init; }
    public bool IsFood { get; init; }
    public string? OreItem { get; init; }
    public string? FuelItem { get; init; }
    public string? QuestUnlock { get; init; }
}

/// <summary>
/// RecipeRegistry — All recipes loaded from the recipe data files.
/// </summary>
public sealed class RecipeRegistry
{
    public Dictionary<string, CraftRecipe> Recipes { get; } = [];

    public void LoadAll()
    {
        foreach (string file in Constants.RecipeFiles)
        {
            List<Dictionary<string, object>> rows;
            try
            {
                rows = DataLoader.LoadJsonList<Dictionary<string, object>>(file, "recipes");
            }
            catch { continue; }

            foreach (var recipe in FromData(rows).Values)
                Recipes[recipe.RecipeId] = recipe;
        }
    }

    public CraftRecipe? GetRecipe(string id) =>
        Recipes.TryGetValue(id, out var recipe) ? recipe : null;

    /// <summary>Parse recipes from raw rows. Values may be plain primitives or
    /// JsonElement (DataLoader deserializes Dictionary<string, object> values as
    /// JsonElement); rows without a recipe id or output item are skipped.</summary>
    public static Dictionary<string, CraftRecipe> FromData(List<Dictionary<string, object>> rows)
    {
        var byId = new Dictionary<string, CraftRecipe>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in rows)
        {
            string? id = GetString(entry, "recipe_id");
            string? output = GetString(entry, "output_item");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(output)) continue;

            byId[id] = new CraftRecipe
            {
                RecipeId = id,
                Name = GetString(entry, "name") ?? id,
                Inputs = ParseInputs(entry),
                OutputItem = output,
                OutputQuantity = GetInt(entry, "output_quantity") ?? 1,
                XpReward = GetFloat(entry, "xp_reward"),
                RequiredSkill = GetString(entry, "required_skill") ?? string.Empty,
                RequiredLevel = GetInt(entry, "required_level") ?? 1,
                Tier = GetInt(entry, "tier") ?? 1,
                RequiresCampfire = GetBool(entry, "requires_campfire"),
                IsFood = GetBool(entry, "is_food"),
                OreItem = GetString(entry, "ore_item"),
                FuelItem = GetString(entry, "fuel_item"),
                QuestUnlock = GetString(entry, "quest_unlock"),
            };
        }
        return byId;
    }

    // input_items shape: [["oak_logs", 1], ...] — item/quantity pairs.
    private static (string, int)[] ParseInputs(Dictionary<string, object> entry)
    {
        if (!entry.TryGetValue("input_items", out var raw) ||
            raw is not JsonElement { ValueKind: JsonValueKind.Array } arr)
            return [];

        var inputs = new List<(string, int)>();
        foreach (var pair in arr.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array) continue;
            string? itemId = null;
            int quantity = 0;
            int index = 0;
            foreach (var value in pair.EnumerateArray())
            {
                if (index == 0 && value.ValueKind == JsonValueKind.String)
                    itemId = value.GetString();
                else if (index == 1 && value.ValueKind == JsonValueKind.Number)
                    quantity = value.GetInt32();
                index++;
            }
            if (!string.IsNullOrEmpty(itemId) && quantity > 0)
                inputs.Add((itemId, quantity));
        }
        return inputs.ToArray();
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

    private static int? GetInt(Dictionary<string, object> entry, string key) =>
        entry.TryGetValue(key, out var v) ? v switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } je => je.GetInt32(),
            int i => i,
            _ => null,
        } : null;
}
