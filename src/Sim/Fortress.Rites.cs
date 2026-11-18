using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// The dead and the drawbridge: burial in coffins, grief for those left
/// lying, spirits that will not rest; bridges that rise and fall.
/// </summary>
public sealed partial class Fortress
{
    // ---- burial -------------------------------------------------------------------------

    private Task PlanBury(Dwarf d)
    {
        var bodies = Items.Values.Where(i => i.Kind == ItemKind.Corpse && i.Of >= 0 && i.Free && DwarfById(i.Of) is { Buried: false }).ToList();
        if (bodies.Count == 0) return null;
        Building coffin = Buildings.Where(b => b.Built && b.Def == "coffin" && b.Owner < 0 && b.Claim < 0)
            .OrderBy(b => new P3(b.X, b.Y, b.Z).Dist(World.Pos(d.Cell))).FirstOrDefault();
        if (coffin == null) return null;
        World.EnsureComponents();
        int comp = World.Comp[d.Cell];
        if (World.Comp[World.Idx(coffin.X, coffin.Y, coffin.Z)] != comp) return null;
        Item body = bodies.Where(b => World.Comp[b.Cell] == comp).OrderBy(b => World.Pos(b.Cell).Dist(World.Pos(d.Cell))).FirstOrDefault();
        if (body == null) return null;
        body.ReservedBy = d.Id;
        coffin.Claim = d.Id;
        return new Task { Kind = TaskKind.Bury, Item = body.Id, Building = coffin.Id };
    }

    private bool DoBury(Dwarf d, Task t)
    {
        Building coffin = BuildingById(t.Building);
        if (coffin == null || coffin.Claim != d.Id) return false;
        d.Activity = "Bearing the dead";
        int r = Fetch(d, t.Item);
        if (r < 0) return false;
        if (r == 0) return true;
        d.Carry = t.Item;
        int w = Walk(d, World.Idx(coffin.X, coffin.Y, coffin.Z));
        if (w < 0) return false;
        if (w == 0) return true;
        Item body = Items[t.Item];
        body.ReservedBy = -1;
        d.Carry = -1;
        PutIn(body, coffin);
        coffin.Claim = -1;
        coffin.Owner = body.Of;
        Dwarf dead = DwarfById(body.Of);
        t.Item = -1;
        if (dead == null) return false;
        dead.Buried = true;
        bool wasRestless = dead.Restless;
        dead.Restless = false;
        Count("buried");
        Say(wasRestless ? $"{dead.Name} is laid to rest at last, and the halls are quiet again." : $"{dead.Name} has been laid to rest.", LogKind.Info, World.Idx(coffin.X, coffin.Y, coffin.Z));
        foreach (Dwarf o in Living) if (o.Opinion.GetValueOrDefault(dead.Id) > 20 || o.Clan == dead.Clan) AddThought(o, "laid_to_rest");
        return false;
    }

    /// <summary>Once a day: grief for the dead who lie unburied, and the spirits of those left too long.</summary>
    private void Mourning()
    {
        foreach (Dwarf dead in Dwarves.Where(x => x.Dead && !x.Buried))
        {
            long lying = Tick - dead.DiedAt;
            if (lying < Tables.TicksPerDay * 2) continue;
            // Only the dead whose bodies are still about; the lost and the eaten are mourned and done.
            if (!Items.Values.Any(i => i.Of == dead.Id)) continue;
            foreach (Dwarf o in Living)
                if (o.Opinion.GetValueOrDefault(dead.Id) > 20 || o.Clan == dead.Clan) AddThought(o, "unburied");
            if (!dead.Restless && lying > Tables.TicksPerSeason)
            {
                dead.Restless = true;
                Say($"The spirit of {dead.Name} walks the halls, restless and unburied!", LogKind.Alert);
            }
            if (dead.Restless)
            {
                var living = Living.ToList();
                for (int k = 0; k < Math.Min(2, living.Count); k++)
                {
                    Dwarf victim = Rng.Pick(living);
                    if (victim.Has("brave") && Rng.Chance(0.5)) continue;
                    AddThought(victim, "haunted");
                    if (Rng.Chance(0.25)) Say($"{victim.FirstName} was terrified by the ghost of {dead.FirstName}.", LogKind.Bad, victim.Cell);
                }
            }
        }
    }

    // ---- drawbridges --------------------------------------------------------------------

    /// <summary>Raise or lower a drawbridge. Raised over a gap, it drops whatever stands on it; on a floor, it seals the cell.</summary>
    public void SetBridge(Building b, bool raised)
    {
        if (b.Def != "bridge" || !b.Built || b.Raised == raised) return;
        b.Raised = raised;
        int c = World.Idx(b.X, b.Y, b.Z);
        if (b.OverAir)
        {
            if (raised) { World.SetShape(c, Shape.Empty); World.Set(c, CellFlag.Built, false); Fall(c); }
            else { World.SetShape(c, Shape.Floor, b.Mat); World.Set(c, CellFlag.Built, true); }
        }
        else
        {
            if (raised) Shove(c);
            World.SetLocked(c, raised);
        }
        World.Changed.Add(c);
    }

    public void SetAllBridges(bool raised)
    {
        int n = 0;
        foreach (Building b in Buildings.Where(b => b.Def == "bridge" && b.Built && b.Raised != raised).ToList()) { SetBridge(b, raised); n++; }
        if (n > 0) Say(raised ? "The drawbridges are raised." : "The drawbridges are lowered.", LogKind.Info);
    }

    /// <summary>Things on a cell that has just lost its floor fall to the level below, if it has one.</summary>
    private void Fall(int c)
    {
        int below = c - World.W * World.H;
        bool soft = below >= 0 && World.Walkable(below);
        int land = soft ? below : FindStandable(World.Pos(c), 2);
        foreach (Item it in ItemsOn(c).Where(i => i.InBuilding < 0).ToList()) Drop(it, land);
        foreach (Dwarf d in Living.Where(x => x.Cell == c).ToList())
        {
            Release(d); d.Task = null;
            d.Cell = land;
            if (soft) { d.Hp -= 25; Say($"{d.FirstName} fell from the drawbridge!", LogKind.Bad, land); if (d.Hp <= 0) Kill(d, "fell from a drawbridge"); }
        }
        foreach (Creature cr in Creatures.Where(x => !x.Dead && x.Cell == c).ToList())
        {
            cr.Path = null;
            cr.Cell = land;
            cr.Hp -= soft ? 35 : 0;
            if (soft) Say($"The drawbridge pitches {A(cr.D.Name)} into the depths!", LogKind.Good, land);
            if (cr.Hp <= 0) KillCreature(cr, null, null);
        }
    }

    /// <summary>A bridge rising off a floor flings what stands there aside.</summary>
    private void Shove(int c)
    {
        int to = FindStandable(World.Pos(c), 1);
        if (to == c) return;
        foreach (Item it in ItemsOn(c).Where(i => i.InBuilding < 0).ToList()) Drop(it, to);
        foreach (Dwarf d in Living.Where(x => x.Cell == c)) { Release(d); d.Task = null; d.Cell = to; }
        foreach (Creature cr in Creatures.Where(x => !x.Dead && x.Cell == c).ToList())
        {
            cr.Path = null; cr.Cell = to; cr.Hp -= 20;
            Say($"The rising drawbridge flings {A(cr.D.Name)} aside.", LogKind.Good, to);
            if (cr.Hp <= 0) KillCreature(cr, null, null);
        }
    }
}
