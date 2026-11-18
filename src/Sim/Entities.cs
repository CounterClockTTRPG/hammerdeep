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

    public static readonly string[] QualityName = { "", "well-crafted ", "finely-crafted ", "superior ", "exceptional ", "masterful " };

    [JsonIgnore] public ItemDef Def => Tables.Items[Kind];
    [JsonIgnore] public Mat M => Tables.Mats[Mat];
    [JsonIgnore] public bool Free => Cell >= 0 && HeldBy < 0 && InBuilding < 0 && ReservedBy < 0;
    [JsonIgnore] public int Value => Math.Max(1, M.Value * Def.Mult * (1 + Quality));

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
                _ => $"{q}{m} {Def.Name}",
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

public enum TaskKind : byte { Idle, Wander, Dig, Chop, Gather, Haul, Build, Craft, Eat, Drink, Sleep, Plant, Harvest, Tantrum, Socialize, Arrive, Store }

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
    public int Cell;
    public bool Dead;
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

    public static int Level(int xp) => Math.Min(15, (int)Math.Sqrt(xp / 40.0));
    public int Skill(Labor l) => Level(Xp[(int)l]);
    public bool Can(Labor l) => Labors[(int)l];
    public bool Has(string trait) => Traits.Contains(trait);
    /// <summary>Work speed for a labor: one plus 12% per level, and the temperament.</summary>
    public float Speed(Labor l) => (1 + 0.12f * Skill(l)) * (Has("hardworking") ? 1.2f : 1) * (Has("lazy") ? 0.8f : 1);
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
    public byte Mat;
    public int Quality;
    /// <summary>A bed's sleeper.</summary>
    public int Owner = -1;
    public List<Order> Orders = new();
    [JsonIgnore] public BuildDef D => Tables.Build[Def];
    [JsonIgnore] public int W => D.W;
    [JsonIgnore] public int H => D.H;
    public bool Covers(int x, int y, int z) => z == Z && x >= X && x < X + W && y >= Y && y < Y + H;
    /// <summary>Where the work happens: the middle of a workshop, the cell of furniture.</summary>
    [JsonIgnore] public P3 Center => new(X + W / 2, Y + H / 2, Z);
}

public enum ZoneKind : byte { Stockpile, Farm, Meeting }

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
}
