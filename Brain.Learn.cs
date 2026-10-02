namespace Doodlefolk;

/// <summary>Learned preferences: figures develop habits from experience. Each kind of activity carries a learned value:
/// after doing it, how much better (or worse) it felt. That nudges future choices up or down (a quarter at most, so
/// personality still leads). Over weeks one becomes the angler, another the dancer, shaped by what actually happened to
/// them. It's reinforcement learning kept as a simple table, so it can be shown and explained in the Studio.</summary>
sealed partial class Brain
{
    /// <summary>Learned value per kind of activity, -1..1.</summary>
    public readonly Dictionary<string, float> Learned = new();
    /// <summary>How many times each has been tried (for the Studio: "tried it 12 times").</summary>
    public readonly Dictionary<string, int> Tried = new();
    string? _learnKey;
    float _learnMoodAt;

    const float LearnRate = 0.12f, LearnInfluence = 0.25f;

    /// <summary>"Ride the bike", "Ride the go-kart" → "ride the"; "Go fishing" → "go fishing": the kind of thing.</summary>
    public static string ActivityKey(string label)
    {
        var words = label.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch { 0 => "", 1 => words[0], _ => words[0] + " " + words[1] };
    }

    /// <summary>A single number for how good life feels right now.</summary>
    float MoodScore() => Joy - Sadness - Annoyance * 0.5f - Frustration * 0.5f - Fear * 0.5f + (1 - Boredom) * 0.3f;

    /// <summary>How much the learned habit tilts this option's score.</summary>
    float LearnedTilt(string label) => Learned.TryGetValue(ActivityKey(label), out var v) ? 1 + v * LearnInfluence : 1;

    static readonly string[] QuietKinds = { "read", "sit in", "sit on", "nap on", "go to", "water", "build the", "go fishing", "watch the", "draw", "warm up", "tend", "pick up" };
    static readonly string[] LoudKinds = { "play a", "start a", "hang out", "go see", "ride the", "dance", "throw", "chase", "hunt", "go for", "ask", "fight", "spar", "grab", "bounce" };

    /// <summary>While you focus, the town keeps to quiet things.</summary>
    static float FocusTilt(string label)
    {
        if (!World.Focus) return 1;
        string k = ActivityKey(label);
        if (QuietKinds.Any(q => k.StartsWith(q))) return 2.5f;
        if (LoudKinds.Any(q => k.StartsWith(q))) return 0.08f;
        return 0.6f;
    }

    /// <summary>A focus session ended: a cheer, and a line in the diary.</summary>
    public void FocusCheer(World w, int minutes)
    {
        if (f.Brain.Asleep || f.Mode != Mode.Control) return;
        f.Emote(V("focus done! ✓", "WE DID IT!! ✓", "done. good work.", "you did so well… ✓", "the work is done ✓"), 2.2f);
        if (f.Grounded) Go(G.Cheer, 1.2f);
        Write("focus", V($"We focused together for {minutes} minutes. Got 3 coins.", $"FOCUS TIME DONE! {minutes} minutes! +3 coins!", $"Kept quiet for {minutes} minutes while you worked.", $"I was very quiet for {minutes} minutes so you could work…", $"{minutes} minutes of quiet work, shared."), "★", 600);
    }

    /// <summary>A new decision: first, learn from how the last one turned out.</summary>
    void LearnFromLast(string nextLabel)
    {
        if (_learnKey is { Length: > 0 } key)
        {
            float reward = Math.Clamp((MoodScore() - _learnMoodAt) * 2.5f, -1, 1);
            float old = Learned.GetValueOrDefault(key);
            Learned[key] = Math.Clamp(old + LearnRate * (reward - old), -1, 1);
            Tried[key] = Tried.GetValueOrDefault(key) + 1;
            if (Learned.Count > 80)
                foreach (var weakest in Learned.OrderBy(kv => MathF.Abs(kv.Value)).Take(10).Select(kv => kv.Key).ToList()) { Learned.Remove(weakest); Tried.Remove(weakest); }
        }
        _learnKey = ActivityKey(nextLabel);
        _learnMoodAt = MoodScore();
    }

    /// <summary>For the Studio: the strongest habits, either way.</summary>
    public IEnumerable<(string what, float v, int tried)> Habits() =>
        Learned.Where(kv => MathF.Abs(kv.Value) > 0.08f && Tried.GetValueOrDefault(kv.Key) >= 3)
               .OrderByDescending(kv => MathF.Abs(kv.Value)).Take(6)
               .Select(kv => (kv.Key, kv.Value, Tried.GetValueOrDefault(kv.Key)));
}
