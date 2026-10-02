using System.Numerics;

namespace StickFight;

/// <summary>Fighting: who picks fights with whom (colour rules + personality + grudges), footwork and
/// spacing, choosing attacks, blocking/dodging, fleeing when hurt, celebrating, friends joining in,
/// and boxing the user's cursor.</summary>
sealed partial class Brain
{
    Figure? _foe, _downBy;
    bool _spar, _reacted, _fleeing, _downInSpar;
    float _atkCd, _retreatT, _foeDownT, _cursorAwayT;
    AttackKind? _plan, _lastAtk;
    int _landed, _helpedFightOf = -1;

    FightSettings Rules => World.Current.Fight;
    Relation RelationTo(Figure o) => Rules.Between(f, o);
    public bool InFight => _g is G.Fight or G.CursorFight;
    public Figure? Foe => _g == G.Fight || (_g == G.Walk && _purpose == WalkPurpose.Social) ? _foe : null;
    public bool IsSparringWith(Figure o) => _g == G.Fight && _foe == o && _spar;
    float FleeHP => 18 + (1 - P.Bravery) * 22;

    bool ValidFoe(Figure? o) => o != null && World.Current.Figures.Contains(o) && o.Mode != Mode.Spawning;

    /// <summary>Who our strikes connect with: the current foe, anyone fighting us, and enemies already brawling.</summary>
    bool CanStrike(Figure o) => !o.Brain.Baby && (
        o == _foe || o.Brain.Foe == f || (RelationTo(o) == Relation.Enemies && o.Brain.InFight && Rules.Enabled));

    // ---------------- starting fights ----------------

    (float, Action)? FightOption(World w)
    {
        if (!Rules.Enabled || Stamina < 0.25f || Rules.Frequency <= 0 || f.HP < 50 || Baby) return null;
        Figure? best = null;
        float bestScore = 0;
        bool bestSpar = false;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || o.Brain.Asleep || o.Climbing || !o.Grounded || o.Brain.InFight || o.Brain.Baby) continue;
            float d = Vector2.Distance(o.Base, f.Base);
            if (d > 1400 * S) continue;
            float a = AffinityWith(o);
            float score;
            bool spar;
            switch (RelationTo(o))
            {
                case Relation.Enemies:
                    score = (0.3f + P.Aggression) * (0.6f + Annoyance + MathF.Max(0, -a));
                    spar = false;
                    break;
                case Relation.Rivals:
                    score = 0.15f + P.Playfulness * 0.6f + P.Aggression * 0.4f;
                    spar = true;
                    break;
                case Relation.Neutral when a < -0.25f:
                    score = P.Aggression * (Annoyance - a);
                    spar = false;
                    break;
                default:
                    continue;
            }
            score /= 1 + d / (600 * S);
            if (score > bestScore) { bestScore = score; best = o; bestSpar = spar; }
        }
        if (best == null) return null;
        var foe = best;
        bool sp = bestSpar;
        return (bestScore * Rules.Frequency * Stamina * Taste(sp ? Thing.Sparring : Thing.Fighting), () => Engage(foe, sp, w));
    }

    void Engage(Figure o, bool spar, World w)
    {
        float side = MathF.Sign(f.Base.X - o.Base.X);
        if (side == 0) side = 1;
        f.ClimbPace = ClimbPace(urgent: true);
        Navigate(() => ValidFoe(o) && o.Mode is Mode.Control or Mode.GetUp ? o.Base + new Vector2(side * 40 * S, 0) : null,
                 14 * S, true, () => StartFight(o, spar, w), WalkPurpose.Social);
        _foe = o;   // remembered while approaching, so the other side knows we're still coming
    }

    void StartFight(Figure o, bool spar, World w)
    {
        if (!ValidFoe(o) || o.Mode != Mode.Control || !Rules.Enabled) { Go(G.Idle, 1); return; }
        if (spar)
        {
            if (!o.Brain.AcceptSpar(f)) { f.Emote("?", 1); Go(G.Idle, 1.5f); return; }
            f.Emote("!", 1);
        }
        else
        {
            f.Emote(P.Aggression > 0.6f ? "#@!" : "!", 1.1f);
            o.Brain.Challenged(f, w);
        }
        BeginFight(o, spar);
    }

    void BeginFight(Figure o, bool spar)
    {
        Go(G.Fight, spar ? rng.Range(10, 20) : rng.Range(20, 45));
        _foe = o;
        _spar = spar;
        _atkCd = rng.Range(0.3f, 0.8f);
        _plan = null;
        _reacted = false;
        _landed = 0;
        f.CanHit = CanStrike;
    }

    bool AcceptSpar(Figure from)
    {
        bool free = f.Mode == Mode.Control && f.Grounded && !f.Climbing && f.Carrying == null && _g is G.Idle or G.Watch or G.Walk or G.SitFloor;
        if (!free || !Rules.Enabled) return false;
        float yes = 0.3f + P.Playfulness * 0.4f + P.Aggression * 0.25f - (1 - Stamina) * 0.5f;
        if (rng.NextDouble() > yes) { f.Emote("…", 1); return false; }
        BeginFight(from, true);
        FaceTo(from.Base.X);
        f.Emote("!", 1);
        return true;
    }

    /// <summary>Someone came at us. Fight back or run.</summary>
    void Challenged(Figure from, World w)
    {
        if (_g is G.Busy or G.Fight || f.Mode != Mode.Control) return;
        if (Baby) { Flee(from, w); return; }
        float fightBack = P.Bravery * 0.6f + P.Aggression * 0.5f + f.HP / 100 * 0.2f;
        if (rng.NextDouble() < fightBack) { BeginFight(from, false); f.Emote(P.Aggression > 0.5f ? "#@!" : "!", 1); }
        else Flee(from, w);
    }

    void Flee(Figure from, World w)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        f.Emote("!", 1);
        if (seg == null) { Go(G.Idle, 1); return; }
        float away = -MathF.Sign(from.Base.X - f.Base.X);
        if (away == 0) away = 1;
        float x = M.ClampIn(f.Base.X + away * 600 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        if (MathF.Abs(x - f.Base.X) < 60 * S) x = M.ClampIn(f.Base.X - away * 600 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        Navigate(() => new Vector2(x, f.Base.Y), 6 * S, true, () => { _fleeing = false; Go(G.Idle, 2); }, WalkPurpose.Other);
        _fleeing = true;
        _foe = null;
    }

    // ---------------- hooks ----------------

    public void OnStruck(Figure from, bool blocked, bool knockedDown, World w)
    {
        Annoyance = M.Clamp01(Annoyance + (knockedDown ? 0.25f : blocked ? 0.03f : 0.08f));
        bool sparring = _spar && from == _foe && _g == G.Fight;
        if (!sparring) AddAffinity(from, knockedDown ? -0.12f : -0.03f);
        if (knockedDown) { _downBy = from; _downInSpar = sparring; return; }
        if (_g == G.Fight || f.Mode != Mode.Control) return;
        // Ambushed: retaliate or run.
        if (RelationTo(from) == Relation.Friends && AffinityWith(from) > 0.3f) { f.Emote("?", 1); Go(G.Annoyed, 1); return; }
        if (f.Grounded && rng.NextDouble() < P.Bravery * 0.6f + P.Aggression * 0.6f) BeginFight(from, false);
        else Flee(from, w);
    }

    /// <summary>After getting up from a knockdown: rematch, give up, or run.</summary>
    bool AfterKnockdown(World w)
    {
        var by = _downBy;
        _downBy = null;
        _foe = null;
        if (by == null || !ValidFoe(by) || !Rules.Enabled) return false;
        if (_downInSpar)
        {
            Saddened(0.15f);
            f.Emote(rng.NextDouble() < 0.5 ? "…" : "ha", 1.2f);
            Go(G.Annoyed, 1.2f);
            return true;
        }
        if (f.HP > FleeHP && rng.NextDouble() < P.Bravery * 0.7f + P.Aggression * 0.5f)
        {
            f.Emote("#@!", 1.2f);
            Engage(by, false, w);
        }
        else Flee(by, w);
        return true;
    }

    // ---------------- the fight itself ----------------

    void DoFight(World w)
    {
        var o = _foe;
        if (!Rules.Enabled || !ValidFoe(o) || o!.Brain.Asleep) { EndFight(); Go(G.Idle, 1); return; }
        float dx = o.Base.X - f.Base.X, d = MathF.Abs(dx);
        f.LookAt = o.Jt[J.Head];
        f.KeepFacing = true;
        f.AllowWalkOff = false;

        if (o.KO)
        {
            if (o.Dead) f.Emote(P.Aggression > 0.7f ? "ha" : "!", 1.2f);
            Victory(o);
            return;
        }
        if (o.Mode is Mode.Ragdoll or Mode.GetUp)
        {
            f.DesiredVX = 0;
            f.SetAction(Act.Fight);
            FaceTo(o.Base.X);
            if (_spar) { Victory(o); return; }
            _foeDownT += World.Dt;
            if (_foeDownT > 0.5f && f.CurrentEmote == null && rng.NextDouble() < 0.02) f.Emote(P.Aggression > 0.5f ? "ha" : "!", 1);
            if (_foeDownT > 5) Victory(o);
            return;
        }
        _foeDownT = 0;
        if (o.Brain.Foe == null)
        {
            // They walked away or ran off. (A foe busy with someone else, e.g. we jumped in to help a
            // friend, or one re-approaching us, still counts as in the fight.)
            if (o.Brain._fleeing && !_spar && P.Aggression > 0.65f && Stamina > 0.4f) { Engage(o, false, w); return; }
            Victory(o);
            return;
        }
        if (o.Brain._g != G.Fight && o.Brain._foe == f) { f.DesiredVX = 0; f.SetAction(Act.Fight); FaceTo(o.Base.X); return; }   // they're re-approaching
        if ((f.Grounded && o.Grounded && MathF.Abs(o.Base.Y - f.Base.Y) > 10 * S) || d > 900 * S)
        {
            if (!_spar && P.Aggression > 0.5f && Stamina > 0.35f) Engage(o, false, w);
            else { EndFight(); Go(G.Idle, 1); }
            return;
        }
        if (_t > _dur || Stamina < 0.12f)
        {
            // Truce: both worn out.
            f.Emote(_spar ? "♪" : "…", 1.2f);
            if (_spar) AddAffinity(o, 0.08f);
            EndFight();
            Go(_spar ? G.Cheer : G.Annoyed, 1);
            return;
        }
        if (!_spar && f.HP < FleeHP) { EndFight(); Flee(o, w); return; }

        f.SetAction(Act.Fight);
        if (!f.Spinning) FaceTo(o.Base.X);
        if (f.HitStun > 0) { f.DesiredVX = 0; return; }
        var style = f.Style;

        // ---- defence: read their windup (each fighting style defends its own way) ----
        var fa = o.Atk;
        if (fa == null) _reacted = false;
        else if (!_reacted && o.InWindup && f.Atk == null && d < fa.Range * S + 16 * S)
        {
            _reacted = true;
            float skill = (0.25f + P.Bravery * 0.25f + P.Energy * 0.2f + P.Playfulness * 0.1f) * (0.6f + 0.4f * Stamina) * style.DefenseSkill;
            if (rng.NextDouble() < skill) Defend(fa, o, dx);
        }
        if (_retreatT > 0) { _retreatT -= World.Dt; f.DesiredVX = -MathF.Sign(dx) * f.WalkSpeed * 1.6f; return; }
        if (f.Atk != null || f.Blocking || f.JumpPending || f.DuckT > 0) { f.DesiredVX = 0; return; }

        // ---- offence ----
        if (ShootFoe(o, d, dx, w)) return;
        if (f.LastAttackLanded) { _landed++; f.LastAttackLanded = false; }
        if (_counter && o.Atk == null)
        {
            // Turtles punish right after a successful block.
            _counter = false;
            _plan = d < 32 * S ? AttackKind.Cross : AttackKind.FrontKick;
            _atkCd = 0;
        }
        _plan ??= PickAttack(d);
        if (_plan == AttackKind.FlyingKick)
        {
            _plan = null;
            if (d > 70 * S && d < 220 * S &&
                SolveLob(f.Base, o.Base - new Vector2(MathF.Sign(dx) * 14 * S, 0), f.Gravity, 28 * S, 200 * S, 1000 * S, out var v))
            {
                f.StartFlyingKick(v);
                _atkCd = AttackCooldown() + 0.3f;
            }
            return;
        }
        var def = AttackDef.Get(_plan!.Value);
        float want = def.Range * S * 0.92f * style.Spacing + (def.Foot ? 0 : f.WeaponReach * 0.8f);
        _atkCd -= World.Dt;
        float err = d - want;
        if (d < 18 * S) f.DesiredVX = -MathF.Sign(dx) * f.WalkSpeed * 1.3f;   // too close: give ourselves room
        else if (err > 5 * S) f.DesiredVX = MathF.Sign(dx) * (d > 120 * S ? f.RunSpeed * 0.8f : f.WalkSpeed * 1.2f);
        else if (err < -8 * S) f.DesiredVX = -MathF.Sign(dx) * f.WalkSpeed;
        else
        {
            // Footsie: boxers bob in and out, brawlers plod forward, turtles hold their ground.
            float sway = style.Fight switch { FightStyle.Boxer => 0.55f, FightStyle.Acrobat => 0.45f, FightStyle.Turtle => 0.12f, _ => 0.3f };
            f.DesiredVX = MathF.Sin(_t * (style.Fight == FightStyle.Boxer ? 4.5f : 3) + f.Id) * f.WalkSpeed * sway
                        + (style.Fight == FightStyle.Brawler ? MathF.Sign(dx) * f.WalkSpeed * 0.15f : 0);
            if (_atkCd <= 0)
            {
                f.StartAttack(_plan.Value);
                _lastAtk = _plan;
                _plan = null;
                _atkCd = AttackCooldown();
            }
        }
    }

    AttackKind PickAttack(float d)
    {
        // Combo: a landed jab is often followed straight away by a cross.
        if (_lastAtk == AttackKind.Jab && _landed > 0 && rng.NextDouble() < 0.5f + P.Playfulness * 0.3f) { _landed = 0; _atkCd = 0.05f; return AttackKind.Cross; }
        _landed = 0;
        var opts = new List<(float w, AttackKind k)>
        {
            (1.2f, AttackKind.Jab),
            (0.9f, AttackKind.Cross),
            (0.8f, AttackKind.FrontKick),
            (0.3f + P.Playfulness * 0.6f, AttackKind.Roundhouse),
            (0.25f + P.Playfulness * 0.25f, AttackKind.Sweep),
            (0.3f + P.Aggression * 0.4f, AttackKind.Haymaker),
            (0.2f + P.Playfulness * 0.3f, AttackKind.SpinKick),
        };
        if (d < 30 * S) opts.Add((0.6f, AttackKind.Uppercut));
        if (d > 70 * S && Stamina > 0.3f) opts.Add((P.Playfulness * 0.5f + P.Energy * 0.3f, AttackKind.FlyingKick));
        // The figure's fighting style shapes its move choice.
        var bias = StyleBias[(int)f.Style.Fight];
        for (int i = 0; i < opts.Count; i++) opts[i] = (opts[i].w * bias[(int)opts[i].k], opts[i].k);
        if (f.Melee)
        {
            // Holding a sword/bat/pan: swing it (the arm attacks), kick less.
            for (int i = 0; i < opts.Count; i++)
                opts[i] = (opts[i].w * (AttackDef.Get(opts[i].k).Foot ? 0.35f : opts[i].k is AttackKind.Haymaker or AttackKind.Cross ? 2.2f : 1.4f), opts[i].k);
        }
        else if (f.Gear != Gear.None)
        {
            // Gear on the fists: box more, kick less (gloves especially).
            float punch = f.Gear == Gear.BoxingGloves ? 1.8f : 1.4f, kick = f.Gear == Gear.BoxingGloves ? 0.35f : 0.7f;
            for (int i = 0; i < opts.Count; i++)
                opts[i] = (opts[i].w * (AttackDef.Get(opts[i].k).Foot ? kick : punch), opts[i].k);
        }
        float roll = rng.Range(0, opts.Sum(o => o.w));
        foreach (var (wt, k) in opts) { roll -= wt; if (roll <= 0) return k; }
        return AttackKind.Jab;
    }

    float AttackCooldown() =>
        rng.Range(0.25f, 0.8f) * (1.4f - P.Aggression * 0.6f - P.Energy * 0.3f) / (0.6f + 0.4f * Stamina)
        / MathF.Max(0.4f, Rules.Frequency) / f.Style.AttackRate;

    // Move preferences per fighting style, indexed [style][AttackKind]:
    //                     Jab   Cross Upper FrontK Round Sweep Flying Haymkr Spin
    static readonly float[][] StyleBias =
    {
        new[] { 1f,   1f,   1f,   1f,    1f,   1f,   1f,    1f,    1f },     // Auto (unused)
        new[] { 2.0f, 2.0f, 1.6f, 0.25f, 0.15f, 0.1f, 0.1f, 0.4f,  0.05f },  // Boxer
        new[] { 0.6f, 0.4f, 0.3f, 1.8f,  2.0f, 0.8f, 0.6f,  0.15f, 1.6f },   // Kicker
        new[] { 0.8f, 1.4f, 1.2f, 0.8f,  0.4f, 0.3f, 0.3f,  2.4f,  0.1f },   // Brawler
        new[] { 0.6f, 0.5f, 0.5f, 0.8f,  1.4f, 1.5f, 2.2f,  0.1f,  1.4f },   // Acrobat
        new[] { 1.6f, 1.4f, 0.6f, 1.0f,  0.3f, 0.2f, 0.1f,  0.2f,  0.1f },   // Turtle
    };

    bool _counter;

    /// <summary>React to an incoming attack the way this fighting style does.</summary>
    void Defend(AttackDef fa, Figure o, float dx)
    {
        var style = f.Style.Fight;
        float r = (float)rng.NextDouble();
        float blockFor = fa.Windup - o.AtkT + fa.Active + 0.12f;
        if (fa.Height == HitHeight.Low)
        {
            // Hop the sweep (acrobats backflip over it).
            if (style == FightStyle.Acrobat) BackflipAway(dx);
            else f.RequestJump(new Vector2(0, -560 * S), 0.02f);
            return;
        }
        switch (style)
        {
            case FightStyle.Boxer:
                if (fa.Height == HitHeight.High && r < 0.7f) f.DuckT = blockFor + 0.1f;     // slip under it
                else if (r < 0.75f) f.BlockT = blockFor;
                else _retreatT = 0.25f;
                break;
            case FightStyle.Turtle:
                f.BlockT = blockFor + 0.1f;
                _counter = true;
                break;
            case FightStyle.Acrobat:
                if (r < 0.45f) BackflipAway(dx);
                else if (fa.Height == HitHeight.High && r < 0.75f) f.DuckT = blockFor;
                else _retreatT = 0.3f;
                break;
            case FightStyle.Kicker:
                if (r < 0.6f) _retreatT = 0.32f;
                else f.BlockT = blockFor;
                break;
            default:   // Brawler: just eats it or covers up
                if (r < 0.5f) f.BlockT = blockFor;
                break;
        }
    }

    void BackflipAway(float dx)
    {
        f.KeepFacing = true;
        f.RequestJump(new Vector2(-MathF.Sign(dx) * 260 * S, -720 * S), 0.03f, flip: true);
    }

    void Victory(Figure o)
    {
        bool spar = _spar;
        EndFight();
        Go(G.Victory, 1.6f);
        _spar = spar;
        f.Emote(spar ? "♪" : P.Aggression > 0.5f ? "ha" : "♪", 1.3f);
        Cheered(0.4f);
        World.Current?.Tourney?.Report(f, o, World.Current);
        RecordBout(o, true);
        o.Brain.RecordBout(f, false);
        DiaryFightWon(o, spar);
        Practice(SkillKind.Fighting, spar ? 0.03f : 0.05f);
        AddAffinity(o, spar ? 0.05f : -0.05f);
        Stamina = MathF.Max(0, Stamina - 0.05f);
    }

    void EndFight()
    {
        f.CancelAttack();
        f.BlockT = 0;
        f.KeepFacing = false;
        f.PunchTarget = null;
        _foe = null;
        _plan = null;
    }

    // ---------------- knock-outs, deaths and revivals ----------------

    readonly HashSet<int> _sawKO = new();
    Figure? _reviving;

    /// <summary>React to anyone nearby being knocked out or killed.</summary>
    void NoticeKOs(World w)
    {
        foreach (var o in w.Figures)
        {
            if (o == f) continue;
            if (!o.KO) { _sawKO.Remove(o.Id); continue; }
            if (_sawKO.Contains(o.Id) || _g == G.Busy) continue;
            float d = Vector2.Distance(o.Jt[J.Pelvis], f.Jt[J.Pelvis]);
            if (d > 1000 * S) continue;
            _sawKO.Add(o.Id);
            bool friend = RelationTo(o) == Relation.Friends || AffinityWith(o) > 0.35f;
            var killer = o.KilledBy;
            if (o.Dead)
            {
                if (killer == f) continue;
                if (friend)
                {
                    // Grief, and a grudge against whoever did it.
                    f.Emote("…", 2.5f);
                    Saddened(0.85f);
                    Annoyance = M.Clamp01(Annoyance + 0.5f);
                    if (killer != null) AddAffinity(killer, -0.7f);
                }
                else f.Emote(P.Aggression > 0.6f ? "ha" : "!", 1.2f);
                continue;
            }
            if (killer == f) continue;
            if (friend && f.Mode == Mode.Control && f.Grounded && !InFight && Stamina > 0.15f && rng.NextDouble() < 0.4f + P.Sociability * 0.5f)
                GoRevive(o, w);
            else f.Emote("!", 1);
        }
    }

    void GoRevive(Figure o, World w)
    {
        f.Emote("!", 1);
        float side = MathF.Sign(f.Base.X - o.Jt[J.Pelvis].X);
        if (side == 0) side = 1;
        Navigate(() =>
        {
            if (!w.Figures.Contains(o) || !o.KO || o.Dead) return null;
            Vector2 pel = o.Jt[J.Pelvis];
            float y = w.Env.Below(pel.X, pel.Y - 6 * S)?.Y ?? pel.Y;
            return new Vector2(pel.X + side * 16 * S, y);
        }, 6 * S, true, () => { Go(G.Revive, 2.2f); _reviving = o; }, WalkPurpose.Social);
    }

    void DoRevive(World w)
    {
        var o = _reviving;
        if (o == null || !w.Figures.Contains(o) || !o.KO || o.Dead) { _reviving = null; Go(G.Idle, 1); return; }
        FaceTo(o.Jt[J.Pelvis].X);
        f.LookAt = o.Jt[J.Head];
        f.SetAction(Act.SitFloor);
        if (_t < _dur) return;
        o.Revive(f);
        w.Witness(f, o, SocialAct.Help, 1);
        f.Emote("♥", 1.2f);
        Cheered(0.4f);
        o.Brain.Cheered(0.4f);
        AddAffinity(o, 0.2f);
        o.Brain.AddAffinity(f, 0.3f);
        _reviving = null;
        Go(G.Idle, 1.5f);
    }

    // ---------------- noticing fights nearby ----------------

    void WatchFights(World w)
    {
        NoticeKOs(w);
        if (!Rules.Enabled) return;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Brain.Foe is not { } enemy || enemy == f) continue;
            float d = Vector2.Distance(o.Base, f.Base);
            if (d > 800 * S) continue;
            bool calm = f.Grounded && f.Mode == Mode.Control &&
                        (_g is G.Idle or G.Watch or G.SitFloor or G.SitEdge || (_g == G.Walk && _purpose == WalkPurpose.Wander));
            if (!calm) continue;
            // Help a friend who's in a real fight with someone we don't like.
            bool friend = RelationTo(o) == Relation.Friends || AffinityWith(o) > 0.45f;
            if (friend && !o.Brain._spar && RelationTo(enemy) != Relation.Friends && AffinityWith(enemy) < 0.35f &&
                _helpedFightOf != o.Id && Stamina > 0.3f && rng.NextDouble() < P.Bravery * 0.5f + P.Sociability * 0.3f)
            {
                _helpedFightOf = o.Id;
                f.Emote("!", 1);
                Engage(enemy, false, w);
                return;
            }
            // Otherwise: spectate.
            if (_g is G.Idle or G.Watch)
            {
                FaceTo(o.Base.X);
                _idleLook = (o.Jt[J.Head] + enemy.Jt[J.Head]) * 0.5f;
                _idleLookUntil = _t + 1;
                if (f.CurrentEmote == null && rng.NextDouble() < 0.04) f.Emote(P.Aggression > 0.5f ? "ha" : rng.NextDouble() < 0.5 ? "!" : "!!", 1);
            }
        }
    }

    // ---------------- boxing the cursor ----------------

    bool WantsCursorFight() =>
        Rules.Enabled && Rules.PunchCursor && Stamina > 0.25f &&
        ((P.Aggression > 0.55f && CursorTrust < 0.55f) || (UserFondness < -0.5f && P.Aggression > 0.4f && P.Bravery > 0.4f));

    void BeginCursorFight()
    {
        Go(G.CursorFight, rng.Range(6, 14));
        _atkCd = 0.2f;
        _plan = null;
        _cursorAwayT = 0;
        f.Emote("#@!", 1);
    }

    bool _airPunch;

    void DoCursorFight(World w)
    {
        Vector2 cur = w.Cursor;
        float dx = cur.X - f.Base.X, d = MathF.Abs(dx);
        Vector2 neck = f.Jt[J.Neck];
        float up = neck.Y - cur.Y;                            // how far the cursor is above our shoulders
        float rel = f.Base.Y - cur.Y;                         // ...and above our feet
        float armReach = f.Arm * 0.95f;
        bool punchable = rel > -4 * S && up <= armReach;
        bool overhead = up > armReach && up < armReach + f.Height * 1.6f;
        _cursorAwayT = d > 450 * S || (!punchable && !overhead) ? _cursorAwayT + World.Dt : 0;
        if (_cursorAwayT > 1.5f || _t > _dur || !Rules.PunchCursor || (Control.MouseButtons & MouseButtons.Left) != 0)
        {
            f.PunchTarget = null;
            _airPunch = false;
            if (_landed >= 2) { f.Emote("ha", 1.2f); Go(G.Victory, 1.4f); } else Go(G.Annoyed, 1);
            return;
        }
        f.SetAction(Act.Fight);
        f.KeepFacing = true;
        if (!f.Spinning) FaceTo(cur.X);
        f.LookAt = cur;
        f.PunchTarget = cur;
        if (f.LastAttackLanded) { _landed++; f.LastAttackLanded = false; }

        // Mid-air: throw the punch as we reach the top of the jump.
        if (!f.Grounded)
        {
            f.DesiredVX = 0;
            // Time the jab so its strike lands right at the top of the jump.
            var jab = AttackDef.Get(AttackKind.Jab);
            if (_airPunch && f.Vel.Y > -f.Gravity * (jab.Windup + jab.Active * 0.5f)) { f.StartAirAttack(AttackKind.Jab); _airPunch = false; }
            return;
        }
        if (f.JumpPending) { f.DesiredVX = 0; return; }   // crouching to jump: keep the punch queued
        _airPunch = false;
        if (f.Atk != null || f.HitStun > 0) { f.DesiredVX = 0; return; }
        _atkCd -= World.Dt;

        // Too high to reach standing: get under it and jump at it.
        if (overhead)
        {
            if (d > 40 * S) { f.DesiredVX = MathF.Sign(dx) * f.RunSpeed * 0.8f; return; }
            f.DesiredVX = 0;
            if (_atkCd > 0) return;
            float need = up - f.Arm * 0.6f;
            float vy = MathF.Sqrt(2 * f.Gravity * MathF.Max(need, 20 * S));
            f.RequestJump(new Vector2(dx * 1.2f, -vy), 0.06f);
            _airPunch = true;
            _atkCd = rng.Range(0.5f, 0.9f);
            if (rng.NextDouble() < 0.3) f.Emote("#@!", 0.7f);
            return;
        }

        // Pick a strike for the cursor's height, and stand where that strike reaches it.
        float hipY = f.Base.Y - f.StandHip;
        bool low = cur.Y > hipY - f.Torso * 0.15f;
        AttackKind k;
        float reachH;
        if (low)
        {
            k = rng.NextDouble() < 0.6 ? AttackKind.FrontKick : AttackKind.Roundhouse;
            float dyHip = cur.Y - hipY;
            reachH = MathF.Sqrt(MathF.Max(0, f.Leg * f.Leg * 0.92f - dyHip * dyHip));
        }
        else
        {
            k = up > f.Arm * 0.35f ? (rng.NextDouble() < 0.5 ? AttackKind.Uppercut : AttackKind.Jab) : (rng.NextDouble() < 0.6 ? AttackKind.Jab : AttackKind.Cross);
            reachH = MathF.Sqrt(MathF.Max(0, armReach * armReach - up * up));
        }
        float want = MathF.Max(reachH * 0.72f, 5 * S);
        float err = d - want;
        if (err > 4 * S) f.DesiredVX = MathF.Sign(dx) * f.WalkSpeed * (err > 60 * S ? 2.2f : 1.3f);
        else if (err < -10 * S) f.DesiredVX = -MathF.Sign(dx) * f.WalkSpeed;
        else
        {
            f.DesiredVX = 0;
            if (_atkCd <= 0) { f.StartAttack(k); _atkCd = rng.Range(0.3f, 0.65f) * (1.3f - P.Aggression * 0.5f); }
        }
    }
}
