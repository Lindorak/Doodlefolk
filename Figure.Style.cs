using System.Numerics;

namespace Doodlefolk;

/// <summary>Body language: how this particular figure walks, runs, idles, fidgets, jumps and
/// celebrates (its BodyStyle), bent in the moment by its mood (tired, angry, happy, scared, sad, hurt).</summary>
sealed partial class Figure
{
    public StyleChoice StyleChoice;
    BodyStyle? _style;
    int _styleKey;

    /// <summary>Resolved style; recomputed automatically when personality or style choices change.</summary>
    public BodyStyle Style
    {
        get
        {
            var t = Traits;
            int key = HashCode.Combine(StyleChoice.Key, (int)(t.Energy * 50), (int)(t.Curiosity * 50), (int)(t.Bravery * 50),
                                       (int)(t.Playfulness * 50), (int)(t.Aggression * 50), (int)(t.Sociability * 50));
            if (_style == null || key != _styleKey) { _style = BodyStyle.Resolve(t, StyleChoice); _styleKey = key; }
            return _style;
        }
    }

    /// <summary>Set by the brain every step.</summary>
    public MoodState Mood;

    public Fidget FidgetKind;
    public float FidgetDur;
    float _landPoseT;

    public static float FidgetLength(Fidget k) => k switch
    {
        Fidget.Stretch => 1.6f, Fidget.ScratchHead => 1.4f, Fidget.CheckWatch => 1.4f, Fidget.FootTap => 1.6f,
        Fidget.Yawn => 1.8f, Fidget.Shrug => 0.9f, _ => 2.4f,
    };

    public void StartFidget(Fidget k)
    {
        FidgetKind = k;
        FidgetDur = FidgetLength(k);
        SetAction(Act.Stand);
        SetAction(Act.Fidget);
    }

    float MoodSpeed => (1 - Mood.Hurt * 0.35f) * (1 - Mood.Tired * 0.2f) * (1 + Mood.Happy * 0.06f) * (1 - Fat * 0.4f) * (Elder ? 0.8f : 1);

    // ---------------- walking & running ----------------

    void WalkPose(float speed, float run, ref float hipT, ref float leanT, ref float tiltT, ref float handW,
                  ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        var st = Style;
        var md = Mood;
        // Arms swing opposite the legs: near arm forward when the far foot is forward.
        Vector2 pn = FootPos(_fN), pf = FootPos(_fF);
        float half = MathF.Max(speed * M.Lerp(0.25f, 0.15f, run) * st.Stride, 4 * S);
        float sw = Math.Clamp((pf.X - pn.X) * Facing / half, -1, 1);

        float swing = st.ArmSwing * (1 - md.Tired * 0.5f) * (1 + md.Angry * 0.2f) * (1 - md.Hurt * 0.4f);
        float elbow = Math.Clamp(st.ElbowBend + md.Angry * 0.4f, 0, 1);
        float reach = Arm * (0.93f - elbow * 0.32f);
        Vector2 wN = M.Dir(MathF.PI - sw * 0.6f * swing) * reach;
        Vector2 wF = M.Dir(MathF.PI + sw * 0.6f * swing) * reach;

        // Sneaking (or scared): hands up at the chest like paws.
        float sneak = MathF.Max(st.Sneak, md.Scared * 0.8f);
        if (sneak > 0)
        {
            wN = Vector2.Lerp(wN, new Vector2(Arm * 0.38f + sw * 2 * S, Torso * 0.2f), sneak);
            wF = Vector2.Lerp(wF, new Vector2(Arm * 0.3f - sw * 2 * S, Torso * 0.28f), sneak);
        }

        Vector2 rN, rF;
        float runLean;
        switch (st.Run)
        {
            case RunStyle.Flailer:
                rN = M.Dir(_time * 15 + Id) * Arm * 0.88f;
                rF = M.Dir(_time * 15 + Id + MathF.PI) * Arm * 0.88f;
                runLean = 0.08f;
                break;
            case RunStyle.NinjaRun:
                rN = new(-Arm * 0.85f, Arm * 0.28f);
                rF = new(-Arm * 0.9f, Arm * 0.18f);
                runLean = 0.5f;
                break;
            case RunStyle.Jogger:
                rN = new(Arm * 0.3f + sw * Arm * 0.2f, Arm * 0.4f - MathF.Max(0, sw) * Arm * 0.12f);
                rF = new(Arm * 0.3f - sw * Arm * 0.2f, Arm * 0.4f - MathF.Max(0, -sw) * Arm * 0.12f);
                runLean = 0.1f;
                break;
            case RunStyle.Tippy:
                rN = new(Arm * 0.65f + sw * Arm * 0.08f, Arm * 0.15f);
                rF = new(-Arm * 0.55f - sw * Arm * 0.08f, Arm * 0.2f);
                runLean = -0.02f;
                break;
            default:
                rN = new(Arm * 0.3f + sw * Arm * 0.38f, Arm * 0.42f - MathF.Max(0, sw) * Arm * 0.25f);
                rF = new(Arm * 0.3f - sw * Arm * 0.38f, Arm * 0.42f - MathF.Max(0, -sw) * Arm * 0.25f);
                runLean = 0.31f;
                break;
        }
        hN = Vector2.Lerp(wN, rN, run);
        hF = Vector2.Lerp(wF, rF, run);
        eN = eF = new(-1, 0.4f + run * 0.3f + elbow * 0.4f);

        float posture = st.Posture + md.Tired * 0.14f + md.Sad * 0.12f + md.Angry * 0.05f - md.Happy * 0.03f + sneak * 0.12f;
        leanT = M.Lerp(0.06f + posture, runLean, run) + sw * 0.07f * st.Swagger * (1 - run);
        hipT = StandHip * (1 - run * 0.06f) * (1 - sneak * 0.14f) * (1 - md.Tired * 0.03f);
        tiltT += (sw * 0.06f * st.HeadBob * (1 - run) + md.Sad * 0.3f + md.Tired * 0.15f) * Facing;
        if (st.Run == RunStyle.NinjaRun) tiltT -= 0.25f * run * Facing;   // head forward, chin up
        handW = st.Run == RunStyle.Flailer && run > 0.3f ? 26 : 18;
        if (md.Hurt > 0.35f) hF = Vector2.Lerp(hF, new Vector2(Arm * 0.2f, Torso * 0.62f), md.Hurt);   // holding its side
    }

    // ---------------- standing around ----------------

    void IdlePose(float br, ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW,
                  ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        var st = Style;
        var md = Mood;
        // Relaxed, not at attention: soft knees, weight drifting slowly between the feet, arms hanging
        // loose with a bend at the elbow and a little breathing sway.
        float shift = MathF.Sin(_time * 0.45f + Id) + 0.35f * MathF.Sin(_time * 0.17f + Id * 2);
        pdxT = shift * 0.9f * S * st.WeightShift;
        hipT = StandHip * (0.955f - 0.01f * MathF.Max(0, shift));
        leanT = 0.02f + br * 0.012f + st.Posture * 0.6f + md.Tired * 0.12f + md.Sad * 0.12f;
        tiltT += (md.Sad * 0.35f + md.Tired * 0.15f) * Facing;
        float sway = br * 0.6f * S;
        hN = new(Arm * 0.13f + sway, Arm * 0.86f);
        hF = new(-Arm * 0.04f + sway * 0.7f, Arm * 0.88f);
        eN = eF = new(-1, 0.35f);
        handW = 10;
        switch (st.Idle)
        {
            case IdleHabit.ArmsCrossed:
                // Forearms folded across the chest, elbows down at the sides.
                hN = new(Arm * 0.3f, Torso * 0.5f + br * 0.3f * S); hF = new(Arm * 0.24f, Torso * 0.44f + br * 0.3f * S);
                eN = eF = new(0.15f, 1); handW = 14;
                leanT -= 0.03f;
                break;
            case IdleHabit.HandsBehind:
                hN = new(-Arm * 0.38f, Torso * 0.74f); hF = new(-Arm * 0.44f, Torso * 0.7f);
                eN = eF = new(-1, 0.3f); leanT -= 0.04f;
                break;
            case IdleHabit.HandsOnHips:
                hN = new(1.5f * S, Torso * 0.85f); hF = new(1 * S, Torso * 0.85f);
                eN = eF = new(-1, 0);
                break;
            case IdleHabit.Fidgety:
                // Can't stand still: rocks side to side and fiddles with its hands.
                pdxT = MathF.Sin(_time * 2.2f + Id) * 1.6f * S;
                hN = new(Arm * 0.32f + MathF.Sin(_time * 3 + Id) * 1.2f * S, Torso * 0.68f);
                hF = new(Arm * 0.28f, Torso * 0.72f + MathF.Sin(_time * 3.4f + Id) * 0.8f * S);
                eN = eF = new(-0.6f, 1);
                break;
        }
        if (md.Scared > 0.4f)
        {
            // Nervous: knees bent, hunched, hands held up in front of the chest.
            hipT = StandHip * 0.9f;
            hN = new(Arm * 0.5f, Torso * 0.42f); hF = new(Arm * 0.44f, Torso * 0.5f);
            eN = eF = new(-0.3f, 1);
            leanT += 0.06f;
        }
        else if (md.Angry > 0.65f && st.Idle == IdleHabit.Loose)
        {
            // Fists clenched at the sides.
            hN = new(Arm * 0.15f, Arm * 0.62f); hF = new(-Arm * 0.1f, Arm * 0.62f);
            eN = eF = new(-1, 0.2f);
        }
        if (md.Hurt > 0.35f) hF = Vector2.Lerp(hF, new Vector2(Arm * 0.2f, Torso * 0.62f), md.Hurt);
    }

    // ---------------- fidgets ----------------

    void FidgetPose(ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW,
                    ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        float t = ActionT, p = M.Clamp01(t / MathF.Max(FidgetDur, 0.1f));
        float env = MathF.Sin(MathF.PI * p);   // ease in and back out
        handW = 18;
        switch (FidgetKind)
        {
            case Fidget.Stretch:
                if (env > 0.25f) { hN = new(Arm * 0.1f, -Arm * 0.98f); hF = new(-Arm * 0.05f, -Arm * 0.98f); eN = eF = new(0.3f, 1); }
                leanT = -0.15f * env;
                hipT = StandHip * (1 + 0.02f * env);
                tiltT -= 0.2f * env * Facing;
                break;
            case Fidget.ScratchHead:
                hN = new(HeadR * 0.6f + MathF.Sin(t * 28) * 1.2f * S, -(HeadR * 2.1f + NeckGap));
                eN = new(1, -0.4f);
                tiltT += 0.18f * Facing;
                handW = 26;
                break;
            case Fidget.CheckWatch:
                hF = new(Arm * 0.5f, Torso * 0.05f);
                eF = new(0, 1);
                tiltT += 0.4f * Facing;
                break;
            case Fidget.FootTap:
                hN = new(1.5f * S, Torso * 0.85f); hF = new(1 * S, Torso * 0.85f);
                eN = eF = new(-1, 0);
                break;
            case Fidget.Yawn:
                if (env > 0.3f) { hN = new(Arm * 0.35f, -Arm * 0.62f); hF = new(-Arm * 0.3f, -Arm * 0.55f); eN = eF = new(0.2f, 1); }
                leanT = -0.12f * env;
                tiltT -= 0.3f * env * Facing;
                break;
            case Fidget.Shrug:
                if (env > 0.2f) { hN = new(Arm * 0.55f, Torso * 0.42f); hF = new(-Arm * 0.45f, Torso * 0.42f); eN = eF = new(-0.5f, 1); }
                hipT = StandHip + 1.2f * S * env;
                tiltT += 0.12f * env * Facing;
                break;
            case Fidget.Groove:
            {
                float s = MathF.Sin(t * 10);
                hipT = StandHip * 0.94f + s * 1.6f * S;
                pdxT = MathF.Sin(t * 5) * 2.5f * S;
                hN = new(Arm * 0.4f, -Arm * 0.25f + s * Arm * 0.3f);
                hF = new(-Arm * 0.2f, -Arm * 0.1f - s * Arm * 0.3f);
                eN = eF = new(0, 1);
                tiltT += MathF.Sin(t * 5) * 0.12f;
                handW = 24;
                break;
            }
        }
    }

    /// <summary>Foot-tapping fidget: the near foot's toes lift and tap.</summary>
    Vector2 FidgetFoot(Vector2 foot) =>
        Action == Act.Fidget && FidgetKind == Fidget.FootTap
            ? foot + new Vector2(Facing * 1 * S, -MathF.Max(0, MathF.Sin(ActionT * 15)) * 2.2f * S)
            : foot;

    // ---------------- celebrating ----------------

    void CelebratePose(ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW,
                       ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        float t = ActionT, b = MathF.Abs(MathF.Sin(t * 9));
        handW = 22;
        switch (Style.Celebrate)
        {
            case CelebrateStyle.Flex:
                // Double biceps: upper arms out, forearms up.
                hN = new(Arm * 0.45f, -Arm * (0.38f + 0.06f * b));
                hF = new(-Arm * 0.42f, -Arm * (0.36f + 0.06f * b));
                eN = new(1, 0.2f); eF = new(-1, 0.2f);
                leanT = -0.05f;
                hipT = StandHip * 0.97f;
                break;
            case CelebrateStyle.Dance:
            {
                float s = MathF.Sin(t * 11);
                hipT = StandHip * (0.9f + 0.1f * b);
                pdxT = MathF.Sin(t * 5.5f) * 3 * S;
                hN = new(Arm * 0.35f, -Arm * 0.9f * (0.5f + 0.5f * s));
                hF = new(-Arm * 0.25f, -Arm * 0.9f * (0.5f - 0.5f * s));
                eN = eF = new(1, 0.4f);
                tiltT += MathF.Sin(t * 5.5f) * 0.15f;
                break;
            }
            case CelebrateStyle.Taunt:
                // "Come on!" beckoning, other hand on hip.
                hN = new(Arm * 0.72f, -Arm * 0.08f + MathF.Sin(t * 14) * Arm * 0.08f);
                eN = new(0, 1);
                hF = new(1 * S, Torso * 0.85f);
                eF = new(-1, 0);
                leanT = -0.06f;
                break;
            case CelebrateStyle.Bow:
                leanT = 0.85f * M.Smooth(t / 0.4f) * (t > 1.1f ? M.Clamp01(1 - (t - 1.1f) / 0.3f) : 1);
                hN = new(Arm * 0.3f, Torso * 0.45f);
                hF = new(-Arm * 0.45f, Torso * 0.75f);
                eN = new(-1, 0.4f); eF = new(-1, 0.3f);
                break;
            default:
                hN = new(Arm * 0.3f, -Arm * (0.8f + 0.15f * b)); hF = new(-Arm * 0.1f, -Arm * (0.85f + 0.1f * b));
                eN = eF = new(1, 0.2f);
                hipT = StandHip * (0.9f + 0.1f * b);
                break;
        }
    }

    // ---------------- jumping ----------------

    bool JumpStylePose(bool rising, ref float leanT, ref Vector2 hN, ref Vector2 hF, ref Vector2 fN, ref Vector2 fF)
    {
        switch (Style.Jump)
        {
            case JumpStyle.Starfish:
                hN = new(Arm * 0.6f, -Arm * 0.72f); hF = new(-Arm * 0.55f, -Arm * 0.68f);
                fN = new(Leg * 0.5f, Leg * 0.8f); fF = new(-Leg * 0.5f, Leg * 0.8f);
                leanT = 0;
                return true;
            case JumpStyle.Superhero:
                hN = rising ? new(Arm * 0.3f, -Arm * 0.98f) : new(Arm * 0.55f, Arm * 0.45f);
                hF = new(-Arm * 0.55f, Arm * 0.35f);
                fN = new(1.5f * S, Leg * 0.95f); fF = new(-1 * S, Leg * 0.92f);
                leanT = rising ? 0.08f : 0.18f;
                return true;
        }
        return false;
    }

    /// <summary>One-knee superhero landing after a big drop (Superhero jumpers).</summary>
    bool SuperLandingPose(ref float hipT, ref float leanT, ref Vector2 hN, ref Vector2 hF, ref Vector2 eN)
    {
        if (_landPoseT <= 0) return false;
        hipT = StandHip * 0.5f;
        leanT = 0.35f;
        hN = new(Arm * 0.55f, Torso * 1.05f);
        hF = new(-Arm * 0.6f, -Arm * 0.1f);
        eN = new(-0.3f, 1);
        return true;
    }
}
