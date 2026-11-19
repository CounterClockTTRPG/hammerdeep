using Godot;

namespace Hammerdeep;

/// <summary>
/// The painted terrain, drawn behind the map node with nearest filtering so the pixel art
/// stays crisp, while the map itself draws the sprites over it with smooth filtering.
/// </summary>
public partial class GroundLayer : Node2D
{
    public MapView Map;
    public override void _Draw() => Map?.DrawGround(this);
}
