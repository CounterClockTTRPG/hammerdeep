using System;
using System.Linq;

namespace Hammerdeep;

public enum Weather : byte { Fair, Floods, Drought, Heat, HardWinter }

/// <summary>
/// The season's weather, chosen as it begins: most are fair, but a Thaw may flood (the surface water rises and
/// runs), a Highsun may bring drought (crops under the sky grow at half speed, the grass gives little) or
/// heat (ale spoils in the stores), and a Deepfrost may be hard (the surface water freezes, the grass gives
/// nothing). Underground, a hold hardly notices -- which is rather the point of a hold.
/// </summary>
public partial class Fortress
{
    public Weather Sky;

    public static string WeatherName(Weather w) => w switch
    {
        Weather.Floods => "floods", Weather.Drought => "drought", Weather.Heat => "a heatwave", Weather.HardWinter => "a hard winter", _ => "fair",
    };

    /// <summary>A new season's weather (the founding Thaw is always fair).</summary>
    internal void NewWeather()
    {
        Weather was = Sky;
        Sky = Weather.Fair;
        if (Tick >= Tables.TicksPerSeason)
        {
            double roll = Rng.Float();
            Sky = Season switch
            {
                0 when roll < 0.25 => Weather.Floods,
                1 when roll < 0.2 => Weather.Drought,
                1 when roll < 0.35 => Weather.Heat,
                3 when roll < 0.3 => Weather.HardWinter,
                _ => Weather.Fair,
            };
        }
        if (Sky == was) return;
        switch (Sky)
        {
            case Weather.Floods:
                Say("The thaw comes all at once: the streams and ponds burst their banks! Mind the low ground.", LogKind.Alert);
                Count("floods");
                Flood();
                break;
            case Weather.Drought:
                Say("Drought: no rain this Highsun. Crops under the sky will grow slowly, and the pastures thin.", LogKind.Bad);
                Count("droughts");
                break;
            case Weather.Heat:
                Say("A heatwave: the ale will spoil in the stores unless it is drunk.", LogKind.Bad);
                Count("heatwaves");
                break;
            case Weather.HardWinter:
                Say("A hard winter: the ponds freeze solid and the pastures give nothing. Water and fodder will be scarce.", LogKind.Bad);
                Count("hard_winters");
                break;
            default:
                if (was != Weather.Fair) Say("The weather turns fair.", LogKind.Good);
                break;
        }
    }

    /// <summary>Every pond and stream under the sky rises; the water finds its own way from there.</summary>
    internal void Flood()
    {
        World w = World;
        for (int i = 0; i < w.N; i++)
        {
            if (!w.WaterAt(i) || !w.IsOutside(i)) continue;
            w.Depth[i] = (byte)Math.Min(7, w.Depth[i] + 2);
            w.Active.Add(i);
            w.Changed.Add(i);
        }
    }

    /// <summary>Daily: the heat spoils ale.</summary>
    internal void DailyWeather()
    {
        if (Sky != Weather.Heat) return;
        var spoiled = Items.Values.Where(i => i.Kind == ItemKind.Drink && i.HeldBy < 0 && i.ReservedBy < 0 && Rng.Chance(0.04)).ToList();
        foreach (Item it in spoiled) Destroy(it);
        if (spoiled.Count > 0) Say($"{spoiled.Count} {(spoiled.Count == 1 ? "barrel of ale has" : "barrels of ale have")} spoiled in the heat.", LogKind.Bad);
    }

    /// <summary>Drought slows what grows under the sky (each 50 ticks, a crop there ripens 25 ticks later).</summary>
    internal void WeatherOnCrops()
    {
        if (Sky != Weather.Drought) return;
        foreach (var (cell, crop) in Crops)
            if (crop.Planted <= Tick && World.IsOutside(cell)) crop.Planted += 25;
    }

    /// <summary>What a grazer gets from a mouthful of grass now.</summary>
    private int GrazeGain => Sky == Weather.HardWinter ? 0 : Sky == Weather.Drought || Season == 3 ? 2 : 7;

    /// <summary>Surface water frozen: drinking means breaking the ice (slow, cold).</summary>
    internal bool Frozen(int cell) => Sky == Weather.HardWinter && World.IsOutside(cell);
}
