namespace DontStarveRuneScape.Building;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Config;

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

    /// <summary>Get snapshot for saving.</summary>
    public BuildingSnapshot GetSnapshot()
    {
        var snapshot = new BuildingSnapshot();
        snapshot.Structures = Structures
            .Where(s => s.IsActive)
            .Select(s => new StructureSnapshot
            {
                StructureId = s.StructureId,
                TileX = s.TileX,
                TileY = s.TileY,
                Hp = s.Health,
                AssignedNpcId = s.AssignedNpcId,
            })
            .ToArray();
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(BuildingSnapshot snapshot, TileMap world, DataLoader dataLoader)
    {
        Structures.Clear();
        if (snapshot.Structures != null)
        {
            foreach (var s in snapshot.Structures)
            {
                var structureDef = dataLoader.StructuresData?
                    .FirstOrDefault(d => d.TryGetValue("id", out var id) && id is string idStr && idStr == s.StructureId);
                if (structureDef != null)
                {
                    var maxHp = 100;
                    if (structureDef.TryGetValue("max_hp", out var hpVal) && hpVal is int hpInt)
                        maxHp = hpInt;

                    var structure = new Structure
                    {
                        StructureId = s.StructureId,
                        TileX = s.TileX,
                        TileY = s.TileY,
                        WorldX = s.TileX * Constants.TileSize + Constants.TileSize / 2f,
                        WorldY = s.TileY * Constants.TileSize + Constants.TileSize / 2f,
                        Health = s.Hp > 0 ? (int)s.Hp : maxHp,
                        MaxHealth = maxHp,
                        IsActive = true,
                        AssignedNpcId = s.AssignedNpcId,
                    };
                    Structures.Add(structure);
                }
            }
        }
    }
}