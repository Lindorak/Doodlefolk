using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Drawing a skipping rope: the loop round a solo skipper, or the long rope(s) between two turners.</summary>
sealed partial class Figure
{
    /// <summary>The rope's angle (0 overhead, π under the feet), or -1 when not skipping or turning.</summary>
    public float SkipPhase = -1;
    public int SkipRopes = 1;
    public bool SkipCross;
    /// <summary>A turner holding the other end of a long rope (set on the turner who draws it).</summary>
    public Figure? RopeMate;

    static readonly Color4 RopeInk = new(0.12f, 0.11f, 0.1f, 0.9f);
    static readonly Color4 RopeRed = new(0.9f, 0.25f, 0.25f, 1), RopeBlue = new(0.25f, 0.45f, 0.9f, 1);

    /// <summary>Hands while skipping (little circles at the hips) or turning (big circles towards the rope).</summary>
    void SkipArms(ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF, ref float handW)
    {
        if (SkipPhase < 0 && !(Brain.Turning && RopeMate == null)) return;
        float ph = SkipPhase >= 0 ? SkipPhase : _time * 8;
        if (Brain.Turning)
        {
            hN = new(Arm * 0.62f + MathF.Sin(ph) * Arm * 0.18f, Torso * 0.42f - MathF.Cos(ph) * Arm * 0.2f);
            hF = new(Arm * 0.5f, Torso * 0.7f);
        }
        else
        {
            float c = MathF.Cos(ph) * Arm * 0.07f, s = MathF.Sin(ph) * Arm * 0.07f;
            hN = SkipCross ? new(Arm * 0.32f + c, Torso * 0.5f + s) : new(Arm * 0.3f + c, Torso * 0.82f + s);
            hF = SkipCross ? new(Arm * 0.36f - c, Torso * 0.55f - s) : new(-Arm * 0.12f - c, Torso * 0.82f - s);
        }
        eN = eF = new(-1, 0.4f);
        handW = 30;
    }

    void DrawRope(Renderer r, float fade)
    {
        if (fade < 0.5f || SkipPhase < 0) return;
        float w = 0.75f * S;
        if (RopeMate is { } mate)
        {
            // A long rope between our hand and our partner's, swinging over the jumper and under their feet.
            Vector2 a = Jt[J.HandN], b = mate.Jt[J.HandN];
            float ground = MathF.Max(Base.Y, mate.Base.Y) - 1 * S;
            for (int k = 0; k < SkipRopes; k++)
            {
                float ph = SkipPhase + k * MathF.PI;
                float lift = (Leg + Torso + HeadR * 2) * 1.05f;
                var mid = (a + b) / 2;
                // Quadratic curve: its lowest point is (a + 2c + b) / 4, kept on the ground.
                float lowY = mid.Y - MathF.Cos(ph) * lift;
                lowY = MathF.Min(lowY, ground);
                var c = new Vector2(mid.X, 2 * lowY - mid.Y);
                Curve(r, a, c, b, RopeInk, w + 0.7f * S);
                Curve(r, a, c, b, k == 0 ? RopeRed : RopeBlue, w);
            }
            return;
        }
        if (Brain.Turning) return;   // the other turner draws it
        // Solo: a loop from the hands round the body (over the head, down in front, under the feet, up behind).
        Vector2 h = (Jt[J.HandN] + Jt[J.HandF]) / 2;
        float top = Jt[J.Head].Y - HeadR - 4 * S, bottom = MathF.Max(Jt[J.FootN].Y, Jt[J.FootF].Y) + 2 * S;
        var centre = new Vector2(Base.X + Facing * 1.5f * S, (top + bottom) / 2);
        float ry = (bottom - top) / 2, rx = ry * 0.55f;
        Vector2 At(float a) => centre + new Vector2(Facing * rx * MathF.Sin(a), -ry * MathF.Cos(a));
        var p = At(SkipPhase);
        var q1 = At(SkipPhase - 0.95f) + (At(SkipPhase - 0.95f) - centre) * 0.15f;
        var q2 = At(SkipPhase + 0.95f) + (At(SkipPhase + 0.95f) - centre) * 0.15f;
        Curve(r, h, q1, p, RopeInk, w + 0.7f * S); Curve(r, p, q2, h, RopeInk, w + 0.7f * S);
        Curve(r, h, q1, p, RopeRed, w); Curve(r, p, q2, h, RopeRed, w);
    }

    static void Curve(Renderer r, Vector2 a, Vector2 c, Vector2 b, Color4 col, float w)
    {
        Vector2 prev = a;
        for (int i = 1; i <= 12; i++)
        {
            float t = i / 12f, u = 1 - t;
            var p = u * u * a + 2 * u * t * c + t * t * b;
            r.Line(prev, p, col, w);
            prev = p;
        }
    }
}
