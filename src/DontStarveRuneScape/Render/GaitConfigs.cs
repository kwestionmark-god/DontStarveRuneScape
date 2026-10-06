namespace DontStarveRuneScape.Render;

/// <summary>
/// Gait configs per entity type. The player uses today's biped rig; monsters
/// get quadruped walk (wolf, bear, ...), biped (goblin, troll, birds, ...), or
/// no feet at all (legless/floating bodies). Humanoid NPCs use the NPC biped.
/// </summary>
public static class GaitConfigs
{
    // Player biped rig: the tunables the boots have always used
    // (StanceWidth 3, TriggerDist 3, SwingDur 0.1, LiftPx 4, stride floor 3).
    // Stance widened to ±3.5 with the 2026-10-04 boot scale-up so the
    // bigger domes keep their old ~1.2px inner clearance.
    public static GaitConfig Player { get; } = new()
    {
        Pattern = GaitPattern.Biped,
        FootOffsets = [(-3.5f, 0f), (3.5f, 0f)],
        SwingDur = 0.1f,
        LiftPx = 4f,
        TriggerDist = 3f,
        MinStride = 3f,
    };

    // Humanoid NPCs are player-styled; slightly smaller boots to match the
    // smaller sprite, otherwise the same rig. Dome dims scale with the old
    // flat foot-quad half (15·4/30 = 2.0px vs the player's 22·4/32 = 2.75px).
    public static GaitConfig Npc { get; } = new()
    {
        Pattern = GaitPattern.Biped,
        FootOffsets = [(-3f, 0f), (3f, 0f)],
        SwingDur = 0.1f,
        LiftPx = 4f,
        TriggerDist = 3f,
        MinStride = 3f,
        DomeBoots = new(toe: 2.6f, heel: 1.9f, halfWidth: 2.1f, height: 2.8f),
    };

    // Quadruped walk: diagonal pairs [front-left, rear-right] then
    // [front-right, rear-left]. Fore/aft offsets ±6px, lateral ±3px, a slower
    // swing (0.14s), and a bigger lift for the longer strides. One config per
    // quadruped so each gets paw domes tinted to its own body color; the paw
    // dims scale with the old flat paw-quad half (16·4/16 = 4.0px vs the
    // player's 2.75px).
    private static GaitConfig Quadruped(byte r, byte g, byte b) => new()
    {
        Pattern = GaitPattern.QuadrupedWalk,
        FootOffsets = [(-3f, 6f), (3f, -6f), (3f, 6f), (-3f, -6f)],
        SwingDur = 0.14f,
        LiftPx = 5f,
        TriggerDist = 3f,
        MinStride = 3f,
        DomeBoots = new(toe: 5.2f, heel: 3.85f, halfWidth: 4.2f, height: 5.6f),
        DomeColor = (r, g, b),
    };

    // Gait config per monster sprite key; null = legless/floating body, no feet.
    public static GaitConfig? ForMonster(string spriteKey) => spriteKey switch
    {
        "monster/wolf" => Quadruped(150, 150, 155),
        "monster/bear" => Quadruped(115, 85, 60),
        "monster/boar" => Quadruped(130, 95, 70),
        "monster/crocodile" => Quadruped(90, 130, 70),
        "monster/crab" => Quadruped(190, 90, 60),
        "monster/scorpion" => Quadruped(120, 80, 50),
        "monster/goblin" => Player,
        "monster/cave_troll" => Player,
        "monster/stone_golem" => Player,
        "monster/poison_frog" => Player,
        "monster/swamp_drake" => Player,
        "monster/eagle" => Player,
        "monster/hawk" => Player,
        "monster/snake" => null,
        "monster/sea_serpent" => null,
        "monster/sand_worm" => null,
        "monster/djinn" => null,
        _ => null,
    };
}
