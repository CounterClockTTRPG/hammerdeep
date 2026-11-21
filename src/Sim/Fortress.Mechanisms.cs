using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>A linkage from a lever or pressure plate to what it works, laid by a dwarf with one mechanism.</summary>
public sealed class Link
{
    public int Id, Switch, Target;
    public bool Built;
    /// <summary>The dwarf laying it, or -1.</summary>
    public int Worker = -1;
}

/// <summary>A bolt in flight, for the view to draw (not saved).</summary>
public sealed class Shot
{
    public int From, To;
    public bool Hit;
}

/// <summary>
/// Mechanisms: levers and pressure plates switch what they are linked to -- doors, floodgates, drawbridges, hatches
/// over pits, cage traps, bolt launchers, stonefall traps and lifts. A switch is on or off and every target takes
/// the state: on locks a door, opens a floodgate or a hatch, raises a drawbridge, drops a cage, fires a launcher
/// or a stonefall trap, stops a lift. A lever is pulled by a dwarf who walks to it; a plate is on while something
/// it watches stands on it, and goes off a little after. Each link is a mechanism a dwarf carries to the target.
/// A lift turns a stair shaft into a fast ride between its levels.
/// </summary>
public partial class Fortress
{
    public List<Link> Links = new();
    [JsonIgnore] public readonly List<Shot> Shots = new();

    public static readonly string[] Switches = { "lever", "plate" };
    public static readonly string[] Linkable = { "door", "floodgate", "bridge", "hatch", "trap", "cage", "launcher", "lift" };
    public static readonly string[] PlateWatches = { "foes", "dwarves", "anyone" };

    /// <summary>How long a plate stays on after it was last stepped on.</summary>
    public const int PlateHold = 80;
    /// <summary>A launcher's reload, and how far its bolts fly.</summary>
    public const int LauncherReload = 200, LauncherRange = 14;

    public static bool IsSwitch(Building b) => Switches.Contains(b.Def);
    public static bool IsLinkable(Building b) => Linkable.Contains(b.Def);

    /// <summary>What a switch does to a target when it goes on, in a few words (for the panels).</summary>
    public static string OnMeans(string def) => def switch
    {
        "door" => "on: locked shut",
        "floodgate" => "on: open",
        "bridge" => "on: raised",
        "hatch" => "on: open, the floor drops away",
        "trap" => "on: drops its stone",
        "cage" => "on: drops the cage",
        "launcher" => "on: fires a volley",
        "lift" => "on: stopped",
        _ => "",
    };

    // ---- links --------------------------------------------------------------------------

    /// <summary>Why a switch cannot be linked to a target, or null; then <see cref="AddLink"/> plans the linkage.</summary>
    public string CannotLink(Building sw, Building target)
    {
        if (!IsSwitch(sw)) return "only a lever or a pressure plate can be linked";
        if (!IsLinkable(target)) return $"a {target.D.Name.ToLower()} cannot be worked by a mechanism";
        if (Links.Any(l => l.Switch == sw.Id && l.Target == target.Id)) return "they are linked already";
        return null;
    }

    public Link AddLink(Building sw, Building target)
    {
        if (CannotLink(sw, target) != null) return null;
        var l = new Link { Id = NextId++, Switch = sw.Id, Target = target.Id };
        Links.Add(l);
        return l;
    }

    /// <summary>Takes a linkage out; a laid one gives its mechanism back.</summary>
    public void RemoveLink(Link l)
    {
        if (l.Worker >= 0 && DwarfById(l.Worker) is Dwarf d) { Release(d); d.Task = null; }
        Links.Remove(l);
        if (l.Built && BuildingById(l.Target) is Building t) Spawn(ItemKind.Mechanism, Tables.Mi("iron"), FindStandable(new P3(t.X, t.Y, t.Z), 2));
    }

    private Task PlanLink(Dwarf d)
    {
        Link l = Links.FirstOrDefault(x => !x.Built && x.Worker < 0 && BuildingById(x.Target) is { Built: true } && BuildingById(x.Switch) is { Built: true });
        if (l == null) return null;
        Item mech = NearestFree(d, it => it.Kind == ItemKind.Mechanism);
        if (mech == null) return null;
        mech.ReservedBy = d.Id;
        l.Worker = d.Id;
        return new Task { Kind = TaskKind.Link, Item = mech.Id, Building = l.Target, Order = l.Id };
    }

    private bool DoLink(Dwarf d, Task t)
    {
        Link l = Links.FirstOrDefault(x => x.Id == t.Order);
        Building target = BuildingById(t.Building);
        if (l == null || target == null) return false;
        d.Activity = "Linking a mechanism";
        if (t.Stage == 0)
        {
            int r = Fetch(d, t.Item);
            if (r < 0) return false;
            if (r == 0) return true;
            d.Carry = t.Item;
            t.Stage = 1;
        }
        if (t.Stage == 1)
        {
            var spots = BuildSpots(target);
            int w = spots.Count == 0 ? -1 : WalkAny(d, spots);
            if (w < 0) return false;
            if (w == 0) return true;
            t.Stage = 2;
            t.Total = 300;
        }
        t.Work += (int)(10 * d.Speed(Labor.Construction));
        if (t.Work < t.Total) return true;
        if (Items.TryGetValue(t.Item, out Item mech)) Destroy(mech);
        d.Carry = -1;
        t.Item = -1;
        l.Built = true;
        l.Worker = -1;
        Gain(d, Labor.Construction, 15);
        Count("links");
        // A new link takes the switch's state at once.
        if (BuildingById(l.Switch) is { On: true }) Apply(target, true);
        return false;
    }

    // ---- switching ----------------------------------------------------------------------

    /// <summary>Turns a switch on or off; everything linked to it follows.</summary>
    public void Signal(Building sw, bool on)
    {
        if (sw.On == on) return;
        sw.On = on;
        World.Changed.Add(World.Idx(sw.X, sw.Y, sw.Z));
        foreach (Link l in Links.Where(l => l.Switch == sw.Id && l.Built).ToList())
            if (BuildingById(l.Target) is { Built: true } t) Apply(t, on);
    }

    private void Apply(Building t, bool on)
    {
        switch (t.Def)
        {
            case "door": SetLock(t, on); break;
            case "floodgate": SetGate(t, on); break;
            case "bridge" or "hatch": SetBridge(t, on); break;
            case "trap": if (on) FireStonefall(t); break;
            case "cage": if (on) DropCage(t); break;
            case "launcher": if (on) Fire(t); break;
            case "lift": SetLiftStopped(t, on); break;
        }
        World.Changed.Add(World.Idx(t.X, t.Y, t.Z));
    }

    /// <summary>Asks for a lever to be pulled: the nearest free dwarf walks over and pulls it.</summary>
    public void WantPull(Building lever) { if (lever.Def == "lever" && lever.Built) lever.PullWanted = true; }

    private Task PlanPull(Dwarf d)
    {
        Building lever = Buildings.Where(b => b.PullWanted && b.Claim < 0 && b.Built)
            .OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(World.Pos(d.Cell))).FirstOrDefault();
        if (lever == null) return null;
        World.EnsureComponents();
        var spots = BuildSpots(lever);
        if (!spots.Any(s => World.Comp[s] == World.Comp[d.Cell])) return null;
        lever.Claim = d.Id;
        return new Task { Kind = TaskKind.Pull, Building = lever.Id };
    }

    private bool DoPull(Dwarf d, Task t)
    {
        Building lever = BuildingById(t.Building);
        if (lever == null || !lever.PullWanted) return false;
        d.Activity = "Pulling a lever";
        if (t.Stage == 0)
        {
            var spots = BuildSpots(lever);
            int w = spots.Count == 0 ? -1 : WalkAny(d, spots);
            if (w < 0) return false;
            if (w == 0) return true;
            t.Stage = 1;
        }
        if (++t.Work < 20) return true;
        lever.PullWanted = false;
        lever.Claim = -1;
        Signal(lever, !lever.On);
        Count("pulls");
        return false;
    }

    /// <summary>Pressure plates: on while something they watch stands on them, off a little after.</summary>
    internal void StepPlates()
    {
        if (Tick % 4 != 0) return;
        foreach (Building p in Buildings)
        {
            if (p.Def != "plate" || !p.Built) continue;
            int c = World.Idx(p.X, p.Y, p.Z);
            bool foes = p.Watch != 1 && Creatures.Any(x => x.Cell == c && !x.Dead && !x.Tame);
            bool dwarves = p.Watch != 0 && Living.Any(x => x.Cell == c);
            if (foes || dwarves)
            {
                p.OffAt = Tick + PlateHold;
                if (!p.On) { Signal(p, true); Count("plates"); }
            }
            else if (p.On && Tick >= p.OffAt) Signal(p, false);
        }
    }

    // ---- what a signal works ------------------------------------------------------------

    private void FireStonefall(Building trap)
    {
        Creature c = Creatures.FirstOrDefault(x => x.Cell == World.Idx(trap.X, trap.Y, trap.Z) && !x.Dead && !x.Tame);
        if (c == null) return;
        trap.ArmedAt = 0;
        CheckTrap(c);
    }

    /// <summary>A cage trap drops: whatever wild or hostile stands on it is caught, and held until let go or slain.</summary>
    public void DropCage(Building cage)
    {
        if (cage.Caught != null) return;
        int cell = World.Idx(cage.X, cage.Y, cage.Z);
        Creature c = Creatures.FirstOrDefault(x => x.Cell == cell && !x.Dead && !x.Tame);
        if (c == null) return;
        cage.Caught = c.Def;
        cage.CaughtRaider = c.Raider;
        c.Dead = true;
        c.Path = null;
        CStepping.Remove(c.Id);
        Count("caged");
        Say($"A cage trap drops over {A(c.D.Name)}!", LogKind.Good, cell);
        if (c.Raider && !Creatures.Any(x => !x.Dead && x.Raider))
        {
            Say("The raid is broken! The survivors flee into the hills.", LogKind.Good, cell);
            foreach (Dwarf d in Living) AddThought(d, "raid_won");
            Count("raids_beaten");
        }
    }

    /// <summary>What to do with a caged creature: slay it, or let it out (a wild beast wanders off).</summary>
    public void EmptyCage(Building cage, bool slay)
    {
        if (cage.Caught == null) return;
        int cell = World.Idx(cage.X, cage.Y, cage.Z);
        string def = cage.Caught;
        cage.Caught = null;
        World.Changed.Add(cell);
        if (slay)
        {
            Say($"The caged {Tables.Creatures[def].Name} is put to death.", LogKind.Info, cell);
            Count("kills");
            return;
        }
        Creature c = SpawnCreature(def, cell, cage.CaughtRaider);
        if (c != null && c.Raider) c.Leave = Tick;
        Say($"The caged {Tables.Creatures[def].Name} is let go.", LogKind.Info, cell);
    }

    private static readonly int[] FaceX = { 0, 1, 0, -1 }, FaceY = { -1, 0, 1, 0 };
    public static readonly string[] FaceNames = { "north", "east", "south", "west" };

    /// <summary>A launcher faces down the longest open run beside it.</summary>
    private void AimLauncher(Building b)
    {
        int best = 0, bestRun = -1;
        for (int f = 0; f < 4; f++)
        {
            int run = Line(b, f).Count;
            if (run > bestRun) { bestRun = run; best = f; }
        }
        b.Facing = best;
    }

    /// <summary>The open cells a launcher's bolts fly through, nearest first.</summary>
    public List<int> Line(Building b, int facing)
    {
        var cells = new List<int>();
        for (int k = 1; k <= LauncherRange; k++)
        {
            int x = b.X + FaceX[facing] * k, y = b.Y + FaceY[facing] * k;
            if (!World.In(x, y, b.Z)) break;
            int c = World.Idx(x, y, b.Z);
            if (!World.Walkable(c) || World.Locked.Contains(c)) break;
            cells.Add(c);
        }
        return cells;
    }

    /// <summary>A volley down the line: three bolts at the first foe in it. Dwarves know to duck.</summary>
    public void Fire(Building b)
    {
        if (Tick < b.ArmedAt) return;
        b.ArmedAt = Tick + LauncherReload;
        List<int> line = Line(b, b.Facing);
        int from = World.Idx(b.X, b.Y, b.Z);
        Count("volleys");
        foreach (int c in line)
        {
            Creature foe = Creatures.FirstOrDefault(x => x.Cell == c && !x.Dead && !x.Tame);
            if (foe == null) continue;
            for (int k = 0; k < 3 && !foe.Dead; k++)
            {
                if (!Rng.Chance(0.75)) continue;
                foe.Hp -= 22 + Rng.Int(22);
                Count("bolts_hit");
                if (foe.Hp <= 0) { Say($"Bolts from a launcher cut down {A(foe.D.Name)}!", LogKind.Good, c); KillCreature(foe, null, null); }
            }
            if (!foe.Dead) Say($"A launcher's bolts strike {A(foe.D.Name)}.", LogKind.Info, c);
            Shots.Add(new Shot { From = from, To = c, Hit = true });
            if (Shots.Count > 20) Shots.RemoveAt(0);
            return;
        }
        if (line.Count > 0) Shots.Add(new Shot { From = from, To = line[^1] });
        if (Shots.Count > 20) Shots.RemoveAt(0);
    }

    // ---- lifts ----------------------------------------------------------------------------

    /// <summary>The stair shaft a lift placed at a cell would run: its bottom and top levels.</summary>
    public (int bottom, int top) LiftShaft(int x, int y, int z)
    {
        static bool Stair(Shape s) => s is Shape.StairUp or Shape.StairDown or Shape.StairUD;
        if (!World.In(x, y, z) || !Stair(World.Shapes[World.Idx(x, y, z)])) return (z, z - 1);
        int bottom = z, top = z;
        while (bottom > 0 && World.Shapes[World.Idx(x, y, bottom)] is Shape.StairDown or Shape.StairUD && World.Shapes[World.Idx(x, y, bottom - 1)] is Shape.StairUp or Shape.StairUD) bottom--;
        while (top + 1 < World.D && World.Shapes[World.Idx(x, y, top)] is Shape.StairUp or Shape.StairUD && World.Shapes[World.Idx(x, y, top + 1)] is Shape.StairDown or Shape.StairUD) top++;
        return (bottom, top);
    }

    private IEnumerable<int> LiftCells(Building b)
    {
        for (int z = b.Z; z <= b.Top; z++) yield return World.Idx(b.X, b.Y, z);
    }

    private void OpenLift(Building b, bool open)
    {
        foreach (int c in LiftCells(b))
        {
            if (open) World.Lifts.Add(c); else World.Lifts.Remove(c);
            World.Changed.Add(c);
        }
        World.Topology++;
    }

    /// <summary>A stopped lift carries nobody: the shaft is shut between its levels.</summary>
    public void SetLiftStopped(Building b, bool stopped)
    {
        b.Raised = stopped;
        foreach (int c in LiftCells(b)) { if (stopped) World.LiftStops.Add(c); else World.LiftStops.Remove(c); World.Changed.Add(c); }
        World.Topology++;
        World.CompDirty = true;
    }
}
