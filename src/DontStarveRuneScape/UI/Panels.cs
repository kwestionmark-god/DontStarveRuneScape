namespace DontStarveRuneScape.UI;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.NPC;

/// <summary>
/// TradePanel — Trading UI panel.
/// </summary>
public sealed class TradePanel
{
    public bool Visible { get; set; } = false;
    public Player? Player { get; set; }
    public string Tab { get; set; } = "buy";
    public int SelectedMerchantIndex { get; set; } = -1;
    public int SelectedPlayerIndex { get; set; } = -1;
    public int BuyQuantity { get; set; } = 1;
    public int SellQuantity { get; set; } = 1;
    public MerchantNPC? TradeSession { get; set; }
    public TradeSystem TradeSystem { get; } = new();

    public bool OpenSession(MerchantNPC merchant)
    {
        TradeSession = merchant;
        Visible = true;
        return true;
    }

    public void Close()
    {
        TradeSession = null;
        Visible = false;
    }

    public List<(string itemId, int quantity, int sellPrice)> CollectSellableItems()
    {
        return new List<(string, int, int)>();
    }

    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// QuestPanel — Quest UI panel.
/// </summary>
public sealed class QuestPanel
{
    public bool Visible { get; set; } = false;
    public string? SelectedQuestId { get; set; }
    public Player? Player { get; set; }

    public void SetPlayer(Player player) => Player = player;

    public bool OpenSession(NPC npc)
    {
        Visible = true;
        return true;
    }

    public void Close() => Visible = false;

    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// RecruitPanel — NPC recruitment UI panel.
/// </summary>
public sealed class RecruitPanel
{
    public bool Visible { get; set; } = false;
    public Player? Player { get; set; }

    public bool OpenSession(RecruitNPC npc)
    {
        Visible = true;
        return true;
    }

    public void Close() => Visible = false;

    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// DiplomacyPanel — Faction diplomacy UI panel.
/// </summary>
public sealed class DiplomacyPanel
{
    public bool Visible { get; set; } = false;
    public Player? Player { get; set; }
    public FactionInfo? FactionInfo { get; set; }

    public bool OpenSession(FactionLeaderNPC npc)
    {
        FactionInfo = new FactionInfo { FactionId = npc.FactionId, Name = npc.Name };
        Visible = true;
        return true;
    }

    public void Close()
    {
        FactionInfo = null;
        Visible = false;
    }

    public void Render(GL gl, int screenWidth, int screenHeight) { }
}

/// <summary>
/// FactionInfo — Info about a faction for diplomacy panel.
/// </summary>
public sealed class FactionInfo
{
    public string FactionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}