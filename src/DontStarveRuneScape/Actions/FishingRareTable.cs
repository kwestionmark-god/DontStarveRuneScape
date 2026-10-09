namespace DontStarveRuneScape.Actions;

/// <summary>
/// Fishing rare-drop table — one roll per successful catch, scaled by
/// the fisher's level at cast start. Pearl first (the payday), then the
/// old boot (the gag), then nothing. Pure: no RNG lives here — callers
/// pass the roll (NextDouble) and the level; tests pass both directly.
/// </summary>
public static class FishingRareTable
{
    /// <summary>Pearl chance at level 1: 1%.</summary>
    public const float PearlBaseChance = 0.01f;

    /// <summary>Each fishing level adds 0.15% pearl chance.</summary>
    public const float PearlPerLevel = 0.0015f;

    /// <summary>Old-boot chance at level 1: 4% (the common gag drop).</summary>
    public const float BootBaseChance = 0.04f;

    /// <summary>Each fishing level adds 0.2% boot chance.</summary>
    public const float BootPerLevel = 0.002f;

    /// <summary>
    /// Roll the table. `roll` is a uniform [0,1) sample. Level clamps to
    /// 1 (an unregistered skill must still roll honestly).
    /// </summary>
    /// <returns>The rare item id, or null for a plain catch.</returns>
    public static string? Roll(float roll, int level)
    {
        int l = Math.Max(1, level);
        float pearl = PearlBaseChance + PearlPerLevel * (l - 1);
        if (roll < pearl) return "pearl";
        if (roll < pearl + BootBaseChance + BootPerLevel * (l - 1))
            return "old_boot";
        return null;
    }
}
