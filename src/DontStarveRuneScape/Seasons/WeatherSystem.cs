namespace DontStarveRuneScape.Seasons;

using System.Collections.Generic;

/// <summary>
/// WeatherSystem — Manages weather effects and gameplay modifiers.
/// </summary>
public sealed class WeatherSystem
{
    public string CurrentWeather { get; private set; } = "clear";
    private float _weatherTimer = 0f;
    private const float WeatherChangeInterval = 120f; // 2 minutes

    public void Tick(float dt)
    {
        _weatherTimer += dt;
        if (_weatherTimer >= WeatherChangeInterval)
        {
            _weatherTimer = 0f;
            // Randomly change weather
            var weathers = new[] { "clear", "rain", "storm", "fog", "snow" };
            var random = new System.Random();
            CurrentWeather = weathers[random.Next(weathers.Length)];
        }
    }

    /// <summary>Get current weather gameplay effects.</summary>
    public Dictionary<string, float> GetEffects()
    {
        return CurrentWeather switch
        {
            "rain" => new Dictionary<string, float>
            {
                ["visibility"] = 0.8f,
                ["movement_speed"] = 0.9f,
                ["outdoor_crafting"] = 0.7f,
                ["spawn_mod"] = 1.2f,
            },
            "storm" => new Dictionary<string, float>
            {
                ["visibility"] = 0.6f,
                ["movement_speed"] = 0.7f,
                ["outdoor_crafting"] = 0.5f,
                ["spawn_mod"] = 1.5f,
            },
            "fog" => new Dictionary<string, float>
            {
                ["visibility"] = 0.5f,
                ["movement_speed"] = 1.0f,
                ["outdoor_crafting"] = 1.0f,
                ["spawn_mod"] = 1.0f,
            },
            "snow" => new Dictionary<string, float>
            {
                ["visibility"] = 0.7f,
                ["movement_speed"] = 0.8f,
                ["outdoor_crafting"] = 0.8f,
                ["spawn_mod"] = 1.1f,
            },
            _ => new Dictionary<string, float>
            {
                ["visibility"] = 1.0f,
                ["movement_speed"] = 1.0f,
                ["outdoor_crafting"] = 1.0f,
                ["spawn_mod"] = 1.0f,
            },
        };
    }
}