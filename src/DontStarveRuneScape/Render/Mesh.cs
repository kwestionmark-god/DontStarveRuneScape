namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using Silk.NET.Maths;
using System;
using System.Runtime.InteropServices;

/// <summary>
/// Simple quad mesh for 2D sprite rendering.
/// </summary>
public sealed class QuadMesh : IDisposable
{
    private readonly GL _gl;
    public readonly uint VAO;
    public readonly uint VBO;
    public readonly uint EBO;
    private bool _disposed;

    // Vertex format: position(2) + texCoord(2) + color(4) = 8 floats per vertex
    private const int Stride = 8 * sizeof(float);

    public unsafe QuadMesh(GL gl)
    {
        _gl = gl;

        VAO = _gl.GenVertexArray();
        VBO = _gl.GenBuffer();
        EBO = _gl.GenBuffer();

        _gl.BindVertexArray(VAO);

        // Quad vertices (two triangles)
        float[] vertices = {
            // Position      TexCoord    Color (white)
            -0.5f, -0.5f,   0f, 0f,     1f, 1f, 1f, 1f,  // Bottom-left
             0.5f, -0.5f,   1f, 0f,     1f, 1f, 1f, 1f,  // Bottom-right
             0.5f,  0.5f,   1f, 1f,     1f, 1f, 1f, 1f,  // Top-right
            -0.5f,  0.5f,   0f, 1f,     1f, 1f, 1f, 1f,  // Top-left
        };

        uint[] indices = { 0, 1, 2, 2, 3, 0 };

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, VBO);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, EBO);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Position attribute (location 0)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, Stride, (void*)0);

        // TexCoord attribute (location 1)
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, Stride, (void*)(2 * sizeof(float)));

        // Color attribute (location 2)
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, Stride, (void*)(4 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    public unsafe void Draw()
    {
        _gl.BindVertexArray(VAO);
        _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, null);
        _gl.BindVertexArray(0);
    }

    public unsafe void DrawInstanced(int count)
    {
        _gl.BindVertexArray(VAO);
        _gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, null, (uint)count);
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteVertexArray(VAO);
            _gl.DeleteBuffer(VBO);
            _gl.DeleteBuffer(EBO);
            _disposed = true;
        }
    }
}

/// <summary>
/// Dynamic quad mesh for batched sprite rendering with per-instance data.
/// </summary>
public sealed class InstancedQuadMesh : IDisposable
{
    private readonly GL _gl;
    public readonly uint VAO;
    public readonly uint VBO;
    public readonly uint InstanceVBO;
    public readonly uint EBO;
    private bool _disposed;

    // Instance data: modelMatrix(16) + texCoord(4) + color(4) = 24 floats per instance
    private const int InstanceStride = 24 * sizeof(float);
    private const int MaxInstances = 10000;

    public unsafe InstancedQuadMesh(GL gl)
    {
        _gl = gl;

        VAO = _gl.GenVertexArray();
        VBO = _gl.GenBuffer();
        InstanceVBO = _gl.GenBuffer();
        EBO = _gl.GenBuffer();

        _gl.BindVertexArray(VAO);

        // Base quad vertices (same for all instances)
        float[] vertices = {
            -0.5f, -0.5f,
             0.5f, -0.5f,
             0.5f,  0.5f,
            -0.5f,  0.5f,
        };

        uint[] indices = { 0, 1, 2, 2, 3, 0 };

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, VBO);
        unsafe
        {
            fixed (float* ptr = vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, EBO);
        unsafe
        {
            fixed (uint* ptr = indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ptr, BufferUsageARB.StaticDraw);
            }
        }

        // Position attribute (location 0)
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);

        // Instance buffer (dynamic)
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, InstanceVBO);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxInstances * InstanceStride), null, BufferUsageARB.DynamicDraw);

        // Model matrix (4 vec4s = locations 1-4)
        for (uint i = 0; i < 4; i++)
        {
            uint loc = 1 + i;
            _gl.EnableVertexAttribArray(loc);
            _gl.VertexAttribPointer(loc, 4, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, InstanceStride, (void*)(i * 16));
            _gl.VertexAttribDivisor(loc, 1);
        }

        // TexCoord (location 5)
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 4, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, InstanceStride, (void*)(16 * sizeof(float)));
        _gl.VertexAttribDivisor(5, 1);

        // Color (location 6)
        _gl.EnableVertexAttribArray(6);
        _gl.VertexAttribPointer(6, 4, (Silk.NET.OpenGL.GLEnum)VertexAttribPointerType.Float, false, InstanceStride, (void*)(20 * sizeof(float)));
        _gl.VertexAttribDivisor(6, 1);

        _gl.BindVertexArray(0);
    }

    public unsafe void UpdateInstances(Span<InstanceData> instances)
    {
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, InstanceVBO);
        fixed (InstanceData* ptr = instances)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(instances.Length * InstanceStride), ptr);
        }
    }

    public void Draw(int instanceCount)
    {
        _gl.BindVertexArray(VAO);
        unsafe
        {
            _gl.DrawElementsInstanced(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, null, (uint)instanceCount);
        }
        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteVertexArray(VAO);
            _gl.DeleteBuffer(VBO);
            _gl.DeleteBuffer(InstanceVBO);
            _gl.DeleteBuffer(EBO);
            _disposed = true;
        }
    }
}

/// <summary>
/// Per-instance data for instanced rendering.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    public Matrix4X4<float> Model;
    public Vector4D<float> TexCoord; // u0, v0, u1, v1
    public Vector4D<float> Color;
}