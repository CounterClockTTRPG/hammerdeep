using System;
using System.Collections.Generic;
using Godot;

namespace Hammerdeep;

/// <summary>
/// The main scene, and the only .tscn in the project. Owns the flow between
/// the title and a fortress, and the command-line entry points: <c>--smoke</c>
/// for the headless self-check, <c>--shots</c> for the screenshot walkthrough,
/// <c>--agent</c> for remote control.
/// </summary>
public partial class Root : Node
{
    private Node2D _world;
    private CanvasLayer _ui;
    public Fortress Fortress;
    public MapView Map;
    public FortressScreen Screen;
    public Control Current;
    private readonly HashSet<Script> _scripts = new();

    public override void _Ready()
    {
        GetWindow().Title = "Hammerdeep";
        GetTree().NodeAdded += n => { if (n.GetScript().Obj is Script s) _scripts.Add(s); };
        string[] args = OS.GetCmdlineUserArgs();
        bool smoke = Array.IndexOf(args, "--smoke") >= 0;
        bool shots = Array.IndexOf(args, "--shots") >= 0;
        bool agent = Array.IndexOf(args, "--agent") >= 0;
        Profile.Scratch = smoke || shots;
        Profile.Agent = agent;
        Profile.Load();
        Profile.Current.Apply();
        _world = new Node2D { Name = "World" };
        AddChild(_world);
        _ui = new CanvasLayer { Name = "UI", Layer = 10 };
        AddChild(_ui);
        if (smoke) { AddChild(new SmokeTest { Root = this }); return; }
        AddChild(new Sfx { Name = "Sfx" });
        AddChild(new Music { Name = "Music" });
        Music.Instance?.SetVolume(Profile.Current.MusicVolume, Profile.Current.MusicOn);
        if (shots) { AddChild(new ShotRunner { Root = this }); return; }
        if (agent) AddChild(new AgentRunner { Root = this, Name = "Agent" });
        ShowTitle();
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F11 })
        {
            Profile.Current.Fullscreen = !Profile.Current.Fullscreen;
            Profile.Current.Apply();
            Profile.Current.Save();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        // Ironman: closing the window keeps the fortress where it was.
        if (what == NotificationWMCloseRequest && Fortress != null && !Profile.Scratch) Profile.SaveFortress(Fortress);
        if (what == NotificationPredelete) Assets.ForgetModels();
    }

    private void Clear()
    {
        foreach (Node n in _ui.GetChildren()) n.QueueFree();
        foreach (Node n in _world.GetChildren()) n.QueueFree();
        Map = null;
        Screen = null;
        Current = null;
    }

    public void Show(Control c)
    {
        _ui.AddChild(c);
        Current = c;
    }

    public void ShowTitle()
    {
        if (Fortress != null && !Profile.Scratch) Profile.SaveFortress(Fortress);
        Fortress = null;
        Clear();
        Show(new TitleScreen { Root = this });
        Music.Instance?.Play(MusicMood.Title);
    }

    /// <summary>A new fortress in a slot, over whatever was there.</summary>
    public void NewFortress(string name, int slot, ulong seed = 0, ulong worldSeed = 0, int wx = -1, int wy = -1, bool tutorial = false)
    {
        Profile.Slot = slot;
        Profile.Current.LastSlot = slot;
        Profile.Current.Save();
        if (seed == 0) seed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Fortress f = Fortress.New(name, seed, worldSeed: worldSeed, ex: wx, ey: wy);
        if (tutorial) f.TutorialStep = 0;
        if (!Profile.Scratch) Profile.SaveFortress(f, slot);
        Play(f);
    }

    /// <summary>The world, to choose where the expedition stops.</summary>
    public void ShowWorld(int slot, ulong worldSeed = 0)
    {
        Clear();
        if (worldSeed == 0) worldSeed = ((ulong)GD.Randi() << 32) | GD.Randi();
        Show(new WorldScreen { Root = this, Slot = slot, Map = new WorldMap(worldSeed) });
    }

    public bool Continue(int slot)
    {
        Fortress f = Profile.LoadFortress(slot);
        if (f == null) return false;
        Profile.Slot = slot;
        Profile.Current.LastSlot = slot;
        Profile.Current.Save();
        Play(f);
        return true;
    }

    public void Play(Fortress f)
    {
        Clear();
        Fortress = f;
        Map = new MapView { Name = "Map" };
        _world.AddChild(Map);
        Map.Bind(f);
        Screen = new FortressScreen { Root = this, F = f, Map = Map };
        Show(Screen);
    }
}
