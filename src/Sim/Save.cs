using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hammerdeep;

/// <summary>
/// A fortress on disk: the whole <see cref="Fortress"/> as JSON, the map's
/// arrays packed as base64 beside it, gzipped. Ironman: the game keeps one
/// file per slot and writes it every season and on quitting.
/// </summary>
public static class Save
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    private sealed class File
    {
        public int Version;
        public Fortress Fortress;
        public int W, H, D, SurfaceZ;
        public string Shapes, Mats, Flags, Desigs;
    }

    public static byte[] Write(Fortress f)
    {
        World w = f.World;
        var file = new File
        {
            Version = Version, Fortress = f, W = w.W, H = w.H, D = w.D, SurfaceZ = w.SurfaceZ,
            Shapes = Convert.ToBase64String(Array.ConvertAll(w.Shapes, s => (byte)s)),
            Mats = Convert.ToBase64String(w.Mats),
            Flags = Convert.ToBase64String(Array.ConvertAll(w.Flags, s => (byte)s)),
            Desigs = Convert.ToBase64String(Array.ConvertAll(w.Desigs, s => (byte)s)),
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(file, Options);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest)) gz.Write(json);
        return ms.ToArray();
    }

    public static Fortress Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        using var outp = new MemoryStream();
        gz.CopyTo(outp);
        var file = JsonSerializer.Deserialize<File>(outp.ToArray(), Options);
        if (file == null || file.Fortress == null) return null;
        var w = new World(file.W, file.H, file.D) { SurfaceZ = file.SurfaceZ };
        byte[] s = Convert.FromBase64String(file.Shapes), m = Convert.FromBase64String(file.Mats),
            fl = Convert.FromBase64String(file.Flags), de = Convert.FromBase64String(file.Desigs);
        for (int i = 0; i < w.N; i++) { w.Shapes[i] = (Shape)s[i]; w.Flags[i] = (CellFlag)fl[i]; w.Desigs[i] = (Desig)de[i]; }
        Array.Copy(m, w.Mats, w.N);
        Fortress f = file.Fortress;
        f.Attach(w);
        w.RebuildComponents();
        // Paths are not saved: walkers simply plan again.
        foreach (Dwarf d in f.Dwarves) { d.Path = null; d.MoveWait = 0; }
        return f;
    }
}
