using System.Numerics;

namespace Doodlefolk;

/// <summary>Holding an animal in one pose or gait for the animation sheets (--animsheet): its mind steps aside.</summary>
sealed partial class Pet
{
    Pose? _puppetPose;
    float _puppetPace;

    public static IEnumerable<string> PuppetPoses(PetKind kind) => Enum.GetValues<Pose>()
        .Where(p => p != Pose.Air && (kind == PetKind.Parrot ? p is Pose.Stand or Pose.Perch or Pose.Fluff or Pose.Groom or Pose.HeadDown or Pose.Crouch or Pose.Lie
                                                            : p is not (Pose.Perch or Pose.Fluff)))
        .Select(p => p.ToString());

    public void PuppetAs(string pose)
    {
        _puppetPose = Enum.Parse<Pose>(pose); _puppetPace = 0;
        // Cat/dog curled art uses the same sleeping state as a real nap.
        if (Kind is PetKind.Cat or PetKind.Dog) _st = _puppetPose == Pose.Curl ? State.Sleep : State.Idle;
    }
    public void PuppetWalk(float pace)
    {
        _puppetPose = Pose.Stand; _puppetPace = pace;
        if (Kind is PetKind.Cat or PetKind.Dog) _st = State.Idle;
    }

    /// <summary>True while puppeted (the mind does nothing else).</summary>
    bool PuppetStep(World w, float dt)
    {
        if (_puppetPose is not { } pose) return false;
        _t += dt; _t0 += dt;
        _pose = pose;
        if (_st == State.Sleep) _sleepZ += dt;
        if (_puppetPace > 0) MoveTo(w, Pos + new Vector2(Facing * 2000 * S, 0), _puppetPace);
        else Vel.X = 0;
        return true;
    }
}
