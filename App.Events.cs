using System.Numerics;
using System.Runtime.InteropServices;

namespace StickFight;

/// <summary>Desktop happenings the figures react to: you finishing a stretch of typing (worked out from the time of the
/// last input and whether the mouse moved; keys are never read) and notification pop-ups.</summary>
sealed partial class App
{
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    uint _lastInput;
    double _typingSince = -1, _lastKeyAt;
    Vector2 _inputCursor;

    // ---------------- debug: a plain test window to ride, shake and close ----------------
    Form? _testWin;
    double _shakeUntil;
    int _testX, _testY;

    void TestWindow(string[] p)
    {
        switch (p.Length > 1 ? p[1] : "")
        {
            case "open":
                _testWin?.Close();
                _testX = int.Parse(p[2]); _testY = int.Parse(p[3]);
                _testWin = new Form { Text = "StickFight test window", StartPosition = FormStartPosition.Manual, ShowInTaskbar = false,
                                      Bounds = new Rectangle(_testX, _testY, p.Length > 4 ? int.Parse(p[4]) : 700, p.Length > 5 ? int.Parse(p[5]) : 400) };
                _testWin.Show();
                break;
            case "shake": _shakeUntil = _clock.Elapsed.TotalSeconds + (p.Length > 2 ? double.Parse(p[2]) : 2.5); break;
            case "close": _testWin?.Close(); _testWin = null; break;
        }
    }

    void WeatherFrame(float dt, double now)
    {
        _w.DayNight = _settings.DayNight;
        _w.UpdateClock();
        _w.Weather.Step(_w, dt, now, _settings.WeatherMode);
        // Snowmen melt once the snow's gone.
        if (!_w.Weather.Snowing)
            foreach (var it in _w.Items.Where(i => i.Def.Key == "snowman" && i.Holder == null).ToList())
            {
                var seg = _w.Env.SupportAt(it.Pos.X, it.Pos.Y, it.GroundHwnd);
                if (seg != null && _w.Weather.SnowOn(seg) > 0.5f) continue;
                it.SizeMul -= dt * 0.006f;
                if (it.SizeMul < 0.35f) _w.RemoveItem(it);
            }
    }

    double _awaySince = -1;

    /// <summary>Notice you being away (no input for 5+ minutes) and coming back.</summary>
    void AwayFrame(double now)
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li)) return;
        double idle = unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        if (idle > 300 && _awaySince < 0) _awaySince = now - idle;
        else if (idle < 2 && _awaySince >= 0)
        {
            double away = now - _awaySince;
            _awaySince = -1;
            if (away > 300) _w.OnUserBack(away);
        }
    }

    void EventsFrame(double now)
    {
        AwayFrame(now);
        _w.Celebrations = _settings.Celebrations;
        _w.UpdateHoliday(now);
        if (_testWin != null && !_testWin.IsDisposed && now < _shakeUntil)
        {
            // Swing it side to side, faster and faster, then stop dead.
            float k = (float)(_shakeUntil - now);
            _testWin.Left = _testX + (int)(MathF.Sin((float)now * (8 + 6 / MathF.Max(0.3f, k))) * 450);
        }
        if (_settings.NoticeTyping) TypingFrame(now);
        if (_settings.Notifications) NotificationFrame(now);
    }

    void TypingFrame(double now)
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref li) || li.dwTime == _lastInput) goto check;
        _lastInput = li.dwTime;
        // Input with the mouse standing still and no buttons down: the keyboard.
        bool mouse = Vector2.Distance(_w.Cursor, _inputCursor) > 2 || Control.MouseButtons != MouseButtons.None;
        _inputCursor = _w.Cursor;
        if (mouse) { _typingSince = -1; return; }
        if (_typingSince < 0) _typingSince = now;
        _lastKeyAt = now;
    check:
        if (_typingSince >= 0 && now - _lastKeyAt > 2.5)
        {
            double span = _lastKeyAt - _typingSince;
            _typingSince = -1;
            if (span > 12)
                foreach (var f in _w.Figures) f.Brain.OnUserFinishedTyping(span, _w);
        }
    }

    // ---------------- notifications ----------------

    readonly HashSet<IntPtr> _toasts = new();
    double _nextToastScan;

    /// <summary>Windows shows notifications in small borderless pop-up windows near the tray; spot new ones.</summary>
    void NotificationFrame(double now)
    {
        if (now < _nextToastScan) return;
        _nextToastScan = now + 0.5;
        var seen = new HashSet<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || !IsToast(h, out var r)) return true;
            seen.Add(h);
            if (_toasts.Add(h))
            {
                World.Log($"notification at {r.Left},{r.Top} {r.Width}x{r.Height}");
                var rect = new RectangleF(r.Left, r.Top, r.Width, r.Height);
                foreach (var f in _w.Figures) f.Brain.OnNotification(rect, _w);
            }
            return true;
        }, IntPtr.Zero);
        _toasts.IntersectWith(seen);
    }

    static bool IsToast(IntPtr h, out Native.RECT r)
    {
        r = default;
        var title = new System.Text.StringBuilder(64);
        Native.GetWindowText(h, title, title.Capacity);
        string t = title.ToString();
        if (!t.Contains("notification", StringComparison.OrdinalIgnoreCase)) return false;
        var cls = new System.Text.StringBuilder(64);
        Native.GetClassName(h, cls, cls.Capacity);
        if (cls.ToString() != "Windows.UI.Core.CoreWindow") return false;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, 16) != 0 && !Native.GetWindowRect(h, out r)) return false;
        return r.Width > 100 && r.Height > 40 && r.Width < 900 && r.Height < 700;
    }
}
