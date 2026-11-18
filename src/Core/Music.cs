using System;
using System.Collections.Generic;
using Godot;

namespace Hammerdeep;

public enum MusicMood { Title, Hall, Deep, Battle }

/// <summary>
/// The soundtrack, composed as it plays: a drone on the mode's root and
/// fifth, phrases plucked on a lute, bells in the deep, anvil taps in the
/// hall, drums when blood is spilled. No audio files: everything is written
/// sample by sample into an <see cref="AudioStreamGenerator"/>. Moods cross-fade.
/// </summary>
public partial class Music : Node
{
    public const int Rate = 22050;
    public static Music Instance { get; private set; }

    private AudioStreamPlayer _player;
    private AudioStreamGeneratorPlayback _playback;
    private MusicMood _mood = MusicMood.Title, _target = MusicMood.Title;
    private float _fade = 1;
    private readonly Random _rng = new(1234);
    private long _sample;
    private double _phaseA, _phaseB, _lfo;
    private Vector2[] _buf = Array.Empty<Vector2>();

    private struct Voice { public double Freq, Phase, Amp, Decay, Partial; public int Kind; public long Age; }
    private readonly List<Voice> _voices = new();
    private int _beat = -1, _phraseNote;

    private sealed class Spec
    {
        public double Root, Tempo, DroneAmp, PluckRate, BellRate, DrumRate, TapRate;
        public int[] Scale;
    }

    private static Spec For(MusicMood m) => m switch
    {
        MusicMood.Title => new Spec { Root = 73.42, Tempo = 64, DroneAmp = 0.11, PluckRate = 0.35, BellRate = 0.05, DrumRate = 0, TapRate = 0, Scale = new[] { 0, 2, 3, 5, 7, 9, 10 } },
        MusicMood.Hall => new Spec { Root = 73.42, Tempo = 84, DroneAmp = 0.07, PluckRate = 0.55, BellRate = 0.02, DrumRate = 0, TapRate = 0.12, Scale = new[] { 0, 2, 3, 5, 7, 9, 10 } },
        MusicMood.Deep => new Spec { Root = 55.0, Tempo = 52, DroneAmp = 0.12, PluckRate = 0.12, BellRate = 0.18, DrumRate = 0, TapRate = 0, Scale = new[] { 0, 1, 3, 5, 7, 8, 10 } },
        _ => new Spec { Root = 61.74, Tempo = 112, DroneAmp = 0.09, PluckRate = 0.75, BellRate = 0, DrumRate = 1, TapRate = 0, Scale = new[] { 0, 2, 3, 5, 7, 8, 10 } },
    };

    private Spec _spec = For(MusicMood.Title);

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        if (DisplayServer.GetName() == "headless") return;
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = 0.4f }, VolumeDb = -4 };
        AddChild(_player);
        _player.Play();
        _playback = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public void Play(MusicMood mood)
    {
        if (mood == _target) return;
        _target = mood;
    }

    /// <summary>Some seconds of a mood, offline: for the self-check and for listening (see --shots).</summary>
    public float[] Render(MusicMood mood, int seconds)
    {
        _target = _mood = mood; _spec = For(mood); _fade = 1; _voices.Clear();
        var outp = new float[seconds * Rate];
        for (int i = 0; i < outp.Length; i++) outp[i] = Sample();
        return outp;
    }

    public static byte[] Wav(float[] samples)
    {
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + bytes); w.Write("WAVEfmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8.ToArray()); w.Write(bytes);
        foreach (float f in samples) w.Write((short)(Math.Clamp(f, -1, 1) * 32000));
        return ms.ToArray();
    }

    public void SetVolume(float v, bool on) { if (_player != null) _player.VolumeDb = on ? Mathf.LinearToDb(Mathf.Max(0.001f, v)) - 4 : -80; }

    public override void _Process(double delta)
    {
        if (_playback == null) return;
        int n = _playback.GetFramesAvailable();
        if (n <= 0) return;
        if (_buf.Length < n) _buf = new Vector2[n];
        for (int i = 0; i < n; i++) { float s = Sample(); _buf[i] = new Vector2(s, s); }
        _playback.PushBuffer(n == _buf.Length ? _buf : _buf[..n]);
    }

    private double NoteFreq(int degree, int octave)
    {
        int[] sc = _spec.Scale;
        int d = ((degree % sc.Length) + sc.Length) % sc.Length;
        int oct = octave + (int)Math.Floor(degree / (double)sc.Length);
        return _spec.Root * Math.Pow(2, oct + sc[d] / 12.0);
    }

    private void Add(int kind, double freq, double amp, double decaySeconds, double partial = 2)
        => _voices.Add(new Voice { Kind = kind, Freq = freq, Amp = amp, Decay = Math.Exp(-1.0 / (decaySeconds * Rate)), Partial = partial });

    /// <summary>On each beat, maybe a note: phrases walk the scale, mostly by step.</summary>
    private void OnBeat(int beat)
    {
        Spec s = _spec;
        if (_rng.NextDouble() < s.PluckRate)
        {
            _phraseNote += _rng.Next(5) - 2;
            _phraseNote = Math.Clamp(_phraseNote, -2, 9);
            Add(0, NoteFreq(_phraseNote, 2), 0.14, 0.9);
            if (_rng.NextDouble() < 0.25) Add(0, NoteFreq(_phraseNote + 2, 2), 0.08, 0.7);
        }
        if (beat % 4 == 0 && _rng.NextDouble() < s.BellRate * 4) Add(1, NoteFreq(_rng.Next(7), 3), 0.07, 3.5, 2.76);
        if (s.DrumRate > 0)
        {
            if (beat % 4 == 0 || beat % 8 == 6) Add(2, 90, 0.5, 0.25);
            if (beat % 4 == 2) Add(3, 0, 0.12, 0.08);
            if (beat % 2 == 1) Add(0, NoteFreq(beat % 8 < 4 ? 0 : -2, 1), 0.12, 0.25);
        }
        if (s.TapRate > 0 && _rng.NextDouble() < s.TapRate) { double f = 1800 + _rng.NextDouble() * 400; Add(4, f, 0.035, 0.12, 2.41); Add(4, f, 0.03, 0.12, 2.41); }
    }

    private float Sample()
    {
        _sample++;
        // Cross-fade between moods: fade out, swap the spec, fade in.
        if (_target != _mood)
        {
            _fade = Math.Max(0, _fade - 1f / (Rate * 2));
            if (_fade == 0) { _mood = _target; _spec = For(_mood); _voices.Clear(); }
        }
        else if (_fade < 1) _fade = Math.Min(1, _fade + 1f / (Rate * 2));
        double beatLen = Rate * 60.0 / _spec.Tempo / 2;
        int beat = (int)(_sample / beatLen);
        if (beat != _beat) { _beat = beat; OnBeat(beat); }
        // The drone: root and fifth, breathing.
        _lfo += 0.08 / Rate;
        double breathe = 0.75 + 0.25 * Math.Sin(_lfo * Math.Tau);
        _phaseA += _spec.Root / Rate; _phaseB += _spec.Root * 1.5 / Rate;
        double drone = (Math.Sin(_phaseA * Math.Tau) + 0.35 * Math.Sin(_phaseA * 2 * Math.Tau) + 0.6 * Math.Sin(_phaseB * Math.Tau)) * _spec.DroneAmp * breathe;
        double v = drone;
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            Voice x = _voices[i];
            x.Phase += x.Freq / Rate;
            x.Age++;
            double attack = Math.Min(1, x.Age / (Rate * 0.004));
            double s = x.Kind switch
            {
                0 => Math.Sin(x.Phase * Math.Tau) + 0.4 * Math.Sin(x.Phase * 2 * Math.Tau) + 0.2 * Math.Sin(x.Phase * 3 * Math.Tau),
                1 => Math.Sin(x.Phase * Math.Tau) + 0.5 * Math.Sin(x.Phase * x.Partial * Math.Tau),
                2 => Math.Sin(x.Phase * Math.Tau * Math.Max(0.45, 1 - x.Age / (double)Rate * 3)),
                3 => _rng.NextDouble() * 2 - 1,
                _ => Math.Sin(x.Phase * Math.Tau) + 0.6 * Math.Sin(x.Phase * x.Partial * Math.Tau),
            };
            v += s * x.Amp * attack;
            x.Amp *= x.Decay;
            if (x.Amp < 0.0005) _voices.RemoveAt(i); else _voices[i] = x;
        }
        return (float)Math.Tanh(v * _fade * 1.2) * 0.8f;
    }
}
