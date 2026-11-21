using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Mechanisms in the interface: the panels of switches and what they work, linking by clicking, the wires.</summary>
public partial class FortressScreen
{
    /// <summary>A switch waiting for the player to click what it should work.</summary>
    public Building LinkFrom;

    private readonly List<(Shot shot, double born)> _shots = new();

    private string Where(Building b) => $"{b.D.Name.ToLower()} at {b.X},{b.Y}" + (b.Z != Map.Z ? $" (level {b.Z})" : "");

    private void MechanismInfo(VBoxContainer v, Building b)
    {
        if (Fortress.IsSwitch(b))
        {
            v.AddChild(UITheme.L(b.On ? "On." : "Off.", 15, b.On ? UITheme.Gold : UITheme.Text));
            if (b.Def == "lever")
            {
                string who = b.Claim >= 0 ? F.DwarfById(b.Claim)?.FirstName : null;
                if (b.PullWanted) v.AddChild(UITheme.L(who != null ? $"{who} is on the way to pull it." : "Waiting for someone to pull it.", 14, UITheme.Dim));
                else v.AddChild(UITheme.B(b.On ? "Pull it (turn off)" : "Pull it (turn on)", () => { F.WantPull(b); RefreshInfo(true); }, 14));
            }
            if (b.Def == "plate")
                v.AddChild(UITheme.B($"Set off by: {Fortress.PlateWatches[b.Watch]}", () => { b.Watch = (b.Watch + 1) % 3; RefreshInfo(true); }, 14));
            var links = F.Links.Where(l => l.Switch == b.Id).ToList();
            v.AddChild(UITheme.L(links.Count == 0 ? "Linked to nothing yet." : "Linked to:", 14, UITheme.Bronze, UITheme.Title));
            foreach (Link l in links)
            {
                Building t = F.BuildingById(l.Target);
                if (t == null) continue;
                HBoxContainer row = UITheme.H(6);
                string state = l.Built ? Fortress.OnMeans(t.Def) : l.Worker >= 0 ? "being linked" : F.Items.Values.Any(i => i.Free && i.Kind == ItemKind.Mechanism) ? "waiting for a dwarf" : "needs a mechanism";
                row.AddChild(UITheme.Wrap($"{Where(t)} -- {state}", 13, l.Built ? UITheme.Text : UITheme.Dim, 230));
                row.AddChild(UITheme.B("Unlink", () => { F.RemoveLink(l); RefreshInfo(true); }, 12));
                v.AddChild(row);
            }
            v.AddChild(UITheme.B(LinkFrom == b ? "Click what it should work..." : "Link to...", () => { LinkFrom = b; Toast("Click a door, floodgate, drawbridge, hatch, cage, launcher, stonefall trap or lift to link it. Right-click to stop."); RefreshInfo(true); }, 14));
            return;
        }
        if (!Fortress.IsLinkable(b)) return;
        var by = F.Links.Where(l => l.Target == b.Id).ToList();
        if (by.Count > 0)
        {
            v.AddChild(UITheme.L($"Worked by ({Fortress.OnMeans(b.Def)}):", 14, UITheme.Bronze, UITheme.Title));
            foreach (Link l in by)
                if (F.BuildingById(l.Switch) is Building sw)
                    v.AddChild(UITheme.L($"{Where(sw)}, {(sw.On ? "on" : "off")}{(l.Built ? "" : " (being linked)")}", 13, UITheme.Text));
        }
        switch (b.Def)
        {
            case "hatch":
                v.AddChild(UITheme.L(b.Raised ? "Open: the floor has dropped away." : "Shut: walked over like a floor.", 14, b.Raised ? UITheme.Ember : UITheme.Text));
                v.AddChild(UITheme.B(b.Raised ? "Shut it" : "Open it", () => { F.SetBridge(b, !b.Raised); RefreshInfo(true); }, 14));
                break;
            case "cage":
                if (b.Caught == null) v.AddChild(UITheme.L("Empty, set.", 14, UITheme.Good));
                else
                {
                    v.AddChild(UITheme.L($"Holds {Fortress.A(Tables.Creatures[b.Caught].Name)}{(b.CaughtRaider ? ", a raider" : "")}.", 14, UITheme.Gold));
                    HBoxContainer row = UITheme.H(6);
                    row.AddChild(UITheme.B("Put it to death", () => { F.EmptyCage(b, true); RefreshInfo(true); }, 13));
                    row.AddChild(UITheme.B("Let it go", () => { F.EmptyCage(b, false); RefreshInfo(true); }, 13));
                    v.AddChild(row);
                }
                break;
            case "launcher":
                v.AddChild(UITheme.L($"Faces {Fortress.FaceNames[b.Facing]}: {F.Line(b, b.Facing).Count} cells of open corridor.", 14, UITheme.Text));
                v.AddChild(UITheme.L(F.Tick >= b.ArmedAt ? "Loaded." : "Reloading...", 14, F.Tick >= b.ArmedAt ? UITheme.Good : UITheme.Ember));
                HBoxContainer lr = UITheme.H(6);
                lr.AddChild(UITheme.B("Turn", () => { b.Facing = (b.Facing + 1) % 4; RefreshInfo(true); }, 13));
                lr.AddChild(UITheme.B("Fire now", () => { F.Fire(b); RefreshInfo(true); }, 13));
                v.AddChild(lr);
                break;
            case "lift":
                v.AddChild(UITheme.L($"Runs from level {b.Z} to level {b.Top} ({b.Top - b.Z + 1} stops).", 14, UITheme.Text));
                v.AddChild(UITheme.L(b.Raised ? "Stopped: nobody rides it." : "Running.", 14, b.Raised ? UITheme.Ember : UITheme.Good));
                v.AddChild(UITheme.B(b.Raised ? "Start it" : "Stop it", () => { F.SetLiftStopped(b, !b.Raised); RefreshInfo(true); }, 14));
                break;
        }
    }

    /// <summary>A click while linking: the building there becomes the switch's target.</summary>
    private bool LinkClick(P3 c)
    {
        if (LinkFrom == null) return false;
        Building sw = LinkFrom;
        LinkFrom = null;
        Building t = F.BuildingOn(F.World.Idx(c));
        if (t == null) { Toast("Nothing there to link."); Sfx.Deny(); SelectThing(sw); return true; }
        string why = F.CannotLink(sw, t);
        if (why != null) { Toast($"Cannot link: {why}."); Sfx.Deny(); SelectThing(sw); return true; }
        F.AddLink(sw, t);
        Sfx.Place();
        Toast($"Linked to the {t.D.Name.ToLower()}: a dwarf will lay the mechanism.");
        SelectThing(sw);
        return true;
    }

    /// <summary>The wires from switches to what they work (lit when on), and bolts in flight.</summary>
    private void DrawWires(UITheme.Drawn c)
    {
        Building sel = Selected as Building;
        foreach (Link l in F.Links)
        {
            if (F.BuildingById(l.Switch) is not Building sw || F.BuildingById(l.Target) is not Building t) continue;
            bool mine = sel != null && (sel.Id == sw.Id || sel.Id == t.Id);
            if (!mine && sw.Z != Map.Z && t.Z != Map.Z) continue;
            Vector2 a = Map.ScreenOf(new P3(sw.X, sw.Y, sw.Z)), b = Map.ScreenOf(new P3(t.X, t.Y, t.Z));
            Color col = !l.Built ? new Color(0.75f, 0.85f, 1f, 0.55f) : sw.On ? new Color(1f, 0.78f, 0.3f, 0.95f) : new Color(0.72f, 0.45f, 0.25f, mine ? 0.9f : 0.55f);
            if (l.Built) c.DrawLine(a, b, col, mine ? 2.5f : 1.6f, true);
            else c.DrawDashedLine(a, b, col, 1.5f, 6, true);
            c.DrawCircle(a, 3, col);
            c.DrawCircle(b, 3, col);
        }
        foreach (Shot s in F.Shots) _shots.Add((s, Now));
        F.Shots.Clear();
        _shots.RemoveAll(s => Now - s.born > 0.5);
        foreach (var (s, born) in _shots)
        {
            P3 from = F.World.Pos(s.From), to = F.World.Pos(s.To);
            if (from.Z != Map.Z) continue;
            float k = (float)((Now - born) / 0.25);
            Vector2 a = Map.ScreenOf(from), b = Map.ScreenOf(to);
            Vector2 head = a.Lerp(b, Mathf.Clamp(k, 0, 1)), tail = a.Lerp(b, Mathf.Clamp(k - 0.35f, 0, 1));
            c.DrawLine(tail, head, new Color(1f, 0.92f, 0.7f, 1 - Mathf.Clamp(k - 1, 0, 1)), 2f, true);
            if (s.Hit && k >= 1) c.DrawCircle(b, 6 * (2 - k), new Color(1f, 0.4f, 0.25f, Mathf.Clamp(2 - k, 0, 1) * 0.7f));
        }
    }
}
