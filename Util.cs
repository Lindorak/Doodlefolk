using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

static class M
{
    /// <summary>Clamp that tolerates lo &gt; hi (e.g. a margin wider than a narrow platform): returns the middle.</summary>
    public static float ClampIn(float v, float lo, float hi) => lo <= hi ? Math.Clamp(v, lo, hi) : (lo + hi) * 0.5f;

    public static float Clamp01(float x) => x < 0 ? 0 : x > 1 ? 1 : x;
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static float Smooth(float t) { t = Clamp01(t); return t * t * (3 - 2 * t); }
    public static float MoveTowards(float a, float b, float d) => MathF.Abs(b - a) <= d ? b : a + MathF.Sign(b - a) * d;

    /// <summary>Unit vector at angle <paramref name="a"/> measured clockwise (on screen) from straight up.</summary>
    public static Vector2 Dir(float a) => new(MathF.Sin(a), -MathF.Cos(a));

    public static void Spring(ref float x, ref float v, float target, float omega, float zeta, float dt)
    {
        v += (-2 * zeta * omega * v - omega * omega * (x - target)) * dt;
        x += v * dt;
    }

    public static void Spring(ref Vector2 x, ref Vector2 v, Vector2 target, float omega, float zeta, float dt)
    {
        v += (-2 * zeta * omega * v - omega * omega * (x - target)) * dt;
        x += v * dt;
    }

    public static Vector2 ClampLength(Vector2 v, float max)
    {
        float l = v.Length();
        return l > max && l > 0 ? v * (max / l) : v;
    }

    public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Clamp01(Vector2.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f));
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Two-bone IK. Returns the middle joint and the (reach-clamped) end point.
    /// <paramref name="pref"/> picks which way the joint bends.</summary>
    public static (Vector2 joint, Vector2 end) IK(Vector2 a, Vector2 target, float l1, float l2, Vector2 pref)
    {
        Vector2 d = target - a;
        float len = d.Length();
        if (len < 1e-4f) return (a + Vector2.Normalize(pref) * l1, target);
        Vector2 dir = d / len;
        float c = Math.Clamp(len, MathF.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.999f);
        float cosA = Math.Clamp((l1 * l1 + c * c - l2 * l2) / (2 * l1 * c), -1f, 1f);
        float ang = MathF.Acos(cosA);
        Vector2 j1 = a + Rotate(dir, ang) * l1, j2 = a + Rotate(dir, -ang) * l1;
        Vector2 joint = Vector2.Dot(j1 - a, pref) >= Vector2.Dot(j2 - a, pref) ? j1 : j2;
        return (joint, a + dir * c);
    }

    public static Vector2 Rotate(Vector2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public static float Range(this Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    public static Color4 Hex(uint rgb, float a = 1) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static Color4 Shade(Color4 c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);
}

static class Palette
{
    public static readonly (string Name, Color4 Color)[] All =
    {
        ("Red", M.Hex(0xE53935)), ("Blue", M.Hex(0x1E88E5)), ("Green", M.Hex(0x43A047)),
        ("Orange", M.Hex(0xFB8C00)), ("Purple", M.Hex(0x8E24AA)), ("Yellow", M.Hex(0xFDD835)),
        ("Cyan", M.Hex(0x00ACC1)), ("Pink", M.Hex(0xEC407A)), ("Black", M.Hex(0x262626)), ("White", M.Hex(0xF4F4F4)),
    };
}
