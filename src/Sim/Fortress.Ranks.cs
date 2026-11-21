using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// The hold's standing and what it opens: four ranks -- Outpost, Hold, Great Hold, Seat of the Mountainhome --
/// earned by how many live there, how rich it is and what it has done. A Hold may raise its workshops to a
/// second tier and begin the first great projects; a Great Hold to the third tier and every project; the Seat
/// brings a king or queen in place of the baron. Each rank draws more migrants -- and larger raids.
/// </summary>
public partial class Fortress
{
    /// <summary>0 Outpost, 1 Hold, 2 Great Hold, 3 Seat of the Mountainhome.</summary>
    public int Rank;

    public static readonly string[] RankNames = { "Outpost", "Hold", "Great Hold", "Seat of the Mountainhome" };

    [System.Text.Json.Serialization.JsonIgnore] public string RankName => RankNames[Math.Clamp(Rank, 0, 3)];

    /// <summary>What the next rank asks (null at the top), as a line for the player.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string NextRankNeeds => Rank switch
    {
        0 => "20 living and wealth 10,000",
        1 => "40 living, wealth 40,000 and a great project finished",
        2 => "60 living, wealth 100,000, two great projects and a great beast or the Warden slain",
        _ => null,
    };

    [System.Text.Json.Serialization.JsonIgnore] public int ProjectsBuilt => Buildings.Count(b => b.Built && b.D.Kind == BuildKind.Monument);

    /// <summary>Whether the hold has earned the next rank, as things stand.</summary>
    public bool Earned(int rank) => rank switch
    {
        1 => Alive >= 20 && Wealth >= 10000,
        2 => Alive >= 40 && Wealth >= 40000 && ProjectsBuilt >= 1,
        3 => Alive >= 60 && Wealth >= 100000 && ProjectsBuilt >= 2 && BeastsSlain.Count >= 1,
        _ => false,
    };

    /// <summary>Each season: a rank earned is granted (one at a time).</summary>
    internal void RankSeason()
    {
        if (Rank >= 3 || !Earned(Rank + 1)) return;
        Rank++;
        Count("ranks");
        Cue("triumph");
        string opens = Rank switch
        {
            1 => "Its workshops may be raised to a second tier, and the first great projects begun: a Great Gate, a Cistern.",
            2 => "Its workshops may be raised to the third tier, and every great project begun: the Colossus, the Hall of Legends.",
            _ => "A king comes to rule it from here.",
        };
        Say($"{Name} is now a {RankName}! {opens} More will come to live here -- and more will come to take it.", LogKind.Alert);
        foreach (Dwarf d in Living) AddThought(d, "rank_up");
    }

    // ---- workshop tiers ----------------------------------------------------------------------

    /// <summary>The rank a tier needs: II a Hold, III a Great Hold.</summary>
    public static int RankForTier(int tier) => tier <= 1 ? 0 : tier - 1;

    /// <summary>Whether this order may be placed at this workshop, and if not, why.</summary>
    public string CannotOrder(Building b, Recipe r)
    {
        if (r.Upgrade)
        {
            if (r.Tier != b.Tier + 1) return r.Tier <= b.Tier ? "already done" : $"needs tier {Roman(r.Tier - 1)} first";
            if (Rank < RankForTier(r.Tier)) return $"the hold must be a {RankNames[RankForTier(r.Tier)]}";
            if (b.Orders.Any(o => Tables.Recipe(o.Recipe).Upgrade)) return "already ordered";
            return null;
        }
        return b.Tier < r.Tier ? $"needs a tier {Roman(r.Tier)} {b.D.Name.ToLower()}" : null;
    }

    public static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", _ => n.ToString() };

    /// <summary>How much faster a workshop of this tier works.</summary>
    public static float TierSpeed(int tier) => 1 + 0.3f * (tier - 1);

    // ---- great projects -------------------------------------------------------------------------

    /// <summary>The rank a great project needs before it can be begun.</summary>
    public static int RankForProject(string def) => def is "greatgate" or "cistern" ? 1 : 2;

    public bool Has(string def) => Buildings.Any(b => b.Def == def && b.Built);

    /// <summary>Daily: the Colossus lifts every heart; the Hall of Legends tells the hold's deeds.</summary>
    internal void ProjectsDaily()
    {
        bool colossus = Has("colossus"), hall = Has("halloflegends");
        if (!colossus && !hall) return;
        int deeds = Deeds.Count + BeastsSlain.Count + Stats.GetValueOrDefault("artifacts");
        foreach (Dwarf d in Living)
        {
            if (colossus) AddThought(d, "colossus");
            if (hall && deeds > 0) AddThought(d, "legends", Math.Min(3, 1 + deeds / 2));
        }
    }

    /// <summary>A raid through a Great Gate: smaller.</summary>
    internal int GateRaid(int size) => Has("greatgate") ? Math.Max(2, size * 7 / 10) : size;
}
