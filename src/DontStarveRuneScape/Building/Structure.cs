namespace DontStarveRuneScape.Building;

using DontStarveRuneScape.Data;

/// <summary>
/// Structure — A placed building/structure in the world.
/// </summary>
public sealed class Structure
{
    public string StructureId { get; set; } = string.Empty;
    public StructureDef StructureDef { get; set; } = new();
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public int TileX { get; set; }
    public int TileY { get; set; }
    public bool IsActive { get; set; } = true;
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public string? AssignedNpcId { get; set; }
    /// <summary>Recipe currently queued at this workplace (if any).</summary>
    public string? WorkRecipeId { get; set; }
    /// <summary>FIFO one-shot recipes to run after the current recipe.</summary>
    public List<string> WorkRecipeQueue { get; } = [];
    /// <summary>Manual queues stop when empty; automatic mode can select repeat work.</summary>
    public bool WorkOrdersPaused { get; set; }
    public bool HasManualWorkOrder { get; set; }
    public bool IsDependencyOrder { get; set; }
    /// <summary>Target tier for an in-progress upgrade (null = none).</summary>
    public string? UpgradingToId { get; set; }

    /// <summary>Blueprint site awaiting stockpile materials and worker time.</summary>
    public bool IsUnderConstruction { get; set; }
    /// <summary>Required materials have been charged to this construction job.</summary>
    public bool ConstructionMaterialsPaid { get; set; }
    /// <summary>Seconds accumulated toward the next production cycle.</summary>
    public float WorkProgress { get; set; }
    public string WorkStatus { get; set; } = "Idle";
    public Dictionary<string, object> State { get; } = new();
}
