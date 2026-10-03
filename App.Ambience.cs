namespace Doodlefolk;

/// <summary>Sets the background-sound channels (see Ambience) from what's going on, a few times a second.</summary>
sealed partial class App
{
    double _ambAt;

    void AmbienceFrame(double now, bool hidden)
    {
        if (now < _ambAt) return;
        _ambAt = now + 0.25;
        var snd = _w.Sound;
        if (snd == null) return;
        bool on = _settings.AmbienceOn && _settings.SoundOn && !hidden;
        if (on) snd.EnsureAmbience();
        if (snd.Ambience is not { } amb) return;
        foreach (var c in Ambience.Channels)
            amb.Target[(int)c] = on ? AmbienceLevel(c) * Level(c) * _settings.AmbienceVolume : 0;
    }

    public float Level(AmbienceChannel c) => _settings.AmbienceLevels.TryGetValue(c.ToString(), out var v) ? Math.Clamp(v, 0, 1) : 1;

    /// <summary>How much the town calls for each sound right now (0..1).</summary>
    float AmbienceLevel(AmbienceChannel c)
    {
        if (c == AmbienceChannel.LoFi) return _settings.LoFiOn || (Focusing && _settings.FocusLoFi) ? 1 : 0;
        if (!_settings.AmbienceFollow) return 1;
        var wx = _w.Weather;
        float night = _w.Night;
        int hour = DateTime.Now.Hour;
        var season = _w.Seasons.Now;
        return c switch
        {
            AmbienceChannel.Rain => wx.Raining ? Math.Clamp(wx.Intensity * 1.2f, 0.3f, 1) : 0,
            AmbienceChannel.Wind => wx.Kind == WeatherKind.Storm ? 0.9f : wx.Snowing ? 0.55f : Math.Clamp(MathF.Abs(wx.Wind) / (320 * _w.Scale), 0.08f, 0.6f),
            AmbienceChannel.Pond => _w.Items.Any(i => i.Def.Key == "pond") ? 0.85f : 0,
            AmbienceChannel.Birds => night < 0.35f && !wx.Raining && season != Season.Winter ? (hour is >= 5 and < 10 ? 1 : 0.55f) : 0,
            AmbienceChannel.Crickets => night > 0.5f && !wx.Raining && season is Season.Summer or Season.Autumn ? 1 : 0,
            AmbienceChannel.Chatter => _w.Figures.Count < 2 ? 0 : Math.Min(1, _w.Figures.Count(f => f.CurrentEmote != null) / 3f) * 0.8f + (_w.Figures.Count >= 4 ? 0.15f : 0),
            AmbienceChannel.Fire => _w.Items.Any(i => i.Def.Key == "campfire" && i.Holder == null && i.Burning) ? 1 : 0,
            _ => 0,
        };
    }

    object AmbienceState() => new
    {
        on = _settings.AmbienceOn, volume = _settings.AmbienceVolume, follow = _settings.AmbienceFollow, lofi = _settings.LoFiOn, focusLofi = _settings.FocusLoFi,
        ready = _w.Sound?.Ambience != null,
        channels = Ambience.Channels.Select(c => new { key = c.ToString(), level = Level(c), now = _settings.AmbienceOn && _settings.SoundOn ? AmbienceLevel(c) : 0 }),
    };
}
