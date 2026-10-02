using System.Numerics;

namespace StickFight;

/// <summary>Likes and dislikes in action (Sims-style): tastes tilt choices, shared interests draw figures
/// together, they react to things they love or hate, and each figure has its own relationship with the user.</summary>
sealed partial class Brain
{
    float? _fondness;
    float _pettingT, _strokeT, _heightWorryCd, _likeSeenCd;
    readonly HashSet<int> _greetedBalls = new();

    /// <summary>How much this figure likes you (the user), -1..1. Separate from CursorTrust (fear vs comfort).</summary>
    public float UserFondness
    {
        get => _fondness ??= Math.Clamp(0.1f + (P.Sociability - 0.5f) * 0.4f + f.Tastes.Of(Thing.YourCursor) * 0.4f, -1, 1);
        set => _fondness = Math.Clamp(value, -1, 1);
    }

    // ---------------- how it remembers you ----------------

    public sealed class UserMemory { public string What = ""; public float Delta, At; }
    /// <summary>The last few things you did to it, newest last (shown in the Studio).</summary>
    public readonly List<UserMemory> Memories = new();
    public float Age => _t0;

    float FondBaseline => f.Hunter ? -1 : Math.Clamp(0.1f + (P.Sociability - 0.5f) * 0.4f + f.Tastes.Of(Thing.YourCursor) * 0.4f, -1, 1);

    /// <summary>Wipe the slate clean with you (debug / testing).</summary>
    public void Forgive()
    {
        _fondness = null;
        CursorTrust = 0.6f;
        Memories.Clear();
    }

    /// <summary>Change how it feels about you, and remember why.</summary>
    void FeelUser(float delta, string what)
    {
        UserFondness += delta;
        var last = Memories.Count > 0 ? Memories[^1] : null;
        if (last != null && last.What == what && _t0 - last.At < 90) { last.Delta += delta; last.At = _t0; return; }
        Memories.Add(new UserMemory { What = what, Delta = delta, At = _t0 });
        if (Memories.Count > 8) Memories.RemoveAt(0);
    }

    /// <summary>Feelings fade back toward its nature over many minutes; forgiving souls let go faster,
    /// grudge-holders (aggressive, unsociable) much slower.</summary>
    void DriftFondness(float dt)
    {
        if (_fondness is not float v) return;
        float forgive = 0.3f + P.Sociability * 0.5f + (1 - P.Aggression) * 0.5f;
        _fondness = v + (FondBaseline - v) * MathF.Min(1, 0.0007f * forgive * dt);
    }

    /// <summary>Someone it cares about (or can't stand) just got roughed up by you.</summary>
    public void WitnessUserHurt(Figure victim, float severity)
    {
        if (f.Mode != Mode.Control || _g == G.Sleep || victim == f) return;
        float a = AffinityWith(victim);
        if (a > 0.25f)
        {
            FeelUser(-0.12f * severity * a, $"Hurt {victim.Name}");
            if (P.Aggression > 0.5f && rng.NextDouble() < 0.5) { f.Emote("#@!", 1.2f); if (_g is G.Idle or G.Watch or G.SitFloor) Go(G.Annoyed, 1.4f); }
            else f.Emote("!", 1);
        }
        else if (a < -0.3f && P.Aggression > 0.45f)
        {
            FeelUser(0.04f * severity, $"Roughed up {victim.Name}");
            f.Emote("ha", 1);
        }
    }

    void TellWitnesses(World w, float severity)
    {
        foreach (var o in w.Figures)
            if (o != f && Vector2.Distance(o.Base, f.Base) < 900 * S) o.Brain.WitnessUserHurt(f, severity);
    }

    /// <summary>A ball you threw reached it.</summary>
    public void GotBallFromUser(Prop b)
    {
        float like = (Tastes.ForProp(b.Kind) is Thing k ? f.Tastes.Of(k) : 0) + f.Tastes.Of(Thing.PlayingBall);
        if (like < -0.4f) { FeelUser(-0.02f, "Threw a ball at them"); return; }
        FeelUser(0.03f + MathF.Max(0, like) * 0.03f, "Played ball with them");
        World.Current?.Witness(null, f, SocialAct.Kind, 0.4f);
        if (f.CurrentEmote == null) f.Emote(like > 0.5f ? "♥" : "♪", 0.9f);
    }

    /// <summary>Studio: you waved them over. What they do depends entirely on how they feel about you.</summary>
    public string CalledByUser(World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded || f.Climbing || InFight || _g == G.CursorFight) return $"{f.Name} is busy right now";
        float fond = UserFondness;
        if (_g == G.Sleep && fond < 0.4f) return $"{f.Name} is asleep";
        if (fond > 0.2f && CursorTrust > 0.3f)
        {
            f.Emote(fond > 0.6f ? "♥" : "!", 1.1f);
            ComeToCursor(w);
            return fond > 0.6f ? $"{f.Name} comes running!" : $"{f.Name} is on the way";
        }
        if (fond < -0.3f) { Snub(w.Cursor); return $"{f.Name} turns their back on you"; }
        if (CursorTrust < 0.3f) { f.Emote("…", 1); RunFromCursor(w, w.Cursor); return $"{f.Name} is too nervous"; }
        if (rng.NextDouble() < 0.45 + fond) { ComeToCursor(w); return $"{f.Name} wanders over"; }
        f.Emote("?", 1);
        Go(G.Watch, 8);
        return $"{f.Name} just looks at you";
    }

    bool _snub;

    /// <summary>The cold shoulder: arms crossed, facing away from your cursor.</summary>
    void Snub(Vector2 cur)
    {
        Go(G.Annoyed, rng.Range(2, 3.5f));
        _snub = true;
        f.Emote(P.Aggression > 0.5f ? "#@!" : "hmph", 1.3f);
        f.Facing = cur.X > f.Base.X ? -1 : 1;
    }

    /// <summary>Choice multiplier from an opinion: loved ~2.5x, neutral 1x, hated ~0.1x.</summary>
    float Taste(Thing t) => MathF.Max(0.1f, 1 + f.Tastes.Of(t) * 1.5f);

    /// <summary>Extra starting affinity from shared tastes and colour preferences.</summary>
    float TasteBond(Figure o)
    {
        float bond = f.Tastes.Similarity(o.Tastes) * 0.25f;
        string team = o.Team;
        if (team == f.Tastes.FavoriteColour) bond += 0.15f;
        if (team == f.Tastes.DislikedColour) bond -= 0.2f;
        return bond;
    }

    public string FeelingsAboutYou()
    {
        float fond = UserFondness, trust = CursorTrust;
        if (fond > 0.6f && trust > 0.5f) return "Adores you";
        if (fond > 0.25f) return trust < 0.3f ? "Likes you, but nervous" : "Likes you";
        if (fond < -0.4f) return P.Bravery < 0.45f ? "Scared of you" : "Can't stand you";
        if (trust < 0.25f) return "Wary of you";
        return "Neutral about you";
    }

    // ---------------- reacting to how the user treats it ----------------

    void FeelAboutBeingPickedUp()
    {
        float o = f.Tastes.Of(Thing.BeingPickedUp);
        if (o > 0.35f) { f.Emote("♪", 1); FeelUser(0.04f, "Picked them up (loved it)"); }
        else if (o < -0.35f) { f.Emote("#@!", 1); FeelUser(-0.08f, "Picked them up (hated it)"); CursorTrust = MathF.Max(0, CursorTrust - 0.05f); }
        else
        {
            FeelUser(-0.01f, "Picked them up");
            if (UserFondness < -0.5f) f.Emote("#@!", 1);   // "put me down!"
        }
    }

    /// <summary>Thrown around: thrill-seekers love it, others hate it.</summary>
    bool FeelAboutBeingThrown(float throwSpeed)
    {
        float o = f.Tastes.Of(Thing.BeingThrown);
        if (throwSpeed < 300 * S) return false;
        if (o > 0.35f)
        {
            f.Emote(rng.NextDouble() < 0.5 ? "!!" : "♪", 1.3f);
            FeelUser(0.08f, "Threw them (wheee!)");
            CursorTrust = MathF.Min(1, CursorTrust + 0.05f);
            Cheered(0.4f);
            Go(G.Cheer, 1);
            return true;   // "again!"
        }
        FeelUser(o < -0.35f ? -0.15f : -0.05f, o < -0.35f ? "Threw them around (hated that)" : "Threw them");
        if (o < -0.35f)
        {
            // Hates it: dusts itself off and glares at you (or cowers, if timid).
            Annoyance = M.Clamp01(Annoyance + 0.3f);
            CursorTrust = MathF.Max(0, CursorTrust - 0.15f);
            if (P.Bravery < 0.35f) { Fear = MathF.Max(Fear, 0.6f); f.Emote("!", 1.2f); Go(G.Startled, 1.2f); }
            else { f.Emote(P.Aggression > 0.4f ? "#@!" : "…", 1.4f); Go(G.Annoyed, 1.6f); }
            return true;
        }
        return false;
    }

    void FeelAboutPoke()
    {
        if (f.Tastes.Likes(Thing.YourCursor)) { FeelUser(0.02f, "Poked them (tickles!)"); f.Emote(rng.NextDouble() < 0.5 ? "♥" : "ha", 0.9f); }
        else FeelUser(f.Tastes.Dislikes(Thing.YourCursor) ? -0.06f : -0.02f, "Poked them");
    }

    /// <summary>Gentle hovering is like petting: cursor-lovers enjoy it.</summary>
    void FeelPetting(World w, float dt)
    {
        // Petting is a gentle stroke: the cursor moving slowly over them, not just resting where they walk.
        float cv = w.CursorVel.Length();
        bool stroking = w.Hover == f && cv > 20 * S && cv < 250 * S;
        _strokeT = stroking ? 0.4f : _strokeT - dt;
        bool petting = w.Hover == f && _strokeT > 0;
        _pettingT = petting ? _pettingT + dt : 0;
        if (!petting) return;
        float o = f.Tastes.Of(Thing.YourCursor);
        FeelUser(dt * (0.01f + MathF.Max(0, o) * 0.03f), "Petted them");
        if (o > 0.35f && _pettingT > 0.7f && f.CurrentEmote == null) { f.Emote("♥", 1.2f); Cheered(0.15f); }
        if (_pettingT > 0.7f && _pettingT - dt <= 0.7f) { w.Witness(null, f, SocialAct.Kind, 0.5f); if (o > -0.2f) DiaryPetted(); }
    }

    // ---------------- things it loves or hates nearby ----------------

    void NoticeTastes(World w)
    {
        _heightWorryCd -= 0.25f;
        _likeSeenCd -= 0.25f;
        if (_g is G.Busy or G.Sleep || f.Mode != Mode.Control) return;

        // New balls: a figure that loves that kind of ball gets excited and may go straight for it.
        foreach (var b in w.Props)
        {
            if (_greetedBalls.Contains(b.Id)) continue;
            if (Vector2.Distance(b.Pos, f.Base) > 900 * S) continue;
            _greetedBalls.Add(b.Id);
            var kind = Tastes.ForProp(b.Kind);
            float o = (kind is Thing k ? f.Tastes.Of(k) : 0) + f.Tastes.Of(Thing.PlayingBall) * 0.5f;
            if (o > 0.5f)
            {
                f.Emote("♥", 1.2f);
                Cheered(0.2f);
                FeelUser(0.03f, $"Gave them a {Prop.KindName(b.Kind).ToLowerInvariant()}");
                if (_g is G.Idle or G.Watch or G.SitFloor && Stamina > 0.3f) GoToBall(b, w, () => ChoosePlay(b, w, null));
            }
            else if (o < -0.4f) f.Emote("…", 1);
        }

        // Heights: up on a high window, some figures are thrilled and some want down.
        if (f.Grounded && _heightWorryCd <= 0)
        {
            var (_, _, top) = w.Env.BoundsAt(f.Base.X);
            var floor = w.Env.Platforms.Where(p => p.Solid && f.Base.X >= p.X1 && f.Base.X <= p.X2).Select(p => p.Y).DefaultIfEmpty(f.Base.Y).First();
            float height = M.Clamp01((floor - f.Base.Y) / MathF.Max(1, floor - top));
            float o = f.Tastes.Of(Thing.HighPlaces);
            if (height > 0.4f)
            {
                _heightWorryCd = 6;
                if (o < -0.35f)
                {
                    Fear = MathF.Max(Fear, 0.5f * height);
                    bool idleish = _g is G.Idle or G.SitFloor or G.SitEdge or G.Watch ||
                                   (_g == G.Walk && _purpose is WalkPurpose.Wander or WalkPurpose.Explore);
                    if (idleish && rng.NextDouble() < 0.85)
                    {
                        f.Emote("!", 1);
                        GoLower(w);
                    }
                }
                else if (o > 0.35f) Cheered(0.1f * height);
            }
        }

        // Liked / disliked colours, and figures sharing a passion.
        if (_likeSeenCd > 0 || _g != G.Idle) return;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || Vector2.Distance(o.Base, f.Base) > 250 * S) continue;
            _likeSeenCd = 8;
            if (o.Team == f.Tastes.DislikedColour && P.Aggression > 0.4f) { f.Emote("…", 1); _glareAt = o; Go(G.Annoyed, 1); return; }
            if (o.Team == f.Tastes.FavoriteColour && rng.NextDouble() < 0.4) { f.Emote("♥", 1); return; }
        }
    }

    /// <summary>Head for the lowest reachable platform (scared of heights).</summary>
    void GoLower(World w)
    {
        var env = w.Env;
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        // Somewhere lower that isn't hidden under this window: just past one of its edges.
        float margin = 25 * S;
        (Platform p, float x)? best = null;
        foreach (var p in env.Platforms)
        {
            if (p.Y <= seg.Y + 60 * S || p.X2 - p.X1 < 30 * S) continue;
            foreach (float x in new[] { seg.X1 - margin, seg.X2 + margin, p.X1 + 15 * S, p.X2 - 15 * S })
            {
                if (x < p.X1 + 10 * S || x > p.X2 - 10 * S || (x > seg.X1 - 5 * S && x < seg.X2 + 5 * S)) continue;
                if (best == null || MathF.Abs(x - f.Base.X) < MathF.Abs(best.Value.x - f.Base.X)) best = (p, x);
            }
        }
        if (best is not { } b) { World.Log($"{f.Name} wants down but sees no way"); return; }
        World.Log($"{f.Name} heading down to y={b.p.Y:0} x={b.x:0}");
        var a = Anchor.On(env, b.p, b.x);
        Navigate(() => a.Resolve(env), 6 * S, true, () => { f.Emote("…", 1); Go(G.Idle, 2); }, WalkPurpose.Other);
    }

    // ---------------- held by the cursor ----------------

    int _holderHits;

    /// <summary>Picked up: fight, flail, enjoy it, sulk, wriggle or go limp, depending on who it is and how
    /// it feels about you (and how long you've been holding it).</summary>
    public HeldMood ChooseHeldMood(float heldT, HeldMood prev)
    {
        if (f.KO || f.Dead || Asleep) return HeldMood.Limp;
        if (heldT < 0.05f) _holderHits = 0;
        float like = f.Tastes.Of(Thing.BeingPickedUp), fond = UserFondness;
        // Struggling is tiring: exhausted figures hang there for a moment before having another go.
        if (prev is HeldMood.Flail or HeldMood.Fight or HeldMood.Wriggle && Stamina < 0.2f) return HeldMood.Limp;
        if (prev != HeldMood.Limp) Stamina = MathF.Max(0, Stamina - (prev is HeldMood.Flail or HeldMood.Fight ? 0.03f : 0.01f));
        if (f.Hunter || (fond < -0.4f && P.Aggression > 0.45f && P.Bravery > 0.4f)) return HeldMood.Fight;
        if (like > 0.35f) return heldT > 8 && rng.NextDouble() < 0.5 ? HeldMood.Wriggle : HeldMood.Happy;
        if (Fear > 0.4f || P.Bravery < 0.35f || like < -0.35f)
            return prev == HeldMood.Flail && heldT > 4 && rng.NextDouble() < 0.3 ? HeldMood.Limp : HeldMood.Flail;
        if (P.Aggression > 0.6f && fond < 0.2f) return HeldMood.Fight;
        if (P.Energy < 0.35f) return rng.NextDouble() < 0.5 ? HeldMood.Grumpy : HeldMood.Limp;
        if (P.Energy > 0.65f || P.Playfulness > 0.65f) return rng.NextDouble() < 0.7 ? HeldMood.Wriggle : HeldMood.Grumpy;
        return heldT < 2 ? HeldMood.Wriggle : HeldMood.Grumpy;
    }

    public void OnHeldMood(HeldMood m)
    {
        string? e = m switch
        {
            HeldMood.Fight => "#@!", HeldMood.Flail => "!!", HeldMood.Happy => "♪", HeldMood.Grumpy => "hmph", HeldMood.Limp => "…", _ => null,
        };
        if (e != null && (f.CurrentEmote == null || m == HeldMood.Fight)) f.Emote(e, 1.1f);
    }

    /// <summary>Landed a punch on the hand holding it. Hunters that land enough wriggle free.</summary>
    public void OnPunchedHolder(World w, float heldT)
    {
        _holderHits++;
        if (_holderHits % 4 == 0) f.Emote(rng.NextDouble() < 0.5 ? "#@!" : "ha", 0.8f);
        if (f.Hunter && _holderHits >= 8 && heldT > 3 && rng.NextDouble() < 0.5)
        {
            _holderHits = 0;
            f.Emote("ha", 1.2f);
            f.Release(new Vector2(rng.Range(-200, 200), -300) * S);
        }
    }

    // ---------------- the user as a friend (or not) ----------------

    /// <summary>Fans of the user come over to hang out with the cursor, show off, or bring it a ball.</summary>
    (float, Action)? UserOption(World w)
    {
        float fond = UserFondness;
        if (fond < 0.35f || CursorTrust < 0.4f || _t0 < _fanUntil) return null;
        float d = MathF.Abs(w.Cursor.X - f.Base.X);
        if (d > 1600 * S || d < 90 * S) return null;
        float weight = (fond - 0.3f) * (0.5f + P.Sociability) * Taste(Thing.YourCursor);
        return (weight, () =>
        {
            _fanUntil = _t0 + rng.Range(25, 60) * (1.3f - fond * 0.5f);
            // Bring a ball if one's handy, otherwise come say hi.
            if (rng.NextDouble() < 0.3 + P.Playfulness * 0.6 && NearestFreeBall(w, 500 * S) is { SizeMul: <= 1.8f } b)
                GoToBall(b, w, () => { PickUp(b, null); _bringToUser = true; });
            else ComeToCursor(w);
        });
    }

    bool _bringToUser;
    float _fanUntil;

    void ComeToCursor(World w)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        float side = MathF.Sign(f.Base.X - w.Cursor.X);
        if (side == 0) side = 1;
        Navigate(() => new Vector2(M.ClampIn(w.Cursor.X + side * 30 * S, seg.X1 + 6 * S, seg.X2 - 6 * S), f.Base.Y), 8 * S, false, () =>
        {
            FaceTo(w.Cursor.X);
            if (rng.NextDouble() < 0.5) { f.Emote("♥", 1.2f); Go(G.Wave, 1.5f); }
            else if (P.Playfulness > 0.5f && Stamina > 0.4f) { Go(G.Trick, 3); f.RequestFlip(60 * S); }
            else { f.Emote("♪", 1); Go(G.Cheer, 1); }
        }, WalkPurpose.Social);
    }

    /// <summary>Carrying a ball for the user: walk under the cursor and toss it up to them.</summary>
    bool BringBallToUser(World w)
    {
        if (!_bringToUser || f.Carrying is not { } b) return false;
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        float target = seg != null ? M.ClampIn(w.Cursor.X - f.Facing * 25 * S, seg.X1 + 6 * S, seg.X2 - 6 * S) : f.Base.X;
        _run = false;
        if (!MoveToward(target, 10 * S) && _t < 8) return true;
        _bringToUser = false;
        FaceTo(w.Cursor.X);
        f.Emote("♥", 1.2f);
        UserFondness += 0.01f;
        BeginThrow(b, w.Cursor, null, false);
        return true;
    }
}
