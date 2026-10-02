namespace Doodlefolk;

/// <summary>The very first run: the first figure says hello and points you at the tray icon, then the Studio opens
/// with its tour.</summary>
sealed partial class App
{
    double _welcomeStep = -1;
    int _welcomeIndex;

    /// <summary>With ageing on, time passes while Doodlefolk is closed too: everyone grown up is older by the time
    /// away (up to a month's worth, so a long break doesn't turn the whole town grey at once).</summary>
    void CatchUpAgeing()
    {
        float pace = _settings.LifePace switch { "slow" => 1, "fast" => 24, _ => 0 };
        if (pace <= 0 || _settings.LastSeen is not DateTime last) return;
        float days = (float)Math.Clamp((DateTime.Now - last).TotalDays, 0, 30);
        foreach (var f in _w.Figures) if (!f.Brain.Baby) f.Brain.AgeBank += days * pace;
        if (days > 0.01f) World.Log($"ageing: {days:0.0} days away, +{days * pace:0.0} years");
    }

    void WelcomeFrame(double now)
    {
        if (!_settings.FirstRun || _selfTest || _trailer || _welcomeIndex >= 3) return;
        if (_welcomeStep < 0) _welcomeStep = now + 3.5;
        if (now < _welcomeStep) return;
        var f = _w.Figures.FirstOrDefault(x => x.Mode == Mode.Control);
        if (f == null) { _welcomeStep = now + 1; return; }
        switch (_welcomeIndex++)
        {
            case 0: f.Emote("hi! I live here now 👋", 3.5f); _welcomeStep = now + 4.5; break;
            case 1: f.Emote("the tray icon by the clock opens my Studio ↘", 4); _welcomeStep = now + 4.5; break;
            default: OpenStudio(); break;
        }
    }
}
