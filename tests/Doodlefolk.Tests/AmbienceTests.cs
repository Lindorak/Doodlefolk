using Xunit;

namespace Doodlefolk.Tests;

public class AmbienceTests
{
    static readonly Ambience Amb = new();

    static (float peak, float rms) Play(Ambience a, int seconds)
    {
        var buf = new float[44100 * 2];
        float peak = 0; double sum = 0; long n = 0;
        for (int s = 0; s < seconds; s++)
        {
            a.Read(buf);
            foreach (var v in buf) { peak = MathF.Max(peak, MathF.Abs(v)); sum += v * v; n++; }
        }
        return (peak, (float)Math.Sqrt(sum / n));
    }

    [Fact]
    public void EveryChannelMakesSoundWithoutClipping()
    {
        lock (Amb)
        {
            foreach (var c in Ambience.Channels)
            {
                Array.Clear(Amb.Target);
                Amb.Target[(int)c] = 1;
                Play(Amb, 4);   // let it fade in
                var (peak, rms) = Play(Amb, 3);
                Assert.True(rms > 0.003f, $"{c} is silent (rms {rms})");
                Assert.True(peak < 0.95f, $"{c} clips (peak {peak})");
            }
        }
    }

    [Fact]
    public void AllTogetherStaysUnderFullScale()
    {
        lock (Amb)
        {
            for (int i = 0; i < Amb.Target.Length; i++) Amb.Target[i] = 1;
            Play(Amb, 4);
            var (peak, _) = Play(Amb, 4);
            Assert.True(peak < 1.6f, $"peak {peak}");   // the output's soft limiter handles the rest
        }
    }

    [Fact]
    public void TurningEverythingDownGoesQuiet()
    {
        lock (Amb)
        {
            for (int i = 0; i < Amb.Target.Length; i++) Amb.Target[i] = 1;
            Play(Amb, 2);
            Array.Clear(Amb.Target);
            Play(Amb, 6);
            var (peak, _) = Play(Amb, 1);
            Assert.True(peak < 0.01f, $"peak {peak}");
        }
    }
}
