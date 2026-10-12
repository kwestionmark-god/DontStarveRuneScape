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

    /// <summary>Sub-cell placement (slice 7a brick grid): a brick def places
    /// at a 16px sub-cell of the tile, stacking courses when the sub-cell
    /// already holds a column. Fences and non-blocks ignore the sub-cell and
    /// route to tile-level placement unchanged.</summary>
    public (bool Success, string Message) PlaceStructure(string structureId, int tileX, int tileY,
        int subX, int subY, TileMap world, Inv inventory, SkillManager skillManager,
        ColonySystem? colony = null, Skills.Firemaking.FiremakingSkill? firemaking = null)
    {
        var def = Registry?.GetStructure(structureId);
        if (def == null)
            return (false, "Unknown structure.");
        if (def.IsBlock && structureId != "fence_block")
            return PlaceBrick(def, tileX, tileY, subX, subY,
                world, inventory, skillManager, colony, firemaking);
        return PlaceStructure(structureId, tileX, tileY, world, inventory, skillManager, colony, firemaking);
    }

    /// <summary>Brick placement: gates in the historical order (occupancy,
    /// pad, biome, skill), then the sub-cell column, then the material
    /// charge. Bricks bypass the colony-site flow — a 20s worker site per
    /// 16px brick would be absurd; the colony store still chips in on
    /// materials.</summary>
    private (bool Success, string Message) PlaceBrick(StructureDef def, int tileX, int tileY,
        int subX, int subY, TileMap world, Inv inventory, SkillManager skillManager,
        ColonySystem? colony, Skills.Firemaking.FiremakingSkill? firemaking)
    {
        if (subX < 0 || subX >= Constants.BrickSubCells
            || subY < 0 || subY >= Constants.BrickSubCells)
            return (false, "Pick a spot on the brick grid.");

        var tile = world.GetTile(tileX, tileY);
        if (tile == null)
            return (false, "Can't build there.");

        // Occupancy partition: bricks coexist with blocks only.
        var occupants = Structures.Where(s => s.IsActive && s.TileX == tileX && s.TileY == tileY).ToList();
        if (occupants.Any(s => s.StructureDef?.IsBlock != true))
            return (false, "That tile already has a structure.");

        // Blocks are ground with vertical extent: flat pad + dry land (the
        // same tile-level truth the old mounds gated on).
        if (tile.HasWater)
            return (false, "Can't build a block in water.");
        var corners = tile.CornerElevations;
        if (corners is { Length: 4 })
        {
            float spread = corners.Max() - corners.Min();
            if (spread > Constants.FlatPadMaxSpread)
                return (false, "Needs flat ground — level this tile with a shovel.");
        }

        if (tile.Biome != null && def.BiomeCompatibility.Length > 0 &&
            !def.BiomeCompatibility.Contains(tile.Biome.Id))
            return (false, $"A {def.Name} can't be built on {tile.Biome.Id}.");

        if (skillManager.GetSkillLevel("construction") < def.RequiresSkillLevel)
            return (false, $"Requires construction level {def.RequiresSkillLevel}.");

        // The sub-cell column: repeated placement stacks courses.
        int layer = occupants.Count(s => s.SubX == subX && s.SubY == subY);
        if ((layer + 1) * def.StackHeight > Constants.BlockColumnMaxHeight)
            return (false, "This block column is already at full height.");

        // Materials: all-or-nothing, inventory first then the colony store.
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

        var placed = new Structure
        {
            StructureId = def.Id,
            StructureDef = def,
            TileX = tileX,
            TileY = tileY,
            SubX = subX,
            SubY = subY,
            Layer = layer,
            WorldX = tileX * Constants.TileSize + subX * Constants.BrickCellSize + Constants.BrickCellSize / 2f,
            WorldY = tileY * Constants.TileSize + subY * Constants.BrickCellSize + Constants.BrickCellSize / 2f,
            Health = (int)def.Hp,
            MaxHealth = (int)def.Hp,
        };
        Structures.Add(placed);
        if (def.OccupiesTile)
            tile.Structure = def;

        // The player laid the brick with their own hands: same placement
        // XP as every other structure build.
        skillManager.AddXp("construction", 10f);

        return (true, $"Built {def.Name}.");
    }

    /// <summary>Place a structure or found a colony construction site at the given tile: biome gate, skill gate,
    /// material gate (all-or-nothing consume), then add to the world. When firemaking is provided and the def
    /// is a hearth (light_seconds > 0), the placed structure lights its fire and the build trains construction.</summary>
    public (bool Success, string Message) PlaceStructure(string structureId, int tileX, int tileY,
        TileMap world, Inv inventory, SkillManager skillManager, ColonySystem? colony = null,
        Skills.Firemaking.FiremakingSkill? firemaking = null)
    {
        var def = Registry?.GetStructure(structureId);
        if (def == null)
            return (false, "Unknown structure.");
        // Bricks place per sub-cell only (slice 7a): a tile-level call has
        // no idea which of the 16 cells the player means. Fences stay
        // tile-level.
        if (def.IsBlock && structureId != "fence_block")
            return (false, "Pick a spot on the brick grid.");

        var tile = world.GetTile(tileX, tileY);
        if (tile == null)
            return (false, "Can't build there.");
        if (tile.Structure?.OccupiesTile == true || Structures.Any(s => s.IsActive
                && s.TileX == tileX && s.TileY == tileY))
        {
            // Slice-3 stacking partition: a block def may stack onto a tile
            // whose occupants are ALL blocks, within the column height budget.
            // Sites count toward the budget (no over-committing a column).
            var occupants = Structures.Where(s => s.IsActive && s.TileX == tileX && s.TileY == tileY).ToList();
            if (!def.IsBlock || occupants.Any(s => s.StructureDef?.IsBlock != true))
                return (false, "That tile already has a structure.");
            // Tile-level budget counts only tile-level blocks (fences) —
            // brick columns budget per sub-cell in PlaceBrick.
            float column = occupants.Where(s => s.SubX < 0).Sum(s => s.StructureDef!.StackHeight);
            if (column + def.StackHeight > Constants.BlockColumnMaxHeight)
                return (false, "This block column is already at full height.");
        }

        // Blocks are ground with vertical extent: they need a flat pad (the
        // corner-stitch quad agrees) and dry land. Non-block defs keep the
        // legacy gates bit-identical — existing structures place on slopes.
        if (def.IsBlock)
        {
            if (tile.HasWater)
                return (false, "Can't build a block in water.");
            var corners = tile.CornerElevations;
            if (corners is { Length: 4 })
            {
                float spread = corners.Max() - corners.Min();
                if (spread > Constants.FlatPadMaxSpread)
                    return (false, "Needs flat ground — level this tile with a shovel.");
            }
        }

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

        // The player raised the hearth with their own hands: construction
        // trains (10 XP, level recomputed — the AddXp root fix), and a hearth
        // (light_seconds > 0) lights its fire on placement when firemaking is
        // wired. Skill/level gates read the player, so the XP is earned here.
        skillManager.AddXp("construction", 10f);
        if (firemaking != null && def.LightSeconds > 0f)
            firemaking.LightHearth(def.LightSeconds, placed.WorldX, placed.WorldY, null);

        return (true, $"Built {def.Name}.");
    }

    /// <summary>Remove a structure. When the world is supplied the tile's
    /// Structure occupancy slot is recomputed from the remaining occupants
    /// (a lone brick's removal frees its tile for pathing; a remaining
    /// sibling keeps it reserved) — without a world the slot is left
    /// untouched (legacy callers).</summary>
    public bool RemoveStructure(Structure structure, TileMap? world = null)
    {
        bool removed = Structures.Remove(structure);
        if (removed && world != null)
        {
            var tile = world.GetTile(structure.TileX, structure.TileY);
            if (tile != null)
            {
                var remaining = Structures.FirstOrDefault(s =>
                    s.TileX == structure.TileX && s.TileY == structure.TileY
                    && s.StructureDef?.OccupiesTile == true);
                tile.Structure = remaining?.StructureDef;
            }
        }
        return removed;
    }

    /// <summary>Chip a built structure (slice 11: wall-attacking monsters).
    /// Health -= amount; at zero the structure is removed with the live
    /// occupancy recompute. Returns true when destroyed.</summary>
    public bool DamageStructure(Structure structure, int amount, TileMap? world = null)
    {
        if (amount <= 0) return false;
        structure.Health = Math.Max(0, structure.Health - amount);
        if (structure.Health > 0) return false;
        return RemoveStructure(structure, world);
    }

    /// <summary>
    /// Enqueue an in-place upgrade of a built structure to its declared
    /// successor tier. Validates the successor against the def's upgrades_to
    /// chain, the player's construction skill, and mid-build state; the
    /// structure then becomes a construction job exactly like a blueprint
    /// (workers charge stockpile materials and build it up). Does not consume
    /// materials here — the worker charge path does, once the stockpile can
    /// pay (matching blueprint flow).
    /// </summary>
    public (bool Success, string Message) UpgradeStructure(Structure structure,
        string targetId, SkillManager skillManager, ColonySystem? colony)
    {
        if (!structure.IsActive)
            return (false, "Structure is inactive.");
        if (structure.IsUnderConstruction)
            return (false, "Structure is already under construction.");
        if (structure.UpgradingToId != null)
            return (false, "An upgrade is already queued.");
        var def = Registry?.GetStructure(structure.StructureId);
        if (def == null)
            return (false, "Unknown structure.");
        if (def.UpgradesTo == null)
            return (false, $"{def.Name} is already the top tier.");
        if (targetId != def.UpgradesTo)
            return (false, $"{def.Name} can't be upgraded to {targetId}.");
        var target = Registry?.GetStructure(targetId);
        if (target == null)
            return (false, "Unknown upgrade target.");
        if (skillManager.GetSkillLevel("construction") < target.RequiresSkillLevel)
            return (false, $"Requires construction level {target.RequiresSkillLevel}.");
        if (colony?.IsFounded != true)
            return (false, "Upgrades need a founded colony stockpile.");

        structure.UpgradingToId = targetId;
        structure.IsUnderConstruction = true;
        structure.ConstructionMaterialsPaid = false;
        structure.WorkProgress = 0f;
        structure.WorkStatus = $"Upgrading to {target.Name}";
        return (true, $"Upgrading to {target.Name}");
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
                SubX = s.SubX,
                SubY = s.SubY,
                Layer = s.Layer,
                Hp = s.Health,
                AssignedNpcId = s.AssignedNpcId,
                WorkRecipeId = s.WorkRecipeId,
                WorkRecipeQueue = s.WorkRecipeQueue.ToArray(),
                WorkOrdersPaused = s.WorkOrdersPaused,
                HasManualWorkOrder = s.HasManualWorkOrder,
                IsDependencyOrder = s.IsDependencyOrder,
                IsUnderConstruction = s.IsUnderConstruction,
                ConstructionMaterialsPaid = s.ConstructionMaterialsPaid,
                UpgradingToId = s.UpgradingToId,
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

            // Slice 7a migration: a pre-brick tile-level mound materializes
            // as a full 4x4 x 2-course brick block — same tile footprint,
            // same wall read (two courses = 2.5 units vs. the old 2.0).
            // Legacy sites fall through to the plain restore below (they
            // are not solid, so they never wall anybody in).
            if (structureDef.IsBlock && s.StructureId != "fence_block"
                && s.SubX < 0 && !s.IsUnderConstruction)
            {
                for (int bx = 0; bx < Constants.BrickSubCells; bx++)
                    for (int by = 0; by < Constants.BrickSubCells; by++)
                        for (int layer = 0; layer < 2; layer++)
                            Structures.Add(new Structure
                            {
                                StructureId = s.StructureId,
                                StructureDef = structureDef,
                                TileX = s.TileX,
                                TileY = s.TileY,
                                SubX = bx,
                                SubY = by,
                                Layer = layer,
                                WorldX = s.TileX * Constants.TileSize + bx * Constants.BrickCellSize + Constants.BrickCellSize / 2f,
                                WorldY = s.TileY * Constants.TileSize + by * Constants.BrickCellSize + Constants.BrickCellSize / 2f,
                                Health = s.Hp > 0 ? (int)s.Hp : (int)structureDef.Hp,
                                MaxHealth = (int)structureDef.Hp,
                                IsActive = true,
                            });
                if (structureDef.OccupiesTile)
                {
                    var mtile = world.GetTile(s.TileX, s.TileY);
                    if (mtile != null) mtile.Structure = structureDef;
                }
                continue;
            }

            Structures.Add(new Structure
            {
                StructureId = s.StructureId,
                StructureDef = structureDef,
                TileX = s.TileX,
                TileY = s.TileY,
                SubX = s.SubX,
                SubY = s.SubY,
                Layer = s.Layer,
                WorldX = s.TileX * Constants.TileSize
                    + (s.SubX < 0 ? Constants.TileSize / 2f
                                  : s.SubX * Constants.BrickCellSize + Constants.BrickCellSize / 2f),
                WorldY = s.TileY * Constants.TileSize
                    + (s.SubY < 0 ? Constants.TileSize / 2f
                                  : s.SubY * Constants.BrickCellSize + Constants.BrickCellSize / 2f),
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
                UpgradingToId = s.UpgradingToId,
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
