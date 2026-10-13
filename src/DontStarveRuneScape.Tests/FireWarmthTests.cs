namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Core;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Skills.Firemaking;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.Config;
using Xunit;

/// <summary>
/// Vertical building, slice 17: WarmthRadius gets a gameplay consumer —
/// standing within a lit fire's warmth radius halves the slice's hunger
/// drain pressure (warm bodies burn fewer calories; winter's 1.2x drain
/// up to 40% softer at a roaring fire).
/// </summary>
public class FireWarmthTests
{
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

    [Fact]
    public void WarmthAt_NoFires_IsZero()
    {
        var game = MakeGame();
        Assert.Equal(0f, game.Firemaking.WarmthAt(100f, 110f, game.BuildingSystem));
    }

    [Fact]
    public void WarmthAt_FireBeyondRadius_IsZero()
    {
        var game = MakeGame();
        // Lit ground fire at (100,100); default WarmthRadius 3 tiles = 96px.
        game.Firemaking.LightFire(new() { ("oak_logs", 1) }, game.Inventory, 100f, 100f);
        Assert.Equal(0f, game.Firemaking.WarmthAt(100f + 97f, 100f, game.BuildingSystem));
    }

    [Theory]
    // Stage scales warmth: embers one-third, burning two-thirds, roaring
    // full. Remaining fractions of the unit's own MaxTime (ground fire).
    [InlineData(0.20f, 1f / 3f)]  // <25% → embers
    [InlineData(0.50f, 2f / 3f)]  // half → burning
    [InlineData(0.90f, 1f)]       // >=75% → roaring
    public void WarmthAt_ScalesWithBurnStage(float remainingFrac, float expected)
    {
        var game = MakeGame();
        game.Firemaking.LightFire(new() { ("oak_logs", 1) }, game.Inventory, 100f, 100f);
        var fire = game.Firemaking.GetActiveFires()[0];
        fire.RemainingTime = fire.MaxTime * remainingFrac;
        Assert.Equal(expected,
            game.Firemaking.WarmthAt(105f, 105f, game.BuildingSystem), 3);
    }

    [Fact]
    public void WarmthAt_TwoFires_TakesStrongest()
    {
        var game = MakeGame();
        game.Firemaking.LightFire(new() { ("oak_logs", 1) }, game.Inventory, 100f, 100f);
        game.Firemaking.LightFire(new() { ("oak_logs", 1) }, game.Inventory, 120f, 100f);
        var fires = game.Firemaking.GetActiveFires();
        fires[0].RemainingTime = fires[0].MaxTime * 0.2f; // embers
        fires[1].RemainingTime = fires[1].MaxTime * 0.9f; // roaring
        Assert.Equal(1f, game.Firemaking.WarmthAt(110f, 100f, game.BuildingSystem), 3);
    }

    [Fact]
    public void HungerDrain_WarmPlayer_DrainsUpToFortyPercentLess()
    {
        var cold = new SurvivalSystem();
        var warm = new SurvivalSystem();
        warm.WarmthFactor = 1f;
        float h0 = cold.Hunger;
        cold.Tick(Constants.HungerDrainInterval);
        warm.Tick(Constants.HungerDrainInterval);
        float coldDrain = h0 - cold.Hunger;
        float warmDrain = h0 - warm.Hunger;
        Assert.Equal(coldDrain * 0.6f, warmDrain, 3);
    }
}
