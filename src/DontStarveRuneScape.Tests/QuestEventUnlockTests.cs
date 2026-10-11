namespace DontStarveRuneScape.Tests;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Unlock-as-event slice: a quest's recipe unlocks land in
/// Player.UnlockedRecipes the moment its objectives complete (the story
/// beat), with a "Recipe learned" line for ids the data actually gates;
/// the quest-unlock gate keys on recipe ids at all three sites; completed
/// quests and unlock sets persist across saves.
/// Spec: docs/superpowers/specs/2026-10-11-unlock-as-event-design.md
/// </summary>
public class QuestEventUnlockTests
{
    private static Player MakePlayer() => new(0f, 0f)
    {
        SkillManager = new SkillManager(),
        ActionSystem = new ActionSystem(),
    };

    private static QuestSystem SystemWith(params QuestDef[] defs)
    {
        var registry = new QuestRegistry();
        foreach (var def in defs)
            registry.Quests[def.QuestId] = def;
        return new QuestSystem { Registry = registry };
    }

    private static void StageActive(QuestSystem system, string questId,
        Dictionary<string, int>? progress = null)
    {
        system.RestoreSnapshot(new QuestSnapshot
        {
            Quests = [new QuestDataSnapshot
            {
                QuestId = questId,
                State = "active",
                Progress = progress ?? [],
            }],
        }, new DataLoader());
    }

    // ─── 1. Every recipe_unlocks id is a real recipe ─────────────────────

    [Fact]
    public void EveryQuestRecipeUnlock_IsARealRecipe()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        foreach (var quest in quests.Quests.Values)
        {
            foreach (var id in quest.RecipeUnlocks)
            {
                Assert.True(recipes.GetRecipe(id) != null,
                    $"{quest.QuestId}: recipe unlock '{id}' is not a real recipe id");
            }
        }
    }

    // ─── 2. Every gated recipe is unlockable by its quest ────────────────

    [Fact]
    public void EveryGatedRecipe_IsUnlockableByItsQuest()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        foreach (var recipe in recipes.Recipes.Values.Where(r => r.QuestUnlock != null))
        {
            var quest = quests.GetQuest(recipe.QuestUnlock!);
            Assert.True(quest != null,
                $"{recipe.RecipeId}: quest '{recipe.QuestUnlock}' does not exist");
            Assert.Contains(recipe.RecipeId, quest!.RecipeUnlocks);
        }
    }

    // ─── 3. The event fires when objectives complete ─────────────────────

    [Fact]
    public void EventUnlock_FiresWhenObjectivesComplete()
    {
        var system = SystemWith(new QuestDef
        {
            QuestId = "learn_sticks",
            Name = "Learn Sticks",
            RecipeUnlocks = ["craft_stick"],
            Conditions = [new QuestCondition
            {
                Type = "collect_item", Target = "stick", RequiredCount = 3,
            }],
        });
        StageActive(system, "learn_sticks");
        var player = MakePlayer();
        var inv = new Inv();

        // Not yet: objectives unmet.
        system.Tick(0.25f, player, null, inv);
        Assert.DoesNotContain("craft_stick", player.UnlockedRecipes);

        // Objectives met: the unlock lands mid-quest, before claim.
        inv.AddItem("stick", 3);
        system.Tick(0.25f, player, null, inv);
        Assert.Contains("craft_stick", player.UnlockedRecipes);
    }

    // ─── 4. Notifications only for newly learned, actually gated ids ─────

    [Fact]
    public void EventUnlock_NotifiesOnlyGatedRecipes()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var system = SystemWith(
            new QuestDef
            {
                QuestId = "gated_lesson",
                Name = "Gated Lesson",
                RecipeUnlocks = ["craft_stick"],
                Conditions = [new QuestCondition
                {
                    Type = "collect_item", Target = "stick", RequiredCount = 1,
                }],
            },
            new QuestDef
            {
                QuestId = "plain_lesson",
                Name = "Plain Lesson",
                RecipeUnlocks = ["grind_bone"],
                Conditions = [new QuestCondition
                {
                    Type = "collect_item", Target = "stick", RequiredCount = 1,
                }],
            });
        system.Recipes = recipes;
        StageActive(system, "gated_lesson");
        StageActive(system, "plain_lesson");
        var player = MakePlayer();
        var inv = new Inv();
        inv.AddItem("stick", 1);

        system.Tick(0.25f, player, null, inv);
        var shown = player.ActionSystem!.FlushNotifications();
        Assert.Contains(shown, n => n.Text == "Recipe learned: craft_stick");
        Assert.DoesNotContain(shown, n => n.Text.Contains("grind_bone"));

        // Fire-once: a second tick repeats nothing.
        system.Tick(0.25f, player, null, inv);
        Assert.Empty(player.ActionSystem!.FlushNotifications());
    }

    // ─── 5. Claim still works after the event fired ──────────────────────

    [Fact]
    public void EventUnlock_ClaimStillWorks()
    {
        var system = SystemWith(new QuestDef
        {
            QuestId = "learn_sticks",
            Name = "Learn Sticks",
            RecipeUnlocks = ["craft_stick"],
            Conditions = [new QuestCondition
            {
                Type = "collect_item", Target = "stick", RequiredCount = 3,
            }],
        });
        StageActive(system, "learn_sticks");
        var player = MakePlayer();
        var inv = new Inv();
        inv.AddItem("stick", 3);

        // The event fires first; claim then merely confirms what is known.
        system.Tick(0.25f, player, null, inv);
        Assert.Contains("craft_stick", player.UnlockedRecipes);

        var npc = new QuestGiverNpc { NpcId = "q", NpcType = "quest_giver" };
        var claim = system.Claim(player, npc, "learn_sticks", inv, player.SkillManager!);
        Assert.True(claim.Success, claim.Message);
        Assert.True(system.IsCompleted("learn_sticks"));
        Assert.Contains("craft_stick", player.UnlockedRecipes);
    }

    // ─── 6. Real data end-to-end: goblin_truce opens craft_stick ─────────

    [Fact]
    public void GoblinTruce_EventUnlock_OpensCraftStick()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var system = new QuestSystem { Registry = quests, Recipes = recipes };
        // Mid-quest-save staging: the negotiations are done, the pelts are
        // not yet collected (collect progress polls the inventory).
        StageActive(system, "goblin_truce", new Dictionary<string, int> { ["1"] = 2 });
        var player = MakePlayer();
        var inv = new Inv();
        inv.AddItem("oak_logs", 1);

        // Gated without the unlock.
        var crafting = new CraftingSystem { Registry = recipes };
        var refused = crafting.Craft("craft_stick", inv, player.SkillManager!, null,
            player.UnlockedRecipes);
        Assert.False(refused.Success);

        // Collect the pelts: the event fires and the gate opens.
        inv.AddItem("wolf_pelt", 3);
        system.Tick(0.25f, player, null, inv);
        Assert.Contains("craft_stick", player.UnlockedRecipes);
        var crafted = crafting.Craft("craft_stick", inv, player.SkillManager!, null,
            player.UnlockedRecipes);
        Assert.True(crafted.Success, $"with the event unlock it should craft, got: {crafted.Message}");
    }

    // ─── 7. Claimed quests persist their completed state ─────────────────

    [Fact]
    public void ClaimedQuest_PersistsCompletedState()
    {
        var system = SystemWith(new QuestDef
        {
            QuestId = "learn_sticks",
            Name = "Learn Sticks",
            Conditions = [new QuestCondition
            {
                Type = "collect_item", Target = "stick", RequiredCount = 1,
            }],
        });
        StageActive(system, "learn_sticks");
        var player = MakePlayer();
        var inv = new Inv();
        inv.AddItem("stick", 1);
        var npc = new QuestGiverNpc { NpcId = "q", NpcType = "quest_giver" };
        Assert.True(system.Claim(player, npc, "learn_sticks", inv, player.SkillManager!).Success);

        var snapshot = system.GetSnapshot();
        Assert.Contains(snapshot.Quests, q => q.QuestId == "learn_sticks" && q.State == "completed");
        Assert.DoesNotContain(snapshot.Quests, q => q.QuestId == "learn_sticks" && q.State == "active");
    }

    // ─── 8. Restore rebuilds the completed set ───────────────────────────

    [Fact]
    public void RestoreSnapshot_RebuildsCompleted()
    {
        var system = SystemWith();
        system.RestoreSnapshot(new QuestSnapshot
        {
            Quests =
            [
                new QuestDataSnapshot { QuestId = "done_quest", State = "completed" },
                new QuestDataSnapshot { QuestId = "live_quest", State = "active" },
            ],
        }, new DataLoader());
        Assert.True(system.IsCompleted("done_quest"));
        Assert.False(system.IsAccepted("done_quest"));
        Assert.True(system.IsAccepted("live_quest"));
    }

    // ─── 9. Unlocks + completion survive a save round-trip ───────────────

    [Fact]
    public void SaveRoundTrip_UnlocksAndCompletedSurvive()
    {
        var quests = new QuestRegistry();
        quests.LoadAll();
        var g1 = new Game
        {
            Player = new Player(0f, 0f) { SkillManager = new SkillManager() },
            QuestSystem = new QuestSystem { Registry = quests },
            DataLoader = new DataLoader(),
        };
        StageActive(g1.QuestSystem!, "first_flame");
        var inv = new Inv();
        inv.AddItem("stick", 3);
        inv.AddItem("tree_sap", 1);
        g1.QuestSystem!.NotifyCollect("stick", 3);
        g1.QuestSystem.NotifyCollect("tree_sap", 1);
        var npc = new QuestGiverNpc { NpcId = "q", NpcType = "quest_giver" };
        Assert.True(g1.QuestSystem.Claim(g1.Player, npc, "first_flame", inv,
            g1.Player.SkillManager!).Success);
        g1.Player.UnlockedRecipes.Add("craft_stick");
        g1.Player.UnlockedGear.Add("iron_sword");

        var saveDir = Path.Combine(Path.GetTempPath(), $"dsr-unlock-{Guid.NewGuid():N}");
        var save = new SaveSystem(saveDir);
        save.Save(g1, 0);

        var g2 = new Game
        {
            Player = new Player(0f, 0f) { SkillManager = new SkillManager() },
            QuestSystem = new QuestSystem { Registry = quests },
            DataLoader = new DataLoader(),
        };
        var data = save.Load(0);
        Assert.NotNull(data);
        save.LoadIntoGame(data!, g2);

        Assert.True(g2.QuestSystem!.IsCompleted("first_flame"),
            "claimed quest must not re-offer after reload");
        Assert.Contains("craft_stick", g2.Player!.UnlockedRecipes);
        Assert.Contains("iron_sword", g2.Player.UnlockedGear);
    }
}
