using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Romance, on top of friendship. Love (0..1) grows toward friends a figure is attracted to while they spend
/// time together, faster doing things side by side; hurting someone kills it. A crush makes them blush, seek the other
/// out and show off; enough love and they confess. A yes makes a couple (hand-holding, dates, defending each other,
/// jealousy when someone else flirts); love running out ends in a breakup and a while of heartbreak.</summary>
sealed partial class Brain
{
    /// <summary>Romantic feelings toward other figures, by id.</summary>
    public readonly Dictionary<int, float> Love = new();
    /// <summary>Who it's dating (figure id), 0 if single.</summary>
    public int SweetheartId;
    readonly Dictionary<int, float> _awkwardUntil = new();
    float _loveTick, _confessCd = 120, _showOffCd = 30, _heartbreakAt = -1000, _datingSince;
    Figure? _confessTo;

    public bool AttractedTo(Figure o) => o != f && (f.Attraction & Romance.Bit(o.Gender)) != 0;
    public float LoveFor(Figure o) => Love.TryGetValue(o.Id, out var v) ? v : 0;
    public bool Dating(Figure o) => SweetheartId != 0 && SweetheartId == o.Id;
    bool Awkward(Figure o) => _awkwardUntil.TryGetValue(o.Id, out var t) && _t0 < t;
    bool Heartbroken => _t0 - _heartbreakAt < 150;

    void AddLove(Figure o, float d)
    {
        if (d > 0 && !AttractedTo(o)) return;
        Love[o.Id] = M.Clamp01(LoveFor(o) + d);
    }

    public Figure? Sweetheart(World w) => SweetheartId == 0 ? null : w.Figures.FirstOrDefault(o => o.Id == SweetheartId);

    /// <summary>Whoever it has the strongest feelings for (if strong enough to count as a crush).</summary>
    public Figure? Crush(World w)
    {
        Figure? best = null;
        float bv = 0.45f;
        foreach (var o in w.Figures)
            if (o != f && !o.Dead && LoveFor(o) > bv) { bv = LoveFor(o); best = o; }
        return best;
    }

    void UpdateLove(float dt, World w)
    {
        _confessCd -= dt;
        _showOffCd -= dt;
        float blush = 0;
        if (w.Romance)
        {
            _loveTick -= dt;
            if (_loveTick <= 0) { _loveTick = 0.5f; TickLove(0.5f, w); }
            // Blushing near the one it likes (less so once they've been together a while).
            if (Crush(w) is { } c && f.Mode == Mode.Control && Vector2.Distance(c.Base, f.Base) < 150 * S)
                blush = LoveFor(c) * (Dating(c) && _t0 - _datingSince > 90 ? 0.35f : 1);
        }
        f.Blush += (blush - f.Blush) * MathF.Min(1, dt * 2.5f);
    }

    void TickLove(float dt, World w)
    {
        foreach (var o in w.Figures)
        {
            if (o == f || o.Dead) continue;
            float love = LoveFor(o);
            if (!AttractedTo(o)) { if (love > 0) Love[o.Id] = MathF.Max(0, love - dt * 0.01f); continue; }
            float d = Vector2.Distance(o.Base, f.Base);
            if (d < 320 * S)
            {
                float aff = AffinityWith(o);
                bool together = (_partner == o && _g is G.Chat or G.SitWith or G.DanceWith or G.Follow or G.HighFive)
                             || (_g == G.WatchScreen && o.Brain._g == G.WatchScreen) || (Match != null && Match == o.Brain.Match);
                float grow = aff > 0.2f
                    ? (aff - 0.2f + MathF.Max(0, f.Tastes.Similarity(o.Tastes)) * 0.3f) * (together ? 0.012f : 0.0015f) * (0.6f + P.Sociability)
                    : aff < -0.2f ? -0.01f : 0;
                if (Dating(o)) grow = MathF.Max(grow, together ? 0.008f : 0.001f);
                bool wasCrush = love > 0.45f;
                AddLove(o, grow * dt);
                if (!wasCrush && LoveFor(o) > 0.45f && !Dating(o)) DiaryCrush(o);
            }
            else if (love > 0 && !Dating(o)) Love[o.Id] = MathF.Max(0, love - dt * 0.0006f);   // out of sight, slowly out of mind
        }
        // Couples last while the love does.
        if (Sweetheart(w) is { } sh)
        {
            if (LoveFor(sh) < 0.25f || sh.Brain.LoveFor(f) < 0.25f) BreakUp(sh, w);
        }
        else SweetheartId = 0;
    }

    void RomanceOptions(World w, OptionList opts)
    {
        if (!w.Romance) return;
        var sh = Sweetheart(w);
        if (sh != null && sh.Mode == Mode.Control && !sh.Dead && Vector2.Distance(sh.Base, f.Base) < 2000 * S)
        {
            float love = LoveFor(sh);
            // Time together: a stroll hand in hand, or just going over to see them.
            opts.Category = $"Spend time with {sh.Name} ♥";
            opts.Add(((0.4f + love + Loneliness) * (0.6f + P.Sociability), () =>
            {
                if (rng.NextDouble() < 0.45 && sh.Brain._g is G.Walk or G.Idle) { _partner = sh; _initiator = true; Go(G.Follow, rng.Range(10, 25)); f.Emote("♥", 1); }
                else Approach(sh, w);
            }));
        }
        if (Heartbroken && Sadness > 0.25f)
            opts.Add(1.5f * Sadness, () => { Go(G.SitFloor, rng.Range(8, 18)); f.Emote("💔", 2); }, "Mope (heartbroken)");
        if (sh != null || Heartbroken) return;
        if (Crush(w) is not { } c || Awkward(c)) return;
        float l = LoveFor(c), dist = Vector2.Distance(c.Base, f.Base);
        // Showing off when they're watching.
        opts.Category = $"Show off for {c.Name}";
        if (_showOffCd <= 0 && Stamina > 0.4f && dist < 320 * S && c.Mode == Mode.Control)
            opts.Add((P.Playfulness * l * 1.2f, () =>
            {
                _showOffCd = rng.Range(25, 60);
                FaceTo(c.Base.X);
                Go(G.Trick, 3);
                f.RequestFlip(70 * S);
                f.Emote("★", 1);
            }));
        // Working up the nerve to say something.
        if (l > 0.7f && _confessCd <= 0 && c.Brain.SweetheartId == 0 && c.Mode == Mode.Control && !c.Brain.InFight)
            opts.Add((0.8f + l + P.Bravery) * 1.5f, () => GoConfess(c, w), $"Tell {c.Name} how they feel");
    }

    void GoConfess(Figure c, World w)
    {
        _confessTo = c;
        _confessCd = rng.Range(240, 420);
        float side = MathF.Sign(f.Base.X - c.Base.X);
        if (side == 0) side = 1;
        Navigate(() => w.Figures.Contains(c) && c.Mode is Mode.Control ? c.Base + new Vector2(side * 26 * S, 0) : null, 6 * S, false, () =>
        {
            Go(G.Confess, 3.2f);
            FaceTo(c.Base.X);
            f.Emote(P.Bravery > 0.65f ? "I like you! ♥" : rng.NextDouble() < 0.5 ? "um… ♥?" : "so, uh… ♥", 1.8f);
            c.Brain.BeingConfessedTo(f);
        }, WalkPurpose.Heart);
    }

    /// <summary>Someone's about to confess: stop and listen.</summary>
    void BeingConfessedTo(Figure from)
    {
        if (f.Mode != Mode.Control || !f.Grounded || InFight || _g is G.Sport or G.Hunt) return;
        Go(G.Idle, 3);
        FaceTo(from.Base.X);
        f.LookAt = from.Jt[J.Head];
    }

    void DoConfess(World w)
    {
        f.DesiredVX = 0;
        var c = _confessTo;
        if (c == null || !w.Figures.Contains(c)) { Go(G.Idle, 1); return; }
        FaceTo(c.Base.X);
        f.LookAt = c.Jt[J.Head];
        f.SetAction(_t < 1.6f ? Act.Talk : Act.Stand);
        if (_t >= 1.6f && _t - World.Dt < 1.6f)
        {
            if (c.Brain.AnswerConfession(f)) Together(c, w);
            else Rejected(c);
        }
        if (_t > _dur) { _confessTo = null; Go(G.Idle, 1.5f); }
    }

    bool AnswerConfession(Figure from)
    {
        bool can = f.Mode == Mode.Control && !InFight && AttractedTo(from) && SweetheartId == 0;
        bool yes = can && (LoveFor(from) > 0.4f || (AffinityWith(from) > 0.55f && rng.NextDouble() < 0.3f + P.Sociability * 0.4f));
        FaceTo(from.Base.X);
        if (yes) f.Emote(rng.NextDouble() < 0.5 ? "♥ yes!" : "me too! ♥", 1.8f);
        else f.Emote(!AttractedTo(from) || AffinityWith(from) > 0.3f ? "sorry… friends?" : "uh… no.", 1.8f);
        return yes;
    }

    void Together(Figure c, World w)
    {
        SweetheartId = c.Id;
        c.Brain.SweetheartId = f.Id;
        _datingSince = _t0;
        c.Brain._datingSince = c.Brain._t0;
        Love[c.Id] = MathF.Max(LoveFor(c), 0.75f);
        c.Brain.Love[f.Id] = MathF.Max(c.Brain.LoveFor(f), 0.7f);
        Cheered(0.6f);
        c.Brain.Cheered(0.6f);
        AddAffinity(c, 0.2f);
        c.Brain.AddAffinity(f, 0.2f);
        Vector2 mid = (f.Jt[J.Head] + c.Jt[J.Head]) * 0.5f - new Vector2(0, 10 * S);
        for (int i = 0; i < 6; i++) w.Fx.Spark(mid + new Vector2(rng.Range(-14, 14), rng.Range(-8, 8)) * S, S * 0.7f, w.Rng, 0.6f, new Color4(1, 0.45f, 0.65f, 1));
        World.Play(Sfx.Chime, mid, 0.45f);
        World.Log($"{f.Name} and {c.Name} are dating");
        DiaryConfessed(c, true);
        c.Brain.DiaryAskedOut(f, true);
        w.CoupleFormed(f, c);
    }

    void Rejected(Figure c)
    {
        DiaryConfessed(c, false);
        c.Brain.DiaryAskedOut(f, false);
        Saddened(0.35f);
        Love[c.Id] = MathF.Max(0, LoveFor(c) - 0.3f);
        _heartbreakAt = _t0 - 90;   // a short sting, not a full heartbreak
        _awkwardUntil[c.Id] = _t0 + 240;
        f.Emote(P.Bravery > 0.6f ? "oh… ok!" : "oh… right", 1.6f);
        _confessTo = null;
        // Get out of there.
        float away = MathF.Sign(f.Base.X - c.Base.X);
        WalkTo(f.Base.X + (away == 0 ? 1 : away) * rng.Range(250, 500) * S, true, () => Go(G.SitFloor, rng.Range(5, 10)));
    }

    void BreakUp(Figure sh, World w)
    {
        SweetheartId = 0;
        if (sh.Brain.SweetheartId == f.Id) sh.Brain.SweetheartId = 0;
        foreach (var (a, b) in new[] { (this, sh.Brain), (sh.Brain, this) })
        {
            a._heartbreakAt = a._t0;
            a._awkwardUntil[b.f.Id] = a._t0 + 600;
            a.Saddened(0.5f);
            a.AddAffinity(b.f, -0.2f);
            a.Love[b.f.Id] = MathF.Min(a.LoveFor(b.f), 0.2f);
            a.DiaryBreakup(b.f);
            if (a.f.Mode == Mode.Control) a.f.Emote("💔", 2);
        }
        World.Log($"{f.Name} and {sh.Name} broke up");
    }

    /// <summary>Hurt by someone: love takes a hit, a lot more if it was your sweetheart.</summary>
    void LoveHurt(Figure from, bool knockedDown)
    {
        if (from == f) return;
        AddLove(from, Dating(from) ? (knockedDown ? -0.3f : -0.12f) : (knockedDown ? -0.15f : -0.05f));
        if (Dating(from) && f.Mode == Mode.Control) f.Emote("💔?", 1.2f);
    }

    /// <summary>From <see cref="Saw"/>: things that matter because of who's dating whom. Returns true if it reacted.</summary>
    bool RomanceSaw(Figure actor, Figure target, SocialAct act, World w)
    {
        if (!w.Romance || SweetheartId == 0) return false;
        // Someone hurting my sweetheart: protect them.
        if (act == SocialAct.Hurt && Dating(target))
        {
            _witnessCd = 4;
            if (Rules.Enabled && P.Bravery > 0.35f && !InFight && !Dating(actor)) { f.Emote("hey! #@!", 1.2f); Engage(actor, false, w); }
            else { f.Emote("!!", 1); _glareAt = actor; Go(G.Annoyed, 1.4f); }
            return true;
        }
        // Someone sweet on my sweetheart being friendly (or my sweetheart being friendly to them): jealous.
        bool flirt = act != SocialAct.Hurt && ((Dating(target) && actor.Brain.LoveFor(target) > 0.35f) || (Dating(actor) && target.Brain.LoveFor(actor) > 0.35f));
        if (flirt && _witnessCd <= 0)
        {
            var rival = Dating(target) ? actor : target;
            _witnessCd = 10;
            AddAffinity(rival, -0.12f);
            Annoyance = M.Clamp01(Annoyance + 0.2f);
            f.Emote(P.Aggression > 0.55f ? "hmph! #@!" : "hmph!", 1.3f);
            if (Sweetheart(w) is { } sh && _g is G.Idle or G.Walk or G.SitFloor) Approach(sh, w);   // go and reclaim them
            return true;
        }
        return false;
    }

    /// <summary>Debug: go and confess to the crush right now.</summary>
    public string ForceConfess(World w)
    {
        if (Crush(w) is not { } c) return "no crush";
        TraceUntil = _t0 + 40;
        GoConfess(c, w);
        return $"going to confess to {c.Name}";
    }

    /// <summary>Debug: why it is or isn't about to confess.</summary>
    public string RomanceDebug(World w)
    {
        var c = Crush(w);
        return $"romance={w.Romance} sweetheart={SweetheartId} heartbroken={Heartbroken} crush={c?.Name} love={(c != null ? LoveFor(c) : 0):0.00} " +
               $"awkward={(c != null && Awkward(c))} confessCd={_confessCd:0} crushSweetheart={c?.Brain.SweetheartId} crushMode={c?.Mode} crushFight={c?.Brain.InFight} t0={_t0:0} hb={_heartbreakAt:0}";
    }

    /// <summary>Debug: make this figure fall for <paramref name="o"/> right now.</summary>
    public string ForceLove(Figure o, float v)
    {
        if (!AttractedTo(o)) return $"{f.Name} isn't attracted to {o.Name} ({o.Gender}; {Romance.Describe(f.Attraction)})";
        Love[o.Id] = M.Clamp01(v);
        _confessCd = 0;
        return $"{f.Name} love for {o.Name} = {v:0.00}";
    }
}

sealed partial class World
{
    public bool Romance = true;

    /// <summary>A new couple: friends nearby are happy for them; anyone else who had feelings for either one isn't.</summary>
    public void CoupleFormed(Figure a, Figure b)
    {
        foreach (var o in Figures)
        {
            if (o == a || o == b || o.Mode != Mode.Control || Vector2.Distance(o.Base, a.Base) > 900 * Scale) continue;
            if (o.Brain.LoveFor(a) > 0.45f || o.Brain.LoveFor(b) > 0.45f)
            {
                o.Brain.Saddened(0.25f);
                o.Brain.AddAffinity(o.Brain.LoveFor(a) > o.Brain.LoveFor(b) ? b : a, -0.15f);
                o.Emote("💔", 1.6f);
            }
            else if (o.Brain.AffinityWith(a) > 0.35f || o.Brain.AffinityWith(b) > 0.35f) o.Emote(Rng.NextDouble() < 0.5 ? "♥" : "aww", 1.3f);
        }
    }
}
