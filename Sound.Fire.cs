using System.Reflection;
using System.Text.Json;
using NAudio.Wave;

namespace Doodlefolk;

/// <summary>A real fire, never on a loop you can hear. Two recordings (CC0, see docs/CREDITS.md) were cut up offline
/// into 84 single crackles and pops and a bed of roar and hiss (Sounds/). Here the bed plays from ever-changing places,
/// crossfading to a new one every few seconds, and the crackles are scattered over it at random: each a different one,
/// at its own pitch, loudness and spot in the stereo field, mostly soft with the odd loud pop. And the whole fire
/// breathes: it livens up and calms down, the crackles thicken and thin with it.</summary>
sealed class FireVoice : ISampleProvider
{
    const int Rate = 44100;
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);
    readonly float[] _bed;     // 22.05 kHz mono
    readonly float[] _grains;  // 44.1 kHz mono, back to back
    readonly (int o, int n, float lvl)[] _index;
    /// <summary>How loud (0 = out), and where it is (-1 left .. 1 right): set by the game.</summary>
    public float Target, Pan;
    float _level, _breath = 1, _breathTo = 1;
    double _a, _b;
    float _x = 0;        // crossfade, 0 = head A only
    bool _fading;
    int _untilJump = Rate * 3, _untilBreath;
    readonly List<Grain> _active = new();
    readonly Random _r = new();

    sealed class Grain { public int I; public double Pos; public float Rate, Gain, L, R; }

    FireVoice(float[] bed, float[] grains, (int, int, float)[] index)
    {
        _bed = bed; _grains = grains; _index = index;
        _a = _r.Next(_bed.Length);
    }

    /// <summary>The fire, from the sounds built into the app (null if they can't be read).</summary>
    public static FireVoice? Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            float[] Wav(string name)
            {
                using var s = asm.GetManifestResourceStream("sounds/" + name) ?? throw new FileNotFoundException(name);
                using var r = new WaveFileReader(s);
                var sp = r.ToSampleProvider();
                var all = new List<float>();
                var buf = new float[8192];
                int n;
                while ((n = sp.Read(buf.AsSpan())) > 0) for (int i = 0; i < n; i++) all.Add(buf[i]);
                return all.ToArray();
            }
            using var js = asm.GetManifestResourceStream("sounds/fire-grains.json") ?? throw new FileNotFoundException("fire-grains.json");
            var idx = JsonDocument.Parse(js).RootElement.EnumerateArray().Select(e => (e.GetProperty("o").GetInt32(), e.GetProperty("n").GetInt32(), e.GetProperty("lvl").GetSingle())).ToArray();
            return new FireVoice(Wav("fire-bed.wav"), Wav("fire-grains.wav"), idx);
        }
        catch (Exception e) { World.Log("fire sound: " + e.Message); return null; }
    }

    /// <summary>A few real crackles, for one-off fire sounds (lighting it).</summary>
    public float[][] SomeCrackles(int count) => Enumerable.Range(0, count).Select(k =>
    {
        var (o, n, lvl) = _index[(k * 37 + 11) % _index.Length];
        float g = 0.5f + lvl * 0.5f;
        return _grains.AsSpan(o, n).ToArray().Select(v => v * g * 0.6f).ToArray();
    }).ToArray();

    float Bed(double pos)
    {
        int i = (int)pos; float f = (float)(pos - i);
        float a = _bed[i % _bed.Length], b = _bed[(i + 1) % _bed.Length];
        return a + (b - a) * f;
    }

    public int Read(Span<float> buffer)
    {
        int n = buffer.Length - buffer.Length % 2;
        buffer.Clear();
        if (Target <= 0 && _level < 0.0005f && _active.Count == 0) { _level = 0; return n; }
        float pa = (Math.Clamp(Pan, -1, 1) + 1) * MathF.PI / 4, bl = MathF.Cos(pa), br = MathF.Sin(pa);
        for (int s = 0; s < n; s += 2)
        {
            _level += (Target - _level) * 0.00004f;
            // Breathing: a new liveliness every second or two, glided to.
            if (--_untilBreath <= 0) { _untilBreath = _r.Next(Rate / 2, Rate * 2); _breathTo = 0.6f + (float)_r.NextDouble() * 0.75f; }
            _breath += (_breathTo - _breath) * 0.00002f;
            // The bed: move on to somewhere else in it every few seconds, crossfading.
            if (--_untilJump <= 0 && !_fading) { _b = _r.Next(_bed.Length); _fading = true; _untilJump = _r.Next(Rate * 3, Rate * 7); }
            if (_fading) { _x += 1f / (Rate * 0.7f); if (_x >= 1) { _x = 0; _a = _b; _fading = false; } }
            float bed = _fading ? Bed(_a) * MathF.Sqrt(1 - _x) + Bed(_b) * MathF.Sqrt(_x) : Bed(_a);
            _a += 0.5; if (_a >= _bed.Length) _a -= _bed.Length;
            if (_fading) { _b += 0.5; if (_b >= _bed.Length) _b -= _bed.Length; }
            bed *= 0.5f + 0.4f * _breath;
            // Crackles: more when it's lively.
            float rate = (3f + 9f * (_breath - 0.6f) / 0.75f) / Rate;
            if (_active.Count < 14 && _r.NextDouble() < rate) Spark();
            float l = bed * bl, r = bed * br;
            for (int k = _active.Count - 1; k >= 0; k--)
            {
                var g = _active[k];
                var (o, len, _) = _index[g.I];
                int i = (int)g.Pos;
                if (i >= len - 1) { _active.RemoveAt(k); continue; }
                float f = (float)(g.Pos - i);
                float v = (_grains[o + i] + (_grains[o + i + 1] - _grains[o + i]) * f) * g.Gain;
                l += v * g.L; r += v * g.R;
                g.Pos += g.Rate;
            }
            buffer[s] = MathF.Tanh(l * _level * 1.6f) * 0.7f;
            buffer[s + 1] = MathF.Tanh(r * _level * 1.6f) * 0.7f;
        }
        return n;
    }

    void Spark()
    {
        int i = _r.Next(_index.Length);
        float u = (float)_r.NextDouble();
        float gain = (0.25f + 0.75f * u * u) * (0.6f + 0.4f * _index[i].lvl) * (_r.NextDouble() < 0.07 ? 1.7f : 0.85f);
        float pan = Math.Clamp(Pan + ((float)_r.NextDouble() - 0.5f) * 0.35f, -1, 1);
        float a = (pan + 1) * MathF.PI / 4;
        _active.Add(new Grain { I = i, Pos = 0, Rate = 0.82f + (float)_r.NextDouble() * 0.4f, Gain = gain, L = MathF.Cos(a), R = MathF.Sin(a) });
    }
}

sealed partial class Sound
{
    FireVoice? _fire;
    int _fireLoading;

    /// <summary>The fires burning: how loud (0 = none) and where across the screen. Real recorded crackle, never
    /// the same twice (FireVoice).</summary>
    public void Fire(float level, float x)
    {
        if (_out == null) return;
        if (_fire == null)
        {
            if (level <= 0 || Interlocked.Exchange(ref _fireLoading, 1) == 1) return;
            Task.Run(() =>
            {
                if (FireVoice.Load() is not { } fv) return;
                _mixer.AddMixerInput(fv);
                _fire = fv;
                // Lighting a fire (and other one-off crackles) uses real ones too.
                _bank[Sfx.Crackle] = fv.SomeCrackles(6);
            });
            return;
        }
        _fire.Target = Enabled ? level : 0;
        _fire.Pan = Math.Clamp(((x - _left) / _width) * 2 - 1, -1, 1) * 0.7f;
    }
}
