using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Text.Json;
using Microsoft.Win32;
using Vortice.Mathematics;
using static Doodlefolk.Native;

namespace Doodlefolk;

/// <summary>Owns the overlay, renderer, tray icon and the frame loop (runs whenever the UI thread is idle,
/// paced by vsync through Present).</summary>
sealed partial class App : ApplicationContext
{
    readonly World _w = new();
    readonly Overlay _overlay;
    readonly Renderer _r;
    readonly NotifyIcon _tray;
    readonly SimClock _clock = new();
    readonly bool _debug;
    readonly Settings _settings = Settings.Load();
    int _refresh = 60;
    double _frameInterval, _nextFrameAt;
    bool _fineTimer;
    readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "doodlefolk.log");
    double _last, _acc, _nextTopmost, _nextDump, _fpsT;
    int _frames, _fps, _hitches, _hitchAcc;
    double _nextAutosave = 120, _msDraw, _nextAuditSample;
    float _regionsPerFrame;
    float _maxDt, _maxDtAcc;
    double _tRefresh, _tRender, _msRefresh, _msRender, _tSim, _msSim;
    bool _paused, _showPlatforms, _hiddenCleared, _displayChanged, _disposed;
    readonly List<Rectangle> _regNow = new(), _regPrev = new(), _regPrev2 = new(), _regAll = new();
    Vector2 _prevCursor;
    readonly Queue<(double t, Vector2 p)> _cursorHist = new();

    // Mouse interaction: a press on a figure becomes a poke or (after moving/holding) a drag;
    // a press on a ball picks it up immediately.
    Figure? _pressFig;
    Prop? _pressProp;
    int _pressJoint;
    Vector2 _pressPos;
    double _pressTime;
    bool _dragging;

    public App(string[] args)
    {
        _debug = args.Contains("--debug");
        _selfTest = args.Contains("--selftest");
        _trailer = args.Contains("--trailer");
        World.Debug = _debug;
        if (_selfTest) SelfTestPrepare();
        if (_trailer) TrailerPrepare(args);
        var boot = System.Diagnostics.Stopwatch.StartNew();
        var marks = new List<string>();
        void Mark(string what) { marks.Add($"{what} {boot.ElapsedMilliseconds}"); }
        if (!_selfTest && !_trailer) SteamHub.Init();
        Mods.Load(SteamHub.WorkshopFolders());
        Mark("mods");
        CleanUpOldVersion();
        if (Migration.NeedsStartupEntry) { _settings.StartWithWindows = true; SetStartWithWindows(true); }
        _showPlatforms = args.Contains("--platforms");
        _w.Scale = ComputeScale(args);
        _w.Fight = _settings.Fight;
        _w.Env.MinHeadroom = 75 * _w.Scale;

        var vs = _w.Env.Virtual;
        _overlay = new Overlay(vs);
        if (!_selfTest && !_trailer) _overlay.Show();
        Mark("overlay");
        _r = new Renderer(_overlay.Handle, vs);
        Mark("renderer");
        _refresh = RefreshRate();
        ApplyFps();
        var full = new List<Rectangle> { _r.Bounds };
        _r.Frame(full, () => { });
        _r.Frame(full, () => { });
        _overlay.MouseDown += OnMouseDown;
        _overlay.MouseUp += (_, _) => EndPress();
        _tray = BuildTray();
        if (_selfTest) { _tray.Visible = false; _clock.Scale = TestSpeed; SelfTestStage(); }
        if (_trailer) { _tray.Visible = false; TrailerStage(); }
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.SessionEnding += (_, _) => { if (_settings.RememberCast) SaveCast(); };
        Application.Idle += OnIdle;

        Mark("tray");
        _w.Env.Refresh(_overlay.Handle);
        Mark("env");
        // Sound effects are synthesised on a worker thread so the figures appear sooner; until then it's quiet.
        World.Voices = _settings.Voices;
        var screen = (_w.Env.Virtual.Left, _w.Env.Virtual.Width);
        if (!_trailer) Task.Run(() =>
        {
            try
            {
                var snd = new Sound(_settings.SoundVolume) { Enabled = _settings.SoundOn };
                snd.SetScreen(screen.Left, screen.Width);
                _overlay.BeginInvoke(() => { if (!_disposed) _w.Sound = snd; else snd.Dispose(); });
            }
            catch (Exception e) { World.Log($"sound init failed: {e.Message}"); }
        });
        Mark("sound");
        Gfx.Q = _settings.Gfx;
        InitScreen();
        Mark("screen");
        _w.MakeProp = kind => SpawnProp(kind);
        _w.MakeBaby = MakeBaby;
        _w.MakePet = k => SpawnPet(k, quiet: true);
        InitSocial();
        _w.MakeItem = key => ItemCatalog.Find(key) is { } d ? SpawnItem(d) : null;
        _w.OpenCrate = (crate, opener) => OpenCrate(crate, opener);
        _w.RareSeen = RareSeen;
        _w.OnMilestone = OnMilestone;
        _w.FishCaught = OnFishCaught;
        _w.CrateDelivered = _ => { _settings.CratesWaiting = Math.Max(0, _settings.CratesWaiting - 1); };
        int si = Array.IndexOf(args, "--spawn");
        if (si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out int count))
            for (int i = 0; i < count; i++) Spawn(null);
        else if (_settings.RememberCast && (_settings.Figures.Count > 0 || _settings.Pets.Count > 0)) RestoreCast();
        else if (!_settings.PetMode) Spawn(null);
        if (_settings.PetMode) { _settings.PetMode = false; SetPetMode(true); }
        CatchUpAgeing();
        if (SteamHub.Ready) foreach (var k in _settings.Stickers.Keys) SteamHub.Achieve(k);
        Mark("cast");
        World.Log("startup steps (ms): " + string.Join(", ", marks));
    }

    // ---------------- frame rate ----------------

    /// <summary>Turns the user's fps choice into a vsync interval where it divides the refresh rate
    /// evenly (smoothest), otherwise a software limiter.</summary>
    void ApplyFps()
    {
        int cap = _settings.FpsCap;
        // Battery saver: 30 frames a second at most.
        if (_lite) cap = cap is Settings.MatchMonitor or Settings.Unlimited ? 30 : Math.Min(cap, 30);
        _frameInterval = 0;
        if (cap == Settings.MatchMonitor) _r.SyncInterval = 1;
        else if (cap == Settings.Unlimited) _r.SyncInterval = 0;
        else if (cap <= _refresh && _refresh % cap == 0) _r.SyncInterval = (uint)(_refresh / cap);
        else
        {
            _r.SyncInterval = cap > _refresh ? 0u : 1u;
            _frameInterval = 1.0 / cap;
        }
        _baseSync = _r.SyncInterval;
        bool fine = _frameInterval > 0;
        if (fine != _fineTimer)
        {
            if (fine) timeBeginPeriod(1); else timeEndPeriod(1);
            _fineTimer = fine;
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
    [System.Runtime.InteropServices.DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

    static int RefreshRate()
    {
        var mode = new DEVMODE { dmSize = (short)System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        return EnumDisplaySettings(null, -1, ref mode) && mode.dmDisplayFrequency > 1 ? mode.dmDisplayFrequency : 60;
    }

    static float ComputeScale(string[] args)
    {
        float user = 1;
        int i = Array.IndexOf(args, "--scale");
        if (i >= 0 && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out user);
        var primary = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        return primary.Height / 1080f * Math.Clamp(user, 0.3f, 4f);
    }

    void OnDisplayChanged(object? sender, EventArgs e) => _displayChanged = true;

    // ---------------- main loop ----------------

    bool _startLogged, _memLogged;

    void OnIdle(object? sender, EventArgs e)
    {
        while (!PeekMessage(out _, IntPtr.Zero, 0, 0, 0))
        {
            try
            {
                if (_trailer) { _clock.Manual(_tFrame / (double)TFps); }
                Frame();
                if (_trailer) { TrailerFrame(); continue; }
                if (!_startLogged)
                {
                    _startLogged = true;
                    using var me = System.Diagnostics.Process.GetCurrentProcess();
                    World.Log($"startup: first frame after {(DateTime.Now - me.StartTime).TotalMilliseconds:0} ms");
                }
                else if (!_memLogged && _clock.Elapsed.TotalSeconds > 30)
                {
                    _memLogged = true;
                    using var me = System.Diagnostics.Process.GetCurrentProcess();
                    World.Log($"memory: managed {GC.GetTotalMemory(false) / 1048576} MB, private {me.PrivateMemorySize64 / 1048576} MB, working set {me.WorkingSet64 / 1048576} MB, {_r.TileCount} layer tiles");
                }
            }
            catch (Exception ex)
            {
                _frameErrors++;
                if (_errorTexts.Count < 50) _errorTexts.Add(ex.GetType().Name + ": " + ex.Message);
                RecordProblem(ex);
                File.AppendAllText(_logPath, $"{DateTime.Now:O} {ex}\n");
                Thread.Sleep(100);
            }
        }
    }

    void Frame()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_frameInterval > 0 && !_trailer)
        {
            // Software frame limiter for caps that don't divide the monitor's refresh rate.
            if (now < _nextFrameAt)
            {
                double ms = (_nextFrameAt - now) * 1000;
                if (ms > 1.5) Thread.Sleep((int)(ms - 1)); else Thread.Yield();
                return;
            }
            _nextFrameAt = Math.Max(_nextFrameAt + _frameInterval, now - _frameInterval);
        }
        float rawDt = (float)(now - _last);
        float dt = MathF.Min(rawDt, 0.05f * (float)_clock.Scale);
        _last = now;
        _maxDtAcc = MathF.Max(_maxDtAcc, rawDt);
        if (_fps > 0 && rawDt > 1.6f / _fps) _hitchAcc++;
        PushStudio(now);

        if (_displayChanged)
        {
            _displayChanged = false;
            _w.Env.RefreshMonitors();
            _overlay.Place(_w.Env.Virtual);
            _r.Resize(_w.Env.Virtual);
        }
        if (now > _nextTopmost)
        {
            _nextTopmost = now + 2; _overlay.KeepOnTop(); Ui.Update(_settings.Theme);
            Seasons.South = _settings.Hemisphere == "south" || (_settings.Hemisphere == "auto" && _settings.WeatherLat is < 0);
            Fishes.RefreshPond(_w.Weather.Raining);
            World.OldAge = _settings.Mortality == "oldage";
        }
        // Save every couple of minutes, so a crash or a forced shutdown loses little.
        if (now > _nextAutosave) { _nextAutosave = now + 120; if (_settings.RememberCast && _w.Figures.Count > 0) SaveCast(); }
        long tr0 = Stopwatch.GetTimestamp();
        _w.Env.Refresh(_overlay.Handle);
        _tRefresh += Stopwatch.GetElapsedTime(tr0).TotalMilliseconds;

        bool hiddenNow = !_selfTest && !_trailer && (_paused || _w.Env.FullscreenActive || QuietHours());
        AmbienceFrame(now, hiddenNow);
        if (hiddenNow)
        {
            if (now > _reminderTick) { _reminderTick = now + 5; ReminderTick(hidden: true); }
            EndPress();
            _overlay.SetClickThrough(true);
            if (!_hiddenCleared)
            {
                // Clear both swap-chain buffers.
                var all = new List<Rectangle> { _r.Bounds };
                _r.Frame(all, () => { });
                _r.Frame(all, () => { });
                _regPrev.Clear(); _regPrev2.Clear();
                _hiddenCleared = true;
            }
            Thread.Sleep(60);
            return;
        }
        _hiddenCleared = false;

        UpdateCursor(now);
        foreach (var it in _w.Items) it.ApplyCarry(_w.Env);
        _w.Env.AddItemSurfaces(_w.Items);
        ScreenFrame();
        _w.Nav.Refresh();
        WishFrame();
        EventsFrame(now);
        GameFrame(now);
        PetFrame(now);
        WorldFrame(now);
        LassoFrame(now, dt);
        TourneyFrame(now);
        _w.UpdateClubs(now);
        TidyTemporary();
        FamilyFrame(now);
        _w.Babies = _settings.Babies;
        PhotoFrame(now);
        WeatherFrame(dt, now);
        try { HappeningFrame(dt, now); }
        catch (Exception e) { World.Log("happening failed: " + e.Message); if (_w.Happening is { } hx) { _w.Happening = null; foreach (var f in hx.Who.Concat(hx.Crowd)) f.Brain.LeaveHappening(); } }
        DesktopFrame(now);
        WelcomeFrame(now);
        BreakFrame(now);
        FocusFrame(now);
        VisitorFrame(now);
        QuestFrame(now);
        GhostFrame(now);
        ToyboxFrame(now);
        VisitorsLeave();
        SteamHub.Frame(now, _w.Figures.Count, _w.Pets.Count);
        RecordFrame(now);
        PowerFrame(now);
        UpdateFrame(now);
        SmartFps(dt);
        if (World.Debug && now > _nextAuditSample)
        {
            _nextAuditSample = now + 2;
            foreach (var f in _w.Figures)
                World.Audit($"snap\t{f.Name}\t{f.Brain.State}\t{f.Brain.Activity}\t{f.Base.X:0},{f.Base.Y:0}\t{f.Mode}\tst={f.Brain.Stamina:F2} bo={f.Brain.Boredom:F2} lo={f.Brain.Loneliness:F2} hu={f.Brain.Hunger:F2} joy={f.Brain.Joy:F2} fear={f.Brain.Fear:F2}\t{f.CurrentEmote}");
        }
        TidyGear(now);
        foreach (var f in _w.Figures) f.ApplyCarry(_w.Env, dt);
        foreach (var p in _w.Props) p.ApplyCarry(_w.Env);
        foreach (var pet in _w.Pets) pet.ApplyCarry(_w.Env);

        _acc += dt;
        int n = (int)(_acc / World.Dt);
        int maxTicks = (int)(8 * _clock.Scale);
        if (n > maxTicks) { n = maxTicks; _acc = 0; } else _acc -= n * World.Dt;
        if (World.Calm) _w.HitStop = 0;
        if (_w.HitStop > 0)
        {
            // Freeze-frame on a big hit.
            _w.HitStop -= dt;
            n = 0;
            _acc = 0;
        }
        long ts0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i++)
        {
            Vector2 pin = Vector2.Lerp(_prevCursor, _w.Cursor, (i + 1f) / n);
            if (_dragging && _pressFig != null && !_pressFig.Held) { _pressFig = null; _dragging = false; }   // it broke free
            if (_dragging && _pressFig != null) _pressFig.Rag.PinTarget = pin;
            if (_pressProp != null) _pressProp.PinTarget = pin;
            // Iterate over copies: brains may add/remove things (e.g. drop a ball) mid-step.
            foreach (var f in _w.Figures.ToArray())
            {
                try { f.Step(World.Dt, _w); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IndexOutOfRangeException or NullReferenceException)
                {
                    // One confused figure shouldn't freeze everyone: log it and reset its mind.
                    LogOnce(ex);
                    f.Brain.Reset();
                }
            }
            foreach (var p in _w.Props.ToArray()) p.Step(World.Dt, _w);
            foreach (var pet in _w.Pets.ToArray()) pet.Step(_w, World.Dt);
            if (_pressItem != null) _pressItem.PinTarget = pin;
            foreach (var it in _w.Items.ToArray()) it.Step(World.Dt, _w);
            _w.Projectiles.RemoveAll(pr => !pr.Step(World.Dt, _w));
            _w.Matches.RemoveAll(m => !m.Step(World.Dt, _w));
            foreach (var f in _w.Figures.Where(f => f.Gone).ToArray())
            {
                if (f.Dead) OnFigureDied(f);
                if (_pressFig == f) { _pressFig = null; _dragging = false; }
                _w.RemoveFigure(f);
            }
            _w.Fx.Step(World.Dt);
        }
        _tSim += Stopwatch.GetElapsedTime(ts0).TotalMilliseconds;
        var radio = _w.Items.FirstOrDefault(it => it.Def.Verbs.Contains(Verb.Dance) && it.Playing && it.OnGround && it.Free);
        _w.Sound?.Radio(radio != null && !_paused, radio?.Pos.X ?? 0);
        _prevCursor = _w.Cursor;
        ShoveCursor();

        long tr1 = Stopwatch.GetTimestamp();
        float alpha = _w.HitStop > 0 ? 1 : (float)Math.Clamp(_acc / World.Dt, 0, 1);
        foreach (var f in _w.Figures) if (f != _pressFig || !_dragging) f.BeginInterp(alpha);
        foreach (var p in _w.Props) if (p != _pressProp) p.BeginInterp(alpha);
        bool drew = _trailer;
        try { if (!_trailer) drew = Render(); }
        finally
        {
            foreach (var f in _w.Figures) f.EndInterp();
            foreach (var p in _w.Props) p.EndInterp();
        }
        if (!drew) Thread.Sleep(15);
        _tRender += Stopwatch.GetElapsedTime(tr1).TotalMilliseconds;
        AlbumFrame(now);

        _frames++;
        if (now - _fpsT >= 1) { _maxDt = _maxDtAcc; _hitches = _hitchAcc; _maxDtAcc = 0; _hitchAcc = 0; _fps = _frames; _msRefresh = _tRefresh / _frames; _msRender = _tRender / _frames; _msSim = _tSim / _frames; _msDraw = _r.DrawMs / _frames; _regionsPerFrame = _r.Regions / (float)_frames; _r.DrawMs = 0; _r.Regions = 0; _tRefresh = _tRender = _tSim = 0; _frames = 0; _fpsT = now; }
        if (_debug && !_selfTest && now > _nextDump) { _nextDump = now + 0.05; Dump(); RunCommands(); }
        if (_selfTest) SelfTestFrame(now);
        if (_trailer) TrailerTick(now);
    }

    readonly HashSet<string> _logged = new();

    void LogOnce(Exception ex)
    {
        _frameErrors++;
        if (_errorTexts.Count < 50) _errorTexts.Add(ex.GetType().Name + ": " + ex.Message);
        RecordProblem(ex);
        string key = ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault();
        if (_logged.Add(key)) File.AppendAllText(_logPath, $"{DateTime.Now:O} {ex}\n");
    }

    Vector2 _shove;
    /// <summary>Debug: pretend the cursor is here (tests without moving the real mouse).</summary>
    Vector2? _fakeCursor;
    bool _skipItems;

    /// <summary>Figures punching the cursor knock it across the screen over a few frames.
    /// Never while the user is holding a mouse button.</summary>
    void ShoveCursor()
    {
        _shove += _w.CursorPush;
        _w.CursorPush = Vector2.Zero;
        if (_fakeCursor != null) { _shove = Vector2.Zero; return; }
        if (_shove.LengthSquared() < 1 || !_w.Fight.PunchCursor || Control.MouseButtons != MouseButtons.None) { _shove = Vector2.Zero; return; }
        Vector2 step = _shove * 0.35f;
        _shove -= step;
        GetCursorPos(out var p);
        var v = _w.Env.Virtual;
        int x = Math.Clamp((int)MathF.Round(p.X + step.X), v.Left, v.Right - 1);
        int y = Math.Clamp((int)MathF.Round(p.Y + step.Y), v.Top, v.Bottom - 1);
        SetCursorPos(x, y);
    }

    // ---------------- input ----------------

    void UpdateCursor(double now)
    {
        GetCursorPos(out var p);
        var c = _fakeCursor ?? new Vector2(p.X, p.Y);
        _cursorHist.Enqueue((now, c));
        while (_cursorHist.Count > 2 && now - _cursorHist.Peek().t > 0.08) _cursorHist.Dequeue();
        var (t0, p0) = _cursorHist.Peek();
        _w.CursorVel = now - t0 > 1e-3 ? (c - p0) / (float)(now - t0) : Vector2.Zero;
        _w.Cursor = c;

        if (_pressPet != null)
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0) EndPress();
            else _pressPet.HoldTarget = c + new Vector2(0, _pressPet.Height * 0.6f);
        }
        if (_pressFig != null || _pressProp != null)
        {
            if ((Control.MouseButtons & MouseButtons.Left) == 0) EndPress();
            else if (_pressFig != null && !_dragging && (Vector2.Distance(c, _pressPos) > 5 || now - _pressTime > 0.2))
            {
                _dragging = true;
                _pressFig.Grab(_pressJoint);
                _pressFig.Rag.PinTarget = c;
                _prevCursor = c;
            }
        }

        var hitProp = HitProp(c);
        var (fig, _) = hitProp == null ? HitTest(c) : (null, -1);
        _w.Hover = _dragging ? null : fig;
        if (_w.Offer != null) _w.Offer.Hot = OfferHit(c);
        if (_w.Wish != null) _w.Wish.Hot = WishHit(c);
        if (_resizeIt != null) ResizeStep(c);
        bool overEdge = ResizeHover(c);
        _overlay.SetClickThrough(!_sprayTool && !overEdge && _w.Offer?.Hot != true && _w.Wish?.Hot != true && _pressPet == null && HitPet(c) == null && fig == null && hitProp == null && _pressFig == null && _pressProp == null && _pressItem == null && HitItem(c) == null);
    }

    (Figure? fig, int joint) HitTest(Vector2 c)
    {
        Figure? best = null;
        int bj = -1;
        float bd = 0;
        foreach (var f in _w.Figures)
        {
            if (f.Mode == Mode.Spawning || f.Dead) continue;
            float d = f.DistanceTo(c, out int j);
            if (d <= bd) { bd = d; best = f; bj = j; }
        }
        return (best, bj);
    }

    Prop? HitProp(Vector2 c)
    {
        for (int i = _w.Props.Count - 1; i >= 0; i--)
            if (_w.Props[i].HitTest(c)) return _w.Props[i];
        return null;
    }

    Pet? HitPet(Vector2 c)
    {
        for (int i = _w.Pets.Count - 1; i >= 0; i--) if (_w.Pets[i].HitTest(c)) return _w.Pets[i];
        return null;
    }

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
        if (_sprayTool)
        {
            if (e.Button == MouseButtons.Left) Spray();
            else if (e.Button == MouseButtons.Right) PickUpSpray(false);
            return;
        }
        if (e.Button == MouseButtons.Left && StartResize(_w.Cursor)) return;
        if (HitPet(_w.Cursor) is { } pet && HitTest(_w.Cursor).fig == null)
        {
            if (e.Button == MouseButtons.Left) { pet.Grab(); _pressPet = pet; }
            else if (e.Button == MouseButtons.Right) ShowPop("pet", pet.Id);
            return;
        }
        if (e.Button == MouseButtons.Left && OfferHit(_w.Cursor)) { TakeOffer(); return; }
        if (e.Button == MouseButtons.Left && WishHit(_w.Cursor)) { GrantWish(); return; }
        if (HitProp(_w.Cursor) is { } prop)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (prop.Holder != null) { prop.Holder.Carrying = null; prop.Holder = null; }
                prop.Pinned = true;
                prop.PinTarget = prop.Pos;
                _pressProp = prop;
                _prevCursor = _w.Cursor;
            }
            else if (e.Button == MouseButtons.Right) ShowPop("prop", prop.Id);
            return;
        }
        var (fig, joint) = HitTest(_w.Cursor);
        if (fig != null && e.Button == MouseButtons.Left && GameClick(fig)) return;
        if (fig == null)
        {
            if (HitItem(_w.Cursor) is { } item)
            {
                if (e.Button == MouseButtons.Left && CareClick(item)) return;
                if (e.Button == MouseButtons.Left) GrabItem(item);
                else if (e.Button == MouseButtons.Right) ShowPop("item", item.Id);
            }
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            _pressFig = fig;
            _pressJoint = joint;
            _pressPos = _w.Cursor;
            _pressTime = _clock.Elapsed.TotalSeconds;
            _dragging = false;
        }
        else if (e.Button == MouseButtons.Right) ShowPop("figure", fig.Id);
    }

    void EndPress()
    {
        ReleaseItem();
        if (_pressPet != null) { _pressPet.Release(M.ClampLength(_w.CursorVel, 4000 * _w.Scale), _w); _pressPet = null; }
        if (_pressProp != null)
        {
            var p = _pressProp;
            _pressProp = null;
            p.Release(M.ClampLength(_w.CursorVel, 5000 * _w.Scale));
            p.LastTouch = null;
            p.ThrownByUser = true;
            p.PassTarget = null;
        }
        if (_pressFig == null) return;
        if (_dragging) { _pressFig.Release(_w.CursorVel); if (_w.CursorVel.Length() > 4000 * _w.Scale) _w.Sticker("yeet"); }
        else _pressFig.Brain.OnPoked(_w);
        _pressFig = null;
        _dragging = false;
    }

    // ---------------- drawing ----------------

    /// <summary>Draws only around things that are (or just were) visible. Returns false if nothing needed presenting.</summary>
    bool Render()
    {
        _regNow.Clear();
        if (_showPlatforms) _regNow.Add(_r.Bounds);
        foreach (var f in _w.Figures)
        {
            _regNow.Add(FigureRect(f));
            if (f.GrappleBounds() is RectangleF gb) _regNow.Add(ToRect(gb));
        }
        foreach (var p in _w.Props) _regNow.Add(ToRect(Gfx.Q.DropShadows ? GrowForDrop(p.Bounds(_w.Env), _w.Scale) : p.Bounds(_w.Env)));
        foreach (var pet in _w.Pets) _regNow.Add(ToRect(Gfx.Q.DropShadows ? GrowForDrop(pet.Bounds(), _w.Scale) : pet.Bounds()));
        foreach (var pr in _w.Projectiles) _regNow.Add(ToRect(pr.Bounds()));
        foreach (var m in _w.Matches) _regNow.Add(ToRect(m.Bounds()));
        foreach (var it in _w.Items)
        {
            // Still objects stay on screen as they are; only moving/animated/changed ones are redrawn.
            if (!it.Changed()) continue;
            _regNow.Add(ToRect(Gfx.Q.DropShadows ? GrowForDrop(it.Bounds(), _w.Scale) : it.Bounds()));
            it.Shadow(_w.Env, out var sc, out float srx, out float sry, out _);
            if (srx > 0) _regNow.Add(ToRect(RectangleF.FromLTRB(sc.X - srx, sc.Y - sry, sc.X + srx, sc.Y + sry)));
        }
        if (_w.Fx.Bounds() is RectangleF fx) _regNow.Add(ToRect(fx));
        if (OfferRect() is RectangleF ofr) _regNow.Add(ToRect(ofr));
        if (WishRect() is RectangleF wr) _regNow.Add(ToRect(wr));
        if (TourneyRect() is RectangleF tr) _regNow.Add(ToRect(tr));
        if (StickerRect() is RectangleF sr) _regNow.Add(ToRect(sr));
        if (FocusRect() is RectangleF fr) _regNow.Add(ToRect(fr));
        if (SprayRect() is RectangleF spr) _regNow.Add(ToRect(spr));
        if (LeashRect() is RectangleF lr) _regNow.Add(ToRect(lr));
        if (LassoRect() is RectangleF lsr) _regNow.Add(ToRect(lsr));
        foreach (var sd in _w.Seasons.Dirty) _regNow.Add(ToRect(sd));
        if (_w.Weather.Active) _regNow.Add(_r.Bounds);

        // Flip model with two buffers: this buffer last held frame N-2, the screen shows N-1.
        _regAll.Clear();
        _regAll.AddRange(_regNow);
        _regAll.AddRange(_regPrev);
        _regAll.AddRange(_regPrev2);
        MergeRects(_regAll);
        _regPrev2.Clear(); _regPrev2.AddRange(_regPrev);
        _regPrev.Clear(); _regPrev.AddRange(_regNow);
        UpdateLayers();
        if (_regAll.Count == 0) return false;
        _r.Frame(_regAll, (Action<RectangleF>)DrawScene);
        return true;
    }

    RectangleF _clip;

    /// <summary>Does this rectangle touch the region being repainted right now? (Skip drawing what doesn't.)</summary>
    bool Dirty(RectangleF b) => b.Right >= _clip.Left && b.Left <= _clip.Right && b.Bottom >= _clip.Top && b.Top <= _clip.Bottom;

    void DrawScene(RectangleF clip)
    {
        _clip = clip;
        if (_showPlatforms && clip.Width >= _r.Bounds.Width - 1)
        {
            foreach (var p in _w.Env.Platforms)
                _r.Line(new(p.X1, p.Y), new(p.X2, p.Y), p.Solid ? new Color4(1, 0.6f, 0, 0.8f) : new Color4(0.2f, 1, 0.3f, 0.8f), 3);
            foreach (var wl in _w.Env.Walls)
                if (wl.ReachesTop) _r.Line(new(wl.X, wl.Y1), new(wl.X, wl.Y2), new Color4(0.3f, 0.7f, 1, 0.6f), 2);
        }
        _r.BlitLayer(false, clip);
        foreach (var it in _w.Items)
        {
            if (IsStatic(it) || !Dirty(it.Bounds())) continue;
            it.Shadow(_w.Env, out var ic, out float irx, out float iry, out float ia);
            if (ia > 0) Gfx.GroundShadow(_r, ic, irx, iry, ia);
        }
        _w.Weather.DrawCover(_r, _w.Env, _w.Scale);
        _w.Seasons.DrawFallen(_r, _w.Scale, Dirty);
        var figVisible = new bool[_w.Figures.Count];
        for (int i = 0; i < _w.Figures.Count; i++) figVisible[i] = Dirty(FigureRect(_w.Figures[i]));
        if (Gfx.Q.DropShadows)
        {
            // Everything's shadow on the window behind, as one layer (so overlaps don't darken).
            var drop = Gfx.DropInk;
            _r.BeginShadowLayer(Gfx.DropOpacity);
            foreach (var it in _w.Items) if (!IsStatic(it) && Dirty(it.Bounds())) it.DrawDropShadow(_r);
            foreach (var p in _w.Props) if (p.Holder == null && Dirty(p.Bounds(_w.Env))) _r.Disc(p.Pos + Gfx.DropOffset * _w.Scale, p.Radius, drop);
            foreach (var pet in _w.Pets) if (Dirty(pet.Bounds())) _r.Oval(pet.Centre + Gfx.DropOffset * pet.S, pet.Length * 0.5f, pet.Height * 0.45f, drop);
            for (int i = 0; i < _w.Figures.Count; i++) if (figVisible[i]) _w.Figures[i].DrawDropShadow(_r);
            _r.EndShadowLayer();
        }
        // Hide-and-seek: hiders crouch behind things, so they're drawn before them.
        for (int i = 0; i < _w.Figures.Count; i++) if (figVisible[i] && _w.Figures[i].HidingBehind) _w.Figures[i].Draw(_r);
        DrawItems(false);
        DrawHomeFlags();
        for (int i = 0; i < _w.Figures.Count; i++)
            if (figVisible[i] && Shadow(_w.Figures[i], out var c, out float rx, out float ry, out float a))
                Gfx.GroundShadow(_r, c, rx, ry, a);
        foreach (var p in _w.Props)
            if (Dirty(p.Bounds(_w.Env)) && p.Shadow(_w.Env, out var c, out float rx, out float ry, out float a))
                Gfx.GroundShadow(_r, c, rx, ry, a);
        if (_w.Fx.Bounds() is RectangleF fxb && Dirty(fxb)) _w.Fx.Draw(_r);
        foreach (var pet in _w.Pets) if (Dirty(pet.Bounds())) pet.Draw(_r);
        DrawLeashes();
        for (int i = 0; i < _w.Figures.Count; i++) if (figVisible[i] && !_w.Figures[i].HidingBehind) _w.Figures[i].Draw(_r);
        DrawItems(true);
        _r.BlitLayer(true, clip);
        DrawLassos();
        foreach (var it in _w.Items)
        {
            if (it.Holder == null) continue;
            if (it.Holder.Weapon == it) it.Holder.SyncWeapon(it);   // follows the (interpolated) hand
            if (Dirty(it.Bounds())) it.Draw(_r, false, _clock.Elapsed.TotalSeconds);      // carried things in front
        }
        foreach (var pr in _w.Projectiles) if (Dirty(pr.Bounds())) pr.Draw(_r);
        foreach (var m in _w.Matches) if (Dirty(m.Bounds())) m.Draw(_r);
        foreach (var p in _w.Props) if (Dirty(p.Bounds(_w.Env))) p.Draw(_r);
        DrawOffer();
        DrawWish();
        DrawTourney();
        if (_w.Weather.Active) _w.Weather.DrawSky(_r, _w.Env, _w.Scale);
        _w.Seasons.DrawAir(_r, _w.Scale, _clock.Elapsed.TotalSeconds);
        DrawStickerToast();
        if (FocusRect() is RectangleF fr2 && Dirty(fr2)) DrawFocusCard();
        DrawSprayTool();
        DrawGameCurtain();
        DrawFlash();
    }

    bool Shadow(Figure f, out Vector2 center, out float rx, out float ry, out float alpha)
    {
        center = default; rx = ry = alpha = 0;
        float lowest = float.MinValue;
        foreach (var j in f.Jt) lowest = MathF.Max(lowest, j.Y);
        float x = f.Jt[J.Pelvis].X;
        if (_w.Env.Below(x, lowest - 2 * f.S) is not { } p) return false;
        float k = M.Clamp01(1 - MathF.Max(0, p.Y - lowest) / (260 * f.S));
        if (f.Mode == Mode.Spawning) k *= f.SpawnT;
        if (k <= 0) return false;
        center = new(x, p.Y);
        rx = 12 * f.S * (0.5f + 0.5f * k);
        ry = 2.2f * f.S * (0.6f + 0.4f * k);
        alpha = 0.2f * k;
        return true;
    }

    Rectangle FigureRect(Figure f)
    {
        float x1 = float.MaxValue, y1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue;
        foreach (var j in f.Jt) { x1 = MathF.Min(x1, j.X); y1 = MathF.Min(y1, j.Y); x2 = MathF.Max(x2, j.X); y2 = MathF.Max(y2, j.Y); }
        float pad = f.HeadR + f.LineW * 2 + 2 * f.S + (Gfx.Q.DropShadows ? 8 * f.S : 0);
        var r = RectangleF.FromLTRB(x1 - pad, y1 - pad, x2 + pad, y2 + pad);
        if (f.CurrentEmote != null)
        {
            // Room for the emote bubble (or rising z's) above the head.
            Vector2 h = f.Jt[J.Head];
            float half = MathF.Max(30, 12 + f.CurrentEmote.Length * 3.2f) * f.S;
            r = RectangleF.Union(r, RectangleF.FromLTRB(h.X - half, h.Y - f.HeadR - 32 * f.S, h.X + half, h.Y));
        }
        if (Shadow(f, out var c, out float rx, out float ry, out _))
            r = RectangleF.Union(r, RectangleF.FromLTRB(c.X - rx, c.Y - ry, c.X + rx, c.Y + ry));
        return ToRect(r);
    }

    /// <summary>Repaint everything (after a graphics change, so nothing drawn the old way lingers).</summary>
    void ForceFullRedraw() { _regPrev.Add(_r.Bounds); _regPrev2.Add(_r.Bounds); }

    static RectangleF GrowForDrop(RectangleF b, float s) => RectangleF.FromLTRB(b.Left, b.Top, b.Right + 8 * s, b.Bottom + 9 * s);

    static Rectangle ToRect(RectangleF r) =>
        Rectangle.FromLTRB((int)MathF.Floor(r.Left) - 2, (int)MathF.Floor(r.Top) - 2, (int)MathF.Ceiling(r.Right) + 2, (int)MathF.Ceiling(r.Bottom) + 2);

    static void MergeRects(List<Rectangle> rects)
    {
        for (bool merged = true; merged;)
        {
            merged = false;
            for (int i = 0; i < rects.Count && !merged; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var grown = rects[i];
                    grown.Inflate(14, 14);
                    if (!grown.IntersectsWith(rects[j])) continue;
                    rects[i] = Rectangle.Union(rects[i], rects[j]);
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }
        }
        // Each region means drawing the scene again: past a handful, merge the pairs that waste the least area.
        while (rects.Count > 14)
        {
            int bi = 0, bj = 1;
            long best = long.MaxValue;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var u = Rectangle.Union(rects[i], rects[j]);
                    long waste = (long)u.Width * u.Height - (long)rects[i].Width * rects[i].Height - (long)rects[j].Width * rects[j].Height;
                    if (waste < best) { best = waste; bi = i; bj = j; }
                }
            rects[bi] = Rectangle.Union(rects[bi], rects[bj]);
            rects.RemoveAt(bj);
        }
    }

    // ---------------- smart frame rate ----------------

    float _calmT;
    uint _baseSync = 1;

    /// <summary>When nothing is moving fast, draw at half the monitor's rate (motion is interpolated, so walking and
    /// idling look the same); anything fast (running, flying, throwing, weather, you dragging something) brings full
    /// speed straight back.</summary>
    void SmartFps(float dt)
    {
        if (!_settings.SmartFps || _frameInterval > 0 || _baseSync != 1 || _refresh < 100) { _calmT = 0; return; }
        bool calm = !_w.Weather.Active && _pressFig == null && _pressProp == null && _pressItem == null && _pressPet == null && _w.Projectiles.Count == 0
                    && _w.CursorVel.Length() < 600 * _w.Scale && _w.Matches.Count == 0;
        if (calm)
            foreach (var f in _w.Figures)
                if (f.Mode != Mode.Control || !f.Grounded || f.Climbing || MathF.Abs(f.Vel.X) > 150 * f.S || f.Brain.InFight) { calm = false; break; }
        if (calm) foreach (var p in _w.Props) if (p.Vel.LengthSquared() > 40 * 40 * _w.Scale * _w.Scale) { calm = false; break; }
        if (calm) foreach (var p in _w.Pets) if (!p.Grounded) { calm = false; break; }
        _calmT = calm ? _calmT + dt : 0;
        _r.SyncInterval = _calmT > 0.6f ? 2u : 1u;
    }

    // ---------------- figures ----------------

    Platform? RandomSpawnPlatform(float minWidth)
    {
        var env = _w.Env;
        var wins = env.Platforms.Where(p => !p.Solid && p.X2 - p.X1 > minWidth).ToList();
        var floors = env.Platforms.Where(p => p.Solid).ToList();
        if (floors.Count == 0) return null;
        return wins.Count > 0 && _w.Rng.NextDouble() < 0.6 ? wins[_w.Rng.Next(wins.Count)] : floors[_w.Rng.Next(floors.Count)];
    }

    Figure? Spawn(int? colorIndex, Personality? traits = null)
    {
        int ci = colorIndex ?? NextColor();
        var (cname, color) = Palette.All[ci];
        // Mods can bring their own names for newcomers.
        var free = Mods.FigureNames.Where(n => !_w.Figures.Any(f => f.Name == n)).ToList();
        if (free.Count > 0) cname = free[_w.Rng.Next(free.Count)];
        var nf = SpawnFigure(color, UniqueName(cname), traits ?? Personality.Random(_w.Rng), 1);
        nf?.Brain.DiaryBorn();
        if (nf != null) { _w.Sticker("hello"); if (_w.Figures.Count >= 8) _w.Sticker("fullhouse"); }
        return nf;
    }

    Figure? SpawnFigure(Color4 color, string name, Personality traits, float size)
    {
        var f = new Figure(color, name, _w.Scale * size, traits, _w.Rng) { SizeMul = size };
        if (RandomSpawnPlatform(50 * f.S) is not { } plat) return null;
        float margin = MathF.Min(30 * f.S, (plat.X2 - plat.X1) / 3);
        f.PlaceAt(plat, _w.Rng.Range(plat.X1 + margin, plat.X2 - margin));
        _w.Figures.Add(f);
        return f;
    }

    /// <summary>Rebuild a figure at a new size, keeping its identity, mood and friendships.</summary>
    Figure ResizeFigure(Figure old, float size)
    {
        if (!_w.Figures.Contains(old) || MathF.Abs(old.SizeMul - size) < 0.01f) return old;
        var f = new Figure(old.Color, old.Name, _w.Scale * size, old.Traits, _w.Rng, old.Id) { SizeMul = size };
        f.Brain.CopyFrom(old.Brain);
        f.Gear = old.Gear;
        f.StyleChoice = old.StyleChoice;
        f.Tastes = old.Tastes;
        f.Hunter = old.Hunter;
        f.Gender = old.Gender;
        f.Attraction = old.Attraction;
        f.Weight = old.Weight;
        f.Look = old.Look;
        var plat = _w.Env.Below(old.Base.X, old.Base.Y - 2) ?? RandomSpawnPlatform(0);
        if (plat == null) return old;
        f.PlaceAt(plat, M.ClampIn(old.Base.X, plat.X1 + 4, plat.X2 - 4));
        f.SpawnT = 0.999f;
        old.DropCarried(Vector2.Zero);
        old.Brain.LeaveOutdoors();
        _w.Figures[_w.Figures.IndexOf(old)] = f;
        SwapInHappening(old, f);
        return f;
    }

    int NextColor()
    {
        for (int i = 0; i < Palette.All.Length; i++)
            if (!_w.Figures.Any(f => f.Color == Palette.All[i].Color)) return i;
        return _w.Rng.Next(Palette.All.Length);
    }

    string UniqueName(string baseName, Figure? except = null)
    {
        string name = baseName;
        for (int i = 2; _w.Figures.Any(f => f != except && f.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }

    /// <summary>Save (or update, by name) a figure's look and personality in the user's library.</summary>
    void SaveToLibrary(Figure f)
    {
        _settings.Library.RemoveAll(s => s.Name == f.Name);
        _settings.Library.Add(new SavedFigure
        {
            Name = f.Name,
            Color = Settings.Hex(f.Color),
            Size = f.SizeMul,
            Gear = f.Gear,
            Style = f.StyleChoice.Clone(),
            Tastes = f.Tastes.Clone(),
            Fondness = f.Brain.UserFondness,
            Trust = f.Brain.CursorTrust,
            Hunter = f.Hunter,
            Look = f.Look.Clone(),
            Traits = f.Traits.Clone(),
        });
        _settings.Save();
    }

    Figure? SpawnFromLibrary(SavedFigure s)
    {
        var f = SpawnFigure(Settings.ParseHex(s.Color), UniqueName(s.Name), s.Traits.Clone(), Math.Clamp(s.Size, 0.4f, 3f));
        if (f != null)
        {
            f.Gear = s.Gear;
            if (s.Style != null) f.StyleChoice = s.Style.Clone();
            ApplySaved(f, s);
        }
        return f;
    }

    /// <summary>Likes, dislikes and feelings about the user carried by a saved figure.</summary>
    static void ApplySaved(Figure f, SavedFigure s)
    {
        if (s.Tastes != null) f.Tastes = s.Tastes.Clone();
        f.Hunter = s.Hunter;
        if (s.Gender is Gender g) f.Gender = g;
        f.Brain.Diary.Clear();
        f.Brain.Diary.AddRange(s.Diary);
        foreach (var (k, v) in s.Skills) if (Enum.TryParse<SkillKind>(k, out var sk)) f.Brain.Skills[sk] = Math.Clamp(v, 0, 1);
        if (s.Born is DateTime born) f.Brain.Born = born;
        f.Brain.Grown = Math.Clamp(s.Grown, 0, 1);
        f.Brain.AdultSize = s.AdultSize > 0 ? s.AdultSize : f.SizeMul;
        f.Brain.LastBaby = s.LastBaby;
        f.Brain.Trophies = s.Trophies;
        f.Brain.ChampionOn = s.ChampionOn;
        if (s.Weight is float wt) f.Weight = Math.Clamp(wt, 0, 1);
        f.Brain.Gifts.AddRange(s.Gifts);
        f.Brain.Collection.AddRange(s.Collection);
        foreach (var (k, v) in s.Learned) f.Brain.Learned[k] = Math.Clamp(v, -1, 1);
        foreach (var (k, v) in s.Tried) f.Brain.Tried[k] = Math.Max(0, v);
        if (Enum.TryParse<Hobby>(s.Hobby, out var hob)) f.Brain.Hobby = hob;
        if (Enum.TryParse<Job>(s.Job, out var job)) f.Brain.Job = job;
        if (s.Coins is int coins) f.Brain.Coins = Math.Max(0, coins);
        f.Brain.AgeBank = Math.Clamp(s.AgeBank, 0, 200);
        f.Brain.AgeLoaded();
        if (s.Attraction is Attraction at) f.Attraction = at;
        if (s.Look != null) f.Look = s.Look.Clone();
        if (s.Fondness is float fond) f.Brain.UserFondness = fond;
        if (s.Trust is float trust) f.Brain.CursorTrust = Math.Clamp(trust, 0, 1);
    }

    // ---------------- props ----------------

    Prop SpawnProp(PropKind kind)
    {
        var p = new Prop(kind, _w.Scale);
        var plat = RandomSpawnPlatform(40 * _w.Scale);
        float x = plat != null ? _w.Rng.Range(plat.X1 + 20, plat.X2 - 20) : _w.Env.Virtual.Left + _w.Env.Virtual.Width / 2f;
        var (_, _, top) = _w.Env.BoundsAt(x);
        p.Pos = new Vector2(x, top + p.Radius + 40 * _w.Scale);
        p.Vel = new Vector2(_w.Rng.Range(-150, 150) * _w.Scale, 0);
        _w.Props.Add(p);
        return p;
    }

    // ---------------- saving the cast ----------------

    void SaveCast()
    {
        // In pet-only mode the figures are set aside, not gone: save them too.
        bool swap = _stash.Count > 0;
        if (swap) _w.Figures.AddRange(_stash);
        try { SaveCastInner(); }
        finally { if (swap) _w.Figures.RemoveAll(_stash.Contains); }
    }

    void SaveCastInner()
    {
        _settings.Figures = _w.Figures.Where(f => f.Visitor == VisitorKind.None).Select(f => new SavedFigure
        {
            Name = f.Name,
            Color = Settings.Hex(f.Color),
            Size = f.SizeMul,
            Gear = f.Gear,
            Style = f.StyleChoice.Clone(),
            Tastes = f.Tastes.Clone(),
            Fondness = f.Brain.UserFondness,
            Trust = f.Brain.CursorTrust,
            Hunter = f.Hunter,
            Look = f.Look.Clone(),
            Traits = f.Traits.Clone(),
            Affinity = _w.Figures.Where(o => o != f).DistinctBy(o => o.Name).ToDictionary(o => o.Name, o => f.Brain.AffinityDelta(o)),
            Gender = f.Gender,
            Attraction = f.Attraction,
            Love = _w.Figures.Where(o => o != f && f.Brain.LoveFor(o) > 0.01f).DistinctBy(o => o.Name).ToDictionary(o => o.Name, o => MathF.Round(f.Brain.LoveFor(o), 3)),
            Sweetheart = f.Brain.Sweetheart(_w)?.Name,
            Diary = f.Brain.Diary.TakeLast(150).ToList(),
            Skills = f.Brain.Skills.ToDictionary(k => k.Key.ToString(), k => MathF.Round(k.Value, 3)),
            Born = f.Brain.Born,
            Parents = _w.Figures.Where(o => f.Brain.ParentIds.Contains(o.Id)).Select(o => o.Name).ToList(),
            Grown = f.Brain.Grown, AdultSize = f.Brain.AdultSize, LastBaby = f.Brain.LastBaby,
            Trophies = f.Brain.Trophies, ChampionOn = f.Brain.ChampionOn, Weight = f.Weight,
            Record = _w.Figures.Where(o => f.Brain.Record.ContainsKey(o.Id)).DistinctBy(o => o.Name).ToDictionary(o => o.Name, o => new[] { f.Brain.Record[o.Id].Won, f.Brain.Record[o.Id].Lost }),
            Gifts = f.Brain.Gifts.ToList(), Hobby = f.Brain.Hobby.ToString(), Collection = f.Brain.Collection.ToList(),
            Learned = f.Brain.Learned.ToDictionary(kv => kv.Key, kv => MathF.Round(kv.Value, 3)), Tried = new(f.Brain.Tried),
            Job = (int)f.Brain.Job >= 0 ? f.Brain.Job.ToString() : "", Coins = f.Brain.Coins, AgeBank = MathF.Round(f.Brain.AgeBank, 3),
        }).ToList();
        _settings.Items = SaveItems();
        SaveSocial();
        _settings.Pets = SavePets();
        _settings.LastSeen = DateTime.Now;
        _settings.Props = _w.Props.Select(p => new SavedProp { Kind = p.Kind, Size = p.SizeMul, Bounce = p.Bounce, Color = Settings.Hex(p.Color) }).ToList();
        _settings.Save();
    }

    void RestoreCast()
    {
        var made = new List<(Figure f, SavedFigure s)>();
        foreach (var s in _settings.Figures)
            if (SpawnFigure(Settings.ParseHex(s.Color), UniqueName(s.Name), s.Traits, Math.Clamp(s.Size, 0.4f, 3f)) is { } f)
            {
                f.Gear = s.Gear;
                if (s.Style != null) f.StyleChoice = s.Style.Clone();
                ApplySaved(f, s);
                made.Add((f, s));
            }
        foreach (var (f, s) in made)
        {
            foreach (var (name, a) in s.Affinity)
                if (made.FirstOrDefault(m => m.f.Name == name).f is { } o) f.Brain.Affinity[o.Id] = a;
            foreach (var (name, l) in s.Love)
                if (made.FirstOrDefault(m => m.f.Name == name).f is { } o) f.Brain.Love[o.Id] = l;
            if (s.Sweetheart != null && made.FirstOrDefault(m => m.f.Name == s.Sweetheart).f is { } sh) f.Brain.SweetheartId = sh.Id;
            foreach (var (rn, rec) in s.Record)
                if (made.FirstOrDefault(m => m.f.Name == rn).f is { } ro && rec.Length == 2) f.Brain.Record[ro.Id] = (rec[0], rec[1]);
            foreach (var pn in s.Parents)
                if (made.FirstOrDefault(m => m.f.Name == pn).f is { } par) f.Brain.ParentIds.Add(par.Id);
        }
        RestoreItems(_settings.Items);
        RestoreClubs();
        RestorePets();
        // Older saves could have two pets with the same name.
        foreach (var g in _w.Pets.GroupBy(pt => pt.Name).Where(g => g.Count() > 1))
            foreach (var (pt, i) in g.Skip(1).Select((pt, i) => (pt, i))) pt.Name = $"{g.Key} {new[] { "II", "III", "IV", "V", "VI" }[Math.Min(i, 4)]}";
        foreach (var s in _settings.Props)
        {
            var p = SpawnProp(s.Kind);
            p.SizeMul = Math.Clamp(s.Size, 0.3f, 5f);
            p.Bounce = Math.Clamp(s.Bounce, 0, 0.95f);
            p.Color = Settings.ParseHex(s.Color);
        }
    }

    // ---------------- tray ----------------

    NotifyIcon BuildTray()
    {
        // No stock Windows menu: left-click opens the Studio, right-click the hand-drawn quick panel.
        var tray = new NotifyIcon { Icon = AppIcon, Text = "Doodlefolk", Visible = true };
        tray.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenStudio();
            else if (e.Button == MouseButtons.Right) ToggleQuick();
        };
        return tray;
    }
    static Icon? _icon;
    public static Icon AppIcon => _icon ??= MakeIcon();

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(System.Drawing.Color.FromArgb(229, 57, 53), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(pen.Color);
            g.FillEllipse(brush, 11, 1, 10, 10);
            g.DrawLine(pen, 16, 11, 16, 21);
            g.DrawLine(pen, 16, 21, 10, 30); g.DrawLine(pen, 16, 21, 22, 30);
            g.DrawLine(pen, 16, 13, 8, 18); g.DrawLine(pen, 16, 13, 25, 9);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    // ---------------- debug ----------------

    /// <summary>Debug channel: one command per line in %TEMP%\doodlefolk_cmd.txt, e.g.
    /// "spawn blue", "ball soccer", "Red jump", "Red walk 1200", "fling Red 2000 -1500", "clear".</summary>
    void RunCommands()
    {
        string path = Path.Combine(Path.GetTempPath(), "doodlefolk_cmd.txt");
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); File.Delete(path); }
        catch (IOException) { return; }
        foreach (var line in lines) RunCommand(line);
    }

    /// <summary>One debug command (also how the trailer directs its scenes).</summary>
    void RunCommand(string line)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) return;
            string result = "ok";
            switch (p[0].ToLowerInvariant())
            {
                case "spawn":
                    int ci = p.Length > 1 ? Array.FindIndex(Palette.All, c => c.Name.Equals(p[1], StringComparison.OrdinalIgnoreCase)) : -1;
                    int pi = p.Length > 2 ? Array.FindIndex(Personality.Presets, x => x.Name.Replace(" ", "").Replace("-", "").Equals(p[2].Replace("-", ""), StringComparison.OrdinalIgnoreCase)) : -1;
                    Spawn(ci >= 0 ? ci : null, pi >= 0 ? Personality.Presets[pi].Traits.Clone() : null);
                    break;
                case "ball":
                {
                    var kind = p.Length > 1 && Enum.TryParse<PropKind>(p[1], true, out var k) ? k : PropKind.SoccerBall;
                    var prop = SpawnProp(kind);
                    if (p.Length >= 4) prop.Pos = new Vector2(float.Parse(p[2], inv), float.Parse(p[3], inv));
                    break;
                }
                case "clear": foreach (var f in _w.Figures.ToArray()) _w.RemoveFigure(f); foreach (var pr in _w.Props.ToArray()) _w.RemoveProp(pr); break;
                case "platforms": _showPlatforms = !_showPlatforms; break;
                case "place":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } pf && p.Length >= 4 &&
                        _w.Env.Below(float.Parse(p[2], inv), float.Parse(p[3], inv)) is { } pl)
                    {
                        pf.PlaceAt(pl, float.Parse(p[2], inv));
                        pf.SpawnT = 0.999f;
                    }
                    break;
                case "rel":
                    if (p.Length >= 4 && Enum.TryParse<Relation>(p[3], true, out var rel))
                        _w.Fight.Pairs[FightSettings.PairKey(p[1], p[2])] = rel;
                    break;
                case "style":
                    // style <Name> <Walk|Run|Idle|Climb|Jump|Fight|Celebrate> <value>
                    if (p.Length >= 4 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } sfig)
                    {
                        var c = sfig.StyleChoice;
                        bool ok = p[2].ToLowerInvariant() switch
                        {
                            "walk" => Enum.TryParse<WalkStyle>(p[3], true, out var a1) && Set(() => c.Walk = a1),
                            "run" => Enum.TryParse<RunStyle>(p[3], true, out var a2) && Set(() => c.Run = a2),
                            "idle" => Enum.TryParse<IdleHabit>(p[3], true, out var a3) && Set(() => c.Idle = a3),
                            "climb" => Enum.TryParse<ClimbStyle>(p[3], true, out var a4) && Set(() => c.Climb = a4),
                            "jump" => Enum.TryParse<JumpStyle>(p[3], true, out var a5) && Set(() => c.Jump = a5),
                            "fight" => Enum.TryParse<FightStyle>(p[3], true, out var a6) && Set(() => c.Fight = a6),
                            "celebrate" => Enum.TryParse<CelebrateStyle>(p[3], true, out var a7) && Set(() => c.Celebrate = a7),
                            "rope" => Enum.TryParse<RopeStyle>(p[3], true, out var a8) && Set(() => c.Rope = a8),
                            _ => false,
                        };
                        if (!ok) result = "failed";
                    }
                    break;
                case "fidget":
                    if (p.Length >= 3 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } ffig && Enum.TryParse<Fidget>(p[2], true, out var fk))
                        ffig.StartFidget(fk);
                    break;
                case "mood":
                    // mood <Name> <joy|sad|fear|annoy|stamina|hp> <value>
                    if (p.Length >= 4 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } mf)
                    {
                        float v = float.Parse(p[3], inv);
                        switch (p[2].ToLowerInvariant())
                        {
                            case "joy": mf.Brain.Joy = v; break;
                            case "sad": mf.Brain.Sadness = v; break;
                            case "fear": mf.Brain.Fear = v; break;
                            case "annoy": mf.Brain.Annoyance = v; break;
                            case "stamina": mf.Brain.Stamina = v; break;
                            case "hp": mf.HP = v; break;
                        }
                    }
                    break;
                case "taste":
                    // taste <Name> <Thing> <-1..1>   |   taste <Name> fav|hate <Colour>
                    if (p.Length >= 4 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } tf)
                    {
                        if (p[2] == "fav") tf.Tastes.FavoriteColour = p[3];
                        else if (p[2] == "hate") tf.Tastes.DislikedColour = p[3];
                        else if (Enum.TryParse<Thing>(p[2], true, out var th)) tf.Tastes.Set(th, float.Parse(p[3], inv));
                        else result = "failed";
                    }
                    break;
                case "fond":
                    if (p.Length >= 3 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } fdf)
                    {
                        fdf.Brain.UserFondness = float.Parse(p[2], inv);
                        if (p.Length >= 4) fdf.Brain.CursorTrust = float.Parse(p[3], inv);
                    }
                    break;
                case "ko":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } kf) kf.DebugKnockOut(_w);
                    break;
                case "deathrule":
                    if (p.Length >= 2 && Enum.TryParse<DeathRule>(p[1], true, out var dr)) _w.Fight.OnZeroHealth = dr;
                    break;
                case "gear":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } gf && p.Length >= 3 && Enum.TryParse<Gear>(p[2], true, out var g)) gf.Gear = g;
                    break;
                case "hp":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } hf && p.Length >= 3) hf.HP = float.Parse(p[2], inv);
                    break;
                case "studio":
                    // studio [page] [figure name]
                    OpenStudio(p.Length > 1 ? p[1] : null, p.Length > 2 && _w.Figures.FirstOrDefault(f => f.Name == p[2]) is { } sfi ? sfi.Id : 0);
                    break;
                case "quick": ToggleQuick(); break;
                case "mockdump":
                    // Dev: real Studio data for previewing the page in a browser (Studio/web/mock-*.json, not shipped).
                    File.WriteAllText(Path.Combine(p[1], "mock-init.json"), System.Text.Json.JsonSerializer.Serialize(StudioInit(), Json));
                    File.WriteAllText(Path.Combine(p[1], "mock-state.json"), System.Text.Json.JsonSerializer.Serialize(StudioState(), Json));
                    break;
                case "throwat":
                    // throwat <Name> [height 0..1] [speed]: a ball flies at the figure as if you threw it
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } taf)
                    {
                        float hk = p.Length > 2 ? float.Parse(p[2], inv) : 0.5f, spd = (p.Length > 3 ? float.Parse(p[3], inv) : 1400) * _w.Scale;
                        var tb = SpawnProp(PropKind.Ball);
                        float side = _w.Rng.NextDouble() < 0.5 ? -1 : 1;
                        var target = new Vector2(taf.Base.X, taf.Base.Y - taf.Height * hk);
                        tb.Pos = target + new Vector2(side * 700 * _w.Scale, -60 * _w.Scale);
                        float tt = 700 * _w.Scale / spd;
                        tb.Vel = new Vector2(-side * spd, (target.Y - tb.Pos.Y) / tt - 0.5f * tb.Grav * tt);
                        tb.ThrownByUser = true;
                        tb.SinceTouch = 0;
                    }
                    break;
                case "witness":
                    // witness <Actor|user> <Target> <hurt|help|kind> [mag]: everyone nearby sees it
                    if (p.Length >= 4 && _w.Figures.FirstOrDefault(f => f.Name == p[2]) is { } wt && Enum.TryParse<SocialAct>(p[3], true, out var act))
                    {
                        var actor = p[1] == "user" ? null : _w.Figures.FirstOrDefault(f => f.Name == p[1]);
                        foreach (var o in _w.Figures.Where(o => o != wt && o != actor))
                            World.Log($"before: {o.Name} feels {(actor != null ? o.Brain.AffinityWith(actor) : o.Brain.UserFondness):0.00} about {p[1]}, {o.Brain.AffinityWith(wt):0.00} about {wt.Name}");
                        _w.Witness(actor, wt, act, p.Length > 4 ? float.Parse(p[4], inv) : 1);
                        foreach (var o in _w.Figures.Where(o => o != wt && o != actor))
                            World.Log($"after:  {o.Name} feels {(actor != null ? o.Brain.AffinityWith(actor) : o.Brain.UserFondness):0.00} about {p[1]}, state {o.Brain.State}, emote {o.CurrentEmote}");
                    }
                    break;
                case "forgive":
                    foreach (var ff2 in _w.Figures.Where(x => !x.Hunter && (p.Length < 2 || x.Name == p[1]))) ff2.Brain.Forgive();
                    break;
                case "summon":
                    World.Log("summon: " + Summon(line[(line.IndexOf(' ') + 1)..]));
                    break;
                case "use":
                    // use <Name> <itemKey> <verb>
                    if (p.Length >= 4 && _w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } uf && _w.Items.FirstOrDefault(i => i.Def.Key == p[2]) is { } ui && Enum.TryParse<Verb>(p[3], true, out var uv))
                        uf.Brain.ForceUse(ui, uv, _w);
                    break;
                case "fakecursor":
                    _fakeCursor = p.Length >= 3 ? new Vector2(float.Parse(p[1], inv), float.Parse(p[2], inv)) : null;
                    break;
                case "boxcursor":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } bcf) bcf.Brain.Force("boxcursor", Array.Empty<string>(), _w);
                    break;
                case "moveitem":
                    // moveitem <key> <x> <y>
                    if (p.Length >= 4 && _w.Items.LastOrDefault(i => i.Def.Key == p[1]) is { } mi)
                    {
                        mi.Pos = new Vector2(float.Parse(p[2], inv), float.Parse(p[3], inv));
                        mi.Vel = default; mi.OnGround = false; mi.Angle = 0;
                    }
                    break;
                case "perf":
                    // perf looks|items|all|none: skip drawing parts (debug)
                    Figure.SkipLooks = p[1] is "looks" or "all";
                    _skipItems = p[1] is "items" or "all";
                    break;
                case "screen": World.Log(ScreenReport()); break;
                case "pop": ShowPop(p[1], int.Parse(p[2], inv)); break;
                case "wish":
                    // wish <Name>: make that figure want something now (debug)
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } wf) World.Log($"wish {wf.Name}: {wf.Brain.ForceWish(_w)}");
                    break;
                case "confess":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } cf) World.Log($"confess {cf.Name}: {cf.Brain.ForceConfess(_w)}");
                    break;
                case "romance":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } rf) World.Log($"romance {rf.Name}: {rf.Brain.RomanceDebug(_w)}");
                    break;
                case "love":
                    // love <A> <B> [0..1]: A falls for B (debug)
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } la && _w.Figures.FirstOrDefault(f => f.Name == p[2]) is { } lb)
                        World.Log("love: " + la.Brain.ForceLove(lb, p.Length > 3 ? float.Parse(p[3], inv) : 0.8f));
                    break;
                case "testwin": TestWindow(p); break;
                case "holiday": _w.HolidayOverride = Enum.TryParse<Holiday>(p[1], true, out var hol) && hol != Holiday.None ? hol : null; break;
                case "party": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } pf2) pf2.Brain.ThrowParty(_w); break;
                case "game": if (p[1] == "stop") StopGame(); else { World.Log(StartGame(Enum.Parse<GameKind>(p[1], true), p.Length > 2 ? _w.Figures.FirstOrDefault(f => f.Name == p[2]) : null)); if (_w.Game != null && p.Contains("quick")) _w.Game.Count = MathF.Min(_w.Game.Count, 0.3f); } break;
                case "photo": TakePhoto(); break;
                case "tourney": World.Log(StartTourney()); break;
                case "happening":
                    if (p.Length > 1 && p[1] == "stop") { if (_w.Happening is { } hp) EndHappening(hp, false); }
                    else if (p.Length > 1 && p[1] == "next" && _w.Happening is { } hn) { hn.PhaseT = 999; }
                    else World.Log("happening: " + StartHappening(p.Length > 1 ? p[1] : "festival"));
                    break;
                case "remove": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } remF) _w.RemoveFigure(remF); break;
                case "figlook": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } flF) { var lk = flF.Look; switch (p[2]) { case "hat": lk.Hat = p.Length > 3 ? p[3] : ""; break; case "hair": lk.Hair = p.Length > 3 ? p[3] : ""; break; } } break;
                case "removeitems": foreach (var ri in _w.Items.Where(i => i.Def.Key == p[1]).ToList()) _w.RemoveItem(ri); break;
                case "baby": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } ba && _w.Figures.FirstOrDefault(f => f.Name == p[2]) is { } bb) MakeBaby(ba, bb); break;
                case "grow": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } growF) growF.Brain.Grown = Math.Clamp(float.Parse(p[2], inv), 0, 1); _growAt = 0; break;
                case "home":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } homeF)
                        homeF.Brain.ClaimHome(_w.Items.Where(Brain.HomeKind).Where(i => i.OwnerId == 0).OrderBy(i => Vector2.Distance(i.Pos, homeF.Base)).FirstOrDefault(), _w);
                    break;
                case "talk": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } talkF) World.Log($"talk {talkF.Name}: {talkF.Brain.Talk(string.Join(' ', p.Skip(2)), _w)}"); break;
                case "back": _w.OnUserBack(p.Length > 1 ? double.Parse(p[1], inv) : 1800); break;
                case "pet": World.Log("summon: " + Summon(p.Length > 1 ? p[1] : "cat")); break;
                case "petop": if (_w.Pets.FirstOrDefault(x => x.Name == p[1]) is { } po) { PetCareEdit(po, p[2], default); World.Log($"petop {po.Name} {p[2]}: {po.Activity}"); } break;
                case "petset":
                    if (_w.Pets.FirstOrDefault(x => x.Name == p[1]) is { } ps)
                    {
                        float pv = float.Parse(p[3], inv);
                        switch (p[2]) { case "hunger": ps.Hunger = pv; break; case "thirst": ps.Thirst = pv; break; case "bladder": ps.Bladder = pv; break; case "bowel": ps.Bowel = pv; break;
                            case "energy": ps.Energy = pv; break; case "attention": ps.Attention = pv; break; case "boredom": ps.Boredom = pv; break; case "weight": ps.Weight = pv; break; case "age": ps.Age = pv; break; case "clean": ps.Clean = pv; break; case "health": ps.Health = pv; break;
                            case "sick": ps.Sick = (Illness)(int)pv; break; }
                    }
                    break;
                case "pets": foreach (var px in _w.Pets) World.Log($"pet {px.Name} {px.Kind} @{px.Pos.X:0},{px.Pos.Y:0} age={px.Age:F2} {px.Activity} | {px.Mood} | H{px.Hunger:F2} T{px.Thirst:F2} B{px.Bladder:F2}/{px.Bowel:F2} E{px.Energy:F2} A{px.Attention:F2} F{px.Boredom:F2} S{px.Stress:F2} W{px.Weight:F2} St{px.Stamina:F2} choice={px.LastChoice}"); break;
                case "petmode": SetPetMode(p[1] == "on"); break;
                case "bout":
                    if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is { } bw && _w.Figures.FirstOrDefault(x => x.Name == p[2]) is { } bl) { bw.Brain.RecordBout(bl, true); bl.Brain.RecordBout(bw, false); }
                    break;
                case "season": _w.Seasons.Override = Enum.TryParse<Season>(p[1], true, out var sn) ? sn : null; _w.Seasons.Gust(_clock.Elapsed.TotalSeconds); break;
                case "garden": foreach (var gi in _w.Items.Where(i => i.IsPlant)) gi.Growth = float.Parse(p[1], inv); _gardenAt = 0; break;
                case "trinket": _trinketAt = 0; break;
                case "lasso": if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is { } lf) lf.Brain.DebugLasso(_w, p.Length > 2 ? p[2] : "cursor"); break;
                case "night": _w.NightOverride = p[1] == "off" ? null : float.Parse(p[1], inv); break;
                case "warm":
                    if (_w.Items.FirstOrDefault(i => i.Def.Key == "campfire") is { } fire)
                        foreach (var wn in p.Skip(1)) if (_w.Figures.FirstOrDefault(x => x.Name == wn) is { } wf2) wf2.Brain.DebugWarm(_w, fire);
                    break;
                case "story": if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is { } storyF) World.Log("story: " + storyF.Brain.DebugStory(_w)); break;
                case "items": World.Log("items: " + string.Join("; ", _w.Items.Select(i => $"{i.Def.Key}@({i.Pos.X:0},{i.Pos.Y:0}){(i.Growth > 0 ? $" g={i.Growth:0.00}" : "")}"))); break;
                case "realwx":
                    // realwx <place...> | realwx off <mode>
                    if (p.Length > 2 && p[1] == "off") { _settings.WeatherMode = p[2]; _settings.WeatherPlace = ""; _settings.WeatherLat = _settings.WeatherLon = null; RealWeatherStatus = ""; }
                    else SetWeatherPlace(string.Join(' ', p.Skip(1)));
                    break;
                case "record": World.Log("record: " + StartRecording(p.Length > 1 ? int.Parse(p[1]) : 10, Path.GetTempPath())); break;
                case "msg":
                    // msg {json}: as if the Studio sent it (debug).
                    OnStudioMessage(System.Text.Json.JsonDocument.Parse(line[(line.IndexOf(' ') + 1)..]).RootElement.Clone());
                    break;
                case "voice": World.Log("voice: " + VoiceCommand(line[(line.IndexOf(' ') + 1)..])); break;
                case "voiceinfo": World.Log("voiceinfo: " + string.Join(", ", System.Speech.Recognition.SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name + " " + r.Name))); break;
                case "listen": World.Log("listen: " + Listen()); break;
                case "shortcuttest": try { MakeShortcut(Path.Combine(Path.GetTempPath(), "sf-test.lnk"), ExePath); World.Log("shortcut: ok " + File.Exists(Path.Combine(Path.GetTempPath(), "sf-test.lnk"))); } catch (Exception e) { World.Log("shortcut: " + e.Message); } break;
                case "update":
                    if (p.Length > 1 && p[1] == "check") _ = CheckForUpdate().ContinueWith(t => World.Log("update: " + t.Result));
                    else if (p.Length > 1 && p[1] == "fake") { _update = (new Version(9, 9, 9), "v0.9.0", $"https://github.com/{Repo}/releases/download/v0.9.0/Doodlefolk-v0.9.0-win-x64.zip", long.Parse(p[2]), "test"); World.Log("update: faked"); }
                    else if (p.Length > 1 && p[1] == "dry") _ = ApplyUpdate(true).ContinueWith(t => World.Log("update: " + t.Result));
                    else if (p.Length > 1 && p[1] == "apply") _ = ApplyUpdate().ContinueWith(t => World.Log("update: " + t.Result));
                    break;
                case "focus": World.Log("focus: " + (p.Length > 1 && p[1] == "stop" ? StopFocus() : StartFocus(p.Length > 1 ? int.Parse(p[1]) : 25))); break;
                case "rarepet": { var rp = SpawnPet(Enum.TryParse<PetKind>(p.Length > 2 ? p[2] : "Cat", true, out var rk) ? rk : PetKind.Cat, quiet: true); rp.GiveRareCoat(p.Length > 1 ? p[1] : "golden"); RareSeen(rp.Rare); break; }
                case "visit": World.Log("visit: " + (Enum.TryParse<VisitorKind>(p.Length > 1 ? p[1] : "", true, out var vk) ? Visit(vk) : "kinds: " + string.Join(", ", Enum.GetNames<VisitorKind>().Skip(1)))); break;
                case "save":
                    try { SaveCast(); World.Log("save: ok"); } catch (Exception e) { World.Log("save failed: " + e); }
                    break;
                case "snapfig":
                {
                    // snapfig Name w h [zoom]: the live scene around a figure (centred on it, ground at the bottom).
                    if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is not { } sf2) break;
                    float sw = float.Parse(p[2], inv), sh = float.Parse(p[3], inv);
                    var ar2 = new RectangleF(sf2.Base.X - sw / 2, sf2.Base.Y - sh + 12, sw, sh);
                    _r.Snapshot(ar2, a => DrawScene(a), Path.Combine(Path.GetTempPath(), "doodlefolk_snap.png"), new Color4(0.96f, 0.95f, 0.92f, 1), p.Length > 4 ? float.Parse(p[4], inv) : 1);
                    break;
                }
                case "toy": World.Log("toy: " + Toybox(p.Length > 1 ? p[1] : "")); break;
                case "ghost": World.Log("ghost: " + (_settings.Memorials.LastOrDefault() is { } gm ? GhostOf(gm) : "nobody to remember")); break;
                case "passaway": if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } paf) paf.PassAway(_w); break;
                case "quest": World.Log("quest: " + NewQuest(null, p.Length > 1 && Enum.TryParse<QuestKind>(p[1], true, out var qk) ? qk : null)); break;
                case "album": World.Log("album: " + TakeAlbumPhoto("snapshot", p.Length > 1 ? string.Join(' ', p.Skip(1)) : "A debug snapshot", _w.Figures.ToList(), _w.Pets.ToList())); break;
                case "snappet":
                {
                    // snappet <name or kind> [zoom]: one animal, close up, on paper.
                    var sp = _w.Pets.FirstOrDefault(x => x.Name.Equals(p[1], StringComparison.OrdinalIgnoreCase) || x.Kind.ToString().Equals(p[1], StringComparison.OrdinalIgnoreCase));
                    if (sp == null) break;
                    float pz = _w.Scale, z = p.Length > 2 ? float.Parse(p[2], inv) : 6;
                    _r.Snapshot(new RectangleF(0, 0, 60 * pz, 44 * pz), _ =>
                    {
                        _clip = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);
                        var saved = sp.Pos;
                        sp.Pos = new Vector2(30 * pz, 38 * pz);
                        sp.Draw(_r);
                        sp.Pos = saved;
                    }, Path.Combine(Path.GetTempPath(), "doodlefolk_snap.png"), new Color4(0.96f, 0.95f, 0.92f, 1), z);
                    break;
                }
                case "snaparea":
                {
                    // snaparea x y w h [zoom]: the live scene in that area, offscreen, to %TEMP%\doodlefolk_snap.png.
                    var ar = new RectangleF(float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv), float.Parse(p[4], inv));
                    _r.Snapshot(ar, a => DrawScene(a), Path.Combine(Path.GetTempPath(), "doodlefolk_snap.png"), new Color4(0.96f, 0.95f, 0.92f, 1), p.Length > 5 ? float.Parse(p[5], inv) : 1);
                    break;
                }
                case "snap" when p.Length > 1 && p[1] == "pets":
                {
                    var pets = _w.Pets.ToList();
                    float ps2 = _w.Scale;
                    _r.Snapshot(new RectangleF(0, 0, Math.Max(1, pets.Count) * 70 * ps2, 90 * ps2), _ =>
                    {
                        _clip = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);
                        for (int i = 0; i < pets.Count; i++)
                        {
                            var pt = pets[i];
                            var saved = pt.Pos;
                            pt.Pos = new Vector2(35 * ps2 + i * 70 * ps2, 80 * ps2);
                            pt.Draw(_r);
                            pt.Pos = saved;
                        }
                    }, Path.Combine(Path.GetTempPath(), "doodlefolk_snap.png"), new Color4(0.96f, 0.95f, 0.92f, 1), 3);
                    break;
                }
                case "snap":
                {
                    // Debug: offscreen picture of everyone (or one figure) on paper, saved to %TEMP%\doodlefolk_snap.png.
                    var who = p.Length > 1 ? _w.Figures.Where(x => x.Name == p[1]).ToList() : _w.Figures.ToList();
                    float snapS = _w.Scale;
                    var snapArea = new RectangleF(0, 0, Math.Max(1, who.Count) * 120 * snapS, 190 * snapS);
                    _r.Snapshot(snapArea, _ =>
                    {
                        _clip = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);
                        for (int i = 0; i < who.Count; i++)
                        {
                            var fx = who[i];
                            Vector2 shift = new Vector2(60 * snapS + i * 120 * snapS, 170 * snapS) - fx.Base;
                            var saved = fx.Jt.ToArray();
                            for (int j = 0; j < fx.Jt.Length; j++) fx.Jt[j] += shift;
                            fx.Draw(_r);
                            Array.Copy(saved, fx.Jt, saved.Length);
                        }
                    }, Path.Combine(Path.GetTempPath(), "doodlefolk_snap.png"), new Color4(0.96f, 0.95f, 0.92f, 1), 3);
                    break;
                }
                case "weight": if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is { } wtF) wtF.Weight = Math.Clamp(float.Parse(p[2], inv), 0, 1); break;
                case "weather": if (Enum.TryParse<WeatherKind>(p[1], true, out var wk)) _w.Weather.Start(wk, _clock.Elapsed.TotalSeconds, _w.Rng, _w); break;
                case "say":
                    // say <Name> <text...>: an emote bubble (debug)
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } sayf) sayf.Emote(string.Join(' ', p.Skip(2)), 5);
                    break;
                case "tracejumps": World.TraceJumps = !World.TraceJumps; break;
                case "fakemedia":
                    // fakemedia music|video <hwnd> [seconds]
                    if (_w.Screen != null)
                    {
                        double secs = p.Length >= 4 ? double.Parse(p[3], inv) : p.Length >= 3 && p[1] == "music" ? double.Parse(p[2], inv) : 60;
                        _w.Screen.Pretend(p[1] == "video" ? new MediaNow(false, true, (IntPtr)long.Parse(p[2], inv), 0.6f, 0) : new MediaNow(true, false, IntPtr.Zero, 0.6f, 0), secs);
                    }
                    break;
                case "screengo":
                    // screengo <name> word|link|watch|groove
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } sgf) World.Log($"screengo {sgf.Name}: {sgf.Brain.ForceScreen(_w, p[2])}");
                    break;
                case "props":
                    World.Log($"props ({_w.Props.Count}, saved {_settings.Props.Count}): " + string.Join("; ", _w.Props.Select(pr => $"{pr.Kind}#{pr.Id}@({pr.Pos.X:0},{pr.Pos.Y:0}) v=({pr.Vel.X:0},{pr.Vel.Y:0}) holder={pr.Holder?.Name}")) + $" | virtual {_w.Env.Virtual}");
                    break;
                case "town": if (_w.Figures.FirstOrDefault(x => x.Name == p[1]) is { } tnf) World.Log("town: " + tnf.Brain.DebugTown(_w, p.Length > 2 ? p[2] : "", p.Length > 3 ? p[3] : "")); break;
                case "state":
                    World.Log("state: " + string.Join("; ", _w.Figures.Select(f => $"{f.Name}@({f.Base.X:0},{f.Base.Y:0}) {f.Brain.Activity} [{f.CurrentEmote}]{(f.Weapon != null ? " holding " + f.Weapon.Def.Key : "")}")));
                    break;
                case "sfxpeaks": World.Log("sfx peaks: " + _w.Sound?.Peaks()); break;
                case "sfx":
                    if (p.Length >= 2 && Enum.TryParse<Sfx>(p[1], true, out var sx)) _w.Sound?.Play(sx, _w.Cursor, 0.8f);
                    break;
                case "duck":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } dkf) dkf.DuckT = 1.2f;
                    break;
                case "hold":
                    // hold <Name>: lift by the head (as if the user grabbed it)
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } hof)
                    {
                        var at = hof.Jt[J.Head] - new Vector2(0, 90 * _w.Scale);
                        hof.Grab(J.Head);
                        hof.Rag.PinTarget = at;
                    }
                    break;
                case "drop":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } drf) drf.Release(Vector2.Zero);
                    break;
                case "hunter":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } hf2) MakeHunter(hf2, p.Length < 3 || p[2] != "off");
                    break;
                case "studiojs":
                    _studio?.Eval(line[(line.IndexOf(' ') + 1)..]);
                    break;
                case "exit":
                    World.Log("exit requested");
                    ExitThread();
                    break;
                case "stamina":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } sf && p.Length >= 3) sf.Brain.Stamina = float.Parse(p[2], inv);
                    break;
                case "fling":
                    if (_w.Figures.FirstOrDefault(f => f.Name == p[1]) is { } ff && p.Length >= 4)
                    {
                        // Counts as a throw by the user (like flinging with the mouse).
                        var fv = new Vector2(float.Parse(p[2], inv), float.Parse(p[3], inv));
                        ff.GoRagdoll(fv);
                        ff.LastThrowSpeed = fv.Length();
                    }
                    break;
                default:
                    var fig = _w.Figures.FirstOrDefault(f => f.Name.Equals(p[0], StringComparison.OrdinalIgnoreCase));
                    result = fig != null && p.Length > 1 && fig.Brain.Force(p[1].ToLowerInvariant(), p[2..], _w) ? "ok" : "failed";
                    break;
            }
            if (!_trailer) File.AppendAllText(Path.Combine(Path.GetTempPath(), "doodlefolk_cmd.log"), $"{line} -> {result}\n");
        }
    }

    static bool Set(Action a) { a(); return true; }

    void Dump()
    {
        var state = new
        {
            fps = _fps,
            fpsCap = _settings.FpsCap,
            msRefresh = _msRefresh,
            msRender = _msRender,
            maxFrameMs = _maxDt * 1000,
            msSim = _msSim,
            msDraw = _msDraw,
            regions = _regionsPerFrame,
            hitches = _hitches,
            windows = _w.Env.WindowCount,
            platforms = _w.Env.Platforms.Select(p => new[] { p.X1, p.X2, p.Y, p.Solid ? 1 : 0 }),
            fullscreen = _w.Env.FullscreenActive,
            figures = _w.Figures.Select(f => new
            {
                f.Name,
                mode = f.Mode.ToString(),
                brain = f.Brain.State,
                action = f.Action.ToString(),
                f.Grounded,
                x = f.Base.X,
                y = f.Base.Y,
                stamina = f.Brain.Stamina,
                hp = f.HP,
                atk = f.Atk?.Kind.ToString(),
                emote = f.CurrentEmote,
                fond = f.Brain.UserFondness,
                trust = f.Brain.CursorTrust,
                feels = f.Brain.FeelingsAboutYou(),
                tastes = f.Tastes.Describe(),
                bbox = new[] { f.Jt.Min(j => j.X), f.Jt.Min(j => j.Y), f.Jt.Max(j => j.X), f.Jt.Max(j => j.Y) },
            }),
            props = _w.Props.Select(p => new { kind = p.Kind.ToString(), x = p.Pos.X, y = p.Pos.Y, vx = p.Vel.X, vy = p.Vel.Y, held = p.Holder?.Name, r = p.Radius }),
            matches = _w.Matches.Select(m => new { kind = m.Kind.ToString(), score = m.ScoreText, t = m.T, players = m.Players.Select(p => p.Name), pause = m.Pause, ball = new[] { m.Ball.Pos.X, m.Ball.Pos.Y }, ballHeld = m.Ball.Holder?.Name, rim = new[] { m.Kind == Sport.Basketball ? m.RimCentre.X : m.NetX, m.Kind == Sport.Basketball ? m.RimCentre.Y : 0 } }),
            items = _w.Items.Select(i => new { key = i.Def.Key, x = i.Pos.X, y = i.Pos.Y, ground = i.OnGround, w = i.Def.W * i.Sc, h = i.Def.H * i.Sc, user = i.User?.Name, seated = i.Seated.Where(s => s != null).Select(s => s!.Name), holder = i.Holder?.Name }),
        };
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "doodlefolk_state.json"), JsonSerializer.Serialize(state)); }
        catch (IOException) { }
    }

    protected override void Dispose(bool disposing)
    {
        // ApplicationContext disposes itself on ExitThread, and Program's `using` disposes again.
        if (disposing && !_disposed)
        {
            _disposed = true;
            World.Log("dispose: start");
            try { if (_settings.RememberCast) SaveCast(); World.Log("dispose: saved"); }
            catch (Exception e) { World.Log("dispose: save failed: " + e); }
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Application.Idle -= OnIdle;
            if (_fineTimer) timeEndPeriod(1);
            _tray.Visible = false;
            _tray.Dispose();
            World.Log("dispose: tray");
            _r.Dispose();
            World.Log("dispose: renderer");
            _overlay.Dispose();
            World.Log("dispose: overlay");
            _w.Sound?.Dispose();
            _downloads?.Dispose();
            _sre?.Dispose();
            SteamHub.Shutdown();
            _w.Screen?.Dispose();
        }
        base.Dispose(disposing);
    }
}
