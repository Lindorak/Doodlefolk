using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Poses for riding (pedalling a bike, balancing on a skateboard and kicking it along, sitting low in a
/// go-kart), swimming (front crawl when going somewhere, treading water otherwise) and fishing (a rod, a line and a
/// float that bobs, and dips when something bites).</summary>
sealed partial class Figure
{
    public Item? Riding;
    public bool Swimming;
    /// <summary>The water it's fishing in, where the float is, and whether something's biting.</summary>
    public Item? FishingIn;
    public Vector2 Bobber;
    public bool Bite;

    bool RideSwimPose(ref float hipT, ref float leanT, ref float tiltT, ref float pdxT, ref float handW, ref Vector2 hN, ref Vector2 hF,
                      ref Vector2 fN, ref Vector2 fF, ref Vector2 eN, ref Vector2 eF, ref Vector2 kPref)
    {
        if (Riding is { } v)
        {
            float u = v.Sc;
            switch (v.Def.Key)
            {
                case "bike":
                {
                    hipT = 19.5f * u; pdxT = -6 * u; leanT = 0.35f;
                    float a = v.WheelAngle * 0.8f * (Facing > 0 ? 1 : -1), cr = 3.2f * u;
                    Vector2 crank = new(3 * u, hipT - 7 * u);
                    fN = crank + new Vector2(MathF.Cos(a), MathF.Sin(a)) * cr;
                    fF = crank - new Vector2(MathF.Cos(a), MathF.Sin(a)) * cr;
                    kPref = new(1, -1);
                    Vector2 neck = new(MathF.Sin(0.35f) * Torso, -MathF.Cos(0.35f) * Torso);
                    hN = new Vector2(15.5f * u, -2.5f * u) - neck; hF = hN + new Vector2(-1 * u, 0.5f * u);
                    eN = eF = new(0.2f, 1); handW = 30;
                    return true;
                }
                case "gokart":
                {
                    hipT = 7 * u; pdxT = -9 * u; leanT = -0.08f;
                    fN = new(Leg * 0.82f, hipT - 6 * u); fF = new(Leg * 0.78f, hipT - 6.5f * u);
                    kPref = new(1, -1);
                    Vector2 neck = new(MathF.Sin(-0.08f) * Torso, -MathF.Cos(-0.08f) * Torso);
                    float steer = MathF.Sin(_time * 1.7f + Id) * 0.8f * u;
                    hN = new Vector2(17 * u, -7.5f * u + steer) - neck; hF = new Vector2(17 * u, -6.5f * u - steer) - neck;
                    eN = eF = new(0, 1); handW = 26;
                    return true;
                }
                default:
                {
                    // Skateboard: side-on, knees soft, arms out; a kick along now and then.
                    float deck = 3.8f * u;
                    float sway = MathF.Sin(_time * 2.1f + Id);
                    hipT = StandHip * 0.9f + deck; leanT = 0.05f + sway * 0.04f;
                    bool push = MathF.Abs(Vel.X) < 150 * S && MathF.Abs(DesiredVX) > 20 * S;
                    fN = new(4.5f * S, hipT - deck);
                    if (push)
                    {
                        float k = (_time * 2.4f) % 1;
                        fF = new(-2 * S - k * 10 * S, k < 0.75f ? hipT : hipT - deck - 3 * S);
                        leanT += 0.12f;
                    }
                    else fF = new(-5 * S, hipT - deck);
                    kPref = new(1, -0.4f);
                    hN = new(Arm * 0.62f, Arm * 0.38f + sway * 2 * S); hF = new(-Arm * 0.6f, Arm * 0.42f - sway * 2 * S);
                    eN = eF = new(0, 1); handW = 14;
                    tiltT += sway * 0.05f;
                    return true;
                }
            }
        }
        if (Swimming)
        {
            float t = _time;
            if (MathF.Abs(Vel.X) > 12 * S)
            {
                // Front crawl.
                hipT = 3.4f * S + MathF.Sin(t * 3) * 0.4f * S; leanT = 1.42f;
                fN = new(-Leg * 0.95f, 2.5f * S + MathF.Sin(t * 10) * 1.6f * S);
                fF = new(-Leg * 0.92f, 2.5f * S - MathF.Sin(t * 10) * 1.6f * S);
                kPref = new(0, -1);
                float a = t * 3.4f;
                hN = new(MathF.Cos(a) * Arm * 0.85f, MathF.Sin(a) * Arm * 0.7f);
                hF = new(MathF.Cos(a + MathF.PI) * Arm * 0.85f, MathF.Sin(a + MathF.PI) * Arm * 0.7f);
                eN = eF = new(0, -1); handW = 24; tiltT -= 0.45f * Facing;
            }
            else
            {
                // Treading water: waist deep, hands sculling.
                hipT = 2 * S + MathF.Sin(t * 2.2f) * 0.8f * S; leanT = 0.04f;
                fN = new(2 * S, 1 * S); fF = new(-1.5f * S, 1.5f * S);
                kPref = new(1, -1);
                float k = MathF.Sin(t * 4.2f + Id);
                hN = new(Arm * 0.55f + k * Arm * 0.18f, Torso * 0.78f); hF = new(-Arm * 0.35f - k * Arm * 0.18f, Torso * 0.8f);
                eN = eF = new(-1, 0.3f); handW = 16;
            }
            return true;
        }
        if (FishingIn != null && Action == Act.SitFloor)
        {
            SitLegs(ref hipT, ref leanT, ref fN, ref fF, ref kPref);
            hN = new(Arm * 0.58f, Torso * 0.42f + (Bite ? MathF.Sin(_time * 18) * 1.2f * S : 0)); hF = new(Arm * 0.4f, Torso * 0.55f);
            eN = eF = new(-0.4f, 1); handW = 20;
            return true;
        }
        return false;
    }

    bool FeetFreeRideSwim => Grounded && (Riding != null || Swimming);

    /// <summary>The fishing rod, its line and the float.</summary>
    void DrawRod(Renderer r, float fade)
    {
        if (FishingIn == null || Mode != Mode.Control) return;
        Vector2 hand = Jt[J.HandN];
        Vector2 dir = Vector2.Normalize(new Vector2(Facing * 0.82f, -0.58f));
        Vector2 butt = hand - dir * 4 * S;
        Vector2 tip = hand + dir * 26 * S + (Bite ? new Vector2(0, 5 * S) : Vector2.Zero);
        Vector2 mid = Vector2.Lerp(butt, tip, 0.55f) + (Bite ? new Vector2(0, 2.5f * S) : Vector2.Zero);
        var rod = Lit(new Color4(0.42f, 0.28f, 0.16f, fade));
        r.Line(butt, mid, rod, 1.6f * S);
        r.Line(mid, tip, rod, 1.0f * S);
        Vector2 bob = Bobber + new Vector2(0, (Bite ? 2.2f : MathF.Sin(_time * 2.5f) * 0.5f) * S);
        r.Line(tip, bob, new Color4(0.15f, 0.15f, 0.15f, 0.55f * fade), 0.5f * S);
        r.Disc(bob, 1.7f * S, new Color4(0.95f, 0.95f, 0.95f, fade));
        r.Disc(bob + new Vector2(0, -0.6f * S), 1.1f * S, new Color4(0.9f, 0.2f, 0.18f, fade));
    }
}
