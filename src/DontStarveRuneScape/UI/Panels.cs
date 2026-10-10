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
    private Inventory? _inventory;
    private SkillManager? _skills;
    public void HandleConfirm()
    {
        var inv = _inventory ?? Player?.Inventory;
        var skills = _skills ?? Player?.SkillManager;
        if (inv == null || skills == null || TradeSession == null) return;
        TradeResult result;
        if(Tab=="buy" && SelectedIndex<BuyRows.Count) result=TradeSystem.ExecuteBuy(BuyRows[SelectedIndex].TradeItemId,BuyQuantity,TradeSession,inv,skills);
        else if(Tab=="sell" && SelectedIndex<SellRows.Count) result=TradeSystem.ExecuteSell(SellRows[SelectedIndex].ItemId,SellQuantity,TradeSession,inv);
        else return;
        Status=result.Message;
    }
    public void Update(InputState input, TradeSystem system, Inventory inventory, SkillManager skills, int screenW, int screenH)
    {
        TradeSystem.Registry=system.Registry; TradeSystem.Quests=system.Quests; TradeSystem.Factions=system.Factions; _inventory=inventory; _skills=skills;
        BuyRows=TradeSession==null?[]:TradeSystem.GetTradeItemsForMerchant(TradeSession);
        SellRows=TradeSystem.Registry?.TradeItems.Values.Select(d=>(ItemId:d.ItemId, Quantity:inventory.GetItemQuantity(d.ItemId), SellPrice:TradeSystem.SellPriceFor(d.ItemId, TradeSession))).Where(r=>r.SellPrice>0).DistinctBy(r=>r.ItemId).ToList()??[];
        SelectedIndex=Math.Clamp(SelectedIndex,0,Math.Max(0,CurrentCount-1));
        var ui=new UiInput(input); float x=screenW/2f-325,y=screenH/2f-193;
        for(int i=0;i<2;i++) if(ui.TryClick(x+i*110,y+23,100,32)) SetTab(i==0?"buy":"sell");
        var rows=CurrentCount;

        // Mouse wheel scroll
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, rows - 7));
        }

        for(int i=0;i<Math.Min(7, rows - _scroll);i++) if(ui.TryClick(x,y+60+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)) { SelectedIndex=_scroll+i; HandleConfirm(); }
    }
    private int _scroll = 0;
    public List<(string itemId,int quantity,int sellPrice)> CollectSellableItems() => SellRows.Select(r=>(r.ItemId,r.Quantity,r.SellPrice)).ToList();
    public void Render(PrimitiveBatch b, TextRenderer? t,int w,int h)
    {
        NpcPanelLayout.Frame(b,t,w,h,"TRADE",out var x,out var y); if(t==null)return;
        NpcPanelLayout.Label(b,t,TradeSession?.Name??"Merchant",x,y+15,16,bold:true);
        NpcPanelLayout.Label(b,t,$"Gold: {Player?.Inventory?.GetItemQuantity(TradeSystem.GoldItemId)??0}    Merchant gold: {(TradeSession==null?0:TradeSystem.MerchantGoldOf(TradeSession))}",x+345,y+15);
        for(int i=0;i<2;i++){ string name=i==0?"BUY":"SELL"; float bx=x+i*110; b.DrawScreenQuad(bx+50,y+39,48,16,Tab==(i==0?"buy":"sell")?(byte)80:(byte)30,50,25); NpcPanelLayout.Label(b,t,name,bx+50,y+39,12); }
        var rows = CurrentCount;
        if(Tab=="buy") for(int i=0;i<Math.Min(7, BuyRows.Count - _scroll);i++){var r=BuyRows[_scroll+i]; NpcPanelLayout.Row(b,t,x,y+60+i*NpcPanelLayout.RowH,650,$"{r.ItemId}   stock {r.Stock}/{r.MaxStock}",r.CommerceRequirement>0?$"{r.BuyPrice}g · commerce {r.CommerceRequirement}":$"{r.BuyPrice}g",_scroll+i==SelectedIndex);}
        else for(int i=0;i<Math.Min(7, SellRows.Count - _scroll);i++){var r=SellRows[_scroll+i]; NpcPanelLayout.Row(b,t,x,y+60+i*NpcPanelLayout.RowH,650,$"{r.ItemId}   x{r.Quantity}",$"{r.SellPrice}g each",_scroll+i==SelectedIndex);}
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
    private bool _journalMode;
    private QuestSystem? _system;
    private Inventory? _inventory;
    private SkillManager? _skills;
    public void SetPlayer(Player p)=>Player=p;
    public bool OpenSession(Npc npc){Session=npc;_journalMode=false;Visible=true;SelectedIndex=0;Status="";return npc.AvailableQuests.Count>0;}
    public void OpenJournal(){Session=null;_journalMode=true;Visible=true;SelectedIndex=0;Status="";}
    public void Close(){Visible=false;Session=null;_journalMode=false;}
    public void HandleKey(Key key){if(key==Key.Up)SelectedIndex=Math.Max(0,SelectedIndex-1);else if(key==Key.Down)SelectedIndex=Math.Min(Math.Max(0,Rows.Count-1),SelectedIndex+1);}
    public void HandleConfirm()
    {
        var inv = _inventory ?? Player?.Inventory;
        var skills = _skills ?? Player?.SkillManager;
        if (inv == null || skills == null || (!_journalMode&&Session==null)||SelectedIndex>=Rows.Count)return;
        var player = Player ?? new Player(0f,0f){SkillManager=skills};
        var q=Rows[SelectedIndex]; QuestResult r;
        if(_journalMode)
        {
            if(_system?.IsCompleted(q.QuestId)==true){Status="That quest is already complete.";return;}
            if(_system?.IsAccepted(q.QuestId)!=true){Status="Quests must be accepted from an NPC.";return;}
            r=_system.ConditionsMet(q,inv)
                ? _system.Claim(player,Session??new QuestGiverNpc(),q.QuestId,inv,skills)
                : new QuestResult{Message="Objectives are still in progress."};
        }
        else if(_system?.IsAccepted(q.QuestId)==true) r=_system.Claim(player,Session!,q.QuestId,inv,skills);
        else r=_system?.AcceptQuest(player,Session!,q.QuestId)??new QuestResult{Message="Quest system unavailable."};
        Status=r.Message;
    }
    public void Update(InputState input, QuestSystem system, Inventory inventory, SkillManager skills,int w,int h)
    {
        _system=system; _inventory=inventory; _skills=skills;
        if(system.Registry==null) Rows=[];
        else if(_journalMode)
        {
            var ids=system.Active.Select(a=>a.QuestId).Concat(system.Completed).Distinct();
            Rows=ids.Select(system.Registry.GetQuest).Where(q=>q!=null).Cast<QuestDef>().ToList();
        }
        else Rows=Session==null?[]:Session.AvailableQuests.Select(system.Registry.GetQuest).Where(q=>q!=null).Cast<QuestDef>().ToList();
        SelectedIndex=Math.Clamp(SelectedIndex,0,Math.Max(0,Rows.Count-1));
        var ui=new UiInput(input);float x=w/2f-325,y=h/2f-193;

        // Mouse wheel scroll
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, Rows.Count - 7));
        }

        for(int i=0;i<Math.Min(7, Rows.Count - _scroll);i++)if(ui.TryClick(x,y+38+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)){SelectedIndex=_scroll+i;HandleConfirm();}
    }
    private int _scroll = 0;
    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h)
    {
        NpcPanelLayout.Frame(b,t,w,h,_journalMode?"QUEST JOURNAL":"QUESTS",out var x,out var y);if(t==null)return;
        NpcPanelLayout.Label(b,t,_journalMode?"Your active and completed quests":Session?.Name??"Quest giver",x,y+14,16,bold:true);
        for(int i=0;i<Math.Min(7, Rows.Count - _scroll);i++){var q=Rows[_scroll+i];var active=_system?.IsAccepted(q.QuestId)==true;var done=_system?.IsCompleted(q.QuestId)==true;var ready=active&&_system!.ConditionsMet(q,Player?.Inventory??new Inventory());var right=done?"COMPLETE":ready?"READY TO CLAIM":active?"TRACKING · in progress":"AVAILABLE";NpcPanelLayout.Row(b,t,x,y+38+i*NpcPanelLayout.RowH,650,q.Name,right,_scroll+i==SelectedIndex);}
        if(SelectedIndex<Rows.Count){var q=Rows[SelectedIndex];NpcPanelLayout.Label(b,t,q.Description,x,y+370,13);}
        NpcPanelLayout.Label(b,t,string.IsNullOrEmpty(Status)?(_journalMode?"↑/↓ select   Enter claim when ready   Esc close":"↑/↓ select   Enter accept / claim   Esc close"):Status,x+325,y+400,12,200,180,130);
    }
}

/// <summary>Recruitment choices with skill gates, routed through NPCFlows.</summary>
public sealed class RecruitPanel
{
    public bool Visible{get;set;} public Player? Player{get;set;} public RecruitNpc? Session{get;private set;}
    public int SelectedIndex{get;private set;} public string Status{get;private set;}="";
    public Action<(string ActionType,object[] Params)>? OnAction{get;set;}
    private SkillManager? _skills;
    public bool OpenSession(RecruitNpc npc){Session=npc;Visible=true;SelectedIndex=0;return true;}
    public void Close(){Visible=false;Session=null;}
    public void HandleKey(Key key){if(key==Key.Up)SelectedIndex=Math.Max(0,SelectedIndex-1);else if(key==Key.Down)SelectedIndex=Math.Min(Math.Max(0,(Session?.AvailableBehaviors.Count??0)-1),SelectedIndex+1);}
    public void HandleConfirm(){var skills=_skills??Player?.SkillManager;if(Session==null||skills==null||SelectedIndex>=Session.AvailableBehaviors.Count)return;var c=skills.GetEffectiveStat("intelligence","commerce");var p=skills.GetEffectiveStat("intelligence","persuasion");if(c<Session.RecruitCommerce||p<Session.RecruitPersuasion||c+p<Session.RecruitComposite){Status=$"Requires commerce {Session.RecruitCommerce}, persuasion {Session.RecruitPersuasion}, combined {Session.RecruitComposite}.";return;}OnAction?.Invoke(("recruit",[Session.NpcId,Session.AvailableBehaviors[SelectedIndex]]));Close();}
    public void Update(InputState input,SkillManager? skills,int w,int h){_skills=skills;var ui=new UiInput(input);float x=w/2f-325,y=h/2f-193;var count = Session?.AvailableBehaviors.Count ?? 0;

        // Mouse wheel scroll
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _scroll = Math.Clamp(_scroll - (int)scroll, 0, Math.Max(0, count - 7));
        }

        for(int i=0;i<Math.Min(7, count - _scroll);i++)if(ui.TryClick(x,y+42+i*NpcPanelLayout.RowH,650,NpcPanelLayout.RowH)){SelectedIndex=_scroll+i;HandleConfirm();}}
    private int _scroll = 0;

    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h){NpcPanelLayout.Frame(b,t,w,h,"RECRUIT",out var x,out var y);if(t==null)return;NpcPanelLayout.Label(b,t,$"{Session?.Name??"Recruit"} · commerce {Session?.RecruitCommerce} · persuasion {Session?.RecruitPersuasion}",x,y+15,15);var count = Session?.AvailableBehaviors.Count ?? 0;if(Session!=null)for(int i=0;i<Math.Min(7, count - _scroll);i++)NpcPanelLayout.Row(b,t,x,y+42+i*NpcPanelLayout.RowH,650,$"Serve as {Session.AvailableBehaviors[_scroll+i]}","RECRUIT",_scroll+i==SelectedIndex);NpcPanelLayout.Label(b,t,string.IsNullOrEmpty(Status)?"↑/↓ choose   Enter recruit   Esc close":Status,x+325,y+390,13,200,180,130);}
}

/// <summary>Faction standing and negotiation panel.</summary>
public sealed class DiplomacyPanel
{
    public bool Visible{get;set;} public Player? Player{get;set;} public FactionInfo? FactionInfo{get;set;}
    public FactionSystem? System{get;set;} public FactionRegistry? Registry{get;set;} public string Status{get;private set;}="";
    public Action<(string ActionType,object[] Params)>? OnAction{get;set;}
    public bool IsOverview{get;private set;} private int _selectedFaction;
    public bool OpenSession(FactionLeaderNpc npc){IsOverview=false;FactionInfo=new(){FactionId=npc.FactionId,Name=npc.Name};Visible=true;Status="";return true;}
    public void OpenOverview(){IsOverview=true;Visible=true;_selectedFaction=0;Status="";SelectOverviewFaction();}
    public void Close(){FactionInfo=null;Visible=false;IsOverview=false;}
    public void HandleKey(Key key)
    {
        if(!IsOverview||Registry==null)return;
        var factions=Registry.Factions.Values.OrderBy(f=>f.Name).ToList();
        if(factions.Count==0)return;
        if(key==Key.Up)_selectedFaction=(_selectedFaction+factions.Count-1)%factions.Count;
        else if(key==Key.Down)_selectedFaction=(_selectedFaction+1)%factions.Count;
        SelectOverviewFaction();
    }
    public void HandleConfirm(){if(FactionInfo==null)return;float before=System?.StandingOf(FactionInfo.FactionId)??QuestSystem.DefaultStanding;OnAction?.Invoke(("negotiate",[FactionInfo.FactionId]));float after=System?.StandingOf(FactionInfo.FactionId)??before;Status=$"Standing: {FactionSystem.TierName(after)} ({after:P0})";}
    public void Update(InputState input,FactionRegistry? registry,FactionSystem? system,int w,int h)
    {
        Registry=registry;System=system;
        if(!IsOverview||Registry==null)return;
        var factions=Registry.Factions.Values.OrderBy(f=>f.Name).ToList();
        if(factions.Count==0){FactionInfo=null;return;}
        _selectedFaction=Math.Clamp(_selectedFaction,0,factions.Count-1);SelectOverviewFaction();
        var ui=new UiInput(input);float x=w/2f-325f,y=h/2f-193f;

        // Mouse wheel scroll for faction list
        float scroll = ui.GetScroll();
        if (scroll != 0f)
        {
            _overviewScroll = Math.Clamp(_overviewScroll - (int)scroll, 0, Math.Max(0, factions.Count - 7));
        }

        for(int i=0;i<Math.Min(7, factions.Count - _overviewScroll);i++)
            if(ui.TryClick(x,y+38+i*NpcPanelLayout.RowH,235,NpcPanelLayout.RowH)){_selectedFaction=_overviewScroll+i;SelectOverviewFaction();return;}
        if(ui.TryClick(x+275,y+225,335,48))HandleConfirm();
    }
    private int _overviewScroll = 0;
    public void Render(PrimitiveBatch b,TextRenderer? t,int w,int h)
    {
        NpcPanelLayout.Frame(b,t,w,h,"DIPLOMACY",out var x,out var y);if(t==null)return;
        if(IsOverview)
        {
            var factions=Registry?.Factions.Values.OrderBy(f=>f.Name).ToList()??[];
            NpcPanelLayout.Label(b,t,"FACTIONS",x+115,y+14,14,bold:true);
            for(int i=0;i<Math.Min(7, factions.Count - _overviewScroll);i++)
            {
                var f=factions[_overviewScroll+i];var rowStanding=System?.StandingOf(f.FactionId)??QuestSystem.DefaultStanding;
                NpcPanelLayout.Row(b,t,x,y+38+i*NpcPanelLayout.RowH,235,f.Name,$"{rowStanding:P0}",_overviewScroll+i==_selectedFaction);
            }
            var selected=Registry?.GetFaction(FactionInfo?.FactionId??"");
            var id=FactionInfo?.FactionId??"";var value=System?.StandingOf(id)??QuestSystem.DefaultStanding;
            NpcPanelLayout.Label(b,t,selected?.Name??id,x+445,y+75,21,bold:true);
            NpcPanelLayout.Label(b,t,selected?.Description??"No faction information available.",x+445,y+125,14);
            NpcPanelLayout.Label(b,t,$"Standing: {FactionSystem.TierName(value)} ({value:P0})",x+445,y+175,16,PanelChrome.BorderR,PanelChrome.BorderG,PanelChrome.BorderB);
            b.DrawScreenQuad(x+445,y+249,165,24,68,48,24);NpcPanelLayout.Label(b,t,"NEGOTIATE",x+445,y+249,14,PanelChrome.BorderR,PanelChrome.BorderG,PanelChrome.BorderB,true);
            NpcPanelLayout.Label(b,t,Status.Length==0?"↑/↓ choose faction   Enter negotiate":Status,x+325,y+390,13,200,180,130);
            return;
        }
        var factionId=FactionInfo?.FactionId??"";var faction=Registry?.GetFaction(factionId);var standing=System?.StandingOf(factionId)??QuestSystem.DefaultStanding;
        NpcPanelLayout.Label(b,t,FactionInfo?.Name??faction?.Name??factionId,x,y+55,22,bold:true);
        NpcPanelLayout.Label(b,t,faction?.Description??$"Relations with {factionId}.",x,y+105,15);
        NpcPanelLayout.Label(b,t,$"Standing: {FactionSystem.TierName(standing)} ({standing:P0})",x,y+165,18,PanelChrome.BorderR,PanelChrome.BorderG,PanelChrome.BorderB);
        NpcPanelLayout.Label(b,t,"Press Enter to negotiate",x,y+245,16);
        NpcPanelLayout.Label(b,t,Status.Length==0?"Each negotiation improves standing. Esc closes.":Status,x+325,y+390,13,200,180,130);
    }
    private void SelectOverviewFaction()
    {
        if(!IsOverview||Registry==null)return;
        var factions=Registry.Factions.Values.OrderBy(f=>f.Name).ToList();
        FactionInfo=factions.Count==0?null:new FactionInfo{FactionId=factions[_selectedFaction].FactionId,Name=factions[_selectedFaction].Name};
    }
}

public sealed class FactionInfo { public string FactionId{get;set;}=""; public string Name{get;set;}=""; }

/// <summary>
/// Multi-role NPC menu — an NPC offering more than one interaction (quests,
/// trade, recruit, diplomacy) opens this tab strip FIRST instead of being
/// hard-routed into one of its panels (the old E-fork: a leader with quests
/// could never reach diplomacy). The dashboard's own idiom applied to NPCs:
/// the strip selects a role, and Game.OpenNpcHubTab launches the existing
/// role panel, so every panel stays the single source of its behaviour.
/// </summary>
public sealed class NpcHubPanel
{
    public const string QuestsTab = "quests";
    public const string TradeTab = "trade";
    public const string RecruitTab = "recruit";
    public const string DiplomacyTab = "diplomacy";

    public bool Visible { get; private set; }
    public Npc? Session { get; private set; }
    public List<string> Tabs { get; private set; } = [];
    public string ActiveTab { get; private set; } = string.Empty;
    public string Status { get; private set; } = "";

    /// <summary>Fired with the chosen tab id when the player confirms.</summary>
    public Action<string>? OnTabSelected { get; set; }

    /// <summary>The interactions an NPC actually offers, in menu order. THE
    /// single source of truth: the menu, the E-key routing and tests all
    /// read this.</summary>
    public static List<string> RolesFor(Npc npc)
    {
        var roles = new List<string>();
        if (npc.AvailableQuests.Count > 0) roles.Add(QuestsTab);
        if (npc is MerchantNpc) roles.Add(TradeTab);
        if (npc is RecruitNpc) roles.Add(RecruitTab);
        if (npc.NpcType == "faction_leader") roles.Add(DiplomacyTab);
        return roles;
    }

    public void OpenSession(Npc npc, IReadOnlyList<string> roles)
    {
        Session = npc;
        Tabs = [.. roles];
        ActiveTab = Tabs.Count > 0 ? Tabs[0] : string.Empty;
        Status = "";
        Visible = true;
    }

    public void SetActive(string tab) { if (Tabs.Contains(tab)) ActiveTab = tab; }

    public void HandleKey(Key key)
    {
        if (Tabs.Count == 0) return;
        int i = Math.Max(0, Tabs.IndexOf(ActiveTab));
        if (key is Key.Left or Key.Up) ActiveTab = Tabs[(i + Tabs.Count - 1) % Tabs.Count];
        else if (key is Key.Right or Key.Down) ActiveTab = Tabs[(i + 1) % Tabs.Count];
    }

    public void HandleConfirm()
    {
        if (ActiveTab.Length == 0) return;
        OnTabSelected?.Invoke(ActiveTab);
    }

    public void Close() { Visible = false; Session = null; Tabs = []; ActiveTab = string.Empty; }

    /// <summary>Display name for a role tab.</summary>
    public static string RoleLabel(string tab) => tab switch
    {
        QuestsTab => "Quests",
        TradeTab => "Trade",
        RecruitTab => "Recruit",
        DiplomacyTab => "Diplomacy",
        _ => tab,
    };

    public void Render(PrimitiveBatch b, TextRenderer? t, int w, int h)
    {
        NpcPanelLayout.Frame(b, t, w, h, "MENU", out var x, out var y);
        if (t == null) return;
        NpcPanelLayout.Label(b, t, Session?.Name ?? "NPC", x, y + 14, 16, bold: true);
        for (int i = 0; i < Tabs.Count; i++)
            NpcPanelLayout.Row(b, t, x, y + 38 + i * NpcPanelLayout.RowH, 650,
                RoleLabel(Tabs[i]), "OPEN", Tabs[i] == ActiveTab);
        NpcPanelLayout.Label(b, t, string.IsNullOrEmpty(Status)
            ? "↑/↓ choose   Enter open   Esc close" : Status, x + 325, y + 400, 12, 200, 180, 130);
    }
}
