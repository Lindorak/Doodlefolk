using System.Globalization;

namespace StickFight;

/// <summary>Quiet hours: on the chosen days, between the chosen times, everyone is hidden and the world is paused
/// (as with "Hide the figures"), and comes back afterwards.</summary>
sealed partial class App
{
    double _quietCheckAt;
    bool _quiet;

    bool QuietHours()
    {
        if (!_settings.PauseSchedule) return false;
        double now = _clock.Elapsed.TotalSeconds;
        if (now < _quietCheckAt) return _quiet;
        _quietCheckAt = now + 5;
        var n = DateTime.Now;
        if (!TimeSpan.TryParseExact(_settings.PauseFrom, @"hh\:mm", CultureInfo.InvariantCulture, out var from) ||
            !TimeSpan.TryParseExact(_settings.PauseTo, @"hh\:mm", CultureInfo.InvariantCulture, out var to)) return _quiet = false;
        var t = n.TimeOfDay;
        // A window that runs past midnight belongs to the day it started on.
        bool overnight = to <= from;
        bool inside = overnight ? t >= from || t < to : t >= from && t < to;
        var day = (int)(overnight && t < to ? n.AddDays(-1) : n).DayOfWeek;
        bool q = inside && _settings.PauseDays.Contains(day);
        if (q != _quiet) World.Log(q ? "quiet hours: hiding" : "quiet hours: over");
        return _quiet = q;
    }
}
