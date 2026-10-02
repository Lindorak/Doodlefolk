using System.Numerics;
using System.Text;
using static StickFight.Native;

namespace StickFight;

/// <summary>A horizontal surface a figure can stand on: the visible part of a window's top edge,
/// or a monitor's floor (the top of the taskbar). Window edges are one-way; floors are solid.</summary>
sealed class Platform
{
    public IntPtr Hwnd;
    public float Y, PrevY, X1, X2;
    public bool Solid;
}

/// <summary>The visible part of a window's left (Side = -1) or right (Side = +1) edge. Climbable;
/// ReachesTop means it runs all the way up to the window's top edge, so a climber can pull up onto it.</summary>
sealed class Wall
{
    public IntPtr Hwnd;
    public float X, Y1, Y2;
    public int Side;
    public bool ReachesTop;
}

/// <summary>What the figures can "see": monitors, top-level windows (in z-order) and the platforms derived from them.</summary>
sealed class Env
{
    public Rectangle Virtual;
    public Rectangle[] MonBounds = Array.Empty<Rectangle>(), MonWork = Array.Empty<Rectangle>();
    public readonly List<Platform> Platforms = new();
    public readonly List<Wall> Walls = new();
    public bool FullscreenActive;
    public int WindowCount => _wins.Count;
    static readonly int[] Sides = { -1, 1 };
    public float MinHeadroom = 140;

    Dictionary<IntPtr, RECT> _rects = new(), _prev = new();
    readonly Dictionary<IntPtr, string> _classes = new();
    readonly List<(IntPtr hwnd, RECT rect)> _wins = new();
    readonly List<(float a, float b)> _spans = new(), _next = new();
    readonly EnumWindowsProc _callback;
    IntPtr _self;

    public Env()
    {
        _callback = OnWindow;
        RefreshMonitors();
    }

    public void RefreshMonitors()
    {
        var screens = Screen.AllScreens;
        MonBounds = screens.Select(s => s.Bounds).ToArray();
        MonWork = screens.Select(s => s.WorkingArea).ToArray();
        Virtual = SystemInformation.VirtualScreen;
    }

    public void Refresh(IntPtr self)
    {
        _self = self;
        (_prev, _rects) = (_rects, _prev);
        _rects.Clear();
        _wins.Clear();
        EnumWindows(_callback, IntPtr.Zero);
        BuildPlatforms();
        DetectFullscreen();
        if (_classes.Count > 4000) _classes.Clear();
    }

    string ClassOf(IntPtr hwnd)
    {
        if (_classes.TryGetValue(hwnd, out var c)) return c;
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return _classes[hwnd] = sb.ToString();
    }

    bool OnWindow(IntPtr h, IntPtr _)
    {
        if (h == _self || !IsWindowVisible(h) || IsIconic(h)) return true;
        long ex = ExStyle(h);
        if ((ex & (WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW)) != 0) return true;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
        string cls = ClassOf(h);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, 16) != 0 && !GetWindowRect(h, out r)) return true;
        if (r.Width < 60 || r.Height < 40) return true;
        _rects[h] = r;
        _wins.Add((h, r));
        return true;
    }

    void BuildPlatforms()
    {
        Platforms.Clear();
        Walls.Clear();
        for (int i = 0; i < _wins.Count; i++)
        {
            var (h, r) = _wins[i];
            float y = r.Top;
            int mon = MonitorAt(r.Left + r.Width / 2f, y);
            if (mon < 0) mon = MonitorAt(MathF.Max(r.Left, Virtual.Left), y);
            if (mon < 0) continue;
            if (y < MonBounds[mon].Top + MinHeadroom || y >= MonWork[mon].Bottom - 4) continue;

            _spans.Clear();
            _spans.Add((r.Left, r.Right));
            for (int j = 0; j < i && _spans.Count > 0; j++)
            {
                var o = _wins[j].rect;
                if (o.Top <= y && o.Bottom > y) Subtract(o.Left, o.Right);
            }
            float dy = Delta(h).Y;
            foreach (var (a, b) in _spans)
                if (b - a >= 24)
                    Platforms.Add(new Platform { Hwnd = h, Y = y, PrevY = y - dy, X1 = a, X2 = b });

            foreach (int side in Sides)
            {
                float x = side < 0 ? r.Left : r.Right;
                var mb = MonBounds[mon];
                if (x <= mb.Left + 4 || x >= mb.Right - 4) continue;
                _spans.Clear();
                _spans.Add((r.Top, MathF.Min(r.Bottom, MonWork[mon].Bottom)));
                for (int j = 0; j < i && _spans.Count > 0; j++)
                {
                    var o = _wins[j].rect;
                    if (o.Left < x && o.Right > x) Subtract(o.Top, o.Bottom);
                }
                foreach (var (a, b) in _spans)
                    if (b - a >= 40)
                        Walls.Add(new Wall { Hwnd = h, X = x, Y1 = a, Y2 = b, Side = side, ReachesTop = MathF.Abs(a - r.Top) < 1 });
            }
        }
        foreach (var w in MonWork)
            Platforms.Add(new Platform { Hwnd = IntPtr.Zero, Y = w.Bottom, PrevY = w.Bottom, X1 = w.Left, X2 = w.Right, Solid = true });
    }

    void Subtract(float a, float b)
    {
        _next.Clear();
        foreach (var (x1, x2) in _spans)
        {
            if (b <= x1 || a >= x2) { _next.Add((x1, x2)); continue; }
            if (a > x1) _next.Add((x1, a));
            if (b < x2) _next.Add((b, x2));
        }
        _spans.Clear();
        _spans.AddRange(_next);
    }

    void DetectFullscreen()
    {
        FullscreenActive = false;
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _self) return;
        string cls = ClassOf(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return;
        if (!GetWindowRect(fg, out var r)) return;
        foreach (var m in MonBounds)
            if (r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom) { FullscreenActive = true; return; }
    }

    int MonitorAt(float x, float y)
    {
        for (int i = 0; i < MonBounds.Length; i++)
        {
            var m = MonBounds[i];
            if (x >= m.Left && x < m.Right && y >= m.Top && y < m.Bottom) return i;
        }
        return -1;
    }

    /// <summary>How far a window moved since the previous refresh.</summary>
    public Vector2 Delta(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Vector2.Zero;
        if (_rects.TryGetValue(hwnd, out var r) && _prev.TryGetValue(hwnd, out var p))
            return new(r.Left - p.Left, r.Top - p.Top);
        return Vector2.Zero;
    }

    public bool TryRect(IntPtr hwnd, out RECT r) => _rects.TryGetValue(hwnd, out r);

    /// <summary>The platform under a standing point, preferring the one the figure was already on.</summary>
    public Platform? SupportAt(float x, float y, IntPtr hwnd)
    {
        Platform? any = null;
        foreach (var p in Platforms)
        {
            if (x < p.X1 - 1 || x > p.X2 + 1 || MathF.Abs(p.Y - y) > 2.5f) continue;
            if (p.Hwnd == hwnd) return p;
            any ??= p;
        }
        return any;
    }

    public Wall? WallAt(IntPtr hwnd, int side, float y)
    {
        foreach (var w in Walls)
            if (w.Hwnd == hwnd && w.Side == side && y >= w.Y1 - 2 && y <= w.Y2 + 2) return w;
        return null;
    }

    /// <summary>First platform crossed by a point falling from yPrev to yNow at x.</summary>
    public Platform? FindLanding(float x, float yPrev, float yNow)
    {
        Platform? best = null;
        foreach (var p in Platforms)
        {
            if (x < p.X1 || x > p.X2) continue;
            bool hit = p.Solid ? yNow >= p.Y : yPrev <= p.PrevY + 0.5f && yNow >= p.Y;
            if (hit && (best == null || p.Y < best.Y)) best = p;
        }
        return best;
    }

    /// <summary>Highest platform at or below y.</summary>
    public Platform? Below(float x, float y)
    {
        Platform? best = null;
        foreach (var p in Platforms)
            if (x >= p.X1 && x <= p.X2 && p.Y >= y - 1 && (best == null || p.Y < best.Y)) best = p;
        return best;
    }

    /// <summary>Left/right/top limits of the monitor column containing x.</summary>
    public (float L, float R, float T) BoundsAt(float x)
    {
        foreach (var m in MonBounds)
            if (x >= m.Left && x < m.Right) return (m.Left, m.Right, m.Top);
        return (Virtual.Left, Virtual.Right, Virtual.Top);
    }
}
