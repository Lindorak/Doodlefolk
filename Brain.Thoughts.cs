namespace Doodlefolk;

/// <summary>One thing on their mind, for a while: how much it lifts (or drags) their mood, and when it fades.</summary>
sealed class Thought
{
    public string Key = "", Text = "";
    public float Weight, Until;
}

/// <summary>Mood made of thoughts (as in RimWorld and The Sims' moodlets): every moment they remember leaves a thought
/// that lifts or drags their mood for a while, more and longer the bigger it was; the sum is their mood, which pulls
/// joy and sadness its way and changes how they choose (sharper when happy, more erratic when miserable). And stress
/// (as in Crusader Kings): doing things against their nature (the shy in crowds, the gentle in fights, the lazy
/// running) builds it up; too much and they break, go off to cope in their own way, and come back relieved.</summary>
sealed partial class Brain
{
    public readonly List<Thought> Thoughts2 = new();
    public float Stress;
    float _thoughtTick, _breakUntil;
    bool _breaking;

    /// <summary>The sum of what's on their mind (roughly -20..+20).</summary>
    public float MoodSum => Thoughts2.Sum(t => t.Weight);

    void Think(string key, string text, float weight, float seconds)
    {
        var same = Thoughts2.FirstOrDefault(t => t.Key == key);
        if (same != null) { same.Until = MathF.Max(same.Until, _t0 + seconds); same.Weight = MathF.Abs(weight) > MathF.Abs(same.Weight) ? weight : same.Weight; return; }
        Thoughts2.Add(new Thought { Key = key, Text = text, Weight = weight, Until = _t0 + seconds });
        if (Thoughts2.Count > 12) Thoughts2.Remove(Thoughts2.OrderBy(t => MathF.Abs(t.Weight)).First());
    }

    /// <summary>A remembered moment becomes a thought: how it felt times how much it mattered.</summary>
    void ThoughtFrom(Episode e)
    {
        if (e.Feel == 0) return;
        float weight = e.Feel * (0.6f + e.Weight * 0.35f);
        Think(e.Key, e.Text, weight, 90 + e.Weight * 90);
    }

    void UpdateThoughts(World w, float dt)
    {
        _thoughtTick -= dt;
        if (_thoughtTick > 0) return;
        _thoughtTick = 1;
        Thoughts2.RemoveAll(t => _t0 > t.Until);
        // Mood pulls joy and sadness its way, gently.
        float m = Math.Clamp(MoodSum / 15f, -1, 1);
        if (m > 0) Joy = MathF.Min(1, Joy + (m * 0.8f - Joy) * 0.03f);
        else Sadness = MathF.Min(1, Sadness + (-m * 0.8f - Sadness) * 0.03f);
        // Stress fades slowly when nothing's adding to it.
        Stress = MathF.Max(0, Stress - 0.004f * (1 + MathF.Max(0, m)));
        if (_breaking && _t0 > _breakUntil) Relieved();
        else if (!_breaking && Stress >= 1 && f.Mode == Mode.Control && !Engaged) Breakdown(w);
    }

    /// <summary>How decisive they are right now: happy figures go for what they want; miserable ones dither and roll the dice.</summary>
    float MoodSharpness => 0.85f + Math.Clamp(MoodSum / 40f, -0.25f, 0.35f);

    /// <summary>Something they just chose to do: is it against their nature? (That's stressful.)</summary>
    void StressOf(string label)
    {
        string k = ActivityKey(label);
        float s = 0;
        if ((k.StartsWith("start a") || k.StartsWith("fight") || k.StartsWith("spar")) && P.Aggression < 0.3f) s += 0.12f;
        if ((k.StartsWith("hang out with") || k.StartsWith("go to") && label.Contains("party") || k.StartsWith("invite")) && P.Sociability < 0.25f) s += 0.06f;
        if ((k.StartsWith("play a") || k.StartsWith("practise") || k.StartsWith("skip")) && P.Energy < 0.25f) s += 0.05f;
        if (k.StartsWith("explore") && P.Bravery < 0.2f) s += 0.05f;
        if (s > 0) Stress = MathF.Min(1.3f, Stress + s);
    }

    void Breakdown(World w)
    {
        _breaking = true;
        _breakUntil = _t0 + rng.Range(40, 80);
        Flash(P.Aggression > 0.55f ? Manpu.Steam : Manpu.Gloom, 6);
        f.Emote(V("I need a break.", "I NEED A BREAK!!!", "ENOUGH.", "it's all too much…", "I must be alone."), 2.4f);
        Write("stress", V("Everything got too much today. Had to get away.", "Had a meltdown! Needed some me-time!", "Snapped today. Needed quiet.", "It all got too much and I had to hide for a while…", "The world was too loud; I went away."), "☁", 1800);
        // Coping, each in their own way: a nap, comfort food, a long walk, hiding.
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (f.Tastes.Likes(Thing.Eating) && w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Eat) && i.Free && i.OnGround && !Unreachable(i)) is { } food) UseItem(food, Verb.Eat, w);
        else if (w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Hide) && i.Free && i.User == null && i.OnGround && !Unreachable(i)) is { } hideout && P.Sociability < 0.5f) UseItem(hideout, Verb.Hide, w);
        else if (seg != null && P.Energy > 0.5f && PickExplore(w.Env, seg, out var walk)) walk();
        else Go(G.Sleep, rng.Range(20, 40));
    }

    void Relieved()
    {
        _breaking = false;
        Stress = 0.15f;
        Think("relief", "Got it out of my system", 3, 600);
        f.Emote(V("better now.", "ALL BETTER!", "…fine. better.", "I feel lighter…", "calm again"), 1.6f);
    }

    /// <summary>For the Studio: what's on their mind, biggest first.</summary>
    public IEnumerable<Thought> OnTheirMind() => Thoughts2.OrderByDescending(t => MathF.Abs(t.Weight)).Take(6);
}
