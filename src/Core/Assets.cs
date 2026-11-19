using System.Collections.Generic;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The generated sprites (<c>assets/art/*.webp</c>): figures, creatures, furniture and
/// workshops, made through ../assetgen by <c>scripts/assets.sh</c> from the manifest beside
/// them. Optional by design: every caller draws its own figure when this returns null.
/// Read as bytes and decoded here, not imported (<c>assets/.gdignore</c>), like the fonts.
/// </summary>
public static class Assets
{
    /// <summary>In the project when run from there; beside the executable in an exported build.</summary>
    public static readonly string ArtDir = FileAccess.FileExists("res://assets/art/manifest.txt")
        ? "res://assets/art"
        : OS.GetExecutablePath().GetBaseDir() + "/assets/art";

    private static readonly Dictionary<string, Texture2D> _sprites = new();

    /// <summary>A sprite by manifest name, or null when it is missing or the player chose the drawn look.</summary>
    public static Texture2D Sprite(string name)
    {
        // The player can choose the original drawn-in-code look instead (settings).
        if (Profile.Current != null && !Profile.Current.PaintedArt) return null;
        if (_sprites.TryGetValue(name, out Texture2D t)) return t;
        t = null;
        string path = $"{ArtDir}/{name}.webp";
        if (FileAccess.FileExists(path))
        {
            var image = new Image();
            if (image.LoadWebpFromBuffer(FileAccess.GetFileAsBytes(path)) == Error.Ok)
            {
                // Drawn from a quarter of their size at far zoom to life size close up: mipmaps.
                image.GenerateMipmaps();
                t = ImageTexture.CreateFromImage(image);
            }
        }
        return _sprites[name] = t;
    }

    /// <summary>The 3D models (<c>assets/models/*.glb</c>), made by the Blender agent through assetgen.</summary>
    public static readonly string ModelDir = FileAccess.FileExists("res://assets/models/manifest.txt")
        ? "res://assets/models"
        : OS.GetExecutablePath().GetBaseDir() + "/assets/models";

    private static readonly Dictionary<string, Node3D> _models = new();

    /// <summary>A fresh copy of a model by name (glTF read at run time, not imported), or null when there is none.</summary>
    public static Node3D Model(string name)
    {
        if (!_models.TryGetValue(name, out Node3D proto))
        {
            proto = null;
            string path = $"{ModelDir}/{name}.glb";
            if (FileAccess.FileExists(path))
            {
                var doc = new GltfDocument();
                var state = new GltfState();
                if (doc.AppendFromBuffer(FileAccess.GetFileAsBytes(path), "", state) == Error.Ok) proto = doc.GenerateScene(state) as Node3D;
                // Collision shapes ride along as "-colonly" meshes (an editor import hint): not for drawing.
                if (proto != null) DropColliders(proto);
            }
            _models[name] = proto;
        }
        return proto?.Duplicate() as Node3D;
    }

    /// <summary>Free the loaded models (they live outside the tree): on quitting, and after the self-check.</summary>
    public static void ForgetModels()
    {
        foreach (Node3D n in _models.Values) n?.Free();
        _models.Clear();
    }

    private static void DropColliders(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            string name = c.Name.ToString();
            if (name.EndsWith("-colonly") || name.EndsWith("-col") || name.EndsWith("_colonly")) { n.RemoveChild(c); c.Free(); }
            else DropColliders(c);
        }
    }
}
