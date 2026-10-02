using System.Numerics;

namespace StickFight;

/// <summary>Likes and dislikes in action (Sims-style): tastes tilt choices, shared interests draw figures
/// together, they react to things they love or hate, and each figure has its own relationship with the user.</summary>
sealed partial class Brain
{
    float? _fondness;
    float _pettingT, _heightWorryCd, _likeSeenCd;
    readonly HashSet<int> _greetedBalls = new();

    /// <summary>How much this figure likes you (the user), -1..1. Separate from CursorTrust (fear vs comfort).</summary>
    public float UserFondness
    {
        get => _fondness ??= Math.Clamp(0.1f + (P.Sociability - 0.5f) * 0.4f + f.Tastes.Of(Thing.YourCursor) * 0.4f, -1, 1);
        set => _fondness = Math.Clamp(value, -1, 1);
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
        if (o > 0.35f) { f.Emote("♪", 1); UserFondness += 0.04f; }
        else if (o < -0.35f) { f.Emote("#@!", 1); UserFondness -= 0.08f; CursorTrust = MathF.Max(0, CursorTrust - 0.05f); }
        else UserFondness -= 0.01f;
    }

    /// <summary>Thrown around: thrill-seekers love it, others hate it.</summary>
    bool FeelAboutBeingThrown(float throwSpeed)
    {
        float o = f.Tastes.Of(Thing.BeingThrown);
        if (throwSpeed < 300 * S) return false;
        if (o > 0.35f)
        {
            f.Emote(rng.NextDouble() < 0.5 ? "!!" : "♪", 1.3f);
            UserFondness += 0.08f;
            CursorTrust = MathF.Min(1, CursorTrust + 0.05f);
            Cheered(0.4f);
            Go(G.Cheer, 1);
            return true;   // "again!"
        }
        UserFondness -= o < -0.35f ? 0.15f : 0.05f;
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
        if (f.Tastes.Likes(Thing.YourCursor)) { UserFondness += 0.02f; f.Emote(rng.NextDouble() < 0.5 ? "♥" : "ha", 0.9f); }
        else UserFondness -= f.Tastes.Dislikes(Thing.YourCursor) ? 0.06f : 0.02f;
    }

    /// <summary>Gentle hovering is like petting: cursor-lovers enjoy it.</summary>
    void FeelPetting(World w, float dt)
    {
        bool petting = w.Hover == f && w.CursorVel.Length() < 250 * S;
        _pettingT = petting ? _pettingT + dt : 0;
        if (!petting) return;
        float o = f.Tastes.Of(Thing.YourCursor);
        UserFondness += dt * (0.01f + MathF.Max(0, o) * 0.03f);
        if (o > 0.35f && _pettingT > 0.7f && f.CurrentEmote == null) { f.Emote("♥", 1.2f); Cheered(0.15f); }
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
        Navigate(() => new Vector2(Math.Clamp(w.Cursor.X + side * 30 * S, seg.X1 + 6 * S, seg.X2 - 6 * S), f.Base.Y), 8 * S, false, () =>
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
        float target = seg != null ? Math.Clamp(w.Cursor.X - f.Facing * 25 * S, seg.X1 + 6 * S, seg.X2 - 6 * S) : f.Base.X;
        _run = false;
        if (!MoveToward(target, 10 * S) && _t < 8) return true;
        _bringToUser = false;
        FaceTo(w.Cursor.X);
        f.Emote("♥", 1.2f);
        UserFondness += 0.03f;
        BeginThrow(b, w.Cursor, null, false);
        return true;
    }
}
