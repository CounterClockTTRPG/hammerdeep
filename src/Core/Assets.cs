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
}
