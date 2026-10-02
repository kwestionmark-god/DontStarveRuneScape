namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;

/// <summary>Builds and switches into deterministic, dry cave maps.</summary>
public sealed class CaveWorldSystem
{
    private readonly Game _game;
    private TileMap? _surface;
    private float _surfaceX, _surfaceY;
    private int _entranceX, _entranceY;

    public bool IsInside => _surface != null;

    public CaveWorldSystem(Game game) => _game = game;

    public void InteractWith(Tile tile)
    {
        if (IsInside && tile.IsCaveExit) { Exit(); return; }
        if (!IsInside && tile.IsCaveEntrance) Enter(tile.X, tile.Y);
    }

    private void Enter(int x, int y)
    {
        if (_game.World == null || _game.Player == null) return;
        _surface = _game.World;
        _surfaceX = _game.Player.WorldX;
        _surfaceY = _game.Player.WorldY;
        _entranceX = x;
        _entranceY = y;

        var cave = Generate(unchecked(_game.Seed * 397 ^ x * 31 ^ y));
        _game.World = cave;
        _game.Player.WorldX = (cave.SpawnX + 0.5f) * Constants.TileSize;
        _game.Player.WorldY = (cave.SpawnY + 0.5f) * Constants.TileSize;
        _game.Player.TargetX = _game.Player.WorldX;
        _game.Player.TargetY = _game.Player.WorldY;
        _game.Player.ActionSystem?.SetTileMap(cave);
        _game.Camera?.SetWorld(cave);
    }

    private void Exit()
    {
        if (_surface == null || _game.Player == null) return;
        _game.World = _surface;
        _game.Player.WorldX = _surfaceX;
        _game.Player.WorldY = _surfaceY;
        _game.Player.TargetX = _surfaceX;
        _game.Player.TargetY = _surfaceY;
        _game.Player.ActionSystem?.SetTileMap(_surface);
        _game.Camera?.SetWorld(_surface);
        _surface = null;
    }

    private static TileMap Generate(int seed)
    {
        const int size = 64;
        // Start at the outer lip, then let the floor descend into the basin.
        var map = new TileMap(size, size) { IsCave = true, SpawnX = 6, SpawnY = size / 2 };
        var cavern = new BiomeDef { Id = "cavern", Name = "Cavern" };
        for (int elevation = 0; elevation <= 31; elevation++)
        {
            float shade = Math.Clamp((elevation - 11) / 15f, 0f, 1f);
            cavern.TerrainColors[elevation.ToString()] = [
                (int)(48 + 42 * shade), (int)(43 + 35 * shade), (int)(54 + 38 * shade)];
        }
        map.BiomeRegistry = new BiomeRegistry([cavern]);
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            var tile = map.Tiles[x, y];
            tile.Biome = cavern;
            // The cave floor is an independent height field and stays well
            // above SeaLevel. A broad central passage is ringed by rising rock.
            float nx = (x - size * .5f) / (size * .5f);
            float ny = (y - size * .5f) / (size * .5f);
            float radius = MathF.Sqrt(nx * nx + ny * ny);
            float rolling = Noise.PNoise2(x * .075f + seed * .00013f, y * .075f - seed * .00017f) * 2.2f
                          + Noise.PNoise2(x * .19f + 73 + seed * .00013f, y * .19f - 41 - seed * .00017f) * .8f;
            tile.Elevation = Math.Clamp(15f + radius * 8f + rolling, 13f, 26f);
            tile.Moisture = .25f;
        }
        for (int x = 0; x < size; x++)
        for (int y = 0; y < size; y++)
        {
            var tile = map.Tiles[x, y];
            float e00 = map.GetTile(x, y)?.Elevation ?? tile.Elevation;
            float e10 = map.GetTile(x + 1, y)?.Elevation ?? tile.Elevation;
            float e11 = map.GetTile(x + 1, y + 1)?.Elevation ?? tile.Elevation;
            float e01 = map.GetTile(x, y + 1)?.Elevation ?? tile.Elevation;
            tile.CornerElevations = [(e00 + tile.Elevation) * .5f,
                (e10 + tile.Elevation) * .5f, (e11 + tile.Elevation) * .5f,
                (e01 + tile.Elevation) * .5f];
        }
        map.Tiles[map.SpawnX, map.SpawnY].IsCaveExit = true;
        return map;
    }
}
