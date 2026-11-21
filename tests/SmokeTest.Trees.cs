namespace Hammerdeep;

/// <summary>Wood within reach of the wagon, even on the barest site.</summary>
public partial class SmokeTest
{
    private void TreesNear()
    {
        foreach (ulong seed in new ulong[] { 11, 4242, 90210 })
        {
            WorldGen.Result r = WorldGen.Generate(seed, land: new EmbarkLand { Forest = 0f });
            World w = r.World;
            P3 e = r.Embark;
            int n = 0;
            for (int y = System.Math.Max(0, e.Y - WorldGen.Reach); y < System.Math.Min(w.H, e.Y + WorldGen.Reach); y++)
                for (int x = System.Math.Max(0, e.X - WorldGen.Reach); x < System.Math.Min(w.W, e.X + WorldGen.Reach); x++)
                    for (int z = e.Z - 6; z <= e.Z + 6; z++)
                        if (w.In(x, y, z) && w.Shapes[w.Idx(x, y, z)] == Shape.Tree && w.IsOutside(w.Idx(x, y, z))) n++;
            Check(n >= WorldGen.MinTrees, $"a bare site (seed {seed}) still has trees near the wagon ({n})");
        }
    }
}
