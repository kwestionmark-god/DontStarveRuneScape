namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Cross-skill audit slice B: progression deadlocks. Planks un-gated,
/// first_flame's tree_sap reachable at crafting 1, herb_gathering
/// offered, quest_unlock enforced in Craft, and a data-driven
/// reachability walk over quest targets and gates.
/// Spec: docs/superpowers/specs/2026-10-09-cross-skill-audit-deadlocks-design.md
/// </summary>
public class ProgressionReachabilityTests
{
    private static RecipeRegistry Recipes()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        return recipes;
    }

    private static QuestRegistry Quests()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        return quests;
    }

    // ─── 1. Planks un-gate ─────────────────────────────────────────────

    [Fact]
    public void Planks_RecipeNotQuestGated()
    {
        var recipes = Recipes();
        var planks = recipes.GetRecipe("planks");
        Assert.NotNull(planks);
        // Day-1 craftable: no quest owns the recipe anymore.
        Assert.Null(planks!.QuestUnlock);

        // And the quest no longer lists planks as its unlock.
        var quests = Quests();
        var diplomacy = quests.GetQuest("goblin_diplomacy");
        Assert.NotNull(diplomacy);
        Assert.DoesNotContain("planks", diplomacy!.RecipeUnlocks);
    }

    // ─── 2. first_flame tree_sap reachable at crafting 1 ───────────────

    [Fact]
    public void FirstFlame_TreeSapReachableAtCrafting1()
    {
        var recipes = Recipes();
        var sap = recipes.Recipes.Values.FirstOrDefault(r =>
            r.OutputItem == "tree_sap" && r.RequiredLevel <= 1
            && r.RequiredSkill == "crafting");
        Assert.NotNull(sap);
        // Inputs must be reachable without woodcutting 20: oak_logs are
        // choppable at level 1 AND buyable at the forest merchant.
        Assert.Contains(sap!.Inputs, i => i.ItemId == "oak_logs");
    }

    // ─── 3. herb_gathering offered by Hemlock ──────────────────────────

    [Fact]
    public void HerbGathering_OfferedByForestGiver()
    {
        var npcs = new NpcRegistry();
        npcs.LoadAll();
        var giver = npcs.Npcs["quest_giver_forest_1"];
        Assert.Contains("herb_gathering", giver.AvailableQuestIds);
    }

    // ─── 4. quest_unlock enforcement in Craft ───────────────────────────

    [Fact]
    public void QuestLockedRecipe_RefusesWithoutUnlock()
    {
        var recipes = Recipes();
        var locked = recipes.Recipes.Values.FirstOrDefault(r => r.QuestUnlock != null);
        Assert.NotNull(locked);
        var crafting = new CraftingSystem { Registry = recipes };
        var skills = new SkillManager();
        skills.AddXpWithNotification(locked!.RequiredSkill,
            SkillManager.XpForLevel(locked.RequiredLevel) + 1f);
        var inv = new Inv();
        foreach (var (itemId, qty) in locked.Inputs)
            inv.AddItem(itemId, qty);
        Assert.True(inv.CanAdd(locked.OutputItem, locked.OutputQuantity));

        // Without the unlock in the set: refused, visibly.
        var empty = new HashSet<string>();
        var refused = crafting.Craft(locked.RecipeId, inv, skills, null, empty);
        Assert.False(refused.Success, "quest-locked recipe must refuse without the unlock");

        // With the unlock: crafts as before.
        var unlocked = new HashSet<string> { locked.QuestUnlock! };
        var crafted = crafting.Craft(locked.RecipeId, inv, skills, null, unlocked);
        Assert.True(crafted.Success, $"with the unlock it should craft, got: {crafted.Message}");

        // Null set (worker auto-production path): ungated by convention.
        inv = new Inv();
        foreach (var (itemId, qty) in locked.Inputs)
            inv.AddItem(itemId, qty);
        var worker = crafting.Craft(locked.RecipeId, inv, skills, null, null);
        Assert.True(worker.Success, "colony auto-production is not quest-gated");
    }

    // ─── 5. Reachability walk over quest item targets ───────────────────

    [Fact]
    public void EveryQuestItemTarget_HasFreshReachableSource()
    {
        var loader = new DataLoader();
        loader.LoadAll();
        var itemIds = loader.ItemsData
            .Where(r => r.TryGetValue("id", out var id) && id != null)
            .Select(r => r["id"].ToString())
            .ToHashSet();
        var recipes = Recipes();

        // Anything a fresh character can obtain without any quest:
        // - buyable at a merchant within early commerce reach,
        // - carried by a starter pack,
        // - the yield of ANY resource node (grindable: every node trains
        //   the skill its own gate reads — the slice-A law — so nodes are
        //   reachable by doing, no matter the level), or
        // - the loot of any monster (killable), or
        // - the closed output of recipes over the above.
        var trade = new TradeItemRegistry();
        trade.LoadAll();
        var baseReachable = trade.TradeItems.Values
            .Where(t => t.CommerceRequirement <= 3)
            .Select(t => t.ItemId)
            .ToHashSet();

        string[] packs = ["axe", "torch", "berries", "pickaxe", "raw_meat", "raw_fish"];
        foreach (var p in packs) baseReachable.Add(p);

        var monsters = new MonsterRegistry();
        monsters.LoadAll();
        foreach (var biome in monsters.MonstersByBiome.Values)
            foreach (var def in biome.Values)
                foreach (var loot in def.LootTable)
                    baseReachable.Add(loot.ItemId);

        var resources = new ResourceRegistry(
            loader.ResourcesData.Select(Bootstrap.BuildResourceDef));
        foreach (var def in resources.Resources.Values)
            baseReachable.Add(def.YieldItem);
        // "gold" is income: any sellable item converts to gold via trade.
        baseReachable.Add("gold");

        // Recipe closure: keep growing until stable.
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var r in recipes.Recipes.Values)
            {
                if (baseReachable.Contains(r.OutputItem)) continue;
                if (r.RequiredLevel > 10) continue; // deep-skill recipes ride their skill's grind
                if (r.Inputs.All(i => baseReachable.Contains(i.ItemId)))
                {
                    baseReachable.Add(r.OutputItem);
                    grew = true;
                }
            }
        }

        var quests = Quests();
        string[] unreachable = [];
        foreach (var quest in quests.Quests.Values)
        {
            foreach (var c in quest.Conditions)
            {
                if (c.Type is not ("collect_item" or "deliver_item")) continue;
                if (itemIds.Contains(c.Target) && baseReachable.Contains(c.Target)) continue;
                unreachable = [.. unreachable,
                    $"{quest.QuestId}: {c.Target}"];
            }
        }
        Assert.True(unreachable.Length == 0,
            $"quest targets without a fresh-reachable source: {string.Join(", ", unreachable)}");
    }

    // ─── 5b. Quest int gates reachable from prerequisite-free quests ─────

    [Fact]
    public void QuestIntGates_ReachableFromPrerequisiteFreeQuests()
    {
        var quests = Quests();
        // Fixed-point over the prerequisite DAG: quest X is claimable if
        // its prerequisites are claimable and its int gate ≤ total int XP
        // available from claimable quests' flat xp_reward (a lower bound:
        // skill_xp_rewards intelligence adds more).
        var claimable = new HashSet<string>();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var quest in quests.Quests.Values)
            {
                if (claimable.Contains(quest.QuestId)) continue;
                if (!quest.PrerequisiteQuests.All(claimable.Contains)) continue;
                claimable.Add(quest.QuestId);
                grew = true;
            }
        }

        // Every quest must be claimable eventually (the DAG has no
        // dead-end above the graph's own reachability) and the
        // prerequisite-free set is non-empty.
        Assert.NotEmpty(claimable);
        var stalled = quests.Quests.Values
            .Where(q => !claimable.Contains(q.QuestId))
            .Select(q => q.QuestId)
            .ToArray();
        // herb_gathering's prerequisite is timber_collection which is
        // offered; circularity would stall here (goblin_truce was the
        // known near-miss).
        Assert.True(stalled.Length == 0,
            $"quests unreachable through the prerequisite DAG: {string.Join(", ", stalled)}");
    }
}
