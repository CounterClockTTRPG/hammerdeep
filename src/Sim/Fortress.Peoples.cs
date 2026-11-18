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

    // ---- the world ---------------------------------------------------------------------

    /// <summary>The world the hold stands in (regenerated from its seed), and where.</summary>
    public ulong WorldSeed;
    public int WorldX = -1, WorldY = -1;
    private WorldMap _map;
    [System.Text.Json.Serialization.JsonIgnore] public WorldMap Map => _map ??= WorldSeed != 0 ? MakeMap() : null;
    /// <summary>How hard raids press: nearby warrens make them likelier (1 with no world).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public float RaidPressure { get; private set; } = 1;

    private WorldMap MakeMap()
    {
        var m = new WorldMap(WorldSeed);
        SetWorld(m);
        return m;
    }

    /// <summary>Read the neighbourhood: which peoples are near, how close the warrens are.</summary>
    public void SetWorld(WorldMap m)
    {
        _map = m;
        if (m == null || WorldX < 0) return;
        if (!m.Sites.Any(s => s.Kind == SiteKind.Fortress))
            m.Sites.Add(new Site { Id = 999, Name = Name, Kind = SiteKind.Fortress, X = WorldX, Y = WorldY, Founded = 0 });
        var near = m.Neighbours(WorldX, WorldY);
        Neighbours = near.Select(p => WorldMap.People(p.site.Kind)).Where(r => r != null).Distinct().ToList();
        float danger = near.Where(p => p.site.Kind == SiteKind.Warren).Sum(p => 8f / (p.days + 4));
        RaidPressure = Math.Clamp(0.4f + danger, 0.4f, 2.2f);
    }

    /// <summary>Where a trading people's caravan comes from: a named site, or null if they live too far to come.</summary>
    public Site CaravanHome(string faction)
    {
        WorldMap m = Map;
        if (m == null || WorldX < 0) return null;
        var near = m.Neighbours(WorldX, WorldY);
        return faction == "mountainhome"
            ? m.Sites.FirstOrDefault(s => s.Kind == SiteKind.Mountainhome)
            : near.Where(p => p.site.Kind == SiteKind.Town).Select(p => p.site).FirstOrDefault();
    }

    public Site NearestWarren()
    {
        WorldMap m = Map;
        if (m == null || WorldX < 0) return null;
        return m.Neighbours(WorldX, WorldY).Where(p => p.site.Kind == SiteKind.Warren).Select(p => p.site).FirstOrDefault();
    }

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
