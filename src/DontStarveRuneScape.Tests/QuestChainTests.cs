namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// First Steps quest chain (frontier slice 4): six data-only quests in
/// quests.json that walk a fresh survivor through fire, food, crafting,
/// the first kill, and founding the settlement — every objective rides
/// the existing condition hooks (collect/craft/kill/visit/negotiate).
/// Spec: docs/superpowers/specs/2026-10-07-first-steps-quest-chain-design.md
/// </summary>
public class QuestChainTests
{
    private static readonly string[] ChainIds =
    [
        "first_flame", "hearth_and_home", "full_belly",
        "first_blood", "settling_in", "forest_friends",
    ];

    private static QuestSystem MakeSystem() => new()
    {
        Registry = new QuestRegistry(),
        Factions = new FactionSystem(),
    };

    static QuestChainTests()
    {
        var system = MakeSystem();
        system.Registry!.LoadAll();
    }

    // -- 1. Every chain quest loads and its data references resolve ------

    [Fact]
    public void ChainQuests_LoadFromData()
    {
        var registry = new QuestRegistry();
        registry.LoadAll();
        foreach (var id in ChainIds)
        {
            var quest = registry.GetQuest(id);
            Assert.NotNull(quest);
            Assert.False(string.IsNullOrWhiteSpace(quest!.Name));
            Assert.NotEmpty(quest.Conditions);
            Assert.False(string.IsNullOrWhiteSpace(quest.SpriteKey));
        }
    }

    [Fact]
    public void ChainQuests_AllTargetsExistInData()
    {
        var registry = new QuestRegistry();
        registry.LoadAll();
        var items = new DataLoader();
        items.LoadAll();
        var monsters = new MonsterRegistry();
        monsters.LoadAll();
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var itemIds = items.ItemsData
            .Select(row => row.TryGetValue("id", out var id) ? id?.ToString() : null)
            .Where(id => id != null)
            .Select(id => id!)
            .ToHashSet();
        var monsterIds = monsters.MonstersByBiome.Values
            .SelectMany(biome => biome.Keys)
            .ToHashSet();
        var recipeIds = recipes.Recipes.Keys.ToHashSet();

        foreach (var id in ChainIds)
        {
            var quest = registry.GetQuest(id)!;
            foreach (var c in quest.Conditions.Concat(quest.FailConditions))
            {
                switch (c.Type)
                {
                    case "collect_item" or "deliver_item" or "craft_item":
                        Assert.True(itemIds.Contains(c.Target) || recipeIds.Contains(c.Target),
                            $"{id}: target '{c.Target}' is neither item nor recipe");
                        break;
                    case "kill_monster":
                        Assert.Contains(c.Target, monsterIds);
                        break;
                    case "visit_location":
                        Assert.True(c.Target is "forest" or "plains" or "coastal"
                            or "swamp" or "mountains" or "desert",
                            $"{id}: unknown biome '{c.Target}'");
                        break;
                    case "negotiate_faction":
                        Assert.True(c.Target is "forest_villagers" or "goblins"
                            or "coastal_merchants" or "desert_djinn"
                            or "swamp_factions" or "mountain_clans",
                            $"{id}: unknown faction '{c.Target}'");
                        break;
                }
            }
            foreach (var (itemId, _) in quest.ItemRewards)
                Assert.True(itemIds.Contains(itemId), $"{id}: reward item '{itemId}' missing");
            foreach (var unlock in quest.RecipeUnlocks)
                Assert.True(recipeIds.Contains(unlock), $"{id}: recipe unlock '{unlock}' missing");
        }
    }

    // -- 2. Prerequisites order the chain ---------------------------------

    [Fact]
    public void ChainQuests_ArePrerequisiteOrdered()
    {
        var registry = new QuestRegistry();
        registry.LoadAll();
        for (int i = 1; i < ChainIds.Length; i++)
        {
            var quest = registry.GetQuest(ChainIds[i])!;
            Assert.Contains(ChainIds[i - 1], quest.PrerequisiteQuests);
        }
        // Step 1 has no prerequisites — a fresh character can start.
        Assert.Empty(registry.GetQuest(ChainIds[0])!.PrerequisiteQuests);
    }

    // -- 3. The forest giver offers the full chain ------------------------

    [Fact]
    public void ForestGiver_OffersFullChain()
    {
        var npcRegistry = new NpcRegistry();
        npcRegistry.LoadAll();
        var def = npcRegistry.Npcs["quest_giver_forest_1"];
        var npc = (QuestGiverNpc)Npc.FromDef(def);

        foreach (var id in ChainIds)
            Assert.Contains(id, npc.AvailableQuests);
        Assert.Contains("timber_collection", npc.AvailableQuests); // kept as a side quest
    }

    // -- 4. A fresh character can accept step 1 ---------------------------

    [Fact]
    public void FreshCharacter_CanAcceptFirstFlame()
    {
        var system = MakeSystem();
        system.Registry!.LoadAll();
        var npcRegistry = new NpcRegistry();
        npcRegistry.LoadAll();
        var npc = (QuestGiverNpc)Npc.FromDef(npcRegistry.Npcs["quest_giver_forest_1"]);
        var player = new Player(0f, 0f) { SkillManager = new SkillManager() };

        var result = system.AcceptQuest(player, npc, "first_flame");
        Assert.True(result.Success, result.Message);
    }

    // -- 5. Walk-through: fire inputs complete step 1, claim pays out -----

    [Fact]
    public void FirstFlame_CompletesAndClaims()
    {
        var system = MakeSystem();
        system.Registry!.LoadAll();
        var npcRegistry = new NpcRegistry();
        npcRegistry.LoadAll();
        var npc = (QuestGiverNpc)Npc.FromDef(npcRegistry.Npcs["quest_giver_forest_1"]);
        var player = new Player(0f, 0f) { SkillManager = new SkillManager() };
        var inventory = new Inv();

        Assert.True(system.AcceptQuest(player, npc, "first_flame").Success);
        Assert.False(system.ConditionsMet(system.Registry!.GetQuest("first_flame")!, inventory));

        // collect_item objectives poll live inventory (ProgressOf reads
        // GetItemQuantity); NotifyCollect also bumps the counter, and the
        // game fires both on every pickup — mirror that here.
        inventory.AddItem("stick", 3);
        system.NotifyCollect("stick", 3);
        inventory.AddItem("tree_sap", 1);
        system.NotifyCollect("tree_sap", 1);

        var quest = system.Registry.GetQuest("first_flame")!;
        Assert.True(system.ConditionsMet(quest, inventory),
            "3 sticks + 1 tree sap should complete first_flame");

        var claim = system.Claim(player, npc, "first_flame", inventory, player.SkillManager!);
        Assert.True(claim.Success, claim.Message);
        Assert.True(system.IsCompleted("first_flame"));
        Assert.Contains("torch", inventory.Slots.Where(s => s.Quantity > 0).Select(s => s.ItemId));
    }

    // -- 6. Step 2 stays gated until step 1 completes ---------------------

    [Fact]
    public void SecondStep_RequiresFirstStepCompletion()
    {
        var system = MakeSystem();
        system.Registry!.LoadAll();
        var npcRegistry = new NpcRegistry();
        npcRegistry.LoadAll();
        var npc = (QuestGiverNpc)Npc.FromDef(npcRegistry.Npcs["quest_giver_forest_1"]);
        var player = new Player(0f, 0f) { SkillManager = new SkillManager() };

        var blocked = system.AcceptQuest(player, npc, "hearth_and_home");
        Assert.False(blocked.Success);

        Assert.True(system.AcceptQuest(player, npc, "first_flame").Success);
        var stillBlocked = system.AcceptQuest(player, npc, "hearth_and_home");
        Assert.False(stillBlocked.Success); // accepted ≠ completed
    }
}
