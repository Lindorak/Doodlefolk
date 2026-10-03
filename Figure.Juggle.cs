using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Juggling with their hands, face on to you: a cascade of beanbags (three; a fountain of four or a cascade of
/// five once they're good). Each hand throws from the inside and catches on the outside, scooping down in between;
/// every catch can fumble (that's the brain's call, by skill), and a fumbled one drops to their feet to be picked up.
/// Keepy-uppies with a ball are a different thing (Brain.Ball).</summary>
sealed partial class Figure
{
    /// <summary>How many they're juggling (0: not juggling).</summary>
    public int Juggling;
    /// <summary>Catches so far, ever (the brain counts the difference).</summary>
    public int JugCaught;
    /// <summary>Asked at every catch: false fumbles it.</summary>
    public Func<bool>? JugCatch;
    /// <summary>A beanbag that got away: falling, then lying at their feet till they pick it up.</summary>
    public Vector2? JugDropped;
    public bool JugDropDown;
    Vector2 _jugDropVel;
    double _jugBeat;
    int _jugNextThrow;
    const float Dwell = 1.2f;   // of each hand's two-throw cycle, how long it holds the bag

    /// <summary>Throws a second: brisk for three, a touch quicker with more.</summary>
    float JugRate => 2.9f + (Juggling - 3) * 0.3f;   // higher throws take longer to come down
    /// <summary>How far above the hands a throw goes: up to about twice their own height off the ground (higher with more).</summary>
    float JugApex => Height * (1.2f + 0.12f * (Juggling - 3));

    public void StartJuggling(int n)
    {
        Juggling = Math.Clamp(n, 3, 5);
        _jugBeat = Juggling;
        _jugNextThrow = 0;
        JugDropped = null;
        JugDropDown = false;
        SetAction(Act.Juggle);
    }

    public void StopJuggling()
    {
        Juggling = 0;
        if (Action == Act.Juggle) SetAction(Act.Stand);
    }

    /// <summary>Leaving the activity releases its bags and catch callback, including an interrupted pickup.</summary>
    public void CancelJuggling()
    {
        StopJuggling();
        JugDropped = null;
        JugDropDown = false;
        JugCatch = null;
        _jugDropVel = Vector2.Zero;
    }

    // Hand points, in facing space from the neck (x across the body face on, y down): catch outside, throw inside.
    Vector2 JugCatchPt(int h) => new((h == 0 ? 1 : -1) * Arm * 0.78f, Arm * 0.5f);
    Vector2 JugThrowPt(int h) => new((h == 0 ? 1 : -1) * Arm * 0.3f, Arm * 0.6f);

    Vector2 JugHand(int h, double t)
    {
        double u = (((t - h) % 2) + 2) % 2 / 2;   // 0 at this hand's throw
        float empty = (2 - Dwell) / 2;
        Vector2 a = JugThrowPt(h), c = JugCatchPt(h);
        if (u < empty)
        {
            float s = (float)(u / empty);   // up and out to meet the next one
            var p = Vector2.Lerp(a, c, M.Smooth(s));
            p.Y -= MathF.Sin(MathF.PI * s) * Arm * 0.1f;
            return p;
        }
        float k = (float)((u - empty) / (1 - empty));   // scoop down and in, and throw
        var q = Vector2.Lerp(c, a, k);
        q.Y += MathF.Sin(MathF.PI * k) * Arm * 0.16f;
        return q;
    }

    Vector2 JugWorld(Vector2 local) => Jt[J.Neck] + new Vector2(local.X * Facing, local.Y);

    /// <summary>Where beanbag i is (world), whether it's in the air, and which hand holds it if not.</summary>
    Vector2 JugBall(int i, double t, out bool air, out int hand)
    {
        int n = Juggling;
        double b = Math.Floor((t - i) / n) * n + i;   // its last throw
        double since = t - b;
        int from = (int)(((long)b % 2 + 2) % 2);
        int to = n % 2 == 1 ? 1 - from : from;   // a cascade crosses; a fountain stays on its side
        double flight = n - Dwell;
        if (since < flight)
        {
            air = true; hand = -1;
            float s = (float)(since / flight);
            var p = Vector2.Lerp(JugThrowPt(from), JugCatchPt(to), s);
            // Every throw a little different: roughly the same height, never exactly.
            uint hb = (uint)((long)b * 2654435761L + Id * 97);
            float vary = 0.88f + 0.24f * ((hb >> 8) & 1023) / 1023f;
            p.Y -= 4 * JugApex * vary * s * (1 - s);
            return JugWorld(p);
        }
        air = false; hand = to;
        return Jt[to == 0 ? J.HandN : J.HandF] + new Vector2(0, -2.1f * S);
    }

    void StepJuggle(float dt)
    {
        if (JugDropped is { } d && !JugDropDown)
        {
            _jugDropVel.Y += Gravity * dt;
            d += _jugDropVel * dt;
            float floor = Base.Y - 2.2f * S;
            if (d.Y >= floor)
            {
                d.Y = floor;
                if (_jugDropVel.Y > 260 * S) { _jugDropVel = new Vector2(_jugDropVel.X * 0.5f, -_jugDropVel.Y * 0.25f); World.Play(Sfx.Thud, d, 0.12f, 1.8f); }
                else JugDropDown = true;
            }
            JugDropped = d;
        }
        if (Juggling == 0) return;
        if (Action != Act.Juggle || Mode != Mode.Control || !Grounded) { StopJuggling(); return; }   // knocked out of it
        _jugBeat += dt * JugRate;
        // Every throw is caught (or not) Dwell beats before that hand throws again.
        while (_jugNextThrow + Juggling - Dwell <= _jugBeat - Juggling)
        {
            int b = _jugNextThrow++ + Juggling;   // beats are counted from the start of the pattern
            if (JugCatch?.Invoke() ?? true) { JugCaught++; continue; }
            // Fumbled: it carries on down past the hand, and the rest are caught and held.
            int i = b % Juggling;
            var at = JugBall(i, b + Juggling - Dwell - 0.05, out _, out _);
            JugDropped = at;
            JugDropDown = false;
            _jugDropVel = new Vector2((i % 2 == 0 ? 1 : -1) * 70 * S, 180 * S);
            StopJuggling();
            return;
        }
    }

    static readonly Color4[] BeanbagCols = { M.Hex(0xE53935), M.Hex(0xFDD835), M.Hex(0x1E88E5), M.Hex(0x43A047), M.Hex(0x8E24AA) };

    void Beanbag(Renderer r, Vector2 at, int i, float fade)
    {
        var c = BeanbagCols[(i + Id) % BeanbagCols.Length];
        float rr = 2.4f * S;
        r.Disc(at, rr + 0.7f * S, new Color4(0.12f, 0.11f, 0.1f, 0.9f * fade));
        r.Disc(at, rr, new Color4(c.R, c.G, c.B, fade));
        r.Line(at + new Vector2(-rr * 0.6f, -rr * 0.2f), at + new Vector2(rr * 0.6f, rr * 0.2f), new Color4(1, 1, 1, 0.45f * fade), 0.5f * S);
    }

    void DrawJuggle(Renderer r, float fade)
    {
        if (Juggling > 0)
            for (int i = 0; i < Juggling; i++)
            {
                var at = JugBall(i, _jugBeat, out bool air, out _);
                Beanbag(r, at, i, fade);
            }
        if (JugDropped is { } d) Beanbag(r, d, 0, fade);
    }

    /// <summary>Arms for juggling: each hand on its loop, quick and light.</summary>
    void JuggleArms(ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref Vector2 eF, ref float handW)
    {
        if (Juggling == 0 || Action != Act.Juggle) return;
        hN = JugHand(0, _jugBeat); hF = JugHand(1, _jugBeat);
        eN = new(0.8f, 1); eF = new(-0.8f, 1);
        handW = 70;
    }
}
