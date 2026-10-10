namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Silk.NET.Input;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// NPC-quest breadth: the multi-role NPC role menu (the leader E-fork fix),
/// the plains quest hub, and the two faction leaders whose boards were empty.
/// Most of this is DATA-driven and fails at RED until npcs.json / quests.json
/// carry it; the role-menu routing test fails because E still forks straight
/// into the quest panel.
/// </summary>
public class NpcQuestBreadthTests
{
    // ─── Role menu: the single source of an NPC's interactions ─────────

    [Fact]
    public void RolesFor_LeaderWithQuests_OffersQuestsThenDiplomacy()
    {
        var leader = new FactionLeaderNpc { NpcId = "l", NpcType = "faction_leader", FactionId = "goblins" };
        leader.AvailableQuests.Add("goblin_diplomacy");

        Assert.Equal(new List<string> { "quests", "diplomacy" }, NpcHubPanel.RolesFor(leader));
    }

    [Fact]
    public void RolesFor_LeaderWithoutQuests_OffersDiplomacyOnly()
    {
        var leader = new FactionLeaderNpc { NpcId = "l", NpcType = "faction_leader", FactionId = "mountain_clans" };

        Assert.Equal(new List<string> { "diplomacy" }, NpcHubPanel.RolesFor(leader));
    }

    [Fact]
    public void RolesFor_SingleRoleNpcs_KeepTheirOnePanel()
    {
        var giver = new QuestGiverNpc { NpcId = "q", NpcType = "quest_giver" };
        giver.AvailableQuests.Add("timber_collection");

        Assert.Equal(new List<string> { "quests" }, NpcHubPanel.RolesFor(giver));
        Assert.Equal(new List<string> { "trade" },
            NpcHubPanel.RolesFor(new MerchantNpc { NpcId = "m", NpcType = "merchant" }));
        Assert.Equal(new List<string> { "recruit" },
            NpcHubPanel.RolesFor(new RecruitNpc { NpcId = "r", NpcType = "recruit" }));
    }

    [Fact]
    public void RolesFor_RealRegistry_GivesLeadersTheirMenu()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        // Grak always had quests AND negotiation — the E-fork case.
        Assert.Equal(new List<string> { "quests", "diplomacy" },
            NpcHubPanel.RolesFor(Npc.FromDef(registry.GetNpc("goblin_chief_grak")!)));
        // elder_mara gains her first quest in this slice → she becomes multi-role.
        Assert.Equal(new List<string> { "quests", "diplomacy" },
            NpcHubPanel.RolesFor(Npc.FromDef(registry.GetNpc("elder_mara")!)));
        // A plains merchant stays a single-role trade NPC.
        Assert.Equal(new List<string> { "trade" },
            NpcHubPanel.RolesFor(Npc.FromDef(registry.GetNpc("merchant_plains_1")!)));
    }

    [Fact]
    public void Hub_TabCycle_AndConfirm_FiresSelection()
    {
        var leader = new FactionLeaderNpc { NpcId = "l", NpcType = "faction_leader", FactionId = "goblins" };
        leader.AvailableQuests.Add("goblin_diplomacy");
        var hub = new NpcHubPanel();
        string? chosen = null;
        hub.OnTabSelected = tab => chosen = tab;

        hub.OpenSession(leader, NpcHubPanel.RolesFor(leader));
        Assert.True(hub.Visible);
        Assert.Equal("quests", hub.ActiveTab);

        hub.HandleKey(Key.Right);
        Assert.Equal("diplomacy", hub.ActiveTab);
        hub.HandleConfirm();
        Assert.Equal("diplomacy", chosen);

        hub.HandleKey(Key.Right); // wraps back to the first tab
        Assert.Equal("quests", hub.ActiveTab);

        hub.Close();
        Assert.False(hub.Visible);
    }

    // ─── E-key routing: the fork is gone ───────────────────────────────

    [Fact]
    public void E_OnQuestBearingLeader_OpensTheRoleMenu()
    {
        var game = new Game();
        var player = new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            ActionSystem = new ActionSystem(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
        };
        game.Player = player;
        game.World = new TileMap(24, 24);
        game.Inventory = player.Inventory;
        game.SkillManager = player.SkillManager;
        game.NPCSystem = new NPCSystem();
        game.QuestPanel = new QuestPanel();
        game.DiplomacyPanel = new DiplomacyPanel();
        game.NpcHub = new NpcHubPanel();
        game.SetState(GameState.Playing);

        var leader = new FactionLeaderNpc
        {
            NpcId = "grak",
            NpcType = "faction_leader",
            FactionId = "goblins",
            WorldX = player.WorldX + 20f,
            WorldY = player.WorldY,
        };
        leader.AvailableQuests.Add("goblin_diplomacy");
        game.NPCSystem.NPCs.Add(leader);

        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));
        interact.HandleInteract();

        // Before: this forked into the quest panel, so a leader's diplomacy
        // (and the negotiation its own quest demands) was unreachable by E.
        Assert.Equal(GameState.NpcHub, game.State);
        Assert.True(game.NpcHub!.Visible);
        Assert.Equal(leader.NpcId, game.NpcHub.Session?.NpcId);
    }

    [Fact]
    public void E_OnPlainQuestGiver_StillOpensTheQuestPanel()
    {
        var game = new Game();
        var player = new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            ActionSystem = new ActionSystem(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
        };
        game.Player = player;
        game.World = new TileMap(24, 24);
        game.Inventory = player.Inventory;
        game.SkillManager = player.SkillManager;
        game.NPCSystem = new NPCSystem();
        game.QuestPanel = new QuestPanel();
        game.DiplomacyPanel = new DiplomacyPanel();
        game.NpcHub = new NpcHubPanel();
        game.SetState(GameState.Playing);

        var giver = new QuestGiverNpc
        {
            NpcId = "hemlock",
            NpcType = "quest_giver",
            FactionId = "forest_villagers",
            WorldX = player.WorldX + 20f,
            WorldY = player.WorldY,
        };
        giver.AvailableQuests.Add("timber_collection");
        game.NPCSystem.NPCs.Add(giver);

        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));
        interact.HandleInteract();

        Assert.Equal(GameState.QuestPanel, game.State);
    }

    [Fact]
    public void E_OnQuestlessLeader_StillOpensDiplomacy()
    {
        var game = new Game();
        var player = new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            ActionSystem = new ActionSystem(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
        };
        game.Player = player;
        game.World = new TileMap(24, 24);
        game.Inventory = player.Inventory;
        game.SkillManager = player.SkillManager;
        game.NPCSystem = new NPCSystem();
        game.QuestPanel = new QuestPanel();
        game.DiplomacyPanel = new DiplomacyPanel();
        game.NpcHub = new NpcHubPanel();
        game.SetState(GameState.Playing);

        var leader = new FactionLeaderNpc
        {
            NpcId = "kree",
            NpcType = "faction_leader",
            FactionId = "swamp_factions",
            WorldX = player.WorldX + 20f,
            WorldY = player.WorldY,
        };
        game.NPCSystem.NPCs.Add(leader);

        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));
        interact.HandleInteract();

        Assert.Equal(GameState.DiplomacyPanel, game.State);
    }

    // ─── Quest breadth: offered, real skills, playable chain ───────────

    [Fact]
    public void EveryQuest_IsOfferedBySomeNpc()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();

        var offered = npcs.Npcs.Values.SelectMany(n => n.AvailableQuestIds).ToHashSet();
        var orphaned = quests.Quests.Keys.Where(id => !offered.Contains(id)).ToArray();

        Assert.True(orphaned.Length == 0,
            $"quests nobody offers: {string.Join(", ", orphaned)}");
    }

    [Fact]
    public void EveryQuestSkillReward_TargetsARegisteredSkill()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var registered = new SkillManager().GetSnapshot().Skills.Keys.ToHashSet();

        var ghosts = quests.Quests.Values
            .SelectMany(q => q.SkillXpRewards.Keys.Select(skill => $"{q.QuestId}:{skill}"))
            .Where(pair => !registered.Contains(pair.Split(':')[1]))
            .ToArray();

        Assert.True(ghosts.Length == 0,
            $"skill XP awarded to unregistered skills: {string.Join(", ", ghosts)}");
    }

    [Fact]
    public void PlainsChain_AcceptsGated_Progresses_AndDelivers()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var giverDef = npcs.GetNpc("quest_giver_plains_1");
        Assert.NotNull(giverDef);
        var hayward = Npc.FromDef(giverDef!);

        var system = new QuestSystem { Registry = quests };
        var skills = new SkillManager();
        var inv = new Inv();
        var player = new Player(0f, 0f) { SkillManager = skills, Inventory = inv };

        // Link 1: no prerequisites, gathering.
        var first = system.AcceptQuest(player, hayward, "plains_harvest");
        Assert.True(first.Success, first.Message);
        inv.AddItem("wheat", 6);
        Assert.True(system.Claim(player, hayward, "plains_harvest", inv, skills).Success);
        Assert.True(system.IsCompleted("plains_harvest"));

        // Link 2: chained, kill-bumped.
        var second = system.AcceptQuest(player, hayward, "boar_hunt");
        Assert.True(second.Success, second.Message);
        system.NotifyKill("boar");
        system.NotifyKill("boar");
        Assert.True(system.Claim(player, hayward, "boar_hunt", inv, skills).Success);

        // Link 3: delivers the hunt to the plains merchant. The chain's XP
        // is the intelligence source, so the gate is reachable by playing.
        if (skills.GetSkillLevel("intelligence") < 2)
            skills.AddXpWithNotification("intelligence", SkillManager.XpForLevel(2) + 1f);
        inv.AddItem("boar_meat", 2);
        var third = system.AcceptQuest(player, hayward, "plains_provender");
        Assert.True(third.Success, third.Message);
        system.NotifyTrade("merchant_plains_1");
        Assert.True(system.Claim(player, hayward, "plains_provender", inv, skills).Success);

        Assert.Equal(0, inv.GetItemQuantity("boar_meat")); // the delivery consumed it
    }

    [Fact]
    public void PlainsChain_SecondLinkRefusesBeforeTheFirst()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var plainsDef = npcs.GetNpc("quest_giver_plains_1");
        Assert.NotNull(plainsDef);
        var giver = Npc.FromDef(plainsDef!);

        var system = new QuestSystem { Registry = quests };
        var player = new Player(0f, 0f) { SkillManager = new SkillManager(), Inventory = new Inv() };

        var refused = system.AcceptQuest(player, giver, "boar_hunt");

        Assert.False(refused.Success);
        Assert.Contains("Requires", refused.Message);
    }

    [Fact]
    public void LeaderGiver_QuestChainsOffItsPrerequisite()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var mara = Npc.FromDef(npcs.GetNpc("elder_mara")!);

        var system = new QuestSystem { Registry = quests };
        var skills = new SkillManager();
        var inv = new Inv();
        var player = new Player(0f, 0f) { SkillManager = skills, Inventory = inv };

        // tribal clinic relief rides the forest giver's chain.
        var refused = system.AcceptQuest(player, mara, "herbal_remedy");
        Assert.False(refused.Success);
        Assert.Contains("Requires quest", refused.Message);

        var timberNpc = new QuestGiverNpc { NpcId = "hemlock", NpcType = "quest_giver" };
        timberNpc.AvailableQuests.Add("timber_collection");
        var timber = system.AcceptQuest(player, timberNpc, "timber_collection");
        Assert.True(timber.Success, timber.Message);
        inv.AddItem("oak_logs", 5);
        Assert.True(system.Claim(player, timberNpc, "timber_collection", inv, skills).Success);

        if (skills.GetSkillLevel("intelligence") < 2)
            skills.AddXpWithNotification("intelligence", SkillManager.XpForLevel(2) + 1f);
        var accepted = system.AcceptQuest(player, mara, "herbal_remedy");
        Assert.True(accepted.Success, accepted.Message);
    }

    // ─── Hub tabs launch the role panels ───────────────────────────────

    [Fact]
    public void HubTab_LaunchesTheRolePanel()
    {
        var leader = new FactionLeaderNpc
        {
            NpcId = "grak",
            NpcType = "faction_leader",
            FactionId = "goblins",
            Name = "Goblin Chief Grak",
        };
        leader.AvailableQuests.Add("goblin_diplomacy");
        var game = HubGame(leader);

        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));
        interact.HandleInteract();
        Assert.Equal(GameState.NpcHub, game.State);

        // Diplomacy tab: the surface the old fork made unreachable by E.
        game.OpenNpcHubTab(NpcHubPanel.DiplomacyTab);
        Assert.Equal(GameState.DiplomacyPanel, game.State);
        Assert.Equal("goblins", game.DiplomacyPanel!.FactionInfo?.FactionId);

        // E again reopens the menu — and the quests tab reaches the same
        // NPC's board (the handoff closes the menu, as any panel move does).
        interact.HandleInteract();
        Assert.Equal(GameState.NpcHub, game.State);
        game.OpenNpcHubTab(NpcHubPanel.QuestsTab);
        Assert.Equal(GameState.QuestPanel, game.State);
    }

    [Fact]
    public void HubDiplomacyTab_NegotiateRaisesStanding()
    {
        var leader = new FactionLeaderNpc
        {
            NpcId = "grak",
            NpcType = "faction_leader",
            FactionId = "goblins",
            Name = "Grak",
        };
        leader.AvailableQuests.Add("goblin_diplomacy");
        var game = HubGame(leader);
        game.FactionSystem = new FactionSystem();
        game.FactionRegistry = new FactionRegistry();
        game.FactionRegistry.LoadAll();
        var flows = new Interactions.NPCFlows(game);
        game.DiplomacyPanel!.OnAction = flows.HandleDiplomacyAction; // Bootstrap's wiring

        var interact = new Interactions.InteractSystem(game, flows);
        interact.HandleInteract();
        game.OpenNpcHubTab(NpcHubPanel.DiplomacyTab);

        float before = game.FactionSystem.StandingOf("goblins");
        game.DiplomacyPanel.HandleConfirm(); // NEGOTIATE

        Assert.True(game.FactionSystem.StandingOf("goblins") > before,
            "negotiating from the hub's diplomacy tab must raise standing");
    }

    // ─── Helpers ───────────────────────────────────────────────────────

    /// <summary>Bare-Game harness with the NPC staged in reach of the player
    /// (PauseFlowTests shape; the game-level Inventory/SkillManager are the
    /// ones HandleInteract's null-guard reads).</summary>
    private static Game HubGame(Npc npc)
    {
        var game = new Game();
        var player = new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            ActionSystem = new ActionSystem(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
        };
        game.Player = player;
        game.World = new TileMap(24, 24);
        game.Inventory = player.Inventory;
        game.SkillManager = player.SkillManager;
        game.NPCSystem = new NPCSystem();
        game.QuestPanel = new QuestPanel();
        game.DiplomacyPanel = new DiplomacyPanel();
        game.NpcHub = new NpcHubPanel();
        game.SetState(GameState.Playing);

        npc.WorldX = player.WorldX + 20f;
        npc.WorldY = player.WorldY;
        game.NPCSystem.NPCs.Add(npc);
        return game;
    }
}
