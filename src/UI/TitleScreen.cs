using System;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The title: a mountain in cross-section by night, its halls lit within,
/// and the three fortress slots -- continue one, or found a new hold.
/// </summary>
public partial class TitleScreen : Control
{
    public Root Root;
    private VBoxContainer _slots;
    private Control _dialog;
    private float _t;
    private readonly Rng _rng = new((ulong)DateTime.Now.Ticks);

    public override void _Ready()
    {
        FortressScreen.Anchor(this, 0, 0, 1, 1, 0, 0, 0, 0);
        Theme = UITheme.Theme;
        var bg = new UITheme.Drawn { OnDraw = DrawBackdrop, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        FortressScreen.Anchor(bg, 0, 0, 1, 1, 0, 0, 0, 0);
        var col = UITheme.V(10);
        col.CustomMinimumSize = new Vector2(560, 0);
        AddChild(col);
        FortressScreen.Anchor(col, 0, 0.5f, 0, 0.5f, 90, -300, 650, 300);
        col.AddChild(UITheme.L("HAMMERDEEP", 72, UITheme.Gold, UITheme.Title));
        col.AddChild(UITheme.L("Dig. Build. Brew. Endure the deep.", 20, UITheme.Text, UITheme.Body));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        _slots = UITheme.V(8);
        col.AddChild(_slots);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        HBoxContainer row = UITheme.H(8);
        var sound = new CheckBox { Text = "Sound", ButtonPressed = Profile.Current.Sound, FocusMode = FocusModeEnum.None };
        sound.Toggled += on => { Profile.Current.Sound = on; Profile.Current.Apply(); Profile.Current.Save(); };
        row.AddChild(sound);
        var music = new CheckBox { Text = "Music", ButtonPressed = Profile.Current.MusicOn, FocusMode = FocusModeEnum.None };
        music.Toggled += on => { Profile.Current.MusicOn = on; Music.Instance?.SetVolume(Profile.Current.MusicVolume, on); Profile.Current.Save(); };
        row.AddChild(music);
        var full = new CheckBox { Text = "Full screen (F11)", ButtonPressed = Profile.Current.Fullscreen, FocusMode = FocusModeEnum.None };
        full.Toggled += on => { Profile.Current.Fullscreen = on; Profile.Current.Apply(); Profile.Current.Save(); };
        row.AddChild(full);
        row.AddChild(UITheme.B("Quit", () => GetTree().Quit(), 15));
        col.AddChild(row);
        FillSlots();
    }

    private void FillSlots()
    {
        foreach (Node n in _slots.GetChildren()) n.QueueFree();
        for (int slot = 1; slot <= 3; slot++)
        {
            int s = slot;
            string peek = Profile.Peek(slot, out bool fallen);
            var v = UITheme.V(4);
            var panel = UITheme.Panel(v);
            v.AddChild(UITheme.L($"Slot {slot}", 13, UITheme.Bronze, UITheme.Title));
            v.AddChild(UITheme.L(peek ?? "An unclaimed mountain.", 17, peek == null ? UITheme.Faint : fallen ? UITheme.Ember : UITheme.Text));
            HBoxContainer h = UITheme.H(6);
            if (peek != null && !fallen) h.AddChild(UITheme.B("Continue", () => Root.Continue(s), 16));
            h.AddChild(UITheme.B(peek == null ? "Found a fortress" : "Found anew (overwrites)", () => Root.ShowWorld(s), 16));
            if (peek != null) h.AddChild(UITheme.B("Abandon", () => { Profile.DeleteSlot(s); FillSlots(); }, 14));
            v.AddChild(h);
            _slots.AddChild(panel);
        }
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
        foreach (Node n in GetChildren()) if (n is UITheme.Drawn d) d.QueueRedraw();
    }

    /// <summary>Night sky, a mountain cut open: strata, a stair shaft, halls by firelight.</summary>
    private void DrawBackdrop(UITheme.Drawn c)
    {
        Vector2 size = c.Size;
        int bands = 40;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)bands;
            c.DrawRect(new Rect2(0, size.Y * t, size.X, size.Y / bands + 1), new Color(0.05f, 0.06f, 0.11f).Lerp(new Color(0.16f, 0.11f, 0.12f), t));
        }
        for (int i = 0; i < 90; i++)
        {
            float x = Noise.Hash01(i, 0, 0, 5) * size.X, y = Noise.Hash01(i, 1, 0, 5) * size.Y * 0.45f;
            float tw = 0.5f + 0.5f * MathF.Sin(_t * (1 + i % 3) + i);
            c.DrawCircle(new Vector2(x, y), 1 + (i % 4 == 0 ? 0.6f : 0), new Color(1, 0.95f, 0.85f, 0.35f + 0.4f * tw));
        }
        // The mountain: a ridge from fBm, peaking right of centre.
        float baseY = size.Y * 0.98f;
        var ridge = new System.Collections.Generic.List<Vector2>();
        for (int k = 0; k <= 80; k++)
        {
            float x = k / 80f * size.X;
            float u = (x - size.X * 0.68f) / (size.X * 0.42f);
            float h = MathF.Max(0, 1 - u * u) * size.Y * 0.62f + Noise.Fbm2(k / 9f, 0, 77) * 60 - 30;
            ridge.Add(new Vector2(x, size.Y * 0.88f - MathF.Max(0, h)));
        }
        var poly = new System.Collections.Generic.List<Vector2>(ridge) { new(size.X, baseY + 10), new(0, baseY + 10) };
        c.DrawColoredPolygon(poly.ToArray(), new Color(0.14f, 0.12f, 0.12f));
        // Strata.
        for (int s = 1; s < 7; s++)
        {
            var line = ridge.Select(p => new Vector2(p.X, p.Y + s * 55 + MathF.Sin(p.X / 140 + s) * 10)).ToArray();
            c.DrawPolyline(line, new Color(0.26f, 0.22f, 0.2f, 0.5f), 2);
        }
        // Snow on the peak.
        Vector2[] snow = ridge.Where(p => p.Y < size.Y * 0.5f).ToArray();
        if (snow.Length >= 2) c.DrawPolyline(snow, new Color(0.85f, 0.88f, 0.92f, 0.85f), 4);
        // A shaft and halls inside, by firelight.
        float sx = size.X * 0.7f, top = ridge[(int)(0.7f * 80)].Y + 12;
        c.DrawRect(new Rect2(sx - 6, top, 12, size.Y * 0.62f), new Color(0.05f, 0.04f, 0.04f));
        for (int h = 0; h < 4; h++)
        {
            float y = top + 70 + h * 95;
            float w = 150 + Noise.Hash01(h, 2, 0, 9) * 140;
            bool left = h % 2 == 0;
            var r = new Rect2(left ? sx - w : sx, y, w, 34);
            float flick = 0.75f + 0.25f * MathF.Sin(_t * 9 + h * 2.1f) * MathF.Sin(_t * 5.3f + h);
            c.DrawRect(r, new Color(0.32f, 0.17f, 0.07f).Lerp(new Color(0.6f, 0.33f, 0.12f), flick * 0.6f));
            c.DrawRect(r.Grow(-4), new Color(0.12f, 0.07f, 0.04f, 0.4f));
            for (int k = 0; k < 3; k++)
            {
                Vector2 fire = new(r.Position.X + 25 + k * (w - 50) / 2, y + 22);
                c.DrawCircle(fire, 5 + flick * 2, new Color(1f, 0.55f, 0.15f, 0.8f));
                c.DrawCircle(fire, 14 + flick * 5, new Color(1f, 0.45f, 0.1f, 0.12f));
            }
            // Little dwarves at work.
            for (int k = 0; k < 2 + h % 2; k++)
            {
                float px = r.Position.X + 40 + ((k * 53 + _t * (k % 2 == 0 ? 14 : -11)) % (w - 80) + (w - 80)) % (w - 80);
                c.DrawCircle(new Vector2(px, y + 24), 4, new Color(0.15f, 0.1f, 0.08f));
                c.DrawCircle(new Vector2(px, y + 17), 3, new Color(0.2f, 0.13f, 0.1f));
            }
        }
        // A door at the foot, glowing.
        float dx = size.X * 0.52f, dy = size.Y * 0.86f;
        c.DrawRect(new Rect2(dx, dy - 40, 30, 40), new Color(0.85f, 0.5f, 0.18f, 0.85f));
        c.DrawCircle(new Vector2(dx + 15, dy - 40), 15, new Color(0.85f, 0.5f, 0.18f, 0.85f));
        c.DrawCircle(new Vector2(dx + 15, dy - 20), 60, new Color(1, 0.5f, 0.15f, 0.06f));
        // A vignette behind the menu so it reads.
        for (int i = 0; i < 24; i++)
            c.DrawRect(new Rect2(0, 0, 720 - i * 22, size.Y), new Color(0.02f, 0.02f, 0.03f, 0.035f));
    }
}
