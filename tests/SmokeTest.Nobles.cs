using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>The baron and their demands.</summary>
public partial class SmokeTest
{
    private void Nobles()
    {
        var f = Tiny(new[] { "################", "#@.............#", "#..............#", "#..............#", "#..............#", "################" });
        World w = f.World;
        f.Peaceful = true;
        f.NobleSeason();
        Check(f.NobleDwarf == null, "a small hold has no noble");
        while (f.Alive < Fortress.NobleAt) f.AddDwarf(f.Dwarves[0].Clan, new P3(5, 3, 1));
        f.NobleSeason();
        Dwarf n = f.NobleDwarf;
        Check(n != null, "at twenty, the Mountainhome names a noble");
        Check(f.PauseFor != null && f.PauseFor.Contains(f.TitleOf(n)), $"with an alert ({f.PauseFor})");
        Check(f.Mandate != null && f.Log.Any(l => l.Text.Contains(" demands ")), $"who demands something at once ({f.Mandate?.Text})");

        // A room: their own bed, with a chest and a table beside it.
        f.Mandate = new Demand { Kind = "room", Due = f.Tick, Text = "a room" };
        Check(!f.DemandMet, "no room yet");
        Building bed = f.Place("bed", 3, 2, 1); bed.Built = true; bed.Owner = n.Id; n.Bed = bed.Id;
        Building chest = f.Place("chest", 4, 2, 1); chest.Built = true;
        Check(!f.DemandMet, "a bed and a chest are not enough");
        Building table = f.Place("table", 3, 3, 1); table.Built = true;
        Check(f.DemandMet, "with a table too, the room is fit for a noble");
        f.Judge();
        Check(f.Stats.GetValueOrDefault("demands_met") == 1 && f.Living.All(d => d.Thoughts.Any(t => t.Id == "noble_pleased")), "a met demand pleases everyone, a little");

        // Things made: counted from when it was asked.
        f.Mandate = new Demand { Kind = "make", Item = ItemKind.Craft, Count = 3, Baseline = f.Items.Values.Count(i => i.Kind == ItemKind.Craft), Due = f.Tick, Text = "three crafts" };
        for (int k = 0; k < 2; k++) f.Spawn(ItemKind.Craft, Tables.Mi("granite"), w.Idx(8, 2, 1));
        Check(!f.DemandMet, "two crafts of three are not enough");
        f.Spawn(ItemKind.Craft, Tables.Mi("granite"), w.Idx(8, 2, 1));
        Check(f.DemandMet, "three are");

        // A ban broken by a trade: the noble rages, the Mountainhome hears.
        Item craft = f.Spawn(ItemKind.Craft, Tables.Mi("granite"), w.Idx(8, 2, 1));
        f.Mandate = new Demand { Kind = "ban", Item = ItemKind.Craft, Due = f.Tick, Text = "that no crafts be traded away" };
        Check(f.DemandMet, "a ban is kept until something goes");
        int standing = f.Standing.GetValueOrDefault("mountainhome");
        Fortress g = Save.Read(Save.Write(f));
        Check(g.Noble == f.Noble && g.Mandate is { Kind: "ban", Item: ItemKind.Craft }, "the noble and the demand survive a save");
        f.TradedAway(craft);
        Check(f.Mandate.Broken && !f.DemandMet, "trading a craft away breaks the ban");
        f.Judge();
        Check(f.Stats.GetValueOrDefault("demands_failed") == 1, "and the demand has failed");
        Eq(f.Standing.GetValueOrDefault("mountainhome"), standing - 10, "the Mountainhome thinks less of the hold");
        Check(f.Living.All(d => d.Thoughts.Any(t => t.Id == "noble_angry")), "and everyone has to listen to the noble rage");

        // A noble gone, another is named.
        int first = n.Id;
        f.Kill(n, "a fall down the stairs");
        f.NobleSeason();
        Check(f.NobleDwarf == null, "a hold that has fallen below twenty waits for its next noble");
        f.AddDwarf(f.Dwarves[0].Clan, new P3(5, 3, 1));
        f.NobleSeason();
        Check(f.NobleDwarf != null && f.Noble != first, "when the noble dies, another is named");
    }
}
