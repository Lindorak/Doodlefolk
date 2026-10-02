using NAudio.Wave;

namespace Doodlefolk;

enum AmbienceChannel { Rain, Wind, Pond, Birds, Crickets, Chatter, Fire, LoFi }

/// <summary>Background sound (an idea from Spirit City and Ropuka): rain on the windows, wind, the pond lapping,
/// birdsong by day, crickets on warm nights, the murmur of the town talking, a crackling campfire, and a lo-fi beat
/// to work to. All synthesised at start-up as seamless loops (no sound files). Each channel follows what's going on
/// in the town (it rains: you hear rain) and has its own level; the lo-fi only plays when you ask, or during focus.</summary>
sealed class Ambience : ISampleProvider
{
    const int Rate = 44100;
    public static readonly AmbienceChannel[] Channels = Enum.GetValues<AmbienceChannel>();
    readonly float[][] _loops = new float[Channels.Length][];   // interleaved stereo
    readonly int[] _pos = new int[Channels.Length];
    readonly float[] _gain = new float[Channels.Length];
    /// <summary>Where each channel is heading (0..1); the mixer glides there over a second or two.</summary>
    public readonly float[] Target = new float[Channels.Length];
    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

    public Ambience()
    {
        var r = new Random(11);
        _loops[(int)AmbienceChannel.Rain] = Rain(r, 8);
        _loops[(int)AmbienceChannel.Wind] = Wind(r, 10);
        _loops[(int)AmbienceChannel.Pond] = Pond(r, 8);
        _loops[(int)AmbienceChannel.Birds] = Birds(r, 12);
        _loops[(int)AmbienceChannel.Crickets] = Crickets(r, 6);
        _loops[(int)AmbienceChannel.Chatter] = Chatter(r, 9);
        _loops[(int)AmbienceChannel.Fire] = Fire(r, 7);
        _loops[(int)AmbienceChannel.LoFi] = LoFi(r);
        for (int c = 0; c < Channels.Length; c++) _pos[c] = r.Next(_loops[c].Length / 2) * 2;
    }

    public int Read(Span<float> buffer)
    {
        buffer.Clear();
        int n = buffer.Length - buffer.Length % 2;
        for (int c = 0; c < Channels.Length; c++)
        {
            float g = _gain[c], target = Target[c];
            if (g < 0.0005f && target <= 0) { _gain[c] = 0; continue; }
            var loop = _loops[c];
            int p = _pos[c];
            for (int i = 0; i < n; i += 2)
            {
                g += (target - g) * 0.00003f;
                buffer[i] += loop[p] * g;
                buffer[i + 1] += loop[p + 1] * g;
                p += 2; if (p >= loop.Length) p = 0;
            }
            _gain[c] = g; _pos[c] = p;
        }
        return n;
    }

    // ---------------- the loops ----------------

    static float[] Stereo(int seconds) => new float[seconds * Rate * 2];

    /// <summary>Make the end flow into the start: crossfade the last half-second into the first.</summary>
    static float[] Seamless(float[] d)
    {
        int fade = Rate / 2 * 2, len = d.Length - fade;
        var o = new float[len];
        Array.Copy(d, o, len);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            o[i] = d[i] * k + d[len + i] * (1 - k);
        }
        return o;
    }

    static void Normalise(float[] d, float peak)
    {
        float m = 0;
        foreach (var v in d) m = MathF.Max(m, MathF.Abs(v));
        if (m > 0) for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
    }

    /// <summary>Soft hiss of rain with drops ticking on the glass, left and right.</summary>
    static float[] Rain(Random r, int seconds)
    {
        var d = Stereo(seconds);
        float l1 = 0, l2 = 0, r1 = 0, r2 = 0;
        for (int i = 0; i < d.Length; i += 2)
        {
            float a = (float)r.NextDouble() * 2 - 1, b = (float)r.NextDouble() * 2 - 1;
            l1 += (a - l1) * 0.35f; l2 += (l1 - l2) * 0.35f;
            r1 += (b - r1) * 0.35f; r2 += (r1 - r2) * 0.35f;
            d[i] = (l1 - l2 * 0.6f) * 0.5f; d[i + 1] = (r1 - r2 * 0.6f) * 0.5f;
        }
        // Drops: tiny bright ticks.
        for (int k = 0; k < seconds * 70; k++)
        {
            int at = r.Next(d.Length / 2 - 600) * 2, side = r.Next(2);
            float f = 2500 + (float)r.NextDouble() * 3500, amp = 0.15f + (float)r.NextDouble() * 0.35f;
            for (int j = 0; j < 300; j++) d[at + j * 2 + side] += MathF.Sin(j * f * MathF.Tau / Rate) * amp * MathF.Exp(-j / 45f);
        }
        Normalise(d, 0.32f);
        return Seamless(d);
    }

    /// <summary>Low wind with slow gusts.</summary>
    static float[] Wind(Random r, int seconds)
    {
        var d = Stereo(seconds);
        float l = 0, rr = 0, l2 = 0, r2 = 0;
        for (int i = 0; i < d.Length; i += 2)
        {
            float t = i / 2f / Rate;
            float gust = 0.45f + 0.35f * MathF.Sin(t * 0.7f) + 0.2f * MathF.Sin(t * 1.9f + 1);
            l += ((float)r.NextDouble() * 2 - 1 - l) * 0.02f; l2 += (l - l2) * 0.05f;
            rr += ((float)r.NextDouble() * 2 - 1 - rr) * 0.02f; r2 += (rr - r2) * 0.05f;
            d[i] = l2 * gust; d[i + 1] = r2 * gust;
        }
        Normalise(d, 0.3f);
        return Seamless(d);
    }

    /// <summary>Water lapping, with the odd plop.</summary>
    static float[] Pond(Random r, int seconds)
    {
        var d = Stereo(seconds);
        float lp = 0, bp = 0;
        for (int i = 0; i < d.Length; i += 2)
        {
            float t = i / 2f / Rate;
            float lap = 0.5f + 0.5f * MathF.Sin(t * MathF.Tau * 0.35f) * MathF.Sin(t * MathF.Tau * 0.13f + 2);
            float n = (float)r.NextDouble() * 2 - 1;
            lp += (n - lp) * 0.08f; bp += (lp - bp) * 0.02f;
            float v = (lp - bp) * lap;
            d[i] = v; d[i + 1] = v * 0.9f;
        }
        for (int k = 0; k < seconds / 2; k++)
        {
            int at = r.Next(d.Length / 2 - 8000) * 2;
            float f = 300 + (float)r.NextDouble() * 300;
            for (int j = 0; j < 6000; j++) { float tt = j / (float)Rate; float v = MathF.Sin(MathF.Tau * f * tt * (1 + tt * 4)) * MathF.Exp(-tt * 25) * 0.6f; d[at + j * 2] += v; d[at + j * 2 + 1] += v; }
        }
        Normalise(d, 0.28f);
        return Seamless(d);
    }

    /// <summary>A few birds: whistled phrases, here and there.</summary>
    static float[] Birds(Random r, int seconds)
    {
        var d = Stereo(seconds);
        int phrases = seconds;
        for (int k = 0; k < phrases; k++)
        {
            int at = r.Next(d.Length / 2 - Rate) * 2;
            float pan = (float)r.NextDouble() * 2 - 1, baseF = 2200 + (float)r.NextDouble() * 2200;
            int notes = 2 + r.Next(5);
            int pos = at;
            for (int n = 0; n < notes; n++)
            {
                float len = 0.05f + (float)r.NextDouble() * 0.12f, f0 = baseF * (0.85f + (float)r.NextDouble() * 0.35f), f1 = f0 * (0.75f + (float)r.NextDouble() * 0.6f);
                int m = (int)(len * Rate);
                double ph = 0;
                for (int j = 0; j < m && pos + j * 2 + 1 < d.Length; j++)
                {
                    float tt = j / (float)m, env = MathF.Sin(MathF.PI * tt);
                    ph += MathF.Tau * (f0 + (f1 - f0) * tt) / Rate;
                    float v = (float)(Math.Sin(ph) + 0.25 * Math.Sin(2 * ph)) * env * 0.5f;
                    d[pos + j * 2] += v * (1 - Math.Max(0, pan)); d[pos + j * 2 + 1] += v * (1 + Math.Min(0, pan));
                }
                pos += (m + (int)(Rate * (0.03f + (float)r.NextDouble() * 0.08f))) * 2;
            }
        }
        Normalise(d, 0.22f);
        return Seamless(d);
    }

    /// <summary>Crickets: quick pulses of a high tone, a few of them out of step.</summary>
    static float[] Crickets(Random r, int seconds)
    {
        var d = Stereo(seconds);
        for (int c = 0; c < 4; c++)
        {
            float f = 4200 + c * 330 + (float)r.NextDouble() * 200, pan = c switch { 0 => -0.7f, 1 => 0.6f, 2 => -0.2f, _ => 0.3f };
            float rateHz = 1.4f + (float)r.NextDouble() * 1.2f, off = (float)r.NextDouble();
            for (int i = 0; i < d.Length; i += 2)
            {
                float t = i / 2f / Rate;
                float cyc = (t * rateHz + off) % 1;
                if (cyc > 0.32f) continue;
                float pulse = MathF.Max(0, MathF.Sin(t * MathF.Tau * 28));   // the trill inside a chirp
                float v = MathF.Sin(t * MathF.Tau * f) * pulse * MathF.Sin(MathF.PI * cyc / 0.32f) * 0.25f;
                d[i] += v * (1 - Math.Max(0, pan)); d[i + 1] += v * (1 + Math.Min(0, pan));
            }
        }
        Normalise(d, 0.12f);
        return Seamless(d);
    }

    /// <summary>The town talking: soft overlapping babble, far enough off that it's only a murmur.</summary>
    static float[] Chatter(Random r, int seconds)
    {
        var d = Stereo(seconds);
        float[] formants1 = { 800, 480, 320, 520, 360 }, formants2 = { 1250, 1900, 2300, 920, 820 };
        for (int k = 0; k < seconds * 6; k++)
        {
            int at = r.Next(d.Length / 2 - Rate) * 2;
            float f0 = 160 + (float)r.NextDouble() * 160, pan = (float)r.NextDouble() * 1.6f - 0.8f;
            int syll = 2 + r.Next(5), pos = at;
            for (int s = 0; s < syll; s++)
            {
                int v = r.Next(5), m = (int)((0.07f + (float)r.NextDouble() * 0.06f) * Rate);
                double ph = 0;
                for (int j = 0; j < m && pos + j * 2 + 1 < d.Length; j++)
                {
                    float tt = j / (float)m, env = MathF.Sin(MathF.PI * tt);
                    ph += MathF.Tau * f0 * (1 + 0.1f * (1 - tt)) / Rate;
                    // A buzz shaped by two vowel resonances (cheap: weighted harmonics near the formants).
                    float val = 0;
                    for (int hN = 1; hN <= 12; hN++)
                    {
                        float hf = f0 * hN, w = MathF.Exp(-MathF.Pow((hf - formants1[v]) / 220, 2)) + 0.6f * MathF.Exp(-MathF.Pow((hf - formants2[v]) / 300, 2));
                        val += (float)Math.Sin(ph * hN) * w;
                    }
                    val *= env * 0.2f;
                    d[pos + j * 2] += val * (1 - Math.Max(0, pan)); d[pos + j * 2 + 1] += val * (1 + Math.Min(0, pan));
                }
                pos += (m + (int)(Rate * 0.03f)) * 2;
            }
        }
        // Muffle it: a gentle low-pass, so no words stand out.
        float l = 0, rr = 0;
        for (int i = 0; i < d.Length; i += 2) { l += (d[i] - l) * 0.18f; rr += (d[i + 1] - rr) * 0.18f; d[i] = l; d[i + 1] = rr; }
        Normalise(d, 0.2f);
        return Seamless(d);
    }

    /// <summary>A campfire: a soft roar and pops.</summary>
    static float[] Fire(Random r, int seconds)
    {
        var d = Stereo(seconds);
        float lp = 0;
        for (int i = 0; i < d.Length; i += 2)
        {
            lp += ((float)r.NextDouble() * 2 - 1 - lp) * 0.04f;
            d[i] = lp * 0.6f; d[i + 1] = lp * 0.55f;
        }
        for (int k = 0; k < seconds * 14; k++)
        {
            int at = r.Next(d.Length / 2 - 400) * 2;
            float amp = (float)Math.Pow(r.NextDouble(), 2) * 0.9f + 0.1f;
            for (int j = 0; j < 160; j++) { float v = ((float)r.NextDouble() * 2 - 1) * amp * MathF.Exp(-j / 18f); d[at + j * 2] += v; d[at + j * 2 + 1] += v * 0.8f; }
        }
        Normalise(d, 0.26f);
        return Seamless(d);
    }

    /// <summary>A lo-fi loop: four bars of soft jazzy chords (Fmaj7, Em7, Dm7, Cmaj7) on a warm electric piano, a lazy
    /// kick and snare, brushed hats and vinyl crackle, at 76 BPM.</summary>
    static float[] LoFi(Random r)
    {
        const double bpm = 76, beat = 60 / bpm;
        int bars = 4, n = (int)(bars * 4 * beat * Rate) + Rate / 2;   // + the crossfade
        var d = new float[n * 2];
        int[][] chords = { new[] { 53, 57, 60, 64 }, new[] { 52, 55, 59, 62 }, new[] { 50, 53, 57, 60 }, new[] { 48, 52, 55, 59 } };
        static double Hz(int m) => 440 * Math.Pow(2, (m - 69) / 12.0);
        void Add(int i, float v, float pan) { if (i < 0 || i >= n) return; d[i * 2] += v * (1 - Math.Max(0, pan)); d[i * 2 + 1] += v * (1 + Math.Min(0, pan)); }
        void Keys(double at, int midi, double len, float vol, float pan)
        {
            int s = (int)(at * Rate), m = (int)(len * Rate);
            double f = Hz(midi);
            for (int j = 0; j < m; j++)
            {
                double t = j / (double)Rate;
                float env = (float)(Math.Min(1, t / 0.01) * Math.Exp(-t * 1.6) * Math.Min(1, (len - t) / 0.08));
                float wob = 1 + 0.003f * MathF.Sin((float)t * 5);
                float v = (float)(Math.Sin(2 * Math.PI * f * wob * t) + 0.3 * Math.Sin(4 * Math.PI * f * t) * Math.Exp(-t * 4) + 0.12 * Math.Sin(6 * Math.PI * f * t));
                Add(s + j, v * env * vol, pan);
            }
        }
        for (int b = 0; b < bars; b++)
        {
            double t0 = b * 4 * beat;
            var c = chords[b];
            // Chord on 1, a softer push on the "and" of 2.
            foreach (var (m, k) in c.Select((m, k) => (m, k))) { Keys(t0 + k * 0.012, m, beat * 2.4, 0.09f, -0.2f + k * 0.13f); Keys(t0 + beat * 2.5 + k * 0.01, m, beat * 1.3, 0.05f, -0.2f + k * 0.13f); }
            Keys(t0, c[0] - 12, beat * 3.5, 0.13f, 0);
            // A little melody note now and then.
            if (b % 2 == 1) Keys(t0 + beat * 3.25, c[3] + 12, beat * 0.7, 0.05f, 0.4f);
            for (int q = 0; q < 4; q++)
            {
                double at = t0 + q * beat;
                if (q is 0 || (q == 2 && b % 2 == 1)) { int s = (int)(at * Rate); double ph = 0; for (int j = 0; j < Rate / 4; j++) { double tt = j / (double)Rate; ph += (45 + 70 * Math.Exp(-tt * 28)) / Rate; Add(s + j, (float)(Math.Sin(2 * Math.PI * ph) * Math.Exp(-tt * 10) * 0.32), 0); } }
                if (q is 1 or 3) { int s = (int)((at + 0.012) * Rate); float lp = 0; for (int j = 0; j < Rate / 6; j++) { float no = (float)r.NextDouble() * 2 - 1; lp += (no - lp) * 0.3f; Add(s + j, (no - lp) * MathF.Exp(-j / (float)Rate * 22) * 0.12f, 0.05f); } }
                for (int e = 0; e < 2; e++) { int s = (int)((at + e * beat / 2 + (e == 1 ? beat * 0.06 : 0)) * Rate); float prev = 0; for (int j = 0; j < Rate / 20; j++) { float no = (float)r.NextDouble() * 2 - 1, hp = no - prev; prev = no; Add(s + j, hp * MathF.Exp(-j / (float)Rate * 60) * 0.035f, -0.3f); } }
            }
        }
        // Vinyl: hiss and crackle; then warm it (a soft low-pass) and loop.
        for (int i = 0; i < n; i++) { float v = ((float)r.NextDouble() * 2 - 1) * 0.004f; if (r.NextDouble() < 0.0004) v += ((float)r.NextDouble() - 0.5f) * 0.25f; Add(i, v, 0); }
        float l = 0, rr = 0;
        for (int i = 0; i < d.Length; i += 2) { l += (d[i] - l) * 0.45f; rr += (d[i + 1] - rr) * 0.45f; d[i] = MathF.Tanh(l * 1.2f); d[i + 1] = MathF.Tanh(rr * 1.2f); }
        Normalise(d, 0.3f);
        return Seamless(d);
    }
}
