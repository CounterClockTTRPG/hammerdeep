using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

public enum Terrain : byte { Lake, Marsh, Plains, Forest, Hills, Mountains, Peaks }

public enum SiteKind : byte { Mountainhome, Hold, Town, Warren, Burrow, Grove, Crag, Ruin, Fortress }

public sealed class Site
{
    public int Id;
    public string Name;
    public SiteKind Kind;
    public int X, Y;
    public int Founded;
    public int Size;
    public bool Fallen;
    public string FallenTo;
}

public sealed class HistoryEvent
{
    public int Year;
    public string Text;
    public int Site = -1;
}

/// <summary>What the land at an embark gives the local map.</summary>
public sealed class EmbarkLand
{
    public float Mountain = 0.5f, Forest = 0.5f, Wet = 0.5f, Richness = 0.5f;
    public string Describe() =>
        $"{(Mountain > 0.65f ? "steep, rocky" : Mountain > 0.35f ? "hilly" : "gentle")} ground, {(Forest > 0.6f ? "thick woods" : Forest > 0.3f ? "scattered woods" : "few trees")}, {(Wet > 0.6f ? "many pools" : Wet > 0.3f ? "a few ponds" : "dry")}, {(Richness > 0.65f ? "rich in ore" : Richness > 0.35f ? "some ore" : "poor in ore")}";
}

/// <summary>
/// The world around the hold: a map of terrain, the sites of the peoples who
/// live in it, and a couple of centuries of history. All of it comes from one
/// seed, so a save keeps only the seed and where the expedition stopped.
/// </summary>
public sealed class WorldMap
{
    public const int W = 56, H = 36, HistoryYears = 220;
    public readonly ulong Seed;
    public readonly Terrain[] Tiles = new Terrain[W * H];
    public readonly float[] Height = new float[W * H], Moist = new float[W * H];
    public readonly bool[] River = new bool[W * H];
    public readonly List<Site> Sites = new();
    public readonly List<HistoryEvent> History = new();
    public string Name;
    private readonly Rng _rng;

    private static readonly string[] A = { "Ash", "Iron", "Brindle", "Hollow", "Grey", "Thorn", "Raven", "Under", "Gold", "Frost", "Mire", "Ember", "Bright", "Wold", "Stag", "Elder" };
    private static readonly string[] B = { "ford", "mere", "wick", "holm", "stead", "ton", "fell", "dale", "brook", "march", "moor", "gate" };
    private static readonly string[] GrimA = { "Gut", "Skull", "Rot", "Gnash", "Blight", "Mawl", "Scab", "Fester" };
    private static readonly string[] GrimB = { "rot", "pit", "maw", "hole", "warren", "mire", "den" };

    public WorldMap(ulong seed)
    {
        Seed = seed;
        _rng = new Rng(seed ^ 0xA5A5A5A5UL);
        uint s = (uint)seed ^ (uint)(seed >> 32);
        Name = $"the {_rng.Pick(new[] { "Reach", "Marches", "Wilds", "Shattered Lands", "Old Kingdom", "Long Vale" })} of {_rng.Pick(A)}{_rng.Pick(B)}";
        // Terrain: an elevation field with a mountain spine, and moisture.
        float spineX = W * (0.45f + (float)_rng.Float() * 0.2f), tilt = ((float)_rng.Float() - 0.5f) * 0.6f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                float n = Noise.Fbm2(x / 9f, y / 9f, s + 3, 5);
                float spine = 1 - Math.Min(1, Math.Abs(x - (spineX + (y - H / 2f) * tilt)) / (W * 0.28f));
                float e = n * 0.65f + spine * spine * 0.55f - 0.12f;
                float edge = Math.Min(Math.Min(x, W - 1 - x), Math.Min(y, H - 1 - y)) / 4f;
                if (edge < 1) e = e * (0.6f + 0.4f * edge);
                Height[i] = e;
                Moist[i] = Noise.Fbm2(x / 7f, y / 7f, s + 9, 4);
            }
        for (int i = 0; i < W * H; i++)
        {
            float e = Height[i], m = Moist[i];
            Tiles[i] = e < 0.18f ? Terrain.Lake : e > 0.78f ? Terrain.Peaks : e > 0.62f ? Terrain.Mountains : e > 0.48f ? Terrain.Hills
                : m > 0.66f && e < 0.3f ? Terrain.Marsh : m > 0.5f ? Terrain.Forest : Terrain.Plains;
        }
        // A few rivers from the high ground down to water.
        for (int r = 0; r < 4; r++)
        {
            int x = _rng.Range(4, W - 5), y = _rng.Range(4, H - 5);
            if (Height[y * W + x] < 0.6f) continue;
            for (int step = 0; step < 80; step++)
            {
                int i = y * W + x;
                if (Tiles[i] == Terrain.Lake) break;
                River[i] = true;
                int bx = x, by = y;
                float best = Height[i];
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    if (Height[ny * W + nx] < best) { best = Height[ny * W + nx]; bx = nx; by = ny; }
                }
                if (bx == x && by == y) { Tiles[i] = Terrain.Lake; break; }
                x = bx; y = by;
            }
        }
        PlaceSites();
        RunHistory();
    }

    public Terrain At(int x, int y) => Tiles[y * W + x];
    public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;

    private string TownName() => _rng.Pick(A) + _rng.Pick(B);

    private void PlaceSites()
    {
        int id = 1;
        bool Free(int x, int y, int gap) => !Sites.Any(q => Math.Abs(q.X - x) < gap && Math.Abs(q.Y - y) < gap);
        void Put(SiteKind kind, Func<Terrain, bool> where, int count, Func<string> name, int gap = 5)
        {
            for (int k = 0, tries = 0; k < count && tries < 2000; tries++)
            {
                int x = _rng.Range(2, W - 3), y = _rng.Range(2, H - 3);
                if (!where(At(x, y)) || !Free(x, y, gap)) continue;
                // No two sites share a name (two Tok's Burrows read as one place).
                string nm = name();
                for (int n = 0; n < 20 && Sites.Any(q => q.Name == nm); n++) nm = name();
                Sites.Add(new Site { Id = id++, Kind = kind, X = x, Y = y, Name = nm, Size = _rng.Range(40, 400), Founded = -_rng.Range(20, HistoryYears) });
                k++;
            }
        }
        Put(SiteKind.Mountainhome, t => t is Terrain.Mountains or Terrain.Peaks, 1, () => $"{_rng.Pick(new[] { "Khaz", "Dur", "Bar", "Thra", "Kel" })}{_rng.Pick(new[] { "adrum", "gathol", "zarak", "dunn", "morak" })}");
        Sites[0].Size = 2000;
        Sites[0].Founded = -HistoryYears;
        Put(SiteKind.Hold, t => t is Terrain.Mountains or Terrain.Hills, 3, () => $"{_rng.Pick(new[] { "Iron", "Deep", "Stone", "Ember", "Grim" })}{_rng.Pick(new[] { "hold", "delve", "forge", "vault" })}");
        Put(SiteKind.Town, t => t is Terrain.Plains or Terrain.Forest, 5, TownName);
        Put(SiteKind.Warren, t => t is Terrain.Hills or Terrain.Forest or Terrain.Marsh, 4, () => _rng.Pick(GrimA) + _rng.Pick(GrimB));
        Put(SiteKind.Burrow, t => t is Terrain.Hills or Terrain.Mountains, 2, () => $"{_rng.Pick(new[] { "Skrit", "Tok", "Vix", "Nib" })}'s Burrow");
        Put(SiteKind.Grove, t => t is Terrain.Forest or Terrain.Plains, 2, () => $"{_rng.Pick(new[] { "Moss", "Bramble", "Fern", "Sorrel" })}{_rng.Pick(new[] { "bottom", "hollow", "green", "dell" })}");
        Put(SiteKind.Crag, t => t is Terrain.Peaks or Terrain.Mountains, 1, () => $"the {_rng.Pick(new[] { "Grey", "Silent", "Weeping", "Broken" })} Crag");
    }

    private Site Near(Site from, Func<Site, bool> pred) => Sites.Where(s => s != from && !s.Fallen && pred(s)).OrderBy(s => Math.Abs(s.X - from.X) + Math.Abs(s.Y - from.Y)).FirstOrDefault();

    private static readonly string[] Heroes = { "Ulfrida Stonefist", "Brannoc the Tall", "Old Mabur", "Kesk Ironjaw", "Thelda Brightaxe", "Gorm the Unlucky", "Vessa of the Wells", "Durrik Halfbeard" };
    private static readonly string[] Beasts = { "the Ashwyrm", "the Pale Stalker", "the Thing in the Mere", "Gnashjaw", "the Many-Legged Queen" };

    private void RunHistory()
    {
        foreach (Site s in Sites.OrderBy(s => s.Founded))
            History.Add(new HistoryEvent { Year = s.Founded, Site = s.Id, Text = s.Kind switch
            {
                SiteKind.Mountainhome => $"The dwarves carve {s.Name}, the Mountainhome, from the heart of the mountains.",
                SiteKind.Hold => $"Dwarves from the Mountainhome found {s.Name}.",
                SiteKind.Town => $"Lowlanders raise the town of {s.Name}.",
                SiteKind.Warren => $"Grimlings infest {s.Name}.",
                SiteKind.Burrow => $"Delvlings dig {s.Name}.",
                SiteKind.Grove => $"Rootfolk plant the gardens of {s.Name}.",
                SiteKind.Crag => $"The Stonekin wake at {s.Name}.",
                _ => $"{s.Name} is founded.",
            } });
        for (int year = -HistoryYears + 10; year < 0; year++)
        {
            double roll = _rng.Float();
            var alive = Sites.Where(s => !s.Fallen && s.Founded < year).ToList();
            if (alive.Count == 0) continue;
            if (roll < 0.05)
            {
                // A warren raids, and sometimes sacks, the nearest settlement.
                Site w = _rng.Pick(alive.Where(s => s.Kind == SiteKind.Warren).ToList() is { Count: > 0 } ws ? ws : alive);
                if (w.Kind != SiteKind.Warren) continue;
                Site t = Near(w, s => s.Kind is SiteKind.Town or SiteKind.Hold or SiteKind.Grove or SiteKind.Burrow);
                if (t == null) continue;
                if (_rng.Chance(0.25) && t.Size < 300)
                {
                    t.Fallen = true; t.FallenTo = w.Name; t.Kind = SiteKind.Ruin;
                    History.Add(new HistoryEvent { Year = year, Site = t.Id, Text = $"Grimlings of {w.Name} sack {t.Name}. It lies in ruins." });
                }
                else History.Add(new HistoryEvent { Year = year, Site = t.Id, Text = $"Grimlings of {w.Name} raid {t.Name}{(_rng.Chance(0.5) ? " and are driven off" : ", carrying off its grain")}." });
            }
            else if (roll < 0.075)
            {
                Site s = _rng.Pick(alive);
                string hero = _rng.Pick(Heroes);
                Site w = Near(s, x => x.Kind == SiteKind.Warren);
                if (w != null && _rng.Chance(0.4) && Sites.Count(x => x.Kind == SiteKind.Warren && !x.Fallen) > 1)
                {
                    w.Fallen = true; w.FallenTo = hero; w.Kind = SiteKind.Ruin;
                    History.Add(new HistoryEvent { Year = year, Site = w.Id, Text = $"{hero} of {s.Name} leads a host against {w.Name} and burns it out." });
                }
                else History.Add(new HistoryEvent { Year = year, Site = s.Id, Text = $"{hero} is born in {s.Name}." });
            }
            else if (roll < 0.09)
            {
                Site s = _rng.Pick(alive);
                string beast = _rng.Pick(Beasts);
                History.Add(new HistoryEvent { Year = year, Site = s.Id, Text = _rng.Chance(0.5) ? $"{beast.ToUpperFirst()} ravages the lands around {s.Name}." : $"{beast.ToUpperFirst()} is slain near {s.Name}." });
            }
            else if (roll < 0.1)
            {
                Site s = _rng.Pick(alive.Where(x => x.Kind is SiteKind.Hold or SiteKind.Mountainhome).ToList() is { Count: > 0 } hs ? hs : alive);
                History.Add(new HistoryEvent { Year = year, Site = s.Id, Text = $"The artifact {_rng.Pick(new[] { "Emberwake", "Stonesong", "the Iron Crown", "Deepheart", "Frostfang" })} is lost from {s.Name}." });
            }
            else if (roll < 0.115)
            {
                Site a = _rng.Pick(alive), b = Near(a, x => x.Kind != SiteKind.Warren && x.Kind != a.Kind);
                if (a.Kind != SiteKind.Warren && b != null) History.Add(new HistoryEvent { Year = year, Site = a.Id, Text = $"{a.Name} and {b.Name} swear friendship and open a trade road." });
            }
        }
        History.Sort((x, y) => x.Year.CompareTo(y.Year));
    }

    // ---- choosing where to dig ------------------------------------------------------------

    public bool CanEmbark(int x, int y) => In(x, y) && At(x, y) is Terrain.Hills or Terrain.Mountains && !Sites.Any(s => s.X == x && s.Y == y);

    /// <summary>A sensible default: hills near mountains, not on top of a warren, not too far from friends.</summary>
    public (int x, int y) DefaultEmbark()
    {
        var best = (x: W / 2, y: H / 2);
        double bestScore = double.MaxValue;
        for (int y = 2; y < H - 2; y++)
            for (int x = 2; x < W - 2; x++)
            {
                if (!CanEmbark(x, y)) continue;
                double danger = Sites.Where(s => s.Kind == SiteKind.Warren && !s.Fallen).Select(s => 10.0 / (1 + Dist(x, y, s))).DefaultIfEmpty(0).Sum();
                double friends = Sites.Where(s => s.Kind is SiteKind.Mountainhome or SiteKind.Town or SiteKind.Hold).Select(s => Dist(x, y, s)).DefaultIfEmpty(30).Min();
                double score = danger * 4 + friends * 0.3 + (At(x, y) == Terrain.Hills ? 0 : 1);
                if (score < bestScore) { bestScore = score; best = (x, y); }
            }
        return best;
    }

    public static int Dist(int x, int y, Site s) => Math.Max(Math.Abs(s.X - x), Math.Abs(s.Y - y));

    /// <summary>What the land around a tile is like, as the local map should show it.</summary>
    public EmbarkLand Land(int x, int y)
    {
        float m = 0, f = 0, wet = 0;
        int n = 0;
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!In(x + dx, y + dy)) continue;
                Terrain t = At(x + dx, y + dy);
                m += t switch { Terrain.Peaks => 1, Terrain.Mountains => 0.8f, Terrain.Hills => 0.45f, _ => 0.1f };
                f += t == Terrain.Forest ? 1 : t == Terrain.Marsh ? 0.5f : 0;
                wet += t is Terrain.Lake or Terrain.Marsh ? 1 : River[(y + dy) * W + x + dx] ? 0.8f : 0;
                n++;
            }
        float rich = Math.Clamp(m / n * 0.8f + (float)Noise.Hash01(x, y, 7, (uint)Seed) * 0.4f, 0, 1);
        return new EmbarkLand { Mountain = Math.Clamp(m / n, 0, 1), Forest = Math.Clamp(f / n * 1.6f, 0, 1), Wet = Math.Clamp(wet / n * 2f + Moist[y * W + x] * 0.3f, 0, 1), Richness = rich };
    }

    /// <summary>The neighbours that matter to a hold at (x, y): within a dozen days' travel, nearest first.</summary>
    public List<(Site site, int days)> Neighbours(int x, int y) => Sites.Where(s => !s.Fallen && s.Kind != SiteKind.Fortress)
        .Select(s => (s, days: Dist(x, y, s) * 2 + 1)).Where(p => p.days <= 24).OrderBy(p => p.days).ToList();

    public static string KindName(SiteKind k) => k switch
    {
        SiteKind.Mountainhome => "the Mountainhome",
        SiteKind.Hold => "dwarven hold",
        SiteKind.Town => "Lowland town",
        SiteKind.Warren => "grimling warren",
        SiteKind.Burrow => "Delvling burrow",
        SiteKind.Grove => "Rootfolk gardens",
        SiteKind.Crag => "Stonekin crag",
        SiteKind.Ruin => "ruin",
        _ => "your fortress",
    };

    public static string People(SiteKind k) => k switch { SiteKind.Burrow => "delvling", SiteKind.Grove => "rootfolk", SiteKind.Crag => "stonekin", _ => null };
}
