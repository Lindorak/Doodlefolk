using System.Numerics;

namespace Doodlefolk;

/// <summary>A leg-up (idea from StickBuddies, MIT): a window ledge that's too high to jump to? Ask a friend. The friend
/// crouches with cupped hands, you run, step into them, and get thrown up onto it. And jumping gets more confident with
/// practice: beginners whoop when they land a big jump, old hands barely notice.</summary>
sealed partial class Brain
{
    static bool Gestures => World.Gestures;
    float _boostCd = 120;
    Figure? _boostPartner;
    Platform? _boostTarget;
    float _boostX, _boostSide;
    bool _boostIsJumper;
    int _boostPhase;

    /// <summary>A lob that a leg-up makes possible (higher than a figure can jump on its own).</summary>
    static bool BoostLob(Vector2 from, Vector2 to, float g, float S, out Vector2 v)
    {
        v = default;
        float dx = to.X - from.X, rise = from.Y - to.Y;
        float apex = MathF.Max(rise, 0) + 30 * S + MathF.Abs(dx) * 0.1f;
        if (apex > 600 * S) return false;
        float vy = -MathF.Sqrt(2 * g * apex);
        float tUp = -vy / g, tDown = MathF.Sqrt(2 * (apex - rise) / g);
        v = new(dx / (tUp + tDown), vy);
        return MathF.Abs(v.X) < 500 * S;
    }

    void BoostOptions(World w, OptionList opts)
    {
        if (_boostCd > _t0 || Baby || !f.Grounded) return;
        var here = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (here == null) return;
        // A ledge above that's out of reach alone, but within a leg-up.
        Platform? best = null;
        foreach (var p in w.Env.Platforms)
        {
            float rise = here.Y - p.Y;
            if (p.Item != null || rise < 260 * S || rise > 520 * S || p.X2 - p.X1 < 80 * S) continue;
            float cx = M.ClampIn(f.Base.X, p.X1 + 30 * S, p.X2 - 30 * S);
            if (cx < here.X1 + 20 * S || cx > here.X2 - 20 * S || MathF.Abs(cx - f.Base.X) > 500 * S) continue;
            if (NavGraph.Lob(new Vector2(cx, here.Y), new Vector2(cx, p.Y), f.Gravity, S, out _)) continue;   // could just jump
            best = p; break;
        }
        if (best == null) return;
        var friend = w.Figures.Where(o => o != f && o.Mode == Mode.Control && o.Grounded && !o.Brain.Baby && !o.Hunter && !o.Brain.Asleep && !o.Brain.InFight
                                          && o.Brain._g is not (G.Work or G.Build or G.Ride or G.Swim or G.Fish or G.Boost or G.Busy or G.Happening or G.Sport or G.Game or G.Tourney)
                                          && w.Env.SupportAt(o.Base.X, o.Base.Y, o.GroundHwnd) is { } there && MathF.Abs(there.Y - here.Y) < 3 && there.X1 <= f.Base.X && there.X2 >= f.Base.X && AffinityWith(o) > 0.15f && o.Brain.HapRole.Length == 0)
                              .OrderBy(o => MathF.Abs(o.Base.X - f.Base.X)).FirstOrDefault();
        if (friend == null) return;
        var target = best;
        opts.Add(MathF.Max(0.02f, (0.2f + P.Curiosity * 0.4f + f.Tastes.Of(Thing.HighPlaces) * 0.4f) * (0.5f + Boredom)), () => AskForBoost(friend, target, w), $"Ask {friend.Name} for a leg-up");
    }

    void AskForBoost(Figure friend, Platform target, World w)
    {
        _boostCd = _t0 + rng.Range(300, 700);
        var here = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (here == null) return;
        float x = M.ClampIn(M.ClampIn(f.Base.X, target.X1 + 30 * S, target.X2 - 30 * S), here.X1 + 40 * S, here.X2 - 40 * S);
        float side = MathF.Sign(f.Base.X - x); if (side == 0) side = rng.NextDouble() < 0.5 ? -1 : 1;
        // Booster stands at x, the climber takes a run-up from the side it's on.
        StartBoost(friend, target, x, side, jumper: true);
        friend.Brain.StartBoost(f, target, x, side, jumper: false);
        f.LookAt = new Vector2(x, target.Y);
        f.Emote(Gestures ? "☝" : V("leg-up?", "BOOST ME!!", "help me up.", "um… could you lift me?", "lend me your hands"), 1.4f);
    }

    void StartBoost(Figure partner, Platform target, float x, float side, bool jumper)
    {
        _boostPartner = partner; _boostTarget = target; _boostX = x; _boostSide = side; _boostIsJumper = jumper; _boostPhase = 0;
        if (f.Riding != null) Dismount();
        Go(G.Boost, 20);
    }

    void DoBoost(World w)
    {
        var o = _boostPartner;
        if (o == null || !w.Figures.Contains(o) || o.Mode != Mode.Control || _boostTarget == null || _t > _dur ||
            (o.Brain._g != G.Boost && _boostPhase < 2)) { EndBoost(); return; }
        if (!_boostIsJumper)
        {
            // The booster: to the spot, crouch, cupped hands; a heave when the climber steps in.
            if (_boostPhase == 0) { if (MoveToward(_boostX, 3 * S)) { _boostPhase = 1; f.Emote(Gestures ? "👍" : V("hop on!", "UP YOU GO!!", "fine. step up.", "o-okay, careful…", "my hands are your stair"), 1.2f); } return; }
            FaceTo(_boostX + _boostSide * 100);
            f.DesiredVX = 0;
            f.Boosting = _boostPhase == 1 ? 1 : 2;
            if (_boostPhase >= 2 && _t > _dur - 18.8f) EndBoost();
            return;
        }
        // The climber: back off for a run-up, wait for the hands, run in, launch.
        float runFrom = _boostX + _boostSide * 70 * S;
        switch (_boostPhase)
        {
            case 0:
                if (MoveToward(runFrom, 4 * S)) _boostPhase = 1;
                break;
            case 1:
                FaceTo(_boostX);
                f.DesiredVX = 0;
                if (o.Boosting == 1) { _boostPhase = 2; _run = true; }
                break;
            case 2:
                if (!MoveToward(_boostX + _boostSide * 9 * S, 4 * S)) break;
                var land = new Vector2(M.ClampIn(_boostX, _boostTarget.X1 + 20 * S, _boostTarget.X2 - 20 * S), _boostTarget.Y);
                if (BoostLob(f.Base, land, f.Gravity, S, out var v))
                {
                    f.RequestJump(v, crouch: 0.04f);
                    o.Brain._boostPhase = 2;
                    o.Brain._dur = o.Brain._t + 1.2f;
                    World.Play(Sfx.Whoosh, f.Base, 0.4f, 1.2f);
                    _boostPhase = 3;
                    _dur = _t + 3;
                }
                else EndBoost();
                break;
            case 3:
                if (f.Grounded && !f.JumpPending && _t > 0.3f && w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { } on)
                {
                    bool made = MathF.Abs(on.Y - _boostTarget.Y) < 4;
                    f.Emote(made ? (Gestures ? "🙌" : V("thanks!", "WOOO! THANKS!!", "…thanks.", "th-thank you!", "the view! thank you")) : (Gestures ? "🤷" : "oops…"), 1.5f);
                    if (made) { AddAffinity(o, 0.06f); o.Brain.AddAffinity(f, 0.04f); Cheered(0.2f); Write("boost:" + o.Name, V($"{o.Name} gave me a leg-up onto a high window.", $"{o.Name} THREW ME UP A WINDOW!!", $"{o.Name} boosted me up. Fine.", $"{o.Name} helped me up somewhere high…", $"{o.Name}'s hands were my stairs today."), "★", 900); }
                    EndBoost();
                }
                break;
        }
    }

    void EndBoost()
    {
        f.Boosting = 0;
        _boostPartner = null;
        _boostTarget = null;
        _run = false;
        if (_g == G.Boost) Go(G.Idle, 1);
    }

    /// <summary>Landed a big jump: a little more confident each time (and proud of it while still learning).</summary>
    void BigJumpLanded()
    {
        if (_jRise < 140 * S) return;
        float skill = Sk(SkillKind.Climbing);
        Practice(SkillKind.Climbing, 0.01f);
        if (rng.NextDouble() < 0.35 * (1 - skill)) f.Emote(Gestures ? "🙌" : V("nailed it!", "NAILED IT!!", "easy.", "I did it!", "a clean landing"), 1.2f);
    }
}
