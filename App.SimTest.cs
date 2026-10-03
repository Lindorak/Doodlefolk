using System.Numerics;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>One feature the simulation tests can switch: its name, the settings it can take, and what choosing one
/// does to a fresh town (spawning, settings, timed nudges, and the rules that must hold while it's on).</summary>
sealed record SimFactor(string Name, string[] Values, Action<App, string> Setup);

/// <summary>One simulated scenario (Doodlefolk.exe --simrun "seed=1|dur=90|Figures=4|Pets=zoo|...").
/// The real app, hidden, on its own clock and as fast as it goes, in a pretend 1920×1080 desktop with its own throwaway
/// data. Each feature the scenario turns on sets itself up and brings its own rules; on top of those, rules that hold
/// in every town: no errors, nothing at an impossible place or off the screen for long, riders and swimmers consistent,
/// numbers of things bounded, events ending, nobody stuck doing one thing forever or flipping between two, drawing
/// works. --simtest (SimTest.cs) picks the scenarios and runs many of these side by side.</summary>
sealed partial class App
{
    bool _simRun;
    readonly Dictionary<string, string> _simSpec = new();
    double _simDur = 90;
    int _simSeed;
    readonly List<(double at, string name, Action act)> _simSteps = new();
    readonly List<SimRuleState> _simRules = new();
    readonly List<string> _simFails = new();
    readonly HashSet<string> _simFailKeys = new();
    readonly Dictionary<string, object> _simMetrics = new();
    double _simNextCheck, _simNextDraw;
    readonly Dictionary<object, double> _simAway = new();
    readonly Dictionary<int, SimActivity> _simActs = new();
    readonly Dictionary<string, HashSet<string>> _simPetActs = new();
    readonly List<double> _simMs = new();
    int _simFights, _simMaxItems, _simMaxFigures, _simDraws;
    DateTime _simWall;

    sealed class SimRuleState
    {
        public required string Name;
        public required Func<double, bool> Holds;
        public Func<string>? Detail;
        public double From, To = double.MaxValue;
        public int Grace = 1, Bad;
        public bool Failed;
    }

    sealed class SimActivity
    {
        public string Name = "", Now = "";
        public double Since;
        public int Switches;
        public bool InFight;
        public readonly HashSet<string> Seen = new();
        public readonly Queue<(double t, string act)> Recent = new();
    }

    // ---------------- the features ----------------

    /// <summary>Everything the simulation tests can switch, in the order they're set up. Add a feature here (its
    /// settings, setup and rules) and the covering arrays fold it into every run: each of its settings meets each
    /// setting of every other feature (every triple, in the deep run) somewhere.</summary>
    public static readonly SimFactor[] SimFactors =
    {
        new("Layout", new[] { "desktop", "strip" }, (a, v) =>
        {
            if (v != "strip") return;
            a._settings.TownLayout = "strip"; a.ApplyLayout();
            a.SimRule("the taskbar village keeps everyone in the strip", 2,
                t => a._w.Figures.Where(f => f.Mode == Mode.Control).All(f => f.Base.Y >= a._w.Env.BoundsAt(f.Base.X).T - 5 * a._w.Scale),
                () => string.Join(", ", a._w.Figures.Where(f => f.Mode == Mode.Control && f.Base.Y < a._w.Env.BoundsAt(f.Base.X).T - 5 * a._w.Scale).Select(f => $"{f.Name} at y {f.Base.Y:0}, {f.Brain.Activity}")), grace: 4);
        }),
        new("Windows", new[] { "still", "moving" }, (a, v) =>
        {
            if (v != "moving") return;
            // A window slides back and forth all the time, another closes under whoever's on it and comes back.
            a.SimEvery(0.1, 0, "slide", t => a.MoveStaged(0, new Rectangle((int)(110 + 300 * MathF.Sin((float)t * 0.3f)), 560, 600, 300)));
            a.SimAt(30, "close a window", () => a.CloseStaged(2));
            a.SimAt(50, "reopen it", () => a.MoveStaged(2, new Rectangle(560, 250, 460, 230)));
        }),
        new("Figures", new[] { "1", "4", "10" }, (a, v) =>
        {
            for (int i = 0; i < int.Parse(v); i++) a.Spawn(null);
            int start = a._w.Figures.Count;
            a.SimRule("the town never gets more crowded than it allows", 0, t => a._w.Figures.Count <= Math.Max(start, World.MaxFigures),
                () => $"{a._w.Figures.Count} figures (started with {start})");
        }),
        new("Things", new[] { "few", "lots" }, (a, v) =>
        {
            string[] few = { "bed", "couch", "table", "radio", "box" };
            string[] lots = { "chair", "armchair", "lamp", "campfire", "pond", "pool", "bike", "skateboard", "gokart", "stage", "shopstall", "foodcart", "fairylights", "lantern", "pizza", "cake", "book", "beanbag", "throne", "tent", "petbed", "goal", "hoop", "jumprope", "longrope" };
            foreach (var k in v == "lots" ? few.Concat(lots) : few) a.Put(k);
            if (v == "lots") foreach (var k in Enum.GetValues<PropKind>()) a.SpawnProp(k);
        }),
        new("Pets", new[] { "none", "two", "zoo" }, (a, v) =>
        {
            if (v == "none") return;
            var kinds = v == "two" ? new[] { PetKind.Cat, PetKind.Dog } : Enum.GetValues<PetKind>().Concat(new[] { PetKind.Dog, PetKind.Cat }).ToArray();
            foreach (var k in kinds) a.SpawnPet(k, quiet: true);
        }),
        new("Bathroom", new[] { "on", "off" }, (a, v) =>
        {
            // Everyone needs to go: either it happens (and gets cleaned up), or in no-bathroom mode it never does.
            foreach (var p in a._w.Pets) { p.Bladder = 0.95f; p.Bowel = 0.95f; }
            if (v != "off") return;
            a.SetPetBathroom(false);
            a.SimRule("no-bathroom mode: no needs and no messes", 1,
                t => a._w.Pets.All(p => p.Bladder == 0 && p.Bowel == 0 && p.Want is not (PetNeed.Potty or PetNeed.Walk)) && !a._w.Items.Any(i => i.IsMess),
                () => string.Join(", ", a._w.Pets.Select(p => $"{p.Name} {p.Bladder:0.00}/{p.Bowel:0.00} {p.Want}")) + "; messes " + a._w.Items.Count(i => i.IsMess));
        }),
        new("Fights", new[] { "off", "knockdown", "knockout", "permanent" }, (a, v) =>
        {
            a._settings.Fight.Enabled = v != "off";
            a._settings.Fight.OnZeroHealth = v switch { "knockout" => DeathRule.KnockOut, "permanent" => DeathRule.Permanent, _ => DeathRule.KnockdownOnly };
            a._w.Fight = a._settings.Fight;
            if (v == "off")
                a.SimRule("fights off: nobody fights", 1, t => !a._w.Figures.Any(f => f.Brain.InFight), () => a.Fighters());
        }),
        new("Calm", new[] { "off", "on" }, (a, v) =>
        {
            if (v != "on") return;
            a._settings.Calm = World.Calm = true;
            a.SimRule("calm mode: nobody fights", 1, t => !a._w.Figures.Any(f => f.Brain.InFight), () => a.Fighters());
        }),
        new("Hunter", new[] { "none", "one" }, (a, v) =>
        {
            if (v == "one") a.SimAt(3, "make a hunter", () => { if (a._w.Figures.FirstOrDefault() is { } f) a.MakeHunter(f, true); });
        }),
        new("Mood", new[] { "classic", "cozy", "chaos" }, (a, v) => a._settings.TownMood = v),
        new("Weather", new[] { "clear", "rain", "storm", "snow" }, (a, v) =>
        {
            var kind = Enum.Parse<WeatherKind>(v, true);
            if (kind != WeatherKind.Clear) a.SimAt(0.5, "weather", () => a._w.Weather.Start(kind, a._clock.Elapsed.TotalSeconds, a._w.Rng, a._w));
        }),
        new("Time", new[] { "day", "night" }, (a, v) => { if (v == "night") { a._w.NightOverride = 0.85f; Life.HourOverride = 23.5f; } }),
        new("Event", new[] { "none", "festival", "race", "talent" }, (a, v) =>
        {
            if (v == "none") return;
            a.SimAt(4, "start " + v, () => a._simMetrics["event"] = a.StartHappening(v));
        }),
        new("Toybox", new[] { "none", "moon", "heavy", "chaos" }, (a, v) =>
        {
            switch (v)
            {
                case "moon" or "heavy":
                    a.SimAt(10, "gravity " + v, () => a.SetGravity(v));
                    a.SimRule($"{v} gravity while it's on", 11, t => v == "moon" ? World.GravityMul < 0.5f : World.GravityMul > 1.2f, () => $"gravity ×{World.GravityMul}", to: 39);
                    a.SimAt(40, "gravity back", () => a.SetGravity("normal"));
                    a.SimRule("gravity goes back to normal", 41, t => World.GravityMul == 1, () => $"gravity ×{World.GravityMul}");
                    break;
                case "chaos":
                    a.SimAt(10, "everything in the toybox", () => { a.Earthquake(); a.GiantBall(); a.Gust(); a.Confetti(); a.SlowMotion(); });
                    a.SimAt(26, "another quake", () => a.Earthquake());
                    break;
            }
        }),
        new("Size", new[] { "normal", "small", "big" }, (a, v) =>
        {
            if (v == "normal") return;
            float to = v == "small" ? 0.6f : 1.6f;
            a.SimAt(20, "resize everything", () => a.SimRescale(to));
            a.SimAt(50, "and back", () => a.SimRescale(1));
        }),
        new("Focus", new[] { "off", "on" }, (a, v) =>
        {
            if (v != "on") return;
            a.SimAt(8, "focus session", () => a.StartFocus(1));
            a.SimRule("a focus session keeps the town calm", 9, t => !World.Focus || !a._w.Figures.Any(f => f.Brain.InFight), () => a.Fighters());
            a.SimRule("a focus session ends", 75, t => !World.Focus);
        }),
        new("Ageing", new[] { "off", "fast", "mortal" }, (a, v) =>
        {
            if (v == "off") return;
            a._settings.LifePace = "fast";
            if (v == "mortal") a._settings.Mortality = "oldage";
        }),
        new("Visitors", new[] { "off", "on" }, (a, v) => a._settings.Visitors = v == "on"),
        new("Chat", new[] { "off", "viewers" }, (a, v) =>
        {
            if (v != "viewers") return;
            a._settings.StreamOn = true;
            a.SimAt(6, "chat joins", () =>
            {
                a._simMetrics["roomForViewers"] = a._w.Figures.Count < World.MaxFigures - 3;
                foreach (var who in new[] { "ana", "bo", "cy" }) a._chat.Enqueue(new ChatLine(who, who.ToUpperInvariant(), "#9146FF", "!join", false, false));
                foreach (var t in new[] { "hi http://spam.example", "!dance", "!weather snow", "!fight" }) a._chat.Enqueue(new ChatLine("ana", "ANA", "#9146FF", t, false, false));
            });
            a.SimAt(9, "viewers here", () => { if (a._simMetrics.GetValueOrDefault("roomForViewers") is true) a.SimCheck("chatters join the town", a._viewers.Count > 0, $"{a._viewers.Count} viewers"); });
            a.SimAt(40, "a viewer leaves", () => a._chat.Enqueue(new ChatLine("bo", "BO", "", "!leave", false, false)));
            a.SimRule("links from chat are never shown", 7, t => !a._w.Figures.Any(f => f.CurrentEmote?.Contains("spam.example") == true));
        }),
        new("Pranks", new[] { "off", "on" }, (a, v) =>
        {
            if (v != "on") return;
            string dir = Path.Combine(AppPaths.DataDir, "my memes");
            Directory.CreateDirectory(dir);
            using (var bmp = new System.Drawing.Bitmap(120, 90)) { using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.Orange); bmp.Save(Path.Combine(dir, "rain day.png")); }
            a._settings.Pranks = true; a._settings.MemeFolder = dir;
            a.MakeMemeSoon();
        }),
        new("Requests", new[] { "off", "on" }, (a, v) =>
        {
            if (v != "on") return;
            a.SimAt(5, "a request", () => a.NewQuest(a._w.Figures.FirstOrDefault(), QuestKind.Thing));
            a.SimAt(7, "grant it", () => { if (a._settings.Quests.LastOrDefault() is { } q && ItemCatalog.Find(q.Target) is { } d) a.SpawnItem(d); });
        }),
        new("Cursor", new[] { "away", "playing" }, (a, v) =>
        {
            if (v != "playing") return;
            // A pretend cursor wanders over the town (the real one is never touched), and they may punch it.
            a._settings.Fight.PunchCursor = true;
            a.SimEvery(1 / 30.0, 0, "cursor", t => a._fakeCursor = new Vector2(960 + 800 * MathF.Sin((float)t * 0.21f), 700 + 300 * MathF.Sin((float)t * 0.53f)));
        }),
        new("Knockabout", new[] { "none", "rough" }, (a, v) =>
        {
            if (v != "rough") return;
            // Someone flung every few seconds, and a thing tossed: whatever they were doing gets interrupted.
            a.SimEvery(6, 6, "fling", t =>
            {
                var who = a._w.Figures.Where(f => f.Mode == Mode.Control).ToList();
                if (who.Count > 0) who[a._w.Rng.Next(who.Count)].GoRagdoll(new Vector2(a._w.Rng.Range(-900, 900), -a._w.Rng.Range(300, 1100)) * a._w.Scale);
                var things = a._w.Items.Where(i => i.Free && i.Def.Carry).ToList();
                if (things.Count > 0) { var it = things[a._w.Rng.Next(things.Count)]; it.Vel = new Vector2(a._w.Rng.Range(-800, 800), -600) * a._w.Scale; it.OnGround = false; }
            });
        }),
        new("Characters", new[] { "none", "mixed", "jealous" }, (a, v) =>
        {
            var figs = a._w.Figures.ToList();
            if (v == "none") { foreach (var f in figs) f.Archetype = Archetype.None; return; }
            var kinds = Enum.GetValues<Archetype>().Where(k => k != Archetype.None).ToArray();
            for (int i = 0; i < figs.Count; i++) figs[i].Archetype = v == "jealous" && i == 0 ? Archetype.Yandere : kinds[i % kinds.Length];
            if (v == "jealous" && figs.Count >= 3)
            {
                // A yandere devoted to one figure, who's good friends with a third.
                figs[0].Brain.AddAffinity(figs[1], 0.9f);
                figs[1].Brain.AddAffinity(figs[2], 0.7f); figs[2].Brain.AddAffinity(figs[1], 0.7f);
            }
            a.SimRule("jealousy never turns into a fight", 1, t => a._w.Figures.All(f => f.Brain.Jealousy is not { jealousOf: { } j, foe: { } foe } || foe != j),
                () => string.Join(", ", a._w.Figures.Where(f => f.Brain.Jealousy.foe != null).Select(f => $"{f.Name} fighting {f.Brain.Jealousy.foe!.Name}")));
        }),
        new("Wishes", new[] { "none", "granted" }, (a, v) =>
        {
            if (v != "granted") return;
            Item? gift = null; Prop? giftBall = null; string what = ""; Figure? to = null; float footTime = 0;
            a.SimAt(5, "a wish, granted", () =>
            {
                // Someone free (an event's racers and acts finish that first, rightly).
                if (a._w.Figures.FirstOrDefault(f => f.Mode == Mode.Control && f.Visitor == VisitorKind.None && f.Brain.HapRole.Length == 0) is not { } f) return;
                what = f.Brain.ForceWish(a._w);
                to = f;
                int items = a._w.Items.Count, props = a._w.Props.Count;
                a.GrantWish();
                gift = a._w.Items.Count > items ? a._w.Items[^1] : null;
                giftBall = a._w.Props.Count > props ? a._w.Props[^1] : null;
            });
            // Watched all along (a campfire is warmed by and left; a pizza is eaten and gone).
            bool used = false;
            a.SimEvery(0.5, 5.5, "watch the present", t =>
            {
                if (to != null && to.Brain.FreeForGift) footTime += 0.5f;   // time they could have gone for it
                if (used) return;
                if (gift != null)
                    used = !a._w.Items.Contains(gift) || gift.BitesLeft < gift.Def.Bites || gift.User != null || gift.Seated.Any(s => s != null) || gift.Holder != null || gift.Lifted
                           || a._w.Figures.Any(f => f.Brain.UsingNow == gift || f.Brain.Activity.Contains(gift.Def.Name, StringComparison.OrdinalIgnoreCase));
                else if (giftBall != null) used = giftBall.LastTouch != null || giftBall.Holder != null || a._w.Figures.Any(f => f.Brain.Seeking == giftBall);
            });
            a.SimRule("no asking for another present before using the last", 6, t => used || to == null || a._w.Wish?.By != to,
                () => $"{to?.Name} asked for {a._w.Wish?.What.Name} with {what} unused", to: 44);
            a.SimAt(45, "the wish was enjoyed", () =>
            {
                // Fair only if they had a chance (on their feet for a while, not flung about the whole time).
                if ((gift != null || giftBall != null) && footTime >= 25)
                    a.SimCheck("a granted wish gets used", used, $"{what}: never touched in {footTime:0}s free; {to?.Name} is {to?.Brain.Activity}, last chose {to?.Brain.LastDecision}; gift at {gift?.Pos ?? giftBall?.Pos}, on ground {gift?.OnGround ?? giftBall?.OnGround}, free {gift?.Free ?? giftBall?.Free}, they're at {to?.Base}");
            });
        }),
        new("Sports", new[] { "none", "soccer", "basketball", "tennis", "badminton" }, (a, v) =>
        {
            if (v == "none") return;
            a.SimAt(3, "a game of " + v, () => a.SimSport(v));
            // Another game whenever one finishes, counting into the same stats.
            a.SimEvery(2, 6, "keep playing", t => { if (t < a._simDur - 25 && a._simGear.Count > 0 && !a._w.Matches.Any() && a._w.Happening == null) a.SimSport(v); });
        }),
        new("Moving", new[] { "none", "furniture" }, (a, v) =>
        {
            if (v != "furniture") return;
            a.SimAt(8, "move things", () =>
            {
                var figs = a._w.Figures.Where(f => f.Mode == Mode.Control && f.Brain.HapRole.Length == 0).ToList();
                foreach (var (key, i) in new[] { ("couch", 0), ("chair", 1) })
                {
                    if (i >= figs.Count) break;
                    var it = a._w.Items.FirstOrDefault(x => x.Def.Key == key) ?? a.Put(key);
                    if (it == null) continue;
                    var seg = a._w.Env.Below(it.Pos.X, it.Pos.Y - 4);
                    float x = seg != null ? M.ClampIn(it.Pos.X + 300, seg.X1 + 60, seg.X2 - 60) : it.Pos.X;
                    figs[i].Brain.HaulNow(it, x, a._w);
                }
            });
        }),
        new("Dreams", new[] { "later", "now" }, (a, v) =>
        {
            if (v != "now") return;
            a.SimAt(3, "dream", () => { foreach (var f in a._w.Figures) f.Brain.ThinkAboutDreams(); });
            // A dream's next step competes fairly: someone deciding freely, again and again, takes one sooner or later.
            a.SimRule("dreams get worked on", 10, t => a._w.Figures.All(f => f.Brain.Dream == null || f.Brain.DreamSteps > 0 || f.Brain.DreamOffers < 16),
                () => string.Join("; ", a._w.Figures.Where(f => f.Brain.Dream != null && f.Brain.DreamSteps == 0).Select(f => $"{f.Name}: {f.Brain.DreamTitle}, offered {f.Brain.DreamOffers} times, last {f.Brain.LastDecision}")));
            a.SimAt(a._simDur - 1, "dreams", () => a._simMetrics["dreams"] = string.Join("; ", a._w.Figures.Select(f => f.Brain.DreamTitle).Where(t => t.Length > 0)));
        }),
        new("Babies", new[] { "on", "off" }, (a, v) => a._settings.Babies = v == "on"),
        new("Jobs", new[] { "on", "off" }, (a, v) => a._settings.Jobs = v == "on"),
        new("Graphics", new[] { "low", "high", "ultra" }, (a, v) => { a._settings.Gfx = GfxSettings.For(v); Gfx.Q = a._settings.Gfx; }),
    };

    // ---------------- building blocks for the features ----------------

    void SimAt(double at, string name, Action act) => _simSteps.Add((at, name, act));

    void SimEvery(double every, double from, string name, Action<double> act)
    {
        for (double t = from; t < _simDur; t += every) { double tt = t; _simSteps.Add((t, name, () => act(tt))); }
    }

    void SimRule(string name, double from, Func<double, bool> holds, Func<string>? detail = null, int grace = 1, double to = double.MaxValue) =>
        _simRules.Add(new SimRuleState { Name = name, From = from, To = to, Holds = holds, Detail = detail, Grace = grace });

    void SimCheck(string name, bool pass, string detail = "")
    {
        if (!pass) SimFail(name, detail);
    }

    void SimFail(string name, string detail = "")
    {
        if (!_simFailKeys.Add(name)) return;
        double t = _tFrame / (double)TFps;
        _simFails.Add($"{t:0.0}s  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        World.Log($"simtest FAIL at {t:0.0}s: {name} {detail}");
    }

    string Fighters() => string.Join(", ", _w.Figures.Where(f => f.Brain.InFight).Select(f => $"{f.Name}: {f.Brain.Activity}{(f.Visitor != VisitorKind.None ? " (" + f.Visitor + ")" : "")}"));

    void MoveStaged(int i, Rectangle r)
    {
        var list = _w.Env.Staged!;
        var h = (IntPtr)(0x7F000200 + i);
        int at = list.FindIndex(x => x.Item1 == h);
        var rect = new Native.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
        if (at >= 0) list[at] = (h, rect); else list.Add((h, rect));
    }

    void CloseStaged(int i) => _w.Env.Staged!.RemoveAll(x => x.Item1 == (IntPtr)(0x7F000200 + i));

    /// <summary>Resize everything and check nobody and nothing was lost on the way.</summary>
    void SimRescale(float to)
    {
        var before = _w.Figures.ToDictionary(f => f.Id, f => f.S / _w.Scale);
        int pets = _w.Pets.Count, items = _w.Items.Count(i => !i.Temporary);
        Rescale(to);
        SimCheck($"resizing to ×{to} keeps everyone, at the new size",
                 _w.Figures.Count == before.Count && _w.Figures.All(f => before.TryGetValue(f.Id, out var k) && MathF.Abs(f.S - k * to) < 0.03f * k * to),
                 $"figures {_w.Figures.Count}/{before.Count}");
        SimCheck($"resizing to ×{to} keeps every animal and thing", _w.Pets.Count == pets && _w.Items.Count(i => !i.Temporary) == items,
                 $"pets {_w.Pets.Count}/{pets}, things {_w.Items.Count(i => !i.Temporary)}/{items}");
    }

    // ---------------- running ----------------

    void SimPrepare(string[] args)
    {
        World.SoundTap = null;
        int i = Array.IndexOf(args, "--simrun");
        string spec = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : "";
        foreach (var part in spec.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) _simSpec[kv[0].Trim()] = kv[1].Trim();
        }
        _simSeed = _simSpec.TryGetValue("seed", out var s) && int.TryParse(s, out var sv) ? sv : Environment.TickCount & 0xFFFF;
        if (_simSpec.TryGetValue("dur", out var d) && double.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, out var dv)) _simDur = Math.Clamp(dv, 5, 24 * 3600);
        // Anything not given: a random choice (so a bare --simrun is a random town).
        var pick = new Random(_simSeed);
        foreach (var fct in SimFactors)
            if (!_simSpec.TryGetValue(fct.Name, out var val) || !fct.Values.Contains(val)) _simSpec[fct.Name] = fct.Values[pick.Next(fct.Values.Length)];
        _w.Rng = new Random(_simSeed);
        Life.HourOverride = 14;   // the afternoon, unless the scenario says night (results never depend on when tests run)
        _settings.Gfx = GfxSettings.For("high");
    }

    /// <summary>The same pretend desktop as the self-test: 1920×1080, three windows and a taskbar.</summary>
    void SimStage()
    {
        _r.Headless = true;
        _baseScale = 1;
        _w.Scale = 1;
        _w.Env.MinHeadroom = 75;
        _w.Env.StageScreen(new Rectangle(0, 0, 1920, 1080), 48);
        _w.Env.Staged!.Clear();
        MoveStaged(0, new Rectangle(110, 560, 600, 300));
        MoveStaged(1, new Rectangle(1010, 410, 760, 430));
        MoveStaged(2, new Rectangle(560, 250, 460, 230));
        _fakeCursor = new Vector2(-5000, -5000);
    }

    void SimBegin()
    {
        _simWall = DateTime.Now;
        World.Log("simtest: " + SimSpecText());
        // Replaying a failure: DF_TRACE_AT=<seconds> logs every brain's goal changes from then on (events.log).
        if (double.TryParse(Environment.GetEnvironmentVariable("DF_TRACE_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var traceAt))
            SimAt(traceAt, "trace on", () => { foreach (var f in _w.Figures) f.Brain.TraceUntil = 1e9f; });
        _w.Env.Refresh(_overlay.Handle);
        foreach (var fct in SimFactors)
        {
            try { fct.Setup(this, _simSpec[fct.Name]); }
            catch (Exception e) { SimFail($"setting up {fct.Name}={_simSpec[fct.Name]}", e.GetType().Name + ": " + e.Message); }
        }
        _simSteps.Sort((x, y) => x.at.CompareTo(y.at));
    }

    string SimSpecText() => $"seed={_simSeed}|dur={_simDur.ToString(System.Globalization.CultureInfo.InvariantCulture)}|" + string.Join("|", SimFactors.Select(f => $"{f.Name}={_simSpec[f.Name]}"));

    int _simStepAt;

    void SimFrame()
    {
        double t = _tFrame / (double)TFps;
        if (_tFrame == 0) SimBegin();
        _tFrame++;
        while (_simStepAt < _simSteps.Count && _simSteps[_simStepAt].at <= t)
        {
            var (_, name, act) = _simSteps[_simStepAt++];
            try { act(); }
            catch (Exception e) { SimFail($"step \"{name}\" ran", e.GetType().Name + ": " + e.Message + " " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()); }
        }
        SimWatch(t);
        if (t >= _simNextCheck) { _simNextCheck = t + 0.5; SimChecks(t); }
        // Draw now and then, so the drawing code meets every combination too.
        if (t >= _simNextDraw)
        {
            _simNextDraw = t + 2.5;
            try { Render(); _simDraws++; }
            catch (Exception e) { SimFail("drawing works", e.GetType().Name + ": " + e.Message + " " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim()); }
        }
        if (t >= _simDur) SimFinish();
    }

    /// <summary>Every frame: what everyone's doing (for the stuck, dithering and variety measures).</summary>
    void SimWatch(double t)
    {
        foreach (var f in _w.Figures)
        {
            if (!_simActs.TryGetValue(f.Id, out var a)) _simActs[f.Id] = a = new SimActivity { Name = f.Name, Since = t };
            string act = f.Brain.Activity;
            bool fight = f.Brain.InFight;
            if (fight && !a.InFight) _simFights++;
            a.InFight = fight;
            if (act != a.Now)
            {
                if (a.Now.Length > 0) a.Switches++;
                a.Now = act; a.Since = t; a.Seen.Add(act);
                a.Recent.Enqueue((t, act));
            }
            while (a.Recent.Count > 0 && t - a.Recent.Peek().t > 12) a.Recent.Dequeue();
        }
        foreach (var p in _w.Pets)
        {
            if (!_simPetActs.TryGetValue(p.Name, out var seen)) _simPetActs[p.Name] = seen = new();
            seen.Add(p.Activity);
        }
        _simMaxItems = Math.Max(_simMaxItems, _w.Items.Count);
        _simMaxFigures = Math.Max(_simMaxFigures, _w.Figures.Count);
        if (_msSim > 0) _simMs.Add(_msSim);
    }

    /// <summary>Twice a (simulated) second: the rules for every town, then each feature's own.</summary>
    void SimChecks(double t)
    {
        static bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        var screen = RectangleF.FromLTRB(-250, -700, 1920 + 250, 1080 + 150);
        void Away(object who, string what, Vector2 at, double limit)
        {
            if (screen.Contains(at.X, at.Y)) { _simAway.Remove(who); return; }
            if (!_simAway.TryGetValue(who, out var since)) _simAway[who] = since = t;
            else if (t - since > limit) SimFail($"{what} stays off the screen", $"at {at.X:0},{at.Y:0} for {t - since:0}s");
        }
        foreach (var f in _w.Figures)
        {
            if (!Finite(f.Base) || !f.Jt.All(Finite)) SimFail($"{f.Name} is somewhere impossible", $"{f.Base}");
            else Away(f, $"figure {f.Name} ({f.Brain.Activity})", f.Base, 8);
            if (f.Riding is { } v && (v.Rider != f || !_w.Items.Contains(v))) SimFail($"{f.Name} rides something that doesn't know it");
        }
        foreach (var p in _w.Pets)
        {
            if (!Finite(p.Pos)) SimFail($"pet {p.Name} is somewhere impossible");
            else Away(p, $"pet {p.Name} ({p.Activity})", p.Pos, 8);
        }
        foreach (var it in _w.Items)
        {
            if (!Finite(it.Pos)) SimFail($"{it.Def.Key} is somewhere impossible");
            else if (it.Free) Away(it, $"{it.Def.Key}", it.Pos, 15);
            if (it.Rider is { } r && (r.Riding != it || !_w.Figures.Contains(r))) SimFail($"{it.Def.Key} is ridden by someone who isn't riding it");
            if (it.Lifted && (it.HaulA is not { } ha || !_w.Figures.Contains(ha) || ha.Hauling != it)) SimFail("nothing is left floating in the air", $"{it.Def.Key} lifted by {it.HaulA?.Name ?? "nobody"}");
            if (it.Rider is { } r2 && r2.Mode != Mode.Control) SimFail($"{it.Def.Key} is stuck to {r2.Name}, who fell off");
            int swimmers = _w.Figures.Count(f => f.Swimming && f.Brain.WaterItem == it);
            if (it.Swimmers != swimmers) SimFail($"{it.Def.Key} counts the wrong number of swimmers", $"{it.Swimmers} vs {swimmers}");
        }
        foreach (var p in _w.Props) if (!Finite(p.Pos)) SimFail($"a {p.Kind} is somewhere impossible");
        foreach (var o in _simAway.Keys.Where(k => k is Figure f && !_w.Figures.Contains(f) || k is Pet p && !_w.Pets.Contains(p) || k is Item i && !_w.Items.Contains(i)).ToList()) _simAway.Remove(o);
        SimCheck("mess never piles up", _w.Items.Count(Pet.Mess) <= Pet.MaxMesses + 2, $"{_w.Items.Count(Pet.Mess)} messes");
        SimCheck("the number of things stays sane", _w.Items.Count < 500 && _w.Props.Count < 80 && _w.Projectiles.Count < 600, $"{_w.Items.Count} things, {_w.Props.Count} balls, {_w.Projectiles.Count} projectiles");
        if (_w.Happening is { } h) SimCheck("events end", h.T < 330, $"{h.Title} has gone on for {h.T:0}s");
        foreach (var f in _w.Figures)
        {
            if (!_simActs.TryGetValue(f.Id, out var a)) continue;
            string act = a.Now;
            if (t - a.Since > 240 && !act.StartsWith("Napping") && !act.StartsWith("Sleep") && !act.StartsWith("Knocked") && !act.StartsWith("Fishing") && !act.StartsWith("Reading"))
                SimFail($"nobody gets stuck doing one thing", $"{f.Name} has been \"{act}\" for {t - a.Since:0}s");
            // Flipping back and forth between the same two things: can't make up their mind.
            if (a.Recent.Count >= 10 && a.Recent.Select(r => r.act).Distinct().Count() <= 2)
                SimFail("nobody flips back and forth between two things", $"{f.Name}: {string.Join(" / ", a.Recent.Select(r => r.act).Distinct())}, {a.Recent.Count} switches in 12s");
        }
        foreach (var r in _simRules)
        {
            if (r.Failed || t < r.From || t > r.To) continue;
            bool ok;
            try { ok = r.Holds(t); }
            catch (Exception e) { SimFail(r.Name, "the rule itself threw: " + e.Message); r.Failed = true; continue; }
            r.Bad = ok ? 0 : r.Bad + 1;
            if (r.Bad >= r.Grace) { r.Failed = true; SimFail(r.Name, r.Detail?.Invoke() ?? ""); }
        }
    }

    readonly List<Item> _simGear = new();
    List<Figure> _simRoster = new();
    MatchStats? _simStats;

    /// <summary>A game on the floor with a full roster, so we can see how well they play.</summary>
    void SimSport(string sport)
    {
        var floor = _w.Env.Platforms.Where(p => p.X2 - p.X1 > 1000).OrderByDescending(p => p.Y).FirstOrDefault();
        if (floor == null) return;
        float cx = (floor.X1 + floor.X2) / 2;
        Item? Place(string key, float x, bool flip)
        {
            if (Put(key) is not { } it) return null;
            it.Pos = new Vector2(x, floor.Y); it.Vel = Vector2.Zero; it.OnGround = true; it.GroundHwnd = floor.Hwnd; it.Flip = flip;
            return it;
        }
        if (_simGear.Count == 0 || _simGear.Any(g => !_w.Items.Contains(g)))
        {
            _simGear.Clear();
            switch (sport)
            {
                case "soccer": _simGear.AddRange(new[] { Place("goal", cx - 400, true), Place("goal", cx + 400, false) }.OfType<Item>()); break;
                case "basketball": if (Place("hoop", cx + 250, false) is { } h) _simGear.Add(h); break;
                default: if (Place(sport == "tennis" ? "tennisnet" : "badmintonnet", cx, false) is { } n) _simGear.Add(n); break;
            }
        }
        if (_simGear.Count == 0) return;
        int need = sport switch { "soccer" => 6, "basketball" => 3, _ => 2 };
        _simRoster.RemoveAll(f => !_w.Figures.Contains(f) || f.Dead || f.Brain.Busy);
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && f.Visitor == VisitorKind.None && !f.Dead && !_simRoster.Contains(f) && !f.Brain.Busy))
            if (_simRoster.Count < need) _simRoster.Add(f);
        while (_simRoster.Count < need && _w.Figures.Count < World.MaxFigures && Spawn(null) is { } nf) { nf.SpawnT = 0.999f; _simRoster.Add(nf); }
        if (_simRoster.Count < need) return;
        int i = 0;
        foreach (var f in _simRoster) { if (f.Brain.Match == null && MathF.Abs(f.Base.Y - floor.Y) > 4) f.PlaceAt(floor, cx - 200 + 130 * i); i++; }
        _simStats ??= new MatchStats();
        _simRoster[0].Brain.StartMatchWith(_simGear[0], _simRoster, _w, _simStats);
    }

    void SimFinish()
    {
        if (_simStats != null) _simMetrics["sport"] = _simStats.Summary();
        if (_testEnd > 0) return;
        _testEnd = 1;
        SimCheck("no errors while running", _frameErrors == 0, string.Join(" | ", _errorTexts.Distinct().Take(5)));
        using (var me = System.Diagnostics.Process.GetCurrentProcess())
        {
            _simMetrics["memoryMB"] = me.PrivateMemorySize64 >> 20;
            SimCheck("memory stays reasonable", me.PrivateMemorySize64 < 1200L << 20, $"{me.PrivateMemorySize64 >> 20} MB");
        }
        double wall = (DateTime.Now - _simWall).TotalSeconds;
        var acts = _simActs.Values.ToList();
        _simMetrics["wallSeconds"] = Math.Round(wall, 1);
        _simMetrics["speed"] = Math.Round(_simDur / Math.Max(wall, 0.01), 1);
        _simMetrics["simMsAvg"] = _simMs.Count > 0 ? Math.Round(_simMs.Average(), 2) : 0;
        _simMetrics["simMsMax"] = _simMs.Count > 0 ? Math.Round(_simMs.Max(), 2) : 0;
        _simMetrics["figuresEnd"] = _w.Figures.Count;
        _simMetrics["figuresMax"] = _simMaxFigures;
        _simMetrics["itemsMax"] = _simMaxItems;
        _simMetrics["fights"] = _simFights;
        _simMetrics["draws"] = _simDraws;
        _simMetrics["activitiesPerFigure"] = acts.Count > 0 ? Math.Round(acts.Average(a => a.Seen.Count), 1) : 0;
        _simMetrics["switchesPerMinute"] = acts.Count > 0 ? Math.Round(acts.Average(a => a.Switches) / (_simDur / 60), 1) : 0;
        _simMetrics["activitiesPerPet"] = _simPetActs.Count > 0 ? Math.Round(_simPetActs.Values.Average(s => s.Count), 1) : 0;
        _simMetrics["activities"] = acts.SelectMany(a => a.Seen).Select(ActivityKind).Distinct().OrderBy(x => x).ToArray();
        var result = new { spec = SimSpecText(), seed = _simSeed, passed = _simFails.Count == 0, fails = _simFails, metrics = _simMetrics };
        File.WriteAllText(Path.Combine(AppPaths.DataDir, "simresult.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        World.Log($"simtest: {(_simFails.Count == 0 ? "passed" : _simFails.Count + " failed")} in {wall:0.0}s");
        SelfTestExitCode = _simFails.Count == 0 ? 0 : 1;
        ExitThread();
    }

    /// <summary>"Chatting with Bo" and "Chatting with Cy" are the same kind of thing.</summary>
    static string ActivityKind(string act)
    {
        int cut = act.IndexOfAny(new[] { ' ', '(', '…', ':' }, Math.Min(act.Length, 3));
        string head = cut > 0 ? act[..cut] : act;
        return head.Trim();
    }
}
