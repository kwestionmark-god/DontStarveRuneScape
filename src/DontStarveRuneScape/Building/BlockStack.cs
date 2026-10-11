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
    /// completed block structures). Sites under construction are not solid.</summary>
    public static float BlockHeightAt(TileMap world, BuildingSystem? buildings, int tx, int ty)
    {
        if (buildings == null) return 0f;
        float height = 0f;
        foreach (var s in buildings.Structures)
        {
            if (s.IsActive && !s.IsUnderConstruction
                && s.StructureDef?.IsBlock == true
                && s.TileX == tx && s.TileY == ty)
                height += s.StructureDef.StackHeight;
        }
        return height;
    }

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
    /// Pure-terrain ascents stay legacy-free.</summary>
    public static bool CanStep(TileMap world, BuildingSystem? buildings,
        float fromX, float fromY, float toX, float toY, bool airborne)
    {
        float ts = Config.Constants.TileSize;
        int fromTx = (int)(fromX / ts), fromTy = (int)(fromY / ts);
        int toTx = (int)(toX / ts), toTy = (int)(toY / ts);
        if (fromTx == toTx && fromTy == toTy) return true;

        var fromTile = world.GetTile(fromTx, fromTy);
        var toTile = world.GetTile(toTx, toTy);
        if (fromTile == null || toTile == null) return true; // edge of map: legacy (bounds clamp handles)

        float fromBlocks = BlockHeightAt(world, buildings, fromTx, fromTy);
        float toBlocks = BlockHeightAt(world, buildings, toTx, toTy);
        float fromTop = fromTile.Elevation + fromBlocks;
        float toTop = toTile.Elevation + toBlocks;

        if (toTop <= fromTop) return true;   // descents and flats: always free
        if (toBlocks <= fromBlocks) return true; // pure-terrain ascent: legacy freedom

        // Block ascent: a wall on foot; a hop within the cap airborne.
        return airborne && (toTop - fromTop) <= Config.Constants.BlockClimbCap;
    }

    /// <summary>Elevation a structure sprite draws at: blocks float at their
    /// top height; everything else draws at tile elevation as before.
    /// Construction sites sit on the ground until built.</summary>
    public static float RenderTop(float tileElevation, Structure structure)
        => structure.StructureDef?.IsBlock == true && !structure.IsUnderConstruction
            ? tileElevation + structure.StructureDef.StackHeight
            : tileElevation;

    /// <summary>Cumulative render top for stacked columns (slice 3): the
    /// block's draw base sits on the summed tops of the column beneath it.
    /// Column order = placement order in <see cref="BuildingSystem.Structures"/>.</summary>
    public static float RenderTop(TileMap world, BuildingSystem? buildings, Structure structure, float tileElevation)
    {
        if (structure.StructureDef?.IsBlock != true || structure.IsUnderConstruction)
            return tileElevation;
        float below = 0f;
        if (buildings != null)
        {
            foreach (var s in buildings.Structures)
            {
                if (ReferenceEquals(s, structure)) break; // column order = list order
                if (s.IsActive && !s.IsUnderConstruction
                    && s.StructureDef?.IsBlock == true
                    && s.TileX == structure.TileX && s.TileY == structure.TileY)
                    below += s.StructureDef.StackHeight;
            }
        }
        return tileElevation + below + structure.StructureDef.StackHeight;
    }

    /// <summary>4-bit fence-connection mask (N=1, E=2, S=4, W=8): a bit is set
    /// where the edge-neighbor tile holds at least one built, active block.
    /// Fences connect to any block, Minecraft-fashion; sites set no bit.</summary>
    public static int ConnectionMask(TileMap world, BuildingSystem? buildings, int tx, int ty)
    {
        if (buildings == null) return 0;
        int mask = 0;
        if (BlockHeightAt(world, buildings, tx, ty - 1) > 0f) mask |= 1; // N
        if (BlockHeightAt(world, buildings, tx + 1, ty) > 0f) mask |= 2; // E
        if (BlockHeightAt(world, buildings, tx, ty + 1) > 0f) mask |= 4; // S
        if (BlockHeightAt(world, buildings, tx - 1, ty) > 0f) mask |= 8; // W
        return mask;
    }
}
