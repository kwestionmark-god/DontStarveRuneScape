namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Data;
using DontStarveRuneScape.Seasons;
using DontStarveRuneScape.Config;

/// <summary>
/// Resource node on a tile (harvestable resource).
/// </summary>
public sealed class ResourceNode
{
    public string ResourceId { get; set; } = string.Empty;
    public ResourceDef? ResourceDef { get; set; }
    public float Density { get; set; } = 1.0f;        // Current density (0-1+)
    public float MaxDensity { get; set; } = 1.0f;     // Maximum density for this node
    public int GrowthStage { get; set; } = 0;         // 0 = depleted, 1 = sapling/young, 2 = mature
    public float RegrowTime { get; set; } = 0f;       // Time until next growth stage
    public float LastHarvestTime { get; set; } = 0f;
    public int TotalHarvests { get; set; } = 0;
    public bool IsDepleted => Density <= 0f;

    /// <summary>Per-node size multiplier, rolled at placement (bigger = rarer).</summary>
    public float SizeScale { get; set; } = 1f;

    // Additional properties for action system compatibility
    public int XpReward => (int)(ResourceDef?.Xp ?? 0);
    public string YieldItem => ResourceDef?.Id ?? string.Empty;
    public int YieldQuantity => ResourceDef?.Yield ?? 1;
    public bool RequiresTool => ResourceDef?.RequiresTool ?? false;

    public ResourceNode() { }

    public ResourceNode(string resourceId, ResourceDef def, float density)
    {
        ResourceId = resourceId;
        ResourceDef = def;
        Density = density;
        MaxDensity = density;
    }

    /// <summary>
    /// Harvest from this node. Returns (itemId, quantity, xp).
    /// </summary>
    public (string ItemId, int Quantity, int Xp) Harvest(float toolEfficiency = 1.0f, SeasonSystem? seasonSystem = null)
    {
        if (IsDepleted) return (string.Empty, 0, 0);

        var def = ResourceDef;
        if (def == null) return (string.Empty, 0, 0);

        // Check seasonal availability
        if (seasonSystem != null && def.Seasons.Length > 0)
        {
            string currentSeason = seasonSystem.CurrentSeason;
            if (!def.Seasons.Contains(currentSeason))
                return (string.Empty, 0, 0);
        }

        // Calculate yield based on density and tool efficiency
        int baseYield = def.Yield;
        int quantity = Math.Max(1, (int)(baseYield * Density * toolEfficiency));
        quantity = Math.Min(quantity, (int)MaxDensity);

        // Deplete
        Density -= quantity / (float)def.Yield;
        if (Density < 0) Density = 0;

        // Set regrow time
        if (Density <= 0 && def.Regrow > 0)
        {
            RegrowTime = def.Regrow;
            GrowthStage = 0; // Depleted
        }
        else if (Density > 0 && Density < MaxDensity)
        {
            GrowthStage = 1; // Regrowing
        }
        else
        {
            GrowthStage = 2; // Mature
        }

        LastHarvestTime = 0; // Will be set by world time
        TotalHarvests++;

        return (def.Id, quantity, (int)def.Xp);
    }

    /// <summary>
    /// Update regrowth over time.
    /// </summary>
    public void UpdateRegrowth(float dt, SeasonSystem? seasonSystem = null)
    {
        if (Density >= MaxDensity) return;

        var def = ResourceDef;
        if (def == null || def.Regrow <= 0) return;

        // Seasonal regrowth modifier
        float seasonMult = 1.0f;
        if (seasonSystem != null && def.Seasons.Length > 0)
        {
            string currentSeason = seasonSystem.CurrentSeason;
            if (!def.Seasons.Contains(currentSeason))
            {
                seasonMult = 0.1f; // Very slow regrowth out of season
            }
        }

        RegrowTime -= dt * seasonMult;
        if (RegrowTime <= 0)
        {
            // Advance growth stage
            float regrowAmount = def.Regrow > 0 ? (MaxDensity / (def.Regrow / 60f)) : 0.01f; // Per second
            Density = Math.Min(MaxDensity, Density + regrowAmount * dt * seasonMult);

            if (Density <= 0)
            {
                GrowthStage = 0;
                RegrowTime = def.Regrow;
            }
            else if (Density < MaxDensity)
            {
                GrowthStage = 1;
            }
            else
            {
                GrowthStage = 2;
                RegrowTime = 0;
            }
        }
    }

    /// <summary>
    /// Get the appropriate sprite key for current state.
    /// </summary>
    public string GetSpriteKey()
    {
        var def = ResourceDef;
        if (def == null) return string.Empty;

        string baseKey = def.SpriteKey;

        if (IsDepleted)
        {
            // Try depleted variant
            return baseKey + "_depleted";
        }
        else if (GrowthStage == 1)
        {
            // Try sapling/young variant
            return baseKey + "_sapling";
        }
        else if (GrowthStage == 0 && Density < MaxDensity * 0.5f)
        {
            // Young stage
            return baseKey + "_young";
        }

        return baseKey;
    }
}

/// <summary>
/// Tile in the world grid.
/// </summary>
public sealed class Tile
{
    public int X { get; set; }
    public int Y { get; set; }
    public float Elevation { get; set; } = 0f;        // 0-31 (normalized 0-1 for rendering)
    public float Moisture { get; set; } = 0.5f;       // 0-1
    public BiomeDef? Biome { get; set; }
    public ResourceNode? ResourceNode { get; set; }
    public StructureDef? Structure { get; set; }      // Structure placed on this tile
    public bool HasStructure => Structure != null;
    public float Temperature { get; set; } = 20f;     // Celsius
    public float Fertility { get; set; } = 1.0f;      // 0-1, affects regrowth
    public int[]? CornerElevations { get; set; }      // 4 corners for 2.5D rendering

    /// <summary>
    /// Water surface level for water tiles: NaN = Constants.SeaLevel (the sea).
    /// Pooled depressions above sea level store their own fill level here.
    /// </summary>
    public float WaterLevel { get; set; } = float.NaN;

    /// <summary>
    /// Water tiles: distance in tiles to the nearest land, from a multi-source
    /// BFS at worldgen. Drives the shallow→deep surface gradient so the blend
    /// spans several tiles even over steep lakebeds.
    /// </summary>
    public float ShoreDistance { get; set; } = 0f;

    /// <summary>Effective water surface level of this tile (sea or pool).</summary>
    public float GetSurfaceElevation()
        => Biome?.Id != "water" ? float.NaN
           : float.IsNaN(WaterLevel) ? Constants.SeaLevel : WaterLevel;

    public Tile(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Get interpolated elevation at a sub-tile position (0-1, 0-1).
    /// </summary>
    public float GetElevationAt(float u, float v)
    {
        if (CornerElevations == null || CornerElevations.Length != 4)
            return Elevation;

        // Bilinear interpolation
        float e00 = CornerElevations[0];
        float e10 = CornerElevations[1];
        float e11 = CornerElevations[2];
        float e01 = CornerElevations[3];

        float e0 = e00 * (1 - u) + e10 * u;
        float e1 = e01 * (1 - u) + e11 * u;
        return e0 * (1 - v) + e1 * v;
    }

    /// <summary>
    /// Get slope shading factor (-1 to 1) for lighting.
    /// </summary>
    public float GetSlopeShading(float lightDirX, float lightDirY)
    {
        if (CornerElevations == null || CornerElevations.Length != 4)
            return 0f;

        // Calculate normal from corner elevations
        float dx = (CornerElevations[1] - CornerElevations[0] + CornerElevations[2] - CornerElevations[3]) * 0.5f;
        float dy = (CornerElevations[3] - CornerElevations[0] + CornerElevations[2] - CornerElevations[1]) * 0.5f;

        float len = MathF.Sqrt(dx * dx + dy * dy + 1);
        float nx = -dx / len;
        float ny = -dy / len;
        float nz = 1.0f / len;

        return nx * lightDirX + ny * lightDirY + nz * 0.5f;
    }
}