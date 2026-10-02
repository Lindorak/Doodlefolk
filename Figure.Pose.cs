using System.Numerics;

namespace StickFight;

/// <summary>Procedural animation. Each action produces pose targets in "facing space" (+x = forward);
/// springs smooth everything in world space so transitions and turns blend naturally.
/// Planted feet come from the step planner; elbows and knees are solved with two-bone IK.</summary>
sealed partial class Figure
{
    float _hip, _hipV, _lean, _leanV, _tilt, _tiltV, _pdx, _pdxV;
    Vector2 _hN, _hNV, _hF, _hFV;              // hand offsets from the shoulder (world space)
    Vector2 _fNRel, _fNRelV, _fFRel, _fFRelV;  // free foot offsets from the pelvis (in air / sitting)
    bool _feetFree;
    readonly Foot _fN = new(), _fF = new();

    void ResetPose()
    {
        _hip = StandHip; _hipV = 0;
        _lean = 0; _leanV = 0; _tilt = 0; _tiltV = 0; _pdx = 0; _pdxV = 0;
        _hN = new(2 * S * Facing, Arm * 0.93f); _hNV = default;
        _hF = new(-1.5f * S * Facing, Arm * 0.93f); _hFV = default;
        _fN.Pos = new(Base.X + Facing * 5.5f * S, Base.Y); _fN.Stepping = false;
        _fF.Pos = new(Base.X - Facing * 5.5f * S, Base.Y); _fF.Stepping = false;
        _feetFree = false;
        Pose(1 / 120f);
        Array.Copy(Jt, _jtPrev, J.Count);
    }

    Vector2 W(Vector2 local) => new(local.X * Facing, local.Y);

    void Pose(float dt)
    {
        if (Mode == Mode.Control && Grounded && Action is Act.SitFront or Act.SitBack) { FrontPose(dt); return; }
        float speed = MathF.Abs(Vel.X);
        float run = M.Clamp01((speed - 90 * S) / (120 * S));
        bool sitting = Grounded && Action is Act.SitEdge or Act.SitFloor or Act.Lie or Act.Curl or Act.Eat or Act.Read or Act.Warm;
        bool feetFree = !Grounded || sitting;
        float br = MathF.Sin(_time * 2.3f + Id);

        float hipT = StandHip, hipW = 16, hipZ = 0.5f;
        float leanT = 0.02f + br * 0.012f, leanW = 10;
        float tiltT = 0, pdxT = 0, handW = 13, footW = 18;
        Vector2 hN = new(2 * S + br * 0.3f * S, Arm * 0.93f), hF = new(-1.5f * S, Arm * 0.93f);
        Vector2 eN = new(-1, 0.25f), eF = new(-1, 0.25f);
        Vector2 kPref = new(1, -0.15f);
        Vector2 fN = default, fF = default;

        if (Climbing)
        {
            ClimbPose(ref hipT, ref leanT, ref handW, ref footW, ref hN, ref hF, ref fN, ref fF, ref eN, ref eF, ref kPref);
        }
        else if (Mode == Mode.GetUp)
        {
            hipW = 7; hipZ = 1; leanW = 6; leanT = 0;
            if (_getUpT < 0.45f) { hN = new(Leg * 0.3f, Torso * 0.95f); hF = new(Leg * 0.25f, Torso * 0.95f); }
        }
        else if (Grounded)
        {
            if (JumpPending)
            {
                hipT = StandHip * 0.6f; leanT = 0.4f; handW = 20;
                hN = new(-Arm * 0.55f, Arm * 0.6f); hF = new(-Arm * 0.65f, Arm * 0.55f);
            }
            else if (Action == Act.Fight || HitStun > 0 || DuckT > 0 || BlockT > 0)
            {
                FightPose(ref hipT, ref leanT, ref handW, ref hN, ref hF, ref eN, ref eF, ref tiltT);
            }
            else if (SuperLandingPose(ref hipT, ref leanT, ref hN, ref hF, ref eN)) { }
            else if (ItemActPose(ref hipT, ref leanT, ref tiltT, ref handW, ref hN, ref hF, ref fN, ref fF, ref eN, ref eF, ref kPref)) { }
            else switch (Action)
            {
                case Act.Fidget:
                    FidgetPose(ref hipT, ref leanT, ref tiltT, ref pdxT, ref handW, ref hN, ref hF, ref eN, ref eF);
                    break;
                case Act.SitEdge:
                {
                    hipT = 1.2f * S; leanT = -0.12f + br * 0.01f;
                    float k1 = MathF.Sin(_time * 2.6f + Id), k2 = MathF.Sin(_time * 2.6f + Id + 2.1f);
                    fN = new(Thigh * 0.95f + k1 * 2.5f * S, Shin * 0.92f);
                    fF = new(Thigh * 0.9f + k2 * 2.5f * S, Shin * 0.92f);
                    kPref = new(1, -1);
                    hN = new(-6 * S, Torso + 0.5f * S); hF = new(-8 * S, Torso + 0.5f * S);
                    eN = eF = new(-1, 0);
                    break;
                }
                case Act.SitFloor:
                    hipT = 2 * S; leanT = 0.1f + br * 0.01f;
                    fN = new(Leg * 0.62f, 2 * S); fF = new(Leg * 0.55f, 2 * S);
                    kPref = new(0.3f, -1);
                    hN = new(6.5f * S, 10.5f * S); hF = new(5.5f * S, 10.5f * S);
                    eN = eF = new(-1, 0.3f);
                    break;
                case Act.Lie:
                    hipT = 3.2f * S + MathF.Sin(_time * 1.6f) * 0.4f * S; leanT = 1.45f; leanW = 3;
                    fN = new(-Leg * 0.95f, 2.5f * S); fF = new(-Leg * 0.9f, 3 * S);
                    kPref = new(0, -1);
                    hN = new(-Torso * 0.5f, -0.5f * S); hF = new(Arm * 0.5f, 4.5f * S);
                    eN = eF = new(0, -1);
                    break;
                case Act.Kick:
                    hN = new(-Arm * 0.45f, Arm * 0.55f); hF = new(Arm * 0.55f, Arm * 0.3f);
                    leanT = ActionT < KickTime * KickContact ? 0.05f : -0.14f; handW = 22;
                    break;
                case Act.Tap:
                    hN = new(Arm * 0.5f, Arm * 0.45f); hF = new(-Arm * 0.5f, Arm * 0.45f);
                    eN = eF = new(0, 1); handW = 18;
                    break;
                case Act.Throw:
                {
                    float p = ActionT / ThrowTime;
                    handW = 30;
                    if (p < ThrowRelease) { hN = new(-Arm * 0.55f, -Arm * 0.62f); hF = new(-Arm * 0.45f, -Arm * 0.7f); leanT = -0.18f; }
                    else { hN = new(Arm * 0.85f, -Arm * 0.3f); hF = new(Arm * 0.6f, -Arm * 0.05f); leanT = 0.22f; }
                    eN = eF = new(-0.5f, -1);
                    break;
                }
                case Act.Talk:
                    hN = new(Arm * 0.42f + MathF.Sin(ActionT * 7) * Arm * 0.13f, Torso * 0.35f + MathF.Cos(ActionT * 5.3f) * Arm * 0.13f);
                    eN = new(-1, 0.8f); handW = 16;
                    tiltT = MathF.Sin(ActionT * 6) * 0.07f;
                    break;
                case Act.HighFive:
                    hN = new(Arm * 0.55f, -Arm * 0.72f); eN = new(0.2f, 1); handW = 24; leanT = 0.08f;
                    break;
                case Act.Ready:
                    hN = new(Arm * 0.6f, Torso * 0.15f); hF = new(Arm * 0.55f, Torso * 0.25f);
                    eN = eF = new(-0.3f, 1); hipT = StandHip * 0.93f; handW = 20;
                    break;
                case Act.HandsHips:
                    hN = new(1.5f * S, Torso * 0.85f); hF = new(1 * S, Torso * 0.85f);
                    eN = eF = new(-1, 0); leanT = -0.05f;
                    break;
                case Act.Wave:
                    hN = new(Arm * 0.3f + MathF.Sin(ActionT * 13) * Arm * 0.25f, -Arm * 0.75f);
                    eN = new(1, 0.6f); handW = 22;
                    break;
                case Act.Cheer:
                    CelebratePose(ref hipT, ref leanT, ref tiltT, ref pdxT, ref handW, ref hN, ref hF, ref eN, ref eF);
                    break;
                case Act.Swat:
                {
                    float p = ActionT / 0.32f;
                    handW = 34;
                    if (p < 0.4f) { hN = new(-Arm * 0.4f, -Arm * 0.7f); leanT = -0.1f; }
                    else { hN = SwatDir() * Arm * 0.98f; leanT = 0.2f; }
                    eN = new(-0.3f, 1);
                    break;
                }
                default:
                    if (speed > 8 * S) WalkPose(speed, run, ref hipT, ref leanT, ref tiltT, ref handW, ref hN, ref hF, ref eN, ref eF);
                    else IdlePose(br, ref hipT, ref leanT, ref tiltT, ref pdxT, ref handW, ref hN, ref hF, ref eN, ref eF);
                    break;
            }
        }
        else
        {
            hipT = StandHip; handW = 20; eN = eF = new(-0.5f, 1);
            if (Atk is { Kind: AttackKind.FlyingKick })
            {
                FlyingKickPose(ref hN, ref hF, ref fN, ref fF, ref leanT, ref footW, ref handW);
            }
            else if (Flipping)
            {
                // Tucked: knees to chest, hands on shins.
                hN = new(Arm * 0.45f, Arm * 0.55f); hF = new(Arm * 0.35f, Arm * 0.6f);
                fN = new(6 * S, Leg * 0.42f); fF = new(4 * S, Leg * 0.48f);
                kPref = new(1, -0.6f); leanT = 0.15f; handW = 30; footW = 30;
            }
            else if (Flailing)
            {
                hN = M.Dir(_time * 16 + Id) * Arm * 0.85f; hF = M.Dir(_time * 16 + Id + 2.6f) * Arm * 0.85f;
                float k = MathF.Sin(_time * 14);
                fN = new(4 * S + k * 4 * S, Leg * 0.8f); fF = new(-3 * S - k * 4 * S, Leg * 0.75f);
                leanT = -0.15f; handW = 26;
            }
            else if (JumpStylePose(Vel.Y < -150 * S, ref leanT, ref hN, ref hF, ref fN, ref fF)) { }
            else if (Vel.Y < -150 * S)
            {
                hN = new(Arm * 0.4f, -Arm * 0.75f); hF = new(Arm * 0.15f, -Arm * 0.8f);
                fN = new(4 * S, Leg * 0.55f); fF = new(-3 * S, Leg * 0.72f);
                leanT = 0.12f;
            }
            else
            {
                hN = new(Arm * 0.8f, -Arm * 0.2f); hF = new(-Arm * 0.75f, -Arm * 0.05f);
                fN = new(5 * S, Leg * 0.88f); fF = new(-4 * S, Leg * 0.84f);
                leanT = 0;
            }
        }

        AimAtCursor(ref hN, ref hF, ref handW);
        CarryArms(ref hN, ref hF, ref eN, ref eF, ref handW);
        GrapplePose(ref hN, ref hF, ref eN, ref eF, ref handW, ref leanT);

        if (LookAt is Vector2 la && Action != Act.Lie)
        {
            Vector2 d = la - Jt[J.Head];
            if (MathF.Abs(d.X) < 5 || MathF.Sign(d.X) == Facing)
                tiltT += Math.Clamp(MathF.Atan2(d.Y, MathF.Abs(d.X) + 1) * 0.55f, -0.5f, 0.55f) * Facing;
        }
        else if (Climbing) tiltT = -0.3f * Facing;   // look up the wall
        if (HeadShake) tiltT += MathF.Sin(_time * 20) * 0.22f;

        M.Spring(ref _hip, ref _hipV, hipT, hipW, hipZ, dt);
        M.Spring(ref _lean, ref _leanV, leanT * Facing, leanW, 0.9f, dt);
        M.Spring(ref _tilt, ref _tiltV, tiltT, 12, 0.8f, dt);
        M.Spring(ref _pdx, ref _pdxV, pdxT * Facing, 4, 1, dt);
        M.Spring(ref _hN, ref _hNV, W(hN), handW, 0.8f, dt);
        M.Spring(ref _hF, ref _hFV, W(hF), handW, 0.8f, dt);

        if (feetFree && !_feetFree)
        {
            _fNRel = Jt[J.FootN] - Jt[J.Pelvis]; _fNRelV = default;
            _fFRel = Jt[J.FootF] - Jt[J.Pelvis]; _fFRelV = default;
        }
        else if (!feetFree && _feetFree)
        {
            _fN.Pos = new(Jt[J.FootN].X, Base.Y); _fN.Stepping = false;
            _fF.Pos = new(Jt[J.FootF].X, Base.Y); _fF.Stepping = false;
        }
        _feetFree = feetFree;

        float hip = _hip, bob = 0;
        if (!feetFree)
        {
            float px = Base.X + _pdx;
            float dx = MathF.Max(MathF.Abs(FootPos(_fN).X - px), MathF.Abs(FootPos(_fF).X - px));
            // Sink a little to keep both feet planted, but never into a crouch: past that the
            // trailing foot lifts onto its toes instead (IK clamps its reach).
            float reach = MathF.Sqrt(MathF.Max(0, Leg * Leg * 0.97f - dx * dx));
            if (reach < hip) hip = MathF.Max(reach, MathF.Min(hip, StandHip * 0.88f));
            if (_fN.Stepping) bob += MathF.Sin(MathF.PI * _fN.T) * _fN.Lift;
            if (_fF.Stepping) bob += MathF.Sin(MathF.PI * _fF.T) * _fF.Lift;
            bob *= 0.3f * Style.Bounce * (1 + Mood.Happy * 0.4f) * (1 - Mood.Tired * 0.4f);
            // Limp: the body dips while the weight is on the hurt (near) leg.
            if (Mood.Hurt > 0.3f && !_fN.Stepping && _fF.Stepping) bob -= Mood.Hurt * 3 * S * MathF.Sin(MathF.PI * _fF.T);
        }

        Vector2 pelvis = new(Base.X + _pdx, Base.Y - hip - bob);
        Vector2 neck = pelvis + M.Dir(_lean) * Torso;
        Vector2 head = neck + M.Dir(_lean + _tilt) * (HeadR + NeckGap);

        var (elN, handN) = M.IK(neck, neck + M.ClampLength(_hN, Arm * 0.995f), UpperArm, ForeArm, W(eN));
        var (elF, handF) = M.IK(neck, neck + M.ClampLength(_hF, Arm * 0.995f), UpperArm, ForeArm, W(eF));

        Vector2 footN, footF;
        if (feetFree)
        {
            M.Spring(ref _fNRel, ref _fNRelV, W(fN), footW, 0.75f, dt);
            M.Spring(ref _fFRel, ref _fFRelV, W(fF), footW, 0.75f, dt);
            footN = pelvis + _fNRel;
            footF = pelvis + _fFRel;
        }
        else
        {
            footN = FidgetFoot(FootPos(_fN));
            footF = FootPos(_fF);
            if (Action is Act.Kick or Act.Tap || AttackUsesFoot)
            {
                Vector2 planted = footN - pelvis;
                Vector2 local = new(planted.X * Facing, planted.Y);
                footN = pelvis + W(AttackUsesFoot ? AttackFootLocal(local) : KickFootLocal(local));
                if (AttackUsesFoot && AimFootAtCursor(pelvis) is Vector2 aimed) footN = aimed;
            }
        }
        var (knN, fNEnd) = M.IK(pelvis, footN, Thigh, Shin, W(kPref));
        var (knF, fFEnd) = M.IK(pelvis, footF, Thigh, Shin, W(kPref));

        Jt[J.Head] = head; Jt[J.Neck] = neck; Jt[J.Pelvis] = pelvis;
        Jt[J.ElbowN] = elN; Jt[J.HandN] = handN; Jt[J.ElbowF] = elF; Jt[J.HandF] = handF;
        Jt[J.KneeN] = knN; Jt[J.FootN] = fNEnd; Jt[J.KneeF] = knF; Jt[J.FootF] = fFEnd;
        UpdateHoldPoint(neck, handN, handF, dt);
        ApplyFlip();
    }

    Vector2 SwatDir()
    {
        if (LookAt is not Vector2 la) return new(0.8f, -0.6f);
        Vector2 d = la - Jt[J.Neck];
        Vector2 local = new(d.X * Facing, d.Y);
        if (local.X < 0) local.X = 0;
        return local.LengthSquared() > 1 ? Vector2.Normalize(local) : new(1, 0);
    }
}
