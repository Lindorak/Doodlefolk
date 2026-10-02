using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum Mode { Spawning, Control, Ragdoll, GetUp }
enum Act { Stand, SitEdge, SitFloor, Lie, HandsHips, Wave, Swat, Cheer, Kick, Tap, Throw, Talk, HighFive, Ready, Fight, Fidget, SitFront, SitBack, Curl, Eat, Read, Warm }

/// <summary>Joint indices. N = near side (drawn in front), F = far side (drawn behind, slightly darker).</summary>
static class J
{
    public const int Head = 0, Neck = 1, Pelvis = 2, ElbowN = 3, HandN = 4, ElbowF = 5, HandF = 6,
        KneeN = 7, FootN = 8, KneeF = 9, FootF = 10, Count = 11;
}

sealed class Foot
{
    public Vector2 Pos, From, To;
    public bool Stepping;
    public float T, Dur, Lift;
}

/// <summary>Six 0..1 traits that weight every decision a figure makes. Editable live.</summary>
sealed class Personality
{
    public float Energy { get; set; }
    public float Curiosity { get; set; }
    public float Bravery { get; set; }
    public float Playfulness { get; set; }
    public float Aggression { get; set; }
    public float Sociability { get; set; }

    public static Personality Random(Random r) => new()
    {
        Energy = r.Range(0.25f, 0.9f),
        Curiosity = r.Range(0.3f, 0.95f),
        Bravery = r.Range(0.2f, 0.9f),
        Playfulness = r.Range(0.2f, 0.9f),
        Aggression = r.Range(0.1f, 0.8f),
        Sociability = r.Range(0.3f, 0.9f),
    };

    public Personality Clone() => (Personality)MemberwiseClone();

    public void CopyFrom(Personality p)
    {
        Energy = p.Energy; Curiosity = p.Curiosity; Bravery = p.Bravery;
        Playfulness = p.Playfulness; Aggression = p.Aggression; Sociability = p.Sociability;
    }

    static Personality P(float e, float c, float b, float p, float a, float s) =>
        new() { Energy = e, Curiosity = c, Bravery = b, Playfulness = p, Aggression = a, Sociability = s };

    public static readonly (string Name, string Blurb, Personality Traits)[] Presets =
    {
        ("Balanced", "A bit of everything.", P(0.55f, 0.55f, 0.55f, 0.55f, 0.35f, 0.55f)),
        ("Explorer", "Always climbing to the next window.", P(0.75f, 0.95f, 0.7f, 0.45f, 0.25f, 0.4f)),
        ("Couch potato", "Sits, naps, sits again.", P(0.12f, 0.25f, 0.45f, 0.3f, 0.2f, 0.45f)),
        ("Hyperactive", "Runs everywhere, never stops.", P(0.98f, 0.75f, 0.65f, 0.85f, 0.35f, 0.6f)),
        ("Scaredy-cat", "Jumps at everything, hates the cursor.", P(0.5f, 0.4f, 0.08f, 0.35f, 0.1f, 0.5f)),
        ("Hothead", "Swats first, asks later.", P(0.7f, 0.5f, 0.9f, 0.4f, 0.95f, 0.3f)),
        ("Social butterfly", "Has to say hi to everyone.", P(0.65f, 0.55f, 0.6f, 0.65f, 0.15f, 0.98f)),
        ("Show-off", "Tricks, flips and ball skills.", P(0.85f, 0.6f, 0.8f, 0.98f, 0.4f, 0.7f)),
        ("Loner", "Prefers its own window.", P(0.45f, 0.65f, 0.55f, 0.3f, 0.35f, 0.08f)),
    };

    public string Describe()
    {
        var t = new List<string>();
        if (Energy > 0.7f) t.Add("energetic"); else if (Energy < 0.35f) t.Add("lazy");
        if (Curiosity > 0.75f) t.Add("curious");
        if (Bravery > 0.75f) t.Add("bold"); else if (Bravery < 0.3f) t.Add("timid");
        if (Playfulness > 0.75f) t.Add("playful");
        if (Aggression > 0.65f) t.Add("feisty");
        if (Sociability > 0.75f) t.Add("friendly"); else if (Sociability < 0.3f) t.Add("aloof");
        return t.Count == 0 ? "easygoing" : string.Join(", ", t.Take(3));
    }
}

/// <summary>A little symbol bubble above a figure's head ("!", "?", "♥", "z"...).</summary>
struct Emote
{
    public string Text;
    public float T, Dur;
    /// <summary>A special colour for the text; null: the theme's ink.</summary>
    public Color4? Ink;
}

/// <summary>A stick figure. Normally driven procedurally (Control); switches to a verlet ragdoll
/// when grabbed, flung or after a big fall, then blends back to its feet (GetUp).</summary>
sealed partial class Figure
{
    static int _nextId;
    public readonly int Id;
    /// <summary>Size relative to the default figure for this screen.</summary>
    public float SizeMul = 1;
    public string Name;
    public Color4 Color;
    public readonly float S;
    public readonly Personality Traits;
    /// <summary>Likes and dislikes (Sims-style), editable.</summary>
    public Tastes Tastes;
    public string Team => FightSettings.Team(Color);
    /// <summary>Hunts your cursor relentlessly and never forgives you.</summary>
    public bool Hunter;
    /// <summary>An object in hand (food, a book).</summary>
    public Item? CarryingItem;

    /// <summary>Sit/lie on a surface of an object (or anything): stand right there, grounded on it.</summary>
    public void Mount(Vector2 at, IntPtr hwnd)
    {
        Base = at;
        Grounded = true;
        GroundHwnd = hwnd;
        Vel = default;
        _jumpVel = null;
        Flailing = false;
        KeepFacing = false;
        _fN.Pos = new(at.X + 3 * S, at.Y); _fN.Stepping = false;
        _fF.Pos = new(at.X - 3 * S, at.Y); _fF.Stepping = false;
    }

    /// <summary>Get down off an object (seat, bed, table): a little hop past its nearer end.</summary>
    public void HopOff()
    {
        var sup = World.Current.Env.SupportAt(Base.X, Base.Y, GroundHwnd);
        if (sup == null) { Grounded = false; return; }
        float l = Base.X - sup.X1, r = sup.X2 - Base.X;
        float dir = l < r ? -1 : 1;
        float target = (dir < 0 ? sup.X1 : sup.X2) + dir * 12 * S;
        float t = 0.42f;
        Grounded = false;
        Vel = new Vector2((target - Base.X) / t, -260 * S);
        if (!KeepFacing) Facing = (int)dir;
        SetAction(Act.Stand);
    }
    public readonly Brain Brain;
    public readonly Ragdoll Rag;

    public readonly float HeadR, NeckGap, Torso, UpperArm, ForeArm, Thigh, Shin, LineW, Gravity;
    public float WalkSpeed => (55 + 30 * Traits.Energy) * S * Style.SpeedMul * MoodSpeed;
    public float RunSpeed => (190 + 70 * Traits.Energy) * S * (Style.Run == RunStyle.Jogger ? 0.8f : Style.Run == RunStyle.Tippy ? 0.85f : 1) * MoodSpeed;
    public float Leg => Thigh + Shin;
    public float Arm => UpperArm + ForeArm;
    public float StandHip => Leg * 0.95f;
    public float Height => StandHip + Torso + NeckGap + HeadR * 2;

    public Mode Mode = Mode.Spawning;
    public float SpawnT;
    public Vector2 Base, Vel;          // Base = point on the ground between the feet
    public bool Grounded;
    public IntPtr GroundHwnd;
    public int Facing = 1;
    public float DesiredVX;
    public bool AllowWalkOff, KeepFacing;
    public Act Action { get; private set; } = Act.Stand;
    public float ActionT;
    public Vector2? LookAt;
    public bool HeadShake, Flailing;
    public float LastThrowSpeed;

    public bool Held => Rag.Pin >= 0;
    public bool JumpPending => _jumpVel.HasValue;

    public readonly Vector2[] Jt = new Vector2[J.Count];
    readonly Vector2[] _jtPrev = new Vector2[J.Count];
    public readonly Vector2[] JVel = new Vector2[J.Count];

    readonly Random _rng;
    Vector2? _jumpVel;
    float _crouchT;
    Vector2 _carryVel;
    float _time, _restT, _ragT, _getUpT;
    /// <summary>Seconds this figure has been simulated (this session).</summary>
    public float Age => _time;
    readonly Vector2[] _ragSnap = new Vector2[J.Count];

    /// <summary>Girl, boy or nonbinary, and who they can fall for (both editable in the Studio).</summary>
    public Gender Gender;
    public Attraction Attraction;
    /// <summary>Pink cheeks near a crush (0..1), set by the brain.</summary>
    public float Blush;
    /// <summary>Holding hands this frame: where the near / far hand should reach (set by the brain, cleared after posing).</summary>
    public Vector2? HoldN, HoldF;
    /// <summary>A hat worn for the day (party hat, Halloween costume) instead of its usual one.</summary>
    public string? HatOverride, HatColourOverride;
    /// <summary>Hide-and-seek: crouched behind something (drawn behind it), or blending into the window (faint).</summary>
    public bool HidingBehind;
    /// <summary>Club colours: worn as a bandana.</summary>
    public Color4? ClubColour;
    /// <summary>0 lean … 0.3 fit … 0.5 chubby … 0.75 fat … 1 obese. Changes with eating and exercise (a setting).</summary>
    public float Weight = 0.2f;
    /// <summary>Lighting this frame: how warmly a nearby fire lights it, and how much the night dims it.</summary>
    public float Warmth, NightDim;

    /// <summary>A colour as the light falls on it (night-time dimming, firelight).</summary>
    public Color4 Lit(Color4 c)
    {
        if (NightDim > 0.005f) c = new Color4(c.R + (0.22f - c.R) * NightDim, c.G + (0.25f - c.G) * NightDim, c.B + (0.38f - c.B) * NightDim, c.A);
        if (Warmth > 0.005f) { float k = Warmth * 0.32f; c = new Color4(c.R + (1 - c.R) * k, c.G + (0.62f - c.G) * k, c.B + (0.3f - c.B) * k, c.A); }
        return c;
    }
    /// <summary>How much heavier than fit (0 at fit or lighter).</summary>
    public float Fat => World.WeightOn && Weight > 0.27f ? MathF.Pow((Weight - 0.27f) / 0.73f, 0.8f) : 0;
    public string WeightWord => Weight < 0.1f ? "Skinny" : Weight < 0.3f ? "Fit" : Weight < 0.5f ? "Chubby" : Weight < 0.75f ? "Fat" : "Obese";
    public float Camo;

    public Figure(Color4 color, string name, float scale, Personality traits, Random rng, int? id = null)
    {
        Id = id ?? ++_nextId;
        (Gender, Attraction) = Romance.Roll(rng);
        Color = color;
        Name = name;
        S = scale;
        Traits = traits;
        HeadR = 6.5f * S; NeckGap = 1.2f * S; Torso = 21 * S;
        UpperArm = 11 * S; ForeArm = 11 * S; Thigh = 14 * S; Shin = 14 * S;
        LineW = 3.3f * S;
        Gravity = 2300 * S;
        Rag = new Ragdoll(this);
        Brain = new Brain(this, rng);
        _rng = rng;
        StyleChoice = new StyleChoice { Seed = rng.Next() };
        Tastes = Tastes.Generate(traits, rng);
        _time = rng.Range(0, 10);
        Facing = rng.Next(2) == 0 ? 1 : -1;
    }

    // ---------- render interpolation ----------
    // The simulation runs at a fixed 120 steps/s; frames are drawn between the last two steps so motion
    // stays even at any frame rate (otherwise some frames repeat a pose and others skip one).
    readonly Vector2[] _drawSave = new Vector2[J.Count];
    bool _interp;

    public void BeginInterp(float a)
    {
        Array.Copy(Jt, _drawSave, J.Count);
        _interp = true;
        if (a >= 1) return;
        for (int i = 0; i < J.Count; i++)
            if (Vector2.DistanceSquared(_jtPrev[i], Jt[i]) < Height * Height) Jt[i] = Vector2.Lerp(_jtPrev[i], Jt[i], a);
    }

    public void EndInterp()
    {
        if (!_interp) return;
        Array.Copy(_drawSave, Jt, J.Count);
        _interp = false;
    }

    public void PlaceAt(Platform p, float x)
    {
        Base = new(x, p.Y);
        Vel = default;
        Grounded = true;
        GroundHwnd = p.Hwnd;
        Mode = Mode.Spawning;
        SpawnT = 0;
        ResetPose();
        World.Play(Sfx.Scribble, Base - new Vector2(0, Height * 0.5f), 0.35f, 1, 0.1);
    }

    public void SetAction(Act a)
    {
        if (Action == a) return;
        Action = a;
        ActionT = 0;
    }

    /// <summary>Jump with launch velocity <paramref name="v"/>. <paramref name="styled"/>: an ordinary
    /// jump (getting somewhere), so flip-happy figures may throw in a flip.</summary>
    public void RequestJump(Vector2 v, float crouch = 0.13f, bool styled = false, bool flip = false, [System.Runtime.CompilerServices.CallerMemberName] string by = "")
    {
        if (!Grounded || JumpPending) return;
        if (World.TraceJumps) World.Log($"jump {Name}: v=({v.X:0},{v.Y:0}) facing {Facing} keep {KeepFacing} by {by}/{Brain.State} at ({Base.X:0},{Base.Y:0}) ground {(long)GroundHwnd}");
        _jumpVel = v;
        _crouchT = crouch;
        if (MathF.Abs(v.X) > 1 && !KeepFacing) Facing = MathF.Sign(v.X);
        SetAction(Act.Stand);
        if (flip || (styled && Style.Jump == JumpStyle.Flipper && v.Y < -500 * S && _rng.NextDouble() < 0.6))
        {
            _flipT = 0;
            _flipDur = 2 * -v.Y / Gravity * 0.8f;
        }
    }

    public void Step(float dt, World w)
    {
        _time += dt;
        TickMoves(dt);
        TickCombat(dt);
        TickWeapon(dt);
        Array.Copy(Jt, _jtPrev, J.Count);
        switch (Mode)
        {
            case Mode.Spawning:
                SpawnT += dt / 1.3f;
                Pose(dt);
                if (SpawnT >= 1) { SpawnT = 1; Mode = Mode.Control; Brain.OnSpawned(); }
                break;

            case Mode.Control:
                Brain.Update(dt, w);
                ActionT += dt;
                Control(dt, w);
                if (Mode == Mode.Control) { Pose(dt); CheckStrike(w); }
                break;

            case Mode.GetUp:
                _getUpT += dt;
                DesiredVX = 0;
                Control(dt, w);
                if (Mode != Mode.GetUp) break;
                Pose(dt);
                float k = M.Smooth(_getUpT / 0.4f);
                for (int i = 0; i < J.Count; i++) Jt[i] = Vector2.Lerp(_ragSnap[i], Jt[i], k);
                if (_getUpT > 0.85f)
                {
                    Mode = Mode.Control;
                    Brain.OnRecovered(LastThrowSpeed, w);
                    LastThrowSpeed = 0;
                }
                break;

            case Mode.Ragdoll:
                _ragT += dt;
                Rag.Step(dt, w);
                Array.Copy(Rag.P, Jt, J.Count);
                if (_ragT > 0.05f) RagdollBowling(w);
                if (!Held && Rag.Contact && Rag.MaxSpeed(dt) < 35 * S) _restT += dt; else _restT = 0;
                TickKO(dt, w);
                if (_gp != GrapplePhase.None) GrappleControl(dt, w);
                if (!Held && !KO && Rag.Contact && (_restT > 0.4f || _ragT > 6)) BeginGetUp(w);
                break;
        }
        StepRope(dt);
        if (Mode != Mode.Spawning) StepCloth(dt);
        float inv = 1 / dt;
        for (int i = 0; i < J.Count; i++) JVel[i] = (Jt[i] - _jtPrev[i]) * inv;
    }

    void Control(float dt, World w)
    {
        var env = w.Env;
        if (_gp != GrapplePhase.None) GrappleControl(dt, w);
        if (Climbing) { ClimbControl(dt, w); return; }
        if (Grounded)
        {
            if (_jumpVel is Vector2 jv)
            {
                DesiredVX = 0;
                _crouchT -= dt;
                if (_crouchT <= 0) { Launch(jv); return; }
            }

            // While reeling from a hit we slide with the knockback (and can be knocked off edges).
            bool reeling = HitStun > 0;
            float target = reeling ? 0 : Brain.DashVX != 0 ? Brain.DashVX : DesiredVX;
            float accel = reeling ? 900 * S : (MathF.Abs(target) > MathF.Abs(Vel.X) ? 1100 : 1600) * S;
            Vel.X = M.MoveTowards(Vel.X, target, accel * dt);
            Vel.Y = 0;
            float nx = Base.X + Vel.X * dt;

            var seg = env.SupportAt(Base.X, Base.Y, GroundHwnd);
            if (seg != null && !AllowWalkOff && !reeling)
            {
                float m = 3 * S;
                if (nx < seg.X1 + m && Vel.X < 0) { nx = MathF.Max(nx, MathF.Min(Base.X, seg.X1 + m)); Vel.X = 0; }
                if (nx > seg.X2 - m && Vel.X > 0) { nx = MathF.Min(nx, MathF.Max(Base.X, seg.X2 - m)); Vel.X = 0; }
            }
            var (L, R, _) = env.BoundsAt(nx);
            float wm = 6 * S;
            if (nx < L + wm) { nx = L + wm; Vel.X = MathF.Max(0, Vel.X); }
            if (nx > R - wm) { nx = R - wm; Vel.X = MathF.Min(0, Vel.X); }
            Base.X = nx;

            var sup = env.SupportAt(Base.X, Base.Y, GroundHwnd);
            if (sup == null)
            {
                // Was the whole window just closed or minimised under us?
                if (!AllowWalkOff && GroundHwnd != IntPtr.Zero && (long)GroundHwnd > 0 && !env.TryRect(GroundHwnd, out _)) Brain.OnFloorVanished();
                Fall(!AllowWalkOff);
                return;
            }
            GroundHwnd = sup.Hwnd;
            Base.Y = sup.Y;
            if (!KeepFacing && MathF.Abs(Vel.X) > 6 * S) Facing = MathF.Sign(Vel.X);
            UpdateFeet(dt);
        }
        else
        {
            Vel.Y = MathF.Min(Vel.Y + Gravity * dt, 3800 * S);
            float py = Base.Y;
            Base += Vel * dt;
            var (L, R, T) = env.BoundsAt(Base.X);
            float wm = 6 * S;
            if (Base.X < L + wm) { Base.X = L + wm; Vel.X = MathF.Abs(Vel.X) * 0.3f; }
            if (Base.X > R - wm) { Base.X = R - wm; Vel.X = -MathF.Abs(Vel.X) * 0.3f; }
            if (Base.Y - Height < T) { Base.Y = T + Height; Vel.Y = MathF.Max(0, Vel.Y); }
            if (!KeepFacing && MathF.Abs(Vel.X) > 30 * S) Facing = MathF.Sign(Vel.X);
            if (Vel.Y > 0 && env.FindLanding(Base.X, py, Base.Y) is { } p) Land(p, w);
        }
    }

    void Fall(bool unexpected)
    {
        StopClimb();
        Grounded = false;
        _jumpVel = null;
        Vel.Y = MathF.Max(0, _carryVel.Y);
        SetAction(Act.Stand);
        if (unexpected)
        {
            Flailing = true;
            Brain.OnUnexpectedFall();
        }
    }

    void Launch(Vector2 v)
    {
        World.Play(Sfx.Jump, Base, M.Clamp01(-v.Y / (900 * S)) * 0.5f, 1.1f / MathF.Sqrt(SizeMul));
        _jumpVel = null;
        Vel = v;
        Grounded = false;
        _hipV += 4 * S;
    }

    void Land(Platform p, World w)
    {
        float impact = Vel.Y;
        if (p.Bounce > 0 && impact > 700 * S)
        {
            // Boing.
            World.Play(Sfx.Boing, Base, M.Clamp01(impact / (1500 * S)), 1);
            Base.Y = p.Y;
            Vel.Y = -impact * p.Bounce;
            Vel.X *= 0.85f;
            w.Fx.Dust(Base, S, 3, 0.3f, w.Rng);
            return;
        }
        if (World.TraceJumps) World.Log($"land {Name}: at ({Base.X:0},{p.Y:0}) on {(long)p.Hwnd} [{p.X1:0}..{p.X2:0}]{(p.Seen != null ? " text" : "")}{(p.Item != null ? " item " + p.Item.Def.Key : "")}");
        Base.Y = p.Y;
        Grounded = true;
        GroundHwnd = p.Hwnd;
        Vel.Y = 0;
        Vel.X *= 0.3f;
        Flailing = false;
        KeepFacing = false;
        World.Play(Sfx.Land, Base, M.Clamp01(impact / (1400 * S)) * 0.8f, 1 / MathF.Sqrt(SizeMul));
        w.Fx.Dust(Base, S, (int)Math.Clamp(impact / (300 * S), 2, 10), impact / (1500 * S), w.Rng);
        if (impact > 3000 * S) { World.Log($"{Name} knocked down by landing {impact / S:F0}S/s"); GoRagdoll(Vector2.Zero); return; }
        _hipV -= impact * 0.06f;
        if (Style.Jump == JumpStyle.Superhero && impact > 1100 * S && !Flailing) _landPoseT = 0.5f;
        Squash(impact);
        Brain.OnLanded(impact);
    }

    void UpdateFeet(float dt)
    {
        float speed = MathF.Abs(Vel.X);
        float run = M.Clamp01((speed - 90 * S) / (120 * S));
        bool idle = speed < 8 * S;
        // Step rhythm and height come from this figure's walk/run style, bent by mood.
        var st = Style;
        var md = Mood;
        float cad = st.Cadence * (1 - md.Tired * 0.15f) * (1 + md.Happy * 0.08f), stride = st.Stride;
        float liftMul = st.FootLift * (1 - md.Tired * 0.35f);
        if (run > 0)
            switch (st.Run)
            {
                case RunStyle.Tippy: stride *= M.Lerp(1, 0.6f, run); cad *= M.Lerp(1, 1.5f, run); liftMul *= M.Lerp(1, 0.6f, run); break;
                case RunStyle.Jogger: liftMul *= M.Lerp(1, 0.8f, run); break;
                case RunStyle.Flailer: liftMul *= M.Lerp(1, 1.2f, run); break;
                case RunStyle.NinjaRun: stride *= M.Lerp(1, 1.15f, run); break;
                default: liftMul *= M.Lerp(1, 1.25f, run); break;
            }
        float dur = idle ? 0.15f : M.Lerp(0.25f, 0.15f, run) * stride / cad;
        float lift = idle ? 3 * S : M.Lerp(5 * S, 9 * S, run) * liftMul;
        // Plan steps for the speed we're heading to, so stopping doesn't leave a foot a full
        // running stride ahead of the body.
        float vPlan = MathF.Abs(DesiredVX) < MathF.Abs(Vel.X) ? DesiredVX : Vel.X;
        float lead = Math.Clamp(vPlan * dur * 1.5f, -Leg * 0.6f, Leg * 0.6f);
        float stance = Action == Act.Fight ? (st.Fight == FightStyle.Kicker ? 10 : 8) * S : idle ? (JumpPending ? 7f : 5.5f) * S : 0;
        float tN = Base.X + Facing * stance, tF = Base.X - Facing * stance;
        if (idle && Action != Act.Fight && !JumpPending)
        {
            // Standing around: feet staggered (one a little ahead), not side by side like a soldier.
            float k = ((Id * 7919) % 100) / 100f;
            tN = Base.X + Facing * (2.5f + 3.5f * k) * S;
            tF = Base.X - Facing * (6.5f - 2.5f * k) * S;
        }

        // Feet still in the air keep re-aiming: land half a step's travel ahead of where the body
        // will be when the swing finishes.
        float LeadAt(float t) => Math.Clamp(vPlan * dur * (1.5f - t), -Leg * 0.6f, Leg * 0.6f);
        if (_fN.Stepping && _fN.T < 0.75f) _fN.To.X = M.Lerp(_fN.To.X, tN + LeadAt(_fN.T), 0.2f);
        if (_fF.Stepping && _fF.T < 0.75f) _fF.To.X = M.Lerp(_fF.To.X, tF + LeadAt(_fF.T), 0.2f);

        bool landed = Advance(_fN, dt) | Advance(_fF, dt);
        if (landed && !idle) World.Play(Sfx.Step, Base, (run > 0.5f ? 0.35f : 0.18f) * (0.6f + SizeMul * 0.4f) * (1 + st.Stomp), 1.15f / MathF.Sqrt(SizeMul), 0.035);
        if (landed && !idle)
        {
            // Heavy-footed (or angry) walkers stomp: the body drops on each footfall.
            float stomp = st.Stomp + md.Angry * 0.6f;
            if (stomp > 0.15f) _hipV -= stomp * 7 * S;
        }
        if (_fN.Stepping && _fF.Stepping) return;
        if (_fN.Stepping || _fF.Stepping)
        {
            // Normally one foot at a time, but a planted foot left too far from the body (starting,
            // stopping, turning) takes a catch-up step once the other is past mid-swing; otherwise
            // the hips have to sink to keep both feet down.
            var moving = _fN.Stepping ? _fN : _fF;
            var planted = _fN.Stepping ? _fF : _fN;
            if (moving.T < 0.5f || MathF.Abs(planted.Pos.X - Base.X) < Leg * 0.45f) return;
        }

        float eN = _fN.Stepping ? 0 : MathF.Abs(_fN.Pos.X - (tN + lead));
        float eF = _fF.Stepping ? 0 : MathF.Abs(_fF.Pos.X - (tF + lead));
        float thr = idle ? 3.5f * S : 1.5f * S;
        if (MathF.Max(eN, eF) <= thr) return;
        var f = eN >= eF ? _fN : _fF;
        float tx = (f == _fN ? tN : tF) + lead;
        f.Stepping = true;
        f.T = 0;
        f.Dur = dur;
        f.Lift = lift;
        if (f == _fN && md.Hurt > 0.3f && !idle) { f.Dur *= 0.7f; f.Lift *= 0.5f; }   // limping on the hurt leg
        f.From = f.Pos;
        f.To = new(tx, Base.Y);
    }

    /// <summary>Advance a stepping foot; true on the frame it lands.</summary>
    bool Advance(Foot f, float dt)
    {
        if (!f.Stepping) { f.Pos.Y = Base.Y; return false; }
        f.T += dt / f.Dur;
        if (f.T < 1) return false;
        f.Stepping = false;
        f.Pos = new(f.To.X, Base.Y);
        return true;
    }

    Vector2 FootPos(Foot f)
    {
        if (!f.Stepping) return new(f.Pos.X, Base.Y);
        float t = M.Smooth(f.T);
        return new(M.Lerp(f.From.X, f.To.X, t), Base.Y - f.Lift * MathF.Sin(MathF.PI * f.T));
    }

    /// <summary>Called once per frame: ride along with the window we're standing on,
    /// and get launched or flung if it stops suddenly.</summary>
    public void ApplyCarry(Env env, float frameDt)
    {
        if (Mode == Mode.Ragdoll) { Rag.ApplyCarry(env); return; }
        if (Climbing)
        {
            Vector2 cd = env.Delta(_climbHwnd);
            Base += cd;
            ShiftClimb(cd);
            for (int i = 0; i < J.Count; i++) { Jt[i] += cd; _jtPrev[i] += cd; }
            return;
        }
        if (!Grounded) return;
        Vector2 d = env.Delta(GroundHwnd);
        Vector2 v = d / MathF.Max(frameDt, 1e-3f);
        Vector2 prev = _carryVel;
        _carryVel = Vector2.Lerp(_carryVel, v, 0.5f);
        if (Mode == Mode.Control && _carryVel.Length() > 350 * S && GroundHwnd != IntPtr.Zero && (long)GroundHwnd > 0) Brain.OnRiding(_carryVel);
        if (d != Vector2.Zero)
        {
            Base += d;
            foreach (var f in new[] { _fN, _fF }) { f.Pos += d; f.From += d; f.To += d; }
            for (int i = 0; i < J.Count; i++) { Jt[i] += d; _jtPrev[i] += d; }
        }
        if (Mode != Mode.Control) return;
        if (prev.Y < -800 * S && v.Y > prev.Y * 0.3f)
        {
            // Window was yanked upward and stopped: momentum carries us into the air.
            Grounded = false;
            Vel = prev;
            Flailing = true;
            _jumpVel = null;
            SetAction(Act.Stand);
            Brain.OnUnexpectedFall();
        }
        else if (MathF.Abs(prev.X) > 1500 * S && MathF.Abs(v.X) < MathF.Abs(prev.X) * 0.3f)
        {
            World.Log($"{Name} flung off a window that stopped suddenly");
            GoRagdoll(prev * 0.8f);
            Brain.OnShakenOff();
        }
    }

    public void GoRagdoll(Vector2 extraVel)
    {
        Mode = Mode.Ragdoll;
        StopClimb();
        CancelGrapple();
        Grounded = false;
        _jumpVel = null;
        _restT = 0;
        _ragT = 0;
        Flailing = false;
        KeepFacing = false;
        SetAction(Act.Stand);
        Rag.Init(Jt, JVel, extraVel);
        DropCarried(JVel[J.HandN] + extraVel);
        _flipT = -1;
        Brain.OnRagdoll();
    }

    public void DropCarried(Vector2 vel)
    {
        if (CarryingItem is { } ci)
        {
            CarryingItem = null;
            ci.Holder = null;
            ci.Open = false;
            ci.Vel = vel;
            ci.OnGround = false;
        }
        if (Carrying == null) return;
        Carrying.Release(vel);
        Carrying = null;
    }

    public void Grab(int joint)
    {
        if (Mode != Mode.Ragdoll) GoRagdoll(Vector2.Zero);
        Rag.Pin = joint;
        _restT = 0;
        Brain.OnGrabbed();
    }

    public void Release(Vector2 vel)
    {
        if (Rag.Pin < 0) return;
        Rag.Release(vel);
        LastThrowSpeed = vel.Length();
        _ragT = 0;
    }

    void BeginGetUp(World w)
    {
        var p = Rag.GroundPlatform(w.Env);
        if (p == null) { _restT = 0; return; }
        Array.Copy(Rag.P, _ragSnap, J.Count);
        Mode = Mode.GetUp;
        _getUpT = 0;
        Vector2 pel = Rag.P[J.Pelvis];
        Base = new(M.ClampIn(pel.X, p.X1 + 3 * S, p.X2 - 3 * S), p.Y);
        Vel = default;
        Grounded = true;
        GroundHwnd = p.Hwnd;
        Facing = Rag.P[J.Head].X >= pel.X ? 1 : -1;

        _hip = Math.Clamp(p.Y - pel.Y, 2 * S, StandHip); _hipV = 0;
        Vector2 spine = Rag.P[J.Neck] - pel;
        _lean = Math.Clamp(MathF.Atan2(spine.X, -spine.Y), -1.5f, 1.5f); _leanV = 0;
        _hN = Rag.P[J.HandN] - Rag.P[J.Neck]; _hNV = default;
        _hF = Rag.P[J.HandF] - Rag.P[J.Neck]; _hFV = default;
        _pdx = 0; _pdxV = 0;
        _fN.Pos = new(Base.X + Facing * 4 * S, p.Y); _fN.Stepping = false;
        _fF.Pos = new(Base.X - Facing * 3 * S, p.Y); _fF.Stepping = false;
        _feetFree = false;
        SetAction(Act.Stand);
    }

    /// <summary>Signed distance from a point to the drawn body (negative = touching), plus the nearest joint.</summary>
    public float DistanceTo(Vector2 p, out int joint)
    {
        float pad = 5 * S;
        float best = Vector2.Distance(p, Jt[J.Head]) - (HeadR + pad);
        joint = J.Head;
        foreach (var (a, b) in Bones)
        {
            float d = M.DistToSegment(p, Jt[a], Jt[b]) - (LineW * 0.5f + pad);
            if (d < best)
            {
                best = d;
                joint = Vector2.DistanceSquared(p, Jt[a]) < Vector2.DistanceSquared(p, Jt[b]) ? a : b;
            }
        }
        return best;
    }

    static readonly (int, int)[] Bones =
    {
        (J.Neck, J.Pelvis), (J.Neck, J.ElbowN), (J.ElbowN, J.HandN), (J.Neck, J.ElbowF), (J.ElbowF, J.HandF),
        (J.Pelvis, J.KneeN), (J.KneeN, J.FootN), (J.Pelvis, J.KneeF), (J.KneeF, J.FootF),
    };
}
