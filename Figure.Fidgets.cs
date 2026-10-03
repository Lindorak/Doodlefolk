using System.Numerics;

namespace Doodlefolk;

/// <summary>The bigger repertoire of little things they do while standing about (on top of the originals in
/// Figure.Style), each easing in and back out; and the dance floor: eight moves, each figure with its own favourites,
/// changing every couple of bars so nobody dances the same way all night.</summary>
sealed partial class Figure
{
    /// <summary>A smooth in-and-out envelope over a fidget (0 at the ends, 1 in the middle stretch).</summary>
    static float Envelope(float p, float edge = 0.18f) => M.Smooth(p / edge) * M.Smooth((1 - p) / edge);

    /// <summary>The newer fidgets. False if it's one of the originals.</summary>
    bool MoreFidgetPose(float t, float p, ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW,
                        ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        float env = Envelope(p);
        Vector2 Ease(Vector2 from, Vector2 to) => Vector2.Lerp(from, to, env);
        Vector2 restN = new(Arm * 0.13f, Arm * 0.86f), restF = new(-Arm * 0.04f, Arm * 0.88f);
        Vector2 face = new(HeadR * 0.6f, -(HeadR + NeckGap) * 1.05f);   // in front of the face
        switch (FidgetKind)
        {
            case Fidget.LookAround:
                // A long look back over the shoulder, then the other way.
                tiltT += (p < 0.5f ? -0.35f : 0.25f) * env * Facing;
                leanT = -0.06f * env;
                pdxT = (p < 0.5f ? -1 : 1) * 1.2f * S * env;
                break;
            case Fidget.Whistle:
                hN = Ease(restN, new(-Arm * 0.38f, Torso * 0.74f)); hF = Ease(restF, new(-Arm * 0.44f, Torso * 0.7f));
                eN = eF = new(-1, 0.3f);
                tiltT += (MathF.Sin(t * 6) * 0.08f - 0.12f) * env * Facing;
                leanT = -0.05f * env;
                pdxT = MathF.Sin(t * 3) * 1.2f * S * env;
                break;
            case Fidget.KickPebble:
                hN = new(Arm * 0.2f, Arm * 0.8f); hF = new(-Arm * 0.15f, Arm * 0.82f);
                leanT = 0.08f * env;
                tiltT += 0.35f * env * Facing;   // looking down at it
                break;
            case Fidget.CrackKnuckles:
            {
                float push = MathF.Max(0, MathF.Sin(t * 9)) * env;
                hN = Ease(restN, new(Arm * (0.55f + push * 0.2f), Torso * 0.4f)); hF = Ease(restF, new(Arm * (0.52f + push * 0.2f), Torso * 0.44f));
                eN = eF = new(-0.4f, 1); handW = 24;
                leanT = -0.04f * env;
                break;
            }
            case Fidget.NeckRoll:
                tiltT += MathF.Sin(p * MathF.Tau * 1.5f) * 0.4f * env * Facing;
                leanT = MathF.Cos(p * MathF.Tau * 1.5f) * 0.06f * env;
                hN = Ease(restN, new(1.5f * S, Torso * 0.85f)); hF = Ease(restF, new(1 * S, Torso * 0.85f));
                eN = eF = new(-1, 0);
                break;
            case Fidget.Sneeze:
            {
                // Ah… ah… (head back) …CHOO (snaps forward), hand up to the face.
                float build = M.Smooth(p / 0.55f), snap = p > 0.55f ? M.Smooth((p - 0.55f) / 0.08f) * M.Smooth((1 - p) / 0.3f) : 0;
                leanT = -0.18f * build * (1 - snap) + 0.32f * snap;
                tiltT += (-0.3f * build * (1 - snap) + 0.3f * snap) * Facing;
                hN = Ease(restN, face + new Vector2(2 * S, 0)); eN = new(-0.2f, 1);
                hipT = StandHip * (1 - 0.05f * snap);
                handW = 26;
                break;
            }
            case Fidget.Balance:
            {
                // On one foot (see FidgetFoot), arms out, wobbling.
                float wob = MathF.Sin(t * 5) * 0.25f + MathF.Sin(t * 8.3f) * 0.12f;
                hN = Ease(restN, new(Arm * 0.92f, -Arm * (0.08f + wob))); hF = Ease(restF, new(-Arm * 0.92f, -Arm * (0.08f - wob)));
                eN = eF = new(0, 1);
                leanT = wob * 0.18f * env;
                pdxT = -2.5f * S * env;
                handW = 20;
                break;
            }
            case Fidget.TouchToes:
                leanT = 1.15f * env;
                hipT = StandHip * (1 - 0.04f * env);
                hN = Ease(restN, new(Arm * 0.98f, Arm * 0.15f)); hF = Ease(restF, new(Arm * 0.92f, Arm * 0.2f));
                eN = eF = new(1, 0.2f);
                tiltT += 0.3f * env * Facing;
                break;
            case Fidget.Think:
                // Hand on chin, the other arm across the chest holding the elbow.
                hN = Ease(restN, face + new Vector2(-HeadR * 0.1f, HeadR * 1.0f)); eN = new(0.1f, 1);
                hF = Ease(restF, new(Arm * 0.32f, Torso * 0.42f)); eF = new(-0.2f, 1);
                tiltT += (0.18f + MathF.Sin(t * 1.3f) * 0.06f) * env * Facing;
                handW = 16;
                break;
            case Fidget.AirGuitar:
            {
                float strum = MathF.Sin(t * 16);
                hN = Ease(restN, new(Arm * 0.15f, Torso * 0.62f + strum * 2 * S)); eN = new(-0.3f, 1);
                hF = Ease(restF, new(Arm * 0.88f, Torso * 0.22f)); eF = new(-0.2f, 1);
                leanT = (-0.12f + MathF.Sin(t * 4) * 0.08f) * env;
                hipT = StandHip * (1 - (0.04f + MathF.Max(0, MathF.Sin(t * 8)) * 0.04f) * env);
                tiltT += MathF.Sin(t * 8) * 0.18f * env;
                handW = 30;
                break;
            }
            case Fidget.DustOff:
            {
                // Brush one shoulder, then the other.
                bool second = p > 0.5f;
                float brush = MathF.Sin(t * 22) * 1.5f * S;
                Vector2 shoulder = new(second ? -2 * S : 2 * S + brush, Torso * 0.08f + brush * 0.3f);
                if (second) hF = Ease(restF, shoulder + new Vector2(brush, 0)); else hN = Ease(restN, shoulder);
                eN = eF = new(0.3f, 1);
                tiltT += (second ? -0.2f : 0.2f) * env * Facing;
                handW = 26;
                break;
            }
            case Fidget.Clap:
            {
                float c = MathF.Abs(MathF.Sin(t * 9));
                hN = Ease(restN, new(Arm * 0.55f + c * 3 * S, Torso * 0.25f)); hF = Ease(restF, new(Arm * 0.55f - c * 3 * S, Torso * 0.27f));
                eN = eF = new(-0.4f, 1);
                hipT = StandHip * (1 - 0.03f * c * env);
                handW = 34;
                break;
            }
            case Fidget.Facepalm:
                hN = Ease(restN, face); eN = new(0, 1);
                tiltT += 0.4f * env * Facing; leanT = 0.1f * env;
                handW = 18;
                break;
            case Fidget.Shiver:
            {
                float sh = MathF.Sin(t * 45) * 0.6f * S;
                hN = Ease(restN, new(Arm * 0.32f + sh, Torso * 0.38f)); hF = Ease(restF, new(Arm * 0.28f - sh, Torso * 0.32f));
                eN = eF = new(0.1f, 1);
                pdxT = sh * env;
                hipT = StandHip * (1 - 0.05f * env);
                leanT = 0.1f * env;
                handW = 40;
                break;
            }
            case Fidget.FanSelf:
            {
                float fan = MathF.Sin(t * 14);
                hN = Ease(restN, face + new Vector2(Arm * 0.15f, HeadR * 0.6f + fan * 2.5f * S)); eN = new(-0.3f, 1);
                tiltT -= 0.15f * env * Facing; leanT = -0.05f * env;
                handW = 32;
                break;
            }
            case Fidget.RubEyes:
            {
                float rub = MathF.Sin(t * 10) * 1.2f * S;
                hN = Ease(restN, face + new Vector2(rub, 0)); hF = Ease(restF, face + new Vector2(-2 * S - rub, 0.5f * S));
                // Reach around the shoulder instead of passing almost through its IK origin.
                float reachArc = MathF.Sin(env * MathF.PI) * Arm * 0.45f;
                hN.X += reachArc; hF.X += reachArc;
                eN = new(-_hN.Y, _hN.X * Facing);
                eF = new(-_hF.Y, _hF.X * Facing);
                tiltT += 0.15f * env * Facing; leanT = 0.06f * env;
                handW = 24;
                break;
            }
            case Fidget.Pockets:
                hN = Ease(restN, new(Arm * 0.05f, Torso * 0.92f)); hF = Ease(restF, new(-Arm * 0.12f, Torso * 0.92f));
                eN = eF = new(-1, -0.2f);
                leanT = -0.07f * env;
                pdxT = MathF.Sin(t * 2.4f) * 1.4f * S * env;   // rocking heel to toe
                tiltT += MathF.Sin(t * 1.2f) * 0.1f * env * Facing;
                break;
            case Fidget.Hiccup:
            {
                // Every so often a little jolt.
                float hic = MathF.Max(0, 1 - ((t % 0.85f) / 0.12f));
                hipT = StandHip * (1 + 0.04f * hic * env);
                tiltT -= 0.15f * hic * Facing;
                hN = Ease(restN, new(Arm * 0.4f, Torso * 0.28f)); eN = new(-0.2f, 1);
                break;
            }
            case Fidget.Peek:
                // Hand shading the eyes, peering into the distance.
                hN = Ease(restN, new(HeadR * 1.2f, -(HeadR * 1.35f + NeckGap))); eN = new(0.6f, 1);
                leanT = 0.12f * env; tiltT -= 0.08f * env * Facing;
                pdxT = 1.5f * S * env;
                handW = 18;
                break;
            case Fidget.Stomp:
                hN = Ease(restN, new(Arm * 0.15f, Arm * 0.62f)); hF = Ease(restF, new(-Arm * 0.1f, Arm * 0.62f));
                eN = eF = new(-1, 0.2f);
                leanT = 0.08f * env;
                break;
            case Fidget.Squats:
            {
                float sq = (1 - MathF.Cos(t * 3.6f)) * 0.5f;
                hipT = StandHip * (1 - 0.4f * sq * env);
                leanT = 0.3f * sq * env;
                hN = Ease(restN, new(Arm * 0.92f, Torso * 0.2f)); hF = Ease(restF, new(Arm * 0.88f, Torso * 0.24f));
                eN = eF = new(0, 1);
                break;
            }
            default:
                return false;
        }
        return true;
    }

    /// <summary>Feet for the fidgets that use them (the near foot only; the other stays planted).</summary>
    Vector2 MoreFidgetFoot(Vector2 foot)
    {
        if (Action != Act.Fidget) return foot;
        float t = ActionT, p = M.Clamp01(t / MathF.Max(FidgetDur, 0.1f)), env = Envelope(p);
        return FidgetKind switch
        {
            Fidget.KickPebble => foot + new Vector2(Facing * MathF.Max(0, MathF.Sin(p * MathF.PI * 2)) * 9 * S, -MathF.Max(0, MathF.Sin(p * MathF.PI * 2)) * 2.5f * S),
            Fidget.Balance => foot + new Vector2(-Facing * 3 * S * env, -Leg * 0.38f * env),
            Fidget.Stomp => foot + new Vector2(0, -MathF.Max(0, MathF.Sin(t * 7)) * 5 * S * env),
            _ => foot,
        };
    }

    // ---------------- dancing ----------------

    /// <summary>The animation sheets: always this move (-1: its own choice).</summary>
    public int ForceDanceMove = -1;

    void EaseDanceHands(float t, Vector2 near, Vector2 far, bool reachAround,
                        ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        float edge = FidgetLength(Fidget.Groove) * 0.18f;
        float env = M.Smooth(t / edge) * M.Smooth((FidgetDur - t) / edge);
        hN = Vector2.Lerp(hN, near, env); hF = Vector2.Lerp(hF, far, env);
        if (reachAround)
        {
            float arc = MathF.Sin(env * MathF.PI) * Arm * 0.45f;
            hN.X += arc; hF.X -= arc;
        }
        eN = new(-_hN.Y, _hN.X * Facing);
        eF = new(-_hF.Y, _hF.X * Facing);
    }

    /// <summary>Eight moves; each figure favours a few (its own taste), and moves on every couple of bars.</summary>
    void DancePose(float t, ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW,
                   ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        bool onBeat = World.BeatPhase >= 0;
        float beat = onBeat ? World.BeatPhase : (t * 2.2f) % 1;        // 0..1 through each beat
        float bar = onBeat ? World.BarPhase : (t * 0.55f) % 1;
        float down = MathF.Cos(beat * MathF.Tau);                      // 1 on the beat
        float sway = MathF.Sin(bar * MathF.PI * 2);
        int segment = (int)(t / 4.2f);
        int[] mine = { Id % 8, (Id * 3 + 1) % 8, (Id * 5 + 4) % 8 };   // its favourite three
        int move = ForceDanceMove >= 0 ? ForceDanceMove : mine[(segment + Id) % 3];
        hipT = StandHip * 0.95f + down * 1.2f * S;
        handW = 30; eN = eF = new(0, 1);
        switch (move)
        {
            case 0:   // the sway
                pdxT = sway * 2.5f * S;
                EaseDanceHands(t, new(Arm * 0.4f, -Arm * 0.25f + down * Arm * 0.3f), new(-Arm * 0.2f, -Arm * 0.1f - down * Arm * 0.3f), true, ref hN, ref hF, ref eN, ref eF);
                tiltT += sway * 0.12f;
                break;
            case 1:   // raise the roof
            {
                float edge = FidgetLength(Fidget.Groove) * 0.18f;
                float env = M.Smooth(t / edge) * M.Smooth((FidgetDur - t) / edge);
                hN = Vector2.Lerp(hN, new(Arm * 0.3f, -Arm * (0.55f + 0.35f * MathF.Max(0, down))), env);
                hF = Vector2.Lerp(hF, new(-Arm * 0.15f, -Arm * (0.55f + 0.35f * MathF.Max(0, down))), env);
                float reachArc = MathF.Sin(env * MathF.PI) * Arm * 0.45f;
                hN.X += reachArc; hF.X -= reachArc;
                eN = new(-_hN.Y, _hN.X * Facing);
                eF = new(-_hF.Y, _hF.X * Facing);
                hipT -= MathF.Max(0, down) * 1.5f * S;
                break;
            }
            case 2:   // disco point: up to the sky, down to the floor
            {
                bool up = ((int)(t * 2.2f)) % 2 == 0;
                hN = up ? new(Arm * 0.55f, -Arm * 0.82f) : new(Arm * 0.62f, Arm * 0.75f);
                hF = new(1.5f * S, Torso * 0.85f); eF = new(-1, 0);
                pdxT = (up ? 1.5f : -1.5f) * S;
                tiltT -= (up ? 0.25f : -0.1f) * Facing;
                break;
            }
            case 3:   // the robot: stiff, square, on the beat
            {
                int step = (int)(t * 2.2f) % 4;
                hN = step % 2 == 0 ? new(Arm * 0.62f, Torso * 0.3f) : new(Arm * 0.2f, -Arm * 0.45f);
                hF = step < 2 ? new(-Arm * 0.15f, Torso * 0.55f) : new(Arm * 0.5f, Torso * 0.1f);
                eN = new(-1, 0.6f); eF = new(-1, 0.6f);
                tiltT += (step % 2 == 0 ? 0.15f : -0.15f) * Facing;
                hipT = StandHip * 0.96f;
                handW = 60;   // snappy
                break;
            }
            case 4:   // the twist: hips one way, shoulders the other, low and back up
            {
                float tw = MathF.Sin(t * 8);
                pdxT = tw * 2.2f * S;
                hipT = StandHip * (0.84f + 0.08f * (sway * 0.5f + 0.5f));
                EaseDanceHands(t, new(Arm * 0.35f - tw * Arm * 0.2f, Torso * 0.3f), new(-Arm * 0.1f + tw * Arm * 0.2f, Torso * 0.35f), false, ref hN, ref hF, ref eN, ref eF);
                leanT = -tw * 0.08f;
                break;
            }
            case 5:   // arms overhead, waving side to side
                EaseDanceHands(t, new(Arm * 0.25f + sway * Arm * 0.35f, -Arm * 0.9f), new(-Arm * 0.05f + sway * Arm * 0.35f, -Arm * 0.88f), true, ref hN, ref hF, ref eN, ref eF);
                leanT = sway * 0.1f;
                break;
            case 6:   // bounce and clap on the off-beat
            {
                float c = MathF.Abs(MathF.Sin(beat * MathF.PI));
                hN = new(Arm * 0.5f + c * 3 * S, Torso * 0.15f); hF = new(Arm * 0.5f - c * 3 * S, Torso * 0.17f);
                eN = eF = new(-0.4f, 1);
                hipT -= MathF.Max(0, down) * 2 * S;
                break;
            }
            default:  // the shoulder shimmy
            {
                float sh = MathF.Sin(t * 18);
                hN = new(Arm * 0.35f, Torso * 0.42f + sh * 1.5f * S); hF = new(Arm * 0.25f, Torso * 0.48f - sh * 1.5f * S);
                eN = eF = new(-0.6f, 1);
                leanT = -0.08f + sh * 0.03f;
                tiltT += sh * 0.05f;
                break;
            }
        }
        if (!onBeat) return;
        handW = MathF.Max(handW, 32);
    }

    // ---------------- talking ----------------

    /// <summary>Talking with the hands: a gesture every second or so (explaining, pointing, palms up, hand on the
    /// heart, counting on fingers, waving it away), chosen by who's talking and what moment it is.</summary>
    void TalkPose(float t, ref float tiltT, ref float leanT, ref float handW, ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF)
    {
        int seg = (int)(t / 1.3f);
        int g = (seg * 7 + Id * 3) % 6;
        float k = M.Smooth((t % 1.3f) / 0.25f);
        handW = 16;
        tiltT = MathF.Sin(t * 6) * 0.07f;
        switch (g)
        {
            case 0:   // explaining: open hand moving in small circles
                hN = new(Arm * 0.42f + MathF.Sin(t * 7) * Arm * 0.13f, Torso * 0.35f + MathF.Cos(t * 5.3f) * Arm * 0.13f); eN = new(-1, 0.8f);
                break;
            case 1:   // pointing at someone
                hN = Vector2.Lerp(new(Arm * 0.3f, Torso * 0.5f), new(Arm * 0.95f, Torso * 0.05f), k); eN = new(0, 1);
                leanT = 0.05f;
                break;
            case 2:   // both palms up
                hN = Vector2.Lerp(new(Arm * 0.2f, Torso * 0.6f), new(Arm * 0.6f, Torso * 0.38f), k); hF = Vector2.Lerp(new(-Arm * 0.05f, Torso * 0.6f), new(Arm * 0.2f, Torso * 0.42f), k);
                eN = eF = new(-0.6f, 1);
                tiltT += 0.1f * Facing;
                break;
            case 3:   // hand on the heart
                hN = Vector2.Lerp(new(Arm * 0.3f, Torso * 0.5f), new(Arm * 0.15f, Torso * 0.22f), k); eN = new(-0.5f, 1);
                leanT = -0.04f;
                break;
            case 4:   // counting on fingers: little taps
                hN = new(Arm * 0.45f, Torso * 0.25f); hF = new(Arm * 0.38f, Torso * 0.3f + MathF.Max(0, MathF.Sin(t * 9)) * 1.5f * S);
                eN = eF = new(-0.4f, 1);
                break;
            default:  // waving it away
                hN = new(Arm * 0.5f + MathF.Sin(t * 12) * Arm * 0.12f, Torso * 0.15f); eN = new(-0.2f, 1);
                tiltT -= 0.08f * Facing;
                break;
        }
    }
}
