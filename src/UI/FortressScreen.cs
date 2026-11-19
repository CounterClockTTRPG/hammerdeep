using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

public enum Tool { Select, Dig, Channel, StairUD, StairDown, StairUp, Chop, Gather, Undesignate, Stockpile, Farm, Meeting, Pasture, Build, Deconstruct }

/// <summary>
/// Playing a fortress: the clock (pause, 1x, 3x, 10x), the camera and the
/// level in view, the tools (designate, zone, build, select), and the HUD
/// around the map -- top bar, toolbar, info panel, log -- plus the screens
/// that open over it (<see cref="FortressScreen"/> partials in Panels.cs).
/// </summary>
public partial class FortressScreen : Control
{
    public Root Root;
    public Fortress F;
    public MapView Map;

    public const float BaseTps = 20;
    public static readonly int[] Speeds = { 0, 1, 3, 10 };
    public int Speed = 1;
    public bool Paused => Speed == 0 || Overlay != null || _menu != null;
    private int _lastSpeed = 1;
    private double _acc;
    private int _season;
    public Tool Tool = Tool.Select;
    private int _shownZ = -1;
    public string BuildDef;
    private P3? _dragFrom;
    private bool _panning;
    private readonly HashSet<Key> _held = new();
    public string Alert;
    public Dwarf Follow;
    /// <summary>The thing the info panel shows.</summary>
    public object Selected;

    private UITheme.Drawn _names;
    public readonly UndoStack Undos = new();
    private Button _undo;

    public void UndoLast()
    {
        string what = Undos.Undo(F);
        if (what == null) { Toast("Nothing to undo."); Sfx.Deny(); return; }
        Toast($"Undone: {what}.");
        Sfx.Place();
        RefreshInfo(true);
        RefreshHud();
    }
    private Button _iso;

    /// <summary>A small triangle for the level buttons, drawn rather than taken from a font.</summary>
    private static ImageTexture Arrow(bool up)
    {
        var img = Image.CreateEmpty(14, 10, false, Image.Format.Rgba8);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 14; x++)
            {
                int row = up ? y : 9 - y;
                bool inside = Math.Abs(x - 6.5f) <= row * 0.72f + 0.5f;
                img.SetPixel(x, y, inside ? new Color(0.98f, 0.8f, 0.36f) : new Color(0, 0, 0, 0));
            }
        return ImageTexture.CreateFromImage(img);
    }

    public void ToggleIso()
    {
        Profile.Current.Isometric = !Map.Iso;
        Profile.Current.Save();
        Map.SetIso(Profile.Current.Isometric);
        RefreshHud();
    }
    private Button _alarm;

    public void ToggleAlarm()
    {
        F.Alarm = !F.Alarm;
        F.Say(F.Alarm ? "The alarm sounds! Everyone to the meeting hall." : "The alarm is lifted.", F.Alarm ? LogKind.Bad : LogKind.Info);
        RefreshHud();
    }
    private Label _name, _date, _depth, _toolHint;
    private RichTextLabel _stats;
    private readonly Button[] _speedButtons = new Button[4];
    private VBoxContainer _info, _log, _toolbar;
    private PanelContainer _infoPanel, _alertPanel, _buildPanel;
    private Label _alertText;
    public Control Overlay;
    private Control _menu;
    private string _infoSig, _logSig;
    private double _refresh;
    private readonly Dictionary<Tool, Button> _toolButtons = new();

    public override void _Ready()
    {
        FortressScreen.Anchor(this, 0, 0, 1, 1, 0, 0, 0, 0);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = UITheme.Theme;
        _season = F.Season;
        BuildTop();
        BuildToolbar();
        BuildInfo();
        BuildLog();
        BuildAlert();
        if (F.TutorialStep == 0) Speed = 0;
        BuildTutorial();
        Map.SetIso(Profile.Current.Isometric);
        var names = new UITheme.Drawn { OnDraw = DrawNames, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(names);
        Anchor(names, 0, 0, 1, 1, 0, 0, 0, 0);
        MoveChild(names, 0);
        _names = names;
        Pick(Tool.Select);
    }

    // ---- layout -------------------------------------------------------------------------

    private void BuildTop()
    {
        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", UITheme.Box(UITheme.PanelFill, UITheme.Edge, 1, 0, 10));
        AddChild(bar);
        Anchor(bar, 0, 0, 1, 0, 0, 0, 0, 42);
        var row = UITheme.H(14);
        bar.AddChild(row);
        _name = UITheme.L(F.Name, 22, UITheme.Gold, UITheme.Title);
        row.AddChild(_name);
        _date = UITheme.L("", 16, UITheme.Text);
        _date.CustomMinimumSize = new Vector2(230, 0);
        row.AddChild(_date);
        var speeds = UITheme.H(3);
        string[] names = { "II", ">", ">>", ">>>" };
        string[] tips = { "Pause (Space)", "Normal speed (1)", "Fast (2)", "Fastest (3)" };
        for (int k = 0; k < 4; k++)
        {
            int s = k;
            Button b = UITheme.B(names[k], () => SetSpeed(s), 14, tips[k]);
            b.CustomMinimumSize = new Vector2(40, 0);
            b.ToggleMode = true;
            _speedButtons[k] = b;
            speeds.AddChild(b);
        }
        row.AddChild(speeds);
        _stats = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.Off, MouseFilter = MouseFilterEnum.Ignore };
        _stats.AddThemeFontOverride("normal_font", UITheme.Body);
        _stats.AddThemeFontSizeOverride("normal_font_size", 15);
        _stats.AddThemeColorOverride("default_color", UITheme.Dim);
        _stats.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _stats.HorizontalAlignment = HorizontalAlignment.Right;
        _stats.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(_stats);
        // Up and down a level, beside where the level is named.
        Button up = UITheme.B("", () => ChangeZ(1), 14, "Up a level (<, Page Up, Shift+wheel)");
        up.Icon = Arrow(true);
        up.CustomMinimumSize = new Vector2(34, 0);
        Button down = UITheme.B("", () => ChangeZ(-1), 14, "Down a level (>, Page Down, Shift+wheel)");
        down.Icon = Arrow(false);
        down.CustomMinimumSize = new Vector2(34, 0);
        _depth = UITheme.L("", 16, UITheme.Gold, UITheme.LabelFont);
        _depth.CustomMinimumSize = new Vector2(130, 0);
        _depth.HorizontalAlignment = HorizontalAlignment.Center;
        row.AddChild(down);
        row.AddChild(_depth);
        row.AddChild(up);
        _iso = UITheme.B("Isometric", ToggleIso, 14, "Switch between the top-down and the isometric view (F2)");
        _iso.ToggleMode = true;
        row.AddChild(_iso);
        _alarm = UITheme.B("Alarm", ToggleAlarm, 14, "Sound the alarm: civilians drop everything and shelter in the meeting hall (A)");
        _alarm.ToggleMode = true;
        row.AddChild(_alarm);
        row.AddChild(UITheme.B("Menu", OpenMenu, 14, "Esc"));
    }

    private void BuildToolbar()
    {
        var panel = new PanelContainer();
        panel.Position = new Vector2(0, 46);
        panel.CustomMinimumSize = new Vector2(176, 0);
        AddChild(panel);
        _toolbar = UITheme.V(3);
        panel.AddChild(_toolbar);
        void Section(string t) => _toolbar.AddChild(UITheme.L(t, 13, UITheme.Bronze, UITheme.Title));
        void ToolB(Tool t, string label, string key, string tip)
        {
            Button b = UITheme.B($"{label}", () => Pick(t), 14, tip);
            b.Alignment = HorizontalAlignment.Left;
            b.ToggleMode = true;
            b.Text = $"{key}  {label}";
            _toolButtons[t] = b;
            _toolbar.AddChild(b);
        }
        Section("Designate");
        ToolB(Tool.Dig, "Dig", "D", "Mine out rock and soil, leaving floor.");
        ToolB(Tool.Channel, "Channel", "H", "Dig away the floor, leaving a ramp below.");
        ToolB(Tool.StairUD, "Up/down stairs", "X", "Stairs going both ways: dig a shaft of these between levels.");
        ToolB(Tool.StairDown, "Down stair", "J", "The top of a shaft.");
        ToolB(Tool.StairUp, "Up stair", "U", "The bottom of a shaft.");
        ToolB(Tool.Chop, "Fell trees", "T", "Woodcutters fell trees for logs.");
        ToolB(Tool.Gather, "Gather plants", "G", "Pick wild shrubs for fruit and seed.");
        ToolB(Tool.Undesignate, "Undesignate", "R", "Drag a box to clear designations in it.");
        _undo = Small("^Z  Undo", UndoLast);
        _undo.TooltipText = "Take back the last designation, zone or building placed (Ctrl+Z).";
        _toolbar.AddChild(_undo);
        Section("Zones");
        ToolB(Tool.Stockpile, "Stockpile", "P", "Where hauled goods are kept.");
        ToolB(Tool.Farm, "Farm plot", "F", "Soil to grow crops in: caves for dimcaps, sky for wheat and brambles.");
        ToolB(Tool.Meeting, "Meeting hall", "M", "Where idle dwarves gather and talk.");
        ToolB(Tool.Pasture, "Pasture", "E", "Grass under the sky where grazing animals are kept.");
        Section("Build");
        ToolB(Tool.Build, "Build...", "B", "Workshops, furniture and constructions.");
        ToolB(Tool.Deconstruct, "Remove", "N", "Take down a building or remove a zone.");
        Section("View");
        _toolbar.AddChild(Small("V  Dwarves", () => Open(new DwarfList(this))));
        _toolbar.AddChild(Small("L  Labors", () => Open(new LaborGrid(this))));
        _toolbar.AddChild(Small("K  Military", () => Open(new MilitaryPanel(this))));
        _toolbar.AddChild(Small("Z  Animals", () => Open(new AnimalsPanel(this))));
        _toolbar.AddChild(Small("W  Chronicle", () => Open(new Chronicle(this))));
        _toolbar.AddChild(Small("O  World", () => Open(new WorldPanel(this))));
        _toolbar.AddChild(Small("I  Stocks", () => Open(new Stocks(this))));
        _toolbar.AddChild(Small("?  Help", () => Open(new HelpPanel(this))));
        _toolHint = UITheme.Wrap("", 12, UITheme.Dim, 150);
        _toolbar.AddChild(_toolHint);
    }

    private Button Small(string text, Action a)
    {
        Button b = UITheme.B(text, a, 14);
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    private void BuildInfo()
    {
        _infoPanel = new PanelContainer();
        AddChild(_infoPanel);
        Anchor(_infoPanel, 1, 0, 1, 1, -356, 46, -4, -8);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(330, 0), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _infoPanel.AddChild(scroll);
        _info = UITheme.V(4);
        _info.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_info);
        _infoPanel.Visible = false;
    }

    private void BuildLog()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.05f, 0.045f, 0.04f, 0.82f), UITheme.Edge, 1, 4, 8));
        panel.CustomMinimumSize = new Vector2(640, 0);
        AddChild(panel);
        Anchor(panel, 0, 1, 0, 1, 184, -8, 824, -8);
        panel.GrowVertical = GrowDirection.Begin;
        _log = UITheme.V(1);
        panel.AddChild(_log);
    }

    private void BuildAlert()
    {
        _alertPanel = new PanelContainer();
        _alertPanel.AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.16f, 0.07f, 0.04f, 0.96f), UITheme.Ember, 2, 4, 12));
        _alertPanel.CustomMinimumSize = new Vector2(600, 0);
        AddChild(_alertPanel);
        Anchor(_alertPanel, 0.5f, 0, 0.5f, 0, -300, 54, 300, 54);
        var row = UITheme.H(10);
        _alertPanel.AddChild(row);
        _alertText = UITheme.Wrap("", 16, UITheme.Text, 440);
        _alertText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(_alertText);
        row.AddChild(UITheme.B("Go there", () => { if (_alertCell >= 0) Jump(F.World.Pos(_alertCell)); }, 14));
        row.AddChild(UITheme.B("Resume", () => { _alertPanel.Visible = false; if (Speed == 0) SetSpeed(_lastSpeed); }, 14));
        _alertPanel.Visible = false;
    }

    private int _alertCell = -1;

    // ---- the tutorial -------------------------------------------------------------------

    private PanelContainer _tutPanel;
    private Label _tutStep, _tutTitle, _tutText, _tutHow;
    private Button _tutNext, _tutSkip, _tutEnd;
    private UITheme.Drawn _tutTrack;
    private readonly TutorialWatch _watch = new();
    private int _tutShown = -2;
    private double _glow;

    private void BuildTutorial()
    {
        _tutPanel = new PanelContainer();
        _tutPanel.AddThemeStyleboxOverride("panel", UITheme.Box(new Color(0.09f, 0.075f, 0.05f, 0.97f), UITheme.Gold, 2, 6, 14));
        AddChild(_tutPanel);
        Anchor(_tutPanel, 1, 1, 1, 1, -770, -8, -364, -8);
        _tutPanel.GrowVertical = GrowDirection.Begin;
        var v = UITheme.V(6);
        _tutPanel.AddChild(v);
        HBoxContainer head = UITheme.H(10);
        var track = new UITheme.Drawn { CustomMinimumSize = new Vector2(Tutorial.Steps.Count * 14, 10), MouseFilter = MouseFilterEnum.Ignore };
        track.OnDraw = c =>
        {
            for (int k = 0; k < Tutorial.Steps.Count; k++)
            {
                var r = new Rect2(k * 14, 2, 10, 6);
                Color col = k < F.TutorialStep ? UITheme.Bronze : k == F.TutorialStep ? UITheme.Gold : new Color(UITheme.Faint, 0.6f);
                c.DrawRect(r, col);
            }
        };
        head.AddChild(track);
        _tutTrack = track;
        _tutStep = UITheme.L("", 13, UITheme.Dim, UITheme.Body);
        head.AddChild(_tutStep);
        v.AddChild(head);
        _tutTitle = UITheme.Heading("", 20);
        v.AddChild(_tutTitle);
        _tutText = UITheme.Wrap("", 15, UITheme.Text, 380);
        v.AddChild(_tutText);
        _tutHow = UITheme.Wrap("", 14, UITheme.Gold, 380);
        v.AddChild(_tutHow);
        HBoxContainer h = UITheme.H(8);
        _tutNext = UITheme.B("Next", () => { Tutorial.Next(F); ShowTutorial(); }, 15);
        h.AddChild(_tutNext);
        _tutSkip = UITheme.B("Skip this step", () => { Tutorial.Next(F); ShowTutorial(); }, 12);
        h.AddChild(_tutSkip);
        _tutEnd = UITheme.B("End the tutorial", () => { Tutorial.End(F); ShowTutorial(); }, 12);
        h.AddChild(_tutEnd);
        v.AddChild(h);
        _watch.StartZ = _watch.Z = Map.Z;
        ShowTutorial();
    }

    private void ShowTutorial()
    {
        TutorialStep s = Tutorial.Current(F);
        _tutPanel.Visible = s != null;
        _tutShown = F.TutorialStep;
        _watch.StartZ = Map.Z;
        _watch.SpeedChanged = false;
        foreach (Button b in _toolButtons.Values) b.Modulate = Colors.White;
        if (s == null) return;
        _tutStep.Text = $"Step {F.TutorialStep + 1} of {Tutorial.Steps.Count}";
        _tutTrack.QueueRedraw();
        _tutSkip.Visible = s.Done != null;
        _tutEnd.Visible = F.TutorialStep < Tutorial.Steps.Count - 1;
        _tutTitle.Text = s.Title;
        _tutText.Text = s.Text;
        _tutHow.Text = s.How;
        _tutNext.Visible = s.Done == null;
        _tutNext.Text = F.TutorialStep == Tutorial.Steps.Count - 1 ? "Finish" : "Next";
    }

    private void TickTutorial(double delta)
    {
        if (F.TutorialStep < 0) { if (_tutPanel.Visible) ShowTutorial(); return; }
        _watch.Z = Map.Z;
        if (_tutShown != F.TutorialStep) ShowTutorial();
        if (Tutorial.Advance(F, _watch)) { Sfx.Good(); ShowTutorial(); }
        // The tool the step wants glows in the toolbar.
        _glow += delta;
        TutorialStep s = Tutorial.Current(F);
        float pulse = 0.75f + 0.25f * Mathf.Sin((float)_glow * 5);
        foreach (var kv in _toolButtons) kv.Value.Modulate = s?.Glow == kv.Key ? new Color(1.25f, 1.1f, 0.6f) * pulse + new Color(0, 0, 0, 1 - pulse) : Colors.White;
        foreach (Node n in _toolbar.GetChildren())
            if (n is Button b && !_toolButtons.ContainsValue(b))
                b.Modulate = s?.GlowButton != null && b.Text.Contains(s.GlowButton) ? new Color(1.25f, 1.1f, 0.6f) * pulse + new Color(0, 0, 0, 1 - pulse) : Colors.White;
    }

    /// <summary>Anchor a control (already added) to fractions of the screen, with pixel offsets.</summary>
    public static new void Anchor(Control c, float l, float t, float r, float b, float ol, float ot, float or, float ob)
    {
        c.AnchorLeft = l; c.AnchorTop = t; c.AnchorRight = r; c.AnchorBottom = b;
        c.OffsetLeft = ol; c.OffsetTop = ot; c.OffsetRight = or; c.OffsetBottom = ob;
    }

    // ---- the clock ----------------------------------------------------------------------

    public void SetSpeed(int s)
    {
        if (s != Speed) _watch.SpeedChanged = true;
        if (s > 0) _lastSpeed = s;
        Speed = s;
        if (s > 0) _alertPanel.Visible = false;
    }

    public override void _Process(double delta)
    {
        if (F == null) return;
        Pan(delta);
        if (AgentRunner.Running && !Paused && !F.Fallen)
        {
            _acc += delta * BaseTps * Speeds[Speed];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int steps = 0;
            while (_acc >= 1 && sw.Elapsed.TotalMilliseconds < 14)
            {
                F.Step();
                _acc -= 1;
                steps++;
                if (F.PauseFor != null) break;
            }
            // Falling behind is fine; catching up in a burst is not.
            if (_acc > 30) _acc = 0;
        }
        if (F.PauseFor != null)
        {
            LogEntry last = F.Log.LastOrDefault(l => l.Kind == LogKind.Alert);
            ShowAlert(F.PauseFor, last?.Cell ?? -1);
            F.PauseFor = null;
        }
        if (F.Season != _season)
        {
            _season = F.Season;
            if (!Profile.Scratch) Profile.SaveFortress(F);
        }
        _names.QueueRedraw();
        TickTutorial(delta);
        // The music follows the hold: battle when foes are close, the deep below the caverns, the hall otherwise.
        bool fighting = F.Creatures.Any(c => !c.Dead && (c.D.Hostile || c.Angry) && !c.Tame && F.World.Pos(c.Cell).Dist(F.Embark) < 40) && F.Living.Any(d => d.Task?.Kind is TaskKind.Fight or TaskKind.Flee);
        Music.Instance?.Play(fighting ? MusicMood.Battle : Map.Z <= F.CavernZ + 2 ? MusicMood.Deep : MusicMood.Hall);
        if (F.Fallen && Overlay is not FallenPanel) Open(new FallenPanel(this));
        if (Follow != null && !Follow.Dead)
        {
            Map.Cam.Position = Map.DwarfPos(Follow);
            Map.SetZ(F.World.Pos(Follow.Cell).Z);
        }
        _refresh -= delta;
        if (_refresh <= 0 || Map.Z != _shownZ) { _refresh = 0.25; _shownZ = Map.Z; RefreshHud(); }
    }

    public void ShowAlert(string text, int cell)
    {
        if (Profile.Current.PauseOnAlerts && Speed > 0) { _lastSpeed = Speed; Speed = 0; }
        _alertText.Text = text;
        _alertCell = cell;
        _alertPanel.Visible = true;
        Sfx.Alert();
    }

    // ---- the HUD ------------------------------------------------------------------------

    public string DepthName(int z)
    {
        int d = z - F.World.SurfaceZ;
        return d == 0 ? "Surface" : d > 0 ? $"Surface +{d}" : $"Depth {-d}";
    }

    private void RefreshHud()
    {
        _date.Text = $"{F.Date}, {TimeOfDay()}";
        for (int k = 0; k < 4; k++) _speedButtons[k].SetPressedNoSignal(Speed == k);
        int idle = F.Living.Count(d => d.Activity == "Idle");
        bool mixed = F.Living.Any(d => d.Race is not (null or "dwarf"));
        string Fig(int n, string word, Color? c = null) => $"[color=#{(c ?? UITheme.Text).ToHtml(false)}]{n}[/color] {word}";
        int ale = F.Items.Values.Count(i => i.Kind == ItemKind.Drink), food = F.Items.Values.Count(i => Tables.Matches("food", i));
        string stats = $"{Fig(F.Alive, mixed ? "folk" : "dwarves")}, {Fig(idle, "idle")}      {Fig(ale, "ale", ale < F.Alive * 2 ? UITheme.Ember : null)}      {Fig(food, "food", food < F.Alive * 2 ? UITheme.Ember : null)}      wealth {Fig(F.Wealth, "")}";
        _depth.Text = $"{DepthName(Map.Z)}  (z {Map.Z})";
        foreach (var kv in _toolButtons) kv.Value.SetPressedNoSignal(kv.Key == Tool);
        _alarm.SetPressedNoSignal(F.Alarm);
        _iso.SetPressedNoSignal(Map.Iso);
        _undo.Disabled = Undos.Count == 0;
        _undo.TooltipText = Undos.Next != null ? $"Undo {Undos.Next} (Ctrl+Z)" : "Nothing to undo";
        _alarm.Text = F.Alarm ? "ALARM!" : "Alarm";
        _alarm.AddThemeColorOverride("font_color", F.Alarm ? UITheme.Bad : UITheme.Text);
        int foes = F.Hostiles.Count();
        if (foes > 0) stats = $"[color=#{UITheme.Bad.ToHtml(false)}]{foes} foes about[/color]      " + stats;
        _stats.Text = $"[right]{stats}[/right]";
        RefreshLog();
        RefreshInfo();
    }

    private string TimeOfDay()
    {
        float f = F.DayFraction;
        return f < 0.25f ? "night" : f < 0.35f ? "dawn" : f < 0.7f ? "day" : f < 0.8f ? "dusk" : "night";
    }

    private void RefreshLog()
    {
        var last = F.Log.Skip(Math.Max(0, F.Log.Count - 7)).ToList();
        string sig = last.Count + ":" + (last.LastOrDefault()?.Tick ?? 0) + (last.LastOrDefault()?.Text ?? "");
        if (sig == _logSig) return;
        _logSig = sig;
        foreach (Node n in _log.GetChildren()) n.QueueFree();
        foreach (LogEntry e in last)
        {
            Color c = e.Kind switch { LogKind.Good => UITheme.Good, LogKind.Bad => UITheme.Ember, LogKind.Alert => UITheme.Bad, _ => UITheme.Dim };
            var b = new Button { Text = $"{Fortress.ShortDate(e.Tick)}  {e.Text}", Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = FocusModeEnum.None, ClipText = true };
            b.CustomMinimumSize = new Vector2(620, 0);
            b.AddThemeFontSizeOverride("font_size", 14);
            b.AddThemeColorOverride("font_color", c);
            b.AddThemeColorOverride("font_hover_color", UITheme.Gold);
            b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            int cell = e.Cell;
            if (cell >= 0) b.Pressed += () => Jump(F.World.Pos(cell));
            _log.AddChild(b);
        }
    }

    public void RefreshInfo(bool force = false)
    {
        string sig = InfoSignature();
        if (!force && sig == _infoSig) return;
        _infoSig = sig;
        foreach (Node n in _info.GetChildren()) n.QueueFree();
        _infoPanel.Visible = FillInfo(_info);
    }

    // ---- input --------------------------------------------------------------------------

    public void Pick(Tool t)
    {
        if (t != Tool.Select && Selected != null && !(t == Tool.Build && Selected is Building)) SelectThing(null);
        Tool = t;
        _dragFrom = null;
        Map.Box = null;
        Map.Ghost = null;
        if (t != Tool.Build) BuildDef = null;
        if (_buildPanel != null) { _buildPanel.QueueFree(); _buildPanel = null; }
        if (t == Tool.Build) { OpenBuildPalette(); }
        _toolHint.Text = t switch
        {
            Tool.Select => "Click a dwarf, building or zone. Arrow keys or middle-drag to pan; wheel zooms; < > or Shift+wheel change level.",
            Tool.Build => BuildDef == null ? "Choose what to build." : $"Click to place the {Tables.Build[BuildDef].Name.ToLower()}. Right-click to stop.",
            Tool.Deconstruct => "Click a building or zone to remove it.",
            _ => "Drag a box. Change level mid-drag to make it 3D. Right-click to stop.",
        };
        Map.BoxColor = t switch
        {
            Tool.Undesignate or Tool.Deconstruct => new Color(1f, 0.4f, 0.35f, 0.3f),
            Tool.Stockpile => new Color(0.95f, 0.82f, 0.45f, 0.3f),
            Tool.Farm => new Color(0.55f, 0.85f, 0.35f, 0.3f),
            Tool.Meeting => new Color(0.55f, 0.7f, 1f, 0.3f),
            Tool.Pasture => new Color(0.85f, 0.7f, 0.45f, 0.3f),
            _ => new Color(1, 0.85f, 0.3f, 0.3f),
        };
        RefreshHud();
    }

    private void OpenBuildPalette()
    {
        _buildPanel = new PanelContainer();
        _buildPanel.Position = new Vector2(182, 46);
        AddChild(_buildPanel);
        var v = UITheme.V(3);
        _buildPanel.AddChild(v);
        v.AddChild(UITheme.Heading("Build", 18));
        foreach (BuildKind kind in new[] { BuildKind.Workshop, BuildKind.Furniture, BuildKind.Construction })
        {
            v.AddChild(UITheme.L(kind switch { BuildKind.Workshop => "Workshops", BuildKind.Furniture => "Furniture (made in shops first)", _ => "Constructions" }, 13, UITheme.Bronze, UITheme.Title));
            foreach (BuildDef d in Tables.Buildings.Where(b => b.Kind == kind))
            {
                string cost = string.Join(", ", d.Cost.Select(c => (c.Count > 1 ? c.Count + " " : "") + Tables.NeedName(c.Tag)));
                int have = CountFor(d);
                Button b = UITheme.B($"{d.Key}  {d.Name}", () => ChooseBuild(d.Id), 14, $"{d.Help}\nNeeds: {cost}");
                b.Alignment = HorizontalAlignment.Left;
                b.CustomMinimumSize = new Vector2(260, 0);
                if (have == 0) b.AddThemeColorOverride("font_color", UITheme.Faint);
                v.AddChild(b);
            }
        }
    }

    private int CountFor(BuildDef d)
    {
        Need n = d.Cost[0];
        return F.Items.Values.Count(i => i.Free && Tables.Matches(n.Tag, i));
    }

    private void ChooseBuild(string id)
    {
        BuildDef = id;
        if (_buildPanel != null) { _buildPanel.QueueFree(); _buildPanel = null; }
        Map.Ghost = id;
        _toolHint.Text = $"Click to place: {Tables.Build[id].Name}. Right-click to stop.";
    }

    public void Jump(P3 p)
    {
        Follow = null;
        Map.CenterOn(p);
    }

    private void Pan(double delta)
    {
        float speed = 700f / Map.Cam.Zoom.X * (float)delta * (_held.Contains(Key.Shift) ? 2.5f : 1);
        Vector2 v = Vector2.Zero;
        if (_held.Contains(Key.Up)) v.Y -= 1;
        if (_held.Contains(Key.Down)) v.Y += 1;
        if (_held.Contains(Key.Left)) v.X -= 1;
        if (_held.Contains(Key.Right)) v.X += 1;
        if (v != Vector2.Zero) { Follow = null; Map.Cam.Position += Map.Iso ? Map.ScreenDeltaToLocal(v * speed * Map.Cam.Zoom.X) : v * speed; ClampCam(); }
    }

    private void ClampCam()
    {
        Vector2 max = new(F.World.W * MapView.T, F.World.H * MapView.T);
        Map.Cam.Position = Map.Cam.Position.Clamp(Vector2.Zero, max);
    }

    public void ChangeZ(int dz)
    {
        Follow = null;
        Map.SetZ(Map.Z + dz);
        if (_dragFrom != null) UpdateDrag(GetViewport().GetMousePosition());
        RefreshHud();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k)
        {
            if (k.Pressed) _held.Add(k.Keycode); else _held.Remove(k.Keycode);
            if (!k.Pressed || k.Echo && k.Keycode is not (Key.Pageup or Key.Pagedown or Key.Comma or Key.Period)) return;
            if (HandleKey(k)) GetViewport().SetInputAsHandled();
            return;
        }
        if (Overlay != null || _menu != null) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown)
            {
                if (!mb.Pressed) return;
                int dir = mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (mb.ShiftPressed || mb.CtrlPressed) ChangeZ(dir);
                else Zoom(dir, mb.Position);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (mb.ButtonIndex == MouseButton.Middle) { _panning = mb.Pressed; return; }
            if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                if (_dragFrom != null) { _dragFrom = null; Map.Box = null; }
                else if (Tool != Tool.Select) Pick(Tool.Select);
                else { Selected = null; Follow = null; Map.SelDwarf = Map.SelBuilding = Map.SelZone = -1; RefreshInfo(true); }
                return;
            }
            if (mb.ButtonIndex == MouseButton.Left) Click(mb);
            return;
        }
        if (e is InputEventMouseMotion mm)
        {
            if (_panning) { Map.Cam.Position -= Map.ScreenDeltaToLocal(mm.Relative); Follow = null; ClampCam(); }
            Map.Hover = Map.CellAt(mm.Position);
            if (Map.Ghost != null && Map.Hover is P3 h) Map.GhostOk = F.CanPlace(Map.Ghost, h.X, h.Y, h.Z) == null;
            if (_dragFrom != null) UpdateDrag(mm.Position);
        }
    }

    private void Zoom(int dir, Vector2 at)
    {
        float z = Math.Clamp(Map.Cam.Zoom.X * (dir > 0 ? 1.2f : 1 / 1.2f), 0.5f, 6f);
        Vector2 before = Map.GetGlobalTransformWithCanvas().AffineInverse() * at;
        Map.Cam.Zoom = new Vector2(z, z);
        Map.Cam.ForceUpdateScroll();
        Vector2 after = Map.GetGlobalTransformWithCanvas().AffineInverse() * at;
        Map.Cam.Position += before - after;
        ClampCam();
    }

    private bool BoxTool => Tool is Tool.Dig or Tool.Channel or Tool.StairUD or Tool.StairDown or Tool.StairUp or Tool.Chop or Tool.Gather or Tool.Undesignate or Tool.Stockpile or Tool.Farm or Tool.Meeting or Tool.Pasture;

    /// <summary>
    /// Trees and shrubs are marked where they are seen: over open air, the cell under the
    /// mouse is the one shown below it, so felling a wood down the slope just works.
    /// </summary>
    private P3 Aim(Vector2 screen)
    {
        P3 c = Map.CellAt(screen);
        return Tool is Tool.Chop or Tool.Gather ? Map.SeenThrough(c) : c;
    }

    private void UpdateDrag(Vector2 screen)
    {
        P3 c = Aim(screen);
        Map.Box = (_dragFrom.Value, c);
    }

    private void Click(InputEventMouseButton mb)
    {
        P3 c = BoxTool ? Aim(mb.Position) : Map.CellAt(mb.Position);
        if (BoxTool)
        {
            if (mb.Pressed) { _dragFrom = c; UpdateDrag(mb.Position); }
            else if (_dragFrom != null) { ApplyBox(_dragFrom.Value, c); _dragFrom = null; Map.Box = null; }
            return;
        }
        if (!mb.Pressed) return;
        if (!F.World.In(c)) return;
        switch (Tool)
        {
            case Tool.Build:
                if (BuildDef == null) return;
                string why = F.CanPlace(BuildDef, c.X, c.Y, c.Z);
                if (why != null) { Toast($"Cannot build here: {why}."); Sfx.Deny(); return; }
                Building b = F.Place(BuildDef, c.X, c.Y, c.Z);
                Undos.Placed(b);
                Sfx.Place();
                if (b.D.Kind == BuildKind.Workshop && !mb.ShiftPressed) { Pick(Tool.Select); SelectThing(b); }
                return;
            case Tool.Deconstruct:
                Building at = F.BuildingOn(F.World.Idx(c));
                if (at != null) { F.Deconstruct(at); Sfx.Place(); return; }
                Zone z = F.ZoneAt(c.X, c.Y, c.Z);
                if (z != null) { F.RemoveZone(z); Sfx.Place(); }
                return;
            default:
                SelectAt(c);
                return;
        }
    }

    private void ApplyBox(P3 a, P3 b)
    {
        Desig? g = Tool switch
        {
            Tool.Dig => Desig.Dig, Tool.Channel => Desig.Channel, Tool.StairUD => Desig.StairUD, Tool.StairDown => Desig.StairDown,
            Tool.StairUp => Desig.StairUp, Tool.Chop => Desig.Chop, Tool.Gather => Desig.Gather, Tool.Undesignate => Desig.None, _ => null,
        };
        if (g != null)
        {
            int n = Undos.Designate(F, a, b, g.Value);
            if (n == 0) { Toast(g == Desig.None ? "Nothing designated there." : "Nothing there can take that designation."); Sfx.Deny(); return; }
            Sfx.Place();
            bool stairsComing = F.Designated.Any(c => F.World.Desigs[c] is Desig.StairDown or Desig.StairUp or Desig.StairUD);
            if (g != Desig.None && !F.AnyReachable(a, b))
                Toast(stairsComing ? "The dwarves will reach it once the stairs you marked are dug."
                    : a.Z != F.World.SurfaceZ || b.Z != F.World.SurfaceZ
                    ? "Nobody can reach that yet. Dig a way down first: J (down stair) on the level above, U (up stair) or X (up/down) beneath it."
                    : "Nobody can reach that yet: it needs a way in from where the dwarves can walk.");
            return;
        }
        if (a.Z != b.Z) b = new P3(b.X, b.Y, a.Z);
        ZoneKind kind = Tool switch { Tool.Farm => ZoneKind.Farm, Tool.Meeting => ZoneKind.Meeting, Tool.Pasture => ZoneKind.Pasture, _ => ZoneKind.Stockpile };
        if (kind == ZoneKind.Pasture)
        {
            int grass = 0;
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    if (F.World.In(x, y, a.Z) && F.World.Has(F.World.Idx(x, y, a.Z), CellFlag.Grass) && F.World.IsOutside(F.World.Idx(x, y, a.Z))) grass++;
            if (grass == 0) { Toast("A pasture needs grass under the open sky."); Sfx.Deny(); return; }
        }
        string crop = null;
        if (kind == ZoneKind.Farm)
        {
            bool outside = F.World.IsOutside(F.World.Idx(a));
            crop = outside ? "hearthwheat" : "dimcap";
            int soil = 0;
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    if (F.World.In(x, y, a.Z) && F.Farmable(F.World.Idx(x, y, a.Z), Tables.M(crop))) soil++;
            if (soil == 0) { Toast(outside ? "Farm plots need soil under open sky -- or soil underground for cave crops." : "Underground farm plots need a soil floor."); Sfx.Deny(); return; }
        }
        Zone z = F.AddZone(kind, a, b, crop);
        Undos.Zoned(z);
        Sfx.Place();
        SelectThing(z);
        Pick(Tool.Select);
    }

    private void SelectAt(P3 c)
    {
        // What is exactly under the cursor wins: a dwarf or creature on that cell, then a
        // building (a dwarf at work beside it must not hide a whole workshop), then a dwarf or
        // creature within a cell -- a forgiving aim on open ground -- then a zone.
        Dwarf DwarfWithin(int r) => F.Living.Where(x => F.World.Pos(x.Cell).Z == c.Z && F.World.Pos(x.Cell).Dist(c) <= r)
            .OrderBy(x => F.World.Pos(x.Cell).Dist(c)).FirstOrDefault();
        Creature CreatureWithin(int r) => F.Creatures.Where(x => !x.Dead && F.World.Pos(x.Cell).Z == c.Z && F.World.Pos(x.Cell).Dist(c) <= r)
            .OrderBy(x => F.World.Pos(x.Cell).Dist(c)).FirstOrDefault();
        if (DwarfWithin(0) is Dwarf d0) { SelectThing(d0); return; }
        if (CreatureWithin(0) is Creature c0) { SelectThing(c0); return; }
        int cell = F.World.Idx(c);
        Building b = F.BuildingOn(cell);
        if (b != null) { SelectThing(b); return; }
        if (DwarfWithin(1) is Dwarf d1) { SelectThing(d1); return; }
        if (CreatureWithin(1) is Creature c1) { SelectThing(c1); return; }
        Zone z = F.ZoneAt(c.X, c.Y, c.Z);
        if (z != null) { SelectThing(z); return; }
        SelectThing(c);
    }

    public void SelectThing(object o)
    {
        Selected = o;
        Map.SelDwarf = o is Dwarf d ? d.Id : -1;
        Map.SelBuilding = o is Building b ? b.Id : -1;
        Map.SelZone = o is Zone z ? z.Id : -1;
        Map.SelCreature = o is Creature cr ? cr.Id : -1;
        if (o is not Dwarf) Follow = null;
        RefreshInfo(true);
    }

    private bool HandleKey(InputEventKey k)
    {
        if (_menu != null) { if (k.Keycode == Godot.Key.Escape) { CloseMenu(); return true; } return false; }
        if (Overlay != null)
        {
            if (k.Keycode == Godot.Key.Escape || (Overlay is not FallenPanel && OverlayKey(k))) { if (Overlay is not FallenPanel) CloseOverlay(); return true; }
            return false;
        }
        if (k.CtrlPressed && k.Keycode == Godot.Key.Z) { UndoLast(); return true; }
        char ch = (char)k.Unicode;
        if (ch == '<' || k.Keycode == Godot.Key.Pageup) { ChangeZ(1); return true; }
        if (ch == '>' || k.Keycode == Godot.Key.Pagedown) { ChangeZ(-1); return true; }
        switch (k.Keycode)
        {
            case Godot.Key.Escape:
                if (Tool != Tool.Select) Pick(Tool.Select);
                else if (Selected != null) { SelectThing(null); }
                else OpenMenu();
                return true;
            case Godot.Key.Space: SetSpeed(Speed == 0 ? _lastSpeed : 0); return true;
            case Godot.Key.Key1: SetSpeed(1); return true;
            case Godot.Key.Key2: SetSpeed(2); return true;
            case Godot.Key.Key3: SetSpeed(3); return true;
            case Godot.Key.Tab: ShowNamesToggle(); return true;
            case Godot.Key.F1: Open(new HelpPanel(this)); return true;
            case Godot.Key.F2: ToggleIso(); return true;
        }
        if (Tool == Tool.Build && BuildDef == null)
        {
            BuildDef d = Tables.Buildings.FirstOrDefault(b => b.Key == ch.ToString());
            if (d != null) { ChooseBuild(d.Id); return true; }
        }
        switch (char.ToLower(ch))
        {
            case 'd': Pick(Tool.Dig); return true;
            case 'h': Pick(Tool.Channel); return true;
            case 'x': Pick(Tool.StairUD); return true;
            case 'j': Pick(Tool.StairDown); return true;
            case 'u': Pick(Tool.StairUp); return true;
            case 't': Pick(Tool.Chop); return true;
            case 'g': Pick(Tool.Gather); return true;
            case 'r': Pick(Tool.Undesignate); return true;
            case 'p': Pick(Tool.Stockpile); return true;
            case 'f': Pick(Tool.Farm); return true;
            case 'm': Pick(Tool.Meeting); return true;
            case 'b': Pick(Tool.Build); return true;
            case 'n': Pick(Tool.Deconstruct); return true;
            case 'v': Open(new DwarfList(this)); return true;
            case 'l': Open(new LaborGrid(this)); return true;
            case 'i': Open(new Stocks(this)); return true;
            case 'k': Open(new MilitaryPanel(this)); return true;
            case 'z': Open(new AnimalsPanel(this)); return true;
            case 'w': Open(new Chronicle(this)); return true;
            case 'o': Open(new WorldPanel(this)); return true;
            case 'y':
                if (F.Trading is Caravan tc) Open(new TradePanel(this, tc));
                else Toast("No caravan is at the depot.");
                return true;
            case 'e': Pick(Tool.Pasture); return true;
            case 'a': ToggleAlarm(); return true;
            case '?': Open(new HelpPanel(this)); return true;
            case 'c': CenterHome(); return true;
        }
        return false;
    }

    private bool OverlayKey(InputEventKey k)
    {
        char ch = char.ToLower((char)k.Unicode);
        return (Overlay is DwarfList && ch == 'v') || (Overlay is MilitaryPanel && ch == 'k') || (Overlay is AnimalsPanel && ch == 'z') || (Overlay is Chronicle && ch == 'w') || (Overlay is WorldPanel && ch == 'o') || (Overlay is TradePanel && ch == 'y') || (Overlay is LaborGrid && ch == 'l') || (Overlay is Stocks && ch == 'i') || (Overlay is HelpPanel && (ch == '?' || k.Keycode == Godot.Key.F1));
    }

    private void ShowNamesToggle() => Map.ShowNames = !Map.ShowNames;

    /// <summary>Names over dwarves, drawn in screen space so they stay crisp at any zoom.</summary>
    private void DrawNames(UITheme.Drawn c)
    {
        foreach (Dwarf d in F.Living)
        {
            if (!(Map.ShowNames || d.Id == Map.SelDwarf) || F.World.Pos(d.Cell).Z != Map.Z) continue;
            Vector2 at = Map.GetGlobalTransformWithCanvas() * Map.DwarfPos(d) + new Vector2(0, Map.Iso ? -26 : -9 * Map.Cam.Zoom.X / 2);
            if (at.X < 0 || at.Y < 0 || at.X > Size.X || at.Y > Size.Y) continue;
            Font f = UITheme.LabelFont;
            Vector2 sz = f.GetStringSize(d.FirstName, HorizontalAlignment.Left, -1, 14);
            var r = new Rect2(at - new Vector2(sz.X / 2 + 4, sz.Y + 2), sz + new Vector2(8, 3));
            c.DrawRect(r, new Color(0.05f, 0.04f, 0.03f, 0.75f));
            c.DrawString(f, new Vector2(r.Position.X + 4, r.End.Y - 5), d.FirstName, HorizontalAlignment.Left, -1, 14, d.Id == Map.SelDwarf ? UITheme.Gold : UITheme.Text);
        }
    }

    private void CenterHome() => Jump(F.Embark);

    public void Open(Control overlay)
    {
        CloseOverlay();
        Overlay = overlay;
        AddChild(overlay);
    }

    public void CloseOverlay()
    {
        if (Overlay == null) return;
        Overlay.QueueFree();
        Overlay = null;
    }

    private Label _toast;
    private SceneTreeTimer _toastTimer;

    public void Toast(string text)
    {
        _toast?.QueueFree();
        _toast = UITheme.L(text, 16, UITheme.Ember, UITheme.Body, HorizontalAlignment.Center);
        AddChild(_toast);
        Anchor(_toast, 0.5f, 1, 0.5f, 1, -300, -300, 300, -270);
        Label mine = _toast;
        GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(mine)) mine.QueueFree(); };
    }

    // ---- the pause menu -----------------------------------------------------------------

    public void OpenMenu()
    {
        if (_menu != null) return;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.55f) };
        var center = new CenterContainer();
        shade.AddChild(center);
        Anchor(center, 0, 0, 1, 1, 0, 0, 0, 0);
        var v = UITheme.V(8);
        var panel = UITheme.Panel(v);
        center.AddChild(panel);
        v.AddChild(UITheme.Heading(F.Name, 28));
        v.AddChild(UITheme.L(F.Date, 15, UITheme.Dim));
        v.AddChild(UITheme.B("Resume", CloseMenu, 18));
        v.AddChild(UITheme.B("Help", () => { CloseMenu(); Open(new HelpPanel(this)); }, 18));
        var sound = new CheckBox { Text = "Sound", ButtonPressed = Profile.Current.Sound, FocusMode = FocusModeEnum.None };
        sound.Toggled += on => { Profile.Current.Sound = on; Profile.Current.Apply(); Profile.Current.Save(); };
        v.AddChild(sound);
        var music = new CheckBox { Text = "Music", ButtonPressed = Profile.Current.MusicOn, FocusMode = FocusModeEnum.None };
        music.Toggled += on => { Profile.Current.MusicOn = on; Music.Instance?.SetVolume(Profile.Current.MusicVolume, on); Profile.Current.Save(); };
        v.AddChild(music);
        var alerts = new CheckBox { Text = "Pause on alerts", ButtonPressed = Profile.Current.PauseOnAlerts, FocusMode = FocusModeEnum.None };
        alerts.Toggled += on => { Profile.Current.PauseOnAlerts = on; Profile.Current.Save(); };
        v.AddChild(alerts);
        var art = new CheckBox { Text = "Painted art", ButtonPressed = Profile.Current.PaintedArt, FocusMode = FocusModeEnum.None, TooltipText = "Painted figures, creatures and buildings. Off: the original look, drawn by the game itself." };
        art.Toggled += on => { Profile.Current.PaintedArt = on; Profile.Current.Save(); };
        v.AddChild(art);
        v.AddChild(UITheme.B("Save and leave to the title", () => { CloseMenu(); Root.ShowTitle(); }, 18));
        v.AddChild(UITheme.B("Save and quit the game", () => { if (!Profile.Scratch) Profile.SaveFortress(F); GetTree().Quit(); }, 18));
        v.AddChild(UITheme.L("Ironman: the fortress saves every season and when you leave.", 13, UITheme.Faint));
        _menu = shade;
        AddChild(shade);
        Anchor(shade, 0, 0, 1, 1, 0, 0, 0, 0);
    }

    public void CloseMenu()
    {
        _menu?.QueueFree();
        _menu = null;
    }

    public bool MenuOpen => _menu != null;
}
