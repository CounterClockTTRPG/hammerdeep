using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>Something at the gate that wants an answer.</summary>
public sealed class Happening
{
    public string Key, Title, Text;
    public string[] Options;
    /// <summary>Unanswered by then, the last option is taken.</summary>
    public long Due;
    public int Value;
    public string Who;
}

/// <summary>
/// The world knocking: a bard who would winter in the hold, someone asking for shelter, an envoy from a
/// neighbouring hold, a wanderer with one odd thing to sell, rats in the stores. Each wants an answer (the
/// game stops for it); left unanswered two days, the last -- the careful -- answer is taken.
/// </summary>
public partial class Fortress
{
    public Happening Pending;
    /// <summary>A bard winters here until then; rats are in the stores for so many days; the envoy's hold sends more next season.</summary>
    public long BardUntil;
    public int RatDays;
    public bool Befriended;

    public static readonly string[] HappeningKeys = { "bard", "refugee", "envoy", "wanderer", "rats" };

    /// <summary>Sounds the game wants played (a beast's roar, a flood, a knock at the gate...): the screen takes them.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public readonly List<string> Cues = new();

    internal void Cue(string name) { if (Cues.Count < 16) Cues.Add(name); }

    /// <summary>Each season from the first Highsun: perhaps something comes to the gate.</summary>
    private void MaybeHappening()
    {
        if (Peaceful || Pending != null || Tick < Tables.TicksPerSeason || Alive == 0 || !Rng.Chance(0.5)) return;
        var can = HappeningKeys.Where(k => k switch
        {
            "bard" => Season is 2 or 3 && BardUntil < Tick,
            "rats" => RatDays == 0 && Items.Values.Count(i => Tables.Matches("food", i)) > 20,
            "envoy" => NeighbourHold() != null,
            _ => true,
        }).ToList();
        if (can.Count > 0) Happen(Rng.Pick(can));
    }

    private string NeighbourHold() => Map == null ? null : Map.Neighbours(WorldX, WorldY).Where(p => p.site.Kind == SiteKind.Hold).Select(p => p.site.Name).FirstOrDefault();

    /// <summary>Bring one about (the self-check and the agent call this directly).</summary>
    public Happening Happen(string key)
    {
        var h = new Happening { Key = key, Due = Tick + Tables.TicksPerDay * 2 };
        switch (key)
        {
            case "bard":
                h.Title = "A bard at the gate";
                h.Text = "A bard with a battered lute asks to winter in the hold. They sing for their supper -- and drink for it too.";
                h.Options = new[] { "Take them in until Thaw", "Send them on their way" };
                break;
            case "refugee":
            {
                RaceDef race = Rng.Pick(Races.All);
                h.Who = race.Key;
                h.Title = "Someone asks for shelter";
                h.Text = $"A ragged {(race.Key == "dwarf" ? "dwarf" : race.Name)} stumbles to the gate: their home was burned, they say, and they ask for shelter.";
                h.Options = new[] { "Take them in", "Turn them away" };
                break;
            }
            case "envoy":
            {
                string hold = NeighbourHold() ?? "a neighbouring hold";
                h.Who = hold;
                h.Value = Math.Clamp(Wealth / 20, 100, 600);
                h.Title = $"An envoy from {hold}";
                h.Text = $"An envoy from {hold} asks for a gift worth {h.Value}, \"between kin\". Paid, {hold} will send word of the hold and more will come; refused, nothing happens -- except that {hold} remembers.";
                h.Options = new[] { $"Give goods worth {h.Value}", "Refuse politely" };
                break;
            }
            case "wanderer":
                h.Value = Rng.Range(80, 200);
                h.Title = "A wanderer with something to sell";
                h.Text = $"A hooded wanderer unwraps one odd thing -- \"older than your mountain\" -- and asks {h.Value} in goods. It may be a treasure. It may be a painted stone.";
                h.Options = new[] { $"Buy it for {h.Value}", "No, thank you" };
                break;
            default:
                h.Key = "rats";
                h.Title = "Rats in the stores";
                h.Text = "Rats have got into the food stores. Clear them out now (and lose a little to the clearing), or leave them be (and lose more, slowly)?";
                h.Options = new[] { "Clear them out", "Leave them be" };
                break;
        }
        Pending = h;
        Count("happenings");
        Cue("gate");
        Say($"{h.Title}: {h.Text}", LogKind.Alert);
        return h;
    }

    /// <summary>The answer: an option of <see cref="Pending"/>.</summary>
    public void Choose(int option)
    {
        Happening h = Pending;
        if (h == null) return;
        Pending = null;
        bool yes = option == 0;
        switch (h.Key)
        {
            case "bard":
                if (!yes) { Say("The bard shrugs and trudges off into the cold.", LogKind.Info); break; }
                BardUntil = (Tick / Tables.TicksPerYear + 1) * Tables.TicksPerYear;
                Say("The bard settles by the hearth. The hall will have music until Thaw.", LogKind.Good);
                break;
            case "refugee":
                if (!yes)
                {
                    Say("The stranger is turned away into the wild. Nobody says much at supper.", LogKind.Bad);
                    foreach (Dwarf d in Living) AddThought(d, "turned_away");
                    break;
                }
                TakeIn(h.Who);
                break;
            case "envoy":
                if (!yes) { Say($"The envoy of {h.Who} bows stiffly and leaves.", LogKind.Info); break; }
                if (Pay(h.Value) < h.Value) { Say($"There is not enough to give: the envoy of {h.Who} leaves, unimpressed.", LogKind.Bad); break; }
                Befriended = true;
                Say($"The envoy takes the gift to {h.Who}. Word of the hold will spread.", LogKind.Good);
                break;
            case "wanderer":
                if (!yes) { Say("The wanderer wraps the thing up again and is gone by morning.", LogKind.Info); break; }
                if (Pay(h.Value) < h.Value) { Say("There is not enough to pay: the wanderer laughs and leaves.", LogKind.Bad); break; }
                P3 at = Embark;
                if (Rng.Chance(0.6))
                {
                    Item t = Spawn(Rng.Chance(0.5) ? ItemKind.CutGem : ItemKind.Craft, Tables.Mi(Rng.Pick(new[] { "gold", "silver", "granite" })), FindStandable(at, 2), 5);
                    t.Label = Rng.Pick(new[] { "the Wanderer's Eye", "the Old Seal", "the Lantern of Kings", "the Whispering Stone" });
                    Say($"It is real: {t.Label}, a masterwork beyond price. The wanderer is long gone.", LogKind.Good);
                }
                else Say("It is a painted stone. The wanderer is long gone.", LogKind.Bad);
                break;
            case "rats":
                if (yes) { int lost = LoseFood(0.05); Say($"The rats are cleared out, and {lost} food with them.", LogKind.Info); }
                else { RatDays = 6; Say("The rats are left be. They will not leave the stores be.", LogKind.Bad); }
                break;
        }
    }

    /// <summary>A newcomer from the wild: of any people; now and then a grimling spy, and a raid follows soon.</summary>
    private void TakeIn(string race)
    {
        ClanDef clan = race == "dwarf" || Races.All.All(r => r.Key != race) ? Rng.Pick(Tables.Clans.Where(c => c.Race == "dwarf").ToList()) : Tables.Clans.First(c => c.Race == race);
        Dwarf d = AddDwarf(clan.Id, Embark);
        d.Race = race == "dwarf" ? null : race;
        d.Hunger = Math.Max(d.Hunger, Tables.TicksPerDay);
        Count("refugees");
        if (Rng.Chance(0.15))
        {
            Say($"{d.FirstName} is taken in -- and slips away that night. A grimling spy! The warren will know the way in.", LogKind.Bad);
            Kill(d, "gone, a grimling spy");
            LaunchRaid(Math.Max(3, 2 + Alive / 4));
            return;
        }
        Say($"{d.FirstName} is taken in, thin and grateful.", LogKind.Good);
    }

    /// <summary>Give away goods worth about <paramref name="value"/> (crafts and the like first). Returns what was given.</summary>
    private int Pay(int value)
    {
        int given = 0;
        foreach (Item it in Tradeable().Where(i => i.Def.Cat is StockCat.Finished or StockCat.Gems).OrderByDescending(i => i.Value).ToList())
        {
            if (given >= value) break;
            if (it.Value > value * 2 && given > 0) continue;
            given += it.Value;
            Destroy(it);
        }
        return given;
    }

    /// <summary>Some of the food, gone. Returns how much.</summary>
    private int LoseFood(double share)
    {
        var food = Items.Values.Where(i => Tables.Matches("food", i) && i.HeldBy < 0 && i.ReservedBy < 0).ToList();
        int n = (int)Math.Ceiling(food.Count * share);
        foreach (Item it in food.OrderBy(_ => Rng.Int(1000)).Take(n)) Destroy(it);
        return n;
    }

    /// <summary>Daily: the bard sings, eats and drinks; rats eat; an answer not given is given.</summary>
    internal void DailyHappenings()
    {
        if (Pending != null && Tick >= Pending.Due) Choose(Pending.Options.Length - 1);
        if (BardUntil > Tick)
        {
            foreach (Dwarf d in Living) AddThought(d, "heard_bard");
            Item meal = Items.Values.FirstOrDefault(i => i.Kind == ItemKind.Meal && i.Free) ?? Items.Values.FirstOrDefault(i => Tables.Matches("food", i) && i.Free);
            if (meal != null) Destroy(meal);
            Item ale = Items.Values.FirstOrDefault(i => i.Kind == ItemKind.Drink && i.Free);
            if (ale != null) Destroy(ale);
        }
        else if (BardUntil > 0 && BardUntil <= Tick) { BardUntil = 0; Say("The bard plays a last song for the Thaw and takes the road.", LogKind.Info); }
        if (RatDays > 0)
        {
            RatDays--;
            int lost = LoseFood(0.04);
            if (lost > 0) Say($"The rats have eaten {lost} food.", LogKind.Bad);
            if (RatDays == 0) Say("The rats have moved on, fat.", LogKind.Info);
        }
    }
}
