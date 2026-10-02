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
    int _focusCoins;

    const double TestSpeed = 6;

    string _albumFile = "", _questText = "", _memoName = "", _travellerName = "";
    double _perfAt = 5, _scriptLag;
    bool _sawRide, _sawPrint;
    int _notified = -1;

    void Check(string name, bool pass, string detail = "")
    {
        _checks.Add((name, pass, detail));
        World.Log($"selftest {(pass ? "PASS" : "FAIL")}: {name} {detail}");
    }

    /// <summary>Before anything starts: quiet, offline, nothing of yours touched; and a test mod to load.</summary>
    /// <summary>A fixed pretend desktop (1920×1080 at scale 1, three windows and a taskbar), so the checks give the
    /// same answers whatever's open (and whatever the screen size) on the machine running them.</summary>
    void SelfTestStage()
    {
        _r.Headless = true;
        _w.Scale = 1;
        _w.Env.MinHeadroom = 75;
        _w.Env.StageScreen(new Rectangle(0, 0, 1920, 1080), 48);
        var wins = new[] { new Rectangle(110, 560, 600, 300), new Rectangle(1010, 410, 760, 430), new Rectangle(560, 250, 460, 230) };
        for (int i = 0; i < wins.Length; i++)
            _w.Env.Staged!.Add(((IntPtr)(0x7F000200 + i), new Native.RECT { Left = wins[i].Left, Top = wins[i].Top, Right = wins[i].Right, Bottom = wins[i].Bottom }));
    }

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
              "jokes": ["a self-test joke"],
              "characters": [ { "name": "QA Captain", "color": "#1E88E5", "look": { "hat": "crown" } } ],
              "storytellers": [ { "key": "qa-calm", "name": "QA calm", "drama": 0.5 } ],
              "behaviours": [ { "key": "qa-ponder", "name": "Pondering (QA)", "weight": 0.01, "steps": [ { "say": "hmm, QA" }, { "act": "sitfloor", "seconds": 2 }, { "diary": "Pondered things (QA)." } ] } ],
              "events": [ { "key": "qa-fest", "title": "The QA fair", "decor": ["lantern"], "food": ["cake"] } ],
              "songs": [ { "title": "QA tune", "lyrics": "la\nla" } ]
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
        At(36, "line up", () =>
        {
            // Riders and their rides side by side on the ground (and fresh, and off shift, or work calls them back),
            // so this is about riding, not about finding the bike. Placing someone redraws them, so it's done early.
            foreach (var (i, key, x) in new[] { (0, "bike", 300f), (1, "gokart", 1500f) })
                if (Fig(i) is { } rider && _w.Items.FirstOrDefault(it => it.Def.Key == key) is { } veh && _w.Env.Below(x, 1000) is { } ground)
                {
                    rider.Brain.DebugTown(_w, "job", "None");
                    rider.Brain.Stamina = 1;
                    rider.PlaceAt(ground, x - 50);
                    veh.Pos = new Vector2(x + 40, ground.Y - 2); veh.Vel = Vector2.Zero; veh.OnGround = false;
                }
        });
        At(40, "outdoors", () =>
        {
            Fig(0)?.Brain.DebugTown(_w, "ride", "bike");
            Fig(1)?.Brain.DebugTown(_w, "ride", "gokart");
            Fig(2)?.Brain.DebugTown(_w, "swim", "pond");
            Fig(3)?.Brain.DebugTown(_w, "fish", "");
            Fig(5)?.Brain.DebugTown(_w, "dream", "");
        });
        At(52, "someone rides", () => Check("figures ride vehicles", _sawRide || _w.Figures.Any(f => f.Riding != null), string.Join(", ", _w.Figures.Select(f => f.Brain.Activity))));
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
        At(153, "album photo", () => _albumFile = TakeAlbumPhoto("test", "Self-test: everyone", _w.Figures.ToList(), _w.Pets.ToList()));
        At(159, "request", () =>
        {
            _questText = NewQuest(Fig(0), QuestKind.Thing);
            if (_settings.Quests.LastOrDefault() is { } q && ItemCatalog.Find(q.Target) is { } d) SpawnItem(d);
        });
        At(163, "history", () =>
        {
            string saved = ExportHistory();
            Check("the town's history is kept and exports", _settings.History.Count > 0 && saved.StartsWith("Saved") && Directory.GetFiles(AppPaths.PicturesDir, "*history*.html").Length > 0, $"{_settings.History.Count} events; {saved}");
        });
        At(164, "song", () =>
        {
            _settings.Songs.Add(new Song { Id = 99, Title = "QA song", Lyrics = "first line of the song\nsecond line" });
            Check("a song can be sung", SingNow(99).Contains("singing"));
        });
        At(167, "singing", () => Check("they sing the words", _w.Figures.Any(f => f.CurrentEmote?.Contains("line") == true || f.Brain.Singing), string.Join(", ", _w.Figures.Select(f => f.CurrentEmote))));
        At(168, "friend visit", () =>
        {
            // Over a loopback: we're both the sender and the friend.
            var loop = new Queue<(ulong, byte[])>();
            _p2pSend = (id, data) => { loop.Enqueue((42, data)); return true; };
            _p2pReceive = () => { var l = loop.ToList(); loop.Clear(); return l; };
            _travellerName = Fig(2)?.Name ?? "";
            Check("a figure can set off to visit a friend", Fig(2) is { } t && SendToVisit(t, 42, "QA friend").Contains("on the way") && _away.Count == 1);
        });
        At(169.5, "guest arrives", () => Check("a friend's figure arrives as a guest", _guests.Count == 1, string.Join(", ", _w.Figures.Where(f => f.Visitor == VisitorKind.Guest).Select(f => f.Name))));
        At(169, "taskbar village", () => { _settings.TownLayout = "strip"; ApplyLayout(); });
        At(178, "traveller home", () =>
        {
            var back = _w.Figures.FirstOrDefault(f => f.Name == _travellerName && f.Visitor == VisitorKind.None);
            Check("…and comes home with a postcard and stories", _away.Count == 0 && back != null && back.Brain.Diary.Any(d => d.Text.StartsWith("Visited")) && _w.Items.Any(i => i.Def.Key == "postcard"),
                  $"away {_away.Count}, back {back != null}, guests {_guests.Count}");
            _p2pSend = SteamHub.SendTo; _p2pReceive = SteamHub.Receive;
        });
        At(176, "village check", () =>
        {
            float top = _w.Env.BoundsAt(960).T;
            Check("the taskbar village keeps everyone in the strip", _w.Env.Platforms.All(p => p.Hwnd == IntPtr.Zero || p.Item != null) && _w.Figures.All(f => f.Base.Y >= top - 5),
                  string.Join(", ", _w.Figures.Where(f => f.Base.Y < top - 5).Select(f => $"{f.Name}@{f.Base.Y:0}")) + $" top {top:0}");
            _settings.TownLayout = "desktop"; ApplyLayout();
            _overlay.SetBehind(true);
            var desk = Overlay.DesktopHost();
            Check("live wallpaper sits just above the desktop", desk == IntPtr.Zero || Overlay.NextVisibleBelow(_overlay.Handle) == desk, desk == IntPtr.Zero ? "no desktop window (CI?)" : $"desktop {Env.ClassName(desk)}, above it {Env.ClassName(Native.GetWindow(desk, Native.GW_HWNDPREV))} {Native.GetWindow(desk, Native.GW_HWNDPREV)}, us {_overlay.Handle}, below us {Env.ClassName(Native.GetWindow(_overlay.Handle, Native.GW_HWNDNEXT))}");
            _overlay.SetBehind(false);
        });
        At(161, "community cap", () =>
        {
            Contribute("FISH_CAUGHT", 100); Contribute("FISH_CAUGHT", 5);
            Check("community goals cap what one player adds a day", _settings.ContribToday.GetValueOrDefault("FISH_CAUGHT") == 40, $"{_settings.ContribToday.GetValueOrDefault("FISH_CAUGHT")}");
        });
        At(162, "request granted", () => Check("a request is granted when you do it", _settings.Quests.LastOrDefault()?.Done == true, _questText));
        At(158, "album saved", () =>
        {
            string path = Path.Combine(AlbumDir, _albumFile);
            int w = 0;
            try { if (File.Exists(path)) { using var img = System.Drawing.Image.FromFile(path); w = img.Width; } } catch (Exception e) { Check("album photo decodes", false, e.Message); }
            Check("album photo saved and indexed", w == AlbumW + 72 && Album.Any(a => a.File == _albumFile), $"{_albumFile} {w}px");
        });
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
        At(531, "mod content", () =>
        {
            Check("mods can add characters, storytellers, festivals and songs", Mods.Characters.Any(c => c.fig.Name == "QA Captain") && Mods.Storytellers.Any(t => t.Key == "qa-calm") && Mods.Events.Any(e => e.Key == "qa-fest") && Mods.Songs.Count > 0,
                  string.Join("; ", Mods.Errors));
            string r = StartHappening("mod:qa-fest");
            Check("a mod's festival starts", r.StartsWith("started") && _w.Happening?.Title == "The QA fair", r);
            Check("a mod's character can join", SpawnModCharacter(Mods.Characters.FindIndex(c => c.fig.Name == "QA Captain")).Contains("on the way"));
        });
        At(533.5, "mod behaviour", () => { if (Fig(1) is { } fb && Mods.Behaviours.FirstOrDefault(b => b.Key == "qa-ponder") is { } mb) fb.Brain.StartBehaviour(mb); });
        At(534, "mod behaviour runs", () => Check("a mod's behaviour runs its steps", Fig(1)?.Brain.Activity == "Pondering (QA)", Fig(1)?.Brain.Activity ?? ""));
        At(542, "mod behaviour done", () => Check("…and finishes", Fig(1) is { } fb2 && fb2.Brain.Activity != "Pondering (QA)" && fb2.Brain.Diary.Any(d => d.Text.Contains("QA")), Fig(1)?.Brain.Activity ?? ""));
        At(533, "mod festival over", () => { if (_w.Happening is { } mh) EndHappening(mh, false); foreach (var f in _w.Figures.Where(f => f.Name.StartsWith("QA Captain")).ToList()) _w.RemoveFigure(f); });
        At(535, "casts", () =>
        {
            _castFigures = _w.Figures.Count;
            string a = SaveCastAs("QA one"), b = SwitchCast("QA two", true), c = SwitchCast("QA one", false);
            Check("cast switching keeps everyone", _w.Figures.Count == (int)_castFigures, $"{a} / {b} / {c}");
            Check("cast name clash refused", SwitchCast("QA two", true).StartsWith("There's already"));
        });
        At(545, "reminder", () => AddReminder("selftest reminder", DateTime.Now.AddSeconds(2), "none"));
        At(548, "chat joins", () =>
        {
            _settings.StreamOn = true;
            foreach (var t in new[] { "!join", "hello everyone http://spam.example", "!dance", "!weather snow" }) _chat.Enqueue(new ChatLine("qaviewer", "QAViewer", "#9146FF", t, false, false));
        });
        At(549.5, "chat in town", () =>
        {
            var v = _w.Figures.FirstOrDefault(f => f.Visitor == VisitorKind.Viewer);
            Check("a chatter can join the town and talk", v != null && v.Name == "QAViewer" && _viewers.ContainsKey("qaviewer"), v?.CurrentEmote ?? "nobody");
            Check("links in chat are hidden and weather isn't allowed by default", !(_w.Figures.Any(f => f.CurrentEmote?.Contains("spam.example") == true)) && !_w.Weather.Snowing);
        });
        At(556, "chat stays", () =>
        {
            Check("a chatter stays in town while they're around", _w.Figures.Any(f => f.Visitor == VisitorKind.Viewer));
            _chat.Enqueue(new ChatLine("qaviewer", "QAViewer", "", "!leave", false, false));
        });
        At(557.5, "chat leaves", () => { Check("…and leave", !_w.Figures.Any(f => f.Visitor == VisitorKind.Viewer)); _settings.StreamOn = false; });
        At(560, "voice", () =>
        {
            int items = _w.Items.Count;
            VoiceCommand("make a pizza");
            Check("spoken request makes a thing", _w.Items.Count == items + 1);
            Check("spoken words reach a figure", Fig(0) is { } f && VoiceCommand($"{f.Name}, tell me a joke").StartsWith(f.Name));
        });
        At(590, "reminder fired", () => Check("reminder fires", _settings.Reminders.All(r => r.Done)));
        At(592, "calm", () => { _settings.Calm = true; World.Calm = true; foreach (var f in _w.Figures) if (f.Brain.InFight) f.Brain.CalmDown(); });
        At(594, "focus", () => { StartFocus(1); _focusCoins = _w.Figures.Sum(f => f.Brain.Coins); });
        At(605, "focus quiet", () => Check("a focus session keeps the town calm", World.Focus && !_w.Figures.Any(f => f.Brain.InFight)));
        At(657, "focus done", () => Check("a focus session ends with a cheer and coins", !World.Focus && _w.Figures.Sum(f => f.Brain.Coins) > _focusCoins, $"{_settings.FocusSessions} sessions"));
        At(618, "habits", () => Check("figures learn habits from experience", _w.Figures.Any(f => f.Brain.Learned.Count > 0), string.Join(", ", _w.Figures.SelectMany(f => f.Brain.Learned.Keys).Distinct().Take(8))));
        At(620, "calm holds", () => { Check("no fights in calm mode", !_w.Figures.Any(f => f.Brain.InFight)); _settings.Calm = World.Calm = false; });
        At(625, "everything at once", () =>
        {
            StartHappening("festival");
            foreach (var f in _w.Figures.Take(3)) f.Brain.DebugTown(_w, "ride", "");
            _w.Weather.Start(WeatherKind.Storm, _clock.Elapsed.TotalSeconds, _w.Rng, _w);
        });
        At(640, "toybox", () =>
        {
            SetGravity("moon"); GiantBall(); Earthquake(); Gust(); Confetti(); SlowMotion();
            Check("moon gravity makes things lighter", World.GravityMul < 0.5f && _w.Figures.All(f => f.Gravity < 1000 * f.S));
        });
        At(652, "toybox over", () => { SetGravity("normal"); Check("gravity goes back to normal", World.GravityMul == 1); });
        At(600, "pranks", () =>
        {
            // A local memes folder (no network in the self-test), a meme that fits ("rain"), muddy feet after rain.
            string dir = Path.Combine(AppPaths.DataDir, "my memes");
            Directory.CreateDirectory(dir);
            using (var bmp = new System.Drawing.Bitmap(120, 90)) { using (var g = System.Drawing.Graphics.FromImage(bmp)) g.Clear(System.Drawing.Color.Orange); bmp.Save(Path.Combine(dir, "rain day.png")); }
            _settings.Pranks = true; _settings.MemeFolder = dir;
            _w.Weather.Start(WeatherKind.Rain, _clock.Elapsed.TotalSeconds, _w.Rng, _w);
            _w.Weather.Intensity = 1;
            MakeMemeSoon();
        });
        At(605, "walk in the mud", () => { Fig(2)?.Brain.DebugTown(_w, "walk", "500"); Fig(3)?.Brain.DebugTown(_w, "walk", "-500"); });
        At(604, "rain stops", () => { _w.Weather.Start(WeatherKind.Clear, _clock.Elapsed.TotalSeconds, _w.Rng, _w); _w.Weather.Intensity = 0; });
        At(612, "pranks happen", () =>
        {
            Check("a meme from your folder is ready", _memeReady != null, _memeReady?.Text ?? "");
            if (TakeMeme() is { } path && ItemCatalog.Find("memeframe") is { } fd && SpawnItem(fd) is { } frame) { frame.Label = path; PrankLeft(frame, 1); }
            Check("muddy footprints after the rain", _sawPrint || _w.Items.Any(i => i.Def.Key == "mudprint"), $"{_w.Figures.Count(f => f.Mode == Mode.Control && MathF.Abs(f.Vel.X) > 40)} walking");
        });
        At(612.5, "listener", () =>
        {
            _settings.DevHooks = true;
            StartDevHooks();
            if (_hookListener != null) Task.Run(() => _notified = Notify("tests-passed", "QA"));
        });
        At(613.8, "listener heard", () =>
        {
            if (_hookListener == null) { World.Log("selftest: port in use (Doodlefolk's own listener?), listener check skipped"); return; }
            Check("tools can tell the town (127.0.0.1 listener)", _notified == 0 && _lastBuild == "pass", $"notify exit {_notified}, last build {_lastBuild}");
            _settings.DevHooks = false; StopDevHooks();
        });
        At(614, "your work", () =>
        {
            DevReact("build-failed", "oops");
            Check("a failing build is noticed (and memes would know)", _lastBuild == "fail" && MemeNow().Tags.Contains("buildfail"));
        });
        At(616, "pranks off", () => { _settings.Pranks = false; _settings.MemeFolder = ""; });
        At(654, "pass away", () => { _memoName = Fig(0)?.Name ?? ""; Fig(0)?.PassAway(_w); });
        At(672, "remembered", () =>
        {
            Check("someone who dies is remembered (headstone and memorial)", _settings.Memorials.Any(m => m.Name == _memoName)
                  && _w.Items.Any(i => i.Def.Key == "memorial" && i.Label == _memoName) && !_w.Figures.Any(f => f.Name == _memoName), _memoName);
            if (_settings.Memorials.LastOrDefault() is { } m) GhostOf(m);
        });
        At(676, "ghost", () => Check("their ghost can come back to visit", _w.Figures.Any(f => f.Spirit && f.Name == _memoName)));
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
        if (t > 40 && _w.Figures.Any(f => f.Riding != null)) _sawRide = true;
        if (t > _perfAt) { _perfAt = t + 30; World.Log($"selftest perf: {_fps} fps, sim {_msSim:0.0} ms, render {_msRender:0.0} ms (draw {_msDraw:0.0}), refresh {_msRefresh:0.0} ms, {_w.Figures.Count} figures, {_w.Items.Count} items"); }
        // One step a frame, and if a step runs late (the app stalled and the clock jumped), everything after it moves
        // back by the same amount, so the steps keep their spacing and each gets the time it was given.
        if (_scriptAt < _script.Count && t >= _script[_scriptAt].at + _scriptLag)
        {
            var (at, name, act) = _script[_scriptAt++];
            if (t - (at + _scriptLag) > 0.5) _scriptLag = t - at;
            World.Log("selftest step: " + name);
            try { act(); }
            catch (Exception e) { Check($"step \"{name}\" ran", false, e.ToString()); }
        }
        if (t > _nextInvariant) { _nextInvariant = t + 0.5; SelfTestInvariants(t); }
        // A hard stop, in case a step never comes.
        if (t > 900 + _scriptLag && _testEnd == 0) FinishSelfTest();
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
