using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The 3D view: the same fortress, the same level in view, drawn in 3D. A cell is a metre;
/// map x is X, map y is Z and a level is a metre of Y (a cell at level z spans Y z..z+1).
/// Everything above the level in view is cut away, and open air shows up to three levels
/// below, dimmer the deeper. <see cref="MapView"/> stays the controller -- camera centre,
/// zoom, level, hover, drag box, ghost, selection -- and this only draws them and answers
/// "which cell is under the mouse" with a ray. Terrain is meshed per 16x16 chunk from vertex
/// colours (rebuilt from <see cref="World.Changed"/>); buildings are the generated models in
/// assets/models when there are some, blocks otherwise; figures are their painted sprites,
/// standing up and turned to the camera.
/// </summary>
public partial class View3D : Node3D
{
    public MapView Map;
    private Fortress F => Map.F;
    private World W => F.World;

    private const int CS = 16, Below = 3;
    private Camera3D _cam;
    private DirectionalLight3D _sun;
    private WorldEnvironment _env;
    private Node3D _terrain, _things;
    private MeshInstance3D _overlay;
    private ImmediateMesh _im;
    private StandardMaterial3D _ground, _water, _lines, _ghost;
    private readonly Dictionary<int, MeshInstance3D> _chunks = new();
    private readonly HashSet<int> _dirty = new();
    private readonly Dictionary<int, Node3D> _buildings = new();
    private readonly Dictionary<int, Node3D> _figures = new();
    private MultiMeshInstance3D _items;
    private int _z = -1, _season = -1;
    private bool _painted;
    private double _itemClock;

    /// <summary>The camera's heading round the view centre, in degrees; [ and ] or a right-drag turn it.</summary>
    public float Yaw = 45;
    /// <summary>How far the camera looks down, in degrees (a right-drag up and down tilts it).</summary>
    public float Pitch = 52;
    public const float PitchMin = 22, PitchMax = 86;

    /// <summary>A right-drag by <paramref name="rel"/> pixels: sideways turns, up and down tilts.</summary>
    public void Orbit(Vector2 rel)
    {
        Yaw = Mathf.PosMod(Yaw - rel.X * 0.35f, 360);
        Pitch = Mathf.Clamp(Pitch + rel.Y * 0.25f, PitchMin, PitchMax);
    }

    public override void _Ready()
    {
        _cam = new Camera3D { Fov = 38, Near = 0.3f, Far = 400 };
        AddChild(_cam);
        _sun = new DirectionalLight3D { RotationDegrees = new Vector3(-58, 32, 0), LightEnergy = 1.0f, ShadowEnabled = true, LightColor = new Color(1, 0.95f, 0.86f) };
        _sun.DirectionalShadowMaxDistance = 90;
        AddChild(_sun);
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.03f, 0.03f, 0.045f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.6f, 0.66f),
            AmbientLightEnergy = 0.45f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
        };
        _env = new WorldEnvironment { Environment = env };
        AddChild(_env);
        _terrain = new Node3D { Name = "Terrain" };
        _things = new Node3D { Name = "Things" };
        AddChild(_terrain);
        AddChild(_things);
        _ground = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f };
        _water = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.2f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _lines = new StandardMaterial3D { VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = false };
        _ghost = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.85f, 1f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _im = new ImmediateMesh();
        _overlay = new MeshInstance3D { Mesh = _im, MaterialOverride = _lines, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_overlay);
        _items = new MultiMeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _items.Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.16f, 0.22f) } };
        _items.MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.8f };
        AddChild(_items);
        Visible = false;
    }

    /// <summary>Show or hide the 3D view; while shown its camera is the one in use.</summary>
    public void SetOn(bool on)
    {
        Visible = on;
        if (on) { _cam.MakeCurrent(); _z = -1; }
        else _cam.ClearCurrent(false);
    }

    /// <summary>What changed in the world, for the chunks that show it (called before the map clears the list).</summary>
    public void Changed(IEnumerable<int> cells)
    {
        if (!Visible) return;
        foreach (int c in cells)
        {
            P3 p = W.Pos(c);
            if (p.Z > _z || p.Z < _z - Below - 1) continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = p.X + dx, y = p.Y + dy;
                    if (W.In(x, y, 0)) _dirty.Add(Key(x / CS, y / CS));
                }
        }
    }

    private static int Key(int cx, int cy) => cy * 1024 + cx;

    // ---- the camera ---------------------------------------------------------------------

    /// <summary>Where the camera looks: the map camera's centre, on the level in view.</summary>
    private Vector3 Target => new(Map.Cam.Position.X / MapView.T, Map.Z + 1, Map.Cam.Position.Y / MapView.T);

    private float Distance => 46f / Map.Cam.Zoom.X;

    private void PlaceCamera()
    {
        float yaw = Mathf.DegToRad(Yaw), pitch = Mathf.DegToRad(Pitch);
        Vector3 back = new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        _cam.Position = Target + back * Distance;
        _cam.LookAt(Target, Vector3.Up);
    }

    /// <summary>A movement on screen (pixels) as a movement of the camera centre in map pixels.</summary>
    public Vector2 ScreenDeltaToLocal(Vector2 v)
    {
        // A screen pixel at the target is about this many metres.
        float h = GetViewport().GetVisibleRect().Size.Y;
        float m = 2 * Distance * Mathf.Tan(Mathf.DegToRad(_cam.Fov / 2)) / h;
        float yaw = Mathf.DegToRad(Yaw);
        Vector2 right = new(Mathf.Cos(yaw), -Mathf.Sin(yaw)), fwd = new(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        Vector2 worldM = right * v.X * m - fwd * v.Y * m / Mathf.Sin(Mathf.DegToRad(Pitch));
        return worldM * MapView.T;
    }

    /// <summary>The map direction for the arrow keys, turned with the camera.</summary>
    public Vector2 PanDir(Vector2 v)
    {
        float yaw = Mathf.DegToRad(Yaw);
        Vector2 right = new(Mathf.Cos(yaw), -Mathf.Sin(yaw)), fwd = new(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        return right * v.X - fwd * v.Y;
    }

    /// <summary>The cell under a screen point: a wall's top on the level in view, else the floor there.</summary>
    public P3 CellAt(Vector2 screen)
    {
        PlaceCamera();
        Vector3 o = _cam.ProjectRayOrigin(screen), d = _cam.ProjectRayNormal(screen);
        if (Mathf.Abs(d.Y) < 1e-4f) return new P3(-1, -1, Map.Z);
        int z = Map.Z;
        // The top of a wall block on this level...
        Vector3 top = o + d * ((z + 1 - o.Y) / d.Y);
        int tx = (int)MathF.Floor(top.X), ty = (int)MathF.Floor(top.Z);
        if (W.In(tx, ty, z) && World.IsSolid(W.Shapes[W.Idx(tx, ty, z)])) return new P3(tx, ty, z);
        // ...else the floor, walking the ray down through the cell so a wall's face is hit too.
        for (int k = 1; k <= 8; k++)
        {
            float y = z + 1 - k / 8f;
            Vector3 q = o + d * ((y - o.Y) / d.Y);
            int x = (int)MathF.Floor(q.X), yy = (int)MathF.Floor(q.Z);
            if (W.In(x, yy, z) && World.IsSolid(W.Shapes[W.Idx(x, yy, z)])) return new P3(x, yy, z);
        }
        Vector3 floor = o + d * ((z - o.Y) / d.Y);
        return new P3((int)MathF.Floor(floor.X), (int)MathF.Floor(floor.Z), z);
    }

    /// <summary>Where a map point (map pixels) at a level is on screen.</summary>
    public Vector2 ScreenOf(Vector2 mapPx, int z, float lift = 0) => _cam.UnprojectPosition(new Vector3(mapPx.X / MapView.T, z + lift, mapPx.Y / MapView.T));

    public bool Behind(Vector2 mapPx, int z) => _cam.IsPositionBehind(new Vector3(mapPx.X / MapView.T, z, mapPx.Y / MapView.T));

    // ---- each frame ---------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (!Visible || F == null) return;
        PlaceCamera();
        _dt = (float)delta;
        bool painted = Profile.Current?.PaintedArt ?? true;
        if (Map.Z != _z || F.Season != _season || painted != _painted)
        {
            _z = Map.Z; _season = F.Season; _painted = painted;
            foreach (var m in _chunks.Values) m.QueueFree();
            _chunks.Clear();
            foreach (var n in _buildings.Values) n.QueueFree();
            _buildings.Clear();
            foreach (var n in _figures.Values) n.QueueFree();
            _figures.Clear();
        }
        // Chunks near the camera first, a few a frame, so a level change never stalls.
        int ccx = (int)(Target.X / CS), ccy = (int)(Target.Z / CS), reach = Math.Clamp((int)(Distance * 1.6f / CS) + 1, 2, 9);
        int built = 0;
        foreach (int k in _dirty) if (_chunks.Remove(k, out var old)) old.QueueFree();
        _dirty.Clear();
        for (int r = 0; r <= reach && built < 6; r++)
            for (int cy = ccy - r; cy <= ccy + r && built < 6; cy++)
                for (int cx = ccx - r; cx <= ccx + r && built < 6; cx++)
                {
                    if (Math.Max(Math.Abs(cx - ccx), Math.Abs(cy - ccy)) != r) continue;
                    if (cx < 0 || cy < 0 || cx * CS >= W.W || cy * CS >= W.H || _chunks.ContainsKey(Key(cx, cy))) continue;
                    _chunks[Key(cx, cy)] = BuildChunk(cx, cy);
                    built++;
                }
        SyncBuildings();
        SyncFigures();
        _itemClock -= delta;
        if (_itemClock <= 0) { _itemClock = 0.4; SyncItems(); }
        DrawOverlay();
    }

    // ---- terrain ------------------------------------------------------------------------

    private sealed class Geo
    {
        public readonly List<Vector3> V = new(), N = new();
        public readonly List<Color> C = new();
        public readonly List<int> I = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color col)
        {
            int o = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (int k = 0; k < 4; k++) { N.Add(n); C.Add(col); }
            // Godot's front faces wind clockwise seen from the front.
            I.Add(o); I.Add(o + 1); I.Add(o + 2); I.Add(o); I.Add(o + 2); I.Add(o + 3);
        }

        /// <summary>An axis-aligned box from lo to hi, all six faces (the bottom left out).</summary>
        public void Box(Vector3 lo, Vector3 hi, Color top, Color side)
        {
            Quad(new(lo.X, hi.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(hi.X, hi.Y, hi.Z), new(lo.X, hi.Y, hi.Z), Vector3.Up, top);
            Quad(new(lo.X, lo.Y, hi.Z), new(lo.X, hi.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(hi.X, lo.Y, hi.Z), Vector3.Back, side);
            Quad(new(hi.X, lo.Y, lo.Z), new(hi.X, hi.Y, lo.Z), new(lo.X, hi.Y, lo.Z), new(lo.X, lo.Y, lo.Z), Vector3.Forward, side);
            Quad(new(hi.X, lo.Y, hi.Z), new(hi.X, hi.Y, hi.Z), new(hi.X, hi.Y, lo.Z), new(hi.X, lo.Y, lo.Z), Vector3.Right, side);
            Quad(new(lo.X, lo.Y, lo.Z), new(lo.X, hi.Y, lo.Z), new(lo.X, hi.Y, hi.Z), new(lo.X, lo.Y, hi.Z), Vector3.Left, side);
        }

        public ArrayMesh Into(ArrayMesh mesh, Material mat)
        {
            if (V.Count == 0) return mesh;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = C.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = I.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
            return mesh;
        }
    }

    private static Color Col(Rgb c, float f = 1) => new(c.R / 255f * f, c.G / 255f * f, c.B / 255f * f);

    private Color Grass() => F.Season switch
    {
        0 => new Color(0.31f, 0.47f, 0.22f),
        1 => new Color(0.28f, 0.44f, 0.18f),
        2 => new Color(0.46f, 0.41f, 0.2f),
        _ => new Color(0.82f, 0.84f, 0.86f),
    };

    private bool Solid(int x, int y, int z) => W.In(x, y, z) && W.Shapes[W.Idx(x, y, z)] is Shape.Wall;

    private MeshInstance3D BuildChunk(int cx, int cy)
    {
        var ground = new Geo();
        var water = new Geo();
        int x0 = cx * CS, y0 = cy * CS, x1 = Math.Min(W.W, x0 + CS), y1 = Math.Min(W.H, y0 + CS);
        int zLo = Math.Max(0, _z - Below);
        for (int z = zLo; z <= _z; z++)
        {
            float dim = 1f - 0.17f * (_z - z);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    Cell(ground, water, x, y, z, dim);
        }
        var mesh = new ArrayMesh();
        ground.Into(mesh, _ground);
        water.Into(mesh, _water);
        var mi = new MeshInstance3D { Mesh = mesh };
        _terrain.AddChild(mi);
        return mi;
    }

    private void Cell(Geo g, Geo water, int x, int y, int z, float dim)
    {
        int i = W.Idx(x, y, z);
        Shape s = W.Shapes[i];
        Mat m = Tables.Mats[W.Mats[i]];
        float n = 0.92f + Noise.Hash01(x, y, z, 31) * 0.14f;
        bool hidden = W.Has(i, CellFlag.Hidden);
        if (s == Shape.Wall || (hidden && s != Shape.Empty))
        {
            Color c = hidden ? new Color(0.07f, 0.065f, 0.07f) : Col(m.Color, dim * n);
            bool cut = z == _z && z + 1 < W.D && World.IsSolid(W.Shapes[W.Idx(x, y, z + 1)]);
            // On the level in view, the top of a wall is where the level above was cut away: darker.
            Color top = z < _z && Solid(x, y, z + 1) ? c : cut ? c * 0.62f : c * 1.05f;
            if (hidden) top = c;
            // Only the faces that something open can see.
            if (z == _z || !Solid(x, y, z + 1))
                g.Quad(new(x, z + 1, y), new(x + 1, z + 1, y), new(x + 1, z + 1, y + 1), new(x, z + 1, y + 1), Vector3.Up, top);
            Color side = c * 0.82f;
            if (!Solid(x, y + 1, z) && W.In(x, y + 1, z)) g.Quad(new(x, z, y + 1), new(x, z + 1, y + 1), new(x + 1, z + 1, y + 1), new(x + 1, z, y + 1), Vector3.Back, side);
            if (!Solid(x, y - 1, z) && W.In(x, y - 1, z)) g.Quad(new(x + 1, z, y), new(x + 1, z + 1, y), new(x, z + 1, y), new(x, z, y), Vector3.Forward, side * 0.9f);
            if (!Solid(x + 1, y, z) && W.In(x + 1, y, z)) g.Quad(new(x + 1, z, y + 1), new(x + 1, z + 1, y + 1), new(x + 1, z + 1, y), new(x + 1, z, y), Vector3.Right, side * 0.95f);
            if (!Solid(x - 1, y, z) && W.In(x - 1, y, z)) g.Quad(new(x, z, y), new(x, z + 1, y), new(x, z + 1, y + 1), new(x, z, y + 1), Vector3.Left, side * 0.88f);
            return;
        }
        if (s == Shape.Empty)
        {
            Liquid(water, i, x, y, z);
            return;
        }
        // Everything else stands on a floor: the ground of this cell.
        bool outside = W.IsOutside(i);
        bool grass = W.Has(i, CellFlag.Grass) && outside;
        Color fc = grass ? Grass() * n : Col(m.Color, (outside ? 0.72f : 0.55f) * n);
        if (!grass && W.Has(i, CellFlag.Wet)) fc = fc.Lerp(new Color(0.2f, 0.17f, 0.13f), 0.45f);
        if (W.Has(i, CellFlag.Built)) fc = Col(m.Color, 0.78f);
        fc *= dim;
        float fy = z + 0.001f;
        switch (s)
        {
            case Shape.Ramp:
                Ramp(g, x, y, z, fc);
                break;
            case Shape.StairUp or Shape.StairDown or Shape.StairUD:
                g.Quad(new(x, fy, y), new(x + 1, fy, y), new(x + 1, fy, y + 1), new(x, fy, y + 1), Vector3.Up, s == Shape.StairUp ? fc : fc * 0.45f);
                Color st = Col(m.Color, 0.9f * dim);
                if (s != Shape.StairDown)
                    for (int k = 0; k < 3; k++) g.Box(new(x + 0.15f, z, y + 0.15f + k * 0.23f), new(x + 0.85f, z + 0.2f + k * 0.2f, y + 0.38f + k * 0.23f), st, st * 0.8f);
                break;
            default:
                g.Quad(new(x, fy, y), new(x + 1, fy, y), new(x + 1, fy, y + 1), new(x, fy, y + 1), Vector3.Up, fc);
                break;
        }
        if (s == Shape.Tree) Tree(g, i, m, x, y, z, dim);
        else if (W.Has(i, CellFlag.Shrub))
        {
            Color b = (F.Season == 3 && outside ? new Color(0.5f, 0.45f, 0.4f) : new Color(0.22f, 0.4f, 0.18f)) * dim;
            g.Box(new(x + 0.3f, z, y + 0.3f), new(x + 0.7f, z + 0.3f, y + 0.7f), b * 1.1f, b);
            if (F.Season is 1 or 2 && outside) g.Box(new(x + 0.42f, z + 0.3f, y + 0.42f), new(x + 0.52f, z + 0.38f, y + 0.52f), new Color(0.7f, 0.15f, 0.25f), new Color(0.55f, 0.1f, 0.2f));
        }
        Liquid(water, i, x, y, z);
    }

    private void Liquid(Geo water, int i, int x, int y, int z)
    {
        int depth = W.Depth[i];
        if (depth == 0) return;
        bool magma = W.Has(i, CellFlag.Magma);
        float h = z + depth / 7f * 0.95f;
        Color c = magma ? new Color(1f, 0.42f, 0.08f, 0.95f) : new Color(0.16f, 0.36f, 0.62f, 0.62f);
        water.Quad(new(x, h, y), new(x + 1, h, y), new(x + 1, h, y + 1), new(x, h, y + 1), Vector3.Up, c);
    }

    /// <summary>A ramp rises toward the wall beside it: the floor tilted up to that side.</summary>
    private void Ramp(Geo g, int x, int y, int z, Color c)
    {
        float lo = z + 0.001f, hi = z + 1;
        if (Solid(x, y - 1, z)) g.Quad(new(x, hi, y), new(x + 1, hi, y), new(x + 1, lo, y + 1), new(x, lo, y + 1), new Vector3(0, 0.7f, 0.7f), c);
        else if (Solid(x, y + 1, z)) g.Quad(new(x, lo, y), new(x + 1, lo, y), new(x + 1, hi, y + 1), new(x, hi, y + 1), new Vector3(0, 0.7f, -0.7f), c);
        else if (Solid(x - 1, y, z)) g.Quad(new(x, hi, y), new(x + 1, lo, y), new(x + 1, lo, y + 1), new(x, hi, y + 1), new Vector3(0.7f, 0.7f, 0), c);
        else if (Solid(x + 1, y, z)) g.Quad(new(x, lo, y), new(x + 1, hi, y), new(x + 1, hi, y + 1), new(x, lo, y + 1), new Vector3(-0.7f, 0.7f, 0), c);
        else g.Quad(new(x, lo, y), new(x + 1, lo, y), new(x + 1, lo, y + 1), new(x, lo, y + 1), Vector3.Up, c);
    }

    private void Tree(Geo g, int i, Mat m, int x, int y, int z, float dim)
    {
        bool winter = F.Season == 3 && W.IsOutside(i);
        Color leaf = m.Key == "stalkwood" ? new Color(0.47f, 0.31f, 0.59f) : m.Key == "pine" ? new Color(0.13f, 0.32f, 0.2f)
            : winter ? new Color(0.43f, 0.38f, 0.31f) : F.Season == 2 ? new Color(0.67f, 0.39f, 0.16f) : new Color(0.19f, 0.41f, 0.17f);
        leaf *= dim * (0.9f + Noise.Hash01(x, y, z, 7) * 0.2f);
        Color bark = new Color(0.33f, 0.22f, 0.13f) * dim;
        float h = 0.9f + Noise.Hash01(x, y, z, 8) * 0.5f;
        g.Box(new(x + 0.42f, z, y + 0.42f), new(x + 0.58f, z + h * 0.7f, y + 0.58f), bark, bark * 0.8f);
        if (m.Key == "pine")
        {
            g.Box(new(x + 0.12f, z + h * 0.45f, y + 0.12f), new(x + 0.88f, z + h * 0.85f, y + 0.88f), leaf * 1.1f, leaf * 0.85f);
            g.Box(new(x + 0.25f, z + h * 0.85f, y + 0.25f), new(x + 0.75f, z + h * 1.25f, y + 0.75f), leaf * 1.15f, leaf * 0.9f);
        }
        else g.Box(new(x + 0.08f, z + h * 0.55f, y + 0.08f), new(x + 0.92f, z + h * 1.25f, y + 0.92f), leaf * 1.12f, leaf * 0.85f);
    }

    // ---- buildings ----------------------------------------------------------------------

    private bool Shown(int z) => z <= _z && z >= _z - Below;

    private void SyncBuildings()
    {
        var seen = new HashSet<int>();
        foreach (Building b in F.Buildings)
        {
            if (!Shown(b.Z)) continue;
            seen.Add(b.Id);
            if (!_buildings.TryGetValue(b.Id, out Node3D n) || (bool)n.GetMeta("built") != b.Built)
            {
                n?.QueueFree();
                n = MakeBuilding(b);
                n.SetMeta("built", b.Built);
                _things.AddChild(n);
                _buildings[b.Id] = n;
            }
        }
        foreach (int id in _buildings.Keys.Where(k => !seen.Contains(k)).ToList()) { _buildings[id].QueueFree(); _buildings.Remove(id); }
    }

    private Node3D MakeBuilding(Building b)
    {
        BuildDef d = b.D;
        var root = new Node3D { Position = new Vector3(b.X + d.W / 2f, b.Z, b.Y + d.H / 2f) };
        Node3D model = _painted ? Assets.Model(b.Def) : null;
        if (model != null)
        {
            // Fit the model to the footprint, standing on the floor.
            Aabb box = Bounds(model);
            float k = Math.Min(d.W * 0.96f / Math.Max(box.Size.X, 0.01f), d.H * 0.96f / Math.Max(box.Size.Z, 0.01f));
            if (d.Kind == BuildKind.Furniture && b.Def == "door") k = Math.Min(d.W / Math.Max(Math.Max(box.Size.X, box.Size.Z), 0.01f), 1 / Math.Max(box.Size.Y, 0.01f));
            model.Scale = Vector3.One * k;
            model.Position = new Vector3(-(box.Position.X + box.Size.X / 2) * k, -box.Position.Y * k, -(box.Position.Z + box.Size.Z / 2) * k);
            root.AddChild(model);
            if (!b.Built) Ghostly(model);
            return root;
        }
        // No model: a block in the building's material, with its letter on top.
        Mat m = Tables.Mats[b.Mat];
        Color c = Col(m.Color, 0.9f);
        float h = d.Kind == BuildKind.Workshop ? 0.45f : d.Kind == BuildKind.Furniture ? 0.5f : 1f;
        float inset = d.Kind == BuildKind.Furniture ? 0.18f : 0.05f;
        var mesh = new BoxMesh { Size = new Vector3(d.W - inset * 2, h, d.H - inset * 2) };
        var mi = new MeshInstance3D { Mesh = mesh, Position = new Vector3(0, h / 2, 0) };
        mi.MaterialOverride = b.Built ? new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f } : _ghost;
        root.AddChild(mi);
        var label = new Label3D
        {
            Text = d.Glyph.ToString(), FontSize = 64, PixelSize = 0.008f, Position = new Vector3(0, h + 0.02f, 0),
            RotationDegrees = new Vector3(-90, 0, 0), Modulate = new Color(1, 0.88f, 0.55f), OutlineSize = 8,
        };
        root.AddChild(label);
        return root;
    }

    private void Ghostly(Node n)
    {
        if (n is GeometryInstance3D gi) gi.MaterialOverride = _ghost;
        foreach (Node c in n.GetChildren()) Ghostly(c);
    }

    private static Aabb Bounds(Node3D n)
    {
        Aabb? box = null;
        void Walk(Node node, Transform3D t)
        {
            Transform3D here = node is Node3D n3 ? t * n3.Transform : t;
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                Aabb a = here * mi.Mesh.GetAabb();
                box = box == null ? a : box.Value.Merge(a);
            }
            foreach (Node c in node.GetChildren()) Walk(c, here);
        }
        Walk(n, Transform3D.Identity);
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }

    // ---- dwarves and creatures ----------------------------------------------------------

    /// <summary>Whether a figure at z is seen: on the level in view, or below it through open air.</summary>
    private bool FigureSeen(int cell)
    {
        P3 p = W.Pos(cell);
        if (p.Z == _z) return true;
        if (p.Z > _z || p.Z < _z - Below) return false;
        for (int z = p.Z + 1; z <= _z; z++) if (W.Shapes[W.Idx(p.X, p.Y, z)] != Shape.Empty) return false;
        return true;
    }

    private void SyncFigures()
    {
        var seen = new HashSet<int>();
        foreach (Dwarf d in F.Dwarves)
        {
            if (d.Dead || !FigureSeen(d.Cell)) continue;
            int key = d.Id;
            seen.Add(key);
            bool modelled = _painted && Figure3D.Available && (d.Race ?? "dwarf") == "dwarf";
            Node3D n = Figure(key, () => modelled ? new Figure3D() : MakeFigure(_painted ? Assets.Sprite("fig-" + d.Clan) : null, UITheme.C(Tables.Clan(d.Clan).Color), 1.05f));
            Vector2 at = Map.DwarfPos(d);
            Vector3 was = n.Position;
            n.Position = new Vector3(at.X / MapView.T, W.Pos(d.Cell).Z, at.Y / MapView.T);
            if (n is Figure3D fig) Animate(fig, d, new Vector2(n.Position.X - was.X, n.Position.Z - was.Z), _dt);
            else ((Node3D)n.GetChild(0)).Scale = Vector3.One * (d.Id == Map.SelDwarf ? 1.12f : 1f);
        }
        foreach (Creature c in F.Creatures)
        {
            if (c.Dead || !FigureSeen(c.Cell)) continue;
            int key = -1 - c.Id;
            seen.Add(key);
            Node3D n = Figure(key, () => MakeFigure(_painted ? Assets.Sprite("cr-" + c.Def) : null, Col(c.D.Color), 0.6f + c.D.Size * 0.25f));
            P3 p = W.Pos(c.Cell);
            Vector2 at = new((p.X + 0.5f) * MapView.T, (p.Y + 0.5f) * MapView.T);
            n.Position = new Vector3(at.X / MapView.T, p.Z, at.Y / MapView.T);
        }
        foreach (int id in _figures.Keys.Where(k => !seen.Contains(k)).ToList()) { _figures[id].QueueFree(); _figures.Remove(id); }
    }

    private float _dt;
    private static readonly Color[] Beards = { new(0.45f, 0.28f, 0.14f), new(0.7f, 0.3f, 0.12f), new(0.75f, 0.72f, 0.68f), new(0.85f, 0.7f, 0.35f), new(0.2f, 0.15f, 0.12f) };

    /// <summary>A modelled dwarf: what they wear and hold, what they are doing, which way they face.</summary>
    private void Animate(Figure3D fig, Dwarf d, Vector2 moved, float dt)
    {
        Item Of(int id) => id >= 0 && F.Items.TryGetValue(id, out Item it) ? it : null;
        Color Metal(Item it) => it == null ? Colors.White : Col(it.M.Color, 1.4f).Lerp(Colors.White, 0.45f);
        Color clan = UITheme.C(Tables.Clan(d.Clan).Color);
        Item armor = Of(d.Armor), helm = Of(d.Helm), shield = Of(d.Shield);
        bool leather = armor != null && armor.M.Key == "leather";
        string set = armor == null ? "cloth" : leather ? "leather" : "plate";
        var pieces = new List<(string, Color)>();
        foreach (string slot in new[] { "torso", "arms", "legs", "feet", "waist", "back" })
        {
            string piece = slot is "torso" or "arms" && armor != null ? $"{set}-{slot}" : $"cloth-{slot}";
            if (!Kit.Has(piece)) piece = $"cloth-{slot}";
            if (Kit.Has(piece)) pieces.Add((piece, piece.StartsWith("cloth") ? clan : Metal(armor)));
        }
        if (helm != null)
        {
            string h = helm.M.Key == "leather" ? "leather-head" : "plate-head";
            if (Kit.Has(h)) pieces.Add((h, Metal(helm)));
        }
        string beard = $"beard-{d.Id % 3 + 1}";
        if (Kit.Has(beard)) pieces.Add((beard, Beards[d.Id % Beards.Length]));
        // In hand: the tool for the work, the weapon for a fight.
        TaskKind k = d.Task?.Kind ?? TaskKind.Idle;
        Item weapon = Of(d.Weapon), pick = Of(d.Pick), axe = Of(d.Axe);
        (string held, Item heldItem) = k switch
        {
            TaskKind.Dig => ("pick", pick),
            TaskKind.Chop => ("axe", axe),
            TaskKind.Fight or TaskKind.Duty or TaskKind.Train => weapon != null ? ("hammer", weapon) : pick != null ? ("pick", pick) : axe != null ? ("axe", axe) : (null, null),
            TaskKind.Build or TaskKind.Craft => ("hammer", null),
            _ => (null, null),
        };
        if (held != null && !Kit.Has("held-" + held)) held = Kit.Has("held-sword") && k is TaskKind.Fight or TaskKind.Duty ? "sword" : null;
        fig.Show(pieces, held, Metal(heldItem), shield != null ? "shield" : null, Metal(shield));
        bool walking = F.Stepping.ContainsKey(d.Id) && d.Path != null;
        if (walking) fig.Act(k == TaskKind.Flee ? new[] { "run", "walk" } : new[] { "walk" });
        else fig.Act(k switch
        {
            TaskKind.Dig => new[] { "dig", "strike", "idle" },
            TaskKind.Chop => new[] { "chop", "strike", "idle" },
            TaskKind.Fight => new[] { "strike", "idle" },
            TaskKind.Build or TaskKind.Craft => new[] { "hammer", "pickup", "idle" },
            TaskKind.Sleep => new[] { "sleep", "idle" },
            TaskKind.Eat or TaskKind.Drink => new[] { "eat", "idle" },
            TaskKind.Pray => new[] { "pray", "idle" },
            TaskKind.Tantrum or TaskKind.Rampage => new[] { "tantrum", "strike", "idle" },
            TaskKind.Haul or TaskKind.Store or TaskKind.Plant or TaskKind.Harvest or TaskKind.Gather => new[] { "pickup", "idle" },
            _ => new[] { "idle" },
        });
        // Face the way they go, or what they work on.
        Vector2 face = moved;
        if (!walking && d.Task != null)
        {
            int target = d.Task.Cell >= 0 ? d.Task.Cell : -1;
            if (k == TaskKind.Fight && F.CreatureById(d.Task.Item) is Creature foe) target = foe.Cell;
            if (target >= 0)
            {
                P3 a = W.Pos(d.Cell), b = W.Pos(target);
                face = new Vector2(b.X - a.X, b.Y - a.Y);
            }
        }
        fig.Face(face, dt);
    }

    private Node3D Figure(int key, Func<Node3D> make)
    {
        if (_figures.TryGetValue(key, out Node3D n)) return n;
        n = make();
        _things.AddChild(n);
        return _figures[key] = n;
    }

    private static Node3D MakeFigure(Texture2D sprite, Color tint, float height)
    {
        var root = new Node3D();
        if (sprite != null)
        {
            var s = new Sprite3D
            {
                Texture = sprite, Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Shaded = false, AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
                PixelSize = height / sprite.GetHeight(), Centered = true, Offset = new Vector2(0, sprite.GetHeight() / 2f),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            };
            root.AddChild(s);
        }
        else
        {
            var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.22f * height, Height = height }, Position = new Vector3(0, height / 2, 0) };
            body.MaterialOverride = new StandardMaterial3D { AlbedoColor = tint, Roughness = 0.8f };
            root.AddChild(body);
        }
        return root;
    }

    // ---- items --------------------------------------------------------------------------

    private void SyncItems()
    {
        var shown = new List<(Vector3, Color)>();
        var perCell = new Dictionary<int, int>();
        foreach (Item it in F.Items.Values)
        {
            if (it.Cell < 0 || it.InBuilding >= 0 || it.HeldBy >= 0) continue;
            P3 p = W.Pos(it.Cell);
            if (!Shown(p.Z) || !FigureSeen(it.Cell)) continue;
            int k = perCell.GetValueOrDefault(it.Cell);
            perCell[it.Cell] = k + 1;
            if (k >= 4) continue;
            float ox = (k % 2) * 0.32f - 0.16f, oz = (k / 2) * 0.32f - 0.16f;
            shown.Add((new Vector3(p.X + 0.5f + ox, p.Z + 0.08f, p.Y + 0.5f + oz), Col(it.M.Color)));
        }
        MultiMesh mm = _items.Multimesh;
        mm.InstanceCount = shown.Count;
        for (int k = 0; k < shown.Count; k++)
        {
            mm.SetInstanceTransform(k, new Transform3D(Basis.Identity, shown[k].Item1));
            mm.SetInstanceColor(k, shown[k].Item2);
        }
    }

    // ---- zones, designations, the cursor ------------------------------------------------

    private void DrawOverlay()
    {
        _im.ClearSurfaces();
        _im.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        int any = 0;
        Vector3 t = Target;
        int r = (int)(Distance * 1.2f) + 8;
        int x0 = Math.Max(0, (int)t.X - r), x1 = Math.Min(W.W - 1, (int)t.X + r), y0 = Math.Max(0, (int)t.Z - r), y1 = Math.Min(W.H - 1, (int)t.Z + r);
        foreach (Zone z in F.Zones)
        {
            if (z.Z != _z || z.X1 < x0 || z.X0 > x1 || z.Y1 < y0 || z.Y0 > y1) continue;
            Color c = z.Kind switch { ZoneKind.Stockpile => new Color(0.95f, 0.82f, 0.45f, 0.22f), ZoneKind.Farm => new Color(0.45f, 0.85f, 0.4f, 0.22f), ZoneKind.Pasture => new Color(0.6f, 0.9f, 0.5f, 0.18f), _ => new Color(0.6f, 0.7f, 1f, 0.2f) };
            if (z.Id == Map.SelZone) c.A = 0.4f;
            Flat(z.X0, z.Y0, z.X1 + 1, z.Y1 + 1, _z + 0.02f, c); any++;
        }
        foreach (int c in F.Designated)
        {
            P3 p = W.Pos(c);
            if (p.Z != _z || p.X < x0 || p.X > x1 || p.Y < y0 || p.Y > y1) continue;
            float y = World.IsSolid(W.Shapes[c]) ? _z + 1.02f : _z + 0.03f;
            Color col = W.Desigs[c] switch { Desig.Chop or Desig.Gather => new Color(0.4f, 0.9f, 0.4f, 0.45f), Desig.Remove => new Color(1, 0.4f, 0.3f, 0.45f), _ => new Color(1f, 0.85f, 0.3f, 0.42f) };
            Flat(p.X + 0.08f, p.Y + 0.08f, p.X + 0.92f, p.Y + 0.92f, y, col); any++;
        }
        if (Map.Box is var (a, b))
        {
            float y = W.In(a) && World.IsSolid(W.Shapes[W.Idx(a)]) ? a.Z + 1.04f : a.Z + 0.05f;
            Flat(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1, y, Map.BoxColor); any++;
            if (Map.BoxUsable != null && W.In(a))
                for (int yy = Math.Min(a.Y, b.Y); yy <= Math.Max(a.Y, b.Y); yy++)
                    for (int xx = Math.Min(a.X, b.X); xx <= Math.Max(a.X, b.X); xx++)
                        if (W.In(xx, yy, a.Z) && Map.BoxUsable(W.Idx(xx, yy, a.Z))) Flat(xx + 0.3f, yy + 0.3f, xx + 0.7f, yy + 0.7f, y + 0.01f, new Color(0.45f, 0.9f, 0.4f, 0.8f));
        }
        if (Map.Hover is P3 h && W.In(h))
        {
            if (Map.Ghost != null)
            {
                BuildDef d = Tables.Build[Map.Ghost];
                Flat(h.X, h.Y, h.X + d.W, h.Y + d.H, h.Z + 0.05f, Map.GhostOk ? new Color(0.5f, 0.9f, 1f, 0.4f) : new Color(1f, 0.35f, 0.3f, 0.45f));
            }
            else
            {
                float y = World.IsSolid(W.Shapes[W.Idx(h)]) ? h.Z + 1.03f : h.Z + 0.04f;
                Frame(h.X, h.Y, h.X + 1, h.Y + 1, y, new Color(1, 1, 1, 0.85f));
            }
            any++;
        }
        if (Map.SelBuilding >= 0 && F.BuildingById(Map.SelBuilding) is Building sb && Shown(sb.Z))
        {
            Frame(sb.X - 0.05f, sb.Y - 0.05f, sb.X + sb.D.W + 0.05f, sb.Y + sb.D.H + 0.05f, sb.Z + 0.04f, UITheme.Gold); any++;
        }
        if (Map.SelDwarf >= 0 && F.DwarfById(Map.SelDwarf) is Dwarf sd && !sd.Dead && FigureSeen(sd.Cell))
        {
            Vector2 at = Map.DwarfPos(sd) / MapView.T;
            Ring(at.X, at.Y, W.Pos(sd.Cell).Z + 0.04f, 0.45f, UITheme.Gold); any++;
        }
        if (any == 0) Flat(0, 0, 0.01f, 0.01f, -10, new Color(0, 0, 0, 0));
        _im.SurfaceEnd();
    }

    private void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        _im.SurfaceSetColor(col); _im.SurfaceSetNormal(Vector3.Up); _im.SurfaceAddVertex(a);
        _im.SurfaceSetColor(col); _im.SurfaceSetNormal(Vector3.Up); _im.SurfaceAddVertex(b);
        _im.SurfaceSetColor(col); _im.SurfaceSetNormal(Vector3.Up); _im.SurfaceAddVertex(c);
    }

    private void Flat(float x0, float y0, float x1, float y1, float h, Color c)
    {
        Vector3 a = new(x0, h, y0), b = new(x1, h, y0), cc = new(x1, h, y1), d = new(x0, h, y1);
        Tri(a, b, cc, c); Tri(a, cc, d, c);
    }

    private void Frame(float x0, float y0, float x1, float y1, float h, Color c, float w = 0.06f)
    {
        Flat(x0, y0, x1, y0 + w, h, c);
        Flat(x0, y1 - w, x1, y1, h, c);
        Flat(x0, y0, x0 + w, y1, h, c);
        Flat(x1 - w, y0, x1, y1, h, c);
    }

    private void Ring(float x, float y, float h, float r, Color c)
    {
        const int n = 24;
        for (int k = 0; k < n; k++)
        {
            float a0 = Mathf.Tau * k / n, a1 = Mathf.Tau * (k + 1) / n;
            Vector3 p0 = new(x + Mathf.Cos(a0) * r, h, y + Mathf.Sin(a0) * r), p1 = new(x + Mathf.Cos(a1) * r, h, y + Mathf.Sin(a1) * r);
            Vector3 q0 = new(x + Mathf.Cos(a0) * (r - 0.07f), h, y + Mathf.Sin(a0) * (r - 0.07f)), q1 = new(x + Mathf.Cos(a1) * (r - 0.07f), h, y + Mathf.Sin(a1) * (r - 0.07f));
            Tri(p0, p1, q1, c); Tri(p0, q1, q0, c);
        }
    }
}
