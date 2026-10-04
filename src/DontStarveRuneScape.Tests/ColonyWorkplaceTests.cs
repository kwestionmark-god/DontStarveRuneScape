namespace DontStarveRuneScape.Tests;

using System.Text.Json;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Colony workplaces from slice C of the colony fusion: effective recipe
/// inputs (fuel/extras folded, no double charge), crafting against the
/// colony stockpile through IItemStorage, station gates, colony-aware
/// structure placement (construction sites, combined payment), and guard
/// combat through the shared monster-death path.
/// </summary>
public class ColonyWorkplaceTests
{
    private static Dictionary<string, object> Row(params (string Key, object Value)[] fields)
    {
        var row = new Dictionary<string, object>();
        foreach (var (key, value) in fields)
            row[key] = JsonSerializer.SerializeToElement(value);
        return row;
    }

    private static TileMap DryWorld(int size = 40)
    {
        var world = new TileMap(size, size);
        for (int x = 0; x < size; x++)
            for (int y = 0; y < size; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        return world;
    }

    private static ColonySystem FoundedColony(TileMap world, int tileX = 3, int tileY = 3)
    {
        var colony = new ColonySystem();
        Assert.True(colony.FoundAt((tileX + 0.5f) * Constants.TileSize, (tileY + 0.5f) * Constants.TileSize, world));
        return colony;
    }

    // ─── Effective recipe inputs ─────────────────────────────────────────

    [Fact]
    public void FromData_FoldsFuelAndExtraInputsIntoRecipeInputs()
    {
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "alloy"), ("output_item", "alloy_bar"),
            ("input_items", new object[] { new object[] { "copper_ore", 1 } }),
            ("fuel_item", "charcoal"), ("base_fuel_cost", 2),
            ("extra_input", "tin_ore"), ("extra_quantity", 1))]);

        var inputs = recipes["alloy"].Inputs;
        Assert.Equal(3, inputs.Length);
        Assert.Contains(("copper_ore", 1), inputs);
        Assert.Contains(("charcoal", 2), inputs); // folded from base_fuel_cost
        Assert.Contains(("tin_ore", 1), inputs); // folded from extra_input
    }

    [Fact]
    public void FromData_FuelFoldTakesTheMaximum_NotTheSum()
    {
        // The row already lists charcoal 1 in input_items; the metadata cost
        // is 2. The unified inputs must carry the max (2), never a double
        // charge (3).
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "smelt"), ("output_item", "ingot"),
            ("input_items", new object[]
            {
                new object[] { "ore", 1 }, new object[] { "charcoal", 1 },
            }),
            ("fuel_item", "charcoal"), ("base_fuel_cost", 2))]);

        var recipe = recipes["smelt"];
        var charcoal = Assert.Single(recipe.Inputs.Where(i => i.ItemId == "charcoal"));
        Assert.Equal(2, charcoal.Quantity);
        Assert.Equal(2, recipe.Inputs.Length);
    }

    [Fact]
    public void FromData_ParsesRequiresStructure()
    {
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "forge_sword"), ("output_item", "sword"),
            ("requires_structure", "anvil"))]);
        Assert.Equal("anvil", recipes["forge_sword"].RequiresStructure);
    }

    [Fact]
    public void LoadAll_RealSmeltRecipes_DoNotDoubleChargeFuel()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();

        var bronze = registry.GetRecipe("smelt_bronze");
        Assert.NotNull(bronze);
        var charcoal = Assert.Single(bronze!.Inputs.Where(i => i.ItemId == "charcoal"));
        Assert.Equal(1, charcoal.Quantity);
        // Bronze needs its alloy extra on top of the copper ingot.
        Assert.Contains(bronze.Inputs, i => i.ItemId == "tin_ore");

        var copper = registry.GetRecipe("smelt_copper");
        Assert.NotNull(copper);
        var copperCharcoal = Assert.Single(copper!.Inputs.Where(i => i.ItemId == "charcoal"));
        Assert.Equal(1, copperCharcoal.Quantity);
    }

    // ─── Crafting against colony storage + station gates ──────────────────

    [Fact]
    public void Craft_RunsAgainstColonyStockpile_AsIItemStorage()
    {
        var registry = new RecipeRegistry();
        registry.LoadAll();
        var system = new CraftingSystem { Registry = registry };
        var world = DryWorld();
        var colony = FoundedColony(world);
        colony.Store("oak_logs", 5);
        var skills = new SkillManager();

        var result = system.Craft("sticks", colony, skills);

        Assert.True(result.Success, result.Message);
        Assert.Equal(4, colony.GetItemQuantity("oak_logs"));
        Assert.Equal(3, colony.GetItemQuantity("stick"));
        Assert.Equal(3f, skills.GetSkill("crafting").Xp);
    }

    [Fact]
    public void Craft_StationGate_BlocksMissingStation()
    {
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "anvil_work"), ("output_item", "plate"),
            ("input_items", new object[] { new object[] { "ingot", 1 } }),
            ("required_skill", "crafting"), ("required_level", 1),
            ("requires_structure", "anvil"))]);
        var system = new CraftingSystem { Registry = MergeRegistry(recipes) };
        var inventory = new Inv();
        inventory.AddItem("ingot", 5);

        var blocked = system.Craft("anvil_work", inventory, new SkillManager(),
            new HashSet<string> { "crafting_station" });
        Assert.False(blocked.Success);
        Assert.Equal(5, inventory.GetItemQuantity("ingot")); // nothing consumed

        var allowed = system.Craft("anvil_work", inventory, new SkillManager(),
            new HashSet<string> { "anvil" });
        Assert.True(allowed.Success, allowed.Message);
        Assert.Equal(4, inventory.GetItemQuantity("ingot"));
    }

    [Fact]
    public void Craft_CampfireGate_AcceptsHeatStationAlternatives()
    {
        var recipes = RecipeRegistry.FromData([Row(
            ("recipe_id", "cook"), ("output_item", "meal"),
            ("input_items", new object[] { new object[] { "raw_meat", 1 } }),
            ("required_skill", "cooking"), ("required_level", 1),
            ("requires_campfire", true))]);
        var registry = new RecipeRegistry();
        foreach (var recipe in recipes.Values)
            registry.Recipes[recipe.RecipeId] = recipe;
        var system = new CraftingSystem { Registry = registry };
        var inventory = new Inv();
        inventory.AddItem("raw_meat", 4);

        foreach (var station in new[] { "campfire", "cooking_station", "furnace", "smelter" })
        {
            var result = system.Craft("cook", inventory, new SkillManager(),
                new HashSet<string> { station });
            Assert.True(result.Success, $"{station} must satisfy the heat gate");
        }
        Assert.Equal(0, inventory.GetItemQuantity("raw_meat"));

        inventory.AddItem("raw_meat", 1);
        var denied = system.Craft("cook", inventory, new SkillManager(), new HashSet<string>());
        Assert.False(denied.Success);
    }

    private static RecipeRegistry MergeRegistry(Dictionary<string, CraftRecipe> recipes)
    {
        var registry = new RecipeRegistry();
        foreach (var recipe in recipes.Values)
            registry.Recipes[recipe.RecipeId] = recipe;
        return registry;
    }

    // ─── Colony-aware structure placement ─────────────────────────────────

    [Fact]
    public void PlaceStructure_RejectsTileThatAlreadyHasAStructure()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var inventory = new Inv();
        inventory.AddItem("stick", 10);
        inventory.AddItem("stone", 10);
        var world = DryWorld(8);

        Assert.True(system.PlaceStructure("campfire", 3, 3, world, inventory, new SkillManager()).Success);
        var second = system.PlaceStructure("campfire", 3, 3, world, inventory, new SkillManager());

        Assert.False(second.Success);
        Assert.Equal(1, system.Structures.Count(s => s.TileX == 3 && s.TileY == 3));
    }

    [Fact]
    public void PlaceStructure_InsideColony_CreatesConstructionSite_AndTransfersMaterials()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var world = DryWorld(12);
        var colony = FoundedColony(world);
        var inventory = new Inv();
        inventory.AddItem("stick", 2);
        inventory.AddItem("stone", 2);

        var result = system.PlaceStructure("campfire", 4, 3, world, inventory, new SkillManager(), colony);

        Assert.True(result.Success, result.Message);
        Assert.Contains("construction site", result.Message);
        var site = Assert.Single(system.Structures);
        Assert.True(site.IsUnderConstruction);
        Assert.Equal("Awaiting materials", site.WorkStatus);
        // Materials the player carried were banked into the stockpile for
        // the site instead of being consumed immediately.
        Assert.Equal(0, inventory.GetItemQuantity("stick"));
        Assert.Equal(2, colony.GetItemQuantity("stick"));
        Assert.Equal(2, colony.GetItemQuantity("stone"));
    }

    [Fact]
    public void PlaceStructure_OutsideColony_PaysFromBothStores()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var world = DryWorld(40);
        var colony = FoundedColony(world);
        colony.Store("stick", 1);
        colony.Store("stone", 2);
        var inventory = new Inv();
        inventory.AddItem("stick", 1);

        // Tile (30, 30) is far outside the 10-tile work radius: the normal
        // build path charges inventory first, then the colony stockpile.
        var result = system.PlaceStructure("campfire", 30, 30, world, inventory, new SkillManager(), colony);

        Assert.True(result.Success, result.Message);
        var structure = Assert.Single(system.Structures);
        Assert.False(structure.IsUnderConstruction);
        Assert.Equal(0, inventory.GetItemQuantity("stick"));
        Assert.Equal(0, colony.GetItemQuantity("stick"));
        Assert.Equal(0, colony.GetItemQuantity("stone"));
    }

    [Fact]
    public void PlaceStructure_CombinedShortage_FailsWithoutCharging()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var system = new BuildingSystem { Registry = registry };
        var world = DryWorld(40);
        var colony = FoundedColony(world);
        colony.Store("stick", 1);
        var inventory = new Inv();
        inventory.AddItem("stone", 2);

        var result = system.PlaceStructure("campfire", 30, 30, world, inventory, new SkillManager(), colony);

        Assert.False(result.Success);
        Assert.Empty(system.Structures);
        Assert.Equal(1, colony.GetItemQuantity("stick"));
        Assert.Equal(2, inventory.GetItemQuantity("stone"));
    }

    // ─── Guard combat ─────────────────────────────────────────────────────

    private static MonsterDef TestMonsterDef(int hp = 10) => new()
    {
        MonsterId = "test_wolf",
        Name = "Test Wolf",
        Hp = hp,
        Attack = 3,
        Defence = 2,
        Speed = 40f,
        AggressionRange = 150f,
        FleeRange = 300f,
        AttackCooldown = 0.5f,
        XpReward = 7,
        IsHostile = true,
        SpriteKey = "monster/wolf",
        LootTable = [new MonsterLootEntry { ItemId = "test_pelt", Chance = 1f }],
    };

    private static RecruitNpc GuardAt(float x, float y, string id = "g1") => new()
    {
        NpcId = id,
        Name = "Guard",
        IsRecruited = true,
        RecruitBehavior = "guard",
        WorldX = x,
        WorldY = y,
        Health = 100,
        MaxHealth = 100,
        IsActive = true,
    };

    [Fact]
    public void GuardAttack_DamagesHostile_AndRespectsCooldown()
    {
        var system = new CombatSystem();
        var guard = GuardAt(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 230f, 100f);
        var monster = Assert.Single(system.Monsters);
        var inv = new Inv();
        var skills = new SkillManager();

        var first = system.GuardAttack(guard, monster, inv, skills, 0.016f);
        Assert.True(first.Success, first.Message);
        Assert.Equal(8, monster.Health); // max(1, 4 - 2 defence) = 2 damage
        Assert.True(Assert.Single(system.DamageNumbers).Value > 0);

        var second = system.GuardAttack(guard, monster, inv, skills, 0.016f);
        Assert.False(second.Success); // still on cooldown
        Assert.Equal(8, monster.Health);

        var third = system.GuardAttack(guard, monster, inv, skills, 2f); // cooldown elapsed
        Assert.True(third.Success);
        Assert.Equal(6, monster.Health);
    }

    [Fact]
    public void GuardAttack_Kill_GrantsLootAndAttackXp()
    {
        var system = new CombatSystem();
        var guard = GuardAt(200f, 100f);
        system.SpawnMonster(TestMonsterDef(hp: 2), 230f, 100f);
        var monster = Assert.Single(system.Monsters);
        var inv = new Inv();
        var skills = new SkillManager();

        var result = system.GuardAttack(guard, monster, inv, skills, 0.016f);

        Assert.True(result.Success);
        Assert.True(result.Killed);
        Assert.Empty(system.Monsters); // removed + respawn scheduled
        Assert.Equal(1, inv.GetItemQuantity("test_pelt")); // loot via the player path
        Assert.True(skills.GetSkill("attack").Xp > 0);
        Assert.Contains("kill", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuardAttack_RejectsDeadOrPeacefulTargets()
    {
        var system = new CombatSystem();
        var guard = GuardAt(200f, 100f);
        system.SpawnMonster(TestMonsterDef(), 230f, 100f);
        var monster = Assert.Single(system.Monsters);
        monster.IsHostile = false;
        Assert.False(system.GuardAttack(guard, monster, new Inv(), new SkillManager(), 0.016f).Success);

        monster.IsHostile = true;
        monster.Health = 0;
        Assert.False(system.GuardAttack(guard, monster, new Inv(), new SkillManager(), 0.016f).Success);
    }

    [Fact]
    public void Monsters_TargetAndInjureRecruitedResidents()
    {
        var system = new CombatSystem();
        var player = new Core.Player(400f, 100f)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        var npcSystem = new NPCSystem();
        var resident = GuardAt(120f, 100f, "r1");
        resident.Health = 4; // one hit from deactivation
        npcSystem.NPCs.Add(resident);
        system.SpawnMonster(TestMonsterDef(), 100f, 100f); // 20px from the resident

        // Tick until the monster lands its attack (aggro range covers the
        // resident; the player is far away).
        for (int i = 0; i < 60 && resident.IsActive; i++)
            system.Tick(0.1f, player, npcSystem);

        Assert.False(resident.IsActive); // zero health deactivates the resident
        Assert.Equal(0, resident.Health);
    }
}
