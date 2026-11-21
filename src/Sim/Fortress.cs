using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>
/// The whole game as plain data: the map, the dwarves, the items, buildings,
/// zones and the log, and the tick that moves it all. A save is this as JSON.
/// No Godot types anywhere below here.
/// </summary>
public sealed partial class Fortress
{
    public string Name;
    public ulong Seed;
    public long Tick;
    [JsonIgnore] public int Year => (int)(Tick / Tables.TicksPerYear) + 1;
    [JsonIgnore] public int Season => (int)(Tick / Tables.TicksPerSeason % Tables.SeasonsPerYear);
    [JsonIgnore] public int Day => (int)(Tick / Tables.TicksPerDay % Tables.DaysPerSeason) + 1;
    [JsonIgnore] public string Date => $"{Day} {Tables.Seasons[Season]}, year {Year}";
    public static string ShortDate(long tick) => $"{tick / Tables.TicksPerDay % Tables.DaysPerSeason + 1} {Tables.Seasons[(int)(tick / Tables.TicksPerSeason % Tables.SeasonsPerYear)]}";
    [JsonIgnore] public float DayFraction => Tick % Tables.TicksPerDay / (float)Tables.TicksPerDay;

    [JsonIgnore] public World World;
    [JsonIgnore] public Pathing Path;
    public Rng Rng;
    public P3 Embark;
    public int CavernZ;

    public List<Dwarf> Dwarves = new();
    public Dictionary<int, Item> Items = new();
    public List<Building> Buildings = new();
    public List<Zone> Zones = new();
    public Dictionary<int, Crop> Crops = new();
    public List<LogEntry> Log = new();
    public int NextId = 1;
    public bool Fallen;
    /// <summary>The guided first fortress: which goal is next (-1: no tutorial).</summary>
    public int TutorialStep = -1;
    public string FallenWhy;
    public Dictionary<string, int> Stats = new();

    /// <summary>Designation cells taken by a dwarf (cell -> dwarf id).</summary>
    public Dictionary<int, int> Claims = new();
    /// <summary>Stockpile cells promised to a hauler (cell -> count).</summary>
    public Dictionary<int, int> StockPromised = new();
    /// <summary>Designations a dwarf could not reach, by the topology they failed at.</summary>
    [JsonIgnore] public Dictionary<int, int> Unreachable = new();

    // Derived lookups, rebuilt on load.
    [JsonIgnore] public Dictionary<int, List<int>> ItemsAt = new();
    [JsonIgnore] public HashSet<int> Designated = new();
    [JsonIgnore] public HashSet<int> Loose = new();
    /// <summary>Set by the sim when something wants the player's attention: the UI pauses.</summary>
    [JsonIgnore] public string PauseFor;
    /// <summary>Building id at each cell, for the view and for placement.</summary>
    [JsonIgnore] public Dictionary<int, int> BuildingAt = new();

    [JsonIgnore] public int Alive => Dwarves.Count(d => !d.Dead);
    [JsonIgnore] public IEnumerable<Dwarf> Living => Dwarves.Where(d => !d.Dead);

    /// <summary>Where a tick's time goes (stopwatch ticks): dwarves, creatures, fluids.</summary>
    [JsonIgnore] public readonly long[] Timing = new long[3];
    [JsonIgnore] public long CreatureNodes, CreatureSearches;

    public void Count(string stat, int n = 1) => Stats[stat] = Stats.GetValueOrDefault(stat) + n;

    // ---- making one ---------------------------------------------------------------------

    public static Fortress New(string name, ulong seed, int w = WorldGen.DefaultW, int h = WorldGen.DefaultH, int d = WorldGen.DefaultD, ulong worldSeed = 0, int ex = -1, int ey = -1)
    {
        WorldMap map = worldSeed != 0 ? new WorldMap(worldSeed) : null;
        EmbarkLand land = map != null && ex >= 0 ? map.Land(ex, ey) : null;
        WorldGen.Result gen = WorldGen.Generate(seed, w, h, d, land);
        var f = new Fortress { Name = name, Seed = seed, Rng = new Rng(seed * 31 + 7), Embark = gen.Embark, CavernZ = gen.CavernZ, Caverns = gen.Caverns, Ruin = gen.Ruin, WorldSeed = worldSeed, WorldX = ex, WorldY = ey };
        f.SetWorld(map);
        f.Attach(gen.World);
        f.Settle();
        f.Livestock();
        f.SpawnWildlife();
        f.FurnishRuin();
        return f;
    }

    /// <summary>Hook a map up: pathing and the derived lookups.</summary>
    public void Attach(World w)
    {
        World = w;
        Path = new Pathing(w);
        RebuildIndexes();
    }

    public void RebuildIndexes()
    {
        ItemsAt.Clear(); Loose.Clear(); Designated.Clear(); BuildingAt.Clear();
        foreach (Item it in Items.Values) if (it.Cell >= 0 && it.HeldBy < 0) AddAt(it);
        for (int i = 0; i < World.N; i++) if (World.Desigs[i] != Desig.None) Designated.Add(i);
        foreach (Building b in Buildings)
        {
            IndexBuilding(b);
            if (b.Def is "door" or "floodgate" && b.Built && b.Locked) World.Locked.Add(World.Idx(b.X, b.Y, b.Z));
            if (b.Def == "bridge" && b.Built && b.Raised && !b.OverAir) World.Locked.Add(World.Idx(b.X, b.Y, b.Z));
        }
        RefreshStored();
    }

    private void IndexBuilding(Building b)
    {
        for (int y = b.Y; y < b.Y + b.H; y++)
            for (int x = b.X; x < b.X + b.W; x++)
                BuildingAt[World.Idx(x, y, b.Z)] = b.Id;
    }

    /// <summary>The seven founders and their wagon's load.</summary>
    private void Settle()
    {
        var clans = Tables.Clans.Where(c => c.Race == "dwarf").Select(c => c.Id).ToList();
        Rng.Shuffle(clans);
        // Two or three clans in the founding party: enough for a feud.
        for (int k = 0; k < 7; k++) AddDwarf(clans[k % 3], Embark, founder: true);
        int c0 = World.Idx(Embark);
        void Put(ItemKind kind, string mat, int n)
        {
            for (int i = 0; i < n; i++) SpawnNear(kind, Tables.Mi(mat), Embark, 3);
        }
        Put(ItemKind.Pick, "copper", 3);
        Put(ItemKind.Axe, "copper", 2);
        Put(ItemKind.Plant, "ration", 40);
        Put(ItemKind.Drink, "dimcap", 50);
        Put(ItemKind.Seed, "dimcap", 12);
        Put(ItemKind.Seed, "hearthwheat", 8);
        Put(ItemKind.Seed, "bramble", 6);
        Put(ItemKind.Log, "oak", 10);
        Put(ItemKind.Bar, "copper", 2);
        Put(ItemKind.Boulder, "coal", 3);
        Say($"The expedition reaches the site of {Name}. Strike the earth!", LogKind.Good, c0);
    }

    public Dwarf AddDwarf(string clan, P3 near, bool founder = false)
    {
        ClanDef cl = Tables.Clan(clan);
        RaceDef race = Races.Of(cl.Race);
        var d = new Dwarf { Id = NextId++, Clan = clan, Race = race.Key, Female = Rng.Chance(0.5), Age = race.Key == "stonekin" ? Rng.Range(60, 300) : race.Key == "dwarf" ? Rng.Range(30, 140) : Rng.Range(15, 60), Arrived = Tick, LastSky = Tick };
        d.Hp = d.MaxHp;
        d.Name = race.NameA == null ? $"{Rng.Pick(Tables.NameA)}{Rng.Pick(Tables.NameB)} {cl.Name}" : $"{Rng.Pick(race.NameA)}{Rng.Pick(race.NameB)} {cl.Name}";
        // Skills: the clan's crafts, plus a couple more.
        ClanDef cd = Tables.Clan(clan);
        foreach (Labor l in cd.Leans) if (Rng.Chance(0.6)) d.Xp[(int)l] = Rng.Range(4, 25) * 40;
        for (int k = 0; k < 2; k++) d.Xp[(int)Rng.Pick(Tables.AllLabors)] += Rng.Range(1, 10) * 40;
        if (founder) d.Xp[(int)Labor.Mining] = Math.Max(d.Xp[(int)Labor.Mining], Rng.Range(0, 9) * 40);
        var traits = Tables.Traits.Select(t => t.Id).ToList();
        Rng.Shuffle(traits);
        d.Traits.AddRange(traits.Take(Rng.Range(1, 3)));
        if (d.Has("hardworking") && d.Has("lazy")) d.Traits.Remove("lazy");
        if (d.Has("sociable") && d.Has("loner")) d.Traits.Remove("loner");
        var likeable = Tables.Mats.Where(m => m.Kind is MatKind.Rock or MatKind.Gem or MatKind.Metal).ToList();
        d.LikesMat = Rng.Pick(likeable).Key;
        d.Hunger = Rng.Range(0, 900); d.Thirst = Rng.Range(0, 600); d.Fatigue = Rng.Range(0, 900);
        d.Cell = FindStandable(near, 4);
        d.NextThink = Tick + Rng.Int(20);
        Dwarves.Add(d);
        return d;
    }

    /// <summary>A walkable cell near <paramref name="p"/> (searching outward), or the cell itself.</summary>
    public int FindStandable(P3 p, int radius)
    {
        // Somewhere random within the radius first, so things scatter; then outward.
        World.EnsureComponents();
        for (int t = 0; t < 40; t++)
        {
            int x = p.X + Rng.Range(-radius, radius), y = p.Y + Rng.Range(-radius, radius);
            for (int dz = -1; dz <= 1; dz++)
                if (World.Walkable(new P3(x, y, p.Z + dz)) && World.Comp[World.Idx(x, y, p.Z + dz)] == World.Comp[World.Idx(p)]) return World.Idx(x, y, p.Z + dz);
        }
        for (int r = 0; r <= radius + 8; r++)
            for (int t = 0; t < 12; t++)
            {
                int x = p.X + Rng.Range(-r, r), y = p.Y + Rng.Range(-r, r);
                for (int dz = 0; dz <= 2; dz++)
                    foreach (int z in new[] { p.Z + dz, p.Z - dz })
                        if (World.Walkable(new P3(x, y, z))) return World.Idx(x, y, z);
            }
        return World.Idx(p);
    }

    // ---- items --------------------------------------------------------------------------

    public Item Spawn(ItemKind kind, byte mat, int cell, int quality = 0)
    {
        var it = new Item { Id = NextId++, Kind = kind, Mat = mat, Cell = cell, Quality = quality };
        Items[it.Id] = it;
        AddAt(it);
        RefreshStored(it);
        return it;
    }

    public Item SpawnNear(ItemKind kind, byte mat, P3 p, int radius) => Spawn(kind, mat, FindStandable(p, radius));

    private void AddAt(Item it)
    {
        if (!ItemsAt.TryGetValue(it.Cell, out var list)) ItemsAt[it.Cell] = list = new List<int>();
        list.Add(it.Id);
    }

    private void RemoveAt(Item it)
    {
        if (it.Cell >= 0 && ItemsAt.TryGetValue(it.Cell, out var list))
        {
            list.Remove(it.Id);
            if (list.Count == 0) ItemsAt.Remove(it.Cell);
        }
    }

    public IEnumerable<Item> ItemsOn(int cell)
    {
        if (!ItemsAt.TryGetValue(cell, out var list)) yield break;
        foreach (int id in list) yield return Items[id];
    }

    public void PickUp(Item it, Dwarf d)
    {
        RemoveAt(it);
        it.Cell = -1;
        it.HeldBy = d.Id;
        it.InBuilding = -1;
        it.Stored = false;
        Loose.Remove(it.Id);
    }

    public void Drop(Item it, int cell)
    {
        if (it.HeldBy > 0)
        {
            Dwarf d = Dwarves.FirstOrDefault(x => x.Id == it.HeldBy);
            if (d != null) Unequip(d, it.Id);
        }
        RemoveAt(it);
        it.HeldBy = -1;
        it.Cell = cell;
        it.InBuilding = -1;
        AddAt(it);
        RefreshStored(it);
    }

    /// <summary>Into a building: kept at its centre, out of the haulers' way.</summary>
    public void PutIn(Item it, Building b)
    {
        Drop(it, World.Idx(b.Center));
        it.InBuilding = b.Id;
        it.Stored = false;
        Loose.Remove(it.Id);
        b.Parts.Add(it.Id);
    }

    private static void Unequip(Dwarf d, int id)
    {
        if (d.Carry == id) d.Carry = -1;
        if (d.Pick == id) d.Pick = -1;
        if (d.Axe == id) d.Axe = -1;
        if (d.Weapon == id) d.Weapon = -1;
        if (d.Armor == id) d.Armor = -1;
        if (d.Helm == id) d.Helm = -1;
        if (d.Shield == id) d.Shield = -1;
    }

    public void Destroy(Item it)
    {
        if (it.HeldBy > 0)
        {
            Dwarf d = DwarfById(it.HeldBy);
            if (d != null) Unequip(d, it.Id);
        }
        RemoveAt(it);
        Loose.Remove(it.Id);
        foreach (Building b in Buildings) b.Parts.Remove(it.Id);
        Items.Remove(it.Id);
    }

    public Zone StockpileAt(int cell)
    {
        P3 p = World.Pos(cell);
        foreach (Zone z in Zones) if (z.Kind == ZoneKind.Stockpile && z.Contains(p.X, p.Y, p.Z)) return z;
        return null;
    }

    public void RefreshStored(Item it)
    {
        if (it.Cell < 0 || it.HeldBy >= 0 || it.InBuilding >= 0) { it.Stored = false; Loose.Remove(it.Id); return; }
        Zone z = StockpileAt(it.Cell);
        it.Stored = z != null && (z.Cats & it.Def.Cat) != 0;
        if (it.Stored) Loose.Remove(it.Id); else Loose.Add(it.Id);
    }

    public void RefreshStored() { foreach (Item it in Items.Values) RefreshStored(it); }

    /// <summary>How many share a stockpile cell: small things five, logs, stone and blocks two, the rest one.</summary>
    public int Capacity(Item it) => it.Def.Small ? 5 : it.Kind is ItemKind.Log or ItemKind.Boulder or ItemKind.Block ? 2 : 1;

    private long _fullSaid = -100000;

    /// <summary>Once in a while: if things lie about because no stockpile has room, say so.</summary>
    private void StockWarning()
    {
        if (Tick - _fullSaid < Tables.TicksPerDay * 3 || Loose.Count < 15 || !Zones.Any(z => z.Kind == ZoneKind.Stockpile)) return;
        World.EnsureComponents();
        int homeless = 0;
        foreach (int id in Loose.Take(200))
        {
            Item it = Items[id];
            if (it.Free && StockSpot(it, World.Comp[it.Cell]) < 0) homeless++;
        }
        if (homeless < 10) return;
        _fullSaid = Tick;
        Say($"The stockpiles are full: {homeless}{(Loose.Count > 200 ? "+" : "")} things lie about with nowhere to go. Lay out more stockpile room (P).", LogKind.Bad);
    }

    [JsonIgnore] private readonly Dictionary<int, long> _wantSince = new();
    [JsonIgnore] private readonly HashSet<int> _wantSaid = new();

    /// <summary>
    /// A planned building that has waited a day for something there is none of says so, once:
    /// otherwise a brewery waiting for stone that soil never gives just sits there, unexplained.
    /// </summary>
    private void BuildWarning()
    {
        foreach (Building b in Buildings)
        {
            if (b.Built || _wantSaid.Contains(b.Id)) continue;
            // Need is a struct: look for "any lacking" rather than a null.
            var lacking = MissingParts(b).Where(n => !Items.Values.Any(i => i.Free && Tables.Matches(n.Tag, i))).ToList();
            if (lacking.Count == 0) { _wantSince.Remove(b.Id); continue; }
            Need lack = lacking[0];
            if (!_wantSince.TryGetValue(b.Id, out long since)) { _wantSince[b.Id] = Tick; continue; }
            if (Tick - since < Tables.TicksPerDay) continue;
            _wantSaid.Add(b.Id);
            string hint = lack.Tag == "stone" ? " Soil gives none: dig into rock." : "";
            Say($"The planned {b.D.Name.ToLower()} is waiting for {Tables.NeedName(lack.Tag)}, and there is none to hand.{hint}", LogKind.Bad, World.Idx(b.X, b.Y, b.Z));
        }
    }

    /// <summary>Items lying on a stockpile cell, plus the ones promised to it.</summary>
    public int StockLoad(int cell) => (ItemsAt.TryGetValue(cell, out var l) ? l.Count : 0) + StockPromised.GetValueOrDefault(cell);

    [JsonIgnore] public int Wealth
    {
        get
        {
            // What the hold has found: a treasure still sealed in an undiscovered ruin is no one's wealth yet
            // (counted, it made the first year's raids huge).
            int v = Items.Values.Where(i => i.Kind != ItemKind.Corpse && (i.Cell < 0 || !World.Has(i.Cell, CellFlag.Hidden))).Sum(i => i.Value);
            v += Buildings.Where(b => b.Built).Sum(b => b.Parts.Count * 2 + 10);
            return v;
        }
    }

    private long _foodSaid = -100000, _drinkSaid = -100000, _starveSaid = -100000;

    /// <summary>Twice a day: say when food or drink is short, and stop the clock when someone starts to starve.</summary>
    private void SupplyWarning()
    {
        int alive = Living.Count();
        if (alive == 0 || Tick < Tables.TicksPerDay) return;
        int food = Items.Values.Count(i => Tables.Matches("food", i));
        int drink = Items.Values.Count(i => i.Kind == ItemKind.Drink);
        Dwarf starving = Living.FirstOrDefault(d => d.Hunger > Tables.StarvingAt || d.Thirst > Tables.ParchedAt);
        if (starving != null && Tick - _starveSaid > Tables.TicksPerDay * 2)
        {
            _starveSaid = Tick;
            bool hunger = starving.Hunger > Tables.StarvingAt;
            Say(hunger
                ? $"{starving.Name} is starving{(food == 0 ? " and there is nothing to eat" : "")}! Gather plants (G), farm bramblefruit, slaughter or butcher animals, or buy food from a caravan."
                : $"{starving.Name} is parched{(drink == 0 ? " and there is nothing to drink" : "")}! Brew ale at a brewery, or dig a well.", LogKind.Alert, starving.Cell);
            return;
        }
        if (food < alive * 2 && Tick - _foodSaid > Tables.TicksPerDay * 3)
        {
            _foodSaid = Tick;
            Say(food == 0 ? $"There is no food left for {alive} mouths. Gather plants, farm, butcher, or trade for it."
                : $"Food is running low: {food} left for {alive} mouths. Hearthwheat only brews; dwarves eat fruit, dimcaps, eggs, meat and stew.", LogKind.Bad);
        }
        if (drink < alive * 2 && Tick - _drinkSaid > Tables.TicksPerDay * 3)
        {
            _drinkSaid = Tick;
            Say($"Ale is running low: {drink} left for {alive}. Keep the brewery busy.", LogKind.Bad);
        }
    }

    // ---- the log ------------------------------------------------------------------------

    public void Say(string text, LogKind kind = LogKind.Info, int cell = -1)
    {
        // The same line again the same day (a demand, a tantrum) is counted, not repeated.
        long day = Tick / Tables.TicksPerDay;
        int at = Log.Count == 0 ? -1 : Log.FindLastIndex(Log.Count - 1, Math.Min(Log.Count, 8), l => l.Text == text && l.Tick / Tables.TicksPerDay == day);
        if (at >= 0)
        {
            LogEntry same = Log[at];
            Log.RemoveAt(at);
            same.Repeat++;
            same.Tick = Tick;
            same.Kind = (LogKind)Math.Max((int)same.Kind, (int)kind);
            if (cell >= 0) same.Cell = cell;
            Log.Add(same);
            if (kind == LogKind.Alert) PauseFor = text;
            return;
        }
        Log.Add(new LogEntry { Tick = Tick, Text = text, Kind = kind, Cell = cell });
        if (Log.Count > 400) Log.RemoveRange(0, Log.Count - 400);
        if (kind == LogKind.Alert) PauseFor = text;
    }

    public Dwarf DwarfById(int id) => Dwarves.FirstOrDefault(d => d.Id == id);
    public Building BuildingById(int id) => Buildings.FirstOrDefault(b => b.Id == id);
    public Building BuildingOn(int cell) => BuildingAt.TryGetValue(cell, out int id) ? BuildingById(id) : null;

    // ---- time ---------------------------------------------------------------------------

    /// <summary>One tick of the world.</summary>
    public void Step()
    {
        if (Fallen) return;
        Tick++;
        _berserkers = 0;
        foreach (Dwarf b in Dwarves) if (!b.Dead && b.Insane == "berserk") _berserkers++;
        // Many dwarves move per tick; a tick never sees a half-updated map.
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int k = 0; k < Dwarves.Count; k++)
        {
            Dwarf d = Dwarves[k];
            if (d.Dead) continue;
            Needs(d);
            if (d.Dead) continue;
            Act(d);
        }
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        long n0 = Path.TotalExpanded, s0 = Path.Searches;
        StepCreatures();
        CreatureNodes += Path.TotalExpanded - n0; CreatureSearches += Path.Searches - s0;
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        if (Tick % FluidEvery == 0) Fluids();
        long t3 = System.Diagnostics.Stopwatch.GetTimestamp();
        Timing[0] += t1 - t0; Timing[1] += t2 - t1; Timing[2] += t3 - t2;
        if (Tick % 50 == 0) { Growth(); WeatherOnCrops(); }
        if (Tick % 100 == 0) { Moods(); CorpseThoughts(); }
        if (Tick % 500 == 250) { StockWarning(); SupplyWarning(); }
        if (Tick % 500 == 300) BuildWarning();
        if (Tick % Tables.TicksPerDay == 500) { Mourning(); MaybeMood(); DailyWeather(); DailyHappenings(); ProjectsDaily(); ChestsDaily(); }
        if (Tick % Tables.TicksPerSeason == 0) NewSeason();
        else if (Tick % Tables.TicksPerSeason == Tables.TicksPerSeason - Tables.TicksPerDay * 4) CaravanWord();
        if (Tick % 20 == 0 && World.Topology != _lastTopo) { _lastTopo = World.Topology; Unreachable.Clear(); }
        if (Alive == 0 && !Fallen)
        {
            Fallen = true;
            FallenWhy = Dwarves.Count > 0 ? "The last dwarf has died." : "No one is left.";
            Say($"{Name} has fallen. {FallenWhy}", LogKind.Alert);
        }
    }

    private int _lastTopo = -1;

    public void Advance(int ticks)
    {
        for (int i = 0; i < ticks && !Fallen; i++) Step();
    }

    private void NewSeason()
    {
        Say($"{Tables.Seasons[Season]} has arrived (year {Year}).", LogKind.Info);
        NewWeather();
        SeasonalDangers();
        if (Season == 0) NewYearRites();
        if (Season is 0 or 1) Breed();
        CaravanSeason();
        RankSeason();
        NobleSeason();
        ClanSeason();
        MaybeHappening();
        // Migrants follow word of a thriving hold.
        if (Tick < Tables.TicksPerSeason || Alive == 0) return;
        int wealth = Wealth;
        int n = Rng.Range(1, 3) + Math.Min(12, wealth / 4000) + (Alive < 10 ? 2 : 0) + (Befriended ? 3 : 0) + Rank * 2;
        Befriended = false;
        // Word of hard times travels too: every death in the past year turns someone back,
        // and a hold with an empty larder draws only the desperate.
        int lately = Dwarves.Count(d => d.Dead && Tick - d.DiedAt < Tables.TicksPerSeason * 4);
        int food = Items.Values.Count(i => Tables.Matches("food", i));
        bool hungry = food < Alive * 3;
        n -= lately;
        if (hungry) n = Math.Min(n, 1);
        if (Alive + n > 220) n = 220 - Alive;
        if (n <= 0)
        {
            if (lately > 0 || hungry) Say($"No migrants come this season: word has spread of {(lately > 0 ? "the dead" : "the empty larder")} at {Name}.", LogKind.Info);
            return;
        }
        P3 edge = EdgeArrival();
        var clans = Tables.Clans.Where(c => c.Race == "dwarf").Select(c => c.Id).ToList();
        // Now and then a band of another people comes too: whoever lives near.
        string band = null;
        var near = NearPeoples();
        if (near.Count > 0 && !hungry && Rng.Chance(0.35)) band = Rng.Pick(near);
        int bandN = band switch { "delvling" => Rng.Range(2, 4), "rootfolk" => Rng.Range(1, 3), "stonekin" => 1, _ => 0 };
        if (band == "stonekin" && !Rng.Chance(0.4)) { band = null; bandN = 0; }
        for (int k = 0; k < n; k++)
        {
            Dwarf d = AddDwarf(Rng.Pick(clans), edge);
            d.Task = new Task { Kind = TaskKind.Arrive, Dest = World.Idx(Embark) };
        }
        for (int k = 0; k < bandN; k++)
        {
            Dwarf d = AddDwarf(Races.Of(band).Clan, edge);
            d.Task = new Task { Kind = TaskKind.Arrive, Dest = World.Idx(Embark) };
        }
        Count("migrants", n + bandN);
        Say($"{n} migrant{(n == 1 ? "" : "s")} have arrived, drawn by tales of {Name}.", LogKind.Good, World.Idx(edge));
        if (bandN > 0)
        {
            RaceDef r = Races.Of(band);
            Say(bandN == 1 ? $"A {r.Name} has come with them, asking to join the hold. {r.Blurb}" : $"{bandN} {r.Plural} have come with them, asking to join the hold. {r.Blurb}", LogKind.Alert, World.Idx(edge));
            Count("migrants_" + band, bandN);
        }
        foreach (Dwarf d in Living) AddThought(d, "new_arrivals");
    }

    /// <summary>A walkable cell on the map's edge that connects to the embark site.</summary>
    public P3 EdgeArrival()
    {
        World.EnsureComponents();
        int target = World.Comp[World.Idx(Embark)];
        for (int t = 0; t < 400; t++)
        {
            int side = Rng.Int(4), a = Rng.Int(World.W), b = Rng.Int(World.H);
            int x = side switch { 0 => 0, 1 => World.W - 1, _ => a }, y = side switch { 2 => 0, 3 => World.H - 1, _ => b };
            for (int z = World.D - 1; z > 0; z--)
            {
                int i = World.Idx(x, y, z);
                if (World.Shapes[i] == Shape.Empty) continue;
                if (World.Walkable(i) && World.Comp[i] == target) return new P3(x, y, z);
                break;
            }
        }
        return Embark;
    }

    // ---- moods and thoughts --------------------------------------------------------------

    public void AddThought(Dwarf d, string id, int scale = 1)
    {
        ThoughtDef t = Tables.Thoughts[id];
        int v = t.Value * scale;
        if (v < 0 && d.Has("cheerful")) v = v * 2 / 3;
        if (v < 0 && d.Has("gloomy")) v = v * 3 / 2;
        if (v > 0 && d.Has("gloomy")) v = Math.Max(1, v * 2 / 3);
        Thought old = d.Thoughts.FirstOrDefault(x => x.Id == id);
        long until = Tick + (d.Has("cheerful") && v > 0 ? t.Ticks * 3 / 2 : t.Ticks);
        if (old != null) { old.Until = until; old.Value = v; return; }
        d.Thoughts.Add(new Thought { Id = id, Until = until, Value = v });
        SpeakThought(d, id, v);
    }

    private void Moods()
    {
        foreach (Dwarf d in Living)
        {
            d.Thoughts.RemoveAll(t => t.Until <= Tick);
            FaithMoods(d);
            RaceMoods(d);
            if (d.Hunger > Tables.StarvingAt) AddThought(d, "hungry", 2);
            else if (d.Hunger > Tables.HungryAt) AddThought(d, "hungry");
            if (d.Thirst > Tables.ParchedAt) AddThought(d, "thirsty", 2);
            if (d.Has("alelover") && !d.Thoughts.Any(t => t.Id == "drank_ale") && Tick - d.Arrived > Tables.TicksPerDay * 2) AddThought(d, "no_ale");
            int mood = d.Mood;
            if (mood < 0) d.Stress += -mood;
            else d.Stress = Math.Max(0, d.Stress - mood / 2 - 2);
            d.Stress = Math.Min(d.Stress, 20000);
            if (d.Breaking == 0 && d.Strange == null && d.Insane == null && d.Stress > 2500 && Rng.Chance(0.15))
            {
                d.Breaking = 300;
                Release(d);
                d.Task = new Task { Kind = TaskKind.Tantrum };
                Speak(d, "tantrum");
                Count("tantrums");
                Say($"{d.Name} is throwing a tantrum!", LogKind.Bad, d.Cell);
            }
        }
    }

    public void Kill(Dwarf d, string cause)
    {
        if (d.Dead) return;
        Release(d);
        if (d.Carry >= 0 && Items.TryGetValue(d.Carry, out Item c)) Drop(c, d.Cell);
        foreach (int id in new[] { d.Pick, d.Axe, d.Weapon, d.Armor, d.Helm, d.Shield })
            if (id >= 0 && Items.TryGetValue(id, out Item worn)) Drop(worn, d.Cell);
        Discharge(d);
        if (d.MoodShop >= 0 && BuildingById(d.MoodShop) is Building ms) ms.MoodClaim = -1;
        d.Strange = null;
        d.Dead = true;
        d.DeathCause = cause;
        d.Activity = "Dead";
        if (d.Bed >= 0) { Building bed = BuildingById(d.Bed); if (bed != null) bed.Owner = -1; d.Bed = -1; }
        if (d.Chest >= 0) { Building chest = BuildingById(d.Chest); if (chest != null) chest.Owner = -1; d.Chest = -1; }
        Item corpse = Spawn(ItemKind.Corpse, Tables.Mi("flesh"), d.Cell);
        corpse.Label = $"corpse of {d.Name}";
        corpse.Of = d.Id;
        d.DiedAt = Tick;
        Count("deaths");
        Say($"{d.Name} has died: {cause}.", LogKind.Alert, d.Cell);
        P3 at = World.Pos(d.Cell);
        foreach (Dwarf o in Living)
        {
            bool friend = o.Opinion.GetValueOrDefault(d.Id) > 30;
            if (friend) AddThought(o, "friend_died");
            else if (World.Pos(o.Cell).Dist(at) < 12 && !o.Has("brave")) AddThought(o, "saw_death");
        }
    }
}
