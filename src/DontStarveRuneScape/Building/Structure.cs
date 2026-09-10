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
    public Dictionary<string, object> State { get; } = new();
}