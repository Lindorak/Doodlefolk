using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>An object on the desktop (chair, bed, pizza, box...). It falls, lands on windows and rides along with
/// them, can be dragged and thrown, and offers surfaces (seats, mattresses, tops) that become platforms the figures
/// can stand, sit or lie on. Figures use it through its verbs (see ItemDef).</summary>
sealed class Item
{
    static int _nextId;
    public readonly int Id = ++_nextId;
    public readonly ItemDef Def;
    public float SizeMul = 1;
    public Color4 Color;
    public bool Flip;
    readonly float _s;

    public Vector2 Pos, Vel;
    public float Angle, Spin;
    /// <summary>The angle it settles at when resting (the user can tip things over or lean them). Tilted things can't be used.</summary>
    public float RestAngle;
    public bool OnGround;
    public IntPtr GroundHwnd;
    public bool Pinned;
    public Vector2 PinTarget, PinOffset;
    public Figure? Holder;           // carried in a hand (food, books)
    public bool Open;               // book being read
    public int BitesLeft;
    public bool Playing = true;     // radio
    public float SwingT, SwingAmp;  // hammock
    public readonly Figure?[] Seated;
    public Figure? User;            // hiding inside / lying on / swinging in it
    public double Born;

    public IntPtr Handle => (IntPtr)(-1000 - Id);
    public float Sc => _s * SizeMul;
    public bool Held => Pinned || Holder != null;
    public bool Free => !Held;

    public Item(ItemDef def, float scale)
    {
        Def = def;
        _s = scale;
        Color = def.Color;
        BitesLeft = def.Bites;
        Seated = new Figure?[Math.Max(1, def.Seats.Length)];
    }

    /// <summary>World position of a point given in this object's local units (x right, y up).</summary>
    public Vector2 Local(float x, float y)
    {
        float sc = Sc;
        var v = new Vector2((Flip ? -x : x) * sc, -y * sc);
        if (Angle != 0) v = M.Rotate(v, Angle);
        return Pos + v;
    }

    public float SeatX(int i) => Local(Def.Seats[i], 0).X;

    // ---------------- physics ----------------

    public void ApplyCarry(Env env)
    {
        if (Free && OnGround) Pos += env.Delta(GroundHwnd);
    }

    public void Step(float dt, World w)
    {
        var env = w.Env;
        if (Def.Verbs.Contains(Verb.Hammock))
        {
            // A hammock with someone in it swings gently; an empty one settles.
            SwingAmp = M.MoveTowards(SwingAmp, User != null ? 1 : 0, dt * 0.5f);
            SwingT += dt * 1.6f;
        }
        if (Holder != null && Holder.Weapon == this)
        {
            Holder.SyncWeapon(this);
            OnGround = false;
            return;
        }
        if (Holder != null)
        {
            Pos = Holder.HoldPoint + new Vector2(0, Def.H * Sc * 0.4f);
            Vel = Holder.HoldVelocity;
            OnGround = false;
            Angle = 0;
            return;
        }
        if (Pinned)
        {
            var target = PinTarget - PinOffset;
            Vel = (target - Pos) / dt;
            Pos = target;
            OnGround = false;
            Angle = M.MoveTowards(Angle, Math.Clamp(-Vel.X / (3000 * _s), -0.5f, 0.5f), dt * 4);
            return;
        }
        if (OnGround)
        {
            if (env.SupportAt(Pos.X, Pos.Y, GroundHwnd) is { } sup && sup.Hwnd != Handle && !(sup.Item != null && (Def.W * SizeMul > 30 || Def.H * SizeMul > 30))) { Pos.Y = sup.Y; GroundHwnd = sup.Hwnd; Vel = default; Angle = M.MoveTowards(Angle, RestAngle, dt * 6); return; }
            OnGround = false;
        }
        float py = Pos.Y;
        Vel.Y = MathF.Min(Vel.Y + 2300 * _s * dt, 4000 * _s);
        Pos += Vel * dt;
        Angle += Spin * dt;
        var (L, R, T) = env.BoundsAt(Pos.X);
        float half = Def.W * Sc * 0.5f;
        if (Pos.X < L + half) { Pos.X = L + half; Vel.X = MathF.Abs(Vel.X) * 0.3f; }
        if (Pos.X > R - half) { Pos.X = R - half; Vel.X = -MathF.Abs(Vel.X) * 0.3f; }
        if (Pos.Y - Def.H * Sc < T) { Pos.Y = T + Def.H * Sc; Vel.Y = MathF.Max(0, Vel.Y); }
        if (Vel.Y > 0 && Landing(env, py) is { } p)
        {
            float impact = Vel.Y;
            Pos.Y = p.Y;
            GroundHwnd = p.Hwnd;
            OnGround = true;
            Vel = default;
            Spin = 0;
            Angle = RestAngle;
            if (impact > 500 * _s) w.Fx.Dust(Pos, _s, (int)Math.Clamp(impact / (250 * _s), 2, 12), impact / (1500 * _s), w.Rng);
            if (impact > 300 * _s) World.Play(Sfx.Thud, Pos, M.Clamp01(impact / (2000 * _s)) * (Def.Carry ? 0.3f : 0.8f), Def.Carry ? 1.6f : 0.8f);
        }
    }

    Platform? Landing(Env env, float py)
    {
        Platform? best = null;
        foreach (var p in env.Platforms)
        {
            if (p.Hwnd == Handle || Pos.X < p.X1 || Pos.X > p.X2) continue;
            // Big things (furniture, sports gear) don't pile up on other objects; small things can sit on them.
            if (p.Item != null && (Def.W * SizeMul > 30 || Def.H * SizeMul > 30)) continue;
            bool hit = p.Solid ? Pos.Y >= p.Y : py <= p.PrevY + 0.5f && Pos.Y >= p.Y;
            if (hit && (best == null || p.Y < best.Y)) best = p;
        }
        return best;
    }

    public void Release(Vector2 vel)
    {
        Pinned = false;
        Vel = M.ClampLength(vel, 3500 * _s);
        Spin = Math.Clamp(vel.X / (900 * _s), -6, 6);
        OnGround = false;
    }

    /// <summary>Surfaces this object offers right now, as platforms (y, x1, x2, bounce).</summary>
    public IEnumerable<(float y, float x1, float x2, float bounce)> Surfaces()
    {
        if (Held || !OnGround || MathF.Abs(Angle) > 0.05f) yield break;
        var d = Def;
        if (d.Surface >= 0)
        {
            float a = Local(d.SurfX1, d.Surface).X, b = Local(d.SurfX2, d.Surface).X;
            yield return (Pos.Y - d.Surface * Sc, MathF.Min(a, b), MathF.Max(a, b), d.Bounce);
        }
        foreach (float sx in d.Seats)
        {
            if (d.Surface >= 0 && MathF.Abs(d.SeatY - d.Surface) < 0.5f) continue;   // the seat is the surface
            float x = Local(sx, 0).X;
            yield return (Pos.Y - d.SeatY * Sc, x - 5 * Sc, x + 5 * Sc, 0);
        }
        if (d.Verbs.Contains(Verb.Hammock))
        {
            var (c, _) = HammockCentre();
            yield return (c.Y, c.X - 26 * Sc, c.X + 26 * Sc, 0.15f);
        }
    }

    /// <summary>Middle of the hammock's net (where a body lies) and its sideways swing.</summary>
    public (Vector2 centre, float swing) HammockCentre()
    {
        float swing = MathF.Sin(SwingT) * 4 * SwingAmp;
        float sag = User != null ? 13 : 17;
        return (Local(swing, sag), swing);
    }

    // ---------------- drawing ----------------

    static readonly Color4 Ink = new(0.12f, 0.11f, 0.1f, 0.9f);
    readonly Vector2[] _pts = new Vector2[48];

    Color4 Col(int c, float alpha = 1)
    {
        Color4 k = c switch
        {
            0 => Color,
            1 => M.Shade(Color, 0.72f),
            2 => Color4.Lerp(Color, new Color4(1, 1, 1, 1), 0.35f),
            _ => ItemDef.Fixed[Math.Clamp(c, 3, ItemDef.Fixed.Length - 1)],
        };
        return new Color4(k.R, k.G, k.B, alpha);
    }

    public void Draw(Renderer r, bool over, double time)
    {
        float k = 1;
        if (Def.Verbs.Contains(Verb.Eat)) k = MathF.Sqrt(MathF.Max(0.15f, BitesLeft / (float)Def.Bites));
        if (Def.Verbs.Contains(Verb.Read) && Open) { if (!over) DrawOpenBook(r); return; }
        foreach (var sh in Def.Shapes)
        {
            if (sh.Over != over) continue;
            if (sh.WhenUsed && User == null && Seated.All(s => s == null)) continue;
            DrawShape(r, sh, k);
        }
        if (over) return;
        if (Def.Verbs.Contains(Verb.Hammock)) DrawHammock(r);
        if (Def.Verbs.Contains(Verb.Warm)) DrawFire(r, time);
        if (Def.Verbs.Contains(Verb.Dance) && Playing && Free && OnGround) DrawNotes(r, time);
    }

    void DrawShape(Renderer r, Shape sh, float k)
    {
        var p = sh.P;
        float sc = Sc;
        var pts = _pts;
        int n = 0;
        void Pt(float x, float y) { if (n < pts.Length) pts[n++] = Local(x * k, y * k); }
        switch (sh.Kind)
        {
            case 'r': Pt(p[0], p[1]); Pt(p[2], p[1]); Pt(p[2], p[3]); Pt(p[0], p[3]); break;
            case 'o':
            {
                float rad = MathF.Min(p[4], MathF.Min(p[2] - p[0], p[3] - p[1]) * 0.5f);
                void Corner(float cx, float cy, float a0)
                {
                    for (int i = 0; i <= 4; i++) { float a = a0 + i * MathF.PI / 8; Pt(cx + MathF.Cos(a) * rad, cy + MathF.Sin(a) * rad); }
                }
                Corner(p[2] - rad, p[1] + rad, -MathF.PI / 2); Corner(p[2] - rad, p[3] - rad, 0);
                Corner(p[0] + rad, p[3] - rad, MathF.PI / 2); Corner(p[0] + rad, p[1] + rad, MathF.PI);
                break;
            }
            case 'e':
                for (int i = 0; i < 20; i++) { float a = i * MathF.Tau / 20; Pt(p[0] + MathF.Cos(a) * p[2], p[1] + MathF.Sin(a) * p[3]); }
                break;
            case 'p':
                for (int i = 0; i + 1 < p.Length; i += 2) Pt(p[i], p[i + 1]);
                break;
            case 'l':
                r.Line(Local(p[0] * k, p[1] * k), Local(p[2] * k, p[3] * k), Ink, (sh.W + 1.2f) * sc * k);
                r.Line(Local(p[0] * k, p[1] * k), Local(p[2] * k, p[3] * k), Col(sh.Col), sh.W * sc * k);
                return;
            case 'c':
                for (int i = 0; i + 3 < p.Length; i += 2) r.Line(Local(p[i] * k, p[i + 1] * k), Local(p[i + 2] * k, p[i + 3] * k), Col(sh.Col), sh.W * sc * k);
                return;
        }
        if (n < 3) return;
        r.FillPolygon(pts.AsSpan(0, n), Col(sh.Col));
        if (sh.NoOutline) return;
        float ow = 1.1f * sc;
        for (int i = 0; i < n; i++) r.Line(pts[i], pts[(i + 1) % n], Ink, ow);
    }

    void DrawHammock(Renderer r)
    {
        float sc = Sc;
        var (c, swing) = HammockCentre();
        Vector2 a = Local(-44, 42), b = Local(44, 42);
        Span<Vector2> band = stackalloc Vector2[34];
        // A sagging band between the post tops, deepest in the middle.
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            Vector2 top = Vector2.Lerp(a, b, t) + new Vector2(0, MathF.Sin(MathF.PI * t) * (c.Y - (a.Y + b.Y) * 0.5f)) + new Vector2(swing * sc * MathF.Sin(MathF.PI * t), 0);
            band[i] = top;
            band[33 - i] = top + new Vector2(0, (2 + 4 * MathF.Sin(MathF.PI * t)) * sc);
        }
        r.FillPolygon(band, Col(0));
        for (int i = 0; i < 16; i++) r.Line(band[i], band[i + 1], Ink, 1.1f * sc);
        for (int i = 2; i < 15; i += 3) r.Line(band[i], band[33 - i], Col(1), 0.8f * sc);
    }

    void DrawFire(Renderer r, double time)
    {
        float sc = Sc, t = (float)time;
        Vector2 b = Local(0, 3);
        r.Oval(b + new Vector2(0, -5 * sc), 22 * sc, 12 * sc, new Color4(1, 0.55f, 0.15f, 0.12f + 0.04f * MathF.Sin(t * 9)));
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 5, h = 13 + 4 * MathF.Sin(t * (7 + i) + i * 2), wob = 1.5f * MathF.Sin(t * 11 + i);
            Span<Vector2> fl = stackalloc Vector2[] { Local(x - 4.5f, 3), Local(x + wob, h), Local(x + 4.5f, 3) };
            r.FillPolygon(fl, ItemDef.Fixed[19]);
            Span<Vector2> inner = stackalloc Vector2[] { Local(x - 2.2f, 3), Local(x + wob * 0.6f, h * 0.6f), Local(x + 2.2f, 3) };
            r.FillPolygon(inner, ItemDef.Fixed[20]);
        }
    }

    void DrawNotes(Renderer r, double time)
    {
        float sc = Sc;
        for (int i = 0; i < 3; i++)
        {
            float ph = (float)((time * 0.6 + i / 3.0) % 1.0);
            Vector2 at = Local(-6 + i * 6 + MathF.Sin(ph * 9 + i) * 3, 16 + ph * 22);
            r.Text(i % 2 == 0 ? "♪" : "♫", at, 9 * sc, Ui.Ink.A((1 - ph) * 0.85f));
        }
    }

    void DrawOpenBook(Renderer r)
    {
        float sc = Sc;
        Span<Vector2> left = stackalloc Vector2[] { Local(0, 0), Local(-9, 1.5f), Local(-9, 11.5f), Local(0, 10) };
        Span<Vector2> right = stackalloc Vector2[] { Local(0, 0), Local(9, 1.5f), Local(9, 11.5f), Local(0, 10) };
        r.FillPolygon(left, ItemDef.Fixed[7]); r.FillPolygon(right, ItemDef.Fixed[7]);
        for (int i = 0; i < 4; i++) { r.Line(left[i], left[(i + 1) % 4], Col(1), 1.2f * sc); r.Line(right[i], right[(i + 1) % 4], Col(1), 1.2f * sc); }
        for (int i = 0; i < 3; i++) { r.Line(Local(-7, 4 + i * 2.5f), Local(-2, 3.6f + i * 2.5f), Ink, 0.6f * sc); r.Line(Local(2, 3.6f + i * 2.5f), Local(7, 4 + i * 2.5f), Ink, 0.6f * sc); }
    }

    // ---------------- redraw tracking ----------------

    int _lastKey;

    /// <summary>Does this object need redrawing this frame (it moved, changed, animates, or someone's using it)?</summary>
    public bool Changed()
    {
        bool animated = Held || !OnGround || Pinned || Def.Verbs.Contains(Verb.Warm) || (Def.Verbs.Contains(Verb.Dance) && Playing)
                     || (Def.Verbs.Contains(Verb.Hammock) && SwingAmp > 0.01f);
        int key = HashCode.Combine(HashCode.Combine(MathF.Round(Pos.X), MathF.Round(Pos.Y), MathF.Round(Angle * 100), SizeMul, Color.GetHashCode(), Flip, Open, BitesLeft),
                                   User?.Id ?? 0, Seated.Count(s => s != null));
        bool changed = key != _lastKey;
        _lastKey = key;
        return animated || changed;
    }

    // ---------------- bounds + hit testing ----------------

    public System.Drawing.RectangleF Bounds()
    {
        float sc = Sc, w = Def.W * sc, h = Def.H * sc, pad = 6 * sc;
        if (Def.Verbs.Contains(Verb.Warm)) h += 14 * sc;
        if (Def.Verbs.Contains(Verb.Dance)) h += 30 * sc;
        if (Def.Verbs.Contains(Verb.Read)) w = MathF.Max(w, 20 * sc);
        float ext = MathF.Max(w, h) * MathF.Abs(MathF.Sin(Angle));
        return System.Drawing.RectangleF.FromLTRB(Pos.X - w / 2 - pad - ext, Pos.Y - h - pad - ext, Pos.X + w / 2 + pad + ext, Pos.Y + pad + ext);
    }

    public bool HitTest(Vector2 p)
    {
        Vector2 d = p - Pos;
        if (Angle != 0) d = M.Rotate(d, -Angle);
        float sc = Sc;
        return MathF.Abs(d.X) < Def.W * sc * 0.5f + 2 * sc && d.Y < 3 * sc && d.Y > -Def.H * sc - 2 * sc;
    }

    public void Shadow(Env env, out Vector2 c, out float rx, out float ry, out float a)
    {
        c = default; rx = ry = a = 0;
        if (Held && Holder != null) return;
        if (env.Below(Pos.X, Pos.Y - 2 * _s) is not { } p) return;
        float k = M.Clamp01(1 - MathF.Max(0, p.Y - Pos.Y) / (300 * _s));
        c = new Vector2(Pos.X, p.Y);
        rx = Def.W * Sc * 0.48f * (0.6f + 0.4f * k);
        ry = 2.2f * _s;
        a = 0.18f * k;
    }
}
