using System.Linq;

namespace Hammerdeep;

/// <summary>Levers, pressure plates, links and what they work; the lift.</summary>
public partial class SmokeTest
{
    private void Mechanisms()
    {
        // A corridor: a lever, a door, a cage trap and a bolt launcher down a long run.
        var f = Tiny(
            new[] { "################", "#@.............>#", "################" },
            new[] { "################", "#..............X#", "################" },
            new[] { "################", "#..............<#", "################" });
        f.Peaceful = true;
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        for (int k = 0; k < d.Labors.Length; k++) d.Labors[k] = true;
        Building Built(string def, int x, int y, int z)
        {
            Building b = f.Place(def, x, y, z);
            Check(b != null, $"a {def} can be placed at {x},{y},{z} ({f.CanPlace(def, x, y, z)})");
            b.Built = true;
            return b;
        }
        Building lever = Built("lever", 2, 1, 3), door = Built("door", 5, 1, 3);
        for (int k = 0; k < 3; k++) f.Spawn(ItemKind.Mechanism, Tables.Mi("iron"), w.Idx(3, 1, 3));
        Check(f.CannotLink(door, lever) != null, "a door switches nothing");
        Link l = f.AddLink(lever, door);
        Check(f.CannotLink(lever, door) != null, "and a link is made once");
        Run(f, () => l.Built, 20000);
        Check(l.Built, "a dwarf lays the linkage");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.Mechanism), 2, "with one mechanism");
        f.WantPull(lever);
        Run(f, () => lever.On, 10000);
        Check(lever.On && door.Locked && w.Locked.Contains(w.Idx(5, 1, 3)), "a dwarf pulls the lever and the door is locked");
        Check(f.Path.Find(d.Cell, w.Idx(8, 1, 3)) == null, "nobody gets past it");
        f.WantPull(lever);
        Run(f, () => !lever.On, 10000);
        Check(!door.Locked && f.Path.Find(d.Cell, w.Idx(8, 1, 3)) != null, "pulled again, it opens");

        // A pressure plate under a foe; a cage trap linked to it.
        Building plate = Built("plate", 9, 1, 3), cage = Built("cage", 10, 1, 3);
        f.Links.Add(new Link { Id = f.NextId++, Switch = plate.Id, Target = cage.Id, Built = true });
        Creature c = f.SpawnCreature("crawler", w.Idx(10, 1, 3));
        c.NextThink = long.MaxValue;
        Creature onPlate = f.SpawnCreature("crawler", w.Idx(9, 1, 3));
        onPlate.NextThink = long.MaxValue;
        f.Tick += 4 - f.Tick % 4;
        f.StepPlates();
        Check(plate.On, "a foe on a pressure plate sets it off");
        Check(c.Dead && cage.Caught == "crawler", "and the cage drops over the one beside it");
        onPlate.Dead = true;
        f.Tick += Fortress.PlateHold + 8 - f.Tick % 4;
        f.StepPlates();
        Check(!plate.On, "the plate goes off once nothing stands on it");
        f.EmptyCage(cage, false);
        Check(cage.Caught == null && f.Creatures.Any(x => !x.Dead && x.Cell == w.Idx(10, 1, 3)), "a caged beast can be let go");
        foreach (Creature x in f.Creatures) x.Dead = true;

        // A launcher fires down the corridor at the first foe.
        Building launcher = Built("launcher", 6, 1, 3);
        launcher.Facing = 1;
        Creature target = f.SpawnCreature("crawler", w.Idx(12, 1, 3));
        target.NextThink = long.MaxValue;
        int hp = target.Hp;
        f.Fire(launcher);
        Check(target.Dead || target.Hp < hp, "a launcher's bolts strike a foe down the corridor");
        Check(f.Shots.Count == 1, "and the view is told of the volley");
        int hp2 = target.Hp;
        f.Fire(launcher);
        Check(target.Dead || target.Hp == hp2, "it must reload before it fires again");

        // The lift: on the stair shaft, the ride is quicker; stopped, nobody rides it.
        int top = w.Idx(15, 1, 3), bottom = w.Idx(15, 1, 1);
        int stairs = f.Path.Cost(f.Path.Find(top, bottom));
        string whyLift = f.CanPlace("lift", 15, 1, 2);
        Building lift = f.Place("lift", 15, 1, 2);
        Check(lift != null && lift.Z == 1 && lift.Top == 3, $"a lift runs the whole stair shaft ({whyLift}; {lift?.Z}..{lift?.Top})");
        Check(f.CanPlace("lift", 3, 1, 3) != null, "and needs one");
        foreach (var (kind, n) in new[] { (ItemKind.Block, 6), (ItemKind.Bar, 4) }) for (int k = 0; k < n; k++) f.Spawn(kind, Tables.Mi(kind == ItemKind.Bar ? "iron" : "granite"), w.Idx(12, 1, 3));
        for (int k = 0; k < 2; k++) f.Spawn(ItemKind.Mechanism, Tables.Mi("iron"), w.Idx(12, 1, 3));
        Run(f, () => lift.Built, 60000);
        Check(lift.Built, "the lift is built");
        Check(f.Path.Cost(f.Path.Find(top, bottom)) < stairs, "riding it is quicker than the stairs");
        f.SetLiftStopped(lift, true);
        Check(f.Path.Find(top, bottom) == null, "a stopped lift carries nobody");
        Fortress g = Save.Read(Save.Write(f));
        Check(g.Path.Find(g.World.Idx(15, 1, 3), g.World.Idx(15, 1, 1)) == null && g.Links.Count == 2, "the lift stays stopped and the links stay laid after a save");
        f.SetLiftStopped(lift, false);
        Check(f.Path.Find(top, bottom) != null, "started again, it runs");

        // A hatch over a pit: walked over until it opens, then whatever is on it drops.
        var h = Tiny(new[] { "#######", "#@. ..#", "#######" }, new[] { "#######", "###.###", "#######" });
        h.Peaceful = true;
        int pit = h.World.Idx(3, 1, 2);
        Check(h.CanPlace("hatch", 2, 1, 2) != null, "a hatch needs a pit under it");
        Building hatch = h.Place("hatch", 3, 1, 2);
        h.Spawn(ItemKind.Mechanism, Tables.Mi("iron"), h.World.Idx(2, 1, 2));
        h.Spawn(ItemKind.Block, Tables.Mi("granite"), h.World.Idx(2, 1, 2));
        Run(h, () => hatch.Built, 6000);
        Check(hatch.Built && h.Path.Find(h.Dwarves[0].Cell, h.World.Idx(5, 1, 2)) != null, "a shut hatch is walked over");
        Creature fall = h.SpawnCreature("crawler", pit);
        fall.NextThink = long.MaxValue;
        Building hl = h.Place("lever", 1, 1, 2); hl.Built = true;
        h.Links.Add(new Link { Id = h.NextId++, Switch = hl.Id, Target = hatch.Id, Built = true });
        h.Signal(hl, true);
        Check(h.World.Shapes[pit] == Shape.Empty && h.World.Pos(fall.Cell).Z == 1, "opened, it drops what stood on it into the pit");
    }
}
