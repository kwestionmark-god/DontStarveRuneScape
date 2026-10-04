namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Skills;

/// <summary>
/// TradeSystem — Buys and sells with merchants: per-merchant stock and gold
/// pools, prices scaled by the merchant's price modifier, premium stock gated
/// by the intelligence commerce sub-stat. Gold is the "gold" inventory item.
/// </summary>
public sealed class TradeSystem
{
    /// <summary>Trade items this system trades from; loaded at world boot.</summary>
    public TradeItemRegistry? Registry { get; set; }

    /// <summary>Quest progress hooks (wired at boot): buys count toward
    /// trade_at_location and collect_item objectives.</summary>
    public QuestSystem? Quests { get; set; }
    public FactionSystem? Factions { get; set; }

    /// <summary>Gold item id used as currency.</summary>
    public const string GoldItemId = "gold";

    // Runtime pools, keyed per merchant and initialized lazily from the def.
    private readonly Dictionary<(string NpcId, string TradeItemId), int> _stock = [];
    private readonly Dictionary<string, int> _merchantGold = [];

    public void Tick(float dt) { }

    /// <summary>Build the merchant's trade rows (buy tab), sorted by tier then
    /// item id. Stock is the runtime pool; prices carry the price modifier.</summary>
    public List<TradeItem> GetTradeItemsForMerchant(MerchantNpc merchant)
    {
        var items = new List<TradeItem>();
        if (Registry == null || string.IsNullOrEmpty(merchant.Biome)) return items;

        foreach (var def in Registry.GetTradeItemsForBiome(merchant.Biome)
                     .OrderBy(t => t.Tier).ThenBy(t => t.ItemId))
        {
            items.Add(new TradeItem
            {
                TradeItemId = def.TradeItemId,
                ItemId = def.ItemId,
                BuyPrice = BuyPriceFor(def, merchant),
                SellPrice = def.SellPrice,
                Stock = StockOf(merchant.NpcId, def),
                MaxStock = def.MaxStock,
                CommerceRequirement = def.CommerceRequirement,
            });
        }
        return items;
    }

    /// <summary>Buy price with the merchant's modifier applied (min 1).</summary>
    public int BuyPriceFor(TradeItemDef def, MerchantNpc merchant)
    {
        float standing = MerchantStanding(merchant);
        float diplomaticModifier = string.IsNullOrEmpty(merchant.FactionId)
            ? 1f : 1f + (QuestSystem.DefaultStanding - standing) * 0.5f;
        return Math.Max(1, (int)MathF.Round(def.BuyPrice * merchant.PriceModifier * diplomaticModifier));
    }

    /// <summary>Sell price for an inventory item from any matching trade def
    /// (items with no trade listing have no market price).</summary>
    public int SellPriceFor(string itemId, MerchantNpc? merchant = null)
    {
        int basePrice = Registry?.TradeItems.Values.FirstOrDefault(t => t.ItemId == itemId)?.SellPrice ?? 0;
        if (basePrice <= 0 || merchant == null || string.IsNullOrEmpty(merchant.FactionId)) return basePrice;
        float diplomaticModifier = 1f + (MerchantStanding(merchant) - QuestSystem.DefaultStanding) * 0.5f;
        return Math.Max(1, (int)MathF.Round(basePrice * diplomaticModifier));
    }

    /// <summary>The merchant's current gold pool.</summary>
    public int MerchantGoldOf(MerchantNpc merchant)
    {
        if (!_merchantGold.TryGetValue(merchant.NpcId, out var gold))
        {
            gold = merchant.StartingGold;
            _merchantGold[merchant.NpcId] = gold;
        }
        return gold;
    }

    /// <summary>Execute a buy action: commerce gate, stock gate, gold cost,
    /// all-or-nothing produce; the merchant's gold pool grows by the price.</summary>
    public TradeResult ExecuteBuy(string tradeItemId, int quantity, MerchantNpc? merchant,
        Inventory inventory, SkillManager skills)
    {
        var def = Registry?.GetTradeItem(tradeItemId);
        if (def == null || merchant == null || quantity <= 0)
            return new TradeResult { Success = false, Message = "Nothing to buy." };

        if (skills.GetEffectiveStat("intelligence", "commerce") < def.CommerceRequirement)
            return new TradeResult { Success = false, Message = $"Requires commerce {def.CommerceRequirement}." };

        int stock = StockOf(merchant.NpcId, def);
        if (stock < quantity)
            return new TradeResult { Success = false, Message = "Out of stock." };

        int price = BuyPriceFor(def, merchant) * quantity;
        if (inventory.GetItemQuantity(GoldItemId) < price)
            return new TradeResult { Success = false, Message = $"Not enough gold: needs {price}." };

        if (!inventory.CanAdd(def.ItemId, quantity))
            return new TradeResult { Success = false, Message = "Inventory is full." };

        inventory.RemoveItem(GoldItemId, price);
        inventory.AddItem(def.ItemId, quantity);
        _stock[(merchant.NpcId, def.TradeItemId)] = stock - quantity;
        _merchantGold[merchant.NpcId] = MerchantGoldOf(merchant) + price;
        Quests?.NotifyTrade(merchant.NpcId);
        Quests?.NotifyCollect(def.ItemId, quantity);
        return new TradeResult { Success = true, Message = $"Bought {quantity} x {def.ItemId} for {price} gold." };
    }

    /// <summary>Execute a sell action: converts inventory items into gold from
    /// the matching trade def's sell price; the merchant's pool shrinks.</summary>
    public TradeResult ExecuteSell(string itemId, int quantity, MerchantNpc? merchant, Inventory inventory)
    {
        if (quantity <= 0)
            return new TradeResult { Success = false, Message = "Nothing to sell." };

        int unit = SellPriceFor(itemId, merchant);
        if (unit <= 0)
            return new TradeResult { Success = false, Message = "No market for that item." };

        if (inventory.GetItemQuantity(itemId) < quantity)
            return new TradeResult { Success = false, Message = $"Not enough {itemId}." };

        int payout = unit * quantity;
        if (merchant != null && MerchantGoldOf(merchant) < payout)
            return new TradeResult { Success = false, Message = "The merchant is out of gold." };

        inventory.RemoveItem(itemId, quantity);
        inventory.AddItem(GoldItemId, payout);
        if (merchant != null)
            _merchantGold[merchant.NpcId] = MerchantGoldOf(merchant) - payout;
        return new TradeResult { Success = true, Message = $"Sold {quantity} x {itemId} for {payout} gold." };
    }

    private int StockOf(string npcId, TradeItemDef def)
    {
        var key = (npcId, def.TradeItemId);
        if (!_stock.TryGetValue(key, out var stock))
        {
            stock = def.StockQuantity;
            _stock[key] = stock;
        }
        return stock;
    }

    private float MerchantStanding(MerchantNpc merchant) =>
        string.IsNullOrEmpty(merchant.FactionId)
            ? QuestSystem.DefaultStanding
            : Math.Clamp(Factions?.StandingOf(merchant.FactionId) ?? QuestSystem.DefaultStanding, 0f, 1f);
}

/// <summary>
/// TradeItem — An item available for trade (runtime row for the panel).
/// </summary>
public sealed class TradeItem
{
    public string TradeItemId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
    public int Stock { get; set; }
    public int MaxStock { get; set; }
    public int CommerceRequirement { get; set; }
}

/// <summary>
/// TradeResult — Result of a trade action.
/// </summary>
public sealed class TradeResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
