namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;

/// <summary>
/// NPCSystem — Manages all NPCs in the world.
/// </summary>
public sealed class NPCSystem
{
    public List<Npc> NPCs { get; } = [];

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
        foreach (var npc in NPCs)
        {
            if (!npc.IsActive) continue;
            float dx = npc.WorldX - player.WorldX;
            float dy = npc.WorldY - player.WorldY;
            if (dx * dx + dy * dy <= 128 * 128) // 128px radius
                return npc;
        }
        return null;
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
            })
            .ToArray();
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(NPCSnapshot snapshot, DataLoader dataLoader)
    {
        NPCs.Clear();
        if (snapshot.NPCs != null)
        {
            foreach (var n in snapshot.NPCs)
            {
                string? foundId = null;
                var npcDef = dataLoader.NPCsData?
                    .FirstOrDefault(d => d.TryGetValue("id", out var id) && id is string idStr && (foundId = idStr) == n.NpcId);
                if (npcDef != null && foundId != null)
                {
                    var npc = Npc.CreateFromDef(npcDef);
                    npc.WorldX = n.WorldX;
                    npc.WorldY = n.WorldY;
                    npc.Health = (int)n.Health;
                    npc.IsActive = n.IsActive;
                    npc.RecruitedBy = n.RecruitedBy;
                    NPCs.Add(npc);
                }
            }
        }
    }
}