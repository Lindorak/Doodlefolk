using System.Numerics;

namespace Doodlefolk;

/// <summary>A life goal being worked towards (saved with the cast).</summary>
sealed class Ambition
{
    public string Key { get; set; } = "";
    public string Target { get; set; } = "";
    public float Progress { get; set; }
    public int Milestone { get; set; }
    public int Count { get; set; }
    public DateTime Since { get; set; } = DateTime.Now;
    /// <summary>Brain time of the last real progress (this run).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public float MovedAt;
}

/// <summary>Dreams to work towards, and plans for getting there. Each figure has at most one ambition at a time,
/// chosen from who they are (what they love, their traits, their friends and rivals, what's around): master a skill,
/// befriend someone, make up with someone, win a heart, redecorate, throw a party, explore every window. An ambition
/// decomposes, step by step, into whatever makes sense next given the situation (a hierarchical-task-network style
/// recipe: practise if there's a ball, ask you for one if there isn't), and that step competes with everything else
/// they could do, so hunger or sleep still come first. Progress is measured, not assumed: milestones are celebrated,
/// a dream come true is a big moment, and one that goes nowhere is given up (and written about).</summary>
sealed partial class Brain
{
    public Ambition? Dream;
    /// <summary>For the tests: steps taken towards dreams, and the decision count when the current one was adopted.</summary>
    public int DreamSteps, DecisionsAtDream, DreamOffers;
    int _noStep;
    public readonly List<string> DreamsDone = new();
    float _dreamCheckAt = 90, _dreamCd;
    string _dreamStep = "";
    readonly HashSet<string> _explored = new();

    sealed record AmbitionDef(string Key, Func<Brain, World, IEnumerable<(string target, float score)>> Candidates,
        Func<Brain, World, string, float> Progress, Func<Brain, World, string, (string label, float weight, Action act)?> Step,
        Func<Brain, string, string> Title);

    static readonly (SkillKind skill, Thing likes)[] DreamSkills = { (SkillKind.Juggling, Thing.Juggling), (SkillKind.Ball, Thing.PlayingBall), (SkillKind.Dancing, Thing.Dancing), (SkillKind.Climbing, Thing.Climbing) };

    static string SkillWords(SkillKind k) => k switch { SkillKind.Ball => "ball games", _ => k.ToString().ToLowerInvariant() };

    static readonly AmbitionDef[] Ambitions =
    {
        // Master a skill they love (and aren't good at yet).
        new("skill",
            (b, w) => DreamSkills.Where(s => b.Sk(s.skill) < 0.7f).Select(s => (s.skill.ToString(), (0.4f + b.f.Tastes.Of(s.likes)) * (1 - b.Sk(s.skill)) * (0.5f + b.P.Playfulness * 0.5f + b.P.Energy * 0.3f))),
            (b, w, t) => Enum.TryParse<SkillKind>(t, out var k) ? M.Clamp01(b.Sk(k) / 0.85f) : 0,
            (b, w, t) =>
            {
                if (!Enum.TryParse<SkillKind>(t, out var k) || b.Stamina < 0.3f) return null;
                switch (k)
                {
                    case SkillKind.Juggling or SkillKind.Ball:
                        if (b.NearestFreeBall(w, 1500 * b.S) is { SizeMul: <= 1.8f })
                            return (k == SkillKind.Juggling ? "Practise juggling" : "Practise with the ball", 2.2f, () => { b.f.Emote(b.V("practice time!", "PRACTICE!!", "again.", "one more try…", "practice"), 1.2f); b.ForceBall(w, k == SkillKind.Juggling ? BallPlay.Juggle : BallPlay.Dribble); });
                        // No ball about: ask you for one.
                        if (w.Wishes && w.Wish == null && b._t0 > b._nextWish)
                            return ("Ask for a ball", 1.2f, () => { b._nextWish = b._t0 + 120; w.Wish = new Wish { By = b.f, What = new(Prop.KindName(PropKind.SoccerBall), null, k == SkillKind.Juggling ? PropKind.Ball : PropKind.SoccerBall, 1, "a ball? (to practise!)"), Until = World.Now + 15 }; });
                        // Plan B: footwork without a ball.
                        return ("Practise footwork", 1.2f, () => { b.f.Emote(b.V("footwork!", "TAP TAP TAP!", "drills.", "left, right…", "the feet remember"), 1.2f); b.Go(G.Idle, 5); b.f.StartFidget(Fidget.KickPebble); b.Practice(k, 0.012f); });
                    case SkillKind.Dancing:
                        if (w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Dance) && i.Free && i.OnGround && !b.Unreachable(i)) is { } radio)
                            return ("Practise dancing", 2f, () => { b.f.Emote("♪", 1); b.UseItem(radio, Verb.Dance, w); });
                        return ("Practise dancing", 1.4f, () => { b.f.Emote(b.V("5, 6, 7, 8!", "DANCE PRACTICE!", "fine. dancing.", "nobody's watching…", "step, step…"), 1.2f); b.Go(G.Idle, 6); b.f.StartFidget(Fidget.Groove); b.f.FidgetDur = 6; b.Practice(SkillKind.Dancing, 0.02f); });
                    case SkillKind.Climbing:
                        var seg = w.Env.SupportAt(b.f.Base.X, b.f.Base.Y, b.f.GroundHwnd);
                        if (seg != null && b.PickExplore(w.Env, seg, out var climb)) return ("Practise climbing", 1.8f, () => { b.f.Emote(b.V("up we go!", "HIGHER!", "climb.", "don't look down…", "upwards"), 1.2f); climb(); });
                        // Plan B: up onto something nearby; plan C: jumps.
                        if (w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Stand) && i.Free && i.OnGround && !b.Unreachable(i) && Vector2.Distance(i.Pos, b.f.Base) < 900 * b.S) is { } perch)
                            return ("Climb onto something", 1.5f, () => { b.UseItem(perch, Verb.Stand, w); b.Practice(SkillKind.Climbing, 0.01f); });
                        return ("Practise jumps", 1.2f, () => { b.f.Emote(b.V("hup!", "JUMP!!", "jumps.", "one… two…", "spring"), 1); b.Go(G.Trick, 3); b.f.RequestFlip(60 * b.S); b.Practice(SkillKind.Climbing, 0.012f); });
                }
                return null;
            },
            (b, t) => Enum.TryParse<SkillKind>(t, out var k) ? $"Get really good at {SkillWords(k)}" : "Get good at something"),

        // Become friends with someone they like the look of.
        new("friend",
            (b, w) => w.Figures.Where(o => o != b.f && o.Visitor == VisitorKind.None && !o.Dead && b.AffinityWith(o) is > 0.05f and < 0.5f && !b.IsRival(o))
                               .Select(o => (o.Name, b.P.Sociability * (0.3f + MathF.Max(0, b.f.Tastes.Similarity(o.Tastes))) * (o.Brain.Skills.Values.DefaultIfEmpty(0).Max() + 0.5f))),
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o ? M.Clamp01((b.AffinityWith(o) - 0.05f) / 0.6f) : -1,
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o && o.Mode == Mode.Control && !b.Unreachable(o) && b.Loneliness + b.P.Sociability > 0.3f
                ? ($"Go and see {o.Name}", 2f, () => { b.f.Emote(b.V($"hi {o.Name}!", $"{o.Name.ToUpperInvariant()}!!", $"…{o.Name}.", $"m-maybe {o.Name} wants to…", $"{o.Name}…"), 1.2f); b.Approach(o, w); })
                : null,
            (b, t) => $"Become friends with {t}"),

        // Make up with someone they fell out with (the forgiving kind do).
        new("makeup",
            (b, w) => w.Figures.Where(o => o != b.f && o.Visitor == VisitorKind.None && !o.Dead && b.AffinityWith(o) is < -0.25f and > -0.9f)
                               .Select(o => (o.Name, (1 - b.P.Aggression) * (0.3f + b.P.Sociability) * 0.8f)),
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o ? M.Clamp01((b.AffinityWith(o) + 0.25f) / 0.45f) : -1,
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o && o.Mode == Mode.Control && !b.Unreachable(o) && b._t0 > b._apologyAt
                ? ($"Make up with {o.Name}", 1.8f, () => b.GoApologise(o, w))
                : null,
            (b, t) => $"Make up with {t}"),

        // Win the heart of the one they have a crush on.
        new("heart",
            (b, w) => w.Romance && b.Crush(w) is { } c && !b.Dating(c) && b.Sweetheart(w) == null ? new[] { (c.Name, 0.6f + b.LoveFor(c)) } : Array.Empty<(string, float)>(),
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o ? (b.Dating(o) ? 1 : M.Clamp01(b.LoveFor(o) * 0.7f + MathF.Max(0, o.Brain.LoveFor(b.f)) * 0.5f)) : -1,
            (b, w, t) => w.Figures.FirstOrDefault(o => o.Name == t) is { } o && o.Mode == Mode.Control && !b.Unreachable(o)
                ? ($"Spend time with {o.Name}", 1.8f, () => { b.f.Blush = MathF.Max(b.f.Blush, 0.5f); b.Approach(o, w); })
                : null,
            (b, t) => $"Win {t}'s heart"),

        // Make their corner nicer: move the seats and beds next to the things they go with.
        new("decorate",
            (b, w) => b.RedecorateIdea(w) != null ? new[] { ("", (0.25f + b.f.Tastes.Of(Thing.Sitting) * 0.5f + b.P.Curiosity * 0.3f) * 0.8f) } : Array.Empty<(string, float)>(),
            (b, w, t) => M.Clamp01(b.Dream!.Count / 2f),
            (b, w, t) => b.Stamina > 0.4f && b.RedecorateIdea(w) is { } idea
                ? ($"Move the {idea.it.Def.Name.ToLowerInvariant()}", 1.8f, () => b.BeginHaul(idea.it, idea.x, w, idea.why, ok => { if (ok && b.Dream?.Key == "decorate") b.Dream.Count++; }))
                : null,
            (b, t) => "Redecorate"),

        // Throw a party: invite friends to the music, then dance.
        new("party",
            (b, w) => w.Items.Any(i => i.Def.Verbs.Contains(Verb.Dance) && i.OnGround) && w.Figures.Count(o => o != b.f && b.AffinityWith(o) > 0.2f) >= 2
                ? new[] { ("", b.P.Sociability * (0.4f + b.P.Playfulness) * (0.5f + b.f.Tastes.Of(Thing.Dancing))) } : Array.Empty<(string, float)>(),
            (b, w, t) => M.Clamp01(b.Dream!.Count / 2f),
            (b, w, t) => b.PartyStep(w),
            (b, t) => "Throw a party"),

        // See every window there is.
        new("explore",
            (b, w) => w.Env.Platforms.Count(p => p.Hwnd != IntPtr.Zero) >= 3 ? new[] { ("", b.P.Curiosity * (0.4f + b.f.Tastes.Of(Thing.Exploring)) * 0.9f) } : Array.Empty<(string, float)>(),
            (b, w, t) => M.Clamp01(b._explored.Count / 6f),
            (b, w, t) =>
            {
                var seg = w.Env.SupportAt(b.f.Base.X, b.f.Base.Y, b.f.GroundHwnd);
                return seg != null && b.Stamina > 0.35f && b.PickExplore(w.Env, seg, out var go) ? ("Explore somewhere new", 1.8f, go) : null;
            },
            (b, t) => "Visit every window"),
    };

    static AmbitionDef? DefOf(string key) => Ambitions.FirstOrDefault(a => a.Key == key);

    /// <summary>For the tests: think about a dream right now.</summary>
    public void ThinkAboutDreams() { _dreamCheckAt = 0; _dreamCd = 0; }

    public string DreamTitle => Dream is { } d && DefOf(d.Key) is { } def ? def.Title(this, d.Target) : "";
    public string DreamStep => _dreamStep;

    /// <summary>Now and then: think about the dream (adopt one, notice progress, celebrate, or let it go).</summary>
    void UpdateAmbition(World w)
    {
        if (_t0 < _dreamCheckAt || f.Visitor != VisitorKind.None || Baby) return;
        // A cursor hunter has only one thing on its mind.
        if (f.Hunter) { if (Dream != null) { Dream = null; _dreamStep = ""; } return; }
        _dreamCheckAt = _t0 + 5;
        if (Dream == null)
        {
            if (_t0 < _dreamCd || Asleep || InFight) return;
            AdoptAmbition(w);
            return;
        }
        var def = DefOf(Dream.Key);
        if (Dream.Key == "party" && _g == G.UseItem && _verb == Verb.Dance && _item is { } music)
            Dream.Count = Math.Max(Dream.Count, w.Figures.Count(o => o != f && o.Brain._partyHost == f && Vector2.Distance(o.Base, music.Pos) < 260 * S));
        float p = def?.Progress(this, w, Dream.Target) ?? -1;
        if (def == null || p < 0) { GiveUpDream("they're gone", w); return; }
        if (p > Dream.Progress + 0.01f) { Dream.Progress = p; Dream.MovedAt = _t0; }
        if (Dream.Milestone == 0 && Dream.Progress >= 0.5f)
        {
            Dream.Milestone = 1;
            Flash(Manpu.Sparkles, 2.5f);
            Write("dream:half:" + Dream.Key + Dream.Target, V($"Halfway there: {DreamTitle.ToLowerInvariant()}.", $"HALFWAY!!! {DreamTitle}!", $"Getting there. {DreamTitle}.", $"I'm halfway… {DreamTitle.ToLowerInvariant()}!", $"Half the road walked: {DreamTitle.ToLowerInvariant()}."), "★", 1e9f);
        }
        if (Dream.Progress >= 0.999f) { DreamComeTrue(w); return; }
        if (_t0 - Dream.MovedAt > 1800) GiveUpDream("", w);
    }

    void AdoptAmbition(World w)
    {
        var cands = new List<(AmbitionDef def, string target, float score)>();
        foreach (var def in Ambitions)
            foreach (var (target, score) in def.Candidates(this, w))
            {
                if (score <= 0.05f || DreamsDone.Contains(def.Key + ":" + target) && rng.NextDouble() < 0.8) continue;
                // Not everyone in town chasing the same thing.
                int same = w.Figures.Count(o => o != f && o.Brain.Dream is { } od && od.Key == def.Key && od.Target == target);
                cands.Add((def, target, score / (1 + same * 1.5f)));
            }
        if (cands.Count == 0) { _dreamCd = _t0 + 180; return; }
        var top = cands.OrderByDescending(c => c.score).Take(3).ToList();
        float roll = rng.Range(0, top.Sum(c => c.score));
        var pick = top[^1];
        foreach (var c in top) { roll -= c.score; if (roll <= 0) { pick = c; break; } }
        Dream = new Ambition { Key = pick.def.Key, Target = pick.target, Since = DateTime.Now, MovedAt = _t0 };
        DecisionsAtDream = Decisions; DreamOffers = 0; DreamSteps = 0;
        Dream.Progress = MathF.Max(0, pick.def.Progress(this, w, pick.target));
        string title = DreamTitle;
        f.Emote(V($"I'm going to {title.ToLowerInvariant()}!", $"NEW DREAM: {title.ToUpperInvariant()}!!", $"Fine. I'll {title.ToLowerInvariant()}.", $"maybe I could… {title.ToLowerInvariant()}?", $"I dream of this: {title.ToLowerInvariant()}."), 2.2f);
        Flash(Manpu.Sparkles, 2);
        Write("dream:start:" + Dream.Key + Dream.Target, V($"I've decided: I want to {title.ToLowerInvariant()}.", $"NEW GOAL!!! {title}!", $"Goal: {title.ToLowerInvariant()}. Don't make it a thing.", $"I have a secret dream… to {title.ToLowerInvariant()}.", $"A new path: {title.ToLowerInvariant()}."), "★", 600);
    }

    void DreamComeTrue(World w)
    {
        string title = DreamTitle;
        DreamsDone.Add(Dream!.Key + ":" + Dream.Target);
        if (DreamsDone.Count > 30) DreamsDone.RemoveAt(0);
        Dream = null;
        _dreamStep = "";
        _dreamCd = _t0 + rng.Range(180, 420);
        Flash(Manpu.Sparkles, 4);
        Cheered(0.6f);
        if (f.Mode == Mode.Control && f.Grounded && _g is G.Idle or G.Walk or G.SitFloor or G.Watch) Go(G.Cheer, 1.4f);
        f.Emote(V("I did it!!", "I DID IT!!! DREAM COME TRUE!!!", "Done. Told you.", "I… actually did it…", "It came true."), 2.4f);
        Write("dream:done:" + title, V($"Dream come true: {title.ToLowerInvariant()}!", $"I DID IT!!! {title.ToUpperInvariant()}!!!", $"Did it. {title}. Next.", $"I really did it… {title.ToLowerInvariant()}. I'm so happy.", $"The dream is real: {title.ToLowerInvariant()}."), "★", 0);
    }

    void GiveUpDream(string why, World w)
    {
        string title = DreamTitle;
        Dream = null;
        _dreamStep = "";
        _dreamCd = _t0 + rng.Range(240, 600);
        Flash(Manpu.Gloom, 4);
        Write("dream:quit:" + title, V($"Gave up on wanting to {title.ToLowerInvariant()}{(why.Length > 0 ? " (" + why + ")" : "")}.", $"Okay, new plan! Not going to {title.ToLowerInvariant()} after all.", $"Forget it. {title}. Waste of time.", $"I don't think I can {title.ToLowerInvariant()}…", $"Some dreams drift away: {title.ToLowerInvariant()}."), "☁", 0);
    }

    /// <summary>The next step towards the dream, offered alongside everything else they could do.</summary>
    void AmbitionOptions(World w, OptionList opts)
    {
        if (Dream == null || DefOf(Dream.Key) is not { } def) return;
        if (def.Step(this, w, Dream.Target) is not { } step)
        {
            // No way forward at all, time after time: let it go (and say why).
            if (Stamina > 0.4f && Hunger < 0.6f && ++_noStep >= 8) { _noStep = 0; GiveUpDream(V("no way to, right now", "can't, for now!", "pointless", "I couldn't find a way…", "the way is closed"), w); }
            return;
        }
        _noStep = 0;
        DreamOffers++;
        // Keener the fresher the dream and the better they feel; needs still come first.
        // A dream that keeps being put off pulls harder each time (until they get round to it).
        int waited = Math.Max(0, DreamOffers - DreamSteps * 3);
        float drive = (0.8f + Joy * 0.4f) * (1 + MathF.Min(waited, 12) * 0.18f) * (Hunger > 0.75f || Stamina < 0.2f ? 0.2f : 1);
        string label = step.label;
        opts.Add(step.weight * drive, () => { _dreamStep = label; DreamSteps++; step.act(); }, label);
    }

    /// <summary>Deliberate practice for the dream counts for more.</summary>
    float PracticeBoost(SkillKind k) => Dream is { Key: "skill" } d && d.Target == k.ToString() ? 1.8f : 1;

    // ---------------- making up ----------------

    float _apologyAt;

    void GoApologise(Figure o, World w)
    {
        _apologyAt = _t0 + 90;
        float side = MathF.Sign(f.Base.X - o.Base.X); if (side == 0) side = 1;
        Navigate(() => o.Mode is Mode.Control && w.Figures.Contains(o) ? o.Base + new Vector2(side * 30 * S, 0) : null, 8 * S, false, () =>
        {
            FaceTo(o.Base.X);
            f.Emote(V($"sorry about before, {o.Name}…", $"{o.Name}! I'm SORRY! Friends?", $"…sorry. there. I said it.", $"s-sorry, {o.Name}…", $"Can we start again, {o.Name}?"), 2);
            Flash(Manpu.SweatDrop, 2);
            AddAffinity(o, 0.12f);
            float forgive = 0.3f + (1 - o.Brain.P.Aggression) * 0.5f + o.Brain.P.Sociability * 0.2f;
            if (rng.NextDouble() < forgive)
            {
                o.Brain.AddAffinity(f, 0.15f);
                o.Emote(o.Brain.V("okay. friends.", "FRIENDS AGAIN!", "…fine.", "o-okay…", "Let's begin again."), 1.6f);
            }
            else { o.Emote("hmph", 1.2f); o.Brain.AddAffinity(f, 0.03f); }
            Go(G.Idle, 2);
        }, WalkPurpose.Social);
        _navAbout = o;
    }

    // ---------------- parties ----------------

    Figure? _partyHost;
    Vector2 _partyAt;
    float _partyUntil;

    /// <summary>The party plan: go round inviting friends (those not asked yet), then go to the music and dance;
    /// guests turning up count towards it.</summary>
    (string, float, Action)? PartyStep(World w)
    {
        var radio = w.Items.Where(i => i.Def.Verbs.Contains(Verb.Dance) && i.OnGround && !Unreachable(i)).OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
        if (radio == null) return null;
        var asked = w.Figures.Where(o => o.Brain._partyHost == f && o.Brain._partyUntil > o.Brain._t0).ToList();
        var next = w.Figures.Where(o => o != f && o.Mode == Mode.Control && AffinityWith(o) > 0.2f && !asked.Contains(o) && !Unreachable(o) && o.Visitor == VisitorKind.None)
                            .OrderByDescending(o => AffinityWith(o)).FirstOrDefault();
        if (next != null && asked.Count < 4)
            return ($"Invite {next.Name} to a party", 2.2f, () =>
            {
                float side = MathF.Sign(f.Base.X - next.Base.X); if (side == 0) side = 1;
                Navigate(() => next.Mode is Mode.Control && w.Figures.Contains(next) ? next.Base + new Vector2(side * 28 * S, 0) : null, 8 * S, false, () =>
                {
                    FaceTo(next.Base.X);
                    f.Emote(V($"party at the {radio.Def.Name.ToLowerInvariant()}! come!", "PARTY!!! YOU'RE INVITED!!!", "Party. Be there.", $"w-would you come to my party, {next.Name}?", "Join me where the music plays."), 2);
                    next.Brain.Invited(f, radio.Pos);
                    Go(G.Idle, 1.5f);
                }, WalkPurpose.Social);
                _navAbout = next;
            });
        // Everyone's asked: off to the music.
        int came = w.Figures.Count(o => o != f && o.Brain._partyHost == f && Vector2.Distance(o.Base, radio.Pos) < 220 * S);
        if (Dream != null) Dream.Count = Math.Max(Dream.Count, came);
        return ("Host the party", 2.4f, () => { f.Emote(V("let's party!", "PARTY TIME!!!", "Party. Now.", "I hope people come…", "Let the night begin."), 1.6f); UseItem(radio, Verb.Dance, w); });
    }

    /// <summary>Someone's invited us to their party.</summary>
    void Invited(Figure host, Vector2 at)
    {
        _partyHost = host; _partyAt = at; _partyUntil = _t0 + 240;
        f.Emote(AffinityWith(host) > 0.3f ? V("yes!!", "I'LL BE THERE!!", "…maybe.", "o-okay!", "I'll come.") : V("hm, maybe", "ooh!", "eh.", "um…"), 1.4f);
        Write("invited:" + host.Name, V($"{host.Name} invited me to a party.", $"{host.Name} invited me to a PARTY!!", $"{host.Name}'s throwing a party. We'll see.", $"{host.Name} invited me to their party… me!"), "♪", 600);
    }

    void GuestOptions(World w, OptionList opts)
    {
        if (_partyHost is not { } host || _t0 > _partyUntil || !w.Figures.Contains(host)) { _partyHost = null; return; }
        if (Vector2.Distance(f.Base, _partyAt) < 160 * S) return;
        var radio = w.Items.Where(i => i.Def.Verbs.Contains(Verb.Dance) && i.OnGround).OrderBy(i => Vector2.Distance(i.Pos, _partyAt)).FirstOrDefault();
        if (radio == null) return;
        float keen = 1.2f + AffinityWith(host) * 2 + P.Sociability;
        opts.Add(keen, () => { f.Emote("party!", 1); UseItem(radio, Verb.Dance, w); }, $"Go to {host.Name}'s party");
    }

    /// <summary>Somewhere new reached (for the explorer's dream).</summary>
    void NoteExplored(Platform p) { if (_explored.Count < 64) _explored.Add($"{p.Hwnd}:{(int)p.Y / 8}"); }
}
