using System.Linq;

namespace Hammerdeep;

/// <summary>Floor styles: marked, laid by masons, kept in a save, stripped again.</summary>
public partial class SmokeTest
{
    private void Floors()
    {
        var f = Tiny(new[] { "##########", "#@.......#", "#........#", "##########" });
        f.Peaceful = true;
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        for (int k = 0; k < d.Labors.Length; k++) d.Labors[k] = true;
        int a = w.Idx(3, 1, 1), b = w.Idx(6, 2, 1);
        int n = f.Pave(new P3(3, 1, 1), new P3(6, 2, 1), 5);
        Eq(n, 8, "a box of eight floors is marked for mosaic");
        Eq(f.Pave(new P3(0, 0, 1), new P3(0, 0, 1), 5), 0, "a wall takes no floor style");
        Run(f, () => w.Floors[a] == 5 && w.Floors[b] == 5, 40000);
        Check(Enumerable.Range(3, 4).All(x => w.Floors[w.Idx(x, 1, 1)] == 5 && w.Floors[w.Idx(x, 2, 1)] == 5), "a mason lays them all");
        Check(!f.Designated.Any(c => w.Desigs[c] == Desig.Pave) && f.Paving.Count == 0, "and nothing is left marked");
        Eq(w.Floors[w.Idx(2, 1, 1)], (byte)0, "the floor outside the box is bare");
        Fortress g = Save.Read(Save.Write(f));
        Eq(g.World.Floors[a], (byte)5, "a laid floor survives a save");
        f.Pave(new P3(3, 1, 1), new P3(3, 1, 1), 0);
        Run(f, () => w.Floors[a] == 0, 20000);
        Eq(w.Floors[a], (byte)0, "Bare strips it back");
        f.Spawn(ItemKind.Pick, Tables.Mi("iron"), w.Idx(2, 2, 1));
        f.Designate(new P3(4, 1, 1), new P3(4, 1, 1), Desig.Channel);
        Run(f, () => w.Shapes[w.Idx(4, 1, 1)] == Shape.Empty, 30000);
        Eq(w.Shapes[w.Idx(4, 1, 1)], Shape.Empty, "the laid floor is channelled");
        Eq(w.Floors[w.Idx(4, 1, 1)], (byte)0, "channelling a laid floor takes the floor with it");
    }
}
