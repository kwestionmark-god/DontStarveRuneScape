using DontStarveRuneScape.Skills;
using Xunit;
using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;

namespace DontStarveRuneScape.Tests;

/// <summary>
/// Individual stat menus — per-skill sub-stat catalogs. RED against the
/// new API surfaces (spec: 2026-10-09-individual-stat-menus-design.md).
/// </summary>
public class SubStatCatalogTests
{
    [Fact]
    public void Catalog_CoversEverySkill_WithNamedStats()
    {
        // Every skill the manager registers has a catalog entry, and
        // every key on every menu carries a display name (the panel
        // renders from this data — logic and UI can never disagree).
        var sm = new SkillManager();
        var skillIds = new[]
        {
            "attack", "woodcutting", "mining", "foraging", "fishing", "cooking",
            "firemaking", "crafting", "metallurgy", "construction", "intelligence",
            "agility"
        };
        foreach (var id in skillIds)
        {
            Assert.True(SkillManager.SubStatCatalog.ContainsKey(id), $"no catalog entry for {id}");
            Assert.NotEmpty(SkillManager.SubStatCatalog[id]);
            foreach (var key in SkillManager.SubStatCatalog[id])
                Assert.True(SkillManager.SubStatNames.ContainsKey(key), $"no display name for {id}.{key}");
        }
    }

    [Fact]
    public void Catalog_IndividualizesTheMenus()
    {
        // The one-size-fits-all five are gone: intelligence is exactly
        // its real gates, attack is exactly power+speed, firemaking is
        // the fire pair, agility the movement pair.
        Assert.Equal(new[] { "commerce", "persuasion" }, SkillManager.SubStatCatalog["intelligence"]);
        Assert.Equal(new[] { "power", "speed" }, SkillManager.SubStatCatalog["attack"]);
        Assert.Equal(new[] { "fuel_saver", "duration" }, SkillManager.SubStatCatalog["firemaking"]);
        Assert.Equal(new[] { "sprint_cost", "jump_cost" }, SkillManager.SubStatCatalog["agility"]);
        // Mining's menu keeps only its real consumers (no stamina_reduction).
        Assert.Equal(new[] { "success_rate", "extra_resources", "efficiency" }, SkillManager.SubStatCatalog["mining"]);
    }

    [Fact]
    public void SkillData_InitsFromCatalog_PruneAndNew()
    {
        var sm = new SkillManager();

        // Pruned: dead keys no longer exist on the skill's SubStats.
        Assert.False(sm.GetSkill("mining").SubStats.ContainsKey("stamina_reduction"));
        Assert.False(sm.GetSkill("firemaking").SubStats.ContainsKey("success_rate"));

        // New: catalog keys are present and start at zero (invested
        // points only — no free power).
        Assert.Equal(0f, sm.GetSkill("attack").SubStats["power"]);
        Assert.Equal(0f, sm.GetSkill("attack").SubStats["speed"]);
        Assert.Equal(0f, sm.GetSkill("firemaking").SubStats["duration"]);
        Assert.Equal(0f, sm.GetSkill("fishing").SubStats["rare_luck"]);
        Assert.Equal(0f, sm.GetSkill("agility").SubStats["sprint_cost"]);

        // Intelligence seeds stay (gate reachability from a fresh character).
        Assert.Equal(1f, sm.GetSkill("intelligence").SubStats["commerce"]);
        Assert.Equal(1f, sm.GetSkill("intelligence").SubStats["persuasion"]);
    }

    [Fact]
    public void SpendPoint_RejectsKeysOffTheSkillsMenu()
    {
        var sm = new SkillManager();
        sm.AddXpWithNotification("firemaking", 83f); // level 2 -> 3 points
        sm.AddXpWithNotification("attack", 83f);

        // Pruned keys are no longer spendable...
        Assert.False(sm.SpendPoint("firemaking", "success_rate"));
        Assert.False(sm.SpendPoint("mining", "stamina_reduction"));
        // ...but the skill's real menu keys are.
        Assert.True(sm.SpendPoint("firemaking", "duration"));
        Assert.True(sm.SpendPoint("attack", "power"));
        Assert.True(sm.SpendPoint("attack", "speed"));
    }

    [Fact]
    public void RestoreSnapshot_RespecsDeadKeysToUnallocated()
    {
        // An old save carries points spent on keys the new menu drops —
        // they must RETURN as unallocated points on that skill, never
        // silently vanish (no-silent-losses rule).
        var snapshot = new Core.SkillSnapshot();
        snapshot.Skills["mining"] = new Core.SkillDataSnapshot
        {
            Level = 5,
            Xp = 500f,
            StatPoints = 5,
            SubStats = new Dictionary<string, int>
            {
                ["efficiency"] = 1,          // kept — on mining's menu
                ["stamina_reduction"] = 2,   // dead on mining — respec
            },
        };

        var sm = new SkillManager();
        int refunded = sm.RestoreSnapshot(snapshot);

        var mining = sm.GetSkill("mining");
        Assert.Equal(1f, mining.SubStats["efficiency"]);      // kept
        Assert.False(mining.SubStats.ContainsKey("stamina_reduction")); // gone
        Assert.Equal(7, mining.UnallocatedPoints);            // 5 + 2 respec'd
        Assert.Equal(2, refunded);
    }

    [Fact]
    public void RestoreSnapshot_NoDeadKeys_RefundsNothing()
    {
        var snapshot = new Core.SkillSnapshot();
        snapshot.Skills["cooking"] = new Core.SkillDataSnapshot
        {
            Level = 3,
            Xp = 200f,
            StatPoints = 4,
            SubStats = new Dictionary<string, int> { ["harvest_boost"] = 3 },
        };

        var sm = new SkillManager();
        int refunded = sm.RestoreSnapshot(snapshot);

        Assert.Equal(0, refunded);
        Assert.Equal(3f, sm.GetSkill("cooking").SubStats["harvest_boost"]);
        Assert.Equal(4, sm.GetSkill("cooking").UnallocatedPoints);
    }

    [Fact]
    public void SnapshotRoundTrip_KeepsNewKeys()
    {
        var sm = new SkillManager();
        sm.AddXpWithNotification("cooking", 83f); // 3 points
        sm.SpendPoint("cooking", "harvest_boost");
        sm.SpendPoint("cooking", "harvest_boost");
        sm.SpendPoint("cooking", "harvest_boost");

        var snapshot = sm.GetSnapshot();
        var restored = new SkillManager();
        restored.RestoreSnapshot(snapshot);

        Assert.Equal(3f, restored.GetSkill("cooking").SubStats["harvest_boost"]);
        Assert.Equal(0, restored.GetSkill("cooking").UnallocatedPoints);
    }

    [Fact]
    public void GetSubStatPoints_ReadsRawInvestedPoints()
    {
        var sm = new SkillManager();
        sm.AddXpWithNotification("attack", 83f); // 3 points
        sm.SpendPoint("attack", "power");
        sm.SpendPoint("attack", "power");

        // Raw invested points — no level bonus, no gear, no scaling.
        Assert.Equal(2f, sm.GetSubStatPoints("attack", "power"));
        Assert.Equal(0f, sm.GetSubStatPoints("attack", "speed"));
        Assert.Equal(0f, sm.GetSubStatPoints("attack", "charisma")); // unknown key
        Assert.Equal(0f, sm.GetSubStatPoints("not_a_skill", "power")); // unknown skill
    }

    // ─── Attack: power + speed (combat seams) ───────────────────────────

    [Fact]
    public void AttackPower_Points_AddFlatDamage()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 260f, 100f);

        // Unarmed + 5 power points: damage 1 + 0 + 5 - 2 defence = 4.
        GrantAttackPoints(skills, "power", 5);
        var result = system.PlayerAttack(player, inv, skills);

        Assert.True(result.Success);
        Assert.Equal(4, (int)Assert.Single(system.DamageNumbers).Value);
    }

    [Fact]
    public void AttackSpeed_Points_CutCooldown()
    {
        var (system, player, inv, skills) = MakeCombat(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 260f, 100f);

        // 0 points: base cooldown 1.5s — 0.5s in, still not ready.
        Assert.True(system.PlayerAttack(player, inv, skills).Success);
        system.Tick(0.5f, player);
        Assert.False(system.PlayerAttack(player, inv, skills).Success);

        // Cooldown elapsed; re-arm, then prove 10 speed points reach the
        // 0.3s floor: after 0.35s the second attack lands.
        system.Tick(2f, player);
        GrantAttackPoints(skills, "speed", 10);
        Assert.True(system.PlayerAttack(player, inv, skills).Success);
        system.Tick(0.35f, player);
        Assert.True(system.PlayerAttack(player, inv, skills).Success,
            "10 speed points (1.2s cut) should reach the 0.3s floor by 0.35s");
    }

    // ─── Agility: sprint_cost + jump_cost (movement seams) ─────────────

    [Fact]
    public void SprintCost_Points_HalveDrain_AtZeroCostUnchanged()
    {
        var (player, actions, skills) = AgilePlayer();

        // 0 points: exact legacy drain (existing pinned math).
        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        Assert.Equal(Constants.MaxStaminaBase - Constants.SprintStaminaDrainPerSecond * 0.5f,
            actions.Stamina.Current, 3);

        // 3 points: −5%/pt → 15% cut → dt 0.5 drains 4·0.5·0.85.
        player.SkillManager!.AddXpWithNotification("agility", SkillManager.XpForLevel(2) + 1f);
        player.SkillManager.SpendPoint("agility", "sprint_cost");
        player.SkillManager.SpendPoint("agility", "sprint_cost");
        player.SkillManager.SpendPoint("agility", "sprint_cost");
        Assert.Equal(3f, player.SkillManager.GetSubStatPoints("agility", "sprint_cost"));

        float before = actions.Stamina.Current;
        player.UpdateSprint(sprintHeld: true, moving: true, dt: 0.5f);
        Assert.Equal(before - Constants.SprintStaminaDrainPerSecond * 0.5f * 0.85f,
            actions.Stamina.Current, 3);
    }

    [Fact]
    public void JumpCost_Points_CutJumpCost_AtZeroCostUnchanged()
    {
        var (player, actions, skills) = AgilePlayer();

        // 0 points: exact legacy cost.
        Assert.True(player.TryJump());
        Assert.Equal(Constants.MaxStaminaBase - Constants.JumpStaminaCost, actions.Stamina.Current, 3);

        // 3 points: −5%/pt → 15% cut → TryJump costs 5·0.85.
        player.SkillManager!.AddXpWithNotification("agility", SkillManager.XpForLevel(2) + 1f);
        player.SkillManager.SpendPoint("agility", "jump_cost");
        player.SkillManager.SpendPoint("agility", "jump_cost");
        player.SkillManager.SpendPoint("agility", "jump_cost");
        Assert.Equal(3f, player.SkillManager.GetSubStatPoints("agility", "jump_cost"));

        player.UpdateJump(5f); // land the first jump
        float before = actions.Stamina.Current;
        Assert.True(player.TryJump());
        Assert.Equal(before - Constants.JumpStaminaCost * 0.85f, actions.Stamina.Current, 3);
    }

    // ─── Craft seam: harvest_boost + efficiency for production skills ──

    [Fact]
    public void CraftHarvestBoost_Points_GrantExtraOutput()
    {
        var (system, inventory, skills) = MakeCraftable();
        GrantStat(skills, "crafting", "harvest_boost", 200f); // deterministic hit

        var result = system.Craft("sticks", inventory, skills);

        Assert.True(result.Success, result.Message);
        int sticks = inventory.GetItemQuantity("sticks");
        Assert.True(sticks == 4, $"200% boost must add +1 output, got {sticks}");
    }

    [Fact]
    public void CraftEfficiency_Points_SaveOneInputUnit()
    {
        var (system, inventory, skills) = MakeCraftable();
        GrantStat(skills, "crafting", "efficiency", 100f); // deterministic hit

        system.Craft("sticks", inventory, skills);

        // Sticks: 2 oak_logs -> 3 stick. 100% save: one log survives.
        int logs = inventory.GetItemQuantity("oak_logs");
        Assert.True(logs == 5, $"100% efficiency must save one log, got {logs}");
    }

    [Fact]
    public void Craft_NoStatPoints_KeepsExactLegacyQuantities()
    {
        var (system, inventory, skills) = MakeCraftable();

        var result = system.Craft("sticks", inventory, skills);

        Assert.True(result.Success, result.Message);
        Assert.Equal(3, inventory.GetItemQuantity("stick"));
        Assert.Equal(3, inventory.GetItemQuantity("oak_logs")); // 5 - 2
    }

    // ─── Firemaking: duration + fuel_saver (fire seams) ────────────────

    [Fact]
    public void FireDuration_Points_ExtendFire()
    {
        var skills = new SkillManager();
        GrantStat(skills, "firemaking", "duration", 4f);
        var firemaking = new DontStarveRuneScape.Skills.Firemaking.FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("log", 2);

        var result = firemaking.LightFire([("log", 2)], inv, 0f, 0f, skills);

        Assert.True(result.Success);
        var fire = Assert.Single(firemaking.GetActiveFires());
        Assert.Equal(50f, fire.MaxTime, 3); // 30 base + 4 pts * 5s
    }

    [Fact]
    public void FireFuelSaver_Points_RefundOneFuelUnit()
    {
        var skills = new SkillManager();
        GrantStat(skills, "firemaking", "fuel_saver", 25f); // 4%/pt -> 100%: guaranteed
        var firemaking = new DontStarveRuneScape.Skills.Firemaking.FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("log", 3);

        var result = firemaking.LightFire([("log", 2)], inv, 0f, 0f, skills);

        Assert.True(result.Success);
        // 2 consumed, 1 in stock, +1 refunded -> 2 remain.
        Assert.Equal(2, inv.GetItemQuantity("log"));
    }

    [Fact]
    public void Fire_NoSkillManager_KeepsLegacyBehavior()
    {
        var firemaking = new DontStarveRuneScape.Skills.Firemaking.FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("log", 2);

        var result = firemaking.LightFire([("log", 2)], inv, 0f, 0f);

        Assert.True(result.Success);
        var fire = Assert.Single(firemaking.GetActiveFires());
        Assert.Equal(30f, fire.MaxTime, 3); // flat legacy 30s
        Assert.Equal(0, inv.GetItemQuantity("log"));
    }

    // ─── Construction: build_speed (colony site tick) ──────────────────

    [Fact]
    public void ConstructionBuildSpeed_Points_SpeedWorkerSiteProgress()
    {
        // The founder's construction competence speeds the colony's
        // builds: +5%/pt on the worker's site tick (raw read).
        var harness = new ColonyHarness();
        var site = harness.PlaceCampfireSite();
        GrantStat(harness.Skills, "construction", "build_speed", 10f);

        harness.Tick(60);

        Assert.True(site.IsUnderConstruction == false,
            "10 build_speed points (+50% progress) should complete the 20s build by 60s of ticks");
    }

    [Fact]
    public void Construction_ZeroPoints_KeepsLegacySiteTiming()
    {
        var harness = new ColonyHarness();
        var site = harness.PlaceCampfireSite();

        harness.Tick(60);

        Assert.True(site.IsUnderConstruction,
            "0 points must keep the legacy 80-tick (20s) timing — incomplete at 60s");
    }

    // ─── Fishing: rare_luck + honest harvest_boost/success_rate ────────

    [Fact]
    public void RareTable_LuckWidensTheWindows()
    {
        // Same roll, same level: no luck says old_boot, 1 luck point says
        // pearl (the window edge crossed) — luck compounds the grind.
        Assert.Equal("old_boot", FishingRareTable.Roll(0.0105f, 1, 0f));
        Assert.Equal("pearl", FishingRareTable.Roll(0.0105f, 1, 1f));
        // Luck never guarantees: the top of the range stays empty.
        Assert.Null(FishingRareTable.Roll(0.999f, 99, 50f));
        // Default luck keeps the pinned windows byte-identical.
        Assert.Equal("pearl", FishingRareTable.Roll(0.005f, 1));
        Assert.Equal("old_boot", FishingRareTable.Roll(0.02f, 1));
        Assert.Null(FishingRareTable.Roll(0.06f, 1));
    }

    [Fact]
    public void FishingLuck_IsStashedAtCastStart()
    {
        var system = new ActionSystem();
        var sm = new SkillManager();
        sm.GetSkill("fishing").SubStats["rare_luck"] = 7f;
        var inv = new Inv();
        inv.AddItem("fishing_rod", 1);

        Assert.Null(system.StartAction(ActionType.Fishing, FishSpot(), sm, inv));

        Assert.Equal(7f, system.Active.FishingLuck);
    }

    [Fact]
    public void FishingSuccessRate_NormalizedToFamilyScale()
    {
        // One point of fishing success_rate must buy the same threshold
        // swing as the other gatherers (×100), not ×1.
        var system = new ActionSystem();
        var sm = new SkillManager();
        sm.GetSkill("fishing").SubStats["success_rate"] = 10f;
        var inv = new Inv();
        inv.AddItem("fishing_rod", 1);

        Assert.Null(system.StartAction(ActionType.Fishing, FishSpot(), sm, inv));

        Assert.Equal(1000f, system.Active.SuccessRateBonus, 3);
    }

    [Fact]
    public void FishingHarvestBoost_FinallyLandsExtraFish()
    {
        // harvest_boost was stashed for fishing but dead — the yield arm
        // is the fix. 200% (raw) guarantees +1.
        var system = new ActionSystem();
        var sm = new SkillManager();
        sm.GetSkill("fishing").SubStats["success_rate"] = 1000f;
        sm.GetSkill("fishing").SubStats["harvest_boost"] = 200f;
        var inv = new Inv();
        inv.AddItem("fishing_rod", 1);

        Assert.Null(system.StartAction(ActionType.Fishing, FishSpot(), sm, inv));
        var result = FinishCast(system);

        Assert.True(result!.Success);
        Assert.Equal(2, result.Quantity); // 1 base + 1 boost
    }

    [Fact]
    public void ForagingHarvestBoost_FinallyLandsExtraYield()
    {
        // Foraging's harvest_boost was stashed but CalculateHarvest never
        // consumed it — the bonus param is the fix.
        var system = new ActionSystem();
        var node = new ResourceNode("bush", new ResourceDef
        {
            Name = "Berry Bush",
            YieldItem = "berries",
            Yield = 2,
            Xp = 5f,
            Tier = 1,
            Regrow = 60f,
            Seasons = [],
            DepletionCount = 100,
        }, 100f);
        var sm = new SkillManager();
        sm.GetSkill("foraging").SubStats["success_rate"] = 1000f;
        sm.GetSkill("foraging").SubStats["harvest_boost"] = 200f;

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, new Inv()));
        var result = system.Update(0.016f);

        Assert.True(result!.Success);
        Assert.Equal(3, result.Quantity); // 2 base + 1 boost
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static void GrantAttackPoints(SkillManager skills, string stat, int points)
    {
        // Levels grant 3 points each; spend them on the stat.
        int levels = (points + 2) / 3;
        skills.AddXpWithNotification("attack", SkillManager.XpForLevel(1 + levels) + 1f);
        for (int i = 0; i < points; i++)
            Assert.True(skills.SpendPoint("attack", stat), $"spend {stat} #{i}");
    }

    private static void GrantStat(SkillManager skills, string skillId, string stat, float points)
    {
        // Direct SubStats assignment: tests assert consumer math, not
        // the point-spending path (that lives above).
        skills.GetSkill(skillId).SubStats[stat] = points;
    }

    private static (DontStarveRuneScape.Combat.CombatSystem, Player, Inv, SkillManager) MakeCombat(
        float px, float py)
    {
        var player = new Player(px, py)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        return (new DontStarveRuneScape.Combat.CombatSystem(), player, player.Inventory!, player.SkillManager!);
    }

    private static MonsterDef TestMonsterDef() => new()
    {
        MonsterId = "test_wolf",
        Name = "Test Wolf",
        Hp = 10,
        Attack = 3,
        Defence = 2,
        Speed = 40f,
        AggressionRange = 150f,
        FleeRange = 300f,
        AttackCooldown = 1.5f,
        XpReward = 7,
        IsHostile = true,
        SpriteKey = "monster/wolf",
        LootTable = [new MonsterLootEntry { ItemId = "test_pelt", Chance = 1f }],
    };

    private static (Player player, ActionSystem actions, SkillManager skills) AgilePlayer()
    {
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        var skills = new SkillManager();
        player.ActionSystem = actions;
        player.SkillManager = skills;
        return (player, actions, skills);
    }

    private static (CraftingSystem system, Inv inventory, SkillManager skills) MakeCraftable()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inv();
        inventory.AddItem("oak_logs", 5);
        return (system, inventory, new SkillManager());
    }

    private static ResourceNode FishSpot() =>
        new("fish_spot", new ResourceDef
        {
            Id = "fish_spot",
            Name = "Fish Spot",
            Biome = "coastal",
            Category = "special",
            YieldItem = "raw_fish",
            Yield = 1,
            Xp = 15f,
            DepletionCount = 100,
            Regrow = 60f,
            ToolRequirement = "fishing_rod",
            RequiredLevel = 1,
            Seasons = [],
        }, 100f);

    private static ActionResult? FinishCast(ActionSystem system)
    {
        float remaining = Constants.FishingCastSeconds;
        while (remaining > 0.25f)
        {
            Assert.Null(system.Update(0.25f));
            remaining -= 0.25f;
        }
        return system.Update(0.25f);
    }

    /// <summary>Minimal colony harness for the construction build_speed
    /// seam: a founded colony, a paid campfire site, and a builder walking
    /// the dispatch (RecruitSkillTests shape, trimmed to this slice).</summary>
    private sealed class ColonyHarness
    {
        public TileMap World = MakeWorld();
        public Player Player = new(3.5f * Constants.TileSize, 3.5f * Constants.TileSize)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        public NPCSystem Npcs = new();
        public ColonySystem Colony = new();
        public BuildingSystem Buildings = MakeBuildings();
        public CraftingSystem Crafting = MakeCrafting();
        public FoodRegistry Foods = MakeFoods();
        public RecruitmentSystem Recruits = new();
        public SkillManager Skills => Player.SkillManager!;

        private static TileMap MakeWorld(int size = 30)
        {
            var world = new TileMap(size, size);
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
            return world;
        }

        private static BuildingSystem MakeBuildings()
        {
            var registry = new StructureDefRegistry();
            registry.LoadAll();
            return new BuildingSystem { Registry = registry };
        }

        private static CraftingSystem MakeCrafting()
        {
            var recipes = new RecipeRegistry();
            recipes.LoadAll();
            return new CraftingSystem { Registry = recipes };
        }

        private static FoodRegistry MakeFoods()
        {
            var loader = new DataLoader();
            loader.LoadAll();
            return new FoodRegistry(loader.ItemsData);
        }

        public Structure PlaceCampfireSite()
        {
            Assert.True(Colony.FoundAt(
                10.5f * Constants.TileSize, 10.5f * Constants.TileSize, World));
            var worker = new RecruitNpc
            {
                NpcId = "builder",
                WorldX = 12.5f * Constants.TileSize,
                WorldY = 10.5f * Constants.TileSize,
                IsActive = true,
                IsRecruited = true,
                RecruitBehavior = "assistant",
                ColonyHunger = 100f,
                ColonyRest = 100f,
                Health = 100,
                MaxHealth = 100,
            };
            Npcs.NPCs.Add(worker);

            var def = Buildings.Registry!.GetStructure("campfire")!;
            var site = new Structure
            {
                StructureId = def.Id,
                StructureDef = def,
                TileX = 14,
                TileY = 10,
                WorldX = 14.5f * Constants.TileSize,
                WorldY = 10.5f * Constants.TileSize,
                IsActive = true,
                IsUnderConstruction = true,
                ConstructionMaterialsPaid = true,
            };
            Buildings.Structures.Add(site);
            return site;
        }

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony, Buildings,
                    Crafting, Player.SkillManager, null, Foods);
        }
    }
}
