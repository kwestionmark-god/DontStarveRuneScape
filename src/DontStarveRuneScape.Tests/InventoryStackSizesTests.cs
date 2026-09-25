namespace DontStarveRuneScape.Tests;

using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using Xunit;

/// <summary>
/// Data-driven stack sizes: items.json stack_size overrides the built-in
/// defaults, with the defaults kept as fallback for unknown ids.
/// </summary>
public class InventoryStackSizesTests
{
    private static Dictionary<string, object> Row(string id, int stackSize) => new()
    {
        ["id"] = JsonSerializer.SerializeToElement(id),
        ["stack_size"] = JsonSerializer.SerializeToElement(stackSize),
    };

    [Fact]
    public void StackSizesFromData_ParsesJsonElementRows()
    {
        var data = new List<Dictionary<string, object>> { Row("oak_logs", 28), Row("berries", 20) };
        var sizes = Inventory.StackSizesFromData(data);
        Assert.Equal(28, sizes["oak_logs"]);
        Assert.Equal(20, sizes["berries"]);
    }

    [Fact]
    public void StackSizesFromData_ParsesPlainPrimitiveRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            new() { ["id"] = "oak_logs", ["stack_size"] = 28 },
        };
        var sizes = Inventory.StackSizesFromData(data);
        Assert.Equal(28, sizes["oak_logs"]);
    }

    [Fact]
    public void StackSizesFromData_SkipsInvalidRows()
    {
        var data = new List<Dictionary<string, object>>
        {
            new() { ["stack_size"] = JsonSerializer.SerializeToElement(28) },
            new() { ["id"] = JsonSerializer.SerializeToElement("no_size") },
            Row("bad_size", 0),
        };
        var sizes = Inventory.StackSizesFromData(data);
        Assert.Empty(sizes);
    }

    [Fact]
    public void GetStackSize_UsesDataOverride_WhenSet()
    {
        var inv = new Inventory();
        inv.StackSizes = new Dictionary<string, int> { ["oak_logs"] = 28 };
        Assert.Equal(28, inv.StackSizeOf("oak_logs"));
    }

    [Fact]
    public void GetStackSize_FallsBackToDefaults_WithoutOverride()
    {
        var inv = new Inventory();
        Assert.Equal(20, inv.StackSizeOf("oak_logs"));
        Assert.Equal(10, inv.StackSizeOf("raw_meat"));
    }

    [Fact]
    public void AddItem_StacksToDataOverride()
    {
        var inv = new Inventory();
        inv.StackSizes = new Dictionary<string, int> { ["oak_logs"] = 28 };
        Assert.True(inv.AddItem("oak_logs", 28));
        Assert.Equal(28, inv.GetItemQuantity("oak_logs"));
        Assert.Equal(1, inv.Slots.Count(s => s.ItemId == "oak_logs"));
    }

    [Fact]
    public void StackSizesFromData_ReadsRealItemsJson()
    {
        // Pins the real deserialization shape (DataLoader -> Dictionary<string, object>)
        // through the parser: oak_logs must pick up stack_size 28 from items.json.
        var items = DataLoader.LoadJsonList<Dictionary<string, object>>(Constants.ItemsFile, "items");
        var sizes = Inventory.StackSizesFromData(items);
        Assert.Equal(28, sizes["oak_logs"]);
    }
}
