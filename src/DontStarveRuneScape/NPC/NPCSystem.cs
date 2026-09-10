namespace DontStarveRuneScape.NPC;

using System.Collections.Generic;
using DontStarveRuneScape.Core;

/// <summary>
/// NPCSystem — Manages all NPCs in the world.
/// </summary>
public sealed class NPCSystem
{
    public List<NPC> NPCs { get; } = [];

    public void Tick(float dt)
    {
        // Update NPC AI, movement, etc.
    }

    public void TickFactionNPCs(float dt)
    {
        // Update faction NPC behavior
    }

    /// <summary>Check if there's an NPC near the player.</summary>
    public NPC? CheckProximity(Player player)
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
}