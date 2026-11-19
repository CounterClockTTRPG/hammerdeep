using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Delvlings, Rootfolk, Stonekin.</summary>
public partial class SmokeTest
{
    private void Peoples()
    {
        foreach (RaceDef r in Races.All.Where(r => r.Key != "dwarf"))
        {
            Check(Tables.Clans.Any(c => c.Id == r.Clan && c.Race == r.Key), $"{r.Plural} have a clan");
            Check(Fortress.God(r.Deity) != null, $"{r.Plural} have a god");
        }
        var f = Tiny(new[] { "##############", "#@...........#", "#............#", "##############" });
        World w = f.World;
        Dwarf giant = f.AddDwarf("graniteborn", new P3(2, 2, 1));
        giant.Cell = w.Idx(2, 2, 1);
        Eq(giant.Race, "stonekin", "a Graniteborn is Stonekin");
        Check(giant.MaxHp == Tables.MaxHp * 2 && giant.Hp == giant.MaxHp, "twice as tough as a dwarf");
        Check(!giant.Name.Contains("Ironhand") && giant.Name.EndsWith("Graniteborn"), $"with a Stonekin name ({giant.Name})");
        f.Dwarves[0].Labors[(int)Labor.Mining] = false;
        f.Designate(new P3(5, 3, 1), new P3(6, 3, 1), Desig.Dig);
        Run(f, () => f.Designated.Count == 0, 6000);
        Eq(f.Designated.Count, 0, "Stonekin dig with their bare hands -- no pick in the hold");
        // Delvlings sleep anywhere, and pocket shiny things.
        var g = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        Dwarf dv = g.AddDwarf("copperclaw", new P3(4, 2, 1));
        dv.Cell = g.World.Idx(4, 2, 1);
        Check(Races.Of(dv).Size < 1, "a Delvling is small");
        dv.Fatigue = Tables.TiredAt + 10;
        Run(g, () => dv.Fatigue == 0, 3000);
        Check(!dv.Thoughts.Any(t => t.Id == "slept_floor"), "and does not mind sleeping on the floor");
        for (int k = 0; k < 8; k++) g.Spawn(ItemKind.RoughGem, Tables.Mi("garnet"), g.World.Idx(3 + k % 4, 1 + k / 4, 1));
        Run(g, () => g.Stats.GetValueOrDefault("pocketed") > 0, Tables.TicksPerDay * 4);
        Check(g.Stats.GetValueOrDefault("pocketed") > 0 && dv.Hoard > 0, "left by loose gems, a Delvling pockets one");
        // Rootfolk pine underground and cheer up outside.
        var h = Tiny(new[] { "########", "#@.....#", "########" });
        Dwarf rf = h.AddDwarf("hollyhock", new P3(3, 1, 1));
        rf.Cell = h.World.Idx(3, 1, 1);
        h.Advance(Tables.TicksPerDay * 3);
        Check(rf.Thoughts.Any(t => t.Id == "misses_sky"), "Rootfolk underground miss the sky");
        for (int x = 1; x < 7; x++) h.World.Set(h.World.Idx(x, 1, 1), CellFlag.Outside, true);
        h.Advance(200);
        Check(rf.Thoughts.Any(t => t.Id == "saw_sky") && !rf.Thoughts.Any(t => t.Id == "misses_sky"), "and are glad of it again");
        Check(rf.Speed(Labor.Farming) > f.Dwarves[0].Speed(Labor.Farming) || rf.Skill(Labor.Farming) >= f.Dwarves[0].Skill(Labor.Farming), "Rootfolk farm faster");
        // Migrants bring other peoples now and then.
        Fortress m = Fortress.New("Mixhold", 4040);
        // A well-stocked larder: a hungry hold draws no bands of other peoples.
        for (int k = 0; k < 400; k++) m.Spawn(ItemKind.Plant, Tables.Mi("ration"), m.World.Idx(m.Embark));
        for (int k = 0; k < 16; k++) { m.Tick = Tables.TicksPerSeason * (k + 1) - 1; m.Step(); }
        Check(m.Living.Any(d => d.Race != "dwarf"), $"over four years, other peoples arrive ({string.Join(",", m.Living.GroupBy(d => d.Race).Select(gr => gr.Key + gr.Count()))})");
        Check(m.Dwarves.Take(7).All(d => d.Race == "dwarf"), "the founders are all dwarves");
        // Old saves: no race means dwarf.
        Fortress o = Tiny(new[] { "#####", "#@..#", "#####" });
        o.Dwarves[0].Race = null;
        Eq(Save.Read(Save.Write(o)).Dwarves[0].Race, "dwarf", "dwarves from older saves stay dwarves");
    }
}
