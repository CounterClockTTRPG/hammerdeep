using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The title: one mountain by night, cut open. The three fortress slots are three halls at
/// three depths on its stair shaft -- a hold you are playing is dug out and lit, its own clans
/// at work in it; an empty slot is still rock with a surveyor's chalk outline; a fallen hold is
/// a cold hall of coffins. Strata, ore, a cavern and a magma seam fill the rest of the cut.
/// </summary>
public partial class TitleScreen : Control
{
    public Root Root;
    private Control _dialog;
    private float _t;
    private readonly Rng _rng = new((ulong)DateTime.Now.Ticks);
    private UITheme.Drawn _bg;
    private VBoxContainer _halls;
    private readonly List<Hall> _hallInfo = new();
    private int _hover = -1;

    // The cut-away's colours: night, the strata, firelight; the text keeps the game's own palette.
    private static readonly Color SkyTop = new(0.043f, 0.063f, 0.125f), SkyLow = new(0.10f, 0.094f, 0.19f);
    private static readonly Color Soil = new(0.23f, 0.17f, 0.13f), RockA = new(0.165f, 0.145f, 0.14f), RockB = new(0.19f, 0.165f, 0.153f);
    private static readonly Color Bedrock = new(0.085f, 0.075f, 0.075f);
    private static readonly Color Fire = new(0.91f, 0.54f, 0.23f), Chalk = new(0.73f, 0.70f, 0.65f);

    private const float HallX = 90, HallW = 780, HallH = 132, HallGap = 22, HallTop = 296;

    /// <summary>What a slot holds, for drawing its hall.</summary>
    private sealed class Hall
    {
        public Control Box;
        public bool Dug, Fallen;
        public string[] Clans = Array.Empty<string>();
        public int Folk;
    }

    public override void _Ready()
    {
        FortressScreen.Anchor(this, 0, 0, 1, 1, 0, 0, 0, 0);
        Theme = UITheme.Theme;
        _bg = new UITheme.Drawn { OnDraw = DrawBackdrop, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_bg);
        FortressScreen.Anchor(_bg, 0, 0, 1, 1, 0, 0, 0, 0);

        Label title = UITheme.L("HAMMERDEEP", 92, UITheme.Gold, UITheme.Title);
        // Cut into the night: a dark lower lip under the gilt.
        title.AddThemeColorOverride("font_shadow_color", new Color(0.02f, 0.015f, 0.01f, 0.85f));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 4);
        AddChild(title);
        FortressScreen.Anchor(title, 0, 0, 0, 0, HallX - 6, 58, HallX + 900, 170);
        Label tag = UITheme.L("Seven dwarves, one wagon, and a mountain nobody asked.", 20, UITheme.Text, UITheme.Body);
        AddChild(tag);
        FortressScreen.Anchor(tag, 0, 0, 0, 0, HallX, 168, HallX + 900, 200);

        _halls = UITheme.V((int)HallGap);
        AddChild(_halls);
        FortressScreen.Anchor(_halls, 0, 0, 0, 0, HallX, HallTop, HallX + HallW, HallTop + 3 * HallH + 2 * HallGap);

        // The settings lie quietly on the bedrock.
        HBoxContainer row = UITheme.H(18);
        row.AddChild(Toggle("Sound", Profile.Current.Sound, on => { Profile.Current.Sound = on; Profile.Current.Apply(); Profile.Current.Save(); }));
        row.AddChild(Toggle("Music", Profile.Current.MusicOn, on => { Profile.Current.MusicOn = on; Music.Instance?.SetVolume(Profile.Current.MusicVolume, on); Profile.Current.Save(); }));
        row.AddChild(Toggle("Painted art", Profile.Current.PaintedArt, on => { Profile.Current.PaintedArt = on; Profile.Current.Save(); }, "Painted figures, creatures and buildings. Off: the original look, drawn by the game itself."));
        row.AddChild(Toggle("Full screen (F11)", Profile.Current.Fullscreen, on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); Profile.Current.Save(); }));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });
        row.AddChild(UITheme.B($"Achievements ({Profile.Current.Achieved.Count(kv => Achievements.All.Any(x => x.Id == kv.Key))}/{Achievements.All.Length})", ShowAchievements, 15));
        row.AddChild(UITheme.B("Quit", () => GetTree().Quit(), 15));
        AddChild(row);
        FortressScreen.Anchor(row, 0, 1, 0, 1, HallX, -62, HallX + 760, -26);
        FillSlots();
    }

    private static CheckBox Toggle(string text, bool on, Action<bool> set, string tip = null)
    {
        var c = new CheckBox { Text = text, ButtonPressed = on, FocusMode = FocusModeEnum.None, TooltipText = tip ?? "" };
        c.AddThemeColorOverride("font_color", UITheme.Dim);
        c.AddThemeColorOverride("font_pressed_color", UITheme.Dim);
        c.Toggled += v => set(v);
        return c;
    }

    private void FillSlots()
    {
        foreach (Node n in _halls.GetChildren()) n.QueueFree();
        _hallInfo.Clear();
        for (int slot = 1; slot <= 3; slot++)
        {
            int s = slot;
            Fortress f = Profile.LoadFortress(slot);
            var hall = new Hall
            {
                Dug = f != null,
                Fallen = f?.Fallen ?? false,
                Folk = f?.Alive ?? 0,
                Clans = f == null ? Array.Empty<string>() : f.Dwarves.Where(d => !d.Dead).Select(d => d.Clan).Distinct().Take(4).ToArray(),
            };
            var box = new Control { CustomMinimumSize = new Vector2(HallW, HallH), MouseFilter = MouseFilterEnum.Pass };
            int idx = slot - 1;
            box.MouseEntered += () => _hover = idx;
            box.MouseExited += () => { if (_hover == idx) _hover = -1; };
            hall.Box = box;
            var v = UITheme.V(2);
            box.AddChild(v);
            FortressScreen.Anchor(v, 0, 0, 0, 1, 26, 16, 520, -14);

            if (f == null)
            {
                v.AddChild(UITheme.L("Unbroken rock", 28, Chalk.Darkened(0.15f), UITheme.Title));
                v.AddChild(UITheme.L("Nothing is dug here yet.", 16, UITheme.Dim));
            }
            else if (hall.Fallen)
            {
                v.AddChild(UITheme.L(f.Name, 28, UITheme.Ember, UITheme.Title));
                int lie = f.Dwarves.Count;
                v.AddChild(UITheme.L($"Fallen by {f.Date}. {lie} {(lie == 1 ? "dwarf lies" : "dwarves lie")} here.", 16, UITheme.Dim));
            }
            else
            {
                v.AddChild(UITheme.L(f.Name, 28, UITheme.Gold, UITheme.Title));
                v.AddChild(UITheme.L($"{f.Alive} {(f.Alive == 1 ? "dwarf" : "dwarves")}. {f.Date}.", 16, UITheme.Text));
            }
            v.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });

            HBoxContainer h = UITheme.H(8);
            if (f != null && !hall.Fallen) h.AddChild(Primary(UITheme.B("Continue", () => Root.Continue(s), 16)));
            if (f == null) h.AddChild(Primary(UITheme.B("Found a fortress", () => Root.ShowWorld(s), 16)));
            else h.AddChild(UITheme.B("Found anew", () => Root.ShowWorld(s), 15, "Founds a new fortress in this slot. The one here is lost for good."));
            if (f != null)
            {
                Button ab = null;
                ab = UITheme.B("Abandon", () =>
                {
                    // Ironman: one click to ask, a second to do it.
                    if (ab.Text == "Abandon") { ab.Text = "Abandon for good?"; ab.AddThemeColorOverride("font_color", UITheme.Ember); return; }
                    Profile.DeleteSlot(s);
                    FillSlots();
                }, 14);
                ab.MouseExited += () => { ab.Text = "Abandon"; ab.RemoveThemeColorOverride("font_color"); };
                h.AddChild(ab);
            }
            v.AddChild(h);
            _halls.AddChild(box);
            _hallInfo.Add(hall);
        }
    }

    private static Button Primary(Button b) => UITheme.Primary(b);

    /// <summary>Every achievement: the earned in gold, with the hold and the day; the rest dim, with how.</summary>
    private void ShowAchievements()
    {
        _dialog?.QueueFree();
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        var center = new CenterContainer();
        shade.AddChild(center);
        FortressScreen.Anchor(center, 0, 0, 1, 1, 0, 0, 0, 0);
        var v = UITheme.V(8);
        center.AddChild(UITheme.Panel(v));
        int got = Achievements.All.Count(x => Profile.Current.Achieved.ContainsKey(x.Id));
        v.AddChild(UITheme.Heading($"Achievements  {got} of {Achievements.All.Length}", 26));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(560, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = UITheme.V(6);
        list.CustomMinimumSize = new Vector2(530, 0);
        scroll.AddChild(list);
        foreach (Achievement x in Achievements.All)
        {
            bool have = Profile.Current.Achieved.TryGetValue(x.Id, out string where);
            var e = UITheme.V(0);
            e.AddChild(UITheme.L(x.Title, 17, have ? UITheme.Gold : UITheme.Dim, UITheme.Title));
            e.AddChild(UITheme.Wrap(have ? $"{x.Text} Earned at {where}." : x.Text, 13, have ? UITheme.Text : UITheme.Faint, 520));
            list.AddChild(e);
        }
        v.AddChild(scroll);
        v.AddChild(UITheme.B("Close", () => { shade.QueueFree(); _dialog = null; }, 16));
        AddChild(shade);
        FortressScreen.Anchor(shade, 0, 0, 1, 1, 0, 0, 0, 0);
        _dialog = shade;
    }

    private void AskName(int slot)
    {
        _dialog?.QueueFree();
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.6f) };
        var center = new CenterContainer();
        shade.AddChild(center);
        FortressScreen.Anchor(center, 0, 0, 1, 1, 0, 0, 0, 0);
        var v = UITheme.V(10);
        center.AddChild(UITheme.Panel(v));
        v.AddChild(UITheme.Heading("Name the fortress", 26));
        v.AddChild(UITheme.Wrap("Seven dwarves of the clans set out with a wagon: picks and axes, ale, rations, seed, a few logs. Where they stop, they will dig.", 15, UITheme.Dim, 460));
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
            Root.NewFortress(n, slot);
        }
        name.TextSubmitted += _ => Embark();
        go.AddChild(UITheme.B("Strike the earth!", Embark, 18));
        go.AddChild(UITheme.B("Back", () => { shade.QueueFree(); _dialog = null; }, 16));
        v.AddChild(go);
        AddChild(shade);
        FortressScreen.Anchor(shade, 0, 0, 1, 1, 0, 0, 0, 0);
        _dialog = shade;
        name.GrabFocus();
        name.SelectAll();
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        _bg.QueueRedraw();
    }

    // ---- the cut-away -------------------------------------------------------------------

    /// <summary>The mountain's skyline: a low shoulder under the title and the halls, then a peak on the right.</summary>
    private static float Ridge(float x, Vector2 size)
    {
        float rise = HallX + HallW + 130, peak = MathF.Max(rise + 280, size.X * 0.8f);
        float y;
        if (x <= rise) y = 240 - 14 * (x / rise);
        else if (x <= peak) y = 226 - 156 * Mathf.SmoothStep(0, 1, (x - rise) / (peak - rise));
        else y = 70 + 110 * Mathf.SmoothStep(0, 1, (x - peak) / MathF.Max(1, size.X - peak));
        return y + Noise.Fbm2(x / 70f, 0, 77) * 22 - 11;
    }

    private void DrawBackdrop(UITheme.Drawn c)
    {
        Vector2 size = c.Size;
        // Sky.
        for (int i = 0; i < 30; i++)
        {
            float t = i / 30f;
            c.DrawRect(new Rect2(0, size.Y * 0.4f * t, size.X, size.Y * 0.4f / 30 + 1), SkyTop.Lerp(SkyLow, t));
        }
        for (int i = 0; i < 110; i++)
        {
            float x = Noise.Hash01(i, 0, 0, 5) * size.X, y = Noise.Hash01(i, 1, 0, 5) * 260;
            if (y > Ridge(x, size) - 6) continue;
            float tw = 0.5f + 0.5f * MathF.Sin(_t * (0.6f + i % 3 * 0.4f) + i);
            c.DrawCircle(new Vector2(x, y), i % 7 == 0 ? 1.5f : 1f, new Color(1, 0.95f, 0.85f, 0.25f + 0.45f * tw));
        }

        // The mountain in bands: topsoil, then the strata, each following the skyline down.
        const int steps = 96;
        float[] xs = new float[steps + 1];
        for (int k = 0; k <= steps; k++) xs[k] = k / (float)steps * size.X;
        float Band(int b, float x) => b == 0 ? Ridge(x, size) : Ridge(x, size) + 34 + (b - 1) * 78 + MathF.Sin(x / 170f + b * 1.7f) * 14 + Noise.Fbm2(x / 120f, b, 31) * 10;
        int bands = (int)(size.Y / 78) + 3;
        for (int b = 0; b < bands; b++)
        {
            var poly = new Vector2[2 * (steps + 1)];
            for (int k = 0; k <= steps; k++)
            {
                poly[k] = new Vector2(xs[k], Band(b, xs[k]));
                poly[2 * steps + 1 - k] = new Vector2(xs[k], b + 1 < bands ? Band(b + 1, xs[k]) + 1 : size.Y + 2);
            }
            Color fill = b == 0 ? Soil : (b % 2 == 0 ? RockA : RockB);
            fill = fill.Lerp(Bedrock, Mathf.Clamp((b - 2) / (float)bands, 0, 0.7f));
            c.DrawColoredPolygon(poly, fill);
        }
        // Grass and snow along the skyline.
        var sky = xs.Select(x => new Vector2(x, Ridge(x, size))).ToArray();
        c.DrawPolyline(sky, new Color(0.28f, 0.36f, 0.2f), 3);
        Vector2[] snow = sky.Where(p => p.Y < 125).ToArray();
        if (snow.Length >= 2) c.DrawPolyline(snow, new Color(0.86f, 0.89f, 0.93f), 5);

        // Ore and gems in the rock right of the shaft, catching the light now and then.
        float shaftX = HallX + HallW + 64;
        for (int i = 0; i < 46; i++)
        {
            float x = shaftX + 60 + Noise.Hash01(i, 3, 0, 11) * (size.X - shaftX - 80);
            float y = Ridge(x, size) + 70 + Noise.Hash01(i, 4, 0, 11) * (size.Y - Ridge(x, size) - 160);
            bool gem = i % 5 == 0;
            Color col = gem ? new Color(0.45f, 0.8f, 0.78f) : new Color(0.85f, 0.68f, 0.3f);
            float glint = MathF.Max(0, MathF.Sin(_t * 0.7f + i * 2.3f));
            col.A = 0.35f + 0.5f * glint * glint * glint;
            float r = gem ? 3.5f : 2.5f;
            c.DrawColoredPolygon(new[] { new Vector2(x, y - r), new Vector2(x + r, y), new Vector2(x, y + r), new Vector2(x - r, y) }, col);
        }

        // A cavern: a ragged pocket hung with stalactites, a still pool on its floor, glowing caps.
        Vector2 cav = new(MathF.Max(shaftX + 330, size.X * 0.78f), size.Y * 0.64f);
        const float cw = 190, ch = 62;
        var roof = new List<Vector2>();
        var floor = new List<Vector2>();
        for (int k = 0; k <= 38; k++)
        {
            float u = -1 + 2 * k / 38f, arc = MathF.Sqrt(MathF.Max(0, 1 - u * u));
            float x = cav.X + u * cw;
            float drip = Noise.Hash01(k, 7, 0, 3) < 0.28f ? 6 + Noise.Hash01(k, 8, 0, 3) * 20 : 0;
            roof.Add(new Vector2(x, cav.Y - ch * arc * (0.8f + Noise.Fbm2(u * 3, 1, 19) * 0.5f) + drip * arc));
            floor.Add(new Vector2(x, cav.Y + ch * 0.55f * arc * (0.85f + Noise.Fbm2(u * 3, 2, 19) * 0.4f)));
        }
        var pocketPoly = new List<Vector2>(roof);
        for (int k = floor.Count - 1; k >= 0; k--) pocketPoly.Add(floor[k]);
        c.DrawColoredPolygon(pocketPoly.ToArray(), new Color(0.04f, 0.04f, 0.055f));
        c.DrawPolyline(pocketPoly.Append(pocketPoly[0]).ToArray(), new Color(0.27f, 0.23f, 0.22f), 2);
        float water = cav.Y + ch * 0.18f;
        var pool = floor.Where(p => p.Y > water + 2).ToList();
        if (pool.Count >= 2)
        {
            var poolPoly = new List<Vector2> { new(pool[0].X, water) };
            poolPoly.Add(new Vector2(pool[^1].X, water));
            for (int k = pool.Count - 1; k >= 0; k--) poolPoly.Add(pool[k]);
            c.DrawColoredPolygon(poolPoly.ToArray(), new Color(0.12f, 0.25f, 0.38f));
            c.DrawLine(new Vector2(pool[0].X, water), new Vector2(pool[^1].X, water), new Color(0.45f, 0.7f, 0.9f, 0.45f + 0.15f * MathF.Sin(_t * 1.3f)), 2);
        }
        foreach (int k in new[] { 4, 7, 31, 34 })
        {
            Vector2 p = floor[k] + new Vector2(0, -3);
            c.DrawCircle(p, 8, new Color(0.35f, 0.9f, 0.85f, 0.10f));
            c.DrawCircle(p, 2.5f, new Color(0.5f, 0.95f, 0.9f, 0.9f));
        }

        // A magma pool deep on the right, breathing.
        float breathe = 0.75f + 0.25f * MathF.Sin(_t * 0.9f);
        var seam = new List<Vector2>();
        float mx0 = shaftX + 160;
        for (int k = 0; k <= 40; k++)
        {
            float x = mx0 + k / 40f * (size.X - mx0);
            float edge = Mathf.SmoothStep(0, 1, MathF.Min(1, k / 6f));
            seam.Add(new Vector2(x, size.Y - 8 - 34 * edge + Noise.Fbm2(x / 60f, 0, 5) * 10));
        }
        var magma = new List<Vector2>(seam) { new(size.X, size.Y + 2), new(mx0, size.Y + 2) };
        c.DrawPolyline(seam.ToArray(), new Color(1f, 0.45f, 0.12f, 0.12f * breathe), 40);
        c.DrawColoredPolygon(magma.ToArray(), new Color(0.36f, 0.09f, 0.03f));
        c.DrawPolyline(seam.Select(p => p + new Vector2(0, 6)).ToArray(), new Color(0.85f, 0.3f, 0.06f, 0.55f * breathe), 8);
        c.DrawPolyline(seam.ToArray(), new Color(1f, 0.66f, 0.24f, 0.85f * breathe), 3);

        // The stair shaft from the surface down past every hall, and the gate where it comes out.
        float top = Ridge(shaftX, size);
        float bottom = HallTop + 3 * HallH + 2 * HallGap + 40;
        c.DrawRect(new Rect2(shaftX - 15, top + 4, 30, bottom - top), new Color(0.05f, 0.04f, 0.04f));
        for (float y = top + 18; y < bottom; y += 14)
            c.DrawLine(new Vector2(shaftX - 11, y), new Vector2(shaftX + 11, y), new Color(0.22f, 0.17f, 0.13f), 2);
        float gx = shaftX - 70, gy = Ridge(gx, size);
        c.DrawCircle(new Vector2(gx, gy - 14), 50, new Color(1, 0.55f, 0.18f, 0.07f));
        c.DrawRect(new Rect2(gx - 12, gy - 26, 24, 26), new Color(0.88f, 0.52f, 0.2f));
        c.DrawCircle(new Vector2(gx, gy - 26), 12, new Color(0.88f, 0.52f, 0.2f));

        // The halls, each where its slot sits.
        for (int i = 0; i < _hallInfo.Count; i++) DrawHall(c, _hallInfo[i], i, shaftX);
    }

    private void DrawHall(UITheme.Drawn c, Hall h, int index, float shaftX)
    {
        if (!IsInstanceValid(h.Box)) return;
        Rect2 r = new(h.Box.GlobalPosition - c.GlobalPosition, h.Box.Size);
        float floorY = r.End.Y;
        bool hot = _hover == index;
        if (!h.Dug)
        {
            // Still rock: a surveyor's chalk outline of the hall that could be.
            DashRect(c, r.Grow(-2), new Color(Chalk, hot ? 0.75f : 0.35f));
            c.DrawLine(new Vector2(r.End.X, r.Position.Y + r.Size.Y * 0.5f), new Vector2(shaftX - 15, r.Position.Y + r.Size.Y * 0.5f), new Color(Chalk, 0.25f), 1);
            return;
        }
        // A passage to the shaft.
        float py = floorY - 34;
        c.DrawRect(new Rect2(r.End.X - 2, py, shaftX - 15 - r.End.X + 4, 34), h.Fallen ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.12f, 0.075f, 0.045f));

        if (h.Fallen)
        {
            c.DrawRect(r, new Color(0.075f, 0.08f, 0.095f));
            c.DrawRect(new Rect2(r.Position.X, floorY - 8, r.Size.X, 8), new Color(0.13f, 0.135f, 0.15f));
            Texture2D coffin = Assets.Sprite("furn-coffin");
            for (int k = 0; k < 3; k++)
            {
                var at = new Rect2(r.End.X - 260 + k * 76, floorY - 66, 60, 60);
                if (coffin != null) c.DrawTextureRect(coffin, at, false, new Color(0.6f, 0.65f, 0.75f));
                else c.DrawRect(new Rect2(at.Position.X + 8, at.End.Y - 16, 32, 14), new Color(0.25f, 0.22f, 0.2f));
            }
            c.DrawRect(r, new Color(0.3f, 0.32f, 0.38f, hot ? 0.8f : 0.4f), false, 1);
            return;
        }

        // A lit hall: dark vault, warm floor, carved pillars, two braziers, its clans at work.
        float flick = 0.8f + 0.2f * MathF.Sin(_t * 8.3f + index * 2) * MathF.Sin(_t * 5.1f + index);
        float warm = (hot ? 1.15f : 1f) * flick;
        int rows = 12;
        for (int k = 0; k < rows; k++)
        {
            float t = k / (float)(rows - 1);
            Color col = new Color(0.075f, 0.05f, 0.04f).Lerp(new Color(0.36f, 0.2f, 0.09f), t * t * warm);
            c.DrawRect(new Rect2(r.Position.X, r.Position.Y + r.Size.Y * k / rows, r.Size.X, r.Size.Y / rows + 1), col);
        }
        for (float x = r.Position.X + 560; x < r.End.X - 20; x += 100)
        {
            c.DrawRect(new Rect2(x, r.Position.Y + 6, 12, r.Size.Y - 14), new Color(0.2f, 0.13f, 0.08f, 0.8f));
            c.DrawRect(new Rect2(x - 3, r.Position.Y + 6, 18, 6), new Color(0.26f, 0.17f, 0.1f));
        }
        c.DrawRect(new Rect2(r.Position.X, floorY - 8, r.Size.X, 8), new Color(0.45f, 0.3f, 0.18f));
        foreach (float bx in new[] { r.End.X - 300, r.End.X - 40 })
        {
            Vector2 fire = new(bx, floorY - 22);
            // Light that stays in the hall: a fan of glow above the floor, not a disc through it.
            for (int g = 0; g < 10; g++)
            {
                float rad = (14 + g * 8) * warm;
                Rect2 glow = new Rect2(bx - rad, floorY - 8 - rad * 1.1f, rad * 2, rad * 1.1f).Intersection(r);
                c.DrawRect(glow, new Color(Fire, 0.022f));
            }
            c.DrawRect(new Rect2(bx - 7, floorY - 18, 14, 10), new Color(0.15f, 0.1f, 0.07f));
            c.DrawCircle(fire + new Vector2(0, -2), 5 + 2 * flick, new Color(1f, 0.7f, 0.3f, 0.95f));
        }
        // The dwarves: the painted figure of each clan in the hold, pacing their stretch of floor.
        for (int k = 0; k < h.Clans.Length; k++)
        {
            float lo = r.End.X - 262 + k * 230f / MathF.Max(1, h.Clans.Length), span = 230f / MathF.Max(1, h.Clans.Length) - 50;
            float ph = _t * (0.22f + 0.07f * k) + k * 1.9f;
            float x = lo + (MathF.Sin(ph) * 0.5f + 0.5f) * span;
            bool left = MathF.Cos(ph) < 0;
            Texture2D tex = Assets.Sprite("fig-" + h.Clans[k]);
            const float s = 64;
            if (tex != null)
            {
                c.DrawSetTransform(new Vector2(x, floorY - 6 - s), 0, new Vector2(left ? -1 : 1, 1));
                c.DrawTextureRect(tex, new Rect2(-s / 2, 0, s, s), false);
                c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                Color body = UITheme.C(Tables.Clan(h.Clans[k]).Color);
                c.DrawRect(new Rect2(x - 7, floorY - 30, 14, 22), body);
                c.DrawCircle(new Vector2(x, floorY - 35), 6, new Color(0.85f, 0.7f, 0.55f));
            }
        }
        c.DrawRect(r, hot ? UITheme.EdgeLit : new Color(UITheme.Edge, 0.7f), false, 1);
    }

    private static void DashRect(UITheme.Drawn c, Rect2 r, Color col)
    {
        void Dash(Vector2 a, Vector2 b)
        {
            float len = a.DistanceTo(b);
            Vector2 d = (b - a) / len;
            for (float s = 0; s < len; s += 14) c.DrawLine(a + d * s, a + d * MathF.Min(s + 8, len), col, 1.5f);
        }
        Dash(r.Position, new Vector2(r.End.X, r.Position.Y));
        Dash(new Vector2(r.End.X, r.Position.Y), r.End);
        Dash(r.End, new Vector2(r.Position.X, r.End.Y));
        Dash(new Vector2(r.Position.X, r.End.Y), r.Position);
    }
}
