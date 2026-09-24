namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Core;

/// <summary>
/// World tile map - column-major grid of Tiles.
/// </summary>
public sealed class TileMap
{
    public int Width { get; }
    public int Height { get; }
    public int SpawnX { get; set; }
    public int SpawnY { get; set; }
    public Tile[,] Tiles { get; }

    /// <summary>
    /// Vertex-averaged ground elevation grid ((Width+1) x (Height+1)): each
    /// entry is the mean of the tiles around that grid vertex. Used by the sea
    /// sheet for smooth depth tinting so per-tile facets don't quilt the ocean.
    /// </summary>
    public float[,]? SmoothedBed { get; set; }
    public SeasonSystem? SeasonSystem { get; set; }
    public BiomeRegistry? BiomeRegistry { get; set; }

    public TileMap(int width, int height)
    {
        Width = width;
        Height = height;
        Tiles = new Tile[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tiles[x, y] = new Tile(x, y);
            }
        }
    }

    /// <summary>
    /// Get tile at grid coordinates. Returns null if out of bounds.
    /// </summary>
    public Tile? GetTile(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return null;
        return Tiles[x, y];
    }

    /// <summary>
    /// Get tile at world pixel coordinates.
    /// </summary>
    public Tile? GetTileAtWorld(float worldX, float worldY)
    {
        int x = (int)(worldX / Constants.TileSize);
        int y = (int)(worldY / Constants.TileSize);
        return GetTile(x, y);
    }

    /// <summary>
    /// Update all tiles (regrowth, seasonal changes).
    /// </summary>
    public void Update(float dt)
    {
        if (SeasonSystem == null) return;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                var tile = Tiles[x, y];
                tile.ResourceNode?.UpdateRegrowth(dt, SeasonSystem);

                // Seasonal color blending would be handled by renderer
            }
        }
    }

    /// <summary>
    /// Find nearest tile of a specific biome.
    /// </summary>
    public (int X, int Y)? FindNearestBiome(int startX, int startY, string biomeId, int maxRadius = 100)
    {
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue;

                    int x = startX + dx;
                    int y = startY + dy;

                    var tile = GetTile(x, y);
                    if (tile?.Biome?.Id == biomeId)
                        return (x, y);
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Get all tiles in a radius.
    /// </summary>
    public IEnumerable<Tile> GetTilesInRadius(int centerX, int centerY, int radius)
    {
        int minX = Math.Max(0, centerX - radius);
        int maxX = Math.Min(Width - 1, centerX + radius);
        int minY = Math.Max(0, centerY - radius);
        int maxY = Math.Min(Height - 1, centerY + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                yield return Tiles[x, y];
            }
        }
    }

    /// <summary>
    /// Get resource nodes in a radius.
    /// </summary>
    public IEnumerable<(Tile Tile, ResourceNode Node)> GetResourceNodesInRadius(int centerX, int centerY, int radius)
    {
        foreach (var tile in GetTilesInRadius(centerX, centerY, radius))
        {
            if (tile.ResourceNode != null)
                yield return (tile, tile.ResourceNode);
        }
    }

    /// <summary>
    /// Get all depleted resource nodes for saving.
    /// </summary>
    public DepletedNodeSnapshot[] GetDepletedNodes()
    {
        var result = new List<DepletedNodeSnapshot>();
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                var node = Tiles[x, y].ResourceNode;
                if (node != null && node.IsDepleted)
                {
                    result.Add(new DepletedNodeSnapshot
                    {
                        TileX = x,
                        TileY = y,
                        ResourceId = node.ResourceId,
                        RegrowTime = node.RegrowTime,
                    });
                }
            }
        }
        return result.ToArray();
    }

    /// <summary>
    /// Restore depleted nodes from save data.
    /// </summary>
    public void RestoreDepletedNodes(DepletedNodeSnapshot[] depletedNodes)
    {
        if (depletedNodes == null) return;
        foreach (var d in depletedNodes)
        {
            var tile = GetTile(d.TileX, d.TileY);
            if (tile != null && tile.ResourceNode != null)
            {
                tile.ResourceNode.Density = 0f; // Mark as depleted
                tile.ResourceNode.RegrowTime = d.RegrowTime;
            }
        }
    }

    /// <summary>
    /// Mark a resource node as regrowing after depletion.
    /// </summary>
    public void MarkRegrowing(int tileX, int tileY)
    {
        var tile = GetTile(tileX, tileY);
        if (tile?.ResourceNode != null)
        {
            tile.ResourceNode.Density = 0f; // Mark as depleted
            tile.ResourceNode.RegrowTime = 0f;
        }
    }
}