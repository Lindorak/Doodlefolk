using System.Numerics;

namespace Doodlefolk;

/// <summary>Learning by watching. Seeing someone you look up to enjoying something (a friend juggling, the town's best
/// dancer dancing) makes you a little keener to try it yourself: the same learned habit table their own experience
/// feeds, nudged by admiration, closeness and curiosity, with diminishing returns, and bounded so a shy figure doesn't
/// turn into a party animal overnight. Fads spread this way. Yawns are catching too.</summary>
sealed partial class Brain
{
    float _watchOthersT = 4;
    readonly Dictionary<string, string> _copiedFrom = new();

    void WatchOthers(World w, float dt)
    {
        _watchOthersT -= dt;
        if (f.Mode != Mode.Control || Asleep) return;
        // Catching a yawn (straight away, not on the timer).
        foreach (var o in w.Figures)
        {
            if (o == f || o.Action != Act.Fidget || o.FidgetKind != Fidget.Yawn || o.ActionT > World.Dt * 1.5f) continue;
            if (Vector2.Distance(o.Base, f.Base) > 260 * S || f.Action == Act.Fidget || _g is not (G.Idle or G.Watch or G.SitFloor or G.SitEdge)) continue;
            if (rng.NextDouble() < 0.25 + (1 - Stamina) * 0.5 + MathF.Max(0, AffinityWith(o)) * 0.2) { f.StartFidget(Fidget.Yawn); break; }
        }
        if (_watchOthersT > 0) return;
        _watchOthersT = rng.Range(2.5f, 4.5f);
        // Who's in view doing something they plainly enjoy, and how much do we look up to them?
        Figure? model = null;
        float best = 0.15f;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || o.Brain._g is G.Idle or G.Busy or G.Sleep or G.Walk) continue;
            if (Vector2.Distance(o.Base, f.Base) > 480 * S || MathF.Abs(o.Base.Y - f.Base.Y) > 220 * S) continue;
            var ob = o.Brain;
            if (ob.LastDecision.Length == 0 || ob.Joy < 0.45f) continue;
            float admire = AffinityWith(o) + (o == Beloved(w) ? 0.5f : 0) + ob.Skills.Values.DefaultIfEmpty(0).Max() * 0.4f + ob.Trophies * 0.05f;
            if (admire > best) { best = admire; model = o; }
        }
        if (model == null) return;
        string label = model.Brain.LastDecision, key = ActivityKey(label), what = label.ToLowerInvariant();
        if (key.Length == 0) return;
        // Picking up fighting from others only happens to those with some fight in them already.
        if ((key.StartsWith("start a") || key.StartsWith("fight") || key.StartsWith("spar")) && P.Aggression < 0.5f) return;
        if (SkillOf(key) is SkillKind watched && model.Brain.Sk(watched) > Sk(watched) + 0.2f) Practice(watched, 0.004f);
        float old = Learned.GetValueOrDefault(key);
        // Small steps, smaller the keener they already are; curious, sociable figures copy more.
        float nudge = 0.035f * best * (0.5f + P.Curiosity * 0.5f + P.Sociability * 0.3f) * (1 - MathF.Max(0, old));
        if (A == Archetype.Yandere && model == Beloved(w)) nudge *= 2;
        Learned[key] = Math.Clamp(old + nudge, -1, 0.85f);
        if (old < 0.3f && Learned[key] >= 0.3f && !_copiedFrom.ContainsKey(key))
        {
            _copiedFrom[key] = model.Name;
            f.Emote(V("ooh", "I WANNA DO THAT!", "…looks fun.", "maybe I could…", "I see."), 1.2f);
            Write("copied:" + key, V($"I want to {what} like {model.Name} does.", $"{model.Name} makes it look SO fun ({what})! My turn!", $"Fine. {model.Name} made \"{what}\" look alright.", $"I've been watching {model.Name}… I want to {what} too.", $"{model.Name} showed me how to {what}, without a word."), "♪", 1e9f);
        }
    }

    static SkillKind? SkillOf(string key) => key switch
    {
        _ when key.StartsWith("practise juggling") || key.StartsWith("juggle") => SkillKind.Juggling,
        _ when key.StartsWith("play ball") || key.StartsWith("practise with") => SkillKind.Ball,
        _ when key.StartsWith("dance") || key.StartsWith("practise dancing") => SkillKind.Dancing,
        _ when key.StartsWith("go fishing") => SkillKind.Fishing,
        _ => null,
    };

    /// <summary>For the Studio: habits picked up from watching someone.</summary>
    public IEnumerable<(string what, string from)> CopiedHabits() => _copiedFrom.Select(kv => (kv.Key, kv.Value));
}
