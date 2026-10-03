using System.Collections;
using System.Numerics;

namespace Doodlefolk;

/// <summary>The things a figure could do next, each with how much it wants to and a plain-English label.</summary>
sealed class OptionList : IEnumerable<(float weight, Action act)>
{
    public readonly List<(float weight, Action act, string label)> Items = new();
    /// <summary>Label given to options added without one.</summary>
    public string Category = "";
    public void Add((float weight, Action act) o) => Items.Add((o.weight, o.act, Category));
    public void Add(float weight, Action act, string label) => Items.Add((weight, act, label));
    public IEnumerator<(float weight, Action act)> GetEnumerator() => Items.Select(i => (i.weight, i.act)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Deciding. Every option gets a desire score from needs, mood, tastes and personality; scores are then
/// sharpened so the best options usually win (decisive figures more so, playful ones stay a bit spontaneous), things
/// done a lot lately lose their shine (curious figures crave variety), and places or things that turned out to be
/// unreachable are skipped for a while. The last decision is kept so the Studio can show what they're thinking.</summary>
sealed partial class Brain
{
    readonly List<(string label, float at)> _recent = new();
    readonly Dictionary<object, float> _unreachable = new();
    object? _navAbout;

    /// <summary>Top options at the last decision (label, share of the chance) and what was picked.</summary>
    public (string label, float share)[] Thoughts = Array.Empty<(string, float)>();
    public string LastDecision = "";
    /// <summary>How many decisions they've made (this run).</summary>
    public int Decisions;
    public float DecidedAgo => _t0 - _decidedAt;
    float _decidedAt;

    /// <summary>A route description for the Studio: what it's heading for and the moves planned.</summary>
    public string RoutePlan => _g == G.Walk && _route is { Count: > 0 } r
        ? string.Join(" → ", r.Skip(_routeStep).Select(e => e.Kind switch { MoveKind.Jump => "jump", MoveKind.Climb => "climb", MoveKind.Drop => "drop down", _ => "walk on" }))
        : "";

    public bool Unreachable(object o) => _unreachable.TryGetValue(o, out var t) && _t0 < t;

    /// <summary>Navigation gave up: remember not to try for the same thing again right away.</summary>
    void NoRoute()
    {
        World.Audit($"noroute\t{f.Name}\t{LastDecision}\t{_navAbout}");
        if (_navAbout != null) _unreachable[_navAbout] = _t0 + 60;
        if (f.Hunter) _huntFails += 1;
        if (_unreachable.Count > 64) foreach (var k in _unreachable.Where(kv => kv.Value < _t0).Select(kv => kv.Key).ToList()) _unreachable.Remove(k);
    }

    float Novelty(string label)
    {
        int n = 0;
        foreach (var (l, at) in _recent) if (l == label && _t0 - at < 180) n++;
        return 1f / (1 + n * 0.3f * (0.5f + P.Curiosity));
    }

    /// <summary>Things tried that fell through almost at once (couldn't get there, got turned down, it moved): a
    /// couple more tries, each a little less keen, then they give up on it for a while (and say so), so nobody flips
    /// between trying and giving up forever. Doing it properly once resets the count.</summary>
    readonly Dictionary<string, (int fails, float until)> _fellThrough = new();
    static readonly string[] ShortByNature = { "hang out", "sit down", "cheer", "do a", "dance", "wave", "nap" };

    float FellThroughTilt(string label)
    {
        if (!_fellThrough.TryGetValue(label, out var ft) || _t0 >= ft.until) return 1;
        int tries = TriesFor(label);
        return ft.fails >= tries ? 0.05f : 1 - 0.65f * ft.fails / tries;   // a little less keen each time; given up once out of tries
    }

    /// <summary>Grit: how long they keep at something before giving up (brave, energetic, stubborn figures longest).</summary>
    float Grit => Math.Clamp(0.35f * P.Bravery + 0.25f * P.Energy + 0.2f * P.Aggression + 0.2f * (1 - P.Playfulness) + A switch
    {
        Archetype.Genki or Archetype.Tsundere => 0.2f,
        Archetype.Kuudere or Archetype.Yandere => 0.1f,
        Archetype.Dandere => -0.2f,
        _ => 0,
    }, 0, 1);

    /// <summary>How many quick failures before giving up on this: grit, plus how much it matters (a challenge they've
    /// set themselves, their dream, a real need, their beloved), minus a sting (being turned down, for the shy).</summary>
    int TriesFor(string label)
    {
        string k = ActivityKey(label);
        float matters = 0;
        if (Dream != null && label == _dreamStep) matters += 1.5f;
        if (k.StartsWith("explore") || k.StartsWith("practise") || k.StartsWith("skip") || k.StartsWith("climb") || k.StartsWith("ride") || k.StartsWith("swing") || k.StartsWith("play ball") || k.StartsWith("lasso")) matters += 1.2f;
        if (k.StartsWith("eat") || k.StartsWith("go to bed") || k.StartsWith("have a") || k.StartsWith("catch their")) matters += MathF.Max(Hunger, MathF.Max(Sleepy, 1 - Stamina)) * 2.5f;
        if (k.StartsWith("hang out with") || k.StartsWith("go and see") || k.StartsWith("spend time"))
            matters += A == Archetype.Yandere ? 1.5f : P.Sociability < 0.35f ? -1 : 0;
        if (k.StartsWith("hunt") || k.StartsWith("start a")) matters += P.Aggression;
        return Math.Clamp((int)MathF.Round(1 + Grit * 3 + matters), 1, 6);
    }

    void Decide(OptionList opts)
    {
        if (opts.Items.Count == 0) { Go(G.Idle, 1); return; }
        // How did the last one go? Over in a moment (and not something brief anyway): don't go straight back to it.
        if (LastDecision.Length > 0 && !ShortByNature.Any(s => ActivityKey(LastDecision).StartsWith(s)))
        {
            if (_t0 - _decidedAt < 2.5f)
            {
                int fails = (_fellThrough.TryGetValue(LastDecision, out var ft) && _t0 < ft.until + 30 ? ft.fails : 0) + 1;
                int tries = TriesFor(LastDecision);
                // Out of tries: a break from it (shorter for the gritty, who'll be back); otherwise a moment before the next go.
                float rest = fails >= tries ? 40 + 120 * (1 - Grit) : 4 + fails * 3;
                _fellThrough[LastDecision] = (fails, _t0 + rest);
                if (fails == tries) f.Emote(Grit > 0.6f ? V("I'll be back.", "NOT DONE YET!", "later. definitely later.", "one day…", "another time") : V("forget it", "ugh, FINE!", "never mind.", "…maybe later", "not today"), 1.2f);
                else if (fails >= 2 && Grit > 0.6f && rng.NextDouble() < 0.5) f.Emote(V("again!", "ONE MORE TIME!", "again.", "try again…", "once more"), 0.9f);
            }
            else _fellThrough.Remove(LastDecision);   // it worked: start afresh
            if (_fellThrough.Count > 30) foreach (var k in _fellThrough.Where(kv => kv.Value.until + 30 < _t0).Select(kv => kv.Key).ToList()) _fellThrough.Remove(k);
        }
        // Decisive figures go for what they want most; playful, impulsive ones roll the dice more.
        float sharp = (1.4f + (1 - P.Playfulness) * 1.1f) * MoodSharpness;
        var scored = opts.Items.Select(o => (o.label, o.act, s: MathF.Pow(MathF.Max(0, o.weight), sharp) * Novelty(o.label) * LearnedTilt(o.label) * FocusTilt(o.label) * ArchetypeTilt(o.label) * SkillTilt(o.label) * FellThroughTilt(o.label))).ToList();
        float total = scored.Sum(x => x.s);
        if (total <= 0) { opts.Items[0].act(); return; }
        Thoughts = scored.GroupBy(x => x.label).Select(g => (g.Key, g.Sum(x => x.s) / total)).OrderByDescending(x => x.Item2).Take(6).ToArray();
        float roll = rng.Range(0, total);
        var pick = scored[^1];
        foreach (var x in scored) { roll -= x.s; if (roll <= 0) { pick = x; break; } }
        LearnFromLast(pick.label);
        LastDecision = pick.label;
        StressOf(pick.label);
        World.Audit($"decide\t{f.Name}\t{pick.label}\t{pick.s / total:F2}\t{string.Join("|", Thoughts.Take(4).Select(t => $"{t.label}:{t.share:F2}"))}");
        _decidedAt = _t0;
        Decisions++;
        _recent.Add((pick.label, _t0));
        if (_recent.Count > 30) _recent.RemoveAt(0);
        pick.act();
    }

    /// <summary>Somewhere else worth going: any surface it can actually get to (multi-hop routes included), favouring
    /// what it likes (high places for climbers, the taskbar for those who like it) and not too far away.</summary>
    bool PickExplore(Env env, Platform seg, out Action plan)
    {
        plan = () => { };
        var w = World.Current;
        var cands = new List<(Platform p, float x, float score)>();
        foreach (var p in env.Platforms)
        {
            if (SameSegment(p, seg) || p.X2 - p.X1 < 28 * S || p.Item != null) continue;
            float x = rng.Range(p.X1 + 12 * S, p.X2 - 12 * S);
            float d = MathF.Abs(x - f.Base.X) + MathF.Abs(p.Y - seg.Y);
            if (d > 1800 * S) continue;
            float score = 1f / (1 + d / (600 * S)) * (p.Y < seg.Y - 40 * S ? 1.3f * Taste(Thing.HighPlaces) : 1f) * (p.Solid ? 0.6f * Taste(Thing.Taskbar) : 1f)
                          * (p.Seen != null ? 0.7f + P.Curiosity * 0.5f : 1);
            if (Unreachable(NavGraph.Key(p))) continue;
            cands.Add((p, x, score));
        }
        // Check a few of the most appealing ones for an actual route.
        foreach (var c in cands.OrderByDescending(c => c.score * rng.Range(0.6f, 1.4f)).Take(5))
        {
            var route = w.Nav.FindPath(seg, f.Base.X, c.p, c.x, MyMover, MoveCost, 250);
            if (route == null || route.Count == 0) { _unreachable[NavGraph.Key(c.p)] = _t0 + 45; continue; }
            bool climbs = route.Any(e => e.Kind == MoveKind.Climb);
            if (climbs && f.Tastes.Dislikes(Thing.Climbing) && rng.NextDouble() < 0.7) continue;
            var target = Anchor.On(env, c.p, c.x);
            float startY = f.Base.Y;
            var key = NavGraph.Key(c.p);
            plan = () =>
            {
                var reached = c.p;
                Navigate(() => target.Resolve(env), 4 * S, false, () =>
                {
                    NoteExplored(reached);
                    if (startY - f.Base.Y > 250 * S) DiaryClimbed(startY - f.Base.Y);
                    if (startY - f.Base.Y > 120 * S && P.Playfulness > 0.45f && rng.NextDouble() < 0.6) Go(G.Cheer, 0.9f);
                    else Go(G.Idle, rng.Range(0.5f, 2f));
                }, WalkPurpose.Explore);
                _navAbout = key;
            };
            return true;
        }
        return false;
    }
}
