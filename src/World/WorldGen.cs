using System;
using System.Collections.Generic;

namespace Hammerdeep;

/// <summary>
/// Makes an embark site from a seed: rolling hills rising to a mountain in the
/// east, ponds in the low ground, woods, soil over sedimentary rock over hard
/// stone, ore veins and gems, a cavern layer, magma at the bottom. Everything
/// below the surface starts hidden.
/// </summary>
public static class WorldGen
{
    public const int DefaultW = 192, DefaultH = 192, DefaultD = 50;

    public sealed class Result
    {
        public World World;
        public P3 Embark;
        public int[] Height;
        public int CavernZ;
    }

    public static Result Generate(ulong seed, int w = DefaultW, int h = DefaultH, int d = DefaultD)
    {
        var rng = new Rng(seed);
        uint s = (uint)seed ^ (uint)(seed >> 32);
        var W = new World(w, h, d);
        int[] height = new int[w * h];
        int baseZ = d - 14;

        // Heightmap: gentle fBm hills, a mountain shoulder to the east.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float n = Noise.Fbm2(x / 48f, y / 48f, s, 5);
                float east = MathF.Max(0, (x - w * 0.62f) / (w * 0.38f));
                float hz = baseZ + (n - 0.5f) * 9 + east * east * 9;
                height[y * w + x] = Math.Clamp((int)MathF.Round(hz), 8, d - 3);
            }
        // Limit slopes to one level per step on most of the map, so it can be walked.
        for (int pass = 0; pass < 6; pass++)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = height[y * w + x], lo = c;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((uint)nx < (uint)w && (uint)ny < (uint)h) lo = Math.Min(lo, height[ny * w + nx]);
                        }
                    // The mountain keeps some cliffs.
                    int allow = x > w * 0.8f && Noise.Hash01(x, y, 1, s) < 0.3f ? 2 : 1;
                    if (c - lo > allow) height[y * w + x] = lo + allow;
                }

        int cavernZ = Math.Max(6, baseZ - 20);
        byte loam = Tables.Mi("loam"), clay = Tables.Mi("clay"), sand = Tables.Mi("sand");
        byte sandstone = Tables.Mi("sandstone"), limestone = Tables.Mi("limestone"), granite = Tables.Mi("granite"),
            slate = Tables.Mi("slate"), marble = Tables.Mi("marble"), basalt = Tables.Mi("basalt"), obsidian = Tables.Mi("obsidian");

        // Columns: rock and soil up to the ground, a floor at ground level, sky above.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int g = height[y * w + x];
                float soilN = Noise.Value2(x / 20f, y / 20f, s + 11);
                int soilDepth = 2 + (int)(Noise.Value2(x / 16f, y / 16f, s + 12) * 4);
                byte soil = soilN < 0.33f ? sand : soilN < 0.7f ? loam : clay;
                int sedBottom = cavernZ + 6 + (int)(Noise.Value2(x / 30f, y / 30f, s + 13) * 5);
                for (int z = 0; z < d; z++)
                {
                    int i = W.Idx(x, y, z);
                    if (z > g) { W.Shapes[i] = Shape.Empty; W.Mats[i] = soil; continue; }
                    if (z == g) { W.Shapes[i] = Shape.Floor; W.Mats[i] = soil; continue; }
                    W.Shapes[i] = Shape.Wall;
                    int depth = g - z;
                    byte m;
                    if (z == 0) m = obsidian;
                    else if (depth <= soilDepth) m = soil;
                    else if (z >= sedBottom) m = Noise.Value3(x / 22f, y / 22f, z / 3f, s + 14) < 0.5f ? sandstone : limestone;
                    else if (z > 8)
                    {
                        float r = Noise.Value3(x / 26f, y / 26f, z / 4f, s + 15);
                        m = r < 0.45f ? granite : r < 0.8f ? slate : marble;
                    }
                    else m = z <= 3 ? obsidian : basalt;
                    W.Mats[i] = m;
                }
            }

        // Ore veins: random walks through the rock that suits them.
        void Vein(string ore, int zLo, int zHi, int count, int len)
        {
            byte om = Tables.Mi(ore);
            for (int v = 0; v < count; v++)
            {
                int x = rng.Int(w), y = rng.Int(h), z = rng.Range(zLo, zHi);
                for (int k = 0; k < len; k++)
                {
                    if (W.In(x, y, z))
                    {
                        int i = W.Idx(x, y, z);
                        if (W.Shapes[i] == Shape.Wall && Tables.Mats[W.Mats[i]].Kind == MatKind.Rock) W.Mats[i] = om;
                    }
                    x += rng.Range(-1, 1); y += rng.Range(-1, 1);
                    if (rng.Chance(0.15)) z += rng.Range(-1, 1);
                }
            }
        }
        int top = baseZ + 6;
        Vein("coal", cavernZ + 4, top, 60, 70);
        Vein("malachite", cavernZ, top, 45, 60);
        Vein("cassiterite", cavernZ, top, 35, 50);
        Vein("hematite", cavernZ - 6, top - 4, 45, 60);
        Vein("argentite", 6, cavernZ + 4, 25, 40);
        Vein("goldvein", 4, cavernZ, 15, 30);
        // Gems: small clusters, richer deeper.
        string[] gems = { "quartz", "quartz", "amethyst", "garnet", "emerald", "ruby" };
        for (int g = 0; g < 500; g++)
        {
            int x = rng.Int(w), y = rng.Int(h), z = rng.Range(4, top);
            int tier = Math.Clamp((top - z) * gems.Length / (top - 3) + rng.Range(-1, 1), 0, gems.Length - 1);
            byte gm = Tables.Mi(gems[tier]);
            int n = rng.Range(1, 3);
            for (int k = 0; k < n; k++)
            {
                int xx = x + rng.Range(-1, 1), yy = y + rng.Range(-1, 1);
                if (!W.In(xx, yy, z)) continue;
                int i = W.Idx(xx, yy, z);
                if (W.Shapes[i] == Shape.Wall && Tables.Mats[W.Mats[i]].Kind == MatKind.Rock) W.Mats[i] = gm;
            }
        }

        // The cavern layer: two-high open caves with stalkwood, moss and pools.
        byte stalk = Tables.Mi("stalkwood");
        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float c = Noise.Fbm2(x / 18f, y / 18f, s + 21, 3);
                if (c < 0.56f) continue;
                int floorZ = cavernZ + (int)MathF.Round((Noise.Value2(x / 25f, y / 25f, s + 22) - 0.5f) * 3);
                int tall = c > 0.64f ? 3 : 2;
                if (floorZ + tall >= height[y * w + x] - 4) continue;
                for (int k = 0; k < tall; k++)
                {
                    int i = W.Idx(x, y, floorZ + k);
                    W.Shapes[i] = k == 0 ? Shape.Floor : Shape.Empty;
                }
                int fi = W.Idx(x, y, floorZ);
                W.Mats[fi] = W.Mats[fi - w * h];
                float r = Noise.Hash01(x, y, 3, s);
                // Pools lie in a basin one level down, so they stay put until disturbed.
                if (c > 0.7f && Noise.Value2(x / 7f, y / 7f, s + 23) > 0.72f && floorZ > 1) { W.Shapes[fi] = Shape.Empty; W.Shapes[fi - w * h] = Shape.Water; }
                else if (r < 0.06f) { W.Shapes[fi] = Shape.Tree; W.Mats[fi] = stalk; }
                else if (r < 0.12f) W.Set(fi, CellFlag.Shrub, true);
            }

        // Magma pockets just above the floor of the world.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (Noise.Fbm2(x / 14f, y / 14f, s + 31, 2) > 0.58f)
                    for (int z = 1; z <= 2; z++) W.Shapes[W.Idx(x, y, z)] = Shape.Magma;

        // Ponds in the low ground.
        for (int p = 0; p < 14; p++)
        {
            int cx = rng.Range(10, w - 10), cy = rng.Range(10, h - 10);
            // Roll downhill a little.
            for (int step = 0; step < 40; step++)
            {
                int bx = cx, by = cy, bh = height[cy * w + cx];
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int nx = Math.Clamp(cx + dx, 4, w - 5), ny = Math.Clamp(cy + dy, 4, h - 5);
                        if (height[ny * w + nx] < bh) { bh = height[ny * w + nx]; bx = nx; by = ny; }
                    }
                if (bx == cx && by == cy) break;
                cx = bx; cy = by;
            }
            int level = height[cy * w + cx];
            int rad = rng.Range(3, 6);
            for (int dy = -rad; dy <= rad; dy++)
                for (int dx = -rad; dx <= rad; dx++)
                {
                    if (dx * dx + dy * dy > rad * rad + rng.Int(3)) continue;
                    int x = cx + dx, y = cy + dy;
                    if (!W.In(x, y, level) || height[y * w + x] != level) continue;
                    W.Shapes[W.Idx(x, y, level)] = Shape.Empty;
                    W.Shapes[W.Idx(x, y, level - 1)] = Shape.Water;
                }
        }

        // Ramps where the ground rises by one beside.
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int g = height[y * w + x];
                int i = W.Idx(x, y, g);
                if (W.Shapes[i] != Shape.Floor) continue;
                bool up = false;
                for (int dy = -1; dy <= 1 && !up; dy++)
                    for (int dx = -1; dx <= 1 && !up; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx < (uint)w && (uint)ny < (uint)h && height[ny * w + nx] == g + 1 && W.Shapes[W.Idx(nx, ny, g + 1)] == Shape.Floor) up = true;
                    }
                if (up) W.Shapes[i] = Shape.Ramp;
            }

        // Embark: the flattest dry spot near the middle of the west half.
        P3 embark = FindEmbark(W, height, rng);

        // Woods and shrubs, kept off the embark clearing.
        byte oak = Tables.Mi("oak"), pine = Tables.Mi("pine");
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int g = height[y * w + x];
                int i = W.Idx(x, y, g);
                bool soil = Tables.Mats[W.Mats[i - w * h]].Kind == MatKind.Soil;
                if (soil && W.Shapes[i] is Shape.Floor or Shape.Ramp) W.Set(i, CellFlag.Grass, true);
                if (W.Shapes[i] != Shape.Floor) continue;
                if (Math.Abs(x - embark.X) < 7 && Math.Abs(y - embark.Y) < 7) continue;
                float wood = Noise.Fbm2(x / 22f, y / 22f, s + 41, 3);
                float r = Noise.Hash01(x, y, 5, s);
                if (soil && wood > 0.55f && r < (wood - 0.5f) * 1.6f) { W.Shapes[i] = Shape.Tree; W.Mats[i] = g > baseZ + 2 ? pine : oak; }
                else if (soil && r > 0.965f) W.Set(i, CellFlag.Shrub, true);
            }

        // Sky and fog: open columns from the top are outside; everything else starts hidden.
        for (int i = 0; i < W.N; i++) W.Flags[i] |= CellFlag.Hidden;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int z = d - 1; z >= 0; z--)
                {
                    int i = W.Idx(x, y, z);
                    W.Flags[i] |= CellFlag.Outside;
                    if (W.Shapes[i] != Shape.Empty) break;
                }
        for (int i = 0; i < W.N; i++)
            if (W.Has(i, CellFlag.Outside))
            {
                P3 p = W.Pos(i);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                            if (W.In(p.X + dx, p.Y + dy, p.Z + dz)) W.Flags[W.Idx(p.X + dx, p.Y + dy, p.Z + dz)] &= ~CellFlag.Hidden;
            }

        W.ConvertLiquids();
        W.SurfaceZ = embark.Z;
        W.RebuildComponents();
        W.Changed.Clear();
        return new Result { World = W, Embark = embark, Height = height, CavernZ = cavernZ };
    }

    private static P3 FindEmbark(World W, int[] height, Rng rng)
    {
        int w = W.W, h = W.H;
        P3 best = new(w / 3, h / 2, height[(h / 2) * w + w / 3]);
        int bestScore = int.MaxValue;
        for (int t = 0; t < 400; t++)
        {
            int x = rng.Range(w / 5, w / 2), y = rng.Range(h / 4, 3 * h / 4);
            int g = height[y * w + x];
            int score = 0;
            for (int dy = -5; dy <= 5; dy++)
                for (int dx = -5; dx <= 5; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!W.In(nx, ny, 0)) { score += 100; continue; }
                    int gg = height[ny * w + nx];
                    score += Math.Abs(gg - g) * 10;
                    if (W.Shapes[W.Idx(nx, ny, gg)] == Shape.Empty) score += 50;
                }
            // Near water is good, near the mountain is good.
            score += Math.Abs(x - w / 2) / 4;
            if (score < bestScore) { bestScore = score; best = new P3(x, y, g); }
        }
        return best;
    }
}
