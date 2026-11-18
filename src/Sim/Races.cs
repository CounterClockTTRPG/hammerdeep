using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// A people that can live in the hold. Dwarves are one; Delvlings, Rootfolk
/// and Stonekin come as migrants. Everything that differs is here, as data:
/// body, pace, appetite, what they are good at, their god, their quirks.
/// </summary>
public sealed class RaceDef
{
    public string Key, Name, Plural, Clan, Deity, Blurb;
    public float Size = 1, HpMult = 1, Pace = 1, Hunger = 1, Thirst = 1, Fist = 1;
    public Dictionary<Labor, float> Work = new();
    /// <summary>Quirks: sleeps anywhere, digs bare-handed, pockets shiny things, pines for the sky.</summary>
    public bool NoBed, BareHands, Thief, NeedsSky;
    public Rgb Skin, Hair;
    public string[] NameA, NameB;
    public float Work_(Labor l) => Work.TryGetValue(l, out float f) ? f : 1f;
}

public static class Races
{
    public static readonly List<RaceDef> All = new()
    {
        new RaceDef
        {
            Key = "dwarf", Name = "dwarf", Plural = "dwarves", Clan = null, Blurb = "Stout, stubborn, and very good with stone.",
            Skin = Rgb.Hex(0xebbd99), Hair = Rgb.Hex(0x73471f),
        },
        new RaceDef
        {
            Key = "delvling", Name = "Delvling", Plural = "Delvlings", Clan = "copperclaw", Deity = "glimmermaw",
            Blurb = "Small, quick and scaly; born to tunnels, and unable to leave anything shiny alone.",
            Size = 0.75f, HpMult = 0.7f, Pace = 0.8f, Hunger = 0.8f, Fist = 0.8f, NoBed = true, Thief = true,
            Work = { [Labor.Mining] = 1.3f, [Labor.Hauling] = 1.25f, [Labor.Gathering] = 1.2f, [Labor.Smithing] = 0.7f, [Labor.Crafting] = 0.85f, [Labor.Fighting] = 0.8f },
            Skin = Rgb.Hex(0x7d9a52), Hair = Rgb.Hex(0x4a5e2c),
            NameA = new[] { "Skrit", "Nib", "Tok", "Vix", "Grub", "Pell", "Snik", "Kreel", "Ziv", "Mott" },
            NameB = new[] { "ik", "ak", "le", "y", "it", "sy", "" },
        },
        new RaceDef
        {
            Key = "rootfolk", Name = "Rootfolk", Plural = "Rootfolk", Clan = "hollyhock", Deity = "velda",
            Blurb = "Small green-capped growers: matchless farmers and brewers, who pine without the sky.",
            Size = 0.85f, HpMult = 0.8f, Pace = 1f, Hunger = 0.9f, Fist = 0.7f, NeedsSky = true,
            Work = { [Labor.Farming] = 1.5f, [Labor.Brewing] = 1.35f, [Labor.Cooking] = 1.3f, [Labor.Gathering] = 1.3f, [Labor.Husbandry] = 1.2f, [Labor.Mining] = 0.6f, [Labor.Fighting] = 0.7f, [Labor.Smithing] = 0.8f },
            Skin = Rgb.Hex(0xd9b08a), Hair = Rgb.Hex(0x3e7a3a),
            NameA = new[] { "Bram", "Wil", "Fen", "Hollis", "Moss", "Tansy", "Pip", "Rue", "Sorrel", "Bryn" },
            NameB = new[] { "", "wick", "by", "kin", "ett", "a" },
        },
        new RaceDef
        {
            Key = "stonekin", Name = "Stonekin", Plural = "Stonekin", Clan = "graniteborn", Deity = "durn",
            Blurb = "Rare half-giants of living grey: they dig with their hands, haul like oxen, and eat like two.",
            Size = 1.5f, HpMult = 2f, Pace = 1.25f, Hunger = 2f, Thirst = 1.5f, Fist = 1.8f, BareHands = true,
            Work = { [Labor.Mining] = 1.5f, [Labor.Construction] = 1.4f, [Labor.Hauling] = 1.5f, [Labor.Masonry] = 1.2f, [Labor.Fighting] = 1.3f, [Labor.Crafting] = 0.6f, [Labor.Carpentry] = 0.8f, [Labor.Cooking] = 0.8f },
            Skin = Rgb.Hex(0x8e8a86), Hair = Rgb.Hex(0x4a4642),
            NameA = new[] { "Ur", "Grond", "Bael", "Morg", "Thul", "Kaar", "Oskr", "Vund" },
            NameB = new[] { "gar", "oth", "um", "ak", "ir" },
        },
    };

    public static RaceDef Of(string key) => All.FirstOrDefault(r => r.Key == key) ?? All[0];
    public static RaceDef Of(Dwarf d) => Of(d.Race);
}
