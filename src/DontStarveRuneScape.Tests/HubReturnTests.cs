namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Silk.NET.Input;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Hub-tab return: Esc from a role panel handed off by the NPC hub returns
/// to the hub menu (session + active tab restored); panels opened directly
/// still close to Playing. Spec:
/// docs/superpowers/specs/2026-10-11-hub-tab-return-design.md
/// </summary>
public class HubReturnTests
{
    /// <summary>Bare Game staged like NpcQuestBreadthTests' E-tests: leader
    /// next to the player, hub + role panels wired, router is the REAL
    /// input surface (keys route through InputRouter.Handle).</summary>
    private static (Game Game, Input.InputRouter Router, FactionLeaderNpc Leader) MakeHubGame()
    {
        var game = new Game();
        var player = new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            ActionSystem = new ActionSystem(),
            SkillManager = new Skills.SkillManager(),
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
        game.SkillPanel = new SkillPanel();
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

        var flows = new Interactions.NPCFlows(game);
        var router = new Input.InputRouter(game, new Interactions.InteractSystem(game, flows),
            new Interactions.FireInteraction(game), flows);
        // Bootstrap wires the hub's tab callback to Game.OpenNpcHubTab; the
        // bare harness repeats that one line (OnTabSelected is not a panel
        // default).
        game.NpcHub!.OnTabSelected = game.OpenNpcHubTab;
        return (game, router, leader);
    }

    [Fact]
    public void Esc_FromHubLaunchedPanel_ReturnsToHubMenu()
    {
        var (game, router, leader) = MakeHubGame();
        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));

        // E on the multi-role leader opens the hub.
        interact.HandleInteract();
        Assert.Equal(GameState.NpcHub, game.State);

        // Choose the diplomacy tab — the handoff opens the diplomacy panel.
        game.NpcHub!.HandleKey(Key.Right); // quests -> diplomacy
        game.NpcHub.HandleConfirm();
        Assert.Equal(GameState.DiplomacyPanel, game.State);

        // Esc in the tab panel returns to the hub, session and tab intact.
        router.Handle(Key.Escape);

        Assert.Equal(GameState.NpcHub, game.State);
        Assert.True(game.NpcHub.Visible);
        Assert.Equal(leader.NpcId, game.NpcHub.Session?.NpcId);
        Assert.Equal(NpcHubPanel.DiplomacyTab, game.NpcHub.ActiveTab);
    }

    [Fact]
    public void Esc_FromSingleRolePanel_StillClosesToPlaying()
    {
        var (game, router, _) = MakeHubGame();
        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));

        // A quest-bearing single-role giver: E opens the quest panel directly.
        var giver = new QuestGiverNpc
        {
            NpcId = "hemlock",
            NpcType = "quest_giver",
            FactionId = "forest_villagers",
            WorldX = game.Player!.WorldX + 20f,
            WorldY = game.Player.WorldY,
        };
        giver.AvailableQuests.Add("timber_collection");
        game.NPCSystem!.NPCs.Clear();
        game.NPCSystem.NPCs.Add(giver);
        interact.HandleInteract();
        Assert.Equal(GameState.QuestPanel, game.State);

        router.Handle(Key.Escape);

        Assert.Equal(GameState.Playing, game.State);
        Assert.Null(game.NpcHub!.Session);
    }

    [Fact]
    public void WorldTransition_ClearsTheHubReturn()
    {
        var (game, router, leader) = MakeHubGame();
        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));

        interact.HandleInteract();                       // hub opens
        game.NpcHub!.HandleKey(Key.Right);               // to diplomacy
        game.NpcHub.HandleConfirm();                     // handoff
        Assert.Equal(GameState.DiplomacyPanel, game.State);

        // The player closes to Playing once (e.g. via the panel's own
        // confirm action), leaving the world — the return must not survive.
        game.SetState(GameState.Playing);

        // A later panel (opened directly, e.g. Tab = skills) must close to
        // Playing, not resurrect the hub.
        game.SetState(GameState.SkillPanel);
        router.Handle(Key.Escape);

        Assert.Equal(GameState.Playing, game.State);
        Assert.False(game.NpcHub!.Visible);
    }

    [Fact]
    public void ReturnedHub_OffersTheSameRoles_Again()
    {
        var (game, router, leader) = MakeHubGame();
        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));

        interact.HandleInteract();
        game.NpcHub!.HandleKey(Key.Right);
        game.NpcHub.HandleConfirm();
        router.Handle(Key.Escape); // back to the hub

        // Same leader, same roles: quests + diplomacy both offered again.
        Assert.NotNull(game.NpcHub.Session);
        Assert.Equal(new System.Collections.Generic.List<string> { "quests", "diplomacy" },
            NpcHubPanel.RolesFor(game.NpcHub.Session!));
        Assert.Equal(2, game.NpcHub.Tabs.Count);
    }
}
