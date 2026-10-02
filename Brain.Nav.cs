using System.Numerics;

namespace Doodlefolk;

/// <summary>A point that stays attached to a window (or the floor) even if the window moves.</summary>
readonly struct Anchor
{
    readonly IntPtr _hwnd;
    readonly float _x, _y;

    Anchor(IntPtr hwnd, float x, float y) { _hwnd = hwnd; _x = x; _y = y; }

    public static Anchor On(Env env, Platform p, float x) =>
        p.Hwnd != IntPtr.Zero && env.TryRect(p.Hwnd, out var r) ? new(p.Hwnd, x - r.Left, p.Y - r.Top) : new(IntPtr.Zero, x, p.Y);

    public Vector2? Resolve(Env env)
    {
        if (_hwnd == IntPtr.Zero) return new Vector2(_x, _y);
        return env.TryRect(_hwnd, out var r) ? new Vector2(r.Left + _x, r.Top + _y) : null;
    }
}

/// <summary>Navigation: walk to a (possibly moving) target, hopping between platforms by jumping or
/// climbing when the target is somewhere else.</summary>
sealed partial class Brain
{
    enum Nav { Direct, ToTakeoff, InAir, ToWall, ToThrow, Climbing, Off }
    enum WalkPurpose { Wander, Explore, Social, Ball, Other, Look, Watch, Heart, Hunt }

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
        _route = null;
        _pending = null;
        _navAbout = null;
    }

    // ---- routes (see NavGraph) ----
    List<NavEdge>? _route;
    int _routeStep, _routeVersion;
    NavKey _routeGoal;
    NavEdge? _pending;
    float _replanAt;
    /// <summary>Moves that went wrong recently (by from/to/kind): avoided for a while instead of tried again and again.</summary>
    readonly Dictionary<(NavKey, NavKey, MoveKind), float> _badMoves = new();

    Mover MyMover => new(S, f.Gravity, f.ClimbStandOffset, f.Height);

    /// <summary>A figure's own take on a move: climbers like climbing, the timid dislike big drops, the tired avoid big
    /// jumps, and anything that just went wrong is avoided for a while.</summary>
    float MoveCost(NavEdge e)
    {
        float c = e.Kind switch
        {
            MoveKind.Climb => (1 - f.Tastes.Of(Thing.Climbing)) * 1.2f + (1 - Stamina) * 1.5f,
            MoveKind.Jump => (1 - Stamina) * 0.6f + (1 - P.Energy) * 0.3f,
            MoveKind.Drop => (1.2f - P.Bravery) * MathF.Max(0, (e.ToY - f.Base.Y) / (500 * S)),
            _ => 0,
        };
        if (_badMoves.TryGetValue((e.From, e.To, e.Kind), out var until) && _t0 < until) c += 30;
        return c;
    }

    bool Replan(World w, Platform seg, Platform tp, float tx)
    {
        _replanAt = _t0 + 0.35f;
        _route = w.Nav.FindPath(seg, f.Base.X, tp, tx, MyMover, MoveCost);
        _routeStep = 0;
        _routeVersion = w.Nav.Version;
        _routeGoal = NavGraph.Key(tp);
        if (World.TraceJumps) World.Log($"route {f.Name}: ({f.Base.X:0},{f.Base.Y:0}) -> ({tx:0},{tp.Y:0}): {(_route == null ? "none" : string.Join(" | ", _route))}");
        return _route != null && _route.Count > 0;
    }

    /// <summary>Start the move for one step of the route (we're standing at its take-off point).</summary>
    void TakeStep(NavEdge e, Platform seg, World w)
    {
        var env = w.Env;
        _pending = e;
        switch (e.Kind)
        {
            case MoveKind.Walk:
            case MoveKind.Drop:
                f.AllowWalkOff = true;
                _takeoffX = e.ToX;
                _nav = Nav.Off;
                _navT = 0;
                break;
            case MoveKind.Jump:
            {
                float toY = w.Nav.Get(e.To)?.Y ?? e.ToY;
                if (!NavGraph.Lob(f.Base, new Vector2(e.ToX, toY), f.Gravity, S, out var v)) { Failed(e); return; }
                _jRise = f.Base.Y - toY;
                if (World.TraceJumps) World.Log($"hop {f.Name}: {e} from ({f.Base.X:0},{f.Base.Y:0})");
                f.RequestJump(v, styled: true);
                _nav = Nav.InAir;
                _navT = 0;
                break;
            }
            case MoveKind.Climb:
            {
                var wall = env.Walls.FirstOrDefault(x => x.Hwnd == e.WallHwnd && x.Side == e.WallSide && x.ReachesTop);
                if (wall == null) { Failed(e); return; }
                _takeoffX = e.FromX;
                _wallHwnd = wall.Hwnd;
                _wallSide = wall.Side;
                _nav = Nav.ToWall;
                // Tall wall: grapple-carriers may throw a hook up instead, from a few steps back.
                float rise = seg.Y - wall.Y1;
                if (_forceGrapple || (rise > f.Height * 1.6f && _t0 >= _noGrappleUntil && rng.NextDouble() < f.Style.GrappleChance * Taste(Thing.Climbing) * 0.8f))
                {
                    float back = Math.Clamp(rise * 0.3f, 40 * S, 110 * S);
                    float tx = M.ClampIn(wall.X + wall.Side * back, seg.X1 + 6 * S, seg.X2 - 6 * S);
                    if (MathF.Abs(tx - wall.X) > 30 * S && f.Style.Rope != RopeStyle.Never) { _takeoffX = tx; _nav = Nav.ToThrow; }
                }
                _forceGrapple = false;
                break;
            }
        }
    }

    void Failed(NavEdge e)
    {
        World.Audit($"movefail\t{f.Name}\t{e.Kind}\t{e}");
        _badMoves[(e.From, e.To, e.Kind)] = _t0 + rng.Range(25, 45);
        if (_badMoves.Count > 200) _badMoves.Clear();
        if (World.TraceJumps) World.Log($"route {f.Name}: {e} failed");
        _route = null;
        _pending = null;
        _hops++;
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
            if (f.Grounded && !f.JumpPending && _navT > 0.25f) { _nav = Nav.Direct; BigJumpLanded(); }
            return;
        }
        if (_nav == Nav.Off)
        {
            // Walking on to the next surface or stepping off the end.
            if (f.Grounded)
            {
                var cur = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
                if (cur != null && _pending != null && NavGraph.Key(cur) == _pending.To) { f.AllowWalkOff = false; _nav = Nav.Direct; return; }
                if (_navT > 2.5f) { f.AllowWalkOff = false; _nav = Nav.Direct; return; }
                float dir = MathF.Sign(_takeoffX - f.Base.X);
                f.DesiredVX = (dir == 0 ? f.Facing : dir) * f.WalkSpeed;
            }
            else { f.DesiredVX = 0; f.AllowWalkOff = false; _nav = Nav.InAir; _navT = 0.26f; }
            return;
        }
        if (_nav == Nav.Climbing)
        {
            f.DesiredVX = 0;
            if (!f.Climbing && !f.GrappleBusy && f.Grounded) _nav = Nav.Direct;
            return;
        }
        if (!f.Grounded) { f.DesiredVX = 0; return; }
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;

        switch (_nav)
        {
            case Nav.Direct:
            {
                f.AllowWalkOff = false;
                // Just finished a move: did it get us where it should?
                if (_pending is { } done)
                {
                    _pending = null;
                    var reached = w.Nav.Get(done.To);
                    if (NavGraph.Key(seg) == done.To || (reached != null && SameSegment(reached, seg))) _routeStep++;
                    else Failed(done);
                }
                var tp = env.SupportAt(t.X, t.Y, IntPtr.Zero) ?? env.Below(t.X, t.Y - 4 * S);
                if (tp == null || SameSegment(tp, seg))
                {
                    _route = null;
                    if (MoveToward(t.X, _within)) _onArrive();
                    break;
                }
                bool stale = _route == null || _routeStep >= _route.Count || NavGraph.Key(tp) != _routeGoal
                             || NavGraph.Key(seg) != _route[_routeStep].From || w.Nav.Get(_route[_routeStep].To) == null;
                if (stale)
                {
                    if (_t0 < _replanAt) { f.DesiredVX = 0; break; }
                    if (_hops >= 8 || !Replan(w, seg, tp, t.X))
                    {
                        if (_t0 < TraceUntil || World.TraceJumps) World.Log($"trace {f.Name}: no route from y={seg.Y:0} [{seg.X1:0}..{seg.X2:0}] to y={tp.Y:0} [{tp.X1:0}..{tp.X2:0}] target {t} hops {_hops}");
                        NoRoute();
                        f.Emote("?", 1);
                        Go(G.Idle, 1);
                        break;
                    }
                }
                var step = _route![_routeStep];
                // Walk to the take-off point, then go.
                if (MoveToward(step.FromX, 3 * S)) TakeStep(step, seg, w);
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
                    if (World.TraceJumps) World.Log($"hop {f.Name}: from ({f.Base.X:0},{f.Base.Y:0}) [{seg.X1:0}..{seg.X2:0}] to ({land.X:0},{land.Y:0}) hops {_hops} target {_navTarget()}");
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
            case Nav.ToThrow:
            {
                if (!MoveToward(_takeoffX, 4 * S)) break;
                var gw = env.Walls.FirstOrDefault(x => x.Hwnd == _wallHwnd && x.Side == _wallSide && x.ReachesTop);
                f.ClimbPace = ClimbPace(_purpose is not (WalkPurpose.Wander or WalkPurpose.Explore) || _run);
                if (gw != null && f.StartGrapple(gw)) _nav = Nav.Climbing;
                else { _nav = Nav.Direct; _hops++; }
                break;
            }
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
    bool PlanHop(Env env, Platform seg, Platform tp, float landX, bool commit, float? fromX = null)
    {
        float baseX = fromX ?? f.Base.X;
        float side = MathF.Sign(landX - baseX);
        if (side == 0) side = 1;
        float tk = M.ClampIn(landX - side * 80 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
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
            float d = MathF.Abs(x - baseX);
            if (d < bestD) { best = wall; bestX = x; bestD = d; }
        }
        if (best == null) return false;
        if (commit)
        {
            _takeoffX = bestX;
            _wallHwnd = best.Hwnd;
            _wallSide = best.Side;
            _nav = Nav.ToWall;
            // Tall wall: grapple-carriers may throw a hook up instead, from a few steps back.
            float rise = seg.Y - best.Y1;
            if (_forceGrapple || (rise > f.Height * 1.6f && _t0 >= _noGrappleUntil && rng.NextDouble() < f.Style.GrappleChance * Taste(Thing.Climbing) * 0.8f))
            {
                float back = Math.Clamp(rise * 0.3f, 40 * S, 110 * S);
                float tx = M.ClampIn(best.X + best.Side * back, seg.X1 + 6 * S, seg.X2 - 6 * S);
                if (MathF.Abs(tx - best.X) > 30 * S && f.Style.Rope != RopeStyle.Never) { _takeoffX = tx; _nav = Nav.ToThrow; }
            }
            _forceGrapple = false;
        }
        return true;
    }

    bool _forceGrapple;

    bool CanReach(Env env, Platform seg, Platform tp, float x) => PlanHop(env, seg, tp, x, false);

    /// <summary>No single jump or climb gets there: search a few hops ahead (ledges, text lines, window tops, the
    /// floor) and head for the first step of the shortest route.</summary>
    bool PlanVia(Env env, Platform seg, Platform goal, float goalX)
    {
        var nodes = env.Platforms.Where(p => p.X2 - p.X1 >= 24 * S && !SameSegment(p, seg)).ToList();
        if (nodes.Count > 220) return false;
        var from = new Dictionary<Platform, (Platform prev, float x)>();
        var queue = new Queue<(Platform p, float x, int depth)>();
        queue.Enqueue((seg, f.Base.X, 0));
        int expanded = 0;
        while (queue.Count > 0 && expanded++ < 70)
        {
            var (a, ax, depth) = queue.Dequeue();
            if (depth >= 4) continue;
            foreach (var b in nodes)
            {
                if (b == a || from.ContainsKey(b)) continue;
                // Land as near the goal as possible, or failing that as near as possible to where we are (drops).
                float x = M.ClampIn(goalX, b.X1 + 10 * S, b.X2 - 10 * S);
                if (!PlanHop(env, a, b, x, false, ax))
                {
                    x = M.ClampIn(M.ClampIn(goalX, a.X1, a.X2), b.X1 + 10 * S, b.X2 - 10 * S);
                    if (!PlanHop(env, a, b, x, false, ax)) continue;
                }
                from[b] = (a, x);
                if (SameSegment(b, goal) || b == goal)
                {
                    // Walk the route back to its first step and take it.
                    var step = b;
                    while (from[step].prev != seg) step = from[step].prev;
                    return PlanHop(env, seg, step, from[step].x, true);
                }
                queue.Enqueue((b, x, depth + 1));
            }
        }
        return false;
    }

    bool CanReachByJump(Env env, Platform seg, Platform tp, float x)
    {
        float side = MathF.Sign(x - f.Base.X);
        if (side == 0) side = 1;
        float tk = M.ClampIn(x - side * 80 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
        return SolveJump(new(tk, seg.Y), new(x, tp.Y), out _);
    }

    /// <summary>How fast to climb: energetic and hurried figures scramble, tired ones plod; a little random.</summary>
    float ClimbPace(bool urgent) =>
        (0.8f + 0.6f * P.Energy + (urgent ? 0.35f : 0) + rng.Range(-0.12f, 0.12f)) * (0.65f + 0.35f * Stamina) * (0.8f + Sk(SkillKind.Climbing) * 0.45f);

    bool SolveJump(Vector2 from, Vector2 to, out Vector2 v) => NavGraph.Lob(from, to, f.Gravity, S, out v);

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
                var a = Anchor.On(env, target, M.ClampIn(f.Base.X, target.X1 + 20 * S, target.X2 - 20 * S));
                Navigate(() => a.Resolve(env), 4 * S, false, () => Go(G.Idle, 1), WalkPurpose.Explore);
                return true;
            }
            case "grapple":
            {
                if (seg == null) return false;
                if (f.Style.Rope == RopeStyle.Never) f.StyleChoice.Rope = RopeStyle.Rappel;
                var target = env.Platforms.Where(p => p.Y < seg.Y - f.Height * 2 && !p.Solid)
                    .FirstOrDefault(p => !SolveJump(f.Base, new((p.X1 + p.X2) / 2, p.Y), out _) && PlanHop(env, seg, p, (p.X1 + p.X2) / 2, false));
                if (target == null) return false;
                var a = Anchor.On(env, target, M.ClampIn(f.Base.X, target.X1 + 30 * S, target.X2 - 30 * S));
                Navigate(() => a.Resolve(env), 4 * S, false, () => Go(G.Idle, 1), WalkPurpose.Explore);
                _forceGrapple = true;
                return true;
            }
            case "sport":
            {
                var gear = w.Items.Where(i => i.Def.Sport != null && (args.Length == 0 || i.Def.Key == args[0])).OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
                if (gear == null) return false;
                StartMatch(gear, w);
                return Match != null;
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
