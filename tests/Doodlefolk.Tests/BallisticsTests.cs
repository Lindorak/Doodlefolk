using System.Numerics;
using Xunit;

namespace Doodlefolk.Tests;

/// <summary>The shot solver has to agree with the game's own step-by-step ball physics, or players aim at the wrong place.</summary>
public class BallisticsTests
{
    // The game's integrator (Prop.Step, in the air): gravity, then drag, then move.
    static Vector2 Step(Vector2 p, Vector2 v, float g, float k, float t)
    {
        for (float s = 0; s < t - 1e-4f; s += Ballistics.Dt)
        {
            v.Y += g * Ballistics.Dt;
            v *= 1 - k * Ballistics.Dt;
            p += v * Ballistics.Dt;
        }
        return p;
    }

    [Theory]
    [InlineData(0.15f, 0.8f)]   // tennis ball
    [InlineData(2.4f, 1.1f)]    // shuttlecock
    [InlineData(0.15f, 1.6f)]   // basketball lob
    public void LaunchLandsOnTarget(float k, float T)
    {
        var a = new Vector2(700, 980);
        var b = new Vector2(1180, 1020);
        float g = 1800;
        var v = Ballistics.Launch(a, b, g, k, T);
        var p = Step(a, v, g, k, T);
        Assert.True(Vector2.Distance(p, b) < 12, $"landed at {p}, aimed at {b}");
        Assert.True(Vector2.Distance(Ballistics.At(a, v, g, k, T), b) < 0.5f);
    }

    [Theory]
    [InlineData(0.15f)]
    [InlineData(2.4f)]
    public void OverClearsTheNet(float k)
    {
        var a = new Vector2(700, 990);
        var b = new Vector2(1200, 1025);
        float g = 1800, netX = 960, netTop = 990;
        var v = Ballistics.Over(a, b, g, k, netX, netTop, 10, 2500);
        Assert.NotNull(v);
        // Step it until it reaches the net: it's above the net top there.
        var p = a; var vel = v!.Value;
        while (p.X < netX) { vel.Y += g * Ballistics.Dt; vel *= 1 - k * Ballistics.Dt; p += vel * Ballistics.Dt; }
        Assert.True(p.Y < netTop - 5, $"at the net it's at {p.Y}, the net top is {netTop}");
    }
}
