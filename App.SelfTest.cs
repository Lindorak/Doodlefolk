using System.Numerics;
using System.Text;

namespace Doodlefolk;

/// <summary>The self-test (Doodlefolk.exe --selftest [--data dir]): the real app, sped up, hidden, with its own
/// throwaway data. It plays through a script that touches every feature (jobs, building, rides, swimming, fishing,
/// each town event, people leaving mid-event, resizing, casts, clips, reminders, voice commands, mods, calm mode,
/// night lights) while checking that nothing breaks: no errors, nothing at an impossible position, riders and swimmers
/// consistent, events ending. It writes selftest-report.txt in the data folder and exits 0 (pass) or 1 (fail).</summary>
sealed partial class App
{
    readonly bool _selfTest;
    public int SelfTestExitCode { get; private set; } = 1;
    int _frameErrors;
    readonly List<string> _errorTexts = new();
    readonly List<(string name, bool pass, string detail)> _checks = new();
    readonly List<(double at, string name, Action act)> _script = new();
    int _scriptAt;
    double _testStart = -1, _nextInvariant, _testEnd;
    readonly Dictionary<int, (string act, double since)> _stuck = new();
    readonly HashSet<string> _warned = new();
    float _castFigures;

    const double TestSpeed = 6;

    void Check(string name, bool pass, string detail = "")
    {
        _checks.Add((name, pass, detail));
        World.Log($"selftest {(pass ? "PASS" : "FAIL")}: {name} {detail}");
    }

    /// <summary>Before anything starts: quiet, offline, nothing of yours touched; and a test mod to load.</summary>
    void SelfTestPrepare()
    {
        World.LogAlways = true;
        World.LogFile = Path.Combine(AppPaths.DataDir, "events.log");
        _settings.SoundOn = false;
        _settings.CheckUpdates = false;
        _settings.NoticeDownloads = false;
        _settings.NoticeFrustration = false;
        _settings.NoticeTyping = false;
        _settings.Notifications = false;
        _settings.ScreenTerrain = _settings.ScreenReact = _settings.ScreenLinks = _settings.ScreenMedia = false;
        _settings.WeatherMode = "off";
        _settings.Events = false;
        _settings.LassoCursor = false;
        _settings.VoiceInput = false;
        _settings.AiChat = false;
        _settings.StartWithWindows = false;
        Directory.CreateDirectory(Mods.Dir);
        File.WriteAllText(Path.Combine(Mods.Dir, "selftest.json"), """
            {
              "items": [ { "key": "qa-lamp", "name": "QA lamp", "w": 12, "h": 28, "verbs": ["Dance"],
                           "svg": "<svg viewBox='0 0 12 28'><rect x='3' y='24' width='6' height='4' fill='#5D6670'/><path d='M4 24 L2 8 L6 2 L10 8 L8 24 Z' fill='main'/></svg>" } ],
              "hats": [ { "key": "qa-hat", "name": "QA hat", "svg": "<svg viewBox='-12 -16 24 8'><path d='M-8 -9 L0 -15 L8 -9 Z' fill='#E53935'/></svg>" } ],
              "names": { "cats": ["Testy"] },
              "jokes": ["a self-test joke"]
            }
            """);
    }

    Figure? Fig(int i) => _w.Figures.Count > i ? _w.Figures[i] : null;
    Item? Put(string key) => ItemCatalog.Find(key) is { } d ? SpawnItem(d) : null;

    void BuildScript()
    {
        void At(double t, string name, Action a) => _script.Add((t, name, a));
        At(1, "cast", () =>
        {
            while (_w.Figures.Count < 6) Spawn(null);
            foreach (var k in new[] { "shopstall", "foodcart", "stage", "schoolboard", "bike", "skateboard", "gokart", "pond", "pool", "lamp", "fairylights", "lantern", "campfire", "bed", "couch", "radio", "qa-lamp" })
                if (Put(k) == null) Check($"spawn {k}", false, "couldn't place it");
            foreach (var k in Enum.GetValues<PetKind>()) SpawnPet(k, quiet: true);
            Check("mods load", ItemCatalog.Find("qa-lamp") != null && Look.Find(Look.Hats, "mod-qa-hat") != null && Mods.Jokes.Contains("a self-test joke"), string.Join("; ", Mods.Errors));
        });
        At(6, "jobs", () =>
        {
            var jobs = new[] { "Shopkeeper", "Chef", "Entertainer", "Teacher", "Builder" };
            for (int i = 0; i < jobs.Length && Fig(i) is { } f; i++) f.Brain.DebugTown(_w, "job", jobs[i]);
            for (int i = 0; i < 4 && Fig(i) is { } f; i++) f.Brain.DebugTown(_w, "work", "");
            Fig(4)?.Brain.DebugTown(_w, "build", "");
        });
        At(40, "outdoors", () =>
        {
            Fig(0)?.Brain.DebugTown(_w, "ride", "bike");
            Fig(1)?.Brain.DebugTown(_w, "ride", "gokart");
            Fig(2)?.Brain.DebugTown(_w, "swim", "pond");
            Fig(3)?.Brain.DebugTown(_w, "fish", "");
            Fig(5)?.Brain.DebugTown(_w, "dream", "");
        });
        At(52, "someone rides", () => Check("figures ride vehicles", _w.Figures.Any(f => f.Riding != null), string.Join(", ", _w.Figures.Select(f => f.Brain.Activity))));
        At(53, "knock riders off", () =>
        {
            foreach (var f in _w.Figures.Where(f => f.Riding != null || f.Swimming).ToList()) f.GoRagdoll(new Vector2(300, -500) * _w.Scale);
        });
        At(53.3, "riders let go", () =>
        {
            Check("vehicles released when a rider is knocked off", _w.Items.Where(i => i.IsVehicle).All(i => i.Rider == null || i.Rider.Mode == Mode.Control));
            Check("swimmers leave the water when knocked out of it", _w.Items.All(i => i.Swimmers == 0 || _w.Figures.Any(f => f.Swimming)));
        });
        At(80, "race", () => Check("race starts", StartHappening("race").StartsWith("started"), _w.Happening?.Title ?? ""));
        At(84, "racer leaves", () => { if (_w.Happening?.Who.FirstOrDefault() is { } r) _w.RemoveFigure(r); });
        At(150, "race over", () => Check("race finishes after a racer leaves", _w.Happening == null));
        At(152, "refill", () => { while (_w.Figures.Count < 6) Spawn(null); });
        At(155, "talent", () => Check("talent show starts", StartHappening("talent").StartsWith("started")));
        At(175, "resize an act", () => { if (_w.Happening?.Who.FirstOrDefault() is { } a) ResizeFigure(a, 1.3f); });
        At(300, "talent over", () => Check("talent show finishes (with a resized act)", _w.Happening == null, _w.Happening?.Title ?? ""));
        At(302, "festival at night", () => { _w.NightOverride = 0.85f; Check("festival starts", StartHappening("festival").StartsWith("started")); });
        At(320, "lights", () => Check("lamps light people at night", _w.Items.Any(i => i.Light() != null)));
        At(500, "festival over", () => { _w.NightOverride = null; Check("festival finishes", _w.Happening == null); });
        At(505, "clip", () => StartRecording(2, AppPaths.PicturesDir));
        At(530, "clip saved", () =>
        {
            var gif = Directory.Exists(AppPaths.PicturesDir) ? Directory.GetFiles(AppPaths.PicturesDir, "*.gif").FirstOrDefault() : null;
            int frames = 0;
            try
            {
                if (gif != null)
                {
                    using var img = System.Drawing.Image.FromFile(gif);
                    frames = img.GetFrameCount(new System.Drawing.Imaging.FrameDimension(img.FrameDimensionsList[0]));
                }
            }
            catch (Exception e) { Check("clip decodes", false, e.Message); }
            Check("clip saved and decodes", frames >= 10, $"{frames} frames");
        });
        At(535, "casts", () =>
        {
            _castFigures = _w.Figures.Count;
            string a = SaveCastAs("QA one"), b = SwitchCast("QA two", true), c = SwitchCast("QA one", false);
            Check("cast switching keeps everyone", _w.Figures.Count == (int)_castFigures, $"{a} / {b} / {c}");
            Check("cast name clash refused", SwitchCast("QA two", true).StartsWith("There's already"));
        });
        At(545, "reminder", () => AddReminder("selftest reminder", DateTime.Now.AddSeconds(2), "none"));
        At(560, "voice", () =>
        {
            int items = _w.Items.Count;
            VoiceCommand("make a pizza");
            Check("spoken request makes a thing", _w.Items.Count == items + 1);
            Check("spoken words reach a figure", Fig(0) is { } f && VoiceCommand($"{f.Name}, tell me a joke").StartsWith(f.Name));
        });
        At(590, "reminder fired", () => Check("reminder fires", _settings.Reminders.All(r => r.Done)));
        At(592, "calm", () => { _settings.Calm = true; World.Calm = true; foreach (var f in _w.Figures) if (f.Brain.InFight) f.Brain.CalmDown(); });
        At(618, "habits", () => Check("figures learn habits from experience", _w.Figures.Any(f => f.Brain.Learned.Count > 0), string.Join(", ", _w.Figures.SelectMany(f => f.Brain.Learned.Keys).Distinct().Take(8))));
        At(620, "calm holds", () => { Check("no fights in calm mode", !_w.Figures.Any(f => f.Brain.InFight)); _settings.Calm = World.Calm = false; });
        At(625, "everything at once", () =>
        {
            StartHappening("festival");
            foreach (var f in _w.Figures.Take(3)) f.Brain.DebugTown(_w, "ride", "");
            _w.Weather.Start(WeatherKind.Storm, _clock.Elapsed.TotalSeconds, _w.Rng, _w);
        });
        At(680, "mass removal", () => { foreach (var f in _w.Figures.ToList()) _w.RemoveFigure(f); });
        At(690, "empty world", () => Check("an event ends when everyone's gone", _w.Happening == null));
        At(695, "done", FinishSelfTest);
    }

    /// <summary>Every half (simulated) second: is the world still sane?</summary>
    void SelfTestInvariants(double t)
    {
        bool Finite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
        foreach (var f in _w.Figures)
        {
            if (!Finite(f.Base) || !f.Jt.All(Finite)) Warn($"figure {f.Name} has an impossible position");
            if (f.Riding is { } v && (v.Rider != f || !_w.Items.Contains(v))) Warn($"{f.Name} rides a bike that doesn't know it");
            var act = f.Brain.Activity;
            if (!_stuck.TryGetValue(f.Id, out var s) || s.act != act) _stuck[f.Id] = (act, t);
            else if (t - s.since > 300 && !act.StartsWith("Napping") && !act.StartsWith("Sleep") && !act.StartsWith("Knocked")) Warn($"{f.Name} has been \"{act}\" for {t - s.since:0}s");
        }
        foreach (var p in _w.Pets) if (!Finite(p.Pos)) Warn($"pet {p.Name} has an impossible position");
        foreach (var it in _w.Items)
        {
            if (!Finite(it.Pos)) Warn($"{it.Def.Key} has an impossible position");
            if (it.Rider is { } r && (r.Riding != it || !_w.Figures.Contains(r))) Warn($"{it.Def.Key} is ridden by someone who isn't riding it");
            if (it.Rider is { } r2 && r2.Mode != Mode.Control) Warn($"{it.Def.Key} is stuck to {r2.Name}, who has fallen off");
            int swimmers = _w.Figures.Count(f => f.Swimming && f.Brain.WaterItem == it);
            if (it.Swimmers != swimmers) Warn($"{it.Def.Key} thinks {it.Swimmers} are swimming, really {swimmers}");
        }
        if (_w.Happening is { } h && h.T > 400) Warn($"{h.Title} has been going for {h.T:0}s");
    }

    void Warn(string what)
    {
        if (_warned.Add(what)) Check("invariant: " + what, false);
    }

    void SelfTestFrame(double now)
    {
        if (_testStart < 0) { _testStart = now; BuildScript(); }
        double t = now - _testStart;
        while (_scriptAt < _script.Count && t >= _script[_scriptAt].at)
        {
            var (_, name, act) = _script[_scriptAt++];
            World.Log("selftest step: " + name);
            try { act(); }
            catch (Exception e) { Check($"step \"{name}\" ran", false, e.ToString()); }
        }
        if (t > _nextInvariant) { _nextInvariant = t + 0.5; SelfTestInvariants(t); }
        // A hard stop, in case a step never comes.
        if (t > 900 && _testEnd == 0) FinishSelfTest();
    }

    void FinishSelfTest()
    {
        if (_testEnd > 0) return;
        _testEnd = _clock.Elapsed.TotalSeconds;
        Check("no errors while running", _frameErrors == 0, string.Join(" | ", _errorTexts.Take(5)));
        using (var me = System.Diagnostics.Process.GetCurrentProcess())
            Check("memory stays reasonable", me.PrivateMemorySize64 < 900L << 20, $"{me.PrivateMemorySize64 >> 20} MB private");
        var sb = new StringBuilder();
        sb.AppendLine($"Doodlefolk self-test, {DateTime.Now:yyyy-MM-dd HH:mm}, version {VersionText}");
        foreach (var (name, pass, detail) in _checks) sb.AppendLine($"{(pass ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        int failed = _checks.Count(c => !c.pass);
        sb.AppendLine(failed == 0 ? $"ALL {_checks.Count} CHECKS PASSED" : $"{failed} OF {_checks.Count} CHECKS FAILED");
        File.WriteAllText(Path.Combine(AppPaths.DataDir, "selftest-report.txt"), sb.ToString());
        SelfTestExitCode = failed == 0 ? 0 : 1;
        ExitThread();
    }
}
