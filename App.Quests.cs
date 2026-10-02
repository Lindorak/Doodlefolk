namespace Doodlefolk;

enum QuestKind { Thing, Pet, Event, Hat, Fish, Game }

/// <summary>Something a figure asked you for.</summary>
sealed class Quest
{
    public int Id { get; set; }
    public QuestKind Kind { get; set; }
    public string By { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>What would do it: an item key, a pet kind, a happening, a fish key, a game.</summary>
    public string Target { get; set; } = "";
    /// <summary>How things stood when they asked (a count, or the hat they had on), to tell when it's done.</summary>
    public string Baseline { get; set; } = "";
    public DateTime Made { get; set; }
    public DateTime Until { get; set; }
    public bool Done { get; set; }
}

/// <summary>Requests (an idea from Tomodachi Life): now and then a figure asks for something that fits who they are:
/// a swing, a race, a new hat, a dog, a perch from the pond, a game of tag. They're listed in the Studio; do it within
/// a day and they're thrilled (fonder of you, a few coins from the town, a diary entry); let it lapse and they shrug.
/// At most three at once, and none while you're focusing.</summary>
sealed partial class App
{
    double _questAt = 600, _questCheckAt;

    static readonly (string key, string ask)[] WishList =
    {
        ("hammock", "a hammock"), ("trampoline", "a trampoline"), ("pool", "a pool"), ("pond", "a pond"), ("campfire", "a campfire"),
        ("radio", "a radio"), ("gokart", "a go-kart"), ("bike", "a bike"), ("skateboard", "a skateboard"), ("stage", "a stage"),
        ("fairylights", "fairy lights"), ("treehouse", "a treehouse"), ("fishtank", "a fish tank"), ("beanbag", "a beanbag"),
        ("hoop", "a basketball hoop"), ("goal", "a football goal"), ("tent", "a tent"), ("throne", "a throne"), ("book", "a book"),
    };

    void QuestFrame(double now)
    {
        if (now > _questCheckAt) { _questCheckAt = now + 2; CheckQuests(); }
        if (now < _questAt || !_settings.Requests || Focusing || _settings.PetMode || _w.Figures.Count == 0) return;
        _questAt = now + _w.Rng.Range(1200, 3600);
        if (_settings.Quests.Count(q => !q.Done && q.Until > DateTime.Now) >= 3) return;
        NewQuest();
    }

    public string NewQuest(Figure? by = null, QuestKind? kind = null)
    {
        var rng = _w.Rng;
        var who = by ?? _w.Figures.Where(f => f.Mode == Mode.Control && f.Visitor == VisitorKind.None && !f.Brain.Asleep
                                             && !_settings.Quests.Any(q => !q.Done && q.By == f.Name && q.Until > DateTime.Now))
                                 .OrderBy(_ => rng.Next()).FirstOrDefault();
        if (who == null) return "nobody to ask";
        var b = who.Brain;
        var options = new List<(float w, Func<Quest?> make)>();
        // A thing they'd like (that isn't out already).
        options.Add((3, () =>
        {
            var missing = WishList.Where(x => ItemCatalog.Find(x.key) != null && !_w.Items.Any(i => i.Def.Key == x.key)).ToList();
            if (missing.Count == 0) return null;
            var (key, ask) = missing[rng.Next(missing.Count)];
            return new Quest { Kind = QuestKind.Thing, Target = key, Text = b.V($"Could we get {ask}?", $"CAN WE GET {ask.ToUpperInvariant()}?! PLEASE!!", $"We need {ask}. Just saying.", $"um… would {ask} be ok?", $"I dreamt of {ask}…") };
        }));
        // A pet.
        if (_w.Pets.Count < 4)
            options.Add((1.2f, () =>
            {
                var kinds = Enum.GetValues<PetKind>().Where(k => !_w.Pets.Any(p => p.Kind == k)).ToList();
                if (kinds.Count == 0) return null;
                var k = kinds[rng.Next(kinds.Count)];
                string a = k.ToString().ToLowerInvariant();
                return new Quest { Kind = QuestKind.Pet, Target = k.ToString(), Baseline = _w.Pets.Count(p => p.Kind == k).ToString(), Text = b.V($"Can we adopt a {a}?", $"A {a.ToUpperInvariant()}! CAN WE HAVE A {a.ToUpperInvariant()}?!", $"A {a} wouldn't be terrible.", $"…maybe a little {a}?", $"I keep thinking about a {a}…") };
            }));
        // An event.
        options.Add((1.5f, () =>
        {
            string e = DateTime.Now.Hour >= 18 ? new[] { "festival", "talent" }[rng.Next(2)] : new[] { "race", "talent" }[rng.Next(2)];
            string what = e switch { "race" => "a race", "talent" => "a talent show", _ => "a festival" };
            return new Quest { Kind = QuestKind.Event, Target = e, Text = b.V($"Could we have {what}?", $"{what.ToUpperInvariant()}! LET'S HAVE {what.ToUpperInvariant()}!", $"Bit boring. {what}, maybe?", $"would… {what} be fun?", $"Imagine {what}…") };
        }));
        // A new hat.
        options.Add((1, () => new Quest { Kind = QuestKind.Hat, Target = "", Baseline = who.Look.Hat, Text = b.V("I'd love a new hat.", "NEW HAT!!! PLEASE!!", "This hat's getting old.", "a new hat… maybe?", "I dreamt of a different hat.") }));
        // A fish that's biting and not yet caught.
        if (_w.Items.Any(i => i.Def.Key == "pond"))
            options.Add((1, () =>
            {
                var biting = Fishes.Biting(DateTime.Now, _w.Weather.Raining).Where(k => !k.Junk && k.Rarity <= FishRarity.Uncommon).ToList();
                if (biting.Count == 0) return null;
                var k = biting.FirstOrDefault(x => !_settings.FishLog.ContainsKey(x.Key)) ?? biting[rng.Next(biting.Count)];
                string a = k.Name.ToLowerInvariant();
                return new Quest { Kind = QuestKind.Fish, Target = k.Key, Baseline = (_settings.FishLog.TryGetValue(k.Key, out var r) ? r.Count : 0).ToString(), Text = b.V($"Someone catch me a {a}?", $"I WANT A {a.ToUpperInvariant()}!!! FROM THE POND!", $"A {a} would be nice. If anyone's fishing.", $"could someone… catch a {a}?", $"The pond has {a}s in it, you know.") };
            }));
        // A game with you.
        options.Add((1.2f, () =>
        {
            var g = new[] { GameKind.Tag, GameKind.HideSeek, GameKind.Catch }[rng.Next(3)];
            string what = g switch { GameKind.Tag => "tag", GameKind.HideSeek => "hide-and-seek", _ => "catch" };
            return new Quest { Kind = QuestKind.Game, Target = g.ToString(), Text = b.V($"Play {what} with me?", $"{what.ToUpperInvariant()}!!! PLAY {what.ToUpperInvariant()} WITH ME!", $"Fine. Play {what}. If you want.", $"would you… play {what}?", $"Let's play {what} sometime.") };
        }));
        if (kind is { } only) options.RemoveAll(o => o.make() is not { } q || q.Kind != only);
        Quest? quest = null;
        for (int tries = 0; tries < 6 && quest == null && options.Count > 0; tries++)
        {
            float total = options.Sum(o => o.w), pick = (float)rng.NextDouble() * total;
            var choice = options[^1];
            foreach (var o in options) { pick -= o.w; if (pick <= 0) { choice = o; break; } }
            quest = choice.make();
            if (quest != null && kind is { } k2 && quest.Kind != k2) quest = null;
        }
        if (quest == null) return "nothing to ask for";
        quest.Id = _settings.Quests.Count == 0 ? 1 : _settings.Quests.Max(q => q.Id) + 1;
        quest.By = who.Name;
        quest.Made = DateTime.Now;
        quest.Until = DateTime.Now.AddHours(24);
        _settings.Quests.Add(quest);
        _settings.Quests.RemoveAll(q => q.Until < DateTime.Now.AddDays(-7));   // tidy away the old ones
        who.Emote(World.Gestures ? "🙏" : quest.Text, 3.2f);
        _w.News("requests", $"{who.Name} asks: \"{quest.Text}\"", 1, who);
        World.Log($"quest: {who.Name}: {quest.Text}");
        return $"{who.Name} asks: {quest.Text}";
    }

    void CheckQuests()
    {
        foreach (var q in _settings.Quests)
        {
            if (q.Done || q.Until < DateTime.Now) continue;
            var by = _w.Figures.FirstOrDefault(f => f.Name == q.By);
            if (by == null) continue;
            bool done = q.Kind switch
            {
                QuestKind.Thing => _w.Items.Any(i => i.Def.Key == q.Target),
                QuestKind.Pet => _w.Pets.Count(p => p.Kind.ToString() == q.Target) > int.Parse(q.Baseline),
                QuestKind.Event => _w.Happening?.Kind == q.Target,
                QuestKind.Hat => by.Look.Hat != q.Baseline && by.Look.Hat.Length > 0,
                QuestKind.Fish => (_settings.FishLog.TryGetValue(q.Target, out var r) ? r.Count : 0) > int.Parse(q.Baseline),
                QuestKind.Game => _w.Game is { } g && g.Kind.ToString() == q.Target && (g.Players.Contains(by) || g.Kind == GameKind.HideSeek),
                _ => false,
            };
            if (!done) continue;
            q.Done = true;
            _settings.QuestsDone++;
            by.Brain.QuestGranted(q.Text, _w);
            _w.News("requests", $"{by.Name} got what they asked for and is over the moon", 2, by);
            _w.Sticker("helper");
            if (_settings.QuestsDone >= 10) _w.Sticker("goodfriend");
            Contribute("REQUESTS_DONE", 1);
            _settings.Save();
        }
    }

    object QuestState() => _settings.Quests.Where(q => !q.Done && q.Until > DateTime.Now || q.Done && q.Until > DateTime.Now.AddHours(-24))
        .OrderBy(q => q.Done).ThenByDescending(q => q.Made)
        .Select(q => new { id = q.Id, by = q.By, text = q.Text, kind = q.Kind.ToString(), target = q.Target, done = q.Done, left = Math.Max(0, (q.Until - DateTime.Now).TotalHours), how = QuestHow(q) });

    static string QuestHow(Quest q) => q.Kind switch
    {
        QuestKind.Thing => "Put one out from Things (or just say it).",
        QuestKind.Pet => "Adopt one from Pets.",
        QuestKind.Event => "Start it from the town menu (right-click the desktop) or the quick panel.",
        QuestKind.Hat => "Pick a hat in their Look tab.",
        QuestKind.Fish => "Make sure there's a pond out: someone has to catch one.",
        QuestKind.Game => "Right-click them and start the game.",
        _ => "",
    };
}
