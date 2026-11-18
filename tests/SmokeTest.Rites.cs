using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Burial, grief and ghosts; drawbridges.</summary>
public partial class SmokeTest
{
    private void Burial()
    {
        var f = Tiny(new[] { "##############", "#@...........#", "#.@..........#", "##############" });
        World w = f.World;
        Dwarf a = f.Dwarves[0], b = f.Dwarves[1];
        a.Opinion[b.Id] = 60;
        f.Kill(b, "struck by a falling boulder");
        Item body = f.Items.Values.First(i => i.Kind == ItemKind.Corpse);
        Eq(body.Of, b.Id, "the corpse knows whose it is");
        f.Advance(Tables.TicksPerDay * 2 + 600);
        Check(a.Thoughts.Any(t => t.Id == "unburied"), "a friend frets over the unburied dead");
        f.Spawn(ItemKind.Coffin, Tables.Mi("granite"), w.Idx(10, 1, 1));
        Building coffin = f.Place("coffin", 12, 2, 1);
        int t = Run(f, () => b.Buried, 4000);
        Check(coffin.Built && b.Buried, $"the coffin is placed and the dead laid in it ({t} ticks)");
        Eq(coffin.Owner, b.Id, "the coffin knows who lies there");
        Check(body.InBuilding == coffin.Id, "the body is inside");
        Check(a.Thoughts.Any(x => x.Id == "laid_to_rest"), "and the friend finds some peace");
        // Left a whole season: a ghost.
        var g = Tiny(new[] { "##########", "#@......~#", "#.@......#", "##########" });
        Dwarf c = g.Dwarves[1];
        for (int k = 0; k < 10; k++) g.Spawn(ItemKind.Plant, Tables.Mi("ration"), g.World.Idx(5, 2, 1));
        g.Kill(c, "drowned in ale");
        g.Advance(Tables.TicksPerSeason + Tables.TicksPerDay * 2);
        Check(c.Restless, "an unburied dwarf's spirit grows restless after a season");
        Check(g.Log.Any(l => l.Text.Contains("restless")), "and the hold hears of it");
    }

    private void Drawbridges()
    {
        // A bridge over a channelled gap, with stone below.
        var f = Tiny(
            new[] { "#########", "#@.. ...#", "#########" },
            new[] { "#########", "####.####", "#########" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        int gap = w.Idx(4, 1, 2);
        Check(f.Path.Find(d.Cell, w.Idx(6, 1, 2)) == null, "the gap cannot be crossed");
        f.Spawn(ItemKind.Mechanism, Tables.Mi("granite"), w.Idx(2, 1, 2));
        f.Spawn(ItemKind.Block, Tables.Mi("granite"), w.Idx(3, 1, 2));
        Building br = f.Place("bridge", 4, 1, 2);
        Check(br != null, "a drawbridge can be placed over the gap");
        Run(f, () => br.Built, 4000);
        Check(br.Built, "and built");
        Check(f.Path.Find(d.Cell, w.Idx(6, 1, 2)) != null, "the lowered bridge spans it");
        Creature c = f.SpawnCreature("crawler", gap);
        c.NextThink = long.MaxValue;
        f.SetBridge(br, true);
        Eq(w.Shapes[gap], Shape.Empty, "raised, the gap opens");
        Eq(w.Pos(c.Cell).Z, 1, "and what stood on it falls below");
        Check(f.Path.Find(d.Cell, w.Idx(6, 1, 2)) == null, "nobody crosses a raised bridge");
        f.SetBridge(br, false);
        Check(f.Path.Find(d.Cell, w.Idx(6, 1, 2)) != null, "lowered again, the way is open");
        // On a floor it seals the way.
        var g = Tiny(new[] { "#######", "#@....#", "#######" });
        Building fb = g.Place("bridge", 3, 1, 1);
        fb.Built = true;
        g.SetBridge(fb, true);
        Check(g.Path.Find(g.Dwarves[0].Cell, g.World.Idx(5, 1, 1)) == null, "a raised bridge on a floor seals the corridor");
        byte[] data = Save.Write(g);
        Fortress h = Save.Read(data);
        Check(h.Path.Find(h.Dwarves[0].Cell, h.World.Idx(5, 1, 1)) == null, "and stays sealed after a save");
    }
}
