namespace DontStarveRuneScape.Render;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;

/// <summary>
/// TileRenderer — Renders the terrain tiles as shaded, biome-colored quads.
/// </summary>
public sealed class TileRenderer
{
    public void Render(PrimitiveBatch batch, Camera camera, TileMap world)
    {
        var (left, top, right, bottom) = camera.GetViewRect();

        int xMin = Math.Max(0, (int)(left / Constants.TileSize));
        int xMax = Math.Min(world.Width, (int)(right / Constants.TileSize) + 1);
        int yMin = Math.Max(0, (int)(top / Constants.TileSize));
        int yMax = Math.Min(world.Height, (int)(bottom / Constants.TileSize) + 1);

        float halfWidth = Constants.TileSize * 0.5f * camera.Zoom;
        float halfHeight = halfWidth * MathF.Cos(camera.Pitch);

        // Draw top (far) rows first so nearer rows paint over them.
        for (int y = yMin; y < yMax; y++)
        {
            for (int x = xMin; x < xMax; x++)
            {
                var tile = world.Tiles[x, y];
                if (tile == null) continue;

                float worldX = x * Constants.TileSize + Constants.TileSize * 0.5f;
                float worldY = y * Constants.TileSize + Constants.TileSize * 0.5f;
                var screen = camera.WorldToScreen(worldX, worldY, tile.Elevation);

                int elevation = (int)MathF.Round(tile.Elevation);
                var color = tile.Biome?.GetTerrainColor(elevation) ?? (128, 128, 128);
                if (color == default) color = (128, 128, 128);

                // Slope-shaded with a light source toward the north-east.
                float slope = tile.GetSlopeShading(0.6f, 0.35f);
                float shade = Math.Clamp(0.75f + slope * 0.30f - elevation / (float)Constants.ElevationLevels * 0.25f, 0.35f, 1.15f);

                byte r = (byte)Math.Clamp((int)(color.R * shade), 0, 255);
                byte g = (byte)Math.Clamp((int)(color.G * shade), 0, 255);
                byte b = (byte)Math.Clamp((int)(color.B * shade), 0, 255);

                batch.DrawScreenQuad(screen.X, screen.Y, halfWidth, halfHeight, r, g, b);
            }
        }
    }
}