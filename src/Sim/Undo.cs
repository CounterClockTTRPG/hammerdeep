using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// Taking back what the player just asked for: designations (and their
/// clearing), zones laid out, buildings placed but not yet built. Each
/// action is remembered as what it changed; undoing puts that back. Kept for
/// the session only.
/// </summary>
public sealed class UndoStack
{
    private sealed class Step
    {
        public string What;
        public List<(int cell, Desig before)> Cells;
        public int Building = -1, Zone = -1;
    }

    private readonly List<Step> _steps = new();
    public const int Depth = 50;

    public int Count => _steps.Count;
    public string Next => _steps.Count > 0 ? _steps[^1].What : null;

    private void Push(Step s)
    {
        _steps.Add(s);
        if (_steps.Count > Depth) _steps.RemoveAt(0);
    }

    /// <summary>Designate a box (or clear it, with None), remembering what each cell had. Returns cells changed.</summary>
    public int Designate(Fortress f, P3 a, P3 b, Desig g)
    {
        World w = f.World;
        var before = new List<(int, Desig)>();
        for (int z = Math.Min(a.Z, b.Z); z <= Math.Max(a.Z, b.Z); z++)
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    if (w.In(x, y, z)) before.Add((w.Idx(x, y, z), w.Desigs[w.Idx(x, y, z)]));
        int n = f.Designate(a, b, g);
        var changed = before.Where(p => w.Desigs[p.Item1] != p.Item2).ToList();
        if (changed.Count > 0) Push(new Step { What = g == Desig.None ? $"clearing {changed.Count} designations" : $"{Name(g)} ({changed.Count} cells)", Cells = changed });
        return n;
    }

    public void Placed(Building b) => Push(new Step { What = $"placing the {b.D.Name.ToLower()}", Building = b.Id });
    public void Zoned(Zone z) => Push(new Step { What = $"laying out {z.Name.ToLower()}", Zone = z.Id });

    /// <summary>Undo the last action that can still be undone. Returns what was undone, or null.</summary>
    public string Undo(Fortress f)
    {
        while (_steps.Count > 0)
        {
            Step s = _steps[^1];
            _steps.RemoveAt(_steps.Count - 1);
            if (s.Cells != null)
            {
                World w = f.World;
                foreach (var (cell, before) in s.Cells)
                {
                    P3 p = w.Pos(cell);
                    // Only cells not already dug or changed since: the rock is still there to undo.
                    if (before == Desig.None) f.Designate(p, p, Desig.None);
                    else if (w.CanDesignate(cell, before)) f.Designate(p, p, before);
                }
                return s.What;
            }
            if (s.Building >= 0)
            {
                Building b = f.BuildingById(s.Building);
                if (b == null || b.Built) continue;
                f.Deconstruct(b);
                return s.What;
            }
            if (s.Zone >= 0)
            {
                Zone z = f.Zones.FirstOrDefault(q => q.Id == s.Zone);
                if (z == null) continue;
                f.RemoveZone(z);
                return s.What;
            }
        }
        return null;
    }

    private static string Name(Desig g) => g switch
    {
        Desig.Dig => "digging",
        Desig.Channel => "channelling",
        Desig.StairUD => "up/down stairs",
        Desig.StairDown => "a down stair",
        Desig.StairUp => "an up stair",
        Desig.Chop => "felling",
        Desig.Gather => "gathering",
        Desig.Pave => "laying floors",
        _ => g.ToString().ToLower(),
    };
}
