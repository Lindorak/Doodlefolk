using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Combat on the body: attacks (poses + hit detection), blocking, hit-stun, health and
/// poise (break it and you go flying), and ragdolls that bowl other figures over.</summary>
sealed partial class Figure
{
    public float HP = 100, Poise = 100;
    public Gear Gear;
    /// <summary>Knocked out: stays down until revived (or the timer runs out).</summary>
    public bool KO { get; private set; }
    /// <summary>Permanently dead: fades away, then is removed (Gone).</summary>
    public bool Dead { get; private set; }
    public bool Gone { get; private set; }
    public Figure? KilledBy { get; private set; }
    float _koT, _fadeT;
    public float Fade => (Dead ? M.Clamp01(1 - (_fadeT - (Peaceful ? 5 : 2.5f)) / (Peaceful ? 2.5f : 1.2f)) : 1) * (1 - Camo * 0.8f) * (Spirit ? 0.55f : 1);
    /// <summary>A ghost: see-through.</summary>
    public bool Spirit;
    /// <summary>Died of old age, quietly.</summary>
    public bool Peaceful { get; private set; }

    /// <summary>Old age: lies down and is gone, gently (no "x_x", a slower fade).</summary>
    public void PassAway(World w)
    {
        if (Dead) return;
        Peaceful = true;
        KO = Dead = true;
        _koT = _fadeT = 0;
        KilledBy = null;
        _emote = null;
        Emote("…", 4);
        if (Riding != null) { Riding.Rider = null; Riding = null; }
        if (Mode != Mode.Ragdoll) GoRagdoll(Vector2.Zero);   // sinks down where they are
        World.Log($"{Name} passed away peacefully");
    }
    public AttackDef? Atk { get; private set; }
    public float AtkT;
    public bool LastAttackLanded;
    bool _atkHit;
    public float HitStun, BlockT;
    public bool Blocking => BlockT > 0;
    /// <summary>Who sent us flying (null = the user, or nobody).</summary>
    public Figure? KnockedBy;
    /// <summary>Who our strikes are allowed to hit (set by the brain).</summary>
    public Func<Figure, bool>? CanHit;
    /// <summary>When fighting the cursor: the point we're punching at.</summary>
    public Vector2? PunchTarget;
    float _bowlCd;

    public bool InWindup => Atk != null && AtkT < Atk.Windup;
    public bool AttackActive => Atk != null && AtkT >= Atk.Windup && AtkT < Atk.Windup + Atk.Active;

    public void StartAttack(AttackKind k)
    {
        if (Atk != null || HitStun > 0 || Mode != Mode.Control || !Grounded || k == AttackKind.FlyingKick) return;
        Atk = AttackDef.Get(k);
        AtkT = 0;
        _atkHit = false;
        LastAttackLanded = false;
        SetAction(Act.Fight);
        World.Play(Sfx.Whoosh, Jt[J.Neck], 0.25f + (Melee ? 0.15f : 0), Melee ? 0.8f : 1.25f, 0.08);
    }

    /// <summary>A punch thrown in mid-air (jumping at the cursor).</summary>
    public void StartAirAttack(AttackKind k)
    {
        if (Atk != null || Mode != Mode.Control || Grounded) return;
        Atk = AttackDef.Get(k);
        AtkT = 0;
        _atkHit = false;
        LastAttackLanded = false;
    }

    bool Striking(AttackDef a) => AtkT >= a.Windup && AtkT < a.Windup + a.Active + a.Recovery * 0.35f;

    /// <summary>Fighting the cursor: point the striking hand straight at it (wherever it is) instead of the
    /// move's usual fixed height. Facing space, relative to the neck.</summary>
    void AimAtCursor(ref Vector2 hN, ref Vector2 hF, ref float handW)
    {
        if (PunchTarget is not Vector2 pt || Atk is not AttackDef a || a.Foot || a.Kind == AttackKind.FlyingKick) return;
        Vector2 d = pt - Jt[J.Neck];
        Vector2 local = M.ClampLength(new Vector2(d.X * Facing, d.Y), Arm * 0.99f);
        if (AtkT < a.Windup) local = local * -0.15f + new Vector2(Arm * 0.2f, 0);   // cock the fist back
        else if (!Striking(a)) return;
        if (a.Kind is AttackKind.Cross or AttackKind.Uppercut or AttackKind.Haymaker) hF = local; else hN = local;
        handW = 60;
    }

    /// <summary>Kicking at the cursor: the foot goes for it (world space).</summary>
    Vector2? AimFootAtCursor(Vector2 pelvis)
    {
        if (PunchTarget is not Vector2 pt || Atk is not AttackDef a || !a.Foot || !Striking(a)) return null;
        return pelvis + M.ClampLength(pt - pelvis, Leg * 0.99f);
    }

    public void StartFlyingKick(Vector2 v)
    {
        if (!Grounded || JumpPending || Atk != null || HitStun > 0) return;
        RequestJump(v, 0.1f);
        Atk = AttackDef.Get(AttackKind.FlyingKick);
        AtkT = 0;
        _atkHit = false;
        LastAttackLanded = false;
    }

    public void CancelAttack() => EndAttack();

    /// <summary>Ducking under high attacks (boxers' favourite defence).</summary>
    public float DuckT;
    bool _spun;
    /// <summary>Mid spinning-back-kick: facing is temporarily turned away from the opponent.</summary>
    public bool Spinning => _spun;

    void EndAttack()
    {
        Atk = null;
        if (_spun) { Facing = -Facing; _spun = false; }
    }

    void TickCombat(float dt)
    {
        Poise = MathF.Min(100, Poise + 15 * dt);
        if (!KO) HP = MathF.Min(100, HP + (Action == Act.Fight ? 0.8f : 4f) * dt);
        HitStun = MathF.Max(0, HitStun - dt);
        BlockT = MathF.Max(0, BlockT - dt);
        DuckT = MathF.Max(0, DuckT - dt);
        _bowlCd -= dt;
        if (Atk == null) return;
        if (Atk.Kind == AttackKind.FlyingKick)
        {
            if (!JumpPending) AtkT += dt;
            if ((Grounded && !JumpPending && AtkT > 0.1f) || Mode != Mode.Control) EndAttack();
        }
        else
        {
            AtkT += dt;
            // Spinning back kick: turn our back to them partway through the wind-up.
            if (Atk.Kind == AttackKind.SpinKick && !_spun && AtkT >= Atk.Windup * 0.45f) { Facing = -Facing; _spun = true; }
            if (AtkT >= Atk.Total || Mode != Mode.Control) EndAttack();
        }
    }

    Vector2 StrikePoint(AttackDef a) =>
        a.Foot ? Jt[J.FootN] : a.Kind is AttackKind.Cross or AttackKind.Uppercut or AttackKind.Haymaker ? Jt[J.HandF] : Jt[J.HandN];

    /// <summary>During an attack's active frames, see whether the striking hand/foot connects.</summary>
    void CheckStrike(World w)
    {
        if (Atk is not AttackDef a || _atkHit || Mode != Mode.Control) return;
        bool active = a.Kind == AttackKind.FlyingKick ? !Grounded && !JumpPending && AtkT > 0.04f : AttackActive;
        if (!active) return;
        bool swing = Melee && !a.Foot && a.Kind != AttackKind.FlyingKick;
        Vector2 p = swing ? WeaponTip() : StrikePoint(a);
        var (dmgMul, knockMul, poiseMul) = swing ? (Weapon!.Def.Damage, Weapon.Def.Knock, 1.3f) : a.Foot ? (1f, 1f, 1f) : GearInfo.Punch(Gear);
        Vector2 knock = new Vector2(a.Knock.X * Facing, a.Knock.Y) * S * w.Fight.Strength * knockMul;

        // The cursor counts as hit anywhere along the striking forearm or shin, not just at the tip.
        bool cursorHit = false;
        if (PunchTarget is Vector2 cur0)
        {
            int tip = a.Foot ? J.FootN : a.Kind is AttackKind.Cross or AttackKind.Uppercut or AttackKind.Haymaker ? J.HandF : J.HandN;
            int mid = a.Foot ? J.KneeN : tip == J.HandF ? J.ElbowF : J.ElbowN;
            cursorHit = Vector2.Distance(p, cur0) < 14 * S || M.DistToSegment(cur0, swing ? Jt[J.HandN] : Jt[mid], swing ? p : Jt[tip]) < 8 * S;
        }
        if (PunchTarget is Vector2 cur && cursorHit)
        {
            _atkHit = LastAttackLanded = true;
            World.Log($"{Name} {a.Kind} hit the cursor");
            World.Play(Sfx.Bonk, cur, 0.6f);
            w.Fx.Spark(cur, S, w.Rng, 0.8f + a.Damage * 0.03f, new Color4(1, 1, 1, 1));
            w.HitStop = MathF.Max(w.HitStop, 0.04f);
            w.CursorPush += Vector2.Normalize(knock) * (25 + a.Damage * 3.5f) * S;
            return;
        }
        foreach (var o in w.Figures)
        {
            if (o == this || o.Mode == Mode.Spawning || (CanHit != null && !CanHit(o))) continue;
            if (o.DuckT > 0 && a.Height == HitHeight.High) continue;   // it sails over their head
            // A swung weapon connects anywhere along its length (sampled), a fist or foot at its tip.
            bool touch = o.BodyDistance(p) <= LineW * 0.5f + 3 * S ||
                         (swing && (o.BodyDistance(Vector2.Lerp(Jt[J.HandN], p, 0.6f)) <= LineW * 0.5f + 3 * S || o.BodyDistance(Vector2.Lerp(Jt[J.HandN], p, 0.3f)) <= LineW * 0.5f + 3 * S));
            if (!touch) continue;
            _atkHit = LastAttackLanded = true;
            // Always knock the victim away from us (a spinning kick faces the other way).
            float dir = MathF.Sign(o.Jt[J.Pelvis].X - Jt[J.Pelvis].X);
            if (dir == 0) dir = Facing;
            o.ReceiveAttack(a, this, new Vector2(MathF.Abs(knock.X) * dir, knock.Y), p, w, dmgMul, poiseMul);
            return;
        }
    }

    /// <summary>Distance from a point to the drawn body's surface.</summary>
    public float BodyDistance(Vector2 p)
    {
        float best = Vector2.Distance(p, Jt[J.Head]) - HeadR;
        foreach (var (a, b) in Bones) best = MathF.Min(best, M.DistToSegment(p, Jt[a], Jt[b]) - LineW * 0.5f);
        return best;
    }

    public void ReceiveAttack(AttackDef a, Figure from, Vector2 knock, Vector2 at, World w, float dmgMul = 1, float poiseMul = 1)
    {
        float mult = Brain.IsSparringWith(from) ? 0.5f : 1;
        float str = w.Fight.Strength * dmgMul;
        Color4? sparkColor = from.Gear == Gear.BrassKnuckles && !a.Foot ? new Color4(1, 0.95f, 0.6f, 1) : null;
        if (Mode == Mode.Ragdoll)
        {
            // Juggled while already flying.
            if (KO) return;
            Rag.PushAll(knock * 0.5f);
            w.Fx.Spark(at, S, w.Rng, 0.7f, sparkColor);
            return;
        }
        if (Mode != Mode.Control && Mode != Mode.GetUp) return;
        float dx = from.Base.X - Base.X;
        bool facing = MathF.Abs(dx) < 2 * S || MathF.Sign(dx) == Facing;
        bool blocked = Blocking && facing && a.Height != HitHeight.Low && Grounded;
        EndAttack();
        DuckT = 0;
        if (blocked)
        {
            World.Play(Sfx.Block, at, 0.6f);
            HP -= a.Damage * 0.15f * str * mult;
            Poise -= a.Poise * 0.3f;
            knock *= 0.35f;
            w.Fx.Spark(at, S, w.Rng, 0.6f, new Color4(0.6f, 0.85f, 1, 1));
            w.HitStop = MathF.Max(w.HitStop, 0.025f);
        }
        else
        {
            World.Play(a.Foot ? Sfx.Kick : Sfx.Punch, at, M.Clamp01(0.4f + a.Damage * dmgMul * 0.04f), a.Foot ? 0.9f : 1);
            HP -= a.Damage * str * mult * (0.85f + from.Brain.Sk(SkillKind.Fighting) * 0.3f);
            Poise -= a.Poise * (0.7f + 0.3f * w.Fight.Strength) * mult * poiseMul / Style.Toughness;
            w.Fx.Spark(at, S, w.Rng, 0.9f + a.Damage * dmgMul * 0.04f, sparkColor);
            w.HitStop = MathF.Max(w.HitStop, 0.035f + a.Damage * dmgMul * 0.0025f);
        }
        bool heavy = a.Kind is AttackKind.Uppercut or AttackKind.Roundhouse;
        bool down = !blocked && (a.Knockdown || Poise <= 0 || HP <= 0 || !Grounded || Climbing || (heavy && HP < 60));
        World.Log($"{from.Name} {a.Kind} -> {Name}: {(blocked ? "blocked" : "hit")} hp={HP:F0} poise={Poise:F0}{(down ? " DOWN" : "")}");
        if (down)
        {
            Poise = 100;
            KnockedBy = from;
            Brain.OnStruck(from, false, true, w);   // before the ragdoll, so the brain still knows what fight this was
            if (!Brain.IsSparringWith(from)) w.Witness(from, this, SocialAct.Hurt, 1);
            GoRagdoll(knock * (HP <= 0 ? 1.3f : 1));
            if (HP <= 0) OutOfHealth(from, w);
            return;
        }
        HitStun = blocked ? 0.12f : 0.26f;
        Vel.X += knock.X * (blocked ? 0.5f : 0.7f);
        _leanV -= Facing * (blocked ? 2 : 5);
        _hipV -= 6 * S;
        if (!blocked && !Brain.IsSparringWith(from)) w.Witness(from, this, SocialAct.Hurt, 0.35f);
        Brain.OnStruck(from, blocked, false, w);
    }

    /// <summary>A flying ragdoll knocks over anyone it slams into.</summary>
    void RagdollBowling(World w)
    {
        if (_bowlCd > 0) return;
        ReadOnlySpan<int> joints = stackalloc[] { J.Head, J.Pelvis, J.FootN, J.FootF, J.HandN, J.HandF };
        foreach (var o in w.Figures)
        {
            if (o == this || o.Mode is not (Mode.Control or Mode.GetUp)) continue;
            foreach (int j in joints)
            {
                Vector2 v = JVel[j];
                if (v.LengthSquared() < 900 * S * 900 * S || o.BodyDistance(Jt[j]) > LineW + 2 * S) continue;
                if (v.Length() > 1100 * S)
                {
                    // A whole flying body takes them down with it.
                    World.Log($"{o.Name} bowled over by flying {Name}");
                    o.KnockedBy = KnockedBy;
                    o.Brain.OnHit(KnockedBy, true, w);
                    o.GoRagdoll(v * 0.5f);
                }
                else o.TakeHit(v * 0.6f, KnockedBy, w);
                Rag.Push(j, -v * 0.4f);
                w.Fx.Spark(Jt[j], S, w.Rng, 0.8f, null);
                _bowlCd = 0.3f;
                return;
            }
        }
    }

    // ---------------- knock-outs and death ----------------

    bool _ghosted;

    /// <summary>Debug: drop to zero health right now.</summary>
    public void DebugKnockOut(World w)
    {
        HP = 0;
        if (Mode != Mode.Ragdoll) GoRagdoll(new Vector2(-Facing * 300 * S, -300 * S));
        OutOfHealth(this, w);
    }

    void OutOfHealth(Figure from, World w)
    {
        if (w.Fight.OnZeroHealth != DeathRule.KnockdownOnly) Brain.DiaryKnockedOut(from == this ? null : from);
        switch (w.Fight.OnZeroHealth)
        {
            case DeathRule.KnockdownOnly:
                HP = 15;
                return;
            case DeathRule.KnockOut:
                KO = true;
                break;
            case DeathRule.Permanent:
                KO = Dead = true;
                break;
        }
        _koT = _fadeT = 0;
        KilledBy = from;
        Emote("x_x", 9999);
        w.Fx.Spark(Jt[J.Head], S, w.Rng, 1.6f, new Color4(1, 1, 1, 1));
        w.HitStop = MathF.Max(w.HitStop, 0.12f);
        World.Log($"{Name} {(Dead ? "killed" : "knocked out")} by {from.Name}");
    }

    /// <summary>Wake up from a knock-out (by a friend, or on our own when the timer runs out).</summary>
    public void Revive(Figure? by)
    {
        if (!KO || Dead) return;
        KO = false;
        HP = by != null ? 50 : 30;
        _emote = null;
        Emote(by != null ? "♥" : "…", 1.5f);
        _restT = 0;
        _ragT = 0;
    }

    void TickKO(float dt, World w)
    {
        if (!KO) return;
        _koT += dt;
        if (Dead)
        {
            _fadeT += dt;
            if (_fadeT > (Peaceful ? 5.5f : 2.6f) && !_ghosted) { w.Fx.Ghost(Jt[J.Pelvis], Color, S); _ghosted = true; }
            if (_fadeT > (Peaceful ? 7.6f : 3.8f)) Gone = true;
        }
        else if (_koT > w.Fight.ReviveSeconds || w.Fight.OnZeroHealth == DeathRule.KnockdownOnly) Revive(null);
    }

    // ---------------- drawing extras ----------------

    static readonly Color4 GloveRed = M.Hex(0xC62828), GloveWhite = M.Hex(0xF2F2F2), Brass = M.Hex(0xE0B040);

    void DrawGear(Renderer r, int elbow, int hand, float shade)
    {
        if (Gear == Gear.None) return;
        Vector2 h = Jt[hand], d = h - Jt[elbow];
        d = d.LengthSquared() > 1e-4f ? Vector2.Normalize(d) : new Vector2(Facing, 0);
        float a = Fade;
        if (Gear == Gear.BoxingGloves)
        {
            var c = FightSettings.Team(Color) == "Red" ? GloveWhite : GloveRed;
            r.Disc(h + d * 0.6f * S, 3.5f * S, new Color4(0, 0, 0, 0.3f * a));
            r.Disc(h + d * 0.6f * S, 2.9f * S, new Color4(c.R * shade, c.G * shade, c.B * shade, a));
        }
        else
        {
            Vector2 perp = new(-d.Y, d.X), c = h + d * 0.8f * S;
            r.Line(c - perp * 2 * S, c + perp * 2 * S, new Color4(0, 0, 0, 0.35f * a), 2.3f * S);
            r.Line(c - perp * 1.8f * S, c + perp * 1.8f * S, new Color4(Brass.R * shade, Brass.G * shade, Brass.B * shade, a), 1.5f * S);
        }
    }

    void DrawHealthBar(Renderer r)
    {
        var rules = World.Current.Fight;
        if (!rules.HealthBars || Mode == Mode.Spawning || Dead || !(Brain.InFight || HP < 97 || KO)) return;
        Vector2 c = Jt[J.Head] - new Vector2(0, HeadR + 4.5f * S);
        float w = 18 * S, h = 1.8f * S, k = M.Clamp01(HP / 100);
        // A little pencil-outlined bar on a card-coloured track, like the Studio's sliders.
        r.Line(c - new Vector2(w / 2, 0), c + new Vector2(w / 2, 0), Ui.Ink.A(0.75f), h + 1.4f * S);
        r.Line(c - new Vector2(w / 2, 0), c + new Vector2(w / 2, 0), Ui.Fill, h);
        if (k > 0)
            r.Line(c - new Vector2(w / 2, 0), c + new Vector2(-w / 2 + w * k, 0), Color4.Lerp(Ui.Accent, Ui.Good, k), h);
    }

    // ---------------- poses ----------------

    void FightPose(ref float hipT, ref float leanT, ref float handW, ref Vector2 hN, ref Vector2 hF,
                   ref Vector2 eN, ref Vector2 eF, ref float tiltT)
    {
        var style = Style.Fight;
        float gb = Style.GuardBounce;
        float bounce = MathF.Sin(_time * (7 + 3 * gb) + Id);
        // Each fighting style has its own guard.
        (Vector2 guardN, Vector2 guardF, float guardLean, float guardHip) = style switch
        {
            FightStyle.Boxer => (new Vector2(Arm * 0.42f, -Arm * 0.3f), new Vector2(Arm * 0.3f, -Arm * 0.22f), 0.15f, 0.86f),
            FightStyle.Kicker => (new Vector2(Arm * 0.55f, -Arm * 0.05f), new Vector2(Arm * 0.15f, Torso * 0.1f), -0.02f, 0.92f),
            FightStyle.Brawler => (new Vector2(Arm * 0.5f, Torso * 0.15f), new Vector2(Arm * 0.35f, Torso * 0.25f), 0.18f, 0.88f),
            FightStyle.Acrobat => (new Vector2(Arm * 0.6f, -Arm * 0.2f), new Vector2(-Arm * 0.45f, -Arm * 0.1f), 0.05f, 0.9f),
            FightStyle.Turtle => (new Vector2(Arm * 0.3f, -Arm * 0.38f), new Vector2(Arm * 0.25f, -Arm * 0.3f), 0.2f, 0.84f),
            _ => (new Vector2(Arm * 0.48f, -Arm * 0.12f), new Vector2(Arm * 0.32f, Arm * 0.02f), 0.1f, 0.9f),
        };
        hipT = StandHip * (guardHip + 0.022f * gb * bounce);
        leanT = guardLean;
        hN = guardN;
        hF = guardF;
        eN = eF = style == FightStyle.Brawler ? new(-1, 0.6f) : new(-0.2f, 1);
        handW = 30;
        if (DuckT > 0)
        {
            // Get right down: knees deep, chest over them, head tucked.
            hipT = StandHip * 0.48f;
            leanT = 0.62f;
            tiltT += 0.3f * Facing;
            hN = new(Arm * 0.3f, -Arm * 0.32f);
            hF = new(Arm * 0.25f, -Arm * 0.25f);
            return;
        }
        if (HitStun > 0)
        {
            leanT = -0.35f;
            hN = new(Arm * 0.1f, Arm * 0.75f);
            hF = new(-Arm * 0.2f, Arm * 0.7f);
            tiltT = -0.35f * Facing;
            handW = 20;
            return;
        }
        if (Blocking)
        {
            hN = new(Arm * 0.38f, -Arm * 0.42f);
            hF = new(Arm * 0.32f, -Arm * 0.25f);
            leanT = -0.08f;
            eN = eF = new(0.3f, 1);
        }
        if (Atk is not AttackDef a || a.Kind == AttackKind.FlyingKick) return;
        float t = AtkT, wu = a.Windup, act = a.Active;
        bool striking = t >= wu && t < wu + act + 0.04f;
        float rec = M.Clamp01((t - wu - act) / a.Recovery);
        handW = 55;
        switch (a.Kind)
        {
            case AttackKind.Jab:
            {
                Vector2 hit = new(Arm, -Arm * 0.1f);
                hN = t < wu ? new(Arm * 0.3f, -Arm * 0.08f) : striking ? hit : Vector2.Lerp(hit, guardN, rec);
                leanT = striking ? 0.16f : 0.1f;
                break;
            }
            case AttackKind.Cross:
            {
                Vector2 hit = new(Arm * 1.02f, -Arm * 0.08f);
                hF = t < wu ? new(Arm * 0.12f, 0) : striking ? hit : Vector2.Lerp(hit, guardF, rec);
                hN = new(Arm * 0.35f, -Arm * 0.2f);
                leanT = striking ? 0.3f : 0.05f;
                break;
            }
            case AttackKind.Uppercut:
            {
                Vector2 hit = new(Arm * 0.55f, -Arm * 0.88f);
                hF = t < wu ? new(Arm * 0.3f, Arm * 0.55f) : striking ? hit : Vector2.Lerp(hit, guardF, rec);
                hipT = t < wu ? StandHip * 0.78f : StandHip;
                leanT = t < wu ? 0.25f : -0.05f;
                break;
            }
            case AttackKind.FrontKick:
                leanT = t < wu ? 0 : -0.18f;
                break;
            case AttackKind.Roundhouse:
                leanT = t < wu ? -0.1f : -0.42f;
                hN = new(Arm * 0.2f, -Arm * 0.3f);
                hF = new(-Arm * 0.5f, -Arm * 0.1f);
                break;
            case AttackKind.Sweep:
                hipT = StandHip * 0.48f;
                leanT = 0.4f;
                hN = new(Arm * 0.35f, Arm * 0.9f);
                hF = new(-Arm * 0.2f, Arm * 0.85f);
                break;
            case AttackKind.Haymaker:
            {
                // Big wind-up behind the head, then a looping swing.
                Vector2 hit = new(Arm * 1.02f, -Arm * 0.05f);
                hF = t < wu ? Vector2.Lerp(guardF, new Vector2(-Arm * 0.75f, -Arm * 0.55f), M.Smooth(t / wu))
                   : striking ? hit : Vector2.Lerp(hit, guardF, rec);
                hN = new(Arm * 0.3f, Torso * 0.2f);
                leanT = t < wu ? -0.22f : striking ? 0.38f : 0.15f;
                eF = new(-0.3f, -1);
                handW = t < wu ? 18 : 60;
                break;
            }
            case AttackKind.SpinKick:
                // Turn away, then drive the heel backwards into them.
                leanT = t < wu * 0.45f ? 0.1f : 0.42f;
                hN = new(Arm * 0.35f, Torso * 0.1f);
                hF = new(Arm * 0.2f, -Arm * 0.1f);
                tiltT -= 0.35f * Facing;   // looking back over the shoulder
                break;
        }
    }

    bool AttackUsesFoot => Atk is { Foot: true } a && a.Kind != AttackKind.FlyingKick && Grounded;

    /// <summary>Kicking foot path (relative to the pelvis, facing space) for grounded kicks.</summary>
    Vector2 AttackFootLocal(Vector2 planted)
    {
        var a = Atk!;
        float t = AtkT, wu = a.Windup, act = a.Active;
        (Vector2 cock, Vector2 hit) = a.Kind switch
        {
            AttackKind.Roundhouse => (new Vector2(Leg * 0.3f, Leg * 0.3f), new Vector2(Leg * 0.72f, -Leg * 0.62f)),
            AttackKind.Sweep => (new Vector2(-Leg * 0.15f, Leg * 0.48f), new Vector2(Leg * 0.95f, StandHip * 0.48f)),
            AttackKind.SpinKick => (new Vector2(Leg * 0.2f, Leg * 0.4f), new Vector2(-Leg * 0.98f, Leg * 0.38f)),
            _ => (new Vector2(Leg * 0.38f, Leg * 0.42f), new Vector2(Leg * 0.98f, Leg * 0.4f)),
        };
        if (t < wu) return Vector2.Lerp(planted, cock, M.Smooth(t / wu));
        if (t < wu + act) return Vector2.Lerp(cock, hit, M.Smooth((t - wu) / (act * 0.8f)));
        return Vector2.Lerp(hit, planted, M.Smooth((t - wu - act) / a.Recovery));
    }

    void FlyingKickPose(ref Vector2 hN, ref Vector2 hF, ref Vector2 fN, ref Vector2 fF, ref float leanT, ref float footW, ref float handW)
    {
        fN = new(Leg * 0.98f, Leg * 0.32f);
        fF = new(-2 * S, Leg * 0.55f);
        hN = new(-Arm * 0.4f, -Arm * 0.3f);
        hF = new(-Arm * 0.6f, Arm * 0.1f);
        leanT = -0.2f;
        footW = 40;
        handW = 30;
    }
}
