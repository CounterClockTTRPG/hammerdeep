using System;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The look of every screen: a dwarven hall by forge-light. Soot-dark slate
/// panels with bronze frames, parchment text, ember orange for what needs
/// attention, moss green for what is good. Cinzel for titles (carved
/// capitals), Barlow for reading, Inconsolata for figures.
/// </summary>
public static partial class UITheme
{
    public static readonly Color Text = new(0.93f, 0.88f, 0.76f);
    public static readonly Color Dim = new(0.66f, 0.61f, 0.52f);
    public static readonly Color Faint = new(0.44f, 0.4f, 0.35f);
    public static readonly Color Bronze = new(0.78f, 0.56f, 0.28f);
    public static readonly Color Gold = new(0.98f, 0.8f, 0.36f);
    public static readonly Color Ember = new(1f, 0.52f, 0.2f);
    public static readonly Color Good = new(0.56f, 0.86f, 0.46f);
    public static readonly Color Bad = new(0.95f, 0.36f, 0.3f);
    public static readonly Color Water = new(0.4f, 0.7f, 0.95f);
    public static readonly Color PanelFill = new(0.075f, 0.068f, 0.062f, 0.94f);
    public static readonly Color PanelFill2 = new(0.11f, 0.098f, 0.085f, 0.96f);
    public static readonly Color Edge = new(0.5f, 0.36f, 0.19f);
    public static readonly Color EdgeLit = new(0.86f, 0.64f, 0.32f);

    private static Font _title, _body, _label, _mono;
    private static Theme _theme;

    public static Font Title => _title ??= Load("Cinzel.ttf", 700);
    public static Font Body => _body ??= Load("Barlow-Regular.ttf", 400);
    public static Font LabelFont => _label ??= Load("BarlowCondensed-SemiBold.ttf", 600);
    public static Font Mono => _mono ??= Load("Inconsolata.ttf", 500);

    private static Font Load(string file, int weight)
    {
        string path = $"res://assets/fonts/{file}";
        if (!FileAccess.FileExists(path)) return ThemeDB.FallbackFont;
        var f = new FontFile { Data = FileAccess.GetFileAsBytes(path), Antialiasing = TextServer.FontAntialiasing.Gray };
        TextServer ts = TextServerManager.GetPrimaryInterface();
        return new FontVariation { BaseFont = f, VariationOpentype = new Godot.Collections.Dictionary { { ts.NameToTag("wght"), weight } } };
    }

    public static StyleBoxFlat Box(Color fill, Color edge, int border = 1, int radius = 3, int pad = 8)
    {
        var s = new StyleBoxFlat { BgColor = fill, BorderColor = edge, CornerDetail = 4 };
        s.SetBorderWidthAll(border);
        s.SetCornerRadiusAll(radius);
        s.ContentMarginLeft = s.ContentMarginRight = pad;
        s.ContentMarginTop = s.ContentMarginBottom = Math.Max(2, pad - 3);
        return s;
    }

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Theme { DefaultFont = Body, DefaultFontSize = 16 };
            t.SetColor("font_color", "Label", Text);
            t.SetColor("font_color", "Button", Text);
            t.SetColor("font_hover_color", "Button", Gold);
            t.SetColor("font_pressed_color", "Button", Gold);
            t.SetColor("font_disabled_color", "Button", Faint);
            t.SetColor("font_focus_color", "Button", Text);
            t.SetStylebox("normal", "Button", Box(PanelFill2, Edge, 1, 3, 8));
            t.SetStylebox("hover", "Button", Box(new Color(0.18f, 0.14f, 0.1f), EdgeLit, 1, 3, 8));
            t.SetStylebox("pressed", "Button", Box(new Color(0.28f, 0.19f, 0.1f), Gold, 1, 3, 8));
            t.SetStylebox("disabled", "Button", Box(new Color(0.07f, 0.065f, 0.06f), new Color(0.25f, 0.2f, 0.15f), 1, 3, 8));
            t.SetStylebox("focus", "Button", new StyleBoxEmpty());
            t.SetStylebox("panel", "PanelContainer", Box(PanelFill, Edge, 1, 4, 10));
            t.SetStylebox("panel", "Panel", Box(PanelFill, Edge, 1, 4, 10));
            t.SetStylebox("normal", "LineEdit", Box(new Color(0.04f, 0.035f, 0.03f), Edge, 1, 3, 8));
            t.SetStylebox("focus", "LineEdit", Box(new Color(0.04f, 0.035f, 0.03f), Gold, 1, 3, 8));
            t.SetColor("font_color", "LineEdit", Text);
            t.SetColor("caret_color", "LineEdit", Gold);
            t.SetColor("font_color", "CheckBox", Text);
            t.SetColor("font_hover_color", "CheckBox", Gold);
            t.SetColor("font_pressed_color", "CheckBox", Text);
            t.SetColor("font_hover_pressed_color", "CheckBox", Gold);
            t.SetStylebox("normal", "CheckBox", new StyleBoxEmpty());
            t.SetStylebox("hover", "CheckBox", new StyleBoxEmpty());
            t.SetStylebox("pressed", "CheckBox", new StyleBoxEmpty());
            t.SetStylebox("hover_pressed", "CheckBox", new StyleBoxEmpty());
            t.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());
            t.SetColor("font_color", "TooltipLabel", Text);
            t.SetStylebox("panel", "TooltipPanel", Box(PanelFill2, EdgeLit, 1, 3, 6));
            t.SetStylebox("scroll", "VScrollBar", Box(new Color(0.05f, 0.045f, 0.04f), new Color(0, 0, 0, 0), 0, 2, 2));
            t.SetStylebox("grabber", "VScrollBar", Box(Edge, Edge, 0, 2, 2));
            t.SetStylebox("grabber_highlight", "VScrollBar", Box(EdgeLit, EdgeLit, 0, 2, 2));
            t.SetStylebox("grabber_pressed", "VScrollBar", Box(Gold, Gold, 0, 2, 2));
            t.SetStylebox("panel", "PopupMenu", Box(PanelFill2, EdgeLit, 1, 3, 6));
            t.SetColor("font_color", "PopupMenu", Text);
            t.SetColor("font_hover_color", "PopupMenu", Gold);
            t.SetStylebox("hover", "PopupMenu", Box(new Color(0.22f, 0.16f, 0.1f), new Color(0, 0, 0, 0), 0, 2, 4));
            t.SetColor("font_color", "OptionButton", Text);
            t.SetStylebox("normal", "OptionButton", Box(PanelFill2, Edge, 1, 3, 8));
            t.SetStylebox("hover", "OptionButton", Box(new Color(0.18f, 0.14f, 0.1f), EdgeLit, 1, 3, 8));
            t.SetStylebox("pressed", "OptionButton", Box(new Color(0.28f, 0.19f, 0.1f), Gold, 1, 3, 8));
            t.SetStylebox("focus", "OptionButton", new StyleBoxEmpty());
            t.SetColor("font_color", "HSlider", Text);
            return _theme = t;
        }
    }

    // ---- builders -----------------------------------------------------------------------

    public static Label L(string text, int size = 16, Color? color = null, Font font = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        if (font != null) l.AddThemeFontOverride("font", font);
        return l;
    }

    public static Label Heading(string text, int size = 20) => L(text, size, Gold, Title);

    public static Label Wrap(string text, int size = 15, Color? color = null, int width = 280)
    {
        Label l = L(text, size, color ?? Dim);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(width, 0);
        return l;
    }

    public static Button B(string text, Action onPress, int size = 15, string tip = null)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, TooltipText = tip ?? "" };
        b.AddThemeFontSizeOverride("font_size", size);
        b.Pressed += () => { Sfx.Click(); onPress?.Invoke(); };
        return b;
    }

    public static PanelContainer Panel(Control content, Color? fill = null)
    {
        var p = new PanelContainer();
        if (fill != null) p.AddThemeStyleboxOverride("panel", Box(fill.Value, Edge, 1, 4, 10));
        p.AddChild(content);
        return p;
    }

    public static VBoxContainer V(int sep = 4)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        return v;
    }

    public static HBoxContainer H(int sep = 6)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        return h;
    }

    public static Color C(Rgb c, float a = 1) => new(c.R / 255f, c.G / 255f, c.B / 255f, a);

    /// <summary>A small bar: <paramref name="frac"/> of the width filled.</summary>
    public static Control Bar(float frac, Color color, int width = 120, int height = 8)
    {
        var bar = new BarControl { Frac = Mathf.Clamp(frac, 0, 1), Fill = color, CustomMinimumSize = new Vector2(width, height), MouseFilter = Control.MouseFilterEnum.Ignore };
        return bar;
    }

    private partial class BarControl : Control
    {
        public float Frac;
        public Color Fill;
        public override void _Draw()
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.03f, 0.03f, 0.03f));
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * Frac, Size.Y)), Fill);
            DrawRect(new Rect2(Vector2.Zero, Size), Edge, false, 1);
        }
    }

    /// <summary>A Control that draws itself with a callback (cheap custom widgets).</summary>
    public partial class Drawn : Control
    {
        public Action<Drawn> OnDraw;
        public override void _Draw() => OnDraw?.Invoke(this);
    }
}
