namespace DontStarveRuneScape.Tests;

using System.Text.Json;
using System.Collections.Generic;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Survival;
using Xunit;

/// <summary>
/// FoodRegistry parsing from the real items.json loader (values arrive boxed
/// as JsonElement) and the SurvivalSystem.Eat flow: hunger/hp restore and
/// stack sizes must come from the data, not silently fall back to defaults.
/// </summary>
public class FoodRegistryTests
{
    [Fact]
    public void Registry_ParsesRealItemsJson()
    {
        // Pins the real deserialization shape: items.json values are JsonElement
        // at runtime, so the parser must pattern-match both shapes.
        var items = DataLoader_LoadItems();
        var registry = new FoodRegistry(items);

        var berries = registry.Get("berries");
        Assert.NotNull(berries);
        Assert.Equal(8f, berries!.HungerRestoration);
        Assert.Equal(0f, berries.HpRestoration);
        Assert.Equal(900f, berries.SpoilageRate);
        Assert.True(berries.IsRaw);

        var cactusFlesh = registry.Get("cactus_flesh");
        Assert.NotNull(cactusFlesh);
        Assert.Equal(-2f, cactusFlesh!.HpRestoration);
        Assert.Equal(string.Empty, cactusFlesh.CookingBaseItem);

        var cookedFish = registry.Get("cooked_fish");
        Assert.NotNull(cookedFish);
        Assert.Equal(20f, cookedFish!.HungerRestoration);
        Assert.Equal(2f, cookedFish.HpRestoration);
        Assert.False(cookedFish.IsRaw);
        Assert.Equal("raw_fish", cookedFish.CookingBaseItem);

        Assert.True(registry.All().Count >= 16, $"expected 16+ foods, got {registry.All().Count}");
    }

    [Fact]
    public void GetStackSize_ReadsRealItemsJson()
    {
        var registry = new FoodRegistry(DataLoader_LoadItems());
        Assert.Equal(20, registry.GetStackSize("berries"));
        Assert.Equal(10, registry.GetStackSize("cooked_fish"));
    }

    [Fact]
    public void Registry_ParsesJsonElementRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = JsonSerializer.SerializeToElement("berries"),
                ["is_food"] = JsonSerializer.SerializeToElement(true),
                ["hunger_restore"] = JsonSerializer.SerializeToElement(8),
                ["hp_restore"] = JsonSerializer.SerializeToElement(0),
                ["spoilage_seconds"] = JsonSerializer.SerializeToElement(900),
                ["is_raw"] = JsonSerializer.SerializeToElement(true),
            },
        };
        var registry = new FoodRegistry(data);

        var food = registry.Get("berries");
        Assert.NotNull(food);
        Assert.Equal(8f, food!.HungerRestoration);
        Assert.Equal(900f, food.SpoilageRate);
        Assert.True(food.IsRaw);
    }

    [Fact]
    public void Registry_ParsesPlainPrimitiveRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            new()
            {
                ["id"] = "berries",
                ["is_food"] = true,
                ["hunger_restore"] = 8,
                ["hp_restore"] = 2,
                ["spoilage_seconds"] = 900.5f,
                ["is_raw"] = false,
                ["cooking_base_item"] = "raw_berries",
            },
        };
        var registry = new FoodRegistry(data);

        var food = registry.Get("berries");
        Assert.NotNull(food);
        Assert.Equal(8f, food!.HungerRestoration);
        Assert.Equal(2f, food.HpRestoration);
        Assert.Equal(900.5f, food.SpoilageRate);
        Assert.False(food.IsRaw);
        Assert.Equal("raw_berries", food.CookingBaseItem);
    }

    [Fact]
    public void Registry_IgnoresNonFoodRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "oak_logs", ["is_food"] = JsonSerializer.SerializeToElement(false) },
            new() { ["id"] = "stone" },
        };
        var registry = new FoodRegistry(data);
        Assert.Null(registry.Get("oak_logs"));
        Assert.Null(registry.Get("stone"));
    }

    [Fact]
    public void Eat_RestoresHungerAndHp_FromRegistry()
    {
        var registry = new FoodRegistry(DataLoader_LoadItems());
        var survival = new SurvivalSystem();

        // Drain hunger first: it starts at 100 (full), so a fresh Eat gains 0.
        survival.Tick(300f); // 1 hunger point per 30s interval
        float before = survival.Hunger;
        Assert.Equal(90f, before);

        // Take a wound: HP is capped at MaxHp (20), so a fresh Eat heals 0.
        survival.TakeDamage(5f);
        var message = survival.Eat(registry.Get("cooked_fish")!);
        Assert.Equal(100f, survival.Hunger); // +20, capped at max
        Assert.Equal(17f, survival.Hp);      // +2 heal on the wound
        Assert.Contains("Hunger +10", message);
    }

    private static List<Dictionary<string, object>> DataLoader_LoadItems() =>
        DontStarveRuneScape.Data.DataLoader.LoadJsonList<Dictionary<string, object>>(Constants.ItemsFile, "items");
}
