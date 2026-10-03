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
    public static GaitConfig Player { get; } = new()
    {
        Pattern = GaitPattern.Biped,
        FootOffsets = [(-3f, 0f), (3f, 0f)],
        SwingDur = 0.1f,
        LiftPx = 4f,
        TriggerDist = 3f,
        MinStride = 3f,
        FootSizeFrac = 4f / 32f,
    };

    // Humanoid NPCs are player-styled; slightly smaller boots to match the
    // smaller sprite, otherwise the same rig.
    public static GaitConfig Npc { get; } = new()
    {
        Pattern = GaitPattern.Biped,
        FootOffsets = [(-3f, 0f), (3f, 0f)],
        SwingDur = 0.1f,
        LiftPx = 4f,
        TriggerDist = 3f,
        MinStride = 3f,
        FootSizeFrac = 4f / 30f,
    };

    // Quadruped walk: diagonal pairs [front-left, rear-right] then
    // [front-right, rear-left]. Fore/aft offsets ±6px, lateral ±3px, a slower
    // swing (0.14s), and a bigger lift for the longer strides. One config per
    // quadruped so each gets a paw sprite tinted to its own body color.
    private static GaitConfig Quadruped(string pawKey) => new()
    {
        Pattern = GaitPattern.QuadrupedWalk,
        FootOffsets = [(-3f, 6f), (3f, -6f), (3f, 6f), (-3f, -6f)],
        SwingDur = 0.14f,
        LiftPx = 5f,
        TriggerDist = 3f,
        MinStride = 3f,
        FootSizeFrac = 4f / 16f,
        FootTextureKey = pawKey,
    };

    // Gait config per monster sprite key; null = legless/floating body, no feet.
    public static GaitConfig? ForMonster(string spriteKey) => spriteKey switch
    {
        "monster/wolf" => Quadruped("monster/paw_wolf"),
        "monster/bear" => Quadruped("monster/paw_bear"),
        "monster/boar" => Quadruped("monster/paw_boar"),
        "monster/crocodile" => Quadruped("monster/paw_crocodile"),
        "monster/crab" => Quadruped("monster/paw_crab"),
        "monster/scorpion" => Quadruped("monster/paw_scorpion"),
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
