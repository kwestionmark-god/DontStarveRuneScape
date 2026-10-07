namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Actions;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.Skills;
using Xunit;

/// <summary>
/// Agility skill + jump action: levels from sprinting and jumping on the
/// OSRS curve (very slow, earned), jump is a stamina-cost hop with a
/// visual arc, and agility grants +1% movement speed per level above 1.
/// </summary>
public class AgilityJumpTests
{
    private static (Player player, ActionSystem actions, SkillManager skills) AgilePlayer()
    {
        var player = new Player(1000f, 1000f);
        var actions = new ActionSystem();
        var skills = new SkillManager();
        player.ActionSystem = actions;
        player.SkillManager = skills;
        return (player, actions, skills);
    }

    // ─── Skill registration ─────────────────────────────────────────────

    [Fact]
    public void FreshSkillManager_RegistersAgility_StartsLevel1()
    {
        var skills = new SkillManager();

        Assert.Equal(1, skills.GetSkillLevel("agility"));
        // Big award levels it: proves it rides the OSRS curve + points flow.
        var messages = skills.AddXpWithNotification("agility",
            SkillManager.XpForLevel(3) + 1f);

        Assert.Equal(3, skills.GetSkillLevel("agility"));
        Assert.Contains(messages, m => m.Contains("agility level is now 3"));
    }

    // ─── Sprint XP ──────────────────────────────────────────────────────

    [Fact]
    public void Sprinting_AccruesAgilityXpOverTime_StandingGrantsNone()
    {
        var (player, _, skills) = AgilePlayer();

        player.UpdateSprint(sprintHeld: true, moving: true, dt: 1.0f);

        Assert.True(player.Sprinting);
        Assert.Equal(Constants.AgilityXpPerSprintSecond, skills.GetSkill("agility").Xp, 3);

        // Standing still with Shift held: no drain, no XP.
        player.UpdateSprint(sprintHeld: true, moving: false, dt: 1.0f);
        float xpAfterStand = skills.GetSkill("agility").Xp;
        player.UpdateSprint(sprintHeld: true, moving: false, dt: 2.0f);
        Assert.Equal(xpAfterStand, skills.GetSkill("agility").Xp, 3);
    }

    // ─── Jump ───────────────────────────────────────────────────────────

    [Fact]
    public void Jump_ConsumesStamina_StartsArc_AwardsXp()
    {
        var (player, actions, skills) = AgilePlayer();

        bool jumped = player.TryJump();

        Assert.True(jumped);
        Assert.True(player.IsJumping);
        Assert.Equal(Constants.MaxStaminaBase - Constants.JumpStaminaCost,
            actions.Stamina.Current, 3);
        Assert.Equal(Constants.AgilityXpPerJump, skills.GetSkill("agility").Xp, 3);

        // Mid-jump the arc is above the ground.
        player.UpdateJump(0.5f * Constants.JumpDuration);
        Assert.True(player.JumpVisualOffset > 0f);
    }

    [Fact]
    public void Jump_ExhaustedPool_FailsWithoutCostOrXp()
    {
        var (player, actions, skills) = AgilePlayer();
        // Drain the pool to exhaustion (sprint drains it dry).
        float dt = 0.25f;
        int guard = 0;
        while (!actions.Stamina.IsExhausted && guard++ < 500)
            player.UpdateSprint(true, true, dt);

        Assert.True(actions.Stamina.IsExhausted);
        float xpBefore = skills.GetSkill("agility").Xp;

        Assert.False(player.TryJump());
        Assert.False(player.IsJumping);
        Assert.Equal(0f, actions.Stamina.Current, 3);
        Assert.Equal(xpBefore, skills.GetSkill("agility").Xp, 3);
    }

    [Fact]
    public void JumpArc_RisesThenLands_BackToBackBlockedWhileAirborne()
    {
        var (player, _, _) = AgilePlayer();

        Assert.True(player.TryJump());
        Assert.False(player.TryJump()); // no double-jump mid-air

        // Tick through the full duration: offset returns to zero.
        float dt = 0.016f;
        int steps = (int)(Constants.JumpDuration / dt) + 5;
        for (int i = 0; i < steps; i++)
            player.UpdateJump(dt);

        Assert.False(player.IsJumping);
        Assert.Equal(0f, player.JumpVisualOffset, 3);
    }

    // ─── Speed bonus ────────────────────────────────────────────────────

    [Fact]
    public void EffectiveSpeed_AgilityGrantsOnePercentPerLevelAboveOne()
    {
        var (player, _, skills) = AgilePlayer();
        float levelOneSpeed = player.EffectiveSpeed;

        skills.AddXpWithNotification("agility", SkillManager.XpForLevel(10) + 1f);

        Assert.Equal(10, skills.GetSkillLevel("agility"));
        float expected = levelOneSpeed * (1f + 9 * Constants.AgilitySprintSpeedBonusPerLevel);
        Assert.Equal(expected, player.EffectiveSpeed, 3);
    }
}
