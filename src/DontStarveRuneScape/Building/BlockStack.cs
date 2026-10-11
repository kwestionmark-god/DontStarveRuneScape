namespace DontStarveRuneScape.Building;

using DontStarveRuneScape.World;

/// <summary>
/// BlockStack — vertical-building slice 2: standing-height and stepping math
/// for block structures (the only structure type with vertical extent).
/// Pure statics so rules are testable without a Game.
///
/// Standing elevation = the tile's center elevation PLUS the summed
/// stack_height of built, active block structures on that tile. A block top
/// is floor; a block side is a wall.
/// </summary>
public static class BlockStack
{
    /// <summary>Total block lift on a tile (summed stack heights of active,
    /// completed block structures). Sites under construction are not solid.
    /// RED stub: returns 0 until GREEN wires the Structures scan.</summary>
    public static float BlockHeightAt(TileMap world, BuildingSystem? buildings, int tx, int ty)
        => 0f;

    /// <summary>Where an entity at (worldX, worldY) effectively stands:
    /// terrain center elevation + the tile's block lift.</summary>
    public static float StandingElevationAt(TileMap world, BuildingSystem? buildings, float worldX, float worldY)
    {
        var tile = world.GetTileAtWorld(worldX, worldY);
        if (tile == null) return 0f;
        return tile.Elevation + BlockHeightAt(world, buildings, (int)(worldX / Config.Constants.TileSize), (int)(worldY / Config.Constants.TileSize));
    }

    /// <summary>Movement rule: descents/flats always allowed; a block ascent
    /// (destination holds more stacked height than the source) is a wall
    /// unless the mover is airborne and the top delta fits the climb cap.
    /// Pure-terrain ascents stay legacy-free. RED stub: always true.</summary>
    public static bool CanStep(TileMap world, BuildingSystem? buildings,
        float fromX, float fromY, float toX, float toY, bool airborne)
        => true;

    /// <summary>Elevation a structure sprite draws at: blocks float at their
    /// top height; everything else draws at tile elevation as before.
    /// RED stub: tile elevation only.</summary>
    public static float RenderTop(float tileElevation, Structure structure)
        => tileElevation;
}
