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
    /// <summary>Springiness (trampolines): landing on it launches you back up.</summary>
    public float Bounce;
    /// <summary>Set when this is part of an object (a seat, a mattress, a table top).</summary>
    public Item? Item;
    /// <summary>Set when this is a line of text or the top of a picture inside a window.</summary>
    public Seen? Seen;
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
    public IEnumerable<IntPtr> Hwnds => _rects.Keys;
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

    // Objects on the desktop offer surfaces too; they get negative pseudo-handles (see Item.Handle).
    readonly Dictionary<IntPtr, Vector2> _itemDelta = new();
    readonly Dictionary<IntPtr, (Vector2 last, RECT rect)> _items = new();

    /// <summary>Add the surfaces of desktop objects (seats, mattresses, table tops) as platforms.</summary>
    public void AddItemSurfaces(List<Item> items)
    {
        _itemDelta.Clear();
        var seen = new HashSet<IntPtr>();
        foreach (var it in items)
        {
            var h = it.Handle;
            seen.Add(h);
            Vector2 d = _items.TryGetValue(h, out var prev) ? it.Pos - prev.last : Vector2.Zero;
            if (d.LengthSquared() > 400 * 400) d = Vector2.Zero;   // teleported: don't fling riders
            _itemDelta[h] = d;
            var rect = new RECT { Left = (int)MathF.Round(it.Pos.X), Top = (int)MathF.Round(it.Pos.Y - it.Def.H * it.Sc * it.ScaleY), Right = (int)MathF.Round(it.Pos.X + 1), Bottom = (int)MathF.Round(it.Pos.Y) };
            _items[h] = (it.Pos, rect);
            foreach (var (y, x1, x2, bounce) in it.Surfaces())
                Platforms.Add(new Platform { Hwnd = h, Y = y, PrevY = y - d.Y, X1 = x1, X2 = x2, Bounce = bounce, Item = it });
        }
        foreach (var k in _items.Keys.Where(k => !seen.Contains(k)).ToList()) _items.Remove(k);
    }

    /// <summary>Text lines and picture tops in windows that have been read become ledges (only where nothing covers them).</summary>
    public void AddScreenSurfaces(IReadOnlyList<ScreenSnap> snaps)
    {
        foreach (var s in snaps)
        {
            int z = _wins.FindIndex(w => w.hwnd == s.Hwnd);
            if (z < 0) continue;
            var r = _wins[z].rect;
            if (r.Width != s.Win.Width || r.Height != s.Win.Height) continue;   // resized: wait for a fresh reading
            float ox = r.Left - s.Win.Left, oy = r.Top - s.Win.Top, dy = Delta(s.Hwnd).Y;
            int mon = MonitorAt(r.Left + r.Width / 2f, MathF.Max(r.Top, Virtual.Top));
            if (mon < 0) continue;
            float lastY = float.NaN;
            foreach (var t in s.Things)
            {
                if (t.Kind is not (SeenKind.Line or SeenKind.Image)) continue;
                float y = MathF.Round(t.Rect.Top + oy);
                if (y < MonBounds[mon].Top + 70 || y > MonWork[mon].Bottom - 12 || y < r.Top + 24) continue;
                // Lines that wrap a paragraph come in pairs at nearly the same height; one ledge is enough.
                if (MathF.Abs(y - lastY) < 3 && t.Kind == SeenKind.Line) continue;
                lastY = y;
                _spans.Clear();
                _spans.Add((t.Rect.Left + ox, t.Rect.Right + ox));
                for (int j = 0; j < z && _spans.Count > 0; j++)
                {
                    var o = _wins[j].rect;
                    if (o.Top <= y && o.Bottom > y) Subtract(o.Left, o.Right);
                }
                foreach (var (a, b) in _spans)
                    if (b - a >= 24)
                        Platforms.Add(new Platform { Hwnd = s.Hwnd, Y = y, PrevY = y - dy, X1 = a, X2 = b, Seen = t });
            }
        }
    }

    /// <summary>Where a window is now relative to where it was when it was read.</summary>
    public Vector2 SnapOffset(ScreenSnap s) =>
        _rects.TryGetValue(s.Hwnd, out var r) && r.Width == s.Win.Width && r.Height == s.Win.Height
            ? new(r.Left - s.Win.Left, r.Top - s.Win.Top) : new(float.NaN, float.NaN);

    /// <summary>Whether a window is still around and how much of a screen rectangle in it isn't covered by other windows (0..1).</summary>
    public float Visible(IntPtr hwnd, RectangleF rect)
    {
        int z = _wins.FindIndex(w => w.hwnd == hwnd);
        if (z < 0 || rect.Width <= 0) return 0;
        _spans.Clear();
        _spans.Add((rect.Left, rect.Right));
        float cy = rect.Top + rect.Height / 2;
        for (int j = 0; j < z && _spans.Count > 0; j++)
        {
            var o = _wins[j].rect;
            if (o.Top <= cy && o.Bottom > cy) Subtract(o.Left, o.Right);
        }
        return _spans.Sum(s => s.b - s.a) / rect.Width;
    }

    public RECT? RectOf(IntPtr hwnd) => _rects.TryGetValue(hwnd, out var r) ? r : null;

    /// <summary>How far a window moved since the previous refresh.</summary>
    public Vector2 Delta(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return Vector2.Zero;
        if ((long)hwnd < 0) return _itemDelta.TryGetValue(hwnd, out var id) ? id : Vector2.Zero;
        if (_rects.TryGetValue(hwnd, out var r) && _prev.TryGetValue(hwnd, out var p))
            return new(r.Left - p.Left, r.Top - p.Top);
        return Vector2.Zero;
    }

    public bool TryRect(IntPtr hwnd, out RECT r)
    {
        if ((long)hwnd < 0)
        {
            bool ok = _items.TryGetValue(hwnd, out var it);
            r = it.rect;
            return ok;
        }
        return _rects.TryGetValue(hwnd, out r);
    }

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
