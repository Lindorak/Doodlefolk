using System.Numerics;

namespace Doodlefolk;

/// <summary>Basketball with a plan. With the ball: dribble (a real bounce), and every quarter second weigh up the
/// options by how likely each is to go in and what it's worth: drive to the hoop when nobody's between them and it,
/// a layup off the drive, a dunk if they can get up there (and dare), a pull-up jumper, a three from out wide, or a
/// step back when someone's right on them. Shots are jumped and let go at the top, aimed with the real ball physics,
/// less surely the further out and the more they're crowded. Without it: whoever's nearest guards the ball, staying
/// between them and the hoop, reaching for steals at the bottom of the dribble and jumping to block; everyone else
/// reads where a miss will come down and goes up for the rebound; a loose ball is gathered properly.</summary>
sealed partial class Brain
{
    enum HoopPlan { None, Drive, Layup, Dunk, Jumper, StepBack }
    HoopPlan _hoopPlan;
    float _hoopDecide, _shotT, _stealCd, _dunkCd;
    bool _clearOut;
    float _clearTo;
    bool _released;
    int _dribbleSeen;
    float _gatherT;

    void PlayBasketball(Match m, Prop b, World w)
    {
        var rim = m.RimCentre;
        float field = m.FieldDir(m.Gear[0]);
        if (f.DribbleBeats != _dribbleSeen) { m.Stats.DribbleBeats += f.DribbleBeats - _dribbleSeen; _dribbleSeen = f.DribbleBeats; }
        if (f.HangT > 0) { f.DesiredVX = 0; return; }
        if (TryBlock(m, b)) return;
        if (f.Carrying == b) { Handle(m, b, w, rim, field); return; }
        f.Dribbling = false;
        _hoopPlan = HoopPlan.None;
        _stealCd -= World.Dt;
        var handler = b.Holder as Figure;
        int team = m.Team.GetValueOrDefault(f);
        bool rival = handler != null && handler != f && (m.Players.Count <= 3 || m.Team.GetValueOrDefault(handler) != team);
        // Guard the ball: the nearest rival to whoever has it.
        if (rival && m.Players.Where(p => p != handler && (m.Players.Count <= 3 || m.Team.GetValueOrDefault(p) != m.Team.GetValueOrDefault(handler))).OrderBy(p => MathF.Abs(p.Base.X - handler!.Base.X)).FirstOrDefault() == f)
        {
            Guard(m, b, handler!, rim);
            return;
        }
        bool loose = b.Holder == null && !b.Pinned && !(b.LastTouch == f && b.SinceTouch < 0.6f);   // let our own shot go
        if (loose && ClosestToBall(m, b, w))
        {
            Rebound(m, b, w, rim);
            return;
        }
        // Off the ball: spread out round the key, facing the play.
        _run = false;
        MoveToward(handler != null ? HomeX(m) : M.ClampIn(b.Pos.X, rim.X - 160 * S, rim.X + 160 * S), 10 * S);
        FaceTo(b.Pos.X);
        if (f.Grounded && MathF.Abs(f.Vel.X) < 5 * S) f.SetAction(Act.Ready);
    }

    /// <summary>Of everyone going for a loose ball, are we the one who'll get there first?</summary>
    bool ClosestToBall(Match m, Prop b, World w)
    {
        var path = BallPath(b, w);
        float Eta(Figure p)
        {
            for (int i = 0; i < path.Length; i++)
                if (MathF.Abs(path[i].X - p.Base.X) / MathF.Max(p.RunSpeed, 1) <= i * PathStep + 0.1f) return i * PathStep;
            return 9 + MathF.Abs(path[^1].X - p.Base.X) / MathF.Max(p.RunSpeed, 1);
        }
        return m.Players.Where(p => p.Brain.Match == m).OrderBy(Eta).FirstOrDefault() == f;
    }

    // ---------------- with the ball ----------------

    void Handle(Match m, Prop b, World w, Vector2 rim, float field)
    {
        float dx = rim.X - f.Base.X, dist = MathF.Abs(dx);
        float toRim = MathF.Sign(dx == 0 ? -field : dx);
        int team = m.Team.GetValueOrDefault(f);
        var guard = m.Players.Where(p => p != f && (m.Players.Count <= 3 || m.Team.GetValueOrDefault(p) != team))
                             .OrderBy(p => MathF.Abs(p.Base.X - f.Base.X)).FirstOrDefault();
        float gd = guard != null ? MathF.Abs(guard.Base.X - f.Base.X) : 999 * S;
        bool guardBetween = guard != null && (guard.Base.X - f.Base.X) * toRim > 0 && gd < 70 * S;
        float skill = Sk(SkillKind.Shooting);

        // Mid-shot: up, let go at the top, follow through, land.
        if (_hoopPlan is HoopPlan.Jumper or HoopPlan.Layup or HoopPlan.Dunk && (_shotT > 0 || !f.Grounded))
        {
            ShootStep(m, b, rim, field, guard);
            return;
        }
        // After a basket: take it back out past the arc before anything else.
        if (_clearOut)
        {
            f.Dribbling = true;
            _run = true;
            FaceTo(rim.X);
            if (_clearTo <= 0) _clearTo = rng.Range(150, 240) * S;
            if (dist < _clearTo) { f.DesiredVX = -toRim * f.RunSpeed * 0.8f; return; }
            _clearTo = 0;
            _clearOut = false;
            _hoopDecide = 0.2f;
        }
        _hoopDecide -= World.Dt;
        _dunkCd -= World.Dt;
        if (_hoopDecide <= 0 || _hoopPlan == HoopPlan.None)
        {
            _hoopDecide = 0.25f;
            // How likely each is to go in (and what it's worth), crowded or not.
            float contest = guard != null ? M.Clamp01(1 - gd / (60 * S)) : 0;
            float reachUp = f.Height + f.Arm * 0.9f + f.Height * 1.25f;   // standing reach plus a good jump
            bool canDunk = f.Base.Y - rim.Y < reachUp && Stamina > 0.35f;
            var opts = new List<(HoopPlan plan, float value)>();
            // A dunk is a moment: for the bold and showy, and not every time.
            bool breakaway = gd > 90 * S;
            if (dist < 80 * S && canDunk && _dunkCd <= 0) opts.Add((HoopPlan.Dunk, 2 * (0.85f - contest * 0.35f) * (0.25f + P.Bravery * 0.4f + P.Playfulness * 0.3f) * (breakaway ? 1.8f : 1)));
            if (dist < 95 * S) opts.Add((HoopPlan.Layup, 2 * (0.62f + skill * 0.25f - contest * 0.25f)));
            if (dist > 60 * S)
            {
                bool three = dist > 200 * S;
                float pMake = three ? 0.22f + skill * 0.3f - contest * 0.15f : 0.5f - dist / (900 * S) + skill * 0.3f - contest * 0.25f;
                opts.Add((HoopPlan.Jumper, (three ? 3 : 2) * MathF.Max(0.05f, pMake) * (three ? 0.8f + P.Bravery * 0.4f : 1)));
            }
            if (!guardBetween && dist > 70 * S) opts.Add((HoopPlan.Drive, 1.25f + P.Energy * 0.3f - skill * 0.2f));
            if (guard != null && gd < 28 * S && dist < 220 * S) opts.Add((HoopPlan.StepBack, 0.9f + skill * 0.4f));
            var pick = opts.OrderByDescending(o => o.value * rng.Range(0.85f, 1.15f)).FirstOrDefault();
            _hoopPlan = pick.plan == HoopPlan.None ? HoopPlan.Drive : pick.plan;
            if (_hoopPlan is HoopPlan.Jumper or HoopPlan.Layup or HoopPlan.Dunk) { _shotT = 0; _released = false; }
        }
        f.Dribbling = true;
        switch (_hoopPlan)
        {
            case HoopPlan.Drive:
                _run = true;
                FaceTo(rim.X);
                f.DesiredVX = toRim * f.RunSpeed * 0.9f;
                if (dist < 60 * S) _hoopDecide = 0;
                break;
            case HoopPlan.StepBack:
                // A hop back away from them to make room, then decide again.
                FaceTo(rim.X);
                if (f.Grounded && !f.JumpPending) { f.RequestJump(new Vector2(-toRim * 260 * S, -300 * S), 0.04f); _hoopPlan = HoopPlan.None; _hoopDecide = 0.4f; }
                break;
            default:
                ShootStep(m, b, rim, field, guard);
                break;
        }
    }

    void ShootStep(Match m, Prop b, Vector2 rim, float field, Figure? guard)
    {
        float dx = rim.X - f.Base.X, dist = MathF.Abs(dx);
        float toRim = MathF.Sign(dx == 0 ? -field : dx);
        _shotT += World.Dt;
        FaceTo(rim.X);
        f.KeepFacing = true;
        var plan = _hoopPlan;
        if (!_released && f.Grounded && !f.JumpPending)
        {
            // Get there first: a layup from a stride or two out, a dunk from right underneath.
            float want = plan == HoopPlan.Dunk ? 10 * S : plan == HoopPlan.Layup ? 45 * S : dist;
            if (plan != HoopPlan.Jumper && dist > want + 6 * S)
            {
                f.Dribbling = true;
                _run = true;
                f.DesiredVX = toRim * f.RunSpeed;
                _shotT = 0;
                return;
            }
            f.Dribbling = false;
            f.DesiredVX = 0;
            if (_shotT < 0.05f) return;
            // Up we go.
            f.SetAction(plan == HoopPlan.Jumper ? Act.JumpShot : Act.Layup);
            float rise = plan switch
            {
                HoopPlan.Dunk => MathF.Max(20 * S, f.Base.Y - rim.Y - f.Height - f.Arm * 0.5f + 8 * S),
                HoopPlan.Layup => MathF.Max(20 * S, (f.Base.Y - rim.Y - f.Height) * 0.6f),
                _ => f.Height * 0.3f,
            };
            float vx = plan == HoopPlan.Jumper ? 0 : (dx - toRim * 4 * S) / MathF.Max(0.25f, MathF.Sqrt(2 * rise / f.Gravity) * 2);
            f.RequestJump(new Vector2(vx, -MathF.Sqrt(2 * f.Gravity * rise)), plan == HoopPlan.Jumper ? 0.12f : 0.06f);
            if (plan == HoopPlan.Dunk) f.Emote(V("watch this!", "SLAAAM!!", "dunk.", "here goes…!", "to the heavens"), 0.8f);
            return;
        }
        if (_released)
        {
            if (f.Grounded && !f.JumpPending && _shotT > 0.3f) { _hoopPlan = HoopPlan.None; f.SetAction(Act.Stand); }
            return;
        }
        if (f.Grounded) return;   // still crouching
        f.SetAction(plan == HoopPlan.Jumper ? Act.JumpShot : Act.Layup);
        // Let go at the top of the jump (a dunk: over the rim, straight down through it).
        bool top = f.Vel.Y > -60 * S;
        if (plan == HoopPlan.Dunk)
        {
            bool over = MathF.Abs(f.HoldPoint.X - rim.X) < 16 * S && f.HoldPoint.Y < rim.Y + 4 * S;
            if (over || (top && f.HoldPoint.Y < rim.Y + 14 * S && MathF.Abs(f.HoldPoint.X - rim.X) < 26 * S))
            {
                f.Carrying = null; b.Holder = null;
                b.Pos = rim + new Vector2(0, -5 * S);
                b.Release(new Vector2(0, 260 * S));
                b.LastTouch = f;
                m.LastShot = "dunk"; m.Stats.Shot("dunk");
                _dunkCd = 25;
                Stamina = MathF.Max(0, Stamina - 0.05f);
                _released = true;
                // Hang off the rim a moment.
                f.HangAt = rim + new Vector2(-toRim * 2 * S, 0);
                f.HangT = 0.45f;
                Practice(SkillKind.Shooting, 0.012f);
                return;
            }
            if (f.Vel.Y > 80 * S) { _hoopPlan = HoopPlan.Layup; }   // didn't get up there: lay it in instead
            return;
        }
        if (!top) return;
        // The shot: a lob into the rim (a layup off the glass); the further and the more crowded, the less sure.
        float skill = 0.3f + Sk(SkillKind.Shooting) * 0.55f + f.Tastes.Of(Thing.Basketballs) * 0.1f;
        float crowd = guard != null ? M.Clamp01(1 - MathF.Abs(guard.Base.X - f.Base.X) / (50 * S)) : 0;
        float spread = ((1 - skill) * (5 + dist / (16 * S)) + dist / (90 * S)) * S * (1 + crowd * 0.8f);
        var aim = plan == HoopPlan.Layup ? m.Gear[0].Local(0, 80) : rim + new Vector2(0, -3 * S);
        aim += new Vector2(rng.Range(-1, 1) * spread, rng.Range(-0.4f, 0.4f) * spread);
        float T = plan == HoopPlan.Layup ? 0.42f : 0.62f + dist / (1400 * S);
        var v = Ballistics.Launch(f.HoldPoint, aim, b.Grav, Ballistics.DragOf(b.Kind), T);
        f.Carrying = null; b.Holder = null;
        b.Release(v);
        b.LastTouch = f;
        f.ShotFollowT = 0.3f;
        _released = true;
        m.LastShot = plan == HoopPlan.Layup ? "layup" : dist > 200 * S ? "three" : "jumper";
        m.Stats.Shot(m.LastShot);
        Practice(SkillKind.Shooting, 0.01f);
        Practice(SkillKind.Ball, 0.003f);
    }

    // ---------------- without it ----------------

    void Guard(Match m, Prop b, Figure handler, Vector2 rim)
    {
        // Between them and the hoop, close but not on top of them.
        float side = MathF.Sign(rim.X - handler.Base.X);
        if (side == 0) side = 1;
        float x = handler.Base.X + side * (26 + (1 - Sk(SkillKind.Ball)) * 10) * S;
        _run = MathF.Abs(x - f.Base.X) > 40 * S;
        MoveToward(x, 4 * S);
        FaceTo(handler.Base.X);
        if (f.Grounded && MathF.Abs(f.Vel.X) < 30 * S && f.Action != Act.Block) f.SetAction(Act.Ready);
        float gap = MathF.Abs(handler.Base.X - f.Base.X);
        // They're going up to shoot: jump with them, arms up.
        // (Timed to meet the ball: leave as they near the top of their jump, when they let go.)
        if (!handler.Grounded && handler.Vel.Y > -220 * S && handler.Brain._hoopPlan is HoopPlan.Jumper or HoopPlan.Layup && f.Grounded && !f.JumpPending && gap < 50 * S && _stealCd <= 0.4f)
        {
            f.SetAction(Act.Block);
            _contesting = handler;
            _contestAt = _t0;
            m.Stats.BlockJumps++;
            f.RequestJump(new Vector2((handler.Base.X - f.Base.X) * 1.6f, -MathF.Sqrt(2 * f.Gravity * f.Height * 0.45f)), 0.02f);
            _stealCd = 1;
            return;
        }
        // Reach in for it when it's low in the dribble.
        if (gap < 30 * S && b.Holder == handler && b.Pos.Y > handler.Base.Y - handler.Leg && _stealCd <= 0 && f.Grounded)
        {
            _stealCd = 0.7f;
            f.SetAction(Act.Swat);
            if (rng.NextDouble() < 0.06f + Sk(SkillKind.Ball) * 0.1f + P.Aggression * 0.06f - handler.Brain.Sk(SkillKind.Ball) * 0.05f)
            {
                handler.Carrying = null; b.Holder = null; handler.Dribbling = false;
                b.Release(new Vector2(-side * rng.Range(150, 280) * S, -rng.Range(120, 220) * S));
                b.LastTouch = f;
                m.Stats.Steals++;
                f.Emote(V("got it!", "STOLEN!!", "mine.", "o-oh!", "the ball chose me"), 0.8f);
            }
        }
    }

    Figure? _contesting;
    float _contestAt;

    /// <summary>Up in the air contesting a shot: if a hand gets to the ball just after it's let go, it's swatted away.</summary>
    bool TryBlock(Match m, Prop b)
    {
        if (_contesting is not { } handler) return false;
        if (f.Grounded) { if (!f.JumpPending && _t0 - _contestAt > 0.3f) _contesting = null; return false; }   // landed (not just crouching to go)
        f.SetAction(Act.Block);   // arms up all the way
        float side = MathF.Sign(m.RimCentre.X - handler.Base.X);
        if (side == 0) side = 1;
        if (b.Holder == null && b.LastTouch == handler && b.SinceTouch < 0.35f &&
            (Vector2.Distance(f.Jt[J.HandN], b.Pos) < b.Radius + 14 * S || Vector2.Distance(f.Jt[J.HandF], b.Pos) < b.Radius + 14 * S))
        {
            _contesting = null;   // one go at it
            if (rng.NextDouble() > 0.35f + Sk(SkillKind.Ball) * 0.3f + (f.Height - handler.Height) / handler.Height) return false;
            b.Vel = new Vector2(-side * MathF.Abs(b.Vel.X) * 0.4f - side * 120 * S, -160 * S);
            b.LastTouch = f;
            m.Stats.Blocks++;
            f.Emote(V("blocked!", "REJECTED!!", "no.", "s-sorry!", "not today"), 0.9f);
            return true;
        }
        return false;
    }

    void Rebound(Match m, Prop b, World w, Vector2 rim)
    {
        var path = BallPath(b, w);
        // Where can we meet it: high enough to grab (a jump if need be), or on the floor.
        Vector2 meet = path[^1];
        float reachTop = f.Height + f.Arm * 0.6f + f.Height * 0.7f;
        for (int i = 0; i < path.Length; i++)
        {
            float above = f.Base.Y - path[i].Y;
            if (above > reachTop) continue;
            if (MathF.Abs(path[i].X - f.Base.X) / MathF.Max(f.RunSpeed, 1) <= i * PathStep + 0.05f) { meet = path[i]; break; }
        }
        float stand = meet.X - MathF.Sign(meet.X - f.Base.X == 0 ? 1 : meet.X - f.Base.X) * (b.Radius + 6 * S);
        _run = MathF.Abs(stand - f.Base.X) > 30 * S;
        MoveToward(stand, 4 * S);
        FaceTo(b.Pos.X);
        Vector2 sh = f.Jt[J.Neck];
        float d = Vector2.Distance(b.Pos, sh);
        bool high = b.Pos.Y < sh.Y - f.Arm * 0.7f;
        // Up for it.
        if (high && f.Grounded && !f.JumpPending && MathF.Abs(b.Pos.X - f.Base.X) < 30 * S && b.Vel.Y > -100 * S && f.Base.Y - b.Pos.Y < reachTop)
        {
            float rise = MathF.Max(16 * S, sh.Y - b.Pos.Y - f.Arm * 0.7f);
            f.RequestJump(new Vector2((b.Pos.X - f.Base.X) * 2, -MathF.Sqrt(2 * f.Gravity * rise)), 0.03f);
            return;
        }
        if (d < f.Arm * 1.05f && !b.OnGround)
        {
            Grab(m, b, "rebound");
            return;
        }
        // On the floor: bend and pick it up.
        if (b.OnGround || b.Pos.Y > f.Base.Y - f.Leg * 0.8f)
        {
            if (MathF.Abs(b.Pos.X - f.Base.X) < b.Radius + f.Arm * 0.8f)
            {
                f.SetAction(Act.Scoop);
                f.HoldN = b.Pos + new Vector2(0, -b.Radius * 0.8f);
                b.Ghost = f; b.GhostUntil = World.Now + 0.15;
                _gatherT += World.Dt;
                if (_gatherT > 0.25f) { Grab(m, b, "gather"); _gatherT = 0; }
            }
            else _gatherT = 0;
        }
    }

    void Grab(Match m, Prop b, string how)
    {
        _clearOut = m.TakeItBack;
        m.TakeItBack = false;
        if (how == "rebound" && b.LastTouch != f) m.Stats.Rebounds++;
        b.Holder = f;
        f.Carrying = b;
        b.LastTouch = f;
        _hoopPlan = HoopPlan.None;
        _hoopDecide = 0.3f;   // gather yourself first
        f.SetAction(Act.Stand);
    }
}
