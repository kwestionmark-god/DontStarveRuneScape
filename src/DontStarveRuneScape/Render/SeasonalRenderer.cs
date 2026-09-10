namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Seasons;

/// <summary>
/// SeasonalRenderer — Handles seasonal visual effects and ambient overlays.
/// </summary>
public sealed class SeasonalRenderer
{
    public void Sync(float seasonProgress, string currentSeason, string? previousSeason) { }
    public void DrawAmbientOverlay(GL gl, int alpha) { }
}