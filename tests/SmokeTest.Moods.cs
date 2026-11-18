using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Strange moods, artifacts and madness.</summary>
public partial class SmokeTest
{
    private static Fortress MoodHold(out Dwarf d, out Building shop, bool stocked = true)
    {
        Fortress f = Tiny(new[] { "##############", "#@...........#", "#.@..........#", "#............#", "#............#", "##############" });
        World w = f.World;
        d = f.Dwarves[0];
        d.Xp[(int)Labor.Carpentry] = 40 * 49;
        f.Dwarves[1].Xp[(int)Labor.Carpentry] = 0;
        shop = f.Place("carpenter", 8, 2, 1);
        shop.Built = true;
        if (stocked)
        {
            string[] mats = { "oak", "oak", "oak" };
            for (int k = 0; k < 3; k++)
            {
                f.Spawn(ItemKind.Log, Tables.Mi("oak"), w.Idx(2 + k, 4, 1));
                f.Spawn(ItemKind.RoughGem, Tables.Mi("amethyst"), w.Idx(5 + k, 4, 1));
                f.Spawn(ItemKind.Bone, Tables.Mi("bone"), w.Idx(1, 3 + k % 2, 1)).Source = "goat";
                f.Spawn(ItemKind.Bar, Tables.Mi("silver"), w.Idx(3 + k, 3, 1));
                f.Spawn(ItemKind.Leather, Tables.Mi("leather"), w.Idx(6 + k, 3, 1));
            }
        }
        return f;
    }

    private void StrangeMoods()
    {
        Fortress f = MoodHold(out Dwarf d, out Building shop);
        f.StartMood(d, "fey");
        Check(d.Strange == "fey" && d.HadMood, "a dwarf is taken by a fey mood");
        Eq(d.MoodLabor, Labor.Carpentry, "for their best craft");
        Check(d.MoodNeeds.Count >= 2 && d.MoodNeeds[0] == "log", $"wanting wood and more ({string.Join(", ", d.MoodNeeds)})");
        Check(f.PauseFor != null, "the mood is announced");
        int t = Run(f, () => d.Strange == null, Tables.TicksPerDay * 5);
        Check(d.Strange == null && d.Insane == null, $"given the materials, the mood ends well ({t} ticks)");
        Item art = f.Items.Values.FirstOrDefault(i => i.Artifact);
        Check(art != null, "with an artifact");
        if (art != null)
        {
            GD.Print($"    artifact: {art.Label}. {art.Description}");
            Check(art.Label != null && art.Description != null && art.Description.Contains(d.Name), $"named and described: {art.Label} -- {art.Description}");
            Check(art.Value > 500, $"and worth a fortune ({art.Value})");
            Eq(art.Maker, d.Name, "the maker is known");
        }
        Eq(d.Skill(Labor.Carpentry), 15, "the maker is now a legendary carpenter");
        Check(d.Thoughts.Any(x => x.Id == "made_artifact"), "and very pleased");
        Eq(shop.MoodClaim, -1, "the workshop is let go");
        Check(f.Artifacts.Count == 1, "the chronicle records it");
        Check(!d.HadMood || !f.Living.Where(x => x != d).Any(x => x.HadMood), "only the one dwarf has had a mood");
        // An artifact bed (or table, chair, door) set in place keeps its name.
        if (art != null && Tables.Items[art.Kind].Places is string place && place != "trap")
        {
            Building b = f.Place(place, 2, 1, 1);
            Run(f, () => b.Built, 3000);
            Check(b.Built && b.Artifact && b.Label == art.Label, "an artifact placed as furniture stays itself");
            f.Deconstruct(b);
            Check(f.Items.ContainsKey(art.Id) && f.Items[art.Id].InBuilding < 0, "and comes back whole when taken down");
        }
        // Denied: madness.
        Fortress g = MoodHold(out Dwarf e, out Building s2, stocked: false);
        g.StartMood(e, "secretive");
        Run(g, () => e.Insane != null, Tables.TicksPerDay * (Fortress.MoodDeadlineDays + 1));
        Check(e.Insane != null, $"denied its materials, a mood ends in madness ({e.Insane})");
        Check(g.Log.Any(l => l.Text.Contains("demands")), "after the dwarf has made their demands known");
        Eq(s2.MoodClaim, -1, "the workshop is freed");
        // No workshop at all.
        Fortress h = Tiny(new[] { "##########", "#@.......#", "##########" });
        h.StartMood(h.Dwarves[0], "fey");
        Run(h, () => false, 200);
        Check(h.Log.Any(l => l.Text.Contains("needs a")), "with no workshop of the craft, the dwarf says what they need");
        // Saves keep it all.
        Fortress back = Save.Read(Save.Write(f));
        Check(back.Items.Values.Any(i => i.Artifact && i.Label == art?.Label), "artifacts survive a save");
    }

    private void Madness()
    {
        var f = Tiny(new[] { "##########", "#@.@.....#", "##########" });
        Dwarf mad = f.Dwarves[0], other = f.Dwarves[1];
        mad.Insane = "berserk";
        mad.Xp[(int)Labor.Fighting] = 40 * 25;
        Run(f, () => mad.Dead || other.Dead || other.Hp < Tables.MaxHp, 2000);
        Check(other.Hp < Tables.MaxHp || other.Dead || mad.Dead, "a berserk dwarf attacks the others");
        Run(f, () => mad.Dead || other.Dead, 8000);
        Check(mad.Dead || other.Dead, "and it ends in blood");
        var g = Tiny(new[] { "########", "#@.....#", "########" });
        Dwarf w = g.Dwarves[0];
        var stay = g.AddDwarf("mossbeard", g.World.Pos(g.World.Idx(5, 1, 1)));
        stay.Cell = g.World.Idx(5, 1, 1);
        w.Insane = "wanderer";
        Run(g, () => w.Dead, 6000);
        Check(w.Dead && w.DeathCause == "lost to madness", "a dwarf gone stark raving mad is lost to the wilds");
        Check(!g.Items.Values.Any(i => i.Of == w.Id), "leaving no body");
        var m = Tiny(new[] { "########", "#@.....#", "########" });
        Dwarf sad = m.Dwarves[0];
        sad.Insane = "melancholy";
        m.Designate(new P3(3, 2, 1), new P3(3, 2, 1), Desig.Dig);
        m.Spawn(ItemKind.Pick, Tables.Mi("copper"), sad.Cell);
        Run(m, () => false, 1500);
        Eq(m.Designated.Count, 1, "a melancholy dwarf does no work");
    }
}
