using System;
using System.Collections.Generic;

namespace Hammerdeep;

/// <summary>A cell of the map: x east, y south, z up (0 is the deepest level).</summary>
public readonly record struct P3(int X, int Y, int Z)
{
    public static P3 operator +(P3 a, P3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static P3 operator -(P3 a, P3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    /// <summary>Chebyshev distance in the plane, plus three per level -- the cheap guess for "near".</summary>
    public int Dist(P3 o) => Math.Max(Math.Abs(X - o.X), Math.Abs(Y - o.Y)) + 3 * Math.Abs(Z - o.Z);
    public override string ToString() => $"{X},{Y},{Z}";
}

/// <summary>A small deterministic generator (xorshift64*), so a seed always plays out the same.</summary>
public sealed class Rng
{
    public ulong State;
    [System.Text.Json.Serialization.JsonConstructor] public Rng() { State = 0x9E3779B97F4A7C15UL; }
    public Rng(ulong seed) { State = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
    public ulong Next()
    {
        State ^= State >> 12; State ^= State << 25; State ^= State >> 27;
        return State * 2685821657736338717UL;
    }
    /// <summary>0 .. n-1.</summary>
    public int Int(int n) => n <= 1 ? 0 : (int)(Next() % (ulong)n);
    public int Range(int lo, int hi) => lo + Int(hi - lo + 1);
    public double Float() => (Next() >> 11) * (1.0 / (1UL << 53));
    public bool Chance(double p) => Float() < p;
    public T Pick<T>(IReadOnlyList<T> list) => list[Int(list.Count)];
    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = Int(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }
}

/// <summary>Hash noise for world generation and for tile art: smooth value noise and fBm.</summary>
public static class Noise
{
    public static uint Hash(int x, int y, int z, uint seed)
    {
        uint h = seed ^ (uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2147483647u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
    public static float Hash01(int x, int y, int z, uint seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / (float)0x1000000;

    private static float Smooth(float t) => t * t * (3 - 2 * t);

    public static float Value2(float x, float y, uint seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = Smooth(x - x0), ty = Smooth(y - y0);
        float a = Hash01(x0, y0, 0, seed), b = Hash01(x0 + 1, y0, 0, seed);
        float c = Hash01(x0, y0 + 1, 0, seed), d = Hash01(x0 + 1, y0 + 1, 0, seed);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
    }

    public static float Value3(float x, float y, float z, uint seed)
    {
        int z0 = (int)MathF.Floor(z);
        float tz = Smooth(z - z0);
        float lo = Value2(x, y, seed + (uint)z0 * 7919u), hi = Value2(x, y, seed + (uint)(z0 + 1) * 7919u);
        return lo + (hi - lo) * tz;
    }

    public static float Fbm2(float x, float y, uint seed, int octaves = 4)
    {
        float sum = 0, amp = 0.5f, norm = 0, f = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += Value2(x * f, y * f, seed + (uint)o * 1013u) * amp;
            norm += amp; amp *= 0.5f; f *= 2;
        }
        return sum / norm;
    }
}

/// <summary>A colour as plain data, so the simulation tables need no Godot types.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Hex(uint v) => new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    public Rgb Scale(float f) => new(Clamp(R * f), Clamp(G * f), Clamp(B * f));
    public Rgb Mix(Rgb o, float t) => new(Clamp(R + (o.R - R) * t), Clamp(G + (o.G - G) * t), Clamp(B + (o.B - B) * t));
    private static byte Clamp(float v) => (byte)Math.Clamp((int)v, 0, 255);
}
