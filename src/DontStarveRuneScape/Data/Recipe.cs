namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Recipe definition for crafting/cooking/smelting.
/// </summary>
public sealed class RecipeDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "craft"; // "craft", "cook", "smelt", "firemaking", "construction"

    [JsonPropertyName("skill")]
    public string Skill { get; init; } = string.Empty; // Skill that grants XP

    [JsonPropertyName("inputs")]
    public RecipeIngredient[] Inputs { get; init; } = [];

    [JsonPropertyName("outputs")]
    public RecipeOutput[] Outputs { get; init; } = [];

    [JsonPropertyName("level_req")]
    public int LevelReq { get; init; } = 1;

    [JsonPropertyName("xp_reward")]
    public int XpReward { get; init; } = 0;

    [JsonPropertyName("success_rate")]
    public float SuccessRate { get; init; } = 1.0f; // For cooking (probabilistic)

    [JsonPropertyName("station")]
    public string? Station { get; init; } // Required structure (e.g., "furnace", "anvil", "cooking_fire")

    [JsonPropertyName("seasonal")]
    public bool Seasonal { get; init; }

    [JsonPropertyName("quest_locked")]
    public string? QuestLocked { get; init; } // Quest ID that must be completed
}

/// <summary>
/// Recipe ingredient.
/// </summary>
public sealed class RecipeIngredient
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; } = 1;

    [JsonPropertyName("consume")]
    public bool Consume { get; init; } = true;
}

/// <summary>
/// Recipe output.
/// </summary>
public sealed class RecipeOutput
{
    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; } = 1;

    [JsonPropertyName("chance")]
    public float Chance { get; init; } = 1.0f;
}

/// <summary>
/// Registry of all recipes.
/// </summary>
public sealed class RecipeRegistry
{
    public Dictionary<string, RecipeDef> Recipes { get; } = [];

    public RecipeRegistry() { }

    public RecipeRegistry(IEnumerable<RecipeDef> recipes)
    {
        foreach (var r in recipes)
            Recipes[r.Id] = r;
    }

    public void LoadAll()
    {
        // Will be populated from JSON at runtime
    }

    public RecipeDef? GetRecipe(string id)
    {
        return Recipes.TryGetValue(id, out var r) ? r : null;
    }

    public IEnumerable<RecipeDef> GetRecipesForSkill(string skillId)
    {
        return Recipes.Values.Where(r => r.Skill == skillId);
    }

    public IEnumerable<RecipeDef> GetRecipesForType(string type)
    {
        return Recipes.Values.Where(r => r.Type == type);
    }

    public IEnumerable<RecipeDef> GetCraftableRecipes(SkillManager skillManager, ItemRegistry itemRegistry, HashSet<string> unlockedRecipes)
    {
        return Recipes.Values.Where(r =>
            r.Type == "craft" &&
            r.LevelReq <= skillManager.GetLevel(r.Skill) &&
            (r.QuestLocked == null || skillManager.IsQuestCompleted(r.QuestLocked)) &&
            (r.Station == null || HasStation(r.Station)) &&
            CanCraft(r, itemRegistry));
    }

    private bool HasStation(string stationId)
    {
        // TODO: Check if player has access to this station
        return true;
    }

    private bool CanCraft(RecipeDef recipe, ItemRegistry itemRegistry)
    {
        // Check if all inputs are available (will be checked at craft time with actual inventory)
        return true;
    }
}