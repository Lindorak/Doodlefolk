using System.Drawing;
using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>A to-do you're working through (Studio → Focus).</summary>
sealed class FocusTask
{
    public int Id { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;
}

/// <summary>Focus sessions (as in Spirit City and Rusty's Retirement): start a 15–60 minute focus block and the town
/// settles into quiet work alongside you: no fights, no coming to see you, no chatter, just reading, drawing, tending
/// and their jobs, with a small timer by the clock. When it's done they cheer, everyone gets a few coins, and there's a
/// short break (when they play) before the next one if you want it. Ticking off a to-do gets a cheer too.</summary>
sealed partial class App
{
    double _focusEnd = -1, _breakEnd = -1;
    int _focusMinutes = 25;

    public bool Focusing => _focusEnd > 0;

    string StartFocus(int minutes)
    {
        _focusMinutes = Math.Clamp(minutes, 1, 120);
        _focusEnd = _clock.Elapsed.TotalSeconds + _focusMinutes * 60.0;
        _breakEnd = -1;
        World.Focus = true;
        foreach (var f in _w.Figures) if (f.Brain.InFight) f.Brain.CalmDown();
        var lead = _w.Figures.FirstOrDefault(f => f.Mode == Mode.Control && !f.Brain.Asleep);
        lead?.Emote("🎯", 2);
        World.Log($"focus: {_focusMinutes} minutes");
        return $"Focus for {_focusMinutes} minutes. They'll keep quiet.";
    }

    string StopFocus()
    {
        if (!Focusing && _breakEnd < 0) return "Not focusing.";
        _focusEnd = _breakEnd = -1;
        World.Focus = false;
        return "Focus stopped.";
    }

    void FocusFrame(double now)
    {
        World.FocusLeft = _focusEnd > 0 ? (float)Math.Max(0.5, _focusEnd - now) : _breakEnd > 0 ? -(float)Math.Max(0.5, _breakEnd - now) : 0;
        if (_focusEnd > 0 && now >= _focusEnd) FocusDone();
        if (_breakEnd > 0 && now >= _breakEnd)
        {
            _breakEnd = -1;
            if (_settings.FocusAutoNext) StartFocus(_focusMinutes);
            else _w.Figures.FirstOrDefault(f => f.Mode == Mode.Control)?.Emote("⏰ break's over!", 2.5f);
        }
    }

    void FocusDone()
    {
        _focusEnd = -1;
        World.Focus = false;
        _settings.FocusSessions++;
        _settings.FocusMinutes += _focusMinutes;
        int fw = System.Globalization.ISOWeek.GetYear(DateTime.Now) * 100 + System.Globalization.ISOWeek.GetWeekOfYear(DateTime.Now);
        _settings.FocusByWeek[fw] = _settings.FocusByWeek.GetValueOrDefault(fw) + _focusMinutes;
        foreach (var old in _settings.FocusByWeek.Keys.Where(k => k < fw - 10).ToList()) _settings.FocusByWeek.Remove(old);
        Contribute("FOCUS_MINUTES", _focusMinutes);
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control))
        {
            f.Brain.Coins += 3;
            f.Brain.FocusCheer(_w, _focusMinutes);
        }
        World.Play(Sfx.TaDa, _w.Cursor, 0.4f, 1);
        _w.Sticker("focus");
        if (_settings.FocusSessions % 10 == 0) _w.News("town", $"{_settings.FocusSessions} focus sessions done together!", 2);
        _breakEnd = _clock.Elapsed.TotalSeconds + 5 * 60.0;
        PostAll(new { t = "toast", text = $"Focus done: {_focusMinutes} minutes! Take five." });
        _settings.Save();
    }

    string TickTask(int id, bool done)
    {
        if (_settings.FocusTasks.FirstOrDefault(t => t.Id == id) is not { } task) return "";
        task.Done = done;
        if (done)
        {
            var f = _w.Figures.Where(x => x.Mode == Mode.Control && !x.Brain.Asleep).OrderByDescending(x => x.Brain.UserFondness).FirstOrDefault();
            f?.Emote(World.Gestures ? "✓🙌" : $"✓ {(task.Text.Length > 22 ? task.Text[..20] + "…" : task.Text)}!", 2.5f);
            if (f != null && f.Grounded) f.Brain.Cheered(0.1f);
            World.Play(Sfx.Chime, f?.Base ?? _w.Cursor, 0.35f, 1.3f);
        }
        _settings.Save();
        return done ? "Done! ✓" : "";
    }

    RectangleF? FocusRect()
    {
        if (World.FocusLeft == 0) return null;
        var work = Screen.PrimaryScreen?.WorkingArea ?? _w.Env.Virtual;
        float s = _w.Scale;
        return new RectangleF(work.Right - 104 * s, work.Bottom - 30 * s, 70 * s, 24 * s);
    }

    /// <summary>The little timer card by the clock while focusing (or on a break).</summary>
    void DrawFocusCard()
    {
        if (World.FocusLeft == 0) return;
        var work = Screen.PrimaryScreen?.WorkingArea ?? _w.Env.Virtual;
        float s = _w.Scale;
        var c = new Vector2(work.Right - 70 * s, work.Bottom - 18 * s);
        bool onBreak = World.FocusLeft < 0;
        int secs = (int)MathF.Abs(World.FocusLeft);
        string text = $"{(onBreak ? "☕" : "🎯")} {secs / 60}:{secs % 60:00}";
        Ui.Card(_r, c, 64 * s, 18 * s, 6 * s, s, 0.92f);
        _r.Text(text, c, 11 * s, Ui.Ink.A(0.9f), true);
    }
}
