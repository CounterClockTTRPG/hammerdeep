using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Combat, doors, traps, squads, the alarm and raids.</summary>
public partial class SmokeTest
{
    private void Duels()
    {
        var f = Tiny(new[] { "############", "#@.........#", "#..........#", "############" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        d.Xp[(int)Labor.Fighting] = 40 * 36;
        Item hammer = f.Spawn(ItemKind.Weapon, Tables.Mi("iron"), d.Cell);
        f.PickUp(hammer, d);
        d.Weapon = hammer.Id;
        Creature c = f.SpawnCreature("crawler", w.Idx(9, 1, 1));
        int t = Run(f, () => c.Dead || d.Dead, 4000);
        Check(c.Dead, $"a dwarf with a hammer kills a cave crawler ({t} ticks)");
        Check(!d.Dead, "and lives");
        Eq(d.Kills, 1, "the kill is counted");
        Check(f.Items.Values.Any(i => i.Kind == ItemKind.Corpse && i.Label?.Contains("crawler") == true), "leaving a crawler corpse");
        Check(f.Creatures.Count == 0, "the dead are gone from the creatures");
    }

    private void Fleeing()
    {
        var f = Tiny(new[] { "##############################", "#@...........................#", "#............................#", "##############################" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        d.Cell = w.Idx(10, 1, 1);
        Creature c = f.SpawnCreature("stalker", w.Idx(5, 1, 1));
        bool fled = false;
        Run(f, () => { fled |= d.Activity == "Fleeing!"; return fled && w.Pos(d.Cell).X > 18; }, 600);
        Check(fled, "a civilian runs from a deep stalker");
        Check(w.Pos(d.Cell).X > 14, "and gets away from it, for now");
    }

    private void Doors()
    {
        var f = Tiny(new[] { "###########", "#@...+....#", "#####.#####", "###########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        // The plus in the map is just floor; put a built door on it.
        Building door = f.Place("door", 5, 1, 1);
        door.Built = true;
        Creature c = f.SpawnCreature("crawler", w.Idx(8, 1, 1));
        Check(f.Path.Find(c.Cell, d.Cell, 5000, cell => f.BuildingOn(cell)?.Def == "door") == null, "a door blocks a crawler's way");
        Check(f.Path.Find(d.Cell, w.Idx(8, 1, 1)) != null, "but not a dwarf's");
        Run(f, () => false, 600);
        Check(!d.Dead && d.Hp == Tables.MaxHp, "the crawler cannot get through to the dwarf");
        // A brute batters it down.
        Creature b = f.SpawnCreature("brute", w.Idx(7, 1, 1));
        int t = Run(f, () => !f.Buildings.Contains(door), 3000);
        Check(!f.Buildings.Contains(door), $"a grimling brute smashes the door ({t} ticks)");
        // Locked doors keep everyone out.
        var g = Tiny(new[] { "#######", "#@.+..#", "#######" });
        Building ld = g.Place("door", 3, 1, 1);
        ld.Built = true;
        g.SetLock(ld, true);
        Check(g.Path.Find(g.Dwarves[0].Cell, g.World.Idx(5, 1, 1)) == null, "a locked door stops dwarves too");
        g.SetLock(ld, false);
        Check(g.Path.Find(g.Dwarves[0].Cell, g.World.Idx(5, 1, 1)) != null, "until it is unlocked");
    }

    private void Traps()
    {
        var f = Tiny(new[] { "############", "#@.........#", "############" });
        World w = f.World;
        Building trap = f.Place("trap", 5, 1, 1);
        trap.Built = true;
        Creature c = f.SpawnCreature("crawler", w.Idx(9, 1, 1));
        Run(f, () => f.Stats.GetValueOrDefault("trapped") > 0 || c.Dead, 1500);
        Check(f.Stats.GetValueOrDefault("trapped") > 0, "the crawler walks onto the stonefall trap");
        Check(c.Dead || c.Hp < Tables.Creatures["crawler"].Hp, "and is crushed or hurt");
        Check(trap.ArmedAt > f.Tick - 1500, "the trap resets itself after a while");
    }

    private void Squads()
    {
        var f = Tiny(new[] { "##############################", "#@...........................#", "#.@..........................#", "##############################" });
        World w = f.World;
        Dwarf a = f.Dwarves[0], b = f.Dwarves[1];
        f.Spawn(ItemKind.Weapon, Tables.Mi("iron"), w.Idx(4, 2, 1));
        f.Spawn(ItemKind.Armor, Tables.Mi("iron"), w.Idx(5, 2, 1));
        Squad s = f.NewSquad();
        f.Enlist(s, a);
        f.SetOrder(s, SquadOrder.Station, new P3(6, 1, 1));
        Run(f, () => a.Weapon >= 0 && a.Armor >= 0, 800);
        Check(a.Weapon >= 0 && a.Armor >= 0, "a soldier on duty arms and armours from the stores");
        Run(f, () => a.Activity == "On duty", 600);
        Eq(a.Activity, "On duty", "and stands at the station");
        Check(f.World.Pos(a.Cell).Dist(new P3(6, 1, 1)) <= 3, "near it");
        Creature c = f.SpawnCreature("raider", w.Idx(27, 1, 1), raider: true);
        f.SetOrder(s, SquadOrder.Charge);
        int t = Run(f, () => c.Dead || a.Dead, 3000);
        Check(c.Dead, $"ordered to charge, the soldier hunts down the raider ({t} ticks)");
        Check(b.Squad < 0 && b.Weapon < 0, "a civilian is left alone");
        Check(f.Log.Any(l => l.Text.Contains("raid is broken")), "the last raider's death breaks the raid");
        f.Disband(s);
        Eq(a.Squad, -1, "disbanding sends them home");
    }

    private void Alarms()
    {
        var f = Tiny(new[] { "##############################", "#@...........................#", "#............................#", "##############################" });
        Dwarf d = f.Dwarves[0];
        f.AddZone(ZoneKind.Meeting, new P3(24, 1, 1), new P3(27, 2, 1));
        f.Alarm = true;
        Run(f, () => f.World.Pos(d.Cell).X >= 21, 800);
        Check(f.World.Pos(d.Cell).X >= 21, "on the alarm, civilians go to the meeting hall");
        Eq(d.Activity, "Taking shelter", "and shelter there");
        f.Alarm = false;
        Run(f, () => d.Activity != "Taking shelter", 400);
        Check(d.Activity != "Taking shelter", "until it stops");
    }

    private void Raids()
    {
        Fortress f = Fortress.New("Raidhold", 31337);
        Check(f.Creatures.Count(c => c.D.Home == Habitat.Surface) >= 4, "wildlife roams the embark");
        int before = f.Creatures.Count;
        f.LaunchRaid(6);
        var raiders = f.Creatures.Where(c => c.Raider).ToList();
        Eq(raiders.Count, 6, "a raid of six");
        Check(raiders.Count(r => r.Def == "brute") >= 1, "with a brute among them");
        Check(f.PauseFor != null, "a raid pauses the game");
        int d0 = raiders.Min(r => f.World.Pos(r.Cell).Dist(f.Embark));
        f.Advance(1500);
        var alive = f.Creatures.Where(c => c.Raider && !c.Dead).ToList();
        Check(alive.Count == 0 || alive.Min(r => f.World.Pos(r.Cell).Dist(f.Embark)) < d0, "the raiders close on the hold");
        f.Advance(Tables.TicksPerDay * 3);
        Check(!f.Creatures.Any(c => c.Raider), "within a few days the raid is over, one way or the other");
        byte[] data = Save.Write(f);
        Fortress g = Save.Read(data);
        Eq(g.Creatures.Count, f.Creatures.Count, "creatures survive a save");
    }
}
