namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Data;
using Xunit;

/// <summary>
/// The four NPC-domain registries against the real data files: the typed
/// records must map the actual JSON field names (npc_id / trade_item_id /
/// quest_id / faction_id, condition_type / required_count, item reward pairs)
/// and load the full datasets.
/// </summary>
public class NpcDataTests
{
    [Fact]
    public void NpcRegistry_ReadsRealNpcsJson()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        // 16 original defs + companion_mara (starting-companion slice).
        Assert.Equal(17, registry.Npcs.Count);
        Assert.Equal(14, registry.SpawnPoints.Count);

        var merchant = registry.GetNpc("merchant_forest_1");
        Assert.NotNull(merchant);
        Assert.Equal("Old Man Hemlock", merchant!.Name);
        Assert.Equal("merchant", merchant.Type);
        Assert.Equal("forest_villagers", merchant.Faction);
        Assert.Equal(100, merchant.WorldX);
        Assert.Equal(100, merchant.WorldY);
        Assert.Equal(200, merchant.StartingGold);
        Assert.Equal(1.0f, merchant.PriceModifier);
        Assert.Equal(3, merchant.DialogueLines.Length);

        var giver = registry.GetNpc("quest_giver_forest_1");
        Assert.NotNull(giver);
        // First Steps chain (frontier slice 4) rides ahead of the original
        // side quests; herb_gathering joined Hemlock's board in the
        // cross-skill audit slice B (it was defined but offered by nobody).
        Assert.Equal(8, giver!.AvailableQuestIds.Length);
        Assert.Equal("first_flame", giver.AvailableQuestIds[0]);
        Assert.Contains("timber_collection", giver.AvailableQuestIds);
        Assert.Contains("herb_gathering", giver.AvailableQuestIds);

        var leader = registry.GetNpc("goblin_chief_grak");
        Assert.NotNull(leader);
        Assert.Equal("faction_leader", leader!.Type);
        Assert.Equal("goblins", leader.Faction);
        Assert.Equal(2, leader.AvailableQuestIds.Length);
        Assert.Equal("goblin_diplomacy", leader.AvailableQuestIds[0]);
        Assert.Equal("goblin_truce", leader.AvailableQuestIds[1]);

        var recruit = registry.GetNpc("recruit_forest_assistant");
        Assert.NotNull(recruit);
        Assert.Equal(2, recruit!.RecruitCommerceRequirement);
        Assert.Equal(3, recruit.RecruitPersuasionRequirement);
        Assert.Equal(5, recruit.RecruitCompositeStat);
        Assert.Equal(2, recruit.AvailableBehaviors.Length);
        Assert.Equal("assistant", recruit.AvailableBehaviors[0]);
        Assert.Equal("guard", recruit.AvailableBehaviors[1]);
    }

    [Fact]
    public void TradeRegistry_ReadsRealTradeItemsJson()
    {
        var registry = new TradeItemRegistry();
        registry.LoadAll();

        Assert.Equal(43, registry.TradeItems.Count);

        var herbs = registry.GetTradeItem("forest_healing_herbs");
        Assert.NotNull(herbs);
        Assert.Equal("healing_herbs", herbs!.ItemId);
        Assert.Equal("forest", herbs.Biome);
        Assert.Equal(8, herbs.BuyPrice);
        Assert.Equal(4, herbs.SellPrice);
        Assert.Equal(10, herbs.StockQuantity);
        Assert.Equal(10, herbs.MaxStock);

        Assert.Contains(registry.GetTradeItemsForBiome("forest"), t => t.ItemId == "oak_logs");
        Assert.Equal(9, registry.GetTradeItemsForBiome("forest").Count());
        Assert.Equal(8, registry.GetTradeItemsForBiome("desert").Count());
    }

    [Fact]
    public void QuestRegistry_ReadsRealQuestsJson()
    {
        var registry = new QuestRegistry();
        registry.LoadAll();

        Assert.Equal(18, registry.Quests.Count); // 12 originals + First Steps chain (6)

        var timber = registry.GetQuest("timber_collection");
        Assert.NotNull(timber);
        Assert.Equal("Timber Collection", timber!.Name);
        Assert.Equal("quest_giver", timber.GiverNpcType);
        Assert.Equal("forest_villagers", timber.GiverFaction);
        Assert.Equal(80f, timber.XpReward);
        Assert.Equal("woodcutting", Assert.Single(timber.SkillXpRewards).Key);
        Assert.Equal(30f, timber.SkillXpRewards["woodcutting"]);
        Assert.Single(timber.ItemRewards);
        Assert.Equal(("healing_herbs", 2), timber.ItemRewards[0]);
        Assert.Equal(1, timber.Conditions.Length);

        var condition = timber.Conditions[0];
        Assert.Equal("collect_item", condition.Type);
        Assert.Equal("oak_logs", condition.Target);
        Assert.Equal(5, condition.RequiredCount);
        Assert.Equal("Collect 5 oak logs", condition.Description);

        var diplomacy = registry.GetQuest("goblin_diplomacy");
        Assert.NotNull(diplomacy);
        Assert.Equal("suspicious", diplomacy!.RequiredFactionStatus);
        Assert.Equal(2, diplomacy.Conditions.Length);
        Assert.Contains(diplomacy.Conditions, c => c.Type == "negotiate_faction" && c.Target == "goblins");
        Assert.Contains(diplomacy.Conditions, c => c.Type == "visit_location" && c.Target == "forest");

        var sandStorm = registry.GetQuest("sand_storm_warning");
        Assert.NotNull(sandStorm);
        Assert.Contains(sandStorm!.Conditions, c => c.Type == "craft_item" && c.Target == "candle");
        Assert.Equal(3, sandStorm.Conditions.First(c => c.Type == "craft_item").RequiredCount);
    }

    [Fact]
    public void FactionRegistry_ReadsRealFactionsJson()
    {
        var registry = new FactionRegistry();
        registry.LoadAll();

        Assert.Equal(6, registry.Factions.Count);

        var goblins = registry.GetFaction("goblins");
        Assert.NotNull(goblins);
        Assert.Equal("Goblin Tribes", goblins!.Name);
        Assert.Equal("goblin_chief_grak", goblins.LeaderNpcId);
        Assert.Equal(0.7f, goblins.BaseHostility);
        Assert.Contains("forest", goblins.TerritoryBiomes);
        Assert.Contains("swamp", goblins.TerritoryBiomes);
        Assert.Contains("wolf", goblins.HostileMonsterTypes);

        Assert.NotNull(registry.GetFaction("forest_villagers"));
        Assert.NotNull(registry.GetFaction("mountain_clans"));
    }

    [Fact]
    public void NpcRegistry_GetNpcsOfType_Filters()
    {
        var registry = new NpcRegistry();
        registry.LoadAll();

        Assert.Equal(4, registry.GetNpcsOfType("merchant").Count());
        Assert.Equal(3, registry.GetNpcsOfType("quest_giver").Count());
        Assert.Equal(6, registry.GetNpcsOfType("faction_leader").Count());
        // 3 original recruits + companion_mara.
        Assert.Equal(4, registry.GetNpcsOfType("recruit").Count());
    }
}
