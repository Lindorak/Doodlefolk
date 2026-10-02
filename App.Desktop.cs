using System.Numerics;
using System.Runtime.InteropServices;

namespace StickFight;

/// <summary>A reminder you set (or imported from a calendar file): someone brings it to you at the time.</summary>
sealed class Reminder
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public DateTime When { get; set; }
    /// <summary>none, daily, weekdays, weekly.</summary>
    public string Repeat { get; set; } = "none";
    public bool Done { get; set; }
}

/// <summary>More of the desktop they notice: a download finishing (only the kind of file is noticed, from its
/// extension; nothing is opened or read), you getting frustrated (lots of clicks in one spot, or a run of windows
/// slammed shut), and reminders you set, which a figure brings over to your cursor when they're due.</summary>
sealed partial class App
{
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);

    FileSystemWatcher? _downloads;
    readonly System.Collections.Concurrent.ConcurrentQueue<string> _downloaded = new();
    double _downloadCheerCd, _frustrationCd, _desktopTick, _reminderTick;
    readonly List<(double t, Vector2 at)> _clicks = new();
    readonly List<double> _closes = new();
    HashSet<IntPtr> _lastWins = new();
    bool _lmbWas;

    static readonly string[] PartialExts = { ".crdownload", ".part", ".partial", ".tmp", ".download", ".opdownload", ".!ut" };

    static string? DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out var p) == 0)
            {
                string s = Marshal.PtrToStringUni(p) ?? "";
                Marshal.FreeCoTaskMem(p);
                if (Directory.Exists(s)) return s;
            }
        }
        catch { }
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(fallback) ? fallback : null;
    }

    void WatchDownloads(bool on)
    {
        if (!on) { _downloads?.Dispose(); _downloads = null; return; }
        if (_downloads != null || DownloadsFolder() is not { } dir) return;
        try
        {
            _downloads = new FileSystemWatcher(dir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
            void Seen(string path)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext.Length == 0 || PartialExts.Contains(ext) || Path.GetFileName(path).StartsWith("~")) return;
                _downloaded.Enqueue(ext);
            }
            _downloads.Created += (_, e) => Seen(e.FullPath);
            _downloads.Renamed += (_, e) => Seen(e.FullPath);
            _downloads.EnableRaisingEvents = true;
        }
        catch (Exception e) { World.Log("downloads watch: " + e.Message); _downloads = null; }
    }

    static string KindOf(string ext) => ext switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" or ".bmp" or ".svg" => "a picture",
        ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" => "a video",
        ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "some music",
        ".pdf" or ".doc" or ".docx" or ".txt" or ".md" or ".rtf" or ".odt" => "a document",
        ".xls" or ".xlsx" or ".csv" => "a spreadsheet",
        ".ppt" or ".pptx" => "some slides",
        ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => "a zip file",
        ".exe" or ".msi" or ".msix" or ".appx" => "an installer",
        _ => "something",
    };

    void DesktopFrame(double now)
    {
        WatchDownloads(_settings.NoticeDownloads);
        // Downloads: a cheer (at most one every couple of minutes).
        if (_downloaded.TryDequeue(out var ext))
        {
            while (_downloaded.TryDequeue(out _)) { }
            if (now > _downloadCheerCd)
            {
                _downloadCheerCd = now + 120;
                var fan = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep && !f.Brain.InFight).OrderByDescending(f => f.Brain.UserFondness + _w.Rng.NextDouble() * 0.3).FirstOrDefault();
                fan?.Brain.OnDownload(KindOf(ext), _w);
                World.Log("download noticed: " + KindOf(ext));
            }
        }
        // Frustration: rage clicks, or windows slammed shut one after another.
        bool lmb = (GetAsyncKeyState(0x01) & 0x8000) != 0;
        if (lmb && !_lmbWas) _clicks.Add((now, _w.Cursor));
        _lmbWas = lmb;
        _clicks.RemoveAll(c => now - c.t > 2.5);
        if (now > _desktopTick)
        {
            _desktopTick = now + 0.5;
            var wins = _w.Env.Hwnds.ToHashSet();
            foreach (var h in _lastWins) if (!wins.Contains(h) && !IsWindow(h)) _closes.Add(now);
            _lastWins = wins;
            _closes.RemoveAll(t => now - t > 20);
        }
        if (_settings.NoticeFrustration && now > _frustrationCd && _w.Game == null)
        {
            var last = _clicks.Count > 0 ? _clicks[^1].at : default;
            bool rage = _clicks.Count >= 8 && _clicks.All(c => Vector2.Distance(c.at, last) < 70 * _w.Scale);
            bool slams = _closes.Count >= 4;
            if (rage || slams)
            {
                _frustrationCd = now + 600;
                _clicks.Clear(); _closes.Clear();
                var friend = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep && !f.Brain.InFight && f.Brain.UserFondness > 0.1f)
                                       .OrderByDescending(f => f.Brain.UserFondness + f.Traits.Sociability * 0.3f).FirstOrDefault();
                friend?.Brain.ComfortUser(_w, rage ? "clicks" : "windows");
                World.Log($"frustration noticed ({(rage ? "clicks" : "windows")})");
            }
        }
        if (now > _reminderTick) { _reminderTick = now + 5; ReminderTick(); }
    }

    // ---------------- reminders ----------------

    void ReminderTick()
    {
        var due = _settings.Reminders.Where(r => !r.Done && r.When <= DateTime.Now).ToList();
        foreach (var r in due)
        {
            // Long overdue (the PC was off): mention it, but don't make a fuss.
            bool stale = (DateTime.Now - r.When).TotalHours > 2;
            var bringer = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.InFight).OrderByDescending(f => f.Brain.UserFondness + (f.Brain.Asleep ? -1 : 0)).FirstOrDefault();
            if (!stale) bringer?.Brain.BringReminder(r.Text, _w);
            World.Log($"reminder: {r.Text}{(stale ? " (missed)" : "")}");
            if (!stale) PostAll(new { t = "toast", text = $"⏰ {r.Text}" });
            r.When = r.Repeat switch
            {
                "daily" => NextAfter(r.When, d => d.AddDays(1)),
                "weekly" => NextAfter(r.When, d => d.AddDays(7)),
                "weekdays" => NextAfter(r.When, d => { d = d.AddDays(1); while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1); return d; }),
                _ => r.When,
            };
            if (r.Repeat is not ("daily" or "weekly" or "weekdays")) r.Done = true;
        }
        // Tidy finished one-offs after a day.
        if (_settings.Reminders.RemoveAll(r => r.Done && (DateTime.Now - r.When).TotalDays > 1) > 0 || due.Count > 0) _settings.Save();
    }

    static DateTime NextAfter(DateTime from, Func<DateTime, DateTime> step)
    {
        var d = step(from);
        while (d <= DateTime.Now) d = step(d);
        return d;
    }

    string AddReminder(string text, DateTime when, string repeat)
    {
        text = text.Trim();
        if (text.Length is 0 or > 120) return "Write what to remind you about (up to 120 letters).";
        if (when <= DateTime.Now && repeat == "none") return "That time has already passed.";
        int id = _settings.Reminders.Count == 0 ? 1 : _settings.Reminders.Max(r => r.Id) + 1;
        _settings.Reminders.Add(new Reminder { Id = id, Text = text, When = when, Repeat = repeat });
        _settings.Save();
        return $"Reminder set for {when:ddd d MMM, HH:mm}.";
    }
}
