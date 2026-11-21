using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>A thing: on the ground (Cell), carried (HeldBy), or part of a building (InBuilding).</summary>
public sealed class Item
{
    public int Id;
    public ItemKind Kind;
    public byte Mat;
    public int Cell = -1;
    public int HeldBy = -1;
    public int InBuilding = -1;
    public int ReservedBy = -1;
    /// <summary>0 ordinary .. 5 masterful.</summary>
    public int Quality;
    /// <summary>Lying in a stockpile that accepts it.</summary>
    public bool Stored;
    /// <summary>A name for corpses and named things.</summary>
    public string Label;
    /// <summary>Who made it.</summary>
    public string Maker;
    /// <summary>A dwarf's corpse: whose.</summary>
    public int Of = -1;
    /// <summary>A carcass, meat, hide or bones: which creature it came from.</summary>
    public string Source;
    /// <summary>A rune carved into it (striking, warding, delving).</summary>
    public string Rune;
    /// <summary>A named masterwork from a strange mood, and its story.</summary>
    public bool Artifact;
    public string Description;

    public static readonly string[] QualityName = { "", "well-crafted ", "finely-crafted ", "superior ", "exceptional ", "masterful " };

    [JsonIgnore] public ItemDef Def => Tables.Items[Kind];
    [JsonIgnore] public Mat M => Tables.Mats[Mat];
    [JsonIgnore] public bool Free => Cell >= 0 && HeldBy < 0 && InBuilding < 0 && ReservedBy < 0;
    [JsonIgnore] public int Value => Math.Max(1, M.Value * Def.Mult * (1 + Quality)) * (Artifact ? 20 : 1);

    [JsonIgnore] public string Name
    {
        get
        {
            if (Label != null) return Label;
            string m = M.Name, q = QualityName[Math.Clamp(Quality, 0, 5)];
            return Kind switch
            {
                ItemKind.Plant => m,
                ItemKind.Seed => $"{m} seeds",
                ItemKind.Drink => $"{m} ale",
                ItemKind.Meal => $"{q}{m} stew",
                ItemKind.Boulder => M.Key == "coal" ? "coal" : $"{m} boulder",
                ItemKind.RoughGem => $"rough {m}",
                ItemKind.CutGem => $"{q}cut {m}",
                ItemKind.Corpse => "corpse",
                ItemKind.Carcass => $"{Source ?? "animal"} carcass",
                ItemKind.Meat => $"{Source ?? "animal"} meat",
                ItemKind.Hide => $"{Source ?? "animal"} hide",
                ItemKind.Bone => $"{Source ?? "animal"} bones",
                ItemKind.Egg => "egg",
                ItemKind.Leather => "leather",
                _ => $"{q}{m} {Def.Name}{(Rune != null ? $" (rune of {Rune})" : "")}",
            };
        }
    }
}

public sealed class Thought
{
    public string Id;
    public long Until;
    public int Value;
}

public enum TaskKind : byte { Idle, Wander, Dig, Chop, Gather, Haul, Build, Craft, Eat, Drink, Sleep, Plant, Harvest, Tantrum, Socialize, Arrive, Store, Fight, Flee, Duty, Equip, Shelter, Bury, Tame, Slaughter, Train, Mood, Rampage, Pray, Feast }

/// <summary>What a dwarf is doing, as plain data: kind, targets and how far along it is.</summary>
public sealed class Task
{
    public TaskKind Kind;
    public int Cell = -1, Item = -1, Building = -1, Order = -1, Dest = -1;
    public int Stage, Work, Total;
    /// <summary>Items reserved for this task that still have to be brought.</summary>
    public List<int> Fetch = new();
    public string Note;
}

public sealed class Dwarf
{
    public int Id;
    public string Name, Clan;
    /// <summary>Which people: dwarf (or null, in older saves), delvling, rootfolk, stonekin.</summary>
    public string Race;
    public int Cell;
    public bool Dead;
    /// <summary>When a sky-lover last stood under the open sky.</summary>
    public long LastSky;
    /// <summary>What a Delvling has squirrelled away.</summary>
    public int Hoard;
    public string DeathCause;
    public int Hunger, Thirst, Fatigue;
    public int Hp = Tables.MaxHp;
    public int Age;
    public bool Female;
    /// <summary>Experience per labor (index by <see cref="Labor"/>).</summary>
    public int[] Xp = new int[Tables.AllLabors.Length];
    public bool[] Labors = Enumerable.Repeat(true, Tables.AllLabors.Length).ToArray();
    public List<string> Traits = new();
    public string LikesMat;
    public List<Thought> Thoughts = new();
    public int Stress;
    public Task Task;
    public List<int> Path;
    public int PathAt;
    public int MoveWait;
    /// <summary>Item carried in hand (hauling), and tools carried for work.</summary>
    public int Carry = -1, Pick = -1, Axe = -1;
    public int Bed = -1;
    /// <summary>When they last came to blows (a dwarf brawls at most once a season).</summary>
    public long Brawled = -1000000;
    public long NextThink;
    /// <summary>When a dwarf last found no water to walk to, they stop looking for a while.</summary>
    public long NoWaterUntil;
    /// <summary>An urgent need interrupts work at most this often, so an unmeetable one cannot stall everything.</summary>
    public long NextInterrupt;
    /// <summary>Ticks of a mood break left, and which.</summary>
    public int Breaking;
    public string Activity = "Idle";
    /// <summary>Opinion of other dwarves by id, -100 .. 100.</summary>
    public Dictionary<int, int> Opinion = new();
    public long Arrived;
    /// <summary>Military: the squad (or -1), what is worn and wielded, and the tally.</summary>
    public int Squad = -1, Weapon = -1, Armor = -1, Helm = -1, Shield = -1, Kills;
    public int AttackWait;
    public long LastHurt = -100000;
    /// <summary>The dead: when, whether laid to rest, whether their spirit walks.</summary>
    public long DiedAt;
    public bool Buried, Restless;
    /// <summary>A spirit quieted by the temple's prayers: it haunts no more, though the body still wants burying.</summary>
    public bool Calmed;
    /// <summary>A strange mood: its kind, the craft and workshop, when it began, what it wants and has. And madness, if it failed.</summary>
    public string Strange, Insane;
    public bool HadMood;
    public Labor MoodLabor;
    public int MoodShop = -1;
    public long MoodStart;
    public List<string> MoodNeeds = new();
    public List<int> MoodGot = new();
    public List<string> MoodAsked = new();
    /// <summary>Time since they last prayed.</summary>
    public int Faith;
    [JsonIgnore] public int MaxHp => (int)(Tables.MaxHp * Races.Of(Race).HpMult);
    [JsonIgnore] public bool Wounded => Hp < MaxHp / 2;

    public static int Level(int xp) => Math.Min(15, (int)Math.Sqrt(xp / 40.0));
    public int Skill(Labor l) => Level(Xp[(int)l]);
    public bool Can(Labor l) => Labors[(int)l];
    public bool Has(string trait) => Traits.Contains(trait);
    /// <summary>Work speed for a labor: one plus 12% per level, and the temperament.</summary>
    public float Speed(Labor l) => (1 + 0.12f * Skill(l)) * (Has("hardworking") ? 1.2f : 1) * (Has("lazy") ? 0.8f : 1) * Races.Of(Race).Work_(l);
    [JsonIgnore] public int Mood => Thoughts.Sum(t => t.Value);
    [JsonIgnore] public string FirstName => Name.Split(' ')[0];

    [JsonIgnore] public string MoodWord => Mood switch
    {
        >= 25 => "Ecstatic",
        >= 10 => "Happy",
        >= 0 => "Content",
        >= -15 => "Unhappy",
        >= -35 => "Miserable",
        _ => "Despairing",
    };
}

public sealed class Order
{
    public int Id;
    public string Recipe;
    public int Left;
    public bool Repeat;
    public int Worker = -1;
    public string Stuck;
}

public sealed class Building
{
    public int Id;
    public string Def;
    public int X, Y, Z;
    public bool Built;
    public int Progress;
    public int Builder = -1;
    /// <summary>Items delivered: building materials while it is being built, inputs while a workshop works.</summary>
    public List<int> Parts = new();
    /// <summary>A workshop's tier, I to III: faster and finer, and the higher ones make more.</summary>
    public int Tier = 1;
    public byte Mat;
    public int Quality;
    /// <summary>A bed's sleeper.</summary>
    public int Owner = -1;
    /// <summary>A door kept shut to everyone; a trap's reload time.</summary>
    public bool Locked;
    public long ArmedAt;
    /// <summary>A drawbridge: up or down, and whether it spans open air.</summary>
    public bool Raised, OverAir;
    /// <summary>Taken for a job that is not building it (a burial).</summary>
    public int Claim = -1;
    /// <summary>A workshop seized by a dwarf in a strange mood; furniture that is an artifact.</summary>
    public int MoodClaim = -1;
    public bool Artifact;
    public string Label;
    /// <summary>A shrine's god.</summary>
    public string Deity;
    public List<Order> Orders = new();
    [JsonIgnore] public BuildDef D => Tables.Build[Def];
    [JsonIgnore] public int W => D.W;
    [JsonIgnore] public int H => D.H;
    public bool Covers(int x, int y, int z) => z == Z && x >= X && x < X + W && y >= Y && y < Y + H;
    /// <summary>Where the work happens: the middle of a workshop, the cell of furniture.</summary>
    [JsonIgnore] public P3 Center => new(X + W / 2, Y + H / 2, Z);
}

public enum ZoneKind : byte { Stockpile, Farm, Meeting, Pasture }

/// <summary>Anything alive that is not a dwarf: beasts of the caves and the surface, raiders.</summary>
public sealed class Creature
{
    public int Id;
    public string Def;
    public int Cell;
    public int Hp;
    public bool Dead;
    public List<int> Path;
    public int PathAt, MoveWait;
    public int Target = -1;
    public int AttackWait;
    public bool Raider;
    /// <summary>When raiders give up and leave.</summary>
    public long Leave;
    public long NextThink;
    /// <summary>Ticks spent battering a door.</summary>
    public int Bash;
    /// <summary>A great beast of the legends; where it lairs; when its trick is ready; steps since its last blow
    /// (Gnashjaw's charge); for the Many-Legged Queen how many she has laid, for her young whose they are.</summary>
    public bool Legend;
    public int Lair = -1, Run, Brood = -1;
    public long NextTrick;
    /// <summary>The beast has been provoked (wild animals turn on whoever came too close).</summary>
    public bool Angry;
    /// <summary>Husbandry: tame, sex, birth, hunger (grazers), trained for war; what the player has asked for.</summary>
    public bool Tame, Female, War;
    public long Born;
    public int Hunger;
    public bool MarkSlaughter, MarkTame, MarkTrain;
    /// <summary>A dwarf at work on this animal (taming, training, slaughter).</summary>
    public int Claim = -1;
    /// <summary>The beast it is fighting (war beasts, and raiders after livestock).</summary>
    public int Foe = -1;
    public string Name;
    /// <summary>A merchant: which caravan.</summary>
    public int Caravan = -1;
    public bool Adult(long tick) => tick - Born >= Tables.TicksPerYear;
    [JsonIgnore] public string Title => Name != null ? $"{Name} the {Tables.Creatures[Def].Name}" : Tables.Creatures[Def].Name;
    [JsonIgnore] public CreatureDef D => Tables.Creatures[Def];
}

public enum SquadOrder : byte { OffDuty, Station, Defend, Charge }

public sealed class Squad
{
    public int Id;
    public string Name;
    public List<int> Members = new();
    public SquadOrder Order;
    public P3 Station;
}

public sealed class Zone
{
    public int Id;
    public ZoneKind Kind;
    public int X0, Y0, X1, Y1, Z;
    public StockCat Cats = StockCat.All;
    /// <summary>A farm's crop (material key).</summary>
    public string Crop;
    public string Name;
    public bool Contains(int x, int y, int z) => z == Z && x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
    [JsonIgnore] public int Area => (X1 - X0 + 1) * (Y1 - Y0 + 1);
    public IEnumerable<P3> Cells()
    {
        for (int y = Y0; y <= Y1; y++) for (int x = X0; x <= X1; x++) yield return new P3(x, y, Z);
    }
}

/// <summary>One farm cell's crop.</summary>
public sealed class Crop
{
    public string Mat;
    public long Planted;
    public int Claim = -1;
}

public enum LogKind : byte { Info, Good, Bad, Alert }

public sealed class LogEntry
{
    public long Tick;
    public string Text;
    public LogKind Kind;
    public int Cell = -1;
    /// <summary>How many times the same line was said that day (shown as "x3").</summary>
    public int Repeat = 1;
    /// <summary>For a line that gathers names as the day goes on (beds claimed).</summary>
    public List<string> Names;
}
