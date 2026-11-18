using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// Living with other peoples: Rootfolk pine for the sky, Delvlings pocket
/// shiny things (and get the blame for more), and which peoples live near
/// enough to send migrants.
/// </summary>
public sealed partial class Fortress
{
    /// <summary>The peoples (besides dwarves) near enough to send migrants. Set at embark from the world; by default all.</summary>
    public List<string> Neighbours;

    public List<string> NearPeoples() => Neighbours ?? new List<string> { "delvling", "rootfolk", "stonekin" };

    private void RaceMoods(Dwarf d)
    {
        RaceDef r = Races.Of(d);
        if (r.NeedsSky)
        {
            if (World.IsOutside(d.Cell))
            {
                if (Tick - d.LastSky > Tables.TicksPerDay) AddThought(d, "saw_sky");
                d.LastSky = Tick;
                d.Thoughts.RemoveAll(t => t.Id == "misses_sky");
            }
            else if (Tick - d.LastSky > Tables.TicksPerDay * 2) AddThought(d, "misses_sky");
        }
        if (r.Thief && d.Task?.Kind is TaskKind.Wander or TaskKind.Haul && Rng.Chance(0.02)) Pocket(d);
    }

    /// <summary>A Delvling's light fingers: a gem, a figurine or a bar beside them goes into the hoard.</summary>
    private void Pocket(Dwarf d)
    {
        P3 at = World.Pos(d.Cell);
        Item shiny = null;
        for (int dy = -1; dy <= 1 && shiny == null; dy++)
            for (int dx = -1; dx <= 1 && shiny == null; dx++)
            {
                if (!World.In(at.X + dx, at.Y + dy, at.Z)) continue;
                shiny = ItemsOn(World.Idx(at.X + dx, at.Y + dy, at.Z)).FirstOrDefault(i => i.Free && !i.Artifact && i.Kind is ItemKind.RoughGem or ItemKind.CutGem or ItemKind.Craft or ItemKind.Bar && i.M.Kind is MatKind.Gem or MatKind.Metal or MatKind.Rock);
            }
        if (shiny == null) return;
        string name = shiny.Name;
        Destroy(shiny);
        d.Hoard++;
        AddThought(d, "pocketed");
        Count("pocketed");
        // Someone always notices something is missing.
        if (Rng.Chance(0.5))
        {
            Say($"{A(name).ToUpperFirst()} has gone missing. Eyes turn to the Delvlings.", LogKind.Bad, d.Cell);
            foreach (Dwarf o in Living.Where(o => Races.Of(o).Key != "delvling").Take(6))
            {
                AddThought(o, "robbed");
                foreach (Dwarf dv in Living.Where(x => x.Race == "delvling")) o.Opinion[dv.Id] = Math.Clamp(o.Opinion.GetValueOrDefault(dv.Id) - 5, -100, 100);
            }
        }
    }
}
