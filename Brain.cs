using System.Numerics;

namespace Doodlefolk;

/// <summary>Decides what a figure does. Needs (stamina, boredom, loneliness, annoyance) and personality
/// weight a utility-style choice of goals; reactions to the cursor, other figures, balls and knocks
/// interrupt them. Navigation, social and ball behaviour live in the other Brain.*.cs files.</summary>
sealed partial class Brain
{
    enum G
    {
        Busy, Idle, Walk, SitEdge, SitFloor, Sleep, Watch, Swat, Annoyed, Wave, Cheer, Startled, Trick,
        Chat, HighFive, Follow, SitWith, Kick, Dribble, Juggle, Carry, Throw, Catch,
        Fight, Victory, CursorFight, Revive, DanceWith, Hunt, UseItem, Sport, Groove, WatchScreen, LookAtScreen, Create, Confess, Snowball, Snowman, PetAnimal, Party, Game, Pose, Tourney, Lasso, Work, Build, Ride, Swim, Fish, Happening, Boost, Visit,
    }

    readonly Figure f;
    readonly Random rng;
    G _g = G.Busy;
    float _t, _dur;
    /// <summary>Total time alive (for cooldowns that outlive a goal).</summary>
    float _t0;

    // ---- needs & mood (0..1) ----
    public float Stamina = 1, Boredom = 0.3f, Loneliness = 0.3f, Annoyance, CursorTrust = 0.6f;
    /// <summary>Wanting something and not getting it (nowhere to sit, no bed to sleep in).</summary>
    public float Frustration;

    /// <summary>Tired, and nowhere comfy to lie down: the floor will do, grudgingly.</summary>
    void NoBed(World w)
    {
        bool any = w.Items.Any(i => i.Free && i.OnGround && (i.Def.Verbs.Contains(Verb.Lie) || i.Def.Verbs.Contains(Verb.Sit)) && i.User == null && Vector2.Distance(i.Pos, f.Base) < 2000 * S);
        if (any) return;
        Frustration = M.Clamp01(Frustration + 0.15f);
        f.Emote(V("no bed… floor it is", "WHERE'S A BED?! fine. floor.", "floor. great.", "nowhere to lie down…", "the floor, again"), 1.6f);
        Write("nobed", V("Nowhere comfy to sleep, so I napped on the floor.", "NO BEDS! I slept on the FLOOR!", "Slept on the floor. Thanks for nothing.", "There was no bed, so I curled up on the floor. My back hurts.", "Slept on the cold floor tonight."), "…", 1800);
    }
    /// <summary>Short-lived feelings: joy from good moments, sadness from losses, fear from scares.</summary>
    public float Joy, Sadness, Fear;

    public void Cheered(float amount) => Joy = M.Clamp01(Joy + amount);
    public void Saddened(float amount) => Sadness = M.Clamp01(Sadness + amount);
    /// <summary>Personal experience with each other figure (by id), added on top of the colour rule's baseline.</summary>
    public readonly Dictionary<int, float> Affinity = new();

    // ---- perception ----
    /// <summary>Habituation to a jumpy cursor (0..0.6): every scare makes the next one less likely; fades over minutes.</summary>
    float _usedToCursor;
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

    /// <summary>Debug: log this figure's goal changes (and who caused them) for a while.</summary>
    public float TraceUntil;

    void Go(G g, float dur, [System.Runtime.CompilerServices.CallerMemberName] string by = "")
    {
        if (_t0 < TraceUntil) World.Log($"trace {f.Name}: {_g} -> {g} ({dur:0.0}s) by {by}");
        _snub = false;
        f.AimAt = null;
        if (g != G.Walk) f.AllowWalkOff = false;
        f.FloorSit = false;
        if (g != G.Groove) _rainDance = false;
        if (_item != null && g != G.UseItem && !(_itemPending && g == G.Walk)) LeaveItem();
        if (g != G.Walk) _itemPending = false;
        if (_g == G.Sport && g != G.Sport && Match != null && !(g == G.Walk && _toMatch)) LeaveMatch();
        if (g == G.Sport) _toMatch = false;
        if (g != G.Throw) _fastball = false;
        if (g is not (G.Carry or G.Walk)) _huntThrow = false;
        if (f.GrappleBusy && !f.Climbing) f.CancelGrapple();
        if (_g is G.Chat or G.HighFive or G.SitWith or G.Follow or G.DanceWith && g != _g) EndSocial();
        if (_g is G.Carry or G.Throw && g is not (G.Carry or G.Throw) && f.Carrying != null) f.DropCarried(Vector2.Zero);
        if (_ball != null && _ball.Juggler == f && g != G.Juggle) _ball.Juggler = null;
        if (_g is G.Fight or G.CursorFight && g != _g) EndFight();
        if (g is not (G.Carry or G.Throw)) _bringToUser = false;
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
        _fondness = o._fondness;
        foreach (var (k, v) in o.Affinity) Affinity[k] = v;
        foreach (var (k, v) in o.Love) Love[k] = v;
        SweetheartId = o.SweetheartId;
        Diary.AddRange(o.Diary);
        foreach (var (k, v) in o.Skills) Skills[k] = v;
        Born = o.Born;
        _places.AddRange(o._places);
        Memories.AddRange(o.Memories);
        ParentIds.AddRange(o.ParentIds);
        ParentNames.AddRange(o.ParentNames);
        Grown = o.Grown; AdultSize = o.AdultSize; LastBaby = o.LastBaby;
        Trophies = o.Trophies; ChampionOn = o.ChampionOn;
        foreach (var (k, v) in o.Record) Record[k] = v;
        Gifts.AddRange(o.Gifts);
        Collection.AddRange(o.Collection);
        _hobby = o._hobby;
        foreach (var (k, v) in o.Learned) Learned[k] = v;
        foreach (var (k, v) in o.Tried) Tried[k] = v;
        Job = o.Job; Coins = o.Coins; AgeBank = o.AgeBank; _toldOld = o._toldOld; HapRole = o.HapRole;
        _datingSince = o._datingSince - o._t0 + _t0;
    }

    float Baseline(Figure o) => FightSettings.Baseline(RelationTo(o)) + (P.Sociability - 0.5f) * 0.2f + TasteBond(o) + FamilyBond(o) + ClubBond(o);
    public float AffinityWith(Figure o) => Math.Clamp(Baseline(o) + (Affinity.TryGetValue(o.Id, out var a) ? a : 0), -1, 1);
    public float AffinityDelta(Figure o) => Affinity.TryGetValue(o.Id, out var a) ? a : 0;
    public void AddAffinity(Figure o, float d)
    {
        float before = AffinityWith(o);
        Affinity[o.Id] = Math.Clamp(AffinityDelta(o) + d, -1 - Baseline(o), 1 - Baseline(o));
        DiaryFeelings(o, before, AffinityWith(o));
    }

    // ================= hooks from the body =================

    public void OnSpawned() => Go(G.Idle, rng.Range(0.6f, 1.4f));
    public void OnRagdoll() { EndSocial(); LeaveOutdoors(); _g = G.Busy; }
    public void OnGrabbed() { EndSocial(); LeaveOutdoors(); _g = G.Busy; CursorTrust = MathF.Max(0, CursorTrust - (f.Tastes.Likes(Thing.BeingPickedUp) ? 0 : 0.08f)); FeelAboutBeingPickedUp(); }
    public void OnLanded(float impact) { if (impact > 1200 * S) Stamina = MathF.Max(0, Stamina - 0.02f); }
    public void OnUnexpectedFall() { if (_g != G.Busy) Go(G.Idle, 1.2f); }

    /// <summary>The grappling hook bit: a little fist pump from the proud ones.</summary>
    public void OnHookCaught()
    {
        if (rng.NextDouble() < 0.35 + P.Playfulness * 0.3) f.Emote(rng.NextDouble() < 0.5 ? "!" : "★", 0.9f);
    }

    /// <summary>The throw missed. Returns whether to have another go.</summary>
    public bool OnHookMissed(int tries)
    {
        Annoyance = M.Clamp01(Annoyance + 0.05f * tries);
        int patience = 2 + (int)MathF.Round(P.Bravery * 1.5f + (1 - Annoyance));
        bool again = tries < patience && Stamina > 0.15f;
        f.Emote(!again ? (P.Aggression > 0.5f ? "#@!" : "…") : tries == 1 ? "…" : P.Aggression > 0.5f ? "#@!" : "!", 1);
        if (!again) _noGrappleUntil = _t0 + rng.Range(25, 60);
        return again;
    }

    float _noGrappleUntil;

    public void OnClimbed()
    {
        Practice(SkillKind.Climbing, 0.015f);
        Stamina = MathF.Max(0, Stamina - 0.06f);
        Cheered(0.1f);
        if (_g == G.Walk && _nav == Nav.Climbing) { _nav = Nav.Direct; return; }
        if (rng.NextDouble() < P.Playfulness * 0.6f) Go(G.Cheer, 0.9f);
        else Go(G.Idle, rng.Range(0.8f, 2f));
    }

    public void OnRecovered(float throwSpeed, World w)
    {
        if (throwSpeed < 1 && AfterKnockdown(w)) return;
        if (throwSpeed > 900 * S) { TellWitnesses(w, M.Clamp01(throwSpeed / (2500 * S))); DiaryThrown(M.Clamp01(throwSpeed / (2500 * S))); RememberPlace(w, -0.4f, "being thrown"); }
        if (FeelAboutBeingThrown(throwSpeed)) return;
        float k = M.Clamp01(throwSpeed / (2500 * S));
        Annoyance = M.Clamp01(Annoyance + 0.2f + 0.4f * k);
        CursorTrust = MathF.Max(0, CursorTrust - 0.05f - 0.25f * k);
        if (throwSpeed > 900 * S || P.Aggression > 0.6f) { f.Emote(P.Aggression > 0.5f ? "#@!" : "!", 1.4f); Go(G.Annoyed, 1.4f); }
        else Go(G.Idle, 1.2f);
    }

    public void OnPoked(World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded) return;
        FeelAboutPoke();
        bool tickled = f.Tastes.Likes(Thing.YourCursor);
        CursorTrust = MathF.Max(0, CursorTrust - (tickled ? 0 : 0.05f));
        Annoyance = M.Clamp01(Annoyance + (tickled ? 0 : 0.12f));
        if (tickled && _g != G.Sleep) { Go(G.Cheer, 0.8f); return; }
        if (_g == G.Sleep) { f.Emote("!", 1); Go(G.Annoyed, 1.2f); return; }
        if (_g == G.CursorFight) return;
        if (WantsCursorFight() && rng.NextDouble() < 0.6) BeginCursorFight();
        else if (P.Aggression > 0.5f) Go(G.Swat, 0.34f);
        else Startle(w.Cursor);
    }

    public void OnHit(Figure? from, bool knockedDown, World w)
    {
        Annoyance = M.Clamp01(Annoyance + (knockedDown ? 0.35f : 0.15f));
        if (from == null)
        {
            CursorTrust = MathF.Max(0, CursorTrust - (knockedDown ? 0.2f : 0.08f));
            FeelUser(knockedDown ? -0.12f : -0.05f, knockedDown ? "Knocked them down" : "Hit them");
            if (knockedDown) TellWitnesses(w, 0.8f);
        }
        else if (from != f)
        {
            from.Brain.NoticeIHit(f);
            AddAffinity(from, knockedDown ? -0.25f : -0.08f);
            LoveHurt(from, knockedDown);
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
        _t0 += dt;
        _usedToCursor = MathF.Max(0, _usedToCursor - dt * 0.003f);
        _watchCd -= dt; _swatCd -= dt; _witnessCd -= dt; _startleCd -= dt; _waveCd -= dt; _scanT -= dt;
        UpdateNeeds(dt, w);
        UpdateLove(dt, w);
        UpdateWeather(dt, w);
        UpdateLife(dt, w);
        UpdateFamily(dt, w);
        UpdateDreams(w);
        TidyOutdoors(w);
        DesktopTick(w);
        if (_g is G.Groove or G.DanceWith || (_g == G.UseItem && _verb == Verb.Dance)) Practice(SkillKind.Dancing, dt * 0.002f, true);

        Vector2 cur = w.Cursor;
        float dist = Vector2.Distance(cur, f.Jt[J.Head]);
        float cspeed = w.CursorVel.Length();
        bool near = dist < 280 * S;
        _hoverT = w.Hover == f ? _hoverT + dt : 0;
        FeelPetting(w, dt);
        DriftFondness(dt);
        WatchForThreats(w, dt);
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
        if (calm && !World.Focus && ReactToCursor(w, cur, dist, cspeed, near, errand)) return;
        _awayT = near ? 0 : _awayT + dt;
        if (calm && _g != G.Sleep && IncomingPass(w)) return;

        switch (_g)
        {
            case G.Busy: break;
            case G.Idle:
                DoIdle(w);
                if (_t > _dur && f.Action != Act.Fidget) Choose(w);
                break;
            case G.Walk: DoWalk(w); break;
            case G.SitEdge:
            case G.SitFloor:
                if (!f.Grounded) { Go(G.Idle, 0.5f); break; }
                // Don't sit down on top of someone: shuffle over first.
                if (_t < 1.2f && f.Action != Act.SitEdge && f.Action != Act.SitFloor && KeepSpace(w)) { f.SetAction(Act.Stand); break; }
                f.DesiredVX = 0;
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
                bool approach = CursorTrust > 0.4f && (P.Curiosity > 0.55f || UserFondness > 0.4f) && MathF.Abs(dx) > (UserFondness > 0.4f ? 90 : 150) * S;
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
                KeepSpace(w);
                if (_glareAt != null && w.Figures.Contains(_glareAt)) { f.LookAt = _glareAt.Jt[J.Head]; FaceTo(_glareAt.Base.X); }
                else if (_snub) { f.LookAt = null; f.Facing = cur.X > f.Base.X ? -1 : 1; }
                else { FaceTo(cur.X); f.LookAt = cur; }
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
                KeepSpace(w);
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
            case G.Hunt: DoHunt(w); break;
            case G.UseItem: DoUseItem(w); break;
            case G.Sport: DoSport(w); break;
            case G.Revive: DoRevive(w); break;
            case G.DanceWith: DoDanceWith(w); break;
            case G.Groove: DoGroove(w); break;
            case G.Create: DoCreate(w); break;
            case G.Confess: DoConfess(w); break;
            case G.Snowball: DoSnowball(w); break;
            case G.Snowman: DoSnowman(w); break;
            case G.PetAnimal: DoPetAnimal(w); break;
            case G.Party: DoParty(w); break;
            case G.Game: DoGame(w); break;
            case G.Pose: DoPose(w); break;
            case G.Tourney: DoTourney(w); break;
            case G.Lasso: DoLasso(w); break;
            case G.Work: DoWork(w); break;
            case G.Build: DoBuild(w); break;
            case G.Ride: DoRide(w); break;
            case G.Swim: DoSwim(w); break;
            case G.Fish: DoFish(w); break;
            case G.Happening: DoHappening(w); break;
            case G.Boost: DoBoost(w); break;
            case G.Visit: DoVisit(w); break;
            case G.WatchScreen: DoWatchScreen(w); break;
            case G.LookAtScreen: DoLookAtScreen(w); break;
            case G.Victory:
                f.SetAction(Act.Cheer);
                KeepSpace(w);
                if (_t > _dur) Go(G.Idle, rng.Range(1, 2.5f));
                break;
        }
    }

    void UpdateNeeds(float dt, World w)
    {
        Hunger = M.Clamp01(Hunger + dt * 0.0012f * (0.6f + P.Energy * 0.8f));
        float speed = MathF.Abs(f.Vel.X);
        float cost = _g switch
        {
            G.Sleep => -0.07f,   // on the floor: not as restful as a bed (see Brain.Items)
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
        if (_g != G.Sleep) cost += World.Current.Night * 0.004f;   // late at night everyone flags
        // Heavier figures run out of puff sooner; exercise burns weight off, idling on a full stomach puts it on.
        if (cost > 0) cost *= 1 + f.Fat * 1.8f;
        if (World.WeightOn)
        {
            if (cost > 0.01f) f.Weight = MathF.Max(0, f.Weight - cost * dt * 0.0016f);
            else if (Hunger < 0.2f) f.Weight = MathF.Min(1, f.Weight + dt * 0.0000025f);
        }
        Stamina = World.StaminaOn ? M.Clamp01(Stamina - cost * dt) : 1;
        Frustration = MathF.Max(0, Frustration - dt * 0.002f);
        if (Frustration > 0.6f && rng.NextDouble() < dt * 0.03) { f.Emote(V("ugh.", "UGHHH!", "everything's annoying.", "…*sigh*", "this is not my day"), 1.2f); Annoyance = M.Clamp01(Annoyance + 0.05f); }

        // How much the current activity keeps boredom away (games, play, music and new things most; sitting about least).
        float fun = _g switch
        {
            G.Sport or G.Snowball or G.DanceWith or G.Groove or G.Hunt or G.CursorFight or G.Fight or G.Create => 1,
            G.Juggle or G.Dribble or G.Kick or G.Catch or G.Throw or G.Carry or G.Trick or G.Snowman or G.Confess => 0.9f,
            G.Chat or G.HighFive or G.PetAnimal or G.LookAtScreen or G.WatchScreen or G.Watch or G.Follow => 0.7f,
            G.UseItem => _verb is Verb.Bounce or Verb.Dance or Verb.Read or Verb.Eat or Verb.Warm or Verb.Hammock ? 0.7f : 0.15f,
            G.Walk => _purpose is WalkPurpose.Explore or WalkPurpose.Look ? 0.6f : 0.3f,
            G.SitWith => 0.4f,
            _ => 0,
        };
        Boredom = M.Clamp01(Boredom + dt * (fun > 0 ? -0.07f * fun : 0.012f * (0.5f + P.Curiosity)));
        // Company: anything done together, a game with others, or just being near friends.
        bool social = _g is G.Chat or G.HighFive or G.Follow or G.SitWith or G.DanceWith or G.Confess || _partner != null
                      || (_g == G.Sport && Match != null && Match.Players.Count > 1) || (_g == G.Snowball && _snowTarget != null)
                      || (_g == G.WatchScreen && w.Figures.Any(o => o != f && o.Brain._g == G.WatchScreen));
        bool company = !social && w.Figures.Any(o => o != f && !o.Dead && AffinityWith(o) > 0.3f && Vector2.Distance(o.Base, f.Base) < 250 * S);
        if (_g == G.PetAnimal) company = true;
        Loneliness = M.Clamp01(Loneliness + dt * (social ? -0.08f : company ? -0.015f : 0.01f * P.Sociability * (w.Figures.Count > 1 ? 1 : 0.3f)));
        Annoyance = M.Clamp01(Annoyance - dt * 0.04f);
        // Joy: quick lifts from good moments, settling toward how content it is overall (rested, busy, not lonely).
        float content = M.Clamp01((1 - Boredom) * 0.45f + (1 - Loneliness) * 0.3f + MathF.Min(Stamina, 0.5f) * 0.5f - Sadness * 0.5f - Annoyance * 0.3f) * (0.6f + P.Playfulness * 0.5f);
        content = M.Clamp01(content);
        Joy = Joy > content ? M.Clamp01(Joy - dt * 0.03f) : M.Clamp01(M.MoveTowards(Joy, content, dt * 0.01f));
        Sadness = M.Clamp01(Sadness - dt * 0.015f);
        Fear = M.Clamp01(Fear - dt * 0.25f);

        // Feed the body: mood bends how it walks, stands and moves.
        float cursorFear = Vector2.Distance(w.Cursor, f.Jt[J.Head]) < 220 * S && CursorTrust < 0.35f ? (1 - P.Bravery) * (0.35f - CursorTrust) * 2.5f : 0;
        float fightFear = InFight && f.HP < 45 ? (1 - P.Bravery) * 0.6f : 0;
        f.Mood = new MoodState
        {
            Tired = M.Clamp01((0.45f - Stamina) / 0.45f),
            Angry = M.Clamp01((Annoyance - 0.3f) / 0.7f),
            Happy = M.Clamp01(Joy * (1 - Sadness)),
            Scared = M.Clamp01(MathF.Max(Fear, MathF.Max(cursorFear, fightFear))),
            Sad = Sadness,
            Hurt = M.Clamp01((55 - f.HP) / 45),
        };
    }

    /// <summary>Notice other figures getting thrown around.</summary>
    void ScanOthers(World w)
    {
        WatchFights(w);
        NoticeTastes(w);
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
        // A fast cursor coming straight at them makes them jump; one just whizzing past doesn't. They get used to it.
        Vector2 toMe = f.Base - new Vector2(0, f.Height * 0.5f) - cur;
        float coming = cspeed > 1 && toMe.LengthSquared() > 1 ? Vector2.Dot(Vector2.Normalize(w.CursorVel), Vector2.Normalize(toMe)) : 0;
        if (_startleCd <= 0 && dist < 160 * S && cspeed > 2400 * S && coming > 0.55f)
        {
            _startleCd = 5;
            if (rng.NextDouble() > P.Bravery * 0.8f + _usedToCursor) { Startle(cur); return true; }
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
        // How it feels about you shows when your cursor comes by.
        float fond = UserFondness;
        if (f.Hunter && near && _g is G.Idle or G.SitFloor or G.SitEdge or G.Watch or G.Walk && Stamina > 0.25f && Rules.Enabled)
        {
            BeginHunt(w);
            return true;
        }
        if (near && fond < -0.35f && _awayT > 0.6f && _swatCd <= 0 && _g is G.Idle or G.Walk or G.SitFloor or G.SitEdge or G.Watch)
        {
            _swatCd = rng.Range(4, 8);
            if (P.Bravery < 0.4f) { f.Emote("!", 1); RunFromCursor(w, cur); }
            else if (WantsCursorFight() && rng.NextDouble() < 0.35) BeginCursorFight();
            else if (P.Aggression > 0.5f) { f.Emote("#@!", 1.2f); Go(G.Annoyed, 1.6f); }
            else Snub(cur);
            return true;
        }
        if (near && fond > 0.55f && CursorTrust > 0.4f && _awayT > 6 && _waveCd <= 0)
        {
            _waveCd = rng.Range(15, 30);
            _awayT = 0;
            if (rng.NextDouble() < 0.5) { f.Emote("♥", 1); Go(G.Wave, 1.5f); }
            else Go(G.Cheer, 1);
            return true;
        }
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
            if (rng.NextDouble() < P.Curiosity + MathF.Max(0, UserFondness) * 0.5f) { _stillT = 0; Go(G.Watch, 30); return true; }
        }
        return false;
    }

    void FaceTo(float x)
    {
        float dx = x - f.Base.X;
        if (MathF.Abs(dx) > 4 * S) f.Facing = MathF.Sign(dx);
    }

    void DoIdle(World w)
    {
        f.DesiredVX = 0;
        if (f.Action == Act.Fidget)
        {
            if (f.ActionT < f.FidgetDur) { KeepSpace(w); return; }
            f.SetAction(Act.Stand);
        }
        else f.SetAction(Act.Stand);
        KeepSpace(w);

        // Little habits while standing around, more often when bored or fidgety by nature.
        float rate = f.Style.FidgetRate * (1 + Boredom) * (1 + f.Mood.Tired * 0.5f);
        if (_t > 0.8f && f.DesiredVX == 0 && rng.NextDouble() < rate * World.Dt) { f.StartFidget(PickFidget()); return; }

        if (_t < _nextLook) return;
        _nextLook = _t + rng.Range(1.2f, 3.5f) / f.Style.LookAround;
        if (f.LookAt != null) return;
        if (rng.NextDouble() < 0.4) f.Facing = -f.Facing;
        else
        {
            _idleLook = f.Jt[J.Head] + new Vector2(f.Facing * rng.Range(60, 200) * S, rng.Range(-120, 80) * S);
            _idleLookUntil = _t + rng.Range(0.8f, 1.8f);
        }
    }

    Fidget PickFidget()
    {
        var md = f.Mood;
        var opts = new (float w, Fidget k)[]
        {
            (0.5f + md.Tired, Fidget.Stretch),
            (0.4f + P.Curiosity * 0.6f, Fidget.ScratchHead),
            (0.2f + P.Energy * 0.5f + Boredom * 0.6f, Fidget.CheckWatch),
            (0.2f + P.Energy * 0.6f + Boredom * 0.5f, Fidget.FootTap),
            (md.Tired * 2.5f + Boredom * 0.3f, Fidget.Yawn),
            (0.3f + P.Sociability * 0.4f, Fidget.Shrug),
            (P.Playfulness * 0.8f * (0.5f + Joy), Fidget.Groove),
        };
        float roll = rng.Range(0, opts.Sum(o => o.w));
        foreach (var (wt, k) in opts) { roll -= wt; if (roll <= 0) return k; }
        return Fidget.Stretch;
    }

    /// <summary>Personal space: when standing about, step aside from anyone we're standing on top of
    /// (partners we're deliberately close to are exempt). Walking past each other is fine.</summary>
    bool KeepSpace(World w)
    {
        if (!f.Grounded || f.JumpPending || f.Mode != Mode.Control) return false;
        float push = 0;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode is Mode.Spawning || o.Dead || o == _partner || o == _foe || o == _reviving) continue;
            if (MathF.Abs(o.Base.Y - f.Base.Y) > 4 * S || MathF.Abs(o.Vel.X) > o.WalkSpeed * 1.5f) continue;
            float dx = f.Base.X - o.Base.X;
            float want = 22 * S * (f.SizeMul + o.SizeMul) * 0.5f;
            if (MathF.Abs(dx) >= want) continue;
            float dir = MathF.Abs(dx) > 0.5f ? MathF.Sign(dx) : (f.Id < o.Id ? -1 : 1);
            push += dir * (1 - MathF.Abs(dx) / want);
        }
        if (push == 0) { f.KeepFacing = false; return false; }
        f.KeepFacing = true;   // sidestep without turning around
        f.DesiredVX = MathF.Sign(push) * f.WalkSpeed * Math.Clamp(MathF.Abs(push), 0.35f, 0.8f);
        return true;
    }

    void DoSleep()
    {
        if (!f.Grounded) { Go(G.Idle, 0.5f); return; }
        f.DesiredVX = 0;
        f.SetAction(_t < 1.2f ? Act.SitFloor : Act.Lie);
        if (_t > 1.2f && f.CurrentEmote != "z") f.Emote("z", 3);
        if ((Stamina > 0.97f && _t > 8) || _t > _dur)
        {
            if (_t > 10) { DiaryNapped(null); RememberPlace(World.Current, 0.3f, "a good nap"); }
            f.Emote("♪", 0.8f);
            Cheered(0.2f);
            Go(G.Cheer, 0.7f);   // a big stretch
        }
    }

    void Startle(Vector2 cur)
    {
        float away = -MathF.Sign(cur.X - f.Base.X);
        if (away == 0) away = -f.Facing;
        bool jumpy = Fear > 0.3f || P.Bravery < 0.45f;
        _usedToCursor = MathF.Min(0.6f, _usedToCursor + 0.15f);
        if (!jumpy || !f.Grounded)
        {
            // Just a flinch: duck and lean away.
            f.DuckT = 0.45f;
            f.Facing = (int)-away;
            f.Emote("!", 0.8f);
            Fear = MathF.Max(Fear, 0.4f);
            Go(G.Idle, 1.2f);
            return;
        }
        Go(G.Startled, 3);
        Fear = 0.9f;
        f.Emote("!", 0.9f);
        f.Facing = (int)-away;
        f.KeepFacing = true;
        f.Flailing = true;
        f.RequestJump(new Vector2(away * 100 * S, -480 * S), 0.03f);
    }

    void RunFromCursor(World w, Vector2 cur)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        float away = -MathF.Sign(cur.X - f.Base.X);
        if (away == 0) away = 1;
        bool scared = CursorTrust < 0.25f;
        float x = M.ClampIn(f.Base.X + away * (scared ? 300 : 90) * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        WalkTo(x, scared, () => Go(G.Idle, 1.5f));
    }

    // ================= choosing what to do =================

    void Choose(World w)
    {
        if (f.Visitor != VisitorKind.None) { Go(G.Visit, 1e6f); return; }
        // Still in a game (e.g. got knocked over): back to it.
        if (Match != null && w.Matches.Contains(Match) && !Match.Over) { Go(G.Sport, 600); return; }
        Match = null;
        if (w.Game is { Over: false } ug && ug.Players.Contains(f)) { Go(G.Game, 600); return; }
        EndGameForMe();
        if (w.Tourney is { Over: false } tn && tn.Entrants.Contains(f)) { Go(G.Tourney, 600); return; }
        if (BabyChoose(w)) return;
        var env = w.Env;
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null || !f.Grounded) { Go(G.Idle, 0.5f); return; }
        float E = P.Energy, tired = 1 - Stamina;
        // Likes and dislikes tilt every choice: loved things are up to ~2.5x as likely, hated ones rare.
        float L(Thing t) => Taste(t);
        var opts = new OptionList();
        opts.Add(0.5f + (1 - E) * 0.5f, () => Go(G.Idle, rng.Range(1.5f, 4.5f)), "Hang out");
        opts.Add((tired * 0.8f + (1 - E) * 0.3f) * L(Thing.Sitting), () => Go(G.SitFloor, rng.Range(4, 12)), "Sit down");
        if (Stamina < 0.3f || (Stamina < 0.55f && f.Tastes.Likes(Thing.Napping)))
            opts.Add((0.55f - Stamina) * 8 * L(Thing.Napping), () => { NoBed(w); Go(G.Sleep, rng.Range(15, 40)); }, "Nap");
        if (seg.X2 - seg.X1 > 60 * S) opts.Add((0.4f + E * 0.6f + Boredom * 0.5f) * (0.4f + Stamina), () => Wander(seg), "Wander");
        if (Stamina > 0.35f && PickExplore(env, seg, out var explore))
            opts.Add(P.Curiosity * (0.5f + Boredom * 1.2f) * Stamina * L(Thing.Exploring), explore, "Explore");
        if (!seg.Solid && PickEdge(env, seg, out float edgeX, out int dir))
            opts.Add((tired + (1 - E) * 0.6f + 0.1f) * L(Thing.Ledges), () => { _sitDir = dir; WalkTo(edgeX - dir * 3.5f * S, false, () => Go(G.SitEdge, rng.Range(5, 15))); }, "Sit on a ledge");
        if (Stamina > 0.4f) opts.Add(P.Playfulness * E * 0.25f * L(Thing.Tricks), () => { Go(G.Trick, 3); f.RequestFlip(70 * S); }, "Do a trick");
        if (Stamina > 0.3f) opts.Add(P.Playfulness * 0.15f, () => Go(G.Cheer, 0.9f), "Cheer");
        if (Stamina > 0.3f && f.Tastes.Likes(Thing.Dancing)) opts.Add(0.25f * L(Thing.Dancing) * (0.5f + Joy), () => { Go(G.Idle, 3); f.StartFidget(Fidget.Groove); }, "Dance");
        opts.Category = "Hang out with someone"; if (SocialOption(w) is { } social) opts.Add(social);
        opts.Category = "Start a fight"; if (FightOption(w) is { } fight) opts.Add(fight);
        opts.Category = "Play ball"; if (Stamina > 0.3f && BallOption(w) is { } ball) opts.Add(ball);
        opts.Category = "Go see you"; if (UserOption(w) is { } user) opts.Add(user);
        opts.Category = "Hunt your cursor"; if (HuntOption(w) is { } hunt) opts.Add(hunt);
        opts.Category = _optItemLabel; if (ItemOption(w) is { } useItem) { opts.Category = _optItemLabel; opts.Add(useItem); }
        opts.Category = "Play a game"; if (SportOption(w) is { } sport) opts.Add(sport);
        ScreenOptions(w, opts);
        WishOptions(w, opts);
        RomanceOptions(w, opts);
        WeatherOptions(w, opts);
        PetOptions(w, opts);
        LifeOptions(w, opts);
        HomeOptions(w, opts);
        RivalOptions(w, opts);
        CampfireOptions(w, opts);
        ClubOptions(w, opts);
        PetCareOptions(w, opts);
        LassoOptions(w, opts);
        GardenOptions(w, opts);
        TownOptions(w, opts);
        HelpBuildOptions(w, opts);
        OutdoorOptions(w, opts);
        BoostOptions(w, opts);
        VisitorOptions(w, opts);
        MemorialOptions(w, opts);
        PrankOptions(w, opts);
        ParentOptions(w, opts);
        Decide(opts);
    }

    void Wander(Platform seg)
    {
        float x = M.ClampIn(f.Base.X + rng.Range(-500, 500) * S, seg.X1 + 8 * S, seg.X2 - 8 * S);
        if (MathF.Abs(x - f.Base.X) < 30 * S) x = M.ClampIn(f.Base.X - MathF.Sign(x - f.Base.X + 0.1f) * 200 * S, seg.X1 + 8 * S, seg.X2 - 8 * S);
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
}
