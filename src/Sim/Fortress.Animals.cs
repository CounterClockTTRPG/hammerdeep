using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// Husbandry: the wagon's goats and hens, pastures and grazing, eggs, young,
/// taming wild beasts, training war beasts, slaughter and the carcasses
/// that feed the butcher, the tanner and the leatherworks.
/// </summary>
public sealed partial class Fortress
{
    public const int GrazerStarve = 9000, HerdCap = 14;
    private static readonly string[] BeastNames = { "Grumble", "Tusk", "Mudfoot", "Old Bitey", "Scrag", "Dorn", "Pebble", "Grizzle", "Snout", "Ironjaw", "Bramble", "Soot" };

    /// <summary>The wagon's animals: a pair of goats and a few hens.</summary>
    private void Livestock()
    {
        void Add(string def, bool female)
        {
            Creature c = SpawnCreature(def, FindStandable(Embark, 4));
            c.Tame = true;
            c.Female = female;
            c.Born = Tick - Tables.TicksPerYear * 2;
        }
        Add("goat", true); Add("goat", false);
        Add("hen", true); Add("hen", true); Add("hen", false);
    }

    private IEnumerable<Creature> Tamed(string def) => Creatures.Where(c => !c.Dead && c.Tame && c.Def == def);

    // ---- what tame animals do -----------------------------------------------------------

    private void TameThink(Creature c)
    {
        CreatureDef d = c.D;
        if (c.War) { WarBeastThink(c); return; }
        // On the alarm the herds are driven in with everyone else.
        Zone hall = Alarm ? Zones.FirstOrDefault(z => z.Kind == ZoneKind.Meeting) : null;
        if (hall != null)
        {
            P3 here = World.Pos(c.Cell);
            if (!hall.Contains(here.X, here.Y, here.Z)) { CreatureWalk(c, FindStandable(new P3((hall.X0 + hall.X1) / 2, (hall.Y0 + hall.Y1) / 2, hall.Z), 2), 12000); c.NextThink = Tick + 60; }
            return;
        }
        // Livestock run from danger.
        Creature threat = Creatures.FirstOrDefault(x => !x.Dead && (x.D.Hostile || x.Angry) && World.Pos(x.Cell).Dist(World.Pos(c.Cell)) <= 5);
        if (threat != null)
        {
            P3 me = World.Pos(c.Cell), them = World.Pos(threat.Cell);
            CreatureWalk(c, FindStandable(new P3(Math.Clamp(me.X + Math.Sign(me.X - them.X) * 8, 1, World.W - 2), Math.Clamp(me.Y + Math.Sign(me.Y - them.Y) * 8, 1, World.H - 2), me.Z), 2), 3000);
            return;
        }
        if (d.Layer && c.Female && c.Adult(Tick) && Rng.Chance(0.012) && Items.Values.Count(x => x.Kind == ItemKind.Egg) < 40)
        {
            Spawn(ItemKind.Egg, Tables.Mi("egg"), c.Cell);
            Count("eggs");
        }
        if (c.Claim >= 0 || !Rng.Chance(0.25)) return;
        Zone pasture = d.Grazer ? PastureFor(c) : null;
        if (pasture != null)
        {
            P3 p = new(Rng.Range(pasture.X0, pasture.X1), Rng.Range(pasture.Y0, pasture.Y1), pasture.Z);
            if (World.Walkable(p)) CreatureWalk(c, World.Idx(p), 6000);
            return;
        }
        // No pasture: roam near the wagon.
        P3 at = World.Pos(c.Cell);
        P3 aim = at.Dist(Embark) > 18 ? Embark : at;
        CreatureWalk(c, FindStandable(aim, 6), 3000);
    }

    /// <summary>Every tick, for tame grazers: hunger, and grass to eat where they stand.</summary>
    private void Graze(Creature c)
    {
        c.Hunger++;
        if (World.Has(c.Cell, CellFlag.Grass) && World.IsOutside(c.Cell)) c.Hunger = Math.Max(0, c.Hunger - (Season == 3 ? 2 : 7));
        if (c.Hunger <= GrazerStarve) return;
        c.Dead = true;
        Spawn(ItemKind.Carcass, Tables.Mi("flesh"), c.Cell).Source = c.Def;
        Count("animals_starved");
        Say($"A {c.D.Name} has starved for want of pasture.", LogKind.Bad, c.Cell);
    }

    public Zone PastureFor(Creature c)
    {
        P3 at = World.Pos(c.Cell);
        return Zones.Where(z => z.Kind == ZoneKind.Pasture).OrderBy(z => new P3((z.X0 + z.X1) / 2, (z.Y0 + z.Y1) / 2, z.Z).Dist(at)).FirstOrDefault();
    }

    private void WarBeastThink(Creature c)
    {
        World.EnsureComponents();
        int comp = World.Comp[c.Cell];
        Creature foe = Creatures.Where(x => !x.Dead && !x.Tame && (x.D.Hostile || x.Angry) && World.Comp[x.Cell] == comp && World.Pos(x.Cell).Dist(World.Pos(c.Cell)) <= 12)
            .OrderBy(x => World.Pos(x.Cell).Dist(World.Pos(c.Cell))).FirstOrDefault();
        if (foe != null)
        {
            c.Foe = foe.Id;
            if (Adjacent(c.Cell, foe.Cell)) { if (c.AttackWait <= 0) { BeastHits(c, foe); c.AttackWait = Tables.AttackTicks; } }
            else CreatureWalk(c, foe.Cell, 8000);
            return;
        }
        c.Foe = -1;
        Squad s = Squads.FirstOrDefault(q => q.Order != SquadOrder.OffDuty);
        P3 home = s?.Station ?? Embark;
        if (World.Pos(c.Cell).Dist(home) > 4 || Rng.Chance(0.2)) CreatureWalk(c, FindStandable(home, 3), 8000);
    }

    /// <summary>One beast's blow at another.</summary>
    private void BeastHits(Creature a, Creature b)
    {
        double hit = Math.Clamp(0.6 + 0.05 * (a.D.Skill - b.D.Skill), 0.15, 0.92);
        if (!Rng.Chance(hit)) return;
        int dmg = Math.Max(1, (int)(a.D.Damage * (a.War ? 1.3 : 1) * (0.7 + Rng.Float() * 0.6)));
        b.Hp -= dmg;
        b.Foe = a.Id;
        if (b.Hp > 0) return;
        if (b.Tame) Count("livestock_killed");
        if (a.War || b.Tame) Say($"{a.Title.ToUpperFirst()} has killed {(b.Tame ? b.Title : A(b.D.Name))}!", a.War ? LogKind.Good : LogKind.Bad, b.Cell);
        KillCreature(b, null, null);
        if (a.War) Count("beast_kills");
    }

    /// <summary>A hostile's choice between a dwarf and a beast: war beasts always draw it; raiders go for livestock too.</summary>
    private Creature BeastTarget(Creature c, int withinDist)
    {
        P3 at = World.Pos(c.Cell);
        // War beasts draw a foe from as far as it can see; raiders only go for livestock close by.
        bool dwarfInSight = withinDist <= Tables.SightRange;
        return Creatures.Where(x => !x.Dead && x.Tame && (x.War || (c.Raider && !dwarfInSight)) && World.Pos(x.Cell).Dist(at) < withinDist && World.Pos(x.Cell).Dist(at) <= (x.War ? Tables.SightRange : 6))
            .OrderBy(x => World.Pos(x.Cell).Dist(at)).FirstOrDefault();
    }

    // ---- births ---------------------------------------------------------------------------

    public int Breed()
    {
        int born = 0;
        foreach (string def in Creatures.Where(c => !c.Dead && c.Tame).Select(c => c.Def).Distinct().ToList())
        {
            var herd = Tamed(def).ToList();
            if (!herd.Any(c => !c.Female && c.Adult(Tick))) continue;
            CreatureDef d = Tables.Creatures[def];
            foreach (Creature mother in herd.Where(c => c.Female && c.Adult(Tick)).ToList())
            {
                if (Tamed(def).Count() >= HerdCap || !Rng.Chance(0.5)) continue;
                int n = Rng.Range(1, d.Litter);
                for (int k = 0; k < n; k++)
                {
                    Creature y = SpawnCreature(def, mother.Cell);
                    y.Tame = true;
                    y.Female = Rng.Chance(0.5);
                    y.Born = Tick;
                    born++;
                }
                Say(n == 1 ? $"A {d.Name} has been born." : $"{n} young {d.Name}s have been born.", LogKind.Good, mother.Cell);
            }
        }
        Count("births", born);
        return born;
    }

    // ---- dwarves at work with animals -----------------------------------------------------

    private Task PlanAnimals(Dwarf d)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        Creature pick = null;
        TaskKind kind = TaskKind.Tame;
        foreach (Creature c in Creatures.OrderBy(c => World.Pos(c.Cell).Dist(at)))
        {
            if (c.Dead || c.Claim >= 0 || World.Comp[c.Cell] != comp) continue;
            if (d.Can(Labor.Husbandry) && c.MarkTame && !c.Tame && c.D.Tamable) { pick = c; kind = TaskKind.Tame; break; }
            if (d.Can(Labor.Husbandry) && c.MarkTrain && c.Tame && !c.War && c.D.Trainable) { pick = c; kind = TaskKind.Train; break; }
            if (d.Can(Labor.Butchery) && c.MarkSlaughter && c.Tame) { pick = c; kind = TaskKind.Slaughter; break; }
        }
        if (pick == null) return null;
        pick.Claim = d.Id;
        return new Task { Kind = kind, Item = pick.Id };
    }

    /// <summary>Close on a creature that may be moving. True when beside it.</summary>
    private bool CloseOn(Dwarf d, Creature c, Task t)
    {
        if (Adjacent(d.Cell, c.Cell)) return true;
        if (++t.Stage > 120) return false;
        var p = Path.Find(d.Cell, c.Cell, 15000);
        if (p != null && p.Count >= 2) SetPath(d, p.Count > 8 ? p.GetRange(0, 8) : p);
        else t.Stage = 1000;
        return false;
    }

    private bool DoTame(Dwarf d, Task t)
    {
        Creature c = CreatureById(t.Item);
        if (c == null || c.Dead || c.Claim != d.Id || c.Tame) return false;
        d.Activity = $"Taming {A(c.D.Name)}";
        if (!CloseOn(d, c, t)) return t.Stage < 1000;
        t.Work += (int)(10 * d.Speed(Labor.Husbandry));
        c.Path = null;
        if (t.Work < 2000) return true;
        double chance = 0.35 + 0.06 * d.Skill(Labor.Husbandry) - (c.D.Timid ? 0.1 : 0);
        if (Rng.Chance(chance))
        {
            c.Tame = true; c.MarkTame = false; c.Angry = false; c.Target = -1;
            c.Female = c.Female || Rng.Chance(0.5);
            c.Born = Math.Min(c.Born, Tick - Tables.TicksPerYear);
            Gain(d, Labor.Husbandry, 40);
            Count("tamed");
            Say($"{d.FirstName} has tamed {A(c.D.Name)}!", LogKind.Good, c.Cell);
        }
        else
        {
            Gain(d, Labor.Husbandry, 15);
            if (!c.D.Timid && Rng.Chance(0.5)) { c.Angry = true; Say($"The {c.D.Name} turns on {d.FirstName}!", LogKind.Bad, c.Cell); }
        }
        return false;
    }

    private bool DoTrain(Dwarf d, Task t)
    {
        Creature c = CreatureById(t.Item);
        if (c == null || c.Dead || c.Claim != d.Id || c.War) return false;
        d.Activity = $"Training {A(c.D.Name)} for war";
        if (!CloseOn(d, c, t)) return t.Stage < 1000;
        c.Path = null;
        t.Work += (int)(10 * d.Speed(Labor.Husbandry));
        if (t.Work < 4000) return true;
        c.War = true; c.MarkTrain = false;
        c.Name ??= Rng.Pick(BeastNames);
        Gain(d, Labor.Husbandry, 50);
        Say($"{d.FirstName} has trained {c.Title} for war.", LogKind.Good, c.Cell);
        return false;
    }

    private bool DoSlaughter(Dwarf d, Task t)
    {
        Creature c = CreatureById(t.Item);
        if (c == null || c.Dead || c.Claim != d.Id || !c.MarkSlaughter) return false;
        d.Activity = $"Slaughtering {A(c.D.Name)}";
        if (!CloseOn(d, c, t)) return t.Stage < 1000;
        c.Path = null;
        if (++t.Work < 60) return true;
        c.Dead = true;
        CStepping.Remove(c.Id);
        Spawn(ItemKind.Carcass, Tables.Mi("flesh"), c.Cell).Source = c.Def;
        Gain(d, Labor.Butchery, 20);
        Count("slaughtered");
        Say($"{d.FirstName} slaughtered {(c.Name != null ? c.Title : A(c.D.Name))}.", LogKind.Info, c.Cell);
        return false;
    }

    /// <summary>The butcher's yield from a carcass: meat, a hide, bones.</summary>
    private void Butcher(Item carcass, int at)
    {
        if (carcass.Source == null || !Tables.Creatures.TryGetValue(carcass.Source, out CreatureDef d)) { Spawn(ItemKind.Meat, Tables.Mi("meat"), at); return; }
        for (int k = 0; k < Math.Max(1, d.Meat); k++) Spawn(ItemKind.Meat, Tables.Mi("meat"), at).Source = d.Key;
        if (d.HasHide) Spawn(ItemKind.Hide, Tables.Mi("hide"), at).Source = d.Key;
        for (int k = 0; k < d.Bones; k++) Spawn(ItemKind.Bone, Tables.Mi("bone"), at).Source = d.Key;
        Count("butchered");
    }

    public void MarkSlaughter(Creature c, bool on) { c.MarkSlaughter = on && c.Tame; if (!on) Unclaim(c); }
    public void MarkTame(Creature c, bool on) { c.MarkTame = on && !c.Tame && c.D.Tamable; if (!on) Unclaim(c); }
    public void MarkTrain(Creature c, bool on) { c.MarkTrain = on && c.Tame && c.D.Trainable && !c.War; if (!on) Unclaim(c); }

    private void Unclaim(Creature c)
    {
        if (c.Claim < 0) return;
        Dwarf w = DwarfById(c.Claim);
        if (w != null) { Release(w); w.Task = null; }
        c.Claim = -1;
    }
}
