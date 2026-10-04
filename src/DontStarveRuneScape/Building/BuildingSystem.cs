namespace DontStarveRuneScape.Building;

using System.Collections.Generic;
using System.Linq;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Data;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Skills;
using DontStarveRuneScape.NPC;
using Inv = DontStarveRuneScape.Inventory.Inventory;

/// <summary>
/// BuildingSystem — Manages placed structures in the world.
/// </summary>
public sealed class BuildingSystem
{
    public List<Structure> Structures { get; } = [];

    /// <summary>Structure definitions this system builds from; loaded at world boot.</summary>
    public StructureDefRegistry? Registry { get; set; }

    /// <summary>Get all structures.</summary>
    public List<Structure> GetAllStructures() => Structures;

    /// <summary>Get active structures.</summary>
    public List<Structure> GetActiveStructures() => Structures.FindAll(s => s.IsActive);

    /// <summary>Tick all structures.</summary>
    public void Tick(float dt, CombatSystem? combatSystem = null)
    {
        // Update structure logic (fires, production, etc.)
    }

    /// <summary>Place a structure or found a colony construction site at the given tile: biome gate, skill gate,
    /// material gate (all-or-nothing consume), then add to the world.</summary>
    public (bool Success, string Message) PlaceStructure(string structureId, int tileX, int tileY,
        TileMap world, Inv inventory, SkillManager skillManager, ColonySystem? colony = null)
    {
        var def = Registry?.GetStructure(structureId);
        if (def == null)
            return (false, "Unknown structure.");

        var tile = world.GetTile(tileX, tileY);
        if (tile == null)
            return (false, "Can't build there.");
        if (tile.Structure?.OccupiesTile == true || Structures.Any(s => s.IsActive
                && s.TileX == tileX && s.TileY == tileY))
            return (false, "That tile already has a structure.");

        if (tile.Biome != null && def.BiomeCompatibility.Length > 0 &&
            !def.BiomeCompatibility.Contains(tile.Biome.Id))
            return (false, $"A {def.Name} can't be built on {tile.Biome.Id}.");

        if (skillManager.GetSkillLevel("construction") < def.RequiresSkillLevel)
            return (false, $"Requires construction level {def.RequiresSkillLevel}.");

        var placed = new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = tileX,
            TileY = tileY,
            WorldX = tileX * Constants.TileSize + Constants.TileSize / 2f,
            WorldY = tileY * Constants.TileSize + Constants.TileSize / 2f,
            Health = (int)def.Hp,
            MaxHealth = (int)def.Hp,
        };
        int homeDx = tileX - (colony?.AnchorTileX ?? tileX);
        int homeDy = tileY - (colony?.AnchorTileY ?? tileY);
        bool insideColonyWorkArea = colony?.IsFounded == true
            && homeDx * homeDx + homeDy * homeDy <= colony.WorkRadiusTiles * colony.WorkRadiusTiles;
        if (insideColonyWorkArea && colony != null)
        {
            foreach (var material in def.Materials)
            {
                int transfer = Math.Min(inventory.GetItemQuantity(material.ItemId), material.Quantity);
                if (transfer > 0 && colony.CanStore(material.ItemId, transfer))
                {
                    inventory.RemoveItem(material.ItemId, transfer);
                    colony.Store(material.ItemId, transfer);
                }
            }
            placed.IsUnderConstruction = true;
            placed.WorkStatus = "Awaiting materials";
            Structures.Add(placed);
            if (def.OccupiesTile) tile.Structure = def;
            return (true, $"{def.Name} construction site placed.");
        }

        foreach (var material in def.Materials)
        {
            int available = inventory.GetItemQuantity(material.ItemId)
                + (colony?.GetItemQuantity(material.ItemId) ?? 0);
            if (available < material.Quantity)
                return (false, $"Missing materials: needs {material.Quantity} {material.ItemId}.");
        }

        foreach (var material in def.Materials)
        {
            int fromInventory = Math.Min(inventory.GetItemQuantity(material.ItemId), material.Quantity);
            if (fromInventory > 0) inventory.RemoveItem(material.ItemId, fromInventory);
            int fromColony = material.Quantity - fromInventory;
            if (fromColony > 0 && colony?.RemoveItem(material.ItemId, fromColony) != true)
                throw new InvalidOperationException("Construction material storage changed during placement.");
        }

        Structures.Add(placed);
        if (def.OccupiesTile)
            tile.Structure = def;

        return (true, $"Built {def.Name}.");
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
                WorkRecipeId = s.WorkRecipeId,
                WorkRecipeQueue = s.WorkRecipeQueue.ToArray(),
                WorkOrdersPaused = s.WorkOrdersPaused,
                HasManualWorkOrder = s.HasManualWorkOrder,
                IsDependencyOrder = s.IsDependencyOrder,
                IsUnderConstruction = s.IsUnderConstruction,
                ConstructionMaterialsPaid = s.ConstructionMaterialsPaid,
                WorkProgress = s.WorkProgress,
                WorkStatus = s.WorkStatus,
            })
            .ToArray();
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(BuildingSnapshot snapshot, TileMap world)
    {
        Structures.Clear();
        if (snapshot.Structures == null) return;

        foreach (var s in snapshot.Structures)
        {
            // Look the def up in the registry (the raw rows use structure_id,
            // which the old "id" lookup never matched).
            var structureDef = Registry?.GetStructure(s.StructureId);
            if (structureDef == null) continue;

            Structures.Add(new Structure
            {
                StructureId = s.StructureId,
                StructureDef = structureDef,
                TileX = s.TileX,
                TileY = s.TileY,
                WorldX = s.TileX * Constants.TileSize + Constants.TileSize / 2f,
                WorldY = s.TileY * Constants.TileSize + Constants.TileSize / 2f,
                Health = s.Hp > 0 ? (int)s.Hp : (int)structureDef.Hp,
                MaxHealth = (int)structureDef.Hp,
                IsActive = true,
                AssignedNpcId = s.AssignedNpcId,
                WorkRecipeId = s.WorkRecipeId,
                WorkOrdersPaused = s.WorkOrdersPaused,
                HasManualWorkOrder = s.HasManualWorkOrder,
                IsDependencyOrder = s.IsDependencyOrder,
                IsUnderConstruction = s.IsUnderConstruction,
                ConstructionMaterialsPaid = s.ConstructionMaterialsPaid,
                WorkProgress = s.WorkProgress,
                WorkStatus = string.IsNullOrEmpty(s.WorkStatus) ? "Idle" : s.WorkStatus,
            });
            Structures[^1].WorkRecipeQueue.AddRange(s.WorkRecipeQueue ?? []);
            if (structureDef.OccupiesTile)
            {
                var tile = world.GetTile(s.TileX, s.TileY);
                if (tile != null) tile.Structure = structureDef;
            }
        }
    }
}
