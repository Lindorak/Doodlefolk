using System.Numerics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace StickFight;

enum Sfx
{
    Step, Jump, Land, Whoosh, Punch, Block, Kick, Bonk,
    BounceBall, BounceSoccer, BounceBasket, BounceTennis, BounceBeach, BallKick,
    Pew, Squirt, DartHit, Boing, Clank, Whirr, Munch, Snore, Scribble, Thud,
    Pip, Chime, Grumble, Laugh, Tune, TaDa, Whistle, Swish,
}

/// <summary>Dynamic sound effects, synthesized at start-up (no sound files): every effect is a few short
/// generated variations played with random pitch, panned by where it happens on screen and scaled by how hard it was.
/// Mixed with NAudio (WASAPI shared mode). Optional, with a master volume.</summary>
sealed class Sound : IDisposable
{
    const int Rate = 44100;
    readonly Dictionary<Sfx, float[][]> _bank = new();
    readonly Dictionary<Sfx, double> _last = new();
    readonly MixingSampleProvider _mixer;
    readonly VolumeSampleProvider _master;
    WasapiOut? _out;
    readonly Random _rng = new(7);
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    float _left, _width = 1920;
    int _voices;
    LoopVoice? _radio;

    public bool Enabled { get; set; } = true;
    public float Volume { get => _master.Volume; set => _master.Volume = Math.Clamp(value, 0, 1); }

    public Sound(float volume)
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
        _mixer.MixerInputEnded += (_, _) => Interlocked.Decrement(ref _voices);
        _master = new VolumeSampleProvider(_mixer) { Volume = volume };
        Build();
        try
        {
            _out = new WasapiOut(AudioClientShareMode.Shared, 50);
            _out.Init(_master);
            _out.Play();
        }
        catch (Exception e)
        {
            World.Log($"sound: no output device ({e.Message})");
            _out = null;
        }
    }

    /// <summary>Debug: loudest sample of each effect (to catch silent or clipping ones).</summary>
    public string Peaks() => string.Join(", ", _bank.Select(kv => $"{kv.Key}={kv.Value.Max(v => v.Max(MathF.Abs)):0.00}")) + $", tune={_tune.Max(MathF.Abs):0.00}, out={(_out != null ? "ok" : "none")}";

    public void SetScreen(float left, float width) { _left = left; _width = MathF.Max(1, width); }

    /// <summary>Play an effect at a screen position. vol 0..1 (scaled by how big the event was), pitch 1 = normal.</summary>
    public void Play(Sfx s, Vector2 at, float vol = 1, float pitch = 1, double minGap = 0.03)
    {
        if (!Enabled || _out == null || vol < 0.02f || _voices > 28) return;
        double now = _clock.Elapsed.TotalSeconds;
        if (_last.TryGetValue(s, out var t) && now - t < minGap) return;
        _last[s] = now;
        var variants = _bank[s];
        var data = variants[_rng.Next(variants.Length)];
        float pan = Math.Clamp(((at.X - _left) / _width) * 2 - 1, -1, 1) * 0.75f;
        float p = pitch * (1 + ((float)_rng.NextDouble() - 0.5f) * 0.12f);
        Interlocked.Increment(ref _voices);
        _mixer.AddMixerInput(new Voice(data, Math.Clamp(vol, 0, 1.2f), p, pan));
    }

    /// <summary>Radio music: a looping tune while any radio plays (panned to it); fades away when none do.</summary>
    public void Radio(bool playing, float x)
    {
        if (_out == null) return;
        if (playing && Enabled)
        {
            if (_radio == null) { _radio = new LoopVoice(_tune); _mixer.AddMixerInput(_radio); }
            _radio.Target = 0.22f;
            _radio.Pan = Math.Clamp(((x - _left) / _width) * 2 - 1, -1, 1) * 0.7f;
        }
        else if (_radio != null) _radio.Target = 0;
        if (_radio is { Done: true }) _radio = null;
    }

    public void Dispose()
    {
        try { _out?.Stop(); _out?.Dispose(); } catch (Exception) { }
        _out = null;
    }

    // ---------------- voices ----------------

    /// <summary>A one-shot mono sample played at a pitch, panned to stereo.</summary>
    sealed class Voice : ISampleProvider
    {
        readonly float[] _d;
        readonly float _gain, _step, _l, _r;
        double _pos;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

        public Voice(float[] d, float gain, float step, float pan)
        {
            _d = d; _gain = gain; _step = step;
            float a = (pan + 1) * MathF.PI / 4;
            _l = MathF.Cos(a) * 1.4f; _r = MathF.Sin(a) * 1.4f;
        }

        public int Read(Span<float> buffer)
        {
            int n = 0;
            while (n + 1 < buffer.Length)
            {
                int i = (int)_pos;
                if (i >= _d.Length - 1) break;
                float f = (float)(_pos - i);
                float v = (_d[i] + (_d[i + 1] - _d[i]) * f) * _gain;
                buffer[n++] = v * _l;
                buffer[n++] = v * _r;
                _pos += _step;
            }
            return n;
        }
    }

    /// <summary>Looping music with a smooth volume ramp; ends itself once faded out.</summary>
    sealed class LoopVoice : ISampleProvider
    {
        readonly float[] _d;
        int _pos;
        float _vol;
        public float Target, Pan;
        public bool Done;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
        public LoopVoice(float[] d) { _d = d; }

        public int Read(Span<float> buffer)
        {
            float a = (Pan + 1) * MathF.PI / 4, l = MathF.Cos(a) * 1.4f, r = MathF.Sin(a) * 1.4f;
            for (int n = 0; n + 1 < buffer.Length; n += 2)
            {
                _vol += (Target - _vol) * 0.0004f;
                float v = _d[_pos] * _vol;
                buffer[n] = v * l; buffer[n + 1] = v * r;
                if (++_pos >= _d.Length) _pos = 0;
            }
            if (Target <= 0 && _vol < 0.001f) { Done = true; return 0; }
            return buffer.Length - buffer.Length % 2;
        }
    }

    // ---------------- synthesis ----------------

    float[] _tune = Array.Empty<float>();

    void Build()
    {
        var r = new Random(11);
        float Rnd() => (float)r.NextDouble() * 2 - 1;
        float[] Make(float secs, Func<float, float, float> f)   // f(t, progress 0..1) → sample
        {
            int n = (int)(secs * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = f(i / (float)Rate, i / (float)n);
            return d;
        }
        float Env(float p, float attack = 0.01f) => p < attack ? p / attack : MathF.Pow(1 - (p - attack) / (1 - attack), 2.2f);
        // A one-pole low-pass applied in place (for thuds and steps).
        float[] Lp(float[] d, float k) { float y = 0; for (int i = 0; i < d.Length; i++) { y += (d[i] - y) * k; d[i] = y; } return d; }
        float[] Noise(float secs, float k, float amp, float attack = 0.005f) => Lp(Make(secs, (t, p) => Rnd() * Env(p, attack) * amp), k);
        float Sine(float t, float f) => MathF.Sin(MathF.Tau * f * t);
        void Add(Sfx s, params float[][] v) => _bank[s] = v;

        Add(Sfx.Step, Noise(0.05f, 0.18f, 0.5f), Noise(0.045f, 0.22f, 0.45f), Noise(0.055f, 0.15f, 0.5f));
        Add(Sfx.Jump, Make(0.12f, (t, p) => Rnd() * Env(p, 0.3f) * 0.18f + Sine(t, 300 + 600 * p) * Env(p) * 0.1f));
        Add(Sfx.Land, Lp(Make(0.14f, (t, p) => (Rnd() * 0.6f + Sine(t, 70) * 0.6f) * Env(p)), 0.12f));
        Add(Sfx.Thud, Lp(Make(0.2f, (t, p) => (Rnd() * 0.5f + Sine(t, 55 - 20 * p) * 0.8f) * Env(p)), 0.1f));
        Add(Sfx.Whoosh, Make(0.22f, (t, p) => { float e = MathF.Sin(MathF.PI * p); return Rnd() * e * e * 0.28f; }), Make(0.18f, (t, p) => { float e = MathF.Sin(MathF.PI * p); return Rnd() * e * e * 0.25f; }));
        Add(Sfx.Punch, Lp(Make(0.11f, (t, p) => (Rnd() * 0.6f + Sine(t, 120 - 60 * p) * 0.55f) * Env(p, 0.003f)), 0.35f), Lp(Make(0.1f, (t, p) => (Rnd() * 0.65f + Sine(t, 150 - 80 * p) * 0.5f) * Env(p, 0.003f)), 0.4f));
        Add(Sfx.Block, Make(0.07f, (t, p) => (Sine(t, 900) * 0.4f + Rnd() * 0.2f) * Env(p, 0.002f)));
        Add(Sfx.Kick, Lp(Make(0.13f, (t, p) => (Rnd() * 0.4f + Sine(t, 90 - 40 * p) * 0.7f) * Env(p, 0.003f)), 0.25f));
        Add(Sfx.Bonk, Make(0.18f, (t, p) => (Sine(t, 520 - 250 * p) * 0.6f + Sine(t, 1040 - 500 * p) * 0.2f) * Env(p, 0.002f)));
        Add(Sfx.BounceBall, Make(0.16f, (t, p) => Sine(t, 260 + 120 * MathF.Exp(-p * 6)) * Env(p, 0.003f) * 0.5f));
        Add(Sfx.BounceSoccer, Lp(Make(0.12f, (t, p) => (Sine(t, 110) * 0.7f + Rnd() * 0.4f) * Env(p, 0.002f)), 0.3f));
        Add(Sfx.BounceBasket, Make(0.26f, (t, p) => (Sine(t, 95) * 0.6f + Sine(t, 190) * 0.25f + Sine(t, 300) * 0.1f) * Env(p, 0.002f)));
        Add(Sfx.BounceTennis, Make(0.06f, (t, p) => (Sine(t, 700) * 0.4f + Rnd() * 0.3f) * Env(p, 0.002f)));
        Add(Sfx.BounceBeach, Lp(Make(0.2f, (t, p) => (Sine(t, 140 - 40 * p) * 0.6f + Rnd() * 0.15f) * Env(p, 0.01f)), 0.2f));
        Add(Sfx.BallKick, Lp(Make(0.1f, (t, p) => (Sine(t, 160 - 60 * p) * 0.8f + Rnd() * 0.5f) * Env(p, 0.002f)), 0.35f));
        Add(Sfx.Pew, Make(0.16f, (t, p) => MathF.Sign(Sine(t, 1600 * MathF.Exp(-p * 2.4f))) * Env(p, 0.002f) * 0.16f));
        Add(Sfx.Squirt, Lp(Make(0.12f, (t, p) => Rnd() * Env(p, 0.02f) * 0.35f), 0.6f));
        Add(Sfx.DartHit, Make(0.04f, (t, p) => Sine(t, 1200) * Env(p, 0.002f) * 0.3f));
        Add(Sfx.Boing, Make(0.45f, (t, p) => Sine(t, 180 + 90 * MathF.Sin(t * 70) * (1 - p)) * Env(p, 0.005f) * 0.45f));
        Add(Sfx.Clank, Make(0.25f, (t, p) => (Sine(t, 820) * 0.3f + Sine(t, 1310) * 0.25f + Sine(t, 2170) * 0.12f) * Env(p, 0.002f)));
        Add(Sfx.Whirr, Make(0.18f, (t, p) => Rnd() * (0.5f + 0.5f * MathF.Sin(t * 120)) * MathF.Sin(MathF.PI * p) * 0.2f));
        Add(Sfx.Munch, Lp(Make(0.16f, (t, p) => Rnd() * (p < 0.4f ? Env(p / 0.4f) : Env((p - 0.4f) / 0.6f)) * 0.45f), 0.5f));
        Add(Sfx.Snore, Lp(Make(1.1f, (t, p) => (Rnd() * 0.5f + Sine(t, 60) * 0.4f) * MathF.Sin(MathF.PI * p) * 0.35f), 0.08f));
        Add(Sfx.Scribble, Make(0.5f, (t, p) => Rnd() * (0.5f + 0.5f * MathF.Sin(t * 60)) * MathF.Sin(MathF.PI * p) * 0.12f));
        Add(Sfx.Pip, Make(0.07f, (t, p) => Sine(t, 1300) * Env(p, 0.003f) * 0.25f));
        Add(Sfx.Chime, Make(0.4f, (t, p) => (Sine(t, 1047) + Sine(t, 1319) * 0.7f) * Env(p, 0.003f) * 0.18f));
        Add(Sfx.Grumble, Make(0.22f, (t, p) => MathF.Sign(Sine(t, 110 + 30 * MathF.Sin(t * 40))) * Env(p, 0.02f) * 0.1f));
        Add(Sfx.Laugh, Make(0.3f, (t, p) => Sine(t, p < 0.5f ? 620 : 520) * MathF.Abs(MathF.Sin(MathF.PI * 2 * p)) * 0.22f));
        Add(Sfx.Tune, Make(0.36f, (t, p) => Sine(t, p < 0.33f ? 784 : p < 0.66f ? 988 : 1175) * Env((p * 3) % 1, 0.02f) * 0.18f));
        Add(Sfx.TaDa, Make(0.8f, (t, p) => (Sine(t, p < 0.2f ? 523 : p < 0.4f ? 659 : 784) * 0.6f + Sine(t, p < 0.4f ? 1047 : 1568) * 0.15f) * Env(p, 0.01f) * 0.3f));
        Add(Sfx.Whistle, Make(0.35f, (t, p) => Sine(t, 2300 + 120 * MathF.Sin(t * 180)) * MathF.Sin(MathF.PI * p) * 0.18f));
        Add(Sfx.Swish, Lp(Make(0.25f, (t, p) => Rnd() * MathF.Sin(MathF.PI * p) * 0.3f), 0.7f));
        _tune = Chiptune(r);
    }

    /// <summary>A short, happy, generated loop for radios: pentatonic melody over a bass line.</summary>
    static float[] Chiptune(Random r)
    {
        const float bpm = 118;
        float beat = 60 / bpm;
        int[] scale = { 0, 2, 4, 7, 9, 12, 14, 16 };
        int[] chords = { 0, 5, 7, 3 };   // I IV V vi-ish (semitone roots relative to the key)
        int bars = 4, steps = bars * 8;
        int n = (int)(steps * beat / 2 * Rate);
        var d = new float[n];
        int[] mel = new int[steps];
        int cur = 4;
        for (int i = 0; i < steps; i++) { cur = Math.Clamp(cur + r.Next(-2, 3), 0, scale.Length - 1); mel[i] = r.NextDouble() < 0.18 ? -1 : scale[cur]; }
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            int step = (int)(t / (beat / 2)) % steps;
            float sp = t / (beat / 2) - (int)(t / (beat / 2));
            int bar = step / 8;
            float root = 220 * MathF.Pow(2, (chords[bar % 4] - 12) / 12f);
            float bass = MathF.Sign(MathF.Sin(MathF.Tau * root * t)) * 0.08f * (1 - sp * 0.6f);
            float lead = 0;
            if (mel[step] >= 0)
            {
                float f = 440 * MathF.Pow(2, (mel[step] + chords[bar % 4]) / 12f);
                lead = (MathF.Sin(MathF.Tau * f * t) * 0.7f + MathF.Sign(MathF.Sin(MathF.Tau * f * t)) * 0.12f) * MathF.Max(0, 1 - sp * 1.3f) * 0.16f;
            }
            float hat = step % 2 == 1 && sp < 0.08f ? ((float)r.NextDouble() * 2 - 1) * 0.05f : 0;
            d[i] = bass + lead + hat;
        }
        return d;
    }
}
