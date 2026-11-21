using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// Strange moods and artifacts: a dwarf is seized, claims a workshop, demands
/// materials, and -- given them -- makes a named masterwork and becomes a
/// legend; denied, goes mad (melancholy, berserk, or off into the wild).
/// </summary>
public sealed partial class Fortress
{
    public static readonly string[] MoodKinds = { "fey", "secretive", "possessed", "macabre" };
    public const int MoodDeadlineDays = 8, MoodWork = 2400;

    private static readonly (Labor labor, string shop, ItemKind[] makes, string tag)[] Crafts =
    {
        (Labor.Carpentry, "carpenter", new[] { ItemKind.Bed, ItemKind.Table, ItemKind.Chair, ItemKind.Door, ItemKind.Shield }, "log"),
        (Labor.Masonry, "mason", new[] { ItemKind.Statue, ItemKind.Table, ItemKind.Door }, "stone"),
        (Labor.Crafting, "craftsdwarf", new[] { ItemKind.Craft, ItemKind.CutGem }, "stone"),
        (Labor.Smithing, "forge", new[] { ItemKind.Weapon, ItemKind.Armor, ItemKind.Helm }, "bar"),
        (Labor.Smithing, "mforge", new[] { ItemKind.Weapon, ItemKind.Armor, ItemKind.Helm }, "bar"),
        (Labor.Leatherwork, "leatherworks", new[] { ItemKind.Armor, ItemKind.Helm }, "leather"),
    };

    private static readonly string[] ArtA = { "Ember", "Iron", "Silent", "Deep", "Grim", "Golden", "Stone", "Ash", "Frost", "Thunder", "Hollow", "Bright", "Under", "Rust", "Gloom", "Star" };
    private static readonly string[] ArtB = { "wake", "heart", "song", "fall", "oath", "crown", "fang", "delve", "ward", "sorrow", "mirth", "root", "gleam", "maw", "brand", "keep" };

    /// <summary>Once a day: perhaps someone is seized by a mood.</summary>
    private void MaybeMood()
    {
        if (Tick < Tables.TicksPerSeason || Alive < 8 || Dwarves.Any(d => !d.Dead && d.Strange != null)) return;
        if (!Rng.Chance(0.01 + 0.005 * Alive / 20.0)) return;
        var candidates = Living.Where(d => !d.HadMood && d.Insane == null && d.Breaking == 0 && d.Squad < 0).ToList();
        if (candidates.Count == 0) return;
        StartMood(Rng.Pick(candidates));
    }

    public void StartMood(Dwarf d, string kind = null)
    {
        var craft = Crafts.OrderByDescending(c => d.Xp[(int)c.labor] + Rng.Int(40)).First();
        kind ??= d.Has("gloomy") ? "macabre" : d.Has("artistic") ? "fey" : Rng.Pick(MoodKinds);
        d.Strange = kind;
        d.HadMood = true;
        d.MoodLabor = craft.labor;
        d.MoodShop = -1;
        d.MoodStart = Tick;
        d.MoodGot = new List<int>();
        d.MoodAsked = new List<string>();
        // What the work demands: its stuff, and two or three things more.
        var needs = new List<string> { craft.tag };
        // Mostly what a hold has; at most one rare thing (a gem, or something that shines).
        string[] common = kind == "macabre" ? new[] { "bone", "bone", "stone" } : new[] { "bone", "log", "stone", "leather", craft.tag };
        int n = Rng.Range(1, 2);
        for (int k = 0; k < n; k++) needs.Add(Rng.Pick(common));
        if (Rng.Chance(0.6)) needs.Add(Rng.Chance(0.6) ? "gem" : "shiny");
        d.MoodNeeds = needs;
        Release(d);
        d.Task = new Task { Kind = TaskKind.Mood };
        string how = kind switch
        {
            "fey" => "is taken by a fey mood",
            "secretive" => "withdraws from society, in a secretive mood",
            "possessed" => "has been possessed",
            _ => "begins to stalk and brood, in a macabre mood",
        };
        Say($"{d.Name} {how}!", LogKind.Alert, d.Cell);
        Count("moods");
    }

    public static string MoodNeedName(string tag) => tag switch
    {
        "gem" => "gems",
        "shiny" => "something that shines",
        "bone" => "bones",
        "bar" => "metal bars",
        "log" => "wood",
        "stone" => "stone",
        "leather" => "leather",
        _ => tag,
    };

    /// <summary>Where the thing a moody dwarf demands comes from, for the player who does not know.</summary>
    public static string MoodNeedHint(string tag) => tag switch
    {
        "gem" => "Dig for them; caravans sell them too.",
        "shiny" => "Gems, or metal bars from a smelter.",
        "bone" => "Butcher a carcass at a butcher's shop.",
        "bar" => "Smelt ore at a smelter.",
        "log" => "Fell trees (T).",
        "stone" => "Dig out rock: boulders are left behind.",
        "leather" => "Tan a hide at a tanner's shop -- butchering gives hides -- or buy it from the Lowland towns.",
        _ => "",
    };

    private bool DoMood(Dwarf d, Task t)
    {
        if (d.Strange == null) return false;
        var craft = Crafts.First(c => c.labor == d.MoodLabor);
        string word = d.Strange == "macabre" ? "brooding" : d.Strange;
        // Too long denied: madness.
        if (Tick - d.MoodStart > Tables.TicksPerDay * MoodDeadlineDays) { GoMad(d); return false; }
        Building shop = d.MoodShop >= 0 ? BuildingById(d.MoodShop) : null;
        if (shop == null || !shop.Built)
        {
            shop = Buildings.Where(b => b.Built && b.Def == craft.shop && b.MoodClaim < 0).OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(World.Pos(d.Cell))).FirstOrDefault();
            if (shop == null)
            {
                d.Activity = $"Looking for a {Tables.Build[craft.shop].Name.ToLower()} ({word})";
                if (t.Stage == 0) { t.Stage = 1; Say($"{d.FirstName} needs a {Tables.Build[craft.shop].Name.ToLower()}!", LogKind.Bad, d.Cell); }
                if (d.Path == null) { int w = Walk(d, WanderSpot(d, World.Pos(d.Cell), 4)); }
                return true;
            }
            // The shop is the dwarf's now: no one else works there.
            shop.MoodClaim = d.Id;
            d.MoodShop = shop.Id;
            foreach (Order o in shop.Orders) if (o.Worker >= 0) { Dwarf w = DwarfById(o.Worker); if (w != null && w != d) { Release(w); w.Task = null; } }
            Say($"{d.FirstName} has claimed a {shop.D.Name.ToLower()}.", LogKind.Info, World.Idx(shop.Center));
        }
        int center = World.Idx(shop.Center);
        // Gather what is wanted, one thing at a time.
        if (t.Item >= 0)
        {
            int r = Fetch(d, t.Item);
            if (r < 0) { t.Item = -1; return true; }
            if (r == 0) return true;
            d.Carry = t.Item;
            int w = Walk(d, center);
            if (w < 0) return false;
            if (w == 0) return true;
            Item it = Items[t.Item];
            d.Carry = -1;
            PutIn(it, shop);
            it.ReservedBy = d.Id;
            d.MoodGot.Add(it.Id);
            t.Item = -1;
            return true;
        }
        string missing = MissingMoodNeed(d);
        if (missing != null)
        {
            Item found = NearestFree(d, x => Tables.Matches(missing, x), center);
            if (found != null) { found.ReservedBy = d.Id; t.Item = found.Id; d.Activity = $"Gathering {MoodNeedName(missing)} ({word})"; return true; }
            d.Activity = $"Demanding {MoodNeedName(missing)}!";
            if (t.Note != missing)
            {
                t.Note = missing;
                // The first demand stops the game: there are days, not weeks, to find it.
                bool first = !d.MoodAsked.Contains(missing);
                d.MoodAsked.Add(missing);
                Say($"{d.FirstName} demands {MoodNeedName(missing)}! {MoodNeedHint(missing)}".TrimEnd(), first ? LogKind.Alert : LogKind.Bad, center);
            }
            if (d.Cell != center && d.Path == null) Walk(d, center);
            return true;
        }
        // Everything is here: work, unspeaking.
        if (d.Cell != center) { int w = Walk(d, center); if (w < 0) return false; if (w == 0) return true; }
        d.Activity = $"Working on something ({word})";
        t.Work += 10;
        if (t.Work < MoodWork * 10) return true;
        MakeArtifact(d, shop, craft.makes);
        return false;
    }

    private string MissingMoodNeed(Dwarf d)
    {
        var have = d.MoodGot.Where(Items.ContainsKey).Select(id => Items[id]).ToList();
        foreach (string need in d.MoodNeeds)
        {
            Item match = have.FirstOrDefault(i => Tables.Matches(need, i));
            if (match == null) return need;
            have.Remove(match);
        }
        return null;
    }

    private void MakeArtifact(Dwarf d, Building shop, ItemKind[] makes)
    {
        var parts = d.MoodGot.Where(Items.ContainsKey).Select(id => Items[id]).ToList();
        Item basis = parts.FirstOrDefault() ;
        ItemKind kind = Rng.Pick(makes);
        byte mat = basis?.Mat ?? Tables.Mi("granite");
        if (kind == ItemKind.CutGem) { Item gem = parts.FirstOrDefault(p => p.Kind is ItemKind.RoughGem or ItemKind.CutGem); if (gem != null) mat = gem.Mat; else kind = ItemKind.Craft; }
        int center = World.Idx(shop.Center);
        Item art = Spawn(kind, mat, center, 5);
        art.Artifact = true;
        art.Maker = d.Name;
        string name = Rng.Pick(ArtA) + Rng.Pick(ArtB);
        art.Label = $"{name}, {(new Item { Kind = kind, Mat = mat }).Name.Replace("masterful ", "")}";
        art.Description = Describe(d, art, name, parts);
        foreach (Item p in parts) Destroy(p);
        d.MoodGot.Clear();
        shop.MoodClaim = -1;
        d.Strange = null;
        d.MoodShop = -1;
        d.Xp[(int)d.MoodLabor] = Math.Max(d.Xp[(int)d.MoodLabor], 40 * 225);
        AddThought(d, "made_artifact");
        foreach (Dwarf o in Living) if (o != d) AddThought(o, "admired");
        Artifacts.Add(art.Id);
        Count("artifacts");
        Say($"{d.Name} has created {name}! {art.Description}", LogKind.Alert, center);
    }

    private string Describe(Dwarf d, Item art, string name, List<Item> parts)
    {
        string what = new Item { Kind = art.Kind, Mat = art.Mat }.Name.Replace("masterful ", "");
        var bits = new List<string> { $"{name} is {A(what)} made by {d.Name} in {Date}." };
        Item gem = parts.FirstOrDefault(p => p.Kind is ItemKind.RoughGem or ItemKind.CutGem);
        Item bone = parts.FirstOrDefault(p => p.Kind == ItemKind.Bone);
        Item shine = parts.FirstOrDefault(p => p.Kind == ItemKind.Bar && p.M.Key is "silver" or "gold");
        if (gem != null) bits.Add($"It is studded with {gem.M.Name}.");
        if (shine != null) bits.Add($"It is banded with {shine.M.Name}.");
        if (bone != null) bits.Add($"It is decorated with {bone.Source ?? "animal"} bone.");
        bits.Add($"On it is an image of {Scene(d)}.");
        return string.Join(" ", bits);
    }

    /// <summary>Something from the hold's own history, to be worked into the piece.</summary>
    private string Scene(Dwarf maker)
    {
        var moments = Log.Where(l => l.Kind is LogKind.Alert or LogKind.Good && !l.Text.Contains("mood") && !l.Text.Contains("has created")).Select(l => l.Text.TrimEnd('.', '!')).ToList();
        if (moments.Count > 0 && Rng.Chance(0.75))
        {
            string m = Rng.Pick(moments);
            return $"the moment when {char.ToLower(m[0])}{m[1..]}";
        }
        string[] stock = { $"{maker.FirstName} and a mountain", "dwarves striking the earth", "a grimling cowering before a hammer", "the first ale of the hold", "a cave crawler, writhing", $"the seven founders of {Name}" };
        return Rng.Pick(stock);
    }

    private void GoMad(Dwarf d)
    {
        Building shop = d.MoodShop >= 0 ? BuildingById(d.MoodShop) : null;
        if (shop != null) { shop.MoodClaim = -1; foreach (int id in d.MoodGot.Where(Items.ContainsKey)) Items[id].ReservedBy = -1; }
        d.MoodGot.Clear();
        d.Strange = null;
        d.MoodShop = -1;
        double r = Rng.Float();
        d.Insane = r < 0.4 ? "melancholy" : r < 0.7 ? "berserk" : "wanderer";
        Count("insane");
        Say(d.Insane switch
        {
            "melancholy" => $"{d.Name} has been stricken by melancholy, denied the work.",
            "berserk" => $"{d.Name} has gone berserk!",
            _ => $"{d.Name} has gone stark raving mad and wanders off, gibbering.",
        }, LogKind.Alert, d.Cell);
        foreach (Dwarf o in Living) if (o.Opinion.GetValueOrDefault(d.Id) > 20) AddThought(o, "friend_mad");
    }

    /// <summary>What the mad do.</summary>
    private Task PlanMadness(Dwarf d) => d.Insane switch
    {
        "berserk" => new Task { Kind = TaskKind.Rampage },
        "wanderer" => new Task { Kind = TaskKind.Rampage, Note = "leave" },
        _ => new Task { Kind = TaskKind.Wander, Dest = WanderSpot(d, World.Pos(d.Cell), 3), Total = 300 },
    };

    private bool DoRampage(Dwarf d, Task t)
    {
        if (t.Note == "leave")
        {
            d.Activity = "Wandering off, gibbering";
            P3 at = World.Pos(d.Cell);
            if (at.X <= 0 || at.Y <= 0 || at.X >= World.W - 1 || at.Y >= World.H - 1 || ++t.Work > 4000)
            {
                Say($"{d.Name} has vanished into the wilds.", LogKind.Bad, d.Cell);
                Lose(d, "lost to madness");
                return false;
            }
            if (d.Path == null) { var p = Path.Find(d.Cell, World.Idx(EdgeNear(at)), 60000); if (p != null && p.Count > 1) SetPath(d, p); else t.Work += 200; }
            return true;
        }
        d.Activity = "Berserk!";
        Dwarf victim = Living.Where(o => o != d && World.Pos(o.Cell).Z == World.Pos(d.Cell).Z).OrderBy(o => World.Pos(o.Cell).Dist(World.Pos(d.Cell))).FirstOrDefault();
        if (victim == null) return ++t.Work < 300;
        if (Adjacent(d.Cell, victim.Cell))
        {
            if (d.AttackWait <= 0) DwarfHitsDwarf(d, victim);
            return true;
        }
        var path = Path.Find(d.Cell, victim.Cell, 15000);
        if (path == null || path.Count < 2) return ++t.Work < 300;
        SetPath(d, path.Count > 6 ? path.GetRange(0, 6) : path);
        return true;
    }

    /// <summary>A blow between dwarves: a berserker's, or a defender's against one.</summary>
    private void DwarfHitsDwarf(Dwarf a, Dwarf b)
    {
        a.AttackWait = Tables.AttackTicks;
        double hit = Math.Clamp(0.6 + 0.05 * (FightSkill(a) - FightSkill(b)), 0.15, 0.92);
        if (!Rng.Chance(hit)) return;
        float stop = Tables.ArmorStop(Worn(b.Armor), Worn(b.Helm));
        int dmg = Math.Max(1, (int)(Tables.DwarfDamage * Tables.WeaponMult(WeaponOf(a)) * (0.7 + Rng.Float() * 0.6) * (1 - stop)));
        b.Hp -= dmg;
        b.LastHurt = Tick;
        if (b.Hp <= 0)
        {
            Kill(b, b.Insane == "berserk" ? $"put down by {a.FirstName}" : $"slain by the berserk {a.FirstName}");
            if (b.Insane == "berserk") Say($"{a.FirstName} has put down the berserk {b.FirstName}. A sad day.", LogKind.Bad, b.Cell);
        }
        else if (b.Insane != "berserk") AddThought(b, "hurt");
    }

    /// <summary>Gone without a body: wandered off, never seen again.</summary>
    private void Lose(Dwarf d, string cause)
    {
        Release(d);
        foreach (int id in new[] { d.Carry, d.Pick, d.Axe, d.Weapon, d.Armor, d.Helm, d.Shield })
            if (id >= 0 && Items.TryGetValue(id, out Item worn)) Destroy(worn);
        Discharge(d);
        d.Dead = true;
        d.Buried = true;
        d.DeathCause = cause;
        d.Activity = "Lost";
        d.DiedAt = Tick;
        if (d.Bed >= 0) { Building bed = BuildingById(d.Bed); if (bed != null) bed.Owner = -1; d.Bed = -1; }
        if (d.Chest >= 0) { Building chest = BuildingById(d.Chest); if (chest != null) chest.Owner = -1; d.Chest = -1; }
        Count("lost");
    }

    /// <summary>Living dwarves near a fine piece on display admire it.</summary>
    private void Admire(Dwarf d)
    {
        P3 at = World.Pos(d.Cell);
        foreach (Building b in Buildings)
        {
            if (!b.Built || b.Z != at.Z || (b.Quality < 4 && !b.Artifact) || b.D.Kind != BuildKind.Furniture) continue;
            if (Math.Abs(b.X - at.X) <= 3 && Math.Abs(b.Y - at.Y) <= 3) { AddThought(d, "admired", b.Artifact ? 3 : 1); return; }
        }
    }

    /// <summary>The hold's artifacts, in the order they were made.</summary>
    public List<int> Artifacts = new();
}
