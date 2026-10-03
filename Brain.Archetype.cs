using System.Numerics;

namespace Doodlefolk;

/// <summary>Character archetypes on top of the six traits: the familiar types from anime and manga, played gently.
/// Tsundere: prickly to the ones they like, flustered and denying it. Yandere: devoted to one person, clingy and
/// jealous (never violent here: they hover, glare and cut in). Kuudere: cool and quiet, few words, caring in deeds.
/// Dandere: shy and silent with strangers, chatty with close friends. Deredere: sweet to everyone. Genki: loud,
/// bouncy and always up for anything.</summary>
enum Archetype { None, Tsundere, Yandere, Kuudere, Dandere, Deredere, Genki }

static class Archetypes
{
    public static readonly (Archetype Kind, string Name, string Blurb)[] All =
    {
        (Archetype.None, "None", "Just themselves."),
        (Archetype.Tsundere, "Tsundere", "Prickly with the ones they like, then flustered about it. \"It's not like I like you!\""),
        (Archetype.Yandere, "Yandere", "Devoted to one special person: clingy, jealous, always nearby (never violent)."),
        (Archetype.Kuudere, "Kuudere", "Cool and quiet. Few words, but they show they care."),
        (Archetype.Dandere, "Dandere", "Silent and shy with strangers, chatty with close friends."),
        (Archetype.Deredere, "Deredere", "Sweet and loving to everyone."),
        (Archetype.Genki, "Genki", "Loud, bouncy, full of energy. LET'S GO!"),
    };

    /// <summary>Some newcomers have one (most are just themselves), nudged by their traits.</summary>
    public static Archetype Roll(Personality p, Random r)
    {
        if (r.NextDouble() > 0.4) return Archetype.None;
        var w = new (float, Archetype)[]
        {
            (0.6f + p.Aggression, Archetype.Tsundere),
            (0.35f + p.Sociability * 0.4f, Archetype.Yandere),
            (0.5f + (1 - p.Playfulness) + (1 - p.Sociability) * 0.5f, Archetype.Kuudere),
            (0.5f + (1 - p.Bravery) + (1 - p.Sociability) * 0.5f, Archetype.Dandere),
            (0.5f + p.Sociability + (1 - p.Aggression) * 0.5f, Archetype.Deredere),
            (0.5f + p.Energy + p.Playfulness * 0.5f, Archetype.Genki),
        };
        float roll = (float)r.NextDouble() * w.Sum(x => x.Item1);
        foreach (var (wt, k) in w) { roll -= wt; if (roll <= 0) return k; }
        return Archetype.Genki;
    }

    public static string Name(Archetype a) => All.First(x => x.Kind == a).Name;
}

sealed partial class Brain
{
    Archetype A => f.Archetype;
    float _jealous, _archCd, _flusterCd;
    Figure? _jealousOf;

    /// <summary>For the tests: who they're jealous of, and who they're fighting (if anyone).</summary>
    public (Figure? jealousOf, Figure? foe) Jealousy => (_jealousOf, InFight ? _foe : null);

    /// <summary>The one a yandere (or anyone) is most devoted to: their sweetheart, else their crush, else their
    /// closest friend if close enough.</summary>
    public Figure? Beloved(World w)
    {
        if (Sweetheart(w) is { } sh) return sh;
        if (Crush(w) is { } c) return c;
        Figure? best = null;
        float bv = 0.6f;
        foreach (var o in w.Figures) if (o != f && !o.Dead && o.Visitor == VisitorKind.None && AffinityWith(o) > bv) { bv = AffinityWith(o); best = o; }
        return best;
    }

    /// <summary>A line in character for a moment, or null to keep the usual one.</summary>
    string? Says(string moment, Figure? o, World w)
    {
        bool close = o != null && (AffinityWith(o) > 0.35f || LoveFor(o) > 0.3f || Dating(o));
        bool dear = o != null && (o == Beloved(w));
        string Pick(params string[] lines) => lines[rng.Next(lines.Length)];
        switch (A)
        {
            case Archetype.Tsundere:
                return moment switch
                {
                    "greet" when dear => Pick("W-what do YOU want?", "It's not like I was waiting for you!", "Hmph. You again.", "D-don't stand so close!"),
                    "greet" when close => Pick("Hmph. Hi, I guess.", "Oh. It's you.", "Don't get used to this."),
                    "greet" => Pick("Hmph.", "What.", "…"),
                    "thanks" => Pick("D-don't get the wrong idea!", "…thanks. Whatever.", "I didn't NEED help! …but thanks.", "It's not like I'm happy or anything!"),
                    "near" when dear => Pick("B-baka…", "Wh-why are you looking at me?!", "It's NOT like I like you!", "Hmph!"),
                    "win" => Pick("Of course I won.", "Did you see that?! …not that I care."),
                    "lose" => Pick("I let you win!", "Hmph! Rematch!"),
                    _ => null,
                };
            case Archetype.Yandere:
                return moment switch
                {
                    "greet" when dear => Pick("There you are~ ♥", "I was looking everywhere for you ♥", "Just you and me, okay? ♥", "Don't go anywhere ♥"),
                    "greet" => Pick("Hi~", "Have you seen " + (Beloved(w)?.Name ?? "them") + "?", "Hello ♥"),
                    "thanks" when dear => Pick("You'd do anything for me ♥", "I'll treasure this forever ♥"),
                    "thanks" => Pick("Thank you~", "How sweet ♥"),
                    "jealous" => Pick("Who's THAT?", "…why are you talking to them?", "You're mine… I-I mean, my friend! ♥", "I'm right HERE, you know."),
                    "glare" => Pick("…", "Stay away from them.", "Mine."),
                    "near" when dear => Pick("♥", "Together forever ♥", "Hehe~ ♥"),
                    _ => null,
                };
            case Archetype.Kuudere:
                return moment switch
                {
                    "greet" when close => Pick("…hi.", "Hm. You.", "(nods)"),
                    "greet" => Pick("…", "Hm.", "(nods)"),
                    "thanks" => Pick("…Thank you. I'll keep it.", "Appreciated.", "…Noted. Thanks."),
                    "near" when dear => Pick("…", "Stay a while."),
                    "win" => Pick("As expected.", "Hm."),
                    "lose" => Pick("Interesting.", "…Next time."),
                    _ => null,
                };
            case Archetype.Dandere:
                return moment switch
                {
                    "greet" when close => Pick("Hi!! I missed you!", "Oh! It's you! Hi hi!", "I was hoping you'd come!"),
                    "greet" => Pick("…", "u-um…", "h-hi…", "…!"),
                    "thanks" when close => Pick("Thank you so much!!", "You're the best!"),
                    "thanks" => Pick("th-thank you…", "…!"),
                    "near" when dear => Pick("…♥", "(hides)"),
                    _ => null,
                };
            case Archetype.Deredere:
                return moment switch
                {
                    "greet" => Pick("Hi hi! ♥", "Hello friend! ♥", "Yay, it's you!", "Hug? ♥"),
                    "thanks" => Pick("You're the best! ♥", "Aww, thank you!! ♥"),
                    "near" when dear => Pick("♥♥", "I like you lots ♥"),
                    "win" => Pick("Yay! Good game everyone! ♥"),
                    "lose" => Pick("You were amazing! ♥"),
                    _ => null,
                };
            case Archetype.Genki:
                return moment switch
                {
                    "greet" => Pick("HEY HEY HEY!", "LET'S GOOO!", "Race you!", "HI!! WHAT ARE WE DOING?!"),
                    "thanks" => Pick("WOOHOO! THANKS!", "BEST DAY EVER!"),
                    "win" => Pick("YEAHHH!!", "UNSTOPPABLE!"),
                    "lose" => Pick("AGAIN! AGAIN!", "So close!! One more!"),
                    _ => null,
                };
        }
        return null;
    }

    /// <summary>Say it in character, if there's something to say (otherwise the fallback, if any).</summary>
    void SayInCharacter(string moment, Figure? o, World w, string? fallback = null, float dur = 1.6f)
    {
        switch (moment)
        {
            case "win": Flash(Manpu.Sparkles, 2.5f); break;
            case "lose": Flash(A == Archetype.Tsundere ? Manpu.Vein : Manpu.Gloom, 3); break;
            case "jealous" or "glare": Flash(Manpu.Vein, 3); break;
            case "near" when A is Archetype.Tsundere or Archetype.Dandere: Flash(Manpu.SweatDrop, 2.5f); break;
            case "thanks" when A == Archetype.Tsundere: Flash(Manpu.SweatDrop, 2); break;
        }
        var line = Says(moment, o, w) ?? fallback;
        if (line != null) f.Emote(line, dur);
    }

    /// <summary>How the archetype tilts a choice (on top of the traits).</summary>
    float ArchetypeTilt(string label)
    {
        if (A == Archetype.None) return 1;
        string k = ActivityKey(label);
        bool loud = LoudKinds.Any(q => k.StartsWith(q)), quiet = QuietKinds.Any(q => k.StartsWith(q));
        return A switch
        {
            Archetype.Genki => loud ? 1.45f : quiet ? 0.7f : k.StartsWith("hang out") || k.StartsWith("sit") ? 0.6f : 1.1f,
            Archetype.Kuudere => loud ? 0.75f : quiet || k.StartsWith("sit") || k.StartsWith("hang out") ? 1.3f : 1,
            Archetype.Dandere => k.StartsWith("start a") ? 0.4f : quiet ? 1.25f : 1,
            Archetype.Deredere => k.StartsWith("hang out with") || k.StartsWith("start a") ? (k.StartsWith("start a") ? 0.3f : 1.4f) : 1,
            Archetype.Tsundere => k.StartsWith("start a") ? 1.2f : 1,
            Archetype.Yandere => k.StartsWith("hang out with") ? 1.5f : 1,
            _ => 1,
        };
    }

    /// <summary>Who they'd rather seek out: a yandere their beloved, a dandere only close friends, a deredere anyone.</summary>
    float ArchetypeSocialTilt(Figure o, World w)
    {
        float a = AffinityWith(o);
        return A switch
        {
            Archetype.Yandere => o == Beloved(w) ? 4 : 0.6f,
            Archetype.Dandere => a > 0.4f ? 1.8f : 0.35f,
            Archetype.Deredere => 1.3f,
            Archetype.Kuudere => a > 0.4f ? 1.2f : 0.6f,
            Archetype.Tsundere => o == Beloved(w) ? 1.6f : 1,   // says it's annoying; keeps turning up anyway
            _ => 1,
        };
    }

    /// <summary>A word in character on top of an emote: a kuudere swallows most exclamations, a genki adds some.</summary>
    public string Flavour(string text)
    {
        switch (A)
        {
            case Archetype.Kuudere when text is "!" or "!!" or "♪" or "♫" or "ha" or "★" && rng.NextDouble() < 0.6: return "…";
            case Archetype.Genki when text.Length > 2 && char.IsLetter(text[^1]) && rng.NextDouble() < 0.6: return text.ToUpperInvariant() + "!";
            case Archetype.Genki when text == "♪" && rng.NextDouble() < 0.4: return "♪♪!";
        }
        return text;
    }

    /// <summary>Twice a second: the yandere keeps an eye on their beloved; the tsundere gets flustered near theirs.</summary>
    void UpdateArchetype(float dt, World w)
    {
        if (A == Archetype.None || f.Mode != Mode.Control) return;
        _archCd -= dt; _flusterCd -= dt;
        if (_archCd > 0) return;
        _archCd = 0.5f;
        var dear = Beloved(w);
        if (dear == null) { _jealous = MathF.Max(0, _jealous - 0.05f); return; }
        float d = Vector2.Distance(dear.Base, f.Base);
        switch (A)
        {
            case Archetype.Yandere:
            {
                // Someone else has their attention: jealousy builds (faster the closer that other one is to them).
                var other = dear.Brain._partner;
                bool busyWithOther = other != null && other != f && dear.Brain._g is G.Chat or G.DanceWith or G.HighFive or G.SitWith or G.Follow;
                _jealous = busyWithOther ? M.Clamp01(_jealous + 0.12f) : MathF.Max(0, _jealous - 0.04f);
                if (busyWithOther) _jealousOf = other;
                if (_jealous > 0.6f && _jealousOf != null && !Engaged && _g is G.Idle or G.Walk or G.SitFloor or G.SitEdge or G.Watch && !InFight && Stamina > 0.2f)
                {
                    _jealous = 0.2f;
                    var rival = _jealousOf;
                    SayInCharacter("jealous", dear, w, null, 2);
                    AddAffinity(rival, -0.04f);
                    Write("jealous:" + rival.Name, V($"Saw {dear.Name} with {rival.Name}. I don't like it.", $"{dear.Name} was talking to {rival.Name}. Fine. FINE. ♥", $"{rival.Name} keeps hanging around {dear.Name}.", $"{dear.Name} was with {rival.Name}… I watched from behind a window.", $"{dear.Name} and {rival.Name}. I'll be closer next time."), "♥", 900);
                    // Cut in: go and stand right by them (and glare at the other one).
                    Navigate(() => w.Figures.Contains(dear) && dear.Mode == Mode.Control ? dear.Base + new Vector2(MathF.Sign(f.Base.X - dear.Base.X + 0.1f) * 24 * S, 0) : null, 8 * S, true, () =>
                    {
                        FaceTo(rival.Base.X);
                        SayInCharacter("glare", rival, w, "…", 1.6f);
                        Go(G.Idle, 2.5f);
                    }, WalkPurpose.Social);
                    _navAbout = dear;
                }
                else if (d < 140 * S && rng.NextDouble() < 0.04) SayInCharacter("near", dear, w);
                break;
            }
            case Archetype.Tsundere:
                // Near the one they like: flustered, a denial, and a look away.
                if (d < 140 * S && _flusterCd <= 0 && LoveFor(dear) + AffinityWith(dear) > 0.6f && rng.NextDouble() < 0.12)
                {
                    _flusterCd = 25;
                    f.Blush = MathF.Max(f.Blush, 0.8f);
                    SayInCharacter("near", dear, w);
                    if (_g is G.Idle or G.Watch) { f.Facing = -MathF.Sign(dear.Base.X - f.Base.X) is var s && s != 0 ? (int)s : f.Facing; f.KeepFacing = true; }
                }
                break;
            case Archetype.Dandere or Archetype.Deredere or Archetype.Kuudere:
                if (d < 120 * S && _flusterCd <= 0 && rng.NextDouble() < 0.03) { _flusterCd = 40; SayInCharacter("near", dear, w); }
                break;
        }
    }
}
