namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Inventory;

/// <summary>
/// TradeSystem — Handles trading with merchants.
/// </summary>
public sealed class TradeSystem
{
    public void Tick(float dt) { }

    /// <summary>Get trade items for a merchant.</summary>
    public List<TradeItem> GetTradeItemsForMerchant(MerchantNPC merchant)
    {
        return new List<TradeItem>();
    }

    /// <summary>Execute a buy action.</summary>
    public TradeResult ExecuteBuy(string tradeItemId, int quantity)
    {
        return new TradeResult { Success = true, Message = $"Bought {quantity} {tradeItemId}." };
    }

    /// <summary>Execute a sell action.</summary>
    public TradeResult ExecuteSell(string itemId, int quantity)
    {
        return new TradeResult { Success = true, Message = $"Sold {quantity} {itemId}." };
    }

    /// <summary>Execute a barter action.</summary>
    public TradeResult ExecuteBarter(string playerItemId, int playerQty, string merchantTradeItemId)
    {
        return new TradeResult { Success = true, Message = $"Bartered {playerQty} {playerItemId} for {merchantTradeItemId}." };
    }
}

/// <summary>
/// TradeItem — An item available for trade.
/// </summary>
public sealed class TradeItem
{
    public string TradeItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
    public int Stock { get; set; }
    public int MaxStock { get; set; }
}

/// <summary>
/// TradeResult — Result of a trade action.
/// </summary>
public sealed class TradeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}