using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>A real net: knots joined by strings (Verlet points with length limits; strings go slack when pushed
/// together and only pull when stretched), tied to the frame of a basketball hoop or a soccer goal. It sags, sways
/// when the hoop is moved, swishes as a ball drops through, bulges when a shot hits the back of the goal (and pushes
/// the ball back as it springs out), and gives way to anyone who runs into it.</summary>
sealed class NetCloth
{
    // Knots, in the world, and where they hang at rest in the object's own units.
    readonly Vector2[] _p, _prev, _rest;
    readonly bool[] _pinned;
    // Strings between knots (rest length in object units), and the ones that are drawn.
    readonly (int a, int b, float len)[] _links;
    readonly (int a, int b)[] _drawn;
    // Strings a ball pushes into: how much of the push the ball takes back, and how much of its speed into the net
    // the string soaks up.
    readonly (int a, int b, float share, float soak)[] _catch;
    readonly int _colour;
    readonly float _width;
    // A hoop's net is a tube the ball drops down inside: it spreads the knots and drags them along, it isn't caught.
    bool _tube;
    bool _placed;
    Vector2 _anchor;

    /// <summary>How much the net moved last step (a still net can be drawn into the cached layer).</summary>
    public float Motion { get; private set; } = 1;

    NetCloth(Vector2[] rest, bool[] pinned, List<(int, int, float)> links, List<(int, int)> drawn, List<(int, int, float, float)> caught, int colour, float width)
    {
        _rest = rest; _pinned = pinned;
        _p = new Vector2[rest.Length]; _prev = new Vector2[rest.Length];
        _links = links.ToArray(); _drawn = drawn.ToArray(); _catch = caught.ToArray();
        _colour = colour; _width = width;
    }

    public static NetCloth? For(ItemDef def) => def.Key switch { "hoop" => Hoop(), "goal" => Goal(), _ => null };

    /// <summary>A basketball net: six strands from the rim, narrowing as they hang, knotted into diamonds.</summary>
    static NetCloth Hoop()
    {
        const int strands = 6, rows = 6;
        var rest = new Vector2[strands * rows];
        var pinned = new bool[rest.Length];
        int Id(int s, int r) => r * strands + s;
        for (int r = 0; r < rows; r++)
        {
            float k = r / (float)(rows - 1);
            float x0 = M.Lerp(-14, -9.5f, k * k * 0.6f + k * 0.4f), x1 = M.Lerp(6, 1.5f, k * k * 0.6f + k * 0.4f), y = M.Lerp(72, 53, k);
            for (int s = 0; s < strands; s++) rest[Id(s, r)] = new Vector2(M.Lerp(x0, x1, s / (float)(strands - 1)), y);
        }
        for (int s = 0; s < strands; s++) pinned[Id(s, 0)] = true;
        var links = new List<(int, int, float)>();
        var drawn = new List<(int, int)>();
        var caught = new List<(int, int, float, float)>();
        void Link(int a, int b, bool draw, float share, float soak)
        {
            links.Add((a, b, Vector2.Distance(rest[a], rest[b])));
            if (draw) drawn.Add((a, b));
            if (share > 0) caught.Add((a, b, share, soak));
        }
        for (int r = 0; r + 1 < rows; r++)
            for (int s = 0; s < strands; s++)
            {
                // The diamonds: each knot to the two below it on either side (and straight down, unseen, so it hangs).
                if (s + 1 < strands) { Link(Id(s, r), Id(s + 1, r + 1), true, 0.08f, 0.06f); Link(Id(s + 1, r), Id(s, r + 1), true, 0.08f, 0.06f); }
                Link(Id(s, r), Id(s, r + 1), false, 0, 0);
            }
        // The bottom ring, and a looser ring halfway: a ball dropping through stretches them.
        for (int s = 0; s + 1 < strands; s++) { Link(Id(s, rows - 1), Id(s + 1, rows - 1), true, 0.1f, 0.08f); Link(Id(s, 3), Id(s + 1, 3), false, 0, 0); }
        return new NetCloth(rest, pinned, links, drawn, caught, 7, 0.55f) { _tube = true };
    }

    /// <summary>A soccer goal seen from the side: the netting between the crossbar, the post and the ground, whose
    /// back edge is the back of the net (that's what stops a shot, bulging out behind the frame).</summary>
    static NetCloth Goal()
    {
        const int cols = 7, rows = 6;
        var rest = new Vector2[cols * rows];
        var pinned = new bool[rest.Length];
        int Id(int c, int r) => r * cols + c;
        Vector2 tl = new(-18, 32), tr = new(12, 25), br = new(18, 0), bl = new(-15, 0);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float u = c / (float)(cols - 1), v = r / (float)(rows - 1);
                rest[Id(c, r)] = Vector2.Lerp(Vector2.Lerp(tl, tr, u), Vector2.Lerp(bl, br, u), v);
                // Tied along the crossbar and the post, pegged at the two bottom corners.
                pinned[Id(c, r)] = r == 0 || c == 0 || (r == rows - 1 && c == cols - 1);
            }
        var links = new List<(int, int, float)>();
        var drawn = new List<(int, int)>();
        var caught = new List<(int, int, float, float)>();
        void Link(int a, int b, float share, float soak)
        {
            links.Add((a, b, Vector2.Distance(rest[a], rest[b]) * (share >= 0.1f ? 1.08f : 1.04f)));   // slack: nets hang, the back most
            drawn.Add((a, b));
            if (share > 0) caught.Add((a, b, share, soak));
        }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                // The side netting gives a little to a ball rolling through it; the back edge of the net stops it
                // (the strings across only brush it, or a ball would rest on them like a shelf).
                if (c + 1 < cols) Link(Id(c, r), Id(c + 1, r), 0.04f, 0.02f);
                if (r + 1 < rows) Link(Id(c, r), Id(c, r + 1), c == cols - 1 ? 0.25f : 0.04f, c == cols - 1 ? 0.32f : 0.02f);
            }
        return new NetCloth(rest, pinned, links, drawn, caught, 5, 0.5f);
    }

    /// <summary>One physics step: hang, follow the frame, give way to bodies, pull back into shape.</summary>
    public void Step(Item it, World w, float dt)
    {
        var anchor = it.Local(0, 0);
        // First time, or the whole thing jumped (resized, restored): start at rest.
        if (!_placed || Vector2.Distance(anchor, _anchor) > 400 * it.Sc)
        {
            for (int i = 0; i < _p.Length; i++) _p[i] = _prev[i] = it.Local(_rest[i].X, _rest[i].Y);
            _placed = true;
        }
        _anchor = anchor;
        float g = 900 * it.Sc * World.GravityMul * dt * dt, moved = 0;
        for (int i = 0; i < _p.Length; i++)
        {
            if (_pinned[i]) { var at = it.Local(_rest[i].X, _rest[i].Y); moved += Vector2.Distance(at, _p[i]); _prev[i] = _p[i] = at; continue; }
            var v = (_p[i] - _prev[i]) * 0.97f;
            _prev[i] = _p[i];
            _p[i] += v + new Vector2(0, g);
            moved += v.Length();
        }
        // People walking into the net push it aside (it doesn't push back: it's only string).
        var box = it.Bounds();
        foreach (var f in w.Figures)
        {
            if (f.Mode == Mode.Spawning || !box.IntersectsWith(new System.Drawing.RectangleF(f.Base.X - 60 * f.S, f.Base.Y - 140 * f.S, 120 * f.S, 150 * f.S))) continue;
            float reach = f.LineW * 0.5f + 1.5f * it.Sc;
            for (int i = 0; i < _p.Length; i++)
            {
                if (_pinned[i]) continue;
                foreach (var (a, b) in Figure.Bones)
                {
                    Vector2 pa = f.Jt[a], ab = f.Jt[b] - pa;
                    float t = M.Clamp01(Vector2.Dot(_p[i] - pa, ab) / MathF.Max(ab.LengthSquared(), 1e-4f));
                    Vector2 d = _p[i] - (pa + ab * t);
                    float dist = d.Length();
                    if (dist < reach && dist > 1e-4f) _p[i] += d / dist * (reach - dist);
                }
            }
        }
        Solve(it.Sc);
        Motion = moved;
    }

    void Solve(float sc)
    {
        for (int iter = 0; iter < 6; iter++)
            foreach (var (a, b, len) in _links)
            {
                Vector2 d = _p[b] - _p[a];
                float dist = d.Length(), rest = len * sc;
                if (dist <= rest || dist < 1e-5f) continue;   // string: slack when pushed together
                Vector2 fix = d * ((dist - rest) / dist);
                bool pa = _pinned[a], pb = _pinned[b];
                if (pa && pb) continue;
                if (pa) _p[b] -= fix;
                else if (pb) _p[a] += fix;
                else { _p[a] += fix * 0.5f; _p[b] -= fix * 0.5f; }
            }
    }

    /// <summary>A ball meets the net: the strings it touches move out of its way, it takes back a share of the push
    /// and loses some of its speed into the net. A fast ball can't slip between two steps through the back.</summary>
    public void Collide(Prop ball, float dt)
    {
        float r = ball.Radius;
        if (_tube) { Swish(ball, r); return; }
        Vector2 from = ball.Pos - ball.Vel * dt;
        foreach (var (a, b, share, soak) in _catch)
        {
            Vector2 pa = _p[a], ab = _p[b] - pa;
            float t = M.Clamp01(Vector2.Dot(ball.Pos - pa, ab) / MathF.Max(ab.LengthSquared(), 1e-4f));
            Vector2 q = pa + ab * t, d = ball.Pos - q;
            float dist = d.Length();
            Vector2 n;
            if (soak >= 0.2f && Crosses(from, ball.Pos, pa, _p[b]))
            {
                // Went straight through in one step: it's really on the near side, pressing in.
                Vector2 side = Vector2.Normalize(new Vector2(-ab.Y, ab.X));
                if (Vector2.Dot(from - pa, side) < 0) side = -side;
                n = side; dist = 0;
                ball.Pos = q + side * 0.01f;
            }
            else if (dist >= r || dist < 1e-4f) continue;
            else n = d / dist;
            float pen = r - dist;
            ball.Pos += n * pen * share;
            Vector2 push = -n * pen * (1 - share);
            if (!_pinned[a]) _p[a] += push * (1 - t) * 1.6f;
            if (!_pinned[b]) _p[b] += push * t * 1.6f;
            float vn = Vector2.Dot(ball.Vel, n);
            if (vn < 0) ball.Vel -= vn * n * soak;
        }
    }

    /// <summary>Through the hoop: knots inside the ball are pushed out to its sides (the net flares) and pulled
    /// along with it (the swish), and each one it brushes takes a little of its speed.</summary>
    void Swish(Prop ball, float r)
    {
        int touched = 0;
        for (int i = 0; i < _p.Length; i++)
        {
            if (_pinned[i]) continue;
            Vector2 d = _p[i] - ball.Pos;
            float dist = d.Length();
            if (dist >= r) continue;
            touched++;
            // Out sideways (the tube widens round the ball) and along with it.
            float side = MathF.Abs(d.X) > 1e-3f ? MathF.Sign(d.X) : (i % 2 == 0 ? 1 : -1);
            float out_ = r - MathF.Abs(d.X);
            _p[i] += new Vector2(side * out_ * 0.5f, 0) + ball.Vel * World.Dt * 0.35f;
        }
        if (touched > 0) ball.Vel *= MathF.Pow(0.985f, touched);
    }

    static bool Crosses(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
        static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        Vector2 r = p2 - p1, s = q2 - q1;
        float den = Cross(r, s);
        if (MathF.Abs(den) < 1e-6f) return false;
        float t = Cross(q1 - p1, s) / den, u = Cross(q1 - p1, r) / den;
        return t is >= 0 and <= 1 && u is >= 0 and <= 1;
    }

    public void Draw(Renderer r, Color4 colour, Color4 ink, float sc)
    {
        // A faint dark edge under each string, so a white net still reads against a pale window.
        float w = _width * sc;
        var edge = new Color4(ink.R, ink.G, ink.B, 0.35f);
        foreach (var (a, b) in _drawn) r.Line(_p[a], _p[b], edge, w + 0.9f * sc);
        foreach (var (a, b) in _drawn) r.Line(_p[a], _p[b], colour, w);
    }

    public int ColourIndex => _colour;
    public bool Placed => _placed;

    public System.Drawing.RectangleF Bounds()
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        foreach (var p in _p) { x0 = MathF.Min(x0, p.X); y0 = MathF.Min(y0, p.Y); x1 = MathF.Max(x1, p.X); y1 = MathF.Max(y1, p.Y); }
        return System.Drawing.RectangleF.FromLTRB(x0 - 3, y0 - 3, x1 + 3, y1 + 3);
    }
}
