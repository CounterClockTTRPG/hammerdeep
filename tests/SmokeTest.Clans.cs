using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>Clan ambitions and old grudges.</summary>
public partial class SmokeTest
{
    private void ClanAmbitions()
    {
        foreach (ClanDef c in Tables.Clans) Check(Fortress.AmbitionOf(c.Id) != null, $"the {c.Name}s want something of the hold");

        // Met: the clan is proud.
        {
            var f = Tiny(new[] { "##########", "#@.......#", "##########" });
            f.Peaceful = true;
            Dwarf d = f.Dwarves[0];
            d.Clan = "gemhollow";
            Check(!f.AmbitionMet("gemhollow"), "no hoard of gems yet");
            for (int k = 0; k < 8; k++) f.Spawn(ItemKind.CutGem, Tables.Mi("garnet"), f.World.Idx(5, 1, 1));
            Check(f.AmbitionMet("gemhollow"), "eight cut gems make a hoard");
            f.ClanSeason();
            Check(f.AmbitionsMet.Contains("gemhollow") && d.Thoughts.Any(t => t.Id == "clan_pride"), "and the Gemhollows are insufferably proud");
            Fortress g = Save.Read(Save.Write(f));
            Check(g.AmbitionsMet.Contains("gemhollow"), "a met ambition survives a save");
        }
        // Unmet at a new year: slighted.
        {
            var f = Tiny(new[] { "##########", "#@.......#", "##########" });
            f.Peaceful = true;
            Dwarf d = f.Dwarves[0];
            d.Clan = "ironhand";
            f.Tick = Tables.TicksPerYear;
            f.ClanSeason();
            Check(d.Thoughts.Any(t => t.Id == "clan_slighted") && f.Log.Any(l => l.Text.Contains("grumble")), "an ambition still unmet at the new year: the clan feels slighted");
        }
        // Brawls: only loathing and a bad day, and never worse than bruises.
        {
            var f = Tiny(new[] { "##########", "#@@......#", "##########" });
            Dwarf a = f.Dwarves[0], b = f.Dwarves[1];
            a.Clan = "ironhand"; b.Clan = "mossbeard";
            bool any = false;
            for (int k = 0; k < 50; k++) any |= f.Brawls(a, b);
            Check(!any, "dwarves who do not loathe each other do not brawl");
            a.Opinion[b.Id] = -80;
            for (int k = 0; k < 50; k++) any |= f.Brawls(a, b);
            Check(!any, "nor do two who loathe each other on a good day");
            f.AddThought(a, "hurt");
            f.AddThought(a, "hurt");
            for (int k = 0; k < 50; k++) any |= f.Brawls(a, b);
            Check(any, "but on a bad day, they may");
            a.Hp = 5;
            f.Brawl(a, b);
            Check(a.Hp >= 1 && !a.Dead && a.Thoughts.Any(t => t.Id == "brawled") && f.Log.Any(l => l.Text.Contains("come to blows")), "a brawl bruises, and is talked about, but kills no one");
        }
    }
}
