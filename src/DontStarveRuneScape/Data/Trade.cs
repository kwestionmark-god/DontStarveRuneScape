namespace DontStarveRuneScape.Data;

using System.Text.Json.Serialization;

/// <summary>
/// Trade item definition for merchants.
/// </summary>
public sealed class TradeItemDef : DataRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("buy_price")]
    public int BuyPrice { get; init; } = 0;

    [JsonPropertyName("sell_price")]
    public int SellPrice { get; init; } = 0;

    [JsonPropertyName("stock")]
    public int Stock { get; init; } = 0; // -1 = unlimited

    [JsonPropertyName("tier")]
    public int Tier { get; init; } = 1;

    [JsonPropertyName("commerce_req")]
    public int CommerceReq { get; init; } = 0; // Intelligence commerce sub-stat required

    [JsonPropertyName("item_id")]
    public string ItemId { get; init; } = string.Empty; // Links to items.json

    [JsonPropertyName("faction")]
    public string Faction { get; init; } = string.Empty; // Faction that sells this
}

/// <summary>
/// Registry of trade items.
/// </summary>
public sealed class TradeItemRegistry
{
    public Dictionary<string, TradeItemDef> TradeItems { get; } = [];

    public TradeItemRegistry() { }

    public TradeItemRegistry(IEnumerable<TradeItemDef> items)
    {
        foreach (var t in items)
            TradeItems[t.Id] = t;
    }

    public TradeItemDef? GetTradeItem(string id)
    {
        return TradeItems.TryGetValue(id, out var t) ? t : null;
    }

    public IEnumerable<TradeItemDef> GetTradeItemsForFaction(string factionId)
    {
        return TradeItems.Values.Where(t => t.Faction == factionId);
    }
}