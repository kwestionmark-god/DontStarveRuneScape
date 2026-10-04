namespace DontStarveRuneScape.NPC;

using DontStarveRuneScape.World;
using DontStarveRuneScape.Config;

/// <summary>A* routing for colony workers over dry, unoccupied world tiles.</summary>
internal static class WorkerPathfinder
{
    private static readonly (int X, int Y, int Cost)[] Neighbors =
    [
        (1, 0, 10), (0, 1, 10), (-1, 0, 10), (0, -1, 10),
        (1, 1, 14), (-1, 1, 14), (-1, -1, 14), (1, -1, 14),
    ];

    public static bool CanStand(TileMap world, int x, int y)
    {
        var tile = world.GetTile(x, y);
        return tile != null && !tile.HasWater && tile.Structure?.OccupiesTile != true;
    }

    public static List<(int X, int Y)>? FindPath(TileMap world,
        int startX, int startY, int targetX, int targetY)
    {
        if (world.GetTile(startX, startY) == null || !CanStand(world, targetX, targetY))
            return null;
        if (startX == targetX && startY == targetY) return [];

        var costs = new Dictionary<(int X, int Y), int>();
        var parents = new Dictionary<(int X, int Y), (int X, int Y)>();
        var closed = new HashSet<(int X, int Y)>();

        var open = new PriorityQueue<(int X, int Y), int>();
        costs[(startX, startY)] = 0;
        open.Enqueue((startX, startY), Heuristic(startX, startY, targetX, targetY));

        while (open.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current)) continue;
            if (current.X == targetX && current.Y == targetY)
                return Reconstruct((startX, startY), current, parents);

            foreach (var (offsetX, offsetY, moveCost) in Neighbors)
            {
                int nextX = current.X + offsetX;
                int nextY = current.Y + offsetY;
                var next = (X: nextX, Y: nextY);
                if (!CanStand(world, nextX, nextY) || closed.Contains(next)) continue;
                float elevationDelta = MathF.Abs(ElevationAt(world, current.X, current.Y)
                    - ElevationAt(world, nextX, nextY));
                if (elevationDelta > Constants.CliffThreshold) continue;
                if (offsetX != 0 && offsetY != 0
                    && (!CanStand(world, current.X + offsetX, current.Y)
                        || !CanStand(world, current.X, current.Y + offsetY)
                        || !CanStep(world, current.X, current.Y, current.X + offsetX, current.Y)
                        || !CanStep(world, current.X, current.Y, current.X, current.Y + offsetY)
                        || !CanStep(world, current.X + offsetX, current.Y, nextX, nextY)
                        || !CanStep(world, current.X, current.Y + offsetY, nextX, nextY)))
                    continue;

                int slopeCost = (int)MathF.Round(elevationDelta * 4f);
                int nextCost = costs[current] + moveCost + slopeCost;
                if (costs.TryGetValue(next, out int previousCost) && nextCost >= previousCost) continue;
                costs[next] = nextCost;
                parents[next] = current;
                open.Enqueue((nextX, nextY), nextCost + Heuristic(nextX, nextY, targetX, targetY));
            }
        }

        return null;
    }

    private static bool CanStep(TileMap world, int fromX, int fromY, int toX, int toY)
        => CanStand(world, fromX, fromY) && CanStand(world, toX, toY)
            && MathF.Abs(ElevationAt(world, fromX, fromY) - ElevationAt(world, toX, toY))
                <= Constants.CliffThreshold;

    private static float ElevationAt(TileMap world, int x, int y)
        => world.GetTile(x, y)?.GetElevationAt(0.5f, 0.5f) ?? float.MaxValue;

    private static int Heuristic(int x, int y, int targetX, int targetY)
    {
        int dx = Math.Abs(targetX - x);
        int dy = Math.Abs(targetY - y);
        int diagonal = Math.Min(dx, dy);
        return diagonal * 14 + (Math.Max(dx, dy) - diagonal) * 10;
    }

    private static List<(int X, int Y)> Reconstruct((int X, int Y) start,
        (int X, int Y) target, Dictionary<(int X, int Y), (int X, int Y)> parents)
    {
        var path = new List<(int X, int Y)>();
        var current = target;
        while (current != start)
        {
            path.Add(current);
            if (!parents.TryGetValue(current, out current)) return [];
        }
        path.Reverse();
        return path;
    }
}
