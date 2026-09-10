namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Building;

/// <summary>
/// SpriteRenderer — Handles rendering of all sprites (resources, player, monsters, NPCs, structures, fires).
/// </summary>
public sealed class SpriteRenderer
{
    public void RenderResource(ResourceNode resource, Camera camera, int elevation, int tileX, int tileY) { }
    public void RenderPlayer(Player player, Camera camera, float elevation, float dt) { }
    public void RenderMonster(Monster monster, Camera camera) { }
    public void RenderNPC(NPC.NPC npc, Camera camera, int elevation) { }
    public void RenderProximityPrompt(NPC.NPC npc, Camera camera) { }
    public void RenderStructure(Structure structure, Camera camera) { }
    public void RenderFire(Firemaking.FireInstance fire, Camera camera) { }
}