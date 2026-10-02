namespace StickFight;

enum WalkStyle { Auto, Normal, Bouncy, Swagger, Sluggish, Sneaky, Stiff, March }
enum RunStyle { Auto, Sprinter, Flailer, NinjaRun, Jogger, Tippy }
enum IdleHabit { Auto, Loose, ArmsCrossed, HandsBehind, HandsOnHips, Fidgety }
enum ClimbStyle { Auto, Methodical, Scrambler, Leaper }
enum JumpStyle { Auto, Tuck, Starfish, Superhero, Flipper }
enum FightStyle { Auto, Boxer, Kicker, Brawler, Acrobat, Turtle }
enum CelebrateStyle { Auto, Cheer, Flex, Dance, Taunt, Bow }
enum RopeStyle { Auto, Never, Rappel, Haul, Zip }
enum GrappleSpin { Overhead, SideWhirl, QuickToss }
enum Fidget { Stretch, ScratchHead, CheckWatch, FootTap, Yawn, Shrug, Groove }

/// <summary>The user's body-language choices for a figure (Auto = derive from personality) plus the
/// random seed behind its individual quirks. Persisted with the figure.</summary>
sealed class StyleChoice
{
    public int Seed { get; set; }
    public WalkStyle Walk { get; set; }
    public RunStyle Run { get; set; }
    public IdleHabit Idle { get; set; }
    public ClimbStyle Climb { get; set; }
    public JumpStyle Jump { get; set; }
    public FightStyle Fight { get; set; }
    public CelebrateStyle Celebrate { get; set; }
    /// <summary>Grappling hook: how it climbs the rope (Never = doesn't carry one).</summary>
    public RopeStyle Rope { get; set; }

    public StyleChoice Clone() => (StyleChoice)MemberwiseClone();

    public int Key => HashCode.Combine(HashCode.Combine(Seed, Walk, Run, Idle, Climb, Jump, Fight, Celebrate), Rope);
}

/// <summary>Live mood signals the brain feeds the body (0..1 each); they bend the base style.</summary>
struct MoodState
{
    public float Tired, Angry, Happy, Scared, Sad, Hurt;
}

/// <summary>A figure's resolved body language: categorical styles plus continuous quirks. Deterministic
/// for a given personality + choices + seed, so a figure always moves like itself.</summary>
sealed class BodyStyle
{
    public WalkStyle Walk;
    public RunStyle Run;
    public IdleHabit Idle;
    public ClimbStyle Climb;
    public JumpStyle Jump;
    public FightStyle Fight;
    public CelebrateStyle Celebrate;
    public RopeStyle Rope;
    public GrappleSpin Spin;
    /// <summary>How often it reaches for the grappling hook instead of climbing a tall wall.</summary>
    public float GrappleChance;

    // Walking quirks (1 = average).
    public float Bounce = 1, Stride = 1, Cadence = 1, Posture, ArmSwing = 1, ElbowBend, Swagger, Sneak, Stomp, FootLift = 1, HeadBob, SpeedMul = 1;
    // Idle.
    public float FidgetRate, LookAround = 1, WeightShift = 1;
    // Climbing.
    public float ClimbCycle = 1, ClimbRise = 1, DynoChance, ClimbFlail = 1;
    // Fighting.
    public float GuardBounce = 1, Spacing = 1, AttackRate = 1, DefenseSkill = 1, Toughness = 1;

    public static BodyStyle Resolve(Personality p, StyleChoice c)
    {
        // Draw every random number up front in a fixed order, so changing one choice doesn't reshuffle the rest.
        var r = new Random(c.Seed);
        Span<double> rolls = stackalloc double[8];
        for (int i = 0; i < rolls.Length; i++) rolls[i] = r.NextDouble();
        Span<float> jit = stackalloc float[16];
        for (int i = 0; i < jit.Length; i++) jit[i] = (float)(r.NextDouble() * 2 - 1);

        float E = p.Energy, C = p.Curiosity, B = p.Bravery, Pl = p.Playfulness, A = p.Aggression, So = p.Sociability;
        var s = new BodyStyle
        {
            Walk = c.Walk != WalkStyle.Auto ? c.Walk : Pick(rolls[0],
                (0.6f, WalkStyle.Normal), (E * Pl * 1.6f, WalkStyle.Bouncy), (B * A * 1.8f, WalkStyle.Swagger),
                ((1 - E) * (1 - E) * 1.6f, WalkStyle.Sluggish), ((1 - B) * C * 1.3f, WalkStyle.Sneaky),
                ((1 - Pl) * (1 - So) * 1.0f, WalkStyle.Stiff), (B * E * (1 - Pl) * 1.2f, WalkStyle.March)),
            Run = c.Run != RunStyle.Auto ? c.Run : Pick(rolls[1],
                (0.6f + E * 0.4f, RunStyle.Sprinter), (Pl * (1 - B) * 1.4f + 0.1f, RunStyle.Flailer),
                (Pl * C * 1.0f, RunStyle.NinjaRun), ((1 - E) * 0.8f, RunStyle.Jogger), ((1 - B) * 0.6f, RunStyle.Tippy)),
            Idle = c.Idle != IdleHabit.Auto ? c.Idle : Pick(rolls[2],
                (0.8f, IdleHabit.Loose), (A * (1 - So) * 1.4f + 0.1f, IdleHabit.ArmsCrossed), (C * (1 - E) * 1.2f, IdleHabit.HandsBehind),
                (B * So * 0.8f, IdleHabit.HandsOnHips), (E * (1 - B * 0.5f) * 1.2f, IdleHabit.Fidgety)),
            Climb = c.Climb != ClimbStyle.Auto ? c.Climb : Pick(rolls[3],
                ((1 - E) * 1.2f + 0.2f, ClimbStyle.Methodical), ((1 - B) * E * 1.2f + 0.1f, ClimbStyle.Scrambler), (E * Pl * 1.4f, ClimbStyle.Leaper)),
            Jump = c.Jump != JumpStyle.Auto ? c.Jump : Pick(rolls[4],
                (0.7f, JumpStyle.Tuck), (Pl * So * 1.2f, JumpStyle.Starfish), (B * E * 1.0f, JumpStyle.Superhero), (Pl * E * 1.0f, JumpStyle.Flipper)),
            Fight = c.Fight != FightStyle.Auto ? c.Fight : Pick(rolls[5],
                (0.5f + B * 0.5f, FightStyle.Boxer), (E * Pl * 1.2f, FightStyle.Kicker), (A * (1 - Pl * 0.5f) * 1.4f, FightStyle.Brawler),
                (Pl * E * 1.3f, FightStyle.Acrobat), ((1 - A) * (1 - B * 0.5f) * 1.2f, FightStyle.Turtle)),
            Celebrate = c.Celebrate != CelebrateStyle.Auto ? c.Celebrate : Pick(rolls[6],
                (0.6f, CelebrateStyle.Cheer), (B * A * 1.2f, CelebrateStyle.Flex), (Pl * So * 1.3f, CelebrateStyle.Dance),
                (A * 1.0f, CelebrateStyle.Taunt), ((1 - A) * So * 0.8f, CelebrateStyle.Bow)),
        };

        // Base numbers for the walk style...
        switch (s.Walk)
        {
            case WalkStyle.Bouncy: s.Bounce = 2.2f; s.Stride = 0.95f; s.Cadence = 1.15f; s.Posture = -0.02f; s.ArmSwing = 1.3f; s.ElbowBend = 0.4f; s.FootLift = 1.4f; s.HeadBob = 0.8f; s.SpeedMul = 1.05f; break;
            case WalkStyle.Swagger: s.Bounce = 0.8f; s.Stride = 1.15f; s.Cadence = 0.85f; s.Posture = -0.08f; s.ArmSwing = 1.45f; s.ElbowBend = 0.15f; s.Swagger = 1; s.Stomp = 0.4f; s.HeadBob = 0.5f; break;
            case WalkStyle.Sluggish: s.Bounce = 0.5f; s.Stride = 0.8f; s.Cadence = 0.8f; s.Posture = 0.18f; s.ArmSwing = 0.35f; s.FootLift = 0.55f; s.HeadBob = 0.2f; s.SpeedMul = 0.8f; break;
            case WalkStyle.Sneaky: s.Bounce = 0.4f; s.Stride = 0.85f; s.Cadence = 0.9f; s.Posture = 0.22f; s.ArmSwing = 0.3f; s.ElbowBend = 0.9f; s.Sneak = 1; s.FootLift = 1.5f; s.HeadBob = 0.1f; s.SpeedMul = 0.8f; break;
            case WalkStyle.Stiff: s.Bounce = 0.3f; s.ArmSwing = 0.15f; s.FootLift = 0.8f; break;
            case WalkStyle.March: s.Bounce = 0.7f; s.Stride = 1.05f; s.Cadence = 1.05f; s.Posture = -0.03f; s.ArmSwing = 1.6f; s.FootLift = 1.6f; s.Stomp = 0.6f; s.SpeedMul = 1.05f; break;
            default: s.HeadBob = 0.3f; s.ElbowBend = 0.25f; break;
        }
        // ...bent by personality, then individual quirks so no two figures match.
        s.Bounce *= (0.7f + 0.6f * E) * (1 + 0.15f * jit[0]);
        s.Cadence *= (0.9f + 0.2f * E) * (1 + 0.08f * jit[1]);
        s.Stride *= 1 + 0.1f * jit[2];
        s.Posture += 0.05f * jit[3];
        s.ArmSwing *= (0.8f + 0.4f * So) * (1 + 0.2f * jit[4]);
        s.ElbowBend = Math.Clamp(s.ElbowBend + 0.15f * jit[5], 0, 1);
        s.FootLift *= 1 + 0.15f * jit[6];
        s.HeadBob = Math.Clamp(s.HeadBob + 0.2f * jit[7], 0, 1);
        s.SpeedMul *= 1 + 0.08f * jit[8];

        s.FidgetRate = (0.05f + E * 0.05f + (s.Idle == IdleHabit.Fidgety ? 0.12f : 0)) * (1 + 0.3f * jit[9]);
        s.LookAround = (0.6f + C * 0.8f) * (1 + 0.2f * jit[10]);
        s.WeightShift = (s.Idle == IdleHabit.Fidgety ? 2.2f : 1) * (1 + 0.3f * jit[11]);

        (s.ClimbCycle, s.ClimbRise, s.DynoChance, s.ClimbFlail) = s.Climb switch
        {
            ClimbStyle.Scrambler => (0.62f, 0.72f, 0.1f, 1.8f),
            ClimbStyle.Leaper => (1.0f, 1.0f, 0.6f, 1.0f),
            _ => (1.15f, 1.0f, 0.05f, 0.6f),
        };

        (s.GuardBounce, s.Spacing, s.AttackRate, s.DefenseSkill, s.Toughness) = s.Fight switch
        {
            FightStyle.Boxer => (1.6f, 0.95f, 1.25f, 1.15f, 1.0f),
            FightStyle.Kicker => (1.2f, 1.08f, 1.0f, 1.0f, 0.95f),
            FightStyle.Brawler => (0.4f, 0.9f, 1.1f, 0.55f, 1.25f),
            FightStyle.Acrobat => (1.8f, 1.0f, 0.95f, 1.1f, 0.9f),
            _ => (0.7f, 0.95f, 0.75f, 1.35f, 1.1f),   // Turtle
        };
        s.AttackRate *= 1 + 0.1f * jit[12];

        s.Rope = c.Rope != RopeStyle.Auto ? c.Rope : Pick(rolls[7],
            ((1 - C) * (1 - E) * 1.2f + 0.25f, RopeStyle.Never), (B * 0.9f + 0.15f, RopeStyle.Rappel),
            ((1 - E) * 0.5f + A * 0.5f, RopeStyle.Haul), (E * Pl * 1.3f, RopeStyle.Zip));
        s.Spin = jit[13] < -0.55f + (1 - Pl) * 0.3f ? GrappleSpin.QuickToss : jit[14] < Pl - 0.5f ? GrappleSpin.Overhead : GrappleSpin.SideWhirl;
        s.GrappleChance = s.Rope == RopeStyle.Never ? 0 : Math.Clamp(0.35f + 0.35f * C + 0.15f * jit[15], 0.1f, 0.9f);
        return s;
    }

    static T Pick<T>(double roll, params (float w, T v)[] opts)
    {
        float total = 0;
        foreach (var o in opts) total += MathF.Max(0, o.w);
        float x = (float)roll * total;
        foreach (var o in opts)
        {
            x -= MathF.Max(0, o.w);
            if (x <= 0) return o.v;
        }
        return opts[0].v;
    }

    /// <summary>"Swaggers, sprints, crosses its arms, fights like a boxer..." for the editor.</summary>
    public string Describe()
    {
        string walk = Walk switch
        {
            WalkStyle.Bouncy => "walks with a bounce", WalkStyle.Swagger => "swaggers", WalkStyle.Sluggish => "shuffles along",
            WalkStyle.Sneaky => "tiptoes around", WalkStyle.Stiff => "walks stiffly", WalkStyle.March => "marches", _ => "strolls",
        };
        string run = Run switch
        {
            RunStyle.Flailer => "runs flailing", RunStyle.NinjaRun => "ninja-runs", RunStyle.Jogger => "jogs",
            RunStyle.Tippy => "scurries on tiptoe", _ => "sprints",
        };
        string idle = Idle switch
        {
            IdleHabit.ArmsCrossed => "crosses its arms", IdleHabit.HandsBehind => "keeps its hands behind its back",
            IdleHabit.HandsOnHips => "stands hands-on-hips", IdleHabit.Fidgety => "can't keep still", _ => "stands loose",
        };
        string fight = Fight switch
        {
            FightStyle.Kicker => "kicks like a taekwondo fighter", FightStyle.Brawler => "brawls with big haymakers",
            FightStyle.Acrobat => "fights with flips and flying kicks", FightStyle.Turtle => "turtles up and counters", _ => "boxes",
        };
        string win = Celebrate switch
        {
            CelebrateStyle.Flex => "flexes", CelebrateStyle.Dance => "dances", CelebrateStyle.Taunt => "taunts",
            CelebrateStyle.Bow => "bows", _ => "cheers",
        };
        string rope = Rope switch
        {
            RopeStyle.Rappel => " Walks up walls on a grappling hook.", RopeStyle.Haul => " Hauls itself up a grappling hook hand over hand.",
            RopeStyle.Zip => " Zips up a grappling hook.", _ => "",
        };
        return $"{Cap(walk)}, {run}, {idle}, {fight}, and {win} when it wins.{rope}";
    }

    static string Cap(string s) => char.ToUpperInvariant(s[0]) + s[1..];
}
