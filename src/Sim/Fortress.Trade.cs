using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>A trading people: who they are, what they bring, what they want.</summary>
public sealed class FactionDef
{
    public string Key, Name, People;
    /// <summary>The season their caravan comes in.</summary>
    public int Season;
    /// <summary>Stock categories they pay half again for.</summary>
    public StockCat Wants;
    public Rgb Color;
    public bool Dwarven;
    /// <summary>What a caravan carries: (what, how many at the least, at the most).</summary>
    public (Good good, int lo, int hi)[] Stock;
}

/// <summary>Something a caravan sells: an item kind of a material, or a live animal.</summary>
public sealed class Good
{
    public ItemKind Kind;
    public string Mat;
    public string Creature;
    public int Count;
    public int Quality;

    public Good Copy(int count) => new() { Kind = Kind, Mat = Mat, Creature = Creature, Count = count, Quality = Quality };

    [JsonIgnore] public int UnitValue => Creature != null ? Tables.Creatures[Creature].Hp / 2 + 10 : new Item { Kind = Kind, Mat = Tables.Mi(Mat), Quality = Quality }.Value;

    [JsonIgnore] public string Name => Creature != null ? Tables.Creatures[Creature].Name : new Item { Kind = Kind, Mat = Tables.Mi(Mat), Quality = Quality }.Name;
}

public enum CaravanState : byte { Coming, Trading, Leaving }

public sealed class Caravan
{
    public int Id;
    public string Faction;
    public CaravanState State;
    public long Arrived, Leave;
    public List<Good> Goods = new();
    public List<int> Merchants = new();
    public int Depot = -1;
    public int Traded;
    [JsonIgnore] public FactionDef F => Fortress.FactionDefs.First(f => f.Key == Faction);
}

/// <summary>
/// Trade: caravans from the Mountainhome and the Lowlands come each year to
/// a trade depot, stay three days, and barter by value. Standing with a
/// people grows with fair trade and gifts, and brings bigger caravans and
/// better prices.
/// </summary>
public sealed partial class Fortress
{
    public static readonly List<FactionDef> FactionDefs = new()
    {
        new FactionDef
        {
            Key = "mountainhome", Name = "the Mountainhome", People = "dwarven", Season = 2, Dwarven = true, Color = Rgb.Hex(0xb07a3a),
            Wants = StockCat.Gems | StockCat.Finished | StockCat.Weapons,
            Stock = new[]
            {
                (G(ItemKind.Bar, "iron"), 3, 8), (G(ItemKind.Bar, "bronze"), 1, 4), (G(ItemKind.Boulder, "coal"), 3, 8),
                (G(ItemKind.Pick, "iron"), 1, 2), (G(ItemKind.Weapon, "iron"), 1, 3), (G(ItemKind.Armor, "iron"), 0, 2), (G(ItemKind.Helm, "iron"), 1, 3),
                (G(ItemKind.Drink, "dimcap"), 10, 25), (G(ItemKind.Seed, "dimcap"), 3, 8), (G(ItemKind.Mechanism, "granite"), 1, 3),
                (G(ItemKind.Plant, "ration"), 4, 10),
            },
        },
        new FactionDef
        {
            Key = "lowlands", Name = "the Lowland towns", People = "lowland", Season = 1, Dwarven = false, Color = Rgb.Hex(0x4f7fa8),
            Wants = StockCat.Finished | StockCat.Furniture | StockCat.Gems | StockCat.Armor,
            Stock = new[]
            {
                (G(ItemKind.Plant, "ration"), 12, 28), (G(ItemKind.Meat, "meat"), 4, 10), (G(ItemKind.Drink, "bramble"), 8, 18),
                (G(ItemKind.Seed, "hearthwheat"), 4, 8), (G(ItemKind.Seed, "bramble"), 3, 6), (G(ItemKind.Log, "oak"), 5, 12),
                (G(ItemKind.Leather, "leather"), 2, 6), (Live("goat"), 1, 3), (Live("hen"), 2, 4),
            },
        },
    };

    private static Good G(ItemKind k, string mat) => new() { Kind = k, Mat = mat };
    private static Good Live(string creature) => new() { Creature = creature };

    public List<Caravan> Caravans = new();
    /// <summary>What each people has been asked to bring next time (goods by name): twice as many.</summary>
    public Dictionary<string, List<string>> Requests = new();
    public const int MaxRequests = 3;

    public bool Requested(string faction, Good g) => Requests.TryGetValue(faction, out var l) && l.Contains(g.Name);

    public void ToggleRequest(string faction, Good g)
    {
        if (!Requests.TryGetValue(faction, out var l)) Requests[faction] = l = new List<string>();
        if (!l.Remove(g.Name) && l.Count < MaxRequests) l.Add(g.Name);
    }
    public Dictionary<string, int> Standing = new();
    [JsonIgnore] public Caravan Trading => Caravans.FirstOrDefault(c => c.State == CaravanState.Trading);

    public const int TradeDays = 3;

    /// <summary>Their price on their own goods, and the share of value they give for ours: both better with standing.</summary>
    public float Markup(string faction) => Math.Clamp(1.35f - Standing.GetValueOrDefault(faction) * 0.003f, 1.05f, 1.35f);

    public int TheirPrice(Caravan c, Good g) => (int)Math.Ceiling(g.UnitValue * Markup(c.Faction));

    public int OurValue(Caravan c, Item it) => Math.Max(1, (int)(it.Value * ((c.F.Wants & it.Def.Cat) != 0 ? 1.5f : 0.9f)));

    // ---- arrivals -----------------------------------------------------------------------

    private void CaravanSeason()
    {
        foreach (FactionDef f in FactionDefs.Where(f => f.Season == Season))
        {
            // In a world, the Lowlanders only come if one of their towns is near.
            if (Map != null && WorldX >= 0 && CaravanHome(f.Key) == null) continue;
            SendCaravan(f);
        }
    }

    /// <summary>Four days before a season turns: word of the caravans it will bring, so there is time for a depot.</summary>
    private void CaravanWord()
    {
        int next = (Season + 1) % Tables.SeasonsPerYear;
        foreach (FactionDef f in FactionDefs.Where(f => f.Season == next))
        {
            if (Map != null && WorldX >= 0 && CaravanHome(f.Key) == null) continue;
            bool depot = Buildings.Any(b => b.Def == "depot");
            Say($"Word comes that a caravan of {f.Name} will arrive in {Tables.Seasons[next]}." + (depot ? "" : " Build a trade depot (B, D) to meet it."),
                depot ? LogKind.Info : LogKind.Bad);
        }
    }

    public Caravan SendCaravan(FactionDef f)
    {
        int standing = Standing.GetValueOrDefault(f.Key);
        var c = new Caravan { Id = NextId++, Faction = f.Key, Arrived = Tick };
        float scale = 1 + Math.Clamp(standing, -50, 200) / 100f;
        foreach (var (good, lo, hi) in f.Stock)
        {
            int n = (int)Math.Round(Math.Max(1, Rng.Range(lo, hi)) * scale * (Requested(f.Key, good) ? 2 : 1));
            if (n > 0) c.Goods.Add(good.Copy(n));
        }
        // Word of a hungry hold travels: merchants bring rations to sell it.
        int food = Items.Values.Count(i => Tables.Matches("food", i));
        if (food < Alive * 3)
        {
            Good ration = c.Goods.FirstOrDefault(g => g.Kind == ItemKind.Plant && g.Mat == "ration");
            int more = Math.Max(10, Alive * 2);
            if (ration != null) ration.Count += more; else c.Goods.Add(G(ItemKind.Plant, "ration").Copy(more));
        }
        if (Requests.Remove(f.Key, out var asked) && asked.Count > 0) Say($"The merchants of {f.Name} bring the {string.Join(", ", asked)} you asked for.", LogKind.Good);
        P3 at = EdgeArrival();
        for (int k = 0; k < 3; k++)
        {
            Creature m = SpawnCreature(f.Dwarven ? "trader" : "merchant", FindStandable(at, 2));
            m.Caravan = c.Id;
            c.Merchants.Add(m.Id);
        }
        Caravans.Add(c);
        Site home = CaravanHome(f.Key);
        Say($"A caravan from {(home != null ? $"{home.Name} ({f.Name})" : f.Name)} has come over the hills{(Buildings.Any(b => b.Def == "depot" && b.Built) ? "" : " -- but you have no trade depot!")}", LogKind.Alert, World.Idx(at));
        foreach (Dwarf d in Living) AddThought(d, "caravan");
        Count("caravans");
        return c;
    }

    private Building Depot() => Buildings.Where(b => b.Def == "depot" && b.Built).OrderBy(b => b.Id).FirstOrDefault();

    /// <summary>A merchant's day: to the depot, trade there, and away.</summary>
    private void MerchantThink(Creature m)
    {
        Caravan c = Caravans.FirstOrDefault(x => x.Id == m.Caravan);
        if (c == null) { m.Dead = true; return; }
        P3 at = World.Pos(m.Cell);
        switch (c.State)
        {
            case CaravanState.Coming:
                Building depot = Depot();
                if (depot == null)
                {
                    // They wait a day -- or, while a depot is being built for them, a few.
                    long patience = Tables.TicksPerDay * (Buildings.Any(b => b.Def == "depot") ? TradeDays : 1);
                    if (Tick - c.Arrived > patience) { c.State = CaravanState.Leaving; Say($"Finding no trade depot, the caravan from {c.F.Name} turns back.", LogKind.Bad); Standing[c.Faction] = Standing.GetValueOrDefault(c.Faction) - 5; }
                    else if (at.Dist(Embark) > 8) MarchTo(m, Embark);
                    return;
                }
                P3 dp = depot.Center;
                if (at.Dist(dp) <= 2)
                {
                    c.State = CaravanState.Trading;
                    c.Depot = depot.Id;
                    c.Leave = Tick + Tables.TicksPerDay * TradeDays;
                    Say($"The merchants of {c.F.Name} have set out their wares at the depot.", LogKind.Alert, World.Idx(dp));
                    return;
                }
                MarchTo(m, dp);
                return;
            case CaravanState.Trading:
                if (Tick > c.Leave || DepotGone(c)) { c.State = CaravanState.Leaving; Say($"The caravan from {c.F.Name} packs up and leaves.", LogKind.Info); return; }
                if (BuildingById(c.Depot) is Building d && at.Dist(d.Center) > 2) CreatureWalk(m, FindStandable(d.Center, 1), 4000);
                else if (Rng.Chance(0.1)) CreatureWalk(m, FindStandable(at, 1), 500);
                return;
            default:
                if (at.X <= 0 || at.Y <= 0 || at.X >= World.W - 1 || at.Y >= World.H - 1 || Tick > c.Leave + Tables.TicksPerDay * 2)
                {
                    m.Dead = true;
                    c.Merchants.Remove(m.Id);
                    if (c.Merchants.Count == 0) Caravans.Remove(c);
                    return;
                }
                CreatureWalk(m, World.Idx(EdgeNear(at)), 60000);
                if (m.Path == null) m.NextThink = Tick + 100;
                return;
        }
    }

    private bool DepotGone(Caravan c) => BuildingById(c.Depot) is not { Built: true };

    private void MarchTo(Creature m, P3 to)
    {
        CreatureWalk(m, FindStandable(to, 2), 150000);
        if (m.Path == null) m.NextThink = Tick + 60;
    }

    // ---- bartering ------------------------------------------------------------------------

    /// <summary>What the fortress can offer: items in the stores, not in use.</summary>
    public IEnumerable<Item> Tradeable() => Items.Values.Where(i => i.Free && i.Kind is not (ItemKind.Corpse or ItemKind.Carcass));

    public sealed class Deal
    {
        public List<Item> Offer = new();
        public Dictionary<Good, int> Ask = new();
    }

    public int OfferValue(Caravan c, Deal d) => d.Offer.Sum(i => OurValue(c, i));
    public int AskValue(Caravan c, Deal d) => d.Ask.Sum(kv => TheirPrice(c, kv.Key) * kv.Value);

    /// <summary>Whether the merchants take a deal, and what they say.</summary>
    public (bool ok, string why) Weigh(Caravan c, Deal d)
    {
        int offer = OfferValue(c, d), ask = AskValue(c, d);
        if (offer == 0 && ask == 0) return (false, "Nothing has been put on the table.");
        if (offer >= ask) return (true, ask == 0 ? "A gift! The merchants bow deeply." : offer > ask * 1.4 ? "The merchants can hardly believe their luck." : "A fair bargain. Done!");
        return (false, $"\"That will not do.\" They want {ask - offer} more in value.");
    }

    /// <summary>Close a deal: our goods leave, theirs appear at the depot. False if refused.</summary>
    public bool Trade(Caravan c, Deal d)
    {
        if (c.State != CaravanState.Trading || !Weigh(c, d).ok) return false;
        Building depot = BuildingById(c.Depot);
        int offer = OfferValue(c, d), ask = AskValue(c, d);
        foreach (Item it in d.Offer.ToList()) if (Items.ContainsKey(it.Id) && it.Free) Destroy(it);
        P3 at = depot.Center;
        foreach (var (g, n) in d.Ask)
        {
            int take = Math.Min(n, g.Count);
            for (int k = 0; k < take; k++)
            {
                if (g.Creature != null)
                {
                    Creature a = SpawnCreature(g.Creature, FindStandable(at, 2));
                    a.Tame = true;
                    a.Female = Rng.Chance(0.6);
                }
                else Spawn(g.Kind, Tables.Mi(g.Mat), FindStandable(at, 1), g.Quality);
            }
            g.Count -= take;
        }
        c.Goods.RemoveAll(g => g.Count <= 0);
        c.Traded += offer;
        // Profit and gifts warm a people to the hold.
        int warmth = Math.Max(0, offer - ask) / 20 + Math.Min(10, offer / 200);
        Standing[c.Faction] = Math.Min(200, Standing.GetValueOrDefault(c.Faction) + warmth);
        Count("trades");
        Count("traded_value", offer);
        Say(ask == 0 ? $"You gave the merchants of {c.F.Name} gifts worth {offer}." : $"Traded goods worth {offer} for {ask} with {c.F.Name}.", LogKind.Good, World.Idx(at));
        return true;
    }
}
