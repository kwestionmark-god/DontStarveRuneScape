namespace DontStarveRuneScape.Crafting;

using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;

/// <summary>
/// CraftingSystem — Handles crafting, cooking, and smelting recipes.
/// </summary>
public sealed class CraftingSystem
{
    public void Tick(float dt) { }

    /// <summary>Try to craft a recipe.</summary>
    public CraftResult Craft(string recipeId, Inventory inventory, SkillManager skillManager)
    {
        return new CraftResult { Success = false, Message = "Recipe not implemented." };
    }
}

/// <summary>
/// CraftResult — Result of a crafting attempt.
/// </summary>
public sealed class CraftResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, int> ProducedItems { get; set; } = new();
    public float XpGained { get; set; }
}