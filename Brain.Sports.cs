using System.Numerics;

namespace Doodlefolk;

/// <summary>Playing sports: getting a game together, and how each player plays. Soccer: strikers dribble and
/// shoot, keepers guard the mouth and jump for high balls. Basketball: take the ball, find a spot, shoot (or dunk),
/// everyone else rebounds. Tennis/badminton: rally over the net with rackets.</summary>
sealed partial class Brain
{
    public Match? Match;
    bool _toMatch;
    int _courtTries;
    float _courtTriedAt = -99;
    float _kickAt = -1, _actCd;
    Vector2 _sportKick;
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
    /// <summary>Spoken for (an event, a game with you, a tournament): not to be pulled into anything else.</summary>
    public bool Busy => Engaged;
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

    /// <summary>A game with exactly these players (the simulation tests), counting into these stats.</summary>
    public void StartMatchWith(Item gear, List<Figure> roster, World w, MatchStats? stats = null) => StartMatch(gear, w, roster, stats);

    void StartMatch(Item gear, World w, List<Figure>? roster = null, MatchStats? stats = null)
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
        // A stray one anywhere will do before bringing out another (balls fly off and pile up otherwise).
        bool made = false;
        if (ball == null || (Vector2.Distance(ball.Pos, gear.Pos) > 1500 * S && w.Props.Count(p => p.Kind == kindBall) < 2))
        {
            if (w.MakeProp == null) return;
            ball = w.MakeProp(kindBall);
            made = true;
        }
        var m = new Match(kind, gearList, ball, stats) { MadeBall = made };
        var players = new List<Figure> { f };
        if (roster != null) players = roster.ToList();
        int want = kind switch { Sport.Soccer => gearList.Count == 2 ? 5 : 3, Sport.Basketball => 3, _ => rng.NextDouble() < 0.3 ? 3 : 1 };
        foreach (var o in w.Figures.Where(o => roster == null && o != f && Vector2.Distance(o.Base, gear.Pos) < 1600 * S).OrderByDescending(o => AffinityWith(o)))
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
                // Can't get onto it (we keep "arriving" somewhere else, on top of something say): give up on this game.
                _courtTries = _t0 - _courtTriedAt < 8 ? _courtTries + 1 : 1;
                _courtTriedAt = _t0;
                if (_courtTries > 3) { _courtTries = 0; f.Emote(V("can't get there…", "HOW DO I GET THERE?!", "forget it.", "um… I can't reach…", "the court eludes me"), 1.2f); LeaveMatch(); Go(G.Idle, 2); return; }
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
            _sportKick = v;
            return false;
        }
        if (f.ActionT < _kickAt) return false;
        _kickAt = -1;
        _actCd = 0.45f;
        if (Vector2.Distance(b.Pos, f.Jt[J.FootN]) < b.Radius + 14 * S) { b.Kick(_sportKick, f); return true; }
        return false;
    }

    Vector2 ShotVelocity(Match m, Prop b, Item goal, float h)
    {
        float miss = 1.45f - (Sk(SkillKind.Kicking) * 0.7f + Sk(SkillKind.Ball) * 0.3f);
        Vector2 aim = goal.Local(-6, h) + new Vector2(rng.Range(-6, 6) * miss * S, rng.Range(-6, 6) * miss * S);
        Practice(SkillKind.Kicking, 0.008f);
        Practice(SkillKind.Ball, 0.003f);
        return SolveLob(b.Pos, aim, b.Grav, 10 * S, 500 * S, 1600 * S, out var v) ? v : new Vector2(MathF.Sign(aim.X - b.Pos.X) * 900 * S, -350 * S);
    }

    bool ClosestTo(Match m, Prop b) =>
        m.Players.Where(p => p.Brain.Match == m).OrderBy(p => MathF.Abs(p.Base.X - b.Pos.X)).FirstOrDefault() == f;

}
