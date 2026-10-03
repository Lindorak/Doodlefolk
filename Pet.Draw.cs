using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Drawing: cats and dogs in every pose (walking, sitting, curled up asleep, crouched to stalk, head down at
/// the bowl, squatting, cocking a leg, grooming, stretching, play-bowing, belly up for a rub, arched and hissing,
/// cowering, up on their hind legs), parrots perched, flying and fluffed up; young ones with big heads and short legs;
/// heavier ones rounder; and the thought bubble saying what they want.</summary>
sealed partial class Pet
{
    /// <summary>Everywhere this pet was drawn this frame / last frame (see Figure.InkNow).</summary>
    public System.Drawing.RectangleF? InkNow, InkLast;

    public void Draw(Renderer r)
    {
        var saved = r.BeginInk();
        try { DrawAll(r); }
        finally { if (r.EndInk(saved) is { } ink) InkNow = InkNow is { } n ? System.Drawing.RectangleF.Union(n, ink) : ink; }
    }

    void DrawAll(Renderer r)
    {
        if (_scuffleWith != null && _st == State.Scuffle)
        {
            if (ScuffleCentre is Vector2 sc) DrawScuffle(r, sc);
            return;
        }
        if (Kind == PetKind.Parrot) DrawParrot(r);
        else if (Kind == PetKind.Rabbit) DrawRabbit(r);
        else if (Kind == PetKind.Hamster) DrawHamster(r);
        else DrawBeast(r);
        DrawHealth(r);
        DrawBubbles(r);
    }

    static Color4 Darken(Color4 c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);

    void DrawBeast(Renderer r)
    {
        float s = S, f = Facing;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.95f);
        var col = Lit(Wet > 0.3f ? Color4.Lerp(Color, Darken(Color, 0.75f), Wet * 0.5f) : Color);
        var dark = Darken(col, 0.7f);
        bool cat = Kind == PetKind.Cat;
        float L = Length, H = Height, g = Girth;
        float headK = 1 + (1 - Age) * 0.45f;
        Vector2 P(float x, float y) => Pos + new Vector2(x * f, y);
        var pose = Held || (!Grounded && !Flying) ? Pose.Air : _pose;
        if (_st == State.Sleep && Grounded && !Held && pose == Pose.Curl)
        {
            // Curled up: a round body, head tucked in, tail wrapped round.
            var c = P(0, -H * 0.32f * g);
            r.Oval(c, L * 0.46f * g + 1.2f * s, H * 0.34f * g + 1.2f * s, ink);
            r.Oval(c, L * 0.46f * g, H * 0.34f * g, col);
            float hr0 = H * 0.26f * headK;
            r.Disc(P(L * 0.3f, -H * 0.3f), hr0 + 1.1f * s, ink);
            r.Disc(P(L * 0.3f, -H * 0.3f), hr0, col);
            if (cat) { EarsCat(r, P(L * 0.3f, -H * 0.3f), hr0, f, ink, 0.6f); r.Line(P(-L * 0.45f, -H * 0.2f), P(-L * 0.1f, -H * 0.02f), dark, 2.4f * s); }
            r.Line(P(L * 0.33f, -H * 0.32f), P(L * 0.4f, -H * 0.32f), ink, 0.9f * s);   // closed eye
            float z = (_sleepZ * 0.5f) % 1;
            r.Text("z", P(L * 0.35f, -H * 0.9f - z * 14 * s), (6 + z * 4) * s, Ui.Ink.A(1 - z), true);
            return;
        }
        if (pose == Pose.Belly && Grounded)
        {
            // Belly up, paws in the air.
            var c = P(0, -H * 0.3f);
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -0.25f : 0.22f) * L + (i % 2) * 4 * s;
                float wig = MathF.Sin(_tail * 6 + i) * 2 * s;
                r.Line(P(x, -H * 0.45f), P(x + wig, -H * 1.0f), i % 2 == 0 ? dark : col, (cat ? 2.6f : 3.2f) * s);
            }
            r.Oval(c, L * 0.44f * g + 1.2f * s, H * 0.3f * g + 1.2f * s, ink);
            r.Oval(c, L * 0.44f * g, H * 0.3f * g, col);
            r.Oval(c + new Vector2(0, -H * 0.08f), L * 0.3f * g, H * 0.16f * g, Gfx.Lighter(col, 0.35f));
            var hh = P(L * 0.5f, -H * 0.3f);
            float hrb = H * (cat ? 0.3f : 0.33f) * headK;
            r.Disc(hh, hrb + 1.2f * s, ink); r.Disc(hh, hrb, col);
            r.Line(hh + new Vector2(-hrb * 0.3f, -hrb * 0.1f), hh + new Vector2(hrb * 0.1f, -hrb * 0.1f), ink, 1 * s);
            if (!cat) r.Line(hh + new Vector2(f * hrb * 0.4f, hrb * 0.5f), hh + new Vector2(f * hrb * 0.5f, hrb * 0.95f), new Color4(0.9f, 0.4f, 0.5f, 1), 2 * s);   // tongue out
            return;
        }

        // ---- the skeleton for this pose: hip and shoulder (body ends), head, where the feet go ----
        float hipY = -H * 0.62f, chestY = -H * 0.62f;
        float hipX = -L * 0.3f, chestX = L * 0.28f;
        Vector2 headOff = new(L * 0.55f, -H * 0.88f);
        float tailAng = 0.55f;   // radians up from straight back
        float ears = 1;          // 1 up, 0 flat back
        bool eyesShut = false, eyesWide = false, mouthOpen = false;
        bool hindFold = false, frontFold = false, frontUp = false, legLift = false;
        float bodyThick = H * (cat ? 0.48f : 0.58f) * g;
        switch (pose)
        {
            case Pose.Sit: hipY = -H * 0.32f; chestY = -H * 0.66f; hipX = -L * 0.18f; headOff = new(L * 0.4f, -H * 1.02f); hindFold = true; tailAng = 0.05f; break;
            case Pose.Lie: hipY = chestY = -H * 0.3f; headOff = new(L * 0.5f, -H * 0.62f); hindFold = frontFold = true; tailAng = -0.1f; break;
            case Pose.Crouch: hipY = -H * 0.42f; chestY = -H * 0.36f; headOff = new(L * 0.55f, -H * 0.48f); tailAng = -0.05f + MathF.Sin(_tail * 9) * 0.1f; eyesWide = true; ears = 0.6f; break;
            case Pose.HeadDown: headOff = new(L * 0.62f, -H * 0.3f); tailAng = cat ? 0.4f : 0.7f; break;
            case Pose.Squat: hipY = -H * 0.36f; chestY = -H * 0.6f; headOff = new(L * 0.5f, -H * 0.9f); tailAng = 1.0f; hindFold = true; break;
            case Pose.LegLift: legLift = true; tailAng = 0.8f; break;
            case Pose.Groom: hipY = -H * 0.32f; chestY = -H * 0.62f; hipX = -L * 0.18f; headOff = new(L * 0.42f, -H * 0.6f + MathF.Sin(_tail * 7) * 1.5f * s); hindFold = true; frontUp = true; eyesShut = true; tailAng = 0.1f; break;
            case Pose.Stretch: hipY = -H * 0.75f; chestY = -H * 0.3f; headOff = new(L * 0.6f, -H * 0.5f); tailAng = 1.1f; frontFold = true; eyesShut = true; mouthOpen = true; break;
            case Pose.PlayBow: hipY = -H * 0.72f; chestY = -H * 0.35f; headOff = new(L * 0.6f, -H * 0.62f); tailAng = 1.2f; frontFold = true; mouthOpen = !cat; break;
            case Pose.Arch: hipY = -H * 0.8f; chestY = -H * 0.8f; headOff = new(L * 0.52f, -H * 0.7f); tailAng = 1.5f; ears = 0; eyesWide = true; mouthOpen = true; bodyThick *= cat ? 1.25f : 1; break;
            case Pose.Cower: hipY = -H * 0.36f; chestY = -H * 0.4f; headOff = new(L * 0.5f, -H * 0.45f); tailAng = -0.7f; ears = 0; break;
            case Pose.Upright: hipY = -H * 0.38f; chestY = -H * 1.05f; hipX = -L * 0.12f; chestX = L * 0.1f; headOff = new(L * 0.22f, -H * 1.4f); hindFold = true; frontUp = true; tailAng = 0.2f; break;
            case Pose.Shake: hipY = chestY = -H * 0.62f; tailAng = 0.3f; break;
            case Pose.Air: tailAng = 0.3f; break;
        }
        bool panting = _panting > 0 || (Stamina < 0.3f && StaminaOn);
        if (Kind == PetKind.Dog && panting) mouthOpen = true;
        float happy = Happiness;
        if (pose is Pose.Stand or Pose.Sit && Kind == PetKind.Dog) tailAng = 0.25f + happy * 0.6f;
        if (pose is Pose.Stand && cat) tailAng = 0.3f + happy * 0.9f;
        if (Stress > 0.55f && pose is Pose.Stand or Pose.Sit) { tailAng = -0.5f; ears = 0.3f; }
        if (_st == State.Petted || _st == State.Bath) eyesShut = true;
        if (_st == State.Noise) mouthOpen = true;
        float wob = pose == Pose.Shake ? MathF.Sin(_t * 45) * 2.5f * s : 0;
        Vector2 hip = P(hipX, hipY) + new Vector2(wob, 0), chest = P(chestX, chestY) + new Vector2(wob, 0);

        // ---- legs ----
        float step = Grounded && MathF.Abs(Vel.X) > 4 * s ? 1 : 0;
        float legW = (cat ? 2.6f : 3.2f) * s * (1 + (g - 1) * 0.4f);
        void Leg(Vector2 top, float footX, float ph, bool far, bool hind)
        {
            var c = far ? dark : col;
            Vector2 foot;
            if (pose == Pose.Air) foot = top + new Vector2((hind ? -5 : 5) * s * f, H * 0.42f);
            else if (hind && (hindFold || (legLift && far)))
            {
                if (legLift && far) { foot = top + new Vector2(-6 * s * f, -1 * s); r.Line(top, foot, c, legW); return; }
                foot = P(footX + L * 0.14f, -0.5f * s);
                Vector2 knee = top + new Vector2(L * 0.12f * f, H * 0.12f);
                r.Line(top, knee, c, legW * 1.15f); r.Line(knee, foot, c, legW);
                return;
            }
            else if (!hind && frontUp) foot = top + new Vector2((far ? 3 : 6) * s * f, (pose == Pose.Groom && !far ? -H * 0.05f : H * 0.18f));
            else if (!hind && frontFold) foot = P(footX + L * 0.2f, -0.5f * s);
            else foot = P(footX + MathF.Sin(ph) * 4 * s * step, -0.5f * s - MathF.Max(0, MathF.Cos(ph)) * 3 * s * step);
            Vector2 mid = Vector2.Lerp(top, foot, 0.5f) + new Vector2((hind ? -1.6f : 0.8f) * s * f, 0);
            r.Line(top, mid, c, legW); r.Line(mid, foot, c, legW);
        }
        Leg(hip + new Vector2(2 * s * f, 0), hipX + 2 * s, _walk + 1.6f, true, true);
        Leg(chest - new Vector2(2 * s * f, 0), chestX - 2 * s, _walk + 0.4f, true, false);

        // ---- tail ----
        float wag = MathF.Sin(_tail * (cat ? 2.2f : (_st is State.Petted or State.Fetch or State.Follow or State.Walk || happy > 0.75f ? 16 : 6)));
        Vector2 t0 = hip + new Vector2(-f * 2 * s, -bodyThick * 0.2f);
        Vector2 dir = new(-f * MathF.Cos(tailAng), -MathF.Sin(tailAng));
        if (cat)
        {
            float bristle = pose == Pose.Arch ? 1.6f : 1;
            Vector2 t1 = t0 + dir * 7 * s + new Vector2(0, wag * 1.2f * s), t2 = t1 + dir * 6 * s + new Vector2(-f * 1.5f * s, -2 * s + wag * 2 * s), t3 = t2 + new Vector2(f * 2 * s + wag * 2.5f * s * f, -3 * s);
            r.Line(t0, t1, ink, 3.6f * s * bristle); r.Line(t1, t2, ink, 3.4f * s * bristle); r.Line(t2, t3, ink, 3.2f * s * bristle);
            r.Line(t0, t1, col, 2.4f * s * bristle); r.Line(t1, t2, col, 2.2f * s * bristle); r.Line(t2, t3, col, 2f * s * bristle);
        }
        else
        {
            Vector2 t1 = t0 + dir * 9 * s + new Vector2(wag * 3 * s * f, 0);
            r.Line(t0, t1, ink, 3.8f * s); r.Line(t0, t1, col, 2.6f * s);
        }

        // ---- body: a capsule from hip to chest (tilts with the pose) ----
        r.Line(hip, chest, ink, bodyThick + 2.4f * s);
        r.Line(hip, chest, col, bodyThick);
        if (Gfx.Q.Shading)
        {
            Vector2 up = new(0, -bodyThick * 0.22f);
            r.Line(hip + up * 0.9f, chest + up * 0.9f, Gfx.Lighter(col, 0.22f).A(0.55f), bodyThick * 0.35f);
            r.Line(hip - up, chest - up, new Color4(0, 0, 0, 0.12f), bodyThick * 0.3f);
        }
        if (g > 1.2f) r.Oval(Vector2.Lerp(hip, chest, 0.5f) + new Vector2(0, bodyThick * 0.3f), L * 0.22f * g, bodyThick * 0.32f, col);   // a belly that sags
        if (Gfx.Q.DetailedArt && cat && col.R > 0.6f && col.G < 0.8f)
            for (int i = 0; i < 3; i++) { var m = Vector2.Lerp(hip, chest, 0.25f + i * 0.22f); r.Line(m + new Vector2(0, -bodyThick * 0.45f), m + new Vector2(f * 1.5f * s, -bodyThick * 0.1f), dark, 1.6f * s); }
        Leg(hip, hipX, _walk, false, true);
        Leg(chest, chestX, _walk + MathF.PI, false, false);

        // ---- head ----
        var head = P(headOff.X, headOff.Y) + new Vector2(wob, 0);
        float hr = H * (cat ? 0.3f : 0.33f) * headK;
        if (cat) EarsCat(r, head, hr, f, ink, ears);
        r.Disc(head, hr + 1.2f * s, ink);
        r.ShadedDisc(head, hr, col);
        if (cat && ears > 0.5f) EarsCatInner(r, head, hr, f, ears);
        if (!cat)
        {
            r.Oval(head + new Vector2(f * hr * 0.75f, hr * 0.25f), hr * 0.5f + 1 * s, hr * 0.38f + 1 * s, ink);
            r.Oval(head + new Vector2(f * hr * 0.75f, hr * 0.25f), hr * 0.5f, hr * 0.38f, col);
            float earLift = ears > 0.8f && (_st is State.Stalk or State.Ask or State.Walk) ? -hr * 0.25f : 0;
            r.Oval(head + new Vector2(-f * hr * 0.2f, hr * 0.15f + earLift), hr * 0.32f, hr * 0.65f, dark);
        }
        r.Disc(head + new Vector2(f * hr * (cat ? 0.85f : 1.2f), hr * (cat ? 0.15f : 0.2f)), 1.3f * s, ink);   // nose
        Vector2 eye = head + new Vector2(f * hr * 0.4f, -hr * 0.15f);
        if (eyesShut) r.Line(eye - new Vector2(f * hr * 0.15f, 0), eye + new Vector2(f * hr * 0.15f, 0), ink, 1 * s);
        else
        {
            if (eyesWide) r.Disc(eye, 2.2f * s, new Color4(1, 1, 1, 0.95f));
            r.Disc(eye, (eyesWide ? 1.5f : 1.3f) * s * (Young ? 1.3f : 1), ink);
            if (Young) r.Disc(eye + new Vector2(-0.5f * s, -0.5f * s), 0.5f * s, new Color4(1, 1, 1, 0.9f));
        }
        if (mouthOpen)
        {
            Vector2 m = head + new Vector2(f * hr * (cat ? 0.6f : 0.95f), hr * 0.5f);
            r.Oval(m, hr * 0.22f, hr * 0.16f, new Color4(0.35f, 0.08f, 0.1f, 0.95f));
            if (!cat && panting) r.Line(m, m + new Vector2(f * hr * 0.05f, hr * 0.45f + MathF.Sin(_tail * 14) * 0.6f * s), new Color4(0.92f, 0.45f, 0.55f, 1), 2.2f * s);
        }
        if (cat && Gfx.Q.DetailedArt)
        {
            var wc = new Color4(0.15f, 0.15f, 0.15f, 0.45f);
            Vector2 n = head + new Vector2(f * hr * 0.85f, hr * 0.2f);
            r.Line(n, n + new Vector2(f * hr * 0.6f, -hr * 0.12f), wc, 0.6f * s);
            r.Line(n, n + new Vector2(f * hr * 0.6f, hr * 0.12f), wc, 0.6f * s);
        }
        DrawOutfit(r, hip, chest, bodyThick, head, hr, f);
        if (Leashed || Owner != null) DrawCollar(r, head, hr, chest, bodyThick, ink, s);
        if (Wet > 0.2f && ((int)(_t * 4) % 3 == 0)) r.Disc(P(0, -H * 0.1f + (_t * 30 % 6) * s), 1 * s, new Color4(0.55f, 0.75f, 1, 0.8f * Wet));
    }

    /// <summary>The collar: a band round the neck, where the head meets the body, with a little tag hanging from it.</summary>
    void DrawCollar(Renderer r, Vector2 head, float hr, Vector2 chest, float bodyThick, Color4 ink, float s)
    {
        Vector2 d = chest - head;
        if (d.LengthSquared() < 1e-3f) d = new Vector2(0, 1);
        d = Vector2.Normalize(d);
        Vector2 across = new(-d.Y, d.X);
        Vector2 neck = head + d * hr * 0.88f;
        float half = MathF.Min(hr * 0.62f, bodyThick * 0.62f + 1 * s);
        var red = Lit(new Color4(0.86f, 0.2f, 0.2f, 1));
        r.Line(neck - across * half, neck + across * half, ink, 3.2f * s);
        r.Line(neck - across * (half - 0.6f * s), neck + across * (half - 0.6f * s), red, 2.0f * s);
        // The tag hangs straight down from the lower side of the band.
        Vector2 low = across.Y >= 0 ? neck + across * half * 0.35f : neck - across * half * 0.35f;
        Vector2 tag = low + new Vector2(0, 1.9f * s);
        r.Disc(tag, 1.35f * s, ink);
        r.Disc(tag, 0.95f * s, Lit(new Color4(0.95f, 0.78f, 0.25f, 1)));
    }

    static void EarsCat(Renderer r, Vector2 head, float hr, float f, Color4 ink, float up)
    {
        float tip = 1.55f * up + 0.7f * (1 - up), back = (1 - up) * 0.6f;
        r.FillPolygon(stackalloc Vector2[] { head + new Vector2(-f * hr * 0.75f, -hr * 0.4f), head + new Vector2(-f * hr * (0.55f + back), -hr * tip), head + new Vector2(-f * hr * 0.05f, -hr * 0.8f) }, ink);
        r.FillPolygon(stackalloc Vector2[] { head + new Vector2(f * hr * 0.05f, -hr * 0.8f), head + new Vector2(f * hr * (0.55f - back), -hr * tip), head + new Vector2(f * hr * 0.75f, -hr * 0.4f) }, ink);
    }

    static void EarsCatInner(Renderer r, Vector2 head, float hr, float f, float up)
    {
        var pink = new Color4(0.95f, 0.65f, 0.7f, 0.8f);
        r.FillPolygon(stackalloc Vector2[] { head + new Vector2(f * hr * 0.2f, -hr * 0.82f), head + new Vector2(f * hr * 0.5f, -hr * 1.3f * up), head + new Vector2(f * hr * 0.6f, -hr * 0.62f) }, pink);
    }

    void DrawParrot(Renderer r)
    {
        float s = S, f = Facing, H = Height;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.95f);
        var col = Lit(Color);
        var dark = Darken(col, 0.72f);
        var accent = Lit(Accent);
        float headK = 1 + (1 - Age) * 0.4f, g = Girth;
        bool fluff = _pose == Pose.Fluff || _st == State.Sleep;
        bool flying = Flying || (!Grounded && !Held && !OnCursor && _perchFig == null);
        Vector2 feet = Pos;
        Vector2 body = Pos + new Vector2(0, -H * 0.45f);
        float bw = H * 0.26f * g * (fluff ? 1.25f : 1), bh = H * 0.36f * (fluff ? 1.12f : 1);
        if (flying) body = Pos + new Vector2(0, -H * 0.5f);
        // Tail feathers (long, angled back and down).
        Vector2 tb = body + new Vector2(-f * bw * 0.4f, bh * 0.6f);
        Vector2 tt = tb + new Vector2(-f * H * (flying ? 0.55f : 0.25f), H * (flying ? 0.15f : 0.45f));
        r.Line(tb, tt, ink, 4.6f * s); r.Line(tb, tt, accent, 3.2f * s);
        r.Line(tb, tt + new Vector2(f * 2 * s, 1 * s), col, 1.6f * s);
        // Feet gripping.
        if (!flying)
        {
            r.Line(feet + new Vector2(-2 * s, -3 * s), feet + new Vector2(-2.5f * s, 0), ink, 1.3f * s);
            r.Line(feet + new Vector2(2 * s, -3 * s), feet + new Vector2(2.5f * s, 0), ink, 1.3f * s);
        }
        // Body.
        r.Oval(body, bw + 1.2f * s, bh + 1.2f * s, ink);
        r.Oval(body, bw, bh, col);
        r.Oval(body + new Vector2(f * bw * 0.25f, bh * 0.25f), bw * 0.55f, bh * 0.55f, Gfx.Lighter(col, 0.3f));
        if (fluff) for (int i = 0; i < 6; i++) { float a = i * 1.05f; r.Line(body + new Vector2(MathF.Cos(a) * bw, MathF.Sin(a) * bh), body + new Vector2(MathF.Cos(a) * bw * 1.18f, MathF.Sin(a) * bh * 1.15f), ink.A(0.6f), 0.8f * s); }
        // Wings: folded at the side, or beating.
        if (flying || Held)
        {
            float beat = MathF.Sin(_flap);
            Vector2 sh = body + new Vector2(0, -bh * 0.3f);
            Vector2 tip = sh + new Vector2(-f * H * 0.35f, -H * 0.45f * beat);
            r.FillPolygon(stackalloc Vector2[] { sh + new Vector2(f * 2 * s, 0), tip, sh + new Vector2(-f * H * 0.2f, H * 0.12f) }, dark);
            r.Line(tip, tip + (tip - sh) * 0.2f, accent, 2 * s);
        }
        else
        {
            r.Oval(body + new Vector2(-f * bw * 0.2f, 0), bw * 0.65f, bh * 0.85f, dark);
            r.Line(body + new Vector2(-f * bw * 0.3f, bh * 0.4f), body + new Vector2(-f * bw * 0.65f, bh * 0.95f), accent, 2.2f * s);
        }
        // Head.
        float hr = H * 0.2f * headK;
        Vector2 head = body + new Vector2(f * bw * 0.25f, -bh - hr * (fluff ? 0.35f : 0.75f));
        if (_st == State.Perch && _pose == Pose.Fluff) head += new Vector2(0, MathF.Sin(_tail * 8) * 1.5f * s);   // head-bobbing to the music
        r.Disc(head, hr + 1.2f * s, ink);
        r.ShadedDisc(head, hr, col);
        r.Disc(head + new Vector2(f * hr * 0.15f, hr * 0.35f), hr * 0.38f, accent);   // cheek patch
        // Hooked beak.
        Vector2 b0 = head + new Vector2(f * hr * 0.75f, -hr * 0.1f);
        r.FillPolygon(stackalloc Vector2[] { b0 + new Vector2(0, -hr * 0.35f), b0 + new Vector2(f * hr * 0.6f, hr * 0.05f), b0 + new Vector2(f * hr * 0.15f, hr * 0.55f), b0 + new Vector2(0, hr * 0.3f) }, new Color4(0.22f, 0.22f, 0.24f, 1));
        // Eye with its pale ring.
        Vector2 e = head + new Vector2(f * hr * 0.25f, -hr * 0.2f);
        if (_st == State.Sleep || _st == State.Petted) r.Line(e - new Vector2(hr * 0.2f, 0), e + new Vector2(hr * 0.2f, 0), ink, 1 * s);
        else { r.Disc(e, hr * 0.32f, new Color4(0.97f, 0.97f, 0.95f, 1)); r.Disc(e, hr * 0.17f, ink); }
        if (Wet > 0.2f) r.Disc(body + new Vector2(0, bh + (_t * 25 % 5) * s), 0.9f * s, new Color4(0.55f, 0.75f, 1, 0.8f * Wet));
    }

    void DrawScuffle(Renderer r, Vector2 c)
    {
        float s = MathF.Max(S, _scuffleWith!.S), t = _t;
        var grey = new Color4(0.86f, 0.86f, 0.84f, 0.95f);
        var ink = new Color4(0.15f, 0.15f, 0.15f, 0.9f);
        for (int i = 0; i < 7; i++)
        {
            float a = i * 0.9f + t * 3;
            var p = c + new Vector2(MathF.Cos(a) * 16 * s, MathF.Sin(a * 1.3f) * 9 * s);
            r.Disc(p, (11 + MathF.Sin(t * 7 + i) * 2) * s, ink);
        }
        for (int i = 0; i < 7; i++)
        {
            float a = i * 0.9f + t * 3;
            var p = c + new Vector2(MathF.Cos(a) * 16 * s, MathF.Sin(a * 1.3f) * 9 * s);
            r.Disc(p, (10 + MathF.Sin(t * 7 + i) * 2) * s, grey);
        }
        // Limbs, tails and stars poking out of the dust.
        foreach (var (pet, k) in new[] { (this, 0f), (_scuffleWith, 1.7f) })
        {
            float a = t * 5 + k;
            Vector2 o = c + new Vector2(MathF.Cos(a) * 26 * s, MathF.Sin(a) * 14 * s);
            r.Line(c + (o - c) * 0.6f, o, pet.Color, 3 * s);
            Vector2 tl = c + new Vector2(MathF.Cos(a + 2.5f) * 24 * s, MathF.Sin(a + 2.5f) * 14 * s);
            r.Line(c + (tl - c) * 0.6f, tl, pet.Color, 2.4f * s);
        }
        for (int i = 0; i < 3; i++)
        {
            var p = c + new Vector2(MathF.Cos(t * 4 + i * 2.1f) * 22 * s, -14 * s + MathF.Sin(t * 6 + i) * 4 * s);
            r.Text("✶", p, 8 * s, new Color4(1, 0.8f, 0.2f, 0.95f), true);
        }
    }

    /// <summary>What it's asking for (a thought bubble with a little picture), or what it's saying.</summary>
    void DrawBubbles(Renderer r)
    {
        float s = _s;
        Vector2 top = Kind == PetKind.Parrot ? Pos + new Vector2(0, -Height * 1.25f) : HeadPos + new Vector2(0, -Height * 0.45f);
        if (Bubble is { } said)
        {
            float w = MathF.Max(26, said.Length * 6.2f + 14) * s, h = 18 * s;
            var c = top + new Vector2(0, -h * 0.5f - 6 * s);
            Ui.Bubble(r, c, w, h, top, s, 0.95f);
            r.Text(said, c, 10 * s, Ui.Ink, true);
            return;
        }
        if (Want == PetNeed.None || _st != State.Ask) return;
        var bc = top + new Vector2(Facing * 14 * s, -28 * s);
        float pulse = 1 + MathF.Sin(_t * 4) * 0.05f, bw = 26 * s * pulse, bh = 22 * s * pulse;
        // The trail: a small puff by the head and a bigger one nearer the cloud, both in the gap between (never
        // touching the cloud's outline), drawn first so the cloud sits over them.
        var d = Vector2.Normalize(bc - top);
        float edge = 1 / MathF.Sqrt(d.X * d.X / (bw * bw / 4) + d.Y * d.Y / (bh * bh / 4));
        float gap = Vector2.Distance(top, bc) - edge;
        foreach (var (k, pr) in new[] { (0.22f, 1.6f), (0.64f, 2.5f) })
        {
            var pc = top + d * gap * k;
            r.Disc(pc, pr * s, Ui.Fill); r.Ring(pc, pr * s, Ui.Ink.A(0.75f), 0.8f * s);
        }
        Ui.Card(r, bc, bw, bh, 11 * s, s, 0.95f);
        DrawNeedIcon(r, bc, Want, s);
    }

    /// <summary>Tiny pictures: a bowl of kibble, a water drop, a litter box, a leash, a heart, a ball, z.</summary>
    static void DrawNeedIcon(Renderer r, Vector2 c, PetNeed need, float s)
    {
        var ink = Ui.Ink;
        switch (need)
        {
            case PetNeed.Food:
                r.FillPolygon(stackalloc Vector2[] { c + new Vector2(-7 * s, -1 * s), c + new Vector2(7 * s, -1 * s), c + new Vector2(5 * s, 5 * s), c + new Vector2(-5 * s, 5 * s) }, new Color4(0.9f, 0.3f, 0.3f, 1));
                for (int i = 0; i < 4; i++) r.Disc(c + new Vector2(-4.5f * s + i * 3 * s, -2.5f * s - (i % 2) * 1.5f * s), 1.4f * s, new Color4(0.6f, 0.4f, 0.2f, 1));
                break;
            case PetNeed.Water:
                r.FillPolygon(stackalloc Vector2[] { c + new Vector2(0, -7 * s), c + new Vector2(4.5f * s, 1 * s), c + new Vector2(0, 5 * s), c + new Vector2(-4.5f * s, 1 * s) }, new Color4(0.35f, 0.65f, 1, 1));
                r.Disc(c + new Vector2(0, 1.5f * s), 4.2f * s, new Color4(0.35f, 0.65f, 1, 1));
                break;
            case PetNeed.Potty:
                r.FillPolygon(stackalloc Vector2[] { c + new Vector2(-7 * s, -2 * s), c + new Vector2(7 * s, -2 * s), c + new Vector2(6 * s, 5 * s), c + new Vector2(-6 * s, 5 * s) }, new Color4(0.55f, 0.6f, 0.65f, 1));
                for (int i = 0; i < 3; i++) r.Line(c + new Vector2(-4 * s + i * 4 * s, -6 * s), c + new Vector2(-3 * s + i * 4 * s, -9 * s), ink.A(0.7f), 1 * s);
                break;
            case PetNeed.Walk:
                r.Ring(c + new Vector2(-3 * s, 2 * s), 3.5f * s, new Color4(0.85f, 0.2f, 0.2f, 1), 1.8f * s);
                r.Line(c + new Vector2(0, 0), c + new Vector2(6 * s, -6 * s), new Color4(0.85f, 0.2f, 0.2f, 1), 1.8f * s);
                break;
            case PetNeed.Attention:
                r.Text("♥", c, 13 * s, new Color4(0.9f, 0.3f, 0.45f, 1), true);
                break;
            case PetNeed.Play:
                r.Disc(c, 5.5f * s, new Color4(0.95f, 0.75f, 0.2f, 1));
                r.Line(c + new Vector2(-5 * s, 0), c + new Vector2(5 * s, 0), ink.A(0.5f), 0.8f * s);
                break;
            default:
                r.Text("z", c, 11 * s, ink, true);
                break;
        }
    }
}
