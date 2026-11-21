using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Speech bubbles: what dwarves say (Fortress.Spoken), over their heads for a few seconds of real time.</summary>
public partial class FortressScreen
{
    private sealed class Bubble
    {
        public int Dwarf;
        public string Text;
        public double Born;
    }

    private const double BubbleLife = 4.5, BubbleFade = 0.6;
    private const int MaxBubbles = 6;
    private readonly List<Bubble> _bubbles = new();
    private UITheme.Drawn _bubbleLayer;

    private void BuildBubbles()
    {
        _bubbleLayer = new UITheme.Drawn { OnDraw = DrawBubbles, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_bubbleLayer);
        Anchor(_bubbleLayer, 0, 0, 1, 1, 0, 0, 0, 0);
        // Under the panels: a bubble never hides a button.
        MoveChild(_bubbleLayer, 0);
    }

    private static double Now => Time.GetTicksMsec() / 1000.0;

    private void StepBubbles()
    {
        if (F.Spoken.Count > 0)
        {
            if (Profile.Current?.SpeechBubbles ?? true)
                foreach (Speech s in F.Spoken)
                {
                    _bubbles.RemoveAll(b => b.Dwarf == s.Dwarf);
                    _bubbles.Add(new Bubble { Dwarf = s.Dwarf, Text = s.Text, Born = Now });
                }
            F.Spoken.Clear();
        }
        _bubbles.RemoveAll(b => Now - b.Born > BubbleLife || F.DwarfById(b.Dwarf) is not { Dead: false });
        while (_bubbles.Count > MaxBubbles) _bubbles.RemoveAt(0);
        _bubbleLayer?.QueueRedraw();
    }

    private void DrawBubbles(UITheme.Drawn c)
    {
        Font font = UITheme.Body;
        const int fs = 14;
        var placed = new List<Rect2>();
        var draw = new List<(Rect2 r, Vector2[] tail, string text, float alpha)>();
        // Newest last, so it is drawn on top; placed from the newest down so older ones make way.
        foreach (Bubble b in _bubbles.AsEnumerable().Reverse().ToList())
        {
            Dwarf d = F.DwarfById(b.Dwarf);
            if (F.World.Pos(d.Cell).Z != Map.Z) continue;
            if (Map.Is3D && Map.Three.Behind(Map.DwarfPos(d), Map.Z)) continue;
            Vector2 head = Map.HeadOnScreen(d);
            if (head.X < -100 || head.Y < -40 || head.X > Size.X + 100 || head.Y > Size.Y + 40) continue;
            double age = Now - b.Born;
            float alpha = (float)Mathf.Clamp(System.Math.Min(age / 0.15, (BubbleLife - age) / BubbleFade), 0, 1);
            Vector2 sz = font.GetStringSize(b.Text, HorizontalAlignment.Left, -1, fs);
            var r = new Rect2(head - new Vector2(sz.X / 2 + 7, sz.Y + 14), sz + new Vector2(14, 6));
            // Shift up past any bubble already placed where this one would go.
            for (int k = 0; k < placed.Count; k++)
                if (placed[k].Intersects(r)) { r.Position = new Vector2(r.Position.X, placed[k].Position.Y - r.Size.Y - 3); k = -1; }
            placed.Add(r);
            // A short tail from the bubble towards the speaker (a stacked bubble does not reach all the way down).
            Vector2 tipBase = new(Mathf.Clamp(head.X, r.Position.X + 8, r.End.X - 8), r.End.Y - 1);
            Vector2 tip = tipBase + (new Vector2(head.X, head.Y - 2) - tipBase).LimitLength(12);
            draw.Add((r, new[] { tipBase + new Vector2(-5, 0), tipBase + new Vector2(5, 0), tip }, b.Text, alpha));
        }
        // Tails first, under every bubble; then the bubbles.
        foreach (var (r, tail, text, alpha) in draw)
        {
            c.DrawColoredPolygon(tail, new Color(0.96f, 0.92f, 0.82f, 0.95f * alpha));
            c.DrawPolyline(new[] { tail[0], tail[2], tail[1] }, new Color(0.36f, 0.25f, 0.12f, alpha), 1.2f, true);
        }
        foreach (var (r, tail, text, alpha) in draw)
        {
            var box = new StyleBoxFlat { BgColor = new Color(0.96f, 0.92f, 0.82f, 0.95f * alpha), BorderColor = new Color(0.36f, 0.25f, 0.12f, alpha), CornerDetail = 4 };
            box.SetBorderWidthAll(1);
            box.SetCornerRadiusAll(7);
            c.DrawStyleBox(box, r);
            c.DrawString(font, new Vector2(r.Position.X + 7, r.Position.Y + 3 + font.GetAscent(fs)), text, HorizontalAlignment.Left, -1, fs, new Color(0.16f, 0.11f, 0.07f, alpha));
        }
    }
}
