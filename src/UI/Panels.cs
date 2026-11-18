using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>The info panel: whatever is selected, in detail, with its controls.</summary>
public partial class FortressScreen
{
    private string InfoSignature()
    {
        switch (Selected)
        {
            case Dwarf d:
                return $"d{d.Id}:{d.Dead}:{d.Activity}:{d.Hunger / 300}:{d.Thirst / 300}:{d.Fatigue / 300}:{d.Stress / 300}:{d.Mood}:{string.Join(",", d.Thoughts.Select(t => t.Id))}:{string.Join("", d.Labors.Select(b => b ? '1' : '0'))}:{d.Xp.Sum() / 50}:{d.Bed}";
            case Building b:
                return $"b{b.Id}:{b.Built}:{b.Parts.Count}:{b.Owner}:{b.Builder}:{string.Join(";", b.Orders.Select(o => $"{o.Recipe},{o.Left},{o.Repeat},{o.Worker},{o.Stuck}"))}";
            case Zone z:
                return $"z{z.Id}:{z.Cats}:{z.Crop}:{F.Zones.Contains(z)}:{(int)(F.Tick / 100)}";
            case P3 p:
                int c = F.World.Idx(p);
                return $"c{p}:{F.World.Shapes[c]}:{F.World.Desigs[c]}:{string.Join(",", F.ItemsOn(c).Select(i => i.Id))}";
        }
        return "none";
    }

    private bool FillInfo(VBoxContainer v)
    {
        switch (Selected)
        {
            case Dwarf d: DwarfInfo(v, d); return true;
            case Building b when F.Buildings.Contains(b): BuildingInfo(v, b); return true;
            case Zone z when F.Zones.Contains(z): ZoneInfo(v, z); return true;
            case P3 p when F.World.In(p): CellInfo(v, p); return true;
        }
        return false;
    }

    private static Control Row(string label, Control value)
    {
        HBoxContainer h = UITheme.H(8);
        Label l = UITheme.L(label, 14, UITheme.Dim);
        l.CustomMinimumSize = new Vector2(70, 0);
        h.AddChild(l);
        h.AddChild(value);
        return h;
    }

    private void NeedBar(VBoxContainer v, string name, int value, int warn, int bad)
    {
        float frac = Math.Clamp(value / (float)bad, 0, 1);
        Color c = value > bad ? UITheme.Bad : value > warn ? UITheme.Ember : UITheme.Good;
        string word = value > bad ? "desperate" : value > warn ? "wants it" : "fine";
        HBoxContainer h = UITheme.H(6);
        h.AddChild(UITheme.Bar(1 - frac, c, 140, 9));
        h.AddChild(UITheme.L(word, 13, c));
        v.AddChild(Row(name, h));
    }

    private void DwarfInfo(VBoxContainer v, Dwarf d)
    {
        ClanDef clan = Tables.Clan(d.Clan);
        v.AddChild(UITheme.Heading(d.Name, 20));
        v.AddChild(UITheme.L($"of clan {clan.Name}, aged {d.Age}  ·  \"{clan.Motto}\"", 13, UITheme.C(clan.Color).Lightened(0.3f)));
        if (d.Dead) { v.AddChild(UITheme.L($"Dead: {d.DeathCause}.", 16, UITheme.Bad)); return; }
        v.AddChild(UITheme.L(d.Activity, 16, UITheme.Text));
        NeedBar(v, "Food", d.Hunger, Tables.HungryAt, Tables.StarvingAt);
        NeedBar(v, "Drink", d.Thirst, Tables.ThirstyAt, Tables.ParchedAt);
        NeedBar(v, "Rest", d.Fatigue, Tables.TiredAt, Tables.ExhaustedAt);
        Color mc = d.Mood >= 10 ? UITheme.Good : d.Mood >= 0 ? UITheme.Text : d.Mood >= -15 ? UITheme.Ember : UITheme.Bad;
        HBoxContainer mood = UITheme.H(6);
        mood.AddChild(UITheme.L($"{d.MoodWord} ({d.Mood:+0;-0;0})", 15, mc));
        if (d.Stress > 500) mood.AddChild(UITheme.L($"stress {d.Stress / 100}", 13, d.Stress > 2000 ? UITheme.Bad : UITheme.Ember));
        v.AddChild(Row("Mood", mood));
        v.AddChild(Row("Nature", UITheme.Wrap(string.Join(", ", d.Traits.Select(t => Tables.Traits.First(x => x.Id == t).Name)) + $"; likes {Tables.M(d.LikesMat).Name}", 14, UITheme.Text, 240)));
        if (d.Bed >= 0) v.AddChild(Row("Bed", UITheme.L("has their own bed", 14, UITheme.Text)));
        v.AddChild(UITheme.L("Thoughts", 13, UITheme.Bronze, UITheme.Title));
        foreach (Thought t in d.Thoughts.OrderBy(t => -Math.Abs(t.Value)).Take(7))
            v.AddChild(UITheme.L($"{(t.Value >= 0 ? "+" : "")}{t.Value}  {Tables.Thoughts[t.Id].Text}", 13, t.Value >= 0 ? UITheme.Good : UITheme.Ember));
        if (d.Thoughts.Count == 0) v.AddChild(UITheme.L("Nothing much on their mind.", 13, UITheme.Faint));
        v.AddChild(UITheme.L("Skills", 13, UITheme.Bronze, UITheme.Title));
        var skills = Tables.AllLabors.Where(l => d.Skill(l) > 0).OrderByDescending(d.Skill).ToList();
        v.AddChild(UITheme.Wrap(skills.Count == 0 ? "None to speak of." : string.Join(", ", skills.Select(l => $"{Tables.LaborName(l)} {d.Skill(l)}")), 13, UITheme.Text, 320));
        v.AddChild(UITheme.L("Labors (what they may do)", 13, UITheme.Bronze, UITheme.Title));
        var grid = new GridContainer { Columns = 2 };
        foreach (Labor l in Tables.AllLabors)
        {
            if (l == Labor.Fighting) continue;
            var cb = new CheckBox { Text = Tables.LaborName(l), ButtonPressed = d.Can(l), FocusMode = FocusModeEnum.None };
            cb.AddThemeFontSizeOverride("font_size", 13);
            cb.Toggled += on => { d.Labors[(int)l] = on; };
            grid.AddChild(cb);
        }
        v.AddChild(grid);
        HBoxContainer h = UITheme.H(6);
        h.AddChild(UITheme.B(Follow == d ? "Following" : "Follow", () => { Follow = Follow == d ? null : d; RefreshInfo(true); }, 14));
        h.AddChild(UITheme.B("Prev", () => Cycle(d, -1), 14));
        h.AddChild(UITheme.B("Next", () => Cycle(d, 1), 14));
        v.AddChild(h);
    }

    private void Cycle(Dwarf d, int dir)
    {
        var all = F.Living.ToList();
        if (all.Count == 0) return;
        int i = all.IndexOf(d);
        Dwarf n = all[((i + dir) % all.Count + all.Count) % all.Count];
        SelectThing(n);
        Jump(F.World.Pos(n.Cell));
    }

    private void BuildingInfo(VBoxContainer v, Building b)
    {
        BuildDef d = b.D;
        string mat = b.Built ? Tables.Mats[b.Mat].Name + " " : "";
        v.AddChild(UITheme.Heading(b.Built ? $"{Item.QualityName[Math.Clamp(b.Quality, 0, 5)]}{mat}{d.Name.ToLower()}".Trim() : d.Name, 20));
        v.AddChild(UITheme.Wrap(d.Help, 13, UITheme.Dim, 320));
        if (!b.Built)
        {
            var missing = F.MissingParts(b).ToList();
            string who = b.Builder >= 0 ? F.DwarfById(b.Builder)?.Name : null;
            v.AddChild(UITheme.L(who != null ? $"{who} is building it." : "Waiting for a builder.", 14, UITheme.Text));
            if (missing.Count > 0)
            {
                bool any = missing.All(n => F.Items.Values.Count(i => i.Free && Tables.Matches(n.Tag, i)) >= n.Count);
                v.AddChild(UITheme.Wrap("Needs " + string.Join(", ", missing.Select(n => $"{n.Count} {Tables.NeedName(n.Tag)}")) + (any ? "." : " -- none to hand!"), 14, any ? UITheme.Text : UITheme.Ember, 320));
            }
            v.AddChild(UITheme.B("Cancel", () => { F.Deconstruct(b); SelectThing(null); }, 14));
            return;
        }
        if (d.Id == "bed") v.AddChild(UITheme.L(b.Owner >= 0 ? $"Sleeper: {F.DwarfById(b.Owner)?.Name}" : "Nobody has claimed it yet.", 14, UITheme.Text));
        if (d.Kind == BuildKind.Workshop) WorkshopOrders(v, b);
        v.AddChild(UITheme.B("Take it down", () => { F.Deconstruct(b); SelectThing(null); }, 14));
    }

    private void WorkshopOrders(VBoxContainer v, Building b)
    {
        v.AddChild(UITheme.L("Work orders", 13, UITheme.Bronze, UITheme.Title));
        if (b.Orders.Count == 0) v.AddChild(UITheme.L("None. Add one below.", 13, UITheme.Faint));
        foreach (Order o in b.Orders.ToList())
        {
            Recipe r = Tables.Recipe(o.Recipe);
            HBoxContainer h = UITheme.H(6);
            Label name = UITheme.L(o.Repeat ? $"{r.Name} (repeat)" : $"{r.Name} x{o.Left}", 14, UITheme.Text);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            h.AddChild(name);
            h.AddChild(UITheme.B("x", () =>
            {
                if (o.Worker >= 0) { Dwarf w = F.DwarfById(o.Worker); if (w != null) { F.Release(w); w.Task = null; } }
                b.Orders.Remove(o);
                RefreshInfo(true);
            }, 12, "Cancel this order"));
            v.AddChild(h);
            string status = o.Worker >= 0 ? $"   {F.DwarfById(o.Worker)?.FirstName} is at work" : o.Stuck != null ? $"   waiting: {o.Stuck}" : "   waiting for a worker";
            v.AddChild(UITheme.L(status, 12, o.Stuck != null && o.Worker < 0 ? UITheme.Ember : UITheme.Dim));
        }
        v.AddChild(UITheme.L("New order", 13, UITheme.Bronze, UITheme.Title));
        foreach (Recipe r in Tables.RecipesFor(b.Def))
        {
            HBoxContainer h = UITheme.H(4);
            string inputs = string.Join(" + ", r.Inputs.Select(n => Tables.NeedName(n.Tag)));
            Label l = UITheme.L(r.Name, 14, UITheme.Text);
            l.TooltipText = $"{inputs}  ({Tables.LaborName(r.Labor)})";
            l.MouseFilter = MouseFilterEnum.Pass;
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            h.AddChild(l);
            h.AddChild(UITheme.B("+1", () => { F.AddOrder(b, r.Id, 1); RefreshInfo(true); }, 12, inputs));
            h.AddChild(UITheme.B("+5", () => { F.AddOrder(b, r.Id, 5); RefreshInfo(true); }, 12, inputs));
            h.AddChild(UITheme.B("∞", () => { F.AddOrder(b, r.Id, 1, true); RefreshInfo(true); }, 12, "Repeat for ever"));
            v.AddChild(h);
        }
    }

    private static readonly StockCat[] Cats = { StockCat.Stone, StockCat.Wood, StockCat.Food, StockCat.Drink, StockCat.Seeds, StockCat.Bars, StockCat.Gems, StockCat.Furniture, StockCat.Weapons, StockCat.Armor, StockCat.Finished, StockCat.Refuse };

    private void ZoneInfo(VBoxContainer v, Zone z)
    {
        v.AddChild(UITheme.Heading(z.Name, 20));
        v.AddChild(UITheme.L($"{z.X1 - z.X0 + 1} x {z.Y1 - z.Y0 + 1} at {DepthName(z.Z)}", 13, UITheme.Dim));
        switch (z.Kind)
        {
            case ZoneKind.Stockpile:
                int n = z.Cells().Sum(p => F.ItemsOn(F.World.Idx(p)).Count());
                v.AddChild(UITheme.L($"Holds {n} things.", 14, UITheme.Text));
                v.AddChild(UITheme.L("Accepts", 13, UITheme.Bronze, UITheme.Title));
                var grid = new GridContainer { Columns = 2 };
                foreach (StockCat c in Cats)
                {
                    var cb = new CheckBox { Text = c.ToString(), ButtonPressed = (z.Cats & c) != 0, FocusMode = FocusModeEnum.None };
                    cb.AddThemeFontSizeOverride("font_size", 13);
                    cb.Toggled += on => { z.Cats = on ? z.Cats | c : z.Cats & ~c; F.RefreshStored(); };
                    grid.AddChild(cb);
                }
                v.AddChild(grid);
                HBoxContainer h = UITheme.H(6);
                h.AddChild(UITheme.B("All", () => { z.Cats = StockCat.All; F.RefreshStored(); RefreshInfo(true); }, 13));
                h.AddChild(UITheme.B("None", () => { z.Cats = StockCat.None; F.RefreshStored(); RefreshInfo(true); }, 13));
                v.AddChild(h);
                break;
            case ZoneKind.Farm:
                bool outside = F.World.IsOutside(F.World.Idx(z.X0, z.Y0, z.Z));
                int planted = z.Cells().Count(p => F.Crops.TryGetValue(F.World.Idx(p), out Crop c) && c.Planted <= F.Tick);
                int soil = z.Cells().Count(p => F.Farmable(F.World.Idx(p), Tables.M(z.Crop ?? "dimcap")) || F.Crops.ContainsKey(F.World.Idx(p)));
                v.AddChild(UITheme.L($"{planted} of {soil} soil cells planted with {Tables.M(z.Crop).Name}.", 14, UITheme.Text));
                int seeds = F.Items.Values.Count(i => i.Kind == ItemKind.Seed && i.M.Key == z.Crop);
                v.AddChild(UITheme.L($"{seeds} {Tables.M(z.Crop).Name} seeds in store.", 14, seeds > 0 ? UITheme.Text : UITheme.Ember));
                v.AddChild(UITheme.L(outside ? "Under the sky: wheat and brambles." : "In the dark: dimcaps and glowmoss.", 13, UITheme.Dim));
                foreach (Mat m in Tables.Mats.Where(m => m.Grow > 0 && (outside ? m.Surface : m.Underground)))
                {
                    Mat mm = m;
                    Button b = UITheme.B((z.Crop == m.Key ? "> " : "") + m.Name, () => { z.Crop = mm.Key; RefreshInfo(true); }, 14);
                    v.AddChild(b);
                }
                break;
            case ZoneKind.Meeting:
                v.AddChild(UITheme.Wrap("Idle dwarves wander here and talk. Friendships form -- and feuds, between clans with old grudges.", 13, UITheme.Dim, 320));
                break;
        }
        v.AddChild(UITheme.B("Remove zone", () => { F.RemoveZone(z); SelectThing(null); }, 14));
    }

    private void CellInfo(VBoxContainer v, P3 p)
    {
        int c = F.World.Idx(p);
        World w = F.World;
        if (w.Has(c, CellFlag.Hidden)) { v.AddChild(UITheme.Heading("Unexplored", 18)); v.AddChild(UITheme.L("Dig to find out.", 14, UITheme.Dim)); return; }
        Shape s = w.Shapes[c];
        Mat m = Tables.Mats[w.Mats[c]];
        string what = s switch
        {
            Shape.Wall => $"{m.Name} {(w.Has(c, CellFlag.Built) ? "wall (built)" : m.Kind == MatKind.Ore ? "vein" : m.Kind == MatKind.Gem ? "cluster" : "wall")}",
            Shape.Floor => w.Has(c, CellFlag.Grass) && w.IsOutside(c) ? "grass" : $"{m.Name} floor",
            Shape.Empty => "open air",
            Shape.Tree => $"{m.Name} tree",
            Shape.Water => "water",
            Shape.Magma => "magma",
            Shape.StairUD => "up/down stairs",
            Shape.StairUp => "up stairs",
            Shape.StairDown => "down stairs",
            Shape.Ramp => "ramp",
            _ => s.ToString(),
        };
        v.AddChild(UITheme.Heading(char.ToUpper(what[0]) + what[1..], 18));
        v.AddChild(UITheme.L($"{p.X}, {p.Y} at {DepthName(p.Z)}{(w.IsOutside(c) ? ", under the sky" : "")}", 13, UITheme.Dim));
        if (w.Has(c, CellFlag.Shrub)) v.AddChild(UITheme.L(w.IsOutside(c) ? "A bramble bush grows here." : "Glowmoss grows here.", 14, UITheme.Text));
        if (w.Desigs[c] != Desig.None) v.AddChild(UITheme.L($"Designated: {w.Desigs[c]}", 14, UITheme.Gold));
        foreach (Item it in F.ItemsOn(c).Take(10)) v.AddChild(UITheme.L("· " + it.Name + (it.Stored ? "" : ""), 14, UITheme.Text));
    }
}

/// <summary>A screen over the map: a shade, a framed panel, a title, a close button.</summary>
public abstract partial class Overlay : Control
{
    protected readonly FortressScreen S;
    protected Fortress F => S.F;
    protected VBoxContainer Body;
    private string _sig;
    private double _t;

    protected Overlay(FortressScreen s, string title, Vector2 size)
    {
        S = s;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.5f) };
        AddChild(shade);
        FortressScreen.Anchor(shade, 0, 0, 1, 1, 0, 0, 0, 0);
        var center = new CenterContainer();
        AddChild(center);
        FortressScreen.Anchor(center, 0, 0, 1, 1, 0, 0, 0, 0);
        var outer = UITheme.V(8);
        var panel = UITheme.Panel(outer);
        center.AddChild(panel);
        HBoxContainer top = UITheme.H(10);
        Label t = UITheme.Heading(title, 24);
        t.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(t);
        top.AddChild(UITheme.B("Close", () => S.CloseOverlay(), 14, "Esc"));
        outer.AddChild(top);
        var scroll = new ScrollContainer { CustomMinimumSize = size, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        outer.AddChild(scroll);
        Body = UITheme.V(4);
        Body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(Body);
    }

    public override void _Ready()
    {
        FortressScreen.Anchor(this, 0, 0, 1, 1, 0, 0, 0, 0);
        Rebuild();
    }

    public override void _Process(double delta)
    {
        _t -= delta;
        if (_t > 0) return;
        _t = 0.5;
        if (Signature() != _sig) Rebuild();
    }

    private void Rebuild()
    {
        _sig = Signature();
        foreach (Node n in Body.GetChildren()) n.QueueFree();
        Fill();
    }

    protected abstract string Signature();
    protected abstract void Fill();
}

public partial class DwarfList : Overlay
{
    public DwarfList(FortressScreen s) : base(s, "Dwarves", new Vector2(860, 560)) { }

    protected override string Signature() => string.Join(";", F.Dwarves.Select(d => $"{d.Id}{d.Activity}{d.MoodWord}{d.Dead}"));

    protected override void Fill()
    {
        foreach (Dwarf d in F.Dwarves.OrderBy(d => d.Dead).ThenBy(d => d.Name))
        {
            HBoxContainer h = UITheme.H(10);
            Button name = UITheme.B(d.Name, () => { S.CloseOverlay(); S.SelectThing(d); S.Jump(F.World.Pos(d.Cell)); }, 14);
            name.CustomMinimumSize = new Vector2(220, 0);
            name.Alignment = HorizontalAlignment.Left;
            h.AddChild(name);
            Labor best = Tables.AllLabors.OrderByDescending(d.Skill).First();
            Label prof = UITheme.L(d.Skill(best) > 0 ? $"{Tables.LaborName(best)} {d.Skill(best)}" : "Peasant", 14, UITheme.Dim);
            prof.CustomMinimumSize = new Vector2(150, 0);
            h.AddChild(prof);
            Label act = UITheme.L(d.Dead ? $"Dead ({d.DeathCause})" : d.Activity, 14, d.Dead ? UITheme.Bad : UITheme.Text);
            act.CustomMinimumSize = new Vector2(300, 0);
            h.AddChild(act);
            if (!d.Dead)
                h.AddChild(UITheme.L(d.MoodWord, 14, d.Mood >= 10 ? UITheme.Good : d.Mood >= 0 ? UITheme.Text : d.Mood >= -15 ? UITheme.Ember : UITheme.Bad));
            Body.AddChild(h);
        }
    }
}

public partial class LaborGrid : Overlay
{
    public LaborGrid(FortressScreen s) : base(s, "Labors", new Vector2(1180, 580)) { }

    private static readonly Labor[] Shown = Tables.AllLabors.Where(l => l != Labor.Fighting).ToArray();

    protected override string Signature() => string.Join(";", F.Living.Select(d => d.Id + string.Join("", d.Labors.Select(b => b ? '1' : '0'))));

    protected override void Fill()
    {
        Body.AddChild(UITheme.L("Tick what each dwarf may do. Numbers are skill levels; skilled dwarves prefer their crafts.", 13, UITheme.Dim));
        var grid = new GridContainer { Columns = Shown.Length + 1 };
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddChild(UITheme.L("", 12));
        foreach (Labor l in Shown)
        {
            Label h = UITheme.L(Tables.LaborName(l)[..Math.Min(6, Tables.LaborName(l).Length)], 12, UITheme.Bronze, UITheme.LabelFont, HorizontalAlignment.Center);
            h.CustomMinimumSize = new Vector2(62, 0);
            h.TooltipText = Tables.LaborName(l);
            grid.AddChild(h);
        }
        foreach (Dwarf d in F.Living.OrderBy(d => d.Name))
        {
            Label n = UITheme.L(d.Name, 13, UITheme.Text);
            n.CustomMinimumSize = new Vector2(190, 0);
            grid.AddChild(n);
            foreach (Labor l in Shown)
            {
                int sk = d.Skill(l);
                var cb = new CheckBox { Text = sk > 0 ? sk.ToString() : "", ButtonPressed = d.Can(l), FocusMode = FocusModeEnum.None, TooltipText = $"{d.FirstName}: {Tables.LaborName(l)}" };
                cb.AddThemeFontSizeOverride("font_size", 12);
                cb.AddThemeColorOverride("font_color", sk >= 5 ? UITheme.Gold : UITheme.Dim);
                Dwarf dd = d;
                Labor ll = l;
                cb.Toggled += on => dd.Labors[(int)ll] = on;
                grid.AddChild(cb);
            }
        }
        Body.AddChild(grid);
    }
}

public partial class Stocks : Overlay
{
    public Stocks(FortressScreen s) : base(s, "Stocks", new Vector2(760, 560)) { }

    protected override string Signature() => $"{F.Items.Count}:{F.Tick / 500}";

    protected override void Fill()
    {
        var groups = F.Items.Values
            .GroupBy(i => i.Def.Cat).OrderBy(g => g.Key);
        foreach (var g in groups)
        {
            Body.AddChild(UITheme.L($"{g.Key}  ({g.Count()})", 15, UITheme.Bronze, UITheme.Title));
            var names = g.GroupBy(i => i.Name).OrderByDescending(x => x.Count()).Select(x => $"{x.Count()} {x.Key}");
            Body.AddChild(UITheme.Wrap(string.Join(",  ", names), 14, UITheme.Text, 720));
        }
        Body.AddChild(UITheme.L($"Total wealth: {F.Wealth}", 15, UITheme.Gold));
    }
}

public partial class HelpPanel : Overlay
{
    public HelpPanel(FortressScreen s) : base(s, "How to run a fortress", new Vector2(820, 560)) { }

    protected override string Signature() => "help";

    private void Section(string head, string text)
    {
        Body.AddChild(UITheme.L(head, 16, UITheme.Bronze, UITheme.Title));
        Body.AddChild(UITheme.Wrap(text, 14, UITheme.Text, 790));
    }

    protected override void Fill()
    {
        Section("You do not command dwarves",
            "You say what should be done -- dig here, build that, stockpile there, make five beds -- and the dwarves choose their own tasks by skill, labor and whim. Hungry, thirsty or tired dwarves see to themselves first.");
        Section("Moving around",
            "Arrow keys or middle-drag pan, the wheel zooms. < and > (or Page Up/Down, or Shift+wheel) go up and down a level. Open air shows the levels below it, dimmer the deeper they are. C centres on the wagon; Tab shows names.");
        Section("Time",
            "Space pauses; 1, 2 and 3 run at normal, fast and fastest. You can give orders while paused. Alerts (deaths, caverns, tantrums...) pause the game.");
        Section("Digging",
            "D designates digging: drag a box. To go down, dig a shaft: J (down stair) on top, X (up/down stairs) through the middle levels -- change level while dragging to make a box several levels deep -- and U at the bottom. H channels: it removes a floor and leaves a ramp below. Miners need a pick; the wagon brought three.");
        Section("Getting started",
            "1. Dig a staircase down and a hall.  2. P: lay a stockpile -- haulers will tidy the wagon's load into it.  3. B: build a carpenter's and a mason's shop, then click them and order beds, tables, chairs.  4. Place the beds (B, then b) and tables and chairs (t, h) side by side.  5. F: a farm plot on soil -- underground for dimcaps, outside for wheat -- and a brewery to turn the harvest into ale. Dwarves without ale grow miserable.");
        Section("Industry",
            "Ore (malachite, cassiterite, hematite...) goes to the smelter with fuel -- coal, or charcoal from the wood furnace -- and comes out as bars. The forge turns copper, bronze or iron bars into picks, axes, weapons and armour. Gems are cut at the craftsdwarf's shop.");
        Section("Moods",
            "Each dwarf has thoughts that come and go: good meals and ale, beds and tables, friends; bad ones like water to drink, sleeping on rock, seeing death, feuding clans. A long run of misery builds stress, and stress breaks out in tantrums.");
        Section("Ironman",
            "One save per slot, written every season and when you leave. If the last dwarf dies, the fortress has fallen -- and that, too, is a story.");
    }
}

public partial class FallenPanel : Overlay
{
    public FallenPanel(FortressScreen s) : base(s, $"{s.F.Name} has fallen", new Vector2(640, 360)) { }

    protected override string Signature() => "fallen";

    protected override void Fill()
    {
        string St(string k) => F.Stats.GetValueOrDefault(k).ToString();
        Body.AddChild(UITheme.Wrap(F.FallenWhy ?? "", 18, UITheme.Text, 600));
        Body.AddChild(UITheme.L($"It stood until {F.Date}.", 16, UITheme.Dim));
        Body.AddChild(UITheme.Wrap($"{F.Dwarves.Count} dwarves called it home. {St("deaths")} died. They dug {St("dug")} cells of earth and stone, felled {St("trees")} trees, made {St("crafted")} things and brought in {St("harvested")} harvests.", 15, UITheme.Text, 600));
        foreach (LogEntry e in F.Log.Where(l => l.Kind == LogKind.Alert).TakeLast(5)) Body.AddChild(UITheme.L("· " + e.Text, 14, UITheme.Ember));
        Body.AddChild(UITheme.B("Return to the title", () => S.Root.ShowTitle(), 18));
    }
}
