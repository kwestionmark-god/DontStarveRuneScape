namespace DontStarveRuneScape.Render;

using System;
using System.Collections.Generic;
using SkiaSharp;
using Silk.NET.OpenGL;

/// <summary>
/// Rasterizes text with SkiaSharp and uploads the result to OpenGL textures.
/// Each glyph run is drawn white with straight alpha, so the glyph coverage is
/// multiplied by the caller-supplied tint color in the batch shader. Each distinct
/// (font, size, bold, italic, text) combination is rasterized once and cached;
/// repeated calls reuse the uploaded texture.
/// </summary>
public sealed class TextRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly string _fontsDir;

    private SKTypeface _regular = SKTypeface.Default;
    private SKTypeface _bold = SKTypeface.Default;

    // textureId -> (width, height) in pixels
    private readonly Dictionary<string, (uint Texture, int W, int H)> _cache = new();

    private int _lastW;
    private int _lastH;

    private bool _disposed;

    public TextRenderer(GL gl, string fontsDir)
    {
        _gl = gl;
        _fontsDir = fontsDir;
        LoadFonts();
    }

    /// <summary>
    /// Resolve the fonts directory from the assembly base directory if not provided.
    /// Bundled fonts ship under Assets/fonts next to the executable.
    /// </summary>
    public static TextRenderer Create(GL gl)
    {
        string baseDir = AppContext.BaseDirectory;
        string fontsDir = System.IO.Path.Combine(baseDir, "Assets", "fonts");
        if (!System.IO.Directory.Exists(fontsDir))
            fontsDir = baseDir;
        return new TextRenderer(gl, fontsDir);
    }

    private string RegularPath => System.IO.Path.Combine(_fontsDir, "LiberationMono-Regular.ttf");
    private string BoldPath => System.IO.Path.Combine(_fontsDir, "LiberationMono-Bold.ttf");

    private void LoadFonts()
    {
        try
        {
            var reg = SKTypeface.FromFile(RegularPath);
            var bol = SKTypeface.FromFile(BoldPath);
            if (reg != null) _regular = reg;
            if (bol != null) _bold = bol;
        }
        catch (Exception)
        {
            // Fall back to the default typeface if bundled fonts are missing.
        }
    }

    private SKTypeface Resolve(bool bold) => bold ? _bold : _regular;

    private SKFont BuildFont(int size, bool bold, bool italic)
        => new SKFont(Resolve(bold), size, 1f, italic ? -0.35f : 0f);

    /// <summary>
    /// Measure the pixel size that `text` would occupy at the given size.
    /// </summary>
    public (float Width, float Height) Measure(string text, int size, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrEmpty(text)) return (0f, 0f);
        var font = BuildFont(size, bold, italic);
        using var paint = new SKPaint();
        // FontMetrics has no "Height"; the line height spans Top(ascent) to Bottom(descent).
        float height = font.Metrics.Bottom - font.Metrics.Top;
        return (font.MeasureText(text, paint), height);
    }

    /// <summary>
    /// Draw a single line of text centered at (centerX, centerY), in screen pixels.
    /// </summary>
    public void DrawText(PrimitiveBatch batch, string text, float centerX, float centerY,
        int size, byte r, byte g, byte b, byte a = 255, bool bold = false, bool italic = false)
    {
        if (string.IsNullOrEmpty(text)) return;

        GetTexture(text, size, bold, italic);
        batch.DrawTexturedScreenQuad(centerX, centerY, _lastW * 0.5f, _lastH * 0.5f,
            _cache[Key(text, size, bold, italic)].Texture, r, g, b, a);
    }

    private uint GetTexture(string text, int size, bool bold, bool italic)
    {
        string key = Key(text, size, bold, italic);
        if (_cache.TryGetValue(key, out var entry))
        {
            _lastW = entry.W;
            _lastH = entry.H;
            return entry.Texture;
        }

        var font = BuildFont(size, bold, italic);
        var metrics = font.Metrics;
        using var paint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 255),
            IsAntialias = true,
        };

        // Height spans Top(ascent) to Bottom(descent); "Height" is not a member.
        int w = Math.Max(1, (int)Math.Ceiling(font.MeasureText(text, paint) + 1));
        int h = Math.Max(1, (int)Math.Ceiling(metrics.Bottom - metrics.Top) + 1);

        // Unpremultiplied keeps text straight-alpha (white glyph on clear background).
        using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.DrawColor(new SKColor(0, 0, 0, 0));
        // Baseline near the bottom so the full ascent/descent fits the texture.
        // Skew and bold live on the font; draw via the modern font-based overload.
        canvas.DrawText(text, 0f, h - metrics.Descent, SKTextAlign.Left, font, paint);

        // Normalize to straight-alpha white: RGB = 255 wherever there is coverage,
        // so the batch shader tints the glyph without darkening antialiased edges.
        Span<byte> span = bitmap.GetPixelSpan();
        byte[] px = new byte[span.Length];
        span.CopyTo(px);
        for (int i = 0; i + 3 < px.Length; i += 4)
        {
            if (px[i + 3] > 0)
            {
                px[i] = 255;
                px[i + 1] = 255;
                px[i + 2] = 255;
            }
        }

        uint tex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, tex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
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
        _lastW = w;
        _lastH = h;
        return tex;
    }

    private static string Key(string text, int size, bool bold, bool italic) =>
        $"{text}|{size}|{bold}|{italic}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kv in _cache)
            _gl.DeleteTexture(kv.Value.Texture);
        _cache.Clear();

        _regular?.Dispose();
        _bold?.Dispose();
    }
}
