namespace Doodlefolk;

/// <summary>A song you wrote for them (see App.Songs).</summary>
sealed class Song
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Lyrics { get; set; } = "";
    /// <summary>Who sings it ("" = whoever's on).</summary>
    public string Singer { get; set; } = "";
    public int Sung { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string[] Lines => Lyrics.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(40).ToArray();
}

/// <summary>Singing: a song line by line, each in a bubble and in their own babble voice, stepping up and down a
/// little pentatonic tune that's the same every time for the same song.</summary>
sealed partial class Brain
{
    Song? _song, _stageSong;
    int _songLine;
    float _songNext;
    static readonly int[] Pentatonic = { 0, 2, 4, 7, 9, 12, 14 };

    public bool Singing => _song != null;

    public void StartSinging(Song s)
    {
        if (s.Lines.Length == 0) return;
        _song = s;
        _songLine = 0;
        _songNext = _t0 + 0.6f;
        s.Sung++;
    }

    /// <summary>Each step while singing (on stage, or wherever they are).</summary>
    void SingStep(World w)
    {
        if (_song is not { } s) return;
        if (f.Mode != Mode.Control || Asleep) { StopSinging(); return; }
        f.DesiredVX = 0;
        f.SetAction((_t0 % 1.6f) < 0.9f ? Act.Talk : Act.Cheer);
        if (_t0 < _songNext) return;
        var lines = s.Lines;
        if (_songLine >= lines.Length)
        {
            f.SingPitch = 1;
            f.Emote(Gestures ? "🎤" : V("thank you!", "THANK YOU, DESKTOP!!!", "…that's the song.", "th-thank you…", "and so the song ends"), 1.8f);
            Write("song:" + s.Id, V($"Sang \"{s.Title}\". People clapped!", $"I SANG \"{s.Title.ToUpperInvariant()}\"!!! STAR!", $"Sang \"{s.Title}\". It went fine.", $"I sang \"{s.Title}\"… out loud!", $"I sang \"{s.Title}\" to the open sky."), "♪", 600);
            Cheered(0.3f);
            StopSinging();
            return;
        }
        string line = lines[_songLine];
        // The tune: a step up or down the scale per line, from the song and the line, so it's always the same tune.
        int h = Math.Abs((s.Id * 7919 + _songLine * 104729 + line.Length * 31) % Pentatonic.Length);
        f.SingPitch = MathF.Pow(2, (Pentatonic[h] - 4) / 12f);
        string shown = line.Length > 48 ? line[..46] + "…" : line;
        float dur = Math.Clamp(1.1f + line.Length * 0.075f, 1.4f, 5f);
        f.Emote($"♪ {shown} ♪", dur);
        _songNext = _t0 + dur + 0.25f;
        _songLine++;
        // Anyone near enough sways along.
        foreach (var o in w.Figures)
            if (o != f && o.Mode == Mode.Control && !o.Brain.Asleep && o.Brain._song == null && System.Numerics.Vector2.Distance(o.Base, f.Base) < 300 * S && rng.NextDouble() < 0.1)
                o.Emote("♪", 1);
    }

    void StopSinging() { _song = null; f.SingPitch = 1; }
}
