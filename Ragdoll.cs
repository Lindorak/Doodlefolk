using System.Numerics;

namespace StickFight;

/// <summary>Verlet ragdoll over the figure's 11 joints. Collides with platforms (one-way window edges,
/// solid floors) and screen edges, and rides along with windows it's lying on.</summary>
sealed class Ragdoll
{
    readonly Figure f;
    public readonly Vector2[] P = new Vector2[J.Count], O = new Vector2[J.Count];
    readonly float[] _inv = new float[J.Count], _r = new float[J.Count];
    readonly bool[] _touch = new bool[J.Count];
    readonly IntPtr[] _touchHwnd = new IntPtr[J.Count];
    readonly float[] _touchY = new float[J.Count];
    readonly (int a, int b, float len, bool min)[] _cons;
    public bool Contact;
    public int Pin = -1;
    public Vector2 PinTarget;
    float _struggleT;

    public Ragdoll(Figure f)
    {
        this.f = f;
        float[] mass = { 1.0f, 1.2f, 1.6f, 0.6f, 0.4f, 0.6f, 0.4f, 0.8f, 0.6f, 0.8f, 0.6f };
        for (int i = 0; i < J.Count; i++) { _inv[i] = 1 / mass[i]; _r[i] = f.LineW * 0.5f; }
        _r[J.Head] = f.HeadR;
        float hn = f.HeadR + f.NeckGap;
        _cons = new[]
        {
            (J.Head, J.Neck, hn, false), (J.Neck, J.Pelvis, f.Torso, false),
            (J.Head, J.Pelvis, (hn + f.Torso) * 0.93f, true),
            (J.Neck, J.ElbowN, f.UpperArm, false), (J.ElbowN, J.HandN, f.ForeArm, false),
            (J.Neck, J.ElbowF, f.UpperArm, false), (J.ElbowF, J.HandF, f.ForeArm, false),
            (J.Pelvis, J.KneeN, f.Thigh, false), (J.KneeN, J.FootN, f.Shin, false),
            (J.Pelvis, J.KneeF, f.Thigh, false), (J.KneeF, J.FootF, f.Shin, false),
            (J.Pelvis, J.FootN, f.Leg * 0.4f, true), (J.Pelvis, J.FootF, f.Leg * 0.4f, true),
            (J.Neck, J.HandN, f.Arm * 0.3f, true), (J.Neck, J.HandF, f.Arm * 0.3f, true),
            (J.Neck, J.KneeN, f.Torso * 0.55f, true), (J.Neck, J.KneeF, f.Torso * 0.55f, true),
        };
    }

    public void Init(Vector2[] pos, Vector2[] vel, Vector2 extra)
    {
        float max = 6000 * f.S;
        for (int i = 0; i < J.Count; i++)
        {
            P[i] = pos[i];
            O[i] = pos[i] - M.ClampLength(vel[i] + extra, max) * World.Dt;
            _touch[i] = false;
        }
        Pin = -1;
        Contact = false;
    }

    public void Release(Vector2 vel)
    {
        int pin = Pin;
        Pin = -1;
        O[pin] = P[pin] - M.ClampLength(vel, 4500 * f.S) * World.Dt;
    }

    /// <summary>Add velocity to one joint (e.g. hit by a ball).</summary>
    public void Push(int joint, Vector2 dv)
    {
        if (joint != Pin) O[joint] -= dv * World.Dt;
    }

    public void PushAll(Vector2 dv)
    {
        for (int i = 0; i < J.Count; i++) Push(i, dv);
    }

    public float MaxSpeed(float dt)
    {
        float m = 0;
        for (int i = 0; i < J.Count; i++) m = MathF.Max(m, (P[i] - O[i]).Length());
        return m / dt;
    }

    public void ApplyCarry(Env env)
    {
        for (int i = 0; i < J.Count; i++)
        {
            if (!_touch[i] || _touchHwnd[i] == IntPtr.Zero) continue;
            Vector2 d = env.Delta(_touchHwnd[i]);
            P[i] += d;
            O[i] += d;
        }
    }

    public Platform? GroundPlatform(Env env)
    {
        int[] order = { J.Pelvis, J.FootN, J.FootF, J.KneeN, J.KneeF, J.Neck, J.Head, J.HandN, J.HandF, J.ElbowN, J.ElbowF };
        foreach (int i in order)
            if (_touch[i] && env.SupportAt(P[i].X, _touchY[i], _touchHwnd[i]) is { } p) return p;
        return null;
    }

    public void Step(float dt, World w)
    {
        if (Pin >= 0) Struggle(dt, w.Rng);
        float g = f.Gravity * dt * dt;
        for (int i = 0; i < J.Count; i++)
        {
            if (i == Pin) { O[i] = P[i]; P[i] = PinTarget; continue; }
            Vector2 v = (P[i] - O[i]) * 0.997f;
            O[i] = P[i];
            P[i] += v + new Vector2(0, g);
        }
        Collide(w.Env, true);
        for (int it = 0; it < 10; it++)
        {
            foreach (var c in _cons) Solve(c.a, c.b, c.len, c.min);
            if (Pin >= 0) P[Pin] = PinTarget;
            Collide(w.Env, false);
        }
    }

    void Solve(int a, int b, float len, bool min)
    {
        Vector2 d = P[b] - P[a];
        float l = d.Length();
        if (l < 1e-4f || (min && l >= len)) return;
        float wa = a == Pin ? 0 : _inv[a], wb = b == Pin ? 0 : _inv[b];
        float sum = wa + wb;
        if (sum <= 0) return;
        Vector2 corr = d * ((l - len) / (l * sum));
        P[a] += corr * wa;
        P[b] -= corr * wb;
    }

    void Collide(Env env, bool first)
    {
        if (first) Contact = false;
        for (int i = 0; i < J.Count; i++)
        {
            if (i == Pin) continue;
            float r = _r[i];
            Vector2 v = P[i] - O[i];
            var (L, R, T) = env.BoundsAt(P[i].X);
            if (P[i].X < L + r) { P[i].X = L + r; if (first) O[i].X = P[i].X + v.X * 0.4f; }
            else if (P[i].X > R - r) { P[i].X = R - r; if (first) O[i].X = P[i].X + v.X * 0.4f; }
            if (P[i].Y < T + r) { P[i].Y = T + r; if (first) O[i].Y = P[i].Y + v.Y * 0.4f; }

            if (first)
            {
                _touch[i] = false;
                // Window edges only catch the body if its middle was above the edge; otherwise a
                // single limb would snag and the figure would hang by its head.
                if (env.FindLanding(P[i].X, O[i].Y + r, P[i].Y + r) is { } p &&
                    (p.Solid || O[J.Pelvis].Y <= p.PrevY + f.Torso * 0.3f))
                {
                    P[i].Y = p.Y - r;
                    O[i].Y = P[i].Y + v.Y * 0.18f;  // small bounce
                    O[i].X = P[i].X - v.X * 0.55f;  // friction
                    _touch[i] = true;
                    _touchHwnd[i] = p.Hwnd;
                    _touchY[i] = p.Y;
                    Contact = true;
                }
            }
            else if (_touch[i] && P[i].Y + r > _touchY[i])
            {
                P[i].Y = _touchY[i] - r;
            }
        }
    }

    void Struggle(float dt, Random rng)
    {
        _struggleT -= dt;
        if (_struggleT > 0) return;
        _struggleT = rng.Range(0.18f, 0.5f) / (0.5f + f.Traits.Energy);
        int[] limbs = { J.FootN, J.FootF, J.HandN, J.HandF, J.KneeN, J.KneeF };
        int k = limbs[rng.Next(limbs.Length)];
        if (k == Pin) return;
        float a = rng.Range(0, MathF.Tau);
        O[k] -= new Vector2(MathF.Cos(a), MathF.Sin(a)) * rng.Range(3, 7) * f.S * (0.4f + f.Traits.Bravery);
    }
}
