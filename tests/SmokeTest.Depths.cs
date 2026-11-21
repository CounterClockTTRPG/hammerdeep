using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>The layers under the hold, and the ruin in the deepest.</summary>
public partial class SmokeTest
{
    private void Depths()
    {
        Fortress f = Fortress.New("Deephold", 4242, worldSeed: 8675309);
        World w = f.World;
        Eq(f.Caverns.Length, 3, "three cavern layers under a real hold");
        Check(f.Caverns[0] > f.Caverns[1] && f.Caverns[1] > f.Caverns[2] && f.Caverns[2] >= 3, $"each deeper than the last ({string.Join(", ", f.Caverns)})");
        for (int k = 0; k < f.Caverns.Length; k++)
        {
            int z = f.Caverns[k], open = 0;
            for (int y = 0; y < w.H; y++) for (int x = 0; x < w.W; x++) for (int dz = -2; dz <= 2; dz++) if (w.In(x, y, z + dz) && w.Shapes[w.Idx(x, y, z + dz)] == Shape.Floor && !w.IsOutside(w.Idx(x, y, z + dz))) open++;
            Check(open > 200, $"{Fortress.CavernNames[k]} has open caves ({open} floor cells)");
        }
        int Count(string mat) { byte m = Tables.Mi(mat); int n = 0; for (int i = 0; i < w.N; i++) if (w.Mats[i] == m && w.Shapes[i] == Shape.Wall) n++; return n; }
        Check(Count("glimmerstone") > 0, "glimmerstone in the walls of the Glimmer Deep");
        Check(Count("starvein") > 0, "starsilver ore near the Ember Halls");

        // The ruin: a sealed marble vault in the Ember Halls, the Warden asleep in it beside a starsilver treasure.
        Check(f.Ruin.X >= 0 && f.LayerAt(f.Ruin.Z) == 2, $"an ancient ruin in the Ember Halls ({f.Ruin})");
        Creature warden = f.Creatures.FirstOrDefault(c => c.Def == "warden");
        Check(warden != null && !warden.Angry && w.Pos(warden.Cell).Dist(f.Ruin) <= 2, "the Warden sleeps in it");
        Item treasure = f.Artifacts.Where(f.Items.ContainsKey).Select(id => f.Items[id]).FirstOrDefault(i => i.M.Key == "starsilver");
        Check(treasure != null && treasure.Kind == ItemKind.Weapon && treasure.Quality == 5, $"beside a starsilver treasure ({treasure?.Label})");
        Check(f.BeastAbout == null, "a sleeping Warden does not keep the great beasts away");
        f.Advance(300);
        Check(w.Pos(warden.Cell).Dist(f.Ruin) <= 2, "it does not wander off");

        // Breaking into each layer: announced, and its own creatures come.
        f.BrokeInto(1, f.World.Idx(f.Embark));
        Check(f.LayerFound(1) && f.PauseFor != null && f.PauseFor.Contains("Glimmer Deep"), "breaking into the Glimmer Deep is announced");
        Check(f.Creatures.Any(c => c.Def == "gloomhound"), "and gloomhounds come");
        f.BrokeInto(2, f.World.Idx(f.Embark));
        Check(f.LayerFound(2) && f.Creatures.Any(c => c.Def == "emberling"), "the Ember Halls send emberlings");
        Fortress g = Save.Read(Save.Write(f));
        Check(g.Caverns.SequenceEqual(f.Caverns) && g.CavernsFound == f.CavernsFound && g.Ruin.Equals(f.Ruin), "the layers and the ruin survive a save");

        // The Warden wakes when someone comes close.
        {
            var t = Tiny(new[] { "##############################", "#@...........................#", "#............................#", "##############################" });
            Creature c = t.SpawnCreature("warden", t.World.Idx(26, 1, 1));
            c.Legend = true;
            t.Dwarves[0].Cell = t.World.Idx(2, 1, 1);
            t.Advance(30);
            Check(!c.Angry, "the Warden sleeps while no one comes near");
            t.Dwarves[0].Cell = t.World.Idx(25, 1, 1);
            Run(t, () => c.Angry, 100);
            Check(c.Angry && t.Log.Any(l => l.Text.StartsWith("The Warden wakes!")), "and wakes when someone does");
        }

        // Starsilver: the finest metal for a weapon, and the forge takes it.
        Item star = new Item { Kind = ItemKind.Weapon, Mat = Tables.Mi("starsilver") }, iron = new Item { Kind = ItemKind.Weapon, Mat = Tables.Mi("iron") };
        Check(Tables.WeaponMult(star) > Tables.WeaponMult(iron), "a starsilver hammer hits harder than iron");
        Check(Tables.Recipe("sshammer").Tier == 3 && Tables.Recipe("sshammer").Inputs.Any(n => n.Tag == "bar:starsilver"), "and a tier III forge works starsilver bars");
        Eq(Tables.M("starvein").Yields, "starsilver", "starsilver ore smelts to starsilver");
    }
}
