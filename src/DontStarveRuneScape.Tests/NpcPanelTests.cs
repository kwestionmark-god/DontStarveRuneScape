using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Interactions;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.UI;
using Silk.NET.Input;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

namespace DontStarveRuneScape.Tests;

public class NpcPanelTests
{
    // ─── Trade panel ───────────────────────────────────────────────────────

    [Fact]
    public void TradePanel_Buy_ExecutesSelectedRow()
    {
        var merchant = new MerchantNpc { NpcId = "m1", Biome = "forest", StartingGold = 100, PriceModifier = 1f };
        var (panel, system, inventory, skills) = MakeTrade(merchant);
        inventory.AddItem(TradeSystem.GoldItemId, 50);

        panel.Update(new InputState(), system, inventory, skills, 1280, 720);
        Assert.NotEmpty(panel.BuyRows);
        var first = panel.BuyRows[0];

        panel.HandleConfirm(); // BUY

        Assert.Equal(1, inventory.GetItemQuantity(first.ItemId));
        Assert.Equal(50 - first.BuyPrice, inventory.GetItemQuantity(TradeSystem.GoldItemId));
        Assert.Contains("Bought", panel.Status);
    }

    [Fact]
    public void TradePanel_Sell_ExecutesSelectedRow()
    {
        var merchant = new MerchantNpc { NpcId = "m1", Biome = "forest", StartingGold = 100 };
        var (panel, system, inventory, skills) = MakeTrade(merchant);

        panel.Update(new InputState(), system, inventory, skills, 1280, 720);
        Assert.NotEmpty(panel.SellRows);
        var sellable = panel.SellRows[0];
        inventory.AddItem(sellable.ItemId, 2);

        panel.SetTab("sell");
        panel.Update(new InputState(), system, inventory, skills, 1280, 720);
        panel.HandleConfirm(); // SELL

        Assert.Equal(1, inventory.GetItemQuantity(sellable.ItemId)); // sold 1 of 2
        Assert.Contains("Sold", panel.Status);
    }

    [Fact]
    public void TradePanel_Buy_GatesCommerce()
    {
        var merchant = new MerchantNpc { NpcId = "m1", Biome = "forest", StartingGold = 100 };
        var (panel, system, inventory, skills) = MakeTrade(merchant);
        skills.GetSkill("intelligence").SubStats["commerce"] = 0; // below any gated stock
        inventory.AddItem(TradeSystem.GoldItemId, 500);

        panel.Update(new InputState(), system, inventory, skills, 1280, 720);
        var gated = panel.BuyRows.FirstOrDefault(r => r.CommerceRequirement > 0);
        if (gated == null) return; // no gated stock in the data — nothing to assert

        int index = panel.BuyRows.IndexOf(gated);
        while (panel.SelectedIndex != index)
            panel.HandleKey(Key.Down);
        panel.HandleConfirm();

        Assert.Equal(0, inventory.GetItemQuantity(gated.ItemId));
        Assert.Contains("commerce", panel.Status);
    }

    // ─── Quest panel ───────────────────────────────────────────────────────

    [Fact]
    public void QuestPanel_AcceptAndClaim_Executes()
    {
        var system = new QuestSystem();
        system.Registry = new QuestRegistry();
        system.Registry.LoadAll();
        var npc = new QuestGiverNpc { NpcId = "q1", FactionId = "forest_villagers" };
        npc.AvailableQuests.Add("timber_collection");
        var panel = new QuestPanel();
        panel.OpenSession(npc);
        var player = new Player(0f, 0f) { SkillManager = new SkillManager() };
        var inventory = new Inv();

        panel.Update(new InputState(), system, inventory, player.SkillManager!, 1280, 720);
        Assert.NotEmpty(panel.Rows);
        Assert.Equal("timber_collection", panel.Rows[0].QuestId);

        panel.HandleConfirm(); // ACCEPT
        Assert.True(system.IsAccepted("timber_collection"));

        inventory.AddItem("oak_logs", 5); // complete the collect objective
        panel.Update(new InputState(), system, inventory, player.SkillManager!, 1280, 720);
        panel.HandleConfirm(); // CLAIM (objectives met)
        Assert.True(system.IsCompleted("timber_collection"));
    }

    [Fact]
    public void QuestPanel_Accept_GatesPrerequisites()
    {
        var system = new QuestSystem();
        system.Registry = new QuestRegistry();
        system.Registry.LoadAll();
        var npc = new QuestGiverNpc { NpcId = "q1", FactionId = "forest_villagers" };
        npc.AvailableQuests.Add("herb_gathering"); // requires intelligence 3, persuasion 2
        var panel = new QuestPanel();
        panel.OpenSession(npc);
        var player = new Player(0f, 0f) { SkillManager = new SkillManager() };
        var inventory = new Inv();

        panel.Update(new InputState(), system, inventory, player.SkillManager!, 1280, 720);
        panel.HandleConfirm(); // ACCEPT — gated out

        Assert.False(system.IsAccepted("herb_gathering"));
        Assert.Contains("Requires", panel.Status);
    }

    // ─── Recruit panel ─────────────────────────────────────────────────────

    [Fact]
    public void RecruitPanel_Recruit_ExecutesViaFlow()
    {
        var game = new Game();
        game.Player = MakePlayerWithAction();
        var flows = new NPCFlows(game);
        var panel = new RecruitPanel { OnAction = flows.HandleRecruitAction };
        var recruit = new RecruitNpc { NpcId = "r1", RecruitCommerce = 1, RecruitPersuasion = 1, RecruitComposite = 2 };
        recruit.AvailableBehaviors.Add("guard");
        recruit.AvailableBehaviors.Add("trader");
        panel.OpenSession(recruit);

        panel.Update(new InputState(), game.Player.SkillManager, 1280, 720);
        panel.HandleConfirm(); // RECRUIT "guard"

        Assert.Contains("r1", game.Player.RecruitedNpcs);
        Assert.False(panel.Visible);
    }

    [Fact]
    public void RecruitPanel_GateBlocksUnqualifiedPlayer()
    {
        var game = new Game();
        game.Player = MakePlayerWithAction();
        var flows = new NPCFlows(game);
        var panel = new RecruitPanel { OnAction = flows.HandleRecruitAction };
        var recruit = new RecruitNpc { NpcId = "r1", RecruitCommerce = 5, RecruitPersuasion = 5, RecruitComposite = 10 };
        recruit.AvailableBehaviors.Add("guard");
        panel.OpenSession(recruit);

        panel.Update(new InputState(), game.Player.SkillManager, 1280, 720);
        panel.HandleConfirm(); // RECRUIT — gated out

        Assert.DoesNotContain("r1", game.Player.RecruitedNpcs);
    }

    // ─── Diplomacy panel ───────────────────────────────────────────────────

    [Fact]
    public void DiplomacyPanel_Negotiate_RaisesStanding()
    {
        var game = new Game();
        game.Player = MakePlayerWithAction();
        game.FactionSystem = new FactionSystem();
        game.FactionRegistry = new FactionRegistry();
        game.FactionRegistry.LoadAll();
        var flows = new NPCFlows(game);
        var panel = new DiplomacyPanel { OnAction = flows.HandleDiplomacyAction };

        panel.OpenSession(new FactionLeaderNpc { NpcId = "l1", FactionId = "forest_villagers", Name = "Leader" });
        panel.Update(new InputState(), game.FactionRegistry, game.FactionSystem, 1280, 720);
        panel.HandleConfirm(); // NEGOTIATE

        Assert.True(game.FactionSystem.StandingOf("forest_villagers") > QuestSystem.DefaultStanding,
            "negotiating should raise the faction standing above neutral");
    }

    // ─── Dashboard ─────────────────────────────────────────────────────────

    [Fact]
    public void DashboardPanel_TabSelect_FiresCallback()
    {
        var panel = new DashboardPanel();
        string? selected = null;
        panel.OnTabSelected = tab => selected = tab;

        panel.SetActive("crafting");
        Assert.Equal("crafting", panel.ActiveTab);

        panel.HandleConfirm(); // open the active tab
        Assert.Equal("crafting", selected);

        panel.HandleKey(Key.Right); // next tab
        panel.HandleConfirm();
        int next = Array.IndexOf(panel.Tabs, "crafting") + 1;
        Assert.Equal(panel.Tabs[next], selected);
    }

    [Fact]
    public void DashboardKey_OpensDashboard()
    {
        var game = new Game();
        game.SetState(GameState.Playing);
        game.InputRouter = new InputRouter(game, new InteractSystem(game),
            new FireInteraction(game), new NPCFlows(game));

        game.InputRouter.Handle(Key.O);

        Assert.Equal(GameState.DashboardOpen, game.State);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static (TradePanel, TradeSystem, Inv, SkillManager) MakeTrade(MerchantNpc? merchant = null)
    {
        var panel = new TradePanel();
        var system = new TradeSystem
        {
            Registry = new TradeItemRegistry(),
        };
        system.Registry.LoadAll();
        var inventory = new Inv();
        var skills = new SkillManager();
        if (merchant != null)
            panel.OpenSession(merchant);
        return (panel, system, inventory, skills);
    }

    private static Player MakePlayerWithAction()
    {
        return new Player(0f, 0f)
        {
            SkillManager = new SkillManager(),
            ActionSystem = new Actions.ActionSystem(),
        };
    }
}
