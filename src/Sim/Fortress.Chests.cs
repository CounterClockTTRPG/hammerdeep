using System.Linq;

namespace Hammerdeep;

/// <summary>
/// A chest of one's own: a dwarf without one claims a free chest -- the one nearest their own bed, if there is
/// one -- and keeps their things in it. Each day that is worth a little (+2), and a chest beside their own bed,
/// a room of their own, a little more (+4). Freed, like a bed, when its owner dies or leaves.
/// </summary>
public partial class Fortress
{
    /// <summary>Daily: claims, and the comfort of having one's things in a chest of one's own.</summary>
    internal void ChestsDaily()
    {
        var free = Buildings.Where(b => b.Built && b.Def == "chest" && (b.Owner < 0 || DwarfById(b.Owner) is not { Dead: false })).ToList();
        foreach (Building c in free) c.Owner = -1;
        foreach (Dwarf d in Living)
        {
            Building mine = d.Chest >= 0 ? BuildingById(d.Chest) : null;
            if (mine == null || mine.Owner != d.Id)
            {
                d.Chest = -1;
                P3 near = d.Bed >= 0 && BuildingById(d.Bed) is Building bed ? new P3(bed.X, bed.Y, bed.Z) : World.Pos(d.Cell);
                mine = free.Where(c => c.Owner < 0).OrderBy(c => new P3(c.X, c.Y, c.Z).Dist(near)).FirstOrDefault();
                if (mine == null) continue;
                mine.Owner = d.Id;
                d.Chest = mine.Id;
                Count("chests_claimed");
            }
            AddThought(d, ChestBesideBed(d, mine) ? "own_room" : "own_chest");
        }
    }

    /// <summary>A chest within two cells of the owner's own bed, on its level: a room of their own.</summary>
    private bool ChestBesideBed(Dwarf d, Building chest) =>
        d.Bed >= 0 && BuildingById(d.Bed) is Building bed && bed.Z == chest.Z && System.Math.Abs(bed.X - chest.X) <= 2 && System.Math.Abs(bed.Y - chest.Y) <= 2;
}
