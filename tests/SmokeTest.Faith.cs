using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Hammerdeep;

/// <summary>Gods, shrines, temples, the feast; runes and the Sleeper.</summary>
public partial class SmokeTest
{
    private void Prayer()
    {
        var f = Tiny(new[] { "############", "#@.........#", "#..........#", "#..........#", "############" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        Check(Fortress.GodOf(d)?.Key == "ankra", "an Ironhand dwarf worships Ankra the Anvil");
        // A shrine to someone else's god does not help.
        Building wrong = f.Place("shrine", 8, 2, 1); wrong.Built = true;
        f.Dedicate(wrong, "velda");
        d.Faith = Fortress.UnprayedAt + 10;
        f.Advance(400);
        Check(d.Thoughts.Any(t => t.Id == "no_prayer"), "with no shrine to their own god, a dwarf cannot pray, and minds");
        Building shrine = f.Place("shrine", 4, 3, 1); shrine.Built = true;
        f.Dedicate(shrine, "ankra");
        Run(f, () => d.Faith == 0, 2000);
        Eq(d.Faith, 0, "with one, they go and pray");
        Check(d.Thoughts.Any(t => t.Id == "prayed") && !d.Thoughts.Any(t => t.Id == "no_prayer"), "and feel better for it");
        // The temple: any god, stronger comfort, and it calms the restless dead.
        var g = Tiny(new[] { "############", "#@.........#", "#.@........#", "#..........#", "#..........#", "############" });
        Dwarf p = g.Dwarves[0], ghost = g.Dwarves[1];
        p.Traits.Add("pious");
        g.Kill(ghost, "bored to death");
        ghost.Restless = true;
        Building temple = g.Place("temple", 6, 2, 1); temple.Built = true;
        for (int k = 0; k < 30 && ghost.Restless; k++) { p.Faith = Fortress.PrayAt + 1; Run(g, () => p.Faith == 0, 2000); }
        Check(p.Thoughts.Any(t => t.Id == "prayed_temple"), "anyone may pray at the temple");
        Check(!ghost.Restless, "and its prayers lay a restless spirit to rest");
        // The faithless never need to.
        p.Traits.Remove("pious"); p.Traits.Add("faithless");
        int before = p.Faith;
        g.Advance(500);
        Check(p.Faith == before, "the faithless have no need to pray");
    }

    private void Feasts()
    {
        Fortress f = Tiny(new[] { "############", "#@.........#", "#.@........#", "#..........#", "#..........#", "############" });
        Building temple = f.Place("temple", 7, 2, 1); temple.Built = true;
        f.Tick = Tables.TicksPerYear - 1;
        f.Step();
        Check(f.Log.Any(l => l.Text.Contains("Feast of the Founding")), "the first Thaw of a new year brings the Feast of the Founding");
        Run(f, () => f.Dwarves.All(d => d.Thoughts.Any(t => t.Id == "feast")), 800);
        Check(f.Dwarves.All(d => d.Thoughts.Any(t => t.Id == "feast")), "and everyone goes, and enjoys it");
    }

    private void Runecraft()
    {
        var f = Tiny(new[] { "##############", "#@...........#", "#............#", "#............#", "##############" });
        World w = f.World;
        Dwarf d = f.Dwarves[0];
        d.Xp[(int)Labor.Crafting] = 40 * 64;
        Check(f.CanPlace("runeforge", 6, 1, 1)?.Contains("runes") == true, "the runeforge waits on the old runes");
        f.CavernZ = 5;
        f.Designate(new P3(2, 2, 0), new P3(2, 2, 0), Desig.Dig);
        f.RunesKnown = false;
        typeof(Fortress).GetMethod("LearnRunes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
        Check(f.RunesKnown && f.CanPlace("runeforge", 6, 1, 1) == null, "once found, it can be built");
        Building rf = f.Place("runeforge", 6, 1, 1); rf.Built = true;
        Item hammer = f.Spawn(ItemKind.Weapon, Tables.Mi("iron"), w.Idx(2, 3, 1));
        Item pick = f.Spawn(ItemKind.Pick, Tables.Mi("iron"), w.Idx(3, 3, 1));
        Item mail = f.Spawn(ItemKind.Armor, Tables.Mi("iron"), w.Idx(4, 3, 1));
        for (int k = 0; k < 3; k++) f.Spawn(ItemKind.RoughGem, Tables.Mi("garnet"), w.Idx(1 + k, 2, 1));
        f.AddOrder(rf, "rune_strike", 1);
        f.AddOrder(rf, "rune_ward", 1);
        f.AddOrder(rf, "rune_delve", 1);
        Run(f, () => rf.Orders.Count == 0, 9000);
        Eq(rf.Orders.Count, 0, "three runes carved");
        Eq(hammer.Rune, "striking", "the war hammer bears a rune of striking");
        Eq(mail.Rune, "warding", "the breastplate a rune of warding");
        Eq(pick.Rune, "delving", "the pick a rune of delving");
        Check(f.Items.ContainsKey(hammer.Id) && hammer.Cell >= 0 && hammer.InBuilding < 0, "and they come out of the forge as themselves");
        Eq(f.Items.Values.Count(i => i.Kind == ItemKind.RoughGem), 0, "each rune costs a gem");
        Item plain = f.Spawn(ItemKind.Weapon, Tables.Mi("iron"), 0);
        Check(Tables.WeaponMult(hammer) > Tables.WeaponMult(plain) * 1.3f, "striking hits harder");
        Item plainMail = f.Spawn(ItemKind.Armor, Tables.Mi("iron"), 0);
        Check(Tables.ArmorStop(mail, null) > Tables.ArmorStop(plainMail, null), "warding stops more");
        Check(hammer.Name.Contains("rune of striking"), $"and the name says so ({hammer.Name})");
        // The Sleeper's shrine only deep down.
        f.CavernZ = -10;
        Building high = f.Place("shrine", 9, 2, 1);
        f.Dedicate(high, "sleeper");
        Check(high.Deity != "sleeper", "the Sleeper is not worshipped near the surface");
    }
}
