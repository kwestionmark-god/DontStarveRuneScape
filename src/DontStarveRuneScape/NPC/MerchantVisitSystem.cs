namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.Config;

/// <summary>
/// MerchantVisitSystem — Handles daily merchant visits from friendly factions.
/// At 06:00 each day, factions with standing >= 0.65 send a merchant to the colony anchor.
/// Merchants stay for 4 hours (06:00–10:00) then depart.
/// </summary>
public sealed class MerchantVisitSystem
{
    private readonly Game _game;
    private readonly HashSet<string> _todaysVisitors = [];
    private float _lastCheckedHour = -1f;
    private MerchantNpc? _activeMerchant;

    public MerchantNpc? ActiveMerchant => _activeMerchant;

    /// <summary>Test hook: forces the daily raid roll outcome (null = random).</summary>
    public bool? RaidRollOverride { get; set; }

    /// <summary>Seeded RNG for the raid chance roll (tests; defaults to shared).</summary>
    public Random? RaidRandom { get; set; }

    public MerchantVisitSystem(Game game) => _game = game;

    /// <summary>Call once per frame with the current hour of day (0–24).</summary>
    public void Tick(float hourOfDay)
    {
        // Check for 06:00 crossing (dawn)
        if (_lastCheckedHour < 6f && hourOfDay >= 6f && hourOfDay < 7f)
        {
            SpawnDailyMerchants();
            RollDailyRaid();
            _todaysVisitors.Clear();
        }
        
        // Check for 10:00 departure (4-hour visit window)
        if (hourOfDay >= 10f && hourOfDay < 11f)
        {
            DepartMerchants();
        }

        _lastCheckedHour = hourOfDay;
        
        // Reset daily tracker at midnight
        if (_lastCheckedHour >= 23f && hourOfDay < 1f)
        {
            _todaysVisitors.Clear();
        }
    }

    private void SpawnDailyMerchants()
    {
        if (_game.FactionSystem == null || _game.FactionRegistry == null || _game.NPCSystem == null)
            return;
        if (_game.ColonySystem == null || !_game.ColonySystem.IsFounded)
            return;

        var colonyBiome = ResolveSurfaceWorld()?.GetTile(
            _game.ColonySystem.AnchorTileX,
            _game.ColonySystem.AnchorTileY)?.Biome?.Id;
        if (string.IsNullOrEmpty(colonyBiome))
            return;

        var anchorX = _game.ColonySystem.AnchorTileX;
        var anchorY = _game.ColonySystem.AnchorTileY;

        foreach (var faction in _game.FactionRegistry.Factions.Values)
        {
            // Check if faction already sent a merchant today
            if (_todaysVisitors.Contains(faction.FactionId))
                continue;

            // Check standing threshold (>= 0.65 = friendly/allied)
            float standing = _game.FactionSystem?.StandingOf(faction.FactionId) ?? 0.5f;
            if (standing < 0.65f)
                continue;

            // Check if faction has territory in this biome
            if (!faction.TerritoryBiomes.Contains(colonyBiome))
                continue;

            // Find a merchant def for this faction in this biome
            var merchantDef = FindMerchantDef(faction.FactionId, colonyBiome);
            if (merchantDef == null)
                continue;

            // Spawn merchant at colony anchor
            SpawnMerchant(merchantDef, faction.FactionId);
            _todaysVisitors.Add(faction.FactionId);
            Notify($"Merchants from {faction.Name} arrived", 255, 220, 120);
        }
    }

    /// <summary>Resolve the surface world map: while the player is inside a
    /// cave, Game.World points at the cave, so use CaveWorlds' stored surface.</summary>
    private TileMap? ResolveSurfaceWorld()
        => _game.CaveWorlds?.IsInside == true ? _game.CaveWorlds.SurfaceWorld : _game.World;

    /// <summary>Resolve the surface combat system (raids target the surface).</summary>
    private CombatSystem? ResolveSurfaceCombat()
        => _game.CaveWorlds?.IsInside == true ? _game.CaveWorlds.SurfaceCombat : _game.CombatSystem;

    /// <summary>Daily at 06:00: hostile factions (standing < 0.25) whose
    /// territory overlaps the colony biome roll 0.15 * (0.25 - standing)
    /// raid chance; success spawns a 2–4 monster raid party at the colony
    /// perimeter on the surface map.</summary>
    private void RollDailyRaid()
    {
        if (_game.FactionSystem == null || _game.FactionRegistry == null)
            return;
        var colony = _game.ColonySystem;
        if (colony == null || !colony.IsFounded)
            return;

        var surface = ResolveSurfaceWorld();
        var combat = ResolveSurfaceCombat();
        if (surface == null || combat == null)
            return;

        var colonyBiome = surface.GetTile(colony.AnchorTileX, colony.AnchorTileY)?.Biome?.Id;
        if (string.IsNullOrEmpty(colonyBiome))
            return;

        var rand = RaidRandom ?? Random.Shared;
        foreach (var faction in _game.FactionRegistry.Factions.Values)
        {
            if (faction.HostileMonsterTypes.Length == 0
                || !faction.TerritoryBiomes.Contains(colonyBiome))
                continue;

            float standing = _game.FactionSystem.StandingOf(faction.FactionId);
            if (standing >= 0.25f)
                continue;

            float chance = 0.15f * (0.25f - standing);
            bool raid = RaidRollOverride ?? rand.NextDouble() < chance;
            if (!raid)
                continue;

            // Resolve monster defs from the faction's hostile list
            var monsterDefs = new List<MonsterDef>();
            var registry = _game.MonsterRegistry;
            if (registry == null)
                continue;
            foreach (var monsterId in faction.HostileMonsterTypes)
            {
                foreach (var biomeMonsters in registry.MonstersByBiome.Values)
                {
                    if (biomeMonsters.TryGetValue(monsterId, out var def))
                    {
                        monsterDefs.Add(def);
                        break;
                    }
                }
            }
            if (monsterDefs.Count == 0)
                continue;

            int count = 2 + rand.Next(3); // 2–4 raiders
            for (int i = 0; i < count; i++)
            {
                double angle = rand.NextDouble() * Math.PI * 2;
                // Ring point clamped to map bounds: the party always materializes.
                int spawnX = Math.Clamp(colony.AnchorTileX + (int)MathF.Round(MathF.Cos((float)angle) * 9f), 0, surface.Width - 1);
                int spawnY = Math.Clamp(colony.AnchorTileY + (int)MathF.Round(MathF.Sin((float)angle) * 9f), 0, surface.Height - 1);
                float wx = (spawnX + 0.5f) * Constants.TileSize;
                float wy = (spawnY + 0.5f) * Constants.TileSize;
                combat.SpawnMonster(monsterDefs[rand.Next(monsterDefs.Count)], wx, wy);
            }
            Notify($"Raid incoming: {faction.Name} raiders at the perimeter!", 255, 90, 90);
        }
    }

    /// <summary>Post a player notification through the existing ActionSystem channel.</summary>
    private void Notify(string text, byte r, byte g, byte b)
        => _game.Player?.ActionSystem?.AddNotification(text, (r, g, b));

    private NpcDef? FindMerchantDef(string factionId, string biome)
    {
        if (_game.NpcRegistry == null)
            return null;

        // Look for a merchant def with matching faction
        // (biome is already checked at faction level via TerritoryBiomes)
        return _game.NpcRegistry.Npcs.Values
            .FirstOrDefault(d => d.Type == "merchant" 
                && d.Faction == factionId);
    }

    private void SpawnMerchant(NpcDef def, string factionId)
    {
        if (_game.NPCSystem == null || _game.World == null)
            return;

        var anchorX = _game.ColonySystem!.AnchorTileX;
        var anchorY = _game.ColonySystem!.AnchorTileY;

        var merchant = Npc.FromDef(def) as MerchantNpc;
        if (merchant == null)
            return;
        merchant.WorldX = (anchorX + 0.5f) * Constants.TileSize;
        merchant.WorldY = (anchorY + 0.5f) * Constants.TileSize;
        merchant.FactionId = factionId;
        merchant.Biome = _game.World.GetTile(
            _game.ColonySystem.AnchorTileX, 
            _game.ColonySystem.AnchorTileY)?.Biome?.Id ?? "plains";
        merchant.IsActive = true;

        _game.NPCSystem.NPCs.Add(merchant);
        _activeMerchant = merchant;
    }

    private void DepartMerchants()
    {
        if (_game.NPCSystem == null)
            return;

        // Remove all merchant NPCs that are not recruited
        var merchantsToRemove = _game.NPCSystem.NPCs
            .Where(n => n.NpcType == "merchant" && !n.IsRecruited)
            .ToList();

        foreach (var merchant in merchantsToRemove)
        {
            _game.NPCSystem.NPCs.Remove(merchant);
        }
        _activeMerchant = null;
    }
}