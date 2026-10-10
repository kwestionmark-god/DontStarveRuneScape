namespace DontStarveRuneScape.Crafting;

using System.Collections.Generic;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;

/// <summary>
/// CraftingSystem — Crafts recipes from the recipe registry against either the
/// player inventory or colony stockpile, with skill and optional station gates.
/// </summary>
public sealed class CraftingSystem
{
    /// <summary>Recipes this system crafts from; loaded at world boot.</summary>
    public RecipeRegistry? Registry { get; set; }

    /// <summary>Invoked with the output item id after a successful craft;
    /// wired to the QuestSystem's NotifyCraft for quest progress.</summary>
    public Action<string>? OnCrafted { get; set; }

    public void Tick(float dt) { }

    /// <summary>Try to craft a recipe: skill gate, quest gate, ingredient
    /// gate, all-or-nothing consume/produce, XP grant. Returns a result with
    /// a player-facing message. unlockedRecipes: the player's completed-quest
    /// recipe unlocks; null (the worker auto-production path) is ungated by
    /// the colony stock-visibility convention.</summary>
    public CraftResult Craft(string recipeId, IItemStorage inventory, SkillManager skillManager,
        IReadOnlySet<string>? availableStructures = null,
        IReadOnlySet<string>? unlockedRecipes = null)
    {
        var recipe = Registry?.GetRecipe(recipeId);
        if (recipe == null)
            return new CraftResult { Success = false, Message = "Unknown recipe." };

        // Quest gate: recipes flagged quest_unlock refuse until the owning
        // quest's claim added the recipe to the unlock set. Display and
        // enforcement share one flag — what the panel says is what happens.
        if (unlockedRecipes != null && recipe.QuestUnlock != null
            && !unlockedRecipes.Contains(recipe.QuestUnlock))
            return new CraftResult
            {
                Success = false,
                Message = $"Requires quest unlock: {recipe.QuestUnlock}.",
            };

        if (skillManager.GetSkillLevel(recipe.RequiredSkill) < recipe.RequiredLevel)
            return new CraftResult
            {
                Success = false,
                Message = $"Requires {recipe.RequiredSkill} level {recipe.RequiredLevel}.",
            };

        if (availableStructures != null && !string.IsNullOrEmpty(recipe.RequiresStructure)
            && !availableStructures.Contains(recipe.RequiresStructure))
            return new CraftResult
            {
                Success = false,
                Message = $"Requires a {recipe.RequiresStructure}.",
            };

        if (availableStructures != null && recipe.RequiresCampfire
            && !availableStructures.Contains("campfire")
            && !availableStructures.Contains("cooking_station")
            && !availableStructures.Contains("furnace")
            && !availableStructures.Contains("smelter"))
            return new CraftResult
            {
                Success = false,
                Message = "Requires a campfire or cooking station.",
            };

        var groupedInputs = recipe.Inputs
            .GroupBy(input => input.ItemId, StringComparer.Ordinal)
            .Select(group => (ItemId: group.Key, Quantity: group.Sum(input => input.Quantity)))
            .ToArray();
        foreach (var (itemId, quantity) in groupedInputs)
        {
            if (inventory.GetItemQuantity(itemId) < quantity)
                return new CraftResult
                {
                    Success = false,
                    Message = $"Missing ingredients: needs {quantity} {itemId}.",
                };
        }

        // All-or-nothing: the output (plus any harvest_boost extra) must
        // fit before anything is consumed.
        // harvest_boost: invested points (raw) = % chance of +1 output —
        // the production-skill yield arm (0 points = exact legacy output).
        float yieldBonus = skillManager.GetSubStatPoints(recipe.RequiredSkill, "harvest_boost");
        int extraOutput = yieldBonus > 0f
            && new Random().NextDouble() * 100f < yieldBonus ? 1 : 0;
        int outputQuantity = recipe.OutputQuantity + extraOutput;

        if (!inventory.CanAdd(recipe.OutputItem, outputQuantity))
            return new CraftResult { Success = false, Message = "Inventory is full." };

        // efficiency: invested points (raw) = % chance to save one unit
        // of the recipe's largest input group (0 points = full consume).
        float saveBonus = skillManager.GetSubStatPoints(recipe.RequiredSkill, "efficiency");
        var largestInput = groupedInputs.OrderByDescending(g => g.Quantity).First();

        foreach (var (itemId, quantity) in groupedInputs)
        {
            int consume = quantity;
            if (itemId == largestInput.ItemId && saveBonus > 0f
                && new Random().NextDouble() * 100f < saveBonus)
                consume--;
            inventory.RemoveItem(itemId, consume);
        }

        inventory.AddItem(recipe.OutputItem, outputQuantity);
        OnCrafted?.Invoke(recipe.OutputItem);

        var levelUpMessages = skillManager.AddXpWithNotification(recipe.RequiredSkill, recipe.XpReward);
        return new CraftResult
        {
            Success = true,
            Message = $"Crafted {outputQuantity} {recipe.OutputItem}.",
            ProducedItems = { [recipe.OutputItem] = outputQuantity },
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
