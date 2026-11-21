using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>
/// Danger: beasts of the caverns and the surface, grimling raids, blows and
/// wounds, doors and traps, and the dwarves' answer -- civilians flee or take
/// shelter when the alarm sounds, squads equip themselves and hold a station,
/// defend it, or charge.
/// </summary>
public sealed partial class Fortress
{
    public List<Creature> Creatures = new();
    public List<Squad> Squads = new();
    /// <summary>Civilians to the meeting hall.</summary>
    public bool Alarm;
    public int RaidsSeen;
    [JsonIgnore] public Dictionary<int, (int from, int total)> CStepping = new();

    [JsonIgnore] public IEnumerable<Creature> Hostiles => Creatures.Where(c => !c.Dead && (c.D.Hostile || c.Angry));
    [JsonIgnore] public bool RaidUnderway => Creatures.Any(c => !c.Dead && c.Raider);

    public Creature CreatureById(int id) => Creatures.FirstOrDefault(c => c.Id == id);

    public Creature SpawnCreature(string def, int cell, bool raider = false)
    {
        CreatureDef d = Tables.Creatures[def];
        var c = new Creature { Id = NextId++, Def = def, Cell = cell, Hp = d.Hp, Raider = raider, NextThink = Tick + Rng.Int(10), Female = Rng.Chance(0.5), Born = Tick - Tables.TicksPerYear * 2 };
        if (raider) c.Leave = Tick + Tables.TicksPerDay * 5 / 2;
        Creatures.Add(c);
        return c;
    }

    private bool DoorAt(int cell) => BuildingAt.TryGetValue(cell, out int id) && BuildingById(id) is { Built: true, Def: "door" };

    private Func<int, bool> Blocker(Creature c) => c.D.Bashes ? null : DoorAt;

    // A name of the legends ("the Ashwyrm", "Gnashjaw") goes as it is.
    public static string A(string name) => name.StartsWith("the ") || (char.IsUpper(name[0]) && !name.Contains(' ')) ? name : "aeiou".Contains(name[0]) ? "an " + name : "a " + name;

    // ---- creatures ----------------------------------------------------------------------

    private int _berserkers;

    private void StepCreatures()
    {
        for (int k = 0; k < Creatures.Count; k++)
        {
            Creature c = Creatures[k];
            if (c.Dead) continue;
            if (c.AttackWait > 0) c.AttackWait--;
            if (c.Tame && c.D.Grazer) { Graze(c); if (c.Dead) continue; }
            if (c.Path != null)
            {
                if (--c.MoveWait > 0) continue;
                CreatureStep(c);
                continue;
            }
            if (Tick < c.NextThink) continue;
            c.NextThink = Tick + 8 + Rng.Int(5);
            CreatureThink(c);
        }
        Creatures.RemoveAll(c => c.Dead);
    }

    private void CreatureStep(Creature c)
    {
        int next = c.Path[c.PathAt + 1];
        if (!Path.CanStep(c.Cell, next))
        {
            c.Path = null;
            CStepping.Remove(c.Id);
            return;
        }
        if (DoorAt(next))
        {
            if (!c.D.Bashes) { c.Path = null; CStepping.Remove(c.Id); return; }
            // Batter the door down.
            c.MoveWait = 1;
            if (++c.Bash < (c.Def == "gnashjaw" ? 40 : 160)) return;
            c.Bash = 0;
            Building door = BuildingOn(next);
            Say($"{A(c.D.Name).ToUpperFirst()} has smashed down a door!", LogKind.Bad, next);
            Destroy(door);
            return;
        }
        int from = c.Cell;
        c.Cell = next;
        c.PathAt++;
        c.Run++;
        CheckTrap(c);
        if (c.Dead) return;
        if (c.PathAt >= c.Path.Count - 1) { c.Path = null; CStepping.Remove(c.Id); return; }
        // Hunters only walk a few steps before looking again: the quarry moves.
        if (c.Target >= 0 && c.PathAt >= 4) { c.Path = null; CStepping.Remove(c.Id); return; }
        int cost = (int)Math.Max(1, Path.StepCost(c.Cell, c.Path[c.PathAt + 1]) * c.D.Pace);
        c.MoveWait = cost;
        CStepping[c.Id] = (from, cost);
    }

    private void Destroy(Building b)
    {
        int c = World.Idx(b.Center);
        World.SetLocked(c, false);
        RemoveBuilding(b);
    }

    private void CreatureWalk(Creature c, int target, int maxNodes = 12000)
    {
        var p = Path.Find(c.Cell, target, maxNodes, Blocker(c));
        if (p == null || p.Count < 2) return;
        c.Path = p;
        c.PathAt = 0;
        c.MoveWait = (int)Math.Max(1, Path.StepCost(p[0], p[1]) * c.D.Pace);
        CStepping[c.Id] = (c.Cell, c.MoveWait);
    }

    private Dwarf NearestDwarf(int cell, int range, bool sameComp = true)
    {
        World.EnsureComponents();
        P3 at = World.Pos(cell);
        int comp = World.Comp[cell];
        Dwarf best = null;
        int bestD = range + 1;
        foreach (Dwarf d in Dwarves)
        {
            if (d.Dead) continue;
            if (sameComp && World.Comp[d.Cell] != comp) continue;
            int dist = World.Pos(d.Cell).Dist(at);
            if (dist < bestD) { bestD = dist; best = d; }
        }
        return best;
    }

    private bool Adjacent(int a, int b)
    {
        P3 pa = World.Pos(a), pb = World.Pos(b);
        return pa.Z == pb.Z && Math.Abs(pa.X - pb.X) <= 1 && Math.Abs(pa.Y - pb.Y) <= 1;
    }

    private void CreatureThink(Creature c)
    {
        CreatureDef d = c.D;
        if (c.Tame) { TameThink(c); return; }
        if (c.Caravan >= 0) { MerchantThink(c); return; }
        bool hostile = d.Hostile || c.Angry;
        if (!hostile)
        {
            Dwarf near = NearestDwarf(c.Cell, d.Timid ? 5 : 2);
            // The tamer, coming slowly with a handful of moss, is let near.
            if (near != null && near.Id == c.Claim) near = null;
            if (near != null && d.Timid)
            {
                // Bolt the other way.
                P3 me = World.Pos(c.Cell), them = World.Pos(near.Cell);
                P3 away = new(Math.Clamp(me.X + (me.X - them.X) * 3, 1, World.W - 2), Math.Clamp(me.Y + (me.Y - them.Y) * 3, 1, World.H - 2), me.Z);
                CreatureWalk(c, FindStandable(away, 3), 3000);
                return;
            }
            if (near != null && c.Def == "warden") { Wakes(c, near); return; }
            if (near != null && !d.Timid)
            {
                c.Angry = true;
                Say($"{A(d.Name).ToUpperFirst()} has been provoked by {near.FirstName}!", LogKind.Bad, c.Cell);
                return;
            }
            if (!d.Still && Rng.Chance(0.15)) CreatureWalk(c, FindStandable(World.Pos(c.Cell), 6), 2000);
            return;
        }
        if (c.Legend && BeastTurn(c)) return;
        // Raiders past their time head for the edge, and leave by it.
        if (c.Raider && Tick > c.Leave)
        {
            P3 p = World.Pos(c.Cell);
            bool gone = Tick > c.Leave + Tables.TicksPerDay;
            if (gone || p.X <= 0 || p.Y <= 0 || p.X >= World.W - 1 || p.Y >= World.H - 1) { c.Dead = true; c.Raider = false; Say($"{A(d.Name).ToUpperFirst()} slinks away.", LogKind.Info, c.Cell); return; }
            CreatureWalk(c, World.Idx(EdgeNear(p)), 30000);
            if (c.Path == null) c.NextThink = Tick + 200;
            return;
        }
        Dwarf target = c.Target >= 0 ? DwarfById(c.Target) : null;
        if (target == null || target.Dead || World.Pos(target.Cell).Dist(World.Pos(c.Cell)) > Tables.SightRange + 6) target = NearestDwarf(c.Cell, Tables.SightRange);
        if (c.Def == "palestalker") target = Sleeper(c) ?? target;
        c.Target = target?.Id ?? -1;
        // A war beast in the way, or (for raiders) livestock to kill, may come first.
        Creature beast = BeastTarget(c, target != null ? World.Pos(target.Cell).Dist(World.Pos(c.Cell)) : Tables.SightRange + 1);
        if (beast != null)
        {
            c.Foe = beast.Id;
            if (Adjacent(c.Cell, beast.Cell)) { if (c.AttackWait <= 0) { BeastHits(c, beast); c.AttackWait = Tables.AttackTicks; } }
            else CreatureWalk(c, beast.Cell, 8000);
            return;
        }
        if (target != null)
        {
            if (Adjacent(c.Cell, target.Cell))
            {
                if (c.AttackWait <= 0) { CreatureHits(c, target); c.AttackWait = Tables.AttackTicks; }
                return;
            }
            CreatureWalk(c, target.Cell);
            if (c.Path == null) { c.Target = -1; c.NextThink = Tick + 60; }
            return;
        }
        // Raiders and the great beasts march on the hold (the Mere's thing waits by its water); beasts roam their haunts.
        if ((c.Raider || (c.Legend && c.Def is not ("merething" or "warden"))) && World.Pos(c.Cell).Dist(Embark) > 6) { March(c); if (c.Path != null) return; c.NextThink = Tick + 50; }
        if (Rng.Chance(0.3)) CreatureWalk(c, FindStandable(World.Pos(c.Cell), 8), 3000);
    }

    /// <summary>Raiders cross the map in legs of a couple of dozen cells, so no single search has to span it.</summary>
    private void March(Creature c)
    {
        P3 at = World.Pos(c.Cell);
        int dist = Math.Max(Math.Abs(Embark.X - at.X), Math.Abs(Embark.Y - at.Y));
        // One long search for the whole way (followed to its end), then legs if even that fails.
        CreatureWalk(c, World.Idx(Embark), dist <= 25 ? 20000 : 150000);
        if (c.Path != null || dist <= 25) return;
        World.EnsureComponents();
        int comp = World.Comp[c.Cell];
        for (int t = 0; t < 4 && c.Path == null; t++)
        {
            int step = 25 - t * 5;
            P3 aim = new(at.X + (Embark.X - at.X) * step / dist, at.Y + (Embark.Y - at.Y) * step / dist, at.Z);
            // The ground rises and falls: look for a standable cell in the column.
            for (int dz = -4; dz <= 4 && c.Path == null; dz++)
            {
                P3 p = new(aim.X, aim.Y, aim.Z + dz);
                if (World.Walkable(p) && World.Comp[World.Idx(p)] == comp) CreatureWalk(c, World.Idx(p), 12000);
            }
        }
    }

    private P3 EdgeNear(P3 p)
    {
        int dl = p.X, dr = World.W - 1 - p.X, du = p.Y, dd = World.H - 1 - p.Y;
        int m = Math.Min(Math.Min(dl, dr), Math.Min(du, dd));
        P3 e = m == dl ? new P3(0, p.Y, p.Z) : m == dr ? new P3(World.W - 1, p.Y, p.Z) : m == du ? new P3(p.X, 0, p.Z) : new P3(p.X, World.H - 1, p.Z);
        for (int z = World.D - 1; z > 0; z--) if (World.Walkable(new P3(e.X, e.Y, z))) return new P3(e.X, e.Y, z);
        return e;
    }

    // ---- blows ----------------------------------------------------------------------------

    private Item Worn(int id) => id >= 0 && Items.TryGetValue(id, out Item it) ? it : null;

    public Item WeaponOf(Dwarf d) => Worn(d.Weapon) ?? Worn(d.Pick) ?? Worn(d.Axe);

    private int FightSkill(Dwarf d) => d.Skill(Labor.Fighting) + (d.Squad >= 0 ? 1 : 0) + (d.Has("brave") ? 1 : 0);

    private void CreatureHits(Creature c, Dwarf d)
    {
        int def = FightSkill(d);
        double hit = Math.Clamp(0.6 + 0.05 * (c.D.Skill - def), 0.15, 0.92);
        if (!Rng.Chance(hit)) { Gain(d, Labor.Fighting, 2); return; }
        if (Worn(d.Shield) is Item sh && Rng.Chance(sh.Rune == "warding" ? 0.4 : 0.25)) { Gain(d, Labor.Fighting, 3); return; }
        float stop = Tables.ArmorStop(Worn(d.Armor), Worn(d.Helm));
        int dmg = Math.Max(1, (int)(c.D.Damage * (0.7 + Rng.Float() * 0.6) * (1 - stop) * (c.Legend ? BeastBlow(c, d) : 1)));
        c.Run = 0;
        bool wasWell = !d.Wounded;
        d.Hp -= dmg;
        d.LastHurt = Tick;
        Gain(d, Labor.Fighting, 4);
        if (d.Hp <= 0) { Kill(d, $"slain by {A(c.D.Name)}"); Count("slain"); return; }
        AddThought(d, "hurt");
        if (wasWell && d.Wounded)
            Say($"{A(c.D.Name).ToUpperFirst()} {c.D.Verb} {d.FirstName} in the {Rng.Pick(Tables.BodyParts)}. {d.FirstName} is badly hurt!", LogKind.Bad, d.Cell);
    }

    private void DwarfHits(Dwarf d, Creature c)
    {
        Item w = WeaponOf(d);
        double hit = Math.Clamp(0.6 + 0.05 * (FightSkill(d) - c.D.Skill), 0.15, 0.92);
        d.AttackWait = Tables.AttackTicks;
        Gain(d, Labor.Fighting, 5);
        if (!Rng.Chance(hit)) return;
        int dmg = Math.Max(1, (int)(Tables.DwarfDamage * Tables.WeaponMult(w) * Races.Of(d).Fist * (0.7 + Rng.Float() * 0.6) * (1 + 0.04f * d.Skill(Labor.Fighting))));
        c.Hp -= dmg;
        if (!c.D.Hostile) c.Angry = true;
        c.Target = d.Id;
        if (c.Hp <= 0) KillCreature(c, d, w);
    }

    private void KillCreature(Creature c, Dwarf by, Item weapon)
    {
        c.Dead = true;
        CStepping.Remove(c.Id);
        if (c.D.Home == Habitat.Raider)
        {
            Item corpse = Spawn(ItemKind.Corpse, Tables.Mi("flesh"), c.Cell);
            corpse.Label = $"{c.D.Name} corpse";
        }
        else Spawn(ItemKind.Carcass, Tables.Mi("flesh"), c.Cell).Source = c.Def;
        if (c.Claim >= 0) { Dwarf w = DwarfById(c.Claim); if (w != null && w.Task?.Item == c.Id) { Release(w); w.Task = null; } }
        if (c.D.Loot != null && Rng.Chance(c.D.LootChance)) Spawn(c.D.Loot.Value, Tables.Mi(c.D.LootMat), c.Cell);
        if (c.Legend) BeastSlain(c, by);
        Count("kills");
        string how = by == null ? "" : weapon == null ? " with bare hands" : $" with {(weapon.Kind == ItemKind.Weapon ? "a" : "their")} {weapon.Name}";
        if (by != null)
        {
            by.Kills++;
            AddThought(by, by.Has("brave") || by.Squad >= 0 ? "killed_brave" : "killed");
            Gain(by, Labor.Fighting, 25);
            Say($"{by.FirstName} has slain {A(c.D.Name)}{how}{(Rng.Chance(0.3) ? $", striking it in the {Rng.Pick(Tables.BodyParts)}" : "")}.", c.Raider || c.D.Hp > 100 ? LogKind.Good : LogKind.Info, c.Cell);
        }
        if (c.Raider && !Creatures.Any(x => !x.Dead && x.Raider))
        {
            Say("The raid is broken! The survivors flee into the hills.", LogKind.Good, c.Cell);
            foreach (Dwarf d in Living) AddThought(d, "raid_won");
            Count("raids_beaten");
        }
    }

    private void CheckTrap(Creature c)
    {
        Building b = BuildingOn(c.Cell);
        if (b == null || !b.Built || b.Def != "trap" || Tick < b.ArmedAt || c.Tame) return;
        b.ArmedAt = Tick + 400;
        int dmg = 45 + Rng.Int(40);
        c.Hp -= dmg;
        Count("trapped");
        if (c.Hp <= 0 || Rng.Chance(0.15)) { c.Hp = 0; Say($"A stonefall trap crushes {A(c.D.Name)}!", LogKind.Good, c.Cell); KillCreature(c, null, null); }
        else Say($"A stonefall trap catches {A(c.D.Name)}.", LogKind.Info, c.Cell);
    }

    // ---- what comes, and when -----------------------------------------------------------

    public void SpawnWildlife()
    {
        for (int k = 0; k < 8; k++)
        {
            string def = k < 6 ? "fellbuck" : "badger";
            P3 p = SurfaceFar(28);
            if (p.X >= 0) SpawnCreature(def, World.Idx(p));
        }
    }

    private P3 SurfaceFar(int minDist)
    {
        World.EnsureComponents();
        int comp = World.Comp[World.Idx(Embark)];
        for (int t = 0; t < 200; t++)
        {
            int x = Rng.Int(World.W), y = Rng.Int(World.H);
            for (int z = World.D - 1; z > 0; z--)
            {
                int i = World.Idx(x, y, z);
                if (World.Shapes[i] == Shape.Empty) continue;
                if (World.Walkable(i) && World.Comp[i] == comp && new P3(x, y, z).Dist(Embark) >= minDist) return new P3(x, y, z);
                break;
            }
        }
        return new P3(-1, -1, -1);
    }

    /// <summary>An open floor cell in a cavern layer (0 the upper caverns; -1 if none found).</summary>
    private int CavernCell(int layer = 0)
    {
        int cz = Caverns.Length > layer ? Caverns[layer] : CavernZ;
        for (int t = 0; t < 400; t++)
        {
            int x = Rng.Int(World.W), y = Rng.Int(World.H);
            for (int z = cz - 2; z <= cz + 2; z++)
            {
                if (!World.In(x, y, z)) continue;
                int i = World.Idx(x, y, z);
                if (World.Shapes[i] == Shape.Floor && !World.IsOutside(i)) return i;
            }
        }
        return -1;
    }

    /// <summary>No raids, beasts or cavern spawns come with the seasons (tests that are about something else).</summary>
    public bool Peaceful;

    private void SeasonalDangers()
    {
        if (Peaceful) return;
        MaybeGreatBeast();
        DeepDangers();
        if (CavernFound)
        {
            int n = Rng.Range(1, 3) + Year / 2;
            for (int k = 0; k < n; k++) { int c = CavernCell(); if (c >= 0) SpawnCreature(Rng.Chance(0.15 + Year * 0.03) ? "stalker" : "crawler", c); }
        }
        if (Creatures.Count(c => c.D.Home == Habitat.Surface) < 4)
            for (int k = 0; k < 2; k++) { P3 p = SurfaceFar(30); if (p.X >= 0) SpawnCreature("fellbuck", World.Idx(p)); }
        // Raids, from the first autumn on, likelier as the hold grows rich.
        if (Year == 1 && Season < 2) return;
        if (Map != null && NearestWarren() == null && !Rng.Chance(0.3)) return;
        double chance = Math.Min(0.9, (0.3 + Wealth / 20000.0 + 0.1 * (Year - 1)) * RaidPressure);
        if (!Rng.Chance(chance)) return;
        int size = Math.Min(30, 2 + Rng.Int(3) + Wealth / 5000 + (Year - 1) * 2);
        // The warrens send what they think the hold can be bled by: a hold already bled thin is picked over,
        // not wiped out (raids sized by wealth and years alone ran a dwindling hold into the ground).
        size = Math.Min(size, 2 + Alive / 2) + Rank;
        size = GateRaid(size);
        LaunchRaid(size);
    }

    public void LaunchRaid(int size)
    {
        P3 at = EdgeArrival();
        for (int k = 0; k < size; k++)
        {
            string def = k % 4 == 3 ? "brute" : "raider";
            SpawnCreature(def, FindStandable(at, 3), raider: true);
        }
        RaidsSeen++;
        Count("raids");
        Site from = NearestWarren();
        Say($"A grimling raid{(from != null ? $" from {from.Name}" : "")}! {size} of them come over the hills{(size > 8 ? " -- a warband" : "")}.", LogKind.Alert, World.Idx(at));
    }

    // ---- dwarves in danger ----------------------------------------------------------------

    private Creature ThreatNear(Dwarf d, int range)
    {
        P3 at = World.Pos(d.Cell);
        Creature best = null;
        int bestD = range + 1;
        foreach (Creature c in Creatures)
        {
            if (c.Dead || !(c.D.Hostile || c.Angry)) continue;
            P3 p = World.Pos(c.Cell);
            if (Math.Abs(p.Z - at.Z) > 1) continue;
            int dist = p.Dist(at);
            // The Pale Stalker is not seen until it is close.
            if (c.Def == "palestalker" && dist > 2) continue;
            if (dist < bestD) { bestD = dist; best = c; }
        }
        return best;
    }

    public Squad SquadOf(Dwarf d) => d.Squad >= 0 ? Squads.FirstOrDefault(s => s.Id == d.Squad) : null;

    private bool OnDuty(Dwarf d) => SquadOf(d) is { Order: not SquadOrder.OffDuty };

    /// <summary>Before anything else: strike back at a foe close by, or run from it.</summary>
    private Task PlanThreat(Dwarf d)
    {
        Creature near = ThreatNear(d, OnDuty(d) ? 3 : 6);
        if (near == null) return null;
        if (OnDuty(d) || (d.Hp > 60 && Adjacent(d.Cell, near.Cell) && !d.Has("loner"))) return new Task { Kind = TaskKind.Fight, Item = near.Id };
        Speak(d, "flee", 0.6);
        return new Task { Kind = TaskKind.Flee, Item = near.Id, Dest = FleeSpot(d, near) };
    }

    /// <summary>After needs: soldiers to their orders, everyone else to shelter while the alarm sounds.</summary>
    private Task PlanOrders(Dwarf d)
    {
        if (OnDuty(d)) return PlanMilitary(d);
        if (Alarm) return new Task { Kind = TaskKind.Shelter };
        if (Feasting && d.Insane == null && d.Strange == null) return new Task { Kind = TaskKind.Feast };
        return null;
    }

    private int FleeSpot(Dwarf d, Creature from)
    {
        Zone meet = Zones.FirstOrDefault(z => z.Kind == ZoneKind.Meeting);
        P3 me = World.Pos(d.Cell), them = World.Pos(from.Cell);
        if (meet != null)
        {
            P3 m = new((meet.X0 + meet.X1) / 2, (meet.Y0 + meet.Y1) / 2, meet.Z);
            if (m.Dist(them) > 10) return FindStandable(m, 2);
        }
        // Home to the wagon, unless that is where the danger is; otherwise just away.
        if (Embark.Dist(them) > 10 && Embark.Dist(me) > 3) return FindStandable(Embark, 3);
        P3 away = new(Math.Clamp(me.X + Math.Sign(me.X - them.X) * 10, 1, World.W - 2), Math.Clamp(me.Y + Math.Sign(me.Y - them.Y) * 10, 1, World.H - 2), me.Z);
        return FindStandable(away, 3);
    }

    private Task PlanMilitary(Dwarf d)
    {
        Squad s = SquadOf(d);
        // Kit first: weapon, armour, helm, shield, from the stores.
        foreach (var (slot, tag) in new[] { (d.Weapon, "weapon"), (d.Armor, "armor"), (d.Helm, "helm"), (d.Shield, "shield") })
        {
            if (slot >= 0) continue;
            Item it = NearestFree(d, x => Tables.Matches(tag, x));
            if (it == null) continue;
            it.ReservedBy = d.Id;
            return new Task { Kind = TaskKind.Equip, Item = it.Id, Note = tag };
        }
        // A foe to go for.
        Creature foe = null;
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        if (s.Order == SquadOrder.Charge)
            foe = Hostiles.Where(c => World.Comp[c.Cell] == comp).MinBy(c => World.Pos(c.Cell).Dist(World.Pos(d.Cell)));
        else if (s.Order == SquadOrder.Defend)
            foe = Hostiles.Where(c => World.Comp[c.Cell] == comp && World.Pos(c.Cell).Dist(s.Station) <= Tables.DefendRadius).MinBy(c => World.Pos(c.Cell).Dist(World.Pos(d.Cell)));
        if (foe != null) return new Task { Kind = TaskKind.Fight, Item = foe.Id };
        return new Task { Kind = TaskKind.Duty, Dest = FindStandable(s.Station, 2), Total = 150 };
    }

    private bool DoFight(Dwarf d, Task t)
    {
        Creature c = CreatureById(t.Item);
        if (c == null || c.Dead) return false;
        d.Activity = $"Fighting {A(c.D.Name)}";
        Squad s = SquadOf(d);
        if (s is { Order: SquadOrder.Defend } && World.Pos(c.Cell).Dist(s.Station) > Tables.DefendRadius + 6) return false;
        if (d.Squad < 0 && d.Hp < 35) return false;
        if (Adjacent(d.Cell, c.Cell))
        {
            if (d.AttackWait <= 0) DwarfHits(d, c);
            return !c.Dead;
        }
        if (++t.Work > 600) return false;
        var p = Path.Find(d.Cell, c.Cell, 15000);
        if (p == null || p.Count < 2) return false;
        // A few steps, then look again: the foe moves too.
        SetPath(d, p.Count > 6 ? p.GetRange(0, 6) : p);
        return true;
    }

    private bool DoFlee(Dwarf d, Task t)
    {
        d.Activity = "Fleeing!";
        if (t.Stage == 0) { int w = Walk(d, t.Dest); t.Stage = 1; return w == 0; }
        return false;
    }

    private bool DoShelter(Dwarf d, Task t)
    {
        d.Activity = "Taking shelter";
        if (!Alarm) return false;
        AddThought(d, "alarm");
        Zone meet = Zones.FirstOrDefault(z => z.Kind == ZoneKind.Meeting);
        if (meet == null) { if (++t.Work > 100) return false; return true; }
        if (t.Stage == 0)
        {
            t.Stage = 1;
            int w = Walk(d, WanderSpot(d, new P3((meet.X0 + meet.X1) / 2, (meet.Y0 + meet.Y1) / 2, meet.Z), 3));
            if (w == 0) return true;
        }
        return ++t.Work < 120;
    }

    private bool DoDuty(Dwarf d, Task t)
    {
        d.Activity = "On duty";
        if (!OnDuty(d)) return false;
        if (t.Stage == 0)
        {
            t.Stage = 1;
            int w = Walk(d, t.Dest);
            if (w == 0) return true;
        }
        // Drill while standing guard.
        if (t.Work % 40 == 0) d.Xp[(int)Labor.Fighting]++;
        if (t.Work % 20 == 0 && ThreatNear(d, Tables.DefendRadius) != null) return false;
        return ++t.Work < t.Total;
    }

    private bool DoEquip(Dwarf d, Task t)
    {
        d.Activity = "Fetching gear";
        int r = Fetch(d, t.Item);
        if (r < 0) return false;
        if (r == 0) return true;
        Item it = Items[t.Item];
        it.ReservedBy = -1;
        switch (t.Note)
        {
            case "weapon": d.Weapon = it.Id; break;
            case "armor": d.Armor = it.Id; break;
            case "helm": d.Helm = it.Id; break;
            default: d.Shield = it.Id; break;
        }
        t.Item = -1;
        return false;
    }

    /// <summary>Every tick: a blow at an adjacent foe if the arm is ready; a look round now and then.</summary>
    private bool CombatReflex(Dwarf d)
    {
        if (d.AttackWait > 0) d.AttackWait--;
        if (Creatures.Count == 0 && _berserkers == 0) return false;
        if (d.AttackWait <= 0 && d.Task?.Kind != TaskKind.Flee && d.Task?.Kind != TaskKind.Sleep)
        {
            foreach (Creature c in Creatures)
                if (!c.Dead && (c.D.Hostile || c.Angry) && Adjacent(d.Cell, c.Cell)) { DwarfHits(d, c); break; }
        }
        // Berserkers are fought off by whoever they come at.
        if (_berserkers > 0 && d.AttackWait <= 0 && d.Insane != "berserk")
            foreach (Dwarf b in Dwarves)
                if (!b.Dead && b.Insane == "berserk" && b != d && Adjacent(d.Cell, b.Cell)) { DwarfHitsDwarf(d, b); break; }
        // Notice danger mid-task.
        if ((Tick + d.Id) % 10 == 0 && d.Task?.Kind is not (TaskKind.Fight or TaskKind.Flee or TaskKind.Equip) && ThreatNear(d, 5) != null) return true;
        return false;
    }

    private void Heal(Dwarf d)
    {
        if (d.Hp >= d.MaxHp) return;
        bool asleep = d.Task?.Kind == TaskKind.Sleep && d.Task.Stage >= 1;
        if (Tick % (asleep ? 6 : 30) == 0) d.Hp++;
    }

    private void CorpseThoughts()
    {
        var corpses = Items.Values.Where(i => i.Kind == ItemKind.Corpse && i.Cell >= 0 && !i.Stored && i.InBuilding < 0).Select(i => World.Pos(i.Cell)).ToList();
        if (corpses.Count == 0) return;
        foreach (Dwarf d in Living)
        {
            P3 p = World.Pos(d.Cell);
            if (corpses.Any(c => c.Z == p.Z && Math.Abs(c.X - p.X) <= 3 && Math.Abs(c.Y - p.Y) <= 3) && !d.Has("brave")) AddThought(d, "saw_corpse");
        }
    }

    // ---- squads -------------------------------------------------------------------------

    private static readonly string[] SquadA = { "Iron", "Stone", "Bronze", "Grim", "Ember", "Deep", "Granite", "Black", "Oath" };
    private static readonly string[] SquadB = { "Hammers", "Wall", "Shields", "Axes", "Guard", "Fists", "Beards", "Anvils" };

    public Squad NewSquad()
    {
        var s = new Squad { Id = NextId++, Name = $"The {Rng.Pick(SquadA)} {Rng.Pick(SquadB)}", Station = Embark };
        Squads.Add(s);
        return s;
    }

    public void Enlist(Squad s, Dwarf d)
    {
        if (d.Squad == s.Id) return;
        Discharge(d);
        d.Squad = s.Id;
        s.Members.Add(d.Id);
        Release(d);
        d.Task = null;
    }

    public void Discharge(Dwarf d)
    {
        Squad old = SquadOf(d);
        old?.Members.Remove(d.Id);
        d.Squad = -1;
    }

    public void Disband(Squad s)
    {
        foreach (int id in s.Members.ToList()) { Dwarf d = DwarfById(id); if (d != null) Discharge(d); }
        Squads.Remove(s);
    }

    public void SetOrder(Squad s, SquadOrder o, P3? station = null)
    {
        s.Order = o;
        if (station != null) s.Station = station.Value;
        foreach (int id in s.Members)
        {
            Dwarf d = DwarfById(id);
            if (d == null || d.Dead) continue;
            if (d.Task?.Kind is TaskKind.Duty or TaskKind.Wander or TaskKind.Shelter or TaskKind.Haul or TaskKind.Dig or TaskKind.Chop or TaskKind.Gather) { Release(d); d.Task = null; d.NextThink = Tick; }
        }
    }

    public void SetLock(Building door, bool locked)
    {
        door.Locked = locked;
        World.SetLocked(World.Idx(door.X, door.Y, door.Z), locked && door.Built);
    }
}

public static class StringExt
{
    public static string ToUpperFirst(this string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
