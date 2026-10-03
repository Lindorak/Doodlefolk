using System.Numerics;

namespace Doodlefolk;

/// <summary>Where a ball will go and how to send it somewhere: the same physics as Prop.Step (gravity, then drag,
/// then move; bouncing off a flat floor), so what the players plan is what actually happens.</summary>
static class Ballistics
{
    public const float Dt = 1 / 120f;

    public static float DragOf(PropKind k) => k switch { PropKind.BeachBall => 0.9f, PropKind.Shuttlecock => 2.4f, _ => 0.15f };

    /// <summary>Fly a ball forward over a flat floor at floorY, a step at a time: visit(t, pos, vel, bounces) each step,
    /// until it returns false or the horizon is reached. Once it stops bouncing it rolls (not simulated further).</summary>
    public static void Fly(Vector2 p, Vector2 v, float g, float k, float r, float floorY, float bounce, float s, float horizon, Func<float, Vector2, Vector2, int, bool> visit)
    {
        int bounces = 0;
        for (float t = Dt; t <= horizon; t += Dt)
        {
            v.Y += g * Dt;
            v *= 1 - k * Dt;
            p += v * Dt;
            if (p.Y + r >= floorY && v.Y > 0)
            {
                p.Y = floorY - r;
                bounces++;
                if (v.Y > 120 * s) v.Y = -v.Y * bounce;
                else { visit(t, p, new Vector2(v.X, 0), bounces); return; }
            }
            if (!visit(t, p, v, bounces)) return;
        }
    }

    /// <summary>The launch velocity that carries a ball from a to b in time T (gravity g, linear drag k).</summary>
    public static Vector2 Launch(Vector2 a, Vector2 b, float g, float k, float T)
    {
        if (k < 1e-3f) return new((b.X - a.X) / T, (b.Y - a.Y) / T - 0.5f * g * T);
        // dv/dt = g - kv (y down): y(T) = y0 + (g/k)T + (v0 - g/k)(1 - e^-kT)/k, solved for v0.
        float e = 1 - MathF.Exp(-k * T);
        return new((b.X - a.X) * k / e, (b.Y - a.Y - g * T / k) * k / e + g / k);
    }

    /// <summary>Where a ball launched from a at velocity v is after time t.</summary>
    public static Vector2 At(Vector2 a, Vector2 v, float g, float k, float t)
    {
        if (k < 1e-3f) return a + v * t + new Vector2(0, 0.5f * g * t * t);
        float e = (1 - MathF.Exp(-k * t)) / k;
        return new(a.X + v.X * e, a.Y + g / k * t + (v.Y - g / k) * e);
    }

    /// <summary>The quickest shot from a to b, no faster than maxSpeed, that passes over (netX, netTop) with clearance
    /// to spare (netX outside the way there: no net to clear). Null if nothing works.</summary>
    public static Vector2? Over(Vector2 a, Vector2 b, float g, float k, float netX, float netTop, float clearance, float maxSpeed, float tMin = 0.2f, float tMax = 3.2f)
    {
        bool crosses = (netX - a.X) * (netX - b.X) < 0;
        for (float T = tMin; T <= tMax; T += 0.03f)
        {
            var v = Launch(a, b, g, k, T);
            if (v.Length() > maxSpeed) continue;
            if (!crosses) return v;
            // When does it get to the net? (Solve x(t) = netX.)
            float dx = netX - a.X, tn;
            if (k < 1e-3f) tn = dx / v.X;
            else
            {
                float q = 1 - dx * k / v.X;
                if (q <= 0) continue;
                tn = -MathF.Log(q) / k;
            }
            if (tn <= 0 || tn > T) continue;
            if (At(a, v, g, k, tn).Y <= netTop - clearance) return v;
        }
        return null;
    }
}
