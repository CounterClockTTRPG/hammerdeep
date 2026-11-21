using System;
using System.Collections.Generic;

namespace Hammerdeep;

/// <summary>
/// Shortest walks over the cell graph (<see cref="World.Neighbors"/>): A* to
/// one cell, or uniform-cost search to the nearest cell that satisfies a test.
/// The buffers are sized to the map once and reused with a generation stamp,
/// so a search costs only what it visits.
/// </summary>
public sealed class Pathing
{
    private readonly World _w;
    private readonly int[] _g, _parent, _stamp;
    private int _gen;
    private int[] _heapNode = new int[1024], _heapKey = new int[1024];
    private int _heapN;
    public int Expanded;
    public long TotalExpanded;
    public int Searches;

    public Pathing(World w)
    {
        _w = w;
        _g = new int[w.N]; _parent = new int[w.N]; _stamp = new int[w.N];
    }

    private void Push(int node, int key)
    {
        if (_heapN == _heapNode.Length) { Array.Resize(ref _heapNode, _heapN * 2); Array.Resize(ref _heapKey, _heapN * 2); }
        int i = _heapN++;
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (_heapKey[p] <= key) break;
            _heapNode[i] = _heapNode[p]; _heapKey[i] = _heapKey[p];
            i = p;
        }
        _heapNode[i] = node; _heapKey[i] = key;
    }

    private int Pop(out int key)
    {
        int top = _heapNode[0]; key = _heapKey[0];
        int lastN = _heapNode[--_heapN], lastK = _heapKey[_heapN];
        int i = 0;
        while (true)
        {
            int c = 2 * i + 1;
            if (c >= _heapN) break;
            if (c + 1 < _heapN && _heapKey[c + 1] < _heapKey[c]) c++;
            if (_heapKey[c] >= lastK) break;
            _heapNode[i] = _heapNode[c]; _heapKey[i] = _heapKey[c];
            i = c;
        }
        if (_heapN > 0) { _heapNode[i] = lastN; _heapKey[i] = lastK; }
        return top;
    }

    private int H(int a, int b)
    {
        int W = _w.W, HH = _w.H;
        int ax = a % W, at = a / W, ay = at % HH, az = at / HH;
        int bx = b % W, bt = b / W, by = bt % HH, bz = bt / HH;
        int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by), dz = Math.Abs(az - bz);
        int lo = Math.Min(dx, dy), hi = Math.Max(dx, dy);
        // With a lift about, a level may cost as little as a ride: the estimate must never be more than the truth.
        return lo * Tables.DiagTicks + (hi - lo) * Tables.StepTicks + dz * (_w.Lifts.Count > 0 ? Tables.LiftTicks : Tables.StairTicks);
    }

    private void Begin()
    {
        _gen++;
        if (_gen == int.MaxValue) { Array.Clear(_stamp); _gen = 1; }
        _heapN = 0;
        Expanded = 0;
        Searches++;
    }

    private List<int> Build(int end)
    {
        var path = new List<int>();
        for (int c = end; c != -1; c = _parent[c]) path.Add(c);
        path.Reverse();
        return path;
    }

    /// <summary>The cells from <paramref name="from"/> to <paramref name="to"/>, both included; null when there is no way.</summary>
    public List<int> Find(int from, int to, int maxNodes = 120000, Func<int, bool> blocked = null)
    {
        if (from == to) return new List<int> { from };
        if (!_w.Walkable(to) || !_w.Walkable(from)) return null;
        _w.EnsureComponents();
        if (_w.Comp[from] != _w.Comp[to]) return null;
        Begin();
        Span<int> nb = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        _stamp[from] = _gen; _g[from] = 0; _parent[from] = -1;
        Push(from, H(from, to));
        while (_heapN > 0)
        {
            int c = Pop(out int key);
            if (c == to) { TotalExpanded += Expanded; return Build(c); }
            if (key - H(c, to) > _g[c]) continue;
            if (++Expanded > maxNodes) break;
            int n = _w.Neighbors(c, nb, cost);
            for (int k = 0; k < n; k++)
            {
                int j = nb[k], g = _g[c] + cost[k];
                if (_stamp[j] == _gen && _g[j] <= g) continue;
                if (blocked != null && j != to && blocked(j)) continue;
                _stamp[j] = _gen; _g[j] = g; _parent[j] = c;
                Push(j, g + H(j, to));
            }
        }
        TotalExpanded += Expanded;
        return null;
    }

    /// <summary>The walk to the nearest cell that passes <paramref name="goal"/>, by walking time; null when none is found.</summary>
    public List<int> Nearest(int from, Func<int, bool> goal, int maxNodes = 40000, Func<int, bool> blocked = null)
    {
        if (!_w.Walkable(from)) return null;
        Begin();
        Span<int> nb = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        _stamp[from] = _gen; _g[from] = 0; _parent[from] = -1;
        Push(from, 0);
        while (_heapN > 0)
        {
            int c = Pop(out int key);
            if (key > _g[c]) continue;
            if (goal(c)) { TotalExpanded += Expanded; return Build(c); }
            if (++Expanded > maxNodes) break;
            int n = _w.Neighbors(c, nb, cost);
            for (int k = 0; k < n; k++)
            {
                int j = nb[k], g = _g[c] + cost[k];
                if (_stamp[j] == _gen && _g[j] <= g) continue;
                if (blocked != null && blocked(j)) continue;
                _stamp[j] = _gen; _g[j] = g; _parent[j] = c;
                Push(j, g);
            }
        }
        TotalExpanded += Expanded;
        return null;
    }

    /// <summary>The walk to whichever of <paramref name="targets"/> is reached first.</summary>
    public List<int> FindAny(int from, IReadOnlyCollection<int> targets, int maxNodes = 120000)
    {
        if (targets.Count == 0) return null;
        if (targets.Count == 1) { foreach (int t in targets) return Find(from, t, maxNodes); }
        var set = targets as HashSet<int> ?? new HashSet<int>(targets);
        return Nearest(from, set.Contains, maxNodes);
    }

    /// <summary>Walking time of a path, in ticks.</summary>
    public int Cost(List<int> path)
    {
        int t = 0;
        for (int i = 1; i < path.Count; i++) t += StepCost(path[i - 1], path[i]);
        return t;
    }

    public int StepCost(int a, int b)
    {
        P3 pa = _w.Pos(a), pb = _w.Pos(b);
        if (pa.Z != pb.Z) return pa.X == pb.X && pa.Y == pb.Y ? (_w.Lifts.Count > 0 && _w.Lifts.Contains(a) && _w.Lifts.Contains(b) ? Tables.LiftTicks : Tables.StairTicks) : Tables.DiagTicks + 2;
        return pa.X != pb.X && pa.Y != pb.Y ? Tables.DiagTicks : Tables.StepTicks;
    }

    /// <summary>Can <paramref name="a"/> step straight to <paramref name="b"/>, now?</summary>
    public bool CanStep(int a, int b)
    {
        if (!_w.Walkable(b) || !_w.Walkable(a)) return false;
        Span<int> nb = stackalloc int[24];
        Span<int> cost = stackalloc int[24];
        int n = _w.Neighbors(a, nb, cost);
        for (int k = 0; k < n; k++) if (nb[k] == b) return true;
        return false;
    }
}
