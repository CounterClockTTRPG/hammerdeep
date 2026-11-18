using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The map, one level at a time. Terrain is pixel art painted per chunk
/// (8 art pixels per cell, shown at 16) and repainted only where the world
/// changed; open air shows the levels below, dimmer with depth. Zones,
/// buildings, items, designations and dwarves are drawn over it each frame.
/// </summary>
public partial class MapView : Node2D
{
    public const int T = 16;
    private const int A = 8, CH = 32;
    public Fortress F;
    private World W => F.World;
    public int Z;
    public Camera2D Cam;

    private readonly Dictionary<int, ImageTexture> _chunks = new();
    private readonly HashSet<int> _dirty = new();
    private int _chunksZ = -1, _season = -1;
    private readonly byte[] _buf = new byte[CH * A * CH * A * 4];

    // What the screen wants shown.
    public P3? Hover;
    public (P3 a, P3 b)? Box;
    public Color BoxColor = new(1, 0.85f, 0.3f, 0.35f);
    public string Ghost;
    public bool GhostOk;
    public int SelDwarf = -1, SelBuilding = -1, SelZone = -1;
    public bool ShowNames;
    public float Time;

    public void Bind(Fortress f)
    {
        F = f;
        Z = f.Embark.Z;
        Cam = new Camera2D { Name = "Cam", Zoom = new Vector2(2, 2) };
        AddChild(Cam);
        Cam.MakeCurrent();
        CenterOn(f.Embark);
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void CenterOn(P3 p)
    {
        Cam.Position = new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T);
        SetZ(p.Z);
    }

    public void SetZ(int z)
    {
        z = Math.Clamp(z, 0, W.D - 1);
        if (z == Z && _chunksZ == z) return;
        Z = z;
        _chunks.Clear();
        _dirty.Clear();
        _chunksZ = z;
    }

    public Vector2 CellCenter(int cell) { P3 p = W.Pos(cell); return new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T); }

    /// <summary>The cell under a screen point, on the level in view.</summary>
    public P3 CellAt(Vector2 screen)
    {
        Vector2 w = GetCanvasTransform().AffineInverse() * screen;
        return new P3((int)MathF.Floor(w.X / T), (int)MathF.Floor(w.Y / T), Z);
    }

    public Vector2 ScreenOf(P3 p) => GetCanvasTransform() * new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T);

    public Rect2 ViewRect()
    {
        Vector2 size = GetViewportRect().Size / Cam.Zoom;
        return new Rect2(Cam.GetScreenCenterPosition() - size / 2, size);
    }

    public override void _Process(double delta)
    {
        if (F == null) return;
        Time += (float)delta;
        if (F.Season != _season) { _season = F.Season; _chunks.Clear(); }
        // The world says what changed; a change at z shows on z..z+3 (seen from above through air).
        if (W.Changed.Count > 0)
        {
            foreach (int c in W.Changed)
            {
                P3 p = W.Pos(c);
                if (p.Z > Z || p.Z < Z - 4) continue;
                int key = Key(p.X / CH, p.Y / CH);
                _dirty.Add(key);
                // Wall edges look at their neighbours.
                if (p.X % CH == 0 && p.X > 0) _dirty.Add(Key(p.X / CH - 1, p.Y / CH));
                if (p.X % CH == CH - 1 && p.X < W.W - 1) _dirty.Add(Key(p.X / CH + 1, p.Y / CH));
                if (p.Y % CH == 0 && p.Y > 0) _dirty.Add(Key(p.X / CH, p.Y / CH - 1));
                if (p.Y % CH == CH - 1 && p.Y < W.H - 1) _dirty.Add(Key(p.X / CH, p.Y / CH + 1));
            }
            W.Changed.Clear();
        }
        foreach (int k in _dirty) _chunks.Remove(k);
        _dirty.Clear();
        QueueRedraw();
    }

    private static int Key(int cx, int cy) => cy * 1024 + cx;

    private ImageTexture Chunk(int cx, int cy)
    {
        int key = Key(cx, cy);
        if (_chunks.TryGetValue(key, out ImageTexture t)) return t;
        Paint(cx, cy);
        var img = Image.CreateFromData(CH * A, CH * A, false, Image.Format.Rgba8, _buf);
        t = ImageTexture.CreateFromImage(img);
        _chunks[key] = t;
        return t;
    }

    // ---- terrain art ------------------------------------------------------------------

    private void Paint(int cx, int cy)
    {
        for (int ly = 0; ly < CH; ly++)
            for (int lx = 0; lx < CH; lx++)
            {
                int x = cx * CH + lx, y = cy * CH + ly;
                if (x >= W.W || y >= W.H) { Fill(lx * A, ly * A, new Rgb(0, 0, 0)); continue; }
                PaintCell(x, y, Z, lx * A, ly * A, 1f, 0);
            }
    }

    private void Px(int px, int py, Rgb c)
    {
        int o = (py * CH * A + px) * 4;
        _buf[o] = c.R; _buf[o + 1] = c.G; _buf[o + 2] = c.B; _buf[o + 3] = 255;
    }

    private Rgb Get(int px, int py) { int o = (py * CH * A + px) * 4; return new Rgb(_buf[o], _buf[o + 1], _buf[o + 2]); }

    private void Fill(int ox, int oy, Rgb c)
    {
        for (int j = 0; j < A; j++) for (int i = 0; i < A; i++) Px(ox + i, oy + j, c);
    }

    private static readonly Rgb Void = new(6, 7, 12), HiddenC = new(9, 8, 8), Haze = new(22, 28, 44);

    private void PaintCell(int x, int y, int z, int ox, int oy, float bright, int depth)
    {
        int i = W.Idx(x, y, z);
        uint seed = (uint)i * 2654435761u;
        if (W.Has(i, CellFlag.Hidden))
        {
            for (int j = 0; j < A; j++)
                for (int k = 0; k < A; k++)
                    Px(ox + k, oy + j, Noise.Hash01(k, j, i, 3) < 0.03f ? new Rgb(20, 17, 15) : HiddenC);
            return;
        }
        Shape s = W.Shapes[i];
        Mat m = Tables.Mats[W.Mats[i]];
        switch (s)
        {
            case Shape.Empty:
                if (z > 0 && depth < 3) { PaintCell(x, y, z - 1, ox, oy, bright * 0.72f, depth + 1); Tint(ox, oy, Haze, 0.18f); }
                else Fill(ox, oy, Void);
                return;
            case Shape.Wall: PaintWall(i, x, y, z, m, ox, oy); break;
            case Shape.Water: PaintLiquid(i, ox, oy, new Rgb(34, 78, 140), new Rgb(70, 130, 200)); break;
            case Shape.Magma: PaintLiquid(i, ox, oy, new Rgb(170, 40, 10), new Rgb(255, 150, 40)); break;
            default:
                PaintFloor(i, x, y, z, m, ox, oy);
                if (s == Shape.Tree) PaintTree(i, m, ox, oy);
                else if (s is Shape.StairUp or Shape.StairDown or Shape.StairUD or Shape.Ramp) PaintGlyph(s, ox, oy);
                else if (W.Has(i, CellFlag.Shrub)) PaintShrub(i, ox, oy);
                break;
        }
        if (bright < 0.999f) Dim(ox, oy, bright);
    }

    private void Dim(int ox, int oy, float f)
    {
        for (int j = 0; j < A; j++) for (int k = 0; k < A; k++) Px(ox + k, oy + j, Get(ox + k, oy + j).Scale(f));
    }

    private void Tint(int ox, int oy, Rgb c, float t)
    {
        for (int j = 0; j < A; j++) for (int k = 0; k < A; k++) Px(ox + k, oy + j, Get(ox + k, oy + j).Mix(c, t));
    }

    private bool OpenAt(int x, int y, int z)
    {
        if (!W.In(x, y, z)) return false;
        int j = W.Idx(x, y, z);
        return !W.Has(j, CellFlag.Hidden) && !World.IsSolid(W.Shapes[j]);
    }

    private void PaintWall(int i, int x, int y, int z, Mat m, int ox, int oy)
    {
        // Raised stone: lighter than the floors around it, in chunky 2x2 grain with cracks.
        bool built = W.Has(i, CellFlag.Built);
        Rgb host = m.Kind is MatKind.Ore or MatKind.Gem ? HostRock(x, y, z) : m.Color;
        Rgb baseC = host.Scale(m.Kind == MatKind.Soil ? 0.85f : 0.92f);
        for (int j = 0; j < A; j++)
            for (int k = 0; k < A; k++)
            {
                float n = Noise.Hash01(k / 2 + x * 4, j / 2 + y * 4, z, 7);
                float f = Noise.Hash01(k, j, i, 8);
                Rgb c = baseC.Scale(0.84f + n * 0.22f);
                if (built)
                {
                    bool mortar = j % 4 == 3 || (k + (j / 4 % 2) * 4) % 8 == 7;
                    c = mortar ? m.Color.Scale(0.5f) : m.Color.Scale(0.9f + f * 0.1f);
                }
                else
                {
                    if (f > 0.95f) c = baseC.Scale(0.62f);
                    if (m.Kind == MatKind.Ore && f > 0.66f) c = m.Color.Scale(0.95f + f * 0.45f);
                    if (m.Kind == MatKind.Ore && m.Key == "coal" && f > 0.5f) c = new Rgb(28, 26, 26);
                    if (m.Kind == MatKind.Gem && f > 0.84f) c = m.Color.Scale(1.25f + (f - 0.84f) * 3);
                }
                Px(ox + k, oy + j, c);
            }
        // A bevel where the stone meets open space: lit from the top left.
        Rgb lit = baseC.Scale(1.3f), shade = baseC.Scale(0.5f);
        if (OpenAt(x, y - 1, z)) for (int k = 0; k < A; k++) Px(ox + k, oy, lit);
        if (OpenAt(x - 1, y, z)) for (int j = 0; j < A; j++) Px(ox, oy + j, lit);
        if (OpenAt(x, y + 1, z)) for (int k = 0; k < A; k++) { Px(ox + k, oy + A - 1, shade); Px(ox + k, oy + A - 2, baseC.Scale(0.7f)); }
        if (OpenAt(x + 1, y, z)) for (int j = 0; j < A; j++) { Px(ox + A - 1, oy + j, shade); }
    }

    /// <summary>The rock around a vein: the nearest plain rock cell, for the ground colour.</summary>
    private Rgb HostRock(int x, int y, int z)
    {
        for (int r = 1; r < 4; r++)
            foreach (var (dx, dy) in new[] { (r, 0), (-r, 0), (0, r), (0, -r) })
            {
                if (!W.In(x + dx, y + dy, z)) continue;
                Mat h = Tables.Mats[W.Mats[W.Idx(x + dx, y + dy, z)]];
                if (h.Kind is MatKind.Rock or MatKind.Soil) return h.Color;
            }
        return new Rgb(110, 104, 98);
    }

    private Rgb GrassColor(uint h) => F.Season switch
    {
        0 => new Rgb(78, 120, 56),
        1 => new Rgb(70, 112, 46),
        2 => new Rgb(118, 106, 50),
        _ => new Rgb(200, 206, 212),
    };

    private bool SolidAt(int x, int y, int z) => W.In(x, y, z) && World.IsSolid(W.Shapes[W.Idx(x, y, z)]) && W.Shapes[W.Idx(x, y, z)] != Shape.Tree;

    private void PaintFloor(int i, int x, int y, int z, Mat m, int ox, int oy)
    {
        bool outside = W.IsOutside(i);
        bool grass = W.Has(i, CellFlag.Grass) && outside;
        bool built = W.Has(i, CellFlag.Built);
        // Recessed ground: darker than walls; under the sky a little brighter.
        Rgb baseC = grass ? GrassColor((uint)i) : m.Color.Scale(outside ? 0.62f : 0.46f);
        for (int j = 0; j < A; j++)
            for (int k = 0; k < A; k++)
            {
                float n = Noise.Hash01(k / 2 + x * 4, j / 2 + y * 4, z, 11);
                float f = Noise.Hash01(k, j, i, 12);
                Rgb c = baseC.Scale(0.94f + n * 0.1f);
                if (grass)
                {
                    if (f > 0.9f) c = baseC.Scale(F.Season == 3 ? 1.04f : 1.22f);
                    else if (f < 0.05f) c = baseC.Scale(0.8f);
                }
                else if (f > 0.96f) c = baseC.Scale(0.75f);
                if (built && (j == 0 || k == 0)) c = m.Color.Scale(0.35f);
                Px(ox + k, oy + j, c);
            }
        // Walls cast a soft shadow onto the floor at their foot.
        if (SolidAt(x, y - 1, z)) for (int k = 0; k < A; k++) { Px(ox + k, oy, Get(ox + k, oy).Scale(0.55f)); Px(ox + k, oy + 1, Get(ox + k, oy + 1).Scale(0.78f)); }
        if (SolidAt(x - 1, y, z)) for (int j = 0; j < A; j++) { Px(ox, oy + j, Get(ox, oy + j).Scale(0.6f)); Px(ox + 1, oy + j, Get(ox + 1, oy + j).Scale(0.82f)); }
    }

    private void PaintTree(int i, Mat m, int ox, int oy)
    {
        bool stalk = m.Key == "stalkwood";
        bool winter = F.Season == 3 && W.IsOutside(i);
        Rgb leaf = stalk ? new Rgb(120, 78, 150) : m.Key == "pine" ? new Rgb(34, 82, 52) : winter ? new Rgb(110, 96, 80) : F.Season == 2 ? new Rgb(170, 100, 40) : new Rgb(48, 104, 44);
        Rgb dark = leaf.Scale(0.6f), hi = leaf.Scale(1.4f);
        for (int j = 0; j < A; j++)
            for (int k = 0; k < A; k++)
            {
                float dx = k - 3.5f, dy = j - 3.3f, r = dx * dx + dy * dy;
                if (r > 13.5f) continue;
                Rgb c = r > 9 ? dark : leaf;
                if (dx < -0.5f && dy < -0.5f && r < 6) c = hi;
                if (Noise.Hash01(k, j, i, 13) < 0.15f) c = c.Scale(0.8f);
                if (winter && m.Key == "pine" && Noise.Hash01(k, j, i, 14) < 0.35f) c = new Rgb(225, 230, 235);
                Px(ox + k, oy + j, c);
            }
        if (stalk) { Px(ox + 3, oy + 3, new Rgb(230, 200, 255)); Px(ox + 5, oy + 2, new Rgb(230, 200, 255)); }
    }

    private void PaintShrub(int i, int ox, int oy)
    {
        bool cave = !W.IsOutside(i);
        Rgb leaf = cave ? new Rgb(50, 140, 120) : new Rgb(40, 96, 40), berry = cave ? new Rgb(140, 255, 220) : new Rgb(200, 40, 70);
        for (int j = 2; j < 7; j++)
            for (int k = 1; k < 7; k++)
            {
                float dx = k - 3.8f, dy = j - 4.4f;
                if (dx * dx + dy * dy > 6.5f) continue;
                Px(ox + k, oy + j, Noise.Hash01(k, j, i, 17) < 0.2f ? berry : leaf);
            }
    }

    private void PaintGlyph(Shape s, int ox, int oy)
    {
        Rgb lit = new(230, 214, 180), dark = new(20, 16, 14);
        switch (s)
        {
            case Shape.StairUp:
                for (int k = 1; k < 7; k++) { Px(ox + k, oy + 5, lit); }
                for (int k = 2; k < 6; k++) { Px(ox + k, oy + 3, lit); }
                Px(ox + 3, oy + 1, lit); Px(ox + 4, oy + 1, lit);
                break;
            case Shape.StairDown:
                for (int j = 1; j < 7; j++) for (int k = 1; k < 7; k++) Px(ox + k, oy + j, dark);
                for (int k = 2; k < 6; k++) Px(ox + k, oy + 2, lit);
                for (int k = 3; k < 5; k++) Px(ox + k, oy + 4, lit);
                break;
            case Shape.StairUD:
                for (int j = 1; j < 7; j++) for (int k = 1; k < 7; k++) Px(ox + k, oy + j, dark.Mix(lit, 0.15f));
                for (int k = 1; k < 7; k++) { Px(ox + k, oy + k, lit); Px(ox + 7 - k, oy + k, lit); }
                break;
            case Shape.Ramp:
                for (int k = 0; k < 4; k++) { Px(ox + 3 - k, oy + 2 + k, Get(ox + 3 - k, oy + 2 + k).Scale(1.45f)); Px(ox + 4 + k, oy + 2 + k, Get(ox + 4 + k, oy + 2 + k).Scale(1.45f)); }
                for (int k = 1; k < 3; k++) { Px(ox + 3 - k, oy + 3 + k, Get(ox + 3 - k, oy + 3 + k).Scale(0.7f)); Px(ox + 4 + k, oy + 3 + k, Get(ox + 4 + k, oy + 3 + k).Scale(0.7f)); }
                break;
        }
    }

    private void PaintLiquid(int i, int ox, int oy, Rgb deep, Rgb light)
    {
        for (int j = 0; j < A; j++)
            for (int k = 0; k < A; k++)
            {
                float n = Noise.Hash01(k, j, i, 19);
                Rgb c = deep.Scale(0.85f + n * 0.25f);
                if ((k + j * 3 + i) % 7 == 0 && n > 0.5f) c = light;
                Px(ox + k, oy + j, c);
            }
    }

    // ---- everything that moves ----------------------------------------------------------

    public override void _Draw()
    {
        if (F == null) return;
        Rect2 view = ViewRect().Grow(T * 2);
        int cx0 = Math.Max(0, (int)(view.Position.X / (CH * T))), cy0 = Math.Max(0, (int)(view.Position.Y / (CH * T)));
        int cx1 = Math.Min((W.W - 1) / CH, (int)(view.End.X / (CH * T))), cy1 = Math.Min((W.H - 1) / CH, (int)(view.End.Y / (CH * T)));
        int built = 0;
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                // A few new chunks per frame keeps scrolling smooth; the rest are drawn next frame.
                if (!_chunks.ContainsKey(Key(cx, cy)) && built++ > 10) continue;
                DrawTextureRect(Chunk(cx, cy), new Rect2(cx * CH * T, cy * CH * T, CH * T, CH * T), false);
            }
        // Map edge.
        DrawRect(new Rect2(0, 0, W.W * T, W.H * T), new Color(0.5f, 0.36f, 0.19f, 0.6f), false, 2);

        int x0 = Math.Max(0, (int)(view.Position.X / T)), y0 = Math.Max(0, (int)(view.Position.Y / T));
        int x1 = Math.Min(W.W - 1, (int)(view.End.X / T)), y1 = Math.Min(W.H - 1, (int)(view.End.Y / T));
        DrawZones(x0, y0, x1, y1);
        DrawDesignations(x0, y0, x1, y1);
        DrawBuildings(view);
        DrawItems(x0, y0, x1, y1);
        DrawDwarves(view);
        DrawCreatures(view);
        DrawCursor();
    }

    private void DrawZones(int x0, int y0, int x1, int y1)
    {
        foreach (Zone z in F.Zones)
        {
            if (z.Z != Z || z.X1 < x0 || z.X0 > x1 || z.Y1 < y0 || z.Y0 > y1) continue;
            var r = new Rect2(z.X0 * T, z.Y0 * T, (z.X1 - z.X0 + 1) * T, (z.Y1 - z.Y0 + 1) * T);
            bool sel = SelZone == z.Id;
            Color c = z.Kind switch
            {
                ZoneKind.Stockpile => new Color(0.95f, 0.82f, 0.45f),
                ZoneKind.Farm => new Color(0.55f, 0.85f, 0.35f),
                ZoneKind.Pasture => new Color(0.85f, 0.7f, 0.45f),
                _ => new Color(0.55f, 0.7f, 1f),
            };
            DrawRect(r, new Color(c, sel ? 0.16f : 0.07f));
            DrawDashed(r, new Color(c, sel ? 0.9f : 0.45f));
            if (z.Kind == ZoneKind.Farm)
                foreach (P3 p in z.Cells())
                {
                    int cell = W.Idx(p);
                    Vector2 at = new((p.X + 0.5f) * T, (p.Y + 0.5f) * T);
                    if (!F.Crops.TryGetValue(cell, out Crop cr)) { DrawRect(new Rect2(at - new Vector2(5, 0.5f), new Vector2(10, 1)), new Color(0.25f, 0.17f, 0.1f, 0.6f)); continue; }
                    if (cr.Planted > F.Tick) continue;
                    Mat cm = Tables.M(cr.Mat);
                    float g = Math.Clamp((F.Tick - cr.Planted) / (float)cm.Grow, 0, 1);
                    Color pc = UITheme.C(cm.Color);
                    DrawLine(at + new Vector2(0, 5), at + new Vector2(0, 5 - 9 * g), new Color(0.3f, 0.6f, 0.25f), 1.5f);
                    DrawCircle(at + new Vector2(0, 5 - 9 * g), 1 + 2.5f * g, g >= 1 ? pc : pc.Darkened(0.4f));
                }
        }
    }

    private void DrawDashed(Rect2 r, Color c)
    {
        Vector2[] pts = { r.Position, new(r.End.X, r.Position.Y), r.End, new(r.Position.X, r.End.Y), r.Position };
        for (int k = 0; k < 4; k++) DrawDashedLine(pts[k], pts[k + 1], c, 1, 4);
    }

    private void DrawDesignations(int x0, int y0, int x1, int y1)
    {
        foreach (int c in F.Designated)
        {
            P3 p = W.Pos(c);
            if (p.Z != Z || p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) continue;
            Desig g = W.Desigs[c];
            var r = new Rect2(p.X * T, p.Y * T, T, T);
            bool taken = F.Claims.ContainsKey(c);
            Color col = g switch
            {
                Desig.Dig => new Color(1f, 0.84f, 0.3f),
                Desig.Channel => new Color(0.85f, 0.5f, 1f),
                Desig.StairUD or Desig.StairDown or Desig.StairUp => new Color(0.4f, 0.9f, 1f),
                Desig.Chop => new Color(0.6f, 1f, 0.4f),
                Desig.Gather => new Color(0.6f, 1f, 0.8f),
                _ => new Color(1, 1, 1),
            };
            DrawRect(r, new Color(col, taken ? 0.32f : 0.18f));
            for (int k = 4; k < T * 2; k += 5)
            {
                Vector2 a = new(p.X * T + Math.Max(0, k - T), p.Y * T + Math.Min(T, k)), b = new(p.X * T + Math.Min(T, k), p.Y * T + Math.Max(0, k - T));
                DrawLine(a, b, new Color(col, 0.55f), 1);
            }
            DrawRect(r.Grow(-0.5f), new Color(col, 0.8f), false, 1);
            string glyph = g switch { Desig.StairUD => "X", Desig.StairDown => ">", Desig.StairUp => "<", Desig.Channel => "v", _ => null };
            if (glyph != null) DrawString(UITheme.Mono, new Vector2(p.X * T + 4, p.Y * T + 12), glyph, HorizontalAlignment.Left, -1, 11, col);
        }
    }

    private void DrawBuildings(Rect2 view)
    {
        foreach (Building b in F.Buildings)
        {
            if (b.Z != Z) continue;
            var r = new Rect2(b.X * T, b.Y * T, b.W * T, b.H * T);
            if (!view.Intersects(r)) continue;
            BuildDef d = b.D;
            Color mc = b.Built ? UITheme.C(Tables.Mats[b.Mat].Color) : new Color(0.7f, 0.7f, 0.7f);
            bool sel = SelBuilding == b.Id;
            if (!b.Built)
            {
                DrawRect(r.Grow(-1), new Color(0.6f, 0.7f, 0.9f, 0.12f));
                DrawDashed(r.Grow(-1), new Color(0.75f, 0.85f, 1f, 0.8f));
                DrawString(UITheme.LabelFont, r.Position + new Vector2(r.Size.X / 2 - 4, r.Size.Y / 2 + 5), d.Glyph.ToString(), HorizontalAlignment.Left, -1, d.W > 1 ? 18 : 12, new Color(0.8f, 0.9f, 1f, 0.8f));
                if (sel) DrawRect(r, UITheme.Gold, false, 1.5f);
                continue;
            }
            switch (d.Kind)
            {
                case BuildKind.Workshop: DrawWorkshop(b, r, mc); break;
                default: DrawFurniture(b, r, mc); break;
            }
            if (b.Artifact) DrawRect(r.Grow(0.5f), new Color(1f, 0.85f, 0.3f, 0.5f + 0.3f * MathF.Sin(Time * 3)), false, 1);
            if (b.MoodClaim >= 0) DrawRect(r.Grow(1), new Color(0.9f, 0.6f, 1f, 0.6f + 0.3f * MathF.Sin(Time * 5)), false, 2);
            if (sel) DrawRect(r.Grow(1), UITheme.Gold, false, 1.5f);
        }
    }

    private void DrawWorkshop(Building b, Rect2 r, Color mc)
    {
        Rect2 inner = r.Grow(-2);
        DrawRect(inner, mc.Darkened(0.55f));
        DrawRect(inner, mc.Darkened(0.1f), false, 2);
        // Benches along the back, an anvil or a hearth in the middle, by kind.
        Vector2 c = r.GetCenter();
        Color hot = new(1f, 0.5f, 0.15f, 0.6f + 0.3f * Mathf.Sin(Time * 6 + b.Id));
        bool busy = b.Orders.Any(o => o.Worker >= 0);
        switch (b.Def)
        {
            case "temple":
                for (int k = 0; k < 4; k++) DrawRect(new Rect2(inner.Position + new Vector2(3 + (k % 2) * (inner.Size.X - 10), 3 + (k / 2) * (inner.Size.Y - 10)), new Vector2(4, 4)), mc.Lightened(0.25f));
                DrawCircle(c, 6, new Color(1f, 0.85f, 0.4f, 0.25f + 0.1f * Mathf.Sin(Time * 2)));
                DrawCircle(c, 2.5f, new Color(1f, 0.85f, 0.4f));
                break;
            case "runeforge":
                for (int k = 0; k < 3; k++)
                {
                    Vector2 rp = c + new Vector2(-10 + k * 10, -8);
                    DrawLine(rp + new Vector2(-2, -3), rp + new Vector2(2, 3), new Color(0.3f, 0.95f, 1f, 0.6f + 0.4f * Mathf.Sin(Time * 3 + k)), 1.2f);
                    DrawLine(rp + new Vector2(2, -3), rp + new Vector2(-1, 0), new Color(0.3f, 0.95f, 1f, 0.6f + 0.4f * Mathf.Sin(Time * 3 + k)), 1.2f);
                }
                DrawCircle(c + new Vector2(0, 3), 5, new Color(0.15f, 0.1f, 0.08f));
                DrawCircle(c + new Vector2(0, 3), 3, busy ? new Color(0.3f, 0.9f, 1f) : new Color(0.2f, 0.35f, 0.4f));
                break;
            case "smelter": case "forge": case "furnace": case "kitchen":
                DrawCircle(c, 7, new Color(0.15f, 0.1f, 0.08f));
                DrawCircle(c, 5, busy ? hot : new Color(0.35f, 0.15f, 0.1f));
                break;
            case "brewery":
                for (int k = -1; k <= 1; k++) { DrawCircle(c + new Vector2(k * 11, -9), 4, new Color(0.45f, 0.3f, 0.16f)); DrawArc(c + new Vector2(k * 11, -9), 4, 0, Mathf.Tau, 12, new Color(0.25f, 0.16f, 0.08f), 1); }
                break;
            default:
                DrawRect(new Rect2(inner.Position + new Vector2(4, 4), new Vector2(inner.Size.X - 8, 7)), mc.Darkened(0.25f));
                break;
        }
        DrawString(UITheme.Title, new Vector2(c.X - 6, r.End.Y - 6), b.D.Glyph.ToString(), HorizontalAlignment.Left, -1, 14, UITheme.Gold);
        if (busy)
        {
            float bob = Mathf.Sin(Time * 8) * 1.5f;
            DrawCircle(c + new Vector2(14, -14 + bob), 2, UITheme.Ember);
        }
        if (b.Orders.Count > 0) DrawString(UITheme.Mono, r.Position + new Vector2(3, 11), b.Orders.Count.ToString(), HorizontalAlignment.Left, -1, 10, UITheme.Text);
    }

    private void DrawFurniture(Building b, Rect2 r, Color mc)
    {
        Vector2 c = r.GetCenter();
        Color dark = mc.Darkened(0.45f);
        switch (b.Def)
        {
            case "bed":
                DrawRect(new Rect2(r.Position + new Vector2(2, 1), new Vector2(12, 14)), dark);
                DrawRect(new Rect2(r.Position + new Vector2(3, 2), new Vector2(10, 4)), new Color(0.9f, 0.88f, 0.8f));
                DrawRect(new Rect2(r.Position + new Vector2(3, 6), new Vector2(10, 8)), b.Owner >= 0 ? new Color(0.55f, 0.2f, 0.2f) : new Color(0.4f, 0.35f, 0.5f));
                break;
            case "table":
                DrawRect(r.Grow(-2), mc.Darkened(0.15f));
                DrawRect(r.Grow(-2), dark, false, 1);
                break;
            case "chair":
                DrawRect(new Rect2(c - new Vector2(4, 4), new Vector2(8, 8)), mc.Darkened(0.2f));
                DrawRect(new Rect2(c - new Vector2(4, 5), new Vector2(8, 2)), dark);
                break;
            case "door":
                DrawRect(r.Grow(-1), dark);
                DrawRect(new Rect2(r.Position + new Vector2(3, 2), new Vector2(10, 12)), mc);
                DrawCircle(r.Position + new Vector2(11, 8), 1, UITheme.Gold);
                break;
            case "chest":
                DrawRect(new Rect2(r.Position + new Vector2(2, 4), new Vector2(12, 9)), mc.Darkened(0.2f));
                DrawRect(new Rect2(r.Position + new Vector2(2, 4), new Vector2(12, 3)), dark);
                break;
            case "shrine":
                DeityDef god = Fortress.God(b.Deity);
                Color gc = god != null ? UITheme.C(god.Color) : new Color(1, 0.8f, 0.4f);
                DrawRect(new Rect2(r.Position + new Vector2(3, 7), new Vector2(10, 7)), mc.Darkened(0.2f));
                DrawRect(new Rect2(r.Position + new Vector2(2, 6), new Vector2(12, 2)), mc.Lightened(0.1f));
                DrawCircle(c + new Vector2(0, -3 + MathF.Sin(Time * 7 + b.Id) * 0.6f), 2.4f, gc);
                DrawCircle(c + new Vector2(0, -3), 5, new Color(gc, 0.18f));
                break;
            case "coffin":
                DrawColoredPolygon(new[] { r.Position + new Vector2(5, 1), r.Position + new Vector2(11, 1), r.Position + new Vector2(13, 5), r.Position + new Vector2(10, 15), r.Position + new Vector2(6, 15), r.Position + new Vector2(3, 5) }, mc.Darkened(0.2f));
                if (b.Owner >= 0) DrawLine(c + new Vector2(0, -3), c + new Vector2(0, 4), new Color(0.85f, 0.8f, 0.7f), 1);
                if (b.Owner >= 0) DrawLine(c + new Vector2(-2, -1), c + new Vector2(2, -1), new Color(0.85f, 0.8f, 0.7f), 1);
                break;
            case "bridge":
                if (b.Raised)
                {
                    DrawRect(r.Grow(-1), new Color(0.08f, 0.06f, 0.05f));
                    DrawRect(new Rect2(r.Position + new Vector2(1, 1), new Vector2(14, 4)), mc.Darkened(0.3f));
                    DrawLine(r.Position + new Vector2(2, 3), r.Position + new Vector2(2, 14), new Color(0.6f, 0.6f, 0.65f), 1);
                    DrawLine(r.Position + new Vector2(14, 3), r.Position + new Vector2(14, 14), new Color(0.6f, 0.6f, 0.65f), 1);
                }
                else
                {
                    DrawRect(r, mc.Darkened(0.2f));
                    for (int k = 3; k < 16; k += 4) DrawLine(r.Position + new Vector2(0, k), r.Position + new Vector2(16, k), mc.Darkened(0.55f), 1);
                }
                break;
            case "statue":
                DrawCircle(c + new Vector2(0, 4), 5, dark);
                DrawCircle(c + new Vector2(0, -3), 3, mc.Lightened(0.2f));
                DrawRect(new Rect2(c + new Vector2(-2, -1), new Vector2(4, 6)), mc);
                break;
        }
    }

    private void DrawItems(int x0, int y0, int x1, int y1)
    {
        foreach (var kv in F.ItemsAt)
        {
            P3 p = W.Pos(kv.Key);
            if (p.Z != Z || p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) continue;
            if (F.BuildingAt.TryGetValue(kv.Key, out int bid) && F.BuildingById(bid)?.Built == true && kv.Value.All(id => F.Items[id].InBuilding >= 0)) continue;
            int n = 0;
            foreach (int id in kv.Value)
            {
                Item it = F.Items[id];
                if (n >= 4) break;
                Vector2 at = new(p.X * T + 4 + (n % 2) * 8, p.Y * T + 4 + (n / 2) * 8);
                if (kv.Value.Count == 1) at = new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T);
                DrawItem(it, at, kv.Value.Count == 1 ? 1f : 0.7f);
                n++;
            }
        }
    }

    public void DrawItem(Item it, Vector2 at, float s, CanvasItem on = null)
    {
        on ??= this;
        Color mc = UITheme.C(it.M.Color);
        Color dark = mc.Darkened(0.5f);
        if (it.Rune != null) on.DrawCircle(at, 6.5f * s, new Color(0.3f, 0.9f, 1f, 0.2f + 0.1f * MathF.Sin(Time * 4 + it.Id)));
        if (it.Artifact) { float g = 0.5f + 0.5f * MathF.Sin(Time * 3); on.DrawCircle(at, 8 * s, new Color(1f, 0.85f, 0.3f, 0.15f + 0.15f * g)); }
        switch (it.Kind)
        {
            case ItemKind.Boulder:
                on.DrawCircle(at + new Vector2(0, 1) * s, 5.5f * s, dark);
                on.DrawCircle(at, 4.5f * s, mc);
                on.DrawCircle(at - new Vector2(1.5f, 1.5f) * s, 1.5f * s, mc.Lightened(0.3f));
                break;
            case ItemKind.Log:
                on.DrawRect(new Rect2(at - new Vector2(6, 2.5f) * s, new Vector2(12, 5) * s), mc);
                on.DrawCircle(at + new Vector2(6, 0) * s, 2.5f * s, mc.Lightened(0.35f));
                break;
            case ItemKind.Drink: case ItemKind.Barrel:
                on.DrawRect(new Rect2(at - new Vector2(3.5f, 4.5f) * s, new Vector2(7, 9) * s), new Color(0.5f, 0.32f, 0.16f));
                on.DrawLine(at + new Vector2(-3.5f, -1.5f) * s, at + new Vector2(3.5f, -1.5f) * s, new Color(0.25f, 0.2f, 0.15f), 1);
                on.DrawLine(at + new Vector2(-3.5f, 2) * s, at + new Vector2(3.5f, 2) * s, new Color(0.25f, 0.2f, 0.15f), 1);
                break;
            case ItemKind.Corpse:
                on.DrawRect(new Rect2(at - new Vector2(6, 3) * s, new Vector2(12, 6) * s), new Color(0.5f, 0.42f, 0.38f));
                on.DrawCircle(at + new Vector2(-5, 0) * s, 2.5f * s, new Color(0.7f, 0.6f, 0.55f));
                break;
            default:
                char g = it.Def.Glyph;
                on.DrawString(UITheme.Mono, at + new Vector2(-3.5f, 4.5f) * s, g.ToString(), HorizontalAlignment.Left, -1, (int)(13 * s), it.Kind is ItemKind.Plant or ItemKind.Seed ? mc : mc.Lightened(0.2f));
                break;
        }
    }

    public int SelCreature = -1;

    private Vector2 CreaturePos(Creature c)
    {
        Vector2 at = CellCenter(c.Cell);
        if (F.CStepping.TryGetValue(c.Id, out var st) && c.Path != null && st.total > 0)
        {
            float t = 1f - Math.Clamp(c.MoveWait / (float)st.total, 0, 1);
            at = CellCenter(st.from).Lerp(at, t);
        }
        return at;
    }

    private void DrawCreatures(Rect2 view)
    {
        foreach (Creature c in F.Creatures)
        {
            if (c.Dead || W.Pos(c.Cell).Z != Z) continue;
            Vector2 at = CreaturePos(c);
            if (!view.HasPoint(at)) continue;
            CreatureDef d = c.D;
            Color col = UITheme.C(d.Color);
            float s = d.Size * (c.Tame && !c.Adult(F.Tick) ? 0.65f : 1f);
            bool foe = (d.Hostile || c.Angry) && !c.Tame;
            DrawCircle(at + new Vector2(0, 4 * s), 5 * s, new Color(0, 0, 0, 0.3f));
            switch (d.Home)
            {
                case Habitat.Visitor:
                    if (c.Def == "trader") { DrawCircle(at + new Vector2(0, 2) * s, 5 * s, col.Darkened(0.3f)); DrawCircle(at + new Vector2(0, 1.5f) * s, 4.2f * s, col); DrawCircle(at + new Vector2(0, -3) * s, 3.2f * s, new Color(0.92f, 0.74f, 0.6f)); DrawColoredPolygon(new[] { at + new Vector2(-3, -2.5f) * s, at + new Vector2(3, -2.5f) * s, at + new Vector2(0, 4) * s }, new Color(0.75f, 0.72f, 0.68f)); }
                    else { DrawCircle(at + new Vector2(0, 2) * s, 4.4f * s, col.Darkened(0.3f)); DrawCircle(at + new Vector2(0, 1.5f) * s, 3.8f * s, col); DrawCircle(at + new Vector2(0, -3.5f) * s, 2.8f * s, new Color(0.95f, 0.8f, 0.68f)); DrawCircle(at + new Vector2(0, -5) * s, 2.6f * s, new Color(0.35f, 0.25f, 0.15f)); }
                    // The lead merchant brings the wagon.
                    Caravan cv = F.Caravans.FirstOrDefault(x => x.Id == c.Caravan);
                    if (cv != null && cv.Merchants.Count > 0 && cv.Merchants[0] == c.Id)
                    {
                        Vector2 w = at + new Vector2(-14, 0);
                        DrawRect(new Rect2(w - new Vector2(8, 5), new Vector2(14, 10)), new Color(0.45f, 0.3f, 0.16f));
                        DrawRect(new Rect2(w - new Vector2(7, 7), new Vector2(12, 4)), cv.F.Dwarven ? new Color(0.6f, 0.5f, 0.35f) : new Color(0.85f, 0.82f, 0.72f));
                        DrawCircle(w + new Vector2(-6, 5), 2.5f, new Color(0.2f, 0.15f, 0.1f));
                        DrawCircle(w + new Vector2(4, 5), 2.5f, new Color(0.2f, 0.15f, 0.1f));
                    }
                    break;
                case Habitat.Raider:
                    DrawCircle(at + new Vector2(0, 1.5f) * s, 4.6f * s, col.Darkened(0.35f));
                    DrawCircle(at + new Vector2(0, -3) * s, 3.2f * s, col);
                    DrawCircle(at + new Vector2(-1.2f, -3.4f) * s, 0.8f * s, new Color(1, 0.85f, 0.2f));
                    DrawCircle(at + new Vector2(1.2f, -3.4f) * s, 0.8f * s, new Color(1, 0.85f, 0.2f));
                    DrawLine(at + new Vector2(4, 1) * s, at + new Vector2(7, -5) * s, new Color(0.6f, 0.55f, 0.5f), 1.5f * s);
                    break;
                case Habitat.Cavern:
                    for (int k = -1; k <= 1; k++)
                    {
                        float wig = MathF.Sin(Time * 10 + c.Id + k) * 1.2f;
                        DrawLine(at + new Vector2(-6, k * 3 + wig) * s, at + new Vector2(6, k * 3 - wig) * s, col.Darkened(0.4f), 1.2f);
                    }
                    DrawCircle(at, 4.5f * s, col);
                    DrawCircle(at + new Vector2(0, -3.5f) * s, 2.5f * s, col.Lightened(0.2f));
                    DrawCircle(at + new Vector2(-1, -4) * s, 0.7f * s, new Color(1, 0.3f, 0.3f));
                    DrawCircle(at + new Vector2(1, -4) * s, 0.7f * s, new Color(1, 0.3f, 0.3f));
                    break;
                case Habitat.Surface when c.Def == "hen":
                    DrawCircle(at + new Vector2(0, 1) * s, 4.5f * s, c.Female ? col : new Color(0.7f, 0.4f, 0.2f));
                    DrawCircle(at + new Vector2(3, -2.5f) * s, 2.6f * s, c.Female ? col : new Color(0.75f, 0.45f, 0.2f));
                    DrawRect(new Rect2(at + new Vector2(2, -5.5f) * s, new Vector2(2, 1.6f) * s), new Color(0.85f, 0.15f, 0.15f));
                    DrawColoredPolygon(new[] { at + new Vector2(5.3f, -3) * s, at + new Vector2(7.5f, -2.3f) * s, at + new Vector2(5.3f, -1.6f) * s }, new Color(0.95f, 0.7f, 0.2f));
                    break;
                default:
                    if (c.Def == "goat") { DrawLine(at + new Vector2(5, -4.5f) * s, at + new Vector2(3.5f, -8) * s, new Color(0.45f, 0.4f, 0.35f), 1.3f); DrawLine(at + new Vector2(6.5f, -4.5f) * s, at + new Vector2(7.5f, -8) * s, new Color(0.45f, 0.4f, 0.35f), 1.3f); }
                    DrawRect(new Rect2(at + new Vector2(-6, -2.5f) * s, new Vector2(11, 6) * s), col);
                    DrawCircle(at + new Vector2(5.5f, -2.5f) * s, 2.8f * s, col.Lightened(0.15f));
                    DrawLine(at + new Vector2(-5, 3.5f) * s, at + new Vector2(-5, 6) * s, col.Darkened(0.4f), 1.2f);
                    DrawLine(at + new Vector2(3, 3.5f) * s, at + new Vector2(3, 6) * s, col.Darkened(0.4f), 1.2f);
                    if (d.Timid) { DrawLine(at + new Vector2(5, -5) * s, at + new Vector2(3, -9) * s, new Color(0.8f, 0.7f, 0.5f), 1); DrawLine(at + new Vector2(6.5f, -5) * s, at + new Vector2(8.5f, -9) * s, new Color(0.8f, 0.7f, 0.5f), 1); }
                    break;
            }
            if (foe) DrawArc(at + new Vector2(0, 1), 8 * s, 0, Mathf.Tau, 20, new Color(1, 0.25f, 0.2f, 0.7f), 1);
            if (c.War) DrawArc(at + new Vector2(0, 1), 7.5f * s, 0, Mathf.Tau, 20, UITheme.Gold, 1.2f);
            if (c.MarkSlaughter) { DrawLine(at + new Vector2(-3, -12), at + new Vector2(3, -6), UITheme.Bad, 1.5f); DrawLine(at + new Vector2(3, -12), at + new Vector2(-3, -6), UITheme.Bad, 1.5f); }
            if (c.MarkTame || c.MarkTrain) DrawCircle(at + new Vector2(0, -10), 2, c.MarkTrain ? UITheme.Gold : UITheme.Good);
            if (c.Hp < d.Hp)
            {
                DrawRect(new Rect2(at + new Vector2(-6, -10 * s), new Vector2(12, 2)), new Color(0.1f, 0, 0));
                DrawRect(new Rect2(at + new Vector2(-6, -10 * s), new Vector2(12 * c.Hp / (float)d.Hp, 2)), UITheme.Bad);
            }
            if (c.Id == SelCreature) DrawArc(at + new Vector2(0, 2), 10 * s, 0, Mathf.Tau, 24, UITheme.Gold, 1.5f);
        }
    }

    private static readonly Color[] Beards = { new(0.45f, 0.28f, 0.14f), new(0.7f, 0.3f, 0.12f), new(0.75f, 0.72f, 0.68f), new(0.85f, 0.7f, 0.35f), new(0.2f, 0.15f, 0.12f) };

    public Vector2 DwarfPos(Dwarf d)
    {
        Vector2 at = CellCenter(d.Cell);
        if (F.Stepping.TryGetValue(d.Id, out var st) && d.Path != null && st.total > 0)
        {
            float t = 1f - Math.Clamp(d.MoveWait / (float)st.total, 0, 1);
            at = CellCenter(st.from).Lerp(at, t);
        }
        return at;
    }

    private void DrawDwarves(Rect2 view)
    {
        foreach (Dwarf d in F.Dwarves)
        {
            P3 p = W.Pos(d.Cell);
            if (p.Z != Z) continue;
            Vector2 at = DwarfPos(d);
            if (!view.HasPoint(at)) continue;
            if (d.Dead) continue;
            DrawDwarf(d, at, this, 1f, Time);
            if (d.Hp < Tables.MaxHp)
            {
                DrawRect(new Rect2(at + new Vector2(-6, -11), new Vector2(12, 2)), new Color(0.1f, 0, 0));
                DrawRect(new Rect2(at + new Vector2(-6, -11), new Vector2(12 * d.Hp / (float)Tables.MaxHp, 2)), d.Wounded ? UITheme.Bad : UITheme.Good);
            }
            if (d.Id == SelDwarf)
            {
                DrawArc(at + new Vector2(0, 2), 9, 0, Mathf.Tau, 24, UITheme.Gold, 1.5f);
            }
        }
    }

    /// <summary>A dwarf, top-down and a bit from the front: clan cloak, head, beard, what they carry.</summary>
    public static void DrawDwarf(Dwarf d, Vector2 at, CanvasItem on, float s, float time)
    {
        ClanDef clan = Tables.Clan(d.Clan);
        Color cloak = UITheme.C(clan.Color);
        Color beard = Beards[d.Id % Beards.Length];
        Color skin = new(0.92f, 0.74f, 0.6f);
        bool asleep = d.Task?.Kind == TaskKind.Sleep && d.Task.Stage >= 1;
        bool walking = d.Path != null;
        float bob = walking ? MathF.Abs(MathF.Sin(time * 14 + d.Id)) * 1.2f : 0;
        at -= new Vector2(0, bob) * s;
        on.DrawCircle(at + new Vector2(0, 5) * s, 4.5f * s, new Color(0, 0, 0, 0.3f));
        if (asleep)
        {
            on.DrawRect(new Rect2(at + new Vector2(-6, -2) * s, new Vector2(12, 6) * s), cloak);
            on.DrawCircle(at + new Vector2(-5, 0) * s, 3 * s, skin);
            on.DrawString(UITheme.LabelFont, at + new Vector2(3, -5) * s, "z", HorizontalAlignment.Left, -1, (int)(9 * s), new Color(0.8f, 0.85f, 1f, 0.6f + 0.4f * MathF.Sin(time * 3)));
            return;
        }
        on.DrawCircle(at + new Vector2(0, 2) * s, 5 * s, cloak.Darkened(0.25f));
        on.DrawCircle(at + new Vector2(0, 1.5f) * s, 4.2f * s, cloak);
        on.DrawCircle(at + new Vector2(0, -3) * s, 3.2f * s, skin);
        on.DrawColoredPolygon(new[] { at + new Vector2(-3, -2.5f) * s, at + new Vector2(3, -2.5f) * s, at + new Vector2(0, 4) * s }, beard);
        on.DrawCircle(at + new Vector2(0, -4.5f) * s, 2.6f * s, d.Pick >= 0 ? new Color(0.55f, 0.5f, 0.45f) : beard.Darkened(0.2f));
        if (d.Carry >= 0) on.DrawRect(new Rect2(at + new Vector2(2.5f, -1) * s, new Vector2(4, 4) * s), new Color(0.75f, 0.6f, 0.35f));
        if (d.Task?.Kind == TaskKind.Dig && d.Task.Stage == 2 || d.Task?.Kind == TaskKind.Chop && d.Task.Stage == 2)
        {
            float swing = MathF.Sin(time * 12 + d.Id) * 0.8f;
            Vector2 hand = at + new Vector2(4, 0) * s;
            on.DrawLine(hand, hand + new Vector2(MathF.Cos(swing - 1), MathF.Sin(swing - 1)) * 6 * s, new Color(0.7f, 0.6f, 0.45f), 1.2f * s);
        }
        if (d.Breaking > 0) on.DrawString(UITheme.Title, at + new Vector2(-2, -8) * s, "!", HorizontalAlignment.Left, -1, (int)(12 * s), UITheme.Bad);
        if (d.Weapon >= 0) on.DrawLine(at + new Vector2(-4, 2) * s, at + new Vector2(-7, -4) * s, new Color(0.75f, 0.75f, 0.8f), 1.5f * s);
        if (d.Armor >= 0) on.DrawArc(at + new Vector2(0, 1.5f) * s, 4.2f * s, 0.3f, Mathf.Pi - 0.3f, 8, new Color(0.7f, 0.72f, 0.78f), 1.2f * s);
        if (d.Squad >= 0) on.DrawRect(new Rect2(at + new Vector2(-1, -9) * s, new Vector2(2, 2) * s), UITheme.Gold);
    }

    private void DrawCursor()
    {
        if (Box != null)
        {
            var (a, b) = Box.Value;
            var r = new Rect2(Math.Min(a.X, b.X) * T, Math.Min(a.Y, b.Y) * T, (Math.Abs(a.X - b.X) + 1) * T, (Math.Abs(a.Y - b.Y) + 1) * T);
            DrawRect(r, BoxColor);
            DrawRect(r, new Color(BoxColor, 0.9f), false, 1.5f);
            string size = $"{Math.Abs(a.X - b.X) + 1}x{Math.Abs(a.Y - b.Y) + 1}" + (a.Z != b.Z ? $"x{Math.Abs(a.Z - b.Z) + 1}" : "");
            DrawString(UITheme.Mono, r.End + new Vector2(3, 0), size, HorizontalAlignment.Left, -1, 10, UITheme.Text);
        }
        if (Hover is P3 h && W.In(h))
        {
            if (Ghost != null)
            {
                BuildDef d = Tables.Build[Ghost];
                var r = new Rect2(h.X * T, h.Y * T, d.W * T, d.H * T);
                Color c = GhostOk ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.35f, 0.3f);
                DrawRect(r, new Color(c, 0.25f));
                DrawRect(r, c, false, 1.5f);
            }
            else DrawRect(new Rect2(h.X * T, h.Y * T, T, T), new Color(1, 1, 1, 0.7f), false, 1);
        }
    }
}
