namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using Xunit;

/// <summary>
/// Vertical building, slice 14 (spec:
/// docs/superpowers/specs/2026-10-12-fire-flame-visuals-design.md):
/// the user's "full fuel" screenshot critique — the fire needs real
/// side faces (not the top ring stretched down) and a VOLUMETRIC,
  /// fuel-capacity-staged flame with animation.
/// </summary>
public class FireFlameVisualTests
{
    // ── §1 Dedicated side art for the footprint voxels ───────────────────

    [Fact]
    public void FireDefs_HaveDedicatedSideSprites()
    {
        var registry = new StructureDefRegistry();
        registry.LoadAll();

        Assert.Equal("structure/campfire_side",
            registry.GetStructure("campfire")!.SideSpriteKey);
        Assert.Equal("structure/stone_fire_ring_side",
            registry.GetStructure("lined_fire_pit")!.SideSpriteKey);
        // Bricks and fences keep the one-sprite law.
        Assert.Null(registry.GetStructure("mound_block")!.SideSpriteKey);
        Assert.Null(registry.GetStructure("fence_block")!.SideSpriteKey);
    }

    private static void AssertIhdr(string path, int w, int h)
    {
        Assert.True(File.Exists(path), $"expected texture at {path}");
        var bytes = File.ReadAllBytes(path);
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        Assert.Equal(w, width);
        Assert.Equal(h, height);
    }

    [Fact]
    public void SideTextures_ShipAt32x16()
    {
        AssertIhdr("Assets/sprites/structure/campfire_side.png", 32, 16);
        AssertIhdr("Assets/sprites/structure/stone_fire_ring_side.png", 32, 16);
    }

    [Fact]
    public void FlameTextures_ShipAt32x48_AllStages()
    {
        AssertIhdr("Assets/sprites/fire/fire_ember.png", 32, 48);
        AssertIhdr("Assets/sprites/fire/fire_low.png", 32, 48);
        AssertIhdr("Assets/sprites/fire/fire_mid.png", 32, 48);
        AssertIhdr("Assets/sprites/fire/fire_roaring.png", 32, 48);
        // Slice 15: every stage animates — embers pulse, roaring licks flip.
        AssertIhdr("Assets/sprites/fire/fire_ember_b.png", 32, 48);
        AssertIhdr("Assets/sprites/fire/fire_roaring_b.png", 32, 48);
    }

    // ── §2b Slice 15: every stage animates (the burning stage already
    // flip-flopped; embers and roaring were static single frames) ────────

    [Theory]
    // Burning keeps the slice-14 0.6s flip.
    [InlineData(2, 0.10f, "fire/fire_low")]
    [InlineData(2, 0.35f, "fire/fire_mid")]
    // Embers pulse SLOW (1.2s period) between two coal-glow frames.
    [InlineData(1, 0.10f, "fire/fire_ember")]
    [InlineData(1, 0.70f, "fire/fire_ember_b")]
    [InlineData(1, 1.30f, "fire/fire_ember")]
    // Roaring flips FAST (0.4s period) between two crown frames.
    [InlineData(3, 0.10f, "fire/fire_roaring")]
    [InlineData(3, 0.30f, "fire/fire_roaring_b")]
    [InlineData(3, 0.50f, "fire/fire_roaring")]
    public void FireSpriteKey_EveryStage_Animates(int stage, float time, string expected)
    {
        Assert.Equal(expected, Render.SpriteRenderer.FireSpriteKey(stage, time));
    }

    // ── §2 The stage of a fire is its RESERVOIR fullness ─────────────────

    private static Game MakeGame()
    {
        var game = new Game();
        game.Player = new Player(100f, 110f) { ActionSystem = new Actions.ActionSystem() };
        game.Inventory = new DontStarveRuneScape.Inventory.Inventory();
        game.SkillManager = new SkillManager();
        game.World = new DontStarveRuneScape.World.TileMap(24, 24);
        game.Firemaking = new FiremakingSkill();
        var registry = new StructureDefRegistry();
        registry.LoadAll();
        game.BuildingSystem = new BuildingSystem { Registry = registry };
        game.FireInteraction = new Interactions.FireInteraction(game);
        return game;
    }

    private static Structure AddHearth(Game game)
    {
        var def = game.BuildingSystem.Registry!.GetStructure("campfire")!;
        var s = new Structure
        {
            StructureId = def.Id, StructureDef = def,
            TileX = 1, TileY = 1, WorldX = 100f, WorldY = 100f, IsActive = true,
        };
        game.BuildingSystem.Structures.Add(s);
        return s;
    }

    [Fact]
    public void StageOf_GroundFire_IsOwnFraction()
    {
        var game = MakeGame();
        game.Firemaking.LightFire(new() { ("oak_logs", 2) }, game.Inventory, 100f, 100f);
        var fire = game.Firemaking.GetActiveFires()[0];
        fire.RemainingTime = 15f; // of 60 max → 0.25 boundary → stage 2

        Assert.Equal(2, game.Firemaking.StageOf(fire, game.BuildingSystem));
        fire.RemainingTime = 12f; // < 25% → embers
        Assert.Equal(1, game.Firemaking.StageOf(fire, game.BuildingSystem));
    }

    [Fact]
    public void StageOf_StockedHearth_StagesOnReservoirNotUnitLife()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "oak_logs";
        hearth.FuelCount = 4;
        // The burning unit is nearly DEAD (3s left of 30) but the tank
        // reads full fuel: the user expects a fire that looks FED.
        game.Firemaking.LightHearth(30f, 100f, 100f);
        var fire = game.Firemaking.GetActiveFires()[0];
        fire.RemainingTime = 3f;

        // Total = 3 + 4*30 = 123s of 300s capacity = 41% → burning (2),
        // NOT embers (the unit's own fraction would say embers).
        Assert.Equal(2, game.Firemaking.StageOf(fire, game.BuildingSystem));
    }

    [Fact]
    public void StageOf_FullHearth_AtNewUnit_Roars()
    {
        var game = MakeGame();
        var hearth = AddHearth(game);
        hearth.FuelItemId = "oak_logs";
        hearth.FuelCount = 9;
        game.Firemaking.LightHearth(30f, 100f, 100f);

        // 30 + 270 = 300 / 300 = full → roaring.
        Assert.Equal(3, game.Firemaking.StageOf(
            game.Firemaking.GetActiveFires()[0], game.BuildingSystem));
    }
}
