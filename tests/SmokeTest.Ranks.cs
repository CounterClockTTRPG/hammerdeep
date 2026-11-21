using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>The hold's rank, raised workshops and great projects.</summary>
public partial class SmokeTest
{
    private void Ranks()
    {
        var f = Tiny(new[] { "####################", "#@.................#", "#..................#", "#..................#", "#..................#", "#..................#", "#..................#", "####################" });
        f.Peaceful = true;
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        for (int k = 0; k < d.Labors.Length; k++) d.Labors[k] = true;

        // Rank: earned, not given.
        f.RankSeason();
        Eq(f.Rank, 0, "a small poor hold is an Outpost");
        while (f.Alive < 20) f.AddDwarf(d.Clan, new P3(10, 4, 1));
        for (int k = 0; k < 30; k++) f.Spawn(ItemKind.Craft, Tables.Mi("gold"), w.Idx(17, 6, 1), 5);
        Check(f.Wealth >= 10000, $"(wealth {f.Wealth})");
        f.RankSeason();
        Eq(f.Rank, 1, "twenty living and wealth 10,000 make it a Hold");
        Check(f.PauseFor != null && f.PauseFor.Contains("is now a Hold"), "announced");
        f.RankSeason();
        Eq(f.Rank, 1, "a Great Hold needs more (forty living, a great project)");

        // A workshop raised a tier through a work order: the stuff fetched, the work done, the tier up.
        Building shop = f.Place("carpenter", 2, 2, 1); shop.Built = true;
        Recipe up2 = Tables.Recipe("up2-carpenter"), up3 = Tables.Recipe("up3-carpenter");
        Check(f.CannotOrder(shop, up3) != null, "a workshop cannot skip a tier");
        Eq(f.CannotOrder(shop, up2), null, "a Hold can raise a workshop to tier II");
        for (int k = 0; k < 4; k++) f.Spawn(ItemKind.Block, Tables.Mi("granite"), w.Idx(8, 3, 1));
        for (int k = 0; k < 2; k++) f.Spawn(ItemKind.Bar, Tables.Mi("iron"), w.Idx(8, 4, 1));
        f.AddOrder(shop, up2.Id, 1);
        Run(f, () => shop.Tier == 2, 30000);
        Eq(shop.Tier, 2, "the carpenter's shop is raised to tier II");
        Check(f.Items.Values.Count(i => i.Kind == ItemKind.Block) == 0, "using up its blocks");
        Check(f.CannotOrder(shop, up3) != null, "tier III waits for a Great Hold");
        Check(Fortress.TierSpeed(2) > 1 && Fortress.TierSpeed(3) > Fortress.TierSpeed(2), "a higher tier works faster");
        Building crafts = f.Place("craftsdwarf", 12, 1, 1); crafts.Built = true;
        Recipe jewel = Tables.Recipe("jewelcraft");
        Check(f.CannotOrder(crafts, jewel) != null, "jewelled crafts need a tier II craftsdwarf's shop");
        crafts.Tier = 2;
        Eq(f.CannotOrder(crafts, jewel), null, "and are made at one");
        Check(!Tables.Matches("toolbar", new Item { Kind = ItemKind.Bar, Mat = Tables.Mi("starsilver") }), "starsilver is worked only at a tier III forge");

        // Great projects: by rank, one of each, and what they do.
        Check(f.CanPlace("colossus", 12, 4, 1) != null, "the Colossus is a Great Hold's work");
        Eq(f.CanPlace("greatgate", 12, 4, 1), null, "a Hold may begin a Great Gate");
        Building gate = f.Place("greatgate", 12, 4, 1);
        Check(f.CanPlace("greatgate", 15, 4, 1) != null, "and only one");
        Eq(f.GateRaid(10), 10, "while it is building, raids are as before");
        gate.Built = true;
        Eq(f.GateRaid(10), 7, "a Great Gate takes a third off a raid");
        f.Rank = 2;
        Building col = f.Place("colossus", 6, 4, 1); col.Built = true;
        f.ProjectsDaily();
        Check(f.Living.All(x => x.Thoughts.Any(t => t.Id == "colossus")), "the Colossus lifts everyone");
        Building cis = f.Place("cistern", 15, 4, 1); cis.Built = true;
        Check(f.BesideWater(w.Idx(14, 5, 1)), "beside a Cistern there is water to drink");

        // The top rank: a king.
        f.Rank = 3;
        f.NobleSeason();
        Check(f.NobleDwarf is Dwarf n && (f.TitleOf(n) is "King" or "Queen"), "the Seat of the Mountainhome is ruled by a king or queen");
        Fortress g = Save.Read(Save.Write(f));
        Check(g.Rank == 3 && g.Buildings.First(b => b.Def == "carpenter").Tier == 2, "the rank and the tiers survive a save");
    }
}
