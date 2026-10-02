using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Body weight, drawn properly. At a healthy weight a figure is the classic stick; as it gets heavier the
/// torso fills out into a real shape (a rounder back, a belly that bulges forward, or out to both sides when it faces
/// you, softer hips), the limbs thicken (upper arms and thighs most, tapering to the hands and feet), the face rounds
/// out (cheeks, then a double chin), arms rest out from the body, and the walk turns into a heavier, rolling gait
/// with a lean back to balance. The belly has a little jiggle when it lands, and running makes heavier figures sweat.</summary>
sealed partial class Figure
{
    float _bellyJ, _bellyV;
    /// <summary>The torso outline this frame (shirts are drawn to fit it).</summary>
    readonly Vector2[] _torso = new Vector2[2 * TorsoSteps + 2];
    int _torsoN;
    const int TorsoSteps = 9;

    /// <summary>Spring for the belly's wobble.</summary>
    void TickBody(float dt)
    {
        _bellyV += (-_bellyJ * 170 - _bellyV * 9) * dt;
        _bellyJ = Math.Clamp(_bellyJ + _bellyV * dt, -0.6f, 0.6f);
    }

    /// <summary>Landing or a hard bump sets the belly wobbling.</summary>
    void Jiggle(float impact) { if (Fat > 0.05f) _bellyV += M.Clamp01(impact / (1500 * S)) * 4 * Fat; }

    /// <summary>Widths at each end of a bone (thicker toward the body the heavier the figure).</summary>
    (float a, float b) BoneW(int a, int b, float w)
    {
        float k = Fat;
        if (k <= 0.001f) return (w, w);
        return (a, b) switch
        {
            (J.Neck, J.ElbowN) or (J.Neck, J.ElbowF) => (w * (1 + 1.6f * k), w * (1 + 0.85f * k)),
            (J.ElbowN, J.HandN) or (J.ElbowF, J.HandF) => (w * (1 + 0.85f * k), w * (1 + 0.35f * k)),
            (J.Pelvis, J.KneeN) or (J.Pelvis, J.KneeF) => (w * (1 + 2.3f * k), w * (1 + 1.15f * k)),
            (J.KneeN, J.FootN) or (J.KneeF, J.FootF) => (w * (1 + 1.15f * k), w * (1 + 0.5f * k)),
            _ => (w, w),
        };
    }

    /// <summary>A limb segment: a plain round-capped line when thin, a tapered capsule when not.</summary>
    void Seg(Renderer r, Vector2 a, Vector2 b, float wa, float wb, Color4 c, bool shade = false)
    {
        if (MathF.Abs(wa - wb) < 0.05f) { if (shade) r.ShadedLine(a, b, c, wa); else r.Line(a, b, c, wa); return; }
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 0.01f) { r.Disc(a, wa * 0.5f, c); return; }
        Vector2 n = new Vector2(-d.Y, d.X) / len;
        r.FillPolygon(stackalloc Vector2[] { a + n * wa * 0.5f, b + n * wb * 0.5f, b - n * wb * 0.5f, a - n * wa * 0.5f }, c);
        r.Disc(a, wa * 0.5f, c);
        r.Disc(b, wb * 0.5f, c);
        if (shade && Gfx.Q.Shading)
        {
            Vector2 lit = n.X + n.Y < 0 ? n : -n;   // toward the light (top left)
            r.Line(a + lit * wa * 0.22f, b + lit * wb * 0.22f, Gfx.Lighter(c, 0.35f).A(0.5f * c.A), MathF.Min(wa, wb) * 0.25f);
        }
    }

    readonly Vector2[] _torsoOut = new Vector2[2 * TorsoSteps + 2];

    /// <summary>Build the torso outline from the neck down to the hips (and a slightly bigger copy for the ink line).</summary>
    void BuildTorso(float w)
    {
        float k = Fat;
        Vector2 neck = Jt[J.Neck], pel = Jt[J.Pelvis];
        Vector2 down = pel - neck;
        float len = MathF.Max(1e-3f, down.Length());
        Vector2 u = down / len;
        Vector2 side = new(-u.Y, u.X);
        // "Front" is the way it's facing (in side view); in front view both sides fill out evenly.
        if (!FrontView && MathF.Sign(side.X) != Facing) side = -side;
        _torsoFront = side;
        float g = MathF.Sqrt(k);   // shows early (chubby), grows steadily to obese
        // Weight goes on everywhere, not just the front: shoulders and chest broaden, the back and sides fill out, the
        // seat rounds; the belly is only a modest extra on top of all that (otherwise it reads as pregnant).
        float girth = g * S * (1.8f + 4.6f * k);
        float belly = g * S * (0.6f + 2.4f * k) * (1 + _bellyJ * 0.35f);
        float sag = 0.62f + 0.06f * k + _bellyJ * 0.05f;
        float ink = 0.85f * S;
        Span<float> fr = stackalloc float[TorsoSteps + 1], bk = stackalloc float[TorsoSteps + 1];
        for (int i = 0; i <= TorsoSteps; i++)
        {
            float t = i / (float)TorsoSteps;
            // Rounded shoulders easing out from the neck, fullest through the middle, hips staying wide into the thighs.
            float shape = 0.42f + 0.58f * MathF.Pow(MathF.Sin(MathF.PI * (0.04f + 0.86f * t)), 0.7f) + 0.22f * t * t * t;
            float bump = MathF.Pow(MathF.Max(0, MathF.Sin(MathF.PI * M.Clamp01((t - (sag - 0.4f)) / 0.8f))), 1.4f);
            float front = w * 0.5f + girth * shape + belly * bump;
            float back = w * 0.5f + girth * shape * 0.92f + g * S * 1.6f * k * t * t;
            if (FrontView) { float avg = (front + back) * 0.52f + g * S * 1.2f * k * MathF.Max(0, t - 0.4f); front = back = avg; }
            fr[i] = front; bk[i] = back;
        }
        int n = 0;
        for (int i = 0; i <= TorsoSteps; i++)
        {
            float t = i / (float)TorsoSteps, ext = i == 0 ? -ink : i == TorsoSteps ? ink : 0;
            _torso[n] = neck + u * (len * t) + side * fr[i];
            _torsoOut[n++] = neck + u * (len * t + ext) + side * (fr[i] + ink);
        }
        for (int i = TorsoSteps; i >= 0; i--)
        {
            float t = i / (float)TorsoSteps, ext = i == 0 ? -ink : i == TorsoSteps ? ink : 0;
            _torso[n] = neck + u * (len * t) - side * bk[i];
            _torsoOut[n++] = neck + u * (len * t + ext) - side * (bk[i] + ink);
        }
        _torsoN = n;
    }

    Vector2 _torsoFront;

    /// <summary>The torso: a filled shape with an ink outline (or the classic line when the figure's slim).</summary>
    void DrawTorso(Renderer r, Color4 body, Color4 outline, float w)
    {
        if (Fat <= 0.02f) { r.ShadedLine(Jt[J.Neck], Jt[J.Pelvis], body, w); _torsoN = 0; return; }
        BuildTorso(w);
        r.FillPolygon(_torsoOut.AsSpan(0, _torsoN), outline);
        r.FillPolygon(_torso.AsSpan(0, _torsoN), body);
        if (Gfx.Q.Shading)
        {
            // Light across the top of the back and chest, a shadow tucked under the belly.
            Vector2 neck = Jt[J.Neck], pel = Jt[J.Pelvis];
            int top = TorsoSteps / 3, low = (int)(TorsoSteps * 0.85f);
            Vector2 hi = Vector2.Lerp(Vector2.Lerp(neck, pel, 0.3f), _torso[top], 0.45f);
            r.Oval(hi, (1.5f + 3 * Fat) * S, (2.5f + 4 * Fat) * S, Gfx.Lighter(body, 0.45f).A(0.2f * body.A));

        }
    }

    /// <summary>Rounder face: fuller cheeks, then a double chin.</summary>
    void DrawFullFace(Renderer r, Color4 body, Color4 outline)
    {
        float k = Fat;
        if (k < 0.25f || (FrontView && Action == Act.SitBack)) return;
        Vector2 h = Jt[J.Head];
        float R = HeadR;
        Vector2 fwd = new(FrontView ? 0 : Facing, 0);
        if (k > 0.5f)
        {
            Vector2 chin = h + fwd * R * 0.3f + new Vector2(0, R * (0.88f + 0.05f * k));
            float rx = R * (0.38f + 0.25f * k), ry = R * (0.16f + 0.12f * k);
            r.Oval(chin, rx + 0.8f * S, ry + 0.8f * S, outline);
            r.Oval(chin, rx, ry, body);
        }
        // Cheeks fill out the jaw line.
        float cr = R * (0.3f + 0.25f * k);
        if (FrontView) { r.Disc(h + new Vector2(-R * 0.55f, R * 0.35f), cr, body); r.Disc(h + new Vector2(R * 0.55f, R * 0.35f), cr, body); }
        else r.Disc(h + fwd * R * 0.45f + new Vector2(0, R * 0.38f), cr, body);
    }

    /// <summary>Heavier figures sweat when they run (or are worn out).</summary>
    void DrawSweat(Renderer r)
    {
        if (Fat < 0.3f || Mode != Mode.Control) return;
        bool working = MathF.Abs(Vel.X) > WalkSpeed * 1.3f || Brain.Stamina < 0.3f;
        if (!working) return;
        var drop = new Color4(0.55f, 0.78f, 1, 0.85f);
        for (int i = 0; i < 2; i++)
        {
            float ph = (_time * 1.4f + i * 0.5f) % 1;
            Vector2 p = Jt[J.Head] + new Vector2((i == 0 ? -1 : 1) * HeadR * 0.9f, -HeadR * 0.3f + ph * HeadR * 1.6f);
            r.Disc(p, (0.9f + 0.4f * (1 - ph)) * S, drop.A(1 - ph));
        }
    }

    /// <summary>After posing: heavier bodies stand and move differently (lean back, arms out from the belly, knees
    /// apart, a rolling walk).</summary>
    void WeightPose(float dt)
    {
        float k = Fat;
        if (k <= 0.02f || Mode != Mode.Control || Climbing) return;
        bool busy = Action is Act.Fight or Act.Swat or Act.Throw or Act.Kick or Act.Tap or Act.Eat or Act.Read or Act.HighFive || AtkT > 0 || HoldN != null;
        Vector2 pel = Jt[J.Pelvis];
        float moving = Grounded ? M.Clamp01(MathF.Abs(Vel.X) / (WalkSpeed + 1)) : 0;
        // Lean back to carry the weight, rocking a little with each step.
        float lean = (FrontView ? 0 : -Facing * k * 0.07f) + MathF.Sin(_time * 9) * k * 0.035f * moving * (FrontView ? 1 : 0.6f);
        if (MathF.Abs(lean) > 1e-4f && Action is not (Act.SitEdge or Act.SitFloor or Act.SitFront or Act.SitBack or Act.Lie or Act.Curl))
            foreach (int j in new[] { J.Head, J.Neck, J.ElbowN, J.HandN, J.ElbowF, J.HandF })
                Jt[j] = pel + M.Rotate(Jt[j] - pel, lean);
        // Arms rest out from the body instead of sinking into it.
        if (!busy)
        {
            Vector2 neck = Jt[J.Neck];
            Vector2 axis = Vector2.Normalize(pel - neck + new Vector2(0, 0.001f));
            Vector2 side = new(-axis.Y, axis.X);
            if (FrontView)
            {
                Vector2 out1 = new(k * 5 * S, 0);
                Jt[J.ElbowN] += Jt[J.ElbowN].X >= neck.X ? out1 : -out1; Jt[J.HandN] += (Jt[J.HandN].X >= neck.X ? out1 : -out1) * 1.4f;
                Jt[J.ElbowF] += Jt[J.ElbowF].X >= neck.X ? out1 : -out1; Jt[J.HandF] += (Jt[J.HandF].X >= neck.X ? out1 : -out1) * 1.4f;
                Jt[J.KneeN] += Jt[J.KneeN].X >= pel.X ? new Vector2(k * 2.5f * S, 0) : new Vector2(-k * 2.5f * S, 0);
                Jt[J.KneeF] += Jt[J.KneeF].X >= pel.X ? new Vector2(k * 2.5f * S, 0) : new Vector2(-k * 2.5f * S, 0);
            }
            else
            {
                if (MathF.Sign(side.X) != Facing) side = -side;
                // The near arm rides over the belly, the far one sits behind the back.
                float bellyAt(Vector2 p) => M.Clamp01(1 - MathF.Abs(Vector2.Dot(p - Vector2.Lerp(neck, pel, 0.62f), axis)) / (Torso * 0.6f + 0.01f));
                Jt[J.HandN] += side * k * 8 * S * bellyAt(Jt[J.HandN]);
                Jt[J.ElbowN] += side * k * 4 * S * bellyAt(Jt[J.ElbowN]);
                Jt[J.HandF] -= side * k * 3 * S * bellyAt(Jt[J.HandF]);
                Jt[J.ElbowF] -= side * k * 2.5f * S * bellyAt(Jt[J.ElbowF]);
                Jt[J.KneeN] += side * k * 1.5f * S;
            }
        }
    }
}
