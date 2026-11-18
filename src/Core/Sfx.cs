using System;
using System.Collections.Generic;
using Godot;

namespace Hammerdeep;

/// <summary>
/// Every sound is synthesised at start-up (no audio files): a pick on stone,
/// an axe in wood, a soft click, a placing thud, a refusal, and the bell for
/// alerts. A small pool of players; calls are safe when there is no audio.
/// </summary>
public partial class Sfx : Node
{
    public static Sfx Instance;
    private const int Rate = 22050;
    private readonly Dictionary<string, AudioStreamWav> _sounds = new();
    private readonly List<AudioStreamPlayer> _pool = new();
    private readonly Dictionary<string, ulong> _last = new();

    public override void _Ready()
    {
        Instance = this;
        if (DisplayServer.GetName() == "headless") return;
        _sounds["click"] = Make(0.04f, (t, i) => Env(t, 0.04f, 0.002f) * Sq(t * 1800) * 0.25f);
        _sounds["place"] = Make(0.12f, (t, i) => Env(t, 0.12f, 0.004f) * (MathF.Sin(t * MathF.Tau * 120 * (1 - t * 3)) * 0.6f + Rnd(i) * 0.15f));
        _sounds["deny"] = Make(0.18f, (t, i) => Env(t, 0.18f, 0.005f) * Sq(t * (t < 0.08f ? 220 : 165)) * 0.18f);
        _sounds["pick"] = Make(0.16f, (t, i) => Env(t, 0.16f, 0.001f) * (MathF.Sin(t * MathF.Tau * 2300) * 0.35f + MathF.Sin(t * MathF.Tau * 3410) * 0.2f + Rnd(i) * 0.3f * MathF.Exp(-t * 60)));
        _sounds["chop"] = Make(0.2f, (t, i) => Env(t, 0.2f, 0.002f) * (MathF.Sin(t * MathF.Tau * 180) * 0.5f + Rnd(i) * 0.35f * MathF.Exp(-t * 30)));
        _sounds["alert"] = Make(1.4f, (t, i) => Env(t, 1.4f, 0.005f) * (MathF.Sin(t * MathF.Tau * 523) * 0.35f + MathF.Sin(t * MathF.Tau * 784) * 0.2f + MathF.Sin(t * MathF.Tau * 1046) * 0.1f));
        _sounds["good"] = Make(0.6f, (t, i) => Env(t, 0.6f, 0.005f) * MathF.Sin(t * MathF.Tau * (t < 0.15f ? 660 : 880)) * 0.3f);
        for (int k = 0; k < 8; k++) { var p = new AudioStreamPlayer(); AddChild(p); _pool.Add(p); }
    }

    private static float Env(float t, float len, float attack) => t < attack ? t / attack : MathF.Exp(-(t - attack) / (len * 0.3f));
    private static float Sq(float phase) => (phase % 1) < 0.5f ? 1 : -1;
    private static uint _r = 12345;
    private static float Rnd(int i) { _r = _r * 1664525 + 1013904223; return ((_r >> 9) & 0xFFFF) / 32768f - 1; }

    private static AudioStreamWav Make(float seconds, Func<float, int, float> f)
    {
        int n = (int)(seconds * Rate);
        byte[] data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            short s = (short)(Math.Clamp(f(i / (float)Rate, i), -1, 1) * 30000);
            data[i * 2] = (byte)s; data[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    public void Play(string name, float volume = 1, float pitch = 1, int minGapMs = 0)
    {
        if (!_sounds.TryGetValue(name, out AudioStreamWav s)) return;
        ulong now = Time.GetTicksMsec();
        if (minGapMs > 0 && _last.TryGetValue(name, out ulong last) && now - last < (ulong)minGapMs) return;
        _last[name] = now;
        foreach (AudioStreamPlayer p in _pool)
        {
            if (p.Playing) continue;
            p.Stream = s;
            p.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, volume));
            p.PitchScale = pitch;
            p.Play();
            return;
        }
    }

    public static void Click() => Instance?.Play("click", 0.5f);
    public static void Place() => Instance?.Play("place", 0.7f);
    public static void Deny() => Instance?.Play("deny", 0.6f);
    public static void Alert() => Instance?.Play("alert", 0.6f);
    public static void Good() => Instance?.Play("good", 0.5f, 1, 400);
    public static void Pick(float v = 0.3f) => Instance?.Play("pick", v, 0.9f + (float)GD.RandRange(0, 0.25), 90);
    public static void Chop(float v = 0.4f) => Instance?.Play("chop", v, 0.9f + (float)GD.RandRange(0, 0.2), 150);
}
