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

        // 16 original defs + companion_mara (starting-companion slice)
        // + quest_giver_plains_1 (NPC-quest breadth).
        Assert.Equal(18, registry.Npcs.Count);
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

        // NPC-quest breadth: the plains hub giver, and the two leaders whose
        // boards were empty (both are faction leaders, so they now carry the
        // multi-role menu: quests + diplomacy).
        var plainsGiver = registry.GetNpc("quest_giver_plains_1");
        Assert.NotNull(plainsGiver);
        Assert.Equal("Hayward the Reaper", plainsGiver!.Name);
        Assert.Equal("quest_giver", plainsGiver.Type);
        Assert.Equal("forest_villagers", plainsGiver.Faction);
        Assert.Equal(3, plainsGiver.AvailableQuestIds.Length);
        Assert.Equal("plains_harvest", plainsGiver.AvailableQuestIds[0]);

        var elder = registry.GetNpc("elder_mara");
        Assert.NotNull(elder);
        Assert.Equal("herbal_remedy", Assert.Single(elder!.AvailableQuestIds));

        var guardian = registry.GetNpc("stone_guardian");
        Assert.NotNull(guardian);
        Assert.Equal("guardians_request", Assert.Single(guardian!.AvailableQuestIds));

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

        Assert.Equal(23, registry.Quests.Count); // 12 originals + First Steps (6) + quest breadth (5)

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

        // NPC-quest breadth: the five new side quests. Pure side content —
        // no recipe/gear unlocks, rewards are XP/items/gold only.
        var harvest = registry.GetQuest("plains_harvest");
        Assert.NotNull(harvest);
        Assert.Equal("quest_giver", harvest!.GiverNpcType);
        Assert.Equal("forest_villagers", harvest.GiverFaction);
        Assert.Equal("collect_item", harvest.Conditions[0].Type);
        Assert.Equal("wheat", harvest.Conditions[0].Target);
        Assert.Equal(6, harvest.Conditions[0].RequiredCount);
        Assert.Empty(harvest.RecipeUnlocks);
        Assert.Empty(harvest.GearUnlocks);
        Assert.Equal(("berries", 3), Assert.Single(harvest.ItemRewards));

        var boarHunt = registry.GetQuest("boar_hunt");
        Assert.NotNull(boarHunt);
        Assert.Equal(new[] { "plains_harvest" }, boarHunt!.PrerequisiteQuests);
        Assert.Equal("kill_monster", boarHunt.Conditions[0].Type);
        Assert.Equal("boar", boarHunt.Conditions[0].Target);
        Assert.Equal(2, boarHunt.Conditions[0].RequiredCount);
        Assert.Equal("attack", Assert.Single(boarHunt.SkillXpRewards).Key);

        var provender = registry.GetQuest("plains_provender");
        Assert.NotNull(provender);
        Assert.Equal(new[] { "boar_hunt" }, provender!.PrerequisiteQuests);
        Assert.Contains(provender.Conditions,
            c => c.Type == "deliver_item" && c.Target == "boar_meat" && c.RequiredCount == 2);
        Assert.Contains(provender.Conditions,
            c => c.Type == "trade_at_location" && c.Target == "merchant_plains_1");

        var remedy = registry.GetQuest("herbal_remedy");
        Assert.NotNull(remedy);
        Assert.Equal("faction_leader", remedy!.GiverNpcType);
        Assert.Equal("forest_villagers", remedy.GiverFaction);
        Assert.Equal(new[] { "timber_collection" }, remedy.PrerequisiteQuests);

        var guardians = registry.GetQuest("guardians_request");
        Assert.NotNull(guardians);
        Assert.Equal("faction_leader", guardians!.GiverNpcType);
        Assert.Equal("mountain_clans", guardians.GiverFaction);
        Assert.Equal(new[] { "mountain_pass" }, guardians.PrerequisiteQuests);

        // The parked audit finding: three quests awarded skill XP to the
        // nonexistent "combat" skill (170 XP vanished). Retargeted to attack.
        foreach (var quest in registry.Quests.Values)
            Assert.DoesNotContain("combat", quest.SkillXpRewards.Keys);
        Assert.Equal("attack", Assert.Single(registry.GetQuest("mountain_pass")!.SkillXpRewards).Key);
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
        Assert.Equal(4, registry.GetNpcsOfType("quest_giver").Count());
        Assert.Equal(6, registry.GetNpcsOfType("faction_leader").Count());
        // 3 original recruits + companion_mara.
        Assert.Equal(4, registry.GetNpcsOfType("recruit").Count());
    }
}
