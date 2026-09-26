namespace DontStarveRuneScape.Crafting;

using System.Collections.Generic;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;

/// <summary>
/// CraftingSystem — Crafts recipes from the recipe registry: checks the skill
/// gate and ingredients, consumes inputs, produces the output, grants XP.
/// </summary>
public sealed class CraftingSystem
{
    /// <summary>Recipes this system crafts from; loaded at world boot.</summary>
    public RecipeRegistry? Registry { get; set; }

    public void Tick(float dt) { }

    /// <summary>Try to craft a recipe: skill gate, ingredient gate, all-or-nothing
    /// consume/produce, XP grant. Returns a result with a player-facing message.</summary>
    public CraftResult Craft(string recipeId, Inventory inventory, SkillManager skillManager)
    {
        var recipe = Registry?.GetRecipe(recipeId);
        if (recipe == null)
            return new CraftResult { Success = false, Message = "Unknown recipe." };

        if (skillManager.GetSkillLevel(recipe.RequiredSkill) < recipe.RequiredLevel)
            return new CraftResult
            {
                Success = false,
                Message = $"Requires {recipe.RequiredSkill} level {recipe.RequiredLevel}.",
            };

        foreach (var (itemId, quantity) in recipe.Inputs)
        {
            if (inventory.GetItemQuantity(itemId) < quantity)
                return new CraftResult
                {
                    Success = false,
                    Message = $"Missing ingredients: needs {quantity} {itemId}.",
                };
        }

        // All-or-nothing: the output must fit before anything is consumed.
        if (!inventory.CanAdd(recipe.OutputItem, recipe.OutputQuantity))
            return new CraftResult { Success = false, Message = "Inventory is full." };

        foreach (var (itemId, quantity) in recipe.Inputs)
            inventory.RemoveItem(itemId, quantity);

        inventory.AddItem(recipe.OutputItem, recipe.OutputQuantity);

        var levelUpMessages = skillManager.AddXpWithNotification(recipe.RequiredSkill, recipe.XpReward);
        return new CraftResult
        {
            Success = true,
            Message = $"Crafted {recipe.OutputQuantity} {recipe.OutputItem}.",
            ProducedItems = { [recipe.OutputItem] = recipe.OutputQuantity },
            XpGained = recipe.XpReward,
            LevelUpMessages = levelUpMessages,
        };
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

    /// <summary>Skill level-up lines from the XP grant, for the panel to show.</summary>
    public List<string> LevelUpMessages { get; set; } = [];
}
