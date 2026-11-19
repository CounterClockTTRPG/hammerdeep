using System;
using System.Collections.Generic;
using System.Linq;

namespace Hammerdeep;

/// <summary>One goal of the guided first fortress: what to do, why, how, and how to tell it is done.</summary>
public sealed class TutorialStep
{
    public string Title, Text, How;
    /// <summary>The toolbar tool to make glow, if any.</summary>
    public Tool? Glow;
    /// <summary>Other buttons to make glow, by label (e.g. "Labors").</summary>
    public string GlowButton;
    /// <summary>Done when this holds. Null: the player reads it and presses Next.</summary>
    public Func<Fortress, TutorialWatch, bool> Done;
}

/// <summary>What the screen tells the tutorial that the fortress cannot: the view and the clock.</summary>
public sealed class TutorialWatch
{
    public int StartZ, Z;
    public bool SpeedChanged, Panned, Zoomed;
}

/// <summary>
/// The guided first fortress: a short list of goals, from looking around to
/// a squad and a trade depot, each checked against the fortress itself so
/// the tutorial moves on as soon as the thing is really done.
/// </summary>
public static class Tutorial
{
    private static int Below(Fortress f) => f.World.SurfaceZ - 1;

    private static bool AnyDesig(Fortress f, Func<int, Desig, bool> pred) => f.Designated.Any(c => pred(c, f.World.Desigs[c]));

    private static bool AnyShape(Fortress f, int z, Func<Shape, bool> pred)
    {
        World w = f.World;
        int plane = w.W * w.H, start = z * plane;
        if (z < 0 || z >= w.D) return false;
        for (int i = start; i < start + plane; i++) if (pred(w.Shapes[i]) && !w.IsOutside(i)) return true;
        return false;
    }

    /// <summary>A way down: a down stair on the surface with an up stair (or up/down) under it, dug or designated.</summary>
    public static bool WayDown(Fortress f)
    {
        World w = f.World;
        int plane = w.W * w.H, z = f.World.SurfaceZ;
        for (int i = z * plane; i < (z + 1) * plane; i++)
        {
            bool top = w.Shapes[i] is Shape.StairDown or Shape.StairUD || w.Desigs[i] is Desig.StairDown or Desig.StairUD;
            if (!top) continue;
            int b = i - plane;
            if (w.Shapes[b] is Shape.StairUp or Shape.StairUD || w.Desigs[b] is Desig.StairUp or Desig.StairUD) return true;
        }
        return false;
    }

    private static int DugBelow(Fortress f)
    {
        World w = f.World;
        int plane = w.W * w.H, n = 0;
        for (int z = Math.Max(0, f.World.SurfaceZ - 6); z < f.World.SurfaceZ; z++)
            for (int i = z * plane; i < (z + 1) * plane; i++)
                if (!w.IsOutside(i) && (w.Desigs[i] == Desig.Dig || (w.Shapes[i] == Shape.Floor && !w.Has(i, CellFlag.Hidden) && w.Depth[i] == 0 && f.World.Comp[i] != 0))) n++;
        return n;
    }

    public static readonly List<TutorialStep> Steps = new()
    {
        new TutorialStep
        {
            Title = "Welcome to your hold",
            Text = "Seven dwarves have stopped their wagon here. You don't command them one by one: you say what should be done, and they choose their own tasks. The game is paused -- take your time.",
            How = "Arrow keys or middle-drag pan the map; the mouse wheel zooms. Have a look around, then press Next.",
        },
        new TutorialStep
        {
            Title = "Up and down",
            Text = "The world has depth. You see one level at a time; open air shows the levels below, dimmer the deeper they are.",
            How = "Use the arrow buttons beside the level's name at the top right (or > and <) to look one level down and come back.",
            Done = (f, w) => w.Z != w.StartZ,
        },
        new TutorialStep
        {
            Title = "Time",
            Text = "The clock is stopped. You can give orders paused or running; alerts stop it for you.",
            How = "Space pauses and resumes. 1, 2 and 3 set normal, fast and fastest speed. Start the clock now.",
            Done = (f, w) => w.SpeedChanged,
        },
        new TutorialStep
        {
            Title = "A stockpile",
            Text = "The wagon's load lies on the grass. Haulers will carry it to a stockpile -- give them one, and a big one: logs and stone take room.",
            How = "Press P (Stockpile), then drag a box on the grass near the wagon -- 8 by 8 or more: the wagon's load and the first logs fill a small one fast.",
            Glow = Tool.Stockpile,
            Done = (f, w) => f.Zones.Any(z => z.Kind == ZoneKind.Stockpile && z.Area >= 25),
        },
        new TutorialStep
        {
            Title = "Wood and wild fruit",
            Text = "Logs make beds, barrels and buildings; the wagon brought only a few. Wild brambles give fruit to eat and to brew, and seed.",
            How = "Press T (Fell trees) and drag over a stand of trees; then G (Gather plants) and drag over some red-berried shrubs.",
            Glow = Tool.Chop,
            Done = (f, w) => f.Designated.Count(c => f.World.Desigs[c] == Desig.Chop) + f.Stats.GetValueOrDefault("trees") >= 5,
        },
        new TutorialStep
        {
            Title = "Food",
            Text = "The rations will not last. Farms grow crops: dimcaps underground on soil, wheat and brambles under the sky.",
            How = "Press F (Farm plot) and drag over soil -- grass outside works for wheat. Click the plot to choose the crop.",
            Glow = Tool.Farm,
            Done = (f, w) => f.Zones.Any(z => z.Kind == ZoneKind.Farm),
        },
        new TutorialStep
        {
            Title = "Ale",
            Text = "Dwarves without ale are miserable dwarves. A brewery turns a plant into five ale, and gives back a seed.",
            How = "Press B (Build) and choose Brewery, place it on clear ground, then click it and press the repeat button (∞) beside Brew ale. Brambles and the farm's wheat will feed it.",
            Glow = Tool.Build,
            Done = (f, w) => f.Buildings.Any(b => b.Def == "brewery" && b.Orders.Any(o => o.Recipe == "ale")),
        },
        new TutorialStep
        {
            Title = "A way down",
            Text = "Dwarves live underground. A staircase needs both ends: a down stair on the surface, and an up stair (or up/down stairs) on the level beneath it, in the same spot.",
            How = "Press J and click one cell of open ground. Then press > to go a level down, press U (or X to keep going deeper) and click the very same cell.",
            Glow = Tool.StairDown,
            Done = (f, w) => WayDown(f),
        },
        new TutorialStep
        {
            Title = "A great hall",
            Text = "Now dig out a room beside the bottom of the stair. Miners need a pick -- the wagon brought three.",
            How = "On the level below the surface, press D (Dig) and drag a box of about 10 by 6 that touches the stair.",
            Glow = Tool.Dig,
            Done = (f, w) => DugBelow(f) >= 30,
        },
        new TutorialStep
        {
            Title = "A carpenter",
            Text = "Furniture is made in workshops, then placed. Start with a carpenter's shop: logs into beds, tables and chairs.",
            How = "Press B (Build) and choose Carpenter's shop, then click a clear 3 by 3 floor -- in the hall, or on the surface. (A red outline means it does not fit there.)",
            Glow = Tool.Build,
            Done = (f, w) => f.Buildings.Any(b => b.Def == "carpenter"),
        },
        new TutorialStep
        {
            Title = "Beds",
            Text = "Dwarves who sleep on the floor grumble. Order beds at the carpenter's -- a worker will come when the shop is built.",
            How = "Press Esc to put the build tool down, click the carpenter's shop, and press +5 beside Bed. (Orders can be queued before the shop is finished.)",
            Done = (f, w) => f.Buildings.Any(b => b.Def == "carpenter" && b.Orders.Any(o => o.Recipe == "bed")) || f.Items.Values.Any(i => i.Kind == ItemKind.Bed) || f.Buildings.Any(b => b.Def == "bed"),
        },
        new TutorialStep
        {
            Title = "Bedrooms",
            Text = "A finished bed sits in the stockpile until you place it. Each dwarf claims a bed and sleeps better in it.",
            How = "Let the clock run: the log says when the beds are made. Then press B (Build), choose Bed, and click a floor cell underground.",
            Glow = Tool.Build,
            Done = (f, w) => f.Buildings.Any(b => b.Def == "bed"),
        },
        new TutorialStep
        {
            Title = "Trade",
            Text = "Caravans come in Highsun and Leaffall -- but only to a trade depot. Without one they turn back.",
            How = "Press B (Build), choose Trade depot, and place it on the surface near the wagon.",
            Glow = Tool.Build,
            Done = (f, w) => f.Buildings.Any(b => b.Def == "depot"),
        },
        new TutorialStep
        {
            Title = "A meeting hall",
            Text = "Idle dwarves gather in the meeting hall to talk; friendships (and feuds) begin there, and it is where everyone shelters when the alarm sounds.",
            How = "Press M (Meeting hall) and drag a box in your great hall.",
            Glow = Tool.Meeting,
            Done = (f, w) => f.Zones.Any(z => z.Kind == ZoneKind.Meeting),
        },
        new TutorialStep
        {
            Title = "Defence",
            Text = "From the first autumn, grimlings raid. A squad arms itself from the stores and holds a place for you.",
            How = "Press K (Military), raise a new squad, and enlist two or three dwarves. Order them to Defend here with the camera on the stair.",
            GlowButton = "Military",
            Done = (f, w) => f.Squads.Any(s => s.Members.Count >= 2),
        },
        new TutorialStep
        {
            Title = "On your own",
            Text = "That is the heart of it. Next: a smelter and forge for picks and weapons, a kitchen, coffins for the dead, shrines for the gods, a well. Keep stockpiles roomy, keep the ale flowing, and dig carefully near water and magma.",
            How = "? opens the help at any time; W is the chronicle, O the world. Good luck.",
        },
    };

    public static TutorialStep Current(Fortress f) => f.TutorialStep >= 0 && f.TutorialStep < Steps.Count ? Steps[f.TutorialStep] : null;

    /// <summary>Move on past every goal that is already done. Returns true if the step changed.</summary>
    public static bool Advance(Fortress f, TutorialWatch w)
    {
        bool moved = false;
        while (Current(f) is TutorialStep s && s.Done != null && s.Done(f, w))
        {
            f.TutorialStep++;
            moved = true;
        }
        return moved;
    }

    public static void Next(Fortress f) { if (f.TutorialStep >= 0) f.TutorialStep++; if (f.TutorialStep >= Steps.Count) f.TutorialStep = -1; }
    public static void End(Fortress f) => f.TutorialStep = -1;
}
