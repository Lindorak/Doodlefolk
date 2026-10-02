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
        if (Def.Verbs.Contains(Verb.Shelter) && Open && Holder is { } holder) { if (over) DrawOpenUmbrella(r, holder); return; }
        bool detail = Gfx.Q.DetailedArt;
        foreach (var sh in Def.Shapes)
        {
            if (sh.Over != over || (sh.Detail && !detail)) continue;
            if (sh.WhenUsed && User == null && Seated.All(s => s == null)) continue;
            DrawShape(r, sh, k);
        }
        if (over) return;
        if (Def.Verbs.Contains(Verb.Hammock)) DrawHammock(r);
        if (Def.Verbs.Contains(Verb.Warm)) DrawFire(r, time);
        if (Def.Verbs.Contains(Verb.Dance) && Playing && Free && OnGround) DrawNotes(r, time);
    }

    /// <summary>A faint, offset silhouette cast onto the window behind (drop shadows).</summary>
    public void DrawDropShadow(Renderer r)
    {
        if (Holder != null || Def.Key == "hammock") return;
        float k = Def.Verbs.Contains(Verb.Eat) ? MathF.Sqrt(MathF.Max(0.15f, BitesLeft / (float)Def.Bites)) : 1;
        foreach (var sh in Def.Shapes)
        {
            if (sh.Detail || (sh.WhenUsed && User == null && Seated.All(s => s == null))) continue;
            DrawShape(r, sh, k, Gfx.DropOffset * _s);
        }
    }

    void DrawShape(Renderer r, Shape sh, float k, Vector2? shadow = null)
    {
        var p = sh.P;
        float sc = Sc;
        var pts = _pts;
        int n = 0;
        // Polygons are built in the object's own units (cached once per shape) and placed with a transform.
        void Pt(float x, float y) { if (n < pts.Length) pts[n++] = new Vector2(x * k, y * k); }
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
                if (shadow is Vector2 so)
                {
                    r.Line(Local(p[0] * k, p[1] * k) + so, Local(p[2] * k, p[3] * k) + so, DropInk, (sh.W + 1.5f) * sc * k);
                    return;
                }
                if (!sh.NoOutline) r.Line(Local(p[0] * k, p[1] * k), Local(p[2] * k, p[3] * k), Ink, (sh.W + 1.2f) * sc * k);
                if (Gfx.Q.Shading && sh.W >= 1.5f && !sh.Detail) r.ShadedLine(Local(p[0] * k, p[1] * k), Local(p[2] * k, p[3] * k), Col(sh.Col), sh.W * sc * k);
                else r.Line(Local(p[0] * k, p[1] * k), Local(p[2] * k, p[3] * k), Col(sh.Col), sh.W * sc * k);
                return;
            case 'c':
                if (shadow is Vector2 sc2)
                {
                    for (int i = 0; i + 3 < p.Length; i += 2) r.Line(Local(p[i] * k, p[i + 1] * k) + sc2, Local(p[i + 2] * k, p[i + 3] * k) + sc2, DropInk, sh.W * sc * k);
                    return;
                }
                for (int i = 0; i + 3 < p.Length; i += 2) r.Line(Local(p[i] * k, p[i + 1] * k), Local(p[i + 2] * k, p[i + 3] * k), Col(sh.Col), sh.W * sc * k);
                return;
        }
        if (n < 3) return;
        var world = Matrix3x2.CreateScale(Flip ? -sc : sc, -sc) * Matrix3x2.CreateRotation(Angle) * Matrix3x2.CreateTranslation(Pos);
        if (shadow is Vector2 off)
        {
            r.CachedShape(sh, (int)MathF.Round(k * 100), pts.AsSpan(0, n), world * Matrix3x2.CreateTranslation(off), DropInk, DropInk, 0);
            return;
        }
        r.CachedShape(sh, (int)MathF.Round(k * 100), pts.AsSpan(0, n), world, Col(sh.Col), Ink, sh.NoOutline ? 0 : 1.1f, !sh.Detail && !sh.NoOutline);
        if (Gfx.Q.DetailedArt && !sh.Detail && !sh.NoOutline) Texture(r, sh, pts.AsSpan(0, n), k);
    }

    static Color4 DropInk => Gfx.DropInk;

    /// <summary>Detailed art: a texture for a part, from what it's made of (wood grain, stitching, a shine...).</summary>
    void Texture(Renderer r, Shape sh, ReadOnlySpan<Vector2> pts, float k)
    {
        Vector2 mn = pts[0], mx = pts[0];
        foreach (var q in pts) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
        float w = mx.X - mn.X, h = mx.Y - mn.Y, sc = Sc;
        if (w < 2.5f && h < 2.5f) return;
        var mat = sh.Col switch { 3 or 4 or 13 => Material.Wood, 5 or 6 => Material.Metal, 7 => Material.Fabric, <= 2 => Def.Material, _ => Material.Plain };
        var baseCol = Col(sh.Col);
        void Ln(float x0, float y0, float x1, float y1, Color4 c, float width) => r.Line(Local(x0, y0), Local(x1, y1), c, width * sc);
        switch (mat)
        {
            case Material.Wood:
            {
                var grain = Gfx.Darker(baseCol, 0.3f).A(0.4f);
                if (w >= h)
                    for (int i = 1; i <= (h > 6 ? 3 : 2); i++)
                    {
                        float y = mn.Y + h * i / (h > 6 ? 4f : 3f);
                        Ln(mn.X + w * (0.08f + 0.05f * i), y, mx.X - w * (0.1f + 0.04f * i), y + h * 0.04f, grain, 0.45f);
                    }
                else
                    for (int i = 1; i <= 2; i++)
                    {
                        float x = mn.X + w * i / 3f;
                        Ln(x, mn.Y + h * 0.08f, x + w * 0.05f, mx.Y - h * 0.12f, grain, 0.45f);
                    }
                break;
            }
            case Material.Metal:
            {
                var shine = new Color4(1, 1, 1, 0.55f);
                if (h > w * 1.6f) Ln(mn.X + w * 0.3f, mn.Y + h * 0.15f, mn.X + w * 0.3f, mx.Y - h * 0.15f, shine, MathF.Min(0.9f, w * 0.18f));
                else Ln(mn.X + w * 0.2f, mx.Y - h * 0.2f, mn.X + w * 0.45f, mx.Y - h * 0.08f, shine, 0.8f);
                break;
            }
            case Material.Fabric:
            {
                if (sh.Kind is not ('r' or 'o') || w < 6 || h < 4) break;
                // Stitching just inside the edge.
                var thread = Gfx.Lighter(baseCol, 0.5f).A(0.55f);
                float i0 = 1.3f;
                void Dashes(float x0, float y0, float x1, float y1)
                {
                    float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    for (float t = 0.6f; t < len - 0.6f; t += 2.6f)
                    {
                        float a = t / len, b = MathF.Min(len, t + 1.4f) / len;
                        Ln(x0 + (x1 - x0) * a, y0 + (y1 - y0) * a, x0 + (x1 - x0) * b, y0 + (y1 - y0) * b, thread, 0.4f);
                    }
                }
                Dashes(mn.X + i0 + 1, mx.Y - i0, mx.X - i0 - 1, mx.Y - i0);
                if (h > 7) Dashes(mn.X + i0 + 1, mn.Y + i0, mx.X - i0 - 1, mn.Y + i0);
                break;
            }
            case Material.Plastic:
                if (w > 3 && h > 2.5f) r.Oval(Local(mn.X + w * 0.28f, mx.Y - h * 0.26f), w * 0.13f * sc, h * 0.09f * sc, new Color4(1, 1, 1, 0.38f));
                break;
            case Material.Cardboard:
            {
                var fold = Gfx.Darker(baseCol, 0.25f).A(0.45f);
                if (w > 8) for (float x = mn.X + 3; x < mx.X - 2; x += 3.2f) Ln(x, mx.Y - 0.6f, x + 0.8f, mx.Y - 1.6f, fold, 0.35f);
                break;
            }
            case Material.Food:
            {
                var speck = Gfx.Darker(baseCol, 0.25f).A(0.5f);
                int seed = Def.Key.Length * 7 + sh.P.Length;
                for (int i = 0; i < 4; i++)
                {
                    float fx = (float)((Math.Sin(seed * 12.9898 + i * 78.233) * 43758.5453) % 1 + 1) % 1, fy = (float)((Math.Sin(seed * 39.3468 + i * 11.135) * 24634.6345) % 1 + 1) % 1;
                    r.Disc(Local(mn.X + w * (0.2f + fx * 0.6f), mn.Y + h * (0.2f + fy * 0.6f)), 0.35f * sc, speck);
                }
                break;
            }
        }
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

    /// <summary>An umbrella up over its holder's head.</summary>
    void DrawOpenUmbrella(Renderer r, Figure h)
    {
        float sc = Sc;
        Vector2 hand = h.Jt[J.HandN], top = h.Jt[J.Head] + new Vector2(0, -(h.HeadR + 10 * sc));
        var shaft = new Color4(0.25f, 0.2f, 0.15f, 1);
        r.Line(hand, top, shaft, 1.4f * sc);
        Span<Vector2> canopy = stackalloc Vector2[14];
        float rx = 17 * sc, ry = 9 * sc;
        for (int i = 0; i < 13; i++)
        {
            float a = MathF.PI + MathF.PI * i / 12;
            canopy[i] = top + new Vector2(MathF.Cos(a) * rx, 4 * sc + MathF.Sin(a) * ry);
        }
        canopy[13] = canopy[0];
        r.FillPolygon(canopy[..13], Color);
        var dark = new Color4(Color.R * 0.6f, Color.G * 0.6f, Color.B * 0.6f, 1);
        for (int i = 0; i < 12; i++) r.Line(canopy[i], canopy[i + 1], dark, 1.1f * sc);
        r.Line(canopy[0], canopy[12], dark, 1.1f * sc);
        for (int i = 3; i <= 9; i += 3) r.Line(top, canopy[i], dark, 0.8f * sc);
        r.Disc(top + new Vector2(0, -ry + 3 * sc), 1.2f * sc, shaft);
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
                                   User?.Id ?? 0, Seated.Count(s => s != null), OwnerId);
        bool changed = key != _lastKey;
        _lastKey = key;
        return animated || changed;
    }

    // ---------------- bounds + hit testing ----------------

    /// <summary>Someone's home: who lives here (0: nobody), shown as a little flag in their colour.</summary>
    public int OwnerId;
    /// <summary>Put out by a figure for a while (a campfire for the night) or a seasonal decoration: not saved.</summary>
    public bool Temporary;
    public string OwnerName = "";
    public Color4 OwnerColour;
    public float Scale => _s;

    public System.Drawing.RectangleF? FlagRect()
    {
        if (OwnerId == 0) return null;
        float sc = Sc, x = Pos.X + Def.W * sc * 0.28f, top = Pos.Y - Def.H * sc - 24 * _s;
        return System.Drawing.RectangleF.FromLTRB(x - 3 * _s, top, x + 26 * _s + (OwnerName.Length + 2) * 5.4f * _s, Pos.Y - Def.H * sc * 0.5f);
    }

    public System.Drawing.RectangleF Bounds()
    {
        var b = BoundsBody();
        return FlagRect() is { } fr ? System.Drawing.RectangleF.Union(b, fr) : b;
    }

    System.Drawing.RectangleF BoundsBody()
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
