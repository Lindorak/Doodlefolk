using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Numerics;
using System.Text.Json;
using Microsoft.Win32;
using Vortice.Mathematics;
using static StickFight.Native;

namespace StickFight;

/// <summary>Owns the overlay, renderer, tray icon and the frame loop (runs whenever the UI thread is idle,
/// paced by vsync through Present).</summary>
sealed partial class App : ApplicationContext
{
    readonly World _w = new();
    readonly Overlay _overlay;
    readonly Renderer _r;
    readonly NotifyIcon _tray;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly bool _debug;
    readonly Settings _settings = Settings.Load();
    int _refresh = 60;
    double _frameInterval, _nextFrameAt;
    bool _fineTimer;
    readonly string _logPath = Path.Combine(AppContext.BaseDirectory, "stickfight.log");
    double _last, _acc, _nextTopmost, _nextDump, _fpsT;
    int _frames, _fps, _hitches, _hitchAcc;
    float _maxDt, _maxDtAcc;
    double _tRefresh, _tRender, _msRefresh, _msRender;
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
        World.Debug = _debug;
        _showPlatforms = args.Contains("--platforms");
        _w.Scale = ComputeScale(args);
        _w.Fight = _settings.Fight;
        _w.Env.MinHeadroom = 75 * _w.Scale;

        var vs = _w.Env.Virtual;
        _overlay = new Overlay(vs);
        _overlay.Show();
        _r = new Renderer(_overlay.Handle, vs);
        _refresh = RefreshRate();
        ApplyFps();
        var full = new List<Rectangle> { _r.Bounds };
        _r.Frame(full, () => { });
        _r.Frame(full, () => { });
        _overlay.MouseDown += OnMouseDown;
        _overlay.MouseUp += (_, _) => EndPress();
        _tray = BuildTray();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Application.Idle += OnIdle;

        _w.Env.Refresh(_overlay.Handle);
        _w.MakeProp = kind => SpawnProp(kind);
        _w.MakeItem = key => ItemCatalog.Find(key) is { } d ? SpawnItem(d) : null;
        int si = Array.IndexOf(args, "--spawn");
        if (si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out int count))
            for (int i = 0; i < count; i++) Spawn(null);
        else if (_settings.RememberCast && _settings.Figures.Count > 0) RestoreCast();
        else Spawn(null);
    }

    // ---------------- frame rate ----------------

    /// <summary>Turns the user's fps choice into a vsync interval where it divides the refresh rate
    /// evenly (smoothest), otherwise a software limiter.</summary>
    void ApplyFps()
    {
        int cap = _settings.FpsCap;
        _frameInterval = 0;
        if (cap == Settings.MatchMonitor) _r.SyncInterval = 1;
        else if (cap == Settings.Unlimited) _r.SyncInterval = 0;
        else if (cap <= _refresh && _refresh % cap == 0) _r.SyncInterval = (uint)(_refresh / cap);
        else
        {
            _r.SyncInterval = cap > _refresh ? 0u : 1u;
            _frameInterval = 1.0 / cap;
        }
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

    void OnIdle(object? sender, EventArgs e)
    {
        while (!PeekMessage(out _, IntPtr.Zero, 0, 0, 0))
        {
            try { Frame(); }
            catch (Exception ex)
            {
                File.AppendAllText(_logPath, $"{DateTime.Now:O} {ex}\n");
                Thread.Sleep(100);
            }
        }
    }

    void Frame()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_frameInterval > 0)
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
        float dt = MathF.Min(rawDt, 0.05f);
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
        if (now > _nextTopmost) { _nextTopmost = now + 2; _overlay.KeepOnTop(); }
        long tr0 = Stopwatch.GetTimestamp();
        _w.Env.Refresh(_overlay.Handle);
        _tRefresh += Stopwatch.GetElapsedTime(tr0).TotalMilliseconds;

        if (_paused || _w.Env.FullscreenActive)
        {
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
        foreach (var f in _w.Figures) f.ApplyCarry(_w.Env, dt);
        foreach (var p in _w.Props) p.ApplyCarry(_w.Env);

        _acc += dt;
        int n = (int)(_acc / World.Dt);
        if (n > 8) { n = 8; _acc = 0; } else _acc -= n * World.Dt;
        if (_w.HitStop > 0)
        {
            // Freeze-frame on a big hit.
            _w.HitStop -= dt;
            n = 0;
            _acc = 0;
        }
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
            if (_pressItem != null) _pressItem.PinTarget = pin;
            foreach (var it in _w.Items.ToArray()) it.Step(World.Dt, _w);
            _w.Projectiles.RemoveAll(pr => !pr.Step(World.Dt, _w));
            _w.Matches.RemoveAll(m => !m.Step(World.Dt, _w));
            foreach (var f in _w.Figures.Where(f => f.Gone).ToArray())
            {
                if (_pressFig == f) { _pressFig = null; _dragging = false; }
                _w.RemoveFigure(f);
            }
            _w.Fx.Step(World.Dt);
        }
        _prevCursor = _w.Cursor;
        ShoveCursor();

        long tr1 = Stopwatch.GetTimestamp();
        float alpha = _w.HitStop > 0 ? 1 : (float)Math.Clamp(_acc / World.Dt, 0, 1);
        foreach (var f in _w.Figures) if (f != _pressFig || !_dragging) f.BeginInterp(alpha);
        foreach (var p in _w.Props) if (p != _pressProp) p.BeginInterp(alpha);
        bool drew;
        try { drew = Render(); }
        finally
        {
            foreach (var f in _w.Figures) f.EndInterp();
            foreach (var p in _w.Props) p.EndInterp();
        }
        if (!drew) Thread.Sleep(15);
        _tRender += Stopwatch.GetElapsedTime(tr1).TotalMilliseconds;

        _frames++;
        if (now - _fpsT >= 1) { _maxDt = _maxDtAcc; _hitches = _hitchAcc; _maxDtAcc = 0; _hitchAcc = 0; _fps = _frames; _msRefresh = _tRefresh / _frames; _msRender = _tRender / _frames; _tRefresh = _tRender = 0; _frames = 0; _fpsT = now; }
        if (_debug && now > _nextDump) { _nextDump = now + 0.05; Dump(); RunCommands(); }
    }

    readonly HashSet<string> _logged = new();

    void LogOnce(Exception ex)
    {
        string key = ex.GetType().Name + ex.StackTrace?.Split('\n').FirstOrDefault();
        if (_logged.Add(key)) File.AppendAllText(_logPath, $"{DateTime.Now:O} {ex}\n");
    }

    Vector2 _shove;
    /// <summary>Debug: pretend the cursor is here (tests without moving the real mouse).</summary>
    Vector2? _fakeCursor;

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
        _overlay.SetClickThrough(fig == null && hitProp == null && _pressFig == null && _pressProp == null && _pressItem == null && HitItem(c) == null);
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

    void OnMouseDown(object? sender, MouseEventArgs e)
    {
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
            else if (e.Button == MouseButtons.Right) OpenStudio("toys", prop.Id);
            return;
        }
        var (fig, joint) = HitTest(_w.Cursor);
        if (fig == null)
        {
            if (HitItem(_w.Cursor) is { } item)
            {
                if (e.Button == MouseButtons.Left) GrabItem(item);
                else if (e.Button == MouseButtons.Right) OpenStudio("toys", item.Id);
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
        else if (e.Button == MouseButtons.Right) OpenStudio("figure", fig.Id);
    }

    void EndPress()
    {
        ReleaseItem();
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
        if (_dragging) _pressFig.Release(_w.CursorVel);
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
        foreach (var p in _w.Props) _regNow.Add(ToRect(p.Bounds(_w.Env)));
        foreach (var pr in _w.Projectiles) _regNow.Add(ToRect(pr.Bounds()));
        foreach (var m in _w.Matches) _regNow.Add(ToRect(m.Bounds()));
        foreach (var it in _w.Items)
        {
            _regNow.Add(ToRect(it.Bounds()));
            it.Shadow(_w.Env, out var sc, out float srx, out float sry, out _);
            if (srx > 0) _regNow.Add(ToRect(RectangleF.FromLTRB(sc.X - srx, sc.Y - sry, sc.X + srx, sc.Y + sry)));
        }
        if (_w.Fx.Bounds() is RectangleF fx) _regNow.Add(ToRect(fx));

        // Flip model with two buffers: this buffer last held frame N-2, the screen shows N-1.
        _regAll.Clear();
        _regAll.AddRange(_regNow);
        _regAll.AddRange(_regPrev);
        _regAll.AddRange(_regPrev2);
        MergeRects(_regAll);
        _regPrev2.Clear(); _regPrev2.AddRange(_regPrev);
        _regPrev.Clear(); _regPrev.AddRange(_regNow);
        if (_regAll.Count == 0) return false;
        _r.Frame(_regAll, DrawScene);
        return true;
    }

    void DrawScene()
    {
        if (_showPlatforms)
        {
            foreach (var p in _w.Env.Platforms)
                _r.Line(new(p.X1, p.Y), new(p.X2, p.Y), p.Solid ? new Color4(1, 0.6f, 0, 0.8f) : new Color4(0.2f, 1, 0.3f, 0.8f), 3);
            foreach (var wl in _w.Env.Walls)
                if (wl.ReachesTop) _r.Line(new(wl.X, wl.Y1), new(wl.X, wl.Y2), new Color4(0.3f, 0.7f, 1, 0.6f), 2);
        }
        foreach (var it in _w.Items)
        {
            it.Shadow(_w.Env, out var ic, out float irx, out float iry, out float ia);
            if (ia > 0) _r.Oval(ic, irx, iry, new Color4(0, 0, 0, ia));
        }
        DrawItems(false);
        foreach (var f in _w.Figures)
            if (Shadow(f, out var c, out float rx, out float ry, out float a))
                _r.Oval(c, rx, ry, new Color4(0, 0, 0, a));
        foreach (var p in _w.Props)
            if (p.Shadow(_w.Env, out var c, out float rx, out float ry, out float a))
                _r.Oval(c, rx, ry, new Color4(0, 0, 0, a));
        _w.Fx.Draw(_r);
        foreach (var f in _w.Figures) f.Draw(_r);
        DrawItems(true);
        foreach (var it in _w.Items)
        {
            if (it.Holder == null) continue;
            if (it.Holder.Weapon == it) it.Holder.SyncWeapon(it);   // follows the (interpolated) hand
            it.Draw(_r, false, _clock.Elapsed.TotalSeconds);      // carried things in front
        }
        foreach (var pr in _w.Projectiles) pr.Draw(_r);
        foreach (var m in _w.Matches) m.Draw(_r);
        foreach (var p in _w.Props) p.Draw(_r);
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
        float pad = f.HeadR + f.LineW * 2 + 2 * f.S;
        var r = RectangleF.FromLTRB(x1 - pad, y1 - pad, x2 + pad, y2 + pad);
        if (f.CurrentEmote != null)
        {
            // Room for the emote bubble (or rising z's) above the head.
            Vector2 h = f.Jt[J.Head];
            r = RectangleF.Union(r, RectangleF.FromLTRB(h.X - 30 * f.S, h.Y - f.HeadR - 32 * f.S, h.X + 30 * f.S, h.Y));
        }
        if (Shadow(f, out var c, out float rx, out float ry, out _))
            r = RectangleF.Union(r, RectangleF.FromLTRB(c.X - rx, c.Y - ry, c.X + rx, c.Y + ry));
        return ToRect(r);
    }

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
                    grown.Inflate(16, 16);
                    if (!grown.IntersectsWith(rects[j])) continue;
                    rects[i] = Rectangle.Union(rects[i], rects[j]);
                    rects.RemoveAt(j);
                    merged = true;
                    break;
                }
        }
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
        return SpawnFigure(color, UniqueName(cname), traits ?? Personality.Random(_w.Rng), 1);
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
        f.Look = old.Look;
        var plat = _w.Env.Below(old.Base.X, old.Base.Y - 2) ?? RandomSpawnPlatform(0);
        if (plat == null) return old;
        f.PlaceAt(plat, M.ClampIn(old.Base.X, plat.X1 + 4, plat.X2 - 4));
        f.SpawnT = 0.999f;
        old.DropCarried(Vector2.Zero);
        _w.Figures[_w.Figures.IndexOf(old)] = f;
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
        _settings.Figures = _w.Figures.Select(f => new SavedFigure
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
            Affinity = _w.Figures.Where(o => o != f).ToDictionary(o => o.Name, o => f.Brain.AffinityDelta(o)),
        }).ToList();
        _settings.Items = SaveItems();
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
            foreach (var (name, a) in s.Affinity)
                if (made.FirstOrDefault(m => m.f.Name == name).f is { } o) f.Brain.Affinity[o.Id] = a;
        RestoreItems(_settings.Items);
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
        var tray = new NotifyIcon { Icon = AppIcon, Text = "StickFight", Visible = true };
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

    /// <summary>Debug channel: one command per line in %TEMP%\stickfight_cmd.txt, e.g.
    /// "spawn blue", "ball soccer", "Red jump", "Red walk 1200", "fling Red 2000 -1500", "clear".</summary>
    void RunCommands()
    {
        string path = Path.Combine(Path.GetTempPath(), "stickfight_cmd.txt");
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); File.Delete(path); }
        catch (IOException) { return; }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var line in lines)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
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
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "stickfight_cmd.log"), $"{line} -> {result}\n");
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
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "stickfight_state.json"), JsonSerializer.Serialize(state)); }
        catch (IOException) { }
    }

    protected override void Dispose(bool disposing)
    {
        // ApplicationContext disposes itself on ExitThread, and Program's `using` disposes again.
        if (disposing && !_disposed)
        {
            _disposed = true;
            World.Log("dispose: start");
            if (_settings.RememberCast) SaveCast();
            World.Log("dispose: saved");
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
        }
        base.Dispose(disposing);
    }
}
