using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// Water and magma: liquid falls through open air, spreads over floors until
/// level, is stopped by walls, closed floodgates and doors; water meeting
/// magma hisses into obsidian. The deep drowns, magma burns, puddles under
/// the sky dry up, and water leaves mud where it passed. Wells, floodgates
/// and the magma workshops put it to work.
/// </summary>
public sealed partial class Fortress
{
    public const int FluidEvery = 3, FluidCap = 6000;
    private long _lastFloodSay = -100000, _lastMagmaSay = -100000, _lastSteamSay = -100000;
    private int _movedThisPass, _floodMoves;

    private static readonly int[] FX = { 1, -1, 0, 0 }, FY = { 0, 0, 1, -1 };

    /// <summary>Can liquid enter this cell? Not rock, not a tree, not a closed gate or a door.</summary>
    public bool LiquidOpen(int i) => !World.IsSolid(World.Shapes[i]) && !World.Locked.Contains(i) && !DoorAt(i);

    private void Fluids()
    {
        World w = World;
        if (w.Active.Count > 0)
        {
            _leveled.Clear();
            var batch = w.Active.Take(FluidCap).ToList();
            foreach (int c in batch) w.Active.Remove(c);
            _movedThisPass = 0;
            // Shuffle a little so flows do not always favour one side.
            for (int k = batch.Count - 1; k > 0; k--) { int j = Rng.Int(k + 1); (batch[k], batch[j]) = (batch[j], batch[k]); }
            foreach (int c in batch) Flow(c);
            if (_movedThisPass > 0 && batch.Any(c => w.Depth[c] > 0 && !w.IsMagma(c) && !w.IsOutside(c))) _floodMoves += _movedThisPass;
            if (Tick % Tables.TicksPerDay == 0) _floodMoves = 0;
            if (_floodMoves > 20 && Tick - _lastFloodSay > Tables.TicksPerDay * 2)
            {
                _floodMoves = 0;
                int at = batch.FirstOrDefault(c => w.Depth[c] > 0 && !w.IsMagma(c) && !w.IsOutside(c));
                if (at > 0) { _lastFloodSay = Tick; Say("Water is flooding in!", LogKind.Alert, at); }
            }
        }
        Immersion();
        // What lies on still magma burns too.
        if (Tick % 30 == 0) foreach (int c in ItemsAt.Keys.ToList()) if (World.Depth[c] > 0 && World.IsMagma(c)) Burn(c);
    }

    private void Flow(int c)
    {
        World w = World;
        int d = w.Depth[c];
        if (d == 0) return;
        bool magma = w.IsMagma(c);
        int plane = w.W * w.H;
        bool moved = false;
        // Down first, through open air.
        if (w.Shapes[c] == Shape.Empty && c - plane >= 0)
        {
            int b = c - plane;
            if (LiquidOpen(b))
            {
                if (w.Depth[b] > 0 && w.IsMagma(b) != magma) { Quench(b); Take(c, 1); w.Active.Add(c); return; }
                int room = 7 - w.Depth[b];
                if (room > 0) { int m = Math.Min(room, d); Move(c, b, m, magma); d -= m; moved = true; }
            }
        }
        // Then sideways, towards anything lower.
        if (d > 0)
        {
            int start = Rng.Int(4);
            P3 p = w.Pos(c);
            for (int k = 0; k < 4 && d > 0; k++)
            {
                int dir = (start + k) % 4;
                int nx = p.X + FX[dir], ny = p.Y + FY[dir];
                if (!w.In(nx, ny, p.Z)) continue;
                int n = w.Idx(nx, ny, p.Z);
                if (!LiquidOpen(n)) continue;
                int nd = w.Depth[n];
                if (nd > 0 && w.IsMagma(n) != magma) { Quench(n); Take(c, 1); d--; moved = true; continue; }
                // Over an edge (open air beside, room below) liquid pours even when shallow.
                bool edge = w.Shapes[n] == Shape.Empty && n - plane >= 0 && LiquidOpen(n - plane) && w.Depth[n - plane] < 7;
                if (nd < d - 1 || (edge && nd < d))
                {
                    Move(c, n, 1, magma);
                    d--;
                    moved = true;
                }
            }
        }
        if (moved) { w.Active.Add(c); return; }
        // Steps of one do not flow; a still body is levelled as a whole.
        if (!_leveled.Contains(c) && UnevenBeside(c)) { Level(c); return; }
        // At rest. A lone puddle under the sky dries up in time.
        if (d == 1 && !magma && w.IsOutside(c) && Rng.Chance(0.02)) Take(c, 1);
    }

    private readonly HashSet<int> _leveled = new();

    private bool UnevenBeside(int c)
    {
        World w = World;
        P3 p = w.Pos(c);
        int d = w.Depth[c];
        bool mg = w.IsMagma(c);
        for (int k = 0; k < 4; k++)
        {
            if (!w.In(p.X + FX[k], p.Y + FY[k], p.Z)) continue;
            int n = w.Idx(p.X + FX[k], p.Y + FY[k], p.Z);
            if (w.Depth[n] > 0 && w.IsMagma(n) == mg && Math.Abs(w.Depth[n] - d) == 1 && LiquidOpen(n)) return true;
        }
        return false;
    }

    /// <summary>Share out the liquid of a connected still body on one level evenly (the volume is kept).</summary>
    private void Level(int start)
    {
        World w = World;
        bool mg = w.IsMagma(start);
        var cells = new List<int> { start };
        var seen = new HashSet<int> { start };
        int total = 0;
        for (int q = 0; q < cells.Count && cells.Count < 800; q++)
        {
            int c = cells[q];
            total += w.Depth[c];
            P3 p = w.Pos(c);
            for (int k = 0; k < 4; k++)
            {
                if (!w.In(p.X + FX[k], p.Y + FY[k], p.Z)) continue;
                int n = w.Idx(p.X + FX[k], p.Y + FY[k], p.Z);
                if (seen.Contains(n) || w.Depth[n] == 0 || w.IsMagma(n) != mg || !LiquidOpen(n)) continue;
                seen.Add(n);
                cells.Add(n);
            }
        }
        if (cells.Count >= 800) { foreach (int c in cells) _leveled.Add(c); return; }
        int each = total / cells.Count, extra = total % cells.Count;
        for (int k = 0; k < cells.Count; k++)
        {
            int c = cells[k];
            _leveled.Add(c);
            int want = each + (k < extra ? 1 : 0);
            int before = w.Depth[c];
            if (want == before) continue;
            w.Depth[c] = (byte)want;
            if (want == 0) w.Set(c, CellFlag.Magma, false);
            Touched(c, before);
            if (want > 0) w.Active.Add(c);
        }
    }

    private void Take(int c, int m)
    {
        World w = World;
        int before = w.Depth[c];
        w.Depth[c] = (byte)Math.Max(0, before - m);
        if (w.Depth[c] == 0) w.Set(c, CellFlag.Magma, false);
        Touched(c, before);
    }

    private void Move(int from, int to, int m, bool magma)
    {
        World w = World;
        int fb = w.Depth[from], tb = w.Depth[to];
        w.Depth[from] = (byte)(fb - m);
        if (w.Depth[from] == 0) w.Set(from, CellFlag.Magma, false);
        w.Depth[to] = (byte)Math.Min(7, tb + m);
        w.Set(to, CellFlag.Magma, magma);
        if (!magma && w.Shapes[to] == Shape.Floor) w.Set(to, CellFlag.Wet, true);
        Touched(from, fb);
        Touched(to, tb);
        w.Active.Add(to);
        _movedThisPass++;
        if (magma)
        {
            Burn(to);
            if (!w.IsOutside(to) && w.Pos(to).Z > 3 && Tick - _lastMagmaSay > Tables.TicksPerDay * 2) { _lastMagmaSay = Tick; Say("Magma is flowing into the fortress!", LogKind.Alert, to); }
        }
    }

    /// <summary>After a depth change: repaint, wake the neighbours, and if the cell crossed into or out of the deep, re-route.</summary>
    private void Touched(int c, int before)
    {
        World w = World;
        w.Changed.Add(c);
        if ((before >= World.Deep) != (w.Depth[c] >= World.Deep))
        {
            w.Topology++;
            if (w.Depth[c] >= World.Deep) w.CompDirty = true;
            else w.Opened(c);
        }
        P3 p = w.Pos(c);
        for (int k = 0; k < 4; k++) if (w.In(p.X + FX[k], p.Y + FY[k], p.Z)) { int n = w.Idx(p.X + FX[k], p.Y + FY[k], p.Z); if (w.Depth[n] > 0) w.Active.Add(n); }
        if (c + w.W * w.H < w.N && w.Depth[c + w.W * w.H] > 0) w.Active.Add(c + w.W * w.H);
    }

    /// <summary>Water and magma meet: the cell becomes obsidian.</summary>
    private void Quench(int c)
    {
        World w = World;
        int before = w.Depth[c];
        w.Depth[c] = 0;
        w.Set(c, CellFlag.Magma, false);
        foreach (Item it in ItemsOn(c).ToList()) Drop(it, FindStandable(w.Pos(c), 2));
        w.SetShape(c, Shape.Wall, Tables.Mi("obsidian"));
        w.Set(c, CellFlag.Hidden, false);
        Touched(c, before);
        Count("obsidian");
        if (Tick - _lastSteamSay > Tables.TicksPerDay) { _lastSteamSay = Tick; Say("Steam bursts up where water meets magma, leaving obsidian.", LogKind.Info, c); }
    }

    /// <summary>Magma destroys what can burn: wood, plants, flesh, leather.</summary>
    private void Burn(int c)
    {
        foreach (Item it in ItemsOn(c).ToList())
            if (it.InBuilding < 0 && it.M.Kind is MatKind.Wood or MatKind.Plant or MatKind.Flesh or MatKind.Cloth) Destroy(it);
    }

    /// <summary>Every fluid pass: those caught in deep water struggle out or drown; those in magma burn.</summary>
    private void Immersion()
    {
        World w = World;
        foreach (Dwarf d in Dwarves)
        {
            if (d.Dead || w.Depth[d.Cell] == 0) continue;
            if (w.IsMagma(d.Cell)) { d.Hp -= 25; if (d.Hp <= 0) Kill(d, "burned in magma"); else Escape(d); continue; }
            if (w.Depth[d.Cell] < World.Deep) continue;
            d.Hp -= 6;
            if (d.Hp <= 0) { Kill(d, "drowned"); continue; }
            Escape(d);
        }
        foreach (Creature c in Creatures)
        {
            if (c.Dead || w.Depth[c.Cell] == 0) continue;
            bool burn = w.IsMagma(c.Cell);
            if (!burn && w.Depth[c.Cell] < World.Deep) continue;
            c.Hp -= burn ? 25 : 6;
            if (c.Hp <= 0)
            {
                if (c.D.Hostile || c.Raider) Say($"{A(c.D.Name).ToUpperFirst()} {(burn ? "is consumed by magma" : "has drowned")}.", LogKind.Good, c.Cell);
                KillCreature(c, null, null);
                continue;
            }
            int to = DryCell(c.Cell);
            if (to >= 0) { c.Cell = to; c.Path = null; }
        }
    }

    private void Escape(Dwarf d)
    {
        int to = DryCell(d.Cell);
        if (to < 0) return;
        Release(d);
        d.Task = null;
        d.Cell = to;
        d.Path = null;
    }

    /// <summary>A walkable cell beside, out of the deep and away from magma, if there is one.</summary>
    private int DryCell(int c)
    {
        World w = World;
        P3 p = w.Pos(c);
        for (int dz = 1; dz >= -1; dz--)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!w.In(p.X + dx, p.Y + dy, p.Z + dz)) continue;
                    int j = w.Idx(p.X + dx, p.Y + dy, p.Z + dz);
                    if (j != c && w.Walkable(j) && !w.IsMagma(j)) return j;
                }
        return -1;
    }

    // ---- putting it to work -------------------------------------------------------------

    /// <summary>Water within a few levels straight down from a cell (for a well).</summary>
    public bool WaterBelow(int c)
    {
        World w = World;
        int plane = w.W * w.H;
        for (int k = 1; k <= 5 && c - k * plane >= 0; k++)
        {
            int j = c - k * plane;
            if (w.WaterAt(j) && w.Depth[j] >= 3) return true;
            if (World.IsSolid(w.Shapes[j])) return false;
        }
        return false;
    }

    /// <summary>Magma near enough to a workshop's footprint to heat it.</summary>
    public bool MagmaNear(int x, int y, int z, int wdt, int hgt)
    {
        World w = World;
        for (int dz = -1; dz <= 0; dz++)
            for (int yy = y - 2; yy < y + hgt + 2; yy++)
                for (int xx = x - 2; xx < x + wdt + 2; xx++)
                    if (w.In(xx, yy, z + dz) && w.IsMagma(w.Idx(xx, yy, z + dz)) && w.Depth[w.Idx(xx, yy, z + dz)] >= World.Deep) return true;
        return false;
    }

    public void SetGate(Building gate, bool open)
    {
        gate.Locked = !open;
        int c = World.Idx(gate.X, gate.Y, gate.Z);
        World.SetLocked(c, !open && gate.Built);
        World.Stir(c);
    }
}
