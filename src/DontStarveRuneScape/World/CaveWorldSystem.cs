namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;

/// <summary>Builds and switches into deterministic, dry cave maps.</summary>
public sealed class CaveWorldSystem
{
    private readonly Game _game;
    private TileMap? _surface;
    private float _surfaceX, _surfaceY;
    private int _entranceX, _entranceY;
    private CombatSystem? _surfaceCombat;
    private TileMap? _cave;
    private CombatSystem? _caveCombat;
    private readonly Dictionary<string, (float X, float Y)> _surfaceWorkerPositions = [];

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

        var seed = unchecked(_game.Seed * 397 ^ x * 31 ^ y);
        if (_cave == null)
        {
            _cave = Generate(seed, _game.ResourceRegistry, _game.SeasonSystem);
            _caveCombat = CreateCaveCombat(_game.MonsterRegistry, _game.QuestSystem, _cave);
        }

        var cave = _cave;
        _game.World = cave;
        _surfaceWorkerPositions.Clear();
        if (_game.NPCSystem != null)
        {
            foreach (var worker in _game.NPCSystem.NPCs.Where(n => n.IsActive && n.IsRecruited))
            {
                _surfaceWorkerPositions[worker.NpcId] = (worker.WorldX, worker.WorldY);
                worker.WorldX = (cave.SpawnX + 0.5f) * Constants.TileSize;
                worker.WorldY = (cave.SpawnY + 0.5f) * Constants.TileSize;
                worker.VelocityX = worker.VelocityY = 0f;
            }
        }
        _surfaceCombat = _game.CombatSystem;
        _game.CombatSystem = _caveCombat;
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
        if (_game.NPCSystem != null)
        {
            foreach (var worker in _game.NPCSystem.NPCs)
                if (_surfaceWorkerPositions.TryGetValue(worker.NpcId, out var position))
                {
                    worker.WorldX = position.X;
                    worker.WorldY = position.Y;
                    worker.VelocityX = worker.VelocityY = 0f;
                }
        }
        _surfaceWorkerPositions.Clear();
        _game.CombatSystem = _surfaceCombat;
        _game.Player.WorldX = _surfaceX;
        _game.Player.WorldY = _surfaceY;
        _game.Player.TargetX = _surfaceX;
        _game.Player.TargetY = _surfaceY;
        _game.Player.ActionSystem?.SetTileMap(_surface);
        _game.Camera?.SetWorld(_surface);
        _surface = null;
        _surfaceCombat = null;
    }

    internal static TileMap Generate(int seed, ResourceRegistry? resources, SeasonSystem? seasons)
    {
        const int size = 64;
        // Start at the outer lip, then let the floor descend into the basin.
        var map = new TileMap(size, size) { IsCave = true, SpawnX = 6, SpawnY = size / 2 };
        map.SeasonSystem = seasons;
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
        // Corner stitch shared with the surface: every vertex reads the
        // min-corner tile's elevation so adjacent quads share edges exactly.
        map.BuildCornerElevations();
        map.Tiles[map.SpawnX, map.SpawnY].IsCaveExit = true;
        PlaceOreVeins(map, resources, seed);
        return map;
    }

    private static void PlaceOreVeins(TileMap map, ResourceRegistry? resources, int seed)
    {
        if (resources == null) return;

        // The cave slopes inward from the western exit. Place guaranteed,
        // deterministic ore pockets along that route so the first visit has
        // an immediately useful copper vein and rarer finds reward exploring.
        var veins = new (string Id, (int X, int Y)[] Tiles)[]
        {
            ("copper_rock", [(12, 27), (14, 35), (17, 30), (19, 38)]),
            ("iron_rock", [(25, 25), (27, 37), (30, 29), (33, 35)]),
            ("gold_vein", [(40, 26), (43, 38), (47, 31)]),
            ("gemstone", [(50, 27), (53, 36)])
        };
        var random = new Random(seed ^ 0x4F524553);
        foreach (var (id, spots) in veins)
        {
            var def = resources.GetResource(id);
            if (def == null) continue;
            foreach (var (x, y) in spots)
            {
                var tile = map.GetTile(x, y);
                if (tile == null || tile.IsCaveExit) continue;
                int charges = Math.Max(1, def.DepletionCount);
                tile.ResourceNode = new ResourceNode(id, def, charges)
                {
                    SizeScale = 1f + (float)random.NextDouble() * def.SizeVariance
                };
            }
        }
    }

    private static CombatSystem CreateCaveCombat(MonsterRegistry? registry, QuestSystem? quests, TileMap cave)
    {
        var combat = new CombatSystem { Quests = quests };
        var troll = registry?.MonstersByBiome.Values
            .SelectMany(monsters => monsters.Values)
            .FirstOrDefault(def => def.MonsterId == "cave_troll");
        if (troll != null)
        {
            float x = (43.5f) * Constants.TileSize;
            float y = (cave.SpawnY + 0.5f) * Constants.TileSize;
            combat.SpawnMonster(troll, x, y, "cavern");
        }
        return combat;
    }
}
