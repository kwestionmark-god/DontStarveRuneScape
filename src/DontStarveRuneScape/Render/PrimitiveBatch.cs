namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using Silk.NET.Maths;

/// <summary>
/// Batches colored screen-space quads into a single OpenGL draw call.
/// Positions are passed to the shader as normalized device coordinates, so the
/// shader's projection and view matrices are kept at identity.
/// </summary>
public sealed class PrimitiveBatch : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vao;
    private readonly uint _vbo;

    // position(2) + texCoord(2) + color(4)
    private const int Stride = 8;
    private const int MaxQuads = 16384;
    private readonly float[] _vertices = new float[MaxQuads * 6 * Stride];
    private int _vertexCount;
    private int _screenWidth = 1;
    private int _screenHeight = 1;

    // Textured quads (e.g. rendered text) share this batch. We flush whenever the
    // active mode flips between color-only and textured so every draw call is
    // homogeneous with respect to the uUseTexture uniform.
    private bool _textureMode;
    private uint _boundTexture;

    private bool _disposed;

    public unsafe PrimitiveBatch(GL gl)
    {
        _gl = gl;
        _shader = new Shader(gl, Shaders.VertexShader2D, Shaders.FragmentShader2D);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertices.Length * sizeof(float)), null, BufferUsageARB.StreamDraw);
        }

        // Position (location 0)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, Stride * sizeof(float), (void*)0);

        // TexCoord (location 1)
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, Stride * sizeof(float), (void*)(2 * sizeof(float)));

        // Color (location 2)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, Stride * sizeof(float), (void*)(4 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public void Begin(int screenWidth, int screenHeight)
    {
        _screenWidth = Math.Max(1, screenWidth);
        _screenHeight = Math.Max(1, screenHeight);
        _vertexCount = 0;
    }

    public void DrawScreenQuad(float centerX, float centerY, float halfWidth, float halfHeight, byte r, byte g, byte b, byte a = 255)
    {
        // Switching from a textured block to a color block ends the previous batch.
        if (_textureMode) Flush();

        if (_vertexCount + 6 * Stride > _vertices.Length)
            Flush();

        float x0 = ToNdcX(centerX - halfWidth);
        float x1 = ToNdcX(centerX + halfWidth);
        float y0 = ToNdcY(centerY + halfHeight); // bottom
        float y1 = ToNdcY(centerY - halfHeight); // top

        float cr = r / 255f;
        float cg = g / 255f;
        float cb = b / 255f;
        float ca = a / 255f;

        // Quad corners: bottom-left, bottom-right, top-right, top-left.
        AddVertex(x0, y0, 0f, 0f, cr, cg, cb, ca);
        AddVertex(x1, y0, 1f, 0f, cr, cg, cb, ca);
        AddVertex(x1, y1, 1f, 1f, cr, cg, cb, ca);
        AddVertex(x0, y0, 0f, 0f, cr, cg, cb, ca);
        AddVertex(x1, y1, 1f, 1f, cr, cg, cb, ca);
        AddVertex(x0, y1, 0f, 1f, cr, cg, cb, ca);
    }

    /// <summary>
    /// Draw a textured screen-space quad. The texture is bound for the batch and
    /// the glyph alpha is multiplied by the supplied tint color.
    /// </summary>
    public void DrawTexturedScreenQuad(float centerX, float centerY, float halfWidth, float halfHeight,
        uint texture, byte r, byte g, byte b, byte a = 255)
    {
        // End the previous batch when switching modes or when the texture changes
        // mid-batch — otherwise buffered quads would be drawn with the last bound
        // texture (mixed sprites would all share one texture).
        if (!_textureMode || _boundTexture != texture) Flush();

        if (_vertexCount + 6 * Stride > _vertices.Length)
            Flush();

        float x0 = ToNdcX(centerX - halfWidth);
        float x1 = ToNdcX(centerX + halfWidth);
        float y0 = ToNdcY(centerY + halfHeight); // bottom
        float y1 = ToNdcY(centerY - halfHeight); // top

        float cr = r / 255f;
        float cg = g / 255f;
        float cb = b / 255f;
        float ca = a / 255f;

        // v is flipped so the uploaded bitmap (row 0 = glyph top) reads upright.
        AddVertex(x0, y0, 0f, 1f, cr, cg, cb, ca);
        AddVertex(x1, y0, 1f, 1f, cr, cg, cb, ca);
        AddVertex(x1, y1, 1f, 0f, cr, cg, cb, ca);
        AddVertex(x0, y0, 0f, 1f, cr, cg, cb, ca);
        AddVertex(x1, y1, 1f, 0f, cr, cg, cb, ca);
        AddVertex(x0, y1, 0f, 0f, cr, cg, cb, ca);

        _textureMode = true;
        _boundTexture = texture;
    }

    /// <summary>
    /// Draw a quadrilateral from four screen-space corners (in screen pixel coords).
    /// Corners are ordered: bottom-left, bottom-right, top-right, top-left.
    /// </summary>
    public void DrawScreenQuadCorners(float blX, float blY, float brX, float brY, float trX, float trY, float tlX, float tlY, byte r, byte g, byte b, byte a = 255)
    {
        if (_textureMode) Flush();

        if (_vertexCount + 6 * Stride > _vertices.Length)
            Flush();

        float cr = r / 255f;
        float cg = g / 255f;
        float cb = b / 255f;
        float ca = a / 255f;

        AddVertex(ToNdcX(blX), ToNdcY(blY), 0f, 0f, cr, cg, cb, ca);
        AddVertex(ToNdcX(brX), ToNdcY(brY), 1f, 0f, cr, cg, cb, ca);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f, 1f, cr, cg, cb, ca);
        AddVertex(ToNdcX(blX), ToNdcY(blY), 0f, 0f, cr, cg, cb, ca);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f, 1f, cr, cg, cb, ca);
        AddVertex(ToNdcX(tlX), ToNdcY(tlY), 0f, 1f, cr, cg, cb, ca);
    }

    /// <summary>
    /// Draw a textured quadrilateral from four screen-space corners.
    /// Corners are ordered: bottom-left, bottom-right, top-right, top-left.
    /// </summary>
    /// <summary>
    /// Draw a textured quadrilateral from four screen-space corners with a UV
    /// offset (used for animated water). Same as the textureless-param
    /// overload but shifted UVs.
    /// </summary>
    public void DrawScreenQuadCornersTexturedUV(float blX, float blY, float brX, float brY, float trX, float trY, float tlX, float tlY,
        uint texture, float uOffset, float vOffset)
    {
        if (!_textureMode || _boundTexture != texture) Flush();
        if (_vertexCount + 6 * Stride > _vertices.Length) Flush();

        AddVertex(ToNdcX(blX), ToNdcY(blY), uOffset, 1f + vOffset, 1, 1, 1, 1);
        AddVertex(ToNdcX(brX), ToNdcY(brY), 1f + uOffset, 1f + vOffset, 1, 1, 1, 1);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f + uOffset, vOffset, 1, 1, 1, 1);
        AddVertex(ToNdcX(blX), ToNdcY(blY), uOffset, 1f + vOffset, 1, 1, 1, 1);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f + uOffset, vOffset, 1, 1, 1, 1);
        AddVertex(ToNdcX(tlX), ToNdcY(tlY), uOffset, vOffset, 1, 1, 1, 1);

        _textureMode = true;
        _boundTexture = texture;
    }

    /// <summary>
    /// Draw a textured quadrilateral from four screen-space corners.
    /// Corners are ordered: bottom-left, bottom-right, top-right, top-left.
    /// </summary>
    public void DrawScreenQuadCornersTextured(float blX, float blY, float brX, float brY, float trX, float trY, float tlX, float tlY,
        uint texture, byte r, byte g, byte b, byte a = 255)
    {
        if (!_textureMode || _boundTexture != texture) Flush();

        if (_vertexCount + 6 * Stride > _vertices.Length)
            Flush();

        float cr = r / 255f;
        float cg = g / 255f;
        float cb = b / 255f;
        float ca = a / 255f;

        AddVertex(ToNdcX(blX), ToNdcY(blY), 0f, 1f, cr, cg, cb, ca);
        AddVertex(ToNdcX(brX), ToNdcY(brY), 1f, 1f, cr, cg, cb, ca);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f, 0f, cr, cg, cb, ca);
        AddVertex(ToNdcX(blX), ToNdcY(blY), 0f, 1f, cr, cg, cb, ca);
        AddVertex(ToNdcX(trX), ToNdcY(trY), 1f, 0f, cr, cg, cb, ca);
        AddVertex(ToNdcX(tlX), ToNdcY(tlY), 0f, 0f, cr, cg, cb, ca);

        _textureMode = true;
        _boundTexture = texture;
    }

    /// <summary>
    /// Draw a convex polygon fan (used for shoreline water overlays that get
    /// cut by terrain crossings). Points are screen-space, already ordered.
    /// </summary>
    public void DrawScreenPolygon(System.Collections.Generic.List<Silk.NET.Maths.Vector2D<float>> pts,
        byte r, byte g, byte b, byte a)
    {
        if (_textureMode) Flush();
        if (pts.Count < 3) return;

        float cr = r / 255f, cg = g / 255f, cb = b / 255f, ca = a / 255f;
        for (int i = 2; i < pts.Count; i++)
        {
            if (_vertexCount + 3 * Stride > _vertices.Length) Flush();
            AddVertex(ToNdcX(pts[0].X), ToNdcY(pts[0].Y), 0f, 0f, cr, cg, cb, ca);
            AddVertex(ToNdcX(pts[i - 1].X), ToNdcY(pts[i - 1].Y), 0f, 0f, cr, cg, cb, ca);
            AddVertex(ToNdcX(pts[i].X), ToNdcY(pts[i].Y), 0f, 0f, cr, cg, cb, ca);
        }
    }

    /// <summary>
    /// Draw a convex polygon fan with per-vertex colors (used for gradient
    /// water: depth-tinted surface quads and alpha-faded shore aprons).
    /// Points are screen-space, already ordered around the polygon.
    /// </summary>
    public void DrawScreenPolygonGradient(
        System.Collections.Generic.List<(float X, float Y, byte R, byte G, byte B, byte A)> pts)
    {
        if (_textureMode) Flush();
        if (pts.Count < 3) return;

        for (int i = 2; i < pts.Count; i++)
        {
            if (_vertexCount + 3 * Stride > _vertices.Length) Flush();
            var p0 = pts[0];
            var p1 = pts[i - 1];
            var p2 = pts[i];
            AddVertex(ToNdcX(p0.X), ToNdcY(p0.Y), 0f, 0f, p0.R / 255f, p0.G / 255f, p0.B / 255f, p0.A / 255f);
            AddVertex(ToNdcX(p1.X), ToNdcY(p1.Y), 0f, 0f, p1.R / 255f, p1.G / 255f, p1.B / 255f, p1.A / 255f);
            AddVertex(ToNdcX(p2.X), ToNdcY(p2.Y), 0f, 0f, p2.R / 255f, p2.G / 255f, p2.B / 255f, p2.A / 255f);
        }
    }

    public void End()
    {
        if (_vertexCount == 0)
        {
            _textureMode = false;
            return;
        }

        _shader.Use();
        _shader.SetUniform("uProjection", Matrix4X4<float>.Identity);
        _shader.SetUniform("uView", Matrix4X4<float>.Identity);
        _shader.SetUniform("uUseTexture", _textureMode);

        if (_textureMode)
            _gl.BindTexture(TextureTarget.Texture2D, _boundTexture);

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* ptr = _vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(_vertexCount * sizeof(float)), ptr, BufferUsageARB.StreamDraw);
            }
        }
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)(_vertexCount / Stride));
        _gl.BindVertexArray(0);
        _vertexCount = 0;
        _textureMode = false;
        _boundTexture = 0;
    }

    public void Flush() => End();

    private float ToNdcX(float screenX) => screenX * 2f / _screenWidth - 1f;
    private float ToNdcY(float screenY) => 1f - screenY * 2f / _screenHeight;

    private void AddVertex(float x, float y, float u, float v, float r, float g, float b, float a)
    {
        int i = _vertexCount;
        if (i + Stride > _vertices.Length) return;

        _vertices[i + 0] = x;
        _vertices[i + 1] = y;
        _vertices[i + 2] = u;
        _vertices[i + 3] = v;
        _vertices[i + 4] = r;
        _vertices[i + 5] = g;
        _vertices[i + 6] = b;
        _vertices[i + 7] = a;
        _vertexCount += Stride;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteVertexArray(_vao);
        _shader.Dispose();
    }
}