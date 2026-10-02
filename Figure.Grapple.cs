using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Grappling hook: spin it up, throw it onto a window's top edge, walk to the wall and climb the
/// rope (walking up the wall, hauling hand over hand, or zipping up), mantle over, coil the rope away.
/// A throw can miss: the hook bounces off or falls short, gets reeled in, and the figure tries again.</summary>
sealed partial class Figure
{
    enum GrapplePhase { None, Spin, Fly, Approach, Retract }

    GrapplePhase _gp;
    float _gT, _spinDur, _spinA;
    IntPtr _gHwnd;
    int _gSide, _gTries;
    float _gAimX;                  // where on the top edge the hook is aimed, relative to the wall (inward)
    Vector2 _hook, _hookVel, _hookPrev;
    Vector2 _hookRel;              // once caught: hook position relative to the window's top-left corner
    bool _hookCaught, _retry;
    bool _onRope;                  // climbing the rope (Climbing is true too)
    float _ropePhase, _ropeStartY, _ropeSpeed;
    float _gWallX;                 // x of the wall the rope will hang down

    const int RN = 18, TN = 9;
    readonly Vector2[] _rp = new Vector2[RN], _rpPrev = new Vector2[RN];
    readonly Vector2[] _tail = new Vector2[TN], _tailPrev = new Vector2[TN];
    bool _ropeInit;

    /// <summary>The hook is out (spinning, flying, caught, or being reeled in) or we're on the rope.</summary>
    public bool Grappling => _gp != GrapplePhase.None || _onRope;
    /// <summary>The brain should leave us alone until the hook is back (nav waits on this).</summary>
    public bool GrappleBusy => _gp is GrapplePhase.Spin or GrapplePhase.Fly or GrapplePhase.Approach || _onRope || (_gp == GrapplePhase.Retract && _retry);
    public bool GrappleFailed { get; private set; }

    float HookGravity => Gravity * 0.85f;
    float MaxRope => 1100 * S;

    /// <summary>Start spinning the hook to throw it onto the top of <paramref name="wall"/>'s window.</summary>
    public bool StartGrapple(Wall wall)
    {
        if (!Grounded || Mode != Mode.Control || Climbing || Carrying != null) return false;
        _gp = GrapplePhase.Spin;
        _gT = 0;
        _gTries = 0;
        _gHwnd = wall.Hwnd;
        _gSide = wall.Side;
        _hookCaught = false;
        GrappleFailed = false;
        _retry = false;
        _ropeInit = false;
        DesiredVX = 0;
        Facing = -wall.Side;
        SetAction(Act.Stand);
        BeginSpin();
        World.Log($"{Name} spins up a grappling hook ({Style.Spin}, {Style.Rope})");
        return true;
    }

    void BeginSpin()
    {
        _gT = 0;
        _spinA = (float)_rng.NextDouble() * MathF.Tau;
        _spinDur = Style.Spin switch
        {
            GrappleSpin.QuickToss => 0.45f,
            GrappleSpin.SideWhirl => 0.9f + (float)_rng.NextDouble() * 0.5f,
            _ => 1.0f + (float)_rng.NextDouble() * 0.7f,
        } * (1.15f - Traits.Energy * 0.3f);
        // Aim a little way in from the edge; worse aim on later tries if flustered, better if careful.
        float skill = 0.55f + Traits.Curiosity * 0.2f + (1 - Traits.Energy) * 0.15f;
        float err = (float)(_rng.NextDouble() * 2 - 1) * (1 - skill) * 70 * S;
        _gAimX = 16 * S + err;      // < 0 means the hook falls short of the edge (a miss)
    }

    public void CancelGrapple()
    {
        _retry = false;
        if (_gp is GrapplePhase.None or GrapplePhase.Retract) return;
        StartRetract();
    }

    void StartRetract()
    {
        _gp = GrapplePhase.Retract;
        _gT = 0;
        _hookCaught = false;
    }

    /// <summary>Where the hook should be aimed: just in from the edge, on top of the window.</summary>
    bool AimPoint(Env env, out Vector2 at, out Wall? wall)
    {
        at = default;
        wall = env.Walls.FirstOrDefault(x => x.Hwnd == _gHwnd && x.Side == _gSide && x.ReachesTop);
        if (wall == null) return false;
        at = new(wall.X - _gSide * _gAimX, wall.Y1);
        return true;
    }

    /// <summary>Per-step grapple update while grounded. True if it fully handled movement this step.</summary>
    bool GrappleControl(float dt, World w)
    {
        var env = w.Env;
        _gT += dt;
        Vector2 hand = Jt[J.HandN];
        switch (_gp)
        {
            case GrapplePhase.Spin:
            {
                DesiredVX = 0;
                Facing = -_gSide;
                float rate = Style.Spin == GrappleSpin.QuickToss ? 5 : 9 + 6 * M.Clamp01(_gT / _spinDur);
                _spinA += rate * dt;
                _hook = SpinHookPos(hand);
                if (_gT < _spinDur) return false;

                if (!AimPoint(env, out var at, out _)) { StartRetract(); return false; }
                if (Vector2.Distance(at, hand) > MaxRope || !SolveHookLob(_hook, at, out var v)) { GrappleFailed = true; StartRetract(); return false; }
                _hookVel = v;
                _hookPrev = _hook;
                _gp = GrapplePhase.Fly;
                _gT = 0;
                InitRope(hand, _hook);
                return false;
            }
            case GrapplePhase.Fly:
            {
                DesiredVX = 0;
                _hookPrev = _hook;
                _hookVel.Y += HookGravity * dt;
                _hook += _hookVel * dt;
                // Hits the side of the window (or comes up short): bounces off and drops.
                foreach (var wl in env.Walls)
                {
                    if ((_hookPrev.X - wl.X) * (_hook.X - wl.X) >= 0 || _hook.Y <= wl.Y1 + 2 || _hook.Y > wl.Y2) continue;
                    _hook.X = wl.X + MathF.Sign(_hookPrev.X - wl.X) * 2 * S;
                    _hookVel.X *= -0.25f;
                    w.Fx.Spark(_hook, S * 0.6f, w.Rng, 0.5f);
                }
                if (_hookVel.Y > 0 && env.FindLanding(_hook.X, _hookPrev.Y, _hook.Y) is { } p)
                {
                    _hook.Y = p.Y;
                    if (p.Hwnd == _gHwnd && !p.Solid && env.TryRect(_gHwnd, out var r))
                    {
                        // Caught! Clank.
                        _hookCaught = true;
                        _hookRel = _hook - new Vector2(r.Left, r.Top);
                        _gp = GrapplePhase.Approach;
                        _gWallX = r.Left + (_gSide > 0 ? r.Width : 0);
                        _gT = 0;
                        w.Fx.Spark(_hook, S * 0.7f, w.Rng, 0.6f);
                        Brain.OnHookCaught();
                    }
                    else Missed(w);
                    return false;
                }
                if (_gT > 2.5f || Vector2.Distance(_hook, hand) > MaxRope * 1.1f) Missed(w);
                return false;
            }
            case GrapplePhase.Approach:
            {
                if (!HookAnchor(env, out var hook, out var wall)) { StartRetract(); return false; }
                _hook = hook;
                _gWallX = wall!.X;
                float x = wall.X + _gSide * ClimbStandOffset;
                float dx = x - Base.X;
                DesiredVX = MathF.Abs(dx) < 2 * S ? 0 : MathF.Sign(dx) * WalkSpeed * 1.1f;
                if (MathF.Abs(dx) < 3 * S || _gT > 6) BeginRopeClimb(wall);
                return false;
            }
            case GrapplePhase.Retract:
            {
                // Reel the hook back in: it skips along toward the hand, then coils away.
                Vector2 d = hand - _hook;
                float dist = d.Length();
                float speed = (500 + 900 * M.Clamp01(_gT / 0.4f)) * S;
                if (dist < speed * dt + 4 * S || _gT > 2)
                {
                    _gp = GrapplePhase.None;
                    if (_retry && Grounded && Mode == Mode.Control && !Climbing)
                    {
                        _retry = false;
                        _gp = GrapplePhase.Spin;
                        BeginSpin();
                    }
                    _retry = false;
                    return false;
                }
                _hook += d / dist * speed * dt;
                return false;
            }
        }
        return false;
    }

    void Missed(World w)
    {
        _gTries++;
        World.Log($"{Name} missed with the hook (try {_gTries})");
        StartRetract();
        _retry = Brain.OnHookMissed(_gTries);
        if (!_retry) GrappleFailed = true;
    }

    /// <summary>Resolve the caught hook (moves with its window) and the wall the rope hangs down.</summary>
    bool HookAnchor(Env env, out Vector2 hook, out Wall? wall)
    {
        hook = _hook;
        wall = null;
        if (!env.TryRect(_gHwnd, out var r)) return false;
        hook = new Vector2(r.Left, r.Top) + _hookRel;
        wall = env.Walls.FirstOrDefault(x => x.Hwnd == _gHwnd && x.Side == _gSide && x.ReachesTop);
        if (wall == null || MathF.Abs(hook.Y - wall.Y1) > 3) return false;
        // The hook must still be on top of the window (it slides off if the window shrank under it).
        return (hook.X - wall.X) * -_gSide > 0;
    }

    bool SolveHookLob(Vector2 from, Vector2 to, out Vector2 v)
    {
        v = default;
        float dx = to.X - from.X, rise = from.Y - to.Y;
        float apex = MathF.Max(rise, 0) + 40 * S + MathF.Abs(dx) * 0.15f;
        float g = HookGravity;
        float vy = -MathF.Sqrt(2 * g * apex);
        float tUp = -vy / g, tDown = MathF.Sqrt(2 * MathF.Max(0, apex - rise) / g);
        float vx = dx / (tUp + tDown);
        if (MathF.Abs(vx) > 1800 * S || apex > 2400 * S) return false;
        v = new(vx, vy);
        return true;
    }

    Vector2 SpinHookPos(Vector2 hand)
    {
        float a = _spinA;
        return Style.Spin switch
        {
            // Lasso overhead: a horizontal circle seen side-on is a flat ellipse.
            GrappleSpin.Overhead => hand + new Vector2(MathF.Cos(a) * 17 * S, MathF.Sin(a) * 4 * S - 2 * S),
            // Whirled in a big vertical circle at the side.
            GrappleSpin.SideWhirl => hand + new Vector2(MathF.Cos(a) * 18 * S * Facing, MathF.Sin(a) * 18 * S),
            // Dangled and swung, then tossed.
            _ => hand + new Vector2(MathF.Sin(a) * 9 * S * Facing, 13 * S),
        };
    }

    // ---------------- on the rope ----------------

    void BeginRopeClimb(Wall wall)
    {
        _gp = GrapplePhase.None;
        _onRope = true;
        Climbing = true;
        _mantling = false;
        _climbHwnd = wall.Hwnd;
        _climbSide = wall.Side;
        _climbX = wall.X;
        _climbTop = wall.Y1;
        ClimbDir = 1;
        Grounded = false;
        Vel = default;
        _jumpVel = null;
        Facing = -wall.Side;
        SetAction(Act.Stand);
        _ropePhase = 0;
        _ropeStartY = Base.Y;
        _ropeSpeed = 0;
        for (int i = 0; i < TN; i++) _tail[i] = _tailPrev[i] = Jt[J.HandN] + new Vector2(0, i * 2 * S);
    }

    /// <summary>Haul ourselves up the rope; hand off to the normal mantle near the top.</summary>
    void RopeClimbControl(float dt, World w)
    {
        var env = w.Env;
        if (!HookAnchor(env, out var hook, out var wall))
        {
            // The window moved out from under the hook (or closed): down we go.
            StopClimb();
            Fall(true);
            return;
        }
        _hook = hook;
        _climbX = wall!.X;
        _climbTop = wall.Y1;
        Facing = -_climbSide;

        float pace = MathF.Max(0.4f, ClimbPace);
        var style = Style.Rope;
        float target = style switch
        {
            RopeStyle.Zip => 380 * S,
            RopeStyle.Haul => 140 * S,
            _ => 120 * S,
        } * pace * (1 + MathF.Min(1, (_ropeStartY - _climbTop) / (1500 * S)) * 0.4f);   // long ropes: get a rhythm going
        _ropeSpeed = M.MoveTowards(_ropeSpeed, target, (style == RopeStyle.Zip ? 600 : 400) * S * dt);
        // Hand-over-hand comes in pulls; walking up the wall comes in steps.
        float cycle = style == RopeStyle.Haul ? 0.5f / pace : 0.42f / pace;
        _ropePhase += dt / cycle;
        float pulse = style == RopeStyle.Zip ? 1 : 0.35f + 1.3f * MathF.Max(0, MathF.Sin(_ropePhase * MathF.PI));
        Base.Y -= _ropeSpeed * pulse * dt;

        // Distance out from the wall: rappellers lean out with their feet on the wall; near the top
        // everyone tucks in to the normal climbing spot to mantle.
        float topGap = Base.Y - ClimbHip - Torso - _climbTop;
        float tuck = M.Clamp01(1 - (topGap - Arm) / (Height * 0.8f));
        float outX = style == RopeStyle.Rappel ? Leg * 0.75f : 7 * S;
        Base.X = _climbX + _climbSide * M.Lerp(outX, ClimbStandOffset, tuck);

        if (Base.Y - ClimbHip - Torso - Arm * 0.7f <= _climbTop)
        {
            _mantling = true;
            _mantleT = 0;
            _mantleFrom = Base;
        }
    }

    /// <summary>Pose on the rope (facing space, like ClimbPose).</summary>
    void RopeClimbPose(ref float hipT, ref float leanT, ref float handW, ref float footW, ref Vector2 hN, ref Vector2 hF,
                       ref Vector2 fN, ref Vector2 fF, ref Vector2 eN, ref Vector2 eF, ref Vector2 kPref)
    {
        Vector2 neck = Jt[J.Neck], pel = Jt[J.Pelvis];
        Vector2 L(Vector2 v) => new(v.X * Facing, v.Y);
        float side = _climbSide;
        Vector2 corner = new(_climbX + side * 1.2f * S, _climbTop);
        float ph = _ropePhase * MathF.PI;
        handW = 30; footW = 26;
        eN = eF = new(-0.5f, 1);
        // A point on the rope line from the corner down past us, at height y.
        Vector2 dir = Vector2.Normalize(new Vector2(Base.X - corner.X, Base.Y - corner.Y) + new Vector2(0, 0.01f));
        Vector2 OnRope(float y) => corner + dir * ((y - corner.Y) / MathF.Max(0.2f, dir.Y));

        switch (Style.Rope)
        {
            case RopeStyle.Rappel:
            {
                // Walking up the wall: legs out to the wall, leaning back, hands working up the rope.
                hipT = ClimbHip * 0.55f;
                leanT = -0.35f;
                float a = MathF.Sin(ph), b = MathF.Sin(ph + MathF.PI);
                float reach = neck.Y - Arm * 0.55f;
                hN = L(OnRope(reach - MathF.Max(0, a) * Arm * 0.3f) - neck);
                hF = L(OnRope(reach + Arm * 0.25f - MathF.Max(0, b) * Arm * 0.3f) - neck);
                float wallX = _climbX + side * 0.6f * S;
                fN = L(new Vector2(wallX, pel.Y + Leg * 0.15f - MathF.Max(0, a) * Leg * 0.3f) - pel);
                fF = L(new Vector2(wallX, pel.Y + Leg * 0.15f - MathF.Max(0, b) * Leg * 0.3f) - pel);
                kPref = new(1, -1);
                break;
            }
            case RopeStyle.Zip:
            {
                // Reeled up fast: both hands overhead on the rope, legs trailing together.
                hipT = ClimbHip;
                leanT = -0.05f;
                hN = L(OnRope(neck.Y - Arm * 0.92f) - neck);
                hF = L(OnRope(neck.Y - Arm * 0.8f) - neck);
                float sway = MathF.Sin(_time * 7) * 2 * S;
                fN = new(-1 * S + sway, Leg * 0.95f);
                fF = new(-3 * S + sway, Leg * 0.92f);
                kPref = new(1, -0.2f);
                break;
            }
            default:
            {
                // Hand over hand, hanging: one hand reaches high while the body pulls up; legs pump.
                hipT = ClimbHip;
                leanT = 0.02f;
                float a = MathF.Max(0, MathF.Sin(ph)), b = MathF.Max(0, MathF.Sin(ph + MathF.PI));
                hN = L(OnRope(neck.Y - Arm * (0.45f + 0.45f * a)) - neck);
                hF = L(OnRope(neck.Y - Arm * (0.45f + 0.45f * b)) - neck);
                float k = MathF.Sin(ph);
                fN = new(4 * S + k * 3 * S, Leg * (0.8f - 0.15f * MathF.Max(0, k)));
                fF = new(2 * S - k * 3 * S, Leg * (0.8f - 0.15f * MathF.Max(0, -k)));
                kPref = new(1, -0.4f);
                break;
            }
        }
    }

    /// <summary>Arm targets while spinning, throwing, walking in on a taut rope, or reeling in.</summary>
    void GrapplePose(ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF, ref float handW, ref float leanT)
    {
        if (_gp == GrapplePhase.None || Climbing) return;
        Vector2 neck = Jt[J.Neck];
        Vector2 L(Vector2 v) => new(v.X * Facing, v.Y);
        switch (_gp)
        {
            case GrapplePhase.Spin:
                handW = 34;
                if (Style.Spin == GrappleSpin.Overhead)
                {
                    hN = new(Arm * 0.2f + MathF.Cos(_spinA) * 3 * S, -Arm * 0.9f + MathF.Sin(_spinA) * 1.5f * S);
                    eN = new(1, 0.4f);
                }
                else if (Style.Spin == GrappleSpin.SideWhirl)
                {
                    hN = new(Arm * 0.45f + MathF.Cos(_spinA) * 4 * S, Arm * 0.15f + MathF.Sin(_spinA) * 4 * S);
                    eN = new(-1, 0.5f);
                }
                else
                {
                    // Wind up behind, ready to toss.
                    float k = M.Clamp01(_gT / _spinDur);
                    hN = new(M.Lerp(Arm * 0.3f, -Arm * 0.6f, k), M.Lerp(Arm * 0.5f, Arm * 0.1f, k));
                    leanT = -0.1f * k;
                }
                hF = new(Arm * 0.35f, Torso * 0.4f);   // the other hand holds the coil
                eF = new(-1, 0.6f);
                break;
            case GrapplePhase.Fly:
            {
                // Follow-through toward the hook, then pay out the rope.
                Vector2 d = _hook - neck;
                Vector2 dirL = d.LengthSquared() > 1 ? Vector2.Normalize(L(d)) : new(1, 0);
                hN = _gT < 0.3f ? dirL * Arm * 0.95f : new(Arm * 0.55f, Torso * 0.1f);
                hF = new(Arm * 0.3f, Torso * 0.4f);
                leanT = _gT < 0.3f ? 0.15f : 0.02f;
                handW = 30;
                break;
            }
            case GrapplePhase.Approach:
            {
                // Both hands on the taut rope, leaning back a touch as we walk in.
                Vector2 corner = new(_gWallX, _hook.Y);
                Vector2 d = corner - neck;
                Vector2 dirL = d.LengthSquared() > 1 ? Vector2.Normalize(L(d)) : new(0.5f, -0.8f);
                hN = dirL * Arm * 0.8f;
                hF = dirL * Arm * 0.55f + new Vector2(0, 2 * S);
                eN = eF = new(-0.4f, 1);
                leanT = -0.08f;
                handW = 22;
                break;
            }
            case GrapplePhase.Retract:
            {
                // Quick hand-over-hand reel-in.
                float k = MathF.Sin(_gT * 22);
                hN = new(Arm * (0.55f + 0.2f * k), Torso * (0.1f - 0.2f * k));
                hF = new(Arm * (0.55f - 0.2f * k), Torso * (0.1f + 0.2f * k));
                handW = 30;
                break;
            }
        }
    }

    // ---------------- rope simulation + drawing ----------------

    void InitRope(Vector2 a, Vector2 b)
    {
        for (int i = 0; i < RN; i++) _rp[i] = _rpPrev[i] = Vector2.Lerp(a, b, i / (RN - 1f));
        _ropeInit = true;
    }

    /// <summary>Verlet rope from the hand to its far end (hook, or the window corner it runs over).</summary>
    void StepRope(float dt)
    {
        if (!Grappling || Mode == Mode.Spawning) { _ropeInit = false; return; }
        Vector2 hand = Jt[J.HandN];
        Vector2 end = RopeEnd();
        if (!_ropeInit) InitRope(hand, end);
        float dist = Vector2.Distance(hand, end);
        float slack = _gp switch { GrapplePhase.Fly => 1.04f, GrapplePhase.Retract => 1.12f, GrapplePhase.Spin => 1.02f, _ => 1.0f };
        float seg = MathF.Max(dist * slack / (RN - 1), 0.5f * S);
        Vector2 g = new(0, Gravity * 0.5f * dt * dt);
        for (int i = 1; i < RN - 1; i++)
        {
            Vector2 v = (_rp[i] - _rpPrev[i]) * 0.97f;
            _rpPrev[i] = _rp[i];
            _rp[i] += v + g;
        }
        for (int it = 0; it < 10; it++)
        {
            _rp[0] = hand; _rp[RN - 1] = end;
            for (int i = 0; i < RN - 1; i++)
            {
                Vector2 d = _rp[i + 1] - _rp[i];
                float len = d.Length();
                if (len < 1e-4f) continue;
                Vector2 c = d * ((len - seg) / len * 0.5f);
                if (i > 0) _rp[i] += c; else _rp[i + 1] -= c;
                if (i + 1 < RN - 1) _rp[i + 1] -= c; else _rp[i] += c;
            }
        }
        _rp[0] = hand; _rp[RN - 1] = end;

        // The climbed rope hangs below the hands.
        if (_onRope)
        {
            float tailLen = Math.Clamp(_ropeStartY - Base.Y, 6 * S, 90 * S);
            float ts = tailLen / (TN - 1);
            for (int i = 1; i < TN; i++)
            {
                Vector2 v = (_tail[i] - _tailPrev[i]) * 0.96f;
                _tailPrev[i] = _tail[i];
                _tail[i] += v + g;
            }
            for (int it = 0; it < 6; it++)
            {
                _tail[0] = hand;
                for (int i = 0; i < TN - 1; i++)
                {
                    Vector2 d = _tail[i + 1] - _tail[i];
                    float len = d.Length();
                    if (len < 1e-4f) continue;
                    Vector2 c = d * ((len - ts) / len);
                    if (i == 0) _tail[i + 1] -= c; else { _tail[i] += c * 0.5f; _tail[i + 1] -= c * 0.5f; }
                }
            }
        }
    }

    bool RopeOverEdge => _onRope || _gp == GrapplePhase.Approach;
    Vector2 RopeEnd() => RopeOverEdge ? new Vector2((_onRope ? _climbX : _gWallX) + _gSide * 1.2f * S, _hook.Y) : _hook;

    static readonly Color4 RopeColor = new(0.62f, 0.47f, 0.28f, 1), RopeDark = new(0.25f, 0.18f, 0.1f, 0.55f), Steel = new(0.32f, 0.34f, 0.38f, 1);

    void DrawGrapple(Renderer r)
    {
        if (!Grappling || !_ropeInit || Mode == Mode.Spawning) return;
        float w = 1.5f * S;
        for (int i = 0; i < RN - 1; i++) r.Line(_rp[i], _rp[i + 1], RopeDark, w + 1.2f * S);
        for (int i = 0; i < RN - 1; i++) r.Line(_rp[i], _rp[i + 1], RopeColor, w);
        if (RopeOverEdge)
        {
            // Over the lip and along the top to the hook.
            r.Line(_rp[RN - 1], _hook, RopeDark, w + 1.2f * S);
            r.Line(_rp[RN - 1], _hook, RopeColor, w);
        }
        if (_onRope)
            for (int i = 0; i < TN - 1; i++) r.Line(_tail[i], _tail[i + 1], RopeColor, w);
        DrawHook(r);
    }

    void DrawHook(Renderer r)
    {
        // Direction the shank points: along the flight, or flat on the window once caught.
        Vector2 dir = _gp == GrapplePhase.Fly && _hookVel.LengthSquared() > 1 ? Vector2.Normalize(_hookVel)
                    : _hookCaught || RopeOverEdge ? new Vector2(-_gSide, 0)
                    : Vector2.Normalize(_hook - (RN > 1 ? _rp[RN - 2] : _hook) + new Vector2(0, 0.001f));
        Vector2 n = new(-dir.Y, dir.X);
        float s = S;
        Vector2 tip = _hook + dir * 4 * s;
        r.Line(_hook - dir * 3 * s, tip, Steel, 1.8f * s);
        // Three prongs curling back.
        foreach (float k in new[] { -1f, 0f, 1f })
        {
            Vector2 side = k == 0 ? -n * 0.6f : n * k;
            Vector2 mid = tip + side * 3.5f * s - dir * 0.5f * s;
            Vector2 end = mid - dir * 2.5f * s + side * 0.5f * s;
            r.Line(tip, mid, Steel, 1.5f * s);
            r.Line(mid, end, Steel, 1.3f * s);
        }
        r.Ring(_hook - dir * 3.5f * s, 1.2f * s, Steel, 0.9f * s);
    }

    /// <summary>Area the rope and hook cover (for redraw regions).</summary>
    public System.Drawing.RectangleF? GrappleBounds()
    {
        if (!Grappling || !_ropeInit) return null;
        float x1 = _hook.X, y1 = _hook.Y, x2 = _hook.X, y2 = _hook.Y;
        foreach (var p in _rp) { x1 = MathF.Min(x1, p.X); y1 = MathF.Min(y1, p.Y); x2 = MathF.Max(x2, p.X); y2 = MathF.Max(y2, p.Y); }
        if (_onRope) foreach (var p in _tail) { x1 = MathF.Min(x1, p.X); y1 = MathF.Min(y1, p.Y); x2 = MathF.Max(x2, p.X); y2 = MathF.Max(y2, p.Y); }
        float pad = 9 * S;
        return System.Drawing.RectangleF.FromLTRB(x1 - pad, y1 - pad, x2 + pad, y2 + pad);
    }
}
