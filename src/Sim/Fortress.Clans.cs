using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// What each clan wants of the hold, and what old grudges come to. The Ironhands want a forge that has armed
/// the hold, the Mossbeards a great garden and full barrels, the Stonewards a strong guard behind a door, the
/// Gemhollows a hoard of cut gems; the other peoples' clans their own. Met, a clan is proud for a season;
/// still unmet at a new year, it feels slighted. And two dwarves of feuding clans who have come to loathe each
/// other may, on a bad day, come to blows -- bruises and a worse opinion, never worse than that.
/// </summary>
public partial class Fortress
{
    /// <summary>The clans whose ambition the hold has met.</summary>
    public List<string> AmbitionsMet = new();

    public static string AmbitionOf(string clan) => clan switch
    {
        "ironhand" => "a forge that has armed the hold (six weapons and armour)",
        "mossbeard" => "a great garden (40 cells of farm) and forty barrels of ale",
        "stoneward" => "a strong guard: six under arms, and a door on the hold",
        "gemhollow" => "a hoard of eight cut gems",
        "copperclaw" => "a shiny hoard: six things of gold or silver",
        "hollyhock" => "gardens under the sky (30 cells of farm outside)",
        "graniteborn" => "great halls (800 cells dug)",
        _ => null,
    };

    /// <summary>Whether a clan's ambition is met as things stand.</summary>
    public bool AmbitionMet(string clan)
    {
        int Of(Func<Item, bool> p) => Items.Values.Count(p);
        int Farm(bool outside) => Zones.Where(z => z.Kind == ZoneKind.Farm && (!outside || World.IsOutside(World.Idx(z.X0, z.Y0, z.Z)))).Sum(z => z.Area);
        return clan switch
        {
            "ironhand" => Buildings.Any(b => b.Def == "forge" && b.Built) && Of(i => i.Kind is ItemKind.Weapon or ItemKind.Armor) >= 6,
            "mossbeard" => Farm(false) >= 40 && Of(i => i.Kind == ItemKind.Drink) >= 40,
            "stoneward" => Squads.Sum(s => s.Members.Count) >= 6 && Buildings.Any(b => b.Def == "door" && b.Built),
            "gemhollow" => Of(i => i.Kind == ItemKind.CutGem) >= 8,
            "copperclaw" => Of(i => i.M.Key is "gold" or "silver" && i.Kind != ItemKind.Bar) >= 6,
            "hollyhock" => Farm(true) >= 30,
            "graniteborn" => Stats.GetValueOrDefault("dug") >= 800,
            _ => false,
        };
    }

    private IEnumerable<string> ClansHere => Living.Select(d => d.Clan).Distinct().Where(c => AmbitionOf(c) != null);

    private static string Plural(ClanDef c) => c.Name + "s";

    /// <summary>Each season: a clan whose ambition is met is proud; at a new year, those still waiting are slighted.</summary>
    internal void ClanSeason()
    {
        foreach (string clan in ClansHere.ToList())
        {
            ClanDef c = Tables.Clan(clan);
            if (!AmbitionsMet.Contains(clan) && AmbitionMet(clan))
            {
                AmbitionsMet.Add(clan);
                Count("ambitions_met");
                Say($"The {Plural(c)} have what they wanted of {Name}: {AmbitionOf(clan)}. They are insufferably proud.", LogKind.Good);
                foreach (Dwarf d in Living.Where(x => x.Clan == clan)) AddThought(d, "clan_pride");
            }
            else if (Season == 0 && !AmbitionsMet.Contains(clan) && Tick >= Tables.TicksPerYear)
            {
                Say($"The {Plural(c)} grumble into their ale: still no {AmbitionOf(clan)}.", LogKind.Info);
                foreach (Dwarf d in Living.Where(x => x.Clan == clan)) AddThought(d, "clan_slighted");
            }
        }
    }

    /// <summary>Two who loathe each other over an old grudge, on a bad day: fists.</summary>
    internal void Brawl(Dwarf a, Dwarf b)
    {
        foreach (Dwarf d in new[] { a, b })
        {
            d.Hp = Math.Max(1, d.Hp - Rng.Range(4, 9));
            d.LastHurt = Tick;
            AddThought(d, "brawled");
        }
        a.Opinion[b.Id] = Math.Max(-100, a.Opinion.GetValueOrDefault(b.Id) - 10);
        b.Opinion[a.Id] = Math.Max(-100, b.Opinion.GetValueOrDefault(a.Id) - 10);
        a.Activity = b.Activity = "Brawling";
        Count("brawls");
        Say($"{a.Name} and {b.Name} come to blows over the old grudge between {Plural(Tables.Clan(a.Clan))} and {Plural(Tables.Clan(b.Clan))}!", LogKind.Bad, a.Cell);
    }

    /// <summary>When two of feuding clans meet: do they brawl? (Loathing, and a bad day for one of them.)</summary>
    internal bool Brawls(Dwarf a, Dwarf b) =>
        a.Opinion.GetValueOrDefault(b.Id) <= -50 && (a.Mood < 0 || b.Mood < 0) && Rng.Chance(0.3);
}
