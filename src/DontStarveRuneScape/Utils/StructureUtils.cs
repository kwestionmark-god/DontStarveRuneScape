namespace DontStarveRuneScape.Utils;

using System.Collections.Generic;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Config;

/// <summary>
/// StructureUtils — Utility functions for structure-based gameplay.
/// </summary>
public static class StructureUtils
{
    private const int StationInteractionRadius = 3; // tiles

    /// <summary>
    /// Find placed structures near the player.
    /// </summary>
    /// <param name="structures">List of Structure instances from building_system.GetAllStructures().</param>
    /// <param name="playerWorldX">Player's world X position in pixels.</param>
    /// <param name="playerWorldY">Player's world Y position in pixels.</param>
    /// <param name="targetIds">Optional list of structure_ids to filter by.</param>
    /// <param name="radius">Optional radius in tiles (default: StationInteractionRadius).</param>
    /// <returns>List of matching Structure instances within range.</returns>
    public static List<Structure> FindNearbyStructures(
        List<Structure> structures,
        float playerWorldX,
        float playerWorldY,
        List<string>? targetIds = null,
        int? radius = null)
    {
        if (structures == null || structures.Count == 0)
            return new List<Structure>();

        int r = radius ?? StationInteractionRadius;
        float radiusPx = r * Constants.TileSize;
        var nearby = new List<Structure>();

        foreach (var structure in structures)
        {
            float dist = System.MathF.Sqrt(
                (structure.WorldX - playerWorldX) * (structure.WorldX - playerWorldX) +
                (structure.WorldY - playerWorldY) * (structure.WorldY - playerWorldY));

            if (dist <= radiusPx)
            {
                if (targetIds == null || targetIds.Contains(structure.StructureDef.Id))
                    nearby.Add(structure);
            }
        }

        return nearby;
    }

    /// <summary>
    /// Check if a specific structure type is near the player.
    /// </summary>
    /// <param name="structures">List of Structure instances.</param>
    /// <param name="playerWorldX">Player's world X position in pixels.</param>
    /// <param name="playerWorldY">Player's world Y position in pixels.</param>
    /// <param name="targetId">Structure_id to check proximity for.</param>
    /// <param name="radius">Optional radius in tiles.</param>
    /// <returns>True if at least one matching structure is within range.</returns>
    public static bool IsStructureNearby(
        List<Structure> structures,
        float playerWorldX,
        float playerWorldY,
        string targetId,
        int? radius = null)
    {
        return FindNearbyStructures(structures, playerWorldX, playerWorldY, [targetId], radius).Count > 0;
    }
}