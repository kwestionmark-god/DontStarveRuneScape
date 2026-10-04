namespace DontStarveRuneScape.NPC;

using System;
using System.Collections.Generic;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.World;

/// <summary>
/// NPCSystem — Manages all NPCs in the world.
/// </summary>
public sealed class NPCSystem
{
    public List<Npc> NPCs { get; } = [];

    /// <summary>Spawn all NPCs from the registry at their definition tile
    /// positions; the tile's real biome is recorded for trade-stock matching.
    /// Called once at world boot.</summary>
    public void LoadFromRegistry(Data.NpcRegistry registry, World.TileMap? world)
    {
        int tileSize = Config.Constants.TileSize;
        foreach (var def in registry.Npcs.Values)
        {
            var npc = Npc.FromDef(def);
            npc.WorldX = (def.WorldX + 0.5f) * tileSize;
            npc.WorldY = (def.WorldY + 0.5f) * tileSize;
            var tile = world?.GetTile(def.WorldX, def.WorldY);
            npc.Biome = tile?.Biome?.Id ?? string.Empty;
            NPCs.Add(npc);
        }
    }

    public void Tick(float dt)
    {
        // Update NPC AI, movement, etc.
    }

    public void TickFactionNPCs(float dt)
    {
        // Update faction NPC behavior
    }

    /// <summary>Check if there's an NPC near the player.</summary>
    public Npc? CheckProximity(Player player)
    {
        return FindNearby(player, null);
    }

    /// <summary>Check if there's an NPC of the given type near the player
    /// (null type matches any).</summary>
    public Npc? FindNearby(Player player, string? type)
    {
        Npc? best = null;
        float bestDistSq = 128 * 128;
        foreach (var npc in NPCs)
        {
            if (!npc.IsActive) continue;
            if (type != null && npc.NpcType != type) continue;
            float dx = npc.WorldX - player.WorldX;
            float dy = npc.WorldY - player.WorldY;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = npc;
            }
        }
        return best;
    }

    /// <summary>Find the nearest active NPC who actually offers at least one quest.</summary>
    public Npc? FindNearbyQuestGiver(Player player)
    {
        Npc? best = null;
        float bestDistSq = 128 * 128;
        foreach (var npc in NPCs)
        {
            if (!npc.IsActive || npc.AvailableQuests.Count == 0) continue;
            float dx = npc.WorldX - player.WorldX;
            float dy = npc.WorldY - player.WorldY;
            float distSq = dx * dx + dy * dy;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = npc;
            }
        }
        return best;
    }

    /// <summary>Assign NPC to structure.</summary>
    public (bool Success, string Message) AssignNpcToStructure(string npcId, string structureId)
    {
        var npc = NPCs.Find(n => n.NpcId == npcId);
        if (npc == null)
            return (false, "NPC not found.");

        // Structure assignment logic would go here
        return (true, $"Assigned {npc.Name} to {structureId}.");
    }

    /// <summary>Get snapshot for saving.</summary>
    public NPCSnapshot GetSnapshot()
    {
        var snapshot = new NPCSnapshot();
        snapshot.NPCs = NPCs
            .Where(n => n.IsActive)
            .Select(n => new NPCDataSnapshot
            {
                NpcId = n.NpcId,
                Type = n.NpcType,
                WorldX = n.WorldX,
                WorldY = n.WorldY,
                Health = n.Health,
                IsActive = n.IsActive,
                RecruitedBy = n.RecruitedBy,
                IsRecruited = n.IsRecruited,
                RecruitBehavior = n.RecruitBehavior,
                ColonyHunger = n.ColonyHunger,
                ColonyRest = n.ColonyRest,
            })
            .ToArray();
        return snapshot;
    }

    /// <summary>Restore from snapshot. NPC definitions come from the real
    /// typed registry (the npcs.json path used at boot); the snapshot
    /// overlays position, health, recruitment, and colony needs.</summary>
    public void RestoreSnapshot(NPCSnapshot snapshot, NpcRegistry? registry, TileMap? world = null)
    {
        NPCs.Clear();
        if (snapshot.NPCs != null)
        {
            foreach (var n in snapshot.NPCs)
            {
                if (registry?.Npcs.TryGetValue(n.NpcId, out var def) != true)
                    continue;
                var npc = Npc.FromDef(def);
                npc.WorldX = n.WorldX;
                npc.WorldY = n.WorldY;
                var tile = world?.GetTileAtWorld(npc.WorldX, npc.WorldY);
                npc.Biome = tile?.Biome?.Id ?? string.Empty;
                npc.Health = (int)n.Health;
                npc.IsActive = n.IsActive;
                npc.RecruitedBy = n.RecruitedBy;
                npc.IsRecruited = n.IsRecruited;
                npc.RecruitBehavior = n.RecruitBehavior;
                npc.ColonyHunger = Math.Clamp(n.ColonyHunger, 0f, 100f);
                npc.ColonyRest = Math.Clamp(n.ColonyRest, 0f, 100f);
                npc.ColonyRestStatus = npc.ColonyRest <= 25f ? "Exhausted"
                    : npc.ColonyRest <= 50f ? "Tired"
                    : npc.ColonyRest <= 75f ? "Weary" : "Rested";
                npc.ColonyNeedStatus = npc.ColonyHunger <= 15f ? "Starving"
                    : npc.ColonyHunger <= 35f ? "Hungry" : "Fed";
                NPCs.Add(npc);
            }
        }
    }
}
