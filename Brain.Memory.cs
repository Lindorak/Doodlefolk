using System.Numerics;

namespace Doodlefolk;

/// <summary>One remembered moment: what happened (in their own words), who with, where, how it felt and how much it
/// mattered. Saved with the cast.</summary>
sealed class Episode
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public string Who { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    /// <summary>How it felt, -3 (awful) … +3 (wonderful).</summary>
    public int Feel { get; set; }
    /// <summary>How much it mattered, 1 (a little thing) … 9 (life-changing).</summary>
    public int Weight { get; set; }
    public DateTime When { get; set; }
    /// <summary>Brain time it happened / was last brought to mind (this run; earlier runs count as long ago).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public float At, Recalled;
}

/// <summary>Memory: everything worth a diary line is also kept as an episode, and the strongest come back. Meeting
/// someone brings back what happened between you (and recalling it warms or sours the feeling again, the way
/// dwelling on things does); coming back to a place brings back what happened there; and now and then they reflect,
/// so what keeps going well becomes something they love, and what keeps going badly something they avoid.
/// Retrieval is by recency, importance and relevance (as in "Generative Agents", without any language model).</summary>
sealed partial class Brain
{
    public readonly List<Episode> Episodes = new();
    float _reflectAt = 300, _placeRecallAt = 20;
    const int MaxEpisodes = 96;

    /// <summary>How important each kind of moment is (diary keys, by prefix).</summary>
    static int WeightOf(string key) => key switch
    {
        _ when key.StartsWith("born") => 9,
        _ when key.StartsWith("confess") || key.StartsWith("breakup") || key.StartsWith("asked") || key.StartsWith("memorial") || key.StartsWith("baby") => 8,
        _ when key.StartsWith("crush") || key.StartsWith("friend:") || key.StartsWith("foe:") || key.StartsWith("master") || key.StartsWith("dream:done") || key.StartsWith("champion") => 7,
        _ when key.StartsWith("won") || key.StartsWith("ko") || key.StartsWith("skill") || key.StartsWith("match") || key.StartsWith("event") || key.StartsWith("thrown") || key.StartsWith("rival") => 6,
        _ when key.StartsWith("jealous") || key.StartsWith("wish") || key.StartsWith("gift") || key.StartsWith("pencil") || key.StartsWith("swim") || key.StartsWith("fish") => 5,
        _ when key.StartsWith("ate") || key.StartsWith("nap") || key.StartsWith("dance") || key.StartsWith("view") => 3,
        _ => 4,
    };

    /// <summary>What a kind of moment was about (so reflecting on it can change what they like).</summary>
    static Thing? ThingOf(string key) => key switch
    {
        _ when key.StartsWith("ate") => Thing.Eating,
        _ when key.StartsWith("nap") => Thing.Napping,
        _ when key.StartsWith("dance") => Thing.Dancing,
        _ when key.StartsWith("view") => Thing.HighPlaces,
        _ when key.StartsWith("match") => Thing.PlayingBall,
        _ when key.StartsWith("swim") || key.StartsWith("fish") => Thing.Exploring,
        _ when key.StartsWith("won") || key.StartsWith("ko") => Thing.Fighting,
        _ when key.StartsWith("thrown") => Thing.BeingThrown,
        _ when key.Contains("Juggling") || key.StartsWith("juggle") => Thing.Juggling,
        _ when key.Contains(":Ball") || key.StartsWith("goal") || key.StartsWith("swish") => Thing.PlayingBall,
        _ when key.Contains("Climbing") => Thing.Climbing,
        _ when key.Contains("Dancing") || key.StartsWith("festival") || key.StartsWith("event:festival") => Thing.Dancing,
        _ when key.StartsWith("chat") || key.StartsWith("friend:") => Thing.Chatting,
        _ when key.StartsWith("read") || key.StartsWith("book") => Thing.Reading,
        _ when key.StartsWith("pet") => Thing.Pets,
        _ when key.StartsWith("petted") => Thing.BeingPickedUp,
        _ => null,
    };

    static int FeelOf(string mood) => mood switch
    {
        "♥" => 3, "★" => 2, "♪" or "✎" or "☾" => 1,
        "💔" => -3, "⚡" or "⚔" => -2, "☁" or "…" => -1,
        _ => 0,
    };

    /// <summary>Keep a diary moment as an episode.</summary>
    void Remember(string key, string text, string mood)
    {
        var w = World.Current;
        string who = "";
        if (w != null)
            foreach (var o in w.Figures)
                if (o != f && o.Name.Length > 1 && text.Contains(o.Name)) { who = o.Name; break; }
        var e = new Episode { Key = key, Text = text, Who = who, X = f.Base.X, Y = f.Base.Y, Feel = FeelOf(mood), Weight = WeightOf(key), When = DateTime.Now, At = _t0, Recalled = _t0 };
        Episodes.Add(e);
        ThoughtFrom(e);
        if (Episodes.Count > MaxEpisodes)
        {
            // Forget the faintest: unimportant and long ago.
            var faintest = Episodes.OrderBy(x => Strength(x)).First();
            Episodes.Remove(faintest);
        }
    }

    /// <summary>How strongly a memory comes to mind: important things stay; everything fades unless recalled.</summary>
    float Strength(Episode e)
    {
        float hours = MathF.Max(0, _t0 - MathF.Max(e.At, e.Recalled)) / 3600f;
        float recency = MathF.Pow(0.6f, hours * (10f / (e.Weight + 1)));
        return e.Weight / 9f + recency + MathF.Abs(e.Feel) * 0.08f;
    }

    /// <summary>The strongest memory matching a question, brought to mind (which keeps it fresh).</summary>
    Episode? Recall(Func<Episode, bool> about, float min = 0.5f)
    {
        Episode? best = null;
        float bs = min;
        foreach (var e in Episodes) if (about(e) && Strength(e) is var s && s > bs) { bs = s; best = e; }
        if (best != null) best.Recalled = _t0;
        return best;
    }

    /// <summary>For the Studio: what they remember most, strongest first.</summary>
    public IEnumerable<Episode> Remembers(int n = 8) => Episodes.OrderByDescending(Strength).Take(n);

    /// <summary>Meeting someone: what happened between you comes back, and it shows (and dwelling on it warms or sours
    /// the feeling again).</summary>
    void RecallOnMeeting(Figure o, World w)
    {
        var e = Recall(x => x.Who == o.Name && MathF.Abs(x.Feel) >= 2, 0.7f);
        if (e == null || rng.NextDouble() > 0.45) return;
        AddAffinity(o, e.Feel * 0.01f);
        string line = e.Key switch
        {
            _ when e.Key.StartsWith("won:") => V("Rematch?", "Good fight last time!", "Back for more?", "…please don't hit me again.", "We meet again."),
            _ when e.Key.StartsWith("ko") => V("You knocked me out.", "Hey, no hard feelings! …mostly.", "I remember what you did.", "…you hit hard."),
            _ when e.Key.StartsWith("friend:") => V("My buddy!", "BEST FRIEND!!", "Oh. You. …good.", "I'm so glad you're here.", "My friend."),
            _ when e.Key.StartsWith("foe:") => V("Hmph. YOU.", "Let's be nice today, okay?", "Ugh. You.", "…", "You again."),
            _ when e.Key.StartsWith("confess") || e.Key.StartsWith("asked") => e.Feel > 0 ? "♥" : "…",
            _ when e.Key.StartsWith("breakup") => "…",
            _ when e.Key.StartsWith("match") => e.Feel > 0 ? V("Great game last time!", "Remember that match?!") : V("We'll get you next time.", "Rematch!"),
            _ => e.Feel > 0 ? V("Good to see you!", "Yay, you!", "Oh. Hi.", "Hi… ♥", "Ah, you.") : V("…", "Hm.", "Not you again."),
        };
        f.Emote(line, 1.6f);
    }

    /// <summary>Moments that belong to a place (a birth, a friendship or a crush happen anywhere).</summary>
    static readonly string[] PlaceKinds = { "event", "won", "ko", "match", "skill", "master", "confess", "asked", "view", "swim", "fish", "thrown", "breakup", "champion", "goal" };

    /// <summary>Back somewhere something big happened: it comes back to them.</summary>
    void RecallPlace(World w)
    {
        if (_t0 < _placeRecallAt || _g is not (G.Idle or G.Watch or G.SitFloor or G.SitEdge) || f.Mode != Mode.Control) return;
        _placeRecallAt = _t0 + rng.Range(40, 90);
        var e = Recall(x => x.Weight >= 5 && x.Feel != 0 && PlaceKinds.Any(k => x.Key.StartsWith(k)) && _t0 - x.At > 120 && Vector2.Distance(new Vector2(x.X, x.Y), f.Base) < 130 * S && _t0 - x.Recalled > 600, 0.7f);
        if (e == null) return;
        if (e.Feel > 0) f.Emote(V("This is where…", "This spot!! ♥", "Huh. This place.", "I remember this spot…", "Here, once…"), 1.6f);
        else { f.Emote(V("…not here.", "Ugh, this spot.", "Bad memories.", "I don't like it here…"), 1.6f); Fear = MathF.Max(Fear, 0.15f); }
        Write("place:" + e.Key, e.Feel > 0
            ? V($"Came back to where it happened: \"{e.Text}\"", $"Went back to THE spot! (\"{e.Text}\")", $"Walked past where \"{e.Text}\". Whatever.", $"I stood where \"{e.Text}\" and felt warm.", $"Returned to the place of \"{e.Text}\".")
            : V($"Walked past where \"{e.Text}\". Didn't stay.", $"Passed THAT spot. (\"{e.Text}\") Moving on!", $"That spot again. \"{e.Text}\" Ugh.", $"I hurried past where \"{e.Text}\"…"), e.Feel > 0 ? "♪" : "☁", 1800);
    }

    /// <summary>Every so often (and in their sleep), thinking it over: what keeps going well becomes a love, what keeps
    /// going badly something they'd rather not; repeated good times with someone settle into friendship, bad ones into
    /// a grudge.</summary>
    void Reflect(World w)
    {
        if (_t0 < _reflectAt) return;
        _reflectAt = _t0 + (Asleep ? 120 : rng.Range(300, 600));
        var recent = Episodes.Where(e => _t0 - e.At < 3 * 3600).ToList();
        foreach (var g in recent.Where(e => ThingOf(e.Key) != null).GroupBy(e => ThingOf(e.Key)!.Value))
        {
            int n = g.Count();
            if (n < 2) continue;
            float mood = g.Average(e => (float)e.Feel);
            if (MathF.Abs(mood) < 0.8f) continue;
            float before = f.Tastes.Of(g.Key), after = Math.Clamp(before + MathF.Sign(mood) * 0.05f * MathF.Min(n, 4), -1, 1);
            f.Tastes.Set(g.Key, after);
            if (before < 0.5f && after >= 0.5f) Write("love:" + g.Key, V($"I think I really love {ThingWords(g.Key)} now.", $"{ThingWords(g.Key).ToUpperInvariant()} IS MY FAVOURITE THING!", $"Fine. I like {ThingWords(g.Key)}. Happy?", $"I've grown fond of {ThingWords(g.Key)}…", $"{ThingWords(g.Key)} has become part of me."), "♥", 1e9f);
            if (before > -0.5f && after <= -0.5f) Write("hate:" + g.Key, V($"I've gone right off {ThingWords(g.Key)}.", $"Not doing {ThingWords(g.Key)} again for a while!", $"{ThingWords(g.Key)}. Never again.", $"{ThingWords(g.Key)} isn't for me…"), "☁", 1e9f);
        }
        foreach (var g in recent.Where(e => e.Who.Length > 0).GroupBy(e => e.Who))
        {
            if (g.Count() < 3 || w.Figures.FirstOrDefault(o => o.Name == g.Key) is not { } o) continue;
            float mood = g.Average(e => (float)e.Feel);
            if (MathF.Abs(mood) >= 1) AddAffinity(o, MathF.Sign(mood) * 0.04f);
        }
    }
}
