using System.Numerics;

namespace Doodlefolk;

/// <summary>Basketball moves: a proper dribble (the ball goes down to the floor and back up into a pumping hand,
/// with a bounce each time), the shot held up above the forehead, a layup off one hand with a knee up, a block with
/// both arms straight up, and hanging off the rim after a dunk.</summary>
sealed partial class Figure
{
    /// <summary>Bouncing the ball they're carrying as they go.</summary>
    public bool Dribbling;
    /// <summary>Bounces so far, ever (the brain counts the difference).</summary>
    public int DribbleBeats;
    float _dribbleP;
    /// <summary>Hanging off the rim (after a dunk): held there for this long, hands on HangAt.</summary>
    public float HangT;
    public Vector2 HangAt;
    /// <summary>Just let go of a shot: the wrist flick follows through.</summary>
    public float ShotFollowT;

    Vector2 LocalOf(Vector2 world)
    {
        Vector2 d = world - Jt[J.Neck];
        d = new Vector2(d.X * Facing, d.Y);
        float len = d.Length(), max = Arm * 0.97f;
        return len > max ? d / len * max : d;
    }

    /// <summary>Where the carried ball is while dribbling: bounced from the hand down to the floor and back.</summary>
    bool DribbleHold(Vector2 neck, float dt, out Vector2 hold)
    {
        hold = default;
        if (!Dribbling || Carrying == null || !Grounded || Action is Act.JumpShot or Act.Layup or Act.Throw) return false;
        float period = MathF.Abs(Vel.X) > WalkSpeed * 1.3f ? 0.32f : 0.42f;
        float prev = _dribbleP;
        _dribbleP = (_dribbleP + dt / period) % 1;
        float r = Carrying.Radius;
        Vector2 hand = neck + W(new Vector2(Arm * 0.55f, Torso * 0.72f));
        float top = hand.Y + r, floor = Base.Y - r;
        if (prev < 0.5f && _dribbleP >= 0.5f)
        {
            DribbleBeats++;
            World.Play(Sfx.BounceBasket, new Vector2(hand.X, floor), 0.14f, 1, 0.05);
        }
        float k = MathF.Abs(MathF.Cos(MathF.PI * _dribbleP));   // 1 in the hand, 0 on the floor (a sharp bounce)
        hold = new Vector2(hand.X + Facing * MathF.Min(MathF.Abs(Vel.X) * 0.02f, 4 * S), top + (floor - top) * (1 - k));
        return true;
    }

    /// <summary>Arms while dribbling: the hand rides the ball at the top and pushes it down.</summary>
    bool DribbleArms(ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF, ref float handW)
    {
        if (!Dribbling || Carrying == null || !Grounded || Action is Act.JumpShot or Act.Layup or Act.Throw) return false;
        float k = MathF.Abs(MathF.Cos(MathF.PI * _dribbleP));
        Vector2 rest = new(Arm * 0.55f, Torso * 0.66f);
        hN = k > 0.55f ? LocalOf(HoldPoint - new Vector2(0, Carrying.Radius)) : rest + new Vector2(0, -2 * S * (1 - k));
        hF = new(-Arm * 0.15f, Torso * 0.55f);   // the other arm out a little, guarding it
        eN = new(-0.6f, 1); eF = new(-0.8f, 0.6f);
        handW = 55;
        return true;
    }

    /// <summary>Basketball poses in the air (and the shot held up on the ground).</summary>
    bool HoopsPose(ref Vector2 hN, ref Vector2 hF, ref Vector2 fN, ref Vector2 fF, ref float leanT, ref float handW)
    {
        if (HangT > 0)
        {
            hN = LocalOf(HangAt + new Vector2(Facing * 1.5f * S, 0)); hF = LocalOf(HangAt - new Vector2(Facing * 1.5f * S, 0));
            fN = new(2 * S, Leg * 0.97f); fF = new(-1 * S, Leg * 0.95f);
            leanT = -0.05f; handW = 70;
            return true;
        }
        switch (Action)
        {
            case Act.JumpShot:
                if (ShotFollowT > 0) { hN = new(Arm * 0.6f, -Arm * 0.75f); hF = new(Arm * 0.35f, -Arm * 0.7f); }   // the flick
                else { hN = new(Arm * 0.3f, -Arm * 0.95f); hF = new(Arm * 0.12f, -Arm * 0.85f); }
                fN = new(1 * S, Leg * 0.85f); fF = new(-2 * S, Leg * 0.9f);
                leanT = -0.05f; handW = 45;
                return true;
            case Act.Layup:
                hN = new(Arm * 0.55f, -Arm * 0.85f); hF = new(Arm * 0.2f, -Arm * 0.1f);
                fN = new(Leg * 0.45f, Leg * 0.45f); fF = new(-Leg * 0.15f, Leg * 0.9f);
                leanT = 0.1f; handW = 45;
                return true;
            case Act.Block:
                hN = new(Arm * 0.15f, -Arm * 0.98f); hF = new(-Arm * 0.05f, -Arm * 0.98f);
                fN = new(2 * S, Leg * 0.85f); fF = new(-2 * S, Leg * 0.88f);
                handW = 60;
                return true;
        }
        return false;
    }

    /// <summary>The ball held for a shot: above the forehead; for a layup, up in the leading hand.</summary>
    bool ShotHold(Vector2 neck, out Vector2 hold)
    {
        hold = default;
        if (Carrying == null) return false;
        if (Action == Act.JumpShot) { hold = neck + W(new Vector2(Arm * 0.25f, -Arm * 1.05f)); return true; }
        if (Action == Act.Layup) { hold = neck + W(new Vector2(Arm * 0.55f, -Arm * 0.98f)); return true; }
        return false;
    }

    void StepHoops(float dt)
    {
        ShotFollowT = MathF.Max(0, ShotFollowT - dt);
        if (Carrying == null) Dribbling = false;
    }
}
