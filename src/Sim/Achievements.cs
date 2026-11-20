using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>One deed worth remembering across every hold the player founds.</summary>
public sealed record Achievement(string Id, string Title, string Text, Func<Fortress, bool> Earned);

/// <summary>
/// The deeds a player can be remembered for, checked against a fortress as it is played (the screen checks
/// them once a day and keeps what is earned in the player's profile, with the hold and the date).
/// </summary>
public static class Achievements
{
    private static int S(Fortress f, string key) => f.Stats.GetValueOrDefault(key);

    public static readonly Achievement[] All =
    {
        new("strike", "Strike the earth", "Found a fortress.", f => true),
        new("first_year", "A year under the mountain", "Keep a hold standing through its first year.", f => f.Year >= 2 && !f.Fallen),
        new("three_years", "Deep roots", "Keep a hold standing three years.", f => f.Year >= 4 && !f.Fallen),
        new("fifty", "A great hold", "Have fifty living in one hold.", f => f.Alive >= 50),
        new("wealth", "Wealth beyond counting", "Raise a hold's wealth to 50,000.", f => f.Wealth >= 50000),
        new("raids", "Hold the gate", "Break five grimling raids.", f => S(f, "raids_beaten") >= 5),
        new("beast", "Beast-slayer", "Slay a great beast of the legends.", f => f.BeastsSlain.Any(b => b != "warden")),
        new("beasts", "Bane of legends", "Slay three great beasts in one hold.", f => f.BeastsSlain.Count(b => b != "warden") >= 3),
        new("glimmer", "Into the glimmer", "Break into the Glimmer Deep.", f => f.LayerFound(1)),
        new("ember", "To the roots of the world", "Break into the Ember Halls.", f => f.LayerFound(2)),
        new("warden", "Breaker of the seal", "Slay the Warden of the ancient vault.", f => f.BeastsSlain.Contains("warden")),
        new("starsilver", "Starsilver", "Smelt a bar of starsilver.", f => f.Items.Values.Any(i => i.Kind == ItemKind.Bar && i.M.Key == "starsilver")),
        new("artifact", "A thing of legend", "See a dwarf in a strange mood make an artifact.", f => S(f, "artifacts") >= 1),
        new("noble", "Yes, my lord", "Meet five of a noble's demands.", f => S(f, "demands_met") >= 5),
        new("winter", "The long cold", "Come through a hard winter.", f => S(f, "hard_winters") >= 1 && f.Sky != Weather.HardWinter),
        new("bard", "Music in the hall", "Take in a bard for the winter.", f => S(f, "bards") >= 1),
        new("refuge", "Open doors", "Shelter three who came asking.", f => S(f, "refugees") >= 3),
        new("clans", "Every clan content", "Meet four clans' ambitions in one hold.", f => f.AmbitionsMet.Count >= 4),
        new("trade", "Merchant prince", "Trade goods worth 5,000 with the caravans.", f => S(f, "traded_value") >= 5000),
        new("fallen", "Losing is fun", "Lose a hold.", f => f.Fallen),
    };

    /// <summary>Those newly earned in this fortress, given the ids already earned.</summary>
    public static IEnumerable<Achievement> NewlyEarned(Fortress f, ICollection<string> have) => All.Where(a => !have.Contains(a.Id) && a.Earned(f));
}
