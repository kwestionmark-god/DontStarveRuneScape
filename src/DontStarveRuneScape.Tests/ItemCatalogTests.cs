namespace DontStarveRuneScape.Tests;

using System.Text.Json;
using System.Collections.Generic;
using DontStarveRuneScape.Data;
using Xunit;

/// <summary>
/// ItemCatalog display parsing: name/sprite key/food stats per item id, from
/// raw items data rows and from the real items.json.
/// </summary>
public class ItemCatalogTests
{
    private static Dictionary<string, object> Row(params (string Key, object Value)[] fields)
    {
        var row = new Dictionary<string, object>();
        foreach (var (key, value) in fields)
            row[key] = JsonSerializer.SerializeToElement(value);
        return row;
    }

    [Fact]
    public void FromData_ParsesJsonElementRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            Row(("id", "berries"), ("name", "Berries"), ("sprite_key", "items/berries"),
                ("is_food", true), ("hunger_restore", 8), ("hp_restore", 0), ("spoilage_seconds", 900)),
        };
        var byId = ItemCatalog.FromData(data);
        var display = byId["berries"];
        Assert.Equal("Berries", display.Name);
        Assert.Equal("items/berries", display.SpriteKey);
        Assert.True(display.IsFood);
        Assert.Equal(8f, display.HungerRestore);
        Assert.Equal(900f, display.SpoilageSeconds);
    }

    [Fact]
    public void FromData_FallsBackNameAndSpriteToId()
    {
        var data = new List<Dictionary<string, object>> { Row(("id", "mystery_item")) };
        var byId = ItemCatalog.FromData(data);
        var display = byId["mystery_item"];
        Assert.Equal("mystery_item", display.Name);
        Assert.Equal("mystery_item", display.SpriteKey);
        Assert.False(display.IsFood);
    }

    [Fact]
    public void FromData_SkipsRowsWithoutId()
    {
        var data = new List<Dictionary<string, object>> { Row(("name", "No Id")) };
        Assert.Empty(ItemCatalog.FromData(data));
    }

    [Fact]
    public void Get_ReturnsRealItemsJsonData()
    {
        // Real file from the game's Data directory (copied into the test bin):
        // oak_logs must resolve to its display name and sprite key.
        var display = ItemCatalog.Get("oak_logs");
        Assert.NotNull(display);
        Assert.Equal("Oak Logs", display!.Name);
        Assert.Equal("items/logs_oak", display.SpriteKey);
    }

    [Fact]
    public void Get_UnknownIdReturnsNull()
    {
        Assert.Null(ItemCatalog.Get("not_a_real_item_id"));
    }
}
