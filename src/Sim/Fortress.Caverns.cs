using System;
using System.Linq;

namespace Hammerdeep;

/// <summary>
/// The layers under the hold: the upper caverns, the Glimmer Deep (glimmerstone, glimmercap, gloomhound packs)
/// and the Ember Halls (starsilver near the magma, emberlings, and an ancient ruin: a sealed marble vault where
/// the Warden sleeps beside a treasure of the first holds). Each is announced when it is first broken into,
/// and after that its creatures come up with the seasons.
/// </summary>
public partial class Fortress
{
    /// <summary>The layers' levels, top down; a bit for each one broken into.</summary>
    public int[] Caverns = Array.Empty<int>();
    public int CavernsFound;
    public P3 Ruin = new(-1, -1, -1);

    public static readonly string[] CavernNames = { "a cavern", "the Glimmer Deep", "the Ember Halls" };

    /// <summary>Which layer a level is in (-1: none).</summary>
    public int LayerAt(int z)
    {
        for (int k = 0; k < Caverns.Length; k++) if (Math.Abs(z - Caverns[k]) <= 2) return k;
        return -1;
    }

    public bool LayerFound(int k) => (CavernsFound & (1 << k)) != 0;

    /// <summary>Part of the ancient vault (its walls, roof and floor): the views draw it as worked stone.</summary>
    public bool InRuin(int x, int y, int z) => Ruin.X >= 0 && Math.Abs(x - Ruin.X) <= 4 && Math.Abs(y - Ruin.Y) <= 3 && z >= Ruin.Z - 1 && z <= Ruin.Z + 2;

    /// <summary>A layer opened by digging: announced, with the first of its creatures.</summary>
    internal void BrokeInto(int layer, int cell)
    {
        CavernsFound |= 1 << layer;
        if (layer == 0) CavernFound = true;
        Count("layers_found");
        switch (layer)
        {
            case 0:
                Say("You have broken into a cavern! Strange things grow here in the dark -- and something stirs.", LogKind.Alert, cell);
                for (int k = 0; k < 2; k++) { int at = CavernCell(0); if (at >= 0) SpawnCreature("crawler", at); }
                LearnRunes();
                break;
            case 1:
                Say("You have broken into the Glimmer Deep! The walls glitter with glimmerstone, glimmercaps glow in the dark -- and something howls.", LogKind.Alert, cell);
                Pack(1, "gloomhound", 3);
                break;
            default:
                Say($"You have broken into the Ember Halls! The rock is warm here, starsilver gleams in the walls{(Ruin.X >= 0 ? ", and somewhere a sealed vault of worked marble waits" : "")} -- and things crackle in the heat.", LogKind.Alert, cell);
                Pack(2, "emberling", 2);
                break;
        }
    }

    private void Pack(int layer, string def, int n)
    {
        int at = CavernCell(layer);
        if (at < 0) return;
        for (int k = 0; k < n; k++) { int c = FindStandable(World.Pos(at), 2); if (c >= 0) SpawnCreature(def, c); }
    }

    /// <summary>Each season: the deep layers found send up their own.</summary>
    private void DeepDangers()
    {
        if (LayerFound(1)) Pack(1, "gloomhound", Rng.Range(2, 4) + Year / 3);
        if (LayerFound(2)) Pack(2, "emberling", Rng.Range(1, 3) + Year / 3);
    }

    /// <summary>The Warden and the treasure it keeps, put in the ruin when the hold is founded.</summary>
    private void FurnishRuin()
    {
        if (Ruin.X < 0) return;
        int at = World.Idx(Ruin);
        Creature w = SpawnCreature("warden", at);
        w.Legend = true;
        w.Lair = at;
        Item t = Spawn(ItemKind.Weapon, Tables.Mi("starsilver"), FindStandable(new P3(Ruin.X + 2, Ruin.Y, Ruin.Z), 1), 5);
        t.Artifact = true;
        t.Label = Rng.Pick(new[] { "Starfall", "Firstlight", "the Kingmaker", "Oathkeeper" });
        t.Description = $"{t.Label} is a starsilver war hammer of the first holds, older than any tale of them. Runes run along its haft that no one living can read.";
        Artifacts.Add(t.Id);
    }

    /// <summary>The Warden wakes when someone comes into its vault.</summary>
    private void Wakes(Creature c, Dwarf by)
    {
        c.Angry = true;
        Say($"The Warden wakes! Stone grinds on stone as it rises to guard the vault against {by.FirstName}.", LogKind.Alert, c.Cell);
    }
}
