using System.Linq;

namespace Hammerdeep;

/// <summary>Dwarves talk: lines for real thoughts and traits, said on strong thoughts, not too often.</summary>
public partial class SmokeTest
{
    private void TalkLines()
    {
        string[] events = { "flee", "tantrum", "mood", "insane" };
        foreach (string key in Talk.Lines.Keys)
        {
            string[] parts = key.Split('/');
            if (!Tables.Thoughts.ContainsKey(parts[0]) && !events.Contains(parts[0])) Check(false, $"talk topic '{parts[0]}' is no thought or event");
            if (parts.Length > 1 && !Tables.Traits.Any(t => t.Id == parts[1])) Check(false, $"talk trait '{parts[1]}' is no trait");
            if (parts.Length > 1 && !Talk.Lines.ContainsKey(parts[0])) Check(false, $"'{key}' has no general lines");
        }
        var f = Tiny(new[] { "#######", "#@@...#", "#######" });
        f.Peaceful = true;
        Dwarf a = f.Dwarves[0], b = f.Dwarves[1];
        f.AddThought(a, "friend_died");
        Eq(f.Spoken.Count, 1, "a dwarf who loses a friend says so");
        Check(f.Spoken[0].Dwarf == a.Id && !f.Spoken[0].Text.Contains('{'), $"in their own words: \"{f.Spoken[0].Text}\"");
        f.AddThought(a, "saw_death");
        Eq(f.Spoken.Count, 1, "and does not speak again straight away");
        f.Speak(b, "prayed");
        Check(f.Spoken.Count == 2 && !f.Spoken[1].Text.Contains('{'), $"another dwarf can: \"{f.Spoken.Last().Text}\"");
        Check(!f.Speak(b, "no_such_topic"), "nobody speaks without a line");
        f.Tick += Fortress.SpeakGap;
        Check(f.Speak(a, "flee"), "after a while, they speak again");
        Fortress g = Save.Read(Save.Write(f));
        Eq(g.Spoken.Count, 0, "what was said is not saved");
    }
}
