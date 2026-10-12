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
    /// <summary>Resolve a world-px coordinate to its 16px brick sub-cell
    /// index (0-3) within the tile.</summary>
    public static int SubCellOf(float worldPx)
        => (int)(worldPx / Config.Constants.BrickCellSize) % Config.Constants.BrickSubCells;

    /// <summary>Standing lift of one 16px sub-cell (slice 7a): the brick
    /// column top at that sub-cell — (Layer+1) courses — floored by any
    /// tile-level block lift (a fence applies tile-wide). Sites under
    /// construction are not solid.</summary>
    public static float SubHeightAt(TileMap world, BuildingSystem? buildings,
        int tx, int ty, int subX, int subY)
    {
        if (buildings == null) return 0f;
        float height = 0f;
        foreach (var s in buildings.Structures)
        {
            if (!s.IsActive || s.IsUnderConstruction
                || s.StructureDef?.IsBlock != true
                || s.TileX != tx || s.TileY != ty)
                continue;
            if (s.SubX >= 0)
            {
                if (s.SubX == subX && s.SubY == subY)
                    height = MathF.Max(height, (s.Layer + 1) * s.StructureDef.StackHeight);
            }
            else
            {
                // Tile-level block (fence): applies to the whole tile.
                height = MathF.Max(height, s.StructureDef.StackHeight);
            }
        }
        return height;
    }

    /// <summary>Slice-8 occluder query: the brick column top at a sub-cell —
    /// the tile's elevation plus its sub-cell brick height — or null when
    /// the sub-cell holds neither bricks nor a tile-level block. Bare
    /// terrain elevation NEVER counts as an occluder (user decision
    /// 2026-10-11: bricks keep their faces beside slopes).</summary>
    internal static float? BrickColumnTopAt(TileMap world, BuildingSystem? buildings,
        int tx, int ty, int subX, int subY)
    {
        var tile = world.GetTile(tx, ty);
        if (tile == null) return null;
        float h = SubHeightAt(world, buildings, tx, ty, subX, subY);
        return h > 0f ? tile.Elevation + h : null;
    }

    /// <summary>Total block lift on a tile: the MAX over its sub-cell columns
    /// and tile-level blocks — never a sum (side-by-side bricks do not stack
    /// lift). Fence connections and tile-level readers use this.</summary>
    public static float BlockHeightAt(TileMap world, BuildingSystem? buildings, int tx, int ty)
    {
        if (buildings == null) return 0f;
        float height = 0f;
        foreach (var s in buildings.Structures)
        {
            if (s.IsActive && !s.IsUnderConstruction
                && s.StructureDef?.IsBlock == true
                && s.TileX == tx && s.TileY == ty)
            {
                float lift = s.SubX >= 0
                    ? (s.Layer + 1) * s.StructureDef.StackHeight
                    : s.StructureDef.StackHeight;
                height = MathF.Max(height, lift);
            }
        }
        return height;
    }

    /// <summary>Where an entity at (worldX, worldY) effectively stands:
    /// terrain center elevation + the lift of the 16px sub-cell under it.</summary>
    public static float StandingElevationAt(TileMap world, BuildingSystem? buildings, float worldX, float worldY)
    {
        var tile = world.GetTileAtWorld(worldX, worldY);
        if (tile == null) return 0f;
        return tile.Elevation + SubHeightAt(world, buildings,
            (int)(worldX / Config.Constants.TileSize), (int)(worldY / Config.Constants.TileSize),
            SubCellOf(worldX), SubCellOf(worldY));
    }

    /// <summary>Movement rule: descents/flats always allowed; a block ascent
    /// (a swept sub-cell holds more lift than the source) is a wall unless
    /// the mover is airborne and the top delta fits the climb cap.
    /// Pure-terrain ascents stay legacy-free. The swept span is sampled at
    /// 8px steps — a 37.5px tick crosses several 16px sub-cells, and a thin
    /// brick column must not be stepped over between ticks.</summary>
    public static bool CanStep(TileMap world, BuildingSystem? buildings,
        float fromX, float fromY, float toX, float toY, bool airborne)
    {
        float ts = Config.Constants.TileSize;
        int fromTx = (int)(fromX / ts), fromTy = (int)(fromY / ts);
        int toTx = (int)(toX / ts), toTy = (int)(toY / ts);
        if (fromTx == toTx && fromTy == toTy
            && SubCellOf(fromX) == SubCellOf(toX) && SubCellOf(fromY) == SubCellOf(toY))
            return true;

        var fromTile = world.GetTile(fromTx, fromTy);
        if (fromTile == null) return true; // edge of map: legacy (bounds clamp handles)

        float fromLift = SubHeightAt(world, buildings, fromTx, fromTy,
            SubCellOf(fromX), SubCellOf(fromY));
        float fromTop = fromTile.Elevation + fromLift;

        float dx = toX - fromX, dy = toY - fromY;
        float span = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
        int steps = (int)(span / 8f) + 1;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float sx = fromX + dx * t, sy = fromY + dy * t;
            int stx = (int)(sx / ts), sty = (int)(sy / ts);
            var sTile = world.GetTile(stx, sty);
            if (sTile == null) continue; // edge of map: legacy

            float sLift = SubHeightAt(world, buildings, stx, sty, SubCellOf(sx), SubCellOf(sy));
            float sTop = sTile.Elevation + sLift;
            if (sTop <= fromTop) continue;          // descents and flats: always free
            if (sLift <= fromLift) continue;        // pure-terrain ascent: legacy freedom

            // Block ascent: a wall on foot; a hop within the cap airborne.
            if (airborne && sTop - fromTop <= Config.Constants.BlockClimbCap) continue;
            return false;
        }
        return true;
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

    /// <summary>Cumulative render base (slice 7a): a brick draws at its
    /// stored course — tile elevation + Layer x stack_height — which makes
    /// the draw order-independent. Tile-level blocks (fences) and
    /// everything else plant on the ground.</summary>
    public static float RenderBase(TileMap world, BuildingSystem? buildings, Structure structure, float tileElevation)
    {
        if (structure.StructureDef?.IsBlock != true || structure.IsUnderConstruction)
            return tileElevation;
        return structure.SubX >= 0
            ? tileElevation + structure.Layer * structure.StructureDef.StackHeight
            : tileElevation;
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
