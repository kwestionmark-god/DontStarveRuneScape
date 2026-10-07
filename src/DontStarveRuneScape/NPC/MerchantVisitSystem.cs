namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.World;
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

    public MerchantVisitSystem(Game game) => _game = game;

    /// <summary>Call once per frame with the current hour of day (0–24).</summary>
    public void Tick(float hourOfDay)
    {
        // Check for 06:00 crossing (dawn)
        if (_lastCheckedHour < 6f && hourOfDay >= 6f && hourOfDay < 7f)
        {
            SpawnDailyMerchants();
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

        var colonyBiome = _game.World?.GetTile(
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
            if (!faction.TerritoryBiomes.Contains(_game.World.GetTile(
                _game.ColonySystem.AnchorTileX, 
                _game.ColonySystem.AnchorTileY)?.Biome?.Id ?? ""))
                continue;

            // Find a merchant def for this faction in this biome
            var merchantDef = FindMerchantDef(faction.FactionId, colonyBiome);
            if (merchantDef == null)
                continue;

            // Spawn merchant at colony anchor
            SpawnMerchant(merchantDef, faction.FactionId);
            _todaysVisitors.Add(faction.FactionId);
        }
    }

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

        var merchant = Npc.FromDef(def);
        merchant.WorldX = (anchorX + 0.5f) * Constants.TileSize;
        merchant.WorldY = (anchorY + 0.5f) * Constants.TileSize;
        merchant.FactionId = factionId;
        merchant.Biome = _game.World.GetTile(
            _game.ColonySystem.AnchorTileX, 
            _game.ColonySystem.AnchorTileY)?.Biome?.Id ?? "plains";
        merchant.IsActive = true;

        _game.NPCSystem.NPCs.Add(merchant);
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
    }
}