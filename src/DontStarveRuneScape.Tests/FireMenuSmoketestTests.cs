namespace DontStarveRuneScape.Tests;

using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.World;
using Xunit;

/// <summary>
/// DSR_TEST_FIREMENU staging — the smoketest picks the first CLEAR dry tile so
/// a scripted E reaches the fire menu and nothing else eats it. Spec:
/// docs/superpowers/specs/2026-10-11-fire-menu-smoketest-design.md
/// </summary>
public class FireMenuSmoketestTests
{
    private static TileMap FlatMap(int w, int h, float elevation = 5f)
    {
        var map = new TileMap(w, h);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                map.Tiles[x, y].Elevation = Constants.SeaLevel + elevation;
        return map;
    }

    [Fact]
    public void ClearFlatMap_ReturnsFirstDryTile()
    {
        var map = FlatMap(8, 8);

        var pick = Game.FindFireMenuStageTile(map, null, null);

        Assert.NotNull(pick);
        // Deterministic scan order: y outer, x inner — tile (0,0) first.
        Assert.Equal((0, 0), pick!.Value);
    }

    [Fact]
    public void AllWetMap_SkipsWater_FindsTheOneDryIsland()
    {
        var map = FlatMap(8, 8, elevation: -10f);
        map.Tiles[5, 6].Elevation = Constants.SeaLevel + 5f; // one dry tile

        var pick = Game.FindFireMenuStageTile(map, null, null);

        Assert.NotNull(pick);
        Assert.Equal((5, 6), pick!.Value);
    }

    [Fact]
    public void NpcNearATile_PushesPickPastInteractBand()
    {
        var map = FlatMap(16, 16);
        var npcs = new NPCSystem();
        var npc = new RecruitNpc { NpcId = "recruit_forest_assistant", IsActive = true };
        npc.WorldX = 0.5f * Constants.TileSize;
        npc.WorldY = 0.5f * Constants.TileSize;
        npcs.NPCs.Add(npc);

        var pick = Game.FindFireMenuStageTile(map, npcs, null);

        // Tiles within 128px of the NPC are excluded; the scan picks past them.
        Assert.NotNull(pick);
        var (px, py) = pick!.Value;
        float dx = (px + 0.5f) * Constants.TileSize - npc.WorldX;
        float dy = (py + 0.5f) * Constants.TileSize - npc.WorldY;
        Assert.True(dx * dx + dy * dy >= 128f * 128f,
            $"picked tile ({px},{py}) must be outside the 128px interact band");
    }

    [Fact]
    public void LiveMonsterNearTile_PushesPickPastTamingBand_DeadDoesNot()
    {
        var map = FlatMap(16, 16);
        var combat = new CombatSystem { };
        var shade = new Monster
        {
            MonsterId = "shade", WorldX = 0.5f * Constants.TileSize,
            WorldY = 0.5f * Constants.TileSize, Health = 50, IsActive = true,
            Def = new MonsterDef { MonsterId = "shade", Tamable = true },
        };
        combat.Monsters.Add(shade);

        var pick = Game.FindFireMenuStageTile(map, null, combat);

        Assert.NotNull(pick);
        var (px, py) = pick!.Value;
        float dx = (px + 0.5f) * Constants.TileSize - shade.WorldX;
        float dy = (py + 0.5f) * Constants.TileSize - shade.WorldY;
        Assert.True(dx * dx + dy * dy > 96f * 96f,
            $"picked tile ({px},{py}) must be outside the 96px taming band");

        // A dead monster blocks nothing.
        shade.Health = 0;
        var pickDead = Game.FindFireMenuStageTile(map, null, combat);
        Assert.NotNull(pickDead);
        Assert.Equal((0, 0), pickDead!.Value);
    }

    [Fact]
    public void CaveCollar_PushesPickPastIts3x3()
    {
        var map = FlatMap(8, 8);
        map.Tiles[1, 1].IsCaveEntrance = true;

        var pick = Game.FindFireMenuStageTile(map, null, null);

        Assert.NotNull(pick);
        var (px, py) = pick!.Value;
        Assert.True(Math.Abs(px - 1) > 1 || Math.Abs(py - 1) > 1,
            $"picked tile ({px},{py}) must be outside the cave collar's 3x3");
    }

    [Fact]
    public void TinyMap_CoveredByOneNpcBand_ReturnsNull()
    {
        // A 2x2 map is 128px across — one NPC at its corner is within 128px
        // of every tile center, so no tile qualifies and the helper gives up.
        var map = FlatMap(2, 2);
        var npcs = new NPCSystem();
        var npc = new RecruitNpc { NpcId = "recruit_forest_assistant", IsActive = true };
        npc.WorldX = 0.5f * Constants.TileSize;
        npc.WorldY = 0.5f * Constants.TileSize;
        npcs.NPCs.Add(npc);

        Assert.Null(Game.FindFireMenuStageTile(map, npcs, null));
    }
}
