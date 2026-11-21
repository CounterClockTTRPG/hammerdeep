using System.Collections.Generic;

namespace Hammerdeep;

/// <summary>
/// Floor styles: purely for the look of a hall or a corridor. A box of floor is marked with a style (Floor style,
/// S); masons lay it, a cell at a time, and the style is kept per cell in <see cref="World.Floors"/>. "Bare"
/// strips a floor back to the rock. Paving takes the grass and the shrubs with it.
/// </summary>
public partial class Fortress
{
    public static readonly string[] FloorStyles = { "Bare", "Smoothed", "Flagstones", "Planks", "Tiles", "Mosaic" };

    public static readonly string[] FloorStyleHelp =
    {
        "Strip a floor back to the bare rock or earth.",
        "The rock ground smooth and even.",
        "Slabs of the stone, laid in courses.",
        "Wooden boards, warm underfoot.",
        "A chequer of light and dark stone.",
        "Small stones set in a pattern of gold and blue.",
    };

    /// <summary>The style marked for each cell designated <see cref="Desig.Pave"/>.</summary>
    public Dictionary<int, byte> Paving = new();

    /// <summary>Marks every floor in the box with a style; returns how many cells take it.</summary>
    public int Pave(P3 a, P3 b, byte style)
    {
        int n = Designate(a, b, Desig.Pave);
        for (int z = System.Math.Min(a.Z, b.Z); z <= System.Math.Max(a.Z, b.Z); z++)
            for (int y = System.Math.Min(a.Y, b.Y); y <= System.Math.Max(a.Y, b.Y); y++)
                for (int x = System.Math.Min(a.X, b.X); x <= System.Math.Max(a.X, b.X); x++)
                    if (World.In(x, y, z) && World.Desigs[World.Idx(x, y, z)] == Desig.Pave) Paving[World.Idx(x, y, z)] = style;
        return n;
    }

    private void FinishPave(int c)
    {
        World.Floors[c] = Paving.GetValueOrDefault(c);
        Paving.Remove(c);
        World.Set(c, CellFlag.Grass, false);
        World.Set(c, CellFlag.Shrub, false);
        World.Changed.Add(c);
        Count("paved");
    }
}
