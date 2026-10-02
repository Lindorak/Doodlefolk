using System.Numerics;

namespace StickFight;

/// <summary>The cursor hunter: a figure with a permanent grudge against you. It chases your cursor across
/// windows, boxes it when it can reach, and throws whatever it can grab (balls, for now) at it when it can't.</summary>
sealed partial class Brain
{
    bool _huntThrow;
    float _huntPlanT;

    /// <summary>Hunting is on the menu (and usually wins).</summary>
    float _huntFails;

    (float, Action)? HuntOption(World w)
    {
        if (!f.Hunter || Stamina < 0.25f) return null;
        // Keeps trying, but not blindly: every failed chase takes the edge off for a while.
        _huntFails = MathF.Max(0, _huntFails - 0.02f);
        return ((6f + P.Aggression * 4) / (1 + _huntFails), () => BeginHunt(w));
    }

    void BeginHunt(World w)
    {
        Go(G.Hunt, rng.Range(20, 45));
        _huntPlanT = 0;
        if (rng.NextDouble() < 0.4) f.Emote(rng.NextDouble() < 0.5 ? "#@!" : "!!", 1);
    }

    void DoHunt(World w)
    {
        Vector2 cur = w.Cursor;
        f.LookAt = cur;
        if (_t > _dur || Stamina < 0.15f)
        {
            // Out of breath: sit and glare for a bit, then go again.
            f.Emote("…", 1);
            Go(G.SitFloor, rng.Range(3, 7));
            return;
        }
        Stamina = MathF.Max(0, Stamina - World.Dt * 0.004f);
        if (ShootCursor(w)) return;
        _huntPlanT -= World.Dt;
        if (_huntPlanT > 0) { HuntStep(w, cur); return; }
        _huntPlanT = 0.5f;

        float dx = cur.X - f.Base.X, d = MathF.Abs(dx);
        float rel = f.Base.Y - cur.Y;
        bool reachable = rel > -4 * S && rel < f.Height * 1.05f;
        bool canHit = Rules.Enabled && Rules.PunchCursor;

        // A blaster lying around? Even better.
        if (canHit && !f.Armed && w.Items.FirstOrDefault(i => i.Def.Ranged && i.Free && i.OnGround && Vector2.Distance(i.Pos, f.Base) < 800 * S) is { } gun)
        {
            UseItem(gun, Verb.Shoot, w);
            return;
        }

        // Close enough to box it.
        if (canHit && reachable && d < 220 * S) { BeginCursorFight(); return; }

        // Out of reach: grab something to throw.
        if (canHit && (!reachable || d > 500 * S) && NearestFreeBall(w, 380 * S) is { SizeMul: <= 2.2f } b)
        {
            GoToBall(b, w, () => { PickUp(b, null); _huntThrow = true; });
            return;
        }
        HuntStep(w, cur);
    }

    /// <summary>Head for the spot under the cursor, on whatever window it's over.</summary>
    void HuntStep(World w, Vector2 cur)
    {
        var env = w.Env;
        var target = env.Below(cur.X, cur.Y - 2 * S);
        if (target == null) { f.DesiredVX = 0; return; }
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        float tx = M.ClampIn(cur.X, target.X1 + 8 * S, target.X2 - 8 * S);
        if (seg != null && SameSegment(seg, target))
        {
            _run = true;
            if (!MoveToward(tx, 30 * S)) return;
            f.DesiredVX = 0;
            FaceTo(cur.X);
            // Right under it but it's too high: jump at it, swinging.
            if (f.Base.Y - cur.Y > f.Height * 1.05f && f.Base.Y - cur.Y < f.Height * 3.2f && MathF.Abs(cur.X - f.Base.X) < 80 * S && _huntPlanT <= 0.1f)
            {
                float rise = f.Base.Y - cur.Y - f.Height * 0.6f;
                f.RequestJump(new Vector2((cur.X - f.Base.X) * 2, -MathF.Sqrt(2 * f.Gravity * MathF.Max(rise, 20 * S))), 0.1f);
                f.Emote("#@!", 0.8f);
            }
            return;
        }
        // Another window: go there (jumping, climbing or grappling), then pick up the hunt again. If there's no way
        // to the spot under the cursor, settle for the closest place it can get to, or stand underneath and glare.
        if (seg == null) return;
        var goal = target;
        float gx = tx;
        if (Unreachable(NavGraph.Key(target)) || w.Nav.FindPath(seg, f.Base.X, target, tx, MyMover, MoveCost, 400) == null)
        {
            _unreachable[NavGraph.Key(target)] = _t0 + 15;
            goal = null!;
            foreach (var p in env.Platforms.Where(p => p != seg && p.Y >= cur.Y - 2 && !Unreachable(NavGraph.Key(p)))
                                          .OrderBy(p => MathF.Abs(M.ClampIn(cur.X, p.X1, p.X2) - cur.X) + (p.Y - cur.Y) * 0.6f).Take(5))
            {
                float px = M.ClampIn(cur.X, p.X1 + 8 * S, p.X2 - 8 * S);
                if (SameSegment(p, seg) || w.Nav.FindPath(seg, f.Base.X, p, px, MyMover, MoveCost, 300) != null) { goal = p; gx = px; break; }
                _unreachable[NavGraph.Key(p)] = _t0 + 15;
            }
            if (goal == null || SameSegment(goal, seg))
            {
                // Nowhere better to be: get as close as this ledge allows and glare up at it.
                _huntFails += 1;
                _run = true;
                if (MoveToward(M.ClampIn(cur.X, seg.X1 + 8 * S, seg.X2 - 8 * S), 20 * S))
                {
                    FaceTo(cur.X);
                    if (rng.NextDouble() < 0.02) f.Emote(rng.NextDouble() < 0.5 ? "#@!" : "get down here!", 1.2f);
                }
                return;
            }
        }
        var a = Anchor.On(env, goal, gx);
        Navigate(() => a.Resolve(env), 30 * S, true, () => BeginHunt(w), WalkPurpose.Hunt);
        _navAbout = NavGraph.Key(goal);
        _dur = 25;
    }

    /// <summary>Carrying a ball for the hunt: throw it at the cursor, aiming where it's going.</summary>
    bool HuntThrow(World w)
    {
        if (!_huntThrow || f.Carrying is not { } b) return false;
        _huntThrow = false;
        Vector2 lead = w.Cursor + w.CursorVel * 0.25f;
        FaceTo(lead.X);
        BeginThrow(b, lead, null, false);
        _fastball = true;
        f.Emote(rng.NextDouble() < 0.5 ? "!!" : "#@!", 0.8f);
        return true;
    }

    bool _fastball;

    /// <summary>A hard, flat throw straight at a point (instead of a friendly lob).</summary>
    Vector2 FastballVelocity(Vector2 from, Vector2 at, float g)
    {
        float dist = Vector2.Distance(from, at);
        float speed = (1500 + P.Aggression * 700) * S;
        float t = Math.Clamp(dist / speed, 0.12f, 1.2f);
        return new Vector2((at.X - from.X) / t, (at.Y - from.Y) / t - 0.5f * g * t);
    }

    /// <summary>One of our throws hit the cursor.</summary>
    public void OnHitCursor()
    {
        f.Emote(rng.NextDouble() < 0.6 ? "ha" : "!!", 1.2f);
        Cheered(0.15f);
        _landed++;
    }
}
