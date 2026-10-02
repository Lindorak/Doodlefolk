using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

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
    float _babbleUntil;

    public void Emote(string text, float dur = 1.4f, Color4? ink = null)
    {
        if (World.Gestures)
        {
            var (symbol, g) = ToGesture(text);
            if (symbol != text) { text = symbol; StartGesture(g, dur); }
        }
        bool fresh = _emote?.Text != text;
        _emote = new Emote { Text = text, Dur = dur, Ink = ink };
        if (!fresh || Mode == Mode.Spawning) return;
        // A little voice for each kind of reaction (pitched by size: big figures sound deeper).
        float pitch = 1.1f / MathF.Sqrt(SizeMul) * (0.9f + (Id % 5) * 0.05f);
        Vector2 at = Jt[J.Head];
        switch (text)
        {
            case "!": case "!!": World.Play(Sfx.Pip, at, 0.35f, pitch * (text == "!!" ? 1.3f : 1), 0.12); break;
            case "?": World.Play(Sfx.Pip, at, 0.25f, pitch * 0.75f, 0.12); break;
            case "♥": World.Play(Sfx.Chime, at, 0.3f, pitch, 0.2); break;
            case "#@!": World.Play(Sfx.Grumble, at, 0.4f, pitch, 0.2); break;
            case "ha": World.Play(Sfx.Laugh, at, 0.35f, pitch, 0.2); break;
            case "♪": case "♫": World.Play(Sfx.Tune, at, 0.25f, pitch, 0.3); break;
            case "z": World.Play(Sfx.Snore, at, 0.25f, pitch * 0.8f, 2.5); break;
            default:
                if (text.Length >= 2 && text.Any(char.IsLetter) && _time > _babbleUntil)
                {
                    // Their own voice: higher for girls, lower for boys and big figures; quick when energetic, soft when shy.
                    float vp = (Gender switch { Gender.Girl => 1.35f, Gender.Boy => 0.88f, _ => 1.1f }) / MathF.Sqrt(SizeMul) * (0.92f + (Id % 7) * 0.025f);
                    float speed = 0.85f + Traits.Energy * 0.35f;
                    float vol = 0.3f + Traits.Sociability * 0.15f;
                    World.Babble(text, at, vp, vol, speed);
                    _babbleUntil = _time + MathF.Min(2.2f, 0.12f * text.Length / speed);
                }
                break;
        }
    }
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
        TickFace(dt);
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
                r.Text("z", p + new Vector2(0.5f, 0.6f) * S, size, Ui.Fill.A(a * 0.7f), true);
                r.Text("z", p, size, Ui.Ink.A(a), true);
            }
            return;
        }
        float pop = M.Smooth(e.T / 0.15f);
        float s = S * (0.6f + 0.4f * pop);
        Vector2 c = head + new Vector2(Facing * 5 * S, -(HeadR + 10 * S));
        float w = MathF.Max(13, 7 + e.Text.Length * 5.4f) * s, h = 12 * s;
        var ink = (e.Ink ?? Ui.Ink).A(fade);
        Ui.Bubble(r, c, w, h, c + new Vector2(-4 * s * Facing, h / 2 + 5 * s), s, fade, line: 1.1f);
        r.Text(e.Text, c + new Vector2(0, -0.3f * s), 9 * s, ink, true);
    }
}
