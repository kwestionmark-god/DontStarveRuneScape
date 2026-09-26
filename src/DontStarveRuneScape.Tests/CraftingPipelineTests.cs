namespace DontStarveRuneScape.Tests;

using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;
using Xunit;

/// <summary>
/// Recipe parsing (actual recipes.json shape) and the CraftingSystem craft
/// flow: skill gate, ingredient gate, all-or-nothing consume/produce, XP.
/// </summary>
public class CraftingPipelineTests
{
    private static Dictionary<string, object> Row(params (string Key, object Value)[] fields)
    {
        var row = new Dictionary<string, object>();
        foreach (var (key, value) in fields)
            row[key] = JsonSerializer.SerializeToElement(value);
        return row;
    }

    private static Dictionary<string, object> SticksRow() => new()
    {
        ["recipe_id"] = JsonSerializer.SerializeToElement("sticks"),
        ["name"] = JsonSerializer.SerializeToElement("Break Sticks"),
        ["input_items"] = JsonSerializer.SerializeToElement(new object[] { new object[] { "oak_logs", 1 } }),
        ["output_item"] = JsonSerializer.SerializeToElement("stick"),
        ["output_quantity"] = JsonSerializer.SerializeToElement(3),
        ["xp_reward"] = JsonSerializer.SerializeToElement(3.0),
        ["required_skill"] = JsonSerializer.SerializeToElement("crafting"),
        ["required_level"] = JsonSerializer.SerializeToElement(1),
        ["tier"] = JsonSerializer.SerializeToElement(1),
        ["requires_campfire"] = JsonSerializer.SerializeToElement(false),
    };

    [Fact]
    public void FromData_ParsesActualRecipeShape()
    {
        var recipes = RecipeRegistry.FromData([SticksRow()]);
        var recipe = recipes["sticks"];
        Assert.Equal("Break Sticks", recipe.Name);
        var input = Assert.Single(recipe.Inputs);
        Assert.Equal(("oak_logs", 1), input);
        Assert.Equal("stick", recipe.OutputItem);
        Assert.Equal(3, recipe.OutputQuantity);
        Assert.Equal(3f, recipe.XpReward);
        Assert.Equal("crafting", recipe.RequiredSkill);
        Assert.Equal(1, recipe.RequiredLevel);
        Assert.False(recipe.RequiresCampfire);
    }

    [Fact]
    public void FromData_ParsesCampfireAndFoodFlags()
    {
        var row = Row(("recipe_id", "cooked_meat"), ("output_item", "cooked_meat"),
            ("required_skill", "cooking"), ("required_level", 1),
            ("requires_campfire", true), ("is_food", true));
        var recipes = RecipeRegistry.FromData([row]);
        var recipe = recipes["cooked_meat"];
        Assert.True(recipe.RequiresCampfire);
        Assert.True(recipe.IsFood);
    }

    [Fact]
    public void FromData_SkipsRowsWithoutIdOrOutput()
    {
        var recipes = RecipeRegistry.FromData(
        [
            Row(("name", "No Id")),
            Row(("recipe_id", "no_output")),
        ]);
        Assert.Empty(recipes);
    }

    [Fact]
    public void LoadAll_ReadsRealRecipeFiles()
    {
        // Real data files (copied into the test bin): the unified registry must
        // pick up the general file plus the per-skill files.
        var registry = new RecipeRegistry();
        registry.LoadAll();
        Assert.True(registry.Recipes.Count >= 80, $"expected 80+ recipes, got {registry.Recipes.Count}");
        var sticks = registry.GetRecipe("sticks");
        Assert.NotNull(sticks);
        Assert.Equal(("oak_logs", 1), Assert.Single(sticks!.Inputs));
        Assert.Equal("stick", sticks.OutputItem);
        Assert.NotNull(registry.GetRecipe("cook_meat"));
    }

    private static (CraftingSystem System, Inventory Inventory, SkillManager Skills) MakeCraftable()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inventory();
        inventory.AddItem("oak_logs", 5);
        return (system, inventory, new SkillManager());
    }

    [Fact]
    public void Craft_ConsumesInputs_ProducesOutput_GrantsXp()
    {
        var (system, inventory, skills) = MakeCraftable();
        var result = system.Craft("sticks", inventory, skills);
        Assert.True(result.Success, result.Message);
        Assert.Equal(4, inventory.GetItemQuantity("oak_logs"));
        Assert.Equal(3, inventory.GetItemQuantity("stick"));
        Assert.Equal(3f, result.XpGained);
        Assert.Equal(3f, skills.GetSkill("crafting").Xp);
    }

    [Fact]
    public void Craft_FailsWithoutIngredients_AndDoesNotConsume()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inventory();
        var result = system.Craft("sticks", inventory, new SkillManager());
        Assert.False(result.Success);
        Assert.Equal(0, inventory.GetItemQuantity("oak_logs"));
        Assert.Equal(0, inventory.GetItemQuantity("stick"));
    }

    [Fact]
    public void Craft_FailsBelowRequiredLevel()
    {
        // Charcoal requires crafting level 3; a fresh SkillManager is level 1.
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inventory();
        inventory.AddItem("oak_logs", 5);
        var result = system.Craft("charcoal", inventory, new SkillManager());
        Assert.False(result.Success, result.Message);
        Assert.Equal(5, inventory.GetItemQuantity("oak_logs"));
    }

    [Fact]
    public void Craft_FailsWhenOutputDoesNotFit_AndDoesNotConsume()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inventory();
        // Fill 19 of the 20 slots with distinct items, then the ingredient: every
        // slot is occupied, so the output needs a new slot and cannot fit.
        for (int i = 0; i < 19; i++)
            inventory.AddItem($"filler_{i}", 1);
        inventory.AddItem("oak_logs", 5);
        Assert.Equal(20, inventory.Slots.Count(s => s.ItemId != null));
        var result = system.Craft("sticks", inventory, new SkillManager());
        Assert.False(result.Success, result.Message);
        Assert.Equal(5, inventory.GetItemQuantity("oak_logs"));
        Assert.Equal(0, inventory.GetItemQuantity("stick"));
    }

    [Fact]
    public void Craft_UnknownRecipeFails()
    {
        var (system, inventory, skills) = MakeCraftable();
        var result = system.Craft("not_a_recipe", inventory, skills);
        Assert.False(result.Success);
    }
}
