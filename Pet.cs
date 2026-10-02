using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum PetKind { Cat, Dog, Parrot, Rabbit, Hamster }

/// <summary>An animal on the desktop: a cat, a dog or a parrot. Each one has needs (food, water, the bathroom, sleep,
/// your attention, play), moods and stress, habits that can be trained (with a spray bottle and treats), friendships and
/// feuds with the other animals, and grows up from a kitten, puppy or chick. They walk, jump and drop between surfaces
/// using the same map as the figures; parrots fly and perch (on you, too). This file: identity, body and physics.
/// See Pet.Needs (care), Pet.Mind (decisions and behaviour), Pet.Social (other animals), Pet.Training, Pet.Draw.</summary>
sealed partial class Pet
{
    static int _nextId;
    public readonly int Id = ++_nextId;
    public PetKind Kind;
    public string Name;
    public string RandomName()
    {
        var names = Kind switch { PetKind.Cat => CatNames, PetKind.Dog => DogNames, PetKind.Rabbit => RabbitNames, PetKind.Hamster => HamsterNames, _ => BirdNames };
        if (Mods.PetNames.TryGetValue(Kind, out var extra) && extra.Count > 0) names = names.Concat(extra).ToArray();
        return names[_rng.Next(names.Length)];
    }
    public Color4 Color, Accent;
    public float SizeMul = 1;
    readonly float _s;
    /// <summary>0 newborn … 1 grown up.</summary>
    public float Age = 1;
    public DateTime Born = DateTime.Now;
    public bool Young => Age < 1;
    float Grow => 0.55f + 0.45f * Age;
    public float S => _s * SizeMul * Grow;
    /// <summary>The world's scale (sizes of UI around the pet, independent of its own size).</summary>
    public float Scale => _s;

    public Vector2 Pos, Vel;
    public bool Grounded, Flying;
    public IntPtr GroundHwnd;
    public int Facing = 1;
    public bool Held;
    public Vector2 HoldTarget;
    /// <summary>On a leash held by your cursor.</summary>
    public bool Leashed;
    /// <summary>A parrot stepped up onto your cursor and rides it around.</summary>
    public bool OnCursor;

    /// <summary>Bonds with figures (by id) and with you.</summary>
    public readonly Dictionary<int, float> Bond = new();
    public float UserBond = 0.2f;
    public Figure? Owner;
    public Prop? Mouth;   // a ball being carried (dogs)
    /// <summary>Lighting this frame (see Figure.Lit).</summary>
    public float Warmth, NightDim;
    Color4 Lit(Color4 c)
    {
        if (NightDim > 0.005f) c = new Color4(c.R + (0.22f - c.R) * NightDim, c.G + (0.25f - c.G) * NightDim, c.B + (0.38f - c.B) * NightDim, c.A);
        if (Warmth > 0.005f) { float k = Warmth * 0.32f; c = new Color4(c.R + (1 - c.R) * k, c.G + (0.62f - c.G) * k, c.B + (0.3f - c.B) * k, c.A); }
        return c;
    }

    readonly Random _rng;
    float _walk, _tail, _flap, _soundCd, _ownerCheck;
    bool _offEdge;
    List<NavEdge>? _route;
    int _routeStep;
    float _replanAt;

    static readonly string[] CatNames = { "Whiskers", "Mittens", "Luna", "Pickle", "Mochi", "Pepper", "Biscuit", "Socks", "Noodle", "Tofu", "Ziggy", "Olive" };
    static readonly string[] DogNames = { "Rex", "Buddy", "Waffles", "Max", "Pudding", "Bean", "Scout", "Nugget", "Bingo", "Toast", "Pepper", "Moose" };
    static readonly string[] RabbitNames = { "Clover", "Thumper", "Hazel", "Biscuit", "Flopsy", "Nibbles", "Willow", "Pip", "Cinnamon", "Dandelion", "Oreo", "Bun" };
    static readonly string[] HamsterNames = { "Peanut", "Nugget", "Hammy", "Squeak", "Butterscotch", "Mochi", "Crumb", "Tiny", "Pebble", "Sesame", "Fudge", "Bean" };
    static readonly string[] BirdNames = { "Kiwi", "Mango", "Rio", "Sunny", "Pip", "Captain", "Peaches", "Zazu", "Tango", "Blu", "Polly", "Sky" };

    public Pet(PetKind kind, float scale, Random rng)
    {
        Kind = kind;
        _s = scale;
        _rng = rng;
        Name = RandomName();
        // Coats and plumage.
        if (kind == PetKind.Parrot)
        {
            var looks = new (uint body, uint accent)[] { (0x43A047, 0xE53935), (0x1E88E5, 0xFDD835), (0xE53935, 0x1E88E5), (0x9E9E9E, 0xFDD835), (0x7CB342, 0xFFB300), (0x26C6DA, 0x8E24AA) };
            var l = looks[rng.Next(looks.Length)];
            Color = M.Hex(l.body);
            Accent = M.Hex(l.accent);
        }
        else
        {
            var cols = kind switch
            {
                PetKind.Cat => new[] { 0x3A3A3A, 0xF2A65A, 0xE8E2D5, 0x8D8D8D, 0xC98B4F, 0x5B4636 },
                PetKind.Rabbit => new[] { 0xF5F2EC, 0x9C7A5B, 0x8D8D8D, 0x5D4037, 0xD7B98E },
                PetKind.Hamster => new[] { 0xE0A458, 0xF3E3C3, 0xB0A090, 0xC88A4E, 0x8D7B6A },
                _ => new[] { 0xC98B4F, 0x6D4C41, 0xEDE0C8, 0x3A3A3A, 0xD9A066, 0xF5F0E6 },
            };
            Color = M.Hex((uint)cols[rng.Next(cols.Length)]);
            Accent = M.Hex(0xF5F0E6);
        }
        _walk = rng.Range(0, 6);
        InitSpecies();
        InitMind();
    }

    public float Height => Kind switch { PetKind.Cat => 15, PetKind.Dog => 18, PetKind.Rabbit => 13, PetKind.Hamster => 7, _ => 15 } * S;
    public float Length => Kind switch { PetKind.Cat => 22, PetKind.Dog => 26, PetKind.Rabbit => 17, PetKind.Hamster => 10, _ => 9 } * S;
    Mover MyMover => new(S * 0.8f, 1900 * S, 0, Height);
    public string Species(bool young = true) => Kind switch
    {
        PetKind.Cat => young && Age < 0.6f ? "kitten" : "cat",
        PetKind.Dog => young && Age < 0.6f ? "puppy" : "dog",
        PetKind.Rabbit => young && Age < 0.6f ? "bunny" : "rabbit",
        PetKind.Hamster => young && Age < 0.6f ? "baby hamster" : "hamster",
        _ => young && Age < 0.6f ? "chick" : "parrot",
    };
    public Vector2 Centre => Pos + new Vector2(0, -Height * 0.5f);
    /// <summary>Where a leash clips on (the collar).</summary>
    public Vector2 Collar => Kind == PetKind.Parrot ? Pos + new Vector2(0, -Height * 0.75f) : HeadPos + new Vector2(-Facing * Height * 0.12f, Height * 0.25f);
    public float LeashLength => 150 * _s;

    // ---------------- physics ----------------

    public void ApplyCarry(Env env)
    {
        if (Grounded && !Held && !OnCursor) Pos += env.Delta(GroundHwnd);
    }

    public void Step(World w, float dt)
    {
        var env = w.Env;
        _tail += dt;
        _soundCd -= dt;
        if (Flying || (Kind == PetKind.Parrot && (Held || OnCursor))) _flap += dt * (Flying ? 22 : 3);
        UpdateNeeds(w, dt);
        if (Held)
        {
            Vel = (HoldTarget - Pos) / MathF.Max(dt, 1e-3f);
            Pos = HoldTarget;
            Grounded = false;
            Flying = false;
            ThinkHeld(w, dt);
            return;
        }
        if (OnCursor)
        {
            Pos = w.Cursor + new Vector2(0, 3 * S);
            Vel = w.CursorVel;
            Grounded = Flying = false;
            if (w.CursorVel.Length() > 2600 * _s) { OnCursor = false; Flying = true; Squawk(w); Stress = M.Clamp01(Stress + 0.1f); }
            Think(w, dt);
            return;
        }
        if (Flying) FlyStep(w, dt);
        else if (Grounded)
        {
            Vel.Y = 0;
            float nx = Pos.X + Vel.X * dt;
            var seg = env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
            if (seg != null && !_offEdge)
            {
                float m = 3 * S;
                if (nx < seg.X1 + m && Vel.X < 0) { nx = MathF.Max(nx, MathF.Min(Pos.X, seg.X1 + m)); Vel.X = 0; }
                if (nx > seg.X2 - m && Vel.X > 0) { nx = MathF.Min(nx, MathF.Max(Pos.X, seg.X2 - m)); Vel.X = 0; }
            }
            var (L, R, _) = env.BoundsAt(nx);
            nx = M.ClampIn(nx, L + 8 * S, R - 8 * S);
            Pos.X = nx;
            var sup = env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
            if (sup == null) { Grounded = false; _offEdge = false; }
            else { Pos.Y = sup.Y; GroundHwnd = sup.Hwnd; }
            if (MathF.Abs(Vel.X) > 4 * S) { Facing = MathF.Sign(Vel.X) > 0 ? 1 : -1; _walk += dt * MathF.Abs(Vel.X) / (6 * S); }
        }
        else if (Kind == PetKind.Parrot && !_thrown) { Flying = true; _flyTo = null; }
        else
        {
            float py = Pos.Y;
            Vel.Y = MathF.Min(Vel.Y + 1900 * S * dt, 3500 * S);
            Pos += Vel * dt;
            var (L, R, T) = env.BoundsAt(Pos.X);
            if (Pos.X < L + 6 * S) { Pos.X = L + 6 * S; Vel.X = MathF.Abs(Vel.X) * 0.3f; }
            if (Pos.X > R - 6 * S) { Pos.X = R - 6 * S; Vel.X = -MathF.Abs(Vel.X) * 0.3f; }
            if (Pos.Y - Height < T) { Pos.Y = T + Height; Vel.Y = MathF.Max(0, Vel.Y); }
            if (Kind == PetKind.Parrot && _thrown && Vel.Y > 200 * S) { _thrown = false; Flying = true; _flyTo = null; Squawk(w); }
            if (Vel.Y > 0 && env.FindLanding(Pos.X, py, Pos.Y) is { } p)
            {
                float impact = Vel.Y;
                Pos.Y = p.Y;
                Grounded = true;
                GroundHwnd = p.Hwnd;
                Vel = new Vector2(Vel.X * 0.25f, 0);
                _thrown = false;
                if (impact > 600 * S) { w.Fx.Dust(Pos, S, 3, 0.3f, w.Rng); World.Play(Sfx.Land, Pos, 0.3f, 1.6f); }
                OnLanded(w, impact);
            }
        }
        if (Leashed) LeashStep(w, dt);
        if (Mouth != null)
        {
            if (!w.Props.Contains(Mouth)) Mouth = null;
            else { Mouth.Pos = HeadPos + new Vector2(Facing * 5 * S, 3 * S); Mouth.Vel = Vel; }
        }
        Think(w, dt);
    }

    bool _thrown;
    Vector2? _flyTo;
    /// <summary>Flight: steer toward a spot, flapping; settle onto whatever's below when it arrives.</summary>
    void FlyStep(World w, float dt)
    {
        var env = w.Env;
        float speed = 300 * S * BodyPace;
        Vector2 desired;
        if (_flyTo is Vector2 t)
        {
            Vector2 d = t - Pos;
            float len = d.Length();
            desired = len > 1 ? d / len * MathF.Min(speed, len * 4 + 40 * S) : Vector2.Zero;
            if (len < 5 * S)
            {
                // Arrived: land on what's here (or hover, if there's nothing).
                if (LandingAt(env, t) is { } p)
                {
                    Pos = new Vector2(t.X, p.Y);
                    Grounded = true; Flying = false; GroundHwnd = p.Hwnd; Vel = Vector2.Zero;
                    _flyTo = null;
                    return;
                }
                _flyTo = null;
            }
        }
        else desired = new Vector2(MathF.Sin(_tail * 0.7f) * 30 * S, MathF.Sin(_tail * 2.1f) * 20 * S);
        Vel += (desired - Vel) * MathF.Min(1, dt * 3.5f);
        Pos += Vel * dt;
        var v = env.Virtual;
        Pos.X = M.ClampIn(Pos.X, v.Left + 10 * S, v.Right - 10 * S);
        Pos.Y = M.ClampIn(Pos.Y, v.Top + Height + 10 * S, v.Bottom - 2);
        if (MathF.Abs(Vel.X) > 8 * S) Facing = Vel.X > 0 ? 1 : -1;
        if (_rng.NextDouble() < dt * 0.5) World.Play(Sfx.Whoosh, Pos, 0.08f, 2.2f, 0.5);
    }

    /// <summary>On a leash: free to sniff about within its length; past that it's pulled along (and, if you yank it
    /// up high, lifted off its feet, which it doesn't love).</summary>
    void LeashStep(World w, float dt)
    {
        Vector2 hand = w.Cursor, c = Collar;
        float d = Vector2.Distance(hand, c), max = LeashLength;
        if (d <= max) return;
        Vector2 dir = (hand - c) / d;
        float over = d - max;
        if (Kind == PetKind.Parrot) { Flying = true; Grounded = false; Pos += dir * over; return; }
        if (Grounded && hand.Y < c.Y - max * 0.9f && over > max * 0.6f)
        {
            // Yanked up.
            Grounded = false;
            Vel = dir * 500 * S;
            Stress = M.Clamp01(Stress + 0.06f);
            UserBond = MathF.Max(-1, UserBond - 0.01f);
            Say(w, true);
            return;
        }
        if (Grounded) Pos.X += dir.X * MathF.Min(over, 600 * S * dt);
        else { Pos += dir * over; Vel = Vector2.Lerp(Vel, dir * 300 * S, 0.2f); }
        _leashTug = 0.4f;
    }

    float _leashTug;

    Vector2 HeadPos => Kind == PetKind.Parrot ? Pos + new Vector2(Facing * 1.5f * S, -Height * 0.85f)
        : Pos + new Vector2(Facing * Length * 0.55f, -Height * (_st == State.Sleep ? 0.35f : _pose is Pose.HeadDown ? 0.3f : 0.85f));

    /// <summary>Head for a point (on this surface or another, via the route map; parrots just fly). True once there.</summary>
    bool MoveTo(World w, Vector2 t, float pace, float within = 8)
    {
        if (Kind == PetKind.Parrot) return FlyTo(w, t, within);
        var env = w.Env;
        float speed = Kind switch { PetKind.Cat => 70, PetKind.Rabbit => 95, PetKind.Hamster => 55, _ => 85 } * S * pace * (Young ? 0.85f : 1) * BodyPace;
        if (!Grounded) return false;
        var seg = env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
        if (seg == null) return false;
        var tp = env.SupportAt(t.X, t.Y, IntPtr.Zero) ?? env.Below(t.X, t.Y - 4 * S);
        if (tp == null || (tp.Hwnd == seg.Hwnd && MathF.Abs(tp.Y - seg.Y) < 2 && tp.X1 < seg.X2 && tp.X2 > seg.X1))
        {
            _route = null;
            float dx = t.X - Pos.X;
            if (MathF.Abs(dx) <= within) { Vel.X = 0; return true; }
            Vel.X = MathF.Sign(dx) * MathF.Min(speed, MathF.Abs(dx) * 6 + 10 * S);
            return false;
        }
        if (_route == null || _routeStep >= _route.Count || NavGraph.Key(seg) != _route[_routeStep].From)
        {
            if (_t < _replanAt) { Vel.X = 0; return false; }
            _replanAt = _t + 0.5f;
            _route = w.Nav.FindPath(seg, Pos.X, tp, t.X, MyMover, e => e.Kind == MoveKind.Climb ? 1000 : 0, 300);
            _routeStep = 0;
            if (_route == null || _route.Count == 0 || _route.Any(e => e.Kind == MoveKind.Climb))
            {
                _route = null; Vel.X = 0; _stuck += 0.5f;
                // No way round: if it's somewhere lower, just hop down off the front of the ledge.
                if (tp.Y > seg.Y + 30 * S && !seg.Solid) { Pos.Y += 4; Grounded = false; Vel = new Vector2(MathF.Sign(t.X - Pos.X) * 70 * S, 0); _stuck = 0; }
                return false;
            }
        }
        var e = _route[_routeStep];
        float ex = e.FromX - Pos.X;
        if (MathF.Abs(ex) > 3 * S) { Vel.X = MathF.Sign(ex) * speed; return false; }
        _routeStep++;
        switch (e.Kind)
        {
            case MoveKind.Jump:
                float toY = w.Nav.Get(e.To)?.Y ?? e.ToY;
                if (NavGraph.Lob(Pos, new Vector2(e.ToX, toY), 1900 * S, S * 0.8f, out var v)) { Vel = v; Grounded = false; Facing = v.X >= 0 ? 1 : -1; }
                break;
            default:
                _offEdge = true;
                Vel.X = MathF.Sign(e.ToX - e.FromX) * speed;
                break;
        }
        return false;
    }

    float _stuck;

    /// <summary>A surface right at (or just under) this point to land on.</summary>
    Platform? LandingAt(Env env, Vector2 t)
    {
        var p = env.SupportAt(t.X, t.Y, IntPtr.Zero) ?? env.Below(t.X, t.Y - 6 * S);
        return p != null && MathF.Abs(p.Y - t.Y) < 10 * S ? p : null;
    }

    /// <summary>Parrots: take off (if needed) and fly to a spot. True once landed (or hovering) there.</summary>
    bool FlyTo(World w, Vector2 t, float within)
    {
        if (Vector2.Distance(Pos, t) <= within + 2 * S)
        {
            // There: settle onto the surface if there is one (or keep hovering).
            if (Flying && LandingAt(w.Env, Pos) is { } p) { Pos = new Vector2(Pos.X, p.Y); Grounded = true; Flying = false; GroundHwnd = p.Hwnd; Vel = Vector2.Zero; _flyTo = null; }
            Vel.X = 0;
            return true;
        }
        if (!Flying) { Flying = true; Grounded = false; Vel = new Vector2(0, -160 * S); World.Play(Sfx.Whoosh, Pos, 0.12f, 2); }
        _flyTo = t;
        return false;
    }

    // ---------------- you ----------------

    public bool HitTest(Vector2 p) => Kind == PetKind.Parrot
        ? MathF.Abs(p.X - Pos.X) < 9 * S && p.Y < Pos.Y + 2 && p.Y > Pos.Y - Height * 1.15f
        : MathF.Abs(p.X - Pos.X) < Length * 0.6f && p.Y < Pos.Y + 2 && p.Y > Pos.Y - Height * 1.1f;

    public void Grab()
    {
        Held = true; OnCursor = false; Mouth = null;
        GoIdle(1);
    }

    public void Release(Vector2 v, World w)
    {
        Held = false;
        Vel = v;
        Grounded = false;
        _thrown = true;
        float k = v.Length() / (2500 * S);
        if (k > 0.4f) { UserBond -= 0.08f * k; Stress = M.Clamp01(Stress + 0.2f * k); Say(w, true); Log($"Got thrown (hated it)"); }
    }

    public System.Drawing.RectangleF Bounds()
    {
        float up = Height * 2 + 46 * _s;
        float wide = MathF.Max(Length, 30 * _s) + (_scuffleWith != null ? 30 * _s : 0);
        var r = System.Drawing.RectangleF.FromLTRB(Pos.X - wide, Pos.Y - up, Pos.X + wide, Pos.Y + 4 * S);
        return r;
    }
}
