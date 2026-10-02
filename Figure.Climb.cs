using System.Numerics;

namespace Doodlefolk;

/// <summary>Climbing window edges hand-over-hand, then mantling over the top onto the window.
/// Each climbing "move" is a reach (one hand releases and grabs higher) followed by a pull (the body
/// hauls itself up while the opposite foot steps up). Holds are fixed in world space so limbs don't slide.</summary>
sealed partial class Figure
{
    public bool Climbing { get; private set; }
    public float ClimbDir = 1;
    public float ClimbStandOffset => 9 * S;

    IntPtr _climbHwnd;
    int _climbSide;
    float _climbX, _climbTop;

    /// <summary>Climbing speed multiplier, set by the brain from energy, urgency and tiredness.</summary>
    public float ClimbPace = 1;

    float _cyc, _cycY0;
    int _lead;                                              // 0: near hand + far foot move; 1: far hand + near foot
    bool _dyno;                                             // a leap: both hands and feet move, double height
    readonly float[] _handY = new float[2], _footY = new float[2];       // world-space hold heights [near, far]
    readonly float[] _handFrom = new float[2], _footFrom = new float[2];

    bool _mantling;
    float _mantleT;
    Vector2 _mantleFrom;

    float ClimbHip => StandHip * 0.8f;
    float Rise => Arm * (0.6f + 0.08f * MathF.Min(ClimbPace, 1.6f)) * Style.ClimbRise * (_dyno ? 1.7f : 1);
    float CycleTime => (0.42f - 0.14f * Traits.Energy) / MathF.Max(0.3f, ClimbPace) * Style.ClimbCycle * (_dyno ? 1.3f : 1);
    float HandReach => ClimbHip + Torso + Arm * 0.82f;     // Base.Y minus this = height of a fresh hand hold

    public bool StartClimb(Wall wall)
    {
        if (!Grounded || Mode != Mode.Control) return false;
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

        float neckY = Base.Y - ClimbHip - Torso;
        _handY[0] = neckY - Arm * 0.35f;
        _handY[1] = Base.Y - HandReach;
        _footY[0] = Base.Y;
        _footY[1] = Base.Y - Leg * 0.3f;
        _lead = 0;
        BeginMove();
        return true;
    }

    void BeginMove()
    {
        _cyc = 0;
        _cycY0 = Base.Y;
        _dyno = _rng.NextDouble() < Style.DynoChance * (ClimbPace > 1.1f ? 1.2f : 0.6f);
        Array.Copy(_handY, _handFrom, 2);
        Array.Copy(_footY, _footFrom, 2);
    }

    void StopClimb()
    {
        Climbing = false;
        _mantling = false;
        if (_onRope) { _onRope = false; StartRetract(); }
    }

    void ShiftClimb(Vector2 d)
    {
        _cycY0 += d.Y; _mantleFrom += d;
        for (int i = 0; i < 2; i++) { _handY[i] += d.Y; _footY[i] += d.Y; _handFrom[i] += d.Y; _footFrom[i] += d.Y; }
        _climbX += d.X;
        _climbTop += d.Y;
    }

    void ClimbControl(float dt, World w)
    {
        var env = w.Env;
        if (_mantling) { MantleControl(dt, env); return; }
        if (_onRope) { RopeClimbControl(dt, w); return; }

        var wall = env.WallAt(_climbHwnd, _climbSide, Base.Y - Height * 0.5f);
        if (wall == null) { StopClimb(); Fall(true); return; }
        _climbX = wall.X;
        _climbTop = wall.Y1;
        Base.X = _climbX + _climbSide * ClimbStandOffset;
        Facing = -_climbSide;
        if (ClimbDir <= 0) return;

        _cyc += dt / CycleTime;
        float p = MathF.Min(_cyc, 1), rise = Rise;
        float handTop = _cycY0 - rise - HandReach, footTop = _cycY0 - rise - ClimbHip + Leg * 0.38f;
        // Sag while reaching (a deeper crouch before a leap), then the pull.
        Base.Y = _cycY0 - rise * M.Smooth((p - 0.3f) / 0.6f) + MathF.Sin(MathF.PI * M.Clamp01(p / 0.35f)) * (_dyno ? 3 : 1.2f) * S;
        if (_dyno)
        {
            // Leap: both hands let go mid-rise and catch higher; both feet follow.
            for (int i = 0; i < 2; i++)
            {
                _handY[i] = M.Lerp(_handFrom[i], handTop + (i == _lead ? 0 : Arm * 0.15f), M.Smooth((p - 0.3f) / 0.4f));
                _footY[i] = M.Lerp(_footFrom[i], footTop + (i == _lead ? Leg * 0.2f : 0), M.Smooth((p - 0.45f) / 0.45f));
            }
        }
        else
        {
            _handY[_lead] = M.Lerp(_handFrom[_lead], handTop, M.Smooth(p / 0.4f));
            _footY[1 - _lead] = M.Lerp(_footFrom[1 - _lead], footTop, M.Smooth((p - 0.35f) / 0.5f));
        }
        if (_cyc < 1) return;

        // Move finished. If the top edge is within reach, go over; otherwise switch sides and continue.
        if (Base.Y - ClimbHip - Torso - Arm * 0.7f <= _climbTop)
        {
            if (!wall.ReachesTop)
            {
                StopClimb();
                Vel = new(_climbSide * 60 * S, 0);
                Fall(false);
                return;
            }
            _mantling = true;
            _mantleT = 0;
            _mantleFrom = Base;
            return;
        }
        _lead = 1 - _lead;
        BeginMove();
    }

    // Mantle: A) pull chest over the lip, B) knee onto the ledge while shifting over, C) stand up.
    const float MantleTime = 0.85f;

    void MantleControl(float dt, Env env)
    {
        _mantleT += dt / MantleTime;
        float m = M.Clamp01(_mantleT);
        float edgeX = _climbX - _climbSide * 5 * S;
        float pullY = _climbTop + StandHip * 0.4f;
        if (m < 0.4f) Base = new(_mantleFrom.X, M.Lerp(_mantleFrom.Y, pullY, M.Smooth(m / 0.4f)));
        else Base = new(M.Lerp(_mantleFrom.X, edgeX, M.Smooth((m - 0.4f) / 0.3f)), M.Lerp(pullY, _climbTop, M.Smooth((m - 0.4f) / 0.3f)));
        if (_mantleT < 1) return;

        StopClimb();
        if (env.SupportAt(Base.X, _climbTop, _climbHwnd) is { } p)
        {
            Grounded = true;
            GroundHwnd = p.Hwnd;
            Base = new(edgeX, p.Y);
            Vel = default;
            Brain.OnClimbed();
        }
        else Fall(true);
    }

    /// <summary>Pose targets while climbing or mantling (facing space).</summary>
    void ClimbPose(ref float hipT, ref float leanT, ref float handW, ref float footW, ref Vector2 hN, ref Vector2 hF,
                   ref Vector2 fN, ref Vector2 fF, ref Vector2 eN, ref Vector2 eF, ref Vector2 kPref)
    {
        Vector2 neck = Jt[J.Neck], pel = Jt[J.Pelvis];
        Vector2 L(Vector2 v) => new(v.X * Facing, v.Y);
        handW = 40;
        footW = 30;
        eN = eF = new(-0.4f, 1);
        float side = _climbSide;
        if (_onRope && !_mantling)
        {
            RopeClimbPose(ref hipT, ref leanT, ref handW, ref footW, ref hN, ref hF, ref fN, ref fF, ref eN, ref eF, ref kPref);
            return;
        }

        if (_mantling)
        {
            float m = M.Clamp01(_mantleT);
            Vector2 lip1 = new(_climbX - side * 2 * S, _climbTop), lip2 = new(_climbX - side * 5 * S, _climbTop);
            if (m < 0.75f)
            {
                hN = L(lip1 - neck);
                hF = L(lip2 - neck);
            }
            else
            {
                hN = new(2 * S, Arm * 0.9f);
                hF = new(-1.5f * S, Arm * 0.9f);
            }
            if (m < 0.4f)
            {
                hipT = StandHip * 0.45f;
                leanT = M.Lerp(0.15f, 0.55f, m / 0.4f);
                fN = new(3 * S, Leg * 0.7f);
                fF = new(1 * S, Leg * 0.85f);
                kPref = new(1, -0.2f);
            }
            else if (m < 0.7f)
            {
                hipT = StandHip * 0.45f;
                leanT = 0.55f;
                fN = L(new Vector2(_climbX - side * 3 * S, _climbTop) - pel);   // knee up onto the ledge
                fF = new(1 * S, Leg * 0.8f);
                kPref = new(1, -1);
            }
            else
            {
                hipT = StandHip;
                leanT = M.Lerp(0.55f, 0.03f, (m - 0.7f) / 0.3f);
                fN = L(new Vector2(Base.X + Facing * 4 * S, _climbTop) - pel);
                fF = L(new Vector2(Base.X - Facing * 4 * S, _climbTop) - pel);
                kPref = new(1, -0.3f);
            }
            return;
        }

        float p = M.Clamp01(_cyc);
        float hx = _climbX + side * 1.2f * S, fx = _climbX + side * 1.5f * S;
        // The moving hand swings out from the wall on its way up; the moving foot lifts its knee out.
        float handArc = MathF.Sin(MathF.PI * M.Clamp01(p / 0.4f)) * 5 * S;
        float footArc = MathF.Sin(MathF.PI * M.Clamp01((p - 0.35f) / 0.5f)) * 6 * S * Style.ClimbFlail;
        if (_dyno)
        {
            handArc = MathF.Sin(MathF.PI * M.Clamp01((p - 0.3f) / 0.4f)) * 6 * S;
            footArc = MathF.Sin(MathF.PI * M.Clamp01((p - 0.45f) / 0.45f)) * 7 * S;
        }
        Vector2 Hand(int i) => new(hx + side * (_dyno || i == _lead ? handArc : 0), _handY[i]);
        Vector2 Foot(int i) => new(fx + side * (_dyno || i == 1 - _lead ? footArc : 0), _footY[i]);
        hN = L(Hand(0) - neck);
        hF = L(Hand(1) - neck);
        fN = L(Foot(0) - pel);
        fF = L(Foot(1) - pel);
        hipT = ClimbHip;
        leanT = -0.12f + 0.12f * MathF.Sin(MathF.PI * M.Clamp01((p - 0.3f) / 0.6f));   // hang back, lean in on the pull
        kPref = new(1, -0.3f);
    }
}
