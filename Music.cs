namespace Doodlefolk;

/// <summary>A cheerful little tune for the trailer, written out note by note and synthesised (nothing sampled):
/// a plucked arpeggio, a soft pad, a bouncy bass and light drums over a I–V–vi–IV progression in C, about 112 BPM,
/// with a gentle start and a ringing final chord. Stereo float samples at 44.1 kHz.</summary>
static class Music
{
    const int Rate = 44100;
    const double Bpm = 112, Beat = 60 / Bpm;

    // C, G, Am, F (each a bar of four beats): root and chord tones as MIDI notes.
    static readonly int[][] Chords = { new[] { 48, 60, 64, 67 }, new[] { 43, 59, 62, 67 }, new[] { 45, 60, 64, 69 }, new[] { 41, 60, 65, 69 } };
    static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

    public static float[] Render(double seconds, int seed = 3)
    {
        int n = (int)(seconds * Rate);
        var left = new float[n]; var right = new float[n];
        var rng = new Random(seed);
        int bars = (int)Math.Ceiling(seconds / (Beat * 4));
        double end = seconds - 3.2;   // the last chord rings out from here
        for (int bar = 0; bar < bars; bar++)
        {
            double t0 = bar * Beat * 4;
            if (t0 >= seconds) break;
            bool final = t0 >= end - 0.01;
            var chord = final ? Chords[0] : Chords[bar % 4];
            bool intro = bar < 2, full = bar >= 2 && !final;
            // Pad: the chord, softly, all bar long (longer at the end).
            double padLen = final ? seconds - t0 : Beat * 4;
            foreach (int m in chord.Skip(1)) Pad(left, right, t0, padLen, Hz(m), final ? 0.075 : 0.05);
            if (final) { Pluck(left, right, t0, Hz(chord[1] + 12), 0.22, 0); Pluck(left, right, t0 + 0.03, Hz(chord[2] + 12), 0.2, 0.3); Pluck(left, right, t0 + 0.06, Hz(chord[3] + 12), 0.2, -0.3); Bass(left, right, t0, Hz(chord[0]), 2.5, 0.3); break; }
            // Arpeggio: eighth notes up and down the chord, an octave up.
            int[] pattern = { 1, 2, 3, 2, 1, 2, 3, 2 };
            for (int i = 0; i < 8; i++)
            {
                int note = chord[pattern[i]] + 12 + (i == 6 && rng.NextDouble() < 0.4 ? 12 : 0);
                Pluck(left, right, t0 + i * Beat / 2, Hz(note), intro ? 0.13 : 0.17, i % 2 == 0 ? -0.35 : 0.35);
            }
            if (!intro)
            {
                // Bass on 1 and 3, a pickup on the "and" of 4.
                Bass(left, right, t0, Hz(chord[0]), Beat * 1.6, 0.32);
                Bass(left, right, t0 + Beat * 2, Hz(chord[0] + 7), Beat * 1.4, 0.26);
                Bass(left, right, t0 + Beat * 3.5, Hz(chord[0] + 12), Beat * 0.4, 0.2);
            }
            if (full)
            {
                for (int b = 0; b < 4; b++)
                {
                    if (b % 2 == 0) Kick(left, right, t0 + b * Beat);
                    else Clap(left, right, t0 + b * Beat, rng);
                    Hat(left, right, t0 + b * Beat + Beat / 2, rng);
                }
            }
            else if (intro && bar == 1) Hat(left, right, t0 + Beat * 3.5, rng);
        }
        // Gentle fade in, soft limit, interleave.
        var mix = new float[n * 2];
        for (int i = 0; i < n; i++)
        {
            double fade = Math.Min(1, i / (Rate * 0.8));
            mix[i * 2] = (float)Math.Tanh(left[i] * fade * 1.1);
            mix[i * 2 + 1] = (float)Math.Tanh(right[i] * fade * 1.1);
        }
        return mix;
    }

    static void Add(float[] l, float[] r, int i, double v, double pan)
    {
        if (i < 0 || i >= l.Length) return;
        l[i] += (float)(v * (1 - Math.Max(0, pan)));
        r[i] += (float)(v * (1 + Math.Min(0, pan)));
    }

    /// <summary>A plucked string (Karplus–Strong): bright, then mellow.</summary>
    static void Pluck(float[] l, float[] r, double at, double hz, double vol, double pan)
    {
        int start = (int)(at * Rate), period = Math.Max(2, (int)(Rate / hz)), len = (int)(Rate * 1.2);
        var buf = new double[period];
        var rng = new Random((int)(hz * 100) ^ start);
        for (int i = 0; i < period; i++) buf[i] = rng.NextDouble() * 2 - 1;
        int p = 0;
        for (int i = 0; i < len; i++)
        {
            double v = buf[p];
            int q = (p + 1) % period;
            buf[p] = (buf[p] + buf[q]) * 0.5 * 0.996;
            p = q;
            Add(l, r, start + i, v * vol * Math.Min(1, i / 40.0), pan);
        }
    }

    /// <summary>A soft pad: two slightly detuned triangle waves with a slow swell.</summary>
    static void Pad(float[] l, float[] r, double at, double len, double hz, double vol)
    {
        int start = (int)(at * Rate), n = (int)(len * Rate);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double env = Math.Min(1, t / 0.35) * Math.Min(1, (len - t) / 0.4);
            double v = Tri(hz * t) * 0.6 + Tri(hz * 1.004 * t) * 0.4;
            Add(l, r, start + i, v * env * vol, Math.Sin(t * 0.7) * 0.4);
        }
    }

    static double Tri(double phase) { double x = phase - Math.Floor(phase); return 4 * Math.Abs(x - 0.5) - 1; }

    static void Bass(float[] l, float[] r, double at, double hz, double len, double vol)
    {
        int start = (int)(at * Rate), n = (int)(len * Rate);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double env = Math.Min(1, t / 0.01) * Math.Exp(-t * 2.2) * Math.Min(1, (len - t) / 0.05);
            double v = Math.Sin(2 * Math.PI * hz * t) + 0.3 * Math.Sin(4 * Math.PI * hz * t);
            Add(l, r, start + i, v * env * vol, 0);
        }
    }

    static void Kick(float[] l, float[] r, double at)
    {
        int start = (int)(at * Rate), n = (int)(0.28 * Rate);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            phase += (50 + 110 * Math.Exp(-t * 30)) / Rate;
            Add(l, r, start + i, Math.Sin(2 * Math.PI * phase) * Math.Exp(-t * 9) * 0.55, 0);
        }
    }

    static void Clap(float[] l, float[] r, double at, Random rng)
    {
        int start = (int)(at * Rate), n = (int)(0.16 * Rate);
        double lp = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            double noise = rng.NextDouble() * 2 - 1;
            lp += (noise - lp) * 0.35;
            double env = Math.Exp(-t * 28) + 0.4 * Math.Exp(-Math.Abs(t - 0.012) * 400);
            Add(l, r, start + i, (noise - lp) * env * 0.22, 0.1);
        }
    }

    static void Hat(float[] l, float[] r, double at, Random rng)
    {
        int start = (int)(at * Rate), n = (int)(0.05 * Rate);
        double prev = 0;
        for (int i = 0; i < n; i++)
        {
            double noise = rng.NextDouble() * 2 - 1, hp = noise - prev; prev = noise;
            Add(l, r, start + i, hp * Math.Exp(-i / (double)Rate * 70) * 0.07, -0.25);
        }
    }
}
