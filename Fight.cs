using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>How two colours get along.</summary>
enum Relation { Default = -1, Friends, Neutral, Rivals, Enemies, Ignore }

/// <summary>What happens when a figure's health runs out in a fight.</summary>
enum DeathRule { KnockdownOnly, KnockOut, Permanent }

/// <summary>Something worn on the hands that changes punches.</summary>
enum Gear { None, BoxingGloves, BrassKnuckles }

/// <summary>User-configurable fight and colour-relationship rules (persisted in settings).</summary>
sealed class FightSettings
{
    public bool Enabled { get; set; } = true;
    public bool PunchCursor { get; set; } = true;
    /// <summary>Multiplier on how often fights start (0..2).</summary>
    public float Frequency { get; set; } = 1;
    /// <summary>Multiplier on damage and knockback (0.25..3).</summary>
    public float Strength { get; set; } = 1;
    public DeathRule OnZeroHealth { get; set; } = DeathRule.KnockdownOnly;
    /// <summary>Seconds a knocked-out figure stays down before getting back up (KnockOut rule).</summary>
    public float ReviveSeconds { get; set; } = 15;
    public bool HealthBars { get; set; } = true;
    public Relation SameColour { get; set; } = Relation.Friends;
    public Relation DifferentColour { get; set; } = Relation.Neutral;
    /// <summary>Per colour-pair overrides, keyed "Blue|Red" (names sorted).</summary>
    public Dictionary<string, Relation> Pairs { get; set; } = new();

    public static string Team(Color4 c)
    {
        string best = Palette.All[0].Name;
        float bd = float.MaxValue;
        foreach (var (name, p) in Palette.All)
        {
            float d = (p.R - c.R) * (p.R - c.R) + (p.G - c.G) * (p.G - c.G) + (p.B - c.B) * (p.B - c.B);
            if (d < bd) { bd = d; best = name; }
        }
        return best;
    }

    public static string PairKey(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";

    public Relation Between(string teamA, string teamB)
    {
        if (Pairs.TryGetValue(PairKey(teamA, teamB), out var r) && r != Relation.Default) return r;
        return teamA == teamB ? SameColour : DifferentColour;
    }

    public Relation Between(Figure a, Figure b) => Between(Team(a.Color), Team(b.Color));

    /// <summary>Starting affinity implied by the relation; experience adds to it.</summary>
    public static float Baseline(Relation r) => r switch
    {
        Relation.Friends => 0.6f,
        Relation.Rivals => -0.05f,
        Relation.Enemies => -0.6f,
        Relation.Ignore => 0,
        _ => 0.15f,
    };

    public static string Describe(Relation r) => r switch
    {
        Relation.Default => "Use default",
        Relation.Friends => "Friends",
        Relation.Neutral => "Neutral",
        Relation.Rivals => "Rivals (spar for fun)",
        Relation.Enemies => "Enemies (fight)",
        Relation.Ignore => "Ignore each other",
        _ => r.ToString(),
    };
}

static class GearInfo
{
    public static string Describe(Gear g) => g switch
    {
        Gear.BoxingGloves => "Boxing gloves (soft hits, big knockback)",
        Gear.BrassKnuckles => "Brass knuckles (punches hurt a lot more)",
        _ => "Bare hands",
    };

    /// <summary>Damage, knockback and poise multipliers for punches.</summary>
    public static (float dmg, float knock, float poise) Punch(Gear g) => g switch
    {
        Gear.BoxingGloves => (0.6f, 1.45f, 1.2f),
        Gear.BrassKnuckles => (1.6f, 1.05f, 1.3f),
        _ => (1, 1, 1),
    };
}

enum AttackKind { Jab, Cross, Uppercut, FrontKick, Roundhouse, Sweep, FlyingKick, Haymaker, SpinKick }
enum HitHeight { Low, Mid, High }

/// <summary>One fighting move. Times in seconds; range/knock in figure-scale units (multiply by S);
/// knock is in facing space (+x = away from the attacker).</summary>
sealed record AttackDef(AttackKind Kind, float Windup, float Active, float Recovery, float Range,
                        float Damage, Vector2 Knock, float Poise, HitHeight Height, bool Foot, bool Knockdown)
{
    public float Total => Windup + Active + Recovery;

    public static readonly AttackDef[] All =
    {
        new(AttackKind.Jab,        0.07f, 0.06f, 0.12f, 27, 6,  new(180, -20),  15, HitHeight.Mid,  false, false),
        new(AttackKind.Cross,      0.11f, 0.07f, 0.18f, 29, 10, new(380, -60),  30, HitHeight.Mid,  false, false),
        new(AttackKind.Uppercut,   0.16f, 0.08f, 0.26f, 22, 14, new(260, -950), 55, HitHeight.High, false, false),
        new(AttackKind.FrontKick,  0.13f, 0.08f, 0.22f, 36, 11, new(700, -120), 45, HitHeight.Mid,  true,  false),
        new(AttackKind.Roundhouse, 0.17f, 0.08f, 0.28f, 34, 16, new(650, -380), 60, HitHeight.High, true,  false),
        new(AttackKind.Sweep,      0.15f, 0.10f, 0.28f, 34, 8,  new(140, -420), 0,  HitHeight.Low,  true,  true),
        new(AttackKind.FlyingKick, 0f,    9f,    0.2f,  0,  18, new(1100, -320), 0, HitHeight.Mid,  true,  true),
        new(AttackKind.Haymaker,   0.30f, 0.08f, 0.35f, 27, 20, new(780, -320), 70, HitHeight.Mid,  false, false),
        new(AttackKind.SpinKick,   0.22f, 0.08f, 0.30f, 34, 17, new(900, -260), 65, HitHeight.Mid,  true,  false),
    };

    public static AttackDef Get(AttackKind k) => All[(int)k];
}
