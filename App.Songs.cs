namespace Doodlefolk;

/// <summary>Songs (an idea from Tomodachi Life's concert hall): write lyrics in the Studio and give them to a figure;
/// they sing them at the talent show (as their act), or right now if you ask.</summary>
sealed partial class App
{
    /// <summary>The song this figure would sing on stage: one written for them, else (sometimes) one for anyone.</summary>
    Song? SongFor(Figure f)
    {
        var mine = _settings.Songs.Where(s => s.Singer == f.Name && s.Lines.Length > 0).ToList();
        if (mine.Count > 0) return mine[_w.Rng.Next(mine.Count)];
        var anyone = _settings.Songs.Where(s => s.Singer.Length == 0 && s.Lines.Length > 0).ToList();
        return anyone.Count > 0 && _w.Rng.NextDouble() < 0.5 ? anyone[_w.Rng.Next(anyone.Count)] : null;
    }

    string SingNow(int id)
    {
        var s = _settings.Songs.FirstOrDefault(x => x.Id == id);
        if (s == null || s.Lines.Length == 0) return "That song has no words yet.";
        var who = _w.Figures.FirstOrDefault(f => f.Name == s.Singer && f.Mode == Mode.Control && !f.Brain.Asleep)
                  ?? _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep && f.Visitor == VisitorKind.None).OrderByDescending(f => f.Traits.Sociability).FirstOrDefault();
        if (who == null) return "Nobody's awake to sing it.";
        who.Brain.StartSinging(s);
        _w.Sticker("song");
        _settings.Save();
        return $"{who.Name} is singing \"{s.Title}\".";
    }

    object SongState() => _settings.Songs.Select(s => new { id = s.Id, title = s.Title, lyrics = s.Lyrics, singer = s.Singer, sung = s.Sung });
}
