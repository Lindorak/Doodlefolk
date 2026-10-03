using System.Numerics;

namespace Doodlefolk;

/// <summary>Playing sports: getting a game together, and how each player plays. Soccer: strikers dribble and
/// shoot, keepers guard the mouth and jump for high balls. Basketball: take the ball, find a spot, shoot (or dunk),
/// everyone else rebounds. Tennis/badminton: rally over the net with rackets.</summary>
sealed partial class Brain
{
    public Match? Match;
    bool _toMatch;
    float _kickAt = -1, _actCd;
    Vector2? _shotSpot;
    bool _dunking, _serving;
    float _serveT;

    static PropKind BallFor(Sport s) => s switch { Sport.Soccer => PropKind.SoccerBall, Sport.Basketball => PropKind.Basketball, Sport.Tennis => PropKind.TennisBall, _ => PropKind.Shuttlecock };

    (float, Action)? SportOption(World w)
    {
        if (Match != null || Stamina < 0.4f || InFight) return null;
        (float score, Item gear)? best = null;
        foreach (var it in w.Items)
        {
            if (it.Def.Sport == null || !it.OnGround || it.Held || w.Matches.Any(m => m.Gear.Contains(it))) continue;
            float d = Vector2.Distance(it.Pos, f.Base);
            if (d > 1800 * S) continue;
            float score = (P.Playfulness * 0.8f + P.Sociability * 0.4f + P.Energy * 0.3f) * Taste(Thing.PlayingBall) / (1 + d / (800 * S));
            if (best == null || score > best.Value.score) best = (score, it);
        }
        if (best is not { } b) return null;
        return (b.score * 1.8f, () => StartMatch(b.gear, w));
    }

    /// <summary>Asked to join a game.</summary>
    /// <summary>Already promised elsewhere: a game with you, or a tournament.</summary>
    bool Engaged => HapRole.Length > 0 || (World.Current.Game is { Over: false } ug && ug.Players.Contains(f)) || (World.Current.Tourney is { Over: false } tn && tn.Entrants.Contains(f));

    bool InvitePlay(Figure from, Sport s)
    {
        if (Engaged) return false;
        bool free = f.Mode == Mode.Control && f.Grounded && Match == null && !InFight && f.Carrying == null &&
                    _g is G.Idle or G.Watch or G.Walk or G.SitFloor or G.Juggle or G.Dribble;
        if (!free) return false;
        float yes = 0.15f + P.Playfulness * 0.4f + P.Sociability * 0.2f + f.Tastes.Of(Thing.PlayingBall) * 0.35f + AffinityWith(from) * 0.3f - Annoyance * 0.5f - (1 - Stamina) * 0.4f
                    - (_g == G.Walk && _purpose is WalkPurpose.Look or WalkPurpose.Watch or WalkPurpose.Heart ? 0.6f : 0)   // busy going to see something
                    - (HasPencil ? 0.55f : 0);                                                           // busy drawing
        if (rng.NextDouble() < yes) { f.Emote(rng.NextDouble() < 0.5 ? "!" : "♪", 0.9f); return true; }
        f.Emote("…", 0.8f);
        return false;
    }

    void StartMatch(Item gear, World w)
    {
        var kind = Enum.Parse<Sport>(gear.Def.Sport!);
        var gearList = new List<Item> { gear };
        if (kind == Sport.Soccer)
        {
            // A second goal facing this one makes it a proper match.
            var other = w.Items.FirstOrDefault(i => i != gear && i.Def.Sport == "Soccer" && i.OnGround && MathF.Abs(i.Pos.Y - gear.Pos.Y) < 40 * S &&
                                                    MathF.Abs(i.Pos.X - gear.Pos.X) < 1400 * S && !w.Matches.Any(m => m.Gear.Contains(i)));
            if (other != null) gearList.Add(other);
        }
        // Bring the right ball (or find one).
        var kindBall = BallFor(kind);
        var ball = w.Props.Where(p => (p.Kind == kindBall || (kind == Sport.Soccer && p.Kind == PropKind.Ball)) && p.Holder == null && !w.Matches.Any(m => m.Ball == p))
                          .OrderBy(p => Vector2.Distance(p.Pos, gear.Pos)).FirstOrDefault();
        if (ball == null || Vector2.Distance(ball.Pos, gear.Pos) > 1500 * S)
        {
            if (w.MakeProp == null) return;
            ball = w.MakeProp(kindBall);
        }
        var m = new Match(kind, gearList, ball);
        var players = new List<Figure> { f };
        int want = kind switch { Sport.Soccer => gearList.Count == 2 ? 5 : 3, Sport.Basketball => 3, _ => rng.NextDouble() < 0.3 ? 3 : 1 };
        foreach (var o in w.Figures.Where(o => o != f && Vector2.Distance(o.Base, gear.Pos) < 1600 * S).OrderByDescending(o => AffinityWith(o)))
        {
            if (players.Count > want) break;
            if (RelationTo(o) is Relation.Ignore) continue;
            if (o.Brain.InvitePlay(f, kind)) players.Add(o);
        }
        if (kind is Sport.Tennis or Sport.Badminton && players.Count % 2 == 1 && players.Count > 1) players.RemoveAt(players.Count - 1);
        if (players.Count < Match.MinPlayers(kind)) { f.Emote("…", 1); Go(G.Idle, 2); return; }
        // Teams: friends and same colours together where possible.
        if (kind == Sport.Soccer && gearList.Count == 1)
        {
            // Shootout: the bravest plays keeper, everyone else shoots.
            var keeper = players.Count > 1 ? players.OrderByDescending(p => p.Traits.Bravery - p.Traits.Energy * 0.3f).First() : null;
            foreach (var p in players) m.Team[p] = p == keeper ? 1 : 0;
        }
        else
        {
            var order = players.OrderBy(p => p.Team).ThenBy(p => p.Id).ToList();
            for (int i = 0; i < order.Count; i++) m.Team[order[i]] = kind is Sport.Tennis or Sport.Badminton ? i % 2 : i % 2;
        }
        foreach (var p in players) { m.Players.Add(p); m.Points[p] = 0; }
        m.ServeTeam = rng.Next(2);
        m.Pause = 1.2f;
        w.Matches.Add(m);
        foreach (var p in players) p.Brain.JoinMatch(m, w);
        f.Emote(kind switch { Sport.Soccer => "⚽", Sport.Basketball => "🏀", _ => "!" }, 1.2f);
    }

    void JoinMatch(Match m, World w)
    {
        Go(G.Sport, 600);
        Match = m;
        _kickAt = -1;
        _shotSpot = null;
        _dunking = _serving = false;
        string key = m.Kind == Sport.Tennis ? "racket" : "badmintonracket";
        if (m.Kind is Sport.Tennis or Sport.Badminton && f.Weapon?.Def.Key != key)
        {
            // Use a spare racket lying around before conjuring a new one (so they don't pile up).
            var spare = w.Items.Where(i => i.Def.Key == key && i.Holder == null && i.User == null && Vector2.Distance(i.Pos, f.Base) < 2500 * S)
                               .OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
            var r = spare ?? w.MakeItem?.Invoke(key);
            if (r != null) f.Equip(r);
        }
        else if (m.Kind is Sport.Soccer or Sport.Basketball) f.DropWeapon(Vector2.Zero);
    }

    void LeaveMatch()
    {
        var m = Match;
        Match = null;
        if (m == null) return;
        m.Players.Remove(f);
        if (f.Carrying == m.Ball) f.DropCarried(Vector2.Zero);
        if (m.Kind is Sport.Tennis or Sport.Badminton && f.Weapon?.Def.Key is "racket" or "badmintonracket") f.DropWeapon(Vector2.Zero);
    }

    public void OnScored(Match m, World w)
    {
        f.Emote(rng.NextDouble() < 0.5 ? "!!" : "♪", 1.2f);
        Cheered(0.15f);
        foreach (var p in m.Players) if (p != f && m.Team.GetValueOrDefault(p) == m.Team.GetValueOrDefault(f)) p.Brain.AddAffinity(f, 0.02f);
    }

    float _celebrateCd;

    public void OnMatchMoment(Match m, bool ourPoint, World w)
    {
        if (ourPoint)
        {
            Cheered(0.1f);
            if (m.Scorer == f && _t0 > _celebrateCd && f.Grounded)
            {
                // A proper celebration for whoever scored (not every point: rallies would be non-stop hopping).
                _celebrateCd = _t0 + 8;
                f.Emote(rng.NextDouble() < 0.5 ? "yes!" : "!!", 1.2f);
                if (P.Playfulness > 0.6f && Stamina > 0.4f) f.RequestFlip(50 * S);
                else f.SetAction(Act.Cheer);
            }
            else if (rng.NextDouble() < 0.4) f.Emote(rng.NextDouble() < 0.5 ? "!" : "♪", 0.9f);
        }
        else if (rng.NextDouble() < 0.5)
        {
            Annoyance = M.Clamp01(Annoyance + (1 - P.Sociability) * 0.04f);
            f.Emote(P.Aggression > 0.6f ? "#@!" : "…", 0.9f);
        }
    }

    public void OnMatchOver(Match m, bool won, bool tie, World w)
    {
        Match = null;
        DiaryMatch(m, won, tie);
        if (!tie && World.Current is { } wm && rng.NextDouble() < 0.6) SayInCharacter(won ? "win" : "lose", null, wm);
        if (won) RememberPlace(w, 0.5f, "winning a game");
        if (f.Weapon?.Def.Key is "racket" or "badmintonracket") f.DropWeapon(Vector2.Zero);
        if (f.Carrying == m.Ball) f.DropCarried(Vector2.Zero);
        foreach (var o in m.Players)
            if (o != f) AddAffinity(o, m.Team.GetValueOrDefault(o) == m.Team.GetValueOrDefault(f) ? 0.08f : (P.Sociability > 0.5f ? 0.03f : -0.02f));
        if (tie) { f.Emote("♪", 1); Go(G.Idle, 1.5f); return; }
        if (won) { Cheered(0.4f); f.Emote("!!", 1.3f); Go(G.Victory, 1.6f); }
        else if (P.Sociability > 0.55f && P.Aggression < 0.5f) { f.Emote("♪", 1); Go(G.Wave, 1.2f); }   // good game!
        else { Saddened(0.15f); Annoyance = M.Clamp01(Annoyance + 0.1f); f.Emote(P.Aggression > 0.6f ? "#@!" : "…", 1.2f); Go(G.Annoyed, 1.5f); }
    }

    // ---------------- playing ----------------

    void DoSport(World w)
    {
        var m = Match;
        if (m == null || m.Over || !w.Matches.Contains(m)) { Match = null; Go(G.Idle, 1); return; }
        if (Stamina < 0.12f) { f.Emote("…", 1); LeaveMatch(); Go(G.SitFloor, 6); return; }
        // Had enough: the game's not their thing, they're worn out, or something (someone) else is calling.
        if (_t > 45 && (int)(_t * 2) != (int)((_t - World.Dt) * 2))
        {
            float stay = 0.6f + f.Tastes.Of(Thing.PlayingBall) * 0.5f + P.Energy * 0.3f - (1 - Stamina) * 0.6f - Boredom * 0.3f
                         - (World.Current.Romance && Crush(w) is { } cr && cr.Brain.Match != m ? 0.25f : 0);
            if (rng.NextDouble() < (0.5f - stay) * 0.04f) { f.Emote(P.Sociability > 0.5f ? "good game!" : "I'm done", 1.2f); LeaveMatch(); Go(G.Idle, 1.5f); return; }
        }
        Stamina = MathF.Max(0, Stamina - World.Dt * 0.003f);
        _actCd -= World.Dt;
        var b = m.Ball;
        f.LookAt = b.Pos;
        // Not on the court (it's up on a window, say): go there first, still in the game.
        var court = m.Gear[0];
        if (f.Grounded && court.OnGround && w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { } here && here.Hwnd != court.GroundHwnd &&
            MathF.Abs(f.Base.Y - court.Pos.Y) > 6 * S && f.Carrying != b)
        {
            var plat = w.Env.Platforms.FirstOrDefault(p => p.Hwnd == court.GroundHwnd && court.Pos.X >= p.X1 - 2 && court.Pos.X <= p.X2 + 2);
            if (plat != null)
            {
                var a = Anchor.On(w.Env, plat, M.ClampIn(HomeX(m), plat.X1 + 10 * S, plat.X2 - 10 * S));
                _toMatch = true;
                Navigate(() => a.Resolve(w.Env), 20 * S, true, () => Go(G.Sport, 600), WalkPurpose.Other);
                return;
            }
        }
        if (m.Pause > 0)
        {
            // Between points: drift back toward our position.
            f.SetAction(Act.Stand);
            _run = false;
            MoveToward(HomeX(m), 20 * S);
            return;
        }
        switch (m.Kind)
        {
            case Sport.Soccer: PlaySoccer(m, b, w); break;
            case Sport.Basketball: PlayBasketball(m, b, w); break;
            default: PlayRacket(m, b, w); break;
        }
    }

    float HomeX(Match m)
    {
        int team = m.Team.GetValueOrDefault(f);
        switch (m.Kind)
        {
            case Sport.Soccer:
                if (m.OneGoal) return team == 1 ? m.Mouth(m.Gear[0], 0).X + m.FieldDir(m.Gear[0]) * 14 * S : m.KickOff().X;
                return Keeper(m) ? m.Mouth(m.Gear[team], 0).X + m.FieldDir(m.Gear[team]) * 14 * S : m.KickOff().X - m.FieldDir(m.Gear[team]) * -60 * S;
            case Sport.Basketball: return m.RimCentre.X + m.FieldDir(m.Gear[0]) * (60 + (f.Id % 4) * 35) * S;
            default:
            {
                var mates = m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => p.Id).ToList();
                int idx = Math.Max(0, mates.IndexOf(f));
                return m.NetX + m.Side(team) * (mates.Count > 1 ? (idx == 0 ? 55 : 150) : 110) * S;
            }
        }
    }

    bool Keeper(Match m)
    {
        int team = m.Team.GetValueOrDefault(f);
        if (m.OneGoal) return team == 1;
        var mates = m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => p.Id).ToList();
        return mates.Count >= 2 && mates[0] == f;
    }

    /// <summary>Kick animation with the ball launched at the contact moment.</summary>
    bool KickBall(Prop b, Vector2 v)
    {
        if (_kickAt < 0)
        {
            if (_actCd > 0) return false;
            f.SetAction(Act.Kick);
            _kickAt = Figure.KickTime * Figure.KickContact;
            return false;
        }
        if (f.ActionT < _kickAt) return false;
        _kickAt = -1;
        _actCd = 0.45f;
        if (Vector2.Distance(b.Pos, f.Jt[J.FootN]) < b.Radius + 14 * S) { b.Kick(v, f); return true; }
        return false;
    }

    void PlaySoccer(Match m, Prop b, World w)
    {
        int team = m.Team.GetValueOrDefault(f);
        var target = m.OneGoal ? m.Gear[0] : m.Gear[1 - team];
        float toGoal = MathF.Sign(m.Mouth(target, 0).X - b.Pos.X);
        if (_kickAt >= 0) { f.DesiredVX = 0; float sh = rng.Range(6, 24); KickBall(b, ShotVelocity(m, b, target, sh)); return; }

        if (Keeper(m))
        {
            var own = m.OneGoal ? m.Gear[0] : m.Gear[team];
            float home = m.Mouth(own, 0).X + m.FieldDir(own) * 14 * S;
            float near = MathF.Abs(b.Pos.X - home);
            _run = true;
            // Shuffle out a little toward the ball, but stay near the line.
            MoveToward(M.ClampIn(b.Pos.X, home - 30 * S, home + 30 * S), 4 * S);
            FaceTo(b.Pos.X);
            // High ball coming at the goal: jump for it.
            bool incoming = MathF.Sign(b.Vel.X) == -m.FieldDir(own) && MathF.Abs(b.Vel.X) > 200 * S;
            if (incoming && f.Grounded && near < 220 * S && b.Pos.Y < f.Base.Y - f.Height * 0.8f && _actCd <= 0) { f.RequestJump(new Vector2(0, -620 * S), 0.03f); _actCd = 1; }
            // Ball dribbling in front: boot it away.
            if (near < 30 * S && b.Vel.LengthSquared() < 300 * 300 * S * S && b.Pos.Y > f.Base.Y - f.Leg)
                KickBall(b, new Vector2(m.FieldDir(own) * 900 * S, -500 * S));
            return;
        }

        // Striker: get behind the ball (on the far side from the goal), then dribble or shoot.
        float behind = b.Pos.X - toGoal * (b.Radius + 9 * S);
        bool onRightSide = MathF.Sign(b.Pos.X - f.Base.X) == toGoal || MathF.Abs(b.Pos.X - f.Base.X) < 4 * S;
        _run = MathF.Abs(behind - f.Base.X) > 60 * S;
        if (!onRightSide)
        {
            // Go around the ball (a little loop) so we don't kick it backwards.
            MoveToward(b.Pos.X - toGoal * 40 * S, 6 * S);
            return;
        }
        if (!MoveToward(behind, 7 * S)) return;
        FaceTo(b.Pos.X + toGoal);
        if (b.Pos.Y < f.Base.Y - f.Leg * 1.2f) return;   // wait for it to come down
        float dist = MathF.Abs(m.Mouth(target, 0).X - b.Pos.X);
        if (dist < 300 * S) KickBall(b, ShotVelocity(m, b, target, rng.Range(6, 24)));
        else KickBall(b, new Vector2(toGoal * (380 + P.Energy * 160) * S, -rng.Range(40, 140) * S));
    }

    Vector2 ShotVelocity(Match m, Prop b, Item goal, float h)
    {
        float miss = 1.45f - (Sk(SkillKind.Kicking) * 0.7f + Sk(SkillKind.Ball) * 0.3f);
        Vector2 aim = goal.Local(-6, h) + new Vector2(rng.Range(-6, 6) * miss * S, rng.Range(-6, 6) * miss * S);
        Practice(SkillKind.Kicking, 0.008f);
        Practice(SkillKind.Ball, 0.003f);
        return SolveLob(b.Pos, aim, b.Grav, 10 * S, 500 * S, 1600 * S, out var v) ? v : new Vector2(MathF.Sign(aim.X - b.Pos.X) * 900 * S, -350 * S);
    }

    void PlayBasketball(Match m, Prop b, World w)
    {
        var rim = m.RimCentre;
        float field = m.FieldDir(m.Gear[0]);
        if (f.Carrying == b)
        {
            if (_dunking)
            {
                // Up we go: let go right over the rim.
                if (!f.Grounded && MathF.Abs(f.HoldPoint.X - rim.X) < 14 * S && f.HoldPoint.Y < rim.Y + 6 * S)
                {
                    f.Carrying = null;
                    b.Holder = null;
                    b.Pos = rim + new Vector2(0, -6 * S);
                    b.Release(new Vector2(0, 250 * S));
                    b.LastTouch = f;
                    f.Emote("!!", 1);
                    _dunking = false;
                }
                else if (f.Grounded && !f.JumpPending)
                {
                    float under = rim.X + field * 6 * S;
                    if (MoveToward(under, 5 * S))
                    {
                        float rise = f.Base.Y - rim.Y - f.Height * 0.55f;
                        if (rise > f.Height * 1.4f) { _dunking = false; return; }   // too high to dunk
                        f.RequestJump(new Vector2(-field * 40 * S, -MathF.Sqrt(2 * f.Gravity * MathF.Max(rise, 20 * S))), 0.08f);
                    }
                    _run = true;
                }
                return;
            }
            _shotSpot ??= new Vector2(rim.X + field * rng.Range(55, 240) * S, f.Base.Y);
            if (!MoveToward(_shotSpot.Value.X, 6 * S)) { _run = false; return; }
            FaceTo(rim.X);
            if (f.Action != Act.Throw) { f.SetAction(Act.Throw); return; }
            if (f.ActionT < Figure.ThrowTime * Figure.ThrowRelease) return;
            // Shoot: a lob at the rim; worse aim from far away.
            float dist = MathF.Abs(rim.X - f.Base.X);
            float skill = 0.25f + Sk(SkillKind.Shooting) * 0.55f + f.Tastes.Of(Thing.Basketballs) * 0.15f;
            Practice(SkillKind.Shooting, 0.01f);
            Practice(SkillKind.Ball, 0.003f);
            Vector2 aim = rim + new Vector2(rng.Range(-1, 1) * (1 - skill) * (6 + dist / (25 * S)) * S, -3 * S);
            if (!SolveLob(f.HoldPoint, aim, b.Grav, 30 * S + dist * 0.25f, 900 * S, 1800 * S, out var v)) v = new Vector2(MathF.Sign(rim.X - f.Base.X) * 500 * S, -900 * S);
            f.Carrying = null;
            b.Holder = null;
            b.Release(v);
            b.LastTouch = f;
            _shotSpot = null;
            f.SetAction(Act.Stand);
            return;
        }
        // Not holding it: if it's loose and low, go get it (rebound!); otherwise hang around the key.
        bool loose = b.Holder == null && !b.Pinned;
        bool someoneElse = b.Holder != null && b.Holder != f;
        if (loose && (b.OnGround || b.Pos.Y > f.Base.Y - f.Height * 1.1f) && ClosestTo(m, b))
        {
            _run = true;
            if (MoveToward(b.Pos.X, b.Radius + 5 * S) && b.Pos.Y > f.Base.Y - f.Height * 0.9f)
            {
                b.Holder = f;
                f.Carrying = b;
                _shotSpot = null;
                _dunking = P.Energy > 0.6f && P.Bravery > 0.45f && rng.NextDouble() < 0.3;
            }
            return;
        }
        _run = false;
        MoveToward(someoneElse || !loose ? HomeX(m) : M.ClampIn(b.Pos.X, rim.X - 160 * S, rim.X + 160 * S), 10 * S);
        FaceTo(rim.X);
    }

    bool ClosestTo(Match m, Prop b) =>
        m.Players.Where(p => p.Brain.Match == m).OrderBy(p => MathF.Abs(p.Base.X - b.Pos.X)).FirstOrDefault() == f;

    void PlayRacket(Match m, Prop b, World w)
    {
        int team = m.Team.GetValueOrDefault(f);
        int side = m.Side(team);
        bool badminton = m.Kind == Sport.Badminton;
        var net = m.Net;
        float netTop = net.Local(0, net.Def.H).Y;
        FaceTo(m.NetX);
        f.KeepFacing = true;
        // Serving: our team serves and we're the server (closest to home).
        var mates = m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => p.Id).ToList();
        bool server = m.ServeTeam == team && mates.LastOrDefault() == f;
        bool ballOurSide = MathF.Sign(b.Pos.X - m.NetX) == side;
        bool served = b.SinceTouch < 30 && b.LastTouch != null;
        if (server && !served && b.Holder == null && b.Vel.LengthSquared() < 1 && !_serving)
        {
            // Hold the ball up and serve.
            _serving = true;
            _serveT = 0.6f;
        }
        if (_serving)
        {
            f.DesiredVX = 0;
            _serveT -= World.Dt;
            b.Pinned = true;
            b.PinTarget = f.Jt[J.Neck] + new Vector2(side * -8 * S, -14 * S);
            if (_serveT <= 0)
            {
                b.Pinned = false;
                Hit(m, b, side, netTop, badminton, true);
                _serving = false;
            }
            return;
        }
        // Where will it come down on our side?
        float landX = PredictLanding(b, m.CourtFloor, badminton);
        bool coming = ballOurSide || MathF.Sign(b.Vel.X) == side;
        float myX = coming && b.LastTouch != f && Responsible(m, landX, team) ? landX + side * 6 * S : HomeX(m);
        myX = side < 0 ? MathF.Min(myX, m.NetX - 14 * S) : MathF.Max(myX, m.NetX + 14 * S);
        _run = MathF.Abs(myX - f.Base.X) > 40 * S;
        MoveToward(myX, 4 * S);
        // Swing when it's in reach of the racket.
        Vector2 head = f.Jt[J.Neck] + new Vector2(-side * f.Arm * 0.6f, -f.Arm * 0.5f);
        float reach = f.Arm + (f.Weapon?.Def.Reach ?? 10) * S * 0.7f;
        if (coming && b.LastTouch != f && _actCd <= 0 && Vector2.Distance(b.Pos, head) < reach && b.Pos.Y < f.Base.Y - 8 * S)
            Hit(m, b, side, netTop, badminton, false);
        else if (coming && f.Grounded && _actCd <= 0 && MathF.Abs(b.Pos.X - f.Base.X) < 30 * S && b.Pos.Y < f.Base.Y - f.Height * 1.5f && b.Vel.Y > 0 && b.Pos.Y > f.Base.Y - f.Height * 2.4f)
            f.RequestJump(new Vector2(0, -520 * S), 0.03f);   // jump smash
    }

    bool Responsible(Match m, float landX, int team) =>
        m.Players.Where(p => m.Team.GetValueOrDefault(p) == team).OrderBy(p => MathF.Abs(p.Base.X - landX)).FirstOrDefault() == f;

    void Hit(Match m, Prop b, int side, float netTop, bool badminton, bool serve)
    {
        f.SetAction(Act.Swat);
        _actCd = 0.35f;
        // Aim somewhere on their side, clearing the net comfortably.
        // Better players place it deeper and more surely; beginners all over the place.
        float rs = Sk(SkillKind.Racket);
        float depth = rng.Range(serve ? 90 : 50, 230) * S * (0.75f + rs * 0.35f) + rng.Range(-60, 60) * (1 - rs) * S;
        Vector2 target = new(m.NetX - side * MathF.Max(25 * S, depth), m.CourtFloor);
        Practice(SkillKind.Racket, 0.008f);
        float clear = MathF.Max(b.Pos.Y - netTop, 0) + (badminton ? 45 : 25) * S + rng.Range(0, 30) * S;
        if (!SolveLob(b.Pos, target, b.Grav, clear, 900 * S, 1500 * S, out var v)) v = new Vector2(-side * 500 * S, -600 * S);
        if (badminton) v *= 1.45f;   // the shuttle's drag eats a lot of it
        b.Pinned = false;
        b.Kick(v, f);
        b.LastTouch = f;
        if (rng.NextDouble() < 0.05) f.Emote(P.Playfulness > 0.5f ? "!" : "hm", 0.6f);
    }

    static float PredictLanding(Prop b, float floor, bool drag)
    {
        Vector2 p = b.Pos, v = b.Vel;
        float g = b.Grav, k = drag ? 2.4f : 0.15f;
        for (int i = 0; i < 240; i++)
        {
            v.Y += g / 60f;
            v *= 1 - k / 60f;
            p += v / 60f;
            if (p.Y + b.Radius >= floor && v.Y > 0) break;
        }
        return p.X;
    }
}
