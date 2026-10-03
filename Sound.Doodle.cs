using System.Numerics;

namespace Doodlefolk;

/// <summary>What a figure is drawn with, and so what they sound like when they talk.</summary>
enum VoicePen { Pencil, Marker, Crayon, Chalk, Fountain }

/// <summary>How they feel as they say it: cheer (-1 sad .. 1 happy), anger, fear and tiredness (0..1).</summary>
readonly record struct VoiceMood(float Cheer, float Anger, float Fear, float Tired);

/// <summary>Doodle voices: they're drawings, so they talk in drawing sounds. Each word is a little hummed tune of its
/// own (the same word always sings the same way, so you start to recognise "pizza"); hard consonants are the tap of a
/// nib on paper, hissy ones a quick scritch of pencil hatching, soft ones a glide. What they're drawn with colours the
/// voice: a soft pencil, a bold squeaky marker, a waxy crayon, dusty chalk or a smooth fountain pen. Sentences drift
/// down as they go, questions lift at the end, shouts jump, "…" trails away; sad ones sing in a minor key, angry ones
/// get gritty, scared ones tremble.</summary>
sealed partial class Sound
{
    /// <summary>The old vowel-buzz babble instead (Studio → Settings → Sound → Voices).</summary>
    public static bool ClassicVoices;

    sealed record PenSound(float[] Harm, float Jitter, float Grain, float Breath, float Vibrato, float VibRate,
                           float Tap, float Hatch, float HatchHz, float HatchCentre, float Squeak, float Warmth);

    static readonly PenSound[] Pens =
    {
        // Pencil: a soft, round hum with a light graphite scratch.
        new(new[] { 1f, 0.28f, 0.1f }, 0.004f, 0.08f, 0.05f, 0.012f, 5.5f, 0.5f, 0.35f, 38, 3600, 0, 0.32f),
        // Marker: bright and bold, a little squeak at the start of each note, firm taps.
        new(new[] { 1f, 0.5f, 0.3f, 0.15f }, 0.006f, 0.04f, 0.02f, 0.01f, 6f, 0.7f, 0.25f, 30, 2600, 1, 0.45f),
        // Crayon: waxy and grainy, slow fat strokes.
        new(new[] { 1f, 0.38f, 0.14f }, 0.01f, 0.35f, 0.06f, 0.015f, 4.5f, 0.45f, 0.45f, 24, 2200, 0, 0.24f),
        // Chalk: dusty and breathy, quick hatching.
        new(new[] { 1f, 0.2f, 0.06f }, 0.005f, 0.15f, 0.28f, 0.01f, 5f, 0.4f, 0.5f, 44, 4200, 0, 0.3f),
        // Fountain pen: smooth and pure, with a gentle wobble.
        new(new[] { 1f, 0.12f, 0.03f }, 0.002f, 0, 0.03f, 0.022f, 5.8f, 0.3f, 0.18f, 52, 4800, 0, 0.3f),
    };

    static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    /// <summary>The finished sound for one line, or null if there's nothing to say.</summary>
    internal static float[]? DoodleSpeech(string text, float pitch, float speed, VoicePen pen, VoiceMood mood)
    {
        var words = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (char ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z') sb.Append(ch);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        if (words.Count == 0) return null;

        string end = text.TrimEnd();
        bool question = end.EndsWith('?');
        bool shout = end.Contains('!') || (text.Count(char.IsLetter) > 1 && !text.Any(char.IsLower));
        bool trail = end.EndsWith('…') || end.EndsWith("...");
        bool sing = text.Contains('~') || text.Contains('♪');
        var P = Pens[(int)pen];

        int[] scale = mood.Cheer < -0.25f ? new[] { 0, 3, 5, 7, 10 } : new[] { 0, 2, 4, 7, 9 };
        float f0 = 190 * pitch * (shout ? 1.15f : 1) * (1 + mood.Fear * 0.12f) * (1 - mood.Tired * 0.08f) * (mood.Cheer < -0.25f ? 0.94f : 1);
        float tempo = MathF.Max(0.4f, speed * (shout ? 1.12f : 1) * (trail ? 0.82f : 1) * (1 - mood.Tired * 0.2f) * (1 + mood.Fear * 0.15f));

        // Syllables: an onset consonant (or none) and a vowel group; each word's notes come from the word itself.
        var syl = new List<(char onset, int degree, bool wordEnd)>();
        foreach (var w in words)
        {
            if (syl.Count >= 10) break;
            int h = 5381;
            foreach (char c in w) h = h * 33 + c;
            var onsets = new List<char>();
            int j = 0;
            while (j < w.Length)
            {
                char onset = '\0';
                if (!IsVowel(w[j])) { onset = w[j]; while (j < w.Length && !IsVowel(w[j])) j++; }
                if (j >= w.Length) { if (onsets.Count == 0) onsets.Add(onset); break; }   // "hmm", "shh": one hummed beat
                while (j < w.Length && IsVowel(w[j])) j++;
                onsets.Add(onset);
            }
            int n = Math.Min(onsets.Count, 3);
            for (int k = 0; k < n && syl.Count < 10; k++)
                syl.Add((onsets[k], (int)((uint)(h >> (k * 4 + 1)) % (uint)scale.Length), k == n - 1));
        }

        float sylLen = 0.092f / tempo, gapWord = 0.045f / tempo;
        int total = (int)(((sylLen * 2 + 0.08f) * syl.Count + gapWord * words.Count) * Rate) + Rate / 4;
        var d = new float[total];
        int seed = 17;
        foreach (char c in text) seed = seed * 31 + c;
        var rng = new Random(seed);
        int pos = 0;
        float prevF = 0, phase = 0;
        for (int i = 0; i < syl.Count; i++)
        {
            var (on, deg, wordEnd) = syl[i];
            bool last = i == syl.Count - 1;
            float prog = syl.Count == 1 ? 0 : i / (float)(syl.Count - 1);
            float semis = scale[deg] - 4 * prog * (question ? 0.3f : 1);   // sentences drift down as they go
            if (shout && i == 0) semis += 3;
            if (sing) semis += i % 2 == 0 ? 2 : -1;
            if (mood.Anger > 0.4f) semis *= 0.5f;   // clipped and flat when cross
            float target = f0 * MathF.Pow(2, semis / 12f);
            float endF = !last ? target : question ? target * 1.335f : trail ? target * 0.794f : target * 0.891f;

            pos = Consonant(d, pos, on, P, target, rng, tempo);
            float len = sylLen * (0.85f + (float)rng.NextDouble() * 0.3f) * (last ? (trail ? 1.9f : 1.35f) : 1) * (wordEnd ? 1.08f : 0.92f);
            bool glide = on is 'm' or 'n' or 'l' or 'r' or 'w' or 'y' or '\0';
            pos = Tone(d, pos, len, glide && prevF > 0 ? prevF : target, target, endF, P, mood, rng, ref phase);
            prevF = endF;
            pos += (int)((wordEnd && !last ? gapWord * (0.7f + (float)rng.NextDouble() * 0.6f) : 0.012f / tempo) * Rate);
            if (pos >= total - Rate / 8) break;
        }
        int len2 = Math.Min(pos + Rate / 20, d.Length);
        // The paper softens it all a little.
        float y = 0, peak = 0;
        for (int i = 0; i < len2; i++) { y += (d[i] - y) * P.Warmth * 1.6f; d[i] = y; peak = MathF.Max(peak, MathF.Abs(y)); }
        if (peak < 1e-4f || !float.IsFinite(peak)) return null;
        var outBuf = new float[len2];
        float k2 = 0.24f / peak;
        for (int i = 0; i < len2; i++) outBuf[i] = d[i] * k2;
        return outBuf;
    }

    /// <summary>One hummed note: gliding in from where the last one ended, settling, then falling or lifting to its end.</summary>
    static int Tone(float[] d, int pos, float secs, float fStart, float fMid, float fEnd, PenSound P, VoiceMood mood, Random r, ref float phase)
    {
        int n = Math.Min((int)(secs * Rate), d.Length - pos - 1);
        if (n <= 0) return pos;
        float hsum = 0;
        foreach (var a in P.Harm) hsum += a;
        float jit = 0, grain = 0, grainTo = 0, breath = 0;
        int attack = (int)(0.012f * Rate);
        for (int i = 0; i < n; i++)
        {
            float p = i / (float)n, t = i / (float)Rate;
            float f = p < 0.22f ? fStart + (fMid - fStart) * Smooth(p / 0.22f) : fMid + (fEnd - fMid) * Smooth((p - 0.22f) / 0.78f);
            if (P.Squeak > 0 && p < 0.08f) f *= 1 + 0.06f * (1 - p / 0.08f) * P.Squeak;
            f *= 1 + P.Vibrato * MathF.Sin(MathF.Tau * P.VibRate * t) * MathF.Min(1, p * 3);
            jit = (jit + ((float)r.NextDouble() * 2 - 1) * P.Jitter * 0.3f) * 0.97f;
            f *= 1 + jit;
            phase += f / Rate;
            if (phase >= 1) phase -= 1;
            float s = 0;
            for (int k = 0; k < P.Harm.Length; k++) s += P.Harm[k] * MathF.Sin(MathF.Tau * (k + 1) * phase);
            s /= hsum;
            if (mood.Anger > 0.3f) s = MathF.Tanh(s * (1 + mood.Anger * 2.5f)) * 0.8f;   // gritty
            if (i % (Rate / 40) == 0) grainTo = (float)r.NextDouble();
            grain += (grainTo - grain) * 0.004f;
            float amp = 1 - P.Grain * grain;
            amp *= 1 + 0.25f * mood.Fear * MathF.Sin(MathF.Tau * 9 * t);   // a tremble
            breath += (((float)r.NextDouble() * 2 - 1) - breath) * 0.3f;
            s += breath * P.Breath;
            float env = MathF.Min(1, i / (float)attack) * (p > 0.65f ? MathF.Pow((1 - p) / 0.35f, 1.5f) : 1);
            d[pos + i] += s * amp * env * 0.3f;
        }
        return pos + n;
    }

    static float Smooth(float x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>The start of a syllable: a nib tap (p t k b d g), a scritch of hatching (s f z x h j v), a short hum
    /// (m n l r w), or nothing.</summary>
    static int Consonant(float[] d, int pos, char c, PenSound P, float f, Random r, float tempo)
    {
        if (c == '\0') return pos;
        if (c is 'p' or 't' or 'k' or 'b' or 'd' or 'g' or 'c' or 'q')
        {
            bool voiced = c is 'b' or 'd' or 'g';
            int n = Math.Min((int)(0.03f * Rate), d.Length - pos - 1);
            float lp = 0, bf = voiced ? f * 0.7f : f * 1.6f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float click = ((float)r.NextDouble() * 2 - 1) * MathF.Exp(-t * 900);
                lp += (click - lp) * (voiced ? 0.25f : 0.6f);
                float body = MathF.Sin(MathF.Tau * bf * t) * MathF.Exp(-t * 120) * 0.5f;
                d[pos + i] += (lp * 0.9f + body * 0.35f) * P.Tap * 0.5f;
            }
            return pos + (int)(0.016f / tempo * Rate);   // the note starts while the tap rings out
        }
        if (c is 's' or 'f' or 'z' or 'x' or 'h' or 'j' or 'v')
        {
            float secs = (c == 'h' ? 0.03f : 0.05f) / MathF.Max(tempo, 0.7f);
            int n = Math.Min((int)(secs * Rate), d.Length - pos - 1);
            float centre = P.HatchCentre * (c is 'f' or 'v' or 'h' ? 0.55f : 1);
            float rr = MathF.Exp(-MathF.PI * 1600 / Rate), th = MathF.Tau * centre / Rate;
            float a1 = 2 * rr * MathF.Cos(th), a2 = -rr * rr, g = 1 - rr;
            float y1 = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                float p = i / (float)n, t = i / (float)Rate;
                float y = g * ((float)r.NextDouble() * 2 - 1) * 3 + a1 * y1 + a2 * y2; y2 = y1; y1 = y;
                float stroke = MathF.Pow(MathF.Abs(MathF.Sin(MathF.PI * P.HatchHz * t)), 0.6f);   // back and forth
                d[pos + i] += y * stroke * MathF.Sin(MathF.PI * p) * P.Hatch * 0.35f;
            }
            return pos + (int)(n * 0.8f);
        }
        if (c is 'm' or 'n' or 'l' or 'r' or 'w')
        {
            int n = Math.Min((int)(0.03f / MathF.Max(tempo, 0.7f) * Rate), d.Length - pos - 1);
            float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float p = i / (float)n;
                ph += f * 0.97f / Rate;
                d[pos + i] += MathF.Sin(MathF.Tau * ph) * 0.3f * 0.35f * Smooth(p * 1.4f);
            }
            return pos + n;
        }
        return pos;
    }
}

/// <summary>--voicesamples folder: every pen saying a few lines, as WAV files (for listening while tuning voices).</summary>
static class VoiceSamples
{
    public static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--voicesamples");
        string dir = i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "doodlefolk-voices");
        Directory.CreateDirectory(dir);
        string[] lines = { "Hello! Is this me?", "Pizza, please!", "Where did everyone go?", "I'm so tired…", "LET'S GO!!", "Nice to meet you, Sparky." };
        foreach (VoicePen pen in Enum.GetValues<VoicePen>())
        {
            var all = new List<float>();
            foreach (var (line, k) in lines.Select((l, k) => (l, k)))
            {
                var mood = k == 3 ? new VoiceMood(-0.6f, 0, 0, 0.7f) : k == 4 ? new VoiceMood(0.8f, 0, 0, 0) : default;
                if (Sound.DoodleSpeech(line, k % 2 == 0 ? 1.35f : 0.9f, 1, pen, mood) is { } d) all.AddRange(d);
                all.AddRange(new float[Sound.SampleRate / 3]);
            }
            Write(Path.Combine(dir, $"{pen.ToString().ToLowerInvariant()}.wav"), all);
            float peak = all.Max(MathF.Abs);
            Console.WriteLine($"{pen}: {all.Count / (float)Sound.SampleRate:F1}s peak {peak:F2} finite {all.All(float.IsFinite)}");
        }
        return 0;
    }

    static void Write(string path, List<float> s)
    {
        using var w = new BinaryWriter(File.Create(path));
        int n = s.Count, rate = Sound.SampleRate;
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(n * 2);
        foreach (var x in s) w.Write((short)Math.Clamp(x * 32767 * 2.5f, -32767, 32767));
    }
}
