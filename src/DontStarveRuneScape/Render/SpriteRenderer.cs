namespace DontStarveRuneScape.Render;

using DontStarveRuneScape.Camera;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Skills.Firemaking;

/// <summary>
/// SpriteRenderer — Handles rendering of all sprites (resources, player, monsters, NPCs, structures, fires).
/// </summary>
public sealed class SpriteRenderer
{
    public void RenderResource(ResourceNode resource, PrimitiveBatch batch, Camera camera, int elevation, int tileX, int tileY)
    {
        (byte r, byte g, byte b) color = resource.IsDepleted
            ? ((byte)120, (byte)120, (byte)120)
            : resource.GrowthStage == 1
                ? ((byte)150, (byte)220, (byte)120)
                : ((byte)90, (byte)160, (byte)70);
        DrawSprite(batch, camera, tileX, tileY, elevation, 22f, color.r, color.g, color.b);
    }

    public void RenderPlayer(Player player, PrimitiveBatch batch, Camera camera, float elevation, float dt)
    {
        DrawSprite(batch, camera, player.WorldX, player.WorldY, elevation, 30f, 60, 120, 220);
    }

    public void RenderMonster(Monster monster, PrimitiveBatch batch, Camera camera)
    {
        DrawSprite(batch, camera, monster.WorldX, monster.WorldY, 0f, 32f, 200, 60, 60);
    }

    public void RenderNPC(Npc npc, PrimitiveBatch batch, Camera camera, int elevation)
    {
        DrawSprite(batch, camera, npc.WorldX, npc.WorldY, elevation, 30f, 70, 180, 100);
    }

    public void RenderProximityPrompt(Npc npc, PrimitiveBatch batch, Camera camera)
    {
        DrawSprite(batch, camera, npc.WorldX, npc.WorldY, 0f, 8f, 255, 255, 120);
    }

    public void RenderStructure(Structure structure, PrimitiveBatch batch, Camera camera)
    {
        DrawSprite(batch, camera, structure.WorldX, structure.WorldY, 0f, 32f, 150, 150, 160);
    }

    public void RenderFire(FireInstance fire, PrimitiveBatch batch, Camera camera)
    {
        DrawSprite(batch, camera, fire.WorldX, fire.WorldY, 0f, 16f, 230, 140, 40);
    }

    private static void DrawSprite(PrimitiveBatch batch, Camera camera, float worldX, float worldY, float elevation, float halfSize, byte r, byte g, byte b)
    {
        var screen = camera.WorldToScreen(worldX, worldY, elevation);
        float half = halfSize * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, r, g, b);
    }

    private static void DrawSprite(PrimitiveBatch batch, Camera camera, int tileX, int tileY, int elevation, float halfSize, byte r, byte g, byte b)
    {
        float worldX = tileX * DontStarveRuneScape.Config.Constants.TileSize + DontStarveRuneScape.Config.Constants.TileSize * 0.5f;
        float worldY = tileY * DontStarveRuneScape.Config.Constants.TileSize + DontStarveRuneScape.Config.Constants.TileSize * 0.5f;
        DrawSprite(batch, camera, worldX, worldY, elevation, halfSize, r, g, b);
    }
}