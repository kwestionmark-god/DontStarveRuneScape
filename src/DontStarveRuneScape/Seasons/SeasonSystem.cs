namespace DontStarveRuneScape.Seasons;

using System.Collections.Generic;
using DontStarveRuneScape.Core;

/// <summary>
/// SeasonSystem — Manages seasonal progression and effects.
/// </summary>
public sealed class SeasonSystem
{
    public string CurrentSeason { get; private set; } = "spring";
    public string PreviousSeason { get; private set; } = "winter";
    public float SeasonProgress { get; private set; } = 0f;
    public float TransitionProgress { get; private set; } = 0f;

    private readonly string[] _seasonOrder = ["spring", "summer", "autumn", "winter"];
    private float _seasonTimer = 0f;
    private bool _inTransition = false;

    public void Tick(float dt)
    {
        const float SeasonDuration = 600f; // 10 minutes per season
        const float TransitionDuration = 30f;

        if (_inTransition)
        {
            TransitionProgress += dt / TransitionDuration;
            if (TransitionProgress >= 1f)
            {
                TransitionProgress = 0f;
                _inTransition = false;
                PreviousSeason = CurrentSeason;
            }
        }
        else
        {
            _seasonTimer += dt;
            SeasonProgress = _seasonTimer / SeasonDuration;
            if (SeasonProgress >= 1f)
            {
                _seasonTimer = 0f;
                SeasonProgress = 0f;
                int currentIndex = System.Array.IndexOf(_seasonOrder, CurrentSeason);
                int nextIndex = (currentIndex + 1) % _seasonOrder.Length;
                PreviousSeason = CurrentSeason;
                CurrentSeason = _seasonOrder[nextIndex];
                _inTransition = true;
            }
        }
    }

    /// <summary>Get survival modifiers for the current season.</summary>
    public Dictionary<string, float> GetSurvivalModifiers()
    {
        return CurrentSeason switch
        {
            "winter" => new Dictionary<string, float> { ["hunger_drain"] = 1.2f },
            "summer" => new Dictionary<string, float> { ["hunger_drain"] = 1.1f },
            _ => new Dictionary<string, float> { ["hunger_drain"] = 1.0f },
        };
    }

    /// <summary>Check if a resource is available in the current season.</summary>
    public bool IsResourceAvailable(string resourceId) => true;

    /// <summary>Get resource multiplier for a category.</summary>
    public float GetResourceMultiplier(string category) => 1.0f;
}