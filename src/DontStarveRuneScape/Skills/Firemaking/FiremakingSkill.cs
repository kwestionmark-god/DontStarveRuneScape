namespace DontStarveRuneScape.Skills.Firemaking;

using System.Collections.Generic;
using DontStarveRuneScape.Inventory;

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
/// FiremakingSkill — Handles fire lighting and management.
/// </summary>
public sealed class FiremakingSkill
{
    private readonly List<FireInstance> _activeFires = [];

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

    public FireResult LightFire(List<(string ItemId, int Quantity)> fuelQueue, Inventory inventory, float x, float y)
    {
        if (fuelQueue.Count == 0)
            return new FireResult { Success = false, Message = "No fuel provided." };

        // Consume fuel
        foreach (var (itemId, qty) in fuelQueue)
        {
            inventory.RemoveItem(itemId, qty);
        }

        // Create fire (duration based on fuel)
        float duration = 30f; // Base 30 seconds
        var fire = new FireInstance
        {
            WorldX = x,
            WorldY = y,
            RemainingTime = duration,
            MaxTime = duration,
        };
        _activeFires.Add(fire);

        return new FireResult
        {
            Success = true,
            Message = "Fire lit!",
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