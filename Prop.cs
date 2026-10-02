using System.Globalization;
using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum PropKind { Ball, SoccerBall, Basketball, BeachBall }

/// <summary>A physics ball the figures can kick, juggle, carry and throw. Bounces off window tops,
/// the taskbar and screen edges, rolls with spin, rides moving windows, and knocks figures over.</summary>
sealed class Prop
{
    static int _nextId;
    public readonly int Id = ++_nextId;
    public PropKind Kind;
    public Color4 Color = M.Hex(0xE53935);
    /// <summary>User size multiplier (1 = default) and bounciness (0..1).</summary>
    public float SizeMul = 1, Bounce = 0.65f;
    readonly float _s;

    public Vector2 Pos, Vel;
    public float Angle, Spin;
    public bool OnGround;
    public IntPtr GroundHwnd;
    public Figure? Holder;
    public bool Pinned;
    public Vector2 PinTarget;
    /// <summary>Who last kicked/threw it (null = the user's mouse), and how long ago.</summary>
    public Figure? LastTouch;
    public bool ThrownByUser;
    public float SinceTouch = 99, SinceBounce = 99;
    /// <summary>Set when someone kicks/throws it at a teammate.</summary>
    public Figure? PassTarget;
    /// <summary>A figure currently juggling it: their body doesn't collide with it.</summary>
    public Figure? Juggler;

    public Prop(PropKind kind, float scale)
    {
        Kind = kind;
        _s = scale;
        Color = kind switch
        {
            PropKind.Basketball => M.Hex(0xE8762B),
            PropKind.SoccerBall or PropKind.BeachBall => M.Hex(0xF5F5F5),
            _ => M.Hex(0xE53935),
        };
        Bounce = kind switch { PropKind.Basketball => 0.78f, PropKind.BeachBall => 0.55f, PropKind.SoccerBall => 0.62f, _ => 0.68f };
        SizeMul = kind == PropKind.BeachBall ? 1.6f : 1;
    }

    public float S => _s;
    public float Radius => 6.5f * _s * SizeMul;
    /// <summary>Relative to a default-size ball; 2D so mass grows with area.</summary>
    public float Mass => SizeMul * SizeMul * (Kind == PropKind.BeachBall ? 0.35f : 1f);
    public float Grav => 2300 * _s * (Kind == PropKind.BeachBall ? 0.55f : 1f);
    public bool Free => Holder == null && !Pinned;

    public static string KindName(PropKind k) => k switch
    {
        PropKind.SoccerBall => SoccerWord(),
        PropKind.Basketball => "Basketball",
        PropKind.BeachBall => "Beach ball",
        _ => "Ball",
    };

    /// <summary>"Soccer ball" where English speakers say soccer, "Football" everywhere else.</summary>
    static string SoccerWord()
    {
        string region = "";
        try { region = RegionInfo.CurrentRegion.TwoLetterISORegionName; } catch (ArgumentException) { }
        return region is "US" or "CA" or "AU" or "NZ" ? "Soccer ball" : "Football";
    }

    public void Kick(Vector2 vel, Figure? by)
    {
        Vel = vel / MathF.Sqrt(Mass);
        Spin = -Vel.X / Radius * 0.6f;
        OnGround = false;
        LastTouch = by;
        ThrownByUser = by == null;
        SinceTouch = 0;
    }

    public void Release(Vector2 vel)
    {
        Holder = null;
        Pinned = false;
        Vel = vel;
        OnGround = false;
        SinceTouch = 0;
    }

    public void ApplyCarry(Env env)
    {
        if (Free && OnGround) { var d = env.Delta(GroundHwnd); Pos += d; _prevPos += d; }
    }

    // Drawn between the last two simulation steps (see Figure.BeginInterp).
    Vector2 _prevPos, _savePos;
    float _prevAngle, _saveAngle;
    bool _interp;

    public void BeginInterp(float a)
    {
        _savePos = Pos; _saveAngle = Angle;
        _interp = true;
        if (a >= 1 || Vector2.DistanceSquared(_prevPos, Pos) > Radius * Radius * 100) return;
        Pos = Vector2.Lerp(_prevPos, Pos, a);
        Angle = _prevAngle + (Angle - _prevAngle) * a;
    }

    public void EndInterp()
    {
        if (!_interp) return;
        Pos = _savePos; Angle = _saveAngle;
        _interp = false;
    }

    public void Step(float dt, World w)
    {
        _prevPos = Pos;
        _prevAngle = Angle;
        SinceTouch += dt;
        SinceBounce += dt;
        if (Holder != null)
        {
            Pos = Holder.HoldPoint;
            Vel = Holder.HoldVelocity;
            Spin *= 1 - 3 * dt;
            Angle += Spin * dt;
            OnGround = false;
            return;
        }
        if (Pinned)
        {
            Vel = (PinTarget - Pos) / dt;
            Pos = PinTarget;
            Angle += Spin * dt;
            OnGround = false;
            return;
        }

        var env = w.Env;
        float r = Radius;
        if (OnGround && env.SupportAt(Pos.X, Pos.Y + r, GroundHwnd) == null) OnGround = false;

        if (!OnGround) Vel.Y += Grav * dt;
        float drag = Kind == PropKind.BeachBall ? 0.9f : 0.15f;
        Vel *= 1 - drag * dt;
        Vector2 prev = Pos;
        Pos += Vel * dt;

        var (L, R, T) = env.BoundsAt(Pos.X);
        if (Pos.X < L + r) { Pos.X = L + r; Vel.X = MathF.Abs(Vel.X) * Bounce; Spin = -Spin * 0.5f; Bounced(w, MathF.Abs(Vel.X)); }
        if (Pos.X > R - r) { Pos.X = R - r; Vel.X = -MathF.Abs(Vel.X) * Bounce; Spin = -Spin * 0.5f; Bounced(w, MathF.Abs(Vel.X)); }
        if (Pos.Y < T + r) { Pos.Y = T + r; Vel.Y = MathF.Abs(Vel.Y) * Bounce; }

        if (OnGround)
        {
            Pos.Y = env.SupportAt(Pos.X, Pos.Y + r, GroundHwnd)?.Y - r ?? Pos.Y;
            Vel.Y = 0;
            Vel.X *= 1 - 0.9f * dt;                    // rolling resistance
            if (MathF.Abs(Vel.X) < 3 * _s) Vel.X = 0;
            Spin = Vel.X / r;
        }
        else if (Vel.Y > 0 && env.FindLanding(Pos.X, prev.Y + r, Pos.Y + r) is { } p)
        {
            Pos.Y = p.Y - r;
            float impact = Vel.Y;
            if (impact > 120 * _s)
            {
                Vel.Y = -impact * Bounce;
                Bounced(w, impact);
            }
            else
            {
                Vel.Y = 0;
                OnGround = true;
                GroundHwnd = p.Hwnd;
            }
            // Friction at contact converts sliding into spin.
            Vel.X = M.Lerp(Vel.X, Spin * r, 0.3f);
            Spin = Vel.X / r;
        }
        Angle += Spin * dt;

        CollideFigures(w);
    }

    void Bounced(World w, float speed)
    {
        SinceBounce = 0;
        if (speed > 900 * _s) w.Fx.Dust(Pos + new Vector2(0, Radius), _s * 0.7f, 3, speed / (2500 * _s), w.Rng);
    }

    void CollideFigures(World w)
    {
        float r = Radius;
        foreach (var f in w.Figures)
        {
            if (f.Mode == Mode.Spawning || f.Carrying == this || f == Juggler) continue;
            if (f == LastTouch && SinceTouch < 0.25f) continue;
            // Nearest point on the figure's body.
            float best = float.MaxValue;
            Vector2 near = default;
            int joint = J.Pelvis;
            void Seg(int a, int b)
            {
                Vector2 pa = f.Jt[a], ab = f.Jt[b] - pa;
                float t = M.Clamp01(Vector2.Dot(Pos - pa, ab) / MathF.Max(ab.LengthSquared(), 1e-4f));
                Vector2 q = pa + ab * t;
                float d = Vector2.Distance(Pos, q) - f.LineW * 0.5f;
                if (d < best) { best = d; near = q; joint = t < 0.5f ? a : b; }
            }
            Seg(J.Neck, J.Pelvis); Seg(J.Neck, J.ElbowN); Seg(J.ElbowN, J.HandN); Seg(J.Neck, J.ElbowF); Seg(J.ElbowF, J.HandF);
            Seg(J.Pelvis, J.KneeN); Seg(J.KneeN, J.FootN); Seg(J.Pelvis, J.KneeF); Seg(J.KneeF, J.FootF);
            float dh = Vector2.Distance(Pos, f.Jt[J.Head]) - f.HeadR;
            if (dh < best) { best = dh; near = f.Jt[J.Head]; joint = J.Head; }
            if (best >= r) continue;

            Vector2 n = Pos - near;
            n = n.LengthSquared() > 1e-4f ? Vector2.Normalize(n) : new Vector2(0, -1);
            Pos = near + n * (r + f.LineW * 0.5f);
            Vector2 rel = Vel - f.JVel[joint];
            float vn = Vector2.Dot(rel, n);
            if (vn >= 0) continue;
            Vel -= (1 + Bounce * 0.6f) * vn * n;
            OnGround = false;
            float hit = -vn * MathF.Sqrt(Mass);
            if (f.Mode == Mode.Ragdoll) f.Rag.Push(joint, -n * hit * 0.5f);
            else if (hit > 350 * _s) f.TakeHit(-n * hit, ThrownByUser ? null : LastTouch, w);
        }
    }

    // ---------------- drawing ----------------

    static readonly Color4 Ink = new(0.1f, 0.1f, 0.1f, 1);

    public void Draw(Renderer rd)
    {
        float r = Radius;
        Vector2 c = Pos;
        Color4 outline = Kind == PropKind.Ball ? M.Shade(Color, 0.55f) : new Color4(0.15f, 0.15f, 0.15f, 1);
        rd.Disc(c, r + 0.9f * _s, new Color4(0, 0, 0, 0.28f));
        switch (Kind)
        {
            case PropKind.SoccerBall: DrawSoccer(rd, c, r); break;
            case PropKind.Basketball: DrawBasketball(rd, c, r); break;
            case PropKind.BeachBall: DrawBeach(rd, c, r); break;
            default: rd.Disc(c, r, Color); break;
        }
        rd.Ring(c, r, outline, MathF.Max(1, 0.7f * _s));
        rd.Disc(c + new Vector2(-r * 0.35f, -r * 0.4f), r * 0.22f, new Color4(1, 1, 1, 0.35f));
    }

    Vector2 Polar(Vector2 c, float rad, float a) => c + new Vector2(MathF.Cos(a + Angle), MathF.Sin(a + Angle)) * rad;

    void DrawSoccer(Renderer rd, Vector2 c, float r)
    {
        rd.Disc(c, r, Color);
        Span<Vector2> pent = stackalloc Vector2[5];
        for (int i = 0; i < 5; i++) pent[i] = Polar(c, r * 0.36f, -MathF.PI / 2 + i * MathF.Tau / 5);
        rd.FillPolygon(pent, Ink);
        for (int i = 0; i < 5; i++)
        {
            float a = -MathF.PI / 2 + i * MathF.Tau / 5;
            rd.Line(pent[i], Polar(c, r * 0.78f, a), Ink, MathF.Max(1, 0.55f * _s));
            // Outer patches, clamped inside the ball's outline.
            Span<Vector2> patch = stackalloc Vector2[5];
            Vector2 pc = Polar(c, r * 0.98f, a + MathF.PI / 5);
            for (int k = 0; k < 5; k++)
            {
                Vector2 v = pc + new Vector2(MathF.Cos(a + MathF.PI / 5 + k * MathF.Tau / 5 + Angle), MathF.Sin(a + MathF.PI / 5 + k * MathF.Tau / 5 + Angle)) * r * 0.3f;
                Vector2 d = v - c;
                if (d.Length() > r * 0.97f) v = c + Vector2.Normalize(d) * r * 0.97f;
                patch[k] = v;
            }
            rd.FillPolygon(patch, Ink);
        }
    }

    void DrawBasketball(Renderer rd, Vector2 c, float r)
    {
        rd.Disc(c, r, Color);
        float w = MathF.Max(1, 0.5f * _s);
        rd.Line(Polar(c, r, 0), Polar(c, r, MathF.PI), Ink, w);
        rd.Line(Polar(c, r, MathF.PI / 2), Polar(c, r, -MathF.PI / 2), Ink, w);
        // Two curved seams: arcs of a larger circle, kept inside the ball.
        for (int side = -1; side <= 1; side += 2)
        {
            Vector2 prev = default;
            bool has = false;
            for (int i = 0; i <= 12; i++)
            {
                float t = -0.75f + 1.5f * i / 12;
                Vector2 local = new(side * (r * 1.55f - MathF.Cos(t) * r * 1.05f), MathF.Sin(t) * r * 1.05f);
                if (local.Length() > r * 0.98f) { has = false; continue; }
                Vector2 p = c + M.Rotate(local, Angle);
                if (has) rd.Line(prev, p, Ink, w);
                prev = p;
                has = true;
            }
        }
    }

    static readonly Color4[] BeachColors =
        { M.Hex(0xE53935), M.Hex(0xFFFFFF), M.Hex(0x1E88E5), M.Hex(0xFDD835), M.Hex(0xFFFFFF), M.Hex(0x43A047) };

    void DrawBeach(Renderer rd, Vector2 c, float r)
    {
        Span<Vector2> wedge = stackalloc Vector2[9];
        for (int i = 0; i < 6; i++)
        {
            wedge[0] = c;
            for (int k = 0; k < 8; k++) wedge[k + 1] = Polar(c, r, (i + k / 7f) * MathF.Tau / 6);
            rd.FillPolygon(wedge, BeachColors[i]);
        }
        rd.Disc(c, r * 0.16f, M.Hex(0xFFFFFF));
    }

    /// <summary>Screen-space bounds including the shadow below it.</summary>
    public RectangleF Bounds(Env env)
    {
        float r = Radius + 2 * _s;
        var b = RectangleF.FromLTRB(Pos.X - r, Pos.Y - r, Pos.X + r, Pos.Y + r);
        if (Shadow(env, out var sc, out float rx, out float ry, out _))
            b = RectangleF.Union(b, RectangleF.FromLTRB(sc.X - rx, sc.Y - ry, sc.X + rx, sc.Y + ry));
        return b;
    }

    public bool Shadow(Env env, out Vector2 center, out float rx, out float ry, out float alpha)
    {
        center = default; rx = ry = alpha = 0;
        if (env.Below(Pos.X, Pos.Y + Radius - 2 * _s) is not { } p) return false;
        float k = M.Clamp01(1 - MathF.Max(0, p.Y - Pos.Y - Radius) / (220 * _s));
        if (k <= 0) return false;
        center = new(Pos.X, p.Y);
        rx = Radius * (0.6f + 0.5f * k);
        ry = 1.6f * _s * (0.6f + 0.4f * k);
        alpha = 0.2f * k;
        return true;
    }

    public bool HitTest(Vector2 p) => Vector2.Distance(p, Pos) <= Radius + 4 * _s;
}
