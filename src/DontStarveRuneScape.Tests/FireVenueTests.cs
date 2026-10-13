namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Interactions;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Firemaking narrowing — the fire venue. Spec:
/// docs/superpowers/specs/2026-10-10-firemaking-narrowing-fire-venue-design.md
/// </summary>
public class FireVenueTests
{
    // ─── §1 AddXp root fix: recompute Level + points ─────────────────────

    [Fact]
    public void AddXp_RecomputesLevel_And_GrantsPoints()
    {
        var sm = new SkillManager();

        sm.AddXp("firemaking", SkillManager.XpForLevel(2) + 1f);

        Assert.True(sm.GetSkillLevel("firemaking") >= 2,
            $"raw AddXp must recompute Level: got {sm.GetSkillLevel("firemaking")}");
        Assert.True(sm.GetSkill("firemaking").UnallocatedPoints >= 1,
            "crossing a level boundary must grant stat points");
    }

    [Fact]
    public void AddXp_BelowBoundary_LeavesLevelAlone()
    {
        var sm = new SkillManager();

        sm.AddXp("firemaking", 50f);

        Assert.Equal(1, sm.GetSkillLevel("firemaking"));
        Assert.Equal(0f, sm.GetSkill("firemaking").UnallocatedPoints);
    }

    // ─── §2 Fuel table drives LightFire duration ──────────────────────────

    [Fact]
    public void FuelTable_Charcoal_BurnsLongerThanLogs()
    {
        var firemaking = new FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("charcoal", 2);
        var logsInv = new Inv();
        logsInv.AddItem("oak_logs", 2);

        var charcoal = firemaking.LightFire([("charcoal", 2)], inv, 0f, 0f);
        var logs = firemaking.LightFire([("oak_logs", 2)], logsInv, 0f, 0f);

        Assert.True(charcoal.Success);
        Assert.True(logs.Success);
        var charcoalFire = firemaking.GetActiveFires()[0];   // lit first
        var logsFire = firemaking.GetActiveFires()[^1];     // lit second
        Assert.True(charcoalFire.MaxTime > logsFire.MaxTime,
            $"charcoal (45s/unit) must outlast logs (30s/unit): {charcoalFire.MaxTime} vs {logsFire.MaxTime}");
    }

    [Fact]
    public void FuelTable_Sticks_BurnFasterThanLogs()
    {
        var firemaking = new FiremakingSkill();
        var sticksInv = new Inv();
        sticksInv.AddItem("stick", 2);
        var logsInv = new Inv();
        logsInv.AddItem("oak_logs", 2);

        var sticks = firemaking.LightFire([("stick", 2)], sticksInv, 0f, 0f);
        var logs = firemaking.LightFire([("oak_logs", 2)], logsInv, 0f, 0f);

        Assert.True(sticks.Success);
        Assert.True(logs.Success);
        var sticksFire = firemaking.GetActiveFires()[0];   // lit first
        var logsFire = firemaking.GetActiveFires()[^1];    // lit second
        Assert.True(logsFire.MaxTime > sticksFire.MaxTime,
            $"logs (30s/unit) must outlast sticks (15s/unit): {logsFire.MaxTime} vs {sticksFire.MaxTime}");
    }

    [Fact]
    public void FuelTable_UnknownFuel_KeepsLegacyPerUnitDuration()
    {
        var firemaking = new FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("mystery_fuel", 2);

        var result = firemaking.LightFire([("mystery_fuel", 2)], inv, 0f, 0f);

        Assert.True(result.Success);
        var fire = Assert.Single(firemaking.GetActiveFires());
        Assert.Equal(60f, fire.MaxTime, 3); // 30s legacy default per unit
    }

    // ─── §3 Hearth placement: auto-light + construction XP ────────────────

    private sealed class Harness
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
        public SkillManager Skills = new();
        public FoodRegistry Foods = MakeFoods();
        public RecruitmentSystem Recruits = new();
        public FiremakingSkill Firemaking = new();

        private static TileMap MakeWorld(int size = 24)
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

        public void Tick(int n)
        {
            for (int i = 0; i < n; i++)
                Recruits.Tick(0.25f, Npcs, Player, World, Colony,
                    Buildings, Crafting, Skills, null, Foods, null, null, null, Firemaking, null);
        }
    }

    [Fact]
    public void PlaceCampfire_LightsFire_AndTrainsConstruction()
    {
        var h = new Harness();
        h.Player.Inventory!.AddItem("stick", 2);
        h.Player.Inventory!.AddItem("stone", 2);

        var result = h.Buildings.PlaceStructure("campfire", 5, 5, h.World,
            h.Player.Inventory!, h.Skills, null, h.Firemaking);

        Assert.True(result.Success, result.Message);
        var structure = Assert.Single(h.Buildings.Structures);
        var fire = Assert.Single(h.Firemaking.GetActiveFires());
        Assert.Equal(structure.WorldX, fire.WorldX, 1);
        Assert.Equal(structure.WorldY, fire.WorldY, 1);
        Assert.True(h.Firemaking.GetFiresInRadius(
            structure.WorldX, structure.WorldY, 8f).Count > 0);
        Assert.True(h.Skills.GetSkill("construction").Xp > 0f,
            "placing a hearth must train construction");
    }

    [Fact]
    public void PlaceCampfire_NullFiremaking_StillPlaces_NoFire()
    {
        var h = new Harness();
        h.Player.Inventory!.AddItem("stick", 2);
        h.Player.Inventory!.AddItem("stone", 2);

        var result = h.Buildings.PlaceStructure("campfire", 5, 5, h.World,
            h.Player.Inventory!, h.Skills, null, null);

        Assert.True(result.Success, result.Message);
        Assert.Single(h.Buildings.Structures);
        Assert.Empty(h.Firemaking.GetActiveFires());
    }

    [Fact]
    public void Firepit_Def_BurnsLongerThanCampfire()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var campfire = registry.GetStructure("campfire");
        var firepit = registry.GetStructure("firepit");

        Assert.NotNull(firepit);
        Assert.True(firepit!.LightSeconds > campfire!.LightSeconds,
            $"firepit ({firepit.LightSeconds}s) must outlast campfire ({campfire.LightSeconds}s)");
    }

    [Fact]
    public void PlaceFirepit_Costs_Stone6Stick2()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        var firepit = registry.GetStructure("firepit");

        Assert.NotNull(firepit);
        Assert.Equal(2, firepit!.Materials.Length);
        Assert.Contains(firepit.Materials, m => m.ItemId == "stone" && m.Quantity == 6);
        Assert.Contains(firepit.Materials, m => m.ItemId == "stick" && m.Quantity == 2);
    }

    [Fact]
    public void WorkerCompletion_LightsSiteFire()
    {
        var h = new Harness();
        Assert.True(h.Colony.FoundAt(
            10.5f * Constants.TileSize, 10.5f * Constants.TileSize, h.World));
        var worker = new RecruitNpc
        {
            NpcId = "builder",
            WorldX = 14.5f * Constants.TileSize,
            WorldY = 10.5f * Constants.TileSize,
            IsActive = true,
            IsRecruited = true,
            RecruitBehavior = "assistant",
            ColonyHunger = 100f,
            ColonyRest = 100f,
            Health = 100,
            MaxHealth = 100,
        };
        h.Npcs.NPCs.Add(worker);
        h.Recruits.OnRecruit("builder", "assistant");
        var def = h.Buildings.Registry!.GetStructure("campfire")!;
        var site = new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 14,
            TileY = 10,
            WorldX = 14.5f * Constants.TileSize,
            WorldY = 10.5f * Constants.TileSize,
            IsUnderConstruction = true,
            ConstructionMaterialsPaid = true,
            WorkStatus = "Awaiting construction",
        };
        h.Buildings.Structures.Add(site);

        h.Tick(80);
        h.Tick(20); // margin for approach + build ticks

        Assert.False(site.IsUnderConstruction, $"site should finish; status {site.WorkStatus}");
        Assert.True(h.Firemaking.GetFiresInRadius(
                14.5f * Constants.TileSize, 10.5f * Constants.TileSize, 4f).Count > 0,
            "a worker-finished hearth must light its fire");
    }

    // ─── §4 Relight at a burned-out hearth feeds firemaking ───────────────

    [Fact]
    public void Relight_AtBurnedOutHearth_ConsumesFuel_PaysFiremaking()
    {
        var game = new Game();
        game.Player = new Player(100f, 100f)
        {
            ActionSystem = new Actions.ActionSystem(),
        };
        game.Inventory = new Inv();
        game.Inventory.AddItem("oak_logs", 5);
        game.SkillManager = new SkillManager();
        game.Firemaking = new FiremakingSkill();
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        game.BuildingSystem = new BuildingSystem { Registry = registry };
        // A burned-out campfire (active structure, no live FireInstance).
        var def = registry.GetStructure("campfire")!;
        game.BuildingSystem.Structures.Add(new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 1,
            TileY = 1,
            WorldX = 100f,
            WorldY = 100f,
            IsActive = true,
        });

        var fire = new FireInteraction(game);
        fire.HandleLightFire();

        var fires = game.Firemaking.GetActiveFires();
        Assert.True(fires.Count > 0, "relight must spawn a fire at the hearth");
        // Slice 13 retruth: a relight feeds the reservoir — the burning unit
        // is one oak_log's fuel-table seconds (30s), NOT the flat
        // light_seconds (90s); the stocked second unit is queued on the
        // structure.
        Assert.Equal(30f, fires[0].MaxTime, 3);
        var hearth = game.BuildingSystem.Structures[0];
        Assert.Equal("oak_logs", hearth.FuelItemId);
        Assert.Equal(1, hearth.FuelCount);
        float xp = game.SkillManager.GetSkill("firemaking").Xp;
        Assert.True(xp > 0f, $"relight must pay firemaking XP, got {xp}");
        Assert.True(game.Inventory.GetItemQuantity("oak_logs") < 5,
            "relight must consume fuel");
    }

    [Fact]
    public void Relight_NoFuel_RefusesVisibly()
    {
        var game = new Game();
        game.Player = new Player(100f, 100f)
        {
            ActionSystem = new Actions.ActionSystem(),
        };
        game.Inventory = new Inv();
        game.SkillManager = new SkillManager();
        game.Firemaking = new FiremakingSkill();
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        game.BuildingSystem = new BuildingSystem { Registry = registry };
        var def = registry.GetStructure("campfire")!;
        game.BuildingSystem.Structures.Add(new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 1,
            TileY = 1,
            WorldX = 100f,
            WorldY = 100f,
            IsActive = true,
        });

        var fire = new FireInteraction(game);
        fire.HandleLightFire();

        Assert.Empty(game.Firemaking.GetActiveFires());
        Assert.True(game.Player.ActionSystem!.FlushNotifications().Count > 0,
            "the refusal must post a visible notification");
    }

    // ─── §5 The fire menu: E near lit fire; partition single-source ───────

    [Fact]
    public void E_NearLitFire_OpensFireMenu()
    {
        var game = MakeGameAtFire();
        var interact = new InteractSystem(game);

        interact.HandleInteract();

        Assert.Equal(GameState.FireMenu, game.State);
    }

    [Fact]
    public void E_NearColdHearth_OpensFireMenu()
    {
        // Slice 13 retruth: the hearth STRUCTURE is the menu handle — a
        // cold campfire opens the fire menu (sleep moved INTO the menu's
        // rest row), so E no longer falls through to TrySleep.
        var game = new Game();
        game.Player = new Player(100f, 100f)
        {
            ActionSystem = new Actions.ActionSystem(),
        };
        game.Inventory = new Inv();
        game.SkillManager = new SkillManager();
        game.World = new TileMap(24, 24);
        for (int x = 0; x < 24; x++)
            for (int y = 0; y < 24; y++)
                game.World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        game.Firemaking = new FiremakingSkill();
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        game.BuildingSystem = new BuildingSystem { Registry = registry };
        var def = registry.GetStructure("campfire")!;
        game.BuildingSystem.Structures.Add(new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 1,
            TileY = 1,
            WorldX = 100f,
            WorldY = 100f,
            IsActive = true,
        });
        game.FireInteraction = new FireInteraction(game);
        var interact = new InteractSystem(game);

        interact.HandleInteract();

        Assert.Equal(GameState.FireMenu, game.State);
    }

    private static Game MakeGameAtFire()
    {
        var game = new Game();
        game.Player = new Player(100f, 100f)
        {
            ActionSystem = new Actions.ActionSystem(),
        };
        game.Inventory = new Inv();
        game.SkillManager = new SkillManager();
        game.World = new TileMap(24, 24);
        for (int x = 0; x < 24; x++)
            for (int y = 0; y < 24; y++)
                game.World.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        game.Firemaking = new FiremakingSkill();
        var inv = new Inv();
        inv.AddItem("oak_logs", 2);
        game.Firemaking.LightFire([("oak_logs", 2)], inv, 100f, 100f);
        game.FireInteraction = new FireInteraction(game);
        return game;
    }

    [Fact]
    public void FireMenuPartition_IsSingleSourced()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();

        int inMenu = 0;
        foreach (var recipe in recipes.Recipes.Values)
        {
            if (FireMenuPanel.IsFireRecipe(recipe))
                inMenu++;
        }

        Assert.True(inMenu >= 11,
            $"fire-menu content must include all requires_campfire + cooking recipes, got {inMenu}");
    }

    [Fact]
    public void FireMenuPartition_CookingRecipes_AreMenuContent()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();

        foreach (var id in new[] { "cook_chicken", "cook_fish", "cook_meat",
                 "render_tallow", "refine_salt", "filter_honey", "brew_mead",
                 "burn_ash", "charcoal" })
        {
            var recipe = recipes.GetRecipe(id);
            Assert.NotNull(recipe);
            Assert.True(FireMenuPanel.IsFireRecipe(recipe),
                $"{id} must be fire-menu content");
        }
    }

    [Fact]
    public void CraftingPanel_DropsFireMenuRecipes()
    {
        var panel = new CraftingPanel();
        var crafting = MakeCraftingForPanel();
        var inv = new Inv();
        var skills = new SkillManager();

        panel.Update(new InputState(), crafting, inv, skills, 1280, 720);

        // The panel's visible list must contain NO fire-menu recipe.
        Assert.All(panel.ListedRecipes, r =>
            Assert.False(FireMenuPanel.IsFireRecipe(r),
                $"{r.RecipeId} must not appear in the crafting panel"));
    }

    private static CraftingSystem MakeCraftingForPanel()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        return new CraftingSystem { Registry = recipes };
    }

    // ─── §6 Proximity honesty in GetAvailableStructureIds ──────────────────

    [Fact]
    public void AvailableStructures_AreProximityFiltered()
    {
        var game = new Game();
        game.Player = new Player(100f, 100f);
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        game.BuildingSystem = new BuildingSystem { Registry = registry };
        // A distant furnace (tile 20,20 = 1280px,1280px), far beyond 3 tiles.
        game.BuildingSystem.Structures.Add(new Structure
        {
            StructureId = "furnace",
            StructureDef = registry.GetStructure("furnace")!,
            TileX = 20,
            TileY = 20,
            WorldX = 20 * Constants.TileSize + Constants.TileSize / 2f,
            WorldY = 20 * Constants.TileSize + Constants.TileSize / 2f,
            IsActive = true,
        });

        var ids = game.GetAvailableStructureIds();

        Assert.False(ids.Contains("furnace"),
            "a distant furnace must not satisfy station gates anywhere");
    }

    // ─── §7 The dominated craft recipe is gone; structure def stays ────────

    [Fact]
    public void Campfire_CraftRecipe_Removed_StructureDefStays()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var structures = new StructureDefRegistry();
        structures.LoadAll();

        Assert.Null(recipes.GetRecipe("campfire"));
        Assert.NotNull(structures.GetStructure("campfire"));
    }
}
