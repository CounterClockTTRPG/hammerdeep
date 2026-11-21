using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using STask = System.Threading.Tasks.Task;

namespace Hammerdeep;

/// <summary>
/// Remote control for an agent playtester: <c>./play.sh --agent</c> runs the
/// real game and watches <c>agent/cmd.txt</c>; <c>scripts/agent.sh</c> writes
/// commands there and prints the reply from <c>agent/out.txt</c>. Keys and
/// clicks go in through <see cref="Input.ParseInputEvent"/>, as a player's
/// would; the real keyboard and mouse are shut out. Commands are separated by
/// ';' -- see <see cref="Help"/>.
/// </summary>
public partial class AgentRunner : Node
{
    public Root Root;
    public static bool Active;
    /// <summary>Game time only passes while a command runs (default), so thinking between commands costs nothing.</summary>
    public static bool Frozen = true;
    public static bool Busy;
    public static bool Running => !Active || !Frozen || Busy;
    public const int Device = 4242;
    private string _dir, _lastId;
    private double _poll;
    private bool _busy;
    private Vector2 _mouse = new(800, 450);
    private bool _down;

    private const string Help = @"commands (separate with ';'):
  state                 what is on screen, in words, with an ASCII map of the level in view
  map [R]               just the ASCII map, R cells around the camera (default 24)
  shot NAME             screenshot to agent/NAME.png
  ui                    list the buttons on screen
  press TEXT            click the first enabled button whose text contains TEXT
  type TEXT             type into the focused text field
  key K [MS]            tap a key, or hold it MS ms (letters, digits, Space Esc Enter Tab Up Down Left Right PgUp PgDn F1 < >)
  mouse X Y             move the mouse to screen pixel X,Y (1600x900)
  click [left|right]    click at the mouse
  at X Y                move the mouse onto map cell X,Y of the level in view
  drag X1 Y1 X2 Y2      left-drag from map cell to map cell on the level in view (designations, zones)
  rdrag DX DY           right-drag by DX DY screen pixels from the mouse (in 3D: turns and tilts the camera)
  down / up             hold / release the left button (e.g. down; key >; key >; at X Y; up -- a box several levels deep)
  goto X Y Z            centre the camera on a cell
  zoom Z                camera zoom, 0.5 (far) .. 6 (near)
  perf [FRAMES]         time FRAMES frames (default 120) as they run: frame and process time, draw calls,
                        objects; in 3D also where View3D's own time goes
  level Z               show level Z
  wait MS               let the game run MS real milliseconds
  tick N                advance the fortress N ticks at once (50 ticks = 2.5 s of normal speed)
  dwarf NAME            select a dwarf by (part of) their name and centre on them
  freeze on|off         on (default): game time passes only while a command runs
  quit                  close the game
  caravan [low|mountain]   (debug) send a caravan and wait until it reaches the depot
  raid [N]              (debug) a grimling raid of N
  mood [NAME]           (debug) a strange mood for a dwarf
  happen KEY            (debug) something at the gate: bard, refugee, envoy, wanderer, rats
  beast KEY             (debug) a great beast: ashwyrm, palestalker, merething, gnashjaw, queen
  creatures [DEF]       the creatures about (or only of one kind): where, and what they are up to
  weather KIND          (debug) the season's weather: fair, floods, drought, heat, hard
  ruin                  (debug) uncover the ancient vault and look at it
  time HOUR             (debug) set the hour of the day, 0..23 (the 3D light follows it)
  lay STYLE X0 Y0 X1 Y1 Z (debug) lay a floor style (0 bare .. 5 mosaic) over a box at once
  rank N                (debug) set the hold's rank, 0 Outpost .. 3 Seat of the Mountainhome
  arm [NAME|all]        (debug) iron armour, helm, hammer and shield
  help";

    public override void _Ready()
    {
        Active = true;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
            DisplayServer.WindowSetMousePassthrough(new[] { Vector2.Zero, new Vector2(1, 0), new Vector2(1, 1) });
        }
        _dir = ProjectSettings.GlobalizePath("res://agent");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "out.txt"), "ready\n" + Help + "\n");
        if (File.Exists(Path.Combine(_dir, "cmd.txt"))) _lastId = File.ReadAllLines(Path.Combine(_dir, "cmd.txt")).FirstOrDefault();
        GD.Print("agent: listening on " + _dir);
    }

    public override void _Input(InputEvent e)
    {
        if (e.Device != Device) GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_busy) return;
        _poll += delta;
        if (_poll < 0.1) return;
        _poll = 0;
        string path = Path.Combine(_dir, "cmd.txt");
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); } catch { return; }
        if (lines.Length < 1 || lines[0] == _lastId) return;
        _lastId = lines[0];
        _ = RunCommands(_lastId, string.Join(";", lines.Skip(1)));
    }

    private async STask RunCommands(string id, string cmds)
    {
        _busy = true;
        var sb = new StringBuilder();
        foreach (string raw in cmds.Split(';'))
        {
            string c = raw.Trim();
            if (c.Length == 0) continue;
            try
            {
                string r = await Do(c);
                if (!string.IsNullOrEmpty(r)) sb.AppendLine(r);
            }
            catch (Exception e) { sb.AppendLine($"error in '{c}': {e.Message} -- stopped here"); break; }
        }
        Busy = false;
        if (sb.Length == 0) sb.AppendLine("ok");
        string tmp = Path.Combine(_dir, "out.tmp"), dst = Path.Combine(_dir, "out.txt");
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try { File.WriteAllText(tmp, id + "\n" + sb); File.Move(tmp, dst, true); break; }
            catch { await Ms(100); }
        }
        _busy = false;
    }

    private async STask Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async STask Ms(int ms)
    {
        ulong end = Time.GetTicksMsec() + (ulong)ms;
        while (Time.GetTicksMsec() < end) await Frames(1);
    }

    // ---- input ------------------------------------------------------------------------

    private static Key KeyOf(string k)
    {
        switch (k.ToLowerInvariant())
        {
            case "space": return Key.Space;
            case "esc": case "escape": return Key.Escape;
            case "enter": return Key.Enter;
            case "tab": return Key.Tab;
            case "up": return Key.Up;
            case "down": return Key.Down;
            case "left": return Key.Left;
            case "right": return Key.Right;
            case "pgup": return Key.Pageup;
            case "pgdn": return Key.Pagedown;
            case "f1": return Key.F1;
            case "shift": return Key.Shift;
            case "<": return Key.Comma;
            case ">": return Key.Period;
        }
        if (k.Length == 1 && char.IsLetter(k[0])) return Key.A + (char.ToUpperInvariant(k[0]) - 'A');
        if (k.Length == 1 && char.IsDigit(k[0])) return Key.Key0 + (k[0] - '0');
        throw new Exception($"unknown key '{k}'");
    }

    private static long Unicode(string k)
    {
        if (k == "<" || k == ">" || k == "?") return k[0];
        if (k.Length == 1) return char.ToLowerInvariant(k[0]);
        return k.ToLowerInvariant() == "space" ? ' ' : 0;
    }

    private async STask TapKey(string k, int holdMs)
    {
        bool ctrl = k.StartsWith("ctrl+", StringComparison.OrdinalIgnoreCase);
        if (ctrl) k = k[5..];
        Key key = KeyOf(k);
        bool shift = k is "<" or ">" or "?";
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Unicode = ctrl ? 0 : Unicode(k), Pressed = true, ShiftPressed = shift, CtrlPressed = ctrl, Device = Device });
        if (holdMs > 0) await Ms(holdMs); else await Frames(2);
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false, Device = Device });
        await Frames(2);
    }

    private async STask Move(Vector2 to, MouseButtonMask mask = 0)
    {
        Vector2 from = _mouse;
        _mouse = to;
        Vector2 wt = Win(to);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = wt, GlobalPosition = wt, Relative = wt - Win(from), ButtonMask = mask, Device = Device });
        await Frames(1);
    }

    private async STask Button(MouseButton b, bool down)
    {
        Input.ParseInputEvent(new InputEventMouseButton { Position = Win(_mouse), GlobalPosition = Win(_mouse), ButtonIndex = b, Pressed = down, ButtonMask = down ? (b == MouseButton.Left ? MouseButtonMask.Left : MouseButtonMask.Right) : 0, Device = Device });
        await Frames(2);
    }

    /// <summary>
    /// The agent works in the game's own 1600x900 layout; input events are read in window pixels,
    /// which differ when the window is bigger or smaller and the layout is stretched to fill it.
    /// </summary>
    private Vector2 Win(Vector2 layout) => GetTree().Root.GetFinalTransform() * layout;

    private Vector2 CellScreen(int x, int y)
    {
        MapView m = Root.Map ?? throw new Exception("no map on screen");
        return m.ScreenOf(new P3(x, y, m.Z));
    }

    // ---- commands ---------------------------------------------------------------------

    private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private async System.Threading.Tasks.Task<string> Do(string c)
    {
        string[] a = c.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string rest = c.Length > a[0].Length ? c[(a[0].Length + 1)..].Trim() : "";
        switch (a[0].ToLowerInvariant())
        {
            case "help": return Help;
            case "state": Busy = true; await Frames(2); Busy = false; return State();
            case "map": return AsciiMap(a.Length > 1 ? I(a[1]) : 24);
            case "shot":
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                string name = a.Length > 1 ? a[1] : "shot";
                GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_dir, name + ".png"));
                return $"saved agent/{name}.png";
            }
            case "ui": return ListUi();
            case "press":
            {
                Button b = Buttons().FirstOrDefault(x => !x.Disabled && x.Text.Contains(rest, StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception($"no enabled button with '{rest}'");
                Vector2 at = b.GetGlobalRect().GetCenter();
                string label = b.Text;
                await Move(at);
                Busy = true;
                await Button(MouseButton.Left, true);
                await Button(MouseButton.Left, false);
                await Frames(3);
                Busy = false;
                return $"pressed '{label}'";
            }
            case "type":
            {
                foreach (char ch in rest)
                {
                    var e = new InputEventKey { Unicode = ch, Pressed = true, Device = Device, Keycode = char.IsLetter(ch) ? Godot.Key.A + (char.ToUpperInvariant(ch) - 'A') : Godot.Key.None };
                    Input.ParseInputEvent(e);
                    await Frames(1);
                }
                return "typed";
            }
            case "key":
                Busy = true;
                await TapKey(a[1], a.Length > 2 ? I(a[2]) : 0);
                Busy = false;
                return null;
            case "mouse": await Move(new Vector2(I(a[1]), I(a[2]))); return null;
            case "click":
            {
                MouseButton b = a.Length > 1 && a[1] == "right" ? MouseButton.Right : MouseButton.Left;
                await Button(b, true);
                await Button(b, false);
                return null;
            }
            case "at": await Move(CellScreen(I(a[1]), I(a[2])), _down ? MouseButtonMask.Left : 0); return null;
            case "down": _down = true; await Button(MouseButton.Left, true); return null;
            case "up": _down = false; await Button(MouseButton.Left, false); return null;
            case "drag":
            {
                await Move(CellScreen(I(a[1]), I(a[2])));
                await Button(MouseButton.Left, true);
                await Move(CellScreen(I(a[3]), I(a[4])), MouseButtonMask.Left);
                await Button(MouseButton.Left, false);
                return null;
            }
            case "zoom":
                if (Root.Map != null) Root.Map.Cam.Zoom = Vector2.One * Math.Clamp(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), 0.5f, 6f);
                await Frames(2);
                return null;
            case "creatures":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                var list = f.Creatures.Where(c => !c.Dead && (a.Length < 2 || c.Def == a[1])).Take(40)
                    .Select(c => $"  {c.Def,-12} at {f.World.Pos(c.Cell)}{(c.Tame ? " tame" : "")}{(c.Raider ? " raider" : "")}{(c.Target >= 0 ? " hunting" : "")}");
                return string.Join("\n", list);
            }
            case "weather":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                f.Sky = (a.Length > 1 ? a[1] : "fair") switch { "floods" => Weather.Floods, "drought" => Weather.Drought, "heat" => Weather.Heat, "hard" => Weather.HardWinter, _ => Weather.Fair };
                if (f.Sky == Weather.Floods) f.Flood();
                await Frames(3);
                return $"the weather: {Fortress.WeatherName(f.Sky)}";
            }
            case "ruin":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                if (f.Ruin.X < 0) return "this hold has no ruin";
                World w = f.World;
                for (int z = f.Ruin.Z - 1; z <= f.Ruin.Z + 3; z++)
                    for (int y = f.Ruin.Y - 6; y <= f.Ruin.Y + 6; y++)
                        for (int x = f.Ruin.X - 7; x <= f.Ruin.X + 7; x++)
                            if (w.In(x, y, z)) { int i = w.Idx(x, y, z); w.Set(i, CellFlag.Hidden, false); w.Changed.Add(i); }
                Root.Map?.SetZ(f.Ruin.Z);
                Root.Screen?.Jump(f.Ruin);
                await Frames(4);
                return $"the vault at {f.Ruin}";
            }
            case "lay":
            {
                // (debug) lay a floor style at once over a box: lay STYLE X0 Y0 X1 Y1 Z
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                byte style = (byte)Math.Clamp(I(a[1]), 0, Fortress.FloorStyles.Length - 1);
                int n = 0;
                for (int y = Math.Min(I(a[3]), I(a[5])); y <= Math.Max(I(a[3]), I(a[5])); y++)
                    for (int x = Math.Min(I(a[2]), I(a[4])); x <= Math.Max(I(a[2]), I(a[4])); x++)
                    {
                        if (!f.World.In(x, y, I(a[6]))) continue;
                        int cell = f.World.Idx(x, y, I(a[6]));
                        if (!f.World.CanDesignate(cell, Desig.Pave)) continue;
                        f.World.Floors[cell] = style; f.World.Set(cell, CellFlag.Grass, false); f.World.Changed.Add(cell); n++;
                    }
                await Frames(3);
                return $"laid {n} floors in {Fortress.FloorStyles[style].ToLower()}";
            }
            case "rank":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                f.Rank = Math.Clamp(a.Length > 1 ? I(a[1]) : f.Rank + 1, 0, 3);
                await Frames(3);
                return $"{f.Name} is now a {f.RankName}";
            }
            case "time":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                long day = f.Tick / Tables.TicksPerDay;
                f.Tick = day * Tables.TicksPerDay + I(a[1]) * Tables.TicksPerDay / 24;
                await Frames(3);
                return $"now {f.Date}";
            }
            case "happen":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                Happening h = f.Happen(a.Length > 1 ? a[1] : "bard");
                await Frames(3);
                return $"{h.Title}: {string.Join(" / ", h.Options)}";
            }
            case "beast":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                Creature beast = f.LaunchBeast(a.Length > 1 ? a[1] : "gnashjaw");
                await Frames(3);
                return $"{beast.D.Name} at {f.World.Pos(beast.Cell)}";
            }
            case "rdrag":
            {
                Vector2 from = _mouse;
                await Button(MouseButton.Right, true);
                for (int k = 1; k <= 8; k++) await Move(from + new Vector2(I(a[1]), I(a[2])) * k / 8f, MouseButtonMask.Right);
                await Button(MouseButton.Right, false);
                await Frames(2);
                return Root.Map != null && Root.Map.Is3D ? $"camera yaw {Root.Map.Three.Yaw:0} pitch {Root.Map.Three.Pitch:0}" : null;
            }
            case "perf":
            {
                int n = a.Length > 1 ? I(a[1]) : 120;
                bool was = Busy;
                Busy = true;
                Root.Map?.Three?.Spent.Clear();
                var frames = new List<double>();
                double procSum = 0, draws = 0, objs = 0, prims = 0;
                ulong last = Time.GetTicksUsec();
                for (int k = 0; k < n; k++)
                {
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    ulong now = Time.GetTicksUsec();
                    frames.Add((now - last) / 1000.0);
                    last = now;
                    procSum += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000;
                    draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                    objs += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
                    prims += Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
                }
                Busy = was;
                frames.Sort();
                string view = Root.Map == null ? "none" : Root.Map.Is3D ? "3D" : "2D";
                var sb = new System.Text.StringBuilder();
                sb.Append($"{view}: frame avg {frames.Average():0.0} ms, median {frames[n / 2]:0.0}, worst {frames[^1]:0.0} ({1000 / frames.Average():0} fps); ");
                sb.Append($"process {procSum / n:0.0} ms; draw calls {draws / n:0}, objects {objs / n:0}, primitives {prims / n / 1000:0}k; ");
                sb.Append($"nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):0}");
                if (Root.Map?.Three is View3D t && t.Spent.Count > 0)
                    sb.Append("\nView3D per frame: " + string.Join(", ", t.Spent.Select(kv => $"{kv.Key} {kv.Value / n:0.00} ms")));
                return sb.ToString();
            }
            case "goto":
                if (a.Length < 4) return "goto needs X Y Z";
                Root.Screen?.Jump(new P3(I(a[1]), I(a[2]), I(a[3])));
                await Frames(2);
                return null;
            case "level":
                Root.Map?.SetZ(I(a[1]));
                await Frames(2);
                return null;
            case "wait": Busy = true; await Ms(I(a[1])); Busy = false; return null;
            case "tick":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                f.Advance(I(a[1]));
                await Frames(2);
                return $"now {f.Date}";
            }
            case "dwarf":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                Dwarf d = f.Living.FirstOrDefault(x => x.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"no dwarf '{rest}'");
                Root.Screen.SelectThing(d);
                Root.Screen.Jump(f.World.Pos(d.Cell));
                await Frames(2);
                return $"{d.Name}: {d.Activity}";
            }
            case "quit":
                // Ironman, as when the window is closed.
                if (Root.Fortress != null && !Profile.Scratch) Profile.SaveFortress(Root.Fortress);
                GetTree().Quit();
                return "bye";
            case "caravan":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                FactionDef fd = Fortress.FactionDefs.FirstOrDefault(x => x.Key.StartsWith(a.Length > 1 ? a[1] : "low")) ?? throw new Exception("caravan lowlands|mountainhome");
                Caravan cv = f.SendCaravan(fd);
                Busy = true;
                for (int k = 0; k < 400 && cv.State == CaravanState.Coming; k++) { f.Advance(25); await Frames(1); }
                Busy = false;
                return $"caravan from {fd.Name}: {cv.State}";
            }
            case "raid":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                f.LaunchRaid(a.Length > 1 ? I(a[1]) : 4);
                return "raid launched";
            }
            case "mood":
            {
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                Dwarf d = f.Living.FirstOrDefault(x => rest.Length == 0 || x.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("no such dwarf");
                f.StartMood(d);
                return $"{d.Name} is in a {d.Strange} mood, wanting {string.Join(", ", d.MoodNeeds)}";
            }
            case "arm":
            {
                // (debug) iron breastplate, helm, war hammer and shield for one dwarf, or everyone: "arm all".
                Fortress f = Root.Fortress ?? throw new Exception("no fortress");
                var who = rest == "all" ? f.Living.ToList() : f.Living.Where(x => rest.Length == 0 || x.Name.Contains(rest, StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
                if (who.Count == 0) throw new Exception("no such dwarf");
                byte iron = Tables.Mi("iron");
                foreach (Dwarf d in who)
                {
                    Item Give(ItemKind k) { Item it = f.Spawn(k, iron, d.Cell); f.PickUp(it, d); return it; }
                    d.Armor = Give(ItemKind.Armor).Id;
                    d.Helm = Give(ItemKind.Helm).Id;
                    d.Weapon = Give(ItemKind.Weapon).Id;
                    d.Shield = Give(ItemKind.Shield).Id;
                }
                return $"armed {who.Count}";
            }
            case "freeze": Frozen = a.Length < 2 || a[1] != "off"; return $"freeze {(Frozen ? "on" : "off")}";
        }
        throw new Exception($"unknown command '{a[0]}' (try help)");
    }

    private IEnumerable<Button> Buttons()
    {
        var all = new List<Button>();
        void Walk(Node n)
        {
            if (n is CanvasItem ci && !ci.IsVisibleInTree()) return;
            if (n is Button b) all.Add(b);
            foreach (Node ch in n.GetChildren()) Walk(ch);
        }
        Walk(GetTree().Root);
        // Overlays and dialogs come last in the tree: prefer them.
        all.Reverse();
        return all.Where(b => GetViewport().GetVisibleRect().Intersects(b.GetGlobalRect()));
    }

    private string ListUi()
    {
        var sb = new StringBuilder();
        foreach (Button b in Buttons().Reverse())
        {
            Vector2 c = b.GetGlobalRect().GetCenter();
            sb.AppendLine($"  [{b.Text}] at {c.X:0},{c.Y:0}{(b.Disabled ? " (disabled)" : "")}{(b.ToggleMode && b.ButtonPressed ? " (on)" : "")}");
        }
        return sb.ToString().TrimEnd();
    }

    private string State()
    {
        var sb = new StringBuilder();
        Fortress f = Root.Fortress;
        if (f == null) { sb.AppendLine($"screen: {Root.Current?.GetType().Name ?? "none"}"); sb.Append(ListUi()); return sb.ToString(); }
        FortressScreen s = Root.Screen;
        MapView m = Root.Map;
        Vector2 cam = m.Cam.Position / MapView.T;
        sb.AppendLine($"{f.Name}: {f.Date}, speed {FortressScreen.Speeds[s.Speed]}x{(s.Paused ? " (paused)" : "")}, {f.Alive} dwarves, wealth {f.Wealth}{(f.Fallen ? ", FALLEN" : "")}");
        sb.AppendLine($"view: {s.DepthName(m.Z)} (z {m.Z}), camera at {(int)cam.X},{(int)cam.Y}, zoom {m.Cam.Zoom.X:0.0}; tool {s.Tool}{(s.BuildDef != null ? " " + s.BuildDef : "")}; overlay {s.Overlay?.GetType().Name ?? "none"}{(s.MenuOpen ? "; menu open" : "")}");
        sb.AppendLine($"stocks: {f.Items.Values.Count(i => i.Kind == ItemKind.Drink)} ale, {f.Items.Values.Count(i => Tables.Matches("food", i))} food, {f.Items.Values.Count(i => i.Kind == ItemKind.Log)} logs, {f.Items.Values.Count(i => i.Kind == ItemKind.Boulder)} boulders; {f.Designated.Count} designations, {f.Buildings.Count(b => !b.Built)} buildings planned");
        if (s.Selected != null) sb.AppendLine("selected: " + s.Selected switch { Dwarf d => $"{d.Name} ({d.Activity}, {d.MoodWord})", Building b => $"{b.D.Name} #{b.Id}{(b.Built ? "" : " (planned)")}, {b.Orders.Count} orders", Zone z => z.Name, P3 p => $"cell {p}", _ => "?" });
        if (s.Alert != null) sb.AppendLine("alert: " + s.Alert);
        foreach (LogEntry e in f.Log.TakeLast(5)) sb.AppendLine($"  log {Fortress.ShortDate(e.Tick)}: {e.Text}");
        sb.AppendLine("dwarves:");
        foreach (Dwarf d in f.Living.Take(30)) { P3 p = f.World.Pos(d.Cell); sb.AppendLine($"  {d.Name,-22} {d.Activity,-26} at {p.X},{p.Y},{p.Z}  {d.MoodWord}"); }
        sb.Append(AsciiMap(18));
        return sb.ToString();
    }

    /// <summary>The level in view around the camera: # wall, . floor, space air, ~ water, T tree, X&lt;&gt; stairs, ^ ramp, ? unknown; d dwarf, b building, i item, D designation.</summary>
    private string AsciiMap(int r)
    {
        Fortress f = Root.Fortress;
        if (f == null) return "";
        MapView m = Root.Map;
        World w = f.World;
        int cx = (int)(m.Cam.Position.X / MapView.T), cy = (int)(m.Cam.Position.Y / MapView.T);
        var dw = new HashSet<int>(f.Living.Select(d => d.Cell));
        var sb = new StringBuilder();
        sb.AppendLine($"map z {m.Z}, x {cx - r}..{cx + r}, y {cy - r / 2}..{cy + r / 2} (d dwarf, b building, i item, D designated, ? unknown):");
        for (int y = cy - r / 2; y <= cy + r / 2; y++)
        {
            var line = new StringBuilder();
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (!w.In(x, y, m.Z)) { line.Append(' '); continue; }
                int i = w.Idx(x, y, m.Z);
                char ch;
                if (w.Has(i, CellFlag.Hidden)) ch = '?';
                else if (dw.Contains(i)) ch = 'd';
                else if (f.BuildingAt.ContainsKey(i)) ch = 'b';
                else if (w.Desigs[i] != Desig.None) ch = 'D';
                else if (f.ItemsAt.ContainsKey(i)) ch = 'i';
                else ch = w.Shapes[i] switch
                {
                    Shape.Wall => '#', Shape.Floor => w.Has(i, CellFlag.Shrub) ? '"' : '.', Shape.Empty => ' ', Shape.Tree => 'T', Shape.Water => '~', Shape.Magma => '&',
                    Shape.StairUD => 'X', Shape.StairUp => '<', Shape.StairDown => '>', Shape.Ramp => '^', _ => '?',
                };
                line.Append(ch);
            }
            sb.AppendLine(line.ToString());
        }
        return sb.ToString().TrimEnd();
    }
}
