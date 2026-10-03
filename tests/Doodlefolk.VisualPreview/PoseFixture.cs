using System.Numerics;
namespace Doodlefolk;

/// <summary>One stationary, unadorned, healthy fixture, not a World/Figure replacement.
/// The generated partial holds exact source slices for dimensions, target defaults, the affected
/// fidget case, springs, and IK. No brain, foot planner, window integration, or GPU renderer is run.</summary>
sealed partial class PoseFixture
{
    readonly bool _before;
    readonly float S;
    readonly int Facing;
    readonly RunStyle _runStyle;
    const int Id = 1;
    float HeadR, NeckGap, Torso, UpperArm, ForeArm, Thigh, Shin, LineW;
    float Arm => UpperArm + ForeArm;
    float Leg => Thigh + Shin;
    float StandHip => Leg * .95f;
    float _time, _hip, _hipV, _lean, _leanV, _tilt, _tiltV, _pdx, _pdxV;
    Vector2 _hN, _hNV, _hF, _hFV;
    readonly Vector2 Base = new(0, 0);
    public readonly Vector2[] Jt = new Vector2[11];
    Vector2 W(Vector2 p) => new(p.X * Facing, p.Y);
    float StretchNow() => 0; // grounded, no landing squash in this fixture
    public PoseFixture(bool before, float scale = 1, int facing = 1, RunStyle runStyle = RunStyle.Sprinter)
    {
        _before = before; S = scale; Facing = facing; _runStyle = runStyle;
        Dimensions();
        _hip = StandHip; _lean = .02f * Facing;
        _hN = new(2*S*Facing, Arm*.93f); _hF = new(-1.5f*S*Facing, Arm*.93f);
    }
}
