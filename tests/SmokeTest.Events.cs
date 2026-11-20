using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>Things at the gate that want an answer.</summary>
public partial class SmokeTest
{
    private void Happenings()
    {
        Fortress Hold()
        {
            var f = Tiny(new[] { "################", "#@.............#", "#..............#", "#..............#", "################" });
            f.Peaceful = true;
            World w = f.World;
            for (int k = 0; k < 30; k++) f.Spawn(ItemKind.Meal, Tables.Mi("dimcap"), w.Idx(10, 2, 1));
            for (int k = 0; k < 20; k++) f.Spawn(ItemKind.Drink, Tables.Mi("dimcap"), w.Idx(11, 2, 1));
            for (int k = 0; k < 10; k++) f.Spawn(ItemKind.Craft, Tables.Mi("gold"), w.Idx(12, 2, 1));
            return f;
        }
        foreach (string key in Fortress.HappeningKeys)
        {
            var f = Hold();
            Happening h = f.Happen(key);
            Check(f.Pending == h && h.Options.Length >= 2 && f.PauseFor != null, $"{key}: it comes to the gate, stops the game and asks ({h.Title})");
        }
        // A bard: music every day, and a meal and an ale with it.
        {
            var f = Hold();
            f.Happen("bard");
            f.Choose(0);
            Check(f.BardUntil > f.Tick && f.Pending == null, "a bard taken in stays until Thaw");
            int ale = f.Items.Values.Count(i => i.Kind == ItemKind.Drink);
            f.DailyHappenings();
            Check(f.Living.All(d => d.Thoughts.Any(t => t.Id == "heard_bard")), "the hall hears the bard");
            Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Drink), ale - 1, "who drinks an ale a day");
        }
        // Shelter: taken in, someone joins (or a spy brings a raid); turned away, it weighs on everyone.
        {
            var f = Hold();
            int alive = f.Alive;
            f.Happen("refugee");
            f.Choose(0);
            Check(f.Alive == alive + 1 || f.Creatures.Any(c => c.Raider), $"someone taken in joins the hold ({f.Alive}) -- or was a spy, and a raid comes");
            var g = Hold();
            g.Happen("refugee");
            g.Choose(1);
            Check(g.Living.All(d => d.Thoughts.Any(t => t.Id == "turned_away")), "turned away, nobody says much at supper");
        }
        // The envoy's gift, paid in goods; the wanderer's thing, real or painted.
        {
            var f = Hold();
            Happening h = f.Happen("envoy");
            int crafts = f.Items.Values.Count(i => i.Kind == ItemKind.Craft);
            f.Choose(0);
            Check(f.Befriended && f.Items.Values.Count(i => i.Kind == ItemKind.Craft) < crafts, $"the envoy's gift ({h.Value}) is paid in goods, and their hold will send word");
            var g = Hold();
            g.Happen("wanderer");
            g.Choose(0);
            Check(g.Log.Any(l => l.Text.StartsWith("It is real") || l.Text.StartsWith("It is a painted stone")), "the wanderer's thing turns out real, or painted");
        }
        // Rats: cleared at once, or left to eat for days.
        {
            var f = Hold();
            int food = f.Items.Values.Count(i => Tables.Matches("food", i));
            f.Happen("rats");
            f.Choose(1);
            Eq(f.RatDays, 6, "rats left be stay six days");
            for (int d = 0; d < 6; d++) f.DailyHappenings();
            int left = f.Items.Values.Count(i => Tables.Matches("food", i));
            Check(left < food && f.RatDays == 0, $"and eat as they go ({food} -> {left})");
        }
        // Unanswered, the careful answer is taken; a question pending survives a save.
        {
            var f = Hold();
            f.Happen("bard");
            Fortress g = Save.Read(Save.Write(f));
            Check(g.Pending is { Key: "bard" }, "a question pending survives a save");
            f.Tick = f.Pending.Due;
            f.DailyHappenings();
            Check(f.Pending == null && f.BardUntil == 0, "left unanswered, the bard is sent on");
        }
    }
}
