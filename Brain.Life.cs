using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum SkillKind { Juggling, Climbing, Fighting, Ball, Dancing, Drawing }
enum Holiday { None, Halloween, Christmas, NewYear, Valentine }

/// <summary>Everyday life: skills that grow with practice, favourite (and avoided) places, birthdays and holidays,
/// and noticing when you come back.</summary>
sealed partial class Brain
{
    // ---------------- skills ----------------

    public readonly Dictionary<SkillKind, float> Skills = new();
    readonly Dictionary<SkillKind, float> _practisedAt = new();

    public float Sk(SkillKind k)
    {
        if (Skills.TryGetValue(k, out var v)) return v;
        // Everyone starts with a little natural talent from their personality.
        v = k switch
        {
            SkillKind.Juggling => 0.12f + P.Playfulness * 0.2f,
            SkillKind.Climbing => 0.12f + P.Energy * 0.15f + P.Bravery * 0.1f,
            SkillKind.Fighting => 0.1f + P.Aggression * 0.2f,
            SkillKind.Ball => 0.12f + P.Playfulness * 0.15f,
            SkillKind.Dancing => 0.1f + P.Playfulness * 0.12f + P.Sociability * 0.08f,
            _ => 0.1f + P.Curiosity * 0.15f,
        };
        Skills[k] = v;
        return v;
    }

    /// <summary>Got a bit better at something (with diminishing returns); big milestones are celebrated.</summary>
    public void Practice(SkillKind k, float amount, bool continuous = false)
    {
        // One-off practice (a shot, a climb, a win) counts at most twice a second, however often it's reported.
        if (!continuous)
        {
            if (_practisedAt.TryGetValue(k, out var at) && f.Age - at < 0.5f) return;
            _practisedAt[k] = f.Age;
        }
        float before = Sk(k), after = MathF.Min(1, before + amount * (1 - before));
        Skills[k] = after;
        string what = k switch { SkillKind.Ball => "ball games", SkillKind.Drawing => "drawing", _ => k.ToString().ToLowerInvariant() };
        if (before < 0.5f && after >= 0.5f)
        {
            f.Emote("★", 1.2f);
            Write("skill:" + k, V($"Getting good at {what}.", $"I'm getting really good at {what}!!", $"Getting decent at {what}. Not that I care.", $"I think I'm getting better at {what}?", $"{what} is starting to make sense."), "★", 1e9f);
        }
        else if (before < 0.8f && after >= 0.8f)
        {
            f.Emote("★★", 1.4f);
            Write("master:" + k, V($"I'm great at {what} now!", $"{what} MASTER!!!", $"Best at {what}. Obviously.", $"I'm actually good at {what}. Huh.", $"{what} feels like breathing now."), "★", 1e9f);
        }
    }

    // ---------------- places ----------------

    sealed record Place(Anchor At, float Score, string Why);
    readonly List<Place> _places = new();

    /// <summary>Something good (or bad) happened here: remember the spot.</summary>
    void RememberPlace(World w, float score, string why)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null || seg.Item != null) return;
        var here = Anchor.On(w.Env, seg, f.Base.X);
        for (int i = 0; i < _places.Count; i++)
        {
            if (_places[i].At.Resolve(w.Env) is not Vector2 p) { _places.RemoveAt(i--); continue; }
            if (Vector2.Distance(p, f.Base) < 160 * S) { _places[i] = _places[i] with { Score = Math.Clamp(_places[i].Score + score, -2, 3), Why = score > 0 ? why : _places[i].Why }; return; }
        }
        _places.Add(new Place(here, score, why));
        if (_places.Count > 10) _places.Remove(_places.OrderBy(p => MathF.Abs(p.Score)).First());
    }

    /// <summary>How it feels about a spot: above 0 it likes it, below 0 it'd rather not go back.</summary>
    float PlaceFeeling(World w, Vector2 at)
    {
        float s = 0;
        foreach (var p in _places)
            if (p.At.Resolve(w.Env) is Vector2 q && Vector2.Distance(q, at) < 200 * S) s += p.Score;
        return s;
    }

    // ---------------- birthdays, holidays, you coming back ----------------

    /// <summary>When it was drawn (its birthday is that date every year).</summary>
    public DateTime Born = DateTime.Now;
    int _partyDay = -1;
    float _lifeCheck;
    Figure? _partyFor;

    public bool BirthdayToday => Born.Month == DateTime.Now.Month && Born.Day == DateTime.Now.Day && (DateTime.Now - Born).TotalDays > 300;

    void UpdateLife(float dt, World w)
    {
        _lifeCheck -= dt;
        if (_lifeCheck > 0) return;
        _lifeCheck = 5;
        DriftAffinity(5);
        f.ClubColour = w.ClubOf(f)?.Colour;
        NoticeMess(w);
        // Holiday dress-up.
        f.HatOverride = w.Celebrations ? w.Holiday switch
        {
            Holiday.Halloween => P.Aggression > 0.6f ? "viking" : P.Curiosity > 0.6f ? "wizard" : P.Playfulness > 0.6f ? "party" : "crown",
            Holiday.Christmas => "beanie",
            Holiday.NewYear => "party",
            _ => BirthdayToday || _partyDay == DateTime.Now.DayOfYear ? "party" : null,
        } : null;
        if (ChampionOn is DateTime won && won.Date == DateTime.Today && f.HatOverride is null or "party") { f.HatOverride = "crown"; f.HatColourOverride = "#FFC83D"; return; }
        f.HatColourOverride = w.Holiday switch { Holiday.Christmas => "#E53935", Holiday.Halloween when f.HatOverride == "wizard" => "#2E2E2E", _ => null };
        if (w.Celebrations && BirthdayToday && _partyDay != DateTime.Now.DayOfYear && f.Mode == Mode.Control && f.Grounded && _g is G.Idle or G.SitFloor or G.Walk)
            ThrowParty(w);
    }

    /// <summary>A birthday party: party hat, a cake, and friends gathering round to sing.</summary>
    public void ThrowParty(World w)
    {
        w.Sticker("party");
        w.News("party", BirthdayToday ? $"Happy birthday, {f.Name}!" : $"{f.Name} threw a party", BirthdayToday ? 3 : 2, f);
        _partyDay = DateTime.Now.DayOfYear;
        f.HatOverride = "party";
        f.Emote(BirthdayToday ? "it's my birthday!" : "party!", 2);
        if (w.MakeItem?.Invoke("cake") is { } cake)
        {
            cake.Pos = f.Base + new Vector2(f.Facing * 30 * S, -4 * S);
            cake.Vel = Vector2.Zero;
            cake.OnGround = false;
            _gift = cake;
        }
        Cheered(0.5f);
        Write("party", BirthdayToday ? V("It's my birthday! Everyone came!", "BIRTHDAY!!! Cake and everybody!", "Birthday. Fine. The cake was good.", "It's my birthday. People sang. I hid my face.")
                                     : V("We had a party!", "PARTY!!!", "There was a party. I went."), "★", 3600);
        Go(G.Party, 25);
        _partyFor = f;
        foreach (var o in w.Figures)
            if (o != f && !o.Dead && o.Mode == Mode.Control && (o.Brain.AffinityWith(f) > 0.1f || o.Brain.Dating(f)) && Vector2.Distance(o.Base, f.Base) < 2500 * S)
                o.Brain.JoinParty(f, w);
    }

    void JoinParty(Figure host, World w)
    {
        if (_g is G.Sleep or G.Fight or G.Sport or G.Hunt) return;
        _partyFor = host;
        float side = rng.NextDouble() < 0.5 ? -1 : 1;
        float off = rng.Range(28, 70) * S;
        Navigate(() => w.Figures.Contains(host) ? host.Base + new Vector2(side * off, 0) : null, 10 * S, true, () => Go(G.Party, rng.Range(14, 22)), WalkPurpose.Social);
        f.Emote("party!", 1);
    }

    void DoParty(World w)
    {
        f.DesiredVX = 0;
        var host = _partyFor;
        if (host == null || !w.Figures.Contains(host)) { Go(G.Idle, 1); return; }
        if (host != f) FaceTo(host.Base.X);
        f.LookAt = host.Jt[J.Head];
        // Sing, then cheer.
        if (_t < 6) { if ((int)(_t * 1.2f) != (int)((_t - World.Dt) * 1.2f)) f.Emote(host == f ? "♪" : rng.NextDouble() < 0.5 ? "♪ happy birthday ♪" : "♪ ♫", 1.1f); f.SetAction(Act.Talk); }
        else if (_t < 8) f.SetAction(Act.Cheer);
        else if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove);
        if (_t > 7.9f && _t - World.Dt <= 7.9f && host != f)
        {
            AddAffinity(host, 0.08f);
            Write("party:" + host.Name, V($"Went to {host.Name}'s party.", $"{host.Name}'s party was AMAZING!", $"{host.Name} had a party. Cake was okay.", $"Sang at {host.Name}'s party. Quietly."), "★", 3600);
        }
        Cheered(World.Dt * 0.04f);
        Loneliness = MathF.Max(0, Loneliness - World.Dt * 0.05f);
        if (_t > _dur) { RememberPlace(w, 0.6f, "a party"); Go(G.Idle, 1); }
    }

    /// <summary>You were away and just came back.</summary>
    public void OnUserBack(double seconds, World w)
    {
        if (f.Mode != Mode.Control) return;
        string span = seconds > 5400 ? $"{seconds / 3600:0} hours" : seconds > 3000 ? "an hour" : $"{seconds / 60:0} minutes";
        if (UserFondness > 0.25f && _g != G.Sleep)
        {
            f.Emote(rng.NextDouble() < 0.5 ? "you're back!" : "welcome back!", 1.8f);
            f.LookAt = w.Cursor;
            if (UserFondness > 0.5f && rng.NextDouble() < 0.6) ComeToCursor(w);
            w.Sticker("welcome");
            Write("back", V($"You were gone for {span}. Missed you!", $"You came back after {span}!! YAY!", $"You left for {span}. Didn't notice. Much.", $"You were away {span}. I waited by the window.", $"You were gone {span}. The desktop felt bigger."), "♥", 1800);
        }
        else if (UserFondness < -0.3f && _g != G.Sleep) { f.Emote(rng.NextDouble() < 0.5 ? "oh. it's you." : "…", 1.4f); }
        else if (_g == G.Sleep && rng.NextDouble() < 0.4) { f.Emote("!", 1); Go(G.Idle, 1.5f); }
    }

    /// <summary>Grudges fade over tens of minutes (faster for forgiving souls); friendships barely fade at all.</summary>
    void DriftAffinity(float dt)
    {
        float forgive = 0.3f + P.Sociability * 0.5f + (1 - P.Aggression) * 0.6f;
        foreach (var id in Affinity.Keys.ToArray())
        {
            float a = Affinity[id];
            float rate = a < 0 ? 0.0006f * forgive : 0.00005f;
            Affinity[id] = a - a * MathF.Min(1, rate * dt);
        }
    }

    float _makeUpAt;

    /// <summary>Go and say sorry to someone it fell out with. They might accept.</summary>
    void MakeUpOptions(World w, OptionList opts)
    {
        if (f.Hunter || _makeUpAt > Age || Annoyance > 0.4f || P.Sociability < 0.3f) return;
        Figure? who = null; float worst = -0.3f;
        foreach (var o in w.Figures)
            if (o != f && !o.Dead && !o.Hunter && o.Mode == Mode.Control && AffinityWith(o) < worst && Vector2.Distance(o.Base, f.Base) < 2200 * S) { worst = AffinityWith(o); who = o; }
        if (who == null) return;
        float want = (0.25f + P.Sociability * 0.6f + (1 - P.Aggression) * 0.4f + Loneliness * 0.5f) * 0.6f;
        opts.Add(want, () =>
        {
            _makeUpAt = Age + 240;
            Navigate(() => w.Figures.Contains(who) ? who.Base + new Vector2(MathF.Sign(f.Base.X - who.Base.X) * 34 * S, 0) : null, 12 * S, false, () => Apologise(who, w), WalkPurpose.Social);
        }, $"Make up with {who.Name}");
    }

    void Apologise(Figure o, World w)
    {
        FaceTo(o.Base.X);
        f.Emote(V("sorry about before", "sorry!! friends?", "...sorry. ok?", "um. sorry.", "I'm sorry."), 1.8f);
        f.SetAction(Act.Talk);
        var ob = o.Brain;
        float forgive = 0.3f + ob.P.Sociability * 0.5f + (1 - ob.P.Aggression) * 0.6f - ob.Annoyance;
        bool yes = !ob.Asleep && o.Mode == Mode.Control && rng.NextDouble() < Math.Clamp(forgive * 0.6f, 0.1f, 0.9f);
        o.LookAt = f.Jt[J.Head];
        if (yes)
        {
            o.Emote(ob.V("it's ok", "FRIENDS!", "fine. whatever.", "ok... thanks", "all forgiven"), 1.6f);
            AddAffinity(o, 0.35f); ob.AddAffinity(f, 0.3f);
            Cheered(0.2f);
            Write("makeup:" + o.Name, V($"Made up with {o.Name}.", $"{o.Name} and me are FRIENDS again!", $"Said sorry to {o.Name}. They took it.", $"I said sorry to {o.Name}. It went okay!", $"{o.Name} forgave me."), "♥", 600);
            ob.Write("makeup:" + f.Name, ob.V($"{f.Name} said sorry. That was nice.", $"{f.Name} APOLOGISED! We're good!", $"{f.Name} apologised. Fine.", $"{f.Name} said sorry to me.", $"{f.Name} came to make peace."), "♥", 600);
            Go(G.Idle, 1.5f);
        }
        else
        {
            o.Emote(ob.V("hmph", "NOPE", "go away", "...not yet", "no."), 1.5f);
            AddAffinity(o, 0.05f);
            Sadness = M.Clamp01(Sadness + 0.1f);
            Write("snubbed:" + o.Name, V($"Tried to make up with {o.Name}. Not yet.", $"{o.Name} said NOPE. Rude!", $"{o.Name} wouldn't listen. Their loss.", $"I said sorry and {o.Name} walked away.", $"{o.Name} isn't ready to forgive me."), "…", 600);
            Go(G.Idle, 2);
        }
    }

    /// <summary>"Go to my favourite spot" (the place where the nicest things happened).</summary>
    void LifeOptions(World w, OptionList opts)
    {
        MakeUpOptions(w, opts);
        var best = _places.Where(p => p.Score > 0.5f).OrderByDescending(p => p.Score).FirstOrDefault();
        if (best != null && best.At.Resolve(w.Env) is Vector2 spot && Vector2.Distance(spot, f.Base) > 150 * S && (Boredom > 0.4f || Sadness > 0.3f))
            opts.Add((0.4f + Sadness + Boredom * 0.5f) * MathF.Min(best.Score, 2) * 0.5f, () =>
            {
                Navigate(() => best.At.Resolve(w.Env), 8 * S, false, () => { Go(G.SitFloor, rng.Range(8, 16)); f.Emote("♪", 1); Cheered(0.15f); }, WalkPurpose.Explore);
            }, $"Go to their favourite spot ({best.Why})");
    }
}

sealed partial class World
{
    public Holiday Holiday;
    public bool Celebrations = true;
    /// <summary>Debug: pretend it's a holiday (null: follow the calendar).</summary>
    public Holiday? HolidayOverride;
    double _fireworksAt;

    public void UpdateHoliday(double now)
    {
        var d = DateTime.Now;
        Holiday = HolidayOverride ?? (d.Month == 10 && d.Day >= 24 ? Holiday.Halloween
            : d.Month == 12 && d.Day is >= 20 and <= 26 ? Holiday.Christmas
            : (d.Month == 12 && d.Day == 31) || (d.Month == 1 && d.Day == 1) ? Holiday.NewYear
            : d.Month == 2 && d.Day == 14 ? Holiday.Valentine : Holiday.None);
        // New Year: fireworks for a minute either side of midnight (or whenever it's forced on).
        bool midnight = Holiday == Holiday.NewYear && ((d.Month == 12 && d.Hour == 23 && d.Minute == 59) || (d.Month == 1 && d.Hour == 0 && d.Minute < 2));
        if (Celebrations && (midnight || HolidayOverride == Holiday.NewYear) && now > _fireworksAt)
        {
            _fireworksAt = now + Rng.Range(0.4f, 1.1f);
            var v = Env.Virtual;
            var at = new Vector2(Rng.Range(v.Left + 100, v.Right - 100), v.Top + Rng.Range(120, 420) * Scale);
            var col = new[] { new Color4(1, 0.3f, 0.3f, 1), new Color4(0.3f, 0.8f, 1, 1), new Color4(1, 0.85f, 0.2f, 1), new Color4(0.7f, 0.4f, 1, 1), new Color4(0.4f, 1, 0.5f, 1) }[Rng.Next(5)];
            for (int i = 0; i < 9; i++) Fx.Spark(at + new Vector2(Rng.Range(-30, 30), Rng.Range(-30, 30)) * Scale, Scale * 1.4f, Rng, 1.2f, col);
            World.Play(Sfx.Thud, at, 0.3f, 1.8f);
            if (midnight && Rng.NextDouble() < 0.1)
                foreach (var f in Figures) if (f.Mode == Mode.Control && f.Brain.Asleep == false) f.Emote("happy new year!", 2);
        }
    }

    /// <summary>You came back after a while away.</summary>
    public void OnUserBack(double seconds)
    {
        World.Log($"user back after {seconds:0}s");
        foreach (var f in Figures.ToArray()) f.Brain.OnUserBack(seconds, this);
    }
}
