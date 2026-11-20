using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// What dwarves do: needs first, then the job that suits them best, carried
/// out in stages (fetch, walk, work, finish). Every claim a task takes is
/// given back by <see cref="Release"/> when it ends, however it ends.
/// </summary>
public sealed partial class Fortress
{
    /// <summary>Chairs taken by someone eating (chair building id -> dwarf).</summary>
    public Dictionary<int, int> Seats = new();

    /// <summary>Beds claimed the same day share one line: "Gimola, Durdis and Morga have claimed beds."</summary>
    private void SayBedClaimed(Dwarf d, Building bed)
    {
        long day = Tick / Tables.TicksPerDay;
        int at = Log.FindLastIndex(l => l.Tick / Tables.TicksPerDay == day && l.Names != null);
        if (at >= 0)
        {
            LogEntry e = Log[at];
            Log.RemoveAt(at);
            e.Names.Add(d.FirstName);
            e.Text = $"{string.Join(", ", e.Names.Take(e.Names.Count - 1))} and {e.Names[^1]} have claimed beds.";
            e.Tick = Tick;
            Log.Add(e);
            return;
        }
        Say($"{d.FirstName} has claimed a bed.", LogKind.Info, World.Idx(bed.X, bed.Y, bed.Z));
        Log[^1].Names = new List<string> { d.FirstName };
    }

    private void Needs(Dwarf d)
    {
        Heal(d);
        FaithTick(d);
        RaceDef race = Races.Of(d);
        int eat = d.Has("glutton") && Tick % 3 == 0 ? 2 : 1;
        d.Hunger += race.Hunger == 1 ? eat : Rate(race.Hunger, d.Id) * eat;
        d.Thirst += race.Thirst == 1 ? 1 : Rate(race.Thirst, d.Id + 7);
        if (d.Task?.Kind != TaskKind.Sleep) d.Fatigue += d.Has("lazy") && Tick % 4 == 0 ? 2 : 1;
        if (d.Hunger > Tables.StarveDeath) Kill(d, "starved to death");
        else if (d.Thirst > Tables.ThirstDeath) Kill(d, "died of thirst");
    }

    /// <summary>A fractional rate per tick, paid out evenly by the clock (no dice): 0.8 is four ticks in five.</summary>
    private int Rate(float r, int salt) => (int)(r * (Tick + salt)) - (int)(r * (Tick + salt - 1));

    private void Act(Dwarf d)
    {
        if (d.Breaking > 0) d.Breaking--;
        if (CombatReflex(d) && d.Breaking == 0) { Release(d); d.Task = null; d.NextThink = Tick; }
        if (d.Dead) return;
        if (d.Path != null)
        {
            if (--d.MoveWait > 0) return;
            StepAlong(d);
            return;
        }
        // Urgent needs interrupt work.
        if (d.Task != null && d.Task.Kind is not (TaskKind.Eat or TaskKind.Drink or TaskKind.Sleep or TaskKind.Tantrum or TaskKind.Fight or TaskKind.Flee))
        {
            bool urgent = d.Thirst > Tables.ParchedAt || d.Hunger > Tables.StarvingAt || d.Fatigue > Tables.ExhaustedAt;
            if (urgent && Tick >= d.NextInterrupt) { d.NextInterrupt = Tick + 500; Release(d); d.Task = null; }
        }
        if (d.Task == null)
        {
            if (Tick < d.NextThink) return;
            Choose(d);
            if (d.Task == null) { d.NextThink = Tick + 12 + Rng.Int(8); return; }
        }
        bool going;
        try { going = Perform(d, d.Task); }
        catch (Exception e) { Say($"(bug) {d.Name}: {e.Message}", LogKind.Info); going = false; }
        if (!going)
        {
            Release(d);
            d.Task = null;
            d.NextThink = Tick + 1;
        }
    }

    /// <summary>The cell a dwarf is leaving, and how long this step takes (for drawing them between cells).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<int, (int from, int total)> Stepping = new();

    private void StepAlong(Dwarf d)
    {
        int next = d.Path[d.PathAt + 1];
        if (!Path.CanStep(d.Cell, next))
        {
            int goal = d.Path[^1];
            d.Path = null;
            var p = Path.Find(d.Cell, goal);
            if (p == null || p.Count < 2) { Release(d); d.Task = null; d.NextThink = Tick + 5; return; }
            SetPath(d, p);
            return;
        }
        int from = d.Cell;
        d.Cell = next;
        d.PathAt++;
        if (d.PathAt >= d.Path.Count - 1) { d.Path = null; Stepping.Remove(d.Id); }
        else
        {
            int cost = Math.Max(1, (int)(Path.StepCost(d.Cell, d.Path[d.PathAt + 1]) * Races.Of(d).Pace));
            d.MoveWait = cost;
            Stepping[d.Id] = (from, cost);
            return;
        }
        Stepping[d.Id] = (from, Tables.StepTicks);
    }

    private void SetPath(Dwarf d, List<int> p)
    {
        d.Path = p;
        d.PathAt = 0;
        d.MoveWait = Path.StepCost(p[0], p[1]);
    }

    /// <summary>1 when there, 0 when on the way, -1 when there is no way.</summary>
    private int Walk(Dwarf d, int target)
    {
        if (d.Cell == target) return 1;
        var p = Path.Find(d.Cell, target);
        if (p == null || p.Count < 2) return -1;
        SetPath(d, p);
        return 0;
    }

    private int WalkAny(Dwarf d, IReadOnlyCollection<int> targets)
    {
        foreach (int t in targets) if (t == d.Cell) return 1;
        var p = Path.FindAny(d.Cell, targets);
        if (p == null || p.Count < 2) return -1;
        SetPath(d, p);
        return 0;
    }

    // ---- choosing ---------------------------------------------------------------------

    private enum Job { Build, Craft, Farm, Mine, Chop, Gather, Haul, Bury, Animals }

    private static Labor LaborOf(Job j) => j switch
    {
        Job.Build => Labor.Construction,
        Job.Farm => Labor.Farming,
        Job.Mine => Labor.Mining,
        Job.Chop => Labor.Woodcutting,
        Job.Gather => Labor.Gathering,
        Job.Haul => Labor.Hauling,
        Job.Animals => Labor.Husbandry,
        _ => Labor.Crafting,
    };

    private static readonly (Job job, int pri)[] Jobs =
    {
        (Job.Bury, 34), (Job.Build, 30), (Job.Animals, 24), (Job.Craft, 26), (Job.Farm, 26), (Job.Mine, 20), (Job.Chop, 16), (Job.Gather, 10), (Job.Haul, 6),
    };

    private void Choose(Dwarf d)
    {
        Task t = null;
        if (d.Breaking > 0) { d.Task = new Task { Kind = TaskKind.Tantrum }; return; }
        t = PlanThreat(d);
        if (t == null && d.Thirst > Tables.ThirstyAt) t = PlanDrink(d);
        if (t == null && d.Hunger > Tables.EatAt) t = PlanEat(d);
        // A long sleep on an empty stomach wakes hungry: eat first.
        if (t == null && d.Fatigue > Tables.TiredAt && d.Hunger > Tables.EatBeforeBedAt && d.Fatigue < Tables.ExhaustedAt) t = PlanEat(d);
        if (t == null && d.Fatigue > Tables.TiredAt) t = new Task { Kind = TaskKind.Sleep };
        if (t == null && d.Strange != null) t = new Task { Kind = TaskKind.Mood };
        if (t == null && d.Strange == null) t = PlanPrayer(d);
        if (t == null && d.Insane != null) { d.Task = PlanMadness(d); return; }
        t ??= PlanOrders(d);
        if (t == null)
        {
            ClanDef clan = Tables.Clan(d.Clan);
            var order = Jobs.Select(j =>
            {
                int score = j.pri;
                if (j.job is not (Job.Craft or Job.Haul or Job.Bury))
                {
                    Labor l = LaborOf(j.job);
                    score += d.Skill(l) * 3 + (clan.Leans.Contains(l) ? 6 : 0);
                }
                return (j.job, score: score + Rng.Int(6));
            }).OrderByDescending(x => x.score);
            foreach (var (job, _) in order)
            {
                t = job switch
                {
                    Job.Build => d.Can(Labor.Construction) ? PlanBuild(d) : null,
                    Job.Craft => PlanCraft(d),
                    Job.Farm => d.Can(Labor.Farming) ? PlanFarm(d) : null,
                    Job.Mine => d.Can(Labor.Mining) ? PlanDig(d, Labor.Mining) : null,
                    Job.Chop => d.Can(Labor.Woodcutting) ? PlanDig(d, Labor.Woodcutting) : null,
                    Job.Gather => d.Can(Labor.Gathering) ? PlanDig(d, Labor.Gathering) : null,
                    Job.Haul => d.Can(Labor.Hauling) ? PlanHaul(d) : null,
                    Job.Bury => d.Can(Labor.Hauling) ? PlanBury(d) : null,
                    Job.Animals => d.Can(Labor.Husbandry) || d.Can(Labor.Butchery) ? PlanAnimals(d) : null,
                    _ => null,
                };
                if (t != null) break;
            }
        }
        // Nothing to do: drop tools no longer needed, then idle near the meeting place.
        if (t == null && d.Pick >= 0 && !d.Can(Labor.Mining) && Items.TryGetValue(d.Pick, out Item pk)) Drop(pk, d.Cell);
        if (t == null && d.Axe >= 0 && !d.Can(Labor.Woodcutting) && Items.TryGetValue(d.Axe, out Item ax)) Drop(ax, d.Cell);
        t ??= PlanIdle(d);
        d.Task = t;
    }

    private static Labor? DesigLabor(Desig g) => g switch
    {
        Desig.Dig or Desig.Channel or Desig.StairUD or Desig.StairDown or Desig.StairUp => Labor.Mining,
        Desig.Chop => Labor.Woodcutting,
        Desig.Gather => Labor.Gathering,
        Desig.Remove => Labor.Construction,
        _ => null,
    };

    private Task PlanDig(Dwarf d, Labor labor)
    {
        if (Designated.Count == 0) return null;
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        // A tool is needed: one in hand, or a free one to fetch.
        Item tool = null;
        if (labor == Labor.Mining && d.Pick < 0 && !Races.Of(d).BareHands) { tool = NearestFree(d, it => it.Kind == ItemKind.Pick); if (tool == null) return null; }
        if (labor == Labor.Woodcutting && d.Axe < 0) { tool = NearestFree(d, it => it.Kind == ItemKind.Axe); if (tool == null) return null; }
        Span<int> spots = stackalloc int[16];
        var cands = new List<(int cell, int dist)>();
        foreach (int c in Designated)
        {
            if (Claims.ContainsKey(c)) continue;
            if (DesigLabor(World.Desigs[c]) != labor) continue;
            if (Unreachable.TryGetValue(c, out int topo) && topo == World.Topology) continue;
            int n = World.WorkSpots(c, spots);
            bool ok = false;
            for (int k = 0; k < n && !ok; k++) ok = World.Comp[spots[k]] == comp;
            if (!ok) continue;
            P3 p = World.Pos(c);
            // Dig top-down and near: a level costs more than a step.
            cands.Add((c, p.Dist(at) + (p.Z < at.Z ? 2 : 0)));
        }
        if (cands.Count == 0) return null;
        int best = cands.MinBy(x => x.dist).cell;
        Claims[best] = d.Id;
        var t = new Task { Kind = World.Desigs[best] switch { Desig.Chop => TaskKind.Chop, Desig.Gather => TaskKind.Gather, _ => TaskKind.Dig }, Cell = best };
        if (tool != null) { tool.ReservedBy = d.Id; t.Item = tool.Id; }
        return t;
    }

    /// <summary>The nearest free item (by straight distance) that a dwarf can walk to.</summary>
    public Item NearestFree(Dwarf d, Func<Item, bool> pred, int nearCell = -1)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(nearCell >= 0 ? nearCell : d.Cell);
        Item best = null;
        int bestD = int.MaxValue;
        foreach (Item it in Items.Values)
        {
            if (!it.Free || !pred(it)) continue;
            if (World.Comp[it.Cell] != comp) continue;
            int dist = World.Pos(it.Cell).Dist(at);
            if (dist < bestD) { bestD = dist; best = it; }
        }
        return best;
    }

    private Task PlanHaul(Dwarf d)
    {
        if (Loose.Count == 0 || !Zones.Any(z => z.Kind == ZoneKind.Stockpile)) return null;
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        var cands = new List<(Item it, int dist)>();
        foreach (int id in Loose)
        {
            Item it = Items[id];
            if (!it.Free || World.Comp[it.Cell] != comp) continue;
            cands.Add((it, World.Pos(it.Cell).Dist(at)));
        }
        foreach (var (it, _) in cands.OrderBy(c => c.dist).Take(6))
        {
            int dest = StockSpot(it, comp);
            if (dest < 0) continue;
            it.ReservedBy = d.Id;
            StockPromised[dest] = StockPromised.GetValueOrDefault(dest) + 1;
            return new Task { Kind = TaskKind.Haul, Item = it.Id, Dest = dest };
        }
        return null;
    }

    /// <summary>The nearest stockpile cell with room that takes <paramref name="it"/>, or -1.</summary>
    public int StockSpot(Item it, int comp)
    {
        P3 from = World.Pos(it.Cell >= 0 ? it.Cell : 0);
        int best = -1, bestD = int.MaxValue, cap = Capacity(it);
        foreach (Zone z in Zones)
        {
            if (z.Kind != ZoneKind.Stockpile || (z.Cats & it.Def.Cat) == 0) continue;
            foreach (P3 p in z.Cells())
            {
                int c = World.Idx(p);
                if (!World.Walkable(c) || World.Comp[c] != comp || BuildingAt.ContainsKey(c)) continue;
                if (StockLoad(c) >= cap) continue;
                // Small things share a cell only with their own kind.
                if (cap > 1 && ItemsAt.TryGetValue(c, out var here) && here.Any(id => Items[id].Kind != it.Kind)) continue;
                int dist = p.Dist(from);
                if (dist < bestD) { bestD = dist; best = c; }
            }
        }
        return best;
    }

    private Task PlanBuild(Dwarf d)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        foreach (Building b in Buildings.Where(b => !b.Built && b.Builder < 0).OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(at)))
        {
            if (!BuildSpots(b).Any(c => World.Comp[c] == comp)) continue;
            // Every missing part has to exist, or there is no point starting.
            var reserve = new List<Item>();
            bool ok = true;
            foreach (Need n in MissingParts(b))
            {
                for (int k = 0; k < n.Count && ok; k++)
                {
                    Item it = NearestFree(d, x => Tables.Matches(n.Tag, x) && !reserve.Contains(x), World.Idx(b.Center));
                    if (it == null) ok = false; else reserve.Add(it);
                }
            }
            if (!ok) continue;
            b.Builder = d.Id;
            foreach (Item it in reserve) it.ReservedBy = d.Id;
            return new Task { Kind = TaskKind.Build, Building = b.Id, Fetch = reserve.Select(i => i.Id).ToList() };
        }
        return null;
    }

    /// <summary>What a planned building still lacks.</summary>
    public IEnumerable<Need> MissingParts(Building b)
    {
        var have = b.Parts.Select(id => Items[id]).ToList();
        foreach (Need n in b.D.Cost)
        {
            int got = 0;
            foreach (Item it in have.ToList())
                if (got < n.Count && Tables.Matches(n.Tag, it)) { got++; have.Remove(it); }
            if (got < n.Count) yield return new Need(n.Tag, n.Count - got);
        }
    }

    /// <summary>Where a builder can stand: on or beside a workshop or furniture; beside a construction.</summary>
    public List<int> BuildSpots(Building b)
    {
        var spots = new List<int>();
        bool beside = b.D.Kind == BuildKind.Construction;
        for (int y = b.Y - 1; y <= b.Y + b.H; y++)
            for (int x = b.X - 1; x <= b.X + b.W; x++)
            {
                if (!World.In(x, y, b.Z)) continue;
                bool inside = b.Covers(x, y, b.Z);
                if (beside && inside) continue;
                int c = World.Idx(x, y, b.Z);
                if (World.Walkable(c)) spots.Add(c);
            }
        // A floor over air is built from the side; stairs can also be reached from the level below.
        return spots;
    }

    private Task PlanCraft(Dwarf d)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        foreach (Building b in Buildings.Where(b => b.Built && b.D.Kind == BuildKind.Workshop && b.Orders.Count > 0).OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(at)))
        {
            int center = World.Idx(b.Center);
            if (World.Comp[center] != comp) continue;
            if (b.Orders.Any(o => o.Worker >= 0) || b.MoodClaim >= 0) continue;
            foreach (Order o in b.Orders)
            {
                Recipe r = Tables.Recipe(o.Recipe);
                if (!d.Can(r.Labor)) continue;
                var inside = b.Parts.Select(id => Items[id]).Where(i => i.ReservedBy < 0).ToList();
                var use = new List<Item>();
                bool ok = true;
                foreach (Need n in r.Inputs)
                    for (int k = 0; k < n.Count && ok; k++)
                    {
                        // The brewer reaches for what nobody can eat (hearthwheat) before the larder's fruit.
                        Item it = n.Tag == "brewable"
                            ? inside.FirstOrDefault(x => Tables.Matches(n.Tag, x) && !x.M.Edible && !use.Contains(x))
                              ?? NearestFree(d, x => Tables.Matches(n.Tag, x) && !x.M.Edible && !use.Contains(x), center)
                            : null;
                        it ??= inside.FirstOrDefault(x => Tables.Matches(n.Tag, x) && !use.Contains(x))
                            ?? NearestFree(d, x => Tables.Matches(n.Tag, x) && !use.Contains(x), center);
                        if (it == null) { ok = false; o.Stuck = "needs " + Tables.NeedName(n.Tag); }
                        else use.Add(it);
                    }
                if (!ok) continue;
                o.Stuck = null;
                o.Worker = d.Id;
                foreach (Item it in use) it.ReservedBy = d.Id;
                return new Task
                {
                    Kind = TaskKind.Craft, Building = b.Id, Order = o.Id,
                    Fetch = use.Where(i => i.InBuilding != b.Id).Select(i => i.Id).ToList(),
                    Note = string.Join(",", use.Select(i => i.Id)),
                };
            }
        }
        return null;
    }

    private Task PlanFarm(Dwarf d)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        foreach (Zone z in Zones.Where(z => z.Kind == ZoneKind.Farm && z.Crop != null).OrderBy(z => new P3(z.X0, z.Y0, z.Z).Dist(at)))
        {
            Mat crop = Tables.M(z.Crop);
            Item seed = null;
            bool seedLooked = false;
            foreach (P3 p in z.Cells())
            {
                int c = World.Idx(p);
                if (World.Comp[c] != comp) continue;
                if (Crops.TryGetValue(c, out Crop cr))
                {
                    if (cr.Claim < 0 && Tick - cr.Planted >= Tables.M(cr.Mat).Grow)
                    {
                        cr.Claim = d.Id;
                        return new Task { Kind = TaskKind.Harvest, Cell = c };
                    }
                    continue;
                }
                if (!Farmable(c, crop)) continue;
                if (!seedLooked) { seed = NearestFree(d, x => x.Kind == ItemKind.Seed && x.Mat == crop.Id, c); seedLooked = true; }
                if (seed == null) break;
                seed.ReservedBy = d.Id;
                Crops[c] = new Crop { Mat = crop.Key, Planted = long.MaxValue / 4, Claim = d.Id };
                return new Task { Kind = TaskKind.Plant, Cell = c, Item = seed.Id };
            }
        }
        return null;
    }

    /// <summary>Soil floor; surface crops need the sky, cave crops need the dark.</summary>
    /// <summary>What a new plot grows: something edible before something only fit for the brewer,
    /// and among those the crop with the most seed in store.</summary>
    public string DefaultCrop(bool outside)
    {
        return Tables.Mats.Where(m => m.Grow > 0 && (outside ? m.Surface : m.Underground))
            .OrderByDescending(m => Items.Values.Any(i => i.Kind == ItemKind.Seed && i.Mat == m.Id) ? 1 : 0)
            .ThenByDescending(m => m.Edible)
            .ThenByDescending(m => Items.Values.Count(i => i.Kind == ItemKind.Seed && i.Mat == m.Id))
            .First().Key;
    }

    public bool Farmable(int c, Mat crop)
    {
        if (World.Shapes[c] != Shape.Floor || BuildingAt.ContainsKey(c) || World.Depth[c] >= 3) return false;
        // Soil, or mud a flood has left behind.
        if (Tables.Mats[World.Mats[c]].Kind != MatKind.Soil && !World.Has(c, CellFlag.Wet)) return false;
        bool outside = World.IsOutside(c);
        return crop.Surface ? outside : !outside;
    }

    private Task PlanEat(Dwarf d)
    {
        Item food = NearestFree(d, x => x.Kind == ItemKind.Meal) ?? NearestFree(d, x => Tables.Matches("food", x));
        if (food == null) return null;
        food.ReservedBy = d.Id;
        return new Task { Kind = TaskKind.Eat, Item = food.Id };
    }

    private Task PlanDrink(Dwarf d)
    {
        Item drink = NearestFree(d, x => x.Kind == ItemKind.Drink);
        if (drink != null) { drink.ReservedBy = d.Id; return new Task { Kind = TaskKind.Drink, Item = drink.Id }; }
        // No ale: water, unless the last look for it found none.
        if (Tick < d.NoWaterUntil) return null;
        return new Task { Kind = TaskKind.Drink, Item = -1 };
    }

    private Task PlanIdle(Dwarf d)
    {
        Zone meet = Zones.FirstOrDefault(z => z.Kind == ZoneKind.Meeting);
        // Idlers loiter at the meeting hall, or else round the wagon, rather than drifting off.
        P3 c = meet != null ? new P3((meet.X0 + meet.X1) / 2, (meet.Y0 + meet.Y1) / 2, meet.Z) : Embark;
        return new Task { Kind = TaskKind.Wander, Dest = WanderSpot(d, c, meet != null ? Math.Max(2, Math.Min(meet.X1 - meet.X0, meet.Y1 - meet.Y0) / 2) : 6), Total = Rng.Range(40, 120) };
    }

    private int WanderSpot(Dwarf d, P3 around, int r)
    {
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        for (int k = 0; k < 20; k++)
        {
            int x = around.X + Rng.Range(-r, r), y = around.Y + Rng.Range(-r, r);
            for (int dz = 0; dz <= 1; dz++)
            {
                P3 p = new(x, y, around.Z - dz);
                if (World.Walkable(p) && World.Comp[World.Idx(p)] == comp) return World.Idx(p);
            }
        }
        return d.Cell;
    }

    // ---- doing ------------------------------------------------------------------------

    private bool Perform(Dwarf d, Task t)
    {
        switch (t.Kind)
        {
            case TaskKind.Dig: case TaskKind.Chop: case TaskKind.Gather: return DoDig(d, t);
            case TaskKind.Haul: return DoHaul(d, t);
            case TaskKind.Build: return DoBuild(d, t);
            case TaskKind.Craft: return DoCraft(d, t);
            case TaskKind.Eat: return DoEat(d, t);
            case TaskKind.Drink: return DoDrink(d, t);
            case TaskKind.Sleep: return DoSleep(d, t);
            case TaskKind.Plant: case TaskKind.Harvest: return DoFarm(d, t);
            case TaskKind.Tantrum: return DoTantrum(d, t);
            case TaskKind.Wander: return DoWander(d, t);
            case TaskKind.Fight: return DoFight(d, t);
            case TaskKind.Flee: return DoFlee(d, t);
            case TaskKind.Shelter: return DoShelter(d, t);
            case TaskKind.Duty: return DoDuty(d, t);
            case TaskKind.Equip: return DoEquip(d, t);
            case TaskKind.Bury: return DoBury(d, t);
            case TaskKind.Mood: return DoMood(d, t);
            case TaskKind.Pray: return DoPray(d, t);
            case TaskKind.Feast: return DoFeast(d, t);
            case TaskKind.Rampage: return DoRampage(d, t);
            case TaskKind.Tame: return DoTame(d, t);
            case TaskKind.Train: return DoTrain(d, t);
            case TaskKind.Slaughter: return DoSlaughter(d, t);
            case TaskKind.Arrive:
                d.Activity = "Arriving";
                if (t.Stage == 0) { int r = Walk(d, FindStandable(World.Pos(t.Dest), 3)); if (r == 0) { t.Stage = 1; return true; } return false; }
                return false;
        }
        return false;
    }

    /// <summary>Fetch a reserved item into the dwarf's hands. 1 when held, 0 when on the way, -1 when lost.</summary>
    private int Fetch(Dwarf d, int itemId)
    {
        if (!Items.TryGetValue(itemId, out Item it)) return -1;
        if (it.HeldBy == d.Id) return 1;
        if (it.ReservedBy != d.Id || it.Cell < 0 || it.HeldBy >= 0) return -1;
        int r = Walk(d, it.Cell);
        if (r <= 0) return r;
        PickUp(it, d);
        return 1;
    }

    private void Gain(Dwarf d, Labor l, int xp)
    {
        int before = d.Skill(l);
        d.Xp[(int)l] += xp;
        if (d.Skill(l) > before && d.Skill(l) % 5 == 0) Say($"{d.Name} is now a level {d.Skill(l)} {Tables.LaborName(l).ToLower()} dwarf.", LogKind.Good, d.Cell);
    }

    private bool DoDig(Dwarf d, Task t)
    {
        Desig g = World.Desigs[t.Cell];
        Labor labor = DesigLabor(g) ?? Labor.Mining;
        if (g == Desig.None || Claims.GetValueOrDefault(t.Cell, -1) != d.Id) return false;
        d.Activity = t.Kind == TaskKind.Chop ? "Felling a tree" : t.Kind == TaskKind.Gather ? "Gathering plants" : "Digging";
        // Stage 0: the tool.
        if (t.Stage == 0)
        {
            if (t.Item >= 0)
            {
                d.Activity = labor == Labor.Mining ? "Fetching a pick" : "Fetching an axe";
                int r = Fetch(d, t.Item);
                if (r < 0) return false;
                if (r == 0) return true;
                Item tool = Items[t.Item];
                tool.ReservedBy = -1;
                if (tool.Kind == ItemKind.Pick) d.Pick = tool.Id; else d.Axe = tool.Id;
                t.Item = -1;
            }
            t.Stage = 1;
        }
        if (t.Stage == 1)
        {
            Span<int> spots = stackalloc int[16];
            int n = World.WorkSpots(t.Cell, spots);
            var list = new List<int>(n);
            for (int k = 0; k < n; k++) list.Add(spots[k]);
            int r = list.Count == 0 ? -1 : WalkAny(d, list);
            if (r < 0) { Unreachable[t.Cell] = World.Topology; return false; }
            if (r == 0) return true;
            Mat m = Tables.Mats[World.Mats[t.Cell]];
            t.Total = t.Kind switch
            {
                TaskKind.Chop => 1300,
                TaskKind.Gather => 500,
                _ => (int)((g == Desig.Channel ? 1400 : 1100) * m.Hard),
            };
            t.Stage = 2;
        }
        t.Work += (int)(10 * d.Speed(labor) * (labor == Labor.Mining && Items.TryGetValue(d.Pick, out Item pk) && pk.Rune == "delving" ? 1.6f : 1f));
        if (t.Work < t.Total) return true;
        FinishDig(d, t.Cell, g);
        Gain(d, labor, 30);
        return false;
    }

    private void FinishDig(Dwarf d, int c, Desig g)
    {
        World W = World;
        Mat m = Tables.Mats[W.Mats[c]];
        Shape before = W.Shapes[c];
        W.Desigs[c] = Desig.None;
        Designated.Remove(c);
        Claims.Remove(c);
        // Digging out the cell under a down stair makes the stair's other end: the way down is open.
        if (g == Desig.Dig && c + W.W * W.H < W.N && W.Shapes[c + W.W * W.H] is Shape.StairDown or Shape.StairUD) g = Desig.StairUp;
        // Recutting a one-way stair (or cutting the other way into one) leaves a stair both ways.
        if (before is Shape.StairUp or Shape.StairDown && g is Desig.StairUp or Desig.StairDown or Desig.StairUD) g = Desig.StairUD;
        switch (g)
        {
            case Desig.Dig: W.SetShape(c, Shape.Floor); break;
            case Desig.StairUD: W.SetShape(c, Shape.StairUD); break;
            case Desig.StairUp: W.SetShape(c, Shape.StairUp); break;
            case Desig.StairDown: W.SetShape(c, Shape.StairDown); break;
            case Desig.Channel:
                W.SetShape(c, Shape.Empty);
                int below = c - W.W * W.H;
                if (W.Shapes[below] == Shape.Wall && !W.Has(below, CellFlag.Built)) W.SetShape(below, Shape.Ramp);
                if (W.IsOutside(c + W.W * W.H) || W.IsOutside(c)) { W.Set(c, CellFlag.Outside, true); W.Set(below, CellFlag.Outside, true); }
                // What stood on it falls.
                foreach (Item it in ItemsOn(c).ToList()) Drop(it, W.Walkable(below) ? below : d.Cell);
                foreach (Dwarf o in Living) if (o.Cell == c) { o.Cell = W.Walkable(below) ? below : d.Cell; o.Path = null; }
                break;
            case Desig.Chop:
                W.SetShape(c, Shape.Floor);
                int logs = Rng.Range(2, 4);
                for (int k = 0; k < logs; k++) Spawn(ItemKind.Log, (byte)m.Id, c);
                Count("trees");
                return;
            case Desig.Gather:
                W.Set(c, CellFlag.Shrub, false);
                W.Changed.Add(c);
                byte plant = Tables.Mi(W.IsOutside(c) ? "bramble" : LayerAt(W.Pos(c).Z) == 1 ? "glimmercap" : "glowmoss");
                for (int k = 0; k < Rng.Range(1, 2); k++) Spawn(ItemKind.Plant, plant, c);
                if (Rng.Chance(0.5)) Spawn(ItemKind.Seed, plant, c);
                Count("gathered");
                return;
        }
        if (before is Shape.Wall or Shape.Floor && g is not (Desig.Chop or Desig.Gather) && before == Shape.Wall)
        {
            Count("dug");
            int drop = g == Desig.Channel ? d.Cell : c;
            if (m.Kind == MatKind.Gem)
            {
                Spawn(ItemKind.RoughGem, m.Id, drop);
                AddThought(d, "struck_gems");
                if (d.Has("greedy")) AddThought(d, "struck_gems");
                Say($"{d.FirstName} struck {m.Name}!", LogKind.Good, c);
                // The rock around the gem is ordinary stone.
                W.Mats[c] = W.Mats[c - W.W * W.H];
            }
            else if (m.Kind == MatKind.Ore || (m.Kind == MatKind.Rock && Rng.Chance(0.3))) Spawn(ItemKind.Boulder, m.Id, drop);
        }
        int opened = W.RevealAround(c);
        if (g == Desig.Channel) opened += W.RevealAround(c - W.W * W.H);
        if (opened > 40)
        {
            // Older saves know only the one cavern.
            int layer = Caverns.Length == 0 ? (CavernFound ? -1 : 0) : LayerAt(W.Pos(c).Z);
            if (layer >= 0 && !LayerFound(layer) && !(layer == 0 && CavernFound)) BrokeInto(layer, c);
        }
        if (!RunesKnown && W.Pos(c).Z <= CavernZ) LearnRunes();
    }

    public bool CavernFound;

    private bool DoHaul(Dwarf d, Task t)
    {
        d.Activity = "Hauling";
        if (t.Stage == 0)
        {
            int r = Fetch(d, t.Item);
            if (r < 0) return false;
            if (r == 0) return true;
            d.Carry = t.Item;
            t.Stage = 1;
        }
        if (t.Stage == 1)
        {
            if (!World.Walkable(t.Dest)) return false;
            int r = Walk(d, t.Dest);
            if (r < 0) return false;
            if (r == 0) return true;
            Item it = Items[t.Item];
            StockPromised[t.Dest] = StockPromised.GetValueOrDefault(t.Dest) - 1;
            if (StockPromised[t.Dest] <= 0) StockPromised.Remove(t.Dest);
            it.ReservedBy = -1;
            d.Carry = -1;
            Drop(it, t.Dest);
            t.Dest = -1;
            t.Item = -1;
            Count("hauled");
        }
        return false;
    }

    private bool DoBuild(Dwarf d, Task t)
    {
        Building b = BuildingById(t.Building);
        if (b == null || b.Built || b.Builder != d.Id) return false;
        d.Activity = $"Building {b.D.Name.ToLower()}";
        // Bring each part.
        while (t.Fetch.Count > 0)
        {
            int id = t.Fetch[0];
            int r = Fetch(d, id);
            if (r < 0) return false;
            if (r == 0) return true;
            d.Carry = id;
            var spots = BuildSpots(b);
            int w = spots.Count == 0 ? -1 : WalkAny(d, spots);
            if (w < 0) return false;
            if (w == 0) return true;
            Item it = Items[id];
            it.ReservedBy = -1;
            d.Carry = -1;
            PutIn(it, b);
            t.Fetch.RemoveAt(0);
        }
        if (MissingParts(b).Any()) return false;
        if (t.Stage == 0)
        {
            var spots = BuildSpots(b);
            int w = spots.Count == 0 ? -1 : WalkAny(d, spots);
            if (w < 0) return false;
            if (w == 0) return true;
            // Nobody may stand where a wall goes up.
            if (b.D.Kind == BuildKind.Construction && b.D.Makes != Shape.Floor && Living.Any(o => o.Cell == World.Idx(b.X, b.Y, b.Z))) return true;
            t.Stage = 1;
            t.Total = b.D.Work * 10;
        }
        t.Work += (int)(10 * d.Speed(Labor.Construction));
        if (t.Work < t.Total) return true;
        FinishBuilding(b, d);
        Gain(d, Labor.Construction, 25);
        return false;
    }

    private void FinishBuilding(Building b, Dwarf d)
    {
        var parts = b.Parts.Select(id => Items[id]).ToList();
        b.Mat = parts.Count > 0 ? parts[0].Mat : (byte)0;
        b.Quality = parts.Count > 0 ? parts.Max(p => p.Quality) : 0;
        // An artifact set in place stays itself, inside the furniture.
        Item art = parts.FirstOrDefault(p => p.Artifact);
        foreach (Item it in parts) if (it != art) Destroy(it);
        b.Parts.Clear();
        if (art != null) { b.Artifact = true; b.Label = art.Label; b.Parts.Add(art.Id); art.ReservedBy = -1; }
        b.Builder = -1;
        if (b.D.Kind == BuildKind.Construction)
        {
            int c = World.Idx(b.X, b.Y, b.Z);
            foreach (Item it in ItemsOn(c).ToList()) Drop(it, d.Cell);
            World.SetShape(c, b.D.Makes, b.Mat);
            World.Set(c, CellFlag.Built, true);
            World.Desigs[c] = Desig.None;
            Designated.Remove(c);
            RemoveBuilding(b);
            Count("constructed");
            return;
        }
        b.Built = true;
        Count("buildings");
        if (b.Def == "bridge")
        {
            int bc = World.Idx(b.X, b.Y, b.Z);
            b.OverAir = World.Shapes[bc] == Shape.Empty;
            if (b.OverAir) { World.SetShape(bc, Shape.Floor, b.Mat); World.Set(bc, CellFlag.Built, true); }
        }
        if (b.Def == "door" && b.Locked) World.SetLocked(World.Idx(b.X, b.Y, b.Z), true);
        // Floodgates are built shut.
        if (b.Def == "floodgate") { b.Locked = true; World.SetLocked(World.Idx(b.X, b.Y, b.Z), true); }
        if (b.Def == "trap") b.ArmedAt = Tick;
        if (b.D.Kind == BuildKind.Workshop) Say($"{d.FirstName} finished the {b.D.Name.ToLower()}.", LogKind.Info, World.Idx(b.Center));
    }

    public void RemoveBuilding(Building b)
    {
        Buildings.Remove(b);
        if (b.Def is "door" or "floodgate") { World.SetLocked(World.Idx(b.X, b.Y, b.Z), false); World.Stir(World.Idx(b.X, b.Y, b.Z)); }
        if (b.Def == "bridge" && b.Built)
        {
            int bc = World.Idx(b.X, b.Y, b.Z);
            World.SetLocked(bc, false);
            if (b.OverAir && World.Shapes[bc] == Shape.Floor) { World.SetShape(bc, Shape.Empty); World.Set(bc, CellFlag.Built, false); Fall(bc); }
        }
        if (b.Def == "coffin" && b.Owner >= 0) { Dwarf dead = DwarfById(b.Owner); if (dead != null) dead.Buried = false; }
        for (int y = b.Y; y < b.Y + b.H; y++)
            for (int x = b.X; x < b.X + b.W; x++)
            {
                int c = World.Idx(x, y, b.Z);
                BuildingAt.Remove(c);
                World.Changed.Add(c);
            }
        foreach (Dwarf o in Dwarves) if (o.Bed == b.Id) o.Bed = -1;
    }

    private bool DoCraft(Dwarf d, Task t)
    {
        Building b = BuildingById(t.Building);
        Order o = b?.Orders.FirstOrDefault(x => x.Id == t.Order);
        if (b == null || o == null || o.Worker != d.Id) return false;
        Recipe r = Tables.Recipe(o.Recipe);
        d.Activity = $"{r.Name}";
        int center = World.Idx(b.Center);
        while (t.Fetch.Count > 0)
        {
            int id = t.Fetch[0];
            int f = Fetch(d, id);
            if (f < 0) return false;
            if (f == 0) return true;
            d.Carry = id;
            int w = Walk(d, center);
            if (w < 0) return false;
            if (w == 0) return true;
            Item it = Items[id];
            d.Carry = -1;
            PutIn(it, b);
            it.ReservedBy = d.Id;
            t.Fetch.RemoveAt(0);
        }
        if (t.Stage == 0)
        {
            int w = Walk(d, center);
            if (w < 0) return false;
            if (w == 0) return true;
            t.Stage = 1;
            t.Total = r.Work * 10;
        }
        t.Work += (int)(10 * d.Speed(r.Labor) * (r.Shop == "runeforge" && SleeperShrine ? 2 : 1));
        if (t.Work < t.Total) return true;
        var inputs = t.Note.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Where(Items.ContainsKey).Select(id => Items[id]).ToList();
        if (inputs.Count == 0) return false;
        Item first = inputs[0];
        foreach (Output outp in r.Outputs)
        {
            byte mat = first.Mat;
            if (outp.Mat != null) mat = Tables.Mi(outp.Mat);
            else if (outp.Kind == ItemKind.Bar && first.M.Yields != null) mat = Tables.Mi(first.M.Yields);
            else if (first.Kind == ItemKind.Bar) mat = first.Mat;
            bool fine = outp.Kind is not (ItemKind.Bar or ItemKind.Charcoal or ItemKind.Seed or ItemKind.Drink or ItemKind.Block);
            for (int k = 0; k < outp.Count; k++)
            {
                int q = fine ? RollQuality(d, r.Labor) : 0;
                Item made = Spawn(outp.Kind, mat, center, q);
                made.Maker = d.Name;
                if (q >= 3) AddThought(d, "good_work");
                if (q >= 5) Say($"{d.Name} made a masterful {made.Name.Replace("masterful ", "")}!", LogKind.Good, center);
            }
        }
        if (r.Id == "butcher") Butcher(first, center);
        if (r.Shop == "runeforge") CarveRune(r, inputs, center, d);
        foreach (Item it in inputs) Destroy(it);
        Gain(d, r.Labor, 40);
        Count("crafted");
        Count("made:" + r.Id);
        o.Worker = -1;
        if (!o.Repeat && --o.Left <= 0)
        {
            b.Orders.Remove(o);
            Say($"{d.FirstName} has finished the order at the {b.D.Name.ToLower()}: {r.Name.ToLower()}.", LogKind.Good, center);
        }
        return false;
    }

    private int RollQuality(Dwarf d, Labor l)
    {
        int s = d.Skill(l) + Rng.Range(-3, 3) + (d.Has("artistic") ? 2 : 0);
        return Math.Clamp(s / 3, 0, 5);
    }

    private bool DoEat(Dwarf d, Task t)
    {
        d.Activity = "Eating";
        if (t.Stage == 0)
        {
            int r = Fetch(d, t.Item);
            if (r < 0) return false;
            if (r == 0) return true;
            d.Carry = t.Item;
            // A chair at a table, if one is free and not far.
            Building chair = Buildings.Where(b => b.Built && b.Def == "chair" && !Seats.ContainsKey(b.Id) && HasTableBeside(b))
                .OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(World.Pos(d.Cell))).FirstOrDefault();
            if (chair != null && new P3(chair.X, chair.Y, chair.Z).Dist(World.Pos(d.Cell)) < 60) { Seats[chair.Id] = d.Id; t.Building = chair.Id; }
            t.Stage = 1;
        }
        if (t.Stage == 1)
        {
            if (t.Building >= 0)
            {
                Building chair = BuildingById(t.Building);
                if (chair == null) { t.Building = -1; }
                else
                {
                    int w = Walk(d, World.Idx(chair.X, chair.Y, chair.Z));
                    if (w < 0) { Seats.Remove(t.Building); t.Building = -1; }
                    else if (w == 0) return true;
                }
            }
            t.Stage = 2;
            t.Total = 60;
        }
        if (++t.Work < t.Total) return true;
        Item food = Items[t.Item];
        d.Carry = -1;
        bool meal = food.Kind == ItemKind.Meal;
        Destroy(food);
        t.Item = -1;
        d.Hunger = meal ? 0 : Math.Max(0, d.Hunger - 2200);
        if (d.Hunger <= Tables.HungryAt) d.Thoughts.RemoveAll(x => x.Id == "hungry");
        AddThought(d, meal ? "ate_meal" : "ate_raw", meal && d.Has("glutton") ? 2 : 1);
        if (food.Kind == ItemKind.Meat) AddThought(d, "ate_meat");
        if (t.Building >= 0) { AddThought(d, "ate_table"); Seats.Remove(t.Building); t.Building = -1; }
        else if (Buildings.Any(b => b.Built && b.Def == "table")) AddThought(d, "ate_floor");
        Count("meals");
        return false;
    }

    private bool HasTableBeside(Building chair)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) != 1 || !World.In(chair.X + dx, chair.Y + dy, chair.Z)) continue;
                Building b = BuildingOn(World.Idx(chair.X + dx, chair.Y + dy, chair.Z));
                if (b != null && b.Built && b.Def == "table") return true;
            }
        return false;
    }

    private bool DoDrink(Dwarf d, Task t)
    {
        d.Activity = "Drinking";
        if (t.Stage == 0)
        {
            if (t.Item >= 0)
            {
                int r = Fetch(d, t.Item);
                if (r < 0) return false;
                if (r == 0) return true;
                d.Carry = t.Item;
            }
            else
            {
                // Water: a well if there is one near, else any cell beside a pond, stream or cave pool.
                var p = (Buildings.Any(b => b.Built && b.Def == "well") ? Path.Nearest(d.Cell, BesideWell, 12000) : null) ?? Path.Nearest(d.Cell, BesideWater, 30000);
                if (p == null) { d.NoWaterUntil = Tick + 600; return false; }
                if (p.Count > 1) { SetPath(d, p); t.Stage = 1; return true; }
            }
            t.Stage = 1;
        }
        // In a hard winter the ice must be broken first: three times as long, and cold comfort.
        if (t.Stage == 1) { t.Stage = 2; t.Total = t.Item < 0 && Frozen(d.Cell) ? 120 : 40; }
        if (++t.Work < t.Total) return true;
        if (t.Item >= 0)
        {
            Item it = Items[t.Item];
            d.Carry = -1;
            Destroy(it);
            t.Item = -1;
            AddThought(d, "drank_ale", d.Has("alelover") ? 2 : 1);
            d.Thoughts.RemoveAll(x => x.Id == "no_ale");
        }
        else
        {
            if (!BesideWater(d.Cell)) return false;
            AddThought(d, WaterFouled ? "foul_water" : Frozen(d.Cell) && !BesideWell(d.Cell) ? "drank_ice" : BesideWell(d.Cell) ? "drank_well" : "drank_water");
        }
        d.Thirst = 0;
        d.Thoughts.RemoveAll(x => x.Id == "thirsty");
        Count("drinks");
        return false;
    }

    public bool BesideWater(int c)
    {
        P3 p = World.Pos(c);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!World.In(p.X + dx, p.Y + dy, p.Z)) continue;
                int j = World.Idx(p.X + dx, p.Y + dy, p.Z);
                if (World.WaterAt(j)) return true;
                if (World.Shapes[j] == Shape.Empty && p.Z > 0 && World.WaterAt(j - World.W * World.H)) return true;
            }
        return _anyWell && BesideWell(c);
    }

    private bool _anyWell => Buildings.Any(b => b.Built && b.Def == "well");

    public bool BesideWell(int c)
    {
        P3 p = World.Pos(c);
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (World.In(p.X + dx, p.Y + dy, p.Z) && BuildingOn(World.Idx(p.X + dx, p.Y + dy, p.Z)) is { Built: true, Def: "well" }) return true;
        return false;
    }

    private bool DoSleep(Dwarf d, Task t)
    {
        if (t.Stage == 0)
        {
            d.Activity = "Going to bed";
            Building bed = d.Bed >= 0 ? BuildingById(d.Bed) : null;
            if ((bed == null || !bed.Built) && Races.Of(d).NoBed) { t.Stage = 1; return true; }
            if (bed == null || !bed.Built)
            {
                d.Bed = -1;
                bed = Buildings.Where(b => b.Built && b.Def == "bed" && b.Owner < 0).OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(World.Pos(d.Cell))).FirstOrDefault();
                if (bed != null) { bed.Owner = d.Id; d.Bed = bed.Id; SayBedClaimed(d, bed); }
            }
            if (bed != null)
            {
                int w = Walk(d, World.Idx(bed.X, bed.Y, bed.Z));
                if (w == 0) return true;
                t.Building = w > 0 ? bed.Id : -1;
            }
            t.Stage = 1;
        }
        d.Activity = "Sleeping";
        d.Fatigue -= Tables.SleepRecover;
        // Wake for a pressing need.
        if (d.Thirst > Tables.ParchedAt || d.Hunger > Tables.StarvingAt) { d.Fatigue = Math.Max(0, d.Fatigue); return false; }
        if (d.Fatigue > 0) return true;
        d.Fatigue = 0;
        if (t.Building >= 0) AddThought(d, d.Bed == t.Building ? "slept_own" : "slept_bed");
        else if (!Races.Of(d).NoBed) AddThought(d, "slept_floor");
        return false;
    }

    private bool DoFarm(Dwarf d, Task t)
    {
        d.Activity = t.Kind == TaskKind.Plant ? "Planting" : "Harvesting";
        if (!Crops.TryGetValue(t.Cell, out Crop cr) || cr.Claim != d.Id) return false;
        if (t.Stage == 0 && t.Kind == TaskKind.Plant)
        {
            int r = Fetch(d, t.Item);
            if (r < 0) return false;
            if (r == 0) return true;
            d.Carry = t.Item;
            t.Stage = 1;
        }
        if (t.Stage <= 1)
        {
            int w = Walk(d, t.Cell);
            if (w < 0) return false;
            if (w == 0) return true;
            t.Stage = 2;
            t.Total = 400;
        }
        t.Work += (int)(10 * d.Speed(Labor.Farming));
        if (t.Work < t.Total) return true;
        if (t.Kind == TaskKind.Plant)
        {
            Destroy(Items[t.Item]);
            d.Carry = -1;
            t.Item = -1;
            cr.Planted = Tick;
            cr.Claim = -1;
            Count("planted");
        }
        else
        {
            byte m = Tables.Mi(cr.Mat);
            int n = 2 + (d.Skill(Labor.Farming) >= 5 ? 1 : 0) + (Rng.Chance(0.3) ? 1 : 0);
            for (int k = 0; k < n; k++) Spawn(ItemKind.Plant, m, t.Cell);
            Spawn(ItemKind.Seed, m, t.Cell);
            Crops.Remove(t.Cell);
            Count("harvested");
        }
        Gain(d, Labor.Farming, 20);
        return false;
    }

    private bool DoTantrum(Dwarf d, Task t)
    {
        d.Activity = "Throwing a tantrum";
        if (d.Breaking <= 0)
        {
            d.Stress = Math.Max(0, d.Stress - 1800);
            AddThought(d, "tantrum_after");
            return false;
        }
        if (Rng.Chance(0.02))
        {
            Item victim = ItemsOn(d.Cell).FirstOrDefault(i => i.Kind is ItemKind.Craft or ItemKind.Chair or ItemKind.Table or ItemKind.Barrel && i.ReservedBy < 0);
            if (victim != null) { Say($"{d.FirstName} smashed {victim.Name} in a rage.", LogKind.Bad, d.Cell); Destroy(victim); }
        }
        int dest = WanderSpot(d, World.Pos(d.Cell), 6);
        if (Walk(d, dest) < 0) return true;
        return true;
    }

    private bool DoWander(Dwarf d, Task t)
    {
        d.Activity = "Idle";
        if (t.Stage == 0)
        {
            int w = Walk(d, t.Dest);
            if (w == 0) { t.Stage = 1; return true; }
            t.Stage = 1;
        }
        // Chat with whoever is near.
        if (t.Work == 0)
        {
            Admire(d);
            Dwarf o = Living.FirstOrDefault(x => x != d && x.Task?.Kind == TaskKind.Wander && x.Path == null && World.Pos(x.Cell).Dist(World.Pos(d.Cell)) <= 2);
            if (o != null && !d.Has("loner") && Rng.Chance(d.Has("sociable") ? 0.6 : 0.3)) Chat(d, o);
            else if (Creatures.Any(c => !c.Dead && c.Tame && Adjacent(c.Cell, d.Cell))) { AddThought(d, "petted"); d.Activity = "Petting an animal"; }
        }
        // Idle dwarves pick up new work quickly.
        if (++t.Work >= t.Total || (t.Work % 25 == 0 && (Designated.Count > 0 || Loose.Count > 0))) return false;
        return true;
    }

    private void Chat(Dwarf a, Dwarf b)
    {
        ClanDef ca = Tables.Clan(a.Clan);
        bool feud = ca.Grudge == b.Clan;
        int delta = feud ? -(a.Has("grudgeful") || b.Has("grudgeful") ? 12 : 6) : 8;
        a.Opinion[b.Id] = Math.Clamp(a.Opinion.GetValueOrDefault(b.Id) + delta, -100, 100);
        b.Opinion[a.Id] = Math.Clamp(b.Opinion.GetValueOrDefault(a.Id) + delta, -100, 100);
        if (feud) { AddThought(a, "feud"); AddThought(b, "feud"); }
        else { AddThought(a, "chatted"); AddThought(b, "chatted"); }
        a.Activity = b.Activity = feud ? "Arguing" : "Chatting";
        if (feud && Brawls(a, b)) Brawl(a, b);
        if (a.Opinion[b.Id] == 100 && delta > 0 && !_friends.Contains((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id))))
        {
            _friends.Add((Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)));
            Say($"{a.FirstName} and {b.FirstName} have become fast friends.", LogKind.Good, a.Cell);
        }
        if (a.Opinion[b.Id] == -100 && delta < 0 && Rng.Chance(0.3)) Say($"{a.FirstName} and {b.FirstName} cannot stand each other.", LogKind.Bad, a.Cell);
    }

    private readonly HashSet<(int, int)> _friends = new();

    /// <summary>Give back everything a dwarf's task held: claims, reservations, the thing in hand.</summary>
    public void Release(Dwarf d)
    {
        Task t = d.Task;
        d.Path = null;
        Stepping.Remove(d.Id);
        if (t == null) return;
        foreach (var kv in Claims.Where(kv => kv.Value == d.Id).ToList()) Claims.Remove(kv.Key);
        if (t.Kind == TaskKind.Haul && t.Dest >= 0 && StockPromised.TryGetValue(t.Dest, out int n))
        {
            if (n <= 1) StockPromised.Remove(t.Dest); else StockPromised[t.Dest] = n - 1;
        }
        foreach (Item it in Items.Values.Where(i => i.ReservedBy == d.Id).ToList())
        {
            it.ReservedBy = -1;
            if (it.HeldBy == d.Id && it.Id != d.Pick && it.Id != d.Axe) Drop(it, d.Cell);
        }
        if (d.Carry >= 0 && Items.TryGetValue(d.Carry, out Item c) && c.HeldBy == d.Id) Drop(c, d.Cell);
        d.Carry = -1;
        foreach (Building b in Buildings)
        {
            if (b.Builder == d.Id) b.Builder = -1;
            if (b.Claim == d.Id) b.Claim = -1;
            foreach (Order o in b.Orders) if (o.Worker == d.Id) o.Worker = -1;
        }
        foreach (var kv in Seats.Where(kv => kv.Value == d.Id).ToList()) Seats.Remove(kv.Key);
        foreach (Creature cr in Creatures) if (cr.Claim == d.Id) cr.Claim = -1;
        if (t.Kind == TaskKind.Equip && t.Item >= 0 && Items.TryGetValue(t.Item, out Item eq) && eq.HeldBy == d.Id) Drop(eq, d.Cell);
        foreach (var kv in Crops.Where(kv => kv.Value.Claim == d.Id).ToList())
        {
            if (kv.Value.Planted > Tick) Crops.Remove(kv.Key);
            else kv.Value.Claim = -1;
        }
    }

    private void Growth()
    {
        // Shrubs creep back over the grass.
        for (int k = 0; k < 4; k++)
        {
            int x = Rng.Int(World.W), y = Rng.Int(World.H);
            for (int z = World.D - 1; z > 0; z--)
            {
                int i = World.Idx(x, y, z);
                if (World.Shapes[i] == Shape.Empty) continue;
                if (World.Shapes[i] == Shape.Floor && World.Has(i, CellFlag.Grass) && !World.Has(i, CellFlag.Shrub) && !BuildingAt.ContainsKey(i) && Rng.Chance(0.1))
                { World.Set(i, CellFlag.Shrub, true); World.Changed.Add(i); }
                break;
            }
        }
    }

    // ---- what the player asks for -----------------------------------------------------

    /// <summary>Designate (or clear, with <see cref="Desig.None"/>) every suitable cell in a box. Returns how many changed.</summary>
    public int Designate(P3 a, P3 b, Desig g)
    {
        int n = 0;
        for (int z = Math.Min(a.Z, b.Z); z <= Math.Max(a.Z, b.Z); z++)
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                {
                    if (!World.In(x, y, z)) continue;
                    int c = World.Idx(x, y, z);
                    if (g == Desig.None)
                    {
                        if (World.Desigs[c] == Desig.None) continue;
                        World.Desigs[c] = Desig.None;
                        Designated.Remove(c);
                        if (Claims.TryGetValue(c, out int who)) { Claims.Remove(c); Dwarf dw = DwarfById(who); if (dw != null) { Release(dw); dw.Task = null; } }
                        World.Changed.Add(c);
                        n++;
                        continue;
                    }
                    // Hidden cells can be dug into blind, but nothing else is designated sight unseen.
                    if (World.Has(c, CellFlag.Hidden) && g is not (Desig.Dig or Desig.StairUD or Desig.StairDown or Desig.StairUp or Desig.Channel)) continue;
                    if (!World.CanDesignate(c, g)) continue;
                    if (World.Desigs[c] == g) continue;
                    // A dig box drawn over a marked shaft keeps the stairs: they are the way down.
                    if (g == Desig.Dig && World.Desigs[c] is Desig.StairUD or Desig.StairUp or Desig.StairDown) continue;
                    World.Desigs[c] = g;
                    Designated.Add(c);
                    World.Changed.Add(c);
                    n++;
                }
        return n;
    }

    /// <summary>Whether any designation in the box can be worked from somewhere a living dwarf can walk to.</summary>
    public bool AnyReachable(P3 a, P3 b)
    {
        World.EnsureComponents();
        var comps = new HashSet<int>(Living.Select(d => World.Comp[d.Cell]));
        Span<int> spots = stackalloc int[16];
        for (int z = Math.Min(a.Z, b.Z); z <= Math.Max(a.Z, b.Z); z++)
            for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); y++)
                for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); x++)
                {
                    if (!World.In(x, y, z)) continue;
                    int c = World.Idx(x, y, z);
                    if (World.Desigs[c] == Desig.None) continue;
                    int n = World.WorkSpots(c, spots);
                    for (int k = 0; k < n; k++) if (comps.Contains(World.Comp[spots[k]])) return true;
                    // A stair dug down from above counts once the stair above it is reachable or designated.
                    if (z + 1 < World.D && World.Desigs[c + World.W * World.H] is Desig.StairDown or Desig.StairUD) return true;
                }
        return false;
    }

    /// <summary>Why a building cannot go here, or null when it can.</summary>
    public string CanPlace(string def, int x, int y, int z)
    {
        BuildDef bd = Tables.Build[def];
        if (def == "runeforge" && !RunesKnown) return "the old runes are not yet known -- dig deeper";
        for (int yy = y; yy < y + bd.H; yy++)
            for (int xx = x; xx < x + bd.W; xx++)
            {
                if (!World.In(xx, yy, z)) return "off the map";
                int c = World.Idx(xx, yy, z);
                if (World.Has(c, CellFlag.Hidden)) return "unexplored";
                if (BuildingAt.ContainsKey(c)) return "something is built there";
                Shape s = World.Shapes[c];
                if (bd.Kind == BuildKind.Construction)
                {
                    if (bd.Makes == Shape.Floor) { if (s != Shape.Empty) return "floors go over open air"; }
                    else if (s != Shape.Floor) return Blocked(s);
                    continue;
                }
                if (def == "bridge") { if (s is not (Shape.Floor or Shape.Empty)) return "a drawbridge goes on a floor or over a gap"; continue; }
                if (def == "well") { if (s != Shape.Empty || !WaterBelow(c)) return "a well goes over open air with water a few levels below"; continue; }
                if (World.Depth[c] >= World.Deep) return "under deep liquid";
                if (s != Shape.Floor) return Blocked(s);
            }
        if (def is "msmelter" or "mforge" && !MagmaNear(x, y, z, bd.W, bd.H)) return "needs magma close by (beside it, or one level down)";
        return null;
    }

    /// <summary>Why a cell will not take a building, in terms of what to do about it.</summary>
    private static string Blocked(Shape s) => s switch
    {
        Shape.Tree => "a tree is in the way -- fell it first (T)",
        Shape.Wall => "solid rock is in the way -- dig it out first (D)",
        Shape.Ramp => "the ground slopes there -- it needs level floor",
        Shape.Empty => "open air -- the ground is lower there (try the level below)",
        Shape.StairUp or Shape.StairDown or Shape.StairUD => "stairs are in the way",
        Shape.Water or Shape.Magma => "liquid is in the way",
        _ => "needs open floor",
    };

    public Building Place(string def, int x, int y, int z)
    {
        if (CanPlace(def, x, y, z) != null) return null;
        var b = new Building { Id = NextId++, Def = def, X = x, Y = y, Z = z };
        if (def == "shrine") b.Deity = DefaultDeity(z);
        Buildings.Add(b);
        IndexBuilding(b);
        for (int yy = y; yy < y + b.H; yy++) for (int xx = x; xx < x + b.W; xx++) World.Changed.Add(World.Idx(xx, yy, z));
        // Nothing should be stored on a building.
        foreach (int c in Enumerable.Range(0, b.W * b.H).Select(k => World.Idx(x + k % b.W, y + k / b.W, z)))
            foreach (Item it in ItemsOn(c).Where(i => i.InBuilding < 0)) RefreshStored(it);
        return b;
    }

    /// <summary>Take a building down (or cancel one planned): its materials go back on the floor.</summary>
    public void Deconstruct(Building b)
    {
        if (b.Builder >= 0) { Dwarf d = DwarfById(b.Builder); if (d != null) { Release(d); d.Task = null; } }
        foreach (Order o in b.Orders) if (o.Worker >= 0) { Dwarf d = DwarfById(o.Worker); if (d != null) { Release(d); d.Task = null; } }
        int c = World.Idx(b.Center);
        if (!World.Walkable(c)) c = FindStandable(b.Center, 2);
        foreach (int id in b.Parts.ToList()) if (Items.TryGetValue(id, out Item it)) { it.ReservedBy = -1; Drop(it, c); }
        b.Parts.Clear();
        if (b.Built && !b.Artifact)
        {
            ItemKind? back = Tables.Items.Values.FirstOrDefault(i => i.Places == b.Def)?.Kind;
            if (back != null) Spawn(back.Value, b.Mat, c, b.Quality);
            else if (b.D.Kind == BuildKind.Workshop) Spawn(Tables.Mats[b.Mat].Kind == MatKind.Wood ? ItemKind.Log : ItemKind.Boulder, b.Mat, c);
        }
        RemoveBuilding(b);
        foreach (Item it in ItemsOn(c).ToList()) RefreshStored(it);
    }

    public Zone AddZone(ZoneKind kind, P3 a, P3 b, string crop = null)
    {
        var z = new Zone
        {
            Id = NextId++, Kind = kind, Z = a.Z,
            X0 = Math.Min(a.X, b.X), X1 = Math.Max(a.X, b.X), Y0 = Math.Min(a.Y, b.Y), Y1 = Math.Max(a.Y, b.Y),
            Crop = crop,
            Cats = StockCat.All & ~StockCat.Refuse,
        };
        z.Name = kind switch { ZoneKind.Stockpile => $"Stockpile #{Zones.Count(q => q.Kind == ZoneKind.Stockpile) + 1}", ZoneKind.Farm => $"Farm plot #{Zones.Count(q => q.Kind == ZoneKind.Farm) + 1}", ZoneKind.Pasture => $"Pasture #{Zones.Count(q => q.Kind == ZoneKind.Pasture) + 1}", _ => "Meeting hall" };
        if (kind == ZoneKind.Meeting) Zones.RemoveAll(q => q.Kind == ZoneKind.Meeting);
        Zones.Add(z);
        RefreshStored();
        return z;
    }

    public void RemoveZone(Zone z)
    {
        Zones.Remove(z);
        if (z.Kind == ZoneKind.Farm) foreach (P3 p in z.Cells()) { int c = World.Idx(p); if (Crops.TryGetValue(c, out Crop cr) && cr.Claim < 0) Crops.Remove(c); }
        RefreshStored();
    }

    public Zone ZoneAt(int x, int y, int z) => Zones.LastOrDefault(q => q.Contains(x, y, z));

    public Order AddOrder(Building b, string recipe, int count, bool repeat = false, bool first = false)
    {
        var o = new Order { Id = NextId++, Recipe = recipe, Left = count, Repeat = repeat };
        if (first) b.Orders.Insert(0, o); else b.Orders.Add(o);
        return o;
    }
}
