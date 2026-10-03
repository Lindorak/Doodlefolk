using System.Numerics;

namespace Doodlefolk;

/// <summary>Seeing things coming: balls and thrown figures on a course for this figure. It predicts where they'll
/// cross its body and reacts in time (or doesn't): jump over low ones, duck high ones, dash aside, catch it,
/// swat it away, or brace. Alertness, reflexes and temperament decide which, and whether it notices at all.</summary>
sealed partial class Brain
{
    float _dodgeCd, _dashT, _dashDir;
    Prop? _batBall;
    readonly Dictionary<int, float> _threatSeen = new();

    /// <summary>A dodge dash in progress overrides whatever the current goal wants to walk at.</summary>
    public float DashVX => _dashT > 0 ? _dashDir * f.RunSpeed : 0;

    readonly struct Threat
    {
        public readonly Vector2 Pos, Vel;
        public readonly float T, Height;      // seconds until impact; where on the body (0 = feet, 1 = head)
        public readonly Prop? Ball;
        public readonly Figure? Body;
        public Threat(Vector2 pos, Vector2 vel, float t, float h, Prop? ball, Figure? body) { Pos = pos; Vel = vel; T = t; Height = h; Ball = ball; Body = body; }
        public int Key => Ball?.Id ?? -(Body?.Id ?? 0);
    }

    /// <summary>Per frame: look for something about to hit us.</summary>
    void WatchForThreats(World w, float dt)
    {
        if (_dashT > 0) { _dashT -= dt; if (_dashT <= 0) f.KeepFacing = false; else f.KeepFacing = true; }
        if (_batBall != null) TryBat(w);
        _dodgeCd -= dt;
        if (_dodgeCd > 0 || !f.Grounded || f.Climbing || f.JumpPending || f.Atk != null || _g is G.Sleep or G.Busy or G.Sport) return;

        Threat? best = null;
        foreach (var b in w.Props)
        {
            if (b.Holder != null || b.PassTarget == f || (b.LastTouch == f && b.SinceTouch < 1)) continue;
            if (b == _ball && _g is G.Catch or G.Juggle or G.Kick or G.Dribble or G.Carry or G.Throw) continue;
            if (b.Vel.LengthSquared() < 450 * 450 * S * S) continue;
            if (Predict(b.Pos, b.Vel, b.Grav, b.Radius, out float t, out float hgt) && (best == null || t < best.Value.T))
                best = new Threat(b.Pos, b.Vel, t, hgt, b, null);
        }
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Ragdoll || o.Held) continue;
            Vector2 v = o.JVel[J.Pelvis];
            if (v.LengthSquared() < 900 * 900 * S * S) continue;
            if (Predict(o.Jt[J.Pelvis], v, o.Gravity, o.Torso * 0.6f, out float t, out float hgt) && (best == null || t < best.Value.T))
                best = new Threat(o.Jt[J.Pelvis], v, t, hgt, null, o);
        }
        if (best is not { } th) return;
        // React to each incoming thing once.
        if (_threatSeen.TryGetValue(th.Key, out float seenAt) && _t0 - seenAt < 1.5f) return;
        _threatSeen[th.Key] = _t0;
        React(th, w);
    }

    /// <summary>Ballistic look-ahead: does it cross our body in the next ~0.7 s, and when/where?</summary>
    bool Predict(Vector2 p0, Vector2 v, float g, float r, out float tHit, out float height)
    {
        tHit = 0; height = 0;
        float top = f.Height, pad = r + 5 * S;
        for (float t = 0.02f; t <= 0.7f; t += 1 / 60f)
        {
            Vector2 p = p0 + v * t + new Vector2(0, 0.5f * g * t * t);
            float bx = f.Base.X + f.Vel.X * t;
            float up = f.Base.Y - p.Y;                       // height above our feet
            if (up < -pad || up > top + pad) continue;
            if (MathF.Abs(p.X - bx) > pad + 4 * S) continue;
            tHit = t;
            height = Math.Clamp(up / top, 0, 1);
            return true;
        }
        return false;
    }

    void React(Threat th, World w)
    {
        _dodgeCd = 0.6f;
        Vector2 from = th.Pos;
        bool facing = MathF.Sign(from.X - f.Base.X) == f.Facing;
        // Did it even see it coming?
        float alert = 0.55f + P.Curiosity * 0.2f + P.Energy * 0.2f + P.Bravery * 0.05f
                    - (facing ? 0 : 0.25f) - (_g is G.Chat or G.DanceWith or G.SitFloor or G.SitEdge ? 0.15f : 0) - (1 - Stamina) * 0.15f;
        if (_g == G.Watch || InFight) alert += 0.2f;
        if (rng.NextDouble() > alert) return;
        float reflex = 0.06f + (1 - P.Energy) * 0.08f + rng.Range(0, 0.06f);   // time to react
        float left = th.T - reflex;
        var b = th.Ball;
        if (left < 0.06f)
        {
            // Too late: flinch.
            f.BlockT = 0.35f;
            f.Emote("!", 0.8f);
            return;
        }

        var opts = new List<(float w, Action act)>();
        bool catchable = b != null && b.SizeMul <= 1.8f;
        if (b != null && catchable && th.Height is > 0.4f and < 0.95f && left > 0.12f && _g is not (G.Chat or G.DanceWith))
            opts.Add(((P.Playfulness * 1.2f + MathF.Max(0, f.Tastes.Of(Thing.PlayingBall))) * Stamina, () =>
            {
                Go(G.Catch, 2.5f);
                _ball = b;
                _passFrom = b.LastTouch;
            }));
        if (th.Height < 0.55f && left > 0.1f)
            opts.Add((1 + P.Energy, () =>
            {
                float rise = f.Height * (0.45f + 0.25f * (1 - th.Height)) + 8 * S;
                f.RequestJump(new Vector2(0, -MathF.Sqrt(2 * f.Gravity * rise)), 0.04f);
            }));
        if (th.Height >= 0.45f)
            opts.Add((1.1f + (1 - P.Energy) * 0.5f, () => { f.DuckT = th.T + 0.35f; f.DesiredVX = 0; }));
        if (left > 0.25f)
            opts.Add((0.5f + P.Energy * 0.6f + (1 - P.Bravery) * 0.4f, () =>
            {
                _dashDir = th.Vel.X != 0 ? MathF.Sign(f.Base.X - from.X) : (rng.NextDouble() < 0.5 ? -1 : 1);
                _dashT = 0.4f;
            }));
        if (b != null && b.SizeMul <= 2.2f && th.Height is > 0.35f and < 1f && left > 0.1f)
            opts.Add((P.Aggression * P.Bravery * 2, () =>
            {
                FaceTo(from.X);
                Go(G.Swat, 0.34f);
                _batBall = b;
            }));
        if (P.Bravery > 0.5f)
            opts.Add((P.Bravery * 0.5f, () => { FaceTo(from.X); f.BlockT = th.T + 0.25f; }));
        if (opts.Count == 0) return;

        float total = opts.Sum(o => o.w), roll = rng.Range(0, total);
        foreach (var (wgt, act) in opts) { roll -= wgt; if (roll <= 0) { act(); break; } }
        if (f.CurrentEmote == null && rng.NextDouble() < 0.4) f.Emote("!", 0.7f);

        // A ball you threw at them: ball-lovers call it dodgeball, everyone else takes it personally.
        if (b != null && b.ThrownByUser)
        {
            if (f.Tastes.Likes(Thing.PlayingBall)) FeelUser(0.01f, "Played dodgeball with them");
            else FeelUser(-0.015f, "Threw a ball at them");
        }
    }

    /// <summary>Mid-swat: if the ball is in reach, knock it back the way it came.</summary>
    void TryBat(World w)
    {
        var b = _batBall!;
        if (_g != G.Swat || !w.Props.Contains(b) || b.Holder != null) { _batBall = null; return; }
        if (Vector2.Distance(b.Pos, f.Jt[J.Neck]) > f.Arm + b.Radius + 4 * S) return;
        float dir = MathF.Sign(b.Pos.X - f.Base.X);
        if (dir == 0) dir = f.Facing;
        b.Kick(new Vector2(dir * (700 + P.Aggression * 500) * S, -rng.Range(250, 500) * S), f);
        w.Fx.Spark(b.Pos, S, w.Rng, 0.8f);
        f.Emote("ha", 1);
        _batBall = null;
    }
}
