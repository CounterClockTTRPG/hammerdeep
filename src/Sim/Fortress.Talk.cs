using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>Something a dwarf said out loud: the view shows it as a bubble over them for a few seconds.</summary>
public sealed class Speech
{
    public int Dwarf;
    public string Text;
    public long Tick;
}

/// <summary>
/// Dwarves talk: a strong new thought (a friend lost, a caravan, an artifact, a good stew), fleeing, a tantrum, a mood.
/// The line is picked by what happened, and by who they are (their traits, their god, their clan). Only for show:
/// talk has its own dice, so it never changes what happens in the hold.
/// </summary>
public partial class Fortress
{
    /// <summary>What was said lately, for the view to show (and take away); not saved.</summary>
    [JsonIgnore] public readonly List<Speech> Spoken = new();

    private readonly Random _talk = new();
    private readonly Dictionary<int, long> _spokeAt = new();
    private readonly Queue<string> _recent = new();

    /// <summary>The least time between two things one dwarf says.</summary>
    public const int SpeakGap = 400;

    /// <summary>Has a dwarf say something about <paramref name="topic"/>, if they have a line for it and have not just spoken.</summary>
    public bool Speak(Dwarf d, string topic, double chance = 1)
    {
        if (d == null || d.Dead) return false;
        if (_spokeAt.TryGetValue(d.Id, out long at) && Tick - at < SpeakGap) return false;
        if (_talk.NextDouble() >= chance) return false;
        string line = Talk.Line(this, d, topic, _talk);
        if (line == null) return false;
        // Not the very line someone just said, if there is another.
        for (int k = 0; k < 4 && _recent.Contains(line); k++) line = Talk.Line(this, d, topic, _talk);
        _recent.Enqueue(line);
        if (_recent.Count > 8) _recent.Dequeue();
        _spokeAt[d.Id] = Tick;
        Spoken.Add(new Speech { Dwarf = d.Id, Text = line, Tick = Tick });
        if (Spoken.Count > 40) Spoken.RemoveAt(0);
        return true;
    }

    /// <summary>A new thought is said aloud more often the stronger it is.</summary>
    private void SpeakThought(Dwarf d, string id, int value) => Speak(d, id, Math.Clamp(Math.Abs(value) / 15.0, 0.12, 1.0));
}

/// <summary>The lines. "{god}", "{clan}" and "{hold}" are filled in; "topic/trait" lines are for dwarves with that trait.</summary>
public static class Talk
{
    public static string Line(Fortress f, Dwarf d, string topic, Random rng)
    {
        if (!Lines.TryGetValue(topic, out string[] general)) return null;
        var own = d.Traits.Where(t => Lines.ContainsKey(topic + "/" + t)).SelectMany(t => Lines[topic + "/" + t]).ToList();
        string[] pool = own.Count > 0 && rng.NextDouble() < 0.6 ? own.ToArray() : general;
        string line = pool[rng.Next(pool.Length)];
        return line.Replace("{god}", Fortress.GodOf(d)?.Name ?? "stone and fire")
            .Replace("{clan}", Tables.Clan(d.Clan).Name)
            .Replace("{hold}", f.Name);
    }

    public static readonly Dictionary<string, string[]> Lines = new()
    {
        ["ate_meal"] = new[] { "Now that's a stew.", "Proper food, at last.", "My compliments to the kitchen!" },
        ["ate_meal/glutton"] = new[] { "Is there more? There's always more.", "A second bowl, I think. And a third." },
        ["drank_ale"] = new[] { "Ahh. That's the stuff.", "To {hold}!", "Good ale. Good day." },
        ["drank_ale/alelover"] = new[] { "Ale! Sweet, sweet ale!", "Now I can work." },
        ["drank_water"] = new[] { "Water. Like a beast.", "Who forgot to brew?" },
        ["drank_water/alelover"] = new[] { "Water?! I'll not stand for it.", "A hold without ale is a hole." },
        ["no_ale"] = new[] { "Not a drop of ale anywhere...", "My throat's a dry tunnel." },
        ["hungry"] = new[] { "My stomach's growling like a cave bear.", "When do we eat?" },
        ["thirsty"] = new[] { "I'd kill for a drink.", "So dry..." },
        ["slept_floor"] = new[] { "Slept on rock again. My back!", "A bed. Is that too much to ask?" },
        ["slept_own"] = new[] { "Nothing like your own bed.", "Slept like a stone." },
        ["own_room"] = new[] { "A room of my own. Finally.", "My things, my bed, my room." },
        ["saw_death"] = new[] { "No... not like that.", "Back to the stone with you, friend." },
        ["saw_death/brave"] = new[] { "They died well. So will I.", "Steady. Keep your axes up." },
        ["friend_died"] = new[] { "They were my friend.", "Gone. Just gone.", "I'll carve their name myself." },
        ["friend_died/gloomy"] = new[] { "Everyone leaves in the end.", "Why them? Why not me?" },
        ["admired"] = new[] { "Look at the work in that!", "Now that's craft." },
        ["admired/artistic"] = new[] { "The line of it... beautiful.", "I could weep." },
        ["good_work"] = new[] { "Fine work, if I say so myself.", "That'll last a thousand years." },
        ["chatted"] = new[] { "...and then the troll sat on it!", "Did you hear about the cave fish?", "Ha! Just like your uncle.", "Fine weather underground today." },
        ["chatted/sociable"] = new[] { "Sit, sit! Tell me everything.", "I love a good gossip." },
        ["chatted/loner"] = new[] { "Mm. Yes. Is that all?", "I was enjoying the quiet." },
        ["feud"] = new[] { "Watch yourself, {clan}-hater.", "Your clan owes mine a debt." },
        ["feud/grudgeful"] = new[] { "I remember what your grandfather did.", "Never forgotten. Never forgiven." },
        ["struck_gems"] = new[] { "Gems! Look at them shine!", "The mountain gives!" },
        ["struck_gems/greedy"] = new[] { "Mine. All mine. Well, mostly.", "Pretty, pretty stones..." },
        ["new_arrivals"] = new[] { "New faces! Welcome!", "More hands for the mountain." },
        ["idle"] = new[] { "Nothing to do...", "Somebody give me a job." },
        ["idle/lazy"] = new[] { "A nap, then. Just a short one.", "Work can wait." },
        ["idle/hardworking"] = new[] { "Idle hands! Give me a pick!", "Is there truly nothing to dig?" },
        ["tantrum_after"] = new[] { "I... might have overreacted.", "Sorry about the table." },
        ["caravan"] = new[] { "Wagons! Traders at the gate!", "Let's see what they've brought." },
        ["rain"] = new[] { "Wet sky. Hate it.", "Water from above. Unnatural." },
        ["hurt"] = new[] { "Argh! My arm!", "Just a scratch... a big scratch." },
        ["killed"] = new[] { "Back to the dark with you!", "One less." },
        ["killed_brave"] = new[] { "Who's next?!", "For {hold}!", "Ha! Is that all?" },
        ["raid_won"] = new[] { "And stay out!", "We held! We held!" },
        ["beast_dread"] = new[] { "Did you hear that roar?", "Something huge is out there." },
        ["beast_dread/brave"] = new[] { "Let it come. I'll have its hide.", "A beast? Good. I was bored." },
        ["rank_up"] = new[] { "{hold} rises!", "They'll sing of this hold!" },
        ["noble_angry"] = new[] { "The noble's screaming again.", "What now, your lordship?" },
        ["beast_slain"] = new[] { "The beast is dead! Sing it!", "A legend falls to {hold}!" },
        ["alarm"] = new[] { "Inside! Everyone inside!", "Bar the doors!" },
        ["laid_to_rest"] = new[] { "Rest in the stone, old friend.", "May {god} keep you." },
        ["petted"] = new[] { "Who's a good beast? You are.", "There, there." },
        ["pocketed"] = new[] { "Nobody saw that. Good.", "Shiny..." },
        ["robbed"] = new[] { "Where's my ring? Delvlings!", "Something's missing. Again." },
        ["misses_sky"] = new[] { "I want to see the sky.", "When did I last see the sun?" },
        ["saw_sky"] = new[] { "The sky! Still up there.", "Fresh air at last." },
        ["made_artifact"] = new[] { "It is finished. It is perfect.", "They'll remember my name for this." },
        ["prayed"] = new[] { "{god}, watch over us.", "Thank you, {god}." },
        ["prayed/pious"] = new[] { "{god} hears me. I know it.", "Blessed be {god}, forever." },
        ["prayed_temple"] = new[] { "What a temple. {god} must be pleased.", "Peace, at last." },
        ["no_prayer"] = new[] { "Nowhere to pray in this hold.", "{god} feels so far away." },
        ["feast"] = new[] { "To the founders!", "Another round for the founders!", "Best night of the year!" },
        ["whispers"] = new[] { "Do you hear it? From below...", "It's calling me." },
        ["friend_mad"] = new[] { "They've lost their mind...", "What happened to them?" },
        ["unburied"] = new[] { "Someone bury the dead!", "The smell... the poor soul." },
        ["haunted"] = new[] { "A ghost! A GHOST!", "It touched me. It was cold." },
        ["heard_bard"] = new[] { "Play the one about the dragon!", "What a voice." },
        ["clan_pride"] = new[] { "{clan} built this hold!", "Proud to be {clan}." },
        ["clan_slighted"] = new[] { "Nobody listens to {clan}.", "We're always last, we {clan}." },
        ["brawled"] = new[] { "He started it!", "I'll knock your teeth in!" },
        ["colossus"] = new[] { "Look at it. Just look at it.", "Our founders, in stone." },
        ["flee"] = new[] { "Run!", "Get back! Back!", "Help!", "Not today!" },
        ["flee/brave"] = new[] { "Fall back, regroup!", "I'll be back for you." },
        ["tantrum"] = new[] { "ENOUGH!", "I can't take it any more!", "Everything! Is! Wrong!" },
        ["mood"] = new[] { "I must make something.", "I see it... I need materials!", "Leave me be. I have work." },
        ["insane"] = new[] { "Hahahaha!", "The walls are talking.", "..." },
    };
}
