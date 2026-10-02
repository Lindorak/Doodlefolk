using System.Globalization;

namespace StickFight;

/// <summary>Quiet hours: on the chosen days, between the chosen times, everyone is hidden and the world is paused
/// (as with "Hide the figures"), and comes back afterwards.</summary>
sealed partial class App
{
    /// <summary>Is <paramref name="n"/> inside the quiet hours? A window that runs past midnight belongs to the
    /// day it started on.</summary>
    internal static bool InQuietHours(DateTime n, string fromText, string toText, IReadOnlyCollection<int> days)
    {
        if (!TimeSpan.TryParseExact(fromText, @"hh\:mm", CultureInfo.InvariantCulture, out var from) ||
            !TimeSpan.TryParseExact(toText, @"hh\:mm", CultureInfo.InvariantCulture, out var to)) return false;
        var t = n.TimeOfDay;
        bool overnight = to <= from;
        bool inside = overnight ? t >= from || t < to : t >= from && t < to;
        var day = (int)(overnight && t < to ? n.AddDays(-1) : n).DayOfWeek;
        return inside && days.Contains(day);
    }

    double _quietCheckAt;
    bool _quiet;

    bool QuietHours()
    {
        if (!_settings.PauseSchedule) return false;
        double now = _clock.Elapsed.TotalSeconds;
        if (now < _quietCheckAt) return _quiet;
        _quietCheckAt = now + 5;
        bool q = InQuietHours(DateTime.Now, _settings.PauseFrom, _settings.PauseTo, _settings.PauseDays);
        if (q != _quiet) World.Log(q ? "quiet hours: hiding" : "quiet hours: over");
        return _quiet = q;
    }
}
