using System.Numerics;

namespace Doodlefolk;

/// <summary>Poses for using objects. Most are side-on like everything else; sitting in an armchair, on a couch or in
/// a watching chair turns the figure to face out of (or into) the screen, which gives the desktop some depth.</summary>
sealed partial class Figure
{
    /// <summary>Front (or back) view seated pose. Joints are placed directly, eased from wherever they were.</summary>
    /// <summary>Sitting on the floor (watching a video) rather than in a seat.</summary>
    public bool FloorSit;

    void FrontPose(float dt)
    {
        bool back = Action == Act.SitBack;
        float br = MathF.Sin(_time * 2.1f + Id);
        float k = 1 - MathF.Exp(-dt * 10);
        Vector2 pel = Base + new Vector2(0, -1.5f * S);
        // Lean back into the cushions a little; watchers sit forward, glued to the screen.
        Vector2 neck = pel + new Vector2(0, -Torso * (back ? 0.98f : 0.95f) + br * 0.25f * S);
        float look = LookAt is Vector2 la ? Math.Clamp((la.X - neck.X) / (220 * S), -1, 1) : 0;
        if (back) look = MathF.Sin(_time * 0.3f + Id) * 0.3f;   // following what's on screen
        Vector2 head = neck + new Vector2(look * 1.6f * S, -(HeadR + NeckGap) + (back ? -0.6f * S : 0));
        // Legs come toward (or away from) us: knees a little apart, shins hanging, one foot idly swinging.
        float swing = MathF.Max(0, MathF.Sin(_time * 1.4f + Id * 3)) * (back ? 0 : 1.5f * S);
        Vector2 kN = pel + new Vector2(5 * S, 2.5f * S), kF = pel + new Vector2(-5 * S, 2.5f * S);
        Vector2 fN = kN + new Vector2(1.5f * S + swing, Shin * 0.9f), fF = kF + new Vector2(-1.5f * S, Shin * 0.9f);
        if (FloorSit)
        {
            // Sitting on the floor, cross-legged: hips down, knees out to the sides, feet tucked in.
            pel = Base + new Vector2(0, -2.5f * S);
            neck = pel + new Vector2(0, -Torso * 0.97f + br * 0.25f * S);
            head = neck + new Vector2(look * 1.6f * S, -(HeadR + NeckGap) - 0.6f * S);
            kN = pel + new Vector2(8 * S, 0.5f * S); kF = pel + new Vector2(-8 * S, 0.5f * S);
            fN = Base + new Vector2(-2 * S, -0.8f * S); fF = Base + new Vector2(2 * S, -0.8f * S);
        }
        Vector2 eN, eF, hN, hF;
        if (back)
        {
            // Seen from behind: elbows out, hands forward out of sight (on the armrests, holding a snack...).
            eN = neck + new Vector2(7 * S, 7 * S); eF = neck + new Vector2(-7 * S, 7 * S);
            hN = neck + new Vector2(4 * S, 13 * S); hF = neck + new Vector2(-4 * S, 13 * S);
        }
        else
        {
            // Hands resting on the knees or armrests.
            eN = neck + new Vector2(6.5f * S, 8 * S); eF = neck + new Vector2(-6.5f * S, 8 * S);
            hN = pel + new Vector2(6 * S, -0.5f * S + br * 0.2f * S); hF = pel + new Vector2(-6 * S, -0.5f * S);
        }
        void Set(int j, Vector2 v) => Jt[j] = Vector2.Lerp(Jt[j], v, k);
        Set(J.Pelvis, pel); Set(J.Neck, neck); Set(J.Head, head);
        Set(J.KneeN, kN); Set(J.FootN, fN); Set(J.KneeF, kF); Set(J.FootF, fF);
        Set(J.ElbowN, eN); Set(J.HandN, hN); Set(J.ElbowF, eF); Set(J.HandF, hF);
        // Keep the side-view springs near the current shape so getting up blends nicely.
        _hip = MathF.Max(2 * S, Base.Y - Jt[J.Pelvis].Y); _hipV = 0;
        _lean = 0; _leanV = 0;
        UpdateHoldPoint(Jt[J.Neck], Jt[J.HandN], Jt[J.HandF], dt);
    }

    /// <summary>Side-on poses for the floor-sitting object verbs (Curl, Eat, Read, Warm).</summary>
    bool ItemActPose(ref float hipT, ref float leanT, ref float tiltT, ref float handW, ref Vector2 hN, ref Vector2 hF,
                     ref Vector2 fN, ref Vector2 fF, ref Vector2 eN, ref Vector2 eF, ref Vector2 kPref)
    {
        switch (Action)
        {
            case Act.Curl:
                // Hiding: hugging the knees, head down.
                hipT = 2 * S; leanT = 1.0f;
                fN = new(Thigh * 0.55f, 1 * S); fF = new(Thigh * 0.45f, 1.5f * S);
                kPref = new(1, -1);
                hN = new(Arm * 0.55f, Torso * 0.42f); hF = new(Arm * 0.48f, Torso * 0.48f);
                eN = eF = new(1, 0.3f);
                tiltT += 0.4f * Facing;
                return true;
            case Act.Eat:
            {
                SitLegs(ref hipT, ref leanT, ref fN, ref fF, ref kPref);
                // Bite cycle: food up to the mouth, chew, back to the lap.
                float p = (ActionT % 1.4f) / 1.4f;
                Vector2 mouth = new(HeadR * 0.9f, -(HeadR * 1.05f + NeckGap));
                Vector2 lap = new(Arm * 0.5f, Torso * 0.5f);
                hN = p < 0.35f ? Vector2.Lerp(lap, mouth, M.Smooth(p / 0.25f)) : Vector2.Lerp(mouth, lap, M.Smooth((p - 0.35f) / 0.3f));
                hF = new(Arm * 0.35f, Torso * 0.65f);
                eN = new(-0.2f, 1); eF = new(-1, 0.5f);
                handW = 22;
                if (p > 0.25f && p < 0.45f) tiltT += MathF.Sin(ActionT * 30) * 0.05f;   // chewing
                return true;
            }
            case Act.Read:
                SitLegs(ref hipT, ref leanT, ref fN, ref fF, ref kPref);
                hN = new(Arm * 0.62f, Torso * 0.38f); hF = new(Arm * 0.55f, Torso * 0.44f);
                eN = eF = new(-0.4f, 1);
                tiltT += 0.38f * Facing;
                leanT += 0.08f;
                return true;
            case Act.Warm:
                SitLegs(ref hipT, ref leanT, ref fN, ref fF, ref kPref);
                // Palms toward the fire.
                hN = new(Arm * 0.9f, Torso * 0.15f + MathF.Sin(_time * 1.5f + Id) * 0.5f * S); hF = new(Arm * 0.85f, Torso * 0.22f);
                eN = eF = new(-0.3f, 1);
                leanT += 0.1f;
                return true;
        }
        return false;
    }

    void SitLegs(ref float hipT, ref float leanT, ref Vector2 fN, ref Vector2 fF, ref Vector2 kPref)
    {
        hipT = 2 * S; leanT = 0.08f;
        fN = new(Leg * 0.62f, 2 * S); fF = new(Leg * 0.55f, 2 * S);
        kPref = new(0.3f, -1);
    }
}
