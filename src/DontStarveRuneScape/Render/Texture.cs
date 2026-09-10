namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;

/// <summary>
/// Represents a texture region with UV coordinates.
/// </summary>
public struct TextureRegion
{
    public float U0, V0, U1, V1;
}

/// <summary>
/// Texture wrapper for OpenGL textures loaded from PNG files.
/// </summary>
public sealed class Texture : IDisposable
{
    private readonly GL _gl;
    public readonly uint Id;
    public readonly int Width;
    public readonly int Height;
    private bool _disposed;
    private Dictionary<string, TextureRegion> _regions;

    public Texture(GL gl, int width, int height, byte[] pixels)
    {
        _gl = gl;
        Width = width;
        Height = height;

        Id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Id);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _regions = new Dictionary<string, TextureRegion>();
    }

    public static Texture FromFile(GL gl, string path)
    {
        // Read the PNG file bytes
        var bytes = System.IO.File.ReadAllBytes(path);

        // Determine dimensions from PNG header
        if (bytes.Length < 8) throw new Exception("Invalid PNG file");

        // Skip PNG signature (8 bytes)
        int offset = 8;

        // Read IHDR chunk
        if (offset + 8 > bytes.Length) throw new Exception("Invalid PNG file: too short");
        int chunkType = BitConverter.ToInt32(bytes, offset + 4);
        if (chunkType != 0x48444952) // "IHDR"
            throw new Exception("Invalid PNG file: missing IHDR chunk");

        int width = BitConverter.ToInt32(bytes, offset + 8);
        int height = BitConverter.ToInt32(bytes, offset + 12);

        // For now, create a placeholder texture with the correct dimensions
        // In a full implementation, decompress the IDAT chunks
        var placeholderPixels = new byte[4 * width * height];
        for (int i = 0; i < placeholderPixels.Length; i += 4)
        {
            placeholderPixels[i] = 255;     // R
            placeholderPixels[i + 1] = 255; // G
            placeholderPixels[i + 2] = 255; // B
            placeholderPixels[i + 3] = 255; // A
        }

        var tex = new Texture(gl, width, height, placeholderPixels);
        tex._regions = new Dictionary<string, TextureRegion>();
        return tex;
    }

    public void Bind(int slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + slot);
        _gl.BindTexture(TextureTarget.Texture2D, Id);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteTexture(Id);
            _disposed = true;
        }
    }
}

/// <summary>
/// Texture atlas for packing multiple sprites into one texture.
/// </summary>
public sealed class TextureAtlas : IDisposable
{
    private readonly GL _gl;
    public readonly uint Id;
    public readonly int Width;
    public readonly int Height;
    private readonly Dictionary<string, TextureRegion> _regions;
    private bool _disposed;

    public TextureAtlas(GL gl, int width, int height)
    {
        _gl = gl;
        Width = width;
        Height = height;

        Id = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, Id);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        // Allocate empty texture
        unsafe
        {
            _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        }

        _regions = new Dictionary<string, TextureRegion>();
    }

    public void AddRegion(string name, int x, int y, int width, int height, byte[] pixels)
    {
        _gl.BindTexture(TextureTarget.Texture2D, Id);
        unsafe
        {
            fixed (byte* ptr = pixels)
            {
                _gl.TexSubImage2D(TextureTarget.Texture2D, 0, x, y, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, ptr);
            }
        }

        float u0 = (float)x / Width;
        float v0 = (float)y / Height;
        float u1 = (float)(x + width) / Width;
        float v1 = (float)(y + height) / Height;
        _regions[name] = new TextureRegion { U0 = u0, V0 = v0, U1 = u1, V1 = v1 };
    }

    public TextureRegion GetRegion(string name)
    {
        return _regions.TryGetValue(name, out var region) ? region : new TextureRegion { U0 = 0f, V0 = 0f, U1 = 1f, V1 = 1f };
    }

    public void Bind(int slot = 0)
    {
        _gl.ActiveTexture(TextureUnit.Texture0 + slot);
        _gl.BindTexture(TextureTarget.Texture2D, Id);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteTexture(Id);
            _disposed = true;
        }
    }
}