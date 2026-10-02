using System.Numerics;

namespace StickFight;

/// <summary>A point that stays attached to a window (or the floor) even if the window moves.</summary>
readonly struct Anchor
{
    readonly IntPtr _hwnd;
    readonly float _x, _y;

    Anchor(IntPtr hwnd, float x, float y) { _hwnd = hwnd; _x = x; _y = y; }

    public static Anchor On(Env env, Platform p, float x) =>
        p.Hwnd != IntPtr.Zero && env.TryRect(p.Hwnd, out var r) ? new(p.Hwnd, x - r.Left, 0) : new(IntPtr.Zero, x, p.Y);

    public Vector2? Resolve(Env env)
    {
        if (_hwnd == IntPtr.Zero) return new Vector2(_x, _y);
        return env.TryRect(_hwnd, out var r) ? new Vector2(r.Left + _x, r.Top) : null;
    }
}

/// <summary>Navigation: walk to a (possibly moving) target, hopping between platforms by jumping or
/// climbing when the target is somewhere else.</summary>
sealed partial class Brain
{
    enum Nav { Direct, ToTakeoff, InAir, ToWall, Climbing }
    enum WalkPurpose { Wander, Explore, Social, Ball, Other }

    Func<Vector2?> _navTarget = () => null;
    Action _onArrive = () => { };
    float _within, _takeoffX, _navT, _stuckT, _jRise;
    bool _run;
    int _hops;
    Nav _nav;
    WalkPurpose _purpose;
    Anchor _hopLand;
    IntPtr _wallHwnd;
    int _wallSide;

    void Navigate(Func<Vector2?> target, float within, bool run, Action onArrive, WalkPurpose purpose)
    {
        Go(G.Walk, 60);
        _navTarget = target;
        _within = within;
        _run = run;
        _onArrive = onArrive;
        _purpose = purpose;
        _nav = Nav.Direct;
        _hops = 0;
        _stuckT = 0;
    }

    void WalkTo(float x, bool run, Action onArrive) =>
        Navigate(() => new Vector2(x, f.Base.Y), 3 * S, run, onArrive, WalkPurpose.Wander);

    static bool SameSegment(Platform a, Platform b) =>
        a.Hwnd == b.Hwnd && MathF.Abs(a.Y - b.Y) < 2 && a.X1 < b.X2 && a.X2 > b.X1;

    void DoWalk(World w)
    {
        var env = w.Env;
        _navT += World.Dt;
        if (_t > _dur) { Go(G.Idle, 1); return; }
        if (_navTarget() is not Vector2 t) { f.Emote("?", 1); Go(G.Idle, 1); return; }

        if (_nav == Nav.InAir)
        {
            f.DesiredVX = 0;
            if (f.Grounded && !f.JumpPending && _navT > 0.25f) { _nav = Nav.Direct; _hops++; }
            return;
        }
        if (_nav == Nav.Climbing)
        {
            f.DesiredVX = 0;
            if (!f.Climbing && f.Grounded) { _nav = Nav.Direct; _hops++; }
            return;
        }
        if (!f.Grounded) { f.DesiredVX = 0; return; }
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;

        switch (_nav)
        {
            case Nav.Direct:
            {
                var tp = env.SupportAt(t.X, t.Y, IntPtr.Zero) ?? env.Below(t.X, t.Y - 4 * S);
                if (tp != null && !SameSegment(tp, seg))
                {
                    if (_hops >= 4 || !PlanHop(env, seg, tp, Math.Clamp(t.X, tp.X1 + 10 * S, tp.X2 - 10 * S), true))
                    {
                        f.Emote("?", 1);
                        Go(G.Idle, 1);
                    }
                    return;
                }
                if (MoveToward(t.X, _within)) _onArrive();
                break;
            }
            case Nav.ToTakeoff:
                if (!MoveToward(_takeoffX, 3 * S)) break;
                if (_hopLand.Resolve(env) is Vector2 land && SolveJump(f.Base, land, out var v))
                {
                    // Big drops: a lob that slowly falls a long way barely moves sideways at first, and would
                    // come back down on this same window. Make sure the arc clears the edge we're leaving.
                    if (land.Y > f.Base.Y + 20 * S && (land.X > seg.X2 || land.X < seg.X1))
                    {
                        float dir = MathF.Sign(land.X - f.Base.X);
                        float edge = dir > 0 ? seg.X2 : seg.X1;
                        float tUp = -v.Y / f.Gravity;
                        float need = (MathF.Abs(edge - f.Base.X) + 12 * S) / MathF.Max(0.05f, 2 * tUp);
                        if (MathF.Abs(v.X) < need) v.X = dir * MathF.Min(need, 700 * S);
                    }
                    _jRise = f.Base.Y - land.Y;
                    f.RequestJump(v, styled: true);
                    _nav = Nav.InAir;
                    _navT = 0;
                }
                else { _nav = Nav.Direct; _hops++; }
                break;
            case Nav.ToWall:
                if (!MoveToward(_takeoffX, 3 * S)) break;
                var wall = env.Walls.FirstOrDefault(x => x.Hwnd == _wallHwnd && x.Side == _wallSide && x.ReachesTop);
                f.ClimbPace = ClimbPace(_purpose is not (WalkPurpose.Wander or WalkPurpose.Explore) || _run);
                if (wall != null && f.StartClimb(wall)) _nav = Nav.Climbing;
                else { _nav = Nav.Direct; _hops++; }
                break;
        }
    }

    /// <summary>Walk toward x; true once within range (or stuck against an edge).</summary>
    bool MoveToward(float x, float within)
    {
        float dx = x - f.Base.X;
        if (MathF.Abs(dx) <= within) { f.DesiredVX = 0; return true; }
        float speed = (_run ? f.RunSpeed : f.WalkSpeed) * (f.Carrying != null ? 0.85f : 1);
        f.DesiredVX = MathF.Sign(dx) * speed * MathF.Max(0.35f, M.Clamp01(MathF.Abs(dx) / (25 * S)));
        _stuckT = MathF.Abs(f.Vel.X) < 4 * S && _t > 0.4f ? _stuckT + World.Dt : 0;
        if (_stuckT > 0.6f) { f.DesiredVX = 0; _stuckT = 0; return true; }
        return false;
    }

    /// <summary>Plan one hop from <paramref name="seg"/> to platform <paramref name="tp"/>, landing near x:
    /// a jump if one is possible, otherwise climbing one of the target window's edges.</summary>
    bool PlanHop(Env env, Platform seg, Platform tp, float landX, bool commit)
    {
        float side = MathF.Sign(landX - f.Base.X);
        if (side == 0) side = 1;
        float tk = Math.Clamp(landX - side * 80 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        if (SolveJump(new(tk, seg.Y), new(landX, tp.Y), out _))
        {
            if (commit)
            {
                _takeoffX = tk;
                _hopLand = Anchor.On(env, tp, landX);
                _nav = Nav.ToTakeoff;
            }
            return true;
        }
        if (tp.Y >= seg.Y || tp.Hwnd == IntPtr.Zero) return false;

        Wall? best = null;
        float bestX = 0, bestD = float.MaxValue;
        foreach (var wall in env.Walls)
        {
            if (wall.Hwnd != tp.Hwnd || !wall.ReachesTop || MathF.Abs(wall.Y1 - tp.Y) > 2 || wall.Y2 < seg.Y - 6 * S) continue;
            float x = wall.X + wall.Side * f.ClimbStandOffset;
            if (x < seg.X1 + 4 * S || x > seg.X2 - 4 * S) continue;
            if (env.SupportAt(wall.X - wall.Side * 9 * S, wall.Y1, wall.Hwnd) is not { } top || top.Hwnd != wall.Hwnd) continue;
            float d = MathF.Abs(x - f.Base.X);
            if (d < bestD) { best = wall; bestX = x; bestD = d; }
        }
        if (best == null) return false;
        if (commit)
        {
            _takeoffX = bestX;
            _wallHwnd = best.Hwnd;
            _wallSide = best.Side;
            _nav = Nav.ToWall;
        }
        return true;
    }

    bool CanReach(Env env, Platform seg, Platform tp, float x) => PlanHop(env, seg, tp, x, false);

    bool CanReachByJump(Env env, Platform seg, Platform tp, float x)
    {
        float side = MathF.Sign(x - f.Base.X);
        if (side == 0) side = 1;
        float tk = Math.Clamp(x - side * 80 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        return SolveJump(new(tk, seg.Y), new(x, tp.Y), out _);
    }

    /// <summary>How fast to climb: energetic and hurried figures scramble, tired ones plod; a little random.</summary>
    float ClimbPace(bool urgent) =>
        (0.8f + 0.6f * P.Energy + (urgent ? 0.35f : 0) + rng.Range(-0.12f, 0.12f)) * (0.65f + 0.35f * Stamina);

    bool SolveJump(Vector2 from, Vector2 to, out Vector2 v) => SolveLob(from, to, f.Gravity, 26 * S, 330 * S, 700 * S, out v);

    /// <summary>Ballistic launch velocity from <paramref name="from"/> to <paramref name="to"/> with an apex
    /// a little above the higher of the two points.</summary>
    bool SolveLob(Vector2 from, Vector2 to, float g, float clearance, float maxApex, float maxVx, out Vector2 v)
    {
        v = default;
        float dx = to.X - from.X, rise = from.Y - to.Y;
        float apex = MathF.Max(rise, 0) + clearance + MathF.Abs(dx) * 0.12f;
        if (apex > maxApex) return false;
        float vy = -MathF.Sqrt(2 * g * apex);
        float tUp = -vy / g;
        float tDown = MathF.Sqrt(2 * (apex - rise) / g);
        float vx = dx / (tUp + tDown);
        if (MathF.Abs(vx) > maxVx) return false;
        v = new(vx, vy);
        return true;
    }

    // ================= debug / remote control =================

    /// <summary>Make the figure do something specific right now (debug channel).</summary>
    public bool Force(string goal, string[] args, World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded) return false;
        var env = w.Env;
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        float Arg(int i, float d) => args.Length > i && float.TryParse(args[i], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d;
        Figure? Other() => args.Length > 0 ? w.Figures.FirstOrDefault(o => o != f && o.Name.Equals(args[0], StringComparison.OrdinalIgnoreCase)) : w.Figures.FirstOrDefault(o => o != f);
        switch (goal)
        {
            case "idle": Go(G.Idle, Arg(0, 3)); return true;
            case "wave": Go(G.Wave, 1.5f); return true;
            case "cheer": Go(G.Cheer, 0.9f); return true;
            case "annoyed": Go(G.Annoyed, 1.4f); return true;
            case "swat": Go(G.Swat, 0.34f); return true;
            case "sitfloor": Go(G.SitFloor, Arg(0, 8)); return true;
            case "sleep": Go(G.Sleep, Arg(0, 20)); return true;
            case "flip": Go(G.Trick, 3); f.RequestFlip(70 * S); return true;
            case "startle": Startle(w.Cursor); return true;
            case "walk": WalkTo(Arg(0, f.Base.X + 300 * S), false, () => Go(G.Idle, 3)); return true;
            case "run": WalkTo(Arg(0, f.Base.X + 600 * S), true, () => Go(G.Idle, 3)); return true;
            case "sitedge":
                if (seg == null || !PickEdge(env, seg, out float ex, out int dir)) return false;
                _sitDir = dir;
                WalkTo(ex - dir * 3.5f * S, false, () => Go(G.SitEdge, Arg(0, 10)));
                return true;
            case "explore":
                if (seg == null || !PickExplore(env, seg, out var plan)) return false;
                plan();
                return true;
            case "climb":
            {
                if (seg == null) return false;
                var target = env.Platforms.Where(p => p.Y < seg.Y - f.Height && !p.Solid)
                    .FirstOrDefault(p => PlanHop(env, seg, p, (p.X1 + p.X2) / 2, false) && !SolveJump(f.Base, new((p.X1 + p.X2) / 2, p.Y), out _));
                if (target == null) return false;
                var a = Anchor.On(env, target, Math.Clamp(f.Base.X, target.X1 + 20 * S, target.X2 - 20 * S));
                Navigate(() => a.Resolve(env), 4 * S, false, () => Go(G.Idle, 1), WalkPurpose.Explore);
                return true;
            }
            case "chat": return Other() is { } o1 && StartSocialWith(o1, SocialKind.Chat, w);
            case "highfive": return Other() is { } o2 && StartSocialWith(o2, SocialKind.HighFive, w);
            case "follow": return Other() is { } o3 && StartSocialWith(o3, SocialKind.Follow, w);
            case "dance": return Other() is { } o6 && StartSocialWith(o6, SocialKind.Dance, w);
            case "meet": if (Other() is { } o7) { Approach(o7, w); return true; } return false;
            case "fan": if (UserOption(w) is { } uo) { uo.Item2(); return true; } return false;
            case "fight": if (Other() is { } o4) { Engage(o4, false, w); return true; } return false;
            case "spar": if (Other() is { } o5) { Engage(o5, true, w); return true; } return false;
            case "boxcursor": BeginCursorFight(); return true;
            case "ball": return ForceBall(w, null);
            case "kick": return ForceBall(w, BallPlay.Kick);
            case "dribble": return ForceBall(w, BallPlay.Dribble);
            case "juggle": return ForceBall(w, BallPlay.Juggle);
            case "carry": return ForceBall(w, BallPlay.Carry);
            case "pass": return ForceBall(w, BallPlay.Pass);
        }
        return false;
    }
}
