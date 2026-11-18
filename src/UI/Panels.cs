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
                return $"b{b.Id}:{b.Built}:{b.Parts.Count}:{b.Owner}:{b.Builder}:{b.Raised}:{b.Locked}:{string.Join(",", F.Caravans.Select(c => c.State))}:{string.Join(";", b.Orders.Select(o => $"{o.Recipe},{o.Left},{o.Repeat},{o.Worker},{o.Stuck}"))}";
            case Zone z:
                return $"z{z.Id}:{z.Cats}:{z.Crop}:{F.Zones.Contains(z)}:{(int)(F.Tick / 100)}";
            case Creature cr:
                return $"cr{cr.Id}:{cr.Hp}:{cr.Dead}:{cr.Angry}:{cr.Target}:{cr.Tame}:{cr.War}:{cr.MarkSlaughter}:{cr.MarkTame}:{cr.MarkTrain}:{cr.Claim}:{cr.Hunger / 1000}";
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
            case Creature cr when !cr.Dead && F.Creatures.Contains(cr): CreatureInfo(v, cr); return true;
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
        HBoxContainer hp = UITheme.H(6);
        hp.AddChild(UITheme.Bar(d.Hp / (float)Tables.MaxHp, d.Hp > 70 ? UITheme.Good : d.Hp > 40 ? UITheme.Ember : UITheme.Bad, 140, 9));
        hp.AddChild(UITheme.L(d.Hp >= Tables.MaxHp ? "unhurt" : d.Wounded ? "badly hurt" : "bruised", 13, d.Wounded ? UITheme.Bad : UITheme.Dim));
        v.AddChild(Row("Health", hp));
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
        Squad sq = F.SquadOf(d);
        var kit = new[] { F.WeaponOf(d), Gear(d.Armor), Gear(d.Helm), Gear(d.Shield) }.Where(i => i != null).Select(i => i.Name).ToList();
        if (sq != null || kit.Count > 0 || d.Kills > 0)
            v.AddChild(Row("Arms", UITheme.Wrap((sq != null ? $"{sq.Name} ({OrderName(sq.Order)}). " : "") + (kit.Count > 0 ? string.Join(", ", kit) : "unarmed") + (d.Kills > 0 ? $"; {d.Kills} kills" : ""), 14, UITheme.Text, 240)));
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

    private Item Gear(int id) => id >= 0 && F.Items.TryGetValue(id, out Item it) ? it : null;

    public static string OrderName(SquadOrder o) => o switch
    {
        SquadOrder.OffDuty => "off duty",
        SquadOrder.Station => "holding a station",
        SquadOrder.Defend => "defending a station",
        _ => "charging",
    };

    private void CreatureInfo(VBoxContainer v, Creature c)
    {
        CreatureDef d = c.D;
        v.AddChild(UITheme.Heading(c.Title.ToUpperFirst(), 20));
        if (c.Tame) { AnimalInfo(v, c); return; }
        v.AddChild(UITheme.L(c.Raider ? "A grimling raider, come for loot and blood." : d.Hostile ? "A beast of the deep. It hunts dwarves." : c.Angry ? "Provoked, and furious." : d.Timid ? "Shy; it bolts from dwarves." : "Wild, and best left alone.", 14, d.Hostile || c.Angry ? UITheme.Ember : UITheme.Dim));
        HBoxContainer hp = UITheme.H(6);
        hp.AddChild(UITheme.Bar(c.Hp / (float)d.Hp, UITheme.Bad, 140, 9));
        hp.AddChild(UITheme.L($"{c.Hp}/{d.Hp}", 13, UITheme.Dim));
        v.AddChild(Row("Health", hp));
        Dwarf t = c.Target >= 0 ? F.DwarfById(c.Target) : null;
        if (t != null) v.AddChild(Row("After", UITheme.L(t.Name, 14, UITheme.Bad)));
        if (d.Bashes) v.AddChild(UITheme.L("Strong enough to smash doors.", 13, UITheme.Ember));
        if (d.Tamable && !d.Hostile)
        {
            v.AddChild(UITheme.L(c.MarkTame ? (c.Claim >= 0 ? $"{F.DwarfById(c.Claim)?.FirstName} is trying to tame it." : "Marked for taming.") : "It could be tamed by someone good with animals.", 14, UITheme.Text));
            v.AddChild(UITheme.B(c.MarkTame ? "Leave it be" : "Tame it", () => { F.MarkTame(c, !c.MarkTame); RefreshInfo(true); }, 14));
        }
    }

    private void AnimalInfo(VBoxContainer v, Creature c)
    {
        CreatureDef d = c.D;
        v.AddChild(UITheme.L($"{(c.Female ? "Female" : "Male")}, {(c.Adult(F.Tick) ? "grown" : "young")}{(c.War ? ", trained for war" : "")}.", 14, UITheme.Text));
        HBoxContainer hp = UITheme.H(6);
        hp.AddChild(UITheme.Bar(c.Hp / (float)d.Hp, c.Hp * 2 < d.Hp ? UITheme.Bad : UITheme.Good, 140, 9));
        v.AddChild(Row("Health", hp));
        if (d.Grazer)
        {
            Zone p = F.PastureFor(c);
            v.AddChild(Row("Grazing", UITheme.L(c.Hunger > Fortress.GrazerStarve * 2 / 3 ? "starving -- needs grass!" : c.Hunger > 3000 ? "hungry" : "well fed", 14, c.Hunger > 3000 ? UITheme.Ember : UITheme.Text)));
            v.AddChild(Row("Pasture", UITheme.L(p?.Name ?? "none -- roams by the wagon", 14, p == null ? UITheme.Dim : UITheme.Text)));
        }
        if (d.Layer) v.AddChild(UITheme.L(c.Female && c.Adult(F.Tick) ? "Lays eggs." : "Lays no eggs.", 13, UITheme.Dim));
        if (c.Claim >= 0) v.AddChild(UITheme.L($"{F.DwarfById(c.Claim)?.FirstName} is seeing to it.", 13, UITheme.Dim));
        HBoxContainer h = UITheme.H(6);
        h.AddChild(UITheme.B(c.MarkSlaughter ? "Spare it" : "Slaughter", () => { F.MarkSlaughter(c, !c.MarkSlaughter); RefreshInfo(true); }, 14, "A butcher will kill it; the butcher's shop turns the carcass into meat, hide and bone."));
        if (d.Trainable && !c.War) h.AddChild(UITheme.B(c.MarkTrain ? "Stop training" : "Train for war", () => { F.MarkTrain(c, !c.MarkTrain); RefreshInfo(true); }, 14, "War beasts guard the squad's station and attack foes."));
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
        if (d.Id == "door")
        {
            v.AddChild(UITheme.L(b.Locked ? "Locked: nobody passes." : "Dwarves pass; beasts and raiders do not (brutes may batter it down).", 14, UITheme.Text));
            v.AddChild(UITheme.B(b.Locked ? "Unlock" : "Lock", () => { F.SetLock(b, !b.Locked); RefreshInfo(true); }, 14));
        }
        if (d.Id == "bridge")
        {
            v.AddChild(UITheme.L(b.Raised ? (b.OverAir ? "Raised: the gap is open." : "Raised: the way is sealed.") : "Lowered: the way is open.", 14, b.Raised ? UITheme.Ember : UITheme.Text));
            v.AddChild(UITheme.B(b.Raised ? "Lower it" : "Raise it", () => { F.SetBridge(b, !b.Raised); RefreshInfo(true); }, 14));
        }
        if (d.Id == "coffin")
        {
            Dwarf dead = b.Owner >= 0 ? F.DwarfById(b.Owner) : null;
            v.AddChild(UITheme.L(dead != null ? $"Here lies {dead.Name}, {dead.DeathCause}." : "Empty, waiting.", 14, UITheme.Text));
        }
        if (d.Id == "trap") v.AddChild(UITheme.L(F.Tick >= b.ArmedAt ? "Armed." : $"Resetting ({(b.ArmedAt - F.Tick) / 10 + 1}%...)", 14, F.Tick >= b.ArmedAt ? UITheme.Good : UITheme.Ember));
        if (d.Id == "depot")
        {
            Caravan c = F.Caravans.FirstOrDefault(x => x.Depot == b.Id && x.State == CaravanState.Trading);
            Caravan coming = F.Caravans.FirstOrDefault(x => x.State == CaravanState.Coming);
            foreach (FactionDef fd in Fortress.FactionDefs)
                v.AddChild(UITheme.Wrap($"{fd.Name.ToUpperFirst()}: standing {F.Standing.GetValueOrDefault(fd.Key)}, they come in {Tables.Seasons[fd.Season]}", 13, UITheme.Dim, 320));
            if (c != null)
            {
                v.AddChild(UITheme.Wrap($"The merchants of {c.F.Name} are here for {Math.Max(0, (c.Leave - F.Tick) / Tables.TicksPerDay) + 1} more days.", 14, UITheme.Good, 320));
                v.AddChild(UITheme.B("Trade (Y)", () => Open(new TradePanel(this, c)), 16));
            }
            else if (coming != null) v.AddChild(UITheme.Wrap($"A caravan from {coming.F.Name} is on its way.", 14, UITheme.Text, 320));
            else v.AddChild(UITheme.L("No caravan at present.", 14, UITheme.Faint));
        }
        else if (d.Kind == BuildKind.Workshop) WorkshopOrders(v, b);
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
            case ZoneKind.Pasture:
                int herd = F.Creatures.Count(c => !c.Dead && c.Tame && c.D.Grazer && F.PastureFor(c) == z);
                int grassCells = z.Cells().Count(p => F.World.Has(F.World.Idx(p), CellFlag.Grass) && F.World.IsOutside(F.World.Idx(p)));
                v.AddChild(UITheme.L($"{herd} grazing animals, {grassCells} cells of grass.", 14, UITheme.Text));
                v.AddChild(UITheme.Wrap("Grazers keep to the nearest pasture. Snow in Deepfrost leaves thin grazing.", 13, UITheme.Dim, 320));
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

    protected void Rebuild()
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

public partial class MilitaryPanel : Overlay
{
    public MilitaryPanel(FortressScreen s) : base(s, "Military", new Vector2(900, 560)) { }

    protected override string Signature() => $"{F.Alarm}:{F.Buildings.Count(b => b.Def == "bridge" && b.Raised)}:" + string.Join(";", F.Squads.Select(q => $"{q.Id}{q.Order}{q.Station}{string.Join(",", q.Members)}")) + ":" + string.Join(",", F.Living.Select(d => $"{d.Weapon}{d.Armor}{d.Hp / 25}"));

    protected override void Fill()
    {
        HBoxContainer top = UITheme.H(10);
        top.AddChild(UITheme.B(F.Alarm ? "Lift the alarm" : "Sound the alarm", () => S.ToggleAlarm(), 15, "Civilians drop everything and shelter in the meeting hall."));
        top.AddChild(UITheme.B("Raise a new squad", () => F.NewSquad(), 15));
        if (F.Buildings.Any(b => b.Def == "bridge" && b.Built))
        {
            bool up = F.Buildings.Any(b => b.Def == "bridge" && b.Built && b.Raised);
            top.AddChild(UITheme.B(up ? "Lower the drawbridges" : "Raise the drawbridges", () => F.SetAllBridges(!up), 15));
        }
        int foes = F.Hostiles.Count();
        top.AddChild(UITheme.L(foes == 0 ? "No foes in sight." : $"{foes} foes about.", 15, foes == 0 ? UITheme.Dim : UITheme.Bad));
        Body.AddChild(top);
        Body.AddChild(UITheme.Wrap("Soldiers on duty fetch weapons, armour, helms and shields from the stores. Station: stand at a place. Defend: also fight whatever comes near it. Charge: hunt every foe that can be reached. The station is set to where the camera looks.", 13, UITheme.Dim, 860));
        foreach (Squad q in F.Squads.ToList())
        {
            var v = UITheme.V(4);
            Body.AddChild(UITheme.Panel(v, UITheme.PanelFill2));
            HBoxContainer h = UITheme.H(8);
            Label name = UITheme.Heading(q.Name, 18);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            h.AddChild(name);
            h.AddChild(UITheme.L(FortressScreen.OrderName(q.Order) + (q.Order != SquadOrder.OffDuty ? $" at {q.Station.X},{q.Station.Y} ({S.DepthName(q.Station.Z)})" : ""), 14, UITheme.Text));
            v.AddChild(h);
            HBoxContainer orders = UITheme.H(6);
            orders.AddChild(UITheme.B("Off duty", () => F.SetOrder(q, SquadOrder.OffDuty), 13));
            orders.AddChild(UITheme.B("Station here", () => F.SetOrder(q, SquadOrder.Station, CameraCell()), 13));
            orders.AddChild(UITheme.B("Defend here", () => F.SetOrder(q, SquadOrder.Defend, CameraCell()), 13));
            orders.AddChild(UITheme.B("Charge!", () => F.SetOrder(q, SquadOrder.Charge), 13));
            orders.AddChild(UITheme.B("Disband", () => F.Disband(q), 13));
            v.AddChild(orders);
            foreach (int id in q.Members.ToList())
            {
                Dwarf d = F.DwarfById(id);
                if (d == null) continue;
                HBoxContainer m = UITheme.H(8);
                Label n = UITheme.L(d.Name, 14, UITheme.Text);
                n.CustomMinimumSize = new Vector2(220, 0);
                m.AddChild(n);
                m.AddChild(UITheme.L($"fighting {d.Skill(Labor.Fighting)}", 13, UITheme.Dim));
                m.AddChild(UITheme.Bar(d.Hp / (float)Tables.MaxHp, d.Wounded ? UITheme.Bad : UITheme.Good, 60, 8));
                string kit = (F.WeaponOf(d)?.Name ?? "unarmed") + (d.Armor >= 0 ? ", armoured" : "");
                Label k = UITheme.L(kit, 13, UITheme.Dim);
                k.CustomMinimumSize = new Vector2(260, 0);
                m.AddChild(k);
                m.AddChild(UITheme.B("Dismiss", () => F.Discharge(d), 12));
                v.AddChild(m);
            }
            if (q.Members.Count < 10)
            {
                HBoxContainer add = UITheme.H(4);
                add.AddChild(UITheme.L("Enlist:", 13, UITheme.Bronze));
                foreach (Dwarf d in F.Living.Where(d => d.Squad < 0).OrderByDescending(d => d.Skill(Labor.Fighting) * 2 + (d.Clan == "stoneward" ? 3 : 0)).Take(6))
                    add.AddChild(UITheme.B(d.FirstName, () => F.Enlist(q, d), 12, $"{d.Name}, fighting {d.Skill(Labor.Fighting)}"));
                v.AddChild(add);
            }
        }
        if (F.Squads.Count == 0) Body.AddChild(UITheme.L("No squads yet. Raise one, and enlist the stout of heart.", 15, UITheme.Faint));
    }

    private P3 CameraCell()
    {
        Vector2 c = S.Map.Cam.Position / MapView.T;
        return new P3((int)c.X, (int)c.Y, S.Map.Z);
    }
}

public partial class TradePanel : Overlay
{
    private readonly Caravan _c;
    private readonly Fortress.Deal _deal = new();
    private string _verdict = "";

    public TradePanel(FortressScreen s, Caravan c) : base(s, $"Trade with {c.F.Name}", new Vector2(1180, 600)) { _c = c; }

    protected override string Signature() => $"{_c.State}:{_c.Goods.Count}:{F.Items.Count}:{_deal.Offer.Count}:{string.Join(",", _deal.Ask.Values)}:{_verdict}";

    private sealed record Lot(string Name, List<Item> Items);

    private List<Lot> OurLots() => F.Tradeable().GroupBy(i => i.Name)
        .OrderByDescending(g => (_c.F.Wants & g.First().Def.Cat) != 0).ThenByDescending(g => g.Max(i => i.Value))
        .Select(g => new Lot(g.Key, g.OrderByDescending(i => i.Value).ToList())).ToList();

    protected override void Fill()
    {
        if (_c.State != CaravanState.Trading) { Body.AddChild(UITheme.L("The merchants have packed up.", 16, UITheme.Ember)); return; }
        int offer = F.OfferValue(_c, _deal), ask = F.AskValue(_c, _deal);
        HBoxContainer top = UITheme.H(16);
        top.AddChild(UITheme.L($"You offer {offer}", 18, UITheme.Gold, UITheme.Title));
        top.AddChild(UITheme.L($"You ask {ask}", 18, UITheme.Gold, UITheme.Title));
        top.AddChild(UITheme.L(offer >= ask ? "" : $"(short by {ask - offer})", 15, UITheme.Ember));
        top.AddChild(UITheme.B("Propose the deal", Propose, 16));
        top.AddChild(UITheme.B("Clear", () => { _deal.Offer.Clear(); _deal.Ask.Clear(); _verdict = ""; Rebuild(); }, 14));
        Body.AddChild(top);
        Body.AddChild(UITheme.L(_verdict, 15, _verdict.StartsWith("\"") ? UITheme.Ember : UITheme.Good));
        Body.AddChild(UITheme.Wrap($"They pay half again for {WantsText(_c.F.Wants)}; for the rest a little under worth. Their own goods cost {(int)((F.Markup(_c.Faction) - 1) * 100)}% over worth -- less, the better they think of you (standing {F.Standing.GetValueOrDefault(_c.Faction)}). Goods change hands at the depot; haulers will carry yours in.", 13, UITheme.Dim, 1140));
        HBoxContainer cols = UITheme.H(20);
        VBoxContainer ours = UITheme.V(2), theirs = UITheme.V(2);
        ours.CustomMinimumSize = new Vector2(560, 0);
        theirs.CustomMinimumSize = new Vector2(560, 0);
        cols.AddChild(ours);
        cols.AddChild(theirs);
        Body.AddChild(cols);
        ours.AddChild(UITheme.L("Your goods", 16, UITheme.Bronze, UITheme.Title));
        foreach (Lot lot in OurLots().Take(60))
        {
            int chosen = lot.Items.Count(i => _deal.Offer.Contains(i));
            int each = F.OurValue(_c, lot.Items[0]);
            ours.AddChild(LotRow(lot.Name, chosen, lot.Items.Count, each,
                () => { Item next = lot.Items.FirstOrDefault(i => !_deal.Offer.Contains(i)); if (next != null) _deal.Offer.Add(next); },
                () => { Item last = lot.Items.LastOrDefault(i => _deal.Offer.Contains(i)); if (last != null) _deal.Offer.Remove(last); },
                () => { foreach (Item i in lot.Items) if (!_deal.Offer.Contains(i)) _deal.Offer.Add(i); }));
        }
        theirs.AddChild(UITheme.L("Their wares", 16, UITheme.Bronze, UITheme.Title));
        foreach (Good g in _c.Goods.ToList())
        {
            int chosen = _deal.Ask.GetValueOrDefault(g);
            theirs.AddChild(LotRow(g.Name + (g.Creature != null ? " (live)" : ""), chosen, g.Count, F.TheirPrice(_c, g),
                () => { if (_deal.Ask.GetValueOrDefault(g) < g.Count) _deal.Ask[g] = _deal.Ask.GetValueOrDefault(g) + 1; },
                () => { if (_deal.Ask.GetValueOrDefault(g) > 0) _deal.Ask[g]--; if (_deal.Ask.GetValueOrDefault(g) == 0) _deal.Ask.Remove(g); },
                () => _deal.Ask[g] = g.Count));
        }
    }

    private static string WantsText(StockCat c) => string.Join(", ", Enum.GetValues<StockCat>().Where(x => x != StockCat.None && x != StockCat.All && (c & x) != 0).Select(x => x.ToString().ToLower()));

    private Control LotRow(string name, int chosen, int total, int each, Action more, Action less, Action all)
    {
        HBoxContainer h = UITheme.H(4);
        h.AddChild(UITheme.B("-", () => { less(); _verdict = ""; Rebuild(); }, 12));
        Label n = UITheme.L($"{chosen}/{total}", 13, chosen > 0 ? UITheme.Gold : UITheme.Dim, UITheme.Mono);
        n.CustomMinimumSize = new Vector2(56, 0);
        h.AddChild(n);
        h.AddChild(UITheme.B("+", () => { more(); _verdict = ""; Rebuild(); }, 12));
        h.AddChild(UITheme.B("all", () => { all(); _verdict = ""; Rebuild(); }, 11));
        Label l = UITheme.L(name, 14, UITheme.Text);
        l.CustomMinimumSize = new Vector2(300, 0);
        l.ClipText = true;
        h.AddChild(l);
        h.AddChild(UITheme.L($"{each} each", 13, UITheme.Dim, UITheme.Mono));
        return h;
    }

    private void Propose()
    {
        var (ok, why) = F.Weigh(_c, _deal);
        if (ok && F.Trade(_c, _deal)) { _deal.Offer.Clear(); _deal.Ask.Clear(); Sfx.Good(); }
        else Sfx.Deny();
        _verdict = why;
        Rebuild();
    }
}

public partial class AnimalsPanel : Overlay
{
    public AnimalsPanel(FortressScreen s) : base(s, "Animals", new Vector2(860, 560)) { }

    protected override string Signature() => string.Join(";", F.Creatures.Where(c => c.Tame || c.D.Tamable).Select(c => $"{c.Id}{c.MarkSlaughter}{c.MarkTame}{c.MarkTrain}{c.Tame}{c.War}{c.Hunger / 2000}"));

    protected override void Fill()
    {
        var tame = F.Creatures.Where(c => !c.Dead && c.Tame).OrderBy(c => c.Def).ThenBy(c => c.Born).ToList();
        Body.AddChild(UITheme.L($"The herds ({tame.Count})", 16, UITheme.Bronze, UITheme.Title));
        foreach (var g in tame.GroupBy(c => c.Def))
            Body.AddChild(UITheme.L($"{g.Count()} {Tables.Creatures[g.Key].Name}s: {g.Count(c => c.Female)} female, {g.Count(c => !c.Adult(F.Tick))} young", 14, UITheme.Dim));
        foreach (Creature c in tame) Body.AddChild(RowFor(c));
        var wild = F.Creatures.Where(c => !c.Dead && !c.Tame && c.D.Tamable && !c.D.Hostile).OrderBy(c => F.World.Pos(c.Cell).Dist(F.Embark)).Take(12).ToList();
        Body.AddChild(UITheme.L($"Wild beasts that could be tamed ({wild.Count})", 16, UITheme.Bronze, UITheme.Title));
        foreach (Creature c in wild) Body.AddChild(RowFor(c));
        Body.AddChild(UITheme.Wrap("Animal carers tame and train; butchers slaughter. Carcasses -- from slaughter or the hunt -- go to the butcher's shop for meat, hide and bone; the tanner makes leather, the leatherworks armour and caps.", 13, UITheme.Dim, 820));
    }

    private Control RowFor(Creature c)
    {
        HBoxContainer h = UITheme.H(8);
        Button name = UITheme.B(c.Title, () => { S.CloseOverlay(); S.SelectThing(c); S.Jump(F.World.Pos(c.Cell)); }, 13);
        name.Alignment = HorizontalAlignment.Left;
        name.CustomMinimumSize = new Vector2(200, 0);
        h.AddChild(name);
        string state = c.Tame ? $"{(c.Female ? "female" : "male")}{(c.Adult(F.Tick) ? "" : ", young")}{(c.War ? ", war beast" : "")}{(c.D.Grazer && c.Hunger > 3000 ? ", hungry" : "")}" : c.Angry ? "angry" : "wild";
        Label st = UITheme.L(state, 13, UITheme.Text);
        st.CustomMinimumSize = new Vector2(240, 0);
        h.AddChild(st);
        if (c.Tame)
        {
            h.AddChild(UITheme.B(c.MarkSlaughter ? "Spare" : "Slaughter", () => F.MarkSlaughter(c, !c.MarkSlaughter), 12));
            if (c.D.Trainable && !c.War) h.AddChild(UITheme.B(c.MarkTrain ? "Stop training" : "Train for war", () => F.MarkTrain(c, !c.MarkTrain), 12));
        }
        else h.AddChild(UITheme.B(c.MarkTame ? "Leave be" : "Tame", () => F.MarkTame(c, !c.MarkTame), 12));
        return h;
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
        Section("Danger",
            "Breaking into the caverns wakes what lives there; from the first autumn, grimlings raid -- more, and more often, as the hold grows rich. K opens the military: raise a squad, enlist dwarves, and tell it to stand, defend a place, or charge. Soldiers arm themselves from the stores, so keep a forge busy. Doors keep beasts out (brutes batter them down); lock one to keep everyone out. Stonefall traps need a mechanism from the mason. A (or the Alarm button) sends civilians to the meeting hall.");
        Section("Trade",
            "Build a trade depot (B, D). Lowland merchants come each Highsun with food, seed, wood, leather and live goats and hens; the Mountainhome sends dwarves each Leaffall with iron, coal, arms and ale. While they are at the depot, Y (or the depot's Trade button) opens the barter: pick goods on both sides and propose. They pay half again for what they want, and charge more for their own the less they know you; fair dealing and gifts raise your standing, and bigger caravans follow.");
        Section("Animals",
            "The wagon brought goats and hens. Grazers need grass: lay a pasture (E) under the sky. Hens lay eggs. Animals breed in Thaw and Highsun. Z lists them: mark beasts to slaughter, wild fellbuck and badgers to tame, tame badgers to train for war -- war beasts guard the squad's station. A butcher's shop turns carcasses into meat, hides and bones; a tanner makes leather; the leatherworks makes armour and caps from it.");
        Section("The dead",
            "Coffins come from the mason or the carpenter; place them in a quiet tomb and the haulers will bear the dead there. Friends and clanmates grieve while a body lies unburied -- and a dwarf left a whole season becomes a restless spirit that haunts the living until laid to rest. Drawbridges (B, g) need a mechanism: over a channelled gap they drop whatever stands on them when raised; on a floor they seal the way.");
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
