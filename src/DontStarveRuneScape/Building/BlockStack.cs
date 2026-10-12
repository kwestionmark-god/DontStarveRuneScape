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

    /// <summary>Standing lift of one 16px sub-cell (slice 7a): the brick
    /// column top at that sub-cell, floored by any tile-level block lift
    /// (a fence applies tile-wide). RED stub: delegates to the per-tile
    /// query until the granulation lands.</summary>
    public static float SubHeightAt(TileMap world, BuildingSystem? buildings,
        int tx, int ty, int subX, int subY)
        => BlockHeightAt(world, buildings, tx, ty);

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
        => structure.StructureDef?.IsBlock == true && !structure.IsUnderConstruction
            ? RenderBase(world, buildings, structure, tileElevation) + structure.StructureDef.StackHeight
            : tileElevation;

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

    /// <summary>Cumulative column BASE for a stacked block (slice 4): tile
    /// elevation + summed stack heights of the built, active blocks placed
    /// BEFORE this one on the tile (column order = placement order).
    /// Non-blocks and sites sit at ground height.</summary>
    public static float RenderBase(TileMap world, BuildingSystem? buildings, Structure structure, float tileElevation)
    {
        if (structure.StructureDef?.IsBlock != true || structure.IsUnderConstruction || buildings == null)
            return tileElevation;
        float below = 0f;
        foreach (var s in buildings.Structures)
        {
            if (ReferenceEquals(s, structure)) break; // column order = list order
            if (s.IsActive && !s.IsUnderConstruction
                && s.StructureDef?.IsBlock == true
                && s.TileX == structure.TileX && s.TileY == structure.TileY)
                below += s.StructureDef.StackHeight;
        }
        return tileElevation + below;
    }

    /// <summary>Axis facing for a block from its connection mask: E|W → the
    /// X axis (1,0); N|S → (0,1); both-or-neither → null (symmetric cross).</summary>
    public static (float X, float Y)? FenceFacingDir(int connectionMask)
    {
        bool ew = (connectionMask & (2 | 8)) != 0;
        bool ns = (connectionMask & (1 | 4)) != 0;
        if (ew && !ns) return (1f, 0f);
        if (ns && !ew) return (0f, 1f);
        return null;
    }

    /// <summary>Rail segments for a fence: one center→edge-midpoint segment
    /// per connected side (world ground coordinates; the renderer projects
    /// them at rail height). N=1/E=2/S=4/W=8.</summary>
    public static IReadOnlyList<(float X0, float Y0, float X1, float Y1)> FenceRails(int connectionMask, int tx, int ty)
    {
        if (connectionMask == 0) return Array.Empty<(float, float, float, float)>();
        float ts = Config.Constants.TileSize;
        float cx = (tx + 0.5f) * ts, cy = (ty + 0.5f) * ts;
        var rails = new List<(float, float, float, float)>(4);
        if ((connectionMask & 1) != 0) rails.Add((cx, cy, cx, ty * ts));        // N
        if ((connectionMask & 2) != 0) rails.Add((cx, cy, (tx + 1) * ts, cy));  // E
        if ((connectionMask & 4) != 0) rails.Add((cx, cy, cx, (ty + 1) * ts));  // S
        if ((connectionMask & 8) != 0) rails.Add((cx, cy, tx * ts, cy));        // W
        return rails;
    }
}
