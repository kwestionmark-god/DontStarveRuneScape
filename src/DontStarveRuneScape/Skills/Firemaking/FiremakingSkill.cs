namespace DontStarveRuneScape.Skills.Firemaking;

using System.Collections.Generic;
using DontStarveRuneScape.Inventory;
using DontStarveRuneScape.Core;

/// <summary>
/// FireInstance — An active fire in the world.
/// </summary>
public sealed class FireInstance
{
    public float WorldX { get; set; }
    public float WorldY { get; set; }
    public float RemainingTime { get; set; }
    public float MaxTime { get; set; }
    public int WarmthRadius { get; set; } = 3;
}

/// <summary>
/// FiremakingSkill — Handles fire lighting and management. Burn duration is
/// fuel-aware: Skills/Firemaking/data/fuel_table.json sets seconds per unit
/// (stick 15s, logs 30s, charcoal 45s...), summed over the fuel queue; an
/// unknown fuel keeps the flat 30s-per-unit legacy default.
/// </summary>
public sealed class FiremakingSkill
{
    private readonly List<FireInstance> _activeFires = [];

    /// <summary>Seconds one unit of an unlisted fuel burns (legacy flat rate).</summary>
    public const float DefaultFuelSeconds = 30f;

    private static Dictionary<string, float>? _fuelTable;

    /// <summary>Fuel id -> burn seconds per unit, from
    /// Skills/Firemaking/data/fuel_table.json; empty when the file is
    /// missing (the legacy flat rate then applies to everything).</summary>
    public static IReadOnlyDictionary<string, float> FuelTable
    {
        get
        {
            if (_fuelTable == null)
            {
                var table = new Dictionary<string, float>(StringComparer.Ordinal);
                try
                {
                    var root = DontStarveRuneScape.Data.DataLoader.LoadJson(
                        "Skills/Firemaking/data/fuel_table.json");
                    if (root.ValueKind == System.Text.Json.JsonValueKind.Object
                        && root.TryGetProperty("fuel_table", out var rows))
                    {
                        foreach (var row in rows.EnumerateObject())
                        {
                            if (row.Value.ValueKind == System.Text.Json.JsonValueKind.Object
                                && row.Value.TryGetProperty("burn_seconds", out var secs)
                                && secs.ValueKind == System.Text.Json.JsonValueKind.Number)
                                table[row.Name] = secs.GetSingle();
                        }
                    }
                }
                catch { /* missing file: fall back to the flat default */ }
                _fuelTable = table;
            }
            return _fuelTable;
        }
    }

    public IReadOnlyList<FireInstance> GetActiveFires() => _activeFires;

    public List<FireInstance> GetFiresInRadius(float x, float y, float radius)
    {
        var result = new List<FireInstance>();
        foreach (var fire in _activeFires)
        {
            float dx = fire.WorldX - x;
            float dy = fire.WorldY - y;
            if (dx * dx + dy * dy <= radius * radius)
                result.Add(fire);
        }
        return result;
    }

    /// <summary>Seconds the given fuel queue burns: the table's per-unit
    /// rate × quantity, summed; unlisted fuels use the flat legacy rate.</summary>
    public static float FuelSeconds(List<(string ItemId, int Quantity)> fuelQueue)
    {
        float total = 0f;
        foreach (var (itemId, qty) in fuelQueue)
            total += (FuelTable.TryGetValue(itemId, out var secs) ? secs : DefaultFuelSeconds) * qty;
        return total;
    }

    public FireResult LightFire(List<(string ItemId, int Quantity)> fuelQueue, Inventory inventory, float x, float y,
        SkillManager? skills = null)
    {
        if (fuelQueue.Count == 0)
            return new FireResult { Success = false, Message = "No fuel provided." };

        // Consume fuel
        foreach (var (itemId, qty) in fuelQueue)
        {
            inventory.RemoveItem(itemId, qty);
        }

        // Burn time is fuel-aware (fuel_table.json per-unit seconds ×
        // quantity, summed), and firemaking.duration: invested points
        // (raw) extend the burn +5s each; no manager = no bonus.
        float fuelSeconds = FuelSeconds(fuelQueue);
        float durationBonus = (skills?.GetSubStatPoints("firemaking", "duration") ?? 0f) * 5f;
        float duration = fuelSeconds + durationBonus;
        var fire = new FireInstance
        {
            WorldX = x,
            WorldY = y,
            RemainingTime = duration,
            MaxTime = duration,
        };
        _activeFires.Add(fire);

        // firemaking.fuel_saver: invested points (raw) = 4%/pt chance to
        // recover one unit of the first consumed fuel — the frugal
        // firemaker's rebate, paid back into the inventory.
        float saveChance = (skills?.GetSubStatPoints("firemaking", "fuel_saver") ?? 0f) * 4f;
        if (saveChance > 0f && new System.Random().NextDouble() * 100f < saveChance)
            inventory.AddItem(fuelQueue[0].ItemId, 1);

        return new FireResult
        {
            Success = true,
            Message = "Fire lit!",
            XpGained = 25f,
        };
    }

    /// <summary>Light a structure's hearth fire: burns the def's
    /// light_seconds flat (the hearth is the multiplier, fuel is the entry
    /// fee), paying full firemaking XP. No skill manager = no bonus, still
    /// lights.</summary>
    public FireResult LightHearth(float lightSeconds, float x, float y,
        SkillManager? skills = null)
    {
        if (lightSeconds <= 0f)
            return new FireResult { Success = false, Message = "Not a hearth." };

        float durationBonus = (skills?.GetSubStatPoints("firemaking", "duration") ?? 0f) * 5f;
        float duration = lightSeconds + durationBonus;
        _activeFires.Add(new FireInstance
        {
            WorldX = x,
            WorldY = y,
            RemainingTime = duration,
            MaxTime = duration,
        });
        return new FireResult
        {
            Success = true,
            Message = "Hearth lit!",
            XpGained = 25f,
        };
    }

    public void Tick(float dt)
    {
        for (int i = _activeFires.Count - 1; i >= 0; i--)
        {
            _activeFires[i].RemainingTime -= dt;
            if (_activeFires[i].RemainingTime <= 0)
                _activeFires.RemoveAt(i);
        }
    }

    /// <summary>Standing-on-a-lit-fire damage (slice 12): while a live
    /// fire sits at a footprint structure with ContactDamage > 0 and the
    /// player stands inside its footprint, bite ContactDamage HP per
    /// second (whole-HP accumulation, visible notification on the first
    /// bite — the no-silent-losses rule). Lined variants carry 0 and
    /// never bite.</summary>
    public void TickContactDamage(Core.Player? player, Building.BuildingSystem? buildings, float dt)
    {
        if (player == null || buildings == null) return;

        foreach (var s in buildings.Structures)
        {
            if (!s.IsActive || s.SubX < 0 || s.StructureDef is not { ContactDamage: > 0f })
                continue;

            // The player must be standing inside the footprint.
            int ptx = (int)(player.WorldX / Config.Constants.TileSize);
            int pty = (int)(player.WorldY / Config.Constants.TileSize);
            if (ptx != s.TileX || pty != s.TileY) continue;
            int psx = (int)(player.WorldX / Config.Constants.BrickCellSize) % Config.Constants.BrickSubCells;
            int psy = (int)(player.WorldY / Config.Constants.BrickCellSize) % Config.Constants.BrickSubCells;
            int fp = Math.Max(1, s.StructureDef.Footprint);
            if (psx < s.SubX || psx >= s.SubX + fp || psy < s.SubY || psy >= s.SubY + fp)
                continue;

            // …and a fire must be LIVE at the structure.
            bool lit = false;
            foreach (var f in _activeFires)
                if (MathF.Abs(f.WorldX - s.WorldX) < 4f && MathF.Abs(f.WorldY - s.WorldY) < 4f)
                {
                    lit = true;
                    break;
                }
            if (!lit) continue;

            _contactAccumulator += s.StructureDef.ContactDamage * dt;
            if (_contactAccumulator >= 1f)
            {
                int bite = (int)_contactAccumulator;
                _contactAccumulator -= bite;
                player.TakeDamage(bite);
                if (!_contactNotified)
                {
                    _contactNotified = true;
                    player.ActionSystem?.AddNotification(
                        "You're standing in the fire!", (255, 120, 40));
                }
            }
            return; // one fire underfoot is enough
        }
        // Off the fire: reset the bite latch.
        _contactAccumulator = 0f;
        _contactNotified = false;
    }

    private float _contactAccumulator;
    private bool _contactNotified;

    /// <summary>Get snapshot for saving.</summary>
    public FireSnapshot GetSnapshot()
    {
        var snapshot = new FireSnapshot();
        snapshot.Fires = _activeFires.Select(f => new FireDataSnapshot
        {
            WorldX = f.WorldX,
            WorldY = f.WorldY,
            RemainingTime = f.RemainingTime,
            MaxTime = f.MaxTime,
        }).ToArray();
        return snapshot;
    }

    /// <summary>Restore from snapshot.</summary>
    public void RestoreSnapshot(FireSnapshot snapshot)
    {
        _activeFires.Clear();
        if (snapshot.Fires != null)
        {
            foreach (var f in snapshot.Fires)
            {
                _activeFires.Add(new FireInstance
                {
                    WorldX = f.WorldX,
                    WorldY = f.WorldY,
                    RemainingTime = f.RemainingTime,
                    MaxTime = f.MaxTime,
                });
            }
        }
    }
}

/// <summary>
/// FireResult — Result of lighting a fire.
/// </summary>
public sealed class FireResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public float XpGained { get; set; }
}