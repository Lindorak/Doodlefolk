using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Extra moves: kicks, juggling taps, carrying and throwing props, backflips,
/// getting hit, and emote bubbles.</summary>
sealed partial class Figure
{
    public const float KickTime = 0.42f, KickContact = 0.5f;    // contact as a fraction of KickTime
    public const float TapTime = 0.3f, TapContact = 0.4f;
    public const float ThrowTime = 0.55f, ThrowRelease = 0.55f;

    /// <summary>The prop this figure is holding, if any.</summary>
    public Prop? Carrying;
    /// <summary>Where a held prop sits (world space), updated every pose.</summary>
    public Vector2 HoldPoint { get; private set; }
    public Vector2 HoldVelocity { get; private set; }

    float _flipT = -1, _flipDur;
    public bool Flipping => _flipT >= 0;

    Emote? _emote;
    public void Emote(string text, float dur = 1.4f, Color4? ink = null) =>
        _emote = new Emote { Text = text, Dur = dur, Ink = ink ?? new Color4(0.12f, 0.12f, 0.14f, 1) };
    public string? CurrentEmote => _emote?.Text;

    public void RequestFlip(float height)
    {
        float vy = -MathF.Sqrt(2 * Gravity * height);
        RequestJump(new Vector2(Vel.X * 0.3f, vy), 0.16f);
        _flipDur = 2 * -vy / Gravity * 0.8f;
        _flipT = 0;
    }

    /// <summary>Hit by something moving at <paramref name="v"/> (relative). Big hits knock the figure over.</summary>
    public void TakeHit(Vector2 v, Figure? from, World w)
    {
        if (Mode is Mode.Spawning) return;
        float sp = v.Length();
        if (Mode == Mode.Ragdoll) return;
        if (sp > 1300 * S || (Climbing && sp > 500 * S) || (!Grounded && sp > 700 * S))
        {
            World.Log($"{Name} knocked down by hit {sp / S:F0}S/s from {from?.Name ?? "user/none"} (climbing={Climbing}, grounded={Grounded})");
            GoRagdoll(v * 0.55f);
            Brain.OnHit(from, true, w);
        }
        else
        {
            Vel.X += v.X * 0.25f;
            _hipV -= 8 * S;
            _leanV += -MathF.Sign(v.X) * Facing * 3;
            Brain.OnHit(from, false, w);
        }
    }

    void TickMoves(float dt)
    {
        _landPoseT = MathF.Max(0, _landPoseT - dt);
        if (_emote is Emote e)
        {
            e.T += dt;
            _emote = e.T >= e.Dur ? null : e;
        }
        if (Flipping)
        {
            if (Grounded && !JumpPending) _flipT = -1;
            else if (!JumpPending) _flipT += dt;
        }
    }

    /// <summary>Kick (or juggle tap) curve for the near foot, relative to the pelvis in facing space.</summary>
    Vector2 KickFootLocal(Vector2 planted)
    {
        if (Action == Act.Tap)
        {
            float t = ActionT / TapTime;
            Vector2 up = new(Leg * 0.5f, Leg * 0.45f);
            return t < TapContact ? Vector2.Lerp(planted, up, M.Smooth(t / TapContact)) : Vector2.Lerp(up, planted, M.Smooth((t - TapContact) / (1 - TapContact)));
        }
        float k = ActionT / KickTime;
        Vector2 back = new(-Leg * 0.5f, Leg * 0.72f), strike = new(Leg * 0.8f, Leg * 0.5f);
        if (k < 0.35f) return Vector2.Lerp(planted, back, M.Smooth(k / 0.35f));
        if (k < 0.58f) return Vector2.Lerp(back, strike, M.Smooth((k - 0.35f) / 0.23f));
        return Vector2.Lerp(strike, planted, M.Smooth((k - 0.58f) / 0.42f));
    }

    float CarryRadius => Carrying?.Radius ?? (CarryingItem != null ? MathF.Max(CarryingItem.Def.W, CarryingItem.Def.H) * CarryingItem.Sc * 0.4f : 4 * S);
    Vector2 HoldLocal => new(MathF.Min(CarryRadius + 4 * S, Arm * 0.75f), Torso * 0.38f);

    /// <summary>Overrides arm targets while holding something (except mid-throw).</summary>
    void CarryArms(ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF, ref float handW)
    {
        if ((Carrying == null && CarryingItem == null) || Action is Act.Throw or Act.Eat or Act.Read) return;
        float r = CarryRadius;
        hN = HoldLocal + new Vector2(-0.2f * r, -0.75f * r);
        hF = HoldLocal + new Vector2(-0.2f * r, 0.75f * r);
        eN = eF = new(-1, 0.6f);
        handW = 24;
    }

    void UpdateHoldPoint(Vector2 neck, Vector2 handN, Vector2 handF, float dt)
    {
        Vector2 prev = HoldPoint;
        HoldPoint = Action == Act.Throw ? (handN + handF) * 0.5f + new Vector2(0, -CarryRadius * 0.3f)
                  : Action == Act.Eat ? handN
                  : Action == Act.Read ? (handN + handF) * 0.5f + new Vector2(0, -2 * S)
                  : neck + W(HoldLocal);
        HoldVelocity = dt > 0 ? (HoldPoint - prev) / dt : Vector2.Zero;
    }

    void ApplyFlip()
    {
        if (!Flipping || Grounded) return;
        float ang = -Facing * MathF.Tau * M.Smooth(_flipT / _flipDur);
        Vector2 c = Jt[J.Pelvis];
        for (int i = 0; i < J.Count; i++) Jt[i] = c + M.Rotate(Jt[i] - c, ang);
    }

    void DrawEmote(Renderer r)
    {
        if (_emote is not Emote e || Mode == Mode.Spawning) return;
        float fade = M.Clamp01((e.Dur - e.T) / 0.25f);
        Vector2 head = Jt[J.Head];
        if (e.Text == "z")
        {
            for (int i = 0; i < 3; i++)
            {
                float t = (e.T * 0.6f + i / 3f) % 1;
                Vector2 p = head + new Vector2(Facing * (4 + t * 10) * S, -(HeadR + 4 * S + t * 18 * S));
                float size = (5 + t * 5) * S, a = (1 - t) * fade;
                r.Text("z", p + new Vector2(0.5f, 0.6f) * S, size, new Color4(0, 0, 0, a * 0.6f));
                r.Text("z", p, size, new Color4(0.92f, 0.92f, 1, a));
            }
            return;
        }
        float pop = M.Smooth(e.T / 0.15f);
        float s = S * (0.6f + 0.4f * pop);
        Vector2 c = head + new Vector2(Facing * 5 * S, -(HeadR + 10 * S));
        float w = MathF.Max(12, 4 + e.Text.Length * 6.5f) * s, h = 11 * s;
        var ink = new Color4(e.Ink.R, e.Ink.G, e.Ink.B, e.Ink.A * fade);
        r.RoundRect(c, w, h, 4 * s, new Color4(1, 1, 1, 0.95f * fade), new Color4(0.1f, 0.1f, 0.1f, 0.8f * fade), 1.1f * s);
        r.FillPolygon(stackalloc Vector2[] { c + new Vector2(-2 * s * Facing, h / 2 - 0.5f), c + new Vector2(2 * s * Facing, h / 2 - 0.5f), c + new Vector2(-3.5f * s * Facing, h / 2 + 4 * s) },
                      new Color4(1, 1, 1, 0.95f * fade));
        r.Text(e.Text, c + new Vector2(0, -0.4f * s), 8.5f * s, ink);
    }
}
