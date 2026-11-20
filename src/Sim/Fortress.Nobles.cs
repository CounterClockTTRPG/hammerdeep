using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>A noble's demand: a room of their own, things made, or a thing not to be traded away.</summary>
public sealed class Demand
{
    public string Kind;
    public ItemKind Item;
    public int Count, Baseline;
    public long Due;
    public bool Broken;
    public string Text;
}

/// <summary>
/// The Mountainhome's eye on the hold: once it is twenty strong, a dwarf is named its baron (or baroness),
/// and every season after they want something -- a room fit for them, so many things made, or a thing
/// that must not leave with the caravans -- by the next season's turn. Met, the hold is spared the
/// noble's temper; failed, the noble rages, everyone hears of it, and the Mountainhome thinks less of the hold.
/// </summary>
public partial class Fortress
{
    public const int NobleAt = 20;
    /// <summary>The baron's id (-1: none yet, or dead), and what they want now.</summary>
    public int Noble = -1;
    public Demand Mandate;

    [System.Text.Json.Serialization.JsonIgnore] public Dwarf NobleDwarf => Noble >= 0 && DwarfById(Noble) is { Dead: false } d ? d : null;

    public static string TitleOf(Dwarf d) => d.Female ? "Baroness" : "Baron";

    /// <summary>At each season's turn: judge the last demand, name a noble if the hold has earned one, make a new demand.</summary>
    internal void NobleSeason()
    {
        if (Mandate != null && Tick >= Mandate.Due) Judge();
        if (NobleDwarf == null && Alive >= NobleAt) Ennoble();
        if (NobleDwarf != null && Mandate == null) MakeDemand();
    }

    private void Ennoble()
    {
        // The Mountainhome favours a dwarf, not a newcomer of another people; the most skilled will do.
        Dwarf d = Living.Where(x => (x.Race ?? "dwarf") == "dwarf" && x.Squad < 0).OrderByDescending(x => x.Xp.Sum()).FirstOrDefault();
        if (d == null) return;
        bool again = Noble >= 0;
        Noble = d.Id;
        Mandate = null;
        Count("nobles");
        Say($"{(again ? "With the old one gone, the" : "The")} Mountainhome names {d.Name} {TitleOf(d)} of {Name}. {d.FirstName} will have things done properly from now on.", LogKind.Alert, d.Cell);
    }

    internal void MakeDemand()
    {
        Dwarf n = NobleDwarf;
        long due = (Tick / Tables.TicksPerSeason + 1) * Tables.TicksPerSeason;
        int pick = Rng.Int(3);
        // A room is asked for until it is had.
        if (pick == 0 && RoomFit(n)) pick = 1;
        var m = new Demand { Due = due };
        switch (pick)
        {
            case 0:
                m.Kind = "room";
                m.Text = "a room fit for a noble: a bed of their own, with a chest and a table beside it";
                break;
            case 1:
            {
                var (kind, count, what) = Rng.Pick(new[] { (ItemKind.Craft, 3, "three fine crafts"), (ItemKind.Statue, 1, "a statue"), (ItemKind.Chair, 2, "two chairs"), (ItemKind.Coffin, 1, "a coffin, just in case"), (ItemKind.Barrel, 3, "three barrels") });
                m.Kind = "make";
                m.Item = kind;
                m.Count = count;
                m.Baseline = Made(kind);
                m.Text = $"{what} made";
                break;
            }
            default:
            {
                var (kind, what) = Rng.Pick(new[] { (ItemKind.Craft, "crafts"), (ItemKind.Bar, "metal bars"), (ItemKind.Drink, "ale"), (ItemKind.Meal, "prepared meals") });
                m.Kind = "ban";
                m.Item = kind;
                m.Text = $"that no {what} be traded away";
                break;
            }
        }
        Mandate = m;
        Say($"{TitleOf(n)} {n.FirstName} demands {m.Text} by {Tables.Seasons[(int)(due / Tables.TicksPerSeason % 4)]}.", LogKind.Bad, n.Cell);
    }

    /// <summary>How many of a kind the hold has, lying about or placed.</summary>
    private int Made(ItemKind kind)
    {
        string def = kind switch { ItemKind.Statue => "statue", ItemKind.Chair => "chair", ItemKind.Coffin => "coffin", _ => null };
        return Items.Values.Count(i => i.Kind == kind) + (def == null ? 0 : Buildings.Count(b => b.Def == def));
    }

    /// <summary>The noble's own bed, with a built chest and table within a few steps on its level.</summary>
    private bool RoomFit(Dwarf n)
    {
        Building bed = n.Bed >= 0 ? BuildingById(n.Bed) : null;
        if (bed is not { Built: true }) return false;
        bool Near(string def) => Buildings.Any(b => b.Built && b.Def == def && b.Z == bed.Z && Math.Abs(b.X - bed.X) <= 3 && Math.Abs(b.Y - bed.Y) <= 3);
        return Near("chest") && Near("table");
    }

    /// <summary>Whether the demand is met as things stand.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool DemandMet => Mandate != null && NobleDwarf is Dwarf n && Met(n, Mandate);

    internal void Judge()
    {
        Dwarf n = NobleDwarf;
        Demand m = Mandate;
        Mandate = null;
        if (n == null) return;
        if (Met(n, m))
        {
            Count("demands_met");
            Say($"{TitleOf(n)} {n.FirstName} is satisfied: {m.Text}. For now.", LogKind.Good, n.Cell);
            foreach (Dwarf d in Living) AddThought(d, "noble_pleased");
            return;
        }
        Count("demands_failed");
        Cue("rage");
        Standing["mountainhome"] = Standing.GetValueOrDefault("mountainhome") - 10;
        Say($"{TitleOf(n)} {n.FirstName} is furious: {m.Text} -- and it was not done! Everyone hears about it, and so will the Mountainhome.", LogKind.Bad, n.Cell);
        foreach (Dwarf d in Living) AddThought(d, "noble_angry");
    }

    private bool Met(Dwarf n, Demand m) => m.Kind switch
    {
        "room" => RoomFit(n),
        "make" => Made(m.Item) >= m.Baseline + m.Count,
        _ => !m.Broken,
    };

    /// <summary>A trade gave this away: a ban on it is broken.</summary>
    internal void TradedAway(Item it)
    {
        if (Mandate is { Kind: "ban", Broken: false } m && it.Kind == m.Item && NobleDwarf is Dwarf n)
        {
            m.Broken = true;
            Say($"{TitleOf(n)} {n.FirstName} saw that go to the merchants, and will not forget it.", LogKind.Bad, n.Cell);
        }
    }
}
