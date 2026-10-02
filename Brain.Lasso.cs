using System.Numerics;

namespace StickFight;

/// <summary>A lasso: whoever's holding one twirls it over their head and ropes a friend (yanking them over, to much
/// protest or laughter), a ball, or your cursor (which then gets spun round and flung; see App.Lasso).</summary>
sealed partial class Brain
{
    float _lassoCd = 20;
    int _lassoPhase;            // 0 twirling, 1 throwing, 2 caught, 3 reeling in
    Figure? _roped;
    Prop? _ropedBall;
    bool _ropedCursor;
    Vector2 _ropeTarget;

    public bool HasLasso => f.Weapon?.Def.Key == "lasso";

    void LassoOptions(World w, OptionList opts)
    {
        if (!HasLasso || _lassoCd > _t0 || Stamina < 0.3f) return;
        var hand = f.Jt[J.HandN];
        var friend = w.Figures.Where(o => o != f && o.Mode == Mode.Control && o.Grounded && !o.Brain.InFight && !o.Brain.Asleep && !o.Brain.Baby)
                              .Where(o => Vector2.Distance(o.Base, f.Base) is var d && d > 60 * S && d < 380 * S && (AffinityWith(o) > 0.2f || IsRival(o)))
                              .OrderBy(o => Vector2.Distance(o.Base, f.Base)).FirstOrDefault();
        if (friend != null) opts.Add((0.2f + P.Playfulness * 0.9f) * Taste(Thing.Tricks), () => StartLasso(friend, null, false), $"Lasso {friend.Name}");
        if (w.CursorLasso == null && World.LassoCursor && Vector2.Distance(w.Cursor, hand) < 420 * S && w.CursorStill > 3
            && ((UserFondness > 0.3f && P.Playfulness > 0.4f) || f.Hunter))
            opts.Add(0.6f + (f.Hunter ? 3 : 0) + P.Playfulness * 0.5f, () => StartLasso(null, null, true), "Lasso your cursor");
        if (NearestFreeBall(w, 380 * S) is { SizeMul: <= 1.8f } ball) opts.Add(0.25f + P.Playfulness * 0.3f, () => StartLasso(null, ball, false), "Lasso a ball");
    }

    /// <summary>Debug: give it a lasso (if it has none) and go for a target.</summary>
    public void DebugLasso(World w, string target)
    {
        if (!HasLasso && w.MakeItem?.Invoke("lasso") is { } l) f.Equip(l);
        if (target == "cursor") { w.CursorStill = 10; StartLasso(null, null, true); }
        else if (target == "ball" && NearestFreeBall(w, 2000 * S) is { } b) StartLasso(null, b, false);
        else if (w.Figures.FirstOrDefault(o => o.Name == target) is { } o) StartLasso(o, null, false);
    }

    void StartLasso(Figure? who, Prop? ball, bool cursor)
    {
        _roped = who; _ropedBall = ball; _ropedCursor = cursor;
        _lassoPhase = 0;
        _lassoCd = _t0 + (cursor ? 300 : rng.Range(40, 90));
        Go(G.Lasso, 8);
        f.Emote(V("yeehaw!", "YEEEHAW!!", "hold still.", "um… yeehaw?", "the rope sings"), 1.2f);
    }

    void DoLasso(World w)
    {
        f.DesiredVX = 0;
        if (!HasLasso) { EndLasso(); return; }
        Vector2 hand = f.Jt[J.HandN];
        Vector2? target = _roped != null && w.Figures.Contains(_roped) ? _roped.Jt[J.Neck] : _ropedBall != null && w.Props.Contains(_ropedBall) ? _ropedBall.Pos : _ropedCursor ? w.Cursor : null;
        if (target is not Vector2 t) { EndLasso(); return; }
        FaceTo(t.X);
        f.HoldN = hand + new Vector2(0, -f.Torso * 0.9f);   // arm up, twirling
        switch (_lassoPhase)
        {
            case 0:
                if (_t > 1.3f) { _lassoPhase = 1; _ropeTarget = hand; World.Play(Sfx.Whoosh, hand, 0.3f, 1.4f); }
                break;
            case 1:
                f.HoldN = null;
                _ropeTarget = Vector2.Lerp(_ropeTarget, t, 0.25f);
                if (Vector2.Distance(_ropeTarget, t) < 8 * S || _t > 2.2f)
                {
                    _lassoPhase = 2;
                    _t = 2.2f;
                    if (_roped != null) RopedFigure(_roped, w);
                    else if (_ropedBall != null) { _ropedBall.Vel = (hand - _ropedBall.Pos) * 2.2f + new Vector2(0, -300 * S); _ropedBall.OnGround = false; World.Play(Sfx.Swish, _ropedBall.Pos, 0.3f); }
                    else if (_ropedCursor) { w.CursorLasso = f; f.Emote(V("got you!", "GOTCHA!!", "mine now.", "o-oh! I got it!", "caught"), 1.2f); }
                }
                break;
            case 2:
                f.HoldN = null;
                _ropeTarget = t;
                if (_ropedCursor) { if (w.CursorLasso != f) EndLasso(); else f.HoldN = hand + new Vector2(0, -f.Torso * 0.7f); return; }
                if (_t > 4) EndLasso();
                break;
        }
        if (_t > _dur) EndLasso();
    }

    void RopedFigure(Figure o, World w)
    {
        float dir = MathF.Sign(f.Base.X - o.Base.X);
        o.RequestJump(new Vector2(dir * 320 * S, -360 * S), 0.02f);
        o.Brain.Lassoed(f, w);
        World.Play(Sfx.Swish, o.Jt[J.Neck], 0.35f);
        Write("lasso:" + o.Name, V($"Lassoed {o.Name}!", $"LASSOED {o.Name.ToUpperInvariant()}!! YEEHAW!", $"Roped {o.Name}. Easy.", $"I lassoed {o.Name}… sorry!", $"Caught {o.Name} with the rope."), "★", 900);
        Practice(SkillKind.Juggling, 0.01f);
    }

    /// <summary>Somebody roped us.</summary>
    public void Lassoed(Figure by, World w)
    {
        float a = AffinityWith(by);
        if (a > 0.3f && P.Playfulness > 0.4f) { f.Emote(V("hey! haha", "WHEEE!", "very funny.", "eep!", "caught, fair and square"), 1.3f); Cheered(0.05f); }
        else { f.Emote(V("hey!!", "HEY!!", "#@!", "ow…", "unhand me"), 1.3f); AddAffinity(by, -0.04f); Annoyance = M.Clamp01(Annoyance + 0.15f); }
    }

    /// <summary>The cursor got away (you moved it) or was flung.</summary>
    public void CursorLassoOver(bool brokeFree)
    {
        if (brokeFree) f.Emote(V("aw, it got away!", "NOOO COME BACK!", "…fine.", "oh! it escaped…", "the wild one runs free"), 1.3f);
        else { f.Emote(f.Hunter ? "ha!" : V("yeehaw!!", "AGAIN AGAIN!", "heh.", "wheee!", "fly, little arrow"), 1.3f); FeelUser(f.Hunter ? 0.01f : 0.03f, "Lassoed your cursor"); }
        Write("lassocursor", V("I lassoed your cursor and spun it round!", "LASSOED THE CURSOR!!! SPUN IT!!!", "Roped the cursor. It didn't stand a chance.", "I caught your cursor with my rope… I hope you didn't mind.", "The cursor and I danced, on a rope."), "★", 900);
    }

    void EndLasso()
    {
        if (_ropedCursor && World.Current.CursorLasso == f) World.Current.CursorLasso = null;
        _roped = null; _ropedBall = null; _ropedCursor = false;
        f.HoldN = null;
        _lassoPhase = 0;
        if (_g == G.Lasso) Go(G.Idle, 1);
    }

    /// <summary>For drawing: the rope from the hand (a twirling loop, a flying loop, or taut to whatever's caught).</summary>
    public bool LassoRope(out Vector2 hand, out Vector2 end, out int phase, out float spin)
    {
        hand = f.Jt[J.HandN]; end = _ropeTarget; phase = _lassoPhase; spin = _t * 14;
        return _g == G.Lasso && HasLasso;
    }
}
