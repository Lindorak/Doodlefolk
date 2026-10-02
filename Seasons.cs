using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum Season { Spring, Summer, Autumn, Winter }

/// <summary>The time of year on the desktop: in autumn, gusts bring leaves tumbling down to settle on window tops
/// (and figures kick them up as they run through); in spring, blossom petals; on summer nights, fireflies. Winter has
/// the snow. Light on the CPU: only the few things moving are redrawn.</summary>
sealed class Seasons
{
    public Season Now = Season.Spring;
    public Season? Override;

    struct Bit { public Vector2 Pos, Vel; public float Rot, RotV, Size, Phase, Age; public Color4 Col; public bool Down; }
    readonly List<Bit> _air = new(), _fallen = new();
    readonly List<Bit> _flies = new();
    double _gustUntil = -1, _nextGust = 25;
    public readonly List<RectangleF> Dirty = new();

    static Season FromDate(DateTime d) => d.Month switch { 3 or 4 or 5 => Season.Spring, 6 or 7 or 8 => Season.Summer, 9 or 10 or 11 => Season.Autumn, _ => Season.Winter };

    public void Gust(double now) { _gustUntil = now + 22; _nextGust = now + 400; }

    public void Step(World w, float dt, double now)
    {
        Now = Override ?? FromDate(DateTime.Now);
        Dirty.Clear();
        var rng = w.Rng;
        float S = w.Scale;
        var env = w.Env;
        var v = env.Virtual;
        bool leafy = Now is Season.Autumn or Season.Spring;
        if (leafy && w.Celebrations && now > _nextGust) { _gustUntil = now + rng.Range(15, 28); _nextGust = now + rng.Range(150, 360); }
        // A gust: leaves (or petals) come in from above, a few at a time.
        if (leafy && now < _gustUntil && _air.Count < 30 && rng.NextDouble() < dt * 0.9)
        {
            float x = rng.Range(v.Left + 40, v.Right - 40);
            var (_, _, top) = env.BoundsAt(x);
            Color4 col = Now == Season.Autumn
                ? new[] { M.Hex(0xE65100), M.Hex(0xF9A825), M.Hex(0xC62828), M.Hex(0x8D6E63), M.Hex(0xFB8C00) }[rng.Next(5)]
                : new[] { M.Hex(0xF8BBD0), M.Hex(0xFCE4EC), M.Hex(0xF48FB1) }[rng.Next(3)];
            _air.Add(new Bit { Pos = new Vector2(x, top - 10 * S), Rot = rng.Range(0, 6.3f), RotV = rng.Range(-3, 3), Size = rng.Range(0.8f, 1.3f) * (Now == Season.Spring ? 0.7f : 1), Phase = rng.Range(0, 6.3f), Col = col });
            w.Sticker("seasons");
        }
        float wind = MathF.Sin((float)now * 0.3f) * 60 * S + 40 * S;
        for (int i = _air.Count - 1; i >= 0; i--)
        {
            var b = _air[i];
            var prev = b.Pos;
            if (b.Down) { b.Vel.Y += 1500 * S * dt; b.Vel.X *= MathF.Pow(0.3f, dt); }   // kicked up: falls back
            else b.Vel = new Vector2(wind + MathF.Sin((float)now * 1.7f + b.Phase) * 50 * S, (40 + 15 * b.Size) * S);
            b.Pos += b.Vel * dt;
            b.Rot += b.RotV * dt;
            bool gone = b.Pos.Y > v.Bottom + 10 || b.Pos.X < v.Left - 50 || b.Pos.X > v.Right + 50;
            if (!gone && b.Pos.Y > prev.Y && env.FindLanding(b.Pos.X, prev.Y, b.Pos.Y) is { } p)
            {
                b.Pos.Y = p.Y - 1; b.Down = false; b.Age = 0; b.Vel = Vector2.Zero;
                _fallen.Add(b);
                if (_fallen.Count > 90) _fallen.RemoveAt(0);
                gone = true;
            }
            Dirty.Add(Box(prev, S)); Dirty.Add(Box(b.Pos, S));
            if (gone) _air.RemoveAt(i); else _air[i] = b;
        }
        // Fallen leaves: kicked up by anyone running through; fade away after a while.
        for (int i = _fallen.Count - 1; i >= 0; i--)
        {
            var b = _fallen[i];
            b.Age += dt;
            if (b.Age > 900) { Dirty.Add(Box(b.Pos, S)); _fallen.RemoveAt(i); continue; }
            foreach (var f in w.Figures)
            {
                if (f.Mode != Mode.Control || MathF.Abs(f.Vel.X) < f.WalkSpeed * 1.2f) continue;
                if (Vector2.Distance(f.Jt[J.FootN], b.Pos) < 10 * S || Vector2.Distance(f.Jt[J.FootF], b.Pos) < 10 * S)
                {
                    b.Down = true;
                    b.Vel = new Vector2(MathF.Sign(f.Vel.X) * rng.Range(60, 220) * S, -rng.Range(150, 330) * S);
                    b.Pos.Y -= 3;
                    _air.Add(b);
                    _fallen.RemoveAt(i);
                    Dirty.Add(Box(b.Pos, S));
                    break;
                }
            }
            if (i < _fallen.Count) _fallen[i] = b;
        }
        if (!leafy && _air.Count == 0 && _fallen.Count > 0 && rng.NextDouble() < dt * 0.05) { Dirty.Add(Box(_fallen[0].Pos, S)); _fallen.RemoveAt(0); }

        // Fireflies on summer nights, drifting low over the floors.
        bool flies = Now == Season.Summer && w.Night > 0.3f && w.Celebrations;
        if (flies && _flies.Count < 12 && rng.NextDouble() < dt * 0.5)
        {
            var floors = env.Platforms.Where(p => p.X2 - p.X1 > 120 * S).ToList();
            if (floors.Count > 0)
            {
                var p = floors[rng.Next(floors.Count)];
                _flies.Add(new Bit { Pos = new Vector2(rng.Range(p.X1, p.X2), p.Y - rng.Range(20, 120) * S), Phase = rng.Range(0, 6.3f), Size = rng.Range(0.8f, 1.2f) });
                w.Sticker("seasons");
            }
        }
        for (int i = _flies.Count - 1; i >= 0; i--)
        {
            var b = _flies[i];
            var prev = b.Pos;
            b.Age += dt;
            b.Pos += new Vector2(MathF.Sin((float)now * 0.7f + b.Phase) * 25 * S, MathF.Cos((float)now * 0.9f + b.Phase * 1.3f) * 12 * S) * dt;
            Dirty.Add(Box(prev, S * 2)); Dirty.Add(Box(b.Pos, S * 2));
            if (!flies && b.Age > 2) { _flies.RemoveAt(i); continue; }
            _flies[i] = b;
        }
    }

    static RectangleF Box(Vector2 p, float s) => new(p.X - 9 * s, p.Y - 9 * s, 18 * s, 18 * s);

    /// <summary>Leaves lying on surfaces (under figures).</summary>
    public void DrawFallen(Renderer r, float S, Func<RectangleF, bool> dirty)
    {
        foreach (var b in _fallen) if (dirty(Box(b.Pos, S))) Leaf(r, b, S, M.Clamp01((900 - b.Age) / 60));
    }

    /// <summary>Leaves in the air and fireflies (in front of everything).</summary>
    public void DrawAir(Renderer r, float S, double now)
    {
        foreach (var b in _air) Leaf(r, b, S, 1);
        foreach (var b in _flies)
        {
            float glow = 0.5f + 0.5f * MathF.Sin((float)now * 3 + b.Phase * 3);
            r.Disc(b.Pos, 5 * S * b.Size, new Color4(0.9f, 1, 0.4f, 0.12f * glow));
            r.Disc(b.Pos, 1.6f * S * b.Size, new Color4(0.95f, 1, 0.55f, 0.5f + 0.5f * glow));
        }
    }

    void Leaf(Renderer r, Bit b, float S, float alpha)
    {
        float s = 4.2f * S * b.Size;
        Vector2 d = new(MathF.Cos(b.Rot), MathF.Sin(b.Rot)), n = new(-d.Y, d.X);
        var c = b.Col.A(alpha);
        if (Now == Season.Spring)
        {
            r.Oval(b.Pos, s * 0.55f, s * 0.4f, c);
            return;
        }
        r.FillPolygon(stackalloc Vector2[] { b.Pos - d * s, b.Pos + n * s * 0.45f, b.Pos + d * s, b.Pos - n * s * 0.45f }, c);
        r.Line(b.Pos - d * s * 1.15f, b.Pos + d * s * 0.8f, new Color4(c.R * 0.6f, c.G * 0.6f, c.B * 0.6f, c.A * 0.8f), 0.6f * S);
    }
}
