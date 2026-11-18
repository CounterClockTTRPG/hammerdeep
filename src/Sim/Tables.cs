using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

public enum MatKind : byte { Soil, Rock, Ore, Gem, Wood, Metal, Plant, Flesh, Cloth }

/// <summary>What a cell or an item is made of.</summary>
public sealed class Mat
{
    public byte Id;
    public string Key, Name;
    public MatKind Kind;
    public Rgb Color;
    /// <summary>Digging effort: soil 0.5, ordinary rock 1, hard rock 1.5.</summary>
    public float Hard = 1;
    /// <summary>Base value per item, before the item kind's multiplier.</summary>
    public int Value = 1;
    /// <summary>For an ore, the metal its bars are.</summary>
    public string Yields;
    /// <summary>Burns in a smelter or forge (coal).</summary>
    public bool Fuel;
    /// <summary>For a plant: eaten raw, brewed into drink, grows underground, grows on the surface.</summary>
    public bool Edible, Brewable, Underground, Surface;
    /// <summary>For a plant crop: ticks from planting to harvest.</summary>
    public int Grow;
}

public enum ItemKind : byte
{
    Boulder, Log, Block, Bar, RoughGem, CutGem, Seed, Plant, Meal, Drink, Charcoal,
    Pick, Axe, Weapon, Armor, Helm, Shield,
    Bed, Table, Chair, Door, Chest, Barrel, Statue, Craft, Corpse,
}

public sealed class ItemDef
{
    public ItemKind Kind;
    public string Name;
    /// <summary>Value multiplier on the material.</summary>
    public int Mult = 1;
    /// <summary>Small items share a stockpile cell (up to four).</summary>
    public bool Small;
    /// <summary>Stockpile category.</summary>
    public StockCat Cat;
    /// <summary>One glyph for the map.</summary>
    public char Glyph = '*';
    /// <summary>Placed as a building of this id.</summary>
    public string Places;
}

[Flags]
public enum StockCat : ushort
{
    None = 0, Stone = 1, Wood = 2, Food = 4, Drink = 8, Bars = 16, Gems = 32, Furniture = 64, Weapons = 128, Armor = 256, Finished = 512, Seeds = 1024, Refuse = 2048,
    All = 0xFFF,
}

public enum Labor : byte { Mining, Woodcutting, Hauling, Construction, Carpentry, Masonry, Smelting, Smithing, Crafting, Cooking, Brewing, Farming, Gathering, Fighting }

public enum BuildKind : byte { Workshop, Furniture, Construction }

/// <summary>A material requirement: <see cref="Tables.Matches"/> reads the tag.</summary>
public readonly record struct Need(string Tag, int Count = 1);

public sealed class BuildDef
{
    public string Id, Name;
    public BuildKind Kind;
    public int W = 1, H = 1;
    public Need[] Cost = Array.Empty<Need>();
    public Labor Labor = Labor.Construction;
    public int Work = 200;
    public char Glyph = '#';
    public string Key;
    /// <summary>For a construction: the shape the cell becomes.</summary>
    public Shape Makes;
    public string Help;
}

public readonly record struct Output(ItemKind Kind, int Count = 1, string Mat = null);

public sealed class Recipe
{
    public string Id, Name, Shop;
    public Labor Labor;
    public Need[] Inputs;
    public Output[] Outputs;
    public int Work = 300;
}

public sealed class ThoughtDef
{
    public string Id, Text;
    public int Value, Ticks;
}

public sealed class TraitDef
{
    public string Id, Name, Text;
}

public sealed class ClanDef
{
    public string Id, Name, Motto;
    public Rgb Color;
    public Labor[] Leans;
    /// <summary>The clan they hold a grudge against.</summary>
    public string Grudge;
}

/// <summary>Every number and name the rules use, in one place.</summary>
public static class Tables
{
    // ---- time --------------------------------------------------------------------------
    public const int TicksPerDay = 1000, DaysPerSeason = 12, SeasonsPerYear = 4;
    public const int TicksPerSeason = TicksPerDay * DaysPerSeason, TicksPerYear = TicksPerSeason * SeasonsPerYear;
    public static readonly string[] Seasons = { "Thaw", "Highsun", "Leaffall", "Deepfrost" };

    // ---- bodies ------------------------------------------------------------------------
    public const int StepTicks = 5, DiagTicks = 7, StairTicks = 8;
    public const int HungryAt = 2600, StarvingAt = 7000, StarveDeath = 14000;
    public const int ThirstyAt = 1500, ParchedAt = 4000, ThirstDeath = 9000;
    public const int TiredAt = 2000, ExhaustedAt = 3600;
    public const int SleepRecover = 6;
    public const int MaxHp = 100;

    // ---- materials ---------------------------------------------------------------------
    public static readonly List<Mat> Mats = new();
    private static readonly Dictionary<string, Mat> _mat = new();
    public static Mat M(string key) => _mat[key];
    public static byte Mi(string key) => _mat[key].Id;

    private static void AddMat(string key, string name, MatKind kind, uint color, float hard = 1, int value = 1, Action<Mat> more = null)
    {
        var m = new Mat { Id = (byte)Mats.Count, Key = key, Name = name, Kind = kind, Color = Rgb.Hex(color), Hard = hard, Value = value };
        more?.Invoke(m);
        Mats.Add(m);
        _mat[key] = m;
    }

    // ---- items -------------------------------------------------------------------------
    public static readonly Dictionary<ItemKind, ItemDef> Items = new();
    public static ItemDef Item(ItemKind k) => Items[k];

    // ---- buildings and recipes ---------------------------------------------------------
    public static readonly List<BuildDef> Buildings = new();
    public static readonly Dictionary<string, BuildDef> Build = new();
    public static readonly List<Recipe> Recipes = new();
    public static IEnumerable<Recipe> RecipesFor(string shop) => Recipes.Where(r => r.Shop == shop);
    public static Recipe Recipe(string id) => Recipes.First(r => r.Id == id);

    public static readonly Dictionary<string, ThoughtDef> Thoughts = new();
    public static readonly List<TraitDef> Traits = new();
    public static readonly List<ClanDef> Clans = new();

    public static readonly string[] NameA = { "Dur", "Bal", "Thra", "Kil", "Mor", "Gim", "Bor", "Har", "Dun", "Fal", "Grim", "Thor", "Nal", "Ov", "Brun", "Eld", "Tor", "Kaz", "Rud", "Sig", "Ulf", "Ast", "Dag", "Hel", "Ing", "Ket", "Lod", "Mab", "Os", "Vod" };
    public static readonly string[] NameB = { "in", "ak", "ur", "ek", "im", "or", "an", "ola", "rik", "unn", "ga", "dis", "ra", "da", "ul", "eth", "va", "om", "ri", "a" };

    public static readonly string[] FortA = { "Bolk", "Thurn", "Gal", "Dor", "Kel", "Mor", "Az", "Ur", "Bar", "Vel", "Grun", "Hald", "Ost", "Brand", "Cairn", "Ember", "Iron", "Glum" };
    public static readonly string[] FortB = { "deep", "hold", "delve", "gard", "heim", "rock", "forge", "hall", "mount", "barrow", "vault", "spire" };
    public static string FortName(Rng r) => r.Pick(FortA) + r.Pick(FortB);

    public static readonly Labor[] AllLabors = (Labor[])Enum.GetValues(typeof(Labor));

    public static string LaborName(Labor l) => l switch
    {
        Labor.Woodcutting => "Woodcutting",
        Labor.Construction => "Construction",
        _ => l.ToString(),
    };

    static Tables()
    {
        // Soils
        AddMat("loam", "loam", MatKind.Soil, 0x6b4f33, 0.45f);
        AddMat("clay", "clay", MatKind.Soil, 0x8a5a3c, 0.5f);
        AddMat("sand", "sand", MatKind.Soil, 0xb39a68, 0.4f);
        // Rock, by depth band
        AddMat("sandstone", "sandstone", MatKind.Rock, 0xa38358, 0.9f, 1);
        AddMat("limestone", "limestone", MatKind.Rock, 0x9c9784, 0.9f, 1);
        AddMat("granite", "granite", MatKind.Rock, 0x77706b, 1.3f, 2);
        AddMat("slate", "slate", MatKind.Rock, 0x4f5559, 1.1f, 2);
        AddMat("marble", "marble", MatKind.Rock, 0xc9c5bd, 1.2f, 3);
        AddMat("basalt", "basalt", MatKind.Rock, 0x3d3a3a, 1.5f, 2);
        AddMat("obsidian", "obsidian", MatKind.Rock, 0x241f2b, 1.7f, 4);
        // Ores and coal
        AddMat("malachite", "malachite", MatKind.Ore, 0x3f8f63, 1.1f, 3, m => m.Yields = "copper");
        AddMat("cassiterite", "cassiterite", MatKind.Ore, 0x5b4636, 1.2f, 3, m => m.Yields = "tin");
        AddMat("hematite", "hematite", MatKind.Ore, 0x8f3b2e, 1.3f, 4, m => m.Yields = "iron");
        AddMat("argentite", "argentite", MatKind.Ore, 0x8e9aa6, 1.2f, 6, m => m.Yields = "silver");
        AddMat("goldvein", "gold-veined quartz", MatKind.Ore, 0xd6b13c, 1.2f, 10, m => m.Yields = "gold");
        AddMat("coal", "coal", MatKind.Ore, 0x1e1c1c, 0.9f, 2, m => m.Fuel = true);
        // Gems
        AddMat("quartz", "rock crystal", MatKind.Gem, 0xdfe8ea, 1.2f, 6);
        AddMat("amethyst", "amethyst", MatKind.Gem, 0x8a54b8, 1.2f, 12);
        AddMat("garnet", "garnet", MatKind.Gem, 0x9c1f32, 1.2f, 15);
        AddMat("emerald", "emerald", MatKind.Gem, 0x22a35a, 1.2f, 30);
        AddMat("ruby", "deepfire ruby", MatKind.Gem, 0xd2202f, 1.2f, 45);
        // Wood
        AddMat("pine", "pine", MatKind.Wood, 0x8d6a3f, 1, 1);
        AddMat("oak", "oak", MatKind.Wood, 0x7a5530, 1, 2);
        AddMat("stalkwood", "stalkwood", MatKind.Wood, 0x6d5b7a, 1, 2);
        // Metals
        AddMat("copper", "copper", MatKind.Metal, 0xc6763c, 1, 4);
        AddMat("tin", "tin", MatKind.Metal, 0xb7b9b4, 1, 3);
        AddMat("bronze", "bronze", MatKind.Metal, 0xb08a3a, 1, 6);
        AddMat("iron", "iron", MatKind.Metal, 0x7d8085, 1, 6);
        AddMat("silver", "silver", MatKind.Metal, 0xd9dde2, 1, 10);
        AddMat("gold", "gold", MatKind.Metal, 0xf0c64b, 1, 20);
        // Plants
        AddMat("dimcap", "dimcap", MatKind.Plant, 0x9b6fb0, 1, 2, m => { m.Edible = true; m.Brewable = true; m.Underground = true; m.Grow = 5000; });
        AddMat("hearthwheat", "hearthwheat", MatKind.Plant, 0xd8b85a, 1, 2, m => { m.Brewable = true; m.Surface = true; m.Grow = 6000; });
        AddMat("bramble", "bramblefruit", MatKind.Plant, 0xa3304a, 1, 2, m => { m.Edible = true; m.Brewable = true; m.Surface = true; m.Grow = 5500; });
        AddMat("glowmoss", "glowmoss", MatKind.Plant, 0x57c4a0, 1, 1, m => { m.Edible = true; m.Underground = true; m.Grow = 4000; });
        // Flesh, provisions
        AddMat("flesh", "dwarf", MatKind.Flesh, 0xc08f70, 1, 0);
        AddMat("ration", "travel", MatKind.Plant, 0xb08d5c, 1, 1, m => m.Edible = true);

        void I(ItemKind k, string name, int mult, StockCat cat, char glyph, bool small = false, string places = null) =>
            Items[k] = new ItemDef { Kind = k, Name = name, Mult = mult, Cat = cat, Glyph = glyph, Small = small, Places = places };
        I(ItemKind.Boulder, "boulder", 1, StockCat.Stone, 'o');
        I(ItemKind.Log, "log", 1, StockCat.Wood, '=');
        I(ItemKind.Block, "blocks", 2, StockCat.Stone, '#');
        I(ItemKind.Bar, "bar", 5, StockCat.Bars, '-', true);
        I(ItemKind.RoughGem, "rough gem", 2, StockCat.Gems, '*', true);
        I(ItemKind.CutGem, "cut gem", 8, StockCat.Gems, '*', true);
        I(ItemKind.Seed, "seeds", 1, StockCat.Seeds, ',', true);
        I(ItemKind.Plant, "", 1, StockCat.Food, '"', true);
        I(ItemKind.Meal, "stew", 4, StockCat.Food, '%', true);
        I(ItemKind.Drink, "ale", 3, StockCat.Drink, '!', true);
        I(ItemKind.Charcoal, "charcoal", 1, StockCat.Bars, '.', true);
        I(ItemKind.Pick, "pick", 6, StockCat.Weapons, '(');
        I(ItemKind.Axe, "axe", 6, StockCat.Weapons, '(');
        I(ItemKind.Weapon, "war hammer", 10, StockCat.Weapons, '/');
        I(ItemKind.Armor, "breastplate", 12, StockCat.Armor, '[');
        I(ItemKind.Helm, "helm", 6, StockCat.Armor, '^');
        I(ItemKind.Shield, "shield", 6, StockCat.Armor, ')');
        I(ItemKind.Bed, "bed", 4, StockCat.Furniture, 'b', places: "bed");
        I(ItemKind.Table, "table", 4, StockCat.Furniture, 't', places: "table");
        I(ItemKind.Chair, "chair", 3, StockCat.Furniture, 'h', places: "chair");
        I(ItemKind.Door, "door", 3, StockCat.Furniture, '+', places: "door");
        I(ItemKind.Chest, "chest", 3, StockCat.Furniture, 'c', places: "chest");
        I(ItemKind.Barrel, "barrel", 2, StockCat.Furniture, 'u');
        I(ItemKind.Statue, "statue", 20, StockCat.Furniture, '&', places: "statue");
        I(ItemKind.Craft, "figurine", 6, StockCat.Finished, '\'', true);
        I(ItemKind.Corpse, "corpse", 0, StockCat.Refuse, 'x');

        void B(string id, string name, BuildKind kind, int w, int h, Labor labor, int work, char glyph, string key, string help, params Need[] cost) =>
            Add(new BuildDef { Id = id, Name = name, Kind = kind, W = w, H = h, Labor = labor, Work = work, Glyph = glyph, Key = key, Help = help, Cost = cost });
        B("carpenter", "Carpenter's shop", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'C', "C", "Logs into beds, tables, chairs, doors, barrels.", new Need("log"));
        B("mason", "Mason's shop", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'M', "M", "Stone into blocks, doors, tables, chairs, statues.", new Need("stone"));
        B("craftsdwarf", "Craftsdwarf's shop", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'R', "R", "Stone into figurines, gems cut, crafts for trade.", new Need("stone"));
        B("furnace", "Wood furnace", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'W', "W", "Burns logs into charcoal.", new Need("stone"));
        B("smelter", "Smelter", BuildKind.Workshop, 3, 3, Labor.Construction, 400, 'S', "S", "Ore and fuel into metal bars.", new Need("stone"), new Need("stone"));
        B("forge", "Metalsmith's forge", BuildKind.Workshop, 3, 3, Labor.Construction, 400, 'F', "F", "Bars and fuel into picks, axes, weapons and armor.", new Need("stone"), new Need("stone"));
        B("kitchen", "Kitchen", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'K', "K", "Two foods into stew, which dwarves prefer.", new Need("stone"));
        B("brewery", "Brewery", BuildKind.Workshop, 3, 3, Labor.Construction, 300, 'B', "B", "One brewable plant into five ale.", new Need("stone"));
        B("bed", "Bed", BuildKind.Furniture, 1, 1, Labor.Construction, 60, 'b', "b", "A dwarf's own place to sleep.", new Need("bed"));
        B("table", "Table", BuildKind.Furniture, 1, 1, Labor.Construction, 60, 't', "t", "Eating at a table is civilised.", new Need("table"));
        B("chair", "Chair", BuildKind.Furniture, 1, 1, Labor.Construction, 60, 'h', "h", "Put one beside a table.", new Need("chair"));
        B("door", "Door", BuildKind.Furniture, 1, 1, Labor.Construction, 60, '+', "d", "Keeps things out (soon).", new Need("door"));
        B("chest", "Chest", BuildKind.Furniture, 1, 1, Labor.Construction, 60, 'c', "c", "Somewhere to keep things.", new Need("chest"));
        B("statue", "Statue", BuildKind.Furniture, 1, 1, Labor.Construction, 80, '&', "s", "Dwarves admire fine statues.", new Need("statue"));
        Add(new BuildDef { Id = "wall", Name = "Wall", Kind = BuildKind.Construction, Labor = Labor.Construction, Work = 120, Glyph = '#', Key = "w", Makes = Shape.Wall, Cost = new[] { new Need("buildmat") }, Help = "A built wall." });
        Add(new BuildDef { Id = "floor", Name = "Floor", Kind = BuildKind.Construction, Labor = Labor.Construction, Work = 80, Glyph = '+', Key = "f", Makes = Shape.Floor, Cost = new[] { new Need("buildmat") }, Help = "A floor over open air." });
        Add(new BuildDef { Id = "stairs", Name = "Up/down stairs", Kind = BuildKind.Construction, Labor = Labor.Construction, Work = 120, Glyph = 'X', Key = "x", Makes = Shape.StairUD, Cost = new[] { new Need("buildmat") }, Help = "Built stairs (match them above and below)." });

        void R(string id, string name, string shop, Labor labor, int work, Need[] inputs, params Output[] outputs) =>
            Recipes.Add(new Recipe { Id = id, Name = name, Shop = shop, Labor = labor, Work = work, Inputs = inputs, Outputs = outputs });
        Need[] Log = { new("log") }, Stone = { new("stone") };
        R("bed", "Bed", "carpenter", Labor.Carpentry, 300, Log, new Output(ItemKind.Bed));
        R("wtable", "Wooden table", "carpenter", Labor.Carpentry, 250, Log, new Output(ItemKind.Table));
        R("wchair", "Wooden chair", "carpenter", Labor.Carpentry, 250, Log, new Output(ItemKind.Chair));
        R("wdoor", "Wooden door", "carpenter", Labor.Carpentry, 250, Log, new Output(ItemKind.Door));
        R("barrel", "Barrel", "carpenter", Labor.Carpentry, 200, Log, new Output(ItemKind.Barrel));
        R("wchest", "Wooden chest", "carpenter", Labor.Carpentry, 250, Log, new Output(ItemKind.Chest));
        R("blocks", "Stone blocks", "mason", Labor.Masonry, 200, Stone, new Output(ItemKind.Block, 2));
        R("stable", "Stone table", "mason", Labor.Masonry, 300, Stone, new Output(ItemKind.Table));
        R("schair", "Stone chair", "mason", Labor.Masonry, 300, Stone, new Output(ItemKind.Chair));
        R("sdoor", "Stone door", "mason", Labor.Masonry, 300, Stone, new Output(ItemKind.Door));
        R("statue", "Statue", "mason", Labor.Masonry, 900, new[] { new Need("stone"), new Need("stone") }, new Output(ItemKind.Statue));
        R("figurine", "Stone figurine", "craftsdwarf", Labor.Crafting, 250, Stone, new Output(ItemKind.Craft, 2));
        R("cutgem", "Cut a gem", "craftsdwarf", Labor.Crafting, 400, new[] { new Need("roughgem") }, new Output(ItemKind.CutGem));
        R("charcoal", "Charcoal", "furnace", Labor.Smelting, 250, Log, new Output(ItemKind.Charcoal, 2));
        R("smelt", "Smelt ore", "smelter", Labor.Smelting, 350, new[] { new Need("ore"), new Need("fuel") }, new Output(ItemKind.Bar, 2));
        R("bronze", "Bronze alloy", "smelter", Labor.Smelting, 400, new[] { new Need("bar:copper"), new Need("bar:tin"), new Need("fuel") }, new Output(ItemKind.Bar, 2, "bronze"));
        R("pick", "Pick", "forge", Labor.Smithing, 400, new[] { new Need("toolbar"), new Need("fuel") }, new Output(ItemKind.Pick));
        R("axe", "Axe", "forge", Labor.Smithing, 400, new[] { new Need("toolbar"), new Need("fuel") }, new Output(ItemKind.Axe));
        R("hammer", "War hammer", "forge", Labor.Smithing, 500, new[] { new Need("toolbar"), new Need("fuel") }, new Output(ItemKind.Weapon));
        R("armor", "Breastplate", "forge", Labor.Smithing, 700, new[] { new Need("toolbar"), new Need("toolbar"), new Need("fuel") }, new Output(ItemKind.Armor));
        R("helm", "Helm", "forge", Labor.Smithing, 400, new[] { new Need("toolbar"), new Need("fuel") }, new Output(ItemKind.Helm));
        R("stew", "Cook stew", "kitchen", Labor.Cooking, 200, new[] { new Need("cookable"), new Need("cookable") }, new Output(ItemKind.Meal, 3));
        R("ale", "Brew ale", "brewery", Labor.Brewing, 300, new[] { new Need("brewable") }, new Output(ItemKind.Drink, 5), new Output(ItemKind.Seed, 1));

        void T(string id, string text, int value, int days) => Thoughts[id] = new ThoughtDef { Id = id, Text = text, Value = value, Ticks = days * TicksPerDay };
        T("ate_meal", "enjoyed a hearty stew", 6, 2);
        T("ate_raw", "ate something raw and plain", -1, 1);
        T("ate_floor", "ate without a table", -2, 1);
        T("ate_table", "dined at a proper table", 3, 1);
        T("drank_ale", "drank good ale", 5, 1);
        T("drank_water", "had to drink water", -5, 2);
        T("no_ale", "has gone without ale", -8, 1);
        T("slept_floor", "slept on the bare floor", -4, 1);
        T("slept_bed", "slept in a bed", 3, 1);
        T("slept_own", "slept in their own bed", 6, 1);
        T("hungry", "is hungry", -6, 1);
        T("thirsty", "is thirsty", -6, 1);
        T("saw_death", "saw a dwarf die", -30, 8);
        T("friend_died", "lost a friend", -45, 16);
        T("admired", "admired a fine piece of work", 4, 2);
        T("good_work", "is proud of their work", 4, 2);
        T("chatted", "had a good chat", 3, 1);
        T("feud", "was near a dwarf of a feuding clan", -4, 1);
        T("struck_gems", "struck gems", 8, 3);
        T("new_arrivals", "welcomed new arrivals", 4, 3);
        T("idle", "has nothing to do", -2, 1);
        T("tantrum_after", "threw a tantrum and feels foolish", -3, 3);
        T("caravan", "saw a caravan arrive", 6, 3);
        T("rain", "was caught in the rain", -2, 1);
        T("darkness", "has not seen the sky for ages", -1, 1);

        void Tr(string id, string name, string text) => Traits.Add(new TraitDef { Id = id, Name = name, Text = text });
        Tr("cheerful", "Cheerful", "Good moods last longer; bad ones fade fast.");
        Tr("gloomy", "Gloomy", "Takes every slight to heart.");
        Tr("hardworking", "Hard-working", "Works faster and complains less about it.");
        Tr("lazy", "Lazy", "Works slowly and naps a lot.");
        Tr("alelover", "Ale-lover", "Without ale, life is not worth living.");
        Tr("glutton", "Glutton", "Gets hungry sooner and loves a stew.");
        Tr("brave", "Brave", "Shrugs off the sight of death.");
        Tr("greedy", "Greedy", "Loves gems and gold; hates parting with them.");
        Tr("sociable", "Sociable", "Talks to everyone; misses company.");
        Tr("loner", "Loner", "Would rather be left alone.");
        Tr("grudgeful", "Grudge-keeper", "Never forgets a clan feud.");
        Tr("artistic", "Artistic", "Crafts with flair; finer results.");

        void C(string id, string name, uint color, string motto, string grudge, params Labor[] leans) =>
            Clans.Add(new ClanDef { Id = id, Name = name, Color = Rgb.Hex(color), Motto = motto, Grudge = grudge, Leans = leans });
        C("ironhand", "Ironhand", 0xb8513a, "Strike while it glows.", "mossbeard", Labor.Mining, Labor.Smithing, Labor.Smelting);
        C("mossbeard", "Mossbeard", 0x5d8a3a, "Every cave can be a garden.", "ironhand", Labor.Farming, Labor.Brewing, Labor.Cooking, Labor.Gathering);
        C("stoneward", "Stoneward", 0x6f7f96, "The wall does not fall.", "gemhollow", Labor.Masonry, Labor.Construction, Labor.Fighting);
        C("gemhollow", "Gemhollow", 0x9a5fb0, "What glitters is ours.", "stoneward", Labor.Crafting, Labor.Carpentry, Labor.Woodcutting);
    }

    private static void Add(BuildDef b) { Buildings.Add(b); Build[b.Id] = b; }

    public static ClanDef Clan(string id) => Clans.First(c => c.Id == id);

    /// <summary>Does <paramref name="it"/> satisfy a requirement tag?</summary>
    public static bool Matches(string tag, Item it)
    {
        Mat m = Mats[it.Mat];
        switch (tag)
        {
            case "log": return it.Kind == ItemKind.Log;
            case "stone": return it.Kind == ItemKind.Boulder && m.Kind == MatKind.Rock;
            case "buildmat": return it.Kind == ItemKind.Block || it.Kind == ItemKind.Log || (it.Kind == ItemKind.Boulder && m.Kind == MatKind.Rock);
            case "ore": return it.Kind == ItemKind.Boulder && m.Kind == MatKind.Ore && m.Yields != null;
            case "fuel": return it.Kind == ItemKind.Charcoal || (it.Kind == ItemKind.Boulder && m.Fuel);
            case "roughgem": return it.Kind == ItemKind.RoughGem;
            case "toolbar": return it.Kind == ItemKind.Bar && (m.Key is "iron" or "bronze" or "copper");
            case "cookable": return it.Kind == ItemKind.Plant && m.Edible;
            case "brewable": return it.Kind == ItemKind.Plant && m.Brewable;
            case "food": return it.Kind == ItemKind.Meal || (it.Kind == ItemKind.Plant && m.Edible);
            case "drink": return it.Kind == ItemKind.Drink;
            case "seed": return it.Kind == ItemKind.Seed;
        }
        if (tag.StartsWith("bar:")) return it.Kind == ItemKind.Bar && m.Key == tag[4..];
        if (tag.StartsWith("seed:")) return it.Kind == ItemKind.Seed && m.Key == tag[5..];
        // A furniture tag names its item kind.
        foreach (ItemDef d in Items.Values) if (d.Places == tag) return it.Kind == d.Kind;
        return false;
    }

    public static string NeedName(string tag) => tag switch
    {
        "stone" => "stone boulder",
        "buildmat" => "stone, log or blocks",
        "ore" => "metal ore",
        "fuel" => "fuel (coal or charcoal)",
        "roughgem" => "rough gem",
        "toolbar" => "copper, bronze or iron bar",
        "cookable" => "edible plant",
        "brewable" => "brewable plant",
        _ when tag.StartsWith("bar:") => tag[4..] + " bar",
        _ => tag,
    };
}
