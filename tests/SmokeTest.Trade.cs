using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Caravans, the depot and bartering.</summary>
public partial class SmokeTest
{
    private void Caravans()
    {
        Fortress f = Fortress.New("Tradehold", 5150);
        FactionDef low = Fortress.FactionDefs.First(x => x.Key == "lowlands");
        // No depot: they turn back.
        Caravan lost = f.SendCaravan(low);
        f.Advance(Tables.TicksPerDay * 3);
        Check(lost.State == CaravanState.Leaving || !f.Caravans.Contains(lost), "with no depot, a caravan turns back");
        Check(f.Standing.GetValueOrDefault("lowlands") < 0, "and thinks less of the hold");
        f.Advance(Tables.TicksPerDay * 3);
        Check(!f.Caravans.Contains(lost), "and is gone in a few days");
        // With a depot they come and set out their wares.
        P3 e = f.Embark;
        Building depot = null;
        for (int r = 4; r < 20 && depot == null; r++)
            for (int t = 0; t < 20 && depot == null; t++)
            {
                int x = e.X + f.Rng.Range(-r, r), y = e.Y + f.Rng.Range(-r, r);
                if (f.CanPlace("depot", x, y, e.Z) == null) depot = f.Place("depot", x, y, e.Z);
            }
        Check(depot != null, "a depot can be placed by the wagon");
        depot.Built = true;
        Caravan c = f.SendCaravan(low);
        Check(f.PauseFor != null, "the caravan's arrival is announced");
        Check(c.Goods.Any(g => g.Creature == "goat"), "Lowland merchants bring live goats");
        int t2 = 0;
        while (c.State == CaravanState.Coming && t2 < Tables.TicksPerDay * 3) { f.Advance(50); t2 += 50; }
        Eq(c.State, CaravanState.Trading, $"the merchants reach the depot ({t2} ticks)");
        // Bartering.
        var deal = new Fortress.Deal();
        Good goats = c.Goods.First(g => g.Creature == "goat");
        deal.Ask[goats] = 1;
        Check(!f.Weigh(c, deal).ok, "nothing for something is refused");
        for (int k = 0; k < 6; k++) deal.Offer.Add(f.Spawn(ItemKind.Craft, Tables.Mi("granite"), f.World.Idx(depot.Center), 3));
        Check(f.OurValue(c, deal.Offer[0]) > deal.Offer[0].Value, "they pay more than worth for crafts, which they want");
        int before = f.Creatures.Count(x => x.Tame && x.Def == "goat");
        int itemsBefore = f.Items.Count;
        int standing = f.Standing.GetValueOrDefault("lowlands");
        Check(f.Weigh(c, deal).ok, "six fine figurines buy a goat");
        Check(f.Trade(c, deal), "the deal is done");
        Eq(f.Creatures.Count(x => x.Tame && x.Def == "goat"), before + 1, "the goat is ours");
        Check(f.Items.Count == itemsBefore - 6, "the figurines are theirs");
        Check(f.Standing.GetValueOrDefault("lowlands") > standing, "an over-generous deal warms them to us");
        // A gift.
        var gift = new Fortress.Deal();
        gift.Offer.Add(f.Spawn(ItemKind.CutGem, Tables.Mi("ruby"), f.World.Idx(depot.Center), 4));
        Check(f.Weigh(c, gift).ok && f.Trade(c, gift), "a gift is always welcome");
        Check(f.Markup("lowlands") < 1.35f, "and better standing means better prices");
        // They leave after their days.
        f.Advance(Tables.TicksPerDay * (Fortress.TradeDays + 4));
        Check(!f.Caravans.Contains(c), "after three days the caravan leaves");
        Check(!f.Creatures.Any(x => x.Caravan == c.Id), "taking its merchants");
        // Requests: twice as many next time.
        FactionDef mh = Fortress.FactionDefs.First(x => x.Key == "mountainhome");
        Good iron = mh.Stock.First(s => s.good.Kind == ItemKind.Bar && s.good.Mat == "iron").good;
        f.ToggleRequest("mountainhome", iron);
        Check(f.Requested("mountainhome", iron), "iron bars can be asked for");
        foreach (var (g2, _, _) in mh.Stock.Skip(1).Take(5)) f.ToggleRequest("mountainhome", g2);
        Check(f.Requests["mountainhome"].Count <= Fortress.MaxRequests, "but only so many things");
        int lo = mh.Stock.First(s => s.good == iron).lo;
        Caravan asked = f.SendCaravan(mh);
        Check(asked.Goods.First(g2 => g2.Kind == ItemKind.Bar && g2.Mat == "iron").Count >= lo * 2, "and the caravan brings twice as many");
        Check(!f.Requests.ContainsKey("mountainhome"), "the request is met");
        byte[] data = Save.Write(f);
        Fortress g = Save.Read(data);
        Eq(g.Standing.GetValueOrDefault("lowlands"), f.Standing.GetValueOrDefault("lowlands"), "standing is saved");
    }
}
