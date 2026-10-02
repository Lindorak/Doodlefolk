using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>The turnaround swing, squash and stretch, drop shadows onto the window behind and motion smears. (Plain
/// heads, no faces: classic stick figures.)</summary>
sealed partial class Figure
{
    float _turnT = 1, _squash;
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
}
