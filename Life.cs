namespace Doodlefolk;

/// <summary>How fast life runs: needs, sleep and the rest follow real biology on the real clock by default (a dog eats
/// twice a day, a hamster sleeps through the day, people go to bed at night), half speed when relaxed, or sped up
/// (lively) for a busier desktop.</summary>
static class Life
{
    /// <summary>Multiplier on every biological rate: 1 realistic, 0.5 relaxed, 8 lively.</summary>
    public static float Pace = 1;
    /// <summary>The hour of the day (0..24) by the real clock; the simulation tests set their own.</summary>
    public static float Hour => HourOverride ?? (float)DateTime.Now.TimeOfDay.TotalHours;
    public static float? HourOverride;

    /// <summary>A real-world span in seconds, at the current pace (a 30-minute nap is 4 minutes when lively).</summary>
    public static float Span(float realSeconds) => realSeconds / MathF.Max(Pace, 0.01f);

    /// <summary>Hours from now until a given hour of the day (wrapping past midnight).</summary>
    public static float HoursUntil(float hour) => ((hour - Hour) % 24 + 24) % 24;

    /// <summary>Whether the hour falls between two hours of the day (wrapping past midnight).</summary>
    public static bool Between(float h, float from, float to) => from <= to ? h >= from && h < to : h >= from || h < to;

    public static (float pace, bool accidents) FromSetting(string care) => care switch
    {
        "relaxed" => (0.5f, false),
        "lively" => (8f, true),
        _ => (1f, true),   // "realistic" (and the old "normal")
    };
}

/// <summary>When people sleep: by the real clock, each with their own bedtime (early birds and night owls), around
/// eight hours a night; a short nap in the day only when they're actually sleepy. Being out of breath from running
/// (Stamina) is a different thing, and comes back in minutes.</summary>
sealed partial class Brain
{
    /// <summary>0 wide awake .. 1 can't keep their eyes open: builds over the day, drops in sleep.</summary>
    public float Sleepy = 0.15f;
    bool _nightSleep;

    /// <summary>Their own bedtime and getting-up time (hours of the day), from who they are.</summary>
    public float Bedtime => 21.5f + ((f.Id * 37) % 7) * 0.4f + (1 - P.Energy) * 0.6f - (Baby ? 2.5f : 0) - (IsElder ? 1 : 0);
    public float WakeHour => 6f + ((f.Id * 53) % 6) * 0.45f + (1 - P.Energy) * 0.5f + (Baby ? 0.5f : 0) - (IsElder ? 0.5f : 0);

    /// <summary>Their night, by the clock.</summary>
    public bool BedtimeNow => Life.Between(Life.Hour, Bedtime % 24, WakeHour);

    void UpdateSleepy(float dt)
    {
        if (Asleep) Sleepy = MathF.Max(0, Sleepy - dt * Life.Pace / (8 * 3600f) * (Baby ? 0.6f : 1));
        else Sleepy = MathF.Min(1, Sleepy + dt * Life.Pace / (16 * 3600f) * (Baby ? 2.5f : 1));
        // The body clock: at their night they're sleepy whatever else; first thing in the morning, fresh.
        if (BedtimeNow) Sleepy = MathF.Max(Sleepy, 0.75f);
    }

    /// <summary>Going to bed (at night) or for a nap (in the day): their own bed, else any bed, sleeping bag, hammock
    /// or couch nearby, else wherever they are.</summary>
    void SleepOptions(World w, OptionList opts)
    {
        if (Engaged || InFight || f.Visitor != VisitorKind.None) return;
        bool night = BedtimeNow;
        bool nap = !night && (Sleepy > 0.7f || (Sleepy > 0.45f && Life.Between(Life.Hour, 13, 16) && f.Tastes.Likes(Thing.Napping)));
        if (!night && !nap) return;
        float weight = night ? 6 + Sleepy * 4 : (Sleepy - 0.4f) * 6 * Taste(Thing.Napping);
        string label = night ? "Go to bed" : "Have a nap";
        opts.Add(weight, () => GoToBed(w, night), label);
    }

    void GoToBed(World w, bool night)
    {
        _nightSleep = night;
        bool Bedlike(Item i) => i.Def.Verbs.Any(v => v is Verb.Lie or Verb.Hammock) && i.Free && i.OnGround && i.User == null && !Unreachable(i);
        var bed = Home(w) is { } home && Bedlike(home) ? home
                : w.Items.Where(Bedlike).OrderBy(i => System.Numerics.Vector2.Distance(i.Pos, f.Base)).FirstOrDefault(i => System.Numerics.Vector2.Distance(i.Pos, f.Base) < 1500 * S);
        if (night) f.Emote(V("g'night!", "NIGHT NIGHT!!", "bed.", "sleepy… g'night…", "to dreams"), 1.4f);
        if (bed != null) UseItem(bed, bed.Def.Verbs.Contains(Verb.Lie) ? Verb.Lie : Verb.Hammock, w);
        else Go(G.Sleep, SleepLength());
    }

    /// <summary>How long this sleep lasts: till their getting-up time at night; 15-40 minutes for a nap.</summary>
    float SleepLength() => _nightSleep || BedtimeNow ? Life.Span(MathF.Max(0.2f, Life.HoursUntil(WakeHour)) * 3600) : Life.Span(rng.Range(15, 40) * 60);

    /// <summary>Lying down on something: the night (at their bedtime), else a nap.</summary>
    float SleepLengthFor() { _nightSleep = BedtimeNow; return SleepLength(); }

    /// <summary>Is it time to get up? (Morning came; or a nap's done and they're rested.)</summary>
    bool SleptEnough => _nightSleep ? !BedtimeNow && Sleepy < 0.3f : Sleepy < 0.12f;
}
