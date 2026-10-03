using System.Numerics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Doodlefolk;

enum Sfx
{
    Step, Jump, Land, Whoosh, Punch, Block, Kick, Bonk,
    BounceBall, BounceSoccer, BounceBasket, BounceTennis, BounceBeach, BallKick,
    Pew, Squirt, DartHit, Boing, Clank, Whirr, Munch, Snore, Scribble, Thud,
    Pip, Chime, Grumble, Laugh, Tune, TaDa, Whistle, Swish,
    Thunder, Splat, Bark, Meow, Purr,
    Hiss, Growl, Chirp, Squawk, Whine, Spray, Yowl, Lap, Squeak, Crackle, Sneeze, Bubble, Crunch,
}

/// <summary>Dynamic sound effects, synthesized at start-up (no sound files): every effect is a few short
/// generated variations played with random pitch, panned by where it happens on screen and scaled by how hard it was.
/// Mixed with NAudio (WASAPI shared mode). Optional, with a master volume.</summary>
sealed partial class Sound : IDisposable
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

    /// <summary>One take of an effect (for mixing offline, as the trailer does). Mono, 44.1 kHz.</summary>
    public float[] Take(Sfx s, Random rng) => _bank.TryGetValue(s, out var v) && v.Length > 0 ? v[rng.Next(v.Length)] : Array.Empty<float>();
    public const int SampleRate = Rate;

    public Sound(float volume) : this(volume, true) { }

    public Sound(float volume, bool output)
    {
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2)) { ReadFully = true };
        _mixer.MixerInputEnded += (_, _) => Interlocked.Decrement(ref _voices);
        _master = new VolumeSampleProvider(_mixer) { Volume = volume };
        Build();
        if (!output) return;
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

    // ---------------- ambience ----------------

    Ambience? _amb;
    int _ambLoading;
    /// <summary>The background-sound mixer, once built (it's built the first time ambience is switched on).</summary>
    public Ambience? Ambience => _amb;

    public void EnsureAmbience()
    {
        if (_amb != null || _out == null || Interlocked.Exchange(ref _ambLoading, 1) == 1) return;
        Task.Run(() =>
        {
            try { var a = new Ambience(); _mixer.AddMixerInput(a); _amb = a; }
            catch (Exception e) { World.Log($"ambience: {e.Message}"); }
        });
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

    // ---------------- babble ----------------

    /// <summary>Gibberish speech for a bubble: one little sung syllable per few letters, vowels taken from the words
    /// (so the same words always sound alike), a rising end for questions and a lift for shouts. pitch 1 ≈ 220 Hz.</summary>
    public void Babble(string text, Vector2 at, float pitch, float vol, float speed, VoicePen pen = VoicePen.Pencil, VoiceMood mood = default)
    {
        if (!Enabled || _out == null || vol < 0.02f || _voices > 24) return;
        if (!ClassicVoices)
        {
            if (DoodleSpeech(text, pitch, speed, pen, mood) is not { } line) return;
            float pan2 = Math.Clamp(((at.X - _left) / _width) * 2 - 1, -1, 1) * 0.75f;
            Interlocked.Increment(ref _voices);
            _mixer.AddMixerInput(new Voice(line, Math.Clamp(vol, 0, 1), 1, pan2));
            return;
        }
        var letters = new System.Text.StringBuilder();
        foreach (char c in text.ToLowerInvariant()) if (c is >= 'a' and <= 'z') letters.Append(c);
        if (letters.Length == 0) return;
        string w = letters.ToString();
        int n = Math.Clamp((int)MathF.Round(w.Length / 2.6f), 1, 9);
        bool question = text.TrimEnd().EndsWith('?'), shout = text.Contains('!') || (text.Any(char.IsUpper) && !text.Any(char.IsLower));
        float f0 = 220 * pitch * (shout ? 1.12f : 1);
        float syl = 0.085f / speed, gap = 0.022f / speed;
        int total = (int)((syl + gap) * n * Rate) + 400;
        var d = new float[total];
        int hash = 17;
        foreach (char c in w) hash = hash * 31 + c;
        var r = new Random(hash);
        int pos = 0;
        for (int i = 0; i < n; i++)
        {
            string chunk = w.Substring(i * w.Length / n, Math.Max(1, (i + 1) * w.Length / n - i * w.Length / n));
            char v = chunk.FirstOrDefault(c => "aeiouy".Contains(c));
            if (v == default) v = "aeiou"[Math.Abs(chunk[0] * 7) % 5];
            (float F1, float F2) = v switch { 'a' => (800f, 1250f), 'e' => (480f, 1900f), 'i' or 'y' => (320f, 2300f), 'o' => (520f, 920f), _ => (360f, 820f) };
            bool hiss = "stkpcfxz".Contains(chunk[0]);
            float len = syl * (0.8f + (float)r.NextDouble() * 0.45f);
            float startF = f0 * (0.9f + (float)r.NextDouble() * 0.25f);
            float endF = i == n - 1 ? (question ? startF * 1.35f : startF * 0.82f) : startF * (0.95f + (float)r.NextDouble() * 0.1f);
            pos = Syllable(d, pos, len, startF, endF, F1 * (pitch > 1.15f ? 1.12f : 1), F2 * (pitch > 1.15f ? 1.1f : 1), hiss, r);
            pos += (int)(gap * Rate * (0.6f + (float)r.NextDouble() * 0.8f));
            if (pos >= total - 10) break;
        }
        // Resonances ring much louder on some vowels than others: level it out so every voice sits at the same volume.
        float peak = 0;
        for (int i = 0; i < pos && i < d.Length; i++) peak = MathF.Max(peak, MathF.Abs(d[i]));
        if (peak < 1e-4f) return;
        float k = 0.28f / peak;
        var outBuf = d.AsSpan(0, Math.Min(pos + 200, d.Length)).ToArray();
        for (int i = 0; i < outBuf.Length; i++) outBuf[i] *= k;
        float pan = Math.Clamp(((at.X - _left) / _width) * 2 - 1, -1, 1) * 0.75f;
        Interlocked.Increment(ref _voices);
        _mixer.AddMixerInput(new Voice(outBuf, Math.Clamp(vol, 0, 1), 1, pan));
    }

    /// <summary>One sung syllable: a buzzy source through two vowel resonances, with an optional breathy consonant first.</summary>
    static int Syllable(float[] d, int pos, float secs, float f0a, float f0b, float F1, float F2, bool hiss, Random r)
    {
        int n = Math.Min((int)(secs * Rate), d.Length - pos - 1);
        if (n <= 0) return pos;
        static (float a1, float a2, float g) Res(float f, float bw) { float rr = MathF.Exp(-MathF.PI * bw / Rate), th = MathF.Tau * f / Rate; return (2 * rr * MathF.Cos(th), -rr * rr, 1 - rr); }
        var (a1, a2, g1) = Res(F1, 90);
        var (b1, b2, g2) = Res(F2, 130);
        float y1 = 0, y2 = 0, z1 = 0, z2 = 0, ph = 0, lp = 0;
        int burst = hiss ? (int)(0.018f * Rate) : 0;
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n;
            float f0 = f0a + (f0b - f0a) * p;
            ph += f0 / Rate;
            if (ph >= 1) ph -= 1;
            float src = (2 * ph - 1);
            lp += (src - lp) * 0.35f;
            float y = g1 * lp * 6 + a1 * y1 + a2 * y2; y2 = y1; y1 = y;
            float z = g2 * lp * 6 + b1 * z1 + b2 * z2; z2 = z1; z1 = z;
            float env = p < 0.1f ? p / 0.1f : p > 0.7f ? (1 - p) / 0.3f : 1;
            float s = (y + z * 0.55f) * env * 0.22f;
            if (i < burst) s = s * (i / (float)burst) + ((float)r.NextDouble() * 2 - 1) * 0.08f * (1 - i / (float)burst);
            d[pos + i] += s;
        }
        return pos + n;
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
        // Snow underfoot: a short burst of tiny squeaky crackles, packed down.
        float[] Crunch(int seed)
        {
            var cr = new Random(seed);
            return Lp(Make(0.11f, (t, p) => (cr.NextDouble() < 0.18 ? ((float)cr.NextDouble() * 2 - 1) * 0.9f : ((float)cr.NextDouble() * 2 - 1) * 0.15f) * Env(p, 0.15f) * 0.6f), 0.55f);
        }
        Add(Sfx.Crunch, Crunch(1), Crunch(2), Crunch(3));
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
        // Thunder: a crack, then a long low rumble.
        Add(Sfx.Thunder, Lp(Make(3.2f, (t, p) => Rnd() * (p < 0.03f ? 1 : MathF.Pow(1 - p, 1.6f) * (0.55f + 0.45f * MathF.Sin(t * 9) * MathF.Sin(t * 2.3f))) * 0.9f), 0.025f));
        Add(Sfx.Splat, Lp(Make(0.12f, (t, p) => Rnd() * Env(p, 0.005f) * 0.5f), 0.3f));
        Add(Sfx.Bark, Make(0.18f, (t, p) => MathF.Sign(Sine(t, 260 - 90 * p)) * (Sine(t, 4) * 0.2f + 0.8f) * Env(p, 0.02f) * 0.14f + Rnd() * Env(p) * 0.05f),
                       Make(0.14f, (t, p) => MathF.Sign(Sine(t, 300 - 120 * p)) * Env(p, 0.02f) * 0.14f + Rnd() * Env(p) * 0.05f));
        Add(Sfx.Meow, Make(0.5f, (t, p) => Sine(t, 520 + 260 * MathF.Sin(MathF.PI * p)) * MathF.Sin(MathF.PI * p) * 0.16f + Sine(t, 1040 + 400 * MathF.Sin(MathF.PI * p)) * MathF.Sin(MathF.PI * p) * 0.05f));
        Add(Sfx.Purr, Lp(Make(1.2f, (t, p) => Rnd() * (0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 26)) * MathF.Sin(MathF.PI * p) * 0.25f), 0.06f));
        // Animals.
        Add(Sfx.Hiss, Make(0.55f, (t, p) => Rnd() * (p < 0.08f ? p / 0.08f : MathF.Pow(1 - p, 0.8f)) * 0.22f));
        Add(Sfx.Growl, Lp(Make(0.7f, (t, p) => (MathF.Sign(Sine(t, 85 + 10 * MathF.Sin(t * 30))) * 0.5f + Rnd() * 0.3f) * MathF.Sin(MathF.PI * p) * (0.7f + 0.3f * MathF.Sin(t * 45)) * 0.3f), 0.2f));
        Add(Sfx.Chirp, Make(0.12f, (t, p) => Sine(t, 2400 + 1600 * MathF.Sin(MathF.PI * p)) * MathF.Sin(MathF.PI * p) * 0.18f),
                       Make(0.18f, (t, p) => Sine(t, 2800 - 1200 * p + 400 * MathF.Sin(t * 120)) * MathF.Sin(MathF.PI * p) * 0.16f));
        Add(Sfx.Squawk, Make(0.35f, (t, p) => (MathF.Sign(Sine(t, 900 + 500 * MathF.Sin(t * 60))) * 0.5f + Rnd() * 0.25f) * MathF.Sin(MathF.PI * p) * 0.16f));
        Add(Sfx.Whine, Make(0.6f, (t, p) => Sine(t, 700 + 300 * MathF.Sin(MathF.PI * p) - 200 * p) * MathF.Sin(MathF.PI * p) * 0.14f));
        Add(Sfx.Spray, Lp(Make(0.18f, (t, p) => Rnd() * (p < 0.1f ? p / 0.1f : 1 - p) * 0.35f), 0.85f));
        Add(Sfx.Yowl, Make(0.8f, (t, p) => Sine(t, 450 + 250 * MathF.Sin(t * 9) + 120 * MathF.Sin(t * 31)) * MathF.Sin(MathF.PI * p) * 0.15f));
        Add(Sfx.Lap, Lp(Make(0.06f, (t, p) => Rnd() * Env(p, 0.05f) * 0.3f), 0.5f));
        Add(Sfx.Crackle, Make(0.03f, (t, p) => Rnd() * Env(p, 0.02f) * 0.5f), Make(0.05f, (t, p) => Rnd() * Env(p, 0.01f) * 0.4f * (p < 0.3f ? 1 : 0.5f)), Lp(Make(0.08f, (t, p) => Rnd() * Env(p, 0.01f) * 0.6f), 0.6f));
        Add(Sfx.Sneeze, Make(0.35f, (t, p) => (p < 0.55f ? Sine(t, 500 + 400 * p) * 0.08f * p * 2 : Rnd() * 0.4f * MathF.Pow(1 - (p - 0.55f) / 0.45f, 2))));
        Add(Sfx.Bubble, Make(0.08f, (t, p) => Sine(t, 600 + 900 * p) * Env(p, 0.05f) * 0.15f));
        Add(Sfx.Squeak, Make(0.2f, (t, p) => Sine(t, 1600 + 700 * MathF.Sin(MathF.PI * p)) * MathF.Sin(MathF.PI * p) * 0.2f));
        _rainLoop = Lp(Make(2.4f, (t, p) => Rnd() * 0.5f + (r.NextDouble() < 0.0015 ? 0.8f : 0)), 0.35f);
        // Crossfade the ends so the loop doesn't click.
        for (int i = 0; i < 2000; i++) { float k = i / 2000f; _rainLoop[i] = _rainLoop[i] * k + _rainLoop[^(2000 - i)] * (1 - k); }
        _tune = Chiptune(r);
    }

    float[] _rainLoop = Array.Empty<float>();
    LoopVoice? _rain;

    /// <summary>The patter of rain, louder the heavier it falls (0 = silent).</summary>
    public void Rain(float level)
    {
        if (_out == null) return;
        if (level > 0.01f && Enabled)
        {
            if (_rain == null) { _rain = new LoopVoice(_rainLoop); _mixer.AddMixerInput(_rain); }
            _rain.Target = 0.12f * level;
        }
        else if (_rain != null) _rain.Target = 0;
        if (_rain is { Done: true }) _rain = null;
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
