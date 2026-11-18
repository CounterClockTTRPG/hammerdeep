using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Water and magma: floods, falls, floodgates, obsidian, burning, wells, mud, magma shops.</summary>
public partial class SmokeTest
{
    private static int Liquid(Fortress f) { int n = 0; for (int i = 0; i < f.World.N; i++) n += f.World.Depth[i]; return n; }

    private void Floods()
    {
        // A pond behind a wall; dig the wall and the water comes in.
        var f = Tiny(new[] { "###############", "#@.......#~~~~#", "#........#~~~~#", "###############" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        int total = Liquid(f);
        f.Advance(300);
        Eq(Liquid(f), total, "a pond lies still");
        Check(w.Active.Count == 0, "and nothing is being moved");
        f.Spawn(ItemKind.Pick, Tables.Mi("copper"), d.Cell);
        f.Designate(new P3(9, 1, 1), new P3(9, 1, 1), Desig.Dig);
        Run(f, () => f.Designated.Count == 0, 6000);
        f.Advance(600);
        Check(w.Depth[w.Idx(5, 1, 1)] > 0, "breaching it floods the room");
        Eq(Liquid(f), total, "water is neither made nor lost");
        Check(f.Log.Any(l => l.Text.Contains("flooding")), "and the hold is warned");
        int max = 0, min = 7;
        for (int x = 1; x < 14; x++) for (int y = 1; y <= 2; y++) { int i = w.Idx(x, y, 1); if (w.Shapes[i] != Shape.Wall) { max = Math.Max(max, w.Depth[i]); min = Math.Min(min, w.Depth[i]); } }
        Check(max - min <= 1, $"and settles nearly level ({min}..{max})");
        Check(w.Has(w.Idx(3, 1, 1), CellFlag.Wet), "leaving mud where it passed");

        // Falling: water poured into open air drops to the floor below.
        var g = Tiny(
            new[] { "#######", "#@. ~.#", "#######" },
            new[] { "#######", "#.....#", "#######" });
        World gw = g.World;
        int top = gw.Idx(4, 1, 2);
        gw.Shapes[top] = Shape.Empty;
        gw.Depth[top] = 7;
        gw.Active.Add(top);
        g.Advance(60);
        Check(gw.Depth[top] == 0 && gw.Depth[gw.Idx(4, 1, 1)] + gw.Depth[gw.Idx(3, 1, 1)] + gw.Depth[gw.Idx(5, 1, 1)] > 0, "water falls through open air to the level below");
    }

    private void Floodgates()
    {
        var f = Tiny(new[] { "##############", "#@.....+~~~~~#", "##############" });
        World w = f.World;
        Building gate = f.Place("floodgate", 7, 1, 1);
        Check(gate != null, "a floodgate goes on a floor");
        f.Spawn(ItemKind.Mechanism, Tables.Mi("granite"), w.Idx(2, 1, 1));
        f.Spawn(ItemKind.Block, Tables.Mi("granite"), w.Idx(3, 1, 1));
        // Keep the water back while it is built.
        w.Depth[w.Idx(7, 1, 1)] = 0;
        w.Locked.Add(w.Idx(7, 1, 1));
        Run(f, () => gate.Built, 3000);
        Check(gate.Built && gate.Locked, "it is built shut");
        w.Stir(w.Idx(7, 1, 1));
        f.Advance(300);
        Eq(w.Depth[w.Idx(5, 1, 1)], (byte)0, "shut, it holds the water back");
        f.SetGate(gate, true);
        f.Advance(300);
        Check(w.Depth[w.Idx(5, 1, 1)] > 0, "opened, the water pours through");
    }

    private void Magma()
    {
        // Water meets magma: obsidian.
        var f = Tiny(new[] { "##########", "#@.~~#&&&#", "#........#", "##########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        d.Cell = w.Idx(1, 2, 1);
        f.Spawn(ItemKind.Pick, Tables.Mi("copper"), d.Cell);
        f.Designate(new P3(5, 1, 1), new P3(5, 1, 1), Desig.Dig);
        Run(f, () => f.Designated.Count == 0, 6000);
        f.Advance(300);
        Check(f.Stats.GetValueOrDefault("obsidian") > 0, "water meeting magma makes obsidian");
        Check(Enumerable.Range(1, 8).Any(x => w.Shapes[w.Idx(x, 1, 1)] == Shape.Wall && w.Mats[w.Idx(x, 1, 1)] == Tables.Mi("obsidian")), "an obsidian wall stands where they met");
        // Magma burns.
        var g = Tiny(new[] { "##########", "#@....&&&#", "##########" });
        Creature c = g.SpawnCreature("crawler", g.World.Idx(6, 1, 1));
        c.NextThink = long.MaxValue;
        Item log = g.Spawn(ItemKind.Log, Tables.Mi("oak"), g.World.Idx(7, 1, 1));
        g.Advance(30);
        Check(c.Dead || c.Hp < Tables.Creatures["crawler"].Hp, "a crawler in magma is burned");
        Check(!g.Items.ContainsKey(log.Id), "and so does a log");
        // Magma workshops need magma close by, and no fuel.
        var h = Tiny(new[] { "############", "#@.......&&#", "#........&&#", "#........&&#", "############" });
        Check(h.CanPlace("mforge", 2, 1, 1) != null, "a magma forge far from magma cannot be built");
        Check(h.CanPlace("mforge", 6, 1, 1) == null, "one beside the magma can");
        Building forge = h.Place("mforge", 6, 1, 1); forge.Built = true;
        h.Spawn(ItemKind.Bar, Tables.Mi("iron"), h.World.Idx(1, 3, 1));
        h.AddOrder(forge, "mpick", 1);
        Run(h, () => forge.Orders.Count == 0, 4000);
        Check(h.Items.Values.Any(i => i.Kind == ItemKind.Pick && i.M.Key == "iron"), "and makes an iron pick without fuel");
    }

    private void Wells()
    {
        var f = Tiny(
            new[] { "#########", "#@.. ...#", "#########" },
            new[] { "#########", "#### ####", "#########" },
            new[] { "#########", "####~####", "#########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        int shaft = w.Idx(4, 1, 2);
        w.Shapes[shaft] = Shape.Empty;
        Check(f.CanPlace("well", 4, 1, 3) == null, "a well goes over a shaft with water below");
        Check(f.CanPlace("well", 2, 1, 3) != null, "but not on a floor");
        Building well = f.Place("well", 4, 1, 3);
        f.Spawn(ItemKind.Boulder, Tables.Mi("granite"), w.Idx(2, 1, 3));
        f.Spawn(ItemKind.Barrel, Tables.Mi("oak"), w.Idx(3, 1, 3));
        Run(f, () => well.Built, 3000);
        Check(well.Built, "and is built");
        d.Thirst = Tables.ThirstyAt + 5;
        Run(f, () => d.Thirst < 100, 1500);
        Check(d.Thirst < 100 && d.Thoughts.Any(t => t.Id == "drank_well"), "a thirsty dwarf drinks from the well, and does not grumble");
        // Mud grows crops.
        var g = Tiny(new[] { "######", "#@...#", "######" });
        int mud = g.World.Idx(3, 1, 1);
        Check(!g.Farmable(mud, Tables.M("dimcap")), "bare rock grows nothing");
        g.World.Set(mud, CellFlag.Wet, true);
        Check(g.Farmable(mud, Tables.M("dimcap")), "but mud does");
        // A generated map: liquid at rest, and saved.
        Fortress gen = _gen ?? Fortress.New("Genhold", 12345);
        int before = Liquid(gen);
        Check(before > 1000, "a generated map has ponds, pools and magma");
        Check(gen.World.Active.Count == 0, "all at rest until disturbed");
        Fortress back = Save.Read(Save.Write(gen));
        Eq(Liquid(back), Liquid(gen), "liquid survives a save");
    }
}
