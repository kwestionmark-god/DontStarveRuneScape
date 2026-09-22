namespace DontStarveRuneScape.Render;

using System.Collections.Generic;
using DontStarveRuneScape.Camera;
using DontStarveRuneScape.Config;
using DontStarveRuneScape.World;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Combat;
using DontStarveRuneScape.NPC;
using DontStarveRuneScape.Building;
using DontStarveRuneScape.Skills.Firemaking;
using SkiaSharp;
using Silk.NET.OpenGL;

/// <summary>
/// SpriteRenderer — Loads sprite textures from Assets/sprites/ and renders
/// resources, player, monsters, NPCs, structures, and fires on the world.
/// Each unique sprite key is loaded once and cached; repeated draws reuse the
/// uploaded OpenGL texture.
/// </summary>
public sealed class SpriteRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly string _spritesDir;

    // textureId -> (width, height) in pixels
    private readonly Dictionary<string, (uint Texture, int W, int H)> _cache = new();

    private bool _disposed;

    public SpriteRenderer(GL gl)
    {
        _gl = gl;
        string baseDir = AppContext.BaseDirectory;
        _spritesDir = System.IO.Path.Combine(baseDir, "Assets", "sprites");
        if (!System.IO.Directory.Exists(_spritesDir))
            _spritesDir = baseDir;
    }

    // ─── Resource rendering ───────────────────────────────────────────────

    public void RenderResource(ResourceNode resource, PrimitiveBatch batch, Camera camera, float elevation, int tileX, int tileY)
    {
        string spriteKey = resource.GetSpriteKey();
        uint tex = GetSpriteTexture(spriteKey);
        float half = 24f * camera.Zoom;

        // Ground point at tile center — bottom-anchored billboard (same scheme
        // as the player sprite) so the resource keeps its tile placement under
        // any camera yaw/pitch instead of sliding around the tile center.
        var screen = camera.WorldToScreen(
            tileX * Constants.TileSize + Constants.TileSize * 0.5f,
            tileY * Constants.TileSize + Constants.TileSize * 0.5f,
            elevation);
        float cx = screen.X;
        float cy = screen.Y - half;

        if (tex != 0)
        {
            // Straight-alpha white texture; tint via the batch shader.
            batch.DrawTexturedScreenQuad(cx, cy, half, half, tex, 255, 255, 255);
        }
        else
        {
            // Fallback colored quad.
            (byte r, byte g, byte b) color = resource.IsDepleted
                ? ((byte)120, (byte)120, (byte)120)
                : resource.GrowthStage == 1
                    ? ((byte)150, (byte)220, (byte)120)
                    : ((byte)90, (byte)160, (byte)70);
            batch.DrawScreenQuad(cx, cy, half, half, color.r, color.g, color.b);
        }
    }

    // ─── Player / monster / NPC / structure / fire ────────────────────────

    private float _playerAnimTime;

    public void RenderPlayer(Player player, PrimitiveBatch batch, Camera camera, float elevation, float dt)
    {
        _playerAnimTime += dt;
        bool moving = player.Moving;
        // Walk cycles run a bit faster than the idle bob.
        float frameDuration = moving ? 0.12f : 0.3f;
        int frame = (int)(_playerAnimTime / frameDuration) % 4;
        string key = (moving ? "player/walk_" : "player/idle_") + frame;

        uint tex = GetSpriteTexture(key);
        // Ground point: the world position is the player's feet.
        var screen = camera.WorldToScreen(player.WorldX, player.WorldY, elevation);
        const float HalfWidth = 22f;
        float half = HalfWidth * camera.Zoom;
        // Anchor bottom-center: feet at the ground point at any zoom/pitch.
        float cx = screen.X;
        float cy = screen.Y - half;

        if (tex != 0)
        {
            batch.DrawTexturedScreenQuad(cx, cy, half, half, tex, 255, 255, 255);
        }
        else
        {
            batch.DrawScreenQuad(cx, cy, half, half, 60, 120, 220);
        }
    }

    public void RenderMonster(Monster monster, PrimitiveBatch batch, Camera camera)
    {
        var screen = camera.WorldToScreen(monster.WorldX, monster.WorldY, 0f);
        float half = 16f * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, 200, 60, 60);
    }

    public void RenderNPC(Npc npc, PrimitiveBatch batch, Camera camera, int elevation)
    {
        var screen = camera.WorldToScreen(npc.WorldX, npc.WorldY, elevation);
        float half = 15f * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, 70, 180, 100);
    }

    public void RenderProximityPrompt(Npc npc, PrimitiveBatch batch, Camera camera)
    {
        var screen = camera.WorldToScreen(npc.WorldX, npc.WorldY, 0f);
        batch.DrawScreenQuad(screen.X, screen.Y, 4f * camera.Zoom, 4f * camera.Zoom, 255, 255, 120);
    }

    public void RenderStructure(Structure structure, PrimitiveBatch batch, Camera camera)
    {
        var screen = camera.WorldToScreen(structure.WorldX, structure.WorldY, 0f);
        float half = 16f * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, 150, 150, 160);
    }

    public void RenderFire(FireInstance fire, PrimitiveBatch batch, Camera camera)
    {
        var screen = camera.WorldToScreen(fire.WorldX, fire.WorldY, 0f);
        float half = 10f * camera.Zoom;
        batch.DrawScreenQuad(screen.X, screen.Y, half, half, 230, 140, 40);
    }

    // ─── Sprite texture loading / caching ─────────────────────────────────

    private uint GetSpriteTexture(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
                return 0;
        }
        if (_cache.TryGetValue(key, out var entry))
        {
            return entry.Texture;
        }

        string path = System.IO.Path.Combine(_spritesDir, key + ".png");
        if (!System.IO.File.Exists(path))
        {
            // Cache the miss so we don't stat the filesystem every frame.
            _cache[key] = (0, 0, 0);
            return 0;
        }

        using var decoded = SKBitmap.Decode(path);
        if (decoded == null)
        {
            _cache[key] = (0, 0, 0);
            return 0;
        }

        // Pixel-art sprites (<=32px) are re-sampled 2-4x with nearest neighbor
        // so they stay crisp under the batcher's Linear filtering at higher zooms.
        SKBitmap bitmap = decoded;
        int upscale = decoded.Width <= 16 ? 4 : decoded.Width <= 32 ? 2 : 1;
        if (upscale > 1)
        {
            var resized = decoded.Resize(
                new SKImageInfo(decoded.Width * upscale, decoded.Height * upscale),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)) ?? decoded;
            bitmap = resized;
        }

        int w = bitmap.Width;
        int h = bitmap.Height;

        // Convert to RGBA8888 unpremultiplied so the batch shader tints cleanly.
        using var rgba = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(rgba);
        canvas.DrawBitmap(bitmap, 0, 0);

        // Convert to straight alpha: multiply RGB by alpha so the batch shader
        // can tint cleanly without double-multiplied alpha.
        Span<byte> span = rgba.GetPixelSpan();
        byte[] px = new byte[span.Length];
        span.CopyTo(px);
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a > 0 && a < 255)
            {
                // Straighten premultiplied alpha: scale RGB up by 255/a
                px[i]     = (byte)Math.Clamp(px[i]     * 255 / a, 0, 255);
                px[i + 1] = (byte)Math.Clamp(px[i + 1] * 255 / a, 0, 255);
                px[i + 2] = (byte)Math.Clamp(px[i + 2] * 255 / a, 0, 255);
            }
        }

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        unsafe
        {
            fixed (byte* ptr = px)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)w, (uint)h, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        _cache[key] = (tex, w, h);
        return tex;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var kv in _cache)
            _gl.DeleteTexture(kv.Value.Texture);
        _cache.Clear();
    }
}
