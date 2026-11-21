using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Every clan has its portraits (scripts/portraits.mjs), and every dwarf gets one.</summary>
public partial class SmokeTest
{
    private void Portraits()
    {
        var missing = (from c in Tables.Clans
                       from sex in new[] { "m", "f" }
                       from age in new[] { "young", "old" }
                       from n in Enumerable.Range(1, Assets.PortraitsEach)
                       let name = $"{c.Id}-{sex}-{age}-{n}"
                       where !FileAccess.FileExists($"{Assets.PortraitDir}/{name}.webp")
                       select name).ToList();
        Check(missing.Count == 0, $"every clan has its portraits (missing {missing.Count}: {string.Join(", ", missing.Take(4))}) -- node scripts/portraits.mjs");
        var f = Tiny(new[] { "#####", "#@@@#", "#####" });
        Check(f.Dwarves.All(d => Assets.Portrait(d) != null), "every dwarf has a portrait");
        Dwarf a = f.Dwarves[0];
        Check(Assets.Portrait(a) == Assets.Portrait(a), "and always the same one");
    }
}
