namespace Doodlefolk;

/// <summary>The weekly paper ("The Stick Times") and the sticker book, for the Studio.</summary>
sealed partial class App
{
    object? _paper;
    double _paperAt = -100;

    static readonly Dictionary<string, string> Desk = new()
    {
        ["romance"] = "Society", ["baby"] = "Society", ["home"] = "Society", ["club"] = "Society", ["party"] = "Society", ["sleepover"] = "Society",
        ["tournament"] = "Sport", ["rival"] = "Sport",
        ["pets"] = "Pets",
    };

    object Paper()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_paper != null && now - _paperAt < 10) return _paper;
        _paperAt = now;
        var week = _w.NewsLog.Where(n => (DateTime.Now - n.When).TotalDays <= 7).OrderByDescending(n => n.Weight).ThenByDescending(n => n.When).ToList();
        var lead = week.FirstOrDefault();
        var sections = week.Skip(1).GroupBy(n => Desk.GetValueOrDefault(n.Kind, "Around the desktop"))
            .Select(g => new { name = g.Key, items = g.Take(6).Select(n => new { text = n.Text, when = n.When.ToString("ddd HH:mm") }) })
            .OrderBy(s => s.name switch { "Society" => 0, "Sport" => 1, "Pets" => 2, _ => 3 }).ToList();
        var figs = _w.Figures.Concat(_stash).Where(f => !f.Dead).ToList();
        Figure? popular = figs.Count > 1 ? figs.OrderByDescending(f => figs.Where(o => o != f).Average(o => o.Brain.AffinityWith(f))).First() : null;
        Figure? fan = figs.OrderByDescending(f => f.Brain.UserFondness).FirstOrDefault();
        Figure? grump = figs.Where(f => f.Brain.UserFondness < -0.3f).OrderBy(f => f.Brain.UserFondness).FirstOrDefault();
        var couples = figs.Where(f => f.Brain.Sweetheart(_w) is { } s && f.Id < s.Id).Select(f => $"{f.Name} & {f.Brain.Sweetheart(_w)!.Name}").ToList();
        var crush = figs.Select(f => (f, c: f.Brain.Crush(_w))).FirstOrDefault(x => x.c != null && f_notDating(x.f, x.c));
        bool f_notDating(Figure a, Figure? b) => b != null && !a.Brain.Dating(b);
        var happiest = _w.Pets.OrderByDescending(p => p.Happiness).FirstOrDefault();
        var needy = _w.Pets.Where(p => p.Happiness < 0.45f).OrderBy(p => p.Happiness).FirstOrDefault();
        var features = new List<object>();
        if (popular != null) features.Add(new { title = "Most popular", text = $"{popular.Name}. Everyone seems to like them." });
        if (fan != null && fan.Brain.UserFondness > 0.3f) features.Add(new { title = "Your biggest fan", text = $"{fan.Name} thinks the world of you." });
        if (grump != null) features.Add(new { title = "Not your fan", text = $"{grump.Name} would rather you kept your cursor to yourself." });
        if (couples.Count > 0) features.Add(new { title = "Couples", text = string.Join(", ", couples) });
        if (crush.c != null) features.Add(new { title = "Gossip", text = $"Rumour has it {crush.f.Name} has a soft spot for someone… (their initials are {crush.c.Name[0]}.)" });
        if (happiest != null) features.Add(new { title = "Pet of the week", text = $"{happiest.Name} the {happiest.Species()}: {happiest.Mood.ToLowerInvariant()}." });
        if (needy != null) features.Add(new { title = "Pet care notice", text = $"{needy.Name} could use some attention ({needy.Mood.ToLowerInvariant()})." });
        int weekStories = week.Count;
        _paper = new
        {
            date = DateTime.Now.ToString("dddd d MMMM yyyy"),
            edition = 1 + (int)((DateTime.Now - (_w.NewsLog.Count > 0 ? _w.NewsLog[0].When : DateTime.Now)).TotalDays / 7),
            lead = lead == null ? null : new { text = lead.Text, when = lead.When.ToString("dddd") },
            sections,
            features,
            weather = $"{_w.Weather.Kind switch { WeatherKind.Rain => "Rain", WeatherKind.Storm => "Thunderstorms", WeatherKind.Snow => "Snow", _ => "Fair" }}, {_w.Seasons.Now.ToString().ToLowerInvariant()}{(_w.Night > 0.5f ? ", night" : "")}",
            numbers = new[]
            {
                $"{figs.Count} figures", $"{_w.Pets.Count} pets", $"{_w.Clubs.Count} clubs", $"{figs.Sum(f => f.Brain.Trophies)} trophies",
                $"{_settings.WishesGranted} wishes granted", $"{weekStories} stories this week", $"{_settings.Stickers.Count}/{Stickers.All.Length} stickers",
            },
        };
        return _paper;
    }

    object StickerBook() => Stickers.All.Select(d => new
    {
        key = d.Key, art = d.Art, title = d.Title, hint = d.Hint,
        got = _settings.Stickers.TryGetValue(d.Key, out var when) ? when.ToString("d MMM") : null,
    });
}
