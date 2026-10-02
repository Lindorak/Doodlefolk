using System.Drawing;
using System.Numerics;
using Vortice.Mathematics;
using static Doodlefolk.Native;

namespace Doodlefolk;

/// <summary>The trailer (Doodlefolk.exe --trailer out.mp4): the real app filming itself. It builds a pretend desktop
/// (wallpaper, a few windows, a taskbar; none of your real windows), plays a script through the town's best moments
/// with a moving camera and hand-drawn captions, steps time exactly one video frame at a time, and writes a 1080p MP4
/// with a synthesised soundtrack (an original tune plus the app's own sound effects, mixed where they happened).
/// Its data lives in a throwaway folder, like the self-test.</summary>
sealed partial class App
{
    readonly bool _trailer;
    string _trailerPath = "";
    string _trailerTagline = "Coming soon to Steam";
    const int TW = 1920, TH = 1080, TFps = 30;
    const double TrailerLength = 46;
    int _tFrame;
    Mp4Writer? _mp4;
    byte[]? _tPixels;
    readonly List<(double t, Sfx s, float x, float vol, float pitch)> _tSounds = new();
    readonly List<(double at, string name, Action act)> _tScript = new();
    int _tScriptAt;
    readonly List<(double t, float minW, string[] who)> _tShots = new();
    RectangleF _tCam = new(0, 0, TW, TH);
    readonly List<(double t0, double t1, string text, string sub, bool big)> _tCaptions = new();
    // The pretend windows: title, rectangle, accent colour.
    readonly List<(string title, RectangleF r, Color4 accent)> _tWindows = new();
    float _tNight;
    double _tCapMs, _tEncMs, _tFireAt;
    DateTime _tWall = DateTime.Now;

    void TrailerPrepare(string[] args)
    {
        int i = Math.Max(Math.Max(Array.IndexOf(args, "--trailer"), Array.IndexOf(args, "--cardart")), Array.IndexOf(args, "--animsheet"));
        if (_animSheet && i + 2 < args.Length && !args[i + 2].StartsWith("--")) _animFilter = args[i + 2];
        _trailerPath = Path.GetFullPath(i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : _cardArt ? "Doodlefolk card art" : "Doodlefolk-trailer.mp4");
        int k = Array.IndexOf(args, "--tagline");
        if (k >= 0 && k + 1 < args.Length) _trailerTagline = args[k + 1];
        World.LogAlways = true;
        World.LogFile = Path.Combine(AppPaths.DataDir, "events.log");
        _settings.SoundOn = false; _settings.CheckUpdates = false; _settings.NoticeDownloads = false; _settings.NoticeFrustration = false;
        _settings.NoticeTyping = false; _settings.Notifications = false; _settings.ScreenTerrain = _settings.ScreenReact = _settings.ScreenLinks = _settings.ScreenMedia = false;
        _settings.WeatherMode = "off"; _settings.Events = false; _settings.Visitors = false; _settings.LassoCursor = false; _settings.Theme = "paper";
        _settings.Gfx = GfxSettings.For("ultra");
        Ui.Update("paper");
        World.SoundTap = (s, at, vol, pitch) => _tSounds.Add((_tFrame / (double)TFps, s, at.X, vol, pitch));
    }

    /// <summary>After the window and renderer exist: the stage and the cast.</summary>
    void TrailerStage()
    {
        _w.Scale = 1.5f;
        _w.Env.MinHeadroom = 75 * _w.Scale;
        _w.Env.StageScreen(new Rectangle(0, 0, TW, TH), 48);
        _tWindows.Add(("Notes", new RectangleF(110, 560, 600, 300), M.Hex(0xFDD835)));
        _tWindows.Add(("Photos", new RectangleF(1010, 410, 760, 430), M.Hex(0x1E88E5)));
        _tWindows.Add(("Music", new RectangleF(560, 250, 460, 230), M.Hex(0xE53935)));
        SyncStagedWindows();
        _fakeCursor = new Vector2(-5000, -5000);
        _tPixels = new byte[TW * TH * 4];
        _mp4 = new Mp4Writer(_trailerPath, TW, TH, TFps, 12_000_000, audio: true);
        BuildTrailerScript();
    }

    void SyncStagedWindows()
    {
        _w.Env.Staged!.Clear();
        for (int i = 0; i < _tWindows.Count; i++)
        {
            var r = _tWindows[i].r;
            _w.Env.Staged.Add(((IntPtr)(0x7F000100 + i), new RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)r.Right, Bottom = (int)r.Bottom }));
        }
    }

    Figure? TF(string name) => _w.Figures.FirstOrDefault(f => f.Name == name);
    Pet? TP(PetKind k) => _w.Pets.FirstOrDefault(p => p.Kind == k);

    Item? TPut(string key, float x, float y = 1031)
    {
        if (ItemCatalog.Find(key) is not { } d || SpawnItem(d) is not { } it) return null;
        it.Pos = new Vector2(x, y); it.Vel = Vector2.Zero; it.OnGround = false;
        return it;
    }

    Figure? TCast(string name, string colourHex, float x, float y, string hat = "", float size = 1)
    {
        if (_w.Env.Below(x, y - 5) is not { } plat) return null;
        var f = new Figure(Settings.ParseHex(colourHex), name, _w.Scale * size, Personality.Random(_w.Rng), _w.Rng) { SizeMul = size };
        if (hat.Length > 0) f.Look.Hat = hat;
        f.PlaceAt(plat, x);
        _w.Figures.Add(f);
        return f;
    }

    void BuildTrailerScript()
    {
        void At(double t, Action a) => _tScript.Add((t, $"{t:0.0}", a));
        // A shot: from time t, frame these figures and pets (by name; "*" = everyone; none = the whole screen).
        void Shot(double t, float minW, params string[] who) => _tShots.Add((t, minW, who));
        void Say(double t0, double t1, string text, string sub = "", bool big = false) => _tCaptions.Add((t0, t1, text, sub, big));
        void Cmd(string line) => RunCommand(line);

        // ---- 0: title; everyone sketches in ----
        Shot(0, TW);
        Say(0.3, 4.3, "Doodlefolk", "little people who live on your desktop", big: true);
        At(0.0, () =>
        {
            foreach (var f in _w.Figures.ToList()) _w.RemoveFigure(f);
            TPut("couch", 300); TPut("radio", 470); TPut("campfire", 1650); TPut("lamp", 1820); TPut("fairylights", 1460);
            TPut("hamsterwheel", 1530, 405);
        });
        At(0.5, () => TCast("Mo", "#E53935", 420, 1031, "cap"));
        At(0.9, () => TCast("Bea", "#1E88E5", 320, 559, "bow"));
        At(1.3, () => TCast("Rex", "#43A047", 1240, 409, "crown"));
        At(1.7, () => TCast("Lou", "#FB8C00", 760, 249, "beanie"));
        At(2.1, () => TCast("Juni", "#8E24AA", 1500, 1031, "flowercrown"));
        At(2.6, () =>
        {
            foreach (var (k, x) in new[] { (PetKind.Cat, 900f), (PetKind.Dog, 1100f), (PetKind.Parrot, 1380f), (PetKind.Rabbit, 640f), (PetKind.Hamster, 1530f) })
            {
                var p = SpawnPet(k, quiet: true);
                p.Pos = new Vector2(x, k == PetKind.Hamster ? 400 : 1000);
                if (TF("Mo") is { } mo) p.Owner = mo;
            }
        });

        // ---- 1: they live on your windows (and ride them) ----
        Say(4.6, 10.8, "They live on your windows");
        Shot(4.6, 1150, "Lou", "Bea", "Mo");
        At(5.0, () => { foreach (var f in _w.Figures) f.Brain.Boredom = 1; Cmd("town Mo ride bike"); });
        At(5.2, () => { if (TF("Bea") is { } b) b.Brain.DebugTown(_w, "boost", ""); });
        // Drag the Music window (with Lou on it) across the screen.
        At(6.2, () => _tDrag = (2, new Vector2(320, 0), 6.2, 9.0));

        // ---- 2: friends, rivals… and crushes ----
        Say(11, 16.8, "Friends, rivals… and crushes");
        Shot(11, 820, "Mo", "Bea", "Rex", "Lou");
        At(11.0, () =>
        {
            foreach (var (n, x) in new[] { ("Mo", 360f), ("Bea", 470f), ("Rex", 640f), ("Lou", 760f) }) if (TF(n) is { } f && _w.Env.Below(x, 1020) is { } fl) { f.PlaceAt(fl, x); f.SpawnT = 0.999f; }
            Cmd("love Mo Bea 0.95"); Cmd("love Bea Mo 0.95"); Cmd("confess Mo");
        });
        At(12.5, () => { Cmd("Rex spar Lou"); });

        // ---- 3: pets with real needs ----
        Say(17, 22.8, "Pets with real needs", "feed them, walk them, teach them tricks");
        Shot(17, 820, "pets");
        At(17.2, () =>
        {
            float px = 880;
            foreach (var p in _w.Pets) { p.Hunger = 0.9f; p.Attention = 0.9f; if (p.Kind != PetKind.Hamster) { p.Pos = new Vector2(px, 1000); p.Vel = Vector2.Zero; px += 90; } }
            if (TP(PetKind.Dog) is { } dog) Cmd($"petop {dog.Name} trick");
            TPut("foodbowl", 960); TPut("waterbowl", 1040); TPut("petbed", 1180);
        });
        At(19.5, () => { if (TP(PetKind.Cat) is { } cat) Cmd($"petop {cat.Name} treat"); });

        // ---- 4: a whole town ----
        Say(23, 28.8, "A whole town at work", "jobs, coins, school, building");
        At(22.9, () =>
        {
            TPut("shopstall", 260); TPut("foodcart", 520); TPut("stage", 1450); TPut("schoolboard", 900);
            Cmd("town Mo job Shopkeeper"); Cmd("town Bea job Chef"); Cmd("town Juni job Entertainer"); Cmd("town Lou job Teacher"); Cmd("town Rex job Builder");
        });
        At(23.1, () => { foreach (var n in new[] { "Mo", "Bea", "Juni", "Lou" }) Cmd($"town {n} work"); Cmd("town Rex build"); });
        Shot(23, 1300, "*");

        // ---- 5: ride, swim and fish ----
        Say(29, 34.3, "Ride, swim and fish");
        At(28.9, () =>
        {
            foreach (var it in _w.Items.Where(i => i.Def.Key is "shopstall" or "foodcart" or "stage" or "schoolboard" or "buildsite" or "couch" or "radio").ToList()) _w.RemoveItem(it);
            TPut("pond", 700); TPut("gokart", 1250);
            foreach (var n in new[] { "Mo", "Bea", "Juni", "Lou", "Rex" }) Cmd($"town {n} job None");
        });
        At(29.4, () => { Cmd("town Rex ride gokart"); Cmd("town Lou swim pond"); Cmd("town Juni fish"); Cmd("town Bea ride bike"); });
        Shot(29, 900, "Rex", "Lou", "Juni", "Bea");

        // ---- 6: festivals, fireworks and music ----
        Say(34.5, 40.6, "Festivals, fireworks and late nights");
        At(34.4, () => { _w.NightOverride = 0.88f; StartHappening("festival"); });
        Shot(34.5, 1100, "*");

        // ---- 7: the end card ----
        Say(40.8, TrailerLength, "Doodlefolk", _trailerTagline, big: true);
        Shot(40.8, TW);
        _tScript.Sort((a, b) => a.at.CompareTo(b.at));
    }

    (int index, Vector2 by, double t0, double t1)? _tDrag;

    void TrailerTick(double t)
    {
        if (_simRun) return;
        while (_tScriptAt < _tScript.Count && t >= _tScript[_tScriptAt].at)
        {
            try { _tScript[_tScriptAt].act(); }
            catch (Exception e) { World.Log($"trailer step {_tScript[_tScriptAt].name}: {e}"); }
            _tScriptAt++;
        }
        // A window being dragged across the screen (they ride it).
        if (_tDrag is { } d)
        {
            float k = (float)Math.Clamp((t - d.t0) / (d.t1 - d.t0), 0, 1);
            float e = k * k * (3 - 2 * k);
            var (title, r, accent) = _tWindows[d.index];
            var start = _tDragFrom ??= r.Location;
            _tWindows[d.index] = (title, new RectangleF(start.X + d.by.X * e, start.Y + d.by.Y * e, r.Width, r.Height), accent);
            if (k >= 1) { _tDrag = null; _tDragFrom = null; }
        }
        SyncStagedWindows();
        _tNight = _w.NightOverride ?? 0;
        // The festival's fireworks, where the camera can see them.
        if (!_cardArt && !_animSheet && t > 35 && t < TrailerLength - 1 && t > _tFireAt)
        {
            _tFireAt = t + _w.Rng.Range(0.25f, 0.55f);
            var at = new Vector2(_tCam.X + _tCam.Width * _w.Rng.Range(0.15f, 0.85f), _tCam.Y + _tCam.Height * _w.Rng.Range(0.12f, 0.4f));
            var col = new[] { new Color4(1, 0.3f, 0.3f, 1), new Color4(0.3f, 0.8f, 1, 1), new Color4(1, 0.85f, 0.2f, 1), new Color4(0.7f, 0.4f, 1, 1), new Color4(0.4f, 1, 0.5f, 1) }[_w.Rng.Next(5)];
            // A ring of little bursts round a bright centre reads as a firework.
            float rad = _w.Rng.Range(45, 75) * _w.Scale;
            for (int i = 0; i < 10; i++)
            {
                float ang = i * MathF.Tau / 10 + _w.Rng.Range(-0.15f, 0.15f);
                _w.Fx.Spark(at + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * rad, _w.Scale * 0.9f, _w.Rng, 1.6f, col);
            }
            _w.Fx.Spark(at, _w.Scale * 1.2f, _w.Rng, 2f, new Color4(1, 0.97f, 0.85f, 1));
            World.Play(Sfx.Thud, at, 0.3f, 1.8f);
        }
    }

    PointF? _tDragFrom;

    /// <summary>The camera: frames the current shot's cast (wherever they've wandered), keeps to the screen,
    /// and eases from one framing to the next like a hand-held camera operator.</summary>
    RectangleF TrailerCamera(double t)
    {
        var shot = _tShots.LastOrDefault(s => s.t <= t);
        if (shot.who == null) return _tCam;
        var pts = new List<Vector2>();
        foreach (var name in shot.who)
        {
            if (name == "*") pts.AddRange(_w.Figures.Select(f => f.Base));
            else if (name == "pets") pts.AddRange(_w.Pets.Where(p => p.Kind is PetKind.Dog or PetKind.Cat or PetKind.Rabbit).Select(p => p.Pos));
            else if (TF(name) is { } f) pts.Add(f.Base);
        }
        float w = Math.Min(TW, shot.minW);
        Vector2 c = new(TW / 2f, TH / 2f);
        if (pts.Count > 0 && shot.minW < TW)
        {
            float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y) - 120 * _w.Scale, y1 = pts.Max(p => p.Y);
            w = Math.Clamp(Math.Max(shot.minW, Math.Max((x1 - x0) * 1.35f, (y1 - y0) * 1.5f * TW / TH)), 300, TW);
            // Keep the ground low in the frame (captions sit up top), unless someone's high up.
            float hh = w * TH / TW;
            c = new((x0 + x1) / 2, y1 - hh * 0.3f);
            if (y0 < c.Y - hh * 0.42f) c.Y = (y0 + y1) / 2 + hh * 0.05f;
        }
        float h = w * TH / TW;
        var want = new RectangleF(Math.Clamp(c.X - w / 2, 0, TW - w), Math.Clamp(c.Y - h / 2, 0, TH - h), w, h);
        // Cut (snap) on the first frame of a shot that changes scale a lot; otherwise ease.
        bool cut = t - shot.t < 1.0 / TFps && Math.Abs(want.Width - _tCam.Width) > 300;
        float k = cut ? 1 : 1 - MathF.Exp(-2.2f / TFps);
        _tCam = new RectangleF(_tCam.X + (want.X - _tCam.X) * k, _tCam.Y + (want.Y - _tCam.Y) * k, _tCam.Width + (want.Width - _tCam.Width) * k, _tCam.Height + (want.Height - _tCam.Height) * k);
        return _tCam;
    }

    /// <summary>Called once per video frame (after the frame's simulation): film it.</summary>
    void TrailerFrame()
    {
        if (_cardArt) { CardArtFrame(); return; }
        if (_animSheet) { AnimSheetFrame(); return; }
        if (_simRun) { SimFrame(); return; }
        double t = _tFrame / (double)TFps;
        var cam = TrailerCamera(t);
        float zoom = TW / cam.Width;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        _r.Capture(cam, a =>
        {
            DrawDesktop(a);
            DrawScene(a);
            DrawCaptions(t, a);
        }, new Color4(0.86f, 0.9f, 0.96f, 1), zoom, _tPixels!, TW, TH);
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        _mp4!.Frame(_tPixels!);
        long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
        _tCapMs += (t1 - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _tEncMs += (t2 - t1) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        _tFrame++;
        if (_tFrame % 300 == 0)
        {
            World.Log($"trailer: frame {_tFrame}, per frame: capture {_tCapMs / 300:0} ms, encode {_tEncMs / 300:0} ms, wall {(DateTime.Now - _tWall).TotalMilliseconds / 300:0} ms");
            _tCapMs = _tEncMs = 0; _tWall = DateTime.Now;
        }
        if (t >= TrailerLength) FinishTrailer();
    }

    /// <summary>A pretend desktop: wallpaper, windows (drawn under everyone), the taskbar.</summary>
    void DrawDesktop(RectangleF area)
    {
        float n = _tNight;
        var top = Color4.Lerp(M.Hex(0xBFD9F2), M.Hex(0x1B2340), n);
        var bottom = Color4.Lerp(M.Hex(0xF6E7C8), M.Hex(0x3A2E4A), n);
        for (int i = 0; i < 36; i++)
        {
            float y0 = TH * i / 36f, y1 = TH * (i + 1) / 36f + 1;
            var c = Color4.Lerp(top, bottom, i / 35f);
            _r.FillPolygon(stackalloc Vector2[] { new(0, y0), new(TW, y0), new(TW, y1), new(0, y1) }, c);
        }
        // Soft hills.
        Span<Vector2> hill = stackalloc Vector2[34];
        for (int i = 0; i < 32; i++) { float x = TW * i / 31f; hill[i] = new(x, 860 - 60 * MathF.Sin(i * 0.31f) - 30 * MathF.Sin(i * 0.77f + 1)); }
        hill[32] = new(TW, TH); hill[33] = new(0, TH);
        _r.FillPolygon(hill, Color4.Lerp(M.Hex(0xA8D5A2), M.Hex(0x2C3A3A), n));
        // Stars at night.
        if (n > 0.3f) for (int i = 0; i < 60; i++) _r.Disc(new Vector2((i * 331) % TW, (i * 197) % 600), 1.6f, new Color4(1, 1, 0.9f, (n - 0.3f) * (0.5f + 0.5f * MathF.Sin(i + _tFrame * 0.1f))));
        foreach (var (title, r, accent) in _tWindows)
        {
            var c = new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2);
            _r.SoftShadow(c + new Vector2(0, 14), r.Width * 0.62f, r.Height * 0.62f, 0.35f);
            var body = Color4.Lerp(new Color4(0.99f, 0.99f, 0.98f, 1), new Color4(0.62f, 0.62f, 0.7f, 1), n * 0.6f);
            _r.RoundRect(c, r.Width, r.Height, 10, body, new Color4(0.2f, 0.2f, 0.25f, 0.6f), 1.5f);
            var bar = new RectangleF(r.X, r.Y, r.Width, 34);
            _r.RoundRect(new Vector2(bar.X + bar.Width / 2, bar.Y + 17), bar.Width, 34, 10, Color4.Lerp(accent, new Color4(0.2f, 0.2f, 0.3f, 1), n * 0.6f), new Color4(0, 0, 0, 0), 0);
            _r.Text(title, new Vector2(r.X + 70, r.Y + 17), 16, new Color4(1, 1, 1, 0.95f), true);
            for (int j = 0; j < 3; j++) _r.Disc(new Vector2(r.Right - 22 - j * 22, r.Y + 17), 6, new Color4(1, 1, 1, 0.6f));
            // Something in the window.
            for (int j = 0; j < (int)((r.Height - 70) / 28); j++)
                _r.RoundRect(new Vector2(r.X + 30 + (r.Width - 60) * (0.4f + 0.1f * (j % 3)) / 2, r.Y + 64 + j * 28), (r.Width - 60) * (0.4f + 0.1f * (j % 3)), 10, 5, new Color4(0.75f, 0.77f, 0.82f, 0.6f), new Color4(0, 0, 0, 0), 0);
        }
        // Taskbar.
        _r.FillPolygon(stackalloc Vector2[] { new(0, TH - 48), new(TW, TH - 48), new(TW, TH), new(0, TH) }, new Color4(0.12f, 0.13f, 0.17f, 0.92f));
        for (int i = 0; i < 6; i++) _r.RoundRect(new Vector2(TW / 2 - 150 + i * 60, TH - 24), 34, 34, 8, new Color4(1, 1, 1, i == 0 ? 0.35f : 0.18f), new Color4(0, 0, 0, 0), 0);
        _r.Text("12:00", new Vector2(TW - 60, TH - 24), 16, new Color4(1, 1, 1, 0.85f));
    }

    /// <summary>Hand-drawn captions on paper cards, in the camera's frame.</summary>
    void DrawCaptions(double t, RectangleF cam)
    {
        float s = cam.Width / TW;
        foreach (var (t0, t1, text, sub, big) in _tCaptions)
        {
            if (t < t0 || t > t1) continue;
            float a = (float)Math.Min(1, Math.Min((t - t0) / 0.35, (t1 - t) / 0.35));
            float pop = 0.92f + 0.08f * MathF.Min(1, (float)((t - t0) / 0.35));
            var c = new Vector2(cam.X + cam.Width / 2, cam.Y + cam.Height * (big ? 0.42f : 0.13f));
            float w = (big ? 900 : 120 + text.Length * 24) * s * pop, h = (big ? 300 : (sub.Length > 0 ? 112 : 86)) * s * pop;
            Ui.Card(_r, c, w, h, 18 * s, s * 2.2f, a * 0.97f, Ui.Accent, 1.4f);
            _r.Text(text, c + new Vector2(0, (big ? -40 : sub.Length > 0 ? -14 : 0) * s), (big ? 120 : 46) * s * pop, Ui.Ink.A(a), true);
            if (sub.Length > 0) _r.Text(sub, c + new Vector2(0, (big ? 70 : 30) * s), (big ? 38 : 24) * s * pop, Ui.Pencil.A(a), true);
        }
    }

    void FinishTrailer()
    {
        if (_mp4 == null) return;
        var sound = new Sound(0.6f, output: false);
        var music = Music.Render(TrailerLength + 0.5);
        var rng = new Random(5);
        // Sound effects, where they happened (panned by where they were on screen), under the music.
        foreach (var (t, s, x, vol, pitch) in _tSounds)
        {
            var take = sound.Take(s, rng);
            if (take.Length == 0) continue;
            int start = (int)(t * Sound.SampleRate);
            float pan = Math.Clamp(x / TW * 2 - 1, -1, 1) * 0.6f, g = Math.Clamp(vol, 0, 1.2f) * 0.55f;
            double step = pitch;
            for (int i = 0; ; i++)
            {
                double src = i * step;
                int j = (int)src;
                if (j + 1 >= take.Length) break;
                int o = (start + i) * 2;
                if (o + 1 >= music.Length) break;
                float v = (float)(take[j] + (take[j + 1] - take[j]) * (src - j)) * g;
                music[o] += v * (1 - Math.Max(0, pan));
                music[o + 1] += v * (1 + Math.Min(0, pan));
            }
        }
        for (int i = 0; i < music.Length; i++) music[i] = MathF.Tanh(music[i]);
        _mp4.Audio(music);
        _mp4.Dispose();
        _mp4 = null;
        World.Log($"trailer: {_tFrame} frames, {_tSounds.Count} sounds → {_trailerPath}");
        SelfTestExitCode = 0;
        ExitThread();
    }
}
