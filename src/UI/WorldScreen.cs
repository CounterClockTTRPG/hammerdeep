using System;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// Draws a <see cref="WorldMap"/> as a chart: terrain blended cell to cell, shores, rivers as
/// lines, mountains lit from the left, forests and hills as small marks, the sites by name.
/// The chart is drawn once; the hover and the chosen tile sit on a thin layer above it.
/// </summary>
public partial class WorldView : Control
{
    public WorldMap Map;
    public (int x, int y)? Chosen, Hover;
    public float Cell = 19;
    public Action<int, int> OnPick;
    private UITheme.Drawn _marks;
    private const float Pad = 8;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(WorldMap.W * Cell + 2 * Pad, WorldMap.H * Cell + 2 * Pad);
        MouseFilter = MouseFilterEnum.Stop;
        _marks = new UITheme.Drawn { OnDraw = DrawMarks, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_marks);
        FortressScreen.Anchor(_marks, 0, 0, 1, 1, 0, 0, 0, 0);
    }

    /// <summary>Redraw the hover and the chosen tile (the chart itself stays).</summary>
    public void Refresh() => _marks?.QueueRedraw();

    private static Color TerrainColor(Terrain t) => t switch
    {
        Terrain.Lake => new Color(0.15f, 0.27f, 0.44f),
        Terrain.Marsh => new Color(0.29f, 0.36f, 0.27f),
        Terrain.Plains => new Color(0.47f, 0.52f, 0.31f),
        Terrain.Forest => new Color(0.22f, 0.36f, 0.2f),
        Terrain.Hills => new Color(0.5f, 0.45f, 0.31f),
        Terrain.Mountains => new Color(0.44f, 0.41f, 0.39f),
        _ => new Color(0.56f, 0.55f, 0.56f),
    };

    public static Color SiteColor(SiteKind k) => k switch
    {
        SiteKind.Mountainhome or SiteKind.Hold => new Color(0.95f, 0.75f, 0.35f),
        SiteKind.Town => new Color(0.55f, 0.75f, 1f),
        SiteKind.Warren => new Color(0.95f, 0.3f, 0.25f),
        SiteKind.Burrow => new Color(0.85f, 0.65f, 0.25f),
        SiteKind.Grove => new Color(0.5f, 0.9f, 0.4f),
        SiteKind.Crag => new Color(0.75f, 0.75f, 0.8f),
        SiteKind.Ruin => new Color(0.45f, 0.4f, 0.38f),
        _ => new Color(1f, 0.95f, 0.6f),
    };

    private Terrain T(int x, int y) => Map.Tiles[y * WorldMap.W + x];
    private Color Col(int x, int y) => TerrainColor(T(x, y)).Lightened(Noise.Hash01(x, y, 1, (uint)Map.Seed) * 0.06f);
    private float J(int x, int y, int k) => (Noise.Hash01(x, y, k, (uint)Map.Seed) - 0.5f) * 3f;

    public override void _Draw()
    {
        // The chart's mount: a dark margin, a bronze frame with a fine inner rule.
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.05f, 0.045f, 0.04f));
        DrawSetTransform(new Vector2(Pad, Pad));
        float c = Cell, h = c / 2;

        // Terrain, each quarter of a cell leaning toward its neighbours, so country flows into country.
        for (int y = 0; y < WorldMap.H; y++)
            for (int x = 0; x < WorldMap.W; x++)
            {
                Color own = Col(x, y);
                for (int q = 0; q < 4; q++)
                {
                    int dx = q % 2 == 0 ? -1 : 1, dy = q < 2 ? -1 : 1;
                    Color nx = Map.In(x + dx, y) ? Col(x + dx, y) : own, ny = Map.In(x, y + dy) ? Col(x, y + dy) : own, nd = Map.In(x + dx, y + dy) ? Col(x + dx, y + dy) : own;
                    Color mix = new((nx.R + ny.R + nd.R) / 3, (nx.G + ny.G + nd.G) / 3, (nx.B + ny.B + nd.B) / 3);
                    DrawRect(new Rect2(x * c + (q % 2) * h, y * c + (q / 2) * h, h + 0.5f, h + 0.5f), own.Lerp(mix, 0.35f));
                }
            }
        // Shores: a pale line where land meets water.
        var shore = new Color(0.78f, 0.74f, 0.6f, 0.55f);
        for (int y = 0; y < WorldMap.H; y++)
            for (int x = 0; x < WorldMap.W; x++)
            {
                if (T(x, y) == Terrain.Lake) continue;
                float x0 = x * c, y0 = y * c;
                if (x > 0 && T(x - 1, y) == Terrain.Lake) DrawLine(new(x0 + 0.5f, y0), new(x0 + 0.5f, y0 + c), shore, 1.5f);
                if (x < WorldMap.W - 1 && T(x + 1, y) == Terrain.Lake) DrawLine(new(x0 + c - 0.5f, y0), new(x0 + c - 0.5f, y0 + c), shore, 1.5f);
                if (y > 0 && T(x, y - 1) == Terrain.Lake) DrawLine(new(x0, y0 + 0.5f), new(x0 + c, y0 + 0.5f), shore, 1.5f);
                if (y < WorldMap.H - 1 && T(x, y + 1) == Terrain.Lake) DrawLine(new(x0, y0 + c - 0.5f), new(x0 + c, y0 + c - 0.5f), shore, 1.5f);
            }
        // Rivers: joined cell to cell, and into the lakes they reach.
        var river = new Color(0.36f, 0.56f, 0.82f);
        for (int y = 0; y < WorldMap.H; y++)
            for (int x = 0; x < WorldMap.W; x++)
            {
                if (!Map.River[y * WorldMap.W + x]) continue;
                Vector2 at = new((x + 0.5f) * c, (y + 0.5f) * c);
                bool joined = false;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (!Map.In(nx, ny)) continue;
                    bool riv = Map.River[ny * WorldMap.W + nx], lake = T(nx, ny) == Terrain.Lake;
                    if (lake || (riv && (dx > 0 || dy > 0))) { DrawLine(at, at + new Vector2(dx, dy) * (lake ? h : c), river, 2.5f); joined = true; }
                    else if (riv) joined = true;
                }
                if (!joined) DrawCircle(at, 1.8f, river);
            }
        // The country's marks, top to bottom so nearer ones overlap farther.
        for (int y = 0; y < WorldMap.H; y++)
            for (int x = 0; x < WorldMap.W; x++)
            {
                Vector2 o = new(x * c, y * c);
                Color own = TerrainColor(T(x, y));
                float j = J(x, y, 2);
                switch (T(x, y))
                {
                    case Terrain.Mountains:
                    case Terrain.Peaks:
                    {
                        bool peak = T(x, y) == Terrain.Peaks;
                        Vector2 l = o + new Vector2(1, c - 3), r = o + new Vector2(c - 1, c - 3), top = o + new Vector2(c / 2 + j, peak ? -3 : 1);
                        Vector2 foot = new(top.X, l.Y);
                        DrawColoredPolygon(new[] { l, top, foot }, own.Lightened(0.14f));
                        DrawColoredPolygon(new[] { foot, top, r }, own.Darkened(0.32f));
                        if (peak)
                        {
                            float k = 0.38f;
                            DrawColoredPolygon(new[] { top.Lerp(l, k), top, top.Lerp(r, k), top.Lerp(foot, k * 0.8f) }, new Color(0.9f, 0.92f, 0.95f));
                        }
                        break;
                    }
                    case Terrain.Hills:
                    {
                        Vector2 b = o + new Vector2(c / 2 + j, c - 4);
                        var mound = new Vector2[10];
                        for (int k = 0; k < 10; k++) { float a = Mathf.Pi + k / 9f * Mathf.Pi; mound[k] = b + new Vector2(Mathf.Cos(a) * 7, Mathf.Sin(a) * 5.5f); }
                        DrawColoredPolygon(mound, own.Darkened(0.12f));
                        DrawPolyline(mound.Take(5).ToArray(), own.Lightened(0.2f), 1.2f);
                        break;
                    }
                    case Terrain.Forest:
                        foreach (var (px, py) in new[] { (5.5f, 10f), (13f, 13.5f), (8.5f, 17f) })
                        {
                            Vector2 b = o + new Vector2(px + J(x, y, (int)px), py);
                            DrawColoredPolygon(new[] { b + new Vector2(-3.5f, 0), b + new Vector2(0, -7.5f), b + new Vector2(3.5f, 0) }, own.Darkened(0.38f));
                        }
                        break;
                    case Terrain.Marsh:
                        for (int k = 0; k < 3; k++)
                        {
                            Vector2 b = o + new Vector2(4 + k * 4.5f + j, 6 + k * 4.5f);
                            DrawLine(b, b + new Vector2(5, 0), own.Lightened(0.18f), 1.2f);
                        }
                        break;
                }
            }
        // Where a hold could be dug: a gold speck.
        if (OnPick != null)
            for (int y = 0; y < WorldMap.H; y++)
                for (int x = 0; x < WorldMap.W; x++)
                    if (Map.CanEmbark(x, y)) DrawCircle(new Vector2((x + 0.5f) * c, (y + 0.72f) * c), 1.4f, new Color(1, 0.84f, 0.4f, 0.75f));
        // The sites, with their names.
        foreach (Site s in Map.Sites)
        {
            Vector2 at = new((s.X + 0.5f) * c, (s.Y + 0.5f) * c);
            Color col = SiteColor(s.Kind);
            bool big = s.Kind is SiteKind.Mountainhome or SiteKind.Fortress;
            DrawCircle(at, big ? 7.5f : 5.5f, new Color(0.03f, 0.03f, 0.03f, 0.85f));
            DrawCircle(at, big ? 5.5f : 4f, col);
            if (s.Kind == SiteKind.Ruin) DrawLine(at - new Vector2(4, 4), at + new Vector2(4, 4), Colors.Black, 1.5f);
            Vector2 tp = at + new Vector2(9, 4);
            foreach (var d in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) })
                DrawString(UITheme.LabelFont, tp + d, s.Name, HorizontalAlignment.Left, -1, 12, new Color(0.02f, 0.02f, 0.02f, 0.9f));
            DrawString(UITheme.LabelFont, tp, s.Name, HorizontalAlignment.Left, -1, 12, col.Lightened(0.2f));
        }
        DrawSetTransform(Vector2.Zero);
        DrawRect(new Rect2(1, 1, Size.X - 2, Size.Y - 2), UITheme.Edge, false, 2);
        DrawRect(new Rect2(Pad - 3, Pad - 3, Size.X - 2 * Pad + 6, Size.Y - 2 * Pad + 6), new Color(UITheme.EdgeLit, 0.45f), false, 1);
    }

    private void DrawMarks(UITheme.Drawn d)
    {
        d.DrawSetTransform(new Vector2(Pad, Pad));
        float c = Cell;
        if (Hover is (int hx, int hy)) d.DrawRect(new Rect2(hx * c, hy * c, c, c), new Color(1, 1, 1, 0.75f), false, 1);
        if (Chosen is (int cx, int cy))
        {
            Vector2 at = new((cx + 0.5f) * c, (cy + 0.5f) * c);
            d.DrawCircle(at, 26, new Color(1f, 0.7f, 0.3f, 0.10f));
            d.DrawCircle(at, 15, new Color(1f, 0.75f, 0.35f, 0.16f));
            d.DrawRect(new Rect2((cx - 2) * c, (cy - 2) * c, c * 5, c * 5), new Color(1, 0.85f, 0.4f, 0.45f), false, 1);
            d.DrawRect(new Rect2(cx * c - 1, cy * c - 1, c + 2, c + 2), UITheme.Gold, false, 2.5f);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var t = ((int)((mm.Position.X - Pad) / Cell), (int)((mm.Position.Y - Pad) / Cell));
            (int, int)? hv = mm.Position.X >= Pad && mm.Position.Y >= Pad && Map.In(t.Item1, t.Item2) ? t : null;
            if (hv != Hover) { Hover = hv; Refresh(); }
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && OnPick != null)
        {
            if (mb.Position.X < Pad || mb.Position.Y < Pad) return;
            int x = (int)((mb.Position.X - Pad) / Cell), y = (int)((mb.Position.Y - Pad) / Cell);
            if (Map.In(x, y)) OnPick(x, y);
        }
    }
}

/// <summary>Before the expedition: the world under the same night as the title, and where to stop.</summary>
public partial class WorldScreen : Control
{
    public Root Root;
    public int Slot;
    public WorldMap Map;
    private WorldView _view;
    private VBoxContainer _side;
    private (int x, int y) _pick;
    private readonly Rng _rng = new((ulong)DateTime.Now.Ticks);

    public override void _Ready()
    {
        FortressScreen.Anchor(this, 0, 0, 1, 1, 0, 0, 0, 0);
        Theme = UITheme.Theme;
        var bg = new UITheme.Drawn { OnDraw = Night, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        FortressScreen.Anchor(bg, 0, 0, 1, 1, 0, 0, 0, 0);
        var row = UITheme.H(20);
        AddChild(row);
        FortressScreen.Anchor(row, 0, 0, 1, 1, 24, 16, -24, -16);
        var left = UITheme.V(4);
        row.AddChild(left);
        Label title = UITheme.L(Map.Name.ToUpperFirst(), 34, UITheme.Gold, UITheme.Title);
        title.AddThemeColorOverride("font_shadow_color", new Color(0.02f, 0.015f, 0.01f, 0.85f));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 3);
        left.AddChild(title);
        left.AddChild(UITheme.L("Choose where the expedition stops. A hold is dug in hills or mountains, marked with a gold speck; the neighbours decide who trades, who settles and who raids.", 14, UITheme.Dim));
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4) });
        _view = new WorldView { Map = Map, OnPick = Pick };
        left.AddChild(_view);
        HBoxContainer legend = UITheme.H(14);
        foreach (SiteKind k in new[] { SiteKind.Mountainhome, SiteKind.Town, SiteKind.Warren, SiteKind.Burrow, SiteKind.Grove, SiteKind.Crag, SiteKind.Ruin })
            legend.AddChild(UITheme.L("● " + WorldMap.KindName(k), 12, WorldView.SiteColor(k)));
        left.AddChild(legend);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 840), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        row.AddChild(UITheme.Panel(scroll));
        _side = UITheme.V(6);
        _side.CustomMinimumSize = new Vector2(396, 0);
        scroll.AddChild(_side);
        var (x, y) = Map.DefaultEmbark();
        Pick(x, y);
    }

    /// <summary>The title's night: a deep sky going down to the dark of the rock, a scatter of stars.</summary>
    private static void Night(UITheme.Drawn c)
    {
        Vector2 size = c.Size;
        for (int i = 0; i < 24; i++)
        {
            float t = i / 24f;
            c.DrawRect(new Rect2(0, size.Y * t, size.X, size.Y / 24 + 1), new Color(0.043f, 0.063f, 0.125f).Lerp(new Color(0.07f, 0.06f, 0.06f), t));
        }
        for (int i = 0; i < 70; i++)
        {
            float x = Noise.Hash01(i, 0, 0, 9) * size.X, y = Noise.Hash01(i, 1, 0, 9) * size.Y * 0.6f;
            c.DrawCircle(new Vector2(x, y), i % 6 == 0 ? 1.4f : 0.9f, new Color(1, 0.95f, 0.85f, 0.25f + 0.35f * Noise.Hash01(i, 2, 0, 9)));
        }
    }

    private static Label Section(string text) => UITheme.L(text, 17, UITheme.Bronze, UITheme.Title);

    private void Pick(int x, int y)
    {
        _pick = (x, y);
        _view.Chosen = (x, y);
        _view.Refresh();
        foreach (Node n in _side.GetChildren()) n.QueueFree();
        Terrain t = Map.At(x, y);
        bool ok = Map.CanEmbark(x, y);
        _side.AddChild(UITheme.L(t.ToString(), 28, UITheme.Gold, UITheme.Title));
        _side.AddChild(UITheme.L($"Tile {x}, {y}", 12, UITheme.Faint));
        var near = ok ? Map.Neighbours(x, y) : null;
        if (!ok) _side.AddChild(UITheme.Wrap("No hold can be dug here: choose hills or mountains, away from others' homes.", 15, UITheme.Ember, 380));
        else _side.AddChild(UITheme.Wrap(Map.Land(x, y).Describe().ToUpperFirst() + ".", 15, UITheme.Text, 380));
        HBoxContainer go = UITheme.H(8);
        Button embark = UITheme.Primary(UITheme.B("Embark here", () => AskName(), 17));
        embark.Disabled = !ok;
        go.AddChild(embark);
        go.AddChild(UITheme.B("Another world", () => Root.ShowWorld(Slot), 14));
        go.AddChild(UITheme.B("Back", () => Root.ShowTitle(), 14));
        _side.AddChild(new Control { CustomMinimumSize = new Vector2(0, 2) });
        _side.AddChild(go);
        if (ok)
        {
            _side.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
            _side.AddChild(Section("Neighbours"));
            if (near.Count == 0) _side.AddChild(UITheme.L("None within two dozen days. Lonely, but quiet.", 13, UITheme.Dim));
            foreach (var (s, days) in near.Take(9))
            {
                string meaning = s.Kind switch
                {
                    SiteKind.Warren => days < 10 ? "Raids, often" : "Raids",
                    SiteKind.Town => "Lowland caravans",
                    SiteKind.Mountainhome => "The Mountainhome's caravans and migrants",
                    SiteKind.Hold => "Kin",
                    SiteKind.Burrow => "Delvling migrants",
                    SiteKind.Grove => "Rootfolk migrants",
                    SiteKind.Crag => "Now and then a Stonekin",
                    _ => "",
                };
                var entry = UITheme.V(0);
                var top = UITheme.H(6);
                top.AddChild(UITheme.L(s.Name, 15, WorldView.SiteColor(s.Kind)));
                top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
                top.AddChild(UITheme.L($"{days} days", 13, UITheme.Dim, UITheme.Mono));
                entry.AddChild(top);
                entry.AddChild(UITheme.L($"{WorldMap.KindName(s.Kind).ToUpperFirst()}. {meaning}.", 13, UITheme.Dim));
                _side.AddChild(entry);
            }
            if (!near.Any(p => p.site.Kind == SiteKind.Town)) _side.AddChild(UITheme.Wrap("No Lowland town is near enough to send caravans.", 13, UITheme.Ember, 380));
        }
        _side.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        _side.AddChild(Section("Legends"));
        _side.AddChild(UITheme.L("Years ago, the newest first.", 12, UITheme.Faint));
        foreach (HistoryEvent e in Map.History.TakeLast(40).Reverse())
        {
            var line = UITheme.H(10);
            Label yr = UITheme.L((-e.Year).ToString(), 12, UITheme.Bronze, UITheme.Mono, HorizontalAlignment.Right);
            yr.CustomMinimumSize = new Vector2(30, 0);
            yr.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            line.AddChild(yr);
            line.AddChild(UITheme.Wrap(e.Text, 13, UITheme.Dim, 340));
            _side.AddChild(line);
        }
    }

    private void AskName()
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        var center = new CenterContainer();
        shade.AddChild(center);
        FortressScreen.Anchor(center, 0, 0, 1, 1, 0, 0, 0, 0);
        var v = UITheme.V(10);
        center.AddChild(UITheme.Panel(v));
        v.AddChild(UITheme.Heading("Name the fortress", 26));
        v.AddChild(UITheme.Wrap($"Seven dwarves of the clans set out from {Map.Sites.FirstOrDefault(s => s.Kind == SiteKind.Mountainhome)?.Name ?? "the Mountainhome"} with a wagon: picks and axes, ale, rations, seed, a few logs, two goats and some hens. Where they stop, they will dig.", 15, UITheme.Dim, 460));
        var name = new LineEdit { Text = Tables.FortName(_rng), CustomMinimumSize = new Vector2(320, 0) };
        name.AddThemeFontSizeOverride("font_size", 20);
        HBoxContainer h = UITheme.H(6);
        h.AddChild(name);
        h.AddChild(UITheme.B("Another", () => name.Text = Tables.FortName(_rng), 14));
        v.AddChild(h);
        bool first = Enumerable.Range(1, 3).All(k => Profile.Peek(k, out _) == null || k == Slot);
        var guide = new CheckBox { Text = "Guide me through the first steps (tutorial)", ButtonPressed = first, FocusMode = FocusModeEnum.None };
        v.AddChild(guide);
        HBoxContainer go = UITheme.H(8);
        void Embark()
        {
            string n = name.Text.Trim();
            if (n.Length == 0) n = Tables.FortName(_rng);
            Root.NewFortress(n, Slot, 0, Map.Seed, _pick.x, _pick.y, guide.ButtonPressed);
        }
        name.TextSubmitted += _ => Embark();
        go.AddChild(UITheme.Primary(UITheme.B("Strike the earth!", Embark, 18)));
        go.AddChild(UITheme.B("Back", () => shade.QueueFree(), 16));
        v.AddChild(go);
        AddChild(shade);
        FortressScreen.Anchor(shade, 0, 0, 1, 1, 0, 0, 0, 0);
        name.GrabFocus();
        name.SelectAll();
    }
}

/// <summary>In the fortress: the world around it, and its legends.</summary>
public partial class WorldPanel : Overlay
{
    public WorldPanel(FortressScreen s) : base(s, s.F.Map?.Name.ToUpperFirst() ?? "The world", new Vector2(1500, 720)) { }

    protected override string Signature() => "world";

    protected override void Fill()
    {
        WorldMap m = F.Map;
        if (m == null) { Body.AddChild(UITheme.L("This hold was founded before the world was known (an older save).", 15, UITheme.Dim)); return; }
        HBoxContainer row = UITheme.H(16);
        Body.AddChild(row);
        // A shade smaller than the world screen's, so the chart and its frame fit the panel.
        row.AddChild(new WorldView { Map = m, Chosen = (F.WorldX, F.WorldY), Cell = 18.4f });
        var side = UITheme.V(4);
        side.CustomMinimumSize = new Vector2(400, 0);
        row.AddChild(side);
        side.AddChild(UITheme.L("Neighbours", 15, UITheme.Bronze, UITheme.Title));
        foreach (var (s, days) in m.Neighbours(F.WorldX, F.WorldY).Take(10))
            side.AddChild(UITheme.L($"● {s.Name}, {WorldMap.KindName(s.Kind)}: {days} days", 13, WorldView.SiteColor(s.Kind)));
        if (F.Deeds.Count > 0)
        {
            side.AddChild(UITheme.L($"Deeds of {F.Name}", 15, UITheme.Gold, UITheme.Title));
            foreach (string deed in Enumerable.Reverse(F.Deeds)) side.AddChild(UITheme.Wrap(deed, 13, UITheme.Text, 390));
        }
        side.AddChild(UITheme.L("Legends", 15, UITheme.Bronze, UITheme.Title));
        foreach (HistoryEvent e in m.History.TakeLast(30).Reverse())
            side.AddChild(UITheme.Wrap($"{-e.Year} years ago: {e.Text}", 12, UITheme.Dim, 390));
    }
}
