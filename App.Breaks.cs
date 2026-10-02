using System.Numerics;

namespace Doodlefolk;

/// <summary>Gentle break nudges (an idea from StickBuddies, MIT; off by default): after a long unbroken stretch of
/// videos, music or a full-screen game, a figure walks down by the clock and points at it. Five quiet minutes resets
/// the count. It never interrupts anything: it waits until the figures are on screen.</summary>
sealed partial class App
{
    double _screenTimeStart = -1, _lastBusyAt, _nudgedAt = -1e9, _breakTick;

    void BreakFrame(double now)
    {
        if (!_settings.BreakNudges || now < _breakTick) return;
        _breakTick = now + 5;
        var m = _w.Screen?.Media ?? MediaNow.Quiet;
        bool busy = m.Music || m.Video || _w.Env.FullscreenActive;
        if (busy) { _lastBusyAt = now; if (_screenTimeStart < 0) _screenTimeStart = now; }
        else if (now - _lastBusyAt > 300) _screenTimeStart = -1;
        if (_screenTimeStart < 0 || _w.Env.FullscreenActive) return;
        double minutes = (now - _screenTimeStart) / 60;
        if (minutes < _settings.BreakMinutes || now - _nudgedAt < _settings.BreakMinutes * 60) return;
        var f = _w.Figures.Where(x => x.Mode == Mode.Control && !x.Brain.Asleep && !x.Brain.InFight && x.Brain.HapRole.Length == 0)
                          .OrderByDescending(x => x.Brain.UserFondness + x.Traits.Sociability * 0.3f).FirstOrDefault();
        if (f == null) return;
        _nudgedAt = now;
        // The clock lives at the right end of the taskbar on the main screen.
        var work = Screen.PrimaryScreen?.WorkingArea ?? _w.Env.Virtual;
        f.Brain.NudgeBreak(_w, new Vector2(work.Right - 60 * _w.Scale, work.Bottom), (int)minutes);
        World.Log($"break nudge after {minutes:0} minutes");
    }
}
