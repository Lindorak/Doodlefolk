using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Faces (eyes that look and blink, brows and a mouth that follow the mood), the turnaround swing, squash and
/// stretch, drop shadows onto the window behind and motion smears.</summary>
sealed partial class Figure
{
    float _blinkAt = 2, _turnT = 1, _squash;
    int _lastFacing = 1;
    const float TurnDur = 0.14f;
    /// <summary>-1..1 while turning round (the body swings through edge-on), 1 otherwise.</summary>
    public float TurnScale { get; private set; } = 1;

    /// <summary>Landing hard squashes the body for a moment.</summary>
    public void Squash(float impact) { _squash = MathF.Max(_squash, M.Clamp01(impact / (2200 * S)) * 0.14f); Jiggle(impact); }

    /// <summary>How much longer (+) or shorter (-) the torso is right now: stretched rising fast, squashed after a landing.</summary>
    float StretchNow() => (!Grounded && Mode == Mode.Control ? M.Clamp01(-Vel.Y / (1800 * S)) * 0.07f : 0) - _squash;

    void TickFace(float dt)
    {
        _squash = MathF.Max(0, _squash - dt * 0.9f);
        TickBody(dt);
        if (_time > _blinkAt + 0.13f) _blinkAt = _time + _rng.Range(1.8f, 5.5f);
    }

    /// <summary>After posing: swing through edge-on when the figure turns round, instead of flipping instantly.</summary>
    void TurnBlend(float dt)
    {
        if (Facing != _lastFacing)
        {
            if (Grounded && Mode == Mode.Control && !FrontView && !Climbing) _turnT = 0;
            _lastFacing = Facing;
        }
        if (_turnT >= TurnDur) { TurnScale = 1; return; }
        _turnT += dt;
        float s = -1 + 2 * M.Smooth(M.Clamp01(_turnT / TurnDur));
        if (MathF.Abs(s) < 0.12f) s = s < 0 ? -0.12f : 0.12f;
        TurnScale = s;
        float px = Jt[J.Pelvis].X;
        for (int i = 0; i < J.Count; i++) Jt[i].X = px + (Jt[i].X - px) * s;
    }

    /// <summary>A faint silhouette on the window behind (drop shadows).</summary>
    public void DrawDropShadow(Renderer r)
    {
        if (!Gfx.Q.DropShadows || Mode == Mode.Spawning) return;
        Vector2 o = Gfx.DropOffset * S;
        var c = new Color4(0, 0, 0, Fade);
        float w = LineW + 1.4f * S;
        foreach (var (a, b) in Bones) r.Line(Jt[a] + o, Jt[b] + o, c, w);
        r.Disc(Jt[J.Head] + o, HeadR + 0.8f * S, c);
    }

    /// <summary>Cartoon smears behind fast-moving hands and feet.</summary>
    void DrawTrails(Renderer r, Color4 col)
    {
        if (!Gfx.Q.Trails || Mode != Mode.Control) return;
        void Smear(int a, int b)
        {
            float sp = JVel[b].Length();
            if (sp < 700 * S) return;
            float k = M.Clamp01((sp - 700 * S) / (1400 * S));
            Vector2 pa = Jt[a] - JVel[a] * 0.035f, pb = Jt[b] - JVel[b] * 0.045f;
            r.FillPolygon(stackalloc Vector2[] { Jt[a], Jt[b], pb, pa }, col.A(0.22f * k));
        }
        Smear(J.ElbowN, J.HandN); Smear(J.ElbowF, J.HandF); Smear(J.KneeN, J.FootN); Smear(J.KneeF, J.FootF);
    }

    /// <summary>Eyes (and a mouth): looking where it looks, blinking, and showing how it feels.</summary>
    void DrawFace(Renderer r, float fade)
    {
        int q = Gfx.Q.Faces;
        if (q == 0 || (FrontView && Action == Act.SitBack)) return;
        Vector2 h = Jt[J.Head];
        float R = HeadR, s = MathF.Abs(TurnScale);
        int face = TurnScale < 0 ? -Facing : Facing;
        bool front = FrontView;
        var ink = Gfx.Luma(Color) < 0.35f ? new Color4(0.96f, 0.96f, 0.96f, 0.92f * fade) : new Color4(0.07f, 0.07f, 0.09f, 0.9f * fade);
        float lw = MathF.Max(0.9f, R * 0.14f);
        Vector2 E(float x, float y) => h + new Vector2((front ? x : face * x) * (front ? 1 : s), y);

        bool ko = Dead || KO;
        bool shut = Brain.Asleep || (_time > _blinkAt && _time < _blinkAt + 0.12f) || (Mode == Mode.Ragdoll && Held == false && Rag.Contact);
        bool surprised = CurrentEmote is "!" or "!!" or "aaah!" or "eek!" || Mood.Scared > 0.6f;
        bool happy = Mood.Happy > 0.55f && Mood.Scared < 0.3f && Mood.Angry < 0.3f;
        // Pupils drift toward whatever it's looking at.
        Vector2 look = Vector2.Zero;
        if (LookAt is Vector2 la && (la - h).LengthSquared() > 1) look = Vector2.Normalize(la - h) * R * 0.08f;

        Vector2 e1 = front ? E(-R * 0.34f, -R * 0.1f) : E(R * 0.16f, -R * 0.1f), e2 = front ? E(R * 0.34f, -R * 0.1f) : E(R * 0.58f, -R * 0.12f);
        foreach (var e in new[] { e1, e2 })
        {
            if (ko)
            {
                float d = R * 0.12f;
                r.Line(e + new Vector2(-d, -d), e + new Vector2(d, d), ink, lw); r.Line(e + new Vector2(-d, d), e + new Vector2(d, -d), ink, lw);
            }
            else if (shut) r.Line(e + new Vector2(-R * 0.12f, 0), e + new Vector2(R * 0.12f, 0), ink, lw);
            else if (happy) { r.Line(e + new Vector2(-R * 0.12f, R * 0.05f), e + new Vector2(0, -R * 0.08f), ink, lw); r.Line(e + new Vector2(0, -R * 0.08f), e + new Vector2(R * 0.12f, R * 0.05f), ink, lw); }
            else
            {
                float er = R * (surprised ? 0.17f : 0.12f);
                if (surprised) r.Disc(e, er + R * 0.06f, ink.A(0.35f));
                r.Disc(e + look, er, ink);
                if (Mood.Tired > 0.5f) r.Line(e + new Vector2(-R * 0.15f, -R * 0.06f), e + new Vector2(R * 0.15f, -R * 0.06f), Color.A(fade), lw * 1.6f);   // heavy lids
            }
            // Brows: angry slope in, sad tilt up in the middle.
            if (!ko && (Mood.Angry > 0.45f || Mood.Sad > 0.5f))
            {
                float inner = (front ? (e.X < h.X ? 1 : -1) : -face) * R * 0.14f;
                float tilt = Mood.Angry > 0.45f ? R * 0.1f : -R * 0.09f;
                r.Line(e + new Vector2(-inner, -R * 0.3f), e + new Vector2(inner, -R * 0.3f + tilt), ink, lw * 0.9f);
            }
        }
        if (q < 2 || ko && !Dead) return;
        // Mouth.
        Vector2 m = front ? h + new Vector2(0, R * 0.42f) : h + new Vector2(face * R * 0.42f * s, R * 0.42f);
        float mw = R * 0.22f;
        if (Action == Act.Talk && ((int)(_time * 8) % 2 == 0)) r.Oval(m, mw * 0.6f, R * 0.13f, ink);
        else if (surprised) r.Ring(m, R * 0.1f, ink, lw * 0.8f);
        else if (Mood.Happy > 0.3f || happy)
        {
            r.Line(m + new Vector2(-mw, -R * 0.05f), m + new Vector2(0, R * 0.06f), ink, lw * 0.85f);
            r.Line(m + new Vector2(0, R * 0.06f), m + new Vector2(mw, -R * 0.05f), ink, lw * 0.85f);
        }
        else if (Mood.Sad > 0.4f || Mood.Hurt > 0.5f || Dead)
        {
            r.Line(m + new Vector2(-mw, R * 0.05f), m + new Vector2(0, -R * 0.04f), ink, lw * 0.85f);
            r.Line(m + new Vector2(0, -R * 0.04f), m + new Vector2(mw, R * 0.05f), ink, lw * 0.85f);
        }
        else r.Line(m + new Vector2(-mw * 0.8f, 0), m + new Vector2(mw * 0.8f, 0), ink, lw * 0.8f);
    }
}
