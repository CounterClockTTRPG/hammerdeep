using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>The season's weather.</summary>
public partial class SmokeTest
{
    private void Weathers()
    {
        // Over many years: each harsh weather comes, and only in its season.
        {
            Fortress f = Tiny(new[] { "#####", "#@..#", "#####" });
            var seen = new HashSet<(int season, Weather sky)>();
            for (int k = 1; k <= 120; k++)
            {
                f.Tick = (long)k * Tables.TicksPerSeason;
                f.NewWeather();
                seen.Add((f.Season, f.Sky));
            }
            Check(seen.Contains((0, Weather.Floods)) && seen.Contains((1, Weather.Drought)) && seen.Contains((1, Weather.Heat)) && seen.Contains((3, Weather.HardWinter)), "floods, drought, heat and hard winters all come");
            Check(seen.All(s => s.sky == Weather.Fair || (s.sky, s.season) is (Weather.Floods, 0) or (Weather.Drought, 1) or (Weather.Heat, 1) or (Weather.HardWinter, 3)), "each in its own season");
            Check(seen.Contains((2, Weather.Fair)), "and Leaffall is always fair");
        }
        // Floods: the water under the sky rises, and runs.
        {
            Fortress f = Tiny(new[] { "##########", "#@..~~...#", "#...~~...#", "##########" });
            World w = f.World;
            int pond = w.Idx(4, 1, 1);
            for (int i = 0; i < w.N; i++) w.Set(i, CellFlag.Outside, true);
            byte before = w.Depth[pond];
            f.Flood();
            Check(w.Depth[pond] == System.Math.Min(7, before + 2) && w.Active.Contains(pond), $"a flood raises the pond ({before} -> {w.Depth[pond]}) and sets it running");
        }
        // A hard winter freezes the water under the sky: it can still be drunk, but the ice must be broken first.
        {
            Fortress f = Tiny(new[] { "##########", "#@..~~...#", "#...~~...#", "##########" });
            World w = f.World;
            int beside = w.Idx(3, 1, 1);
            Check(f.BesideWater(beside), "a dwarf beside the pond can drink");
            f.Sky = Weather.HardWinter;
            Check(!f.Frozen(beside), "underground, a hard winter freezes nothing");
            for (int i = 0; i < w.N; i++) w.Set(i, CellFlag.Outside, true);
            Check(f.Frozen(beside) && f.BesideWater(beside), "under the sky the pond is frozen -- but the ice can be broken for a drink");
        }
        // Heat spoils ale; drought slows crops under the sky.
        {
            Fortress f = Tiny(new[] { "##########", "#@.......#", "##########" });
            World w = f.World;
            for (int k = 0; k < 40; k++) f.Spawn(ItemKind.Drink, Tables.Mi("dimcap"), w.Idx(5, 1, 1));
            f.Sky = Weather.Heat;
            for (int d = 0; d < 20; d++) f.DailyWeather();
            int left = f.Items.Values.Count(i => i.Kind == ItemKind.Drink);
            Check(left < 40 && left > 5, $"a heatwave spoils some of the ale ({left} of 40 left after 20 days)");
            int cell = w.Idx(3, 1, 1);
            w.Set(cell, CellFlag.Outside, true);
            f.Crops[cell] = new Crop { Mat = "hearthwheat", Planted = f.Tick };
            f.Sky = Weather.Drought;
            f.WeatherOnCrops();
            Eq(f.Crops[cell].Planted, f.Tick + 25, "drought sets a crop under the sky back");
            Fortress g = Save.Read(Save.Write(f));
            Eq(g.Sky, Weather.Drought, "the weather survives a save");
        }
    }
}
