namespace DontStarveRuneScape.UI;

using Silk.NET.Input;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Render;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Data;

/// <summary>Shared compact row-based layout for NPC interaction panels.</summary>
internal static class NpcPanelLayout
{
    public const float Width = 650, Height = 420, RowH = 46;
    public static void Frame(PrimitiveBatch batch, TextRenderer? text, int w, int h, string title,
        out float x, out float y) => PanelChrome.Draw(batch, text, w, h, title, Width, Height,
            out x, out y, out _, out _);
    public static void Label(PrimitiveBatch b, TextRenderer t, string s, float x, float y, int size = 14,
        byte r = PanelChrome.TextR, byte g = PanelChrome.TextG, byte blue = PanelChrome.TextB, bool bold = false)
    {
        var (tw, _) = t.Measure(s, size, bold); t.DrawText(b, s, x + tw / 2, y, size, r, g, blue, bold: bold);
    }
    public static void Row(PrimitiveBatch b, TextRenderer t, float x, float y, float w, string left, string right, bool selected)
    {
        b.DrawScreenQuad(x + w/2, y + RowH/2, w/2, RowH/2-2, selected ? (byte)82 : (byte)28, selected ? (byte)61 : (byte)19, selected ? (byte)35 : (byte)11);
        Label(b,t,left,x+12,y+RowH/2,14, selected ? PanelChrome.BorderR : PanelChrome.TextR, selected ? PanelChrome.BorderG : PanelChrome.TextG, selected ? PanelChrome.BorderB : PanelChrome.TextB);
        var (rw,_) = t.Measure(right,13); t.DrawText(b,right,x+w-rw/2-12,y+RowH/2,13,180,170,145);
    }
}

/// <summary>Merchant buy/sell panel. Up/down selects rows, left/right changes tabs, Enter trades one.</summary>
public sealed class TradePanel
{
    public bool Visible { get; set; }
    public Player? Player { get; set; }
    public string Tab { get; private set; } = "buy";
    public int SelectedIndex { get; private set; }
    public int SelectedMerchantIndex { get => SelectedIndex; set => SelectedIndex = Math.Max(0,value); }
    public int SelectedPlayerIndex { get => SelectedIndex; set => SelectedIndex = Math.Max(0,value); }
    public int BuyQuantity { get; set; } = 1;
    public int SellQuantity { get; set; } = 1;
    public MerchantNpc? TradeSession { get; private set; }
    public TradeSystem TradeSystem { get; } = new();
    public List<TradeItem> BuyRows { get; private set; } = [];
    public List<(string ItemId, int Quantity, int SellPrice)> SellRows { get; private set; } = [];
    public string Status { get; private set; } = "";
    public bool OpenSession(MerchantNpc merchant) { TradeSession=merchant; Visible=true; SelectedIndex=0; Status=""; return true; }
    public void Close() { TradeSession=null; Visible=false; }
    public void SetTab(string tab) { if (tab is "buy" or "sell") { Tab=tab; SelectedIndex=0; } }
    public void HandleKey(Key key)
    {
        if(key is Key.Left or Key.Right) SetTab(Tab=="buy"?"sell":"buy");
        else if(key==Key.Up) SelectedIndex=Math.Max(0,SelectedIndex-1);
        else if(key==Key.Down) SelectedIndex=Math.Min(Math.Max(0,CurrentCount-1),SelectedIndex+1);
    }
    private int CurrentCount => Tab=="buy"?BuyRows.Count:SellRows.Count;
    public void HandleConfirm()
    {
        if (Player?.Inventory == null || Player.SkillManager == null || TradeSession == null) return;
        TradeResult result;
        if(Tab=="buy" && SelectedIndex<BuyRows.Count) result=TradeSystem.ExecuteBuy(BuyRows[SelectedIndex].TradeItemId,BuyQuantity,TradeSession,Player.Inventory,Player.SkillManager);
        else if(Tab=="sell" && SelectedIndex<SellRows.Count) result=TradeSystem.ExecuteSell(SellRows[SelectedIndex].ItemId,SellQuantity,TradeSession,Player.Inventory);
        else return;
        Status=result.Message;
    }
    public void Update(InputState input, TradeSystem system, Inventory inventory, SkillManager skills, int screenW, int screenH)
    {
        TradeSystem.Registry=system.Registry; TradeSystem.Quests=system.Quests;
        BuyRows=TradeSession==null?[]:TradeSystem.GetTradeItemsForMerchant(TradeSession);
        SellRows=TradeSystem.Registry?.TradeItems.Values.Select(d=>(d.ItemId,inventory.GetItemQuantity(d.ItemId),d.SellPrice)).Where(r=>r.SellPrice>0).DistinctBy(r=>r.ItemId).ToList()??[];
        SelectedIndex=Math.Clamp(SelectedIndex,0,Math.Max(0,CurrentCount-1));
        var ui=new UiInput(input); float x=screenW/2f-325,y=screenH/2f-210;
        for(int i=0;i<2;i++) if(ui.TryClick(x+i*110,y,100,34)) SetTab(i==0?"buy":"sell");
        var rows=CurrentCount;
        for(int i=0;i<rows;i++) if(ui.TryClick(x,y+50+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)) { SelectedIndex=i; HandleConfirm(); }
    }
    public List<(string itemId,int quantity,int sellPrice)> CollectSellableItems() => SellRows.Select(r=>(r.ItemId,r.Quantity,r.SellPrice)).ToList();
    public void Render(PrimitiveBatch b, TextRenderer? t,int w,int h)
    {
        NpcPanelLayout.Frame(b,t,w,h,"TRADE",out var x,out var y); if(t==null)return;
        NpcPanelLayout.Label(b,t,TradeSession?.Name??"Merchant",x,y+15,16,bold:true);
        NpcPanelLayout.Label(b,t,$"Gold: {Player?.Inventory?.GetItemQuantity(TradeSystem.GoldItemId)??0}    Merchant gold: {(TradeSession==null?0:TradeSystem.MerchantGoldOf(TradeSession))}",x+345,y+15);
        for(int i=0;i<2;i++){ string name=i==0?"BUY":"SELL"; float bx=x+i*110; b.DrawScreenQuad(bx+50,y+39,48,16,Tab==(i==0?"buy":"sell")?(byte)80:(byte)30,50,25); NpcPanelLayout.Label(b,t,name,bx+50,y+39,12); }
        if(Tab=="buy") for(int i=0;i<BuyRows.Count && i<7;i++){var r=BuyRows[i]; NpcPanelLayout.Row(b,t,x,y+60+i*NpcPanelLayout.RowH,650,$"{r.ItemId}   stock {r.Stock}/{r.MaxStock}",r.CommerceRequirement>0?$"{r.BuyPrice}g · commerce {r.CommerceRequirement}":$"{r.BuyPrice}g",i==SelectedIndex);}
        else for(int i=0;i<SellRows.Count && i<7;i++){var r=SellRows[i]; NpcPanelLayout.Row(b,t,x,y+60+i*NpcPanelLayout.RowH,650,$"{r.ItemId}   x{r.Quantity}",$"{r.SellPrice}g each",i==SelectedIndex);}
        NpcPanelLayout.Label(b,t,string.IsNullOrEmpty(Status)?"←/→ tab   ↑/↓ select   Enter trade   Esc close":Status,x+325,y+390,13,200,180,130);
    }
}

/// <summary>Quest giver panel for accepting, tracking, and claiming available quests.</summary>
public sealed class QuestPanel
{
    public bool Visible { get; set; }
    public string? SelectedQuestId { get; set; }
    public Player? Player { get; set; }
    public Npc? Session { get; private set; }
    public int SelectedIndex { get; private set; }
    public string Status { get; private set; }="";
    public List<QuestDef> Rows { get; private set; }=[];
    public void SetPlayer(Player p)=>Player=p;
    public bool OpenSession(Npc npc){Session=npc;Visible=true;SelectedIndex=0;Status="";return npc.AvailableQuests.Count>0;}
    public void Close(){Visible=false;Session=null;}
    public void HandleKey(Key key){if(key==Key.Up)SelectedIndex=Math.Max(0,SelectedIndex-1);else if(key==Key.Down)SelectedIndex=Math.Min(Math.Max(0,Rows.Count-1),SelectedIndex+1);}
    public void HandleConfirm()
    {
        if(Player?.Inventory==null||Player.SkillManager==null||Session==null||SelectedIndex>=Rows.Count)return;
        var q=Rows[SelectedIndex]; QuestResult r;
        if(TradeQuestSystem?.IsAccepted(q.QuestId)==true) r=TradeQuestSystem.Claim(Player,Session,q.QuestId,Player.Inventory,Player.SkillManager);
        else r=TradeQuestSystem?.AcceptQuest(Player,Session,q.QuestId)??new QuestResult{Message="Quest system unavailable."};
        Status=r.Message;
    }
    private QuestSystem? TradeQuestSystem;
    public void Update(InputState input, QuestSystem system, Inventory inventory, SkillManager skills,int w,int h)
    {
        TradeQuestSystem=system;
        Rows=Session==null||system.Registry==null?[]:Session.AvailableQuests.Select(system.Registry.GetQuest).Where(q=>q!=null).Cast<QuestDef>().ToList();
        SelectedIndex=Math.Clamp(SelectedIndex,0,Math.Max(0,Rows.Count-1));
        var ui=new UiInput(input);float x=w/2f-325,y=h/2f-210;
        for(int i=0;i<Rows.Count;i++)if(ui.TryClick(x,y+45+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)){SelectedIndex=i;HandleConfirm();}
    }
    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h)
    {
        NpcPanelLayout.Frame(b,t,w,h,"QUESTS",out var x,out var y);if(t==null)return;
        NpcPanelLayout.Label(b,t,Session?.Name??"Quest giver",x,y+14,16,bold:true);
        for(int i=0;i<Rows.Count&&i<7;i++){var q=Rows[i];var active=TradeQuestSystem?.IsAccepted(q.QuestId)==true;var done=TradeQuestSystem?.IsCompleted(q.QuestId)==true;var right=done?"COMPLETE":active?"TRACKING · claim when ready":"AVAILABLE";NpcPanelLayout.Row(b,t,x,y+38+i*NpcPanelLayout.RowH,650,q.Name,right,i==SelectedIndex);}
        if(SelectedIndex<Rows.Count){var q=Rows[SelectedIndex];NpcPanelLayout.Label(b,t,q.Description,x,y+370,13);}
        NpcPanelLayout.Label(b,t,string.IsNullOrEmpty(Status)?"↑/↓ select   Enter accept / claim   Esc close":Status,x+325,y+400,12,200,180,130);
    }
}

/// <summary>Recruitment choices with skill gates, routed through NPCFlows.</summary>
public sealed class RecruitPanel
{
    public bool Visible{get;set;} public Player? Player{get;set;} public RecruitNpc? Session{get;private set;}
    public int SelectedIndex{get;private set;} public string Status{get;private set;}="";
    public Action<(string ActionType,object[] Params)>? OnAction{get;set;}
    public bool OpenSession(RecruitNpc npc){Session=npc;Visible=true;SelectedIndex=0;return true;}
    public void Close(){Visible=false;Session=null;}
    public void HandleKey(Key key){if(key==Key.Up)SelectedIndex=Math.Max(0,SelectedIndex-1);else if(key==Key.Down)SelectedIndex=Math.Min(Math.Max(0,(Session?.AvailableBehaviors.Count??0)-1),SelectedIndex+1);}
    public void HandleConfirm(){if(Session==null||Player?.SkillManager==null||SelectedIndex>=Session.AvailableBehaviors.Count)return;var c=Player.SkillManager.GetEffectiveStat("intelligence","commerce");var p=Player.SkillManager.GetEffectiveStat("intelligence","persuasion");if(c<Session.RecruitCommerce||p<Session.RecruitPersuasion||c+p<Session.RecruitComposite){Status=$"Requires commerce {Session.RecruitCommerce}, persuasion {Session.RecruitPersuasion}, combined {Session.RecruitComposite}.";return;}OnAction?.Invoke(("recruit",[Session.NpcId,Session.AvailableBehaviors[SelectedIndex]]));}
    public void Update(InputState input,SkillManager? skills,int w,int h){var ui=new UiInput(input);float x=w/2f-325,y=h/2f-210;for(int i=0;i<(Session?.AvailableBehaviors.Count??0);i++)if(ui.TryClick(x,y+45+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)){SelectedIndex=i;HandleConfirm();}}
    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h){NpcPanelLayout.Frame(b,t,w,h,"RECRUIT",out var x,out var y);if(t==null)return;NpcPanelLayout.Label(b,t,$"{Session?.Name??"Recruit"} · commerce {Session?.RecruitCommerce} · persuasion {Session?.RecruitPersuasion}",x,y+15,15);if(Session!=null)for(int i=0;i<Session.AvailableBehaviors.Count&&i<7;i++)NpcPanelLayout.Row(b,t,x,y+42+i*NpcPanelLayout.RowH,650,$"Serve as {Session.AvailableBehaviors[i]}","RECRUIT",i==SelectedIndex);NpcPanelLayout.Label(b,t,string.IsNullOrEmpty(Status)?"↑/↓ choose   Enter recruit   Esc close":Status,x+325,y+390,13,200,180,130);}
}

/// <summary>Faction standing and negotiation panel.</summary>
public sealed class DiplomacyPanel
{
    public bool Visible{get;set;} public Player? Player{get;set;} public FactionInfo? FactionInfo{get;set;}
    public FactionSystem? System{get;set;} public FactionRegistry? Registry{get;set;} public string Status{get;private set;}="";
    public Action<(string ActionType,object[] Params)>? OnAction{get;set;}
    public bool OpenSession(FactionLeaderNpc npc){FactionInfo=new(){FactionId=npc.FactionId,Name=npc.Name};Visible=true;return true;}
    public void Close(){FactionInfo=null;Visible=false;}
    public void HandleConfirm(){if(FactionInfo==null)return;float before=System?.StandingOf(FactionInfo.FactionId)??QuestSystem.DefaultStanding;OnAction?.Invoke(("negotiate",[]));float after=System?.StandingOf(FactionInfo.FactionId)??before;Status=$"Standing: {FactionSystem.TierName(after)} ({after:P0})";}
    public void Update(InputState input,FactionRegistry? registry,FactionSystem? system,int w,int h){Registry=registry;System=system;}
    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h){NpcPanelLayout.Frame(b,t,w,h,"DIPLOMACY",out var x,out var y);if(t==null)return;var id=FactionInfo?.FactionId??"";var def=Registry?.GetFaction(id);var standing=System?.StandingOf(id)??QuestSystem.DefaultStanding;NpcPanelLayout.Label(b,t,FactionInfo?.Name??def?.Name??id,x,y+55,22,bold:true);NpcPanelLayout.Label(b,t,def?.Description??$"Relations with {id}.",x,y+105,15);NpcPanelLayout.Label(b,t,$"Standing: {FactionSystem.TierName(standing)} ({standing:P0})",x,y+165,18,PanelChrome.BorderR,PanelChrome.BorderG,PanelChrome.BorderB);NpcPanelLayout.Label(b,t,"Press Enter to negotiate",x,y+245,16);NpcPanelLayout.Label(b,t,Status.Length==0?"Each negotiation improves standing. Esc closes.":Status,x+325,y+390,13,200,180,130);}
}

public sealed class FactionInfo { public string FactionId{get;set;}=""; public string Name{get;set;}=""; }
