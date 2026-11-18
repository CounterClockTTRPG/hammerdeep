using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// A plain-rules player: digs a stair shaft and a few halls, lays out
/// stockpiles, workshops, farms and bedrooms, and keeps the work orders
/// topped up. The self-check plays whole years with it, and it builds the
/// fortresses the walkthrough photographs.
/// </summary>
public sealed class Overseer
{
    private readonly Fortress F;
    private World W => F.World;
    public int Phase;
    private P3 _shaft;
    private int _hallZ, _bedZ, _farmZ, _mineZ;
    private P3 _hall0;
    private bool _farmUnderground;
    private readonly List<string> _notes = new();
    public IReadOnlyList<string> Notes => _notes;
    public P3 Shaft => _shaft;
    public int HallZ => _hallZ;
    public int BedZ => _bedZ;
    public int FarmZ => _farmZ;
    public int MineZ => _mineZ;
    public P3 HallCenter => new(_hall0.X + 7, _hall0.Y + 5, _hallZ);

    public Overseer(Fortress f) { F = f; }

    /// <summary>Look things over and give orders; call every few hundred ticks.</summary>
    public void Think()
    {
        switch (Phase)
        {
            case 0: Begin(); break;
            case 1: if (Dug(HallBox())) { Furnish(); Phase = 2; } break;
            default: Maintain(); break;
        }
    }

    private bool Dug((P3 a, P3 b) box)
    {
        for (int y = box.a.Y; y <= box.b.Y; y++)
            for (int x = box.a.X; x <= box.b.X; x++)
                if (W.Shapes[W.Idx(x, y, box.a.Z)] == Shape.Wall) return false;
        return true;
    }

    private (P3, P3) HallBox() => (_hall0, new P3(_hall0.X + 14, _hall0.Y + 10, _hallZ));

    private void Begin()
    {
        P3 e = F.Embark;
        // The shaft: a few cells east of the wagon, straight down.
        _shaft = new P3(e.X + 4, e.Y, e.Z);
        if (!W.Walkable(_shaft)) _shaft = e;
        _hallZ = _shaft.Z - 3;
        _bedZ = _shaft.Z - 4;
        _farmZ = _shaft.Z - 2;
        _mineZ = Math.Max(F.CavernZ + 4, _shaft.Z - 12);
        F.Designate(_shaft, _shaft, Desig.StairDown);
        F.Designate(new P3(_shaft.X, _shaft.Y, _shaft.Z - 1), new P3(_shaft.X, _shaft.Y, _mineZ), Desig.StairUD);
        // The great hall, its doorway to the shaft.
        _hall0 = new P3(_shaft.X + 2, _shaft.Y - 5, _hallZ);
        F.Designate(new P3(_shaft.X + 1, _shaft.Y, _hallZ), new P3(_shaft.X + 1, _shaft.Y, _hallZ), Desig.Dig);
        var (a, b) = HallBox();
        F.Designate(a, b, Desig.Dig);
        // Bedrooms: a corridor west of the shaft with cells off it.
        P3 bc = new(_shaft.X - 1, _shaft.Y, _bedZ);
        F.Designate(new P3(_shaft.X - 14, _shaft.Y, _bedZ), bc, Desig.Dig);
        F.Designate(new P3(_shaft.X - 14, _shaft.Y - 2, _bedZ), new P3(_shaft.X - 2, _shaft.Y - 1, _bedZ), Desig.Dig);
        F.Designate(new P3(_shaft.X - 14, _shaft.Y + 1, _bedZ), new P3(_shaft.X - 2, _shaft.Y + 2, _bedZ), Desig.Dig);
        // The farm level: soil, if there is any this deep.
        P3 f0 = new(_shaft.X - 9, _shaft.Y - 3, _farmZ), f1 = new(_shaft.X - 1, _shaft.Y + 3, _farmZ);
        _farmUnderground = Tables.Mats[W.Mats[W.Idx(f0)]].Kind == MatKind.Soil && Tables.Mats[W.Mats[W.Idx(f1)]].Kind == MatKind.Soil;
        if (_farmUnderground) F.Designate(f0, f1, Desig.Dig);
        // A first stockpile at the wagon, and the meeting place.
        F.AddZone(ZoneKind.Stockpile, new P3(e.X - 3, e.Y - 6, e.Z), new P3(e.X + 3, e.Y - 3, e.Z));
        F.AddZone(ZoneKind.Meeting, new P3(e.X - 2, e.Y - 2, e.Z), new P3(e.X + 2, e.Y + 2, e.Z));
        // Fell the nearest woods and gather what grows.
        ChopNear(e, 24);
        GatherNear(e, 10);
        // A surface farm for what needs the sky.
        P3 sf = SurfaceFarmSpot();
        if (sf.X >= 0) F.AddZone(ZoneKind.Farm, sf, new P3(sf.X + 4, sf.Y + 4, sf.Z), "bramble");
        // The first shops on the surface, until the hall is open.
        PlaceNear("carpenter", new P3(e.X - 6, e.Y + 3, e.Z));
        PlaceNear("mason", new P3(e.X + 2, e.Y + 4, e.Z));
        Phase = 1;
        _notes.Add("Struck the earth: shaft, hall, bedrooms" + (_farmUnderground ? ", soil farm" : ""));
    }

    private P3 SurfaceFarmSpot()
    {
        P3 e = F.Embark;
        Mat crop = Tables.M("bramble");
        for (int r = 6; r < 30; r += 2)
            for (int t = 0; t < 30; t++)
            {
                int x = e.X + F.Rng.Range(-r, r), y = e.Y + F.Rng.Range(-r, r);
                bool ok = true;
                for (int dy = 0; dy < 5 && ok; dy++)
                    for (int dx = 0; dx < 5 && ok; dx++)
                        ok = W.In(x + dx, y + dy, e.Z) && F.Farmable(W.Idx(x + dx, y + dy, e.Z), crop) && F.ZoneAt(x + dx, y + dy, e.Z) == null;
                if (ok) return new P3(x, y, e.Z);
            }
        return new P3(-1, -1, -1);
    }

    private void ChopNear(P3 at, int n)
    {
        var trees = new List<(int c, int d)>();
        for (int y = at.Y - 30; y <= at.Y + 30; y++)
            for (int x = at.X - 30; x <= at.X + 30; x++)
                for (int z = at.Z - 2; z <= at.Z + 2; z++)
                {
                    if (!W.In(x, y, z)) continue;
                    int c = W.Idx(x, y, z);
                    if (W.Shapes[c] == Shape.Tree && W.Desigs[c] == Desig.None) trees.Add((c, new P3(x, y, z).Dist(at)));
                }
        foreach (var (c, _) in trees.OrderBy(t => t.d).Take(n)) F.Designate(W.Pos(c), W.Pos(c), Desig.Chop);
    }

    private void GatherNear(P3 at, int n)
    {
        int done = 0;
        for (int r = 1; r < 30 && done < n; r++)
            for (int y = at.Y - r; y <= at.Y + r && done < n; y++)
                for (int x = at.X - r; x <= at.X + r && done < n; x++)
                {
                    if (Math.Max(Math.Abs(x - at.X), Math.Abs(y - at.Y)) != r || !W.In(x, y, at.Z)) continue;
                    int c = W.Idx(x, y, at.Z);
                    if (W.Shapes[c] == Shape.Floor && W.Has(c, CellFlag.Shrub) && W.Desigs[c] == Desig.None) { F.Designate(W.Pos(c), W.Pos(c), Desig.Gather); done++; }
                }
    }

    /// <summary>Place a building at the first open spot spiralling out from <paramref name="p"/>.</summary>
    private Building PlaceNear(string def, P3 p, int radius = 8)
    {
        for (int r = 0; r <= radius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    int x = p.X + dx, y = p.Y + dy;
                    if (F.CanPlace(def, x, y, p.Z) != null) continue;
                    // Keep the stair shaft and the hall door clear.
                    if (Math.Abs(x - _shaft.X) <= 1 + Tables.Build[def].W && Math.Abs(y - _shaft.Y) <= 1 + Tables.Build[def].H && Tables.Build[def].W > 1) continue;
                    if (F.ZoneAt(x, y, p.Z)?.Kind is ZoneKind.Stockpile or ZoneKind.Farm) continue;
                    return F.Place(def, x, y, p.Z);
                }
        return null;
    }

    private void Furnish()
    {
        var (a, b) = HallBox();
        // Shops along the north wall, stockpiles in the middle, dining to the south.
        string[] shops = { "kitchen", "brewery", "craftsdwarf", "furnace", "smelter" };
        F.Place("forge", b.X - 2, b.Y - 2, _hallZ);
        int x = a.X;
        foreach (string s in shops)
        {
            F.Place(s, x, a.Y, _hallZ);
            x += 3;
            if (x + 2 > b.X) break;
        }
        F.AddZone(ZoneKind.Stockpile, new P3(a.X, a.Y + 4, _hallZ), new P3(b.X, a.Y + 6, _hallZ));
        F.AddZone(ZoneKind.Meeting, new P3(a.X, a.Y + 7, _hallZ), new P3(b.X, b.Y, _hallZ));
        if (_farmUnderground)
            F.AddZone(ZoneKind.Farm, new P3(_shaft.X - 9, _shaft.Y - 3, _farmZ), new P3(_shaft.X - 2, _shaft.Y + 3, _farmZ), "dimcap");
        _notes.Add("Furnished the great hall");
    }

    private int Have(Func<Item, bool> p) => F.Items.Values.Count(p);
    private int Planned(string recipe) => F.Buildings.Sum(b => b.Orders.Where(o => o.Recipe == recipe).Sum(o => o.Repeat ? 99 : o.Left));
    private Building Shop(string def) => F.Buildings.FirstOrDefault(b => b.Def == def && b.Built);

    private void Order(string shop, string recipe, int want, int have)
    {
        Building b = Shop(shop);
        if (b == null) return;
        int planned = Planned(recipe);
        if (have + planned < want && planned < 3) F.AddOrder(b, recipe, Math.Min(3, want - have - planned));
    }

    private void Maintain()
    {
        int pop = F.Alive;
        int drinks = Have(i => i.Kind == ItemKind.Drink), meals = Have(i => i.Kind == ItemKind.Meal);
        int food = Have(i => Tables.Matches("food", i));
        Order("brewery", "ale", pop * 3, drinks);
        if (food < pop * 6 || meals < pop) Order("kitchen", "stew", pop * 2, meals);
        int beds = Have(i => i.Kind == ItemKind.Bed) + F.Buildings.Count(b => b.Def == "bed");
        Order("carpenter", "bed", pop, beds);
        int tables = Have(i => i.Kind == ItemKind.Table) + F.Buildings.Count(b => b.Def == "table");
        int chairs = Have(i => i.Kind == ItemKind.Chair) + F.Buildings.Count(b => b.Def == "chair");
        Order("mason", "stable", Math.Min(8, pop / 2 + 1), tables);
        Order("mason", "schair", Math.Min(8, pop / 2 + 1), chairs);
        int picks = Have(i => i.Kind == ItemKind.Pick);
        Order("forge", "pick", 4, picks);
        Order("smelter", "smelt", 6, Have(i => i.Kind == ItemKind.Bar));
        if (Have(i => Tables.Matches("fuel", i)) < 4) Order("furnace", "charcoal", 4, 0);
        if (Have(i => i.Kind == ItemKind.RoughGem) > 0) Order("craftsdwarf", "cutgem", 2, 0);
        if (Have(i => i.Kind == ItemKind.Boulder && i.M.Kind == MatKind.Rock) > 30) Order("craftsdwarf", "figurine", 6, Have(i => i.Kind == ItemKind.Craft));

        // Furniture into place: beds in the bedrooms, tables and chairs in the hall.
        PlaceFurniture(ItemKind.Bed, "bed", BedSpots());
        PlaceFurniture(ItemKind.Table, "table", DiningSpots(true));
        PlaceFurniture(ItemKind.Chair, "chair", DiningSpots(false));

        Defend();

        // More room as the hold grows: more bedrooms, and mining for ore.
        if (F.Rng.Chance(0.1) && F.Designated.Count < 40) Explore();
        if (F.Rng.Chance(0.05) && F.Designated.Count(c => W.Desigs[c] == Desig.Chop) < 5) ChopNear(F.Embark, 10);
        if (F.Rng.Chance(0.05)) GatherNear(F.Embark, 4);
    }

    private IEnumerable<P3> BedSpots()
    {
        for (int x = _shaft.X - 14; x <= _shaft.X - 2; x += 2)
        {
            yield return new P3(x, _shaft.Y - 2, _bedZ);
            yield return new P3(x, _shaft.Y + 2, _bedZ);
        }
        for (int x = _shaft.X - 13; x <= _shaft.X - 3; x += 2)
        {
            yield return new P3(x, _shaft.Y - 2, _bedZ);
            yield return new P3(x, _shaft.Y + 2, _bedZ);
        }
    }

    private IEnumerable<P3> DiningSpots(bool tables)
    {
        var (a, b) = HallBox();
        for (int y = a.Y + 8; y <= b.Y - 1; y += 2)
            for (int x = a.X + 1; x <= b.X - 1; x += 3)
                yield return tables ? new P3(x, y, _hallZ) : new P3(x + 1, y, _hallZ);
    }

    private void PlaceFurniture(ItemKind kind, string def, IEnumerable<P3> spots)
    {
        int spare = F.Items.Values.Count(i => i.Kind == kind && i.ReservedBy < 0 && i.InBuilding < 0 && i.HeldBy < 0) - F.Buildings.Count(b => b.Def == def && !b.Built);
        foreach (P3 p in spots)
        {
            if (spare <= 0) return;
            if (F.CanPlace(def, p.X, p.Y, p.Z) != null) continue;
            if (F.ZoneAt(p.X, p.Y, p.Z)?.Kind == ZoneKind.Stockpile) continue;
            F.Place(def, p.X, p.Y, p.Z);
            spare--;
        }
    }

    private bool _refuse;

    /// <summary>A squad at the stair head, kit from the forge, a door on the hall, traps on the way in, the alarm in a raid.</summary>
    private void Defend()
    {
        if (!_refuse)
        {
            P3 e = F.Embark;
            for (int t = 0; t < 30 && !_refuse; t++)
            {
                P3 p = new(e.X - 14 + F.Rng.Range(-3, 3), e.Y + 10 + F.Rng.Range(-3, 3), e.Z);
                if (!W.In(p) || !W.Walkable(p)) continue;
                Zone z = F.AddZone(ZoneKind.Stockpile, p, new P3(p.X + 2, p.Y + 2, p.Z));
                z.Cats = StockCat.Refuse;
                z.Name = "Refuse pile";
                F.RefreshStored();
                _refuse = true;
            }
        }
        int want = F.Alive >= 30 ? 6 : F.Alive >= 8 ? 3 : 0;
        if (want > 0)
        {
            Squad s = F.Squads.FirstOrDefault();
            if (s == null)
            {
                s = F.NewSquad();
                F.SetOrder(s, SquadOrder.Defend, new P3(_shaft.X, _shaft.Y + 2, _shaft.Z));
                _notes.Add($"Raised a squad: {s.Name}");
            }
            foreach (Dwarf d in F.Living.Where(d => d.Squad < 0 && !d.Wounded).OrderByDescending(d => d.Skill(Labor.Fighting) * 3 + (d.Clan == "stoneward" ? 4 : 0) - d.Skill(Labor.Mining)).Take(want - s.Members.Count))
                F.Enlist(s, d);
        }
        int soldiers = F.Squads.Sum(s => s.Members.Count);
        Order("forge", "hammer", soldiers, Have(i => i.Kind == ItemKind.Weapon) + F.Living.Count(d => d.Weapon >= 0));
        Order("forge", "armor", soldiers, Have(i => i.Kind == ItemKind.Armor) + F.Living.Count(d => d.Armor >= 0));
        Order("forge", "helm", soldiers, Have(i => i.Kind == ItemKind.Helm) + F.Living.Count(d => d.Helm >= 0));
        Order("carpenter", "shield", soldiers, Have(i => i.Kind == ItemKind.Shield) + F.Living.Count(d => d.Shield >= 0));
        // A door on the hall, traps round the stair head.
        Order("carpenter", "wdoor", 1, Have(i => i.Kind == ItemKind.Door) + F.Buildings.Count(b => b.Def == "door"));
        PlaceFurniture(ItemKind.Door, "door", new[] { new P3(_shaft.X + 1, _shaft.Y, _hallZ) });
        Order("mason", "mechanism", 3, Have(i => i.Kind == ItemKind.Mechanism) + F.Buildings.Count(b => b.Def == "trap"));
        var trapSpots = new[] { new P3(_shaft.X - 2, _shaft.Y, _shaft.Z), new P3(_shaft.X + 2, _shaft.Y, _shaft.Z), new P3(_shaft.X, _shaft.Y - 2, _shaft.Z) };
        PlaceFurniture(ItemKind.Mechanism, "trap", trapSpots);
        // The alarm for as long as raiders are about.
        bool raid = F.RaidUnderway;
        if (raid && !F.Alarm) { F.Alarm = true; _notes.Add($"Sounded the alarm ({F.Date})"); }
        if (!raid && F.Alarm) F.Alarm = false;
    }

    private int _explore;

    private void Explore()
    {
        // Long tunnels off the bottom of the shaft find ore and, sooner or later, the caverns.
        _explore++;
        int z = _mineZ - (_explore / 4) * 2;
        if (z < 4) return;
        if (_explore % 4 == 0 && _explore > 0) F.Designate(new P3(_shaft.X, _shaft.Y, z + 1), new P3(_shaft.X, _shaft.Y, z), Desig.StairUD);
        int dir = _explore % 4;
        int len = 20;
        P3 s = new(_shaft.X, _shaft.Y, z), e = dir switch
        {
            0 => new P3(_shaft.X + len, _shaft.Y, z),
            1 => new P3(_shaft.X - len, _shaft.Y, z),
            2 => new P3(_shaft.X, _shaft.Y + len, z),
            _ => new P3(_shaft.X, _shaft.Y - len, z),
        };
        F.Designate(s, e, Desig.Dig);
        // And a second row of bedrooms when the first fills.
        if (F.Alive > 20 && _explore == 3)
        {
            F.Designate(new P3(_shaft.X - 14, _shaft.Y - 4, _bedZ), new P3(_shaft.X - 2, _shaft.Y - 3, _bedZ), Desig.Dig);
            F.Designate(new P3(_shaft.X - 14, _shaft.Y + 3, _bedZ), new P3(_shaft.X - 2, _shaft.Y + 4, _bedZ), Desig.Dig);
        }
    }
}
