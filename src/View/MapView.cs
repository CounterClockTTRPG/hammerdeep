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
    private Weather _sky;
    private readonly byte[] _buf = new byte[CH * A * CH * A * 4];

    // What the screen wants shown.
    public P3? Hover;
    public (P3 a, P3 b)? Box;
    public Color BoxColor = new(1, 0.85f, 0.3f, 0.35f);
    /// <summary>While a box is dragged: which cells in it will take the zone (soil for a farm, grass for a
    /// pasture). Those are marked, and counted beside the size.</summary>
    public Func<int, bool> BoxUsable;
    public string BoxUsableName = "usable";
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
        // The terrain is pixel art (crisp); the sprites drawn here are scaled (smooth).
        TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        _ground = new GroundLayer { Name = "Ground", Map = this, ShowBehindParent = true, TextureFilter = TextureFilterEnum.Nearest };
        AddChild(_ground);
        // The 3D view lives beside the map (a 3D node is not drawn by the canvas) and draws the same state.
        Three = new View3D { Name = "View3D", Map = this };
        GetParent().AddChild(Three);
    }

    // ---- the 3D option ------------------------------------------------------------------

    /// <summary>The 3D view; while it is on, the 2D map is hidden but still keeps the camera, level and cursor state.</summary>
    public View3D Three;
    public bool Is3D { get; private set; }

    public void Set3D(bool on)
    {
        Is3D = on;
        Visible = !on;
        Three?.SetOn(on);
        if (!on) Cam.MakeCurrent();
    }

    private GroundLayer _ground;

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

    // ---- the isometric option ----------------------------------------------------------

    /// <summary>
    /// Isometric: the whole map node is turned 45 degrees and squashed to half height, so
    /// everything drawn flat (terrain, zones, designations) lands in the diamond; walls get
    /// raised caps and faces, and figures are drawn upright by undoing the tilt where they stand.
    /// Clicks, panning and zooming all go through the same transform.
    /// </summary>
    public bool Iso { get; private set; }
    private static readonly Transform2D IsoTilt = new(new Vector2(0.70710678f, 0.35355339f), new Vector2(-0.70710678f, 0.35355339f), Vector2.Zero);
    private const float WallH = 9f;

    public void SetIso(bool on)
    {
        Iso = on;
        Transform = on ? IsoTilt : Transform2D.Identity;
        QueueRedraw();
        _ground?.QueueRedraw();
    }

    /// <summary>A movement on screen (pixels), as a movement of the camera in map space.</summary>
    public Vector2 ScreenDeltaToLocal(Vector2 v) => Is3D ? Three.ScreenDeltaToLocal(v) : Transform.AffineInverse().BasisXform(v / Cam.Zoom);

    /// <summary>Draw figures upright at a point: undo the tilt there. Returns where to draw (relative).</summary>
    private Vector2 Upright(Vector2 at, float lift = 6)
    {
        if (!Iso) return at;
        Transform2D inv = IsoTilt.AffineInverse();
        DrawSetTransformMatrix(new Transform2D(inv.X * 1.25f, inv.Y * 1.25f, at));
        return new Vector2(0, -lift);
    }

    /// <summary>In the isometric view, how far a block's top sits from its ground, in map space.</summary>
    private static Vector2 LiftVec => IsoTilt.AffineInverse().BasisXform(new Vector2(0, -WallH));

    /// <summary>Whether the isometric view draws this cell as a raised block (markers go on its top).</summary>
    public bool Raised(int c) => W.Shapes[c] == Shape.Wall;

    /// <summary>The offset at which a cell's markers and outline are drawn: its top, if it is a raised block.</summary>
    public Vector2 TopOf(int c) => Iso && Raised(c) ? LiftVec : Vector2.Zero;

    /// <summary>The cell actually seen at p: down through open air, as far as the map shows lower levels.</summary>
    public P3 SeenThrough(P3 p)
    {
        if (!W.In(p)) return p;
        P3 q = p;
        for (int k = 0; k < 3 && q.Z > 0; k++)
        {
            int i = W.Idx(q);
            if (W.Shapes[i] != Shape.Empty || W.Has(i, CellFlag.Hidden)) break;
            q = new P3(q.X, q.Y, q.Z - 1);
        }
        return W.Shapes[W.Idx(q)] == Shape.Empty ? p : q;
    }

    /// <summary>
    /// The cell under a screen point, on the level in view. In the isometric view the point is
    /// tested from a block's top down to the ground, so clicking a wall's raised top picks that
    /// wall and not the cell behind it on the ground.
    /// </summary>
    public P3 CellAt(Vector2 screen)
    {
        if (Is3D) return Three.CellAt(screen);
        Vector2 w = GetGlobalTransformWithCanvas().AffineInverse() * screen;
        if (Iso)
        {
            Vector2 lift = LiftVec;
            for (int k = 8; k >= 1; k--)
            {
                Vector2 q = w - lift * (k / 8f);
                int x = (int)MathF.Floor(q.X / T), y = (int)MathF.Floor(q.Y / T);
                if (W.In(x, y, Z) && World.IsSolid(W.Shapes[W.Idx(x, y, Z)])) return new P3(x, y, Z);
            }
        }
        return new P3((int)MathF.Floor(w.X / T), (int)MathF.Floor(w.Y / T), Z);
    }

    /// <summary>Where a cell is seen on screen: its centre, on a raised block's top in the isometric view.</summary>
    public Vector2 ScreenOf(P3 p) => Is3D ? Three.ScreenOf(new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T), p.Z, W.In(p) && World.IsSolid(W.Shapes[W.Idx(p)]) ? 1 : 0) : GetGlobalTransformWithCanvas() * (new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T) + (W.In(p) ? TopOf(W.Idx(p)) : Vector2.Zero));

    public Rect2 ViewRect()
    {
        Vector2 vp = GetViewportRect().Size;
        Transform2D inv = GetGlobalTransformWithCanvas().AffineInverse();
        Vector2 a = inv * Vector2.Zero, b = inv * new Vector2(vp.X, 0), c = inv * vp, d = inv * new Vector2(0, vp.Y);
        Vector2 lo = new(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
        Vector2 hi = new(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)), Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        return new Rect2(lo, hi - lo);
    }

    /// <summary>Isometric walls: a raised cap on every wall in view, and shaded faces where it meets open ground in front.</summary>
    private void DrawWallCell(int x, int y)
    {
        Vector2 up = LiftVec;
            {
                int i = W.Idx(x, y, Z);
                Shape sh = W.Shapes[i];
                if (sh == Shape.Empty && !W.Has(i, CellFlag.Hidden))
                {
                    // Open air: the far sides of the drop, so the level below sinks away.
                    Vector2 q0 = new(x * T, y * T), q1 = new((x + 1) * T, y * T), q3 = new(x * T, (y + 1) * T);
                    if (y > 0 && W.Shapes[W.Idx(x, y - 1, Z)] != Shape.Empty)
                        DrawColoredPolygon(new[] { q0, q1, q1 - up * 1.3f, q0 - up * 1.3f }, new Color(0.05f, 0.04f, 0.04f, 0.55f));
                    if (x > 0 && W.Shapes[W.Idx(x - 1, y, Z)] != Shape.Empty)
                        DrawColoredPolygon(new[] { q0, q3, q3 - up * 1.3f, q0 - up * 1.3f }, new Color(0.05f, 0.04f, 0.04f, 0.4f));
                    return;
                }
                if (sh == Shape.Ramp && !W.Has(i, CellFlag.Hidden))
                {
                    // A ramp rises towards the back: a lit slope from the front edge to half height at the back.
                    Vector2 r0 = new(x * T, y * T), r1 = new((x + 1) * T, y * T), r2 = new((x + 1) * T, (y + 1) * T), r3 = new(x * T, (y + 1) * T);
                    Color rc = UITheme.C(Tables.Mats[W.Mats[i]].Color);
                    DrawColoredPolygon(new[] { r0 + up * 0.6f, r1 + up * 0.6f, r2, r3 }, new Color(rc.Lightened(0.15f), 0.85f));
                    return;
                }
                if (sh is Shape.StairUp or Shape.StairUD && !W.Has(i, CellFlag.Hidden))
                {
                    // Three steps climbing to the back.
                    Color sc = UITheme.C(Tables.Mats[W.Mats[i]].Color);
                    for (int k = 0; k < 3; k++)
                    {
                        float y0f = y * T + T * (2 - k) / 3f, y1f = y0f + T / 3f;
                        Vector2 lift = up * (0.25f * (k + 1));
                        Vector2 a = new(x * T + 2, y0f), b = new((x + 1) * T - 2, y0f), c = new((x + 1) * T - 2, y1f), d = new(x * T + 2, y1f);
                        DrawColoredPolygon(new[] { c, d, d + lift, c + lift }, sc.Darkened(0.45f));
                        DrawColoredPolygon(new[] { a + lift, b + lift, c + lift, d + lift }, sc.Lightened(0.1f * k));
                    }
                    return;
                }
                if (sh == Shape.Tree && !W.Has(i, CellFlag.Hidden))
                {
                    // A tree stands up: trunk and crown, drawn upright.
                    Mat tm = Tables.Mats[W.Mats[i]];
                    bool stalk = tm.Key == "stalkwood";
                    Color leaf = stalk ? new Color(0.47f, 0.31f, 0.59f) : tm.Key == "pine" ? new Color(0.13f, 0.32f, 0.2f) : F.Season == 2 ? new Color(0.67f, 0.39f, 0.16f) : new Color(0.19f, 0.41f, 0.17f);
                    Vector2 o = Upright(new Vector2((x + 0.5f) * T, (y + 0.5f) * T), 0);
                    DrawRect(new Rect2(o + new Vector2(-1.5f, -9), new Vector2(3, 9)), new Color(0.35f, 0.24f, 0.14f));
                    DrawCircle(o + new Vector2(0, -14), 7.5f, leaf.Darkened(0.25f));
                    DrawCircle(o + new Vector2(-1.5f, -15.5f), 6, leaf);
                    DrawCircle(o + new Vector2(-3, -17.5f), 2.2f, leaf.Lightened(0.25f));
                    DrawSetTransformMatrix(Transform2D.Identity);
                    return;
                }
                if (sh != Shape.Wall && sh != Shape.Tree) return;
                bool hidden = W.Has(i, CellFlag.Hidden);
                Vector2 p0 = new(x * T, y * T), p1 = new((x + 1) * T, y * T), p2 = new((x + 1) * T, (y + 1) * T), p3 = new(x * T, (y + 1) * T);
                Color baseC;
                if (hidden) baseC = new Color(0.07f, 0.06f, 0.06f);
                else if (sh == Shape.Tree) baseC = UITheme.C(Tables.Mats[W.Mats[i]].Color).Darkened(0.2f).Lerp(new Color(0.2f, 0.42f, 0.18f), 0.6f);
                else baseC = UITheme.C(Tables.Mats[W.Mats[i]].Color).Darkened(0.12f);
                float n = Noise.Hash01(x, y, Z, 3) * 0.08f;
                if (!hidden)
                {
                    // Faces where the cell in front is open: south (y+1) and east (x+1).
                    if (y + 1 < W.H && !World.IsSolid(W.Shapes[W.Idx(x, y + 1, Z)]))
                        DrawColoredPolygon(new[] { p3, p2, p2 + up, p3 + up }, baseC.Darkened(0.42f));
                    if (x + 1 < W.W && !World.IsSolid(W.Shapes[W.Idx(x + 1, y, Z)]))
                        DrawColoredPolygon(new[] { p1, p2, p2 + up, p1 + up }, baseC.Darkened(0.58f));
                }
                DrawColoredPolygon(new[] { p0 + up, p1 + up, p2 + up, p3 + up }, baseC.Lightened(n));
            }
    }

    public override void _Process(double delta)
    {
        if (F == null) return;
        Time += (float)delta;
        if (F.Season != _season || F.Sky != _sky) { _season = F.Season; _sky = F.Sky; _chunks.Clear(); }
        // The world says what changed; a change at z shows on z..z+3 (seen from above through air).
        if (W.Changed.Count > 0)
        {
            Three?.Changed(W.Changed);
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
        _ground?.QueueRedraw();
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
        int dep = W.Depth[i];
        bool hot = W.IsMagma(i);
        if (dep >= World.Deep)
        {
            if (hot) PaintLiquid(i, ox, oy, new Rgb(170, 40, 10), new Rgb(255, 150, 40));
            else if (F.Frozen(i)) PaintLiquid(i, ox, oy, new Rgb(180, 205, 222), new Rgb(232, 242, 250));
            else PaintLiquid(i, ox, oy, new Rgb(34, 78, 140).Scale(0.85f + dep * 0.03f), new Rgb(70, 130, 200));
            if (bright < 0.999f) Dim(ox, oy, bright);
            return;
        }
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
        if (dep > 0) Tint(ox, oy, hot ? new Rgb(240, 110, 30) : F.Frozen(i) ? new Rgb(215, 232, 245) : new Rgb(50, 110, 190), 0.22f + dep * 0.1f);
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
        // The ancient vault: worked marble, warm and banded, so it stands out once it is found.
        bool vault = F.InRuin(x, y, z);
        if (vault) baseC = baseC.Mix(new Rgb(232, 214, 168), 0.4f);
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

    // A drought burns the grass to straw.
    private Rgb GrassColor(uint h) => F.Sky == Weather.Drought ? new Rgb(150, 132, 72) : F.Season switch
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
        if (W.Floors[i] != 0 && W.Shapes[i] == Shape.Floor) { PaintPaved(i, x, y, z, m, ox, oy, W.Floors[i]); return; }
        // Recessed ground: darker than walls; under the sky a little brighter.
        Rgb baseC = grass ? GrassColor((uint)i) : m.Color.Scale(outside ? 0.62f : 0.46f);
        if (!grass && W.Has(i, CellFlag.Wet)) baseC = baseC.Mix(new Rgb(52, 44, 34), 0.45f);
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

    /// <summary>A floor laid in a style (Fortress.FloorStyles): smoothed, flagstones, planks, tiles, mosaic.</summary>
    private void PaintPaved(int i, int x, int y, int z, Mat m, int ox, int oy, byte style)
    {
        Rgb stone = m.Kind is MatKind.Ore or MatKind.Gem or MatKind.Soil ? new Rgb(150, 142, 132) : m.Color;
        Rgb wood = new(138, 94, 56), gold = new(212, 170, 72), blue = new(54, 82, 140), cream = new(222, 210, 182);
        for (int j = 0; j < A; j++)
            for (int k = 0; k < A; k++)
            {
                float f = Noise.Hash01(k, j, i, 13);
                Rgb c;
                switch (style)
                {
                    case 1: // smoothed: even, with a faint sheen across it
                        c = stone.Scale(0.74f + ((k + j + x + y) % 7 == 0 ? 0.08f : 0f) + f * 0.03f);
                        break;
                    case 2: // flagstones: courses of 4 x 4 slabs, each its own shade, offset row by row
                    {
                        int row = (y * A + j) / 4, col = (x * A + k + (row % 2) * 2) / 4;
                        bool mortar = (y * A + j) % 4 == 3 || (x * A + k + (row % 2) * 2) % 4 == 3;
                        c = mortar ? stone.Scale(0.36f) : stone.Scale(0.6f + Noise.Hash01(col, row, z, 14) * 0.18f);
                        break;
                    }
                    case 3: // planks: boards running east-west, a seam every third row, butt joints staggered
                    {
                        int gy = y * A + j, plank = gy / 3;
                        bool seam = gy % 3 == 2 || (x * A + k + plank * 5) % 13 == 0;
                        c = seam ? wood.Scale(0.45f) : wood.Scale(0.82f + Noise.Hash01(x * A + k, plank, z, 15) * 0.1f + (f > 0.9f ? -0.12f : 0f));
                        break;
                    }
                    case 4: // tiles: a chequer of light and dark, two tiles to a cell
                        c = ((x * A + k) / 4 + (y * A + j) / 4) % 2 == 0 ? stone.Mix(cream, 0.35f).Scale(0.85f) : stone.Scale(0.42f);
                        if (k % 4 == 0 || j % 4 == 0) c = c.Scale(0.85f);
                        break;
                    default: // mosaic: a gold diamond in a ring of blue on pale stone, in 2 x 2 stones
                    {
                        float dx = System.Math.Abs(k / 2 - 1.5f), dy = System.Math.Abs(j / 2 - 1.5f), d = dx + dy;
                        c = d < 1.1f ? gold : d < 2.1f ? blue : stone.Mix(cream, 0.5f).Scale(0.8f);
                        c = c.Scale(0.88f + f * 0.16f);
                        if ((k % 2 == 1 && f > 0.7f) || (j % 2 == 1 && f < 0.3f)) c = c.Scale(0.72f);
                        break;
                    }
                }
                Px(ox + k, oy + j, c);
            }
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

        int x0 = Math.Max(0, (int)(view.Position.X / T)), y0 = Math.Max(0, (int)(view.Position.Y / T));
        int x1 = Math.Min(W.W - 1, (int)(view.End.X / T)), y1 = Math.Min(W.H - 1, (int)(view.End.Y / T));
        DrawZones(x0, y0, x1, y1);
        if (Iso) DrawIsoScene(x0, y0, x1, y1, view);
        else
        {
            DrawDesignations(x0, y0, x1, y1);
            DrawBuildings(view);
            DrawItems(x0, y0, x1, y1);
            DrawDwarves(view);
            DrawCreatures(view);
        }
        DrawSetTransformMatrix(Transform2D.Identity);
        DrawCursor();
    }

    /// <summary>The terrain, on the ground layer behind this node.</summary>
    public void DrawGround(CanvasItem on)
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
                on.DrawTextureRect(Chunk(cx, cy), new Rect2(cx * CH * T, cy * CH * T, CH * T, CH * T), false);
            }
        // Map edge.
        on.DrawRect(new Rect2(0, 0, W.W * T, W.H * T), new Color(0.5f, 0.36f, 0.19f, 0.6f), false, 2);
    }

    /// <summary>
    /// The isometric scene, back to front: diagonal by diagonal (x + y grows towards the
    /// viewer), the raised walls and their markers, then whatever stands on that diagonal --
    /// so a wall in front hides a dwarf behind it, and a dwarf in front stands over the wall.
    /// </summary>
    private void DrawIsoScene(int x0, int y0, int x1, int y1, Rect2 view)
    {
        var things = new List<(float depth, Action draw)>();
        foreach (Building b in F.Buildings)
        {
            if (b.Z != Z) continue;
            var r = new Rect2(b.X * T, b.Y * T, b.W * T, b.H * T);
            if (view.Intersects(r)) things.Add((b.X + b.W - 1 + b.Y + b.H - 1 - 0.5f, () => DrawBuilding(b, r)));
        }
        foreach (var kv in F.ItemsAt)
        {
            P3 p = W.Pos(kv.Key);
            if (p.Z == Z && p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1) things.Add((p.X + p.Y - 0.25f, () => DrawItemStack(kv.Key, kv.Value)));
        }
        foreach (Dwarf d in F.Dwarves)
            if (DwarfShown(d, view)) { Vector2 at = DwarfPos(d); things.Add(((at.X + at.Y) / T - 1, () => DrawOneDwarf(d))); }
        foreach (Creature c in F.Creatures)
            if (CreatureShown(c, view)) { Vector2 at = CreaturePos(c); things.Add(((at.X + at.Y) / T - 1, () => DrawOneCreature(c))); }
        things.Sort((a, b) => a.depth.CompareTo(b.depth));
        var marks = new Dictionary<int, List<int>>();
        foreach (int c in F.Designated)
        {
            P3 p = W.Pos(c);
            if (p.Z != Z || p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) continue;
            if (!marks.TryGetValue(p.X + p.Y, out var l)) marks[p.X + p.Y] = l = new List<int>();
            l.Add(c);
        }
        int next = 0;
        for (int s = x0 + y0; s <= x1 + y1; s++)
        {
            for (int x = Math.Max(x0, s - y1); x <= Math.Min(x1, s - y0); x++) DrawWallCell(x, s - x);
            if (marks.TryGetValue(s, out var ms)) foreach (int c in ms) DrawDesignation(c);
            while (next < things.Count && things[next].depth < s + 1)
            {
                things[next++].draw();
                DrawSetTransformMatrix(Transform2D.Identity);
            }
        }
        while (next < things.Count) { things[next++].draw(); DrawSetTransformMatrix(Transform2D.Identity); }
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
            // Strong enough to find on grass, squashed in the isometric view; faint enough to see the ground.
            DrawRect(r, new Color(c, sel ? 0.2f : 0.12f));
            DrawDashed(r, new Color(c, sel ? 0.95f : 0.75f));
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
            DrawDesignation(c);
        }
    }

    private void DrawDesignation(int c)
    {
        P3 p = W.Pos(c);
        {
            Desig g = W.Desigs[c];
            // In the isometric view a designated wall is marked on its raised top.
            Vector2 lift = TopOf(c);
            var r = new Rect2(p.X * T + lift.X, p.Y * T + lift.Y, T, T);
            bool taken = F.Claims.ContainsKey(c);
            Color col = g switch
            {
                Desig.Dig => new Color(1f, 0.84f, 0.3f),
                Desig.Channel => new Color(0.85f, 0.5f, 1f),
                Desig.StairUD or Desig.StairDown or Desig.StairUp => new Color(0.4f, 0.9f, 1f),
                Desig.Chop => new Color(0.6f, 1f, 0.4f),
                Desig.Gather => new Color(0.6f, 1f, 0.8f),
                Desig.Pave => new Color(0.95f, 0.75f, 0.55f),
                _ => new Color(1, 1, 1),
            };
            DrawRect(r, new Color(col, taken ? 0.32f : 0.18f));
            for (int k = 4; k < T * 2; k += 5)
            {
                Vector2 a = new(p.X * T + Math.Max(0, k - T), p.Y * T + Math.Min(T, k)), b = new(p.X * T + Math.Min(T, k), p.Y * T + Math.Max(0, k - T));
                a += lift; b += lift;
                DrawLine(a, b, new Color(col, 0.55f), 1);
            }
            DrawRect(r.Grow(-0.5f), new Color(col, 0.8f), false, 1);
            string glyph = g switch { Desig.StairUD => "X", Desig.StairDown => ">", Desig.StairUp => "<", Desig.Channel => "v", _ => null };
            if (glyph != null) DrawString(UITheme.Mono, new Vector2(p.X * T + 4, p.Y * T + 12) + lift, glyph, HorizontalAlignment.Left, -1, 11, col);
        }
    }

    private void DrawBuildings(Rect2 view)
    {
        foreach (Building b in F.Buildings)
        {
            if (b.Def == "lift" ? Z < b.Z || Z > b.Top : b.Z != Z) continue;
            var r = new Rect2(b.X * T, b.Y * T, b.W * T, b.H * T);
            if (!view.Intersects(r)) continue;
            DrawBuilding(b, r);
        }
    }

    private void DrawBuilding(Building b, Rect2 r)
    {
        {
            BuildDef d = b.D;
            Color mc = b.Built ? UITheme.C(Tables.Mats[b.Mat].Color) : new Color(0.7f, 0.7f, 0.7f);
            bool sel = SelBuilding == b.Id;
            if (!b.Built)
            {
                DrawRect(r.Grow(-1), new Color(0.6f, 0.7f, 0.9f, 0.12f));
                DrawDashed(r.Grow(-1), new Color(0.75f, 0.85f, 1f, 0.8f));
                DrawString(UITheme.LabelFont, r.Position + new Vector2(r.Size.X / 2 - 4, r.Size.Y / 2 + 5), d.Glyph.ToString(), HorizontalAlignment.Left, -1, d.W > 1 ? 18 : 12, new Color(0.8f, 0.9f, 1f, 0.8f));
                if (sel) DrawRect(r, UITheme.Gold, false, 1.5f);
                return;
            }
            switch (d.Kind)
            {
                case BuildKind.Workshop: if (!DrawShopSprite(b, r)) DrawWorkshop(b, r, mc); break;
                case BuildKind.Monument: DrawWorkshop(b, r, mc); DrawRect(r.Grow(-1), new Color(UITheme.Gold, 0.55f), false, 1.5f); break;
                default: if (!DrawFurnitureSprite(b, r)) DrawFurniture(b, r, mc); break;
            }
            if (b.Artifact) DrawRect(r.Grow(0.5f), new Color(1f, 0.85f, 0.3f, 0.5f + 0.3f * MathF.Sin(Time * 3)), false, 1);
            if (b.MoodClaim >= 0) DrawRect(r.Grow(1), new Color(0.9f, 0.6f, 1f, 0.6f + 0.3f * MathF.Sin(Time * 5)), false, 2);
            if (sel) DrawRect(r.Grow(1), UITheme.Gold, false, 1.5f);
        }
    }

    /// <summary>A workshop's painted sprite: from above in the top-down view, standing on its diamond in the isometric one.</summary>
    private bool DrawShopSprite(Building b, Rect2 r)
    {
        Texture2D t = Assets.Sprite($"shop-{b.Def}-{(Iso ? "iso" : "top")}");
        if (t == null) return false;
        if (Iso)
        {
            // In the tilted frame the footprint is a diamond: the picture spans its width and stands on its front corner.
            Vector2 a = IsoTilt * r.Position, bq = IsoTilt * new Vector2(r.End.X, r.Position.Y), c = IsoTilt * r.End, d = IsoTilt * new Vector2(r.Position.X, r.End.Y);
            float minX = Math.Min(Math.Min(a.X, bq.X), Math.Min(c.X, d.X)), maxX = Math.Max(Math.Max(a.X, bq.X), Math.Max(c.X, d.X));
            float bottom = Math.Max(Math.Max(a.Y, bq.Y), Math.Max(c.Y, d.Y));
            float w = (maxX - minX) * 1.04f, h = w * t.GetHeight() / t.GetWidth();
            DrawSetTransformMatrix(IsoTilt.AffineInverse());
            DrawTextureRect(t, new Rect2((minX + maxX) / 2 - w / 2, bottom - h + w * 0.02f, w, h), false);
            float top = Math.Min(Math.Min(a.Y, bq.Y), Math.Min(c.Y, d.Y));
            if (b.Orders.Any(o => o.Worker >= 0)) DrawCircle(new Vector2((minX + maxX) / 2, (top + bottom) / 2 - 18 + Mathf.Sin(Time * 8) * 1.5f), 2.5f, UITheme.Ember);
            DrawSetTransformMatrix(Transform2D.Identity);
            return true;
        }
        DrawTextureRect(t, r, false);
        Vector2 cc = r.GetCenter();
        if (b.Orders.Any(o => o.Worker >= 0)) DrawCircle(cc + new Vector2(14, -14 + Mathf.Sin(Time * 8) * 1.5f), 2, UITheme.Ember);
        if (b.Orders.Count > 0)
        {
            DrawRect(new Rect2(r.Position + new Vector2(2, 2), new Vector2(10, 11)), new Color(0, 0, 0, 0.55f));
            DrawString(UITheme.Mono, r.Position + new Vector2(4, 11), b.Orders.Count.ToString(), HorizontalAlignment.Left, -1, 10, UITheme.Text);
        }
        return true;
    }

    /// <summary>A piece of furniture's sprite, standing upright on its cell in both views.</summary>
    private bool DrawFurnitureSprite(Building b, Rect2 r)
    {
        Texture2D t = Assets.Sprite("furn-" + b.Def);
        if (t == null) return false;
        Vector2 o = Upright(r.GetCenter(), 0);
        float k = 17f / Math.Max(t.GetWidth(), t.GetHeight());
        Vector2 size = new(t.GetWidth() * k, t.GetHeight() * k);
        DrawTextureRect(t, new Rect2(o + new Vector2(-size.X / 2, 6 - size.Y), size), false);
        if (b.Def == "bed" && b.Owner >= 0) DrawCircle(o + new Vector2(size.X / 2 - 2, 4 - size.Y), 1.8f, new Color(0.85f, 0.3f, 0.3f));
        DrawSetTransformMatrix(Transform2D.Identity);
        return true;
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
            case "smelter": case "forge": case "furnace": case "kitchen": case "msmelter": case "mforge":
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
            case "well":
                DrawCircle(c, 6.5f, mc.Darkened(0.2f));
                DrawCircle(c, 4.5f, new Color(0.15f, 0.3f, 0.55f));
                DrawLine(c + new Vector2(-6, -7), c + new Vector2(6, -7), new Color(0.45f, 0.3f, 0.16f), 2);
                break;
            case "floodgate":
                DrawRect(r.Grow(-1), b.Locked ? mc.Darkened(0.3f) : new Color(0, 0, 0, 0.2f));
                for (int k = 3; k < 16; k += 4) DrawLine(r.Position + new Vector2(k, 1), r.Position + new Vector2(k, 15), b.Locked ? new Color(0.65f, 0.65f, 0.7f) : new Color(0.65f, 0.65f, 0.7f, 0.35f), 1.5f);
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
            case "lever":
            {
                Color iron = new(0.55f, 0.55f, 0.6f);
                DrawRect(new Rect2(c + new Vector2(-5, 3), new Vector2(10, 4)), dark);
                Vector2 tip = c + (b.On ? new Vector2(5, -5) : new Vector2(-5, -5));
                DrawLine(c + new Vector2(0, 4), tip, iron, 2, true);
                DrawCircle(tip, 2.2f, b.On ? UITheme.Gold : new Color(0.75f, 0.3f, 0.2f));
                break;
            }
            case "plate":
                DrawRect(r.Grow(-3), b.On ? new Color(0.95f, 0.75f, 0.3f, 0.9f) : mc.Darkened(0.25f));
                DrawRect(r.Grow(-3), dark, false, 1);
                break;
            case "hatch":
                if (b.Raised) { DrawRect(r.Grow(-1), new Color(0.05f, 0.04f, 0.04f)); DrawRect(r.Grow(-1), new Color(0.5f, 0.2f, 0.15f), false, 1); }
                else
                {
                    DrawRect(r.Grow(-1), mc.Darkened(0.15f));
                    DrawLine(r.Position + new Vector2(8, 1), r.Position + new Vector2(8, 15), mc.Darkened(0.55f), 1);
                    DrawRect(r.Grow(-1), new Color(0.3f, 0.3f, 0.32f), false, 1);
                }
                break;
            case "cage":
            {
                Color bars = new(0.42f, 0.4f, 0.4f);
                if (b.Caught != null)
                {
                    DrawCircle(c, 4.5f, UITheme.C(Tables.Creatures[b.Caught].Color));
                    for (int k = 2; k < 16; k += 3) DrawLine(r.Position + new Vector2(k, 1), r.Position + new Vector2(k, 15), bars, 1);
                    DrawRect(r.Grow(-1), bars, false, 1.5f);
                }
                else { DrawRect(r.Grow(-2), bars, false, 1); DrawRect(new Rect2(c - new Vector2(2, 2), new Vector2(4, 4)), bars); }
                break;
            }
            case "launcher":
            {
                Vector2 dir = b.Facing switch { 0 => Vector2.Up, 1 => Vector2.Right, 2 => Vector2.Down, _ => Vector2.Left };
                DrawRect(new Rect2(c - new Vector2(5, 5), new Vector2(10, 10)), mc.Darkened(0.3f));
                DrawLine(c, c + dir * 8, new Color(0.6f, 0.6f, 0.65f), 3);
                DrawLine(c - dir.Orthogonal() * 6, c + dir.Orthogonal() * 6, new Color(0.4f, 0.3f, 0.2f), 2);
                break;
            }
            case "lift":
            {
                Color post = new(0.42f, 0.28f, 0.16f), chain = b.Raised ? new Color(0.6f, 0.25f, 0.2f) : new Color(0.55f, 0.55f, 0.6f);
                if (Z == b.Z) DrawRect(r.Grow(-2), new Color(0.45f, 0.45f, 0.48f));
                else DrawRect(r.Grow(-1), new Color(0.05f, 0.04f, 0.04f, 0.6f));
                foreach (Vector2 o in new[] { new Vector2(1, 1), new Vector2(13, 1), new Vector2(1, 13), new Vector2(13, 13) }) DrawRect(new Rect2(r.Position + o, new Vector2(2, 2)), post);
                DrawLine(c - new Vector2(0, 6), c + new Vector2(0, 6), chain, 1.5f);
                break;
            }
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
            DrawItemStack(kv.Key, kv.Value);
        }
    }

    private void DrawItemStack(int cell, List<int> ids)
    {
        P3 p = W.Pos(cell);
        if (F.BuildingAt.TryGetValue(cell, out int bid) && F.BuildingById(bid)?.Built == true && ids.All(id => F.Items[id].InBuilding >= 0)) return;
        {
            int n = 0;
            foreach (int id in ids)
            {
                Item it = F.Items[id];
                if (n >= 4) break;
                Vector2 at = new(p.X * T + 4 + (n % 2) * 8, p.Y * T + 4 + (n / 2) * 8);
                if (ids.Count == 1) at = new Vector2((p.X + 0.5f) * T, (p.Y + 0.5f) * T);
                DrawItem(it, Upright(at, 2), ids.Count == 1 ? 1f : 0.7f);
                DrawSetTransformMatrix(Transform2D.Identity);
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

    public Vector2 CreaturePos(Creature c)
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
            if (CreatureShown(c, view)) { DrawOneCreature(c); DrawSetTransformMatrix(Transform2D.Identity); }
    }

    private bool CreatureShown(Creature c, Rect2 view) => !c.Dead && W.Pos(c.Cell).Z == Z && view.HasPoint(CreaturePos(c));

    private void DrawOneCreature(Creature c)
    {
        {
            Vector2 at = Upright(CreaturePos(c));
            CreatureDef d = c.D;
            Color col = UITheme.C(d.Color);
            float s = d.Size * (c.Tame && !c.Adult(F.Tick) ? 0.65f : 1f);
            bool foe = (d.Hostile || c.Angry) && !c.Tame;
            DrawCircle(at + new Vector2(0, 4 * s), 5 * s, new Color(0, 0, 0, 0.3f));
            Texture2D spr = Assets.Sprite("cr-" + c.Def);
            if (spr != null)
            {
                float k = 22f * s / Math.Max(spr.GetWidth(), spr.GetHeight());
                Vector2 sz = new(spr.GetWidth() * k, spr.GetHeight() * k);
                DrawTextureRect(spr, new Rect2(at + new Vector2(-sz.X / 2, 6 * s - sz.Y), sz), false);
            }
            else switch (d.Home)
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

    /// <summary>Where a dwarf's head is on the screen (for speech bubbles), in either view.</summary>
    public Vector2 HeadOnScreen(Dwarf d)
    {
        Vector2 at = DwarfPos(d);
        if (Is3D) return Three.ScreenOf(at, W.Pos(d.Cell).Z, 1.45f);
        return GetGlobalTransformWithCanvas() * at - new Vector2(0, 16 * Cam.Zoom.Y);
    }

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
            if (DwarfShown(d, view)) { DrawOneDwarf(d); DrawSetTransformMatrix(Transform2D.Identity); }
    }

    private bool DwarfShown(Dwarf d, Rect2 view) => !d.Dead && W.Pos(d.Cell).Z == Z && view.HasPoint(DwarfPos(d));

    private void DrawOneDwarf(Dwarf d)
    {
        {
            Vector2 at = Upright(DwarfPos(d));
            DrawDwarf(d, at, this, 1f, Time);
            if (d.Hp < d.MaxHp)
            {
                DrawRect(new Rect2(at + new Vector2(-6, -11), new Vector2(12, 2)), new Color(0.1f, 0, 0));
                DrawRect(new Rect2(at + new Vector2(-6, -11), new Vector2(12 * d.Hp / (float)d.MaxHp, 2)), d.Wounded ? UITheme.Bad : UITheme.Good);
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
        RaceDef race = Races.Of(d);
        s *= race.Size;
        Color cloak = UITheme.C(clan.Color);
        Color beard = race.Key == "dwarf" ? Beards[d.Id % Beards.Length] : UITheme.C(race.Hair);
        Color skin = UITheme.C(race.Skin);
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
        Texture2D spr = Assets.Sprite("fig-" + d.Clan);
        if (spr != null)
        {
            // The clan's painted figure, feet on the shadow; what the dwarf is doing still shows over it.
            float k = 22f * s / spr.GetHeight();
            Vector2 sz = new(spr.GetWidth() * k, spr.GetHeight() * k);
            on.DrawTextureRect(spr, new Rect2(at + new Vector2(-sz.X / 2, 6.5f * s - sz.Y), sz), false);
            if (d.Carry >= 0) on.DrawRect(new Rect2(at + new Vector2(3.5f, 0) * s, new Vector2(4, 4) * s), new Color(0.75f, 0.6f, 0.35f));
            if (d.Breaking > 0) on.DrawString(UITheme.Title, at + new Vector2(-2, -16) * s, "!", HorizontalAlignment.Left, -1, (int)(12 * s), UITheme.Bad);
            if (d.Squad >= 0) on.DrawRect(new Rect2(at + new Vector2(-1.5f, -18) * s, new Vector2(3, 3) * s), UITheme.Gold);
            return;
        }
        on.DrawCircle(at + new Vector2(0, 2) * s, 5 * s, cloak.Darkened(0.25f));
        on.DrawCircle(at + new Vector2(0, 1.5f) * s, 4.2f * s, cloak);
        on.DrawCircle(at + new Vector2(0, -3) * s, 3.2f * s, skin);
        switch (race.Key)
        {
            case "delvling":
                // A snout and a tail.
                on.DrawCircle(at + new Vector2(0, -1.2f) * s, 1.6f * s, skin.Darkened(0.15f));
                on.DrawLine(at + new Vector2(3, 4) * s, at + new Vector2(7, 6 + MathF.Sin(time * 6 + d.Id) * 1.5f) * s, skin.Darkened(0.3f), 1.4f * s);
                on.DrawCircle(at + new Vector2(-1.2f, -3.6f) * s, 0.7f * s, new Color(1f, 0.85f, 0.2f));
                on.DrawCircle(at + new Vector2(1.2f, -3.6f) * s, 0.7f * s, new Color(1f, 0.85f, 0.2f));
                break;
            case "rootfolk":
                // A broad leafy cap.
                on.DrawColoredPolygon(new[] { at + new Vector2(-5, -4) * s, at + new Vector2(5, -4) * s, at + new Vector2(0, -9) * s }, beard);
                on.DrawCircle(at + new Vector2(0, -8.6f) * s, 1.2f * s, new Color(0.85f, 0.3f, 0.35f));
                break;
            case "stonekin":
                // A heavy brow, no beard: stone does not grow hair.
                on.DrawRect(new Rect2(at + new Vector2(-3, -5) * s, new Vector2(6, 1.6f) * s), skin.Darkened(0.35f));
                break;
            default:
                on.DrawColoredPolygon(new[] { at + new Vector2(-3, -2.5f) * s, at + new Vector2(3, -2.5f) * s, at + new Vector2(0, 4) * s }, beard);
                on.DrawCircle(at + new Vector2(0, -4.5f) * s, 2.6f * s, d.Pick >= 0 ? new Color(0.55f, 0.5f, 0.45f) : beard.Darkened(0.2f));
                break;
        }
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
            // A box started on a block's top is drawn on the tops, where the markers will appear.
            if (W.In(a)) r.Position += TopOf(W.Idx(a));
            DrawRect(r, BoxColor);
            DrawRect(r, new Color(BoxColor, 0.9f), false, 1.5f);
            string size = $"{Math.Abs(a.X - b.X) + 1}x{Math.Abs(a.Y - b.Y) + 1}" + (a.Z != b.Z ? $"x{Math.Abs(a.Z - b.Z) + 1}" : "");
            if (BoxUsable != null && W.In(a))
            {
                int good = 0;
                Vector2 lift = TopOf(W.Idx(a));
                for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                    for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                    {
                        if (!W.In(x, y, a.Z) || !BoxUsable(W.Idx(x, y, a.Z))) continue;
                        good++;
                        DrawRect(new Rect2(new Vector2(x * T + T * 0.3f, y * T + T * 0.3f) + lift, new Vector2(T * 0.4f, T * 0.4f)), new Color(0.45f, 0.9f, 0.4f, 0.75f));
                    }
                size += $"  {good} {BoxUsableName}";
            }
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
            else DrawRect(new Rect2(new Vector2(h.X * T, h.Y * T) + TopOf(W.Idx(h)), new Vector2(T, T)), new Color(1, 1, 1, 0.7f), false, 1);
        }
    }
}
