using System;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Draws a <see cref="WorldMap"/>: terrain, rivers, sites with names; a chosen tile.</summary>
public partial class WorldView : Control
{
    public WorldMap Map;
    public (int x, int y)? Chosen, Hover;
    public float Cell = 19;
    public Action<int, int> OnPick;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(WorldMap.W * Cell, WorldMap.H * Cell);
        MouseFilter = MouseFilterEnum.Stop;
    }

    private static Color TerrainColor(Terrain t, float h) => t switch
    {
        Terrain.Lake => new Color(0.16f, 0.3f, 0.5f),
        Terrain.Marsh => new Color(0.28f, 0.36f, 0.26f),
        Terrain.Plains => new Color(0.48f, 0.55f, 0.3f),
        Terrain.Forest => new Color(0.2f, 0.38f, 0.2f),
        Terrain.Hills => new Color(0.5f, 0.45f, 0.3f),
        Terrain.Mountains => new Color(0.45f, 0.42f, 0.4f),
        _ => new Color(0.82f, 0.84f, 0.88f),
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

    public override void _Draw()
    {
        for (int y = 0; y < WorldMap.H; y++)
            for (int x = 0; x < WorldMap.W; x++)
            {
                int i = y * WorldMap.W + x;
                Color c = TerrainColor(Map.Tiles[i], Map.Height[i]);
                float n = Noise.Hash01(x, y, 1, (uint)Map.Seed) * 0.08f;
                var r = new Rect2(x * Cell, y * Cell, Cell, Cell);
                DrawRect(r, c.Lightened(n));
                switch (Map.Tiles[i])
                {
                    case Terrain.Mountains: case Terrain.Peaks:
                        DrawColoredPolygon(new[] { r.Position + new Vector2(3, Cell - 4), r.Position + new Vector2(Cell / 2, 4), r.Position + new Vector2(Cell - 3, Cell - 4) }, c.Darkened(0.25f));
                        if (Map.Tiles[i] == Terrain.Peaks) DrawColoredPolygon(new[] { r.Position + new Vector2(Cell / 2 - 3, 8), r.Position + new Vector2(Cell / 2, 4), r.Position + new Vector2(Cell / 2 + 3, 8) }, Colors.White);
                        break;
                    case Terrain.Forest:
                        DrawCircle(r.Position + new Vector2(6, 8), 3.5f, c.Darkened(0.3f));
                        DrawCircle(r.Position + new Vector2(13, 12), 3.5f, c.Darkened(0.3f));
                        break;
                    case Terrain.Hills:
                        DrawArc(r.Position + new Vector2(Cell / 2, Cell - 4), 6, Mathf.Pi, Mathf.Tau, 8, c.Darkened(0.3f), 1.5f);
                        break;
                }
                if (Map.River[i]) DrawCircle(r.GetCenter(), 3, new Color(0.3f, 0.5f, 0.8f));
            }
        // Where a hold could go, faintly.
        if (OnPick != null)
            for (int y = 0; y < WorldMap.H; y++)
                for (int x = 0; x < WorldMap.W; x++)
                    if (Map.CanEmbark(x, y)) DrawRect(new Rect2(x * Cell + 1, y * Cell + 1, Cell - 2, Cell - 2), new Color(1, 0.85f, 0.4f, 0.12f), false, 1);
        foreach (Site s in Map.Sites)
        {
            Vector2 c = new((s.X + 0.5f) * Cell, (s.Y + 0.5f) * Cell);
            Color col = SiteColor(s.Kind);
            DrawCircle(c, s.Kind is SiteKind.Mountainhome or SiteKind.Fortress ? 7 : 5, new Color(0, 0, 0, 0.6f));
            DrawCircle(c, s.Kind is SiteKind.Mountainhome or SiteKind.Fortress ? 5.5f : 4f, col);
            if (s.Kind == SiteKind.Ruin) DrawLine(c - new Vector2(4, 4), c + new Vector2(4, 4), Colors.Black, 1.5f);
            DrawString(UITheme.LabelFont, c + new Vector2(9, 5), s.Name, HorizontalAlignment.Left, -1, 12, new Color(0, 0, 0, 0.85f));
            DrawString(UITheme.LabelFont, c + new Vector2(8, 4), s.Name, HorizontalAlignment.Left, -1, 12, col.Lightened(0.15f));
        }
        if (Hover is (int hx, int hy)) DrawRect(new Rect2(hx * Cell, hy * Cell, Cell, Cell), new Color(1, 1, 1, 0.7f), false, 1);
        if (Chosen is (int cx, int cy))
        {
            DrawRect(new Rect2(cx * Cell - 1, cy * Cell - 1, Cell + 2, Cell + 2), UITheme.Gold, false, 2.5f);
            DrawRect(new Rect2((cx - 2) * Cell, (cy - 2) * Cell, Cell * 5, Cell * 5), new Color(1, 0.85f, 0.4f, 0.4f), false, 1);
        }
        DrawRect(new Rect2(Vector2.Zero, Size), UITheme.Edge, false, 2);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var t = ((int)(mm.Position.X / Cell), (int)(mm.Position.Y / Cell));
            Hover = Map.In(t.Item1, t.Item2) ? t : null;
            QueueRedraw();
        }
        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb && OnPick != null)
        {
            int x = (int)(mb.Position.X / Cell), y = (int)(mb.Position.Y / Cell);
            if (Map.In(x, y)) OnPick(x, y);
        }
    }
}

/// <summary>Before the expedition: the world, and where to stop.</summary>
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
        var bg = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f) };
        AddChild(bg);
        FortressScreen.Anchor(bg, 0, 0, 1, 1, 0, 0, 0, 0);
        var row = UITheme.H(16);
        AddChild(row);
        FortressScreen.Anchor(row, 0, 0, 1, 1, 24, 20, -24, -20);
        var left = UITheme.V(6);
        row.AddChild(left);
        left.AddChild(UITheme.Heading(Map.Name.ToUpperFirst(), 26));
        left.AddChild(UITheme.L("Choose where the expedition stops: hills or mountains, outlined in gold. Neighbours shape the hold's fortunes.", 14, UITheme.Dim));
        _view = new WorldView { Map = Map, OnPick = Pick };
        left.AddChild(_view);
        HBoxContainer legend = UITheme.H(12);
        foreach (SiteKind k in new[] { SiteKind.Mountainhome, SiteKind.Town, SiteKind.Warren, SiteKind.Burrow, SiteKind.Grove, SiteKind.Crag, SiteKind.Ruin })
            legend.AddChild(UITheme.L("● " + WorldMap.KindName(k), 12, WorldView.SiteColor(k)));
        left.AddChild(legend);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(420, 820), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        row.AddChild(UITheme.Panel(scroll));
        _side = UITheme.V(6);
        _side.CustomMinimumSize = new Vector2(400, 0);
        scroll.AddChild(_side);
        var (x, y) = Map.DefaultEmbark();
        Pick(x, y);
    }

    private void Pick(int x, int y)
    {
        _pick = (x, y);
        _view.Chosen = (x, y);
        _view.QueueRedraw();
        foreach (Node n in _side.GetChildren()) n.QueueFree();
        Terrain t = Map.At(x, y);
        bool ok = Map.CanEmbark(x, y);
        _side.AddChild(UITheme.Heading($"{t} at {x}, {y}", 20));
        if (!ok) { _side.AddChild(UITheme.Wrap("No hold can be dug here: choose hills or mountains, away from others' homes.", 14, UITheme.Ember, 380)); }
        else
        {
            EmbarkLand land = Map.Land(x, y);
            _side.AddChild(UITheme.Wrap("The land: " + land.Describe() + ".", 14, UITheme.Text, 380));
            var near = Map.Neighbours(x, y);
            _side.AddChild(UITheme.L("Neighbours", 15, UITheme.Bronze, UITheme.Title));
            if (near.Count == 0) _side.AddChild(UITheme.L("None within two dozen days. Lonely, but quiet.", 13, UITheme.Dim));
            foreach (var (s, days) in near.Take(9))
            {
                string meaning = s.Kind switch
                {
                    SiteKind.Warren => days < 10 ? "raids -- often" : "raids",
                    SiteKind.Town => "Lowland caravans",
                    SiteKind.Mountainhome => "the Mountainhome's caravans and migrants",
                    SiteKind.Hold => "kin",
                    SiteKind.Burrow => "Delvling migrants",
                    SiteKind.Grove => "Rootfolk migrants",
                    SiteKind.Crag => "now and then a Stonekin",
                    _ => "",
                };
                _side.AddChild(UITheme.L($"● {s.Name}, {WorldMap.KindName(s.Kind)}: {days} days -- {meaning}", 13, WorldView.SiteColor(s.Kind)));
            }
            bool town = near.Any(p => p.site.Kind == SiteKind.Town);
            if (!town) _side.AddChild(UITheme.Wrap("No Lowland town is near enough to send caravans.", 13, UITheme.Ember, 380));
        }
        HBoxContainer go = UITheme.H(8);
        Button embark = UITheme.B("Embark here", () => AskName(), 17);
        embark.Disabled = !ok;
        go.AddChild(embark);
        go.AddChild(UITheme.B("Another world", () => Root.ShowWorld(Slot), 14));
        go.AddChild(UITheme.B("Back", () => Root.ShowTitle(), 14));
        _side.AddChild(go);
        _side.AddChild(UITheme.L("Legends", 15, UITheme.Bronze, UITheme.Title));
        foreach (HistoryEvent e in Map.History.TakeLast(40).Reverse())
            _side.AddChild(UITheme.Wrap($"{-e.Year} years ago: {e.Text}", 12, UITheme.Dim, 380));
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
        HBoxContainer go = UITheme.H(8);
        void Embark()
        {
            string n = name.Text.Trim();
            if (n.Length == 0) n = Tables.FortName(_rng);
            Root.NewFortress(n, Slot, 0, Map.Seed, _pick.x, _pick.y);
        }
        name.TextSubmitted += _ => Embark();
        go.AddChild(UITheme.B("Strike the earth!", Embark, 18));
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
        row.AddChild(new WorldView { Map = m, Chosen = (F.WorldX, F.WorldY) });
        var side = UITheme.V(4);
        side.CustomMinimumSize = new Vector2(400, 0);
        row.AddChild(side);
        side.AddChild(UITheme.L("Neighbours", 15, UITheme.Bronze, UITheme.Title));
        foreach (var (s, days) in m.Neighbours(F.WorldX, F.WorldY).Take(10))
            side.AddChild(UITheme.L($"● {s.Name}, {WorldMap.KindName(s.Kind)}: {days} days", 13, WorldView.SiteColor(s.Kind)));
        side.AddChild(UITheme.L("Legends", 15, UITheme.Bronze, UITheme.Title));
        foreach (HistoryEvent e in m.History.TakeLast(30).Reverse())
            side.AddChild(UITheme.Wrap($"{-e.Year} years ago: {e.Text}", 12, UITheme.Dim, 390));
    }
}
