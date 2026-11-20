using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// The great beasts of the legends. The world's history names five (<see cref="WorldMap.Beasts"/>); one that
/// the history leaves alive may come to the hold, at most one a year from the second, each with its trick:
/// the Ashwyrm breathes fire, the Pale Stalker comes up from the deep for sleepers and is not seen until it
/// is close, the Thing in the Mere lairs in water and fouls it, Gnashjaw batters through doors and charges,
/// the Many-Legged Queen lays a brood. A beast stays until it is slain; slaying one is a thing sung of.
/// </summary>
public partial class Fortress
{
    /// <summary>The great beasts that have come, and those slain here.</summary>
    public List<string> BeastsCome = new(), BeastsSlain = new();
    public int LastBeastYear;
    /// <summary>The hold's own legends: the deeds done here (the World panel lists them before the old ones).</summary>
    public List<string> Deeds = new();

    public static readonly string[] BeastKeys = { "ashwyrm", "palestalker", "merething", "gnashjaw", "queen" };

    [System.Text.Json.Serialization.JsonIgnore] public Creature BeastAbout => Creatures.FirstOrDefault(c => !c.Dead && c.Legend && c.Def != "warden");

    /// <summary>A beast's line in the old history, if it has one: the last thing told of it.</summary>
    private HistoryEvent LegendOf(string key)
    {
        string name = Tables.Creatures[key].Name;
        return Map?.History.LastOrDefault(e => e.Text.StartsWith(name.ToUpperFirst(), StringComparison.Ordinal));
    }

    /// <summary>Slain in the old days, it cannot come.</summary>
    private bool SlainOfOld(string key) => LegendOf(key)?.Text.Contains(" is slain ") == true;

    /// <summary>Each season from the second year: perhaps a great beast, if none is about and none came this year.</summary>
    private void MaybeGreatBeast()
    {
        // They come out of the world's tales: a hold with no world around it (an old save, a hand-built map) has none.
        if (Map == null || Year < 2 || LastBeastYear == Year || BeastAbout != null) return;
        var free = BeastKeys.Where(k => !BeastsCome.Contains(k) && !SlainOfOld(k)).ToList();
        if (free.Count == 0) return;
        if (!Rng.Chance(Math.Min(0.4, 0.12 + 0.05 * (Year - 2)))) return;
        LaunchBeast(Rng.Pick(free));
    }

    /// <summary>A great beast comes: where its kind would, with the old tale of it.</summary>
    public Creature LaunchBeast(string key)
    {
        int at = key switch
        {
            "palestalker" or "queen" when CavernFound => CavernCell(),
            "merething" => MereCell(),
            _ => -1,
        };
        if (at < 0) { P3 e = EdgeArrival(); at = FindStandable(e, 3); }
        Creature c = SpawnCreature(key, at);
        c.Legend = true;
        c.Lair = at;
        c.NextTrick = Tick + 200;
        BeastsCome.Add(key);
        LastBeastYear = Year;
        Cue("roar");
        Count("beasts_come");
        CreatureDef d = c.D;
        HistoryEvent old = LegendOf(key);
        string tale = old != null ? $" The old tales tell of it: {old.Text.TrimEnd('.')}, {-old.Year} years ago." : "";
        string how = key switch
        {
            "ashwyrm" => "Its breath is fire.",
            "palestalker" => "It hunts sleepers, and is not seen until it is close.",
            "merething" => "It lairs in the water, and fouls what it touches.",
            "gnashjaw" => "No door has held it.",
            "queen" => "Her brood comes with her.",
            _ => "",
        };
        Say($"{d.Name.ToUpperFirst()} has come!{tale} {how}", LogKind.Alert, c.Cell);
        foreach (Dwarf w in Living) AddThought(w, "beast_dread");
        return c;
    }

    /// <summary>A cell beside the water nearest the hold, for the Thing in the Mere (-1: there is none).</summary>
    private int MereCell()
    {
        int best = -1, bestD = int.MaxValue;
        for (int t = 0; t < 3000; t++)
        {
            int x = Rng.Int(World.W), y = Rng.Int(World.H);
            for (int z = World.D - 2; z > 0; z--)
            {
                int i = World.Idx(x, y, z);
                if (World.Shapes[i] == Shape.Empty && !World.WaterAt(i)) continue;
                if (World.Walkable(i) && BesideWater(i) && !_anyWell)
                {
                    int dd = World.Pos(i).Dist(Embark);
                    if (dd > 12 && dd < bestD) { best = i; bestD = dd; }
                }
                break;
            }
        }
        return best;
    }

    /// <summary>The Thing in the Mere fouls the water while it lives.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool WaterFouled => Creatures.Any(c => !c.Dead && c.Def == "merething");

    /// <summary>A beast's own turn: its trick if it has one ready (true: the turn is spent).</summary>
    private bool BeastTurn(Creature c)
    {
        switch (c.Def)
        {
            case "ashwyrm" when Tick >= c.NextTrick:
            {
                Dwarf near = NearestDwarf(c.Cell, 4, sameComp: true);
                if (near == null || World.Pos(near.Cell).Z != World.Pos(c.Cell).Z) return false;
                Breathe(c, near.Cell);
                c.NextTrick = Tick + 400;
                return true;
            }
            case "queen" when Tick >= c.NextTrick:
            {
                c.NextTrick = Tick + 250;
                if (Creatures.Count(x => !x.Dead && x.Brood == c.Id) >= 6) return false;
                int spot = FindStandable(World.Pos(c.Cell), 2);
                if (spot < 0) return false;
                Creature young = SpawnCreature("crawler", spot);
                young.Brood = c.Id;
                young.Angry = true;
                if (c.Brood++ == 0) Say($"{c.D.Name.ToUpperFirst()} lays a brood: crawlers swarm from her!", LogKind.Bad, c.Cell);
                return false;
            }
            case "merething":
            {
                // Back to the water when there is nobody to drag in.
                if (c.Target < 0 && c.Lair >= 0 && World.Pos(c.Cell).Dist(World.Pos(c.Lair)) > 3 && NearestDwarf(c.Cell, 8) == null)
                {
                    CreatureWalk(c, c.Lair, 8000);
                    return c.Path != null;
                }
                return false;
            }
        }
        return false;
    }

    /// <summary>The Ashwyrm's breath: fire on everyone near where it looks, and on the wood lying there.</summary>
    private void Breathe(Creature c, int at)
    {
        P3 p = World.Pos(at);
        int burned = 0;
        foreach (Dwarf d in Living.ToList())
        {
            P3 q = World.Pos(d.Cell);
            if (q.Z != p.Z || q.Dist(p) > 2) continue;
            float stop = Tables.ArmorStop(Worn(d.Armor), Worn(d.Helm)) * 0.5f;
            int dmg = Math.Max(1, (int)(Rng.Range(14, 22) * (1 - stop)));
            d.Hp -= dmg;
            d.LastHurt = Tick;
            burned++;
            if (d.Hp <= 0) { Kill(d, $"burned by {c.D.Name}"); Count("slain"); }
            else AddThought(d, "hurt");
        }
        foreach (Item it in Items.Values.Where(i => i.Cell >= 0 && i.HeldBy < 0 && i.InBuilding < 0 && i.M.Kind == MatKind.Wood && World.Pos(i.Cell).Z == p.Z && World.Pos(i.Cell).Dist(p) <= 2).ToList())
            Destroy(it);
        Say($"{c.D.Name.ToUpperFirst()} breathes fire!{(burned > 0 ? $" {burned} {(burned == 1 ? "dwarf is" : "dwarves are")} burned." : "")}", LogKind.Bad, at);
    }

    /// <summary>How much harder a beast's blow lands on this dwarf: a sleeper under the Pale Stalker, a dwarf at the
    /// water's edge in the Mere's reach, the first blow of Gnashjaw's charge.</summary>
    private float BeastBlow(Creature c, Dwarf d)
    {
        switch (c.Def)
        {
            case "palestalker" when d.Task?.Kind == TaskKind.Sleep: return 2.5f;
            case "merething" when BesideWater(d.Cell): return 1.6f;
            case "gnashjaw" when c.Run >= 3:
                c.Run = 0;
                Say($"Gnashjaw charges {d.FirstName}!", LogKind.Bad, d.Cell);
                return 2f;
        }
        return 1;
    }

    /// <summary>The Pale Stalker goes for whoever sleeps, wherever it can reach them.</summary>
    private Dwarf Sleeper(Creature c)
    {
        World.EnsureComponents();
        int comp = World.Comp[c.Cell];
        return Living.Where(d => d.Task?.Kind == TaskKind.Sleep && World.Comp[d.Cell] == comp)
            .OrderBy(d => World.Pos(d.Cell).Dist(World.Pos(c.Cell))).FirstOrDefault();
    }

    /// <summary>A great beast slain: a trophy, a deed for the legends, a hold that breathes again.</summary>
    private void BeastSlain(Creature c, Dwarf by)
    {
        BeastsSlain.Add(c.Def);
        Count("beasts_slain");
        Cue("triumph");
        string name = c.D.Name.ToUpperFirst();
        string who = by != null ? by.Name : "the hold";
        string part = c.Def switch { "ashwyrm" => "skull", "palestalker" => "long fingers", "merething" => "eye", "gnashjaw" => "tusks", "warden" => "heartstone", _ => "crown of spines" };
        Item trophy = Spawn(ItemKind.Bone, Tables.Mi("bone"), c.Cell);
        trophy.Label = $"{c.D.Name}'s {part}";
        trophy.Quality = 5;
        trophy.Maker = who;
        Deeds.Add($"{Date}: {name} was slain at {Name} by {who}.");
        Say($"{name} is slain{(by != null ? $" by {by.Name}" : "")}! Its {part} is taken as a trophy, and the tale will be told for an age.", LogKind.Alert, c.Cell);
        foreach (Dwarf w in Living)
        {
            w.Thoughts.RemoveAll(t => t.Id == "beast_dread");
            AddThought(w, "beast_slain");
        }
        // Her brood scatters.
        foreach (Creature y in Creatures.Where(x => !x.Dead && x.Brood == c.Id)) y.Angry = false;
    }
}
