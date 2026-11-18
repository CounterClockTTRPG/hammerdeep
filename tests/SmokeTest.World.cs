using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>The world map, its sites and history, and what an embark takes from it.</summary>
public partial class SmokeTest
{
    private void TheWorld()
    {
        var m = new WorldMap(8675309);
        var again = new WorldMap(8675309);
        Check(m.Name == again.Name && m.Sites.Count == again.Sites.Count && m.History.Count == again.History.Count && m.Tiles.SequenceEqual(again.Tiles), "a seed always makes the same world");
        Check(m.Sites.Count(s => s.Kind == SiteKind.Mountainhome) == 1, "there is one Mountainhome");
        Check(m.Sites.Any(s => s.Kind == SiteKind.Town) && m.Sites.Any(s => s.Kind is SiteKind.Warren or SiteKind.Ruin), "and towns, and warrens (or their ruins)");
        Check(m.Sites.Any(s => WorldMap.People(s.Kind) != null), "and the homes of other peoples");
        Check(m.History.Count > 20, $"two centuries of history ({m.History.Count} events)");
        GD.Print($"    world: {m.Name}; {m.Sites.Count} sites; e.g. \"{m.History[m.History.Count / 2].Text}\"");
        var (ex, ey) = m.DefaultEmbark();
        Check(m.CanEmbark(ex, ey), "a default embark is on hills or mountains");
        int tiles = 0;
        for (int y = 0; y < WorldMap.H; y++) for (int x = 0; x < WorldMap.W; x++) if (m.CanEmbark(x, y)) tiles++;
        Check(tiles > 40, $"with plenty of choice ({tiles} tiles)");
        // The land shapes the local map.
        var steep = new EmbarkLand { Mountain = 0.95f, Forest = 0.05f, Wet = 0.1f, Richness = 0.9f };
        var soft = new EmbarkLand { Mountain = 0.1f, Forest = 0.95f, Wet = 0.9f, Richness = 0.1f };
        var a = WorldGen.Generate(77, land: steep);
        var b = WorldGen.Generate(77, land: soft);
        int Trees(WorldGen.Result r) { int n = 0; for (int i = 0; i < r.World.N; i++) if (r.World.Shapes[i] == Shape.Tree && r.World.IsOutside(i)) n++; return n; }
        int Ore(WorldGen.Result r) { int n = 0; for (int i = 0; i < r.World.N; i++) if (r.World.Shapes[i] == Shape.Wall && Tables.Mats[r.World.Mats[i]].Kind == MatKind.Ore) n++; return n; }
        int Relief(WorldGen.Result r) => r.Height.Max() - r.Height.Min();
        Check(Trees(b) > Trees(a) * 2, $"a wooded embark has more trees ({Trees(b)} against {Trees(a)})");
        Check(Ore(a) > Ore(b) * 1.5, $"a rich one more ore ({Ore(a)} against {Ore(b)})");
        Check(Relief(a) > Relief(b), $"a steep one more relief ({Relief(a)} against {Relief(b)})");
        // The neighbours shape the hold.
        Fortress f = Fortress.New("Worldhold", 31, worldSeed: 8675309, ex: ex, ey: ey);
        Check(f.Map != null && f.Map.Sites.Any(s => s.Kind == SiteKind.Fortress && s.Name == "Worldhold"), "the hold is on the world map");
        var near = m.Neighbours(ex, ey);
        Check(f.NearPeoples().All(p => near.Any(n => WorldMap.People(n.site.Kind) == p)), $"only near peoples send migrants ({string.Join(",", f.NearPeoples())})");
        float pressure = f.RaidPressure;
        Check(pressure >= 0.4f && pressure <= 2.2f, $"raid pressure from the warrens ({pressure:0.00})");
        Fortress back = Save.Read(Save.Write(f));
        Check(back.Map != null && back.Map.Name == f.Map.Name && back.WorldX == ex, "a save keeps its world");
        Eq(back.RaidPressure, pressure, "and its neighbours");
        // A hold far from any warren is raided less.
        int bx = -1, by = -1;
        float least = 99;
        for (int y = 2; y < WorldMap.H - 2; y++)
            for (int x = 2; x < WorldMap.W - 2; x++)
            {
                if (!m.CanEmbark(x, y)) continue;
                float p = m.Neighbours(x, y).Where(q => q.site.Kind == SiteKind.Warren).Sum(q => 8f / (q.days + 4));
                if (p < least) { least = p; bx = x; by = y; }
            }
        Fortress quiet = Fortress.New("Quiethold", 31, worldSeed: 8675309, ex: bx, ey: by);
        Check(quiet.RaidPressure <= pressure + 0.001f, "and a hold far from the warrens less pressed");
    }
}
