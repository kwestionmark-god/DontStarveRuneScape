namespace DontStarveRuneScape.Render;

using Silk.NET.OpenGL;
using Silk.NET.Maths;
using System;
using System.IO;

/// <summary>
/// Shader program wrapper for OpenGL shaders.
/// </summary>
public sealed class Shader : IDisposable
{
    private readonly GL _gl;
    public readonly uint Program;
    private bool _disposed;

    public Shader(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        Program = CreateProgram(vertexSource, fragmentSource);
    }

    private uint CreateProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);
        _gl.ShaderSource(vertexShader, vertexSource);
        _gl.CompileShader(vertexShader);
        CheckCompileError(vertexShader, "VERTEX");

        uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
        _gl.ShaderSource(fragmentShader, fragmentSource);
        _gl.CompileShader(fragmentShader);
        CheckCompileError(fragmentShader, "FRAGMENT");

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);
        CheckLinkError(program);

        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }

    private void CheckCompileError(uint shader, string type)
    {
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = _gl.GetShaderInfoLog(shader);
            throw new Exception($"Shader compile error ({type}): {infoLog}");
        }
    }

    private void CheckLinkError(uint program)
    {
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = _gl.GetProgramInfoLog(program);
            throw new Exception($"Shader link error: {infoLog}");
        }
    }

    public void Use() => _gl.UseProgram(Program);

    public void SetUniform(string name, int value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, bool value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform1(loc, value ? 1 : 0);
    }

    public void SetUniform(string name, float value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform1(loc, value);
    }

    public void SetUniform(string name, Matrix4X4<float> value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1)
        {
            var m = value;
            unsafe
            {
                float* ptr = (float*)&m;
                _gl.UniformMatrix4(loc, 1, false, ptr);
            }
        }
    }

    public void SetUniform(string name, Vector2D<float> value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform2(loc, value.X, value.Y);
    }

    public void SetUniform(string name, Vector3D<float> value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform3(loc, value.X, value.Y, value.Z);
    }

    public void SetUniform(string name, Vector4D<float> value)
    {
        int loc = _gl.GetUniformLocation(Program, name);
        if (loc != -1) _gl.Uniform4(loc, value.X, value.Y, value.Z, value.W);
    }

    public int GetAttribLocation(string name) => _gl.GetAttribLocation(Program, name);

    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteProgram(Program);
            _disposed = true;
        }
    }

    public static Shader LoadFromFiles(GL gl, string vertexPath, string fragmentPath)
    {
        string vertexSource = File.ReadAllText(vertexPath);
        string fragmentSource = File.ReadAllText(fragmentPath);
        return new Shader(gl, vertexSource, fragmentSource);
    }
}

/// <summary>
/// Basic vertex shader for 2D rendering with camera projection.
/// </summary>
public static class Shaders
{
    public const string VertexShader2D = @"
#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

uniform mat4 uProjection;
uniform mat4 uView;

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    gl_Position = uProjection * uView * vec4(aPosition, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}
";

    public const string FragmentShader2D = @"
#version 330 core
in vec2 vTexCoord;
in vec4 vColor;

uniform sampler2D uTexture;
uniform bool uUseTexture;

out vec4 FragColor;

void main()
{
    vec4 texColor = uUseTexture ? texture(uTexture, vTexCoord) : vec4(1.0);
    FragColor = texColor * vColor;
}
";

    public const string FragmentShaderTerrain = @"
#version 330 core
in vec2 vTexCoord;
in vec4 vColor;

uniform sampler2D uTexture;
uniform bool uUseTexture;
uniform vec3 uBiomeColor;
uniform float uElevation;

out vec4 FragColor;

void main()
{
    vec4 baseColor = uUseTexture ? texture(uTexture, vTexCoord) : vec4(uBiomeColor, 1.0);
    // Apply elevation shading
    float shade = 1.0 - uElevation * 0.1;
    FragColor = vec4(baseColor.rgb * shade, baseColor.a) * vColor;
}
";
}