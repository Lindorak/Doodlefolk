using System.Numerics;

namespace StickFight;

/// <summary>Decides what a figure does. Needs (stamina, boredom, loneliness, annoyance) and personality
/// weight a utility-style choice of goals; reactions to the cursor, other figures, balls and knocks
/// interrupt them. Navigation, social and ball behaviour live in the other Brain.*.cs files.</summary>
sealed partial class Brain
{
    enum G
    {
        Busy, Idle, Walk, SitEdge, SitFloor, Sleep, Watch, Swat, Annoyed, Wave, Cheer, Startled, Trick,
        Chat, HighFive, Follow, SitWith, Kick, Dribble, Juggle, Carry, Throw, Catch,
        Fight, Victory, CursorFight, Revive,
    }

    readonly Figure f;
    readonly Random rng;
    G _g = G.Busy;
    float _t, _dur;

    // ---- needs & mood (0..1) ----
    public float Stamina = 1, Boredom = 0.3f, Loneliness = 0.3f, Annoyance, CursorTrust = 0.6f;
    /// <summary>Personal experience with each other figure (by id), added on top of the colour rule's baseline.</summary>
    public readonly Dictionary<int, float> Affinity = new();

    // ---- perception ----
    float _hoverT, _watchCd = 3, _swatCd, _startleCd, _waveCd = 5, _awayT = 100, _stillT, _nextLook, _scanT;
    Vector2? _idleLook;
    float _idleLookUntil;
    readonly HashSet<int> _sawFall = new();
    Figure? _glareAt;

    public Brain(Figure f, Random rng) { this.f = f; this.rng = rng; }

    float S => f.S;
    Personality P => f.Traits;
    public string State => _partner != null ? $"{_g} ({_partner.Name})" : _foe != null && _g == G.Fight ? $"{(_spar ? "Spar" : "Fight")} ({_foe.Name})" : _fleeing ? "Flee" : _g.ToString();
    public bool Asleep => _g == G.Sleep;

    void Go(G g, float dur)
    {
        if (_g is G.Chat or G.HighFive or G.SitWith or G.Follow && g != _g) EndSocial();
        if (_g is G.Carry or G.Throw && g is not (G.Carry or G.Throw) && f.Carrying != null) f.DropCarried(Vector2.Zero);
        if (_ball != null && _ball.Juggler == f && g != G.Juggle) _ball.Juggler = null;
        if (_g is G.Fight or G.CursorFight && g != _g) EndFight();
        if (g != G.Walk) _fleeing = false;
        if (g is not (G.Fight or G.Walk)) _foe = null;
        _g = g;
        _t = 0;
        _dur = dur;
        _nextLook = 0.8f;
        _idleLook = null;
        f.SetAction(Act.Stand);
        f.DesiredVX = 0;
        if (f.Grounded) f.KeepFacing = false;
    }

    /// <summary>Carry mood and relationships over when a figure is rebuilt (e.g. resized).</summary>
    public void CopyFrom(Brain o)
    {
        Stamina = o.Stamina; Boredom = o.Boredom; Loneliness = o.Loneliness; Annoyance = o.Annoyance; CursorTrust = o.CursorTrust;
        foreach (var (k, v) in o.Affinity) Affinity[k] = v;
    }

    float Baseline(Figure o) => FightSettings.Baseline(RelationTo(o)) + (P.Sociability - 0.5f) * 0.2f;
    public float AffinityWith(Figure o) => Math.Clamp(Baseline(o) + (Affinity.TryGetValue(o.Id, out var a) ? a : 0), -1, 1);
    public float AffinityDelta(Figure o) => Affinity.TryGetValue(o.Id, out var a) ? a : 0;
    public void AddAffinity(Figure o, float d) =>
        Affinity[o.Id] = Math.Clamp(AffinityDelta(o) + d, -1 - Baseline(o), 1 - Baseline(o));

    // ================= hooks from the body =================

    public void OnSpawned() => Go(G.Idle, rng.Range(0.6f, 1.4f));
    public void OnRagdoll() { EndSocial(); _g = G.Busy; }
    public void OnGrabbed() { EndSocial(); _g = G.Busy; CursorTrust = MathF.Max(0, CursorTrust - 0.08f); }
    public void OnLanded(float impact) { if (impact > 1200 * S) Stamina = MathF.Max(0, Stamina - 0.02f); }
    public void OnUnexpectedFall() { if (_g != G.Busy) Go(G.Idle, 1.2f); }

    public void OnClimbed()
    {
        Stamina = MathF.Max(0, Stamina - 0.06f);
        if (_g == G.Walk && _nav == Nav.Climbing) { _nav = Nav.Direct; _hops++; return; }
        if (rng.NextDouble() < P.Playfulness * 0.6f) Go(G.Cheer, 0.9f);
        else Go(G.Idle, rng.Range(0.8f, 2f));
    }

    public void OnRecovered(float throwSpeed, World w)
    {
        if (throwSpeed < 1 && AfterKnockdown(w)) return;
        float k = M.Clamp01(throwSpeed / (2500 * S));
        Annoyance = M.Clamp01(Annoyance + 0.2f + 0.4f * k);
        CursorTrust = MathF.Max(0, CursorTrust - 0.05f - 0.25f * k);
        if (throwSpeed > 900 * S || P.Aggression > 0.6f) { f.Emote(P.Aggression > 0.5f ? "#@!" : "!", 1.4f); Go(G.Annoyed, 1.4f); }
        else Go(G.Idle, 1.2f);
    }

    public void OnPoked(World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded) return;
        CursorTrust = MathF.Max(0, CursorTrust - 0.05f);
        Annoyance = M.Clamp01(Annoyance + 0.12f);
        if (_g == G.Sleep) { f.Emote("!", 1); Go(G.Annoyed, 1.2f); return; }
        if (_g == G.CursorFight) return;
        if (WantsCursorFight() && rng.NextDouble() < 0.6) BeginCursorFight();
        else if (P.Aggression > 0.5f) Go(G.Swat, 0.34f);
        else Startle(w.Cursor);
    }

    public void OnHit(Figure? from, bool knockedDown, World w)
    {
        Annoyance = M.Clamp01(Annoyance + (knockedDown ? 0.35f : 0.15f));
        if (from == null) CursorTrust = MathF.Max(0, CursorTrust - (knockedDown ? 0.2f : 0.08f));
        else if (from != f)
        {
            from.Brain.NoticeIHit(f);
            AddAffinity(from, knockedDown ? -0.25f : -0.08f);
        }
        if (knockedDown) return;
        f.Emote(P.Aggression > 0.55f ? "#@!" : "!", 1.1f);
        if (_g is G.Sleep or G.SitEdge or G.SitFloor or G.Idle or G.Walk or G.Watch)
        {
            if (from != null) _glareAt = from;
            Go(G.Annoyed, 1.2f);
        }
    }

    void NoticeIHit(Figure victim)
    {
        // The kicker notices: feisty ones are amused, the rest feel a bit bad.
        if (P.Aggression > 0.6f) f.Emote("ha", 1);
        else f.Emote("!", 0.9f);
    }

    // ================= main loop =================

    public void Update(float dt, World w)
    {
        _t += dt;
        _watchCd -= dt; _swatCd -= dt; _startleCd -= dt; _waveCd -= dt; _scanT -= dt;
        UpdateNeeds(dt, w);

        Vector2 cur = w.Cursor;
        float dist = Vector2.Distance(cur, f.Jt[J.Head]);
        float cspeed = w.CursorVel.Length();
        bool near = dist < 280 * S;
        _hoverT = w.Hover == f ? _hoverT + dt : 0;
        if (near && cspeed < 300 * S && _g != G.Sleep) CursorTrust = MathF.Min(1, CursorTrust + dt * 0.01f);

        f.LookAt = null;
        f.HeadShake = false;
        if (_g != G.Sleep)
        {
            if (near) f.LookAt = cur;
            else if (InterestingBall(w) is { } b) f.LookAt = b.Pos;
            else if (_idleLook.HasValue && _t < _idleLookUntil) f.LookAt = _idleLook;
        }

        if (_scanT <= 0) { _scanT = 0.25f; ScanOthers(w); }

        bool calm = f.Grounded && !f.JumpPending && _g is G.Idle or G.Walk or G.SitEdge or G.SitFloor or G.Watch or G.Sleep;
        bool errand = _g == G.Walk && _purpose != WalkPurpose.Wander;
        if (calm && ReactToCursor(w, cur, dist, cspeed, near, errand)) return;
        _awayT = near ? 0 : _awayT + dt;
        if (calm && _g != G.Sleep && IncomingPass(w)) return;

        switch (_g)
        {
            case G.Busy: break;
            case G.Idle:
                DoIdle();
                if (_t > _dur) Choose(w);
                break;
            case G.Walk: DoWalk(w); break;
            case G.SitEdge:
            case G.SitFloor:
                if (!f.Grounded) { Go(G.Idle, 0.5f); break; }
                if (_g == G.SitEdge) f.Facing = _sitDir;
                f.SetAction(_g == G.SitEdge ? Act.SitEdge : Act.SitFloor);
                if (_t > _dur || (Stamina > 0.98f && _t > 3 && Boredom > 0.6f)) Go(G.Idle, rng.Range(0.6f, 1.5f));
                break;
            case G.Sleep: DoSleep(); break;
            case G.Watch:
            {
                f.LookAt = cur;
                float dx = cur.X - f.Base.X;
                if (MathF.Abs(dx) > 12 * S) f.Facing = MathF.Sign(dx);
                bool approach = CursorTrust > 0.4f && P.Curiosity > 0.55f && MathF.Abs(dx) > 150 * S;
                f.DesiredVX = approach ? MathF.Sign(dx) * f.WalkSpeed : 0;
                _stillT = cspeed < 40 * S ? _stillT + dt : 0;
                if (_stillT > 2.5f || dist > 600 * S) { _watchCd = rng.Range(5, 12); Go(G.Idle, 1); }
                break;
            }
            case G.Swat:
                f.LookAt = cur;
                FaceTo(cur.X);
                f.SetAction(Act.Swat);
                if (_t > _dur)
                {
                    if (w.Hover == f && P.Aggression > 0.6f) Go(G.Swat, 0.34f);
                    else Go(G.Annoyed, 0.9f);
                }
                break;
            case G.Annoyed:
                if (_glareAt != null && w.Figures.Contains(_glareAt)) { f.LookAt = _glareAt.Jt[J.Head]; FaceTo(_glareAt.Base.X); }
                else if (near) FaceTo(cur.X);
                f.SetAction(Act.HandsHips);
                f.HeadShake = _t < 0.8f;
                if (_t > _dur) { _glareAt = null; Go(G.Idle, 1); }
                break;
            case G.Wave:
                f.LookAt = cur;
                FaceTo(cur.X);
                f.SetAction(Act.Wave);
                if (_t > _dur) Go(G.Idle, 1);
                break;
            case G.Cheer:
                f.SetAction(Act.Cheer);
                if (_t > _dur) Go(G.Idle, 1);
                break;
            case G.Startled:
                f.LookAt = cur;
                if (f.Grounded && !f.JumpPending && _t > 0.15f)
                {
                    _stillT = 0;
                    if (P.Bravery > 0.55f) Go(G.Annoyed, 1.0f);
                    else Go(G.Watch, 30);
                }
                break;
            case G.Trick:
                if (_t > 0.3f && f.Grounded && !f.JumpPending)
                {
                    if (rng.NextDouble() < 0.6) { f.Emote("♪", 1); Go(G.Cheer, 0.8f); }
                    else Go(G.Idle, 1);
                }
                break;
            case G.Chat: DoChat(w); break;
            case G.HighFive: DoHighFive(w); break;
            case G.Follow: DoFollow(w); break;
            case G.SitWith: DoSitWith(w); break;
            case G.Kick: DoKick(w); break;
            case G.Dribble: DoDribble(w); break;
            case G.Juggle: DoJuggle(w); break;
            case G.Carry: DoCarry(w); break;
            case G.Throw: DoThrow(w); break;
            case G.Catch: DoCatch(w); break;
            case G.Fight: DoFight(w); break;
            case G.CursorFight: DoCursorFight(w); break;
            case G.Revive: DoRevive(w); break;
            case G.Victory:
                f.SetAction(Act.Cheer);
                if (_t > _dur) Go(G.Idle, rng.Range(1, 2.5f));
                break;
        }
    }

    void UpdateNeeds(float dt, World w)
    {
        float speed = MathF.Abs(f.Vel.X);
        float cost = _g switch
        {
            G.Sleep => -0.09f,
            G.SitEdge or G.SitFloor or G.SitWith => -0.035f,
            G.Idle or G.Watch or G.Chat => -0.012f,
            G.Juggle or G.Dribble or G.Kick or G.Catch or G.Throw => 0.022f,
            G.Fight or G.CursorFight => 0.035f,
            _ => 0,
        };
        if (speed > f.WalkSpeed * 1.2f) cost += 0.025f;
        else if (speed > 4 * S) cost += 0.006f;
        if (f.Climbing) cost += 0.03f;
        if (cost > 0) cost *= 1.3f - 0.6f * P.Energy;   // energetic figures tire more slowly
        Stamina = M.Clamp01(Stamina - cost * dt);

        bool stimulating = _g is G.Walk or G.Chat or G.HighFive or G.Juggle or G.Dribble or G.Kick or G.Catch or G.Throw or G.Carry or G.Trick or G.Watch or G.Fight or G.CursorFight;
        Boredom = M.Clamp01(Boredom + dt * (stimulating ? -0.06f : 0.012f * (0.5f + P.Curiosity)));
        bool social = _g is G.Chat or G.HighFive or G.Follow or G.SitWith || _partner != null;
        Loneliness = M.Clamp01(Loneliness + dt * (social ? -0.08f : 0.01f * P.Sociability * (w.Figures.Count > 1 ? 1 : 0.3f)));
        Annoyance = M.Clamp01(Annoyance - dt * 0.04f);
    }

    /// <summary>Notice other figures getting thrown around.</summary>
    void ScanOthers(World w)
    {
        WatchFights(w);
        foreach (var o in w.Figures)
        {
            if (o == f) continue;
            if (o.Mode != Mode.Ragdoll) { _sawFall.Remove(o.Id); continue; }
            if (_sawFall.Contains(o.Id) || _g is G.Busy or G.Sleep) continue;
            if (Vector2.Distance(o.Jt[J.Pelvis], f.Jt[J.Pelvis]) > 700 * S || o.JVel[J.Pelvis].Length() < 500 * S) continue;
            _sawFall.Add(o.Id);
            if (P.Aggression > 0.55f && AffinityWith(o) < 0.4f) f.Emote("ha", 1.2f);
            else f.Emote("!", 1.2f);
            if (f.Grounded && _g is G.Idle or G.Watch)
            {
                FaceTo(o.Base.X);
                _idleLook = o.Jt[J.Head];
                _idleLookUntil = _t + 1.5f;
            }
        }
    }

    /// <summary>Cursor reactions. <paramref name="errand"/>: walking somewhere on purpose (ball, friend,
    /// exploring), so only urgent reactions (startles, being hovered) interrupt.</summary>
    bool ReactToCursor(World w, Vector2 cur, float dist, float cspeed, bool near, bool errand)
    {
        if (_g == G.Sleep)
        {
            if (_startleCd <= 0 && dist < 150 * S && cspeed > 2200 * S)
            {
                _startleCd = 3;
                if (rng.NextDouble() > P.Bravery) { f.Emote("!", 1); Go(G.Idle, 1); }
            }
            return false;
        }
        if (_startleCd <= 0 && dist < 170 * S && cspeed > 1600 * S)
        {
            _startleCd = 2.5f;
            if (rng.NextDouble() > P.Bravery * 0.8f) { Startle(cur); return true; }
        }
        if (_hoverT > 1.0f && _swatCd <= 0)
        {
            _swatCd = 2.5f;
            if (WantsCursorFight()) BeginCursorFight();
            else if (P.Aggression + P.Playfulness * 0.5f > 0.55f) Go(G.Swat, 0.34f);
            else RunFromCursor(w, cur);
            return true;
        }
        if (errand) return false;
        // Figures that have learned to distrust the cursor keep their distance (or square up to it).
        if (near && CursorTrust < 0.25f && _awayT > 0.5f && _swatCd <= 0 && _g is G.Idle or G.Walk or G.SitFloor)
        {
            _swatCd = 3;
            if (P.Bravery < 0.5f) { f.Emote("!", 1); RunFromCursor(w, cur); }
            else { f.Emote("#@!", 1.2f); Go(G.Annoyed, 1.5f); }
            return true;
        }
        if (near && _awayT > 10 && _waveCd <= 0 && P.Sociability > 0.4f && CursorTrust > 0.35f)
        {
            _waveCd = 25;
            _awayT = 0;
            Go(G.Wave, 1.5f);
            return true;
        }
        if (near && cspeed > 150 * S && _watchCd <= 0 && (_g == G.Idle || (_g == G.Walk && _purpose == WalkPurpose.Wander)))
        {
            _watchCd = 4;
            if (rng.NextDouble() < P.Curiosity) { _stillT = 0; Go(G.Watch, 30); return true; }
        }
        return false;
    }

    void FaceTo(float x)
    {
        float dx = x - f.Base.X;
        if (MathF.Abs(dx) > 4 * S) f.Facing = MathF.Sign(dx);
    }

    void DoIdle()
    {
        f.DesiredVX = 0;
        f.SetAction(Act.Stand);
        if (_t < _nextLook) return;
        _nextLook = _t + rng.Range(1.2f, 3.5f);
        if (f.LookAt != null) return;
        if (rng.NextDouble() < 0.4) f.Facing = -f.Facing;
        else
        {
            _idleLook = f.Jt[J.Head] + new Vector2(f.Facing * rng.Range(60, 200) * S, rng.Range(-120, 80) * S);
            _idleLookUntil = _t + rng.Range(0.8f, 1.8f);
        }
    }

    void DoSleep()
    {
        if (!f.Grounded) { Go(G.Idle, 0.5f); return; }
        f.DesiredVX = 0;
        f.SetAction(_t < 1.2f ? Act.SitFloor : Act.Lie);
        if (_t > 1.2f && f.CurrentEmote != "z") f.Emote("z", 3);
        if ((Stamina > 0.97f && _t > 8) || _t > _dur)
        {
            f.Emote("♪", 0.8f);
            Go(G.Cheer, 0.7f);   // a big stretch
        }
    }

    void Startle(Vector2 cur)
    {
        float away = -MathF.Sign(cur.X - f.Base.X);
        if (away == 0) away = -f.Facing;
        Go(G.Startled, 3);
        f.Emote("!", 0.9f);
        f.Facing = (int)-away;
        f.KeepFacing = true;
        f.Flailing = true;
        f.RequestJump(new Vector2(away * 140 * S, -620 * S), 0.03f);
    }

    void RunFromCursor(World w, Vector2 cur)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        float away = -MathF.Sign(cur.X - f.Base.X);
        if (away == 0) away = 1;
        bool scared = CursorTrust < 0.25f;
        float x = Math.Clamp(f.Base.X + away * (scared ? 300 : 90) * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        WalkTo(x, scared, () => Go(G.Idle, 1.5f));
    }

    // ================= choosing what to do =================

    void Choose(World w)
    {
        var env = w.Env;
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null || !f.Grounded) { Go(G.Idle, 0.5f); return; }
        float E = P.Energy, tired = 1 - Stamina;
        var opts = new List<(float weight, Action act)>
        {
            (0.5f + (1 - E) * 0.5f, () => Go(G.Idle, rng.Range(1.5f, 4.5f))),
            (tired * 0.8f + (1 - E) * 0.3f, () => Go(G.SitFloor, rng.Range(4, 12))),
        };
        if (Stamina < 0.3f) opts.Add(((0.3f - Stamina) * 10, () => Go(G.Sleep, rng.Range(15, 40))));
        if (seg.X2 - seg.X1 > 60 * S) opts.Add(((0.4f + E * 0.6f + Boredom * 0.5f) * (0.4f + Stamina), () => Wander(seg)));
        if (Stamina > 0.35f && PickExplore(env, seg, out var explore))
            opts.Add((P.Curiosity * (0.5f + Boredom * 1.2f) * Stamina, explore));
        if (!seg.Solid && PickEdge(env, seg, out float edgeX, out int dir))
            opts.Add((tired + (1 - E) * 0.6f + 0.1f, () => { _sitDir = dir; WalkTo(edgeX - dir * 3.5f * S, false, () => Go(G.SitEdge, rng.Range(5, 15))); }));
        if (Stamina > 0.4f) opts.Add((P.Playfulness * E * 0.25f, () => { Go(G.Trick, 3); f.RequestFlip(70 * S); }));
        if (Stamina > 0.3f) opts.Add((P.Playfulness * 0.15f, () => Go(G.Cheer, 0.9f)));
        if (SocialOption(w) is { } social) opts.Add(social);
        if (FightOption(w) is { } fight) opts.Add(fight);
        if (Stamina > 0.3f && BallOption(w) is { } ball) opts.Add(ball);

        float total = opts.Sum(o => o.weight);
        float roll = rng.Range(0, total);
        foreach (var (weight, act) in opts)
        {
            roll -= weight;
            if (roll <= 0) { act(); return; }
        }
        opts[0].act();
    }

    void Wander(Platform seg)
    {
        float x = Math.Clamp(f.Base.X + rng.Range(-500, 500) * S, seg.X1 + 8 * S, seg.X2 - 8 * S);
        if (MathF.Abs(x - f.Base.X) < 30 * S) x = Math.Clamp(f.Base.X - MathF.Sign(x - f.Base.X + 0.1f) * 200 * S, seg.X1 + 8 * S, seg.X2 - 8 * S);
        bool run = P.Energy > 0.6f && Stamina > 0.5f && rng.NextDouble() < 0.35 && MathF.Abs(x - f.Base.X) > 250 * S;
        WalkTo(x, run, () => Go(G.Idle, rng.Range(1, 3)));
    }

    int _sitDir;

    bool PickEdge(Env env, Platform seg, out float edgeX, out int dir)
    {
        edgeX = 0; dir = 0;
        var (L, R, _) = env.BoundsAt(f.Base.X);
        bool OpenAt(float x, int d) =>
            x > L + 20 * S && x < R - 20 * S && MathF.Abs(x - f.Base.X) < 700 * S &&
            env.SupportAt(x + d * 8 * S, seg.Y, seg.Hwnd) == null;
        bool left = OpenAt(seg.X1, -1), right = OpenAt(seg.X2, 1);
        if (!left && !right) return false;
        bool useRight = left && right ? MathF.Abs(seg.X2 - f.Base.X) < MathF.Abs(seg.X1 - f.Base.X) : right;
        edgeX = useRight ? seg.X2 : seg.X1;
        dir = useRight ? 1 : -1;
        return true;
    }

    /// <summary>Pick another platform to visit (by jumping or climbing) and return the plan.</summary>
    bool PickExplore(Env env, Platform seg, out Action plan)
    {
        plan = () => { };
        var cands = new List<(Platform p, float x, float score)>();
        foreach (var p in env.Platforms)
        {
            if (SameSegment(p, seg) || p.X2 - p.X1 < 28 * S) continue;
            float x = rng.Range(p.X1 + 12 * S, p.X2 - 12 * S);
            if (!CanReach(env, seg, p, x)) continue;
            float d = MathF.Abs(x - f.Base.X) + MathF.Abs(p.Y - seg.Y);
            float score = 1f / (1 + d / (500 * S)) * (p.Y < seg.Y ? 1.3f : 1f) * (p.Solid ? 0.6f : 1f);
            cands.Add((p, x, score));
        }
        if (cands.Count == 0) return false;
        float roll = rng.Range(0, cands.Sum(c => c.score));
        var pick = cands[^1];
        foreach (var c in cands) { roll -= c.score; if (roll <= 0) { pick = c; break; } }
        var target = Anchor.On(env, pick.p, pick.x);
        float startY = f.Base.Y;
        plan = () => Navigate(() => target.Resolve(env), 4 * S, false, () =>
        {
            if (startY - f.Base.Y > 120 * S && P.Playfulness > 0.45f && rng.NextDouble() < 0.6) Go(G.Cheer, 0.9f);
            else Go(G.Idle, rng.Range(0.5f, 2f));
        }, WalkPurpose.Explore);
        return true;
    }
}
