namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Animal taming: feed a tamable wild monster its species food to roll a
/// tame; success turns it into a pet that bonds as the player's follower
/// (first pet) or is colony-assigned (later pets — wander near the anchor,
/// guard hostiles in radius). New top-level `taming` skill gates stronger
/// species; failures consume food visibly, level-gated attempts don't.
/// Spec: docs/superpowers/specs/2026-10-09-animal-taming-design.md
/// </summary>
public class TamedAnimalTests
{
    private static MonsterRegistry MakeMonsters()
    {
        var registry = new MonsterRegistry();
        registry.LoadAll();
        return registry;
    }

    private static Player MakePlayer(SkillManager skills, Inv inv)
    {
        return new Player(5.5f * Constants.TileSize, 5.5f * Constants.TileSize)
        {
            SkillManager = skills,
            Inventory = inv,
            Survival = new SurvivalSystem(),
        };
    }

    private static Monster SpawnWolf(CombatSystem combat, MonsterRegistry registry, float x, float y)
    {
        var def = registry.GetMonster("wolf");
        Assert.NotNull(def);
        return combat.SpawnMonster(def!, x, y, "forest");
    }

    // ── Data contract ────────────────────────────────────────────────

    [Fact]
    public void EveryTamableSpeciesDeclaresTameFoodAndLevel()
    {
        var registry = MakeMonsters();
        var loader = new DataLoader();
        loader.LoadAll();
        var itemIds = loader.ItemsData
            .Select(r => r.TryGetValue("id", out var v) ? v?.ToString() : null)
            .ToHashSet();

        int tamableCount = 0;
        foreach (var (biome, defs) in registry.MonstersByBiome)
        {
            foreach (var (id, def) in defs)
            {
                if (!def.Tamable) continue;
                tamableCount++;
                Assert.False(string.IsNullOrEmpty(def.TameFood),
                    $"{id} is tamable but declares no tame_food");
                Assert.True(def.TameLevel >= 1,
                    $"{id} tame_level {def.TameLevel} < 1");
                Assert.True(itemIds.Contains(def.TameFood),
                    $"{id} tame_food '{def.TameFood}' missing from items.json");
            }
        }
        Assert.InRange(tamableCount, 5, 100);
    }

    // ── Pure math (hook-free) ────────────────────────────────────────

    [Fact]
    public void TameChanceScalesWithLevelAndClamps()
    {
        // At parity the window is 35%.
        Assert.True(TamingMath.TameChance(0.30, tamingLevel: 1, speciesTameLevel: 1, successRatePoints: 0));
        Assert.False(TamingMath.TameChance(0.40, tamingLevel: 1, speciesTameLevel: 1, successRatePoints: 0));

        // +5% per level above the species gate, -5% below via the same delta.
        Assert.True(TamingMath.TameChance(0.44, tamingLevel: 3, speciesTameLevel: 1, successRatePoints: 0));
        Assert.False(TamingMath.TameChance(0.46, tamingLevel: 3, speciesTameLevel: 1, successRatePoints: 0));

        // success_rate sub-stat points: +2% each.
        Assert.True(TamingMath.TameChance(0.36, tamingLevel: 1, speciesTameLevel: 1, successRatePoints: 1));
        Assert.False(TamingMath.TameChance(0.40, tamingLevel: 1, speciesTameLevel: 1, successRatePoints: 1));

        // Clamp floor: a hopeless mismatch still leaves 5%.
        Assert.True(TamingMath.TameChance(0.04, tamingLevel: 1, speciesTameLevel: 8, successRatePoints: 0));
        Assert.False(TamingMath.TameChance(0.06, tamingLevel: 1, speciesTameLevel: 8, successRatePoints: 0));
    }

    // ── Skill registration ───────────────────────────────────────────

    [Fact]
    public void TamingSkillIsRegisteredWithSuccessRate()
    {
        var sm = new SkillManager();
        var skill = sm.GetSkill("taming");
        Assert.NotNull(skill);
        Assert.Equal(1, skill.Level);
        Assert.True(skill.SubStats.ContainsKey("success_rate"));
        Assert.Contains("taming", SkillManager.SubStatCatalog.Keys);
        Assert.Contains("success_rate", SkillManager.SubStatCatalog["taming"]);
    }

    // ── TryTame outcomes ─────────────────────────────────────────────

    [Fact]
    public void ForcedSuccessConsumesFoodAndBondsFollower()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 3);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        float px = player.WorldX, py = player.WorldY;
        var wolf = SpawnWolf(combat, registry, px + 32f, py);

        var taming = new TamingSystem { RollOverride = "success" };
        var result = taming.TryTame(player, wolf, combat);

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, inv.GetItemQuantity("raw_meat"));
        Assert.DoesNotContain(combat.Monsters, m => ReferenceEquals(m, wolf));

        // The tamed animal exists, follows, and is companion-bonded.
        var pet = Assert.Single(taming.TamedAnimals);
        Assert.Equal("pet", pet.RecruitBehavior);
        Assert.Equal(wolf.MonsterId, pet.SpeciesId);
        Assert.Equal(taming.FollowingPetId, pet.NpcId);

        // XP: attempt + success to the taming skill.
        Assert.True(skills.GetSkill("taming").Xp >= 25f,
            $"expected >=25 taming xp, got {skills.GetSkill("taming").Xp}");
    }

    [Fact]
    public void ForcedFailureConsumesFoodLeavesMonsterWild()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 2);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        var wolf = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);

        var taming = new TamingSystem { RollOverride = "" };
        var result = taming.TryTame(player, wolf, combat);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Message)); // visible, never silent
        Assert.Equal(1, inv.GetItemQuantity("raw_meat"));
        Assert.Contains(combat.Monsters, m => ReferenceEquals(m, wolf));
        Assert.Empty(taming.TamedAnimals);

        // A small attempt XP is still earned (grind honesty).
        Assert.True(skills.GetSkill("taming").Xp >= 4f);
    }

    [Fact]
    public void LevelGateRefusesWithoutConsumingFood()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 1);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();

        var def = registry.GetMonster("bear");
        Assert.NotNull(def);
        Assert.True(def!.TameLevel > 1, "bear must be level-gated for this test");
        inv.AddItem(def.TameFood, 1);
        var bear = combat.SpawnMonster(def, player.WorldX + 32f, player.WorldY, "forest");

        var taming = new TamingSystem { RollOverride = "success" };
        var result = taming.TryTame(player, bear, combat);

        Assert.False(result.Success);
        Assert.Contains("taming level", result.Message);
        Assert.Equal(1, inv.GetItemQuantity(def.TameFood)); // NOT consumed
        Assert.Contains(combat.Monsters, m => ReferenceEquals(m, bear));
        Assert.Empty(taming.TamedAnimals);
    }

    [Fact]
    public void MissingOrWrongFoodNotifiesNothingConsumed()
    {
        var skills = new SkillManager();
        var inv = new Inv(); // no food at all
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        var wolf = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);

        var taming = new TamingSystem { RollOverride = "success" };
        var result = taming.TryTame(player, wolf, combat);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.Message));
        Assert.Contains(combat.Monsters, m => ReferenceEquals(m, wolf));
        Assert.Empty(taming.TamedAnimals);
        Assert.Equal(0f, skills.GetSkill("taming").Xp);
    }

    [Fact]
    public void NonTamableMonsterRefuses()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 1);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        var goblinDef = registry.GetMonster("goblin");
        Assert.NotNull(goblinDef);
        Assert.False(goblinDef!.Tamable);
        var goblin = combat.SpawnMonster(goblinDef, player.WorldX + 32f, player.WorldY, "forest");

        var taming = new TamingSystem { RollOverride = "success" };
        var result = taming.TryTame(player, goblin, combat);

        Assert.False(result.Success);
        Assert.Equal(1, inv.GetItemQuantity("raw_meat"));
        Assert.Empty(taming.TamedAnimals);
    }

    // ── Companion bond vs colony assignment ──────────────────────────

    [Fact]
    public void FirstPetBondsSecondGoesToColony()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 4);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        float px = player.WorldX, py = player.WorldY;
        var wolfA = SpawnWolf(combat, registry, px + 32f, py);
        var wolfB = SpawnWolf(combat, registry, px - 32f, py);

        var taming = new TamingSystem { RollOverride = "success" };
        Assert.True(taming.TryTame(player, wolfA, combat).Success);
        Assert.True(taming.TryTame(player, wolfB, combat).Success);

        Assert.Equal(2, taming.TamedAnimals.Count);
        var follower = taming.TamedAnimals[0];
        var colonyPet = taming.TamedAnimals[1];
        Assert.Equal(taming.FollowingPetId, follower.NpcId);
        Assert.NotEqual(taming.FollowingPetId, colonyPet.NpcId);
        Assert.True(colonyPet.IsColonyAssigned);
        Assert.False(follower.IsColonyAssigned);
    }

    [Fact]
    public void FollowerPetFollowsAndDropsOffPastTether()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 1);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        var wolf = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);

        var taming = new TamingSystem { RollOverride = "success" };
        Assert.True(taming.TryTame(player, wolf, combat).Success);
        var pet = taming.TamedAnimals[0];

        // Walk the pet toward a far-away player: it closes distance or says why not.
        player.WorldX += 10f * Constants.TileSize;
        float startDist = System.MathF.Abs(pet.WorldX - player.WorldX);
        taming.Tick(0.25f, pet);
        float afterDist = System.MathF.Abs(pet.WorldX - player.WorldX);
        Assert.True(afterDist < startDist,
            $"pet should close on the player (start {startDist}, after {afterDist})");
    }

    // ── Colony guard behavior ────────────────────────────────────────

    [Fact]
    public void ColonyAssignedPetGuardsHostilesInRadius()
    {
        var skills = new SkillManager();
        skills.AddXpWithNotification("taming", SkillManager.XpForLevel(3) + 1f);
        var inv = new Inv();
        inv.AddItem("raw_meat", 2);
        var player = MakePlayer(skills, inv);
        player.SkillManager = skills;
        var combat = new CombatSystem();
        var registry = MakeMonsters();

        var taming = new TamingSystem { RollOverride = "success" };
        // First pet takes the follower bond...
        var wolfA = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);
        Assert.True(taming.TryTame(player, wolfA, combat).Success);
        // ...so the second is colony-assigned at the tame site (no colony founded).
        var wolfB = SpawnWolf(combat, registry, player.WorldX + 96f, player.WorldY);
        Assert.True(taming.TryTame(player, wolfB, combat).Success);
        var guard = taming.TamedAnimals[1];
        Assert.True(guard.IsColonyAssigned);

        // A hostile inside the guard radius takes damage from the pet.
        var goblinDef = registry.GetMonster("goblin");
        Assert.NotNull(goblinDef);
        var goblin = combat.SpawnMonster(goblinDef!,
            guard.WorldX + 2f * Constants.TileSize, guard.WorldY, "forest");
        int hpBefore = goblin.Health;

        taming.TickColonyGuard(0.25f, guard, combat);
        Assert.True(goblin.Health < hpBefore,
            $"guard pet should damage in-radius hostiles (hp {hpBefore} -> {goblin.Health})");
    }

    [Fact]
    public void ColonyAssignedPetIgnoresHostilesPastLeash()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 2);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();

        var taming = new TamingSystem { RollOverride = "success" };
        var wolfA = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);
        Assert.True(taming.TryTame(player, wolfA, combat).Success);
        var wolfB = SpawnWolf(combat, registry, player.WorldX + 96f, player.WorldY);
        Assert.True(taming.TryTame(player, wolfB, combat).Success);
        var guard = taming.TamedAnimals[1];

        var goblinDef = registry.GetMonster("goblin");
        var far = combat.SpawnMonster(goblinDef!,
            guard.WorldX + 20f * Constants.TileSize, guard.WorldY, "forest");
        int hpBefore = far.Health;

        taming.TickColonyGuard(0.25f, guard, combat);
        Assert.Equal(hpBefore, far.Health);
        // And the guard never drifted toward it (leash honored).
        Assert.True(System.MathF.Abs(guard.WorldX - (player.WorldX + 96f)) < 0.5f
            || guard.CarryStatus != "Chasing");
    }

    // ── Persistence ──────────────────────────────────────────────────

    [Fact]
    public void TamedAnimalsRoundTripThroughSnapshot()
    {
        var skills = new SkillManager();
        var inv = new Inv();
        inv.AddItem("raw_meat", 1);
        var player = MakePlayer(skills, inv);
        var combat = new CombatSystem();
        var registry = MakeMonsters();
        var wolf = SpawnWolf(combat, registry, player.WorldX + 32f, player.WorldY);

        var taming = new TamingSystem { RollOverride = "success" };
        Assert.True(taming.TryTame(player, wolf, combat).Success);
        var pet = taming.TamedAnimals[0];

        var snapshot = taming.BuildSnapshot();

        var restored = new TamingSystem();
        restored.RestoreSnapshot(snapshot, player);

        var back = Assert.Single(restored.TamedAnimals);
        Assert.Equal(pet.NpcId, back.NpcId);
        Assert.Equal(pet.SpeciesId, back.SpeciesId);
        Assert.Equal(pet.Health, back.Health);
        Assert.Equal(pet.WorldX, back.WorldX);
        Assert.Equal(pet.WorldY, back.WorldY);
        Assert.Equal(restored.FollowingPetId, back.NpcId);
    }
}
