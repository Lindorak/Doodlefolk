using System.Numerics;

namespace StickFight;

/// <summary>Homes (a bed, tent or sleeping bag a figure has made its own) and families (couples who've been together a
/// long while can have a little one, who grows up over a few hours of play).</summary>
sealed partial class Brain
{
    // ---------------- homes ----------------

    readonly Dictionary<Item, int> _napsOn = new();

    public Item? Home(World w) => w.Items.FirstOrDefault(i => i.OwnerId == f.Id);

    /// <summary>Things a figure can call home: anything to sleep on, or to crawl inside.</summary>
    public static bool HomeKind(Item it) => it.Def.Verbs.Contains(Verb.Lie) || (it.Def.Verbs.Contains(Verb.Hide) && it.Def.W >= 30);

    /// <summary>Napped somewhere: the second good nap in the same place and it's home.</summary>
    void NappedOn(Item it, World w)
    {
        if (!HomeKind(it) || it.OwnerId != 0 || f.Hunter || Home(w) != null) return;
        _napsOn[it] = _napsOn.GetValueOrDefault(it) + 1;
        if (_napsOn[it] >= 2) ClaimHome(it, w);
    }

    public void ClaimHome(Item? it, World w)
    {
        foreach (var old in w.Items) if (old.OwnerId == f.Id) old.OwnerId = 0;
        if (it == null) return;
        it.OwnerId = f.Id;
        it.OwnerName = f.Name;
        it.OwnerColour = f.Color;
        string thing = it.Def.Name.ToLowerInvariant();
        f.Emote(V("home sweet home!", "MY HOUSE!!!", "mine now.", "a little home…", "a place of my own"), 1.6f);
        Write("home", V($"Moved into the {thing}. Home sweet home.", $"I have a HOUSE! It's the {thing}!!", $"The {thing} is mine now.", $"I made the {thing} my home. It feels safe.", $"The {thing}: my small kingdom."), "♥", 3600);
    }

    /// <summary>How it feels about using this thing: its own home most of all; somebody else's only if they're close.</summary>
    float HomeFactor(Item it, Verb v, World w)
    {
        if (it.OwnerId == 0) return 1;
        if (it.OwnerId == f.Id) return v is Verb.Lie or Verb.Hide or Verb.Sit ? 2.6f : 1.3f;
        var owner = w.Figures.FirstOrDefault(o => o.Id == it.OwnerId);
        if (owner == null) return 1;
        if (Dating(owner) || ParentIds.Contains(owner.Id) || owner.Brain.ParentIds.Contains(f.Id) || AffinityWith(owner) > 0.45f) return 1;
        return 0.25f;
    }

    float _visitCd;

    void HomeOptions(World w, OptionList opts)
    {
        if (f.Hunter) return;
        // Going home: at night, when sad or scared, or just to rest.
        if (Home(w) is { } home && Vector2.Distance(home.Pos, f.Base) > 220 * S && home.Free && home.OnGround && !Unreachable(home))
        {
            float want = (0.15f + w.Night * 1.1f) * (0.4f + (1 - Stamina)) + Sadness * 0.4f + Fear * 0.8f;
            var v = Stamina < 0.5f && home.Def.Verbs.Contains(Verb.Lie) ? Verb.Lie : home.Def.Verbs.Contains(Verb.Hide) ? Verb.Hide : home.Def.Verbs.Contains(Verb.Sit) ? Verb.Sit : Verb.Lie;
            opts.Add(want, () => UseItem(home, v, w), "Go home");
        }
        // Visiting: a friend (or sweetheart) who's home right now.
        if (_visitCd > _t0) return;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || o.Brain.Home(w) is not { } theirs) continue;
            if (Vector2.Distance(o.Base, theirs.Pos) > 160 * S || Vector2.Distance(o.Base, f.Base) < 200 * S || Vector2.Distance(o.Base, f.Base) > 2000 * S) continue;
            float a = AffinityWith(o) + (Dating(o) ? 0.5f : 0);
            if (a < 0.35f) continue;
            opts.Add((a * 0.5f + Loneliness * 0.6f) * (0.5f + P.Sociability), () =>
            {
                _visitCd = _t0 + 240;
                float side = MathF.Sign(f.Base.X - o.Base.X);
                Navigate(() => w.Figures.Contains(o) ? o.Base + new Vector2(side * 40 * S, 0) : null, 14 * S, false, () =>
                {
                    FaceTo(o.Base.X);
                    f.Emote(V("knock knock!", "KNOCK KNOCK!!", "it's me.", "um… hi! can I come in?", "I came to visit"), 1.3f);
                    o.Emote(o.Brain.V("come in!", "YAY, a visitor!!", "oh. it's you. fine.", "o-oh! come in", "welcome"), 1.3f);
                    AddAffinity(o, 0.04f); o.Brain.AddAffinity(f, 0.04f);
                    Loneliness = MathF.Max(0, Loneliness - 0.3f);
                    Write("visit:" + o.Name, V($"Visited {o.Name} at home.", $"Went to {o.Name}'s house!!", $"Dropped by {o.Name}'s place.", $"I visited {o.Name}. It was cosy.", $"Called on {o.Name} at home."), "♥", 900);
                    Go(G.SitFloor, rng.Range(8, 16));
                }, WalkPurpose.Social);
            }, $"Visit {o.Name} at home");
            break;
        }
    }

    // ---------------- families ----------------

    public readonly List<int> ParentIds = new();
    /// <summary>0 a newborn … 1 grown up. Babies grow over about four hours of play.</summary>
    public float Grown = 1;
    public float AdultSize = 1;
    public DateTime? LastBaby;
    public bool Baby => Grown < 1;
    const float GrowSeconds = 4 * 3600;
    float _familyCheck = 60;

    /// <summary>Family ties make for a warm start: parents and children, then siblings.</summary>
    float FamilyBond(Figure o) =>
        ParentIds.Contains(o.Id) || o.Brain.ParentIds.Contains(f.Id) ? 0.6f
        : ParentIds.Count > 0 && ParentIds.Any(o.Brain.ParentIds.Contains) ? 0.35f : 0;

    void UpdateFamily(float dt, World w)
    {
        if (Baby)
        {
            float before = Grown;
            Grown = MathF.Min(1, Grown + dt / GrowSeconds);
            if (before < 0.5f && Grown >= 0.5f) Write("half", V("I'm getting bigger!", "I'm SO BIG now!!", "Growing. Finally.", "I'm a little taller now…", "I'm growing like a weed."), "★", 1e9f);
            if (Grown >= 1) { f.Emote("all grown up!", 2); Write("grown", V("I'm all grown up!", "I'M A GROWN-UP!!!", "Grown up. About time.", "I'm grown up now… scary.", "The little one is gone. It's me now."), "★", 1e9f); }
        }
        _familyCheck -= dt;
        if (_familyCheck > 0) return;
        _familyCheck = 60;
        if (!w.Babies || Baby || f.Hunter || f.Mode != Mode.Control) return;
        var sw = Sweetheart(w);
        if (sw == null || sw.Brain.Baby || f.Id > sw.Id || sw.Mode != Mode.Control) return;   // one of the pair decides
        if (_t0 - _datingSince < 1200 || LoveFor(sw) < 0.75f || sw.Brain.LoveFor(f) < 0.75f || Joy < 0.4f || sw.Brain.Joy < 0.4f) return;
        if (LastBaby is DateTime lb && (DateTime.Now - lb).TotalHours < 3) return;
        if (w.Figures.Count >= World.MaxFigures) return;
        if (w.Figures.Count(o => o.Brain.ParentIds.Contains(f.Id) && o.Brain.ParentIds.Contains(sw.Id)) >= 2) return;
        if (Vector2.Distance(sw.Base, f.Base) > 400 * S || !f.Grounded || !sw.Grounded) return;
        if (rng.NextDouble() > 0.12) return;
        w.MakeBaby?.Invoke(f, sw);
    }

    /// <summary>A little one arrived: tell the diary (and everyone nearby).</summary>
    public void BabyArrived(Figure baby, Figure other)
    {
        LastBaby = DateTime.Now;
        f.Emote(V("♥ a baby!", "A BABY!!! ♥♥♥", "…a baby. huh. ♥", "o-oh… ♥ a baby", "♥ new life"), 2.2f);
        Cheered(0.6f);
        Write("baby:" + baby.Name, V($"{other.Name} and I had a baby! Welcome, {baby.Name}.", $"WE HAD A BABY!!! {baby.Name}!!! ♥", $"{other.Name} and I have a kid now. {baby.Name}. Cute, I guess.", $"A baby… {baby.Name}. I'm so happy I could cry.", $"{baby.Name} came into the world today, with {other.Name} and me."), "♥", 0);
    }

    public void WasBorn(Figure a, Figure b) =>
        Write("born", $"I was born today! My parents are {a.Name} and {b.Name}.", "★", 1e9f);

    static readonly string[] BabyWords = { "goo!", "ba!", "mama!", "papa!", "hehe", "♪", "da!", "bah!", "uh-oh", "wa!" };

    /// <summary>Babies: lots of naps, and toddling after a parent; no fights, ever.</summary>
    bool BabyChoose(World w)
    {
        if (!Baby) return false;
        var parent = w.Figures.Where(o => ParentIds.Contains(o.Id) && o.Mode == Mode.Control).OrderBy(o => Vector2.Distance(o.Base, f.Base)).FirstOrDefault();
        double r = rng.NextDouble();
        if (Stamina < 0.5f || r < 0.18 * (1 - Grown)) { Go(G.Sleep, rng.Range(10, 25)); return true; }
        if (parent != null && Vector2.Distance(parent.Base, f.Base) > 110 * S && r < 0.75 - Grown * 0.4)
        {
            float side = rng.NextDouble() < 0.5 ? -1 : 1;
            Navigate(() => w.Figures.Contains(parent) ? parent.Base + new Vector2(side * 36 * S, 0) : null, 20 * S, false, () =>
            {
                FaceTo(parent.Base.X);
                f.Emote(BabyWords[rng.Next(BabyWords.Length)], 1);
                Go(G.Idle, rng.Range(1.5f, 4));
            }, WalkPurpose.Social);
            return true;
        }
        if (r < 0.9 && f.CurrentEmote == null) f.Emote(BabyWords[rng.Next(BabyWords.Length)], 1);
        return false;
    }

    /// <summary>Parents check on their little ones now and then.</summary>
    void ParentOptions(World w, OptionList opts)
    {
        var kid = w.Figures.Where(o => o.Brain.Baby && o.Brain.ParentIds.Contains(f.Id) && o.Mode == Mode.Control).OrderBy(o => Vector2.Distance(o.Base, f.Base)).FirstOrDefault();
        if (kid == null || Vector2.Distance(kid.Base, f.Base) < 240 * S) return;
        opts.Add(0.35f + P.Sociability * 0.4f + Loneliness * 0.3f, () =>
        {
            float side = MathF.Sign(f.Base.X - kid.Base.X);
            Navigate(() => w.Figures.Contains(kid) ? kid.Base + new Vector2(side * 38 * S, 0) : null, 14 * S, false, () =>
            {
                FaceTo(kid.Base.X);
                f.Emote("♥", 1.2f);
                kid.Emote(BabyWords[rng.Next(BabyWords.Length)], 1);
                AddAffinity(kid, 0.02f); kid.Brain.AddAffinity(f, 0.03f);
                Go(G.Wave, 1.4f);
            }, WalkPurpose.Social);
        }, $"Check on {kid.Name}");
    }
}
