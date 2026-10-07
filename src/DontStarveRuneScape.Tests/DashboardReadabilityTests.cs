namespace DontStarveRuneScape.Tests;

using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Crafting;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Input;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.Survival;
using DontStarveRuneScape.UI;
using DontStarveRuneScape.World;
using Inv = DontStarveRuneScape.Inventory.Inventory;
using Xunit;

/// <summary>
/// Phase-5, Cycle 3: dashboard readability at scale. The colonist list
/// grows past its 3 visible rows via mouse-wheel scrolling; the COLONISTS
/// header shows the full population count.
/// </summary>
public class DashboardReadabilityTests
{
    private static (Game Game, DashboardPanel Panel) MakeDashboard(int colonistCount)
    {
        var game = new Game();
        var world = new TileMap(24, 24);
        for (int tx = 0; tx < 24; tx++)
            for (int ty = 0; ty < 24; ty++)
            {
                world.Tiles[tx, ty].Elevation = Constants.SeaLevel + 5f;
                world.Tiles[tx, ty].Biome = new BiomeDef { Id = "plains", Name = "Plains" };
            }
        game.World = world;
        game.Player = new Player(100f, 100f)
        {
            Gear = new PlayerGear(),
            SkillManager = new SkillManager(),
            Inventory = new Inv(),
            Survival = new SurvivalSystem(),
        };
        game.NPCSystem = new NPCSystem();
        game.ColonySystem = new ColonySystem();
        game.BuildingSystem = new BuildingSystem
        {
            Registry = new StructureDefRegistry(),
        };
        Assert.True(game.ColonySystem.FoundAt(100f, 100f, world));
        for (int i = 0; i < colonistCount; i++)
        {
            game.NPCSystem.NPCs.Add(new RecruitNpc
            {
                NpcId = $"c{i}",
                Name = $"Colonist {i}",
                IsRecruited = true,
                IsActive = true,
                RecruitBehavior = "assistant",
            });
        }
        var panel = new DashboardPanel { Game = game };
        panel.SetActive("colony");
        return (game, panel);
    }

    [Fact]
    public void ColonistList_ScrollsPastThreeRows()
    {
        var (game, panel) = MakeDashboard(5);
        // Colony layout: x = 1280*0.5 - 325 = 315, y = 720*0.5 - 158 = 202
        // First colonist row click region: y+215 .. y+236
        // Scroll wheel down over the list selects the 4th colonist after scroll
        var state = new InputState { ZoomDelta = -1f };
        panel.Update(state, 1280, 720);
        Assert.Equal(1, panel.ColonistScroll);
        // Row 0 click region: x+16..x+266, y+215..y+236 → (331..581, 417..438)
        state = new InputState { MouseX = 400f, MouseY = 425f, MouseLeftClick = true };
        panel.Update(state, 1280, 720);

        // After scrolling down one, row 0 shows colonist index 1 (rows 1..3)
        var selected = panel.SelectedColonistId;
        Assert.NotNull(selected);
        Assert.Equal("c1", selected);
    }
}
