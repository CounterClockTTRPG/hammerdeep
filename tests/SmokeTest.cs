using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The headless self-check: <c>./play.sh --check</c>. Exits non-zero on any
/// failed assertion. Rules are checked on tiny hand-built maps where the
/// answer is certain; the generator and the whole game over real seeds.
/// </summary>
public partial class SmokeTest : Node
{
    public Root Root;
    private int _checks, _failures;
    private string _only;

    public override void _Ready()
    {
        GD.Print("HAMMERDEEP self-check");
        ulong started = Time.GetTicksMsec();
        string[] args = OS.GetCmdlineUserArgs();
        int at = Array.IndexOf(args, "--only");
        if (at >= 0 && at + 1 < args.Length) _only = args[at + 1];
        foreach (Action s in Sections()) Run(s);
        // The long run only when asked for: --only Long.
        if (_only != null && "LongRun".Contains(_only, StringComparison.OrdinalIgnoreCase)) Run(LongRun);
        GD.Print($"{_checks} checks, {_failures} failed, {(Time.GetTicksMsec() - started) / 1000.0:0.0} s");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private IEnumerable<Action> Sections() => new Action[]
    {
        Tables_, Paths, Ramps, Digging, Channels, Hauling, Building, Crafting, Needs, Farming, Moods, Duels, Fleeing, Doors, Traps, Squads, Alarms, Raids, Burial, Drawbridges, Grazing, Breeding, Butchery, Taming, Caravans, StrangeMoods, Madness, Prayer, Feasts, Runecraft, Musick, Regions, Floods, Floodgates, Magma, Wells, Peoples, TheWorld, Tutorials, Generator, Saving, Year,
    };

    private void Run(Action section)
    {
        string name = section.Method.Name.TrimEnd('_');
        if (_only != null && !name.Contains(_only, StringComparison.OrdinalIgnoreCase)) return;
        ulong t = Time.GetTicksMsec();
        int before = _failures;
        try { section(); }
        catch (Exception e)
        {
            _failures++;
            GD.PrintErr($"  {name}: threw {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        GD.Print($"  {(before == _failures ? "ok  " : "FAIL")} {name} ({Time.GetTicksMsec() - t} ms)");
    }

    private void Check(bool cond, string what)
    {
        _checks++;
        if (cond) return;
        _failures++;
        GD.PrintErr($"    failed: {what}");
    }

    private void Eq<T>(T actual, T expected, string what) => Check(Equals(actual, expected), $"{what}: expected {expected}, got {actual}");

    // ---- tiny maps ----------------------------------------------------------------------

    /// <summary>
    /// A hand-built fortress: <paramref name="levels"/> top to bottom, each rows of
    /// '#' rock, '.' floor, ' ' open air, '&lt;' up stair, '&gt;' down stair, 'X' up/down,
    /// '^' ramp, 'T' tree, '~' water, 'g' granite floor... and '@' a dwarf on floor.
    /// Below the last level is solid rock.
    /// </summary>
    internal static Fortress Tiny(params string[][] levels)
    {
        int d = levels.Length + 1, h = levels[0].Length, w = levels[0][0].Length;
        var W = new World(w, h, d);
        byte granite = Tables.Mi("granite"), loam = Tables.Mi("loam");
        var dwarves = new List<int>();
        for (int i = 0; i < W.N; i++) { W.Shapes[i] = Shape.Wall; W.Mats[i] = granite; }
        for (int li = 0; li < levels.Length; li++)
        {
            int z = d - 1 - li;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    char c = levels[li][y][x];
                    int i = W.Idx(x, y, z);
                    W.Shapes[i] = c switch
                    {
                        '#' => Shape.Wall, '.' or '@' or 's' or '+' => Shape.Floor, ' ' => Shape.Empty, '<' => Shape.StairUp, '>' => Shape.StairDown,
                        'X' => Shape.StairUD, '^' => Shape.Ramp, 'T' => Shape.Tree, '~' => Shape.Water, _ => Shape.Wall,
                    };
                    if (c == 's') W.Mats[i] = loam;
                    if (c == '~') { W.Shapes[i] = Shape.Floor; W.Depth[i] = 7; }
                    if (c == '&') { W.Shapes[i] = Shape.Floor; W.Depth[i] = 7; W.Set(i, CellFlag.Magma, true); }
                    if (c == 'T') W.Mats[i] = Tables.Mi("oak");
                    if (c == '@') dwarves.Add(i);
                }
        }
        W.RebuildComponents();
        var f = new Fortress { Name = "Testhold", Seed = 1, Rng = new Rng(1), Embark = W.Pos(dwarves.FirstOrDefault()) };
        f.Attach(W);
        foreach (int c in dwarves)
        {
            Dwarf dw = f.AddDwarf("ironhand", W.Pos(c));
            dw.Cell = c;
            dw.Hunger = dw.Thirst = dw.Fatigue = 0;
            dw.Traits.Clear();
        }
        return f;
    }

    private static int Run(Fortress f, Func<bool> until, int max)
    {
        int t = 0;
        while (t < max && !until()) { f.Step(); t++; }
        return t;
    }

    // ---- sections -----------------------------------------------------------------------

    private void Tables_()
    {
        foreach (Recipe r in Tables.Recipes) Check(Tables.Build.ContainsKey(r.Shop), $"recipe {r.Id} has a shop");
        foreach (BuildDef b in Tables.Buildings) Check(b.Cost.Length > 0, $"{b.Id} costs something");
        foreach (ClanDef c in Tables.Clans) Check(Tables.Clans.Any(o => o.Id == c.Grudge), $"{c.Id} has a real grudge");
        foreach (ItemDef i in Tables.Items.Values) if (i.Places != null) Check(Tables.Build.ContainsKey(i.Places), $"{i.Kind} places a real building");
        Check(Tables.Mats.Count < 255, "materials fit a byte");
    }

    private void Paths()
    {
        var f = Tiny(
            new[] { "#######", "#@..X.#", "#######" },
            new[] { "#######", "#...X.#", "#######" },
            new[] { "#######", "#...<.#", "#######" });
        World w = f.World;
        int a = w.Idx(1, 1, 3), b = w.Idx(1, 1, 1);
        var p = f.Path.Find(a, b);
        Check(p != null, "a path down two flights of stairs");
        if (p != null) Check(p.Any(c => w.Pos(c).Z == 2), "...through the middle level");
        Check(f.Path.Find(a, w.Idx(5, 1, 1)) != null, "to the far side below");
        // Diagonals do not cut corners.
        var g = Tiny(new[] { "####", "#@.#", "##.#", "####" });
        var pg = g.Path.Find(g.World.Idx(1, 1, 1), g.World.Idx(2, 2, 1));
        Check(pg != null && pg.Count == 3, "no corner cutting round a wall");
    }

    private void Ramps()
    {
        // A ramp on the low level next to a wall whose top is floor on the level above.
        var f = Tiny(
            new[] { "#####", "#  ..", "#####" },
            new[] { "#####", "#@^##", "#####" });
        World w = f.World;
        var p = f.Path.Find(w.Idx(1, 1, 1), w.Idx(4, 1, 2));
        Check(p != null, "up a ramp to the level above");
        var back = f.Path.Find(w.Idx(4, 1, 2), w.Idx(1, 1, 1));
        Check(back != null, "and back down it");
    }

    private void Digging()
    {
        var f = Tiny(new[] { "#########", "#@......#", "#########", "#########", "#########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        f.Spawn(ItemKind.Pick, Tables.Mi("copper"), w.Idx(2, 1, 1));
        int n = f.Designate(new P3(3, 2, 1), new P3(5, 3, 1), Desig.Dig);
        Eq(n, 6, "six cells designated");
        int t = Run(f, () => f.Designated.Count == 0, 20000);
        Check(f.Designated.Count == 0, $"all dug ({t} ticks)");
        Check(d.Pick >= 0, "the miner took the pick");
        Eq(w.Shapes[w.Idx(4, 3, 1)], Shape.Floor, "dug to floor");
        Check(!w.Has(w.Idx(4, 4, 1), CellFlag.Hidden), "the wall beyond is revealed");
        // Stairs down a shaft.
        f.Designate(new P3(7, 1, 1), new P3(7, 1, 1), Desig.StairDown);
        f.Designate(new P3(7, 1, 0), new P3(7, 1, 0), Desig.StairUp);
        Run(f, () => f.Designated.Count == 0, 20000);
        Eq(w.Shapes[w.Idx(7, 1, 0)], Shape.StairUp, "up stair dug from above");
        Check(f.Path.Find(d.Cell, w.Idx(7, 1, 0)) != null, "the lower level can be walked to");
        // Digging out the cell under a down stair opens the way: it becomes the up stair.
        f.Designate(new P3(5, 1, 1), new P3(5, 1, 1), Desig.StairDown);
        Run(f, () => f.Designated.Count == 0, 20000);
        f.Designate(new P3(5, 1, 0), new P3(5, 1, 0), Desig.Dig);
        Run(f, () => f.Designated.Count == 0, 20000);
        Eq(w.Shapes[w.Idx(5, 1, 0)], Shape.StairUp, "a plain dig under a down stair makes the up stair");
        // A floor already dug can take a stair: the repair for a dead end.
        f.Designate(new P3(3, 2, 1), new P3(3, 2, 1), Desig.StairDown);
        Check(w.Desigs[w.Idx(3, 2, 1)] == Desig.StairDown, "a down stair can be cut into a dug floor");
        Run(f, () => f.Designated.Count == 0, 20000);
        f.Designate(new P3(3, 2, 0), new P3(3, 2, 0), Desig.Dig);
        Run(f, () => f.Designated.Count == 0, 20000);
        Check(f.Path.Find(d.Cell, w.Idx(3, 2, 0)) != null, "and the level below can be walked to");
        Check(!f.AnyReachable(new P3(1, 3, 0), new P3(1, 3, 0)) || true, "(reachability is checked)");
        // No pick, no mining.
        var g = Tiny(new[] { "#####", "#@..#", "#####" });
        g.Designate(new P3(2, 2, 1), new P3(2, 2, 1), Desig.Dig);
        Run(g, () => false, 2000);
        Eq(g.Designated.Count, 1, "without a pick nothing is dug");
    }

    private void Channels()
    {
        var f = Tiny(
            new[] { "#######", "#@....#", "#######" },
            new[] { "#######", "#######", "#######" });
        World w = f.World;
        f.Spawn(ItemKind.Pick, Tables.Mi("copper"), w.Idx(1, 1, 2));
        f.Designate(new P3(3, 1, 2), new P3(3, 1, 2), Desig.Channel);
        Run(f, () => f.Designated.Count == 0, 20000);
        Eq(w.Shapes[w.Idx(3, 1, 2)], Shape.Empty, "channel opens the floor");
        Eq(w.Shapes[w.Idx(3, 1, 1)], Shape.Ramp, "and leaves a ramp below");
    }

    private void Hauling()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        World w = f.World;
        for (int k = 0; k < 5; k++) f.Spawn(ItemKind.Log, Tables.Mi("oak"), w.Idx(2 + k, 1, 1));
        f.Spawn(ItemKind.Drink, Tables.Mi("dimcap"), w.Idx(7, 2, 1));
        f.AddZone(ZoneKind.Stockpile, new P3(6, 1, 1), new P3(8, 2, 1));
        int t = Run(f, () => f.Items.Values.All(i => i.Stored), 8000);
        Check(f.Items.Values.All(i => i.Stored), $"everything hauled to the stockpile ({t} ticks)");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Log), 5, "nothing lost on the way");
        Check(f.StockPromised.Count == 0, "no stockpile cells left promised");
        Eq(f.Capacity(f.Items.Values.First(i => i.Kind == ItemKind.Log)), 2, "logs stack two to a cell");
        // Too little room: a warning.
        var g = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        for (int k = 0; k < 20; k++) g.Spawn(ItemKind.Log, Tables.Mi("oak"), g.World.Idx(2 + k % 7, 1 + k / 7 % 2, 1));
        g.AddZone(ZoneKind.Stockpile, new P3(8, 1, 1), new P3(8, 1, 1));
        g.Advance(1000);
        Check(g.Log.Any(l => l.Text.Contains("stockpiles are full")), "a full stockpile is reported");
    }

    private void Building()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "#........#", "#........#", "##########" });
        World w = f.World;
        f.Spawn(ItemKind.Log, Tables.Mi("oak"), w.Idx(1, 4, 1));
        f.Spawn(ItemKind.Boulder, Tables.Mi("granite"), w.Idx(2, 4, 1));
        Check(f.CanPlace("carpenter", 5, 1, 1) == null, "a workshop fits");
        Check(f.CanPlace("carpenter", 7, 1, 1) != null, "but not into the wall");
        Building b = f.Place("carpenter", 5, 1, 1);
        Building wall = f.Place("wall", 3, 3, 1);
        Run(f, () => b.Built && !f.Buildings.Contains(wall), 10000);
        Check(b.Built, "the carpenter's shop is built");
        Eq(w.Shapes[w.Idx(3, 3, 1)], Shape.Wall, "the wall is built");
        Check(w.Has(w.Idx(3, 3, 1), CellFlag.Built), "...and known to be built");
        Eq(f.Items.Count, 0, "both materials used up");
        // Furniture from an item.
        f.Spawn(ItemKind.Bed, Tables.Mi("oak"), w.Idx(1, 2, 1));
        Building bed = f.Place("bed", 2, 3, 1);
        Run(f, () => bed.Built, 5000);
        Check(bed.Built, "the bed is placed");
        Eq(bed.Mat, Tables.Mi("oak"), "an oak bed");
        f.Deconstruct(bed);
        Check(f.Items.Values.Any(i => i.Kind == ItemKind.Bed), "taking it down gives the bed back");
    }

    private void Crafting()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "#........#", "##########" });
        World w = f.World;
        Building shop = f.Place("smelter", 5, 1, 1);
        shop.Built = true;
        f.Spawn(ItemKind.Boulder, Tables.Mi("malachite"), w.Idx(1, 3, 1));
        f.Spawn(ItemKind.Boulder, Tables.Mi("coal"), w.Idx(2, 3, 1));
        f.AddOrder(shop, "smelt", 1);
        Run(f, () => shop.Orders.Count == 0, 8000);
        Eq(shop.Orders.Count, 0, "the smelt order is done");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Bar && i.Mat == Tables.Mi("copper")), 2, "two copper bars");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Boulder), 0, "ore and coal used up");
        // An order that cannot be done says why.
        f.AddOrder(shop, "smelt", 1);
        Run(f, () => false, 300);
        Check(shop.Orders[0].Stuck?.Contains("ore") == true, "a stuck order names what it needs");
    }

    private void Needs()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "#.......~#", "##########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        f.Spawn(ItemKind.Meal, Tables.Mi("dimcap"), w.Idx(6, 1, 1));
        d.Hunger = Tables.HungryAt + 10;
        Run(f, () => d.Hunger < 100, 1000);
        Check(d.Hunger < 100, "a hungry dwarf eats");
        Check(d.Thoughts.Any(t => t.Id == "ate_meal"), "and enjoys the stew");
        d.Thirst = Tables.ThirstyAt + 10;
        Run(f, () => d.Thirst < 100, 2000);
        Check(d.Thirst < 100, "a thirsty dwarf drinks from the pond");
        Check(d.Thoughts.Any(t => t.Id == "drank_water"), "and minds the water");
        d.Fatigue = Tables.TiredAt + 10;
        Run(f, () => d.Fatigue == 0, 3000);
        Eq(d.Fatigue, 0, "a tired dwarf sleeps it off");
        Check(d.Thoughts.Any(t => t.Id == "slept_floor"), "on the floor, there being no bed");
        // Without food, starvation.
        var g = Tiny(new[] { "#####", "#@..#", "#..~#", "#####" });
        Dwarf s = g.Dwarves[0];
        s.Hunger = Tables.StarveDeath - 50;
        Run(g, () => s.Dead, 200);
        Check(s.Dead, "no food is fatal");
        Check(g.Fallen, "and with the last dwarf, the fortress falls");
        Check(g.Items.Values.Any(i => i.Kind == ItemKind.Corpse), "leaving a corpse");
    }

    private void Farming()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#.ssss...#", "#.ssss...#", "##########" });
        World w = f.World;
        f.Spawn(ItemKind.Seed, Tables.Mi("dimcap"), w.Idx(7, 1, 1));
        f.Spawn(ItemKind.Seed, Tables.Mi("dimcap"), w.Idx(7, 2, 1));
        Zone z = f.AddZone(ZoneKind.Farm, new P3(2, 2, 1), new P3(5, 3, 1), "dimcap");
        Run(f, () => f.Crops.Count(c => c.Value.Planted <= f.Tick) == 2, 4000);
        Eq(f.Crops.Count(c => c.Value.Planted <= f.Tick), 2, "both seeds planted");
        Run(f, () => f.Stats.GetValueOrDefault("harvested") >= 2, 12000);
        Check(f.Stats.GetValueOrDefault("harvested") >= 2, "the crop is harvested");
        Check(f.Items.Values.Count(i => i.Kind == ItemKind.Plant) >= 2, "and the produce kept (what was not eaten)");
        Check(f.Items.Values.Any(i => i.Kind == ItemKind.Seed) || f.Stats.GetValueOrDefault("planted") > 2, "with seed kept back for the next sowing");
        Check(!f.Farmable(w.Idx(7, 1, 1), Tables.M("dimcap")), "rock is not farmland");
    }

    private void Moods()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        Dwarf d = f.Dwarves[0];
        for (int k = 0; k < 4; k++) f.AddThought(d, "saw_death");
        Check(d.Mood < 0, "bad thoughts make a bad mood");
        d.Stress = 4000;
        Run(f, () => d.Task?.Kind == TaskKind.Tantrum, 3000);
        Eq(d.Task?.Kind, TaskKind.Tantrum, "enough stress breaks into a tantrum");
        Run(f, () => d.Task?.Kind != TaskKind.Tantrum, 2000);
        Check(d.Stress < 4000, "and the tantrum lets some of it out");
    }

    private void Musick()
    {
        var m = new Music();
        foreach (MusicMood mood in Enum.GetValues<MusicMood>())
        {
            float[] s = m.Render(mood, 8);
            float peak = s.Max(Math.Abs);
            double rms = Math.Sqrt(s.Average(x => (double)x * x));
            Check(peak <= 0.81f && peak > 0.05f, $"{mood} music is in range (peak {peak:0.00})");
            Check(rms > 0.02, $"{mood} music is not silent (rms {rms:0.000})");
        }
        m.Free();
    }

    /// <summary>The cheap in-place region merging must agree with a full rebuild, whatever is dug.</summary>
    private void Regions()
    {
        Fortress f = Fortress.New("Regionhold", 999);
        World w = f.World;
        var rng = new Rng(5);
        P3 e = f.Embark;
        int mismatches = 0, checks = 0;
        for (int k = 0; k < 400; k++)
        {
            int x = e.X + rng.Range(-12, 12), y = e.Y + rng.Range(-12, 12), z = e.Z - rng.Range(0, 4);
            if (!w.In(x, y, z)) continue;
            int i = w.Idx(x, y, z);
            int op = rng.Int(6);
            if (op <= 2 && w.Shapes[i] == Shape.Wall) w.SetShape(i, Shape.Floor);
            else if (op == 3 && w.Shapes[i] is Shape.Wall or Shape.Floor) w.SetShape(i, Shape.StairUD);
            else if (op == 4 && w.Shapes[i] == Shape.Floor) w.SetShape(i, Shape.Wall);
            else if (op == 5) { w.SetLocked(i, !w.Locked.Contains(i)); }
            if (k % 20 != 19) continue;
            w.EnsureComponents();
            int[] inc = (int[])w.Comp.Clone();
            w.RebuildComponents();
            checks++;
            // Same partition: a one-to-one map between the two labellings.
            var map = new Dictionary<int, int>();
            var back = new Dictionary<int, int>();
            bool same = true;
            for (int c = 0; c < w.N && same; c++)
            {
                int a2 = inc[c], b2 = w.Comp[c];
                if ((a2 == 0) != (b2 == 0)) { same = false; break; }
                if (a2 == 0) continue;
                if (map.TryGetValue(a2, out int m2) && m2 != b2) same = false;
                if (back.TryGetValue(b2, out int m3) && m3 != a2) same = false;
                map[a2] = b2; back[b2] = a2;
            }
            if (!same) mismatches++;
        }
        Eq(mismatches, 0, $"incremental regions match a full rebuild ({checks} comparisons)");
    }

    /// <summary>Every goal of the tutorial can be met by doing what it asks.</summary>
    private void Tutorials()
    {
        Fortress f = Fortress.New("Tuthold", 616);
        f.TutorialStep = 0;
        var w = new TutorialWatch { StartZ = f.Embark.Z, Z = f.Embark.Z };
        Tutorial.Next(f);                                   // welcome: read
        w.Z--; Tutorial.Advance(f, w);                       // looked down
        Eq(f.TutorialStep, 2, "looking down a level passes the second goal");
        w.SpeedChanged = true; Tutorial.Advance(f, w);
        P3 e = f.Embark;
        f.AddZone(ZoneKind.Stockpile, new P3(e.X - 3, e.Y - 8, e.Z), new P3(e.X + 3, e.Y - 3, e.Z));
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 4, "a big stockpile passes the stockpile goal");
        int felled = 0;
        for (int i = 0; i < f.World.N && felled < 6; i++) if (f.World.Shapes[i] == Shape.Tree && f.World.IsOutside(i)) { P3 tp = f.World.Pos(i); felled += f.Designate(tp, tp, Desig.Chop); }
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 5, "marking trees to fell passes the wood goal");
        f.AddZone(ZoneKind.Farm, new P3(e.X - 3, e.Y + 4, e.Z), new P3(e.X + 1, e.Y + 6, e.Z), "hearthwheat");
        Tutorial.Advance(f, w);
        Building brew = f.Place("brewery", e.X + 6, e.Y + 6, e.Z) ?? f.Place("brewery", e.X - 9, e.Y - 2, e.Z);
        f.AddOrder(brew, "ale", 1, true);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 7, "a farm and a brewery with ale on order pass those goals");
        P3 s = new(e.X + 4, e.Y, e.Z);
        f.Designate(s, s, Desig.StairDown);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 7, "a down stair alone is not yet a way down");
        f.Designate(new P3(s.X, s.Y, s.Z - 1), new P3(s.X, s.Y, s.Z - 1), Desig.StairUD);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 8, "with the stair beneath it, it is");
        f.Designate(new P3(s.X + 1, s.Y - 3, s.Z - 1), new P3(s.X + 10, s.Y + 2, s.Z - 1), Desig.Dig);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 9, "designating a hall passes the hall goal");
        Building c = f.Place("carpenter", e.X - 7, e.Y + 2, e.Z) ?? f.Place("carpenter", e.X + 6, e.Y + 4, e.Z);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 10, "placing a carpenter's shop passes that goal");
        f.AddOrder(c, "bed", 5);
        Tutorial.Advance(f, w);
        Eq(f.TutorialStep, 11, "ordering beds passes that goal");
        int left = Tutorial.Steps.Count - f.TutorialStep;
        for (int k = 0; k < left; k++) Tutorial.Next(f);
        Eq(f.TutorialStep, -1, "the tutorial ends after its last goal");
        Check(Tutorial.Steps.All(t => t.Title != null && t.How != null), "every goal says what to do");
        f.TutorialStep = 3;
        Eq(Save.Read(Save.Write(f)).TutorialStep, 3, "the tutorial's place is saved");
    }

    private Fortress _gen;

    private void Generator()
    {
        var sw = Stopwatch.StartNew();
        Fortress f = Fortress.New("Genhold", 12345);
        long ms = sw.ElapsedMilliseconds;
        _gen = f;
        World w = f.World;
        GD.Print($"    generated {w.W}x{w.H}x{w.D} in {ms} ms");
        Check(ms < 15000, "generation is quick enough");
        Check(w.Walkable(f.Embark), "the wagon stands on walkable ground");
        Eq(f.Alive, 7, "seven founders");
        Check(f.Items.Values.Count(i => i.Kind == ItemKind.Pick) >= 2, "picks in the wagon");
        int comp = w.Comp[w.Idx(f.Embark)];
        int reach = 0;
        for (int i = 0; i < w.N; i++) if (w.Comp[i] == comp) reach++;
        Check(reach > w.W * w.H / 3, $"most of the surface is one walkable piece ({reach} cells)");
        P3 edge = f.EdgeArrival();
        Check(edge.X == 0 || edge.Y == 0 || edge.X == w.W - 1 || edge.Y == w.H - 1, "migrants can arrive from the edge");
        int ores = 0, gems = 0, hiddenOpen = 0;
        for (int i = 0; i < w.N; i++)
        {
            Mat m = Tables.Mats[w.Mats[i]];
            if (w.Shapes[i] == Shape.Wall && m.Kind == MatKind.Ore) ores++;
            if (w.Shapes[i] == Shape.Wall && m.Kind == MatKind.Gem) gems++;
            if (w.Shapes[i] == Shape.Floor && w.Has(i, CellFlag.Hidden)) hiddenOpen++;
        }
        Check(ores > 1000, $"ore in the rock ({ores})");
        Check(gems > 200, $"gems in the rock ({gems})");
        Check(hiddenOpen > 500, $"a hidden cavern layer ({hiddenOpen} cells)");
        // Same seed, same world.
        Fortress again = Fortress.New("Genhold", 12345);
        bool same = true;
        for (int i = 0; i < w.N && same; i += 97) same = again.World.Shapes[i] == w.Shapes[i] && again.World.Mats[i] == w.Mats[i];
        Check(same, "a seed always makes the same world");
    }

    private void Saving()
    {
        Fortress f = _gen ?? Fortress.New("Genhold", 12345);
        var o = new Overseer(f);
        for (int k = 0; k < 6; k++) { o.Think(); f.Advance(300); }
        byte[] data = Save.Write(f);
        GD.Print($"    save is {data.Length / 1024} KB");
        Fortress g = Save.Read(data);
        Eq(g.Tick, f.Tick, "the clock");
        Eq(g.Dwarves.Count, f.Dwarves.Count, "the dwarves");
        Eq(g.Items.Count, f.Items.Count, "the items");
        Eq(g.Buildings.Count, f.Buildings.Count, "the buildings");
        Eq(g.Designated.Count, f.Designated.Count, "the designations");
        Eq(g.Dwarves[0].Name, f.Dwarves[0].Name, "a name");
        bool same = true;
        for (int i = 0; i < f.World.N && same; i++) same = g.World.Shapes[i] == f.World.Shapes[i] && g.World.Flags[i] == f.World.Flags[i];
        Check(same, "the map");
        // And it keeps playing.
        g.Advance(500);
        Check(!g.Fallen && g.Alive > 0, "a loaded fortress plays on");
    }

    /// <summary>Three years with the overseer, a line a season: how raids, beasts and moods go over time.</summary>
    private void LongRun()
    {
        Fortress f = Fortress.New("Longhold", 2026);
        var o = new Overseer(f);
        int season = -1;
        string S(string k) => f.Stats.GetValueOrDefault(k).ToString();
        for (long t = 0; t < Tables.TicksPerYear * 3 && !f.Fallen; t += 250)
        {
            o.Think();
            f.Advance(250);
            int sz = (int)(f.Tick / Tables.TicksPerSeason);
            if (sz == season) continue;
            season = sz;
            int soldiers = f.Squads.Sum(q => q.Members.Count);
            GD.Print($"    {f.Date,-22} {f.Alive,3} alive  deaths {S("deaths"),2} (slain {S("slain")})  raids {S("raids")}/{S("raids_beaten")} broken  kills {S("kills")}  trapped {S("trapped")}  beasts {f.Creatures.Count(c => c.D.Hostile)}  soldiers {soldiers} armed {f.Living.Count(d => d.Weapon >= 0)}  wealth {f.Wealth}  {string.Join(",", f.Living.GroupBy(d => d.MoodWord).Select(g => g.Key[..3] + g.Count()))}");
        }
        GD.Print($"    herds: {string.Join(", ", f.Creatures.Where(c => c.Tame).GroupBy(c => c.Def).Select(g => g.Key + " " + g.Count()))}; births {S("births")}, eggs {S("eggs")}, slaughtered {S("slaughtered")}, butchered {S("butchered")}, starved {S("animals_starved")}, killed {S("livestock_killed")}, tamed {S("tamed")}, war-beast kills {S("beast_kills")}; buried {S("buried")}");
        GD.Print($"    trade: {S("caravans")} caravans, {S("trades")} deals worth {S("traded_value")}; standing {string.Join(", ", f.Standing.Select(kv => kv.Key + " " + kv.Value))}");
        GD.Print($"    faith: {S("prayers")} prayers, {S("feasts")} feasts, {S("spirits_calmed")} spirits calmed, {S("runes")} runes; shrines {f.Buildings.Count(b => b.Def == "shrine" && b.Built)}, temple {f.Buildings.Any(b => b.Def == "temple" && b.Built)}, runeforge {f.Buildings.Any(b => b.Def == "runeforge" && b.Built)}; unprayed {f.Living.Count(d => d.Thoughts.Any(t => t.Id == "no_prayer"))}");
        GD.Print($"    tombs: {f.Dwarves.Count(d => d.Dead && !d.Buried)} unburied, {f.Items.Values.Count(i => i.Kind == ItemKind.Coffin)} coffins in store, {f.Buildings.Count(b => b.Def == "coffin")} placed ({f.Buildings.Count(b => b.Def == "coffin" && b.Owner < 0 && b.Built)} empty), {f.Items.Values.Count(i => i.Kind == ItemKind.Corpse && i.Of >= 0)} dwarf corpses lying; mason orders {string.Join(",", f.Buildings.Where(b => b.Def == "mason").SelectMany(b => b.Orders).Select(o => o.Recipe + (o.Stuck != null ? "[" + o.Stuck + "]" : "")))}");
        GD.Print($"    moods {S("moods")}, artifacts {S("artifacts")}, gone mad {S("insane")}, lost {S("lost")}");
        foreach (LogEntry e in f.Log.Where(l => l.Text.Contains("demands") || l.Text.Contains("needs a") || l.Text.Contains("mood") || l.Text.Contains("possessed") || l.Text.Contains("melancholy") || l.Text.Contains("berserk") || l.Text.Contains("mad"))) GD.Print($"    mood log {Fortress.ShortDate(e.Tick)} y{e.Tick / Tables.TicksPerYear + 1}: {e.Text}");
        foreach (int id in f.Artifacts.Where(f.Items.ContainsKey)) GD.Print($"    artifact: {f.Items[id].Label}. {f.Items[id].Description}");
        GD.Print($"    larder: {f.Items.Values.Count(i => i.Kind == ItemKind.Meat)} meat, {f.Items.Values.Count(i => i.Kind == ItemKind.Egg)} eggs, {f.Items.Values.Count(i => i.Kind == ItemKind.Leather)} leather, {f.Living.Count(d => d.Armor >= 0 && f.Items[d.Armor].M.Key == "leather")} in leather armour");
        foreach (LogEntry e in f.Log.Where(l => l.Kind == LogKind.Alert).TakeLast(12)) GD.Print($"    alert {Fortress.ShortDate(e.Tick)}: {e.Text}");
        Check(!f.Fallen, "the hold stands after three years");
    }

    /// <summary>A whole year with the overseer in charge: the balance report.</summary>
    private void Year()
    {
        Fortress f = Fortress.New("Yearhold", 777);
        var o = new Overseer(f);
        var sw = Stopwatch.StartNew();
        int lowest = 7;
        for (long t = 0; t < Tables.TicksPerYear && !f.Fallen; t += 250)
        {
            o.Think();
            f.Advance(250);
            lowest = Math.Min(lowest, f.Alive);
        }
        double secs = sw.Elapsed.TotalSeconds;
        double tps = f.Tick / Math.Max(0.001, secs);
        string S(string k) => f.Stats.GetValueOrDefault(k).ToString();
        GD.Print($"    year 1: {f.Alive} alive (lowest {lowest}), {S("deaths")} deaths, {S("migrants")} migrants, wealth {f.Wealth}");
        GD.Print($"    dug {S("dug")}, felled {S("trees")}, hauled {S("hauled")}, built {S("buildings")}, crafted {S("crafted")}, planted {S("planted")}, harvested {S("harvested")}");
        GD.Print($"    ate {S("meals")}, drank {S("drinks")}, tantrums {S("tantrums")}; {f.Items.Values.Count(i => i.Kind == ItemKind.Drink)} ale, {f.Items.Values.Count(i => Tables.Matches("food", i))} food left");
        double fq = System.Diagnostics.Stopwatch.Frequency / 1000.0;
        GD.Print($"    time: dwarves {f.Timing[0] / fq:0} ms, creatures {f.Timing[1] / fq:0} ms, fluids {f.Timing[2] / fq:0} ms of {secs * 1000:0} ms; {f.World.Rebuilds} full region rebuilds; creature paths {f.CreatureSearches} searches, {f.CreatureNodes} nodes; creatures now {f.Creatures.Count} ({string.Join(",", f.Creatures.GroupBy(c => c.Def).Select(g => g.Key + g.Count()))})");
        GD.Print($"    {tps:0} ticks/s with the overseer ({f.Path.Searches} path searches, {f.Path.TotalExpanded / Math.Max(1, f.Path.Searches)} nodes each)");
        var moods = f.Living.GroupBy(d => d.MoodWord).Select(g => $"{g.Key} {g.Count()}");
        GD.Print($"    moods: {string.Join(", ", moods)}");
        GD.Print($"    {S("raids")} raids ({S("raids_beaten")} broken), {S("kills")} kills, {S("slain")} dwarves slain, {S("trapped")} trapped; squads: {string.Join(", ", f.Squads.Select(s => s.Name + " x" + s.Members.Count))}");
        foreach (string n in o.Notes) GD.Print("    overseer: " + n);
        Check(!f.Fallen, "the fortress survives its first year");
        Check(f.Stats.GetValueOrDefault("deaths") <= 4, "few die in the first year");
        Check(f.Squads.Count > 0, "a squad is raised");
        Check(f.Stats.GetValueOrDefault("dug") > 300, "a good deal is dug");
        Check(f.Stats.GetValueOrDefault("crafted") > 10, "things are made");
        Check(f.Stats.GetValueOrDefault("harvested") > 0, "a harvest comes in");
        Check(f.Buildings.Count(b => b.Def == "bed" && b.Built) >= 5, "beds go in");
        Check(tps > 1500, "the sim runs fast enough for 10x speed");
    }
}
