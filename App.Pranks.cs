using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>Prank mode (an idea from Desktop Goose, kept friendly; off by default): the playful ones leave sticky
/// notes on your windows, hide a whoopee cushion on the couch, track muddy footprints after rain, and now and then
/// carry in a framed meme that fits the moment (see Memes): from your own memes folder, or, if you allow it, made from
/// classic templates or found on r/wholesomememes. Everything it leaves tidies itself away after a while.</summary>
sealed partial class App
{
    double _memeAt = 300, _printAt, _rainEndedAt = -1e9;
    bool _wasRaining, _memeBusy;
    MemeEntry? _memeReady;
    readonly Dictionary<Item, double> _prankExpiry = new();
    string _lastBuild = "";
    double _lastBuildAt = -1e9, _backAt = -1e9;

    void PrankFrame(double now)
    {
        World.Pranks = _settings.Pranks && !Focusing && !_settings.Calm;
        // Things pranksters leave go away by themselves.
        foreach (var (it, until) in _prankExpiry.ToList())
            if (!_w.Items.Contains(it)) _prankExpiry.Remove(it);
            else if (now > until) { _w.Fx.Dust(it.Pos, _w.Scale, 5, 0.6f, _w.Rng); _w.RemoveItem(it); _prankExpiry.Remove(it); }
        if (!_settings.Pranks) return;
        // Muddy footprints for a few minutes after the rain stops.
        bool raining = _w.Weather.Raining;
        if (_wasRaining && !raining) _rainEndedAt = now;
        _wasRaining = raining;
        if (now - _rainEndedAt < 300 && now > _printAt)
        {
            _printAt = now + 0.45;
            foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && f.Grounded && MathF.Abs(f.Vel.X) > 40 * f.S && f.Visitor == VisitorKind.None).Take(3))
                if (_prankExpiry.Keys.Count(i => i.Def.Key == "mudprint") < 16 && ItemCatalog.Find("mudprint") is { } md && SpawnItem(md) is { } print)
                {
                    print.Pos = f.Base + new Vector2(0, -0.5f); print.Vel = Vector2.Zero; print.OnGround = false; print.Flip = f.Facing < 0;
                    PrankLeft(print, 10);
                }
        }
        // Whoopee cushions go off when someone sits on them.
        foreach (var cushion in _w.Items.Where(i => i.Def.Key == "whoopee").ToList())
        {
            var seat = _w.Items.FirstOrDefault(s => s != cushion && s.Seated.Any(x => x != null) && MathF.Abs(s.Pos.X - cushion.Pos.X) < s.Def.W * s.Sc * 0.5f && MathF.Abs(s.Pos.Y - cushion.Pos.Y) < s.Def.H * s.Sc + 10 * _w.Scale);
            if (seat == null) continue;
            var sitter = seat.Seated.First(x => x != null)!;
            World.Play(Sfx.Squeak, cushion.Pos, 0.9f, 0.35f);
            World.Play(Sfx.Squeak, cushion.Pos, 0.6f, 0.28f, 0);
            sitter.Emote(World.Gestures ? "😳" : new[] { "THAT WASN'T ME!", "who put that there?!", "…oh no.", "excuse ME?!" }[_w.Rng.Next(4)], 2.2f);
            foreach (var o in _w.Figures.Where(o => o != sitter && o.Mode == Mode.Control && Vector2.Distance(o.Base, sitter.Base) < 600 * _w.Scale))
            {
                o.Emote(World.Gestures ? "🤣" : new[] { "HAHAHA", "pfft!", "hehehe", "classic!" }[_w.Rng.Next(4)], 1.8f);
                o.Brain.Cheered(0.15f);
            }
            _w.RemoveItem(cushion);
            _w.Sticker("prank");
        }
        // Memes: one ready at a time, made in the background when it fits.
        if (now > _memeAt && _memeReady == null && !_memeBusy)
        {
            _memeAt = now + _w.Rng.Range(1500, 3000);
            MakeMemeSoon();
        }
    }

    /// <summary>What's going on, for picking a meme (and a sticky note) that fits.</summary>
    MemeContext MemeNow()
    {
        var tags = new HashSet<string>();
        var d = DateTime.Now;
        if (d.DayOfWeek == DayOfWeek.Monday && d.Hour < 14) tags.Add("monday");
        if (d.DayOfWeek == DayOfWeek.Friday && d.Hour >= 14) tags.Add("friday");
        if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) tags.Add("weekend");
        if (d.Hour is >= 0 and < 4) tags.Add("latenight");
        if (d.Hour is >= 6 and < 10) tags.Add("morning");
        var wx = _w.Weather;
        if (wx.Kind == WeatherKind.Storm && wx.Intensity > 0.15f) tags.Add("storm"); else if (wx.Raining) tags.Add("rain");
        if (wx.Snowing) tags.Add("snow");
        if (_w.TempC is float temp && temp >= 28) tags.Add("hot");
        double now = _clock.Elapsed.TotalSeconds;
        if (_typingSince >= 0 && now - _typingSince > 1800) tags.Add("typing");
        if (now - _backAt < 300) tags.Add("back");
        if (_w.Media.Music) tags.Add("music");
        if (now - _lastBuildAt < 600) tags.Add(_lastBuild == "fail" ? "buildfail" : "buildpass");
        if (_settings.FocusSessions > 0 && Focusing) tags.Add("focus");
        // The town's recent news.
        Figure? who = null, other = null;
        foreach (var n in _w.NewsLog.Where(n => (DateTime.Now - n.When).TotalMinutes < 20))
        {
            string? tag = n.Kind switch { "romance" => "love", "baby" => "baby", "rival" => "fight", "visitors" => "visitor", "fish" => "fish", _ => null };
            if (n.Kind == "town" && n.Text.Contains("race", StringComparison.OrdinalIgnoreCase)) tag = "race";
            if (n.Kind == "town" && n.Text.Contains("festival", StringComparison.OrdinalIgnoreCase)) tag = "festival";
            if (tag == null) continue;
            tags.Add(tag);
            who ??= _w.Figures.FirstOrDefault(f => n.Who.Contains(f.Name));
            other ??= _w.Figures.FirstOrDefault(f => n.Who.Contains(f.Name) && f != who);
        }
        if (_w.Happening?.Kind is "festival" or "race") tags.Add(_w.Happening.Kind);
        var hungry = _w.Pets.Where(p => p.Hunger > 0.6f).OrderByDescending(p => p.Hunger).FirstOrDefault();
        if (hungry != null) tags.Add("pet");
        who ??= _w.Figures.Where(f => f.Visitor == VisitorKind.None).OrderByDescending(f => f.Traits.Playfulness).FirstOrDefault();
        other ??= who == null ? null : _w.Figures.Where(f => f != who && f.Visitor == VisitorKind.None).OrderByDescending(f => who.Brain.AffinityWith(f)).FirstOrDefault();
        string weather = _w.TempC is float t2 ? $"{t2:0}°C" : wx.Raining ? "RAINING" : "SUNNY";
        return new MemeContext(tags, who?.Name ?? "us", other?.Name ?? "", (hungry ?? _w.Pets.FirstOrDefault())?.Name ?? "", weather, "");
    }

    void MakeMemeSoon()
    {
        var c = MemeNow();
        bool fromFolder = _settings.MemeFolder.Length > 0 && Directory.Exists(_settings.MemeFolder) && (!_settings.InternetMemes || _w.Rng.NextDouble() < 0.4);
        if (!fromFolder && !_settings.InternetMemes) return;
        _memeBusy = true;
        var rng = new Random(_w.Rng.Next());
        bool ai = AiReady;
        string? key = ai ? AiKey() : null;
        string model = _settings.AiModel.Length > 0 ? _settings.AiModel : "gpt-5-mini";
        bool real = _settings.RealMemes;
        string folder = _settings.MemeFolder;
        _ = Task.Run(async () =>
        {
            MemeEntry? made = null;
            try
            {
                if (fromFolder)
                {
                    var files = Directory.EnumerateFiles(folder).Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)).ToList();
                    // Prefer files whose names fit the moment (monday.png, cat-dinner.jpg…).
                    var words = c.Tags.ToList();
                    var fit = files.Where(p => words.Any(w => Path.GetFileNameWithoutExtension(p).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
                    var pick = (fit.Count > 0 ? fit : files).OrderBy(_ => rng.Next()).FirstOrDefault();
                    if (pick != null) made = new MemeEntry { File = pick, When = DateTime.Now, Template = "your folder", Text = Path.GetFileNameWithoutExtension(pick), Why = fit.Count > 0 ? "fits" : "folder", Source = "folder" };
                }
                else
                {
                    if (real && rng.NextDouble() < 0.5) made = await Memes.FindReal(c);
                    if (made == null && Memes.Choose(c, rng) is { } choice)
                    {
                        var lines = choice.lines;
                        if (key != null) lines = await AiMemeLines(key, model, choice.t, c, choice.why) ?? lines;
                        made = await Memes.Make(choice.t, lines, choice.why, key != null ? "ai" : "town");
                    }
                }
            }
            catch (Exception e) { World.Log($"meme: {e.Message}"); }
            _overlay.BeginInvoke(() => { _memeBusy = false; _memeReady = made; if (made != null) World.Log($"meme ready: {made.Text} ({made.Why})"); });
        });
    }

    /// <summary>The AI writes the lines (with the user's key, to api.openai.com only): what's going on, the template,
    /// how many lines. Kind and silly; no real people.</summary>
    static async Task<string[]?> AiMemeLines(string key, string model, MemeTemplate t, MemeContext c, string why)
    {
        try
        {
            string situation = $"Situation tags: {string.Join(", ", c.Tags)}. Main character: {c.Figure} (a little stick figure living on the user's desktop). " +
                               $"{(c.Other.Length > 0 ? $"Their friend: {c.Other}. " : "")}{(c.Pet.Length > 0 ? $"A pet called {c.Pet}. " : "")}Weather: {c.Weather}. It's {DateTime.Now:dddd h tt}.";
            var body = new
            {
                model,
                instructions = $"You write captions for the meme template \"{t.Name}\". Reply with exactly {t.Boxes.Length} short lines (one per text box, in the template's usual order), nothing else. Gentle, funny, wholesome; about the situation; no real people, no insults.",
                input = situation,
                max_output_tokens = 200,
                store = false,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var sb = new StringBuilder();
            foreach (var item in doc.RootElement.GetProperty("output").EnumerateArray())
                if (item.TryGetProperty("content", out var content))
                    foreach (var cc in content.EnumerateArray())
                        if (cc.TryGetProperty("type", out var ty) && ty.GetString() == "output_text") sb.Append(cc.GetProperty("text").GetString());
            return Memes.CleanAiLines(sb.ToString(), t.Boxes.Length);
        }
        catch { return null; }
    }

    /// <summary>A prankster takes the ready meme (if there is one) to hang up.</summary>
    string? TakeMeme()
    {
        if (_memeReady is not { } m) return null;
        _memeReady = null;
        m.Shown = true;
        return Path.IsPathRooted(m.File) ? m.File : Path.Combine(Memes.Dir, m.File);
    }

    /// <summary>A sticky note that fits the moment.</summary>
    string NoteText(Figure by)
    {
        var c = MemeNow();
        var notes = new List<string> { $"{by.Name} was here", "hi :)", "you're doing great", "don't forget to smile", "look behind you", "nice cursor", "boo!" };
        if (c.Tags.Contains("monday")) notes.AddRange(new[] { "only 4 more days!", "coffee first" });
        if (c.Tags.Contains("friday")) notes.AddRange(new[] { "IT'S FRIDAY!!", "weekend soon :)" });
        if (c.Tags.Contains("latenight")) notes.AddRange(new[] { "go to bed!", "it's SO late" });
        if (c.Tags.Contains("typing")) notes.AddRange(new[] { "stretch your hands", "drink some water", "blink!" });
        if (c.Tags.Contains("rain")) notes.AddRange(new[] { "it's raining!", "puddle season" });
        if (c.Tags.Contains("pet") && c.Pet.Length > 0) notes.Add($"{c.Pet} is hungry");
        if (c.Tags.Contains("buildfail")) notes.Add("it'll work next time");
        if (c.Tags.Contains("buildpass")) notes.Add("it builds! :D");
        return notes[_w.Rng.Next(notes.Count)];
    }

    /// <summary>Something a prankster left: it goes away after a while.</summary>
    void PrankLeft(Item it, double minutes) { it.Temporary = true; _prankExpiry[it] = _clock.Elapsed.TotalSeconds + minutes * 60; }

    /// <summary>The Studio's list of memes.</summary>
    object MemeState() => Memes.List.AsEnumerable().Reverse().Take(30).Select(m => new { file = m.File, text = m.Text, template = m.Template, why = m.Why, source = m.Source, when = m.When.ToString("d MMM, HH:mm"), shown = m.Shown });
}
