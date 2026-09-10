namespace DontStarveRuneScape.Building;

using System.Collections.Generic;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.World;

/// <summary>
/// BuildingSystem — Manages placed structures in the world.
/// </summary>
public sealed class BuildingSystem
{
    public List<Structure> Structures { get; } = [];

    /// <summary>Get all structures.</summary>
    public List<Structure> GetAllStructures() => Structures;

    /// <summary>Get active structures.</summary>
    public List<Structure> GetActiveStructures() => Structures.FindAll(s => s.IsActive);

    /// <summary>Tick all structures.</summary>
    public void Tick(float dt, CombatSystem? combatSystem = null)
    {
        // Update structure logic (fires, production, etc.)
    }

    /// <summary>Place a structure at the given tile position.</summary>
    public bool PlaceStructure(string structureId, int tileX, int tileY, TileMap world)
    {
        // Placeholder implementation
        return false;
    }

    /// <summary>Remove a structure.</summary>
    public bool RemoveStructure(Structure structure)
    {
        return Structures.Remove(structure);
    }

    /// <summary>Assign an NPC to a structure.</summary>
    public (bool Success, string Message) AssignNpcToStructure(string npcId, string structureId)
    {
        var structure = Structures.Find(s => s.StructureId == structureId && s.IsActive);
        if (structure == null)
            return (false, "Structure not found.");

        structure.AssignedNpcId = npcId;
        return (true, $"NPC assigned to {structureId}.");
    }
}