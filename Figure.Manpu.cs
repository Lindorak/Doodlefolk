using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Manga symbols (manpu): the little marks around a head that say how someone feels at a glance.</summary>
[Flags]
enum Manpu
{
    None = 0,
    Vein = 1,        // 💢 the throbbing cross of an angry vein
    Steam = 2,       // puffs rising off the head: fuming
    SweatDrop = 4,   // one big drop on the side of the head: awkward, embarrassed, exasperated
    Gloom = 8,       // vertical lines over the head and a little cloud: down in the dumps
    Sparkles = 16,   // twinkling stars: delighted, proud
    Shock = 32,      // lines bursting out round the head: startled
    Dizzy = 64,      // stars circling the head: knocked silly
    Hearts = 128,    // little hearts floating up: smitten
    Blush = 256,     // pink hatching on the cheek
    Nervous = 512,   // droplets flying off: scared stiff
}

/// <summary>Drawing the manga symbols round the head (see Brain.UpdateSymbols for when they show).</summary>
sealed partial class Figure
{
    /// <summary>What shows right now (set by the brain every frame).</summary>
    public Manpu Symbols;

    void DrawManpu(Renderer r, float fade)
    {
        if (!World.Manga || Symbols == Manpu.None || fade < 0.5f) return;
        float R = HeadR * 1.35f, t = _time + Id * 0.37f;   // marks a little bigger than life, as in manga
        Vector2 c = Jt[J.Head];
        float F = Facing;
        var ink = new Color4(0.12f, 0.11f, 0.1f, 0.85f * fade);

        if (Symbols.HasFlag(Manpu.Blush))
        {
            var pink = new Color4(0.95f, 0.42f, 0.55f, 0.8f * fade);
            for (int i = 0; i < 3; i++)
            {
                var a = c + new Vector2(F * (0.18f + i * 0.2f) * R, 0.05f * R);
                r.Line(a, a + new Vector2(F * 0.16f * R, 0.28f * R), pink, 0.45f * S);
            }
        }
        if (Symbols.HasFlag(Manpu.Gloom))
        {
            // Lines hanging down over the head, and a little rain cloud above.
            var gloom = new Color4(0.24f, 0.16f, 0.38f, 0.8f * fade);
            for (int i = 0; i < 5; i++)
            {
                float x = (-0.6f + i * 0.3f) * R;
                float top = c.Y - R * 0.95f + MathF.Abs(x) * 0.5f, bot = c.Y - R * 0.05f + MathF.Sin(t * 2 + i) * 0.06f * R;
                r.Line(new Vector2(c.X + x, top), new Vector2(c.X + x, bot), gloom, 0.65f * S);
            }
            var cl = c + new Vector2(0, -2.05f * R + MathF.Sin(t * 1.2f) * 0.08f * R);
            var cloud = new Color4(0.55f, 0.56f, 0.62f, 0.9f * fade);
            foreach (var (dx, dy, rr) in new[] { (-0.42f, 0.05f, 0.34f), (0f, -0.12f, 0.44f), (0.44f, 0.05f, 0.32f) }) r.Disc(cl + new Vector2(dx * R, dy * R), (rr + 0.08f) * R, ink);
            foreach (var (dx, dy, rr) in new[] { (-0.42f, 0.05f, 0.34f), (0f, -0.12f, 0.44f), (0.44f, 0.05f, 0.32f) }) r.Disc(cl + new Vector2(dx * R, dy * R), rr * R, cloud);
            for (int i = 0; i < 3; i++)
            {
                float p = (t * 1.6f + i * 0.33f) % 1;
                var d = cl + new Vector2((-0.35f + i * 0.35f) * R, (0.35f + p * 0.55f) * R);
                r.Line(d, d + new Vector2(-0.05f * R, 0.18f * R), new Color4(0.45f, 0.65f, 0.95f, (1 - p) * 0.9f * fade), 0.35f * S);
            }
        }
        if (Symbols.HasFlag(Manpu.Vein))
        {
            // Four bowed strokes round a cross-shaped gap, throbbing.
            var v = c + new Vector2(F * 0.82f * R, -0.85f * R);
            float s = 0.62f * R * (1 + 0.16f * MathF.Max(0, MathF.Sin(t * 11)));
            var red = new Color4(0.9f, 0.16f, 0.16f, fade);
            for (int q = 0; q < 4; q++)
            {
                float a = MathF.PI / 4 + q * MathF.PI / 2;
                Vector2 P(float ang, float rad) => v + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rad;
                var p0 = P(a - 0.62f, s); var pm = P(a, s * 0.52f); var p1 = P(a + 0.62f, s);
                r.Line(p0, pm, ink, 1.25f * S); r.Line(pm, p1, ink, 1.25f * S);
                r.Line(p0, pm, red, 0.8f * S); r.Line(pm, p1, red, 0.8f * S);
            }
        }
        if (Symbols.HasFlag(Manpu.Steam))
        {
            // Puffs rising off the top of the head, one side then the other.
            for (int i = 0; i < 4; i++)
            {
                float p = (t * 0.95f + i * 0.25f) % 1;
                float side = i % 2 == 0 ? 1 : -1;
                var at = c + new Vector2(side * (0.55f + p * 0.7f) * R, -(1.0f + p * 1.5f) * R);
                float rr = (0.26f + 0.36f * p) * R;
                float a = (1 - p * p) * 0.95f * fade;
                r.Disc(at, rr + 0.35f * S, new Color4(0.35f, 0.35f, 0.38f, a * 0.85f));
                r.Disc(at, rr, new Color4(0.98f, 0.98f, 0.98f, a));
            }
        }
        if (Symbols.HasFlag(Manpu.SweatDrop))
        {
            // One big drop by the back of the head, sliding down a little.
            float slide = (MathF.Sin(t * 1.4f) + 1) * 0.08f * R;
            var d = c + new Vector2(-F * 1.02f * R, -0.62f * R + slide);
            float rr = 0.26f * R;
            Span<Vector2> drop = stackalloc Vector2[14];
            drop[0] = d + new Vector2(0, -rr * 2.4f);
            for (int i = 0; i < 13; i++)
            {
                float ang = -MathF.PI * 0.18f + i * (MathF.PI * 1.36f) / 12;
                drop[i + 1] = d + new Vector2(MathF.Cos(ang) * rr, MathF.Sin(ang) * rr);
            }
            r.FillPolygon(drop, new Color4(0.15f, 0.35f, 0.6f, 0.9f * fade));
            for (int i = 0; i < drop.Length; i++) drop[i] = d + (drop[i] - d) * 0.8f;
            r.FillPolygon(drop, new Color4(0.62f, 0.85f, 0.98f, 0.95f * fade));
            r.Disc(d + new Vector2(-rr * 0.3f, -rr * 0.25f), rr * 0.22f, new Color4(1, 1, 1, 0.9f * fade));
        }
        if (Symbols.HasFlag(Manpu.Nervous))
        {
            // Little droplets flicking off either side.
            for (int i = 0; i < 4; i++)
            {
                float p = (t * 1.8f + i * 0.25f) % 1;
                float side = i % 2 == 0 ? 1 : -1;
                var at = c + new Vector2(side * (0.95f + p * 0.7f) * R, (-0.5f - MathF.Sin(MathF.PI * p) * 0.5f + p * 0.4f) * R);
                r.Disc(at, 0.21f * R, new Color4(0.25f, 0.5f, 0.8f, (1 - p) * 0.9f * fade));
                r.Disc(at, 0.15f * R, new Color4(0.6f, 0.85f, 1f, (1 - p) * 0.95f * fade));
            }
        }
        if (Symbols.HasFlag(Manpu.Shock))
        {
            // Lines bursting out all round the head.
            float k = 0.85f + 0.15f * MathF.Sin(t * 30);
            for (int i = 0; i < 9; i++)
            {
                float ang = -MathF.PI * 0.95f + i * MathF.PI * 0.95f / 8 * 2 * 0.5f - MathF.PI * 0.025f;
                ang = -MathF.PI + (i + 0.5f) * MathF.PI / 9;
                var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                float a0 = 1.3f * R, a1 = (i % 2 == 0 ? 1.95f : 1.7f) * R * k;
                r.Line(c + dir * a0, c + dir * a1, ink, 0.5f * S);
            }
        }
        if (Symbols.HasFlag(Manpu.Dizzy))
        {
            // Stars circling the head (the ones behind are smaller and fainter).
            var mid = c + new Vector2(0, -1.15f * R);
            for (int i = 0; i < 3; i++)
            {
                float ang = t * 4.5f + i * MathF.Tau / 3;
                float depth = MathF.Sin(ang);
                var at = mid + new Vector2(MathF.Cos(ang) * 1.05f * R, depth * 0.32f * R);
                Star(r, at, (0.24f + depth * 0.06f) * R, new Color4(1, 0.82f, 0.2f, (0.75f + depth * 0.25f) * fade), ink, t * 3 + i);
            }
        }
        if (Symbols.HasFlag(Manpu.Sparkles))
        {
            for (int i = 0; i < 3; i++)
            {
                float ang = t * 0.6f + i * MathF.Tau / 3;
                float tw = 0.55f + 0.45f * MathF.Sin(t * 5 + i * 2.1f);
                var at = c + new Vector2(MathF.Cos(ang) * 1.6f * R, MathF.Sin(ang) * 1.25f * R - 0.4f * R);
                Star(r, at, 0.38f * R * tw, new Color4(1, 0.93f, 0.45f, fade), new Color4(0.8f, 0.52f, 0.05f, 0.95f * fade), 0, sharp: true);
            }
        }
        if (Symbols.HasFlag(Manpu.Hearts))
        {
            for (int i = 0; i < 2; i++)
            {
                float p = (t * 0.55f + i * 0.5f) % 1;
                var at = c + new Vector2(F * (0.9f + MathF.Sin(p * 6 + i) * 0.2f) * R, -(0.7f + p * 1.6f) * R);
                Heart(r, at, (0.22f + 0.1f * (1 - p)) * R, new Color4(0.93f, 0.25f, 0.42f, (1 - p * p) * fade));
            }
        }
    }

    /// <summary>A star: five points (or four long and four short, for a sparkle).</summary>
    static void Star(Renderer r, Vector2 at, float size, Color4 fill, Color4 edge, float spin, bool sharp = false)
    {
        int n = sharp ? 8 : 10;
        Span<Vector2> pts = stackalloc Vector2[10];
        for (int i = 0; i < n; i++)
        {
            float ang = spin + i * MathF.Tau / n - MathF.PI / 2;
            float rad = sharp ? (i % 2 == 0 ? size : size * 0.28f) : (i % 2 == 0 ? size : size * 0.45f);
            pts[i] = at + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rad;
        }
        var poly = pts[..n];
        Span<Vector2> outer = stackalloc Vector2[10];
        for (int i = 0; i < n; i++) outer[i] = at + (poly[i] - at) * 1.25f;
        r.FillPolygon(outer[..n], edge);
        r.FillPolygon(poly, fill);
    }

    static void Heart(Renderer r, Vector2 at, float s, Color4 c)
    {
        r.Disc(at + new Vector2(-0.5f * s, -0.2f * s), 0.55f * s, c);
        r.Disc(at + new Vector2(0.5f * s, -0.2f * s), 0.55f * s, c);
        r.FillPolygon(stackalloc Vector2[] { at + new Vector2(-1.02f * s, 0), at + new Vector2(1.02f * s, 0), at + new Vector2(0, 1.15f * s) }, c);
    }
}
