namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Data;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// Gathering actions resolve instantly (no harvest timeout): the first tick
/// after start completes the action, fixed cooldowns are gone, and the stamina
/// pool is the only limiter — acting is possible while it covers the cost,
/// and draining it triggers the forced rest that gates the next action.
/// </summary>
public class ActionSystemTests
{
    private static (ActionSystem system, ResourceNode node) ForagingTarget(int density = 5)
    {
        var node = new ResourceNode("bush", new ResourceDef
        {
            Name = "Berry Bush",
            YieldItem = "berries",
            Yield = 2,
            Xp = 5f,
            Tier = 1,
            Regrow = 60f,
            Seasons = [],
        }, density);
        return (new ActionSystem(), node);
    }

    private static void PumpSuccessRate(SkillManager sm, float value)
    {
        sm.GetSkill("foraging").SubStats["success_rate"] = value;
    }

    [Fact]
    public void GatherAction_CompletesOnFirstTick_NoTimeout()
    {
        var (system, node) = ForagingTarget();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f); // deterministic success roll
        var inv = new Inv();

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv, tileXy: (3, 3)));
        Assert.True(system.Active.IsBusy);

        var result = system.Update(0.016f);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal("berries", result.ItemId);
        Assert.Equal(2, result.Quantity);
        Assert.False(system.Active.IsBusy);

        // No repeated completion on later ticks.
        Assert.Null(system.Update(0.016f));
    }

    [Fact]
    public void FailedGather_HasNoCooldown_NextActionStartsImmediately()
    {
        var (system, node) = ForagingTarget();
        var sm = new SkillManager();
        var inv = new Inv();

        // A depleted node fails at completion (no yield, no cooldown).
        node.Density = 0f;
        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        var result = system.Update(0.016f);
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("The resource is depleted.", result.Message);

        // The removed cooldown guard used to reject the next start with
        // "You must wait a moment before acting again." — now it proceeds.
        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        Assert.True(system.Active.IsBusy);
        Assert.NotNull(system.Update(0.016f));
    }

    [Fact]
    public void Actions_ChainWhileStaminaCoversTheCost()
    {
        var (system, node) = ForagingTarget(density: 100);
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();

        // Pool 30, cost 2 → 15 straight actions with no fixed cooldown; the
        // small per-tick regen keeps the last action affordable.
        for (int i = 0; i < 15; i++)
        {
            Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
            var result = system.Update(0.016f);
            Assert.NotNull(result);
            Assert.True(result.Success);
        }

        // The pool is spent; the next action rests instead of waiting on a timer.
        Assert.False(system.Stamina.Consume(2f));
        Assert.True(system.Stamina.IsExhausted);
    }

    [Fact]
    public void Stamina_GatesGathering_WhenPoolCannotCoverTheCost()
    {
        var (system, node) = ForagingTarget();
        var sm = new SkillManager();
        PumpSuccessRate(sm, 1000f);
        var inv = new Inv();

        // Drain the pool to 1 (foraging cost is 2 without a wired skill).
        system.Stamina.Consume(29f);
        Assert.False(system.Stamina.IsExhausted);

        Assert.Null(system.StartAction(ActionType.Foraging, node, sm, inv));
        var result = system.Update(0.016f);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Contains("too exhausted", result.Message);
        Assert.True(system.Stamina.IsExhausted); // forced rest triggered
    }

    [Fact]
    public void Stamina_RegeneratesWhileIdle()
    {
        var (system, _) = ForagingTarget();
        system.Stamina.Consume(10f);

        system.Update(1f);

        Assert.Equal(22f, system.Stamina.Current, 2); // 30 - 10 + 2/s regen
    }

    [Fact]
    public void Exhaustion_ForcedRestRefillsThePool()
    {
        var (system, _) = ForagingTarget();

        system.Stamina.Consume(29f);
        Assert.False(system.Stamina.Consume(2f)); // exhausts the pool
        Assert.True(system.Stamina.IsExhausted);

        system.Update(4.9f);
        Assert.True(system.Stamina.IsExhausted); // still resting

        system.Update(0.2f);
        Assert.False(system.Stamina.IsExhausted);
        Assert.Equal(30f, system.Stamina.Current, 2); // full refill
    }
}
