using System.Linq;

namespace Hammerdeep;

/// <summary>A chest of one's own.</summary>
public partial class SmokeTest
{
    private void Chests()
    {
        var f = Tiny(new[] { "##############", "#@@..........#", "#............#", "##############" });
        f.Peaceful = true;
        Dwarf a = f.Dwarves[0], b = f.Dwarves[1];
        Building bed = f.Place("bed", 3, 1, 1); bed.Built = true; bed.Owner = a.Id; a.Bed = bed.Id;
        Building near = f.Place("chest", 4, 1, 1); near.Built = true;
        Building far = f.Place("chest", 11, 2, 1); far.Built = true;
        f.ChestsDaily();
        Eq(a.Chest, near.Id, "a dwarf claims the free chest nearest their bed");
        Eq(near.Owner, a.Id, "and it is theirs");
        Check(a.Thoughts.Any(t => t.Id == "own_room"), "a chest beside their own bed: a room of their own");
        Eq(b.Chest, far.Id, "another dwarf takes the other chest");
        Check(b.Thoughts.Any(t => t.Id == "own_chest"), "and is pleased to keep their things in it");
        f.Kill(a, "old age");
        Eq(near.Owner, -1, "a chest is freed when its owner dies");
        Fortress g = Save.Read(Save.Write(f));
        Check(g.Dwarves.First(x => x.Id == b.Id).Chest == far.Id, "a claimed chest survives a save");
    }
}
