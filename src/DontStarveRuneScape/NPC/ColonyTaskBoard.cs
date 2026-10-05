namespace DontStarveRuneScape.NPC;

/// <summary>
/// One unit of colony work on the shared task board. Hauling follows the
/// MountainCore-inspired rule "reserve, then move": a gather task reserves
/// its source tile at claim time so two workers can never walk to the same
/// node, and a deposit task owns a worker's carried goods until they land
/// in the stockpile. Releasing a task is the single cleanup path — the
/// reservation dies with the task, so interruptions cannot leak claims.
/// </summary>
public sealed class ColonyTask
{
    public ColonyTaskKind Kind { get; init; }
    public string AssigneeNpcId { get; internal set; } = string.Empty;
    public string ItemId { get; internal set; } = string.Empty;
    public int Quantity { get; internal set; }
    /// <summary>Gather source tile. Null for deposit tasks.</summary>
    public (int X, int Y)? ReservedTile { get; internal set; }
    public string Status { get; internal set; } = "Open";
}

public enum ColonyTaskKind
{
    Gather,
    Deposit,
}

/// <summary>
/// The settlement's shared task list. Workers claim tasks through the board
/// (never through world scans alone) so reservations are synchronous and
/// global. Runtime-only state: tasks re-derive from the live world each
/// session, so snapshots do not persist them.
/// </summary>
public sealed class ColonyTaskBoard
{
    private readonly List<ColonyTask> _tasks = [];

    public IReadOnlyList<ColonyTask> Tasks => _tasks;

    public bool IsTileReserved(int x, int y)
        => _tasks.Any(task => task.ReservedTile == (x, y));

    public ColonyTask? FindGatherByTile(int x, int y)
        => _tasks.FirstOrDefault(task => task.Kind == ColonyTaskKind.Gather
            && task.ReservedTile == (x, y));

    /// <summary>Claim the gather rights for a tile. Idempotent: re-claiming
    /// a tile the worker already holds returns the existing task. Returns
    /// null when another worker holds the reservation.</summary>
    public ColonyTask? ClaimGather(string npcId, int x, int y, string itemId)
    {
        var existing = FindGatherByTile(x, y);
        if (existing != null)
            return existing.AssigneeNpcId == npcId ? existing : null;
        var task = new ColonyTask
        {
            Kind = ColonyTaskKind.Gather,
            AssigneeNpcId = npcId,
            ItemId = itemId,
            ReservedTile = (x, y),
            Status = "Claimed",
        };
        _tasks.Add(task);
        return task;
    }

    /// <summary>The worker's haul-home task for carried goods.</summary>
    public ColonyTask GetOrAddDeposit(string npcId)
    {
        var existing = _tasks.FirstOrDefault(task => task.Kind == ColonyTaskKind.Deposit
            && task.AssigneeNpcId == npcId);
        if (existing != null) return existing;
        var task = new ColonyTask
        {
            Kind = ColonyTaskKind.Deposit,
            AssigneeNpcId = npcId,
            Status = "Hauling to stockpile",
        };
        _tasks.Add(task);
        return task;
    }

    public bool Release(ColonyTask task) => _tasks.Remove(task);

    /// <summary>Release every task a worker holds (dismissal, death, or an
    /// interruption such as rest). Carried goods stay on the worker.</summary>
    public int ReleaseAllFor(string npcId)
    {
        int released = 0;
        for (int i = _tasks.Count - 1; i >= 0; i--)
        {
            if (_tasks[i].AssigneeNpcId != npcId) continue;
            _tasks.RemoveAt(i);
            released++;
        }
        return released;
    }

    public void Clear() => _tasks.Clear();
}
