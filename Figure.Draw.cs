using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

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

    /// <summary>Everywhere this figure was drawn this frame / last frame (symbols, gear and bubbles included), so
    /// nothing it leaves behind lingers on screen.</summary>
    public System.Drawing.RectangleF? InkNow, InkLast;

    public void Draw(Renderer r)
    {
        var saved = r.BeginInk();
        try { DrawAll(r); }
        finally { if (r.EndInk(saved) is { } ink) InkNow = InkNow is { } n ? System.Drawing.RectangleF.Union(n, ink) : ink; }
    }

    void DrawAll(Renderer r)
    {
        if (Mode == Mode.Spawning) { DrawSketch(r); return; }
        DrawGrapple(r);

        // Dead figures fade out (and go grey) before disappearing.
        float fade = Fade;
        Color4 baseColor = Lit(Dead ? Color4.Lerp(Color, new Color4(0.55f, 0.55f, 0.55f, 1), M.Clamp01(_fadeT / 1.5f)) : Color);
        Color4 near = new(baseColor.R, baseColor.G, baseColor.B, fade);
        Color4 far = new(baseColor.R * 0.84f, baseColor.G * 0.84f, baseColor.B * 0.84f, fade);
        float w = LineW, ow = 1.6f * S;
        float headR = HeadR * (1 + Fat * 0.08f);
        void Bone(int a, int b, Color4 c, bool shade, float extra = 0) { var (wa, wb) = BoneW(a, b, w); Seg(r, Jt[a], Jt[b], wa + extra, wb + extra, c, shade); }

        DrawTrails(r, near);
        DrawLookBack(r, fade);
        // Rounded caps overlap at elbows, knees and hips. Draw solid into one bounded layer so the
        // thin outline keeps 28% opacity throughout its silhouette instead of darkening at each join.
        BeginBodyOutline(r, Outline.A * fade, headR);
        try
        {
            Color4 ink = new(0, 0, 0, 1);
            foreach (var (a, b) in Bones) if (!(a == J.Neck && b == J.Pelvis) || Fat <= 0.02f) Bone(a, b, ink, false, ow);
            r.Disc(Jt[J.Head], headR + 0.8f * S, ink);
            DrawTorsoOutline(r, ink, w);
            DrawFullFaceOutline(r, ink);
        }
        finally { r.EndOpacityLayer(); }

        Bone(J.Neck, J.ElbowF, far, false); Bone(J.ElbowF, J.HandF, far, false);
        DrawGear(r, J.ElbowF, J.HandF, 0.75f);
        Bone(J.Pelvis, J.KneeF, far, false); Bone(J.KneeF, J.FootF, far, false);
        DrawTorso(r, near, w);
        DrawFullFace(r, near);
        // A little form, without the glossy spot and heavy rim used on round props.
        r.Disc(Jt[J.Head], headR, near);
        r.ShadeDisc(Jt[J.Head], headR, near.A * 0.35f);
        Bone(J.Pelvis, J.KneeN, near, true); Bone(J.KneeN, J.FootN, near, true);
        Bone(J.Neck, J.ElbowN, near, true); Bone(J.ElbowN, J.HandN, near, true);
        DrawSweat(r);
        DrawLookBody(r, fade);
        DrawLookFront(r, fade);
        DrawGear(r, J.ElbowN, J.HandN, 1);
        DrawCane(r, fade);
        DrawTeamBadge(r, fade);
        DrawRod(r, fade);
        DrawHealthBar(r);
        DrawRope(r, fade);
        DrawJuggle(r, fade);
        DrawManpu(r, fade);
        if (fade > 0.5f && !NoBubbles) DrawEmote(r);
        if (fade > 0.5f && !NoBubbles) DrawDream(r);
    }

    void BeginBodyOutline(Renderer r, float opacity, float headR)
    {
        Vector2 min = Jt[0], max = Jt[0];
        foreach (Vector2 p in Jt) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        // Includes tapered upper limbs, the widest torso and double chin, with antialias margin.
        float pad = headR + (2 + 14 * Fat) * S;
        r.BeginOpacityLayer(new System.Drawing.RectangleF(min.X - pad, min.Y - pad, max.X - min.X + 2 * pad, max.Y - min.Y + 2 * pad), opacity);
    }

    /// <summary>Drawing them clipped (inside a tent): leave their bubbles for DrawBubbles, outside the clip, so a
    /// "z" still shows who's in there.</summary>
    public bool NoBubbles;

    public void DrawBubbles(Renderer r)
    {
        if (Fade <= 0.5f || Mode == Mode.Spawning) return;
        var saved = r.BeginInk();
        try { DrawEmote(r); DrawDream(r); }
        finally { if (r.EndInk(saved) is { } ink) InkNow = InkNow is { } n ? System.Drawing.RectangleF.Union(n, ink) : ink; }
    }

    void DrawSketch(Renderer r)
    {
        // Head first (a quick circle pop), then each stroke drawn out like a pen.
        int strokes = SketchOrder.Length + 1;
        float p = SpawnT * strokes;
        float headK = M.Smooth(p);
        Color4 far = M.Shade(Color, 0.84f);
        BeginBodyOutline(r, Outline.A, HeadR);
        try
        {
            Color4 ink = new(0, 0, 0, 1);
            if (headK > 0) r.Disc(Jt[J.Head], (HeadR + 0.8f * S) * headK, ink);
            for (int i = 0; i < SketchOrder.Length; i++)
            {
                float k = M.Clamp01(p - (i + 1));
                if (k <= 0) break;
                var (a, b, _) = SketchOrder[i];
                r.Line(Jt[a], Vector2.Lerp(Jt[a], Jt[b], k), ink, LineW + 1.6f * S);
            }
        }
        finally { r.EndOpacityLayer(); }
        if (headK > 0)
        {
            r.Disc(Jt[J.Head], HeadR * headK, Color);
        }
        Vector2? tip = p < 1 ? Jt[J.Head] + M.Dir(p * MathF.Tau) * HeadR : null;
        for (int i = 0; i < SketchOrder.Length; i++)
        {
            float k = M.Clamp01(p - (i + 1));
            if (k <= 0) break;
            var (a, b, isFar) = SketchOrder[i];
            Vector2 end = Vector2.Lerp(Jt[a], Jt[b], k);
            r.Line(Jt[a], end, isFar ? far : Color, LineW);
            if (k < 1) tip = end;
        }
        if (tip is Vector2 t) r.Disc(t, 1.6f * S, Pencil);
    }
}
