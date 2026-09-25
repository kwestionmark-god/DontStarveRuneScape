using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.World;
using Xunit;

namespace DontStarveRuneScape.Tests;

public class GameUpdateTests
{
    [Fact]
    public void WorldTicksWhilePanelIsOpen()
    {
        var game = new Game();
        var world = new TileMap(4, 4);
        world.SeasonSystem = new SeasonSystem();
        var node = new ResourceNode
        {
            ResourceDef = new ResourceDef { Regrow = 60, Seasons = [] },
            Density = 0.5f,
            MaxDensity = 1f,
            RegrowTime = 10f,
        };
        world.GetTile(1, 1)!.ResourceNode = node;
        game.World = world;
        game.SetState(GameState.SkillPanel);

        game.Update(1f);

        Assert.True(node.RegrowTime < 10f, "world regrowth should tick while a panel is open");
    }

    [Fact]
    public void PlayerDoesNotMoveWhilePanelIsOpen()
    {
        var game = new Game();
        var player = new Player(100f, 100f);
        game.Player = player;
        game.SetState(GameState.SkillPanel);
        game.InputManager!.InputState.MoveLeft = true;

        game.Update(0.5f);

        Assert.Equal(100f, player.WorldX);
    }
}
