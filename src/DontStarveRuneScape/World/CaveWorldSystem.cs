namespace DontStarveRuneScape.World;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.World;

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
    // Phase-4 remainder: cave expedition guard management
    private readonly HashSet<string> _guardsOnExpedition = [];
    private float _surfaceUnguardedTimer = 0f;
    private const float RaidThresholdSeconds = 300f; // 5 minutes real-time

    public bool IsInside => _surface != null;
    /// <summary>Timer counting while the cave is active and no guards remain on the surface.</summary>
    public float SurfaceUnguardedTimer => _surfaceUnguardedTimer;
    
    /// <summary>Surface combat system (for raid spawning and other surface events).</summary>
    public CombatSystem? SurfaceCombat => _surfaceCombat;

    /// <summary>Surface world map while inside a cave (null otherwise).</summary>
    public TileMap? SurfaceWorld => _surface;

    public CaveWorldSystem(Game game) => _game = game;

    public void InteractWith(Tile tile)
    {
        if (IsInside && tile.IsCaveExit) { Exit(); return; }
        if (!IsInside && tile.IsCaveEntrance) Enter(tile.X, tile.Y);
    }

    /// <summary>Move a specific guard to the cave (expedition). Called from
    /// the colony dashboard when the player clicks "Cave Expedition" on a
    /// guard. Returns false if the NPC is not a guard, not on the surface,
    /// or already on expedition.</summary>
    public bool BringGuard(string npcId)
    {
        if (_surface == null) return false; // not in a cave
        var npc = _game.NPCSystem?.NPCs.FirstOrDefault(n => n.NpcId == npcId);
        if (npc == null || !npc.IsActive || npc.RecruitBehavior != "guard"
            || _guardsOnExpedition.Contains(npcId))
            return false;

        // Teleport this guard to the cave
        var cave = _cave;
        if (cave == null) return false;
        _surfaceWorkerPositions[npcId] = (npc.WorldX, npc.WorldY);
        npc.WorldX = (cave.SpawnX + 0.5f) * Constants.TileSize;
        npc.WorldY = (cave.SpawnY + 0.5f) * Constants.TileSize;
        npc.VelocityX = npc.VelocityY = 0f;
        _guardsOnExpedition.Add(npcId);
        return true;
    }

    /// <summary>Whether any guard NPCs remain on the surface (not on
    /// expedition). Used by the surface-unguarded timer.</summary>
    public bool HasSurfaceGuards()
    {
        if (_surface == null) return true; // not in a cave, trivially safe
        return _game.NPCSystem?.NPCs.Any(n => n.IsActive && n.IsRecruited
            && n.RecruitBehavior == "guard" && !_guardsOnExpedition.Contains(n.NpcId))
            == true;
    }

    /// <summary>Advance the surface-unguarded timer by the given seconds
    /// (test helper; the real timer ticks in CaveWorldSystem.Tick).</summary>
    public void AdvanceSurfaceTimer(float seconds)
    {
        bool inside = IsInside;
        bool hasGuards = HasSurfaceGuards();
        Console.WriteLine($"AdvanceSurfaceTimer: inside={inside}, hasGuards={hasGuards}, seconds={seconds}");
        if (!inside || hasGuards) 
        { 
            Console.WriteLine($"AdvanceSurfaceTimer: SKIPPED - inside={inside}, hasGuards={hasGuards}");
            return; 
        }
        _surfaceUnguardedTimer += seconds;
        Console.WriteLine($"AdvanceSurfaceTimer: ADVANCED by {seconds}, timer={_surfaceUnguardedTimer}");
        TryRollRaid();
    }

    /// <summary>Whether the named guard is currently on the cave expedition.</summary>
    public bool IsGuardOnExpedition(string npcId) => _guardsOnExpedition.Contains(npcId);

    /// <summary>Snapshot the expedition state for saving (spec section 6).</summary>
    public CaveSnapshot GetSnapshot() => new()
    {
        IsInside = IsInside,
        EntranceX = _entranceX,
        EntranceY = _entranceY,
        SurfaceWorkerPositions = [.. _surfaceWorkerPositions.Select(kv => new CaveWorkerPositionSnapshot
        {
            NpcId = kv.Key,
            X = kv.Value.X,
            Y = kv.Value.Y,
        })],
        GuardsOnExpedition = [.. _guardsOnExpedition],
        SurfaceUnguardedTimer = _surfaceUnguardedTimer,
    };

    /// <summary>Restore expedition state. Rebuilds the cave (deterministic
    /// from seed + entrance), re-points Game.World/CombatSystem at the cave,
    /// and marks expedition guards. Worker surface positions are stored so
    /// Exit() returns everyone correctly. No-op when the snapshot says the
    /// player was on the surface.</summary>
    public void RestoreSnapshot(CaveSnapshot snapshot, TileMap surface, CombatSystem surfaceCombat)
    {
        _guardsOnExpedition.Clear();
        _surfaceWorkerPositions.Clear();
        foreach (var guard in snapshot.GuardsOnExpedition)
            _guardsOnExpedition.Add(guard);
        foreach (var pos in snapshot.SurfaceWorkerPositions)
            _surfaceWorkerPositions[pos.NpcId] = (pos.X, pos.Y);

        if (!snapshot.IsInside)
        {
            _surface = null;
            _surfaceCombat = null;
            _surfaceUnguardedTimer = 0f;
            return;
        }

        // Re-enter: rebuild the cave from the seed at the saved entrance
        _surface = surface;
        _surfaceCombat = surfaceCombat;
        _entranceX = snapshot.EntranceX;
        _entranceY = snapshot.EntranceY;
        var seed = unchecked(_game.Seed * 397 ^ _entranceX * 31 ^ _entranceY);
        _cave = Generate(seed, _game.ResourceRegistry, _game.SeasonSystem);
        _caveCombat = CreateCaveCombat(_game.MonsterRegistry, _game.QuestSystem, _cave);
        _game.World = _cave;
        _game.CombatSystem = _caveCombat;
        _surfaceUnguardedTimer = snapshot.SurfaceUnguardedTimer;

        // Expedition guards teleport to the cave spawn with the player
        if (_game.NPCSystem != null)
        {
            foreach (var guardId in _guardsOnExpedition)
            {
                var guard = _game.NPCSystem.NPCs.FirstOrDefault(n => n.NpcId == guardId);
                if (guard == null) continue;
                guard.WorldX = (_cave.SpawnX + 0.5f) * Constants.TileSize;
                guard.WorldY = (_cave.SpawnY + 0.5f) * Constants.TileSize;
                guard.VelocityX = guard.VelocityY = 0f;
            }
        }

        var player = _game.Player;
        if (player != null)
        {
            player.WorldX = (_cave!.SpawnX + 0.5f) * Constants.TileSize;
            player.WorldY = (_cave.SpawnY + 0.5f) * Constants.TileSize;
            player.TargetX = player.WorldX;
            player.TargetY = player.WorldY;
            player.ActionSystem?.SetTileMap(_cave);
        }
        _game.Camera?.SetWorld(_cave!);
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
        _guardsOnExpedition.Clear();
        if (_game.NPCSystem != null)
        {
            foreach (var worker in _game.NPCSystem.NPCs.Where(n => n.IsActive && n.IsRecruited))
            {
                // Guards stay on the surface by default; only assistants/
                // other behaviors teleport to the cave. Player always goes.
                if (worker.RecruitBehavior == "guard") continue;
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
            // Return expedition guards to their surface positions
            foreach (var guardId in _guardsOnExpedition)
            {
                var guard = _game.NPCSystem.NPCs.FirstOrDefault(n => n.NpcId == guardId);
                if (guard != null && _surfaceWorkerPositions.TryGetValue(guardId, out var pos))
                {
                    guard.WorldX = pos.X;
                    guard.WorldY = pos.Y;
                    guard.VelocityX = guard.VelocityY = 0f;
                }
            }
        }
        _surfaceWorkerPositions.Clear();
        _guardsOnExpedition.Clear();
        _surfaceUnguardedTimer = 0f;
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

    /// <summary>Advance the surface-unguarded timer and roll for raids when
    /// the threshold is crossed. Called once per game tick while the cave
    /// is active and no guards remain on the surface.</summary>
    public void Tick(float dt)
    {
        if (!IsInside || HasSurfaceGuards())
        {
            _surfaceUnguardedTimer = 0f;
            return;
        }
        _surfaceUnguardedTimer += dt;
        TryRollRaid();
    }

    private void TryRollRaid()
    {
        Console.WriteLine($"TryRollRaid ENTRY: timer={_surfaceUnguardedTimer}, threshold={RaidThresholdSeconds}");
        if (_surfaceUnguardedTimer < RaidThresholdSeconds) return;
        if (_surface == null || _game.NPCSystem == null || _game.MonsterRegistry == null) return;

        // Find a hostile faction with standing < 0.25 that has territory
        // overlapping the colony biome, then spawn a raid party at the
        // colony perimeter.
        var factionRegistry = _game.FactionRegistry;
        if (factionRegistry == null) { Console.WriteLine("TryRollRaid: no factionRegistry"); return; }
        var colonySystem = _game.ColonySystem;
        if (colonySystem == null || !colonySystem.IsFounded) { Console.WriteLine("TryRollRaid: no colonySystem"); return; }
        var colonyBiome = _surface.GetTile(colonySystem.AnchorTileX, colonySystem.AnchorTileY)?.Biome?.Id;
        if (string.IsNullOrEmpty(colonyBiome)) { Console.WriteLine("TryRollRaid: no colonyBiome"); return; }
        Console.WriteLine($"TryRollRaid: colonyBiome={colonyBiome}");

        var hostileFactions = factionRegistry.Factions.Values
            .Where(f => f.BaseHostility > 0f
                && f.TerritoryBiomes.Contains(colonyBiome)
                && f.FactionId.Length > 0)
            .ToArray();
        Console.WriteLine($"TryRollRaid: hostileFactions count={hostileFactions.Length}");
        if (hostileFactions.Length == 0) return;

        // Filter to factions with standing < 0.25 (hostile tier)
        var factionSystem = _game.FactionSystem;
        var trulyHostile = hostileFactions
            .Where(f => 
            {
                float standing = factionSystem?.StandingOf(f.FactionId) ?? QuestSystem.DefaultStanding;
                Console.WriteLine($"TryRollRaid: faction={f.FactionId}, BaseHostility={f.BaseHostility}, standing={standing}");
                return standing < 0.25f;
            })
            .ToArray();
        Console.WriteLine($"TryRollRaid: trulyHostile count={trulyHostile.Length}");
        if (trulyHostile.Length == 0) return;

        var rand = new Random();
        var faction = trulyHostile[rand.Next(trulyHostile.Length)];
        Console.WriteLine($"TryRollRaid: selected faction={faction.FactionId}, monsterTypes={string.Join(",", faction.HostileMonsterTypes)}");
        if (faction.HostileMonsterTypes.Length == 0) return;

        var monsterDefs = new List<MonsterDef>();
        var monsterRegistry = _game.MonsterRegistry;
        if (monsterRegistry == null) return;
        foreach (var monsterId in faction.HostileMonsterTypes)
        {
            foreach (var biomeMonsters in monsterRegistry.MonstersByBiome.Values)
            {
                if (biomeMonsters.TryGetValue(monsterId, out var def))
                {
                    monsterDefs.Add(def);
                    Console.WriteLine($"TryRollRaid: found monster {monsterId} in biome");
                    break;
                }
            }
        }
        Console.WriteLine($"TryRollRaid: monsterDefs count={monsterDefs.Count}");
        if (monsterDefs.Count == 0) return;

        int count = 2 + rand.Next(3); // 2-4
        int anchorX = colonySystem.AnchorTileX;
        int anchorY = colonySystem.AnchorTileY;
        for (int i = 0; i < count; i++)
        {
            var def = monsterDefs[rand.Next(monsterDefs.Count)];
            double angle = rand.NextDouble() * Math.PI * 2;
            // Ring point, clamped to the map: a raid party always materializes
            // even when the colony sits near a map edge.
            int spawnX = Math.Clamp(anchorX + (int)MathF.Round(MathF.Cos((float)angle) * 9f), 0, _surface.Width - 1);
            int spawnY = Math.Clamp(anchorY + (int)MathF.Round(MathF.Sin((float)angle) * 9f), 0, _surface.Height - 1);
            float wx = (spawnX + 0.5f) * Constants.TileSize;
            float wy = (spawnY + 0.5f) * Constants.TileSize;
            var combat = _surfaceCombat;
            if (combat == null) continue;
            combat.SpawnMonster(def, wx, wy);
            Console.WriteLine($"TryRollRaid: spawned {def.Name} at ({spawnX},{spawnY})");
        }

        _surfaceUnguardedTimer = 0f;
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
        var rand = new Random();
        // Place a few ore veins in the cavern
        for (int v = 0; v < 8; v++)
        {
            int x = rand.Next(1, map.Width - 1);
            int y = rand.Next(1, map.Height - 1);
            if (!WorkerPathfinder.CanStand(map, x, y)) continue;
            string oreId = rand.Next(3) switch
            {
                0 => "copper_ore",
                1 => "tin_ore",
                _ => "iron_ore",
            };
            map.GetTile(x, y)!.ResourceNode = new ResourceNode($"ore_{oreId}_{x}_{y}", new ResourceDef
            {
                Id = oreId,
                Name = oreId,
                YieldItem = oreId,
                Yield = 3,
                Xp = 5f,
                DepletionCount = 5,
                Seasons = [],
            }, 1f);
        }
    }

    internal static CombatSystem CreateCaveCombat(MonsterRegistry? monsterRegistry, QuestSystem? questSystem, TileMap cave)
    {
        var combat = new CombatSystem { Quests = questSystem };
        // Spawn a few initial cave monsters
        if (monsterRegistry != null)
        {
            combat.SpawnFromRegistry(monsterRegistry, cave, null);
        }
        return combat;
    }
}

/// <summary>Cave expedition state for saving (spec section 6).</summary>
public sealed class CaveSnapshot
{
    public bool IsInside { get; set; }
    public int EntranceX { get; set; }
    public int EntranceY { get; set; }
    public CaveWorkerPositionSnapshot[] SurfaceWorkerPositions { get; set; } = [];
    public string[] GuardsOnExpedition { get; set; } = [];
    public float SurfaceUnguardedTimer { get; set; }
}

/// <summary>A worker's saved surface position (restored on cave exit).</summary>
public sealed class CaveWorkerPositionSnapshot
{
    public string NpcId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
}