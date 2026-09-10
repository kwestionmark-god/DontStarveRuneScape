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
        AddVertex(x0, y0, cr, cg, cb, ca);
        AddVertex(x1, y0, cr, cg, cb, ca);
        AddVertex(x1, y1, cr, cg, cb, ca);
        AddVertex(x0, y0, cr, cg, cb, ca);
        AddVertex(x1, y1, cr, cg, cb, ca);
        AddVertex(x0, y1, cr, cg, cb, ca);
    }

    public void End()
    {
        if (_vertexCount == 0) return;

        _shader.Use();
        _shader.SetUniform("uProjection", Matrix4X4<float>.Identity);
        _shader.SetUniform("uView", Matrix4X4<float>.Identity);
        _shader.SetUniform("uUseTexture", false);

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
    }

    public void Flush() => End();

    private float ToNdcX(float screenX) => screenX * 2f / _screenWidth - 1f;
    private float ToNdcY(float screenY) => 1f - screenY * 2f / _screenHeight;

    private void AddVertex(float x, float y, float r, float g, float b, float a)
    {
        int i = _vertexCount;
        if (i + Stride > _vertices.Length) return;

        _vertices[i + 0] = x;
        _vertices[i + 1] = y;
        _vertices[i + 2] = 0f; // u
        _vertices[i + 3] = 0f; // v
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