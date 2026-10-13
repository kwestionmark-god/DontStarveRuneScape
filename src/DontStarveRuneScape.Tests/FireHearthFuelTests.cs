namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Interactions;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Silk.NET.Input;
using Xunit;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// Vertical building, slice 13 (spec:
/// docs/superpowers/specs/2026-10-12-fire-hearth-menu-design.md):
/// the fire structure becomes the menu handle (E opens the venue near a
/// COLD hearth too), hearths gain a per-structure fuel RESERVOIR (one
/// fuel type, 10-unit cap, auto-feed on burnout, burn_rate per unit),
/// the menu gains burn metrics (stage + gauge), and the campfire upgrades
/// to the Stone-Lined Fire Pit through the colony upgrade flow.
/// User decisions 2026-10-12 (clarify round, verbatim in the spec):
/// burn metrics + staged gauge + stage graphics; hoverable reservoir
/// (empty = leads to the inventory; stocked = click adds one more);
/// sleep + structure upgrading live in the fire menu.
/// </summary>
public class FireHearthFuelTests
{
    // ── Harness: a bare Game the FireInteraction routing actually reads ───

    private static Game MakeGame(float px = 100f, float py = 110f)
    {
        var game = new Game();
        game.Player = new Player(px, py)
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
        game.FireInteraction = new FireInteraction(game);
        return game;
    }

    /// <summary>Stage an ACTIVE, FINISHED, COLD hearth next to the player
    /// (the player stands at (100,110); hearth at (100,100)).</summary>
    private static Structure AddHearth(Game game, string id = "campfire")
    {
        var def = game.BuildingSystem.Registry!.GetStructure(id)!;
        var s = new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = 1,
            TileY = 1,
            WorldX = 100f,
            WorldY = 100f,
            IsActive = true,
        };
        game.BuildingSystem.Structures.Add(s);
        return s;
    }

    // ── §1 The hearth structure is the menu handle (cold included) ────────

    [Fact]
    public void CanOpenFireMenu_ColdCampfire_Opens()
    {
        var game = MakeGame();
        AddHearth(game);

        Assert.True(game.FireInteraction.CanOpenFireMenu(),
            "a cold campfire STRUCTURE opens the fire menu now");
        // And the full E-route lands on the menu, not TrySleep.
        new InteractSystem(game).HandleInteract();
        Assert.Equal(GameState.FireMenu, game.State);
    }

    [Fact]
    public void CanOpenFireMenu_ColdFirepitAndLinedPit_Open()
    {
        var game = MakeGame();
        AddHearth(game, "firepit");
        Assert.True(game.FireInteraction.CanOpenFireMenu(), "cold firepit opens the menu");

        var game2 = MakeGame();
        AddHearth(game2, "lined_fire_pit");
        Assert.True(game2.FireInteraction.CanOpenFireMenu(), "cold lined pit opens the menu");
    }

    [Fact]
    public void CanOpenFireMenu_NoHearthNoFire_StillClosed()
    {
        var game = MakeGame();
        Assert.False(game.FireInteraction.CanOpenFireMenu());
    }

    // ── §2 The reservoir: feed rules ─────────────────────────────────────

    [Fact]
    public void Feed_ColdEmptyStock_FeedsBestFuel_AndLights()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        game.Inventory.AddItem("oak_logs", 3);

        bool fed = game.FireInteraction.MenuFeedFire();

        Assert.True(fed, "feeding a cold stocked fire succeeds");
        Assert.Equal("oak_logs", hearth.FuelItemId); // the type memory sticks at zero
        Assert.Equal(0, hearth.FuelCount); // fed 1, popped 1 to light
        var fire = Assert.Single(game.Firemaking.GetActiveFires());
        Assert.Equal(30f, fire.MaxTime, 1); // one oak_log unit (fuel table), rate 1.0
        Assert.Equal(2, game.Inventory.GetItemQuantity("oak_logs"));
        var xp = game.SkillManager.GetSkill("firemaking").Xp;
        Assert.Equal(30f, xp, 1); // 5 feed + 25 light
    }

    [Fact]
    public void Feed_Lit_AddsOneToStock_WithoutRelighting()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "stick";
        hearth.FuelCount = 1;
        game.Inventory.AddItem("stick", 4);
        game.Firemaking.LightHearth(90f, 100f, 100f);

        bool fed = game.FireInteraction.MenuFeedFire();

        Assert.True(fed);
        Assert.Equal("stick", hearth.FuelItemId);
        Assert.Equal(2, hearth.FuelCount);
        Assert.Equal(3, game.Inventory.GetItemQuantity("stick"));
        Assert.Equal(90f, Assert.Single(game.Firemaking.GetActiveFires()).MaxTime, 1);
    }

    [Fact]
    public void Feed_OccupiedStock_WrongTypeCarried_RefusesVisibly()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "stick";
        hearth.FuelCount = 1;
        game.Inventory.AddItem("oak_logs", 4);

        bool fed = game.FireInteraction.MenuFeedFire();

        Assert.False(fed);
        Assert.Equal(1, hearth.FuelCount); // untouched
        Assert.Equal(4, game.Inventory.GetItemQuantity("oak_logs"));
        game.Player!.ActionSystem!.FlushNotifications();
        Assert.Contains(game.Player.ActionSystem.Notifications,
            n => n.Text.Contains("Stick"));
    }

    [Fact]
    public void Feed_FullReservoir_RefusesVisibly()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "stick";
        hearth.FuelCount = 10;
        game.Inventory.AddItem("stick", 4);

        Assert.False(game.FireInteraction.MenuFeedFire());
        Assert.Equal(10, hearth.FuelCount);
        Assert.Equal(4, game.Inventory.GetItemQuantity("stick"));
    }

    [Fact]
    public void Feed_EmptyNoFuel_OpensInventory()
    {
        var game = MakeGame();
        AddHearth(game);

        bool fed = game.FireInteraction.MenuFeedFire();

        Assert.False(fed);
        // The user's reservoir UX: engaging an empty reservoir with no fuel
        // carried leads to the inventory.
        Assert.Equal(GameState.InventoryOpen, game.State);
    }

    // ── §3 Burn: auto-feed on burnout, per-unit rates, stages ─────────────

    [Fact]
    public void Burnout_WithStock_PopsNextUnit_AndStaysLit()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "stick";
        hearth.FuelCount = 2;
        game.Firemaking.LightHearth(0.1f, 100f, 100f); // about to die

        game.Firemaking.Tick(0.25f, game.BuildingSystem);

        var fire = Assert.Single(game.Firemaking.GetActiveFires());
        Assert.Equal(1, hearth.FuelCount);      // popped one
        Assert.Equal(15f, fire.MaxTime, 1);     // a stick's fuel-table seconds
    }

    [Fact]
    public void Burnout_EmptyStock_FireDies()
    {
        var game = MakeGame();
        AddHearth(game);
        game.Firemaking.LightHearth(0.1f, 100f, 100f);

        game.Firemaking.Tick(0.25f, game.BuildingSystem);

        Assert.Empty(game.Firemaking.GetActiveFires());
    }

    [Fact]
    public void LinedPit_BurnRate_ScalesPerUnit()
    {
        var game = MakeGame();
        AddHearth(game, "lined_fire_pit");
        game.Inventory.AddItem("stick", 2);

        Assert.True(game.FireInteraction.MenuFeedFire());

        // A lined pit's 1.5x burn rate turns a 15s stick into 22.5s.
        Assert.Equal(22.5f, Assert.Single(game.Firemaking.GetActiveFires()).MaxTime, 1);
    }

    [Fact]
    public void BurnStageOf_Stages_AtCapacityFractions()
    {
        Assert.Equal(0, FiremakingSkill.BurnStageOf(0f, 120f));
        Assert.Equal(1, FiremakingSkill.BurnStageOf(20f, 120f));   // < 25%
        Assert.Equal(2, FiremakingSkill.BurnStageOf(50f, 120f));   // < 75%
        Assert.Equal(3, FiremakingSkill.BurnStageOf(90f, 120f));   // >= 75%
    }

    // ── §4 Burn metrics: status label + fuel preview ──────────────────────

    [Fact]
    public void FireStatusLabel_Cold_NamesOutAndStock()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "oak_logs";
        hearth.FuelCount = 3;

        Assert.Equal("Campfire — out (stocked 3 Oak Logs)", game.FireInteraction.FireStatusLabel());
    }

    [Fact]
    public void FireStatusLabel_Burning_NamesStageAndMinutes()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "oak_logs";
        hearth.FuelCount = 1;
        game.Firemaking.LightHearth(30f, 100f, 100f);
        // 1 unit burning (30s) + 1 stocked (30s) = 60s of 300s capacity → 20% → embers.
        Assert.Equal("Campfire — embers (about 1 min)", game.FireInteraction.FireStatusLabel());
    }

    [Fact]
    public void FuelPreviewText_Stocked_NamesUnitValue()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "oak_logs";
        hearth.FuelCount = 3;

        Assert.Equal("+30s per Oak Logs", game.FireInteraction.FuelPreviewText());
    }

    [Fact]
    public void FuelPreviewText_Empty_NamesNothingCarried()
    {
        var game = MakeGame();
        AddHearth(game);

        Assert.Equal("no fuel carried", game.FireInteraction.FuelPreviewText());
    }

    // ── §5 Upgrade verb (campfire -> lined fire pit, colony flow) ─────────

    [Fact]
    public void Upgrade_DefData_CampfireUpgradesToLinedPit()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        Assert.Equal("lined_fire_pit", registry.GetStructure("campfire")!.UpgradesTo);
        Assert.Null(registry.GetStructure("lined_fire_pit")!.UpgradesTo);
    }

    [Fact]
    public void TryUpgrade_NoColony_RefusesVisibly()
    {
        var game = MakeGame();
        AddHearth(game);
        game.SkillManager!.AddXp("construction", SkillManager.XpForLevel(2) + 1f);

        var result = game.FireInteraction.TryUpgradeNearestHearth();

        Assert.False(result.Success);
        Assert.Contains("colony", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── §6 Panel rows ─────────────────────────────────────────────────────

    [Fact]
    public void FireMenu_FuelRow_SelectsAfterRecipes_AndFiresOnFuel()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var crafting = new CraftingSystem { Registry = recipes };
        // FireTargetDef null = a bare fire: NO upgrade row, fuel comes
        // first after the recipes, rest one past.
        var panel = new FireMenuPanel();
        panel.Update(new InputState(), crafting, new Inv(), new SkillManager(), 1280, 720);

        // Walk the selection past the recipe list: the FUEL row.
        while (panel.SelectedIndex < panel.ListedRecipes.Count)
            panel.HandleKey(Key.Down);
        Assert.Equal(panel.ListedRecipes.Count, panel.SelectedIndex);

        bool fueled = false, rested = false;
        panel.OnFuel = _ => fueled = true;
        panel.OnRest = _ => rested = true;
        panel.HandleKey(Key.Tab);   // to the action column
        panel.HandleKey(Key.Enter); // engage

        panel.Update(new InputState(), crafting, new Inv(), new SkillManager(), 1280, 720);
        Assert.True(fueled, "the fuel row must fire OnFuel");
        Assert.False(rested, "the fuel row is not rest");
    }

    [Fact]
    public void FireMenu_RestRow_LivesPastFuel_AndStillFiresOnRest()
    {
        var recipes = new RecipeRegistry();
        recipes.LoadAll();
        var crafting = new CraftingSystem { Registry = recipes };
        var panel = new FireMenuPanel();
        panel.Update(new InputState(), crafting, new Inv(), new SkillManager(), 1280, 720);

        for (int i = 0; i < 40; i++)
            panel.HandleKey(Key.Down);
        // Rest is one past the fuel row when the target is not upgradeable.
        Assert.Equal(panel.ListedRecipes.Count + 1, panel.SelectedIndex);

        bool rested = false, fueled = false;
        panel.OnRest = _ => rested = true;
        panel.OnFuel = _ => fueled = true;
        panel.HandleKey(Key.Tab);
        panel.HandleKey(Key.Enter);

        panel.Update(new InputState(), crafting, new Inv(), new SkillManager(), 1280, 720);
        Assert.True(rested);
        Assert.False(fueled);
    }

    // ── §7 Persistence ────────────────────────────────────────────────────

    [Fact]
    public void StructureSnapshot_FuelFields_RoundTrip()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        var buildings = new BuildingSystem { Registry = registry };
        var world = new TileMap(8, 8);
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
                world.Tiles[x, y].Elevation = Constants.SeaLevel + 5f;
        var inv = new Inv();
        inv.AddItem("stick", 4);
        inv.AddItem("stone", 4);
        var skills = new SkillManager();
        Assert.True(buildings.PlaceStructure("campfire", 3, 3, world, inv, skills).Success);
        var fire = buildings.Structures.Single(x => x.StructureId == "campfire");
        fire.FuelItemId = "oak_logs";
        fire.FuelCount = 7;

        var restored = new BuildingSystem { Registry = registry };
        restored.RestoreSnapshot(buildings.GetSnapshot(), world);

        var s = Assert.Single(restored.Structures.Where(x => x.StructureId == "campfire"));
        Assert.Equal("oak_logs", s.FuelItemId);
        Assert.Equal(7, s.FuelCount);
    }
}
