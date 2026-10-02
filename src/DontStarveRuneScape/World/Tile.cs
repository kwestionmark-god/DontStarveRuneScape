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
    public float Density { get; set; } = 1.0f;        // Remaining harvest charges
    public float MaxDensity { get; set; } = 1.0f;     // Generated harvest-charge capacity
    public int GrowthStage { get; set; } = 0;         // 0 = depleted, 1 = recovering, 2 = full
    public float RegrowTime { get; set; } = 0f;       // Time until one charge returns
    public float LastHarvestTime { get; set; } = 0f;
    public int TotalHarvests { get; set; } = 0;
    public bool IsDepleted => ResourceDef?.DepletionCount < 0 ? false : Density <= 0f;

    /// <summary>Per-node size multiplier, rolled at placement (bigger = rarer).</summary>
    public float SizeScale { get; set; } = 1f;

    // Additional properties for action system compatibility
    public int XpReward => (int)(ResourceDef?.Xp ?? 0);
    public string YieldItem => ResourceDef?.YieldItem ?? string.Empty;
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

        // Density stores remaining successful harvests. A negative depletion
        // count means inexhaustible; output quantity stays independent of reserve.
        int baseYield = def.Yield;
        int quantity = Math.Max(1, (int)(baseYield * toolEfficiency));

        if (def.DepletionCount >= 0)
            Density = Math.Max(0f, Density - 1f);

        if (Density < MaxDensity && def.Regrow > 0)
            RegrowTime = def.Regrow;

        if (Density <= 0)
            GrowthStage = 0;
        else if (Density < MaxDensity)
        {
            GrowthStage = 1; // Regrowing
        }
        else
        {
            GrowthStage = 2; // Mature
        }

        LastHarvestTime = 0; // Will be set by world time
        TotalHarvests++;

        return (def.YieldItem, quantity, (int)def.Xp);
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

        if (RegrowTime > 0f)
        {
            RegrowTime -= dt * seasonMult;
            if (RegrowTime > 0f) return;
        }

        Density = Math.Min(MaxDensity, Density + 1f);
        if (Density >= MaxDensity)
        {
            GrowthStage = 2;
            RegrowTime = 0f;
        }
        else
        {
            GrowthStage = Density <= 0f ? 0 : 1;
            RegrowTime = def.Regrow;
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
    public bool IsCaveEntrance { get; set; }
    public bool IsCaveExit { get; set; }
    public bool HasStructure => Structure != null;
    public float Temperature { get; set; } = 20f;     // Celsius
    public float Fertility { get; set; } = 1.0f;      // 0-1, affects regrowth
    public float[]? CornerElevations { get; set; }    // 4 corners for 2.5D rendering

    /// <summary>
    /// Water is not a tile classification — it is a physical layer. Any tile
    /// whose ground sits at or below the constant sea level is underwater;
    /// the water surface is always exactly Constants.SeaLevel.
    /// </summary>
    public bool HasWater => Elevation <= Constants.SeaLevel;

    /// <summary>
    /// Water tiles: distance in tiles to the nearest land, from a multi-source
    /// BFS at worldgen. Drives the shallow→deep surface gradient so the blend
    /// spans several tiles even over steep lakebeds.
    /// </summary>
    public float ShoreDistance { get; set; } = 0f;

    /// <summary>
    /// Land tiles only: ring distance in tiles to the nearest water body
    /// (1 = touching water, 0 = not near any), with the surface elevation of
    /// that body. Computed once at worldgen so shading stays O(1) per tile.
    /// </summary>
    public int LandDistToWater { get; set; }
    public float ShoreSurface { get; set; } = float.NaN;

    /// <summary>Water surface level of this tile, or NaN when dry.</summary>
    public float GetSurfaceElevation() => HasWater ? Constants.SeaLevel : float.NaN;

    /// <summary>Water depth over this tile's ground (0 when dry).</summary>
    public float WaterDepth => HasWater ? Constants.SeaLevel - Elevation : 0f;

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
