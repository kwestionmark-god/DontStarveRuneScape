using DontStarveRuneScape.Skills;
using Xunit;

namespace DontStarveRuneScape.Tests;

public class SkillManagerTests
{
    private static SkillManager LeveledManager(string skillId, float xp)
    {
        var sm = new SkillManager();
        sm.AddXpWithNotification(skillId, xp);
        return sm;
    }

    [Theory]
    // Real OSRS cumulative XP thresholds: xp(L) = sum_{i=1}^{L-1} floor(i + 300*2^(i/7)) / 4
    [InlineData(0f, 1)]
    [InlineData(82f, 1)]
    [InlineData(83f, 2)]
    [InlineData(1154f, 10)]
    [InlineData(101333f, 50)]
    [InlineData(13034431f, 99)]
    public void Level_FollowsOsrsXpTable(float xp, int expectedLevel)
    {
        var sm = LeveledManager("mining", xp);
        Assert.Equal(expectedLevel, sm.GetSkillLevel("mining"));
    }

    [Theory]
    [InlineData(2, 83)]
    [InlineData(99, 13034431)]
    public void XpForLevel_MatchesOsrsTable(int level, int expectedXp)
    {
        Assert.Equal(expectedXp, (int)SkillManager.XpForLevel(level));
    }

    [Fact]
    public void XpForLevel_IsStrictlyIncreasing()
    {
        for (int level = 2; level < 99; level++)
            Assert.True(SkillManager.XpForLevel(level) < SkillManager.XpForLevel(level + 1));
    }

    [Fact]
    public void ProgressToNext_ReportsFractionWithinLevel()
    {
        var sm = new SkillManager();
        // Level 2 spans 83..174 (91 xp). Put 83 + 45 xp in: halfway.
        sm.AddXpWithNotification("mining", 83f + 45f);
        sm.ProgressToNext("mining", out float into, out float needed);
        Assert.Equal(45f, into, 1);
        Assert.Equal(91f, needed, 1);
    }

    [Fact]
    public void LevelUp_GrantsStatPoints_AndAnnounces()
    {
        var sm = new SkillManager();
        var messages = sm.AddXpWithNotification("mining", 83f); // level 2
        Assert.Contains(messages, m => m.Contains("mining") && m.Contains("2"));
        Assert.Equal(3, sm.GetSkill("mining").UnallocatedPoints);
    }

    [Fact]
    public void SpendPoint_Succeeds_OnlyWithPointsAndValidStat()
    {
        var sm = new SkillManager();
        Assert.False(sm.SpendPoint("mining", "efficiency")); // no points yet

        sm.AddXpWithNotification("mining", 83f); // level 2 -> 3 points
        Assert.False(sm.SpendPoint("mining", "charisma")); // unknown sub-stat
        Assert.False(sm.SpendPoint("cooking", "efficiency")); // points live on mining

        Assert.True(sm.SpendPoint("mining", "efficiency"));
        var skill = sm.GetSkill("mining");
        Assert.Equal(2, skill.UnallocatedPoints);
        Assert.Equal(1f, skill.SubStats["efficiency"]);

        Assert.True(sm.SpendPoint("mining", "efficiency"));
        Assert.True(sm.SpendPoint("mining", "efficiency"));
        Assert.False(sm.SpendPoint("mining", "efficiency")); // out of points
        Assert.Equal(3f, sm.GetSkill("mining").SubStats["efficiency"]);
    }

    [Fact]
    public void Snapshot_RoundTrips_LevelXpAndSpentPoints()
    {
        var sm = new SkillManager();
        sm.AddXpWithNotification("mining", 101333f); // level 50 -> 147 points
        sm.SpendPoint("mining", "efficiency");
        sm.SpendPoint("mining", "efficiency");

        var snapshot = sm.GetSnapshot();
        var restored = new SkillManager();
        restored.RestoreSnapshot(snapshot);

        var skill = restored.GetSkill("mining");
        Assert.Equal(50, skill.Level);
        Assert.Equal(101333f, skill.Xp);
        Assert.Equal(145, skill.UnallocatedPoints);
        Assert.Equal(2, skill.SubStats["efficiency"]);
    }
}
