namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;
using DontStarveRuneScape.Config;

/// <summary>
/// Trade item definition for merchants (trade_items.json). Items are keyed by
/// biome — merchants map to items via their spawn-point biome or faction
/// territory.
/// </summary>
public sealed class TradeItemDef : DataRecord
{
    [JsonPropertyName("trade_item_id")]
    public string TradeItemId { get; init; } = string.Empty;

    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty; // Links to items.json

    [JsonPropertyName("biome")]
    public string Biome { get; init; } = string.Empty;

    [JsonPropertyName("buy_price")]
    public int BuyPrice { get; init; } = 0;

    [JsonPropertyName("sell_price")]
    public int SellPrice { get; init; } = 0;

    [JsonPropertyName("stock_quantity")]
    public int StockQuantity { get; init; } = 0;

    [JsonPropertyName("max_stock")]
    public int MaxStock { get; init; } = 0;

    [JsonPropertyName("tier")]
    public int Tier { get; init; } = 1;

    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; init; } = false;

    [JsonPropertyName("commerce_requirement")]
    public int CommerceRequirement { get; init; } = 0; // Intelligence commerce sub-stat required
}

/// <summary>
/// Registry of trade items, loaded from trade_items.json.
/// </summary>
public sealed class TradeItemRegistry
{
    public Dictionary<string, TradeItemDef> TradeItems { get; } = [];

    public void LoadAll()
    {
        try
        {
            var items = DataLoader.LoadJsonList<TradeItemDef>(Constants.TradeItemsFile, "trade_items");
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item.TradeItemId))
                    TradeItems[item.TradeItemId] = item;
            }
        }
        catch { /* missing file: empty registry */ }
    }

    public TradeItemDef? GetTradeItem(string id)
    {
        return TradeItems.TryGetValue(id, out var t) ? t : null;
    }

    public IEnumerable<TradeItemDef> GetTradeItemsForBiome(string biomeId) =>
        TradeItems.Values.Where(t => t.Biome == biomeId);
}
