using Xunit;

namespace Doodlefolk.Tests;

/// <summary>The campfire's sound comes from recordings built into the app: they must load, make a fire-like sound
/// (a bed plus plenty of separate crackles), and never clip.</summary>
public class FireVoiceTests
{
    [Fact]
    public void BurnsAndCrackles()
    {
        var fire = FireVoice.Load(seed: 7);   // the same fire every run
        Assert.NotNull(fire);
        fire!.Target = 0.42f;
        var buf = new float[44100 * 2 * 6];
        for (int o = 0; o < buf.Length; o += 4096) fire.Read(buf.AsSpan(o, Math.Min(4096, buf.Length - o)));
        var tail = buf.AsSpan(44100 * 2 * 3).ToArray();   // after it has faded in
        float peak = tail.Max(MathF.Abs), rms = MathF.Sqrt(tail.Average(v => v * v));
        Assert.True(rms > 0.01f, $"too quiet: rms {rms}");
        Assert.True(peak < 0.95f, $"clips: peak {peak}");
        // Crackles: sharp jumps well above the bed, several a second.
        int jumps = 0;
        for (int i = 2; i < tail.Length; i += 2) if (MathF.Abs(tail[i] - tail[i - 2]) > rms * 1.5f) jumps++;
        Assert.True(jumps > 30, $"only {jumps} sharp crackle samples");
    }

    [Fact]
    public void FallsSilentWhenOut()
    {
        var fire = FireVoice.Load(seed: 7)!;
        fire.Target = 0;
        var buf = new float[44100 * 2];
        fire.Read(buf);
        Assert.True(buf.All(v => v == 0));
    }
}
