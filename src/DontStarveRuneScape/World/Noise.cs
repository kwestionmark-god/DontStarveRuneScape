namespace DontStarveRuneScape.World;

using DontStarveRuneScape.Config;

/// <summary>
/// Perlin/Simplex noise implementation for procedural generation.
/// Ported from Python's noise library (pnoise2).
/// </summary>
public static class Noise
{
    // Permutation table
    private static readonly int[] Perm = new int[512];
    private static readonly int[] PermMod12 = new int[512];

    static Noise()
    {
        // Initialize permutation table
        var p = new int[256];
        for (int i = 0; i < 256; i++) p[i] = i;

        // Shuffle with fixed seed for determinism
        var rand = new Random(42);
        for (int i = 255; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }

        for (int i = 0; i < 512; i++)
        {
            Perm[i] = p[i & 255];
            PermMod12[i] = Perm[i] % 12;
        }
    }

    /// <summary>
    /// 2D Perlin noise (equivalent to noise.pnoise2).
    /// </summary>
    public static float PNoise2(float x, float y, int octaves = 1, float persistence = 0.5f, float lacunarity = 2.0f)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            value += amplitude * Perlin2D(x * frequency, y * frequency);
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

    /// <summary>
    /// 2D Ridged noise for sharp ridges/valleys.
    /// </summary>
    public static float RidgedNoise2(float x, float y, int octaves = 1, float persistence = 0.5f, float lacunarity = 2.0f)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            // Matches the Python port: n = 1 - 2*|pnoise| per octave. The
            // previous |n| -> invert -> square form biased the sum upward,
            // contributing to elevation saturation.
            float n = Perlin2D(x * frequency, y * frequency);
            n = 1.0f - 2.0f * Math.Abs(n);
            value += amplitude * n;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value / maxValue;
    }

    /// <summary>
    /// Domain warping for geological distortion.
    /// </summary>
    public static (float X, float Y) DomainWarp(float x, float y, float scale, int octaves, float amplitude)
    {
        float wx = x;
        float wy = y;

        for (int i = 0; i < octaves; i++)
        {
            float freq = scale * (1 << i);
            float amp = amplitude * (float)Math.Pow(0.5, i);

            float dx = PNoise2(wx * freq, wy * freq) * amp;
            float dy = PNoise2((wx + 1000) * freq, (wy + 1000) * freq) * amp;

            wx += dx;
            wy += dy;
        }

        return (wx, wy);
    }

    // Simplex/Perlin 2D implementation
    private static float Perlin2D(float x, float y)
    {
        // Skew the input space to determine which simplex cell we're in
        const float F2 = 0.366025403f; // 0.5 * (sqrt(3) - 1)
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        const float G2 = 0.211324865f; // (3 - sqrt(3)) / 6
        float t = (i + j) * G2;
        float X0 = i - t;
        float Y0 = j - t;
        float x0 = x - X0;
        float y0 = y - Y0;

        // Determine which simplex we're in
        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }
        else { i1 = 0; j1 = 1; }

        // Offsets for corners
        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1.0f + 2.0f * G2;
        float y2 = y0 - 1.0f + 2.0f * G2;

        // Hash coordinates
        int ii = i & 255;
        int jj = j & 255;

        int gi0 = PermMod12[ii + Perm[jj]];
        int gi1 = PermMod12[ii + i1 + Perm[jj + j1]];
        int gi2 = PermMod12[ii + 1 + Perm[jj + 1]];

        // Gradient dot products. Simplex attenuation must be t^4 (t0 squared
        // twice); t^2 keeps contributions far too large and the 70x scale pushes
        // the output well beyond ±1, which saturated the worldgen elevation
        // clamp (most tiles collapsed to max elevation).
        float t0 = 0.5f - x0 * x0 - y0 * y0;
        float n0 = 0f;
        if (t0 > 0) { t0 *= t0; n0 = t0 * t0 * Grad(gi0, x0, y0); }

        float t1 = 0.5f - x1 * x1 - y1 * y1;
        float n1 = 0f;
        if (t1 > 0) { t1 *= t1; n1 = t1 * t1 * Grad(gi1, x1, y1); }

        float t2 = 0.5f - x2 * x2 - y2 * y2;
        float n2 = 0f;
        if (t2 > 0) { t2 *= t2; n2 = t2 * t2 * Grad(gi2, x2, y2); }

        return 70.0f * (n0 + n1 + n2); // Scale to roughly -1..1
    }

    private static int FastFloor(float x)
    {
        int i = (int)x;
        return x < i ? i - 1 : i;
    }

    private static float Grad(int hash, float x, float y)
    {
        // 12 gradient vectors for 2D
        var grads = new (float x, float y)[]
        {
            (1, 1), (-1, 1), (1, -1), (-1, -1),
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (-1, 1), (1, -1), (-1, -1) // Duplicate for 12
        };
        var g = grads[hash];
        return g.x * x + g.y * y;
    }
}