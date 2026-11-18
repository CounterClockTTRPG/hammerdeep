using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Husbandry: grazing, breeding, eggs, slaughter and the leather chain, taming, war beasts.</summary>
public partial class SmokeTest
{
    /// <summary>Make the top level of a tiny map open-sky grass.</summary>
    private static void Meadow(Fortress f)
    {
        World w = f.World;
        int z = w.D - 1;
        for (int y = 0; y < w.H; y++)
            for (int x = 0; x < w.W; x++)
            {
                int i = w.Idx(x, y, z);
                if (w.Shapes[i] == Shape.Floor) { w.Set(i, CellFlag.Grass, true); w.Set(i, CellFlag.Outside, true); }
            }
    }

    private static Creature Beast(Fortress f, string def, int cell, bool tame = true, bool female = true, bool adult = true)
    {
        Creature c = f.SpawnCreature(def, cell);
        c.Tame = tame; c.Female = female;
        c.Born = adult ? f.Tick - Tables.TicksPerYear * 2 : f.Tick;
        return c;
    }

    private void Grazing()
    {
        var f = Tiny(new[] { "############", "#@.........#", "#..........#", "#..........#", "############" });
        Meadow(f);
        Creature goat = Beast(f, "goat", f.World.Idx(6, 2, 1));
        f.Advance(6000);
        Check(!goat.Dead && goat.Hunger < 2000, $"a goat on grass stays fed (hunger {goat.Hunger})");
        var g = Tiny(new[] { "############", "#@.........#", "#..........#", "############" });
        Creature hungry = Beast(g, "goat", g.World.Idx(6, 2, 1));
        g.Advance(12000);
        Check(hungry.Dead || !g.Creatures.Contains(hungry), "a goat kept on bare rock starves");
        Check(g.Items.Values.Any(i => i.Kind == ItemKind.Carcass && i.Source == "goat"), "leaving a goat carcass");
    }

    private void Breeding()
    {
        var f = Tiny(new[] { "############", "#@.........#", "#..........#", "#..........#", "############" });
        Meadow(f);
        Beast(f, "goat", f.World.Idx(4, 2, 1), female: true);
        Beast(f, "goat", f.World.Idx(5, 2, 1), female: true);
        Beast(f, "goat", f.World.Idx(6, 2, 1), female: false);
        int born = 0;
        for (int k = 0; k < 6 && born == 0; k++) born += f.Breed();
        Check(born > 0, "a billy and two nannies have kids");
        Check(f.Creatures.Any(c => c.Def == "goat" && !c.Adult(f.Tick)), "and the kids are young");
        var lone = Tiny(new[] { "#######", "#@....#", "#######" });
        Beast(lone, "goat", lone.World.Idx(3, 1, 1), female: true);
        Eq(lone.Breed(), 0, "a lone nanny has none");
        // Hens lay.
        var h = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        Beast(h, "hen", h.World.Idx(5, 2, 1));
        h.Advance(6000);
        Check(h.Stats.GetValueOrDefault("eggs") > 0, "a hen lays eggs");
        Check(Tables.Matches("food", h.Items.Values.FirstOrDefault(i => i.Kind == ItemKind.Egg) ?? h.Spawn(ItemKind.Egg, Tables.Mi("egg"), 0)), "and eggs are food");
    }

    private void Butchery()
    {
        var f = Tiny(new[] { "##############", "#@...........#", "#............#", "#............#", "#............#", "##############" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        Creature goat = Beast(f, "goat", w.Idx(10, 2, 1));
        f.MarkSlaughter(goat, true);
        Run(f, () => goat.Dead, 2000);
        Check(goat.Dead, "a goat marked for slaughter is slaughtered");
        Item carcass = f.Items.Values.FirstOrDefault(i => i.Kind == ItemKind.Carcass);
        Check(carcass != null && carcass.Source == "goat", "into a goat carcass");
        Building b = f.Place("butcher", 2, 2, 1); b.Built = true;
        Building t = f.Place("tanner", 6, 2, 1); t.Built = true;
        Building l = f.Place("leatherworks", 9, 2, 1); l.Built = true;
        f.AddOrder(b, "butcher", 1);
        Run(f, () => b.Orders.Count == 0, 3000);
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Meat), Tables.Creatures["goat"].Meat, "the butcher takes meat from it");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Hide), 1, "a hide");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Bone), Tables.Creatures["goat"].Bones, "and bones");
        f.Spawn(ItemKind.Hide, Tables.Mi("hide"), w.Idx(1, 4, 1));
        f.AddOrder(t, "tan", 2);
        Run(f, () => t.Orders.Count == 0, 4000);
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Leather), 2, "two hides tan to leather");
        f.AddOrder(l, "larmor", 1);
        Run(f, () => l.Orders.Count == 0, 4000);
        Item armour = f.Items.Values.FirstOrDefault(i => i.Kind == ItemKind.Armor);
        Check(armour != null && armour.M.Key == "leather", "and leather makes leather armour");
        Check(armour != null && Tables.ArmorStop(armour, null) < Tables.ArmorStop(f.Spawn(ItemKind.Armor, Tables.Mi("iron"), 0), null), "which stops less than iron");
        // The hunt: wild game killed by a dwarf leaves a carcass too.
        var g = Tiny(new[] { "##########", "#@.......#", "##########" });
        Creature buck = g.SpawnCreature("badger", g.World.Idx(3, 1, 1));
        buck.Hp = 1;
        buck.Angry = true;
        Run(g, () => buck.Dead, 600);
        Check(g.Items.Values.Any(i => i.Kind == ItemKind.Carcass && i.Source == "badger"), "a badger killed in a fight leaves a carcass for the butcher");
    }

    private void Taming()
    {
        var f = Tiny(new[] { "##############", "#@...........#", "#............#", "##############" });
        Dwarf d = f.Dwarves[0];
        d.Xp[(int)Labor.Husbandry] = 40 * 225;
        Creature badger = f.SpawnCreature("badger", f.World.Idx(11, 2, 1));
        f.MarkTame(badger, true);
        Run(f, () => badger.Tame || badger.Dead || d.Dead, 6000);
        for (int k = 0; k < 5 && !badger.Tame && !badger.Dead && !d.Dead; k++) { badger.Angry = false; f.MarkTame(badger, true); Run(f, () => badger.Tame || d.Dead, 4000); }
        Check(badger.Tame, "a skilled carer tames a dire badger");
        f.MarkTrain(badger, true);
        Run(f, () => badger.War, 4000);
        Check(badger.War && badger.Name != null, "and trains it for war, giving it a name");
        Creature crawler = f.SpawnCreature("crawler", f.World.Idx(12, 1, 1));
        d.Cell = f.World.Idx(1, 1, 1);
        Run(f, () => crawler.Dead, 2000);
        Check(crawler.Dead, "the war badger goes for a cave crawler and kills it");
        // Raiders kill livestock.
        var g = Tiny(new[] { "################################", "#@#............................#", "################################" });
        g.Dwarves[0].Cell = g.World.Idx(1, 1, 1);
        Creature hen = Beast(g, "hen", g.World.Idx(30, 1, 1));
        Creature raider = g.SpawnCreature("raider", g.World.Idx(26, 1, 1), raider: true);
        Run(g, () => hen.Dead || hen.Hp < 15, 2500);
        Check(hen.Dead || hen.Hp < 15, "a raider with no dwarf in sight goes for the hens");
        // An old save, with fewer labors, loads with the new ones switched on.
        Fortress h = Tiny(new[] { "#####", "#@..#", "#####" });
        Dwarf o = h.Dwarves[0];
        Array.Resize(ref o.Xp, 14);
        Array.Resize(ref o.Labors, 14);
        Fortress back = Save.Read(Save.Write(h));
        Check(back.Dwarves[0].Labors.Length == Tables.AllLabors.Length && back.Dwarves[0].Can(Labor.Husbandry), "old saves gain the new labors");
    }
}
