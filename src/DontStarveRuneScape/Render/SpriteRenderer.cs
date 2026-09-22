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

    public void RenderResource(ResourceNode resource, PrimitiveBatch batch, Camera camera, float elevation, int tileX, int tileY, World.Tile? tile = null)
    {
        float half = 24f
            * (resource.ResourceDef?.DisplayScale > 0 ? resource.ResourceDef.DisplayScale : FallbackScale(resource))
            * resource.SizeScale
            * camera.Zoom;

        // Ground point at tile center — bottom-anchored billboard (same scheme
        // as the player sprite) so the resource keeps its tile placement under
        // any camera yaw/pitch instead of sliding around the tile center.
        var screen = camera.WorldToScreen(
            tileX * Constants.TileSize + Constants.TileSize * 0.5f,
            tileY * Constants.TileSize + Constants.TileSize * 0.5f,
            elevation);
        float cx = screen.X;
        float cy = screen.Y - half;

        var tier = camera.Tier;
        if (tier == Camera.LodTier.Far)
        {
            // Far: no textures, no texture binds — a single tiny dot carries
            // the tile's occupancy. 'Ubiquitous' filler isn't worth drawing.
            if (resource.ResourceDef?.Rarity == "ubiquitous") return;
            float dot = 3f * camera.Zoom;
            batch.DrawScreenQuad(cx, screen.Y, dot, dot, 200, 210, 160);
            return;
        }

        string spriteKey = resource.GetSpriteKey();
        uint tex = 0;
        // Deterministic per-tile variant: try "key_v0..v2" sprites (if artist
        // files ever exist); the negative cache makes misses free after first check.
        if (resource.ResourceDef != null && !resource.IsDepleted)
        {
            int variant = (tileX * 73856093 ^ tileY * 19349663) & 0x7fffffff % 3;
            uint vt = GetSpriteTexture($"{spriteKey}_v{variant}");
            if (vt != 0) { spriteKey = $"{spriteKey}_v{variant}"; tex = vt; }
        }
        if (tex == 0)
            tex = GetSpriteTexture(spriteKey);

        // Ground-decal resources (water pools etc.) are drawn flat on their
        // tile's projected footprint — never as billboards.
        if (resource.ResourceDef?.GroundDecal == true && tile != null && tex != 0)
        {
            float ts = Constants.TileSize;
            const float inset = 0.08f; // keep the sprite just inside the tile
            var c00 = camera.WorldToScreen((tileX + inset) * ts, (tileY + inset) * ts, tile.CornerElevations?[0] ?? tile.Elevation);
            var c10 = camera.WorldToScreen((tileX + 1 - inset) * ts, (tileY + inset) * ts, tile.CornerElevations?[1] ?? tile.Elevation);
            var c11 = camera.WorldToScreen((tileX + 1 - inset) * ts, (tileY + 1 - inset) * ts, tile.CornerElevations?[2] ?? tile.Elevation);
            var c01 = camera.WorldToScreen((tileX + inset) * ts, (tileY + 1 - inset) * ts, tile.CornerElevations?[3] ?? tile.Elevation);
            batch.DrawScreenQuadCornersTextured(c00.X, c00.Y, c10.X, c10.Y, c11.X, c11.Y, c01.X, c01.Y, tex, 255, 255, 255, (byte)220);
            return;
        }

        // Soft shadow grounds every sprite (Nearest only — dots don't cast,
        // and neither does a flat ground decal).
        if (tier == Camera.LodTier.Nearest && !resource.IsDepleted)
            DrawShadow(batch, cx, screen.Y, half, 200);
        if (tex != 0)
        {
            // Mid: sprites smaller than ~6 px half-size degrade to dots —
            // the texture fetch costs more than the detail is worth.
            if (tier == Camera.LodTier.Mid && half < 6f)
            {
                batch.DrawScreenQuad(cx, screen.Y, 3f * camera.Zoom, 3f * camera.Zoom, 200, 210, 160);
                return;
            }
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

    /// <summary>
    /// Fallback size scale by sprite family when the JSON has no display_scale.
    /// Trees read larger than shrubs and smalls.
    /// </summary>
    private static float FallbackScale(ResourceNode resource)
    {
        string key = resource.ResourceDef?.SpriteKey ?? string.Empty;
        if (key.StartsWith("trees/")) return 2.2f;
        if (key.StartsWith("rocks/")) return 1.25f;
        if (key.StartsWith("world/")) return 0.75f;
        return 1f;
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

        DrawShadow(batch, screen.X, screen.Y, half, 220);

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

    // ─── Soft ground shadow ───────────────────────────────────────────────

    private uint _shadowTex;

    private uint GetShadowTexture()
    {
        if (_shadowTex != 0) return _shadowTex;

        const int Size = 64;
        var px = new byte[Size * Size * 4];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float nx = (x + 0.5f) / Size * 2f - 1f;
                float ny = (y + 0.5f) / Size * 2f - 1f;
                float d = MathF.Sqrt(nx * nx + ny * ny);
                float t = Math.Clamp(1f - d, 0f, 1f);
                byte a = (byte)(t * t * 200f); // quadratic falloff
                int i = (y * Size + x) * 4;
                px[i] = 20; px[i + 1] = 20; px[i + 2] = 25; px[i + 3] = a;
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
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, Size, Size, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }
        _shadowTex = tex;
        return _shadowTex;
    }

    /// <summary>
    /// Soft ellipse shadow under a sprite standing at (cx, groundY). Kept
    /// flattened and tightly inside the sprite's own tile band — anything
    /// taller would get buried by nearer tile quads in the painter order.
    /// </summary>
    public void DrawShadow(PrimitiveBatch batch, float cx, float groundY, float half, byte alphaScale = 255)
    {
        uint tex = GetShadowTexture();
        batch.DrawTexturedScreenQuad(
            cx + half * 0.10f, groundY - half * 0.10f,
            half * 0.85f, half * 0.22f,
            tex, 255, 255, 255, alphaScale);
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
