namespace DontStarveRuneScape.Tests;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Coastal/swamp side quests: Maris (first merchant-with-quests) carries the
/// coastal line, the new swamp merchant Fenwick carries the swamp line AND
/// gives swamp its first shop, and two load-bearing merchant infra fixes —
/// authored biome on merchant defs (stock was a seed lottery) and the daily
/// depart sweep no longer despawns stationary shopkeepers.
/// Spec: docs/superpowers/specs/2026-10-11-coastal-swamp-side-quests-design.md
/// </summary>
public class CoastalSwampQuestTests
{
    // ─── Data pins: the two boards and Fenwick's def ─────────────────────

    [Fact]
    public void Maris_CarriesTheCoastalSideLine()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        var maris = registry.GetNpc("merchant_coastal_1");
        Assert.NotNull(maris);
        Assert.Equal("merchant", maris!.Type);
        Assert.Equal(3, maris.AvailableQuestIds.Length);
        Assert.Equal("driftwood_scour", maris.AvailableQuestIds[0]);
        Assert.Contains("salt_run", maris.AvailableQuestIds);
        Assert.Contains("crab_cove", maris.AvailableQuestIds);
        Assert.Equal("coastal", maris.Biome);
    }

    [Fact]
    public void Fenwick_DefIsTheSwampMerchantWithTheSideLine()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        var fenwick = registry.GetNpc("merchant_swamp_1");
        Assert.True(fenwick != null, "merchant_swamp_1 def missing");
        Assert.Equal("Fenwick the Bog Trader", fenwick!.Name);
        Assert.Equal("merchant", fenwick.Type);
        Assert.Equal("swamp_factions", fenwick.Faction);
        Assert.Equal("swamp", fenwick.Biome);
        Assert.Equal(3, fenwick.AvailableQuestIds.Length);
        Assert.Equal("reed_cut", fenwick.AvailableQuestIds[0]);
        Assert.Contains("peat_dig", fenwick.AvailableQuestIds);
        Assert.Contains("croc_bog", fenwick.AvailableQuestIds);
    }

    [Fact]
    public void QuestData_SixNewDefs_GatesConditionsRewards()
    {
        var registry = new QuestRegistry();
        registry.LoadAll();

        // Count pin: 23 -> 29.
        Assert.Equal(29, registry.Quests.Count);

        var driftwood = registry.GetQuest("driftwood_scour");
        Assert.NotNull(driftwood);
        Assert.Equal("merchant", driftwood!.GiverNpcType);
        Assert.Equal("coastal_merchants", driftwood.GiverFaction);
        Assert.Empty(driftwood.PrerequisiteQuests);
        Assert.Equal(60f, driftwood.XpReward);
        Assert.Equal(30f, driftwood.SkillXpRewards["foraging"]);
        Assert.Empty(driftwood.RecipeUnlocks);
        Assert.Empty(driftwood.GearUnlocks);
        Assert.Equal(("shells", 3), driftwood.ItemRewards[0]);
        Assert.Equal("collect_item", driftwood.Conditions[0].Type);
        Assert.Equal("driftwood", driftwood.Conditions[0].Target);
        Assert.Equal(6, driftwood.Conditions[0].RequiredCount);

        var salt = registry.GetQuest("salt_run");
        Assert.NotNull(salt);
        Assert.Empty(salt!.PrerequisiteQuests);
        Assert.Equal(70f, salt.XpReward);
        Assert.Equal(40f, salt.SkillXpRewards["foraging"]);
        Assert.Equal("salt", salt.Conditions[0].Target);
        Assert.Equal(4, salt.Conditions[0].RequiredCount);

        var crab = registry.GetQuest("crab_cove");
        Assert.NotNull(crab);
        Assert.Equal(new[] { "salt_run" }, crab!.PrerequisiteQuests);
        Assert.Equal("cooking", Assert.Single(crab.SkillXpRewards).Key);
        Assert.Contains(crab.Conditions,
            c => c.Type == "kill_monster" && c.Target == "crab" && c.RequiredCount == 2);
        Assert.Contains(crab.Conditions,
            c => c.Type == "deliver_item" && c.Target == "crab_meat" && c.RequiredCount == 2);
        Assert.Contains(crab.Conditions,
            c => c.Type == "trade_at_location" && c.Target == "merchant_coastal_1");

        var reed = registry.GetQuest("reed_cut");
        Assert.NotNull(reed);
        Assert.Empty(reed!.PrerequisiteQuests);
        Assert.Equal("reed", reed.Conditions[0].Target);
        Assert.Equal(6, reed.Conditions[0].RequiredCount);
        Assert.Equal(("poison_antidote", 1), reed.ItemRewards[0]);

        var peat = registry.GetQuest("peat_dig");
        Assert.NotNull(peat);
        Assert.Empty(peat!.PrerequisiteQuests);
        Assert.Equal("peat", peat.Conditions[0].Target);
        Assert.Equal(4, peat.Conditions[0].RequiredCount);
        Assert.Equal(40f, peat.SkillXpRewards["foraging"]);

        var croc = registry.GetQuest("croc_bog");
        Assert.NotNull(croc);
        Assert.Equal(new[] { "peat_dig" }, croc!.PrerequisiteQuests);
        Assert.Equal("attack", Assert.Single(croc.SkillXpRewards).Key);
        Assert.Contains(croc.Conditions,
            c => c.Type == "kill_monster" && c.Target == "crocodile" && c.RequiredCount == 2);
        Assert.Contains(croc.Conditions,
            c => c.Type == "deliver_item" && c.Target == "crocodile_tooth" && c.RequiredCount == 2);
        Assert.Contains(croc.Conditions,
            c => c.Type == "trade_at_location" && c.Target == "merchant_swamp_1");
    }

    [Fact]
    public void NpcRegistry_CountPins_MoveForData()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        // 18 -> 19 (Fenwick), merchants 4 -> 5.
        Assert.Equal(19, registry.Npcs.Count);
        Assert.Equal(5, registry.GetNpcsOfType("merchant").Count());
    }

    // ─── Merchant stock determinism: authored biome ───────────────────────

    private static TileMap Seed42World()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        var biomes = new BiomeRegistry(loader.Biomes);
        var resources = new ResourceRegistry(
            loader.ResourcesData.Select(Bootstrap.BuildResourceDef));
        return WorldGen.Generate(42, biomes, resources, null);
    }

    [Fact]
    public void MerchantStock_IsAuthoredBiome_RegardlessOfSeedTiles()
    {
        // On the seed-42 map every merchant def tile lands on a MISMATCHED
        // biome (verified by probe): the authored biome on the def must win
        // over the tile-derived one for stock matching.
        var world = Seed42World();
        var registry = new NpcRegistry();
        registry.LoadAll();
        var npcs = new NPCSystem();

        npcs.LoadFromRegistry(registry, world);

        var byId = npcs.NPCs.ToDictionary(n => n.NpcId);
        AssertBiome(byId, "merchant_coastal_1", "coastal");
        AssertBiome(byId, "merchant_swamp_1", "swamp");
        AssertBiome(byId, "merchant_forest_1", "forest");
        AssertBiome(byId, "merchant_plains_1", "plains");
        AssertBiome(byId, "merchant_desert_1", "desert");
    }

    private static void AssertBiome(Dictionary<string, Npc> byId, string id, string expected)
    {
        Assert.True(byId.TryGetValue(id, out var npc), $"{id} not spawned");
        Assert.True(npc.Biome == expected,
            $"{id}: expected biome {expected}, got {npc.Biome}");
    }

    [Fact]
    public void AuthoredBiome_SurvivesSaveRestore()
    {
        var world = Seed42World();
        var registry = new NpcRegistry();
        registry.LoadAll();
        var npcs = new NPCSystem();
        npcs.LoadFromRegistry(registry, world);
        var maris = npcs.NPCs.First(n => n.NpcId == "merchant_coastal_1");
        var snapshot = npcs.GetSnapshot();

        var restored = new NPCSystem();
        restored.RestoreSnapshot(snapshot, registry, world);

        var marisAgain = restored.NPCs.FirstOrDefault(n => n.NpcId == "merchant_coastal_1");
        Assert.True(marisAgain != null, "merchant_coastal_1 lost on restore");
        Assert.Equal("coastal", marisAgain!.Biome);
        Assert.Equal(maris.WorldX, marisAgain.WorldX);
    }

    [Fact]
    public void TradeRows_FenwickSellsSwampStock()
    {
        var world = Seed42World();
        var registry = new NpcRegistry();
        registry.LoadAll();
        var npcs = new NPCSystem();
        npcs.LoadFromRegistry(registry, world);
        var fenwick = npcs.NPCs.OfType<MerchantNpc>()
            .FirstOrDefault(n => n.NpcId == "merchant_swamp_1");
        Assert.True(fenwick != null, "merchant_swamp_1 not spawned");

        var trade = new TradeSystem();
        var tradeRegistry = new TradeItemRegistry();
        tradeRegistry.LoadAll();
        trade.Registry = tradeRegistry;

        var rows = trade.GetTradeItemsForMerchant(fenwick);
        // The five swamp entries are live data: with an authored swamp biome
        // Fenwick finally sells them (today they match no spawned merchant).
        Assert.Contains(rows, r => r.ItemId == "toxic_reed");
        Assert.Contains(rows, r => r.ItemId == "peat_block");
        Assert.Contains(rows, r => r.ItemId == "poison_gland");
        Assert.Contains(rows, r => r.ItemId == "crocodile_tooth");
        Assert.Contains(rows, r => r.ItemId == "poison_antidote");
    }

    // ─── Depart sweep: stationary shopkeepers survive ─────────────────────

    private sealed class VisitHarness
    {
        public Game Game = new();
        public NPCSystem Npcs = new();
        public DayNightCycle Clock = new();
        public FactionSystem FactionSystem = new();
        public TileMap Surface = MakeSurface();

        private static TileMap MakeSurface(int size = 24)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
                    world.Tiles[x, y].Biome = new BiomeDef { Id = "plains", Name = "Plains" };
                }
            return world;
        }

        public void SetupGame()
        {
            Game.World = Surface;
            Game.Player = new Player(4.5f * Constants.TileSize, 4.5f * Constants.TileSize)
            {
                SkillManager = new SkillManager(),
                Inventory = new Inv(),
                Survival = new SurvivalSystem(),
                Gear = new PlayerGear(),
                ActionSystem = new ActionSystem(),
            };
            Game.NPCSystem = Npcs;
            Game.DayNight = Clock;
            Game.FactionSystem = FactionSystem;
            Game.ColonySystem = new ColonySystem();
            Game.ColonySystem.FoundAt(5.5f * Constants.TileSize, 5.5f * Constants.TileSize, Surface);
            Game.FactionRegistry = new FactionRegistry();
            Game.FactionRegistry.LoadAll();
            Game.NpcRegistry = new NpcRegistry();
            Game.NpcRegistry.LoadAll();
            Game.MerchantVisitSystem = new MerchantVisitSystem(Game);
        }

        public void Tick(int steps = 1, float dt = 0.25f)
        {
            for (int i = 0; i < steps; i++)
            {
                Game.MerchantVisitSystem?.Tick(Clock.HourOfDay);
                Clock.Tick(dt);
            }
        }

        public void SetClockHour(float hour)
            => Clock.RestoreSnapshot(new DayNightSnapshot { TimeOfDay = (hour - 6f) / 24f });
    }

    [Fact]
    public void DepartSweep_StationaryMerchantSurvives()
    {
        var h = new VisitHarness();
        h.SetupGame();

        // Stage the REAL stationary merchants (as Bootstrap does).
        h.Npcs.LoadFromRegistry(h.Game.NpcRegistry!, h.Surface);

        // Cross 06:00 and into the 10:00 departure window.
        h.SetClockHour(5.9f);
        h.Tick(10); // ~06:00 spawn check runs (no faction at 0.65 -> no visitors)
        h.SetClockHour(10.1f);
        h.Tick(10);

        // The stationary shopkeepers must still be there.
        Assert.Contains(h.Npcs.NPCs, n => n.NpcId == "merchant_coastal_1");
        Assert.Contains(h.Npcs.NPCs, n => n.NpcId == "merchant_swamp_1");
    }

    [Fact]
    public void DepartSweep_VisitorsStillDepart()
    {
        var h = new VisitHarness();
        h.SetupGame();

        // A friendly faction with a merchant def -> a visitor spawns at 06:00.
        var friendly = new FactionDef
        {
            FactionId = "test_friendly",
            Name = "Test Friendlies",
            BaseHostility = 0f,
            TerritoryBiomes = ["plains"],
        };
        h.Game.FactionRegistry!.Factions["test_friendly"] = friendly;
        h.Game.FactionSystem!.SetStanding("test_friendly", 0.8f);
        h.Game.NpcRegistry!.Npcs["test_merchant_plains"] = new NpcDef
        {
            NpcId = "test_merchant_plains",
            Name = "Test Merchant",
            Type = "merchant",
            Faction = "test_friendly",
            StartingGold = 100,
            SpriteKey = "npc/merchant_male_0",
        };

        h.SetClockHour(5.9f);
        h.Tick(250); // cross 06:00
        var visitor = h.Npcs.NPCs.FirstOrDefault(n => n.NpcId == "test_merchant_plains");
        Assert.NotNull(visitor);

        // Into the departure window: the visitor leaves.
        h.SetClockHour(10.1f);
        h.Tick(10);
        Assert.DoesNotContain(h.Npcs.NPCs, n => n.NpcId == "test_merchant_plains");
    }

    // ─── Hub routing: merchant-with-quests ────────────────────────────────

    [Fact]
    public void RolesFor_MerchantWithQuests_OffersQuestsThenTrade()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        var marisDef = registry.GetNpc("merchant_coastal_1");
        Assert.True(marisDef != null, "merchant_coastal_1 def missing");
        var roles = NpcHubPanel.RolesFor(Npc.FromDef(marisDef!));
        Assert.Equal(new List<string> { "quests", "trade" }, roles);

        var fenwickDef = registry.GetNpc("merchant_swamp_1");
        Assert.True(fenwickDef != null, "merchant_swamp_1 def missing");
        var fenwickRoles = NpcHubPanel.RolesFor(Npc.FromDef(fenwickDef!));
        Assert.Equal(new List<string> { "quests", "trade" }, fenwickRoles);
    }

    [Fact]
    public void E_OnMerchantWithQuests_OpensTheHub()
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

        var registry = new NpcRegistry();
        registry.LoadAll();
        var marisDef = registry.GetNpc("merchant_coastal_1");
        Assert.True(marisDef != null, "merchant_coastal_1 def missing");
        var maris = Npc.FromDef(marisDef!);
        maris.WorldX = player.WorldX + 20f;
        maris.WorldY = player.WorldY;
        game.NPCSystem.NPCs.Add(maris);

        var interact = new Interactions.InteractSystem(game, new Interactions.NPCFlows(game));
        interact.HandleInteract();

        // Before: a merchant E-opened the trade panel directly. With quests
        // on the board, the hub menu is the honest interaction surface.
        Assert.Equal(GameState.NpcHub, game.State);
        Assert.True(game.NpcHub!.Visible);
        Assert.Equal(maris.NpcId, game.NpcHub.Session?.NpcId);
    }

    // ─── End-to-end chain flow through QuestSystem ───────────────────────

    [Fact]
    public void CoastalChain_AcceptsGated_Progresses_Claims_Delivers()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();

        var system = new QuestSystem { Registry = quests };
        var skills = new SkillManager();
        var inv = new Inv();
        var player = new Player(0f, 0f) { SkillManager = skills, Inventory = inv };

        var marisDef = npcs.GetNpc("merchant_coastal_1");
        Assert.True(marisDef != null, "merchant_coastal_1 def missing");
        var maris = Npc.FromDef(marisDef!);

        // Link 1: driftwood gather, no prerequisites, fresh-acceptable.
        var first = system.AcceptQuest(player, maris, "driftwood_scour");
        Assert.True(first.Success, first.Message);
        inv.AddItem("driftwood", 6);
        Assert.True(system.Claim(player, maris, "driftwood_scour", inv, skills).Success);

        // Link 2: salt gather.
        var second = system.AcceptQuest(player, maris, "salt_run");
        Assert.True(second.Success, second.Message);
        inv.AddItem("salt", 4);
        Assert.True(system.Claim(player, maris, "salt_run", inv, skills).Success);

        // Link 3: gated on salt_run (now complete) — the chain order holds.
        var third = system.AcceptQuest(player, maris, "crab_cove");
        Assert.True(third.Success, third.Message);
        system.NotifyKill("crab");
        system.NotifyKill("crab");
        system.NotifyTrade("merchant_coastal_1");
        inv.AddItem("crab_meat", 2);
        var claim = system.Claim(player, maris, "crab_cove", inv, skills);
        Assert.True(claim.Success, claim.Message);
        // The delivery consumed the crab meat.
        Assert.Equal(0, inv.GetItemQuantity("crab_meat"));
        Assert.True(system.IsCompleted("crab_cove"));
    }

    [Fact]
    public void SwampChain_ReedToPeatToCroc_EndToEnd()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var fenwickDef = npcs.GetNpc("merchant_swamp_1");
        Assert.True(fenwickDef != null, "merchant_swamp_1 def missing");
        var fenwick = Npc.FromDef(fenwickDef!);

        var system = new QuestSystem { Registry = quests };
        var skills = new SkillManager();
        var inv = new Inv();
        var player = new Player(0f, 0f) { SkillManager = skills, Inventory = inv };

        var first = system.AcceptQuest(player, fenwick, "reed_cut");
        Assert.True(first.Success, first.Message);
        inv.AddItem("reed", 6);
        Assert.True(system.Claim(player, fenwick, "reed_cut", inv, skills).Success);

        var second = system.AcceptQuest(player, fenwick, "peat_dig");
        Assert.True(second.Success, second.Message);
        inv.AddItem("peat", 4);
        Assert.True(system.Claim(player, fenwick, "peat_dig", inv, skills).Success);

        var third = system.AcceptQuest(player, fenwick, "croc_bog");
        Assert.True(third.Success, third.Message);
        system.NotifyKill("crocodile");
        system.NotifyKill("crocodile");
        system.NotifyTrade("merchant_swamp_1");
        inv.AddItem("crocodile_tooth", 2);
        var claim = system.Claim(player, fenwick, "croc_bog", inv, skills);
        Assert.True(claim.Success, claim.Message);
        Assert.Equal(0, inv.GetItemQuantity("crocodile_tooth"));
        Assert.True(system.IsCompleted("croc_bog"));
    }

    [Fact]
    public void QuestAcceptance_MerchantGiverType_MatchesMaris()
    {
        // The giver-type gate (AcceptQuest :69) must ACCEPT a merchant npc
        // for a merchant-typed quest — and refuse a quest_giver npc for it.
        var quests = new QuestRegistry();
        quests.LoadAll();
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var marisDef = npcs.GetNpc("merchant_coastal_1");
        Assert.True(marisDef != null, "merchant_coastal_1 def missing");
        var maris = Npc.FromDef(marisDef!);
        var hemlockDef = npcs.GetNpc("quest_giver_forest_1");
        Assert.True(hemlockDef != null, "quest_giver_forest_1 def missing");
        var hemlock = Npc.FromDef(hemlockDef!);

        var system = new QuestSystem { Registry = quests };
        var skills = new SkillManager();
        var player = new Player(0f, 0f) { SkillManager = skills, Inventory = new Inv() };

        // Hemlock's board doesn't list driftwood_scour; even if it did, the
        // type gate refuses. Pin the type gate with a board listing too.
        hemlock.AvailableQuests.Add("driftwood_scour");
        var wrongType = system.AcceptQuest(player, hemlock, "driftwood_scour");
        Assert.False(wrongType.Success);
        Assert.Contains("different kind", wrongType.Message);

        var rightType = system.AcceptQuest(player, maris, "driftwood_scour");
        Assert.True(rightType.Success, rightType.Message);
    }
}
