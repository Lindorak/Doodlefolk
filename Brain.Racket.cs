using System.Numerics;

namespace Doodlefolk;

/// <summary>Tennis and badminton. A point starts with a serve: the server takes the ball to the back of their half,
/// tosses it and hits it over (badminton: a short underhand flick). Then a rally: whoever's nearer reads the flight
/// (Ballistics: the real physics, drag and bounces included), picks the first spot they can get to in time where the
/// ball is at a height they can hit (tennis lets it bounce once; a shuttle must never land), gets there, and sends
/// it back over the net, away from the other player. Skill buys pace, placement and fewer mishits. Then back to the
/// middle of their half for the next one.</summary>
sealed partial class Brain
{
    bool _tossed;
    float _readT;
    (float x, float t)? _meet;

    void PlayRacket(Match m, Prop b, World w)
    {
        int team = m.Team.GetValueOrDefault(f);
        int side = m.Side(team);
        bool badminton = m.Kind == Sport.Badminton;
        float netTop = m.Net.Local(0, m.Net.Def.H).Y;
        float half = m.CourtHalf;
        FaceTo(m.NetX);
        f.KeepFacing = true;
        var mates = m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => p.Id).ToList();
        bool server = m.ServeTeam == team && mates.LastOrDefault() == f;

        // ---- the serve ----
        if (m.AwaitServe && !_serving)
        {
            if (!server) { _run = false; MoveToward(HomeX(m), 8 * S); return; }
            float spot = m.NetX + side * half * (badminton ? 0.45f : 0.8f);
            b.Pinned = true;
            b.PinTarget = f.Jt[J.HandF] + new Vector2(0, -3 * S);   // in the free hand
            _run = MathF.Abs(spot - f.Base.X) > 60 * S;
            if (!MoveToward(spot, 8 * S)) return;
            f.DesiredVX = 0;
            _serving = true; _serveT = 0; _tossed = false;
        }
        if (_serving)
        {
            f.DesiredVX = 0;
            _serveT += World.Dt;
            if (badminton)
            {
                // Held out low in front, then a flick up and over.
                b.Pinned = true;
                b.PinTarget = f.Jt[J.Neck] + new Vector2(-side * f.Arm * 0.55f, f.Arm * 0.45f);
                if (_serveT > 0.55f) { Hit(m, b, side, netTop, true, true); _serving = false; }
                return;
            }
            if (!_tossed)
            {
                b.Pinned = true;
                b.PinTarget = f.Jt[J.Neck] + new Vector2(-side * 5 * S, f.Arm * 0.1f);
                if (_serveT > 0.4f)
                {
                    // Toss it straight up, a little over arm's reach above the head.
                    b.Pinned = false;
                    b.OnGround = false;
                    b.Vel = new Vector2(0, -MathF.Sqrt(2 * b.Grav * f.Arm * 1.3f));
                    _tossed = true;
                }
                return;
            }
            // Hit it on the way down, at the top of the reach.
            if ((b.Vel.Y > 0 && b.Pos.Y > f.Jt[J.Neck].Y - f.Arm * 1.15f) || _serveT > 2.2f)
            {
                Hit(m, b, side, netTop, false, true);
                _serving = false; _tossed = false;
            }
            return;
        }

        // ---- the rally ----
        int lastTeam = b.LastTouch != null && m.Team.TryGetValue(b.LastTouch, out int lt) ? lt : -1;
        bool ours = lastTeam >= 0 && lastTeam != team;   // they hit it: it's coming to us
        if (ours && Responsible(m, team, b))
        {
            _readT -= World.Dt;
            if (_readT <= 0 || _meet == null) { _readT = 0.1f; _meet = ReadShot(m, b, side, badminton); }
            float standX = _meet?.x ?? HomeX(m);
            standX = side < 0 ? MathF.Min(standX, m.NetX - 12 * S) : MathF.Max(standX, m.NetX + 12 * S);
            _run = MathF.Abs(standX - f.Base.X) > 30 * S;
            MoveToward(standX, 3 * S);
            // Swing as it comes into the racket's reach (in front of us).
            Vector2 sh = f.Jt[J.Neck];
            float reach = f.Arm + (f.Weapon?.Def.Reach ?? 10) * S * 0.8f;
            Vector2 d = b.Pos - sh;
            bool inFront = d.X * -side > -6 * S;
            if (_actCd <= 0 && inFront && d.Length() < reach && b.Pos.Y < f.Base.Y - 6 * S)
            {
                // A swing and a miss now and then (less the better they are).
                if (rng.NextDouble() < 0.06f * (1 - Sk(SkillKind.Racket)) + Stress * 0.03f) { f.SetAction(Act.Swat); _actCd = 0.5f; f.Emote("!", 0.5f); }
                else Hit(m, b, side, netTop, badminton, false);
            }
            // A high one dropping in: jump for it (a smash).
            else if (f.Grounded && _actCd <= 0 && MathF.Abs(b.Pos.X - f.Base.X) < 40 * S && b.Vel.Y > 0 &&
                     b.Pos.Y < sh.Y - reach && b.Pos.Y > sh.Y - reach - f.Height * 0.9f)
                f.RequestJump(new Vector2(0, -MathF.Sqrt(2 * f.Gravity * MathF.Max(20 * S, sh.Y - reach - b.Pos.Y + 10 * S))), 0.03f);
            return;
        }
        // Not ours to play: back to the middle of our half, ready.
        _meet = null;
        _run = MathF.Abs(HomeX(m) - f.Base.X) > 80 * S;
        MoveToward(HomeX(m), 10 * S);
        if (MathF.Abs(HomeX(m) - f.Base.X) < 12 * S) f.SetAction(Act.Ready);
    }

    /// <summary>Whose ball is it on our side: the one of us nearest where it's coming down.</summary>
    bool Responsible(Match m, int team, Prop b)
    {
        float landX = b.Pos.X;
        Ballistics.Fly(b.Pos, b.Vel, b.Grav, Ballistics.DragOf(b.Kind), b.Radius, m.CourtFloor, b.Bounce, S, 2.5f, (t, p, v, n) => { landX = p.X; return n == 0; });
        return m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => MathF.Abs(p.Base.X - landX)).FirstOrDefault() == f;
    }

    /// <summary>Where to stand to meet it: the first moment on its way (on our side, at a height we can hit, no
    /// more than one bounce in tennis and none in badminton) that we can run to in time.</summary>
    (float x, float t)? ReadShot(Match m, Prop b, int side, bool badminton)
    {
        (float x, float t)? best = null, fallback = null;
        float lo = f.Height * 0.3f, hi = f.Height * 1.5f, reachX = f.Arm * 0.7f;
        Ballistics.Fly(b.Pos, b.Vel, b.Grav, Ballistics.DragOf(b.Kind), b.Radius, m.CourtFloor, b.Bounce, S, 3f, (t, p, v, n) =>
        {
            if (badminton ? n > 0 : n > 1) return false;
            if (MathF.Sign(p.X - m.NetX) != side) return true;
            float hgt = m.CourtFloor - p.Y;
            if (hgt < lo || hgt > hi) return true;
            float standX = p.X + side * reachX;   // the ball out in front of us (we face the net)
            fallback ??= (standX, t);
            float run = MathF.Abs(standX - f.Base.X) / MathF.Max(f.RunSpeed, 1) + 0.12f;
            if (run <= t) { best = (standX, t); return false; }
            return true;
        });
        return best ?? fallback;
    }

    void Hit(Match m, Prop b, int side, float netTop, bool badminton, bool serve)
    {
        f.SetAction(Act.Swat);
        _actCd = 0.35f;
        float rs = Sk(SkillKind.Racket);
        float half = m.CourtHalf;
        int team = m.Team.GetValueOrDefault(f);
        // Away from them: of a few depths, the one furthest from the nearest player over there (good players see it).
        float oppX = m.Players.Where(p => m.Team.GetValueOrDefault(p) != team).Select(p => p.Base.X).DefaultIfEmpty(m.NetX - side * half * 0.5f).First();
        float[] depths = serve ? new[] { 0.35f, 0.55f, 0.75f } : new[] { 0.3f, 0.55f, 0.82f };
        float depth = depths.OrderByDescending(dp => MathF.Abs(m.NetX - side * half * dp - oppX) * (rng.NextDouble() < 0.4 + rs * 0.5 ? 1 : (float)rng.NextDouble())).First();
        float tx = m.NetX - side * half * depth + rng.Range(-1, 1) * (1 - rs) * 40 * S;
        var target = new Vector2(tx, m.CourtFloor - b.Radius);
        float k = Ballistics.DragOf(b.Kind);
        float clear = (badminton ? 28 : 12) * S + (float)rng.NextDouble() * (1 - rs) * 25 * S;
        float maxV = (badminton ? 1800 : 1050) * S * (0.75f + 0.35f * rs) * (serve ? 0.9f : 1);
        var v = Ballistics.Over(b.Pos, target, b.Grav, k, m.NetX, netTop, clear, maxV)
             ?? Ballistics.Over(b.Pos, target, b.Grav, k, m.NetX, netTop, clear * 0.5f, maxV * 1.6f)
             ?? Ballistics.Launch(b.Pos, target, b.Grav, k, 1.2f);
        // Mishits: off the frame, wide or short.
        if (rng.NextDouble() > 0.84 + 0.14 * rs)
        {
            float a = rng.Range(-0.22f, 0.22f), c = MathF.Cos(a), sn = MathF.Sin(a);
            v = new Vector2(v.X * c - v.Y * sn, v.X * sn + v.Y * c) * rng.Range(0.8f, 1.12f);
            m.Stats.Fumbles++;
        }
        Practice(SkillKind.Racket, 0.008f);
        m.Touched();
        m.Stats.Hit();
        if (serve) m.Stats.Serves++;
        b.Pinned = false;
        b.Strike(v, f);
        b.LastTouch = f;
        _meet = null;
        if (m.Stats.RallyHits is 10 or 20 or 30) f.Emote(m.Stats.RallyHits + "!", 0.8f);
        else if (rng.NextDouble() < 0.05) f.Emote(P.Playfulness > 0.5f ? "!" : "hm", 0.6f);
    }
}
