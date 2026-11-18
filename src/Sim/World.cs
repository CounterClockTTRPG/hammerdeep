using System;
using System.Collections.Generic;

namespace Hammerdeep;

/// <summary>
/// What occupies a cell. A cell's floor is the top of the cell beneath it, so
/// digging a wall leaves a floor, and channelling removes it.
/// </summary>
public enum Shape : byte { Empty, Floor, Wall, StairUp, StairDown, StairUD, Ramp, Tree, Water, Magma }

[Flags]
public enum CellFlag : byte { None = 0, Hidden = 1, Built = 2, Shrub = 4, Grass = 8, Smooth = 16, Outside = 32, Wet = 64, Magma = 128 }

public enum Desig : byte { None, Dig, Channel, StairUD, StairDown, StairUp, Chop, Gather, Remove }

/// <summary>
/// The map: W x H x D cells in flat arrays, indexed (z*H + y)*W + x. Knows
/// shapes, materials, flags and designations, which cells a dwarf can stand
/// on and step between, and which walkable cells connect (components).
/// </summary>
public sealed class World
{
    public readonly int W, H, D;
    public readonly int N;
    public readonly Shape[] Shapes;
    public readonly byte[] Mats;
    public readonly CellFlag[] Flags;
    public readonly Desig[] Desigs;
    /// <summary>Liquid in each cell, 0..7 (7 full); the Magma flag says which. Four or more cannot be walked.</summary>
    public readonly byte[] Depth;
    /// <summary>Cells whose liquid may move: the fluid pass looks only at these.</summary>
    public readonly HashSet<int> Active = new();
    public const int Deep = 4;
    /// <summary>Connected-walkable component of each cell (0 = not walkable). Recomputed lazily.</summary>
    public readonly int[] Comp;
    public bool CompDirty = true;
    /// <summary>Changes since the last look, for the view to repaint: cell indices.</summary>
    public readonly HashSet<int> Changed = new();
    /// <summary>Bumped on any change to walkability.</summary>
    public int Topology;
    public int SurfaceZ;
    /// <summary>Cells nobody may pass: locked doors.</summary>
    public readonly HashSet<int> Locked = new();

    public World(int w, int h, int d)
    {
        W = w; H = h; D = d; N = w * h * d;
        Shapes = new Shape[N]; Mats = new byte[N]; Flags = new CellFlag[N]; Desigs = new Desig[N]; Comp = new int[N]; Depth = new byte[N];
    }

    public int Idx(int x, int y, int z) => (z * H + y) * W + x;
    public int Idx(P3 p) => (p.Z * H + p.Y) * W + p.X;
    public P3 Pos(int i) { int x = i % W, t = i / W; return new P3(x, t % H, t / H); }
    public bool In(int x, int y, int z) => (uint)x < (uint)W && (uint)y < (uint)H && (uint)z < (uint)D;
    public bool In(P3 p) => In(p.X, p.Y, p.Z);

    public Shape S(int x, int y, int z) => In(x, y, z) ? Shapes[Idx(x, y, z)] : Shape.Wall;
    public bool Has(int i, CellFlag f) => (Flags[i] & f) != 0;
    public void Set(int i, CellFlag f, bool on) { if (on) Flags[i] |= f; else Flags[i] &= ~f; }

    public static bool IsWalkable(Shape s) => s is Shape.Floor or Shape.StairUp or Shape.StairDown or Shape.StairUD or Shape.Ramp;
    public static bool IsSolid(Shape s) => s is Shape.Wall or Shape.Tree;
    public bool Walkable(int i) => IsWalkable(Shapes[i]) && Depth[i] < Deep;
    public bool Walkable(P3 p) => In(p) && Walkable(Idx(p));
    public bool IsMagma(int i) => (Flags[i] & CellFlag.Magma) != 0;
    public bool WaterAt(int i) => Depth[i] > 0 && !IsMagma(i);

    /// <summary>The ground a liquid cell stands on: a floor over solid rock, open air otherwise.</summary>
    public Shape BaseShape(int i) => i - W * H >= 0 && IsSolid(Shapes[i - W * H]) ? Shape.Floor : Shape.Empty;

    /// <summary>Turn the old whole-cell Water and Magma shapes into liquid depth (generation, old saves).</summary>
    public void ConvertLiquids()
    {
        for (int i = 0; i < N; i++)
        {
            if (Shapes[i] is not (Shape.Water or Shape.Magma)) continue;
            bool magma = Shapes[i] == Shape.Magma;
            Depth[i] = 7;
            Set(i, CellFlag.Magma, magma);
        }
        for (int i = 0; i < N; i++) if (Shapes[i] is Shape.Water or Shape.Magma) Shapes[i] = BaseShape(i);
    }

    /// <summary>Wake the liquid around a cell that changed.</summary>
    public void Stir(int i)
    {
        P3 p = Pos(i);
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!In(p.X + dx, p.Y + dy, p.Z + dz)) continue;
                    int j = Idx(p.X + dx, p.Y + dy, p.Z + dz);
                    if (Depth[j] > 0) Active.Add(j);
                }
    }

    private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    /// <summary>
    /// The cells a dwarf can step to from <paramref name="i"/> (which must be walkable),
    /// and the ticks each step takes. Symmetric, so components are well defined.
    /// </summary>
    public int Neighbors(int i, Span<int> to, Span<int> cost)
    {
        int n = 0;
        int x = i % W, t = i / W, y = t % H, z = t / H;
        Shape here = Shapes[i];
        for (int k = 0; k < 8; k++)
        {
            int nx = x + DX[k], ny = y + DY[k];
            if ((uint)nx >= (uint)W || (uint)ny >= (uint)H) continue;
            int j = i + DX[k] + DY[k] * W;
            if (Locked.Count > 0 && Locked.Contains(j)) continue;
            if (IsWalkable(Shapes[j]) && Depth[j] < Deep)
            {
                if (k >= 4 && (IsSolid(Shapes[i + DX[k]]) || IsSolid(Shapes[i + DY[k] * W]))) { }
                // Wading is slow.
                else { to[n] = j; cost[n++] = (k < 4 ? Tables.StepTicks : Tables.DiagTicks) + Depth[j] * 3; }
            }
            // Up a ramp: from a ramp under open air to a walkable cell beside it one level up.
            if (here == Shape.Ramp && z + 1 < D && Shapes[i + W * H] == Shape.Empty)
            {
                int u = j + W * H;
                if (IsWalkable(Shapes[u]) && Depth[u] < Deep && IsSolid(Shapes[j])) { to[n] = u; cost[n++] = Tables.DiagTicks + 2; }
            }
            // Down onto a ramp beside, below.
            if (z > 0 && Shapes[j] == Shape.Empty)
            {
                int dn = j - W * H;
                if (Shapes[dn] == Shape.Ramp && Depth[dn] < Deep && IsSolid(Shapes[i - W * H])) { to[n] = dn; cost[n++] = Tables.DiagTicks + 2; }
            }
        }
        if (z + 1 < D && here is Shape.StairUp or Shape.StairUD && Shapes[i + W * H] is Shape.StairDown or Shape.StairUD && Depth[i + W * H] < Deep)
        { to[n] = i + W * H; cost[n++] = Tables.StairTicks; }
        if (z > 0 && here is Shape.StairDown or Shape.StairUD && Shapes[i - W * H] is Shape.StairUp or Shape.StairUD && Depth[i - W * H] < Deep)
        { to[n] = i - W * H; cost[n++] = Tables.StairTicks; }
        return n;
    }

    private int _nextComp;
    private readonly Dictionary<int, int> _compSize = new();
    public int Rebuilds;

    /// <summary>Recompute the connected components of walkable cells.</summary>
    public void RebuildComponents()
    {
        Rebuilds++;
        Array.Clear(Comp);
        _compSize.Clear();
        int next = 0;
        var stack = new Stack<int>();
        Span<int> to = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        for (int i = 0; i < N; i++)
        {
            if (Comp[i] != 0 || !Walkable(i)) continue;
            next++;
            Comp[i] = next;
            stack.Push(i);
            int size = 0;
            while (stack.Count > 0)
            {
                int c = stack.Pop();
                size++;
                int k = Neighbors(c, to, cost);
                for (int a = 0; a < k; a++)
                {
                    int j = to[a];
                    if (Comp[j] != 0) continue;
                    Comp[j] = next;
                    stack.Push(j);
                }
            }
            _compSize[next] = size;
        }
        _nextComp = next;
        CompDirty = false;
    }

    /// <summary>
    /// A cell has become walkable, or gained ways out (a stair, a ramp): it can only join
    /// regions together, so merge them in place -- the smaller relabelled into the larger --
    /// instead of rebuilding the whole map.
    /// </summary>
    public void Opened(int i)
    {
        if (CompDirty) return;
        if (!Walkable(i)) { CompDirty = true; return; }
        Span<int> to = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        int n = Neighbors(i, to, cost);
        int target = Comp[i];
        for (int k = 0; k < n; k++)
        {
            int c = Comp[to[k]];
            if (c != 0 && (target == 0 || _compSize.GetValueOrDefault(c) > _compSize.GetValueOrDefault(target))) target = c;
        }
        if (target == 0) { target = ++_nextComp; _compSize[target] = 0; }
        if (Comp[i] != target)
        {
            if (Comp[i] != 0) Relabel(i, Comp[i], target);
            else { Comp[i] = target; _compSize[target] = _compSize.GetValueOrDefault(target) + 1; }
        }
        for (int k = 0; k < n; k++)
        {
            int c = Comp[to[k]];
            if (c != target) Relabel(to[k], c, target);
        }
    }

    private void Relabel(int start, int from, int into)
    {
        var stack = new Stack<int>();
        Span<int> to = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        Comp[start] = into;
        stack.Push(start);
        int moved = 0;
        while (stack.Count > 0)
        {
            int c = stack.Pop();
            moved++;
            int n = Neighbors(c, to, cost);
            for (int k = 0; k < n; k++)
            {
                int j = to[k];
                if (Comp[j] != from || from == 0) { if (from == 0 && Comp[j] == 0 && Walkable(j)) { Comp[j] = into; stack.Push(j); } continue; }
                Comp[j] = into;
                stack.Push(j);
            }
        }
        _compSize.Remove(from);
        _compSize[into] = _compSize.GetValueOrDefault(into) + moved;
    }

    /// <summary>After a change to whether a cell can be walked, or how: merge cheaply when nothing can have been cut apart.</summary>
    public void Rewalk(int i, bool wasWalkable, Shape was)
    {
        bool now = Walkable(i);
        bool opensOnly = now && (!wasWalkable || Shapes[i] is Shape.StairUp or Shape.StairDown or Shape.StairUD or Shape.Ramp || was == Shapes[i]);
        // A solid cell turned to floor no longer props up a ramp beside it, or a ramp's top; play it safe.
        bool rampsNear = false;
        if (opensOnly && !wasWalkable && IsSolid(was))
        {
            P3 p = Pos(i);
            for (int dy = -1; dy <= 1 && !rampsNear; dy++)
                for (int dx = -1; dx <= 1 && !rampsNear; dx++)
                    if (In(p.X + dx, p.Y + dy, p.Z) && Shapes[Idx(p.X + dx, p.Y + dy, p.Z)] == Shape.Ramp) rampsNear = true;
        }
        if (opensOnly && !rampsNear) Opened(i);
        else if (wasWalkable || now) CompDirty = true;
        // A wall dug out above opens the cell below's ramp top, and so on: neighbours above and below can gain links.
        if (opensOnly && !CompDirty)
        {
            if (i + W * H < N && Walkable(i + W * H)) Opened(i + W * H);
            if (i - W * H >= 0 && Walkable(i - W * H)) Opened(i - W * H);
        }
    }

    public void EnsureComponents() { if (CompDirty) RebuildComponents(); }

    public void SetLocked(int i, bool locked)
    {
        if (locked ? !Locked.Add(i) : !Locked.Remove(i)) return;
        if (locked) CompDirty = true;
        else
        {
            // Unlocking only joins: the cell and whatever lies beyond it.
            Opened(i);
            Span<int> to = stackalloc int[24];
            Span<int> cost = stackalloc int[24];
            if (Walkable(i)) { int n = Neighbors(i, to, cost); for (int k = 0; k < n; k++) Opened(to[k]); }
        }
        Topology++;
        Changed.Add(i);
    }

    /// <summary>Change a cell's shape (and optionally material), marking everything that depends on it.</summary>
    public void SetShape(int i, Shape s, int mat = -1)
    {
        Shape was = Shapes[i];
        bool wasWalkable = Walkable(i);
        Shapes[i] = s;
        if (mat >= 0) Mats[i] = (byte)mat;
        Rewalk(i, wasWalkable, was);
        Topology++;
        Changed.Add(i);
        if (i + W * H < N) Changed.Add(i + W * H);
        Stir(i);
    }

    // ---- what is known ----------------------------------------------------------------

    /// <summary>Reveal a cell and the 26 around it; breaching into a hidden open space reveals all of it.</summary>
    public int RevealAround(int i)
    {
        int opened = 0;
        P3 p = Pos(i);
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
                    if (!In(x, y, z)) continue;
                    int j = Idx(x, y, z);
                    if (!Has(j, CellFlag.Hidden)) continue;
                    if (!IsSolid(Shapes[j]) && Shapes[j] != Shape.Water && Shapes[j] != Shape.Magma) opened += FloodReveal(j);
                    else { Set(j, CellFlag.Hidden, false); Changed.Add(j); }
                }
        return opened;
    }

    /// <summary>Reveal a hidden open region (a cavern) and its walls. Returns cells revealed.</summary>
    public int FloodReveal(int start)
    {
        int count = 0;
        var q = new Queue<int>();
        Set(start, CellFlag.Hidden, false);
        q.Enqueue(start);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            Changed.Add(c);
            count++;
            P3 p = Pos(c);
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = p.X + dx, y = p.Y + dy, z = p.Z + dz;
                        if (!In(x, y, z)) continue;
                        int j = Idx(x, y, z);
                        if (!Has(j, CellFlag.Hidden)) continue;
                        Set(j, CellFlag.Hidden, false);
                        if (!IsSolid(Shapes[j])) q.Enqueue(j);
                        else Changed.Add(j);
                    }
        }
        return count;
    }

    // ---- designations -----------------------------------------------------------------

    /// <summary>Whether <paramref name="d"/> makes sense on cell <paramref name="i"/>.</summary>
    public bool CanDesignate(int i, Desig d)
    {
        Shape s = Shapes[i];
        bool built = Has(i, CellFlag.Built);
        return d switch
        {
            Desig.None => true,
            Desig.Dig => s == Shape.Wall && !built,
            // Stairs can be cut into rock or into a floor that is already dug.
            Desig.StairUD or Desig.StairUp or Desig.StairDown => (s == Shape.Wall && !built) || (s == Shape.Floor && !built),
            Desig.Channel => s is Shape.Floor or Shape.Wall && !built && Pos(i).Z > 0,
            Desig.Chop => s == Shape.Tree,
            Desig.Gather => s == Shape.Floor && Has(i, CellFlag.Shrub),
            Desig.Remove => built && s is Shape.Wall or Shape.Floor or Shape.StairUD,
            _ => false,
        };
    }

    /// <summary>Cells a worker can stand on to work designation at <paramref name="i"/>.</summary>
    public int WorkSpots(int i, Span<int> spots)
    {
        int n = 0;
        P3 p = Pos(i);
        Desig d = Desigs[i];
        for (int k = 0; k < 8; k++)
        {
            int x = p.X + DX[k], y = p.Y + DY[k];
            if (!In(x, y, p.Z)) continue;
            int j = Idx(x, y, p.Z);
            if (IsWalkable(Shapes[j])) spots[n++] = j;
        }
        // Stairs dug down a shaft: from the stair above or below.
        if (d is Desig.StairUD or Desig.StairUp or Desig.Dig && p.Z + 1 < D && Shapes[i + W * H] is Shape.StairDown or Shape.StairUD) spots[n++] = i + W * H;
        if (d is Desig.StairUD or Desig.StairDown or Desig.Dig && p.Z > 0 && Shapes[i - W * H] is Shape.StairUp or Shape.StairUD) spots[n++] = i - W * H;
        // Floor-ish targets can be worked standing on them.
        if (d is Desig.StairDown or Desig.StairUp or Desig.StairUD or Desig.Gather && Walkable(i)) spots[n++] = i;
        return n;
    }

    public bool IsOutside(int i) => Has(i, CellFlag.Outside);
}
