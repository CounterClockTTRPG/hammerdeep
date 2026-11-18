using Godot;
namespace Hammerdeep;
public partial class MapView : Node2D { public void Bind(Fortress f) { } }
public partial class FortressScreen : Control { public Root Root; public Fortress F; public MapView Map; }
public partial class TitleScreen : Control { public Root Root; }
public partial class Sfx : Node { }
public partial class ShotRunner : Node { public Root Root; }
public partial class AgentRunner : Node { public Root Root; }
