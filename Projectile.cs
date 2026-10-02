using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Something fired from a toy gun: a foam dart (stings a bit, knocks a little) or a squirt of water
/// (harmless, but nobody likes getting wet). Hits figures, your cursor (if aimed at it), and stops at windows.</summary>
sealed class Projectile
{
    public Vector2 Pos, Vel, Prev;
    public Ammo Kind;
    public Figure? Owner;
    public bool AtCursor;
    public float Life;
    readonly float _s;

    public Projectile(Ammo kind, Vector2 pos, Vector2 vel, Figure? owner, float scale, bool atCursor)
    {
        Kind = kind; Pos = Prev = pos; Vel = vel; Owner = owner; _s = scale; AtCursor = atCursor;
        Life = kind == Ammo.Water ? 0.9f : 1.8f;
    }

    float Grav => (Kind == Ammo.Water ? 1400 : 500) * _s;

    /// <summary>Returns false when it's done (hit something or ran out).</summary>
    public bool Step(float dt, World w)
    {
        Life -= dt;
        if (Life <= 0) return false;
        Prev = Pos;
        Vel.Y += Grav * dt;
        Pos += Vel * dt;
        var (L, R, T) = w.Env.BoundsAt(Pos.X);
        if (Pos.X < L || Pos.X > R || Pos.Y < T) return false;

        if (AtCursor && M.DistToSegment(w.Cursor, Prev, Pos) < 10 * _s)
        {
            if (w.Fight.Enabled && w.Fight.PunchCursor) w.CursorPush += Vector2.Normalize(Vel) * (Kind == Ammo.Water ? 6 : 35) * _s;
            w.Fx.Spark(w.Cursor, _s * 0.8f, w.Rng, 0.6f, Kind == Ammo.Water ? new Color4(0.5f, 0.8f, 1, 1) : null);
            Owner?.Brain.OnHitCursor();
            return false;
        }
        foreach (var f in w.Figures)
        {
            if (f == Owner || f.Mode == Mode.Spawning || f.Dead) continue;
            if (f.BodyDistance(Pos) > 3 * _s) continue;
            if (Kind == Ammo.Water)
            {
                f.Brain.OnSplashed(Owner, w);
                w.Fx.Dust(Pos, _s * 0.5f, 2, 0.2f, w.Rng);
            }
            else
            {
                f.TakeHit(Vel * 0.35f, Owner, w);
                if (f.Mode == Mode.Control) { f.HP -= 3 * w.Fight.Strength; f.HitStun = MathF.Max(f.HitStun, 0.12f); }
                w.Fx.Spark(Pos, _s * 0.6f, w.Rng, 0.5f);
            }
            return false;
        }
        // Darts stop at the first window edge they cross; water splashes on it.
        if (Vel.Y > 0 && w.Env.FindLanding(Pos.X, Prev.Y, Pos.Y) is { } p)
        {
            if (Kind == Ammo.Water) w.Fx.Dust(new Vector2(Pos.X, p.Y), _s * 0.5f, 2, 0.15f, w.Rng);
            return false;
        }
        return true;
    }

    public void Draw(Renderer r)
    {
        if (Kind == Ammo.Water)
        {
            r.Disc(Pos, 1.6f * _s, new Color4(0.45f, 0.75f, 1, 0.85f));
            r.Disc(Prev, 1.1f * _s, new Color4(0.45f, 0.75f, 1, 0.5f));
            return;
        }
        Vector2 d = Vel.LengthSquared() > 1 ? Vector2.Normalize(Vel) : new Vector2(1, 0);
        r.Line(Pos - d * 5 * _s, Pos, new Color4(0.98f, 0.55f, 0.1f, 1), 2.4f * _s);
        r.Disc(Pos, 1.5f * _s, new Color4(0.15f, 0.15f, 0.15f, 1));
    }

    public System.Drawing.RectangleF Bounds()
    {
        float pad = 7 * _s;
        return System.Drawing.RectangleF.FromLTRB(MathF.Min(Pos.X, Prev.X) - pad, MathF.Min(Pos.Y, Prev.Y) - pad, MathF.Max(Pos.X, Prev.X) + pad, MathF.Max(Pos.Y, Prev.Y) + pad);
    }
}
