using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The parts a 3D figure is put together from (<c>assets/figures</c>), all made by assetgen's
/// Blender agent on one skeleton template (root, hips, spine_01..03, neck, head, shoulder/
/// upper_arm/forearm/hand _l/_r, thigh/shin/foot _l/_r), so any piece fits any body and any
/// clip plays on it:
///   body.glb             the base body, with sockets (socket_hand_r, socket_hand_l, socket_forearm_l, socket_back)
///   &lt;set&gt;-&lt;slot&gt;.glb    an outfit piece skinned to the template (cloth-torso, plate-head, beard-1...)
///   clip-&lt;name&gt;.glb      an animation clip (walk, idle, dig, chop, strike, hammer, sleep...)
///   held-&lt;name&gt;.glb      something held, its grip at the origin (pick, axe, sword, hammer, shield)
/// Read at run time like the building models; everything is optional.
/// </summary>
public static class Kit
{
    public static readonly string Dir = FileAccess.FileExists("res://assets/figures/manifest.txt")
        ? "res://assets/figures"
        : OS.GetExecutablePath().GetBaseDir() + "/assets/figures";

    private static readonly Dictionary<string, Node3D> _protos = new();
    private static AnimationLibrary _clips;

    private static readonly Dictionary<string, bool> _has = new();

    /// <summary>Is there such a part? Asked many times a frame, so the answer is kept (the files do not change while playing).</summary>
    public static bool Has(string name)
    {
        if (!_has.TryGetValue(name, out bool yes)) _has[name] = yes = FileAccess.FileExists($"{Dir}/{name}.glb");
        return yes;
    }

    /// <summary>A fresh copy of a part, or null.</summary>
    public static Node3D Part(string name)
    {
        if (!_protos.TryGetValue(name, out Node3D proto))
        {
            proto = null;
            string path = $"{Dir}/{name}.glb";
            if (FileAccess.FileExists(path))
            {
                var doc = new GltfDocument();
                var state = new GltfState();
                if (doc.AppendFromBuffer(FileAccess.GetFileAsBytes(path), "", state) == Error.Ok) proto = doc.GenerateScene(state) as Node3D;
                if (proto != null) DropColliders(proto);
            }
            _protos[name] = proto;
        }
        return proto?.Duplicate() as Node3D;
    }

    /// <summary>Every clip, its tracks aimed at the figure's skeleton (under its "Body" node) and fitted to that body.</summary>
    public static AnimationLibrary Clips(Skeleton3D ours)
    {
        if (_clips != null) return _clips;
        _clips = new AnimationLibrary();
        ClipTrouble.Clear();
        foreach (string name in ClipNames())
        {
            Node3D scene = Part("clip-" + name);
            if (scene == null) { ClipTrouble.Add($"{name}: did not load"); continue; }
            var player = Find<AnimationPlayer>(scene);
            if (player == null) ClipTrouble.Add($"{name}: no animation player");
            if (player != null)
            {
                string src = player.GetAnimationList().FirstOrDefault();
                if (src == null) ClipTrouble.Add($"{name}: no animation");
                if (src != null)
                {
                    var anim = (Animation)player.GetAnimation(src).Duplicate(true);
                    Retarget(anim, Find<Skeleton3D>(scene), ours);
                    anim.LoopMode = name is "death" or "sleep" or "pickup" ? Animation.LoopModeEnum.None : Animation.LoopModeEnum.Linear;
                    _clips.AddAnimation(name, anim);
                }
            }
            scene.Free();
        }
        return _clips;
    }

    /// <summary>
    /// Point every bone track at the figure's skeleton and drop the rest (the mannequin's own
    /// meshes, scale). Rotations carry over as they are -- the template's rest pose is shared --
    /// but positions are the clip's movement away from its rest, scaled by how much shorter
    /// (or taller) our body stands, added to our rest: so a dwarf's hips stay at dwarf height.
    /// </summary>
    private static void Retarget(Animation anim, Skeleton3D theirs, Skeleton3D ours)
    {
        float ratio = 1;
        if (theirs != null && ours != null)
        {
            int th = theirs.FindBone("hips"), oh = ours.FindBone("hips");
            if (th >= 0 && oh >= 0)
            {
                float a = theirs.GetBoneGlobalRest(th).Origin.Y, b = ours.GetBoneGlobalRest(oh).Origin.Y;
                if (a > 0.01f && b > 0.01f) ratio = b / a;
            }
        }
        for (int t = anim.GetTrackCount() - 1; t >= 0; t--)
        {
            NodePath p = anim.TrackGetPath(t);
            string bone = p.GetConcatenatedSubNames();
            Animation.TrackType type = anim.TrackGetType(t);
            if (string.IsNullOrEmpty(bone) || type == Animation.TrackType.Scale3D || (ours != null && ours.FindBone(bone) < 0)) { anim.RemoveTrack(t); continue; }
            if (type == Animation.TrackType.Position3D && theirs != null && ours != null)
            {
                Vector3 theirRest = theirs.GetBoneRest(theirs.FindBone(bone)).Origin, ourRest = ours.GetBoneRest(ours.FindBone(bone)).Origin;
                for (int k = 0; k < anim.TrackGetKeyCount(t); k++)
                    anim.TrackSetKeyValue(t, k, ourRest + ((Vector3)anim.TrackGetKeyValue(t, k) - theirRest) * ratio);
            }
            anim.TrackSetPath(t, new NodePath($"Body/{SkeletonPath}:{bone}"));
        }
    }

    /// <summary>
    /// The clips there are: from the manifest, since an exported build's pack can read a file but not list
    /// a folder (every clip was lost that way); the folder itself only when there is no manifest.
    /// </summary>
    private static IEnumerable<string> ClipNames()
    {
        string mf = $"{Dir}/manifest.txt";
        if (FileAccess.FileExists(mf))
            return FileAccess.GetFileAsString(mf).Split('\n').Select(l => l.Trim())
                .Where(l => l.StartsWith("clip-")).Select(l => l.Split(' ')[0][5..]).Where(n => Has("clip-" + n)).ToList();
        var dir = DirAccess.Open(Dir);
        if (dir == null) { ClipTrouble.Add($"cannot list {Dir}: {DirAccess.GetOpenError()}"); return Array.Empty<string>(); }
        return dir.GetFiles().Where(f => f.StartsWith("clip-") && f.EndsWith(".glb")).Select(f => f[5..^4]).ToList();
    }

    /// <summary>Why clips were left out of the last <see cref="Clips"/> (the self-check reports it).</summary>
    public static readonly List<string> ClipTrouble = new();

    /// <summary>Where the skeleton sits in a body, set when the first body is loaded.</summary>
    public static string SkeletonPath = "Skeleton3D";

    public static T Find<T>(Node n) where T : Node
    {
        if (n is T t) return t;
        foreach (Node c in n.GetChildren()) if (Find<T>(c) is T f) return f;
        return null;
    }

    public static Node FindNamed(Node n, string name)
    {
        if (n.Name == name) return n;
        foreach (Node c in n.GetChildren()) if (FindNamed(c, name) is Node f) return f;
        return null;
    }

    private static void DropColliders(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            string name = c.Name.ToString();
            if (name.EndsWith("-colonly") || name.EndsWith("-col")) { n.RemoveChild(c); c.Free(); }
            else DropColliders(c);
        }
    }

    public static void Forget()
    {
        foreach (Node3D n in _protos.Values) n?.Free();
        _protos.Clear();
        _has.Clear();
        _clips = null;
    }
}

/// <summary>
/// One figure: a body, outfit pieces moved onto its skeleton, held things in its sockets, and an
/// animation player with every clip. <see cref="Show"/> sets what it wears and holds (rebuilt only
/// when that changes) and <see cref="Act"/> what it does.
/// </summary>
public partial class Figure3D : Node3D
{
    private Node3D _body;
    private Skeleton3D _skel;
    private AnimationPlayer _player;
    private readonly List<Node> _worn = new(), _held = new();
    private string _wearing = "", _holding = "", _clip = "";
    private float _scale = 1;

    /// <summary>What the view last dressed this figure from (its gear, task and gait); it re-dresses only when that changes.</summary>
    public object Gear;

    /// <summary>Whether figures can be built at all (there is a body).</summary>
    public static bool Available => Kit.Has("body");

    public override void _Ready()
    {
        _body = Kit.Part("body");
        if (_body == null) return;
        _body.Name = "Body";
        AddChild(_body);
        _skel = Kit.Find<Skeleton3D>(_body);
        if (_skel != null) Kit.SkeletonPath = _body.GetPathTo(_skel).ToString();
        // Its own player only: the body's (if any) would fight it.
        foreach (AnimationPlayer p in _body.FindChildren("*", "AnimationPlayer", true, false).Cast<AnimationPlayer>()) p.QueueFree();
        _player = new AnimationPlayer { Name = "Anim" };
        AddChild(_player);
        _player.RootNode = new NodePath("..");
        _player.AddAnimationLibrary("", Kit.Clips(_skel));
        // A dwarf stands about 1.3 m whatever the body was modelled at.
        Aabb box = MeshBounds(_body);
        _scale = box.Size.Y > 0.1f ? 1.3f / box.Size.Y : 1;
        _body.Scale = Vector3.One * _scale;
    }

    /// <summary>What the figure wears (outfit pieces with tints) and holds (right hand, left arm).</summary>
    public void Show(IReadOnlyList<(string piece, Color tint)> pieces, string right, Color rightTint, string left, Color leftTint)
    {
        if (_skel == null) return;
        // Clothes and what is in hand are rebuilt apart: a dwarf changes tools far more often than clothes,
        // and re-binding every skinned piece for a new pick is what made task changes stutter.
        string wear = string.Join(",", pieces.Select(p => p.piece + p.tint.ToHtml()));
        if (wear != _wearing)
        {
            _wearing = wear;
            foreach (Node n in _worn) n.QueueFree();
            _worn.Clear();
            foreach (var (piece, tint) in pieces) Wear(piece, tint);
        }
        string hold = right + rightTint.ToHtml() + "|" + left + leftTint.ToHtml();
        if (hold != _holding)
        {
            _holding = hold;
            foreach (Node n in _held) n.QueueFree();
            _held.Clear();
            Hold(right, "socket_hand_r", rightTint);
            Hold(left, "socket_forearm_l", leftTint);
        }
    }

    /// <summary>Move a piece's skinned meshes onto this skeleton, binding by bone name.</summary>
    private void Wear(string piece, Color tint)
    {
        Node3D part = Kit.Part(piece);
        if (part == null) return;
        Skeleton3D theirs = Kit.Find<Skeleton3D>(part);
        foreach (MeshInstance3D mi in part.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().ToList())
        {
            if (mi.Skin == null)
            {
                // A rigid part riding a bone (beard rings, a moustache, a helm's crest): on our bone, where it sat on theirs.
                BoneAttachment3D on = Ancestor<BoneAttachment3D>(mi);
                if (on == null || _skel.FindBone(on.BoneName) < 0) continue;
                Transform3D rel = Transform3D.Identity;
                for (Node n = mi; n != on; n = n.GetParent()) if (n is Node3D n3) rel = n3.Transform * rel;
                var at = new BoneAttachment3D { BoneName = on.BoneName };
                _skel.AddChild(at);
                mi.GetParent().RemoveChild(mi);
                at.AddChild(mi);
                mi.Transform = rel;
                Tint(mi, tint);
                _worn.Add(at);
                continue;
            }
            if (theirs == null) continue;
            var skin = (Skin)mi.Skin.Duplicate();
            for (int b = 0; b < skin.GetBindCount(); b++)
            {
                string bone = skin.GetBindName(b);
                if (string.IsNullOrEmpty(bone)) bone = theirs.GetBoneName(skin.GetBindBone(b));
                int ours = _skel.FindBone(bone);
                if (ours >= 0) { skin.SetBindName(b, ""); skin.SetBindBone(b, ours); }
            }
            mi.GetParent().RemoveChild(mi);
            _skel.AddChild(mi);
            mi.Transform = Transform3D.Identity;
            mi.Skin = skin;
            mi.Skeleton = mi.GetPathTo(_skel);
            Tint(mi, tint);
            _worn.Add(mi);
        }
        part.Free();
    }

    private static T Ancestor<T>(Node n) where T : Node
    {
        for (Node p = n.GetParent(); p != null; p = p.GetParent()) if (p is T t) return t;
        return null;
    }

    private void Hold(string what, string socket, Color tint)
    {
        if (string.IsNullOrEmpty(what)) return;
        Node3D item = Kit.Part("held-" + what);
        Node at = Kit.FindNamed(_body, socket);
        if (item == null || at == null) { item?.Free(); return; }
        at.AddChild(item);
        if (tint != Colors.White) foreach (MeshInstance3D mi in item.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()) Tint(mi, tint);
        _held.Add(item);
    }

    /// <summary>Multiply a mesh's materials by a tint (clan cloth, the metal of armour).</summary>
    private static void Tint(MeshInstance3D mi, Color tint)
    {
        if (tint == Colors.White || mi.Mesh == null) return;
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
            if (mi.Mesh.SurfaceGetMaterial(s) is BaseMaterial3D m)
            {
                var c = (BaseMaterial3D)m.Duplicate();
                c.AlbedoColor *= tint;
                mi.SetSurfaceOverrideMaterial(s, c);
            }
    }

    /// <summary>Play a clip (the first of the names that exists), blending from the last.</summary>
    public void Act(params string[] clips)
    {
        if (_player == null) return;
        string clip = clips.FirstOrDefault(c => _player.HasAnimation(c)) ?? "";
        if (clip == _clip) return;
        _clip = clip;
        if (clip == "") _player.Stop();
        else _player.Play(clip, 0.18);
    }

    /// <summary>Turn to face a direction on the map (x, y), smoothly.</summary>
    public void Face(Vector2 dir, float delta)
    {
        if (dir.LengthSquared() < 1e-4f) return;
        float want = Mathf.Atan2(dir.X, dir.Y);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, want, Math.Min(1, delta * 10)), 0);
    }

    private static Aabb MeshBounds(Node3D n)
    {
        Aabb? box = null;
        foreach (MeshInstance3D mi in n.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            Aabb a = mi.Mesh.GetAabb();
            box = box == null ? a : box.Value.Merge(a);
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }
}

/// <summary>
/// A creature or one of the other peoples as a posed model (assets/models/cr-*.glb, race-*.glb): fitted to
/// its height and stood on the ground, turned to where it goes, and given a little bob while it walks --
/// the models have no skeleton, so the game supplies the life.
/// </summary>
public partial class Posed3D : Node3D
{
    private readonly Node3D _model;
    private readonly float _height;
    private float _step;

    /// <summary>How tall each one stands, in metres (a dwarf is 1.3).</summary>
    public static float HeightOf(string name) => name switch
    {
        "cr-hen" => 0.45f, "cr-crawler" => 0.5f, "cr-badger" => 0.7f, "cr-goat" => 0.9f, "cr-fellbuck" => 1.5f,
        "cr-stalker" => 2.1f, "cr-raider" => 1.2f, "cr-brute" => 2.1f, "cr-trader" => 1.3f, "cr-merchant" => 1.75f,
        "race-delvling" => 1.1f, "race-rootfolk" => 1.0f, "race-stonekin" => 2.2f, _ => 1.2f,
    };

    public Posed3D() { }

    public Posed3D(Node3D model, float height)
    {
        _height = height;
        _model = new Node3D();
        AddChild(_model);
        _model.AddChild(model);
        Aabb box = Bounds(model);
        float s = box.Size.Y > 0.01f ? height / box.Size.Y : 1;
        model.Scale = Vector3.One * s;
        Vector3 c = box.GetCenter();
        model.Position = new Vector3(-c.X * s, -box.Position.Y * s, -c.Z * s);
    }

    /// <summary>Moved by <paramref name="by"/> (map x, y) this frame: face that way and bob; standing, settle.</summary>
    public void Step(Vector2 by, float dt, bool selected)
    {
        if (_model == null) return;
        if (by.LengthSquared() > 1e-6f)
        {
            Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(by.X, by.Y), Math.Min(1, dt * 10)), 0);
            _step += dt * 11;
        }
        else _step = Mathf.MoveToward(_step % Mathf.Pi, 0, dt * 6);
        _model.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(_step)) * 0.05f * _height, 0);
        _model.Rotation = new Vector3(0, 0, Mathf.Sin(_step) * 0.06f);
        _model.Scale = Vector3.One * (selected ? 1.12f : 1f);
    }

    /// <summary>The model's box in its own frame, through every node between it and each mesh.</summary>
    private static Aabb Bounds(Node3D n)
    {
        Aabb? box = null;
        foreach (MeshInstance3D mi in n.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
        {
            if (mi.Mesh == null) continue;
            Transform3D t = Transform3D.Identity;
            for (Node p = mi; p != null && p != n; p = p.GetParent()) if (p is Node3D p3) t = p3.Transform * t;
            Aabb a = t * mi.Mesh.GetAabb();
            box = box == null ? a : box.Value.Merge(a);
        }
        return box ?? new Aabb(Vector3.Zero, Vector3.One);
    }
}
