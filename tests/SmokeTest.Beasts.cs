using System.Linq;

namespace Hammerdeep;

/// <summary>The great beasts of the legends.</summary>
public partial class SmokeTest
{
    private void Beasts()
    {
        foreach (string k in Fortress.BeastKeys)
            Check(Tables.Creatures.TryGetValue(k, out CreatureDef def) && def.Home == Habitat.Legend && def.Hostile, $"{k} is a hostile beast of the legends");

        // The Ashwyrm's breath burns the dwarves near where it looks, and the wood lying there.
        {
            var f = Tiny(new[] { "##############", "#@...........#", "#............#", "##############" });
            World w = f.World;
            Dwarf d = f.Dwarves[0];
            d.Cell = w.Idx(4, 1, 1);
            Item log = f.Spawn(ItemKind.Log, Tables.Mi("oak"), w.Idx(5, 2, 1));
            Creature c = f.SpawnCreature("ashwyrm", w.Idx(8, 1, 1));
            c.Legend = true;
            int hp = d.Hp;
            Run(f, () => f.Log.Any(l => l.Text.Contains("breathes fire")), 600);
            Check(f.Log.Any(l => l.Text.Contains("The Ashwyrm breathes fire")), "the Ashwyrm breathes fire");
            Check(d.Hp < hp || d.Dead, "and burns the dwarf it looks at");
            Check(!f.Items.ContainsKey(log.Id), "and the log beside them");
        }

        // The Pale Stalker passes a dwarf awake to take one asleep, and is not seen from afar.
        {
            var f = Tiny(new[] { "######################", "#@...................#", "#....................#", "######################" });
            World w = f.World;
            Dwarf awake = f.Dwarves[0];
            awake.Cell = w.Idx(8, 1, 1);
            Dwarf sleeper = f.AddDwarf(awake.Clan, new P3(18, 2, 1));
            sleeper.Cell = w.Idx(18, 2, 1);
            sleeper.Task = new Task { Kind = TaskKind.Sleep };
            sleeper.Fatigue = 100000;
            Creature c = f.SpawnCreature("palestalker", w.Idx(12, 1, 1));
            c.Legend = true;
            f.Step();
            Check(awake.Activity != "Fleeing!", "a dwarf four cells off does not see the Pale Stalker");
            Run(f, () => c.Target >= 0, 100);
            Eq(c.Target, sleeper.Id, "it goes for the sleeper, not the nearer dwarf awake");
        }

        // The Many-Legged Queen lays a brood.
        {
            var f = Tiny(new[] { "##############", "#@...........#", "#............#", "##############" });
            World w = f.World;
            f.Dwarves[0].Cell = w.Idx(1, 1, 1);
            Creature q = f.SpawnCreature("queen", w.Idx(10, 2, 1));
            q.Legend = true;
            Run(f, () => f.Creatures.Any(x => x.Brood == q.Id), 800);
            Check(f.Creatures.Any(x => x.Brood == q.Id && x.Def == "crawler"), "the Many-Legged Queen lays a brood of crawlers");
        }

        // A beast comes to a real hold with its tale, and stays; slain, it leaves a trophy and a deed.
        {
            Fortress f = Fortress.New("Beasthold", 4242, worldSeed: 8675309);
            Creature c = f.LaunchBeast("gnashjaw");
            Check(c != null && c.Legend && f.BeastAbout == c, "Gnashjaw comes to the hold");
            Check(f.PauseFor != null && f.PauseFor.StartsWith("Gnashjaw has come!"), $"with an alert ({f.PauseFor})");
            Check(f.BeastsCome.Contains("gnashjaw"), "and it is remembered that it came");
            Fortress g = Save.Read(Save.Write(f));
            Check(g.BeastAbout is { Legend: true, Def: "gnashjaw" } && g.BeastsCome.Contains("gnashjaw"), "the beast survives a save");
            // Slain by a dwarf with a hammer.
            Dwarf d = f.Living.First();
            c.Cell = f.FindStandable(f.World.Pos(d.Cell), 1);
            c.Hp = 1;
            Item hammer = f.Spawn(ItemKind.Weapon, Tables.Mi("iron"), d.Cell);
            f.PickUp(hammer, d);
            d.Weapon = hammer.Id;
            d.Xp[(int)Labor.Fighting] = 40 * 36;
            Run(f, () => c.Dead, 600);
            Check(c.Dead, "Gnashjaw can be slain");
            Check(f.BeastsSlain.Contains("gnashjaw") && f.Deeds.Count == 1, $"the deed is told ({string.Join(" / ", f.Deeds)})");
            Check(f.Items.Values.Any(i => i.Label == "Gnashjaw's tusks" && i.Quality == 5), "its tusks are kept as a trophy");
            Check(f.Living.All(x => x.Thoughts.Any(t => t.Id == "beast_slain")), "and the whole hold breathes again");
        }

        // The Thing in the Mere fouls the water while it lives.
        {
            var f = Tiny(new[] { "##########", "#@..~~...#", "#...~~...#", "##########" });
            Check(!f.WaterFouled, "the water is clean");
            Creature c = f.SpawnCreature("merething", f.World.Idx(7, 2, 1));
            c.Legend = true;
            Check(f.WaterFouled, "until the Thing in the Mere comes");
        }
    }
}
