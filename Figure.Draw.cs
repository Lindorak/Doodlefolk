using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

sealed partial class Figure
{
    static readonly Color4 Outline = new(0, 0, 0, 0.28f);
    static readonly Color4 Pencil = new(0.1f, 0.1f, 0.1f, 0.85f);

    // Order the "animator" sketches strokes in when a figure spawns.
    static readonly (int a, int b, bool far)[] SketchOrder =
    {
        (J.Neck, J.Pelvis, false), (J.Pelvis, J.KneeN, false), (J.KneeN, J.FootN, false),
        (J.Pelvis, J.KneeF, true), (J.KneeF, J.FootF, true), (J.Neck, J.ElbowN, false),
        (J.ElbowN, J.HandN, false), (J.Neck, J.ElbowF, true), (J.ElbowF, J.HandF, true),
    };

    public void Draw(Renderer r)
    {
        if (Mode == Mode.Spawning) { DrawSketch(r); return; }
        DrawGrapple(r);

        // Dead figures fade out (and go grey) before disappearing.
        float fade = Fade;
        Color4 baseColor = Dead ? Color4.Lerp(Color, new Color4(0.55f, 0.55f, 0.55f, 1), M.Clamp01(_fadeT / 1.5f)) : Color;
        Color4 near = new(baseColor.R, baseColor.G, baseColor.B, fade);
        Color4 far = new(baseColor.R * 0.72f, baseColor.G * 0.72f, baseColor.B * 0.72f, fade);
        Color4 outline = new(0, 0, 0, Outline.A * fade);
        float w = LineW, ow = LineW + 1.6f * S;

        DrawLookBack(r, fade);
        foreach (var (a, b) in Bones) r.Line(Jt[a], Jt[b], outline, ow);
        r.Disc(Jt[J.Head], HeadR + 0.8f * S, outline);

        r.Line(Jt[J.Neck], Jt[J.ElbowF], far, w); r.Line(Jt[J.ElbowF], Jt[J.HandF], far, w);
        DrawGear(r, J.ElbowF, J.HandF, 0.75f);
        r.Line(Jt[J.Pelvis], Jt[J.KneeF], far, w); r.Line(Jt[J.KneeF], Jt[J.FootF], far, w);
        r.Line(Jt[J.Neck], Jt[J.Pelvis], near, w);
        r.Disc(Jt[J.Head], HeadR, near);
        r.Line(Jt[J.Pelvis], Jt[J.KneeN], near, w); r.Line(Jt[J.KneeN], Jt[J.FootN], near, w);
        r.Line(Jt[J.Neck], Jt[J.ElbowN], near, w); r.Line(Jt[J.ElbowN], Jt[J.HandN], near, w);
        DrawLookBody(r, fade);
        DrawLookFront(r, fade);
        DrawGear(r, J.ElbowN, J.HandN, 1);
        DrawHealthBar(r);
        if (fade > 0.5f) DrawEmote(r);
    }

    void DrawSketch(Renderer r)
    {
        // Head first (a quick circle pop), then each stroke drawn out like a pen.
        int strokes = SketchOrder.Length + 1;
        float p = SpawnT * strokes;
        float headK = M.Smooth(p);
        Color4 far = M.Shade(Color, 0.72f);
        if (headK > 0)
        {
            r.Disc(Jt[J.Head], (HeadR + 0.8f * S) * headK, Outline);
            r.Disc(Jt[J.Head], HeadR * headK, Color);
        }
        Vector2? tip = p < 1 ? Jt[J.Head] + M.Dir(p * MathF.Tau) * HeadR : null;
        for (int i = 0; i < SketchOrder.Length; i++)
        {
            float k = M.Clamp01(p - (i + 1));
            if (k <= 0) break;
            var (a, b, isFar) = SketchOrder[i];
            Vector2 end = Vector2.Lerp(Jt[a], Jt[b], k);
            r.Line(Jt[a], end, Outline, LineW + 1.6f * S);
            r.Line(Jt[a], end, isFar ? far : Color, LineW);
            if (k < 1) tip = end;
        }
        if (tip is Vector2 t) r.Disc(t, 1.6f * S, Pencil);
    }
}
