using System.Numerics;

namespace Doodlefolk;

/// <summary>Football, played with some sense. Every quarter second each player takes a role: the keeper; whoever's
/// nearest the ball goes for it (the carrier, or on the other side the one who presses); a teammate runs ahead into
/// space for a pass; the rest cover between the ball and their own goal. On the ball: dribble with little touches that
/// keep it just ahead at running pace; shoot for a corner when the goal's in range; pass to someone open and further
/// up when a defender's in the way (along the ground, or chipped over them); knock it past them and chase it; or clear
/// it when under pressure near our own goal. Never walk through the ball: get round it (it goes behind you). Keepers
/// narrow the angle, read shots (Ballistics) and catch what they can reach, dive for what they can't, and throw it out.</summary>
sealed partial class Brain
{
    enum SoccerRole { Carrier, Support, Press, Cover }
    float _socDecide, _touchCd, _keeperHold, _tackleCd;
    bool _diving;

    void PlaySoccer(Match m, Prop b, World w)
    {
        int team = m.Team.GetValueOrDefault(f);
        var attack = m.OneGoal ? m.Gear[0] : m.Gear[1 - team];
        float atk = -m.FieldDir(attack);   // the way we attack
        if (_kickAt >= 0) { f.DesiredVX = 0; if (KickBall(b, _sportKick)) Practice(SkillKind.Kicking, 0.004f); return; }
        if (Keeper(m)) { PlayKeeper(m, b, w, m.OneGoal ? m.Gear[0] : m.Gear[team]); return; }
        _touchCd -= World.Dt;
        switch (RoleOf(m, b, team))
        {
            case SoccerRole.Carrier: SoccerCarry(m, b, w, team, attack, atk); break;
            case SoccerRole.Press: SoccerPress(m, b, w, team, atk); break;
            case SoccerRole.Support:
            {
                // Up ahead of the ball, in space (away from teammates), ready for a pass.
                var mates = m.Players.Where(p => p != f && m.Team.GetValueOrDefault(p) == team && !p.Brain.Keeper(m)).ToList();
                int i = mates.Count(p => p.Id < f.Id);
                float x = b.Pos.X + atk * (110 + 70 * i) * S;
                if (m.Court != null) x = M.ClampIn(x, m.Court.X1 + 20 * S, m.Court.X2 - 20 * S);
                _run = MathF.Abs(x - f.Base.X) > 90 * S;
                MoveToward(x, 12 * S);
                FaceTo(b.Pos.X);
                break;
            }
            default:
            {
                // Between the ball and our goal (in a shootout: back out of the way, waiting a turn).
                float x = m.OneGoal ? b.Pos.X - atk * 160 * S : (b.Pos.X + m.Mouth(m.Gear[team], 0).X) / 2;
                _run = MathF.Abs(x - f.Base.X) > 90 * S;
                MoveToward(x, 12 * S);
                FaceTo(b.Pos.X);
                break;
            }
        }
    }

    SoccerRole RoleOf(Match m, Prop b, int team)
    {
        int have = b.LastTouch != null && m.Team.TryGetValue(b.LastTouch, out int ht) ? ht : -1;
        var mine = m.Players.Where(p => m.Team.GetValueOrDefault(p) == team && !p.Brain.Keeper(m)).OrderBy(p => MathF.Abs(p.Base.X - b.Pos.X)).ToList();
        int rank = mine.IndexOf(f);
        if (rank == 0) return have >= 0 && have != team && b.SinceTouch < 1.5f ? SoccerRole.Press : SoccerRole.Carrier;
        if (m.OneGoal) return SoccerRole.Cover;
        return have == team || have < 0 ? (rank == 1 ? SoccerRole.Support : SoccerRole.Cover) : SoccerRole.Cover;
    }

    /// <summary>Kick it so it leaves at exactly this velocity (the kick's own oomph is in the numbers here).</summary>
    bool SportKick(Prop b, Vector2 v, string kind, Match m)
    {
        if (_kickAt >= 0 || _actCd > 0) return false;
        KickBall(b, v * MathF.Sqrt(b.Mass));
        switch (kind)
        {
            case "pass": m.Stats.Passes++; break;
            case "shot": m.Stats.Shot("shot"); break;
            case "clear": m.Stats.Shot("clear"); break;
        }
        return true;
    }

    void SoccerCarry(Match m, Prop b, World w, int team, Item attack, float atk)
    {
        float rel = (b.Pos.X - f.Base.X) * atk;   // how far the ball is ahead of us
        // Ahead of it (we'd kick it backwards): get round it; it passes behind us.
        if (rel < -2 * S)
        {
            b.Ghost = f; b.GhostUntil = World.Now + 0.4;
            _run = true;
            MoveToward(b.Pos.X - atk * (b.Radius + 14 * S), 4 * S);
            return;
        }
        // In the air: be where it comes down.
        if (b.Pos.Y < f.Base.Y - f.Leg * 1.1f)
        {
            var path = BallPath(b, w);
            var land = path.FirstOrDefault(p => p.Y > f.Base.Y - f.Leg, path[^1]);
            _run = true;
            MoveToward(land.X - atk * (b.Radius + 8 * S), 6 * S);
            return;
        }
        float gap = rel - b.Radius;
        if (gap > f.Leg * 0.9f)
        {
            // Catch it up (from behind it).
            _run = gap > 30 * S;
            MoveToward(b.Pos.X - atk * (b.Radius + 4 * S), 3 * S);
            return;
        }
        // At our feet. What now?
        FaceTo(b.Pos.X + atk);
        float goalX = m.Mouth(attack, 0).X, toGoal = MathF.Abs(goalX - b.Pos.X);
        var opps = m.Players.Where(p => m.Team.GetValueOrDefault(p) != team).ToList();
        var blocker = opps.Where(p => !p.Brain.Keeper(m) || m.OneGoal == false).Select(p => (p, ahead: (p.Base.X - b.Pos.X) * atk))
                          .Where(x => x.ahead > 0 && x.ahead < 80 * S).OrderBy(x => x.ahead).Select(x => x.p).FirstOrDefault();
        float kick = Sk(SkillKind.Kicking);
        float range = (170 + 170 * kick + P.Bravery * 40) * S;
        _socDecide -= World.Dt;
        if (_socDecide <= 0 && _actCd <= 0)
        {
            _socDecide = 0.3f;
            if (toGoal < range && (blocker == null || toGoal < 110 * S || rng.NextDouble() < 0.25))
            {
                if (SportKick(b, AimShot(m, b, attack), "shot", m)) { f.Emote(V("shoot!", "SHOOOT!!", "hm.", "here goes…", "for glory"), 0.7f); return; }
            }
            if (blocker != null && OpenMate(m, b, team, atk) is { } mate)
            {
                if (SportKick(b, PassTo(b, mate, blocker, w), "pass", m)) { if (rng.NextDouble() < 0.4) f.Emote(mate.Name + "!", 0.7f); return; }
            }
            if (blocker != null && rng.NextDouble() < 0.35 + P.Bravery * 0.4f)
            {
                // Chip it over them, or knock it past and race them to it.
                var over = Ballistics.Over(b.Pos, new Vector2(blocker.Base.X + atk * 70 * S, f.Base.Y - b.Radius), b.Grav, Ballistics.DragOf(b.Kind),
                                           blocker.Base.X, blocker.Jt[J.Head].Y, 8 * S, 900 * S);
                if (SportKick(b, over ?? new Vector2(atk * f.RunSpeed * 1.9f, -60 * S), "touch", m)) { m.Stats.Touches++; return; }
            }
            if (!m.OneGoal && blocker != null && MathF.Abs(b.Pos.X - m.Mouth(m.Gear[team], 0).X) < 160 * S)
            {
                // Under pressure in front of our own goal: get it away.
                if (SportKick(b, new Vector2(atk * rng.Range(700, 1000) * S, -rng.Range(400, 650) * S), "clear", m)) return;
            }
        }
        // Dribble: a touch that sends it a little ahead at running pace, then run on to it.
        _run = true;
        f.DesiredVX = atk * f.RunSpeed * (0.85f + Sk(SkillKind.Ball) * 0.15f);
        if (_touchCd <= 0 && gap < f.Leg * 0.45f)
        {
            float pace = f.RunSpeed * rng.Range(1.25f, 1.5f) * (1.1f - Sk(SkillKind.Ball) * 0.15f);
            b.Kick(new Vector2(atk * pace, -rng.Range(10, 40) * S) * MathF.Sqrt(b.Mass), f);
            m.Stats.Touches++;
            Practice(SkillKind.Ball, 0.002f);
            _touchCd = 0.18f;
        }
    }

    /// <summary>For a corner, away from the keeper; worse aim the less practised they are.</summary>
    Vector2 AimShot(Match m, Prop b, Item goal)
    {
        float kick = Sk(SkillKind.Kicking);
        var keeper = m.Players.FirstOrDefault(p => p.Brain.Keeper(m) && m.Team.GetValueOrDefault(p) != m.Team.GetValueOrDefault(f));
        // High if the keeper's low (or small), low otherwise, now and then just hit it.
        bool high = keeper == null ? rng.NextDouble() < 0.5 : keeper.Height < f.Height * 0.95f || rng.NextDouble() < 0.35;
        float h = high ? rng.Range(18, 25) : rng.Range(3, 9);
        float miss = (1 - kick) * 9;
        var aim = goal.Local(-6, h + rng.Range(-miss, miss)) + new Vector2(rng.Range(-miss, miss) * S, 0);
        float k = Ballistics.DragOf(b.Kind);
        float dist = MathF.Abs(aim.X - b.Pos.X);
        float T = Math.Clamp(dist / ((700 + 500 * kick) * S), 0.25f, 1.1f);
        Practice(SkillKind.Kicking, 0.008f);
        return Ballistics.Launch(b.Pos, aim, b.Grav, k, T);
    }

    /// <summary>A teammate who's open (nobody on them) and further up, worth passing to.</summary>
    Figure? OpenMate(Match m, Prop b, int team, float atk) =>
        m.Players.Where(p => p != f && m.Team.GetValueOrDefault(p) == team && !p.Brain.Keeper(m) && p.Grounded)
                 .Select(p => (p, fwd: (p.Base.X - b.Pos.X) * atk, free: m.Players.Where(o => m.Team.GetValueOrDefault(o) != team).Select(o => MathF.Abs(o.Base.X - p.Base.X)).DefaultIfEmpty(999).Min()))
                 .Where(x => x.fwd > -40 * S && x.fwd < 450 * S && x.free > 45 * S)
                 .OrderByDescending(x => x.free + x.fwd * 0.5f).Select(x => x.p).FirstOrDefault();

    /// <summary>Along the ground to where they'll be, or lofted over someone in the way.</summary>
    Vector2 PassTo(Prop b, Figure mate, Figure? inWay, World w)
    {
        float lead = mate.Vel.X * 0.4f;
        var to = new Vector2(mate.Base.X + lead, mate.Base.Y - b.Radius);
        float dist = MathF.Abs(to.X - b.Pos.X);
        bool between = inWay != null && (inWay.Base.X - b.Pos.X) * (to.X - b.Pos.X) > 0 && MathF.Abs(inWay.Base.X - b.Pos.X) < dist;
        float k = Ballistics.DragOf(b.Kind);
        if (between && Ballistics.Over(b.Pos, to, b.Grav, k, inWay!.Base.X, inWay.Jt[J.Head].Y, 10 * S, 1100 * S) is { } lob) return lob;
        // Rolled: friction 0.9/s; it should still be moving when it gets there.
        float T = 0.5f + dist / (900 * S);
        float vx = (to.X - b.Pos.X) * 0.9f / (1 - MathF.Exp(-0.9f * T));
        Practice(SkillKind.Ball, 0.004f);
        return new Vector2(vx * (1 + rng.Range(-0.12f, 0.12f) * (1 - Sk(SkillKind.Ball))), -15 * S);
    }

    /// <summary>Close down whoever has it, staying goal-side; nip in when their touch leaves the ball loose.</summary>
    void SoccerPress(Match m, Prop b, World w, int team, float atk)
    {
        _tackleCd -= World.Dt;
        // Goal-side: between the ball and our goal (we defend the opposite way to how we attack).
        float x = b.Pos.X - atk * (b.Radius + 16 * S);
        _run = MathF.Abs(x - f.Base.X) > 30 * S;
        MoveToward(x, 4 * S);
        FaceTo(b.Pos.X);
        var carrier = b.LastTouch;
        float loose = carrier != null ? MathF.Abs(b.Pos.X - carrier.Base.X) : 99 * S;
        bool reach = MathF.Abs(b.Pos.X - f.Base.X) < f.Leg * 0.8f && b.Pos.Y > f.Base.Y - f.Leg;
        if (reach && _tackleCd <= 0 && _actCd <= 0 && (loose > 12 * S || rng.NextDouble() < 0.02 + P.Aggression * 0.03))
        {
            _tackleCd = 0.8f;
            bool won = rng.NextDouble() < 0.45 + Sk(SkillKind.Ball) * 0.25f + P.Aggression * 0.15f;
            if (won)
            {
                // Poke it away up the pitch.
                b.Kick(new Vector2(atk * rng.Range(250, 450) * S, -rng.Range(20, 80) * S) * MathF.Sqrt(b.Mass), f);
                m.Stats.Steals++;
                f.SetAction(Act.Tap);
                if (rng.NextDouble() < 0.4) f.Emote(V("mine!", "STOLEN!!", "tch, got it.", "s-sorry!", "a gift"), 0.8f);
            }
            else f.SetAction(Act.Tap);   // a stab at it, and a miss
        }
    }

    // ---------------- the keeper ----------------

    void PlayKeeper(Match m, Prop b, World w, Item own)
    {
        float dirOut = m.FieldDir(own);
        float line = m.Mouth(own, 0).X + dirOut * 10 * S;
        int team = m.Team.GetValueOrDefault(f);
        // Holding it: a moment, then throw it out to someone open (or kick it long).
        if (f.Carrying == b)
        {
            f.DesiredVX = 0;
            FaceTo(b.Pos.X + dirOut);
            _keeperHold -= World.Dt;
            if (_keeperHold > 0) return;
            var mate = m.Players.Where(p => p != f && m.Team.GetValueOrDefault(p) == team).OrderBy(p => MathF.Abs(p.Base.X - f.Base.X)).FirstOrDefault();
            Vector2 to = mate != null ? new Vector2(mate.Base.X, mate.Base.Y - mate.Height * 0.6f) : f.Base + new Vector2(dirOut * 300 * S, -f.Height);
            var v = Ballistics.Over(f.HoldPoint, to, b.Grav, Ballistics.DragOf(b.Kind), f.Base.X, f.Base.Y, -9999, 900 * S) ?? new Vector2(dirOut * 500 * S, -500 * S);
            f.Carrying = null; b.Holder = null;
            b.Release(v);
            b.LastTouch = f;
            m.Stats.Passes++;
            f.SetAction(Act.Throw);
            return;
        }
        // Narrow the angle: off the line toward the ball, more the nearer it is.
        float d = MathF.Abs(b.Pos.X - line);
        float stand = line + dirOut * MathF.Min(45 * S, d * 0.14f);
        FaceTo(b.Pos.X);
        // A shot coming in? Where and when does it reach us?
        (float t, Vector2 at)? save = null;
        if (b.Holder == null && (b.Vel.X * dirOut) < -150 * S)
        {
            float bar = own.Local(0, 33).Y;
            Ballistics.Fly(b.Pos, b.Vel, b.Grav, Ballistics.DragOf(b.Kind), b.Radius, own.Pos.Y, b.Bounce, S, 1.2f, (t, p, v, n) =>
            {
                if ((p.X - f.Base.X) * dirOut > 0) return true;   // not to us yet
                if (p.Y > bar - 6 * S) save = (t, p);
                return false;
            });
        }
        if (save is { } s)
        {
            Vector2 sh = f.Jt[J.Neck];
            float standReach = f.Arm * 1.05f;
            if (_diving)
            {
                // In the air: hands to it.
                f.HoldN = b.Pos; f.HoldF = b.Pos + new Vector2(0, 4 * S);
            }
            if (Vector2.Distance(b.Pos, sh) < standReach + (_diving ? f.Arm * 0.3f : 0))
            {
                bool catches = rng.NextDouble() < 0.55f + Sk(SkillKind.Ball) * 0.3f + P.Bravery * 0.1f - b.Vel.Length() / (4000 * S);
                if (catches && b.Pos.Y < f.Base.Y - 4 * S)
                {
                    b.Holder = f; f.Carrying = b; b.LastTouch = f;
                    _keeperHold = rng.Range(0.8f, 1.5f);
                    f.Emote(V("got it!", "SAVED!!", "mine.", "o-oh, I caught it!", "the hands of fate"), 1);
                }
                else
                {
                    // Palmed away: up and back out.
                    b.Vel = new Vector2(dirOut * MathF.Abs(b.Vel.X) * 0.45f, -MathF.Abs(b.Vel.Y) * 0.3f - 250 * S);
                    b.LastTouch = f;
                    f.Emote("!", 0.6f);
                }
                m.Stats.Saves++;
                Practice(SkillKind.Ball, 0.01f);
                _diving = false;
                return;
            }
            // Can we get there standing? Step across and reach. Otherwise: dive.
            bool high = s.at.Y < sh.Y - standReach * 0.8f, wide = MathF.Abs(s.at.X - f.Base.X) > 18 * S;
            if (f.Grounded && !f.JumpPending && _actCd <= 0 && s.t > 0.12f && (high || wide) && Stamina > 0.15f)
            {
                float dx = s.at.X - f.Base.X;
                float rise = MathF.Max(0, sh.Y - s.at.Y - f.Arm * 0.6f);
                f.RequestJump(new Vector2(dx / MathF.Max(s.t, 0.2f) * 0.9f, -MathF.Sqrt(2 * f.Gravity * MathF.Max(rise, 14 * S))), 0.02f);
                f.DiveAt = s.at;
                _diving = true;
                _actCd = 0.9f;
                Stamina = MathF.Max(0, Stamina - 0.03f);
                return;
            }
            f.HoldN = s.at; f.HoldF = s.at + new Vector2(0, 5 * S);
            MoveToward(M.ClampIn(s.at.X, line - dirOut * 2 * S, line + dirOut * 50 * S), 3 * S);
            return;
        }
        if (f.Grounded) { _diving = false; f.DiveAt = null; }
        _run = MathF.Abs(stand - f.Base.X) > 40 * S;
        MoveToward(stand, 4 * S);
        if (MathF.Abs(stand - f.Base.X) < 6 * S) f.SetAction(Act.Ready);
        // A ball dribbling about right in front of us: boot it clear.
        if (MathF.Abs(b.Pos.X - f.Base.X) < f.Leg * 0.7f && b.Vel.LengthSquared() < 300 * 300 * S * S && b.Pos.Y > f.Base.Y - f.Leg)
            SportKick(b, new Vector2(dirOut * 800 * S, -550 * S), "clear", m);
    }
}
