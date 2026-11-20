using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>Achievements: what earns them, and that the tests never do.</summary>
public partial class SmokeTest
{
    private void AchievementsList()
    {
        Eq(Achievements.All.Select(a => a.Id).Distinct().Count(), Achievements.All.Length, "every achievement has its own id");
        Check(Achievements.All.All(a => a.Title.Length > 0 && a.Text.EndsWith(".")), "and a title and a line saying how");
        Check(!Profile.Counts, "the self-check's play never counts toward the player's achievements");

        var f = Tiny(new[] { "##########", "#@.......#", "##########" });
        f.Peaceful = true;
        var none = new List<string>();
        var got = Achievements.NewlyEarned(f, none).Select(a => a.Id).ToList();
        Check(got.Contains("strike") && !got.Contains("first_year") && !got.Contains("beast"), $"a new hold has struck the earth, and nothing more ({string.Join(",", got)})");
        f.Tick = Tables.TicksPerYear;
        Check(Achievements.NewlyEarned(f, none).Any(a => a.Id == "first_year"), "a year on, the first year is earned");
        f.BeastsSlain.Add("gnashjaw");
        Check(Achievements.NewlyEarned(f, none).Any(a => a.Id == "beast") && !Achievements.NewlyEarned(f, none).Any(a => a.Id == "warden"), "a beast slain earns Beast-slayer, not the Warden's");
        f.BeastsSlain.Add("warden");
        Check(Achievements.NewlyEarned(f, none).Any(a => a.Id == "warden"), "the Warden slain earns Breaker of the seal");
        var have = new List<string> { "strike", "first_year", "beast", "warden" };
        Check(!Achievements.NewlyEarned(f, have).Any(a => have.Contains(a.Id)), "what is already earned is not earned again");
        f.Spawn(ItemKind.Bar, Tables.Mi("starsilver"), f.World.Idx(4, 1, 1));
        Check(Achievements.NewlyEarned(f, have).Any(a => a.Id == "starsilver"), "a starsilver bar earns Starsilver");
    }
}
