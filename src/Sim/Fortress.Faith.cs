using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

public sealed class DeityDef
{
    public string Key, Name, Title, Clan, Sphere;
    public Rgb Color;
    /// <summary>The god of the deep: no clan's, and not safe to worship.</summary>
    public bool Deep;
}

/// <summary>
/// Faith and runes: each clan's god, shrines and temples, the need to pray,
/// the new year's feast, temple prayers that calm the restless dead; the old
/// runes the deep teaches, carved at the runeforge, and the Sleeper Below.
/// </summary>
public sealed partial class Fortress
{
    public static readonly List<DeityDef> Deities = new()
    {
        new DeityDef { Key = "ankra", Name = "Ankra", Title = "the Anvil", Clan = "ironhand", Sphere = "fire and the forge", Color = Rgb.Hex(0xe0702a) },
        new DeityDef { Key = "velda", Name = "Velda", Title = "of the Green Dark", Clan = "mossbeard", Sphere = "growing things and ale", Color = Rgb.Hex(0x6fb04a) },
        new DeityDef { Key = "durn", Name = "Durn", Title = "the Unbroken", Clan = "stoneward", Sphere = "walls and war", Color = Rgb.Hex(0x8a9ab8) },
        new DeityDef { Key = "sith", Name = "Sith", Title = "the Glimmering", Clan = "gemhollow", Sphere = "gems and wealth", Color = Rgb.Hex(0xb070d0) },
        new DeityDef { Key = "glimmermaw", Name = "Glimmermaw", Title = "of the Hoard", Clan = "copperclaw", Sphere = "tunnels and shiny things", Color = Rgb.Hex(0xd8a020) },
        new DeityDef { Key = "sleeper", Name = "That Which Sleeps Below", Title = "", Clan = null, Sphere = "the deep, and dreams", Color = Rgb.Hex(0x40c0b0), Deep = true },
    };

    public static DeityDef God(string key) => Deities.FirstOrDefault(g => g.Key == key);
    public static DeityDef GodOf(Dwarf d) => d.Has("faithless") ? null : Races.Of(d).Deity is string k ? God(k) : Deities.FirstOrDefault(g => g.Clan == d.Clan);

    public const int PrayAt = 3500, UnprayedAt = 7500, FeastTicks = 600;
    public static readonly string[] Runes = { "striking", "warding", "delving" };

    public bool RunesKnown;
    public long FeastUntil;

    // ---- prayer -------------------------------------------------------------------------

    private void FaithTick(Dwarf d)
    {
        if (d.Has("faithless")) return;
        d.Faith += d.Has("pious") && Tick % 2 == 0 ? 2 : 1;
    }

    /// <summary>A shrine to their god, or the temple, if one can be reached.</summary>
    private Task PlanPrayer(Dwarf d)
    {
        DeityDef god = GodOf(d);
        if (god == null || d.Faith < PrayAt || d.Insane != null) return null;
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        P3 at = World.Pos(d.Cell);
        Building place = Buildings.Where(b => b.Built && (b.Def == "temple" || (b.Def == "shrine" && b.Deity == god.Key)) && World.Comp[World.Idx(b.Center)] == comp)
            .OrderBy(b => b.Center.Dist(at)).FirstOrDefault();
        // The gloomy, and those who live deep, are drawn to the Sleeper's shrine.
        Building deep = Buildings.Where(b => b.Built && b.Def == "shrine" && b.Deity == "sleeper" && World.Comp[World.Idx(b.Center)] == comp).OrderBy(b => b.Center.Dist(at)).FirstOrDefault();
        if (deep != null && (place == null || (d.Has("gloomy") && Rng.Chance(0.5)) || Rng.Chance(0.1))) place = deep;
        if (place == null) return null;
        return new Task { Kind = TaskKind.Pray, Building = place.Id };
    }

    private bool DoPray(Dwarf d, Task t)
    {
        Building b = BuildingById(t.Building);
        if (b == null || !b.Built) return false;
        DeityDef god = b.Def == "temple" ? GodOf(d) : God(b.Deity);
        d.Activity = $"Praying to {god?.Name ?? "the gods"}";
        if (t.Stage == 0)
        {
            int w = WalkAny(d, BuildSpots(b));
            if (w < 0) return false;
            if (w == 0) return true;
            t.Stage = 1;
        }
        if (++t.Work < 150) return true;
        d.Faith = 0;
        d.Thoughts.RemoveAll(x => x.Id == "no_prayer");
        if (b.Def == "shrine" && God(b.Deity)?.Deep == true) SleeperWhispers(d);
        else AddThought(d, b.Def == "temple" ? "prayed_temple" : "prayed", d.Has("pious") ? 2 : 1);
        Count("prayers");
        // Prayers at a temple may settle a restless spirit.
        if (b.Def == "temple" && Rng.Chance(0.15))
        {
            Dwarf ghost = Dwarves.FirstOrDefault(x => x.Dead && x.Restless && !x.Calmed);
            if (ghost != null)
            {
                ghost.Restless = false;
                ghost.Calmed = true;
                Say($"The prayers of the temple have calmed the spirit of {ghost.Name}. It walks no more.", LogKind.Good, World.Idx(b.Center));
                Count("spirits_calmed");
            }
        }
        return false;
    }

    private void FaithMoods(Dwarf d)
    {
        if (d.Faith > UnprayedAt) AddThought(d, "no_prayer", d.Has("pious") ? 2 : 1);
    }

    // ---- the feast ------------------------------------------------------------------------

    /// <summary>The first of Thaw, with a temple: the Feast of the Founding.</summary>
    private void NewYearRites()
    {
        Building temple = Buildings.FirstOrDefault(b => b.Built && b.Def == "temple");
        if (temple == null || Year < 2) return;
        FeastUntil = Tick + FeastTicks;
        Say($"The Feast of the Founding! {Name} gathers at the temple to give thanks for its {Year - 1}{(Year - 1 == 1 ? "st" : Year - 1 == 2 ? "nd" : Year - 1 == 3 ? "rd" : "th")} year.", LogKind.Good, World.Idx(temple.Center));
        Count("feasts");
    }

    private bool Feasting => Tick < FeastUntil && Buildings.Any(b => b.Built && b.Def == "temple");

    private bool DoFeast(Dwarf d, Task t)
    {
        Building temple = Buildings.FirstOrDefault(b => b.Built && b.Def == "temple");
        if (temple == null || !Feasting) return false;
        d.Activity = "At the feast";
        if (t.Stage == 0)
        {
            t.Stage = 1;
            int w = Walk(d, WanderSpot(d, temple.Center, 3));
            if (w == 0) return true;
        }
        if (t.Work++ == 0) AddThought(d, "feast");
        return t.Work < 120;
    }

    // ---- runes ----------------------------------------------------------------------------

    /// <summary>The deep teaches the old runes: once the caverns are found, or the delving goes this far.</summary>
    private void LearnRunes()
    {
        if (RunesKnown) return;
        RunesKnown = true;
        Say("Deep in the stone, your dwarves have found the old runes carved by those who came before. A runeforge can now be built.", LogKind.Alert);
    }

    /// <summary>A rune carved: the item is kept and marked, the gem spent.</summary>
    private void CarveRune(Recipe r, List<Item> inputs, int at, Dwarf d)
    {
        string rune = r.Id switch { "rune_strike" => "striking", "rune_ward" => "warding", _ => "delving" };
        Item target = inputs.FirstOrDefault(i => i.Kind != ItemKind.RoughGem && i.Kind != ItemKind.CutGem);
        if (target == null) return;
        inputs.Remove(target);
        target.Rune = rune;
        target.Quality = Math.Max(target.Quality, Math.Min(5, d.Skill(Labor.Crafting) / 3));
        target.Maker ??= d.Name;
        target.InBuilding = -1;
        Building shop = BuildingById(target.Cell >= 0 && BuildingAt.TryGetValue(target.Cell, out int bid) ? bid : -1);
        shop?.Parts.Remove(target.Id);
        target.ReservedBy = -1;
        Drop(target, at);
        Count("runes");
        Say($"{d.FirstName} carved a rune of {rune} into {target.Name}.", LogKind.Good, at);
    }

    /// <summary>A shrine to the Sleeper hastens rune work, and its worshippers hear things.</summary>
    private bool SleeperShrine => Buildings.Any(b => b.Built && b.Def == "shrine" && b.Deity == "sleeper");

    private void SleeperWhispers(Dwarf d)
    {
        AddThought(d, "whispers");
        if (Rng.Chance(0.04) && d.Strange == null && d.Insane == null && !d.HadMood)
        {
            Say($"{d.FirstName} hears the Sleeper in a dream, and wakes changed.", LogKind.Bad, d.Cell);
            StartMood(d, "possessed");
        }
    }

    /// <summary>Shrines need a god: the commonest clan's by default, or the Sleeper's at depth.</summary>
    public string DefaultDeity(int z)
    {
        if (RunesKnown && z <= CavernZ + 2) return "sleeper";
        string clan = Living.GroupBy(d => d.Clan).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "ironhand";
        return Deities.First(g => g.Clan == clan).Key;
    }

    public void Dedicate(Building shrine, string deity)
    {
        DeityDef g = God(deity);
        if (g == null || shrine.Def != "shrine") return;
        if (g.Deep && shrine.Z > CavernZ + 2) return;
        shrine.Deity = deity;
    }
}
