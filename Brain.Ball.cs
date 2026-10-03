using System.Numerics;

namespace Doodlefolk;

enum BallPlay { Kick, Pass, Dribble, Juggle, Carry }

/// <summary>Playing with balls: kicking (at windows, at friends), dribbling, juggling, carrying,
/// throwing and catching. Passes set the ball's PassTarget so the receiver gets ready for it.</summary>
sealed partial class Brain
{
    Prop? _ball;
    Figure? _passTo, _passFrom;
    Vector2 _kickVel, _throwAt;
    bool _didContact, _selfCatch;
    int _count, _countMax;
    float _carryX;

    /// <summary>Chance each juggling tap succeeds: ~0.89 for an average figure, ~0.97 for a show-off.</summary>
    float Skill => 0.72f + Sk(SkillKind.Juggling) * 0.27f;

    /// <summary>A ball worth looking at (flying past nearby, or the one we're playing with).</summary>
    Prop? InterestingBall(World w)
    {
        if (_ball != null && _g is G.Kick or G.Dribble or G.Juggle or G.Catch or G.Throw && w.Props.Contains(_ball)) return _ball;
        foreach (var b in w.Props)
            if (b.Vel.Length() > 350 * S && Vector2.Distance(b.Pos, f.Jt[J.Head]) < 600 * S) return b;
        return null;
    }

    Prop? NearestFreeBall(World w, float maxDist)
    {
        Prop? best = null;
        float bd = maxDist;
        foreach (var b in w.Props)
        {
            if (!b.Free || (b.PassTarget != null && b.PassTarget != f && b.SinceTouch < 3) || Unreachable(b)) continue;
            float d = Vector2.Distance(b.Pos, f.Base);
            if (d < bd) { bd = d; best = b; }
        }
        return best;
    }

    (float, Action)? BallOption(World w)
    {
        if (NearestFreeBall(w, 1600 * S) is not { } b) return null;
        float kindLove = Tastes.ForProp(b.Kind) is Thing k ? f.Tastes.Of(k) : 0;
        float weight = P.Playfulness * (0.5f + Boredom) * Stamina * 1.8f * Taste(Thing.PlayingBall) * MathF.Max(0.2f, 1 + kindLove);
        return (weight, () => GoToBall(b, w, () => ChoosePlay(b, w, null)));
    }

    bool ForceBall(World w, BallPlay? play)
    {
        if (NearestFreeBall(w, float.MaxValue) is not { } b) return false;
        GoToBall(b, w, () => ChoosePlay(b, w, play));
        return true;
    }

    /// <summary>Walk (or run) to stand next to the ball, on <paramref name="fromSide"/> of it if given.</summary>
    void GoToBall(Prop b, World w, Action then, float? fromSide = null)
    {
        _ball = b;
        bool run = Vector2.Distance(b.Pos, f.Base) > 300 * S && Stamina > 0.3f;
        Navigate(() =>
        {
            if (!w.Props.Contains(b) || !b.Free) return null;
            // Head for where it's going to be (a rolling ball slows as it goes), not where it is now.
            float gap = MathF.Abs(b.Pos.X - f.Base.X);
            float eta = Math.Clamp(gap / MathF.Max(f.RunSpeed, 1), 0, 1.2f);
            float x = b.Pos.X + b.Vel.X * eta * 0.6f;
            float side = fromSide ?? -MathF.Sign(x - f.Base.X);
            if (side == 0) side = -f.Facing;
            float y = w.Env.Below(x, b.Pos.Y)?.Y ?? b.Pos.Y + b.Radius;
            return new Vector2(x + side * (b.Radius + 7 * S), y);
        }, 5 * S, run, then, WalkPurpose.Ball);
        _navAbout = b;   // a ball that can't be reached is left alone for a while
    }

    /// <summary>The ball we're going for or bending down to (our own feet only nudge it: we trap it, not kick it on).</summary>
    public Prop? Seeking => (_g == G.Walk && _purpose == WalkPurpose.Ball) || _g == G.Scoop ? _ball : null;

    /// <summary>Easing off on the way to a ball: a run becomes a walk, then careful steps, so we arrive beside it
    /// instead of running into it.</summary>
    float BallApproachSpeed(float speed)
    {
        if (_purpose != WalkPurpose.Ball || _ball == null) return speed;
        float gap = MathF.Abs(_ball.Pos.X - f.Base.X) - _ball.Radius;
        if (gap > 90 * S) return gap > 160 * S ? speed : MathF.Min(speed, f.WalkSpeed * 1.15f);
        return MathF.Min(speed, f.WalkSpeed * M.Lerp(0.4f, 1, M.Clamp01((gap - 10 * S) / (60 * S))));
    }

    int _scoopMiss;

    /// <summary>For the contact sheets and tests: go and pick up this ball.</summary>
    public void FetchNow(Prop b, World w) => ScoopThen(b, w, () => { });

    /// <summary>Bend down and pick it up properly (a crouch, both hands to it, then up), instead of it jumping into
    /// our hands. If it's rolled off, go after it again; after a few tries, let it go.</summary>
    void BeginScoop(Prop b, Figure? throwTo)
    {
        Go(G.Scoop, 1.4f);
        _ball = b;
        _passTo = throwTo;
        FaceTo(b.Pos.X);
    }

    Action _afterScoop = () => { };

    void ScoopThen(Prop b, World w, Action then)
    {
        _afterScoop = then;
        _scoopMiss = 0;
        GoToBall(b, w, () => BeginScoop(b, null));
    }

    void DoScoop(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b) || !b.Free) { Go(G.Idle, 1); return; }
        f.DesiredVX = 0;
        float reach = f.Arm * 1.25f + b.Radius;
        bool low = b.Pos.Y > f.Base.Y - f.Leg - b.Radius;
        if (MathF.Abs(b.Pos.X - f.Base.X) > reach || !low || _t > _dur)
        {
            // It got away.
            if (++_scoopMiss > 3)
            {
                f.Emote(V("forget it", "COME BACK!!", "…fine. stay there.", "it keeps running away…", "the ball wishes to be free"), 1.3f);
                _scoopMiss = 0;
                Go(G.Idle, 1.5f);
                return;
            }
            var keep = _passTo;
            GoToBall(b, w, () => BeginScoop(b, keep));
            return;
        }
        FaceTo(b.Pos.X);
        f.SetAction(Act.Scoop);
        // Both hands to the ball, one each side.
        f.HoldN = b.Pos + new Vector2(f.Facing * b.Radius * 0.55f, -b.Radius * 0.1f);
        f.HoldF = b.Pos - new Vector2(f.Facing * b.Radius * 0.55f, b.Radius * 0.1f);
        float handGap = Vector2.Distance((f.Jt[J.HandN] + f.Jt[J.HandF]) / 2, b.Pos);
        // Hands on it: it stops rolling; a moment later, up it comes.
        if (handGap < b.Radius + 6 * S) b.Vel *= 0.6f;
        if (_t > 0.45f && handGap < b.Radius + 8 * S)
        {
            _scoopMiss = 0;
            PickUp(b, _passTo);
            var then = _afterScoop;
            _afterScoop = () => { };
            then();
        }
    }

    Figure? PassCandidate(World w, Prop b)
    {
        Figure? best = null;
        float bs = 0;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || !o.Grounded || o.Brain.Asleep || o.Brain._g is G.Busy || o.Brain.InFight) continue;
            if (RelationTo(o) is Relation.Ignore or Relation.Enemies) continue;
            float d = MathF.Abs(o.Base.X - b.Pos.X);
            if (d < 60 * S || d > 1100 * S || MathF.Abs(o.Base.Y - f.Base.Y) > 500 * S) continue;
            float a = AffinityWith(o);
            if (a < -0.2f) continue;
            float score = (a + 0.6f) * (o == _passFrom ? 2 : 1) / (1 + d / (500 * S));
            if (score > bs) { bs = score; best = o; }
        }
        return best;
    }

    void ChoosePlay(Prop b, World w, BallPlay? forced)
    {
        if (!w.Props.Contains(b) || !b.Free) { Go(G.Idle, 1); return; }
        FaceTo(b.Pos.X);
        var friend = PassCandidate(w, b);
        bool small = b.SizeMul <= 1.8f;
        BallPlay play;
        if (forced is BallPlay fp) play = fp == BallPlay.Pass && friend == null ? BallPlay.Kick : fp;
        else
        {
            var opts = new List<(float w, BallPlay p)>
            {
                (0.7f, BallPlay.Kick),
                (0.4f + P.Energy * 0.6f, BallPlay.Dribble),
            };
            if (friend != null) opts.Add((P.Sociability * 1.2f + MathF.Max(0, AffinityWith(friend)) + 0.3f, BallPlay.Pass));
            if (small) opts.Add((P.Playfulness * 1.3f * Taste(Thing.Juggling), BallPlay.Juggle));
            if (small) opts.Add((0.5f, BallPlay.Carry));
            float roll = rng.Range(0, opts.Sum(o => o.w));
            play = opts[0].p;
            foreach (var (wt, p) in opts) { roll -= wt; if (roll <= 0) { play = p; break; } }
        }
        if (!small && play is BallPlay.Juggle or BallPlay.Carry) play = BallPlay.Kick;

        switch (play)
        {
            case BallPlay.Kick:
                BeginKick(b, KickAim(w, b), null);
                break;
            case BallPlay.Pass:
            {
                var to = forced == BallPlay.Pass ? friend : friend ?? PassCandidate(w, b);
                if (to == null) { BeginKick(b, KickAim(w, b), null); break; }
                float dirTo = MathF.Sign(to.Base.X - b.Pos.X);
                if (dirTo != f.Facing)
                {
                    // Get round to the other side of the ball first.
                    GoToBall(b, w, () => { FaceTo(b.Pos.X); BeginKick(b, PassVelocity(b, to), to); }, -dirTo);
                    break;
                }
                BeginKick(b, PassVelocity(b, to), to);
                break;
            }
            case BallPlay.Dribble:
                _count = 0;
                _countMax = rng.Next(3, 8);
                Go(G.Dribble, 25);
                _ball = b;
                break;
            case BallPlay.Juggle:
                _count = 0;
                _countMax = rng.Next(8, 25);
                Go(G.Juggle, 30);
                _ball = b;
                break;
            case BallPlay.Carry:
                BeginScoop(b, friend);
                break;
        }
    }

    Vector2 Strength(Vector2 desired, Prop b) =>
        M.ClampLength(desired * MathF.Sqrt(b.Mass), (1300 + 900 * P.Energy) * S);

    Vector2 KickAim(World w, Prop b)
    {
        // Sometimes aim for the top of a window in front of us.
        if (rng.NextDouble() < 0.5)
        {
            var targets = w.Env.Platforms.Where(p => !p.Solid && p.X2 - p.X1 > 40 * S &&
                MathF.Sign((p.X1 + p.X2) / 2 - b.Pos.X) == f.Facing && MathF.Abs((p.X1 + p.X2) / 2 - b.Pos.X) < 1200 * S).ToList();
            if (targets.Count > 0)
            {
                var p = targets[rng.Next(targets.Count)];
                float x = rng.Range(p.X1 + 15 * S, p.X2 - 15 * S);
                if (SolveLob(b.Pos, new(x, p.Y - b.Radius), b.Grav, 30 * S, 400 * S, 1700 * S, out var v)) return Strength(v, b);
            }
        }
        return Strength(new Vector2(f.Facing * rng.Range(600, 1100) * S, -rng.Range(450, 950) * S), b);
    }

    Vector2 PassVelocity(Prop b, Figure to)
    {
        Vector2 target = to.Base + new Vector2(-MathF.Sign(to.Base.X - b.Pos.X) * 10 * S, -b.Radius);
        if (SolveLob(b.Pos, target, b.Grav, 25 * S + MathF.Abs(target.X - b.Pos.X) * 0.08f, 400 * S, 1800 * S, out var v))
            return Strength(v, b);
        return Strength(new Vector2(MathF.Sign(target.X - b.Pos.X) * 700 * S, -500 * S), b);
    }

    void BeginKick(Prop b, Vector2 vel, Figure? passTo)
    {
        Go(G.Kick, 2);
        _ball = b;
        _kickVel = vel;
        _passTo = passTo;
        _didContact = false;
        if (MathF.Abs(vel.X) > 1) f.Facing = MathF.Sign(vel.X);
    }

    /// <summary>Kick contact check; returns true on the frame the foot connects.</summary>
    bool KickContact(Prop b, Vector2 vel, Figure? passTo, float reach)
    {
        if (_didContact || f.ActionT < Figure.KickTime * Figure.KickContact) return false;
        _didContact = true;
        if (!b.Free || Vector2.Distance(f.Jt[J.FootN], b.Pos) > b.Radius + reach) { f.Emote("?", 0.8f); return false; }
        b.Kick(vel, f);
        b.PassTarget = passTo;
        Stamina = MathF.Max(0, Stamina - 0.01f);
        return true;
    }

    void DoKick(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b)) { Go(G.Idle, 1); return; }
        f.SetAction(Act.Kick);
        f.DesiredVX = 0;
        if (KickContact(b, _kickVel, _passTo, 11 * S) && _passTo != null) f.Emote(rng.NextDouble() < 0.5 ? "!" : "♪", 0.8f);
        if (f.ActionT < Figure.KickTime) return;
        if (_passTo != null && _didContact)
        {
            Go(G.Idle, 2.5f);
            _idleLook = _passTo.Jt[J.Head];
            _idleLookUntil = 2.5f;
        }
        else Go(G.Idle, rng.Range(1, 2));
    }

    void DoDribble(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b) || !b.Free) { Go(G.Idle, 1); return; }
        f.LookAt = b.Pos;
        if (f.Action == Act.Kick)
        {
            f.DesiredVX = f.Facing * f.WalkSpeed * 0.4f;
            if (KickContact(b, Strength(new Vector2(f.Facing * (f.RunSpeed * 0.9f + 80 * S), -90 * S), b), null, 12 * S)) _count++;
            if (f.ActionT >= Figure.KickTime) f.SetAction(Act.Stand);
            return;
        }
        if (_count >= _countMax || _t > _dur)
        {
            if (rng.NextDouble() < 0.5) GoToBall(b, w, () => BeginKick(b, KickAim(w, b), null));
            else { f.Emote("♪", 0.8f); Go(G.Cheer, 0.8f); }
            return;
        }
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (b.Pos.Y > f.Base.Y + 10 * S || seg == null || b.Pos.X < seg.X1 - b.Radius || b.Pos.X > seg.X2 + b.Radius)
        {
            // It got away (rolled off the edge): chase it down.
            GoToBall(b, w, () => ChoosePlay(b, w, BallPlay.Dribble));
            return;
        }
        float ahead = (b.Pos.X - f.Base.X) * f.Facing;
        if (ahead > b.Radius + 3 * S && ahead < b.Radius + 10 * S && b.Pos.Y > f.Base.Y - b.Radius * 2.5f)
        {
            f.SetAction(Act.Kick);
            _didContact = false;
            return;
        }
        _run = true;
        if (ahead < 0) f.Facing = -f.Facing;
        MoveToward(b.Pos.X - f.Facing * (b.Radius + 8 * S), 3 * S);
    }

    void DoJuggle(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b) || !b.Free) { Go(G.Idle, 1); return; }
        f.LookAt = b.Pos;
        b.Juggler = f;   // the ball passes through the juggler's body; only the foot touches it
        f.KeepFacing = _count > 0;   // shuffle backwards rather than turning the tapping foot away
        float tapX = f.Base.X + f.Facing * f.Leg * 0.5f;
        if (f.Action == Act.Tap)
        {
            f.DesiredVX = 0;
            if (!_didContact && f.ActionT >= Figure.TapTime * Figure.TapContact)
            {
                _didContact = true;
                float footDist = Vector2.Distance(f.Jt[J.FootN], b.Pos);
                bool hit = footDist < b.Radius + 18 * S && (_count == 0 || rng.NextDouble() < Skill);
                if (hit) Practice(SkillKind.Juggling, 0.004f);
                World.Log($"{f.Name} tap #{_count}: footDist={footDist / S:F1}S ball=({b.Pos.X - f.Base.X:F0},{b.Pos.Y - f.Base.Y:F0}) vel=({b.Vel.X:F0},{b.Vel.Y:F0}) foot=({f.Jt[J.FootN].X - f.Base.X:F0},{f.Jt[J.FootN].Y - f.Base.Y:F0}) hit={hit}");
                if (!hit)
                {
                    b.Juggler = null;
                    if (_count >= 5) Cheered(0.3f);
                    f.Emote(_count >= 5 ? "♪" : "…", 1);
                    Go(_count >= 5 ? G.Cheer : G.Idle, 1);
                    return;
                }
                // Pop it straight back up over the tapping spot, with a little wobble.
                float vx = (tapX - b.Pos.X) * 2.5f + rng.Range(-20, 20) * S;
                b.Kick(new Vector2(vx, -rng.Range(560, 660) * S) * MathF.Sqrt(b.Mass), f);
                _count++;
                if (_count % 5 == 0) f.Emote(_count.ToString(), 0.8f);
                Stamina = MathF.Max(0, Stamina - 0.004f);
            }
            if (f.ActionT >= Figure.TapTime) f.SetAction(Act.Stand);
            return;
        }
        if (_count >= _countMax || _t > _dur)
        {
            // Finish with a flourish: catch it (if it fits) or boot it away.
            if (b.SizeMul <= 1.8f && b.Vel.Y > 0 && Vector2.Distance(f.HoldPoint, b.Pos) < 40 * S) { PickUp(b, null); f.Emote("♪", 1); }
            else if (b.Vel.Y > 0) { BeginKick(b, KickAim(w, b), null); }
            return;
        }
        if (b.OnGround)
        {
            if (_count > 0) { b.Juggler = null; f.Emote(_count >= 5 ? "♪" : "…", 1); Go(_count >= 5 ? G.Cheer : G.Idle, 1); return; }
            // Flick it up to start.
            if (MoveToward(b.Pos.X - f.Facing * f.Leg * 0.5f, 4 * S)) { FaceTo(b.Pos.X); f.SetAction(Act.Tap); _didContact = false; }
            return;
        }
        // Keep the tapping foot under the ball and time the tap.
        float targetX = b.Pos.X - f.Facing * f.Leg * 0.5f;
        f.DesiredVX = Math.Clamp((targetX - f.Base.X) * 7, -f.RunSpeed * 0.7f, f.RunSpeed * 0.7f);
        if (b.Vel.Y > 0)
        {
            float th = Figure.TapTime * Figure.TapContact;
            float yPred = b.Pos.Y + b.Vel.Y * th + 0.5f * b.Grav * th * th;
            float tapH = f.Base.Y - f.Leg * 0.45f;
            if (yPred + b.Radius >= tapH && b.Pos.Y < tapH) { f.SetAction(Act.Tap); _didContact = false; }
        }
    }

    void PickUp(Prop b, Figure? throwTo)
    {
        if (b.ThrownByUser && b.SinceTouch < 6) { GotBallFromUser(b); b.ThrownByUser = false; }
        Go(G.Carry, throwTo != null ? rng.Range(0.6f, 1.4f) : rng.Range(2, 5));
        _ball = b;
        _passTo = throwTo;
        b.Holder = f;
        b.PassTarget = null;
        f.Carrying = b;
        // Playing catch with you: that's a catch (if you threw it), and it goes back to you.
        if (World.Current?.Game is { Kind: GameKind.Catch, Over: false } ug && ug.Thrower == f && ug.Ball == b)
        {
            _bringToUser = true;
            if (ug.Flight == 2) { ug.Streak++; ug.Best = Math.Max(ug.Best, ug.Streak); World.Log($"catch: {f.Name} caught it (streak {ug.Streak})"); if (ug.Streak >= 3) f.Emote($"x{ug.Streak}!", 1); }
            ug.Flight = 0;
        }
        _carryX = f.Base.X + rng.Range(-250, 250) * S;
    }

    void DoCarry(World w)
    {
        var b = _ball;
        if (b == null || f.Carrying != b) { Go(G.Idle, 1); return; }
        if (HuntThrow(w)) return;
        if (BringBallToUser(w)) return;
        if (_passTo == null) MoveToward(_carryX, 5 * S);
        else { f.DesiredVX = 0; FaceTo(_passTo.Base.X); f.LookAt = _passTo.Jt[J.Head]; }
        if (_t < _dur) return;
        var to = _passTo != null && w.Figures.Contains(_passTo) && _passTo.Mode == Mode.Control ? _passTo : PassCandidate(w, b);
        if (to != null && rng.NextDouble() < 0.8) BeginThrow(b, to.Base + new Vector2(0, -to.Torso), to, false);
        else BeginThrow(b, f.Base, null, true);
    }

    void BeginThrow(Prop b, Vector2 at, Figure? to, bool selfCatch)
    {
        Go(G.Throw, Figure.ThrowTime + 0.15f);
        _ball = b;
        _throwAt = at;
        _passTo = to;
        _selfCatch = selfCatch;
        _didContact = false;
        if (!selfCatch) FaceTo(at.X);
    }

    void DoThrow(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b)) { Go(G.Idle, 1); return; }
        f.SetAction(Act.Throw);
        f.DesiredVX = 0;
        if (f.Carrying == b && f.ActionT >= Figure.ThrowTime * Figure.ThrowRelease)
        {
            Vector2 v;
            if (_selfCatch) v = new Vector2(f.Facing * rng.Range(0, 60) * S, -rng.Range(800, 1050) * S);
            else if (_fastball) v = FastballVelocity(f.HoldPoint, _throwAt, b.Grav);
            else if (!SolveLob(f.HoldPoint, _throwAt, b.Grav, 40 * S, 450 * S, 1900 * S, out v))
            {
                // Too steep for a lob (say, straight up to your cursor): throw it so it peaks right there.
                Vector2 d = _throwAt - f.HoldPoint;
                if (d.Y < -10 * S)
                {
                    float vy = -MathF.Sqrt(2 * b.Grav * -d.Y), t = -vy / b.Grav;
                    v = new Vector2(d.X / t, vy);
                }
                else v = new Vector2(MathF.Sign(_throwAt.X - f.Base.X) * 800 * S, -700 * S);
            }
            f.Carrying = null;
            b.Release(v);
            b.LastTouch = f;
            b.ThrownByUser = false;
            b.PassTarget = _selfCatch ? f : _passTo;
            b.CursorThrow = _fastball ? f : null;
            if (_passTo != null && !_fastball) w.Witness(f, _passTo, SocialAct.Kind, 0.25f);
            _fastball = false;
            Stamina = MathF.Max(0, Stamina - 0.01f);
        }
        if (f.ActionT < Figure.ThrowTime) return;
        if (_selfCatch) { Go(G.Catch, 4); _ball = b; _passFrom = null; }
        else
        {
            Go(G.Idle, 2.5f);
            if (_passTo != null) { _idleLook = _passTo.Jt[J.Head]; _idleLookUntil = 2.5f; }
        }
    }

    /// <summary>A ball is on its way to us: get ready to catch or trap it.</summary>
    bool IncomingPass(World w)
    {
        if (_g == G.Catch) return false;
        foreach (var b in w.Props)
        {
            if (b.PassTarget != f || !b.Free || b.SinceTouch > 4 || b.LastTouch == f) continue;
            Go(G.Catch, 5);
            _ball = b;
            _passFrom = b.LastTouch;
            return true;
        }
        return false;
    }

    void DoCatch(World w)
    {
        var b = _ball;
        if (b == null || !w.Props.Contains(b) || !b.Free || _t > _dur) { Go(G.Idle, 1); return; }
        f.LookAt = b.Pos;
        FaceTo(b.Pos.X);
        _run = true;
        bool catchable = b.SizeMul <= 1.8f;
        if (!b.OnGround && catchable)
        {
            f.SetAction(Act.Ready);
            // Move to where the ball will drop through chest height.
            float hy = f.HoldPoint.Y, g = b.Grav;
            float disc = b.Vel.Y * b.Vel.Y + 2 * g * (hy - b.Pos.Y);
            if (disc > 0 && b.Vel.Y > -50 * S)
            {
                float t = (-b.Vel.Y + MathF.Sqrt(disc)) / g;
                float x = b.Pos.X + b.Vel.X * t;
                MoveToward(x - f.Facing * MathF.Min(b.Radius + 4 * S, f.Arm * 0.75f), 4 * S);
            }
            else f.DesiredVX = 0;
            if (Vector2.Distance(f.HoldPoint, b.Pos) < b.Radius + 11 * S)
            {
                f.Emote("♪", 0.8f);
                Cheered(0.15f);
                var from = _passFrom;
                bool back = from != null && from != f && w.Figures.Contains(from) &&
                            rng.NextDouble() < (P.Playfulness * 0.6f + 0.3f) * Stamina && Boredom < 0.85f;
                if (from != null && from != f) AddAffinity(from, 0.04f);
                PickUp(b, back ? from : null);
            }
            return;
        }
        // On the ground (or too big to catch): trap it with a foot.
        f.SetAction(Act.Stand);
        float spot = b.Pos.X - f.Facing * (b.Radius + 7 * S);
        if (!MoveToward(spot, 6 * S) || !b.OnGround) return;
        b.Vel *= 0.15f;
        var passer = _passFrom;
        if (passer != null && passer != f && w.Figures.Contains(passer) && rng.NextDouble() < (P.Playfulness * 0.6f + 0.3f) * Stamina)
        {
            _passFrom = passer;
            ChoosePlay(b, w, BallPlay.Pass);
        }
        else ChoosePlay(b, w, null);
    }
}
