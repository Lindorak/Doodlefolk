using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum PetKind { Cat, Dog }

/// <summary>A cat or a dog. Pets pick a favourite figure (whoever spends time with them) and follow it around; dogs
/// chase balls and bring them back, cats nap on soft things and pounce at your cursor. They walk, jump and drop
/// between surfaces using the same map as the figures. You can stroke them (purring, wagging), pick them up and
/// throw them (cats land on their feet).</summary>
sealed class Pet
{
    static int _nextId;
    public readonly int Id = ++_nextId;
    public PetKind Kind;
    public string Name;
    public Color4 Color;
    public float SizeMul = 1;
    readonly float _s;
    public float S => _s * SizeMul;

    public Vector2 Pos, Vel;
    public bool Grounded;
    public IntPtr GroundHwnd;
    public int Facing = 1;
    public bool Held;
    public Vector2 HoldTarget;

    /// <summary>Bonds with figures (by id) and with you.</summary>
    public readonly Dictionary<int, float> Bond = new();
    public float UserBond;
    public Figure? Owner;
    public Prop? Mouth;   // a ball being carried (dogs)

    enum State { Idle, Wander, Follow, Sleep, Chase, Fetch, Pounce, Sit, Petted }
    State _st = State.Idle;
    float _t, _dur = 1, _walk, _tail, _petT, _soundCd, _ownerCheck, _energy = 1, _sleepZ;
    Vector2? _target;
    Prop? _ball;
    bool _offEdge;
    List<NavEdge>? _route;
    int _routeStep;
    float _replanAt;
    readonly Random _rng;

    static readonly string[] CatNames = { "Whiskers", "Mittens", "Luna", "Pickle", "Mochi", "Pepper", "Biscuit", "Socks", "Noodle", "Tofu" };
    static readonly string[] DogNames = { "Rex", "Buddy", "Waffles", "Max", "Pudding", "Bean", "Scout", "Nugget", "Bingo", "Toast" };

    public Pet(PetKind kind, float scale, Random rng)
    {
        Kind = kind;
        _s = scale;
        _rng = rng;
        Name = (kind == PetKind.Cat ? CatNames : DogNames)[rng.Next(10)];
        var cols = kind == PetKind.Cat
            ? new[] { 0x3A3A3A, 0xF2A65A, 0xE8E2D5, 0x8D8D8D, 0xC98B4F }
            : new[] { 0xC98B4F, 0x6D4C41, 0xEDE0C8, 0x3A3A3A, 0xD9A066 };
        Color = M.Hex((uint)cols[rng.Next(cols.Length)]);
        _walk = rng.Range(0, 6);
    }

    public float Height => (Kind == PetKind.Cat ? 15 : 18) * S;
    public float Length => (Kind == PetKind.Cat ? 22 : 26) * S;
    Mover MyMover => new(S * 0.8f, 1900 * S, 0, Height);
    public bool Asleep => _st == State.Sleep;
    public string Activity => Held ? "Being held by you" : _st switch
    {
        State.Sleep => "Napping", State.Follow => Owner != null ? $"Following {Owner.Name}" : "Following someone", State.Chase => "Chasing a ball",
        State.Fetch => "Bringing the ball back", State.Pounce => "Pouncing at your cursor", State.Petted => "Being petted", State.Sit => "Sitting", State.Wander => "Wandering",
        _ => "Hanging out",
    };

    // ---------------- physics ----------------

    public void ApplyCarry(Env env)
    {
        if (Grounded && !Held) Pos += env.Delta(GroundHwnd);
    }

    public void Step(World w, float dt)
    {
        var env = w.Env;
        _t += dt;
        _tail += dt;
        _soundCd -= dt;
        _energy = M.Clamp01(_energy + dt * (_st == State.Sleep ? 0.02f : -0.0015f));
        if (Held)
        {
            Vel = (HoldTarget - Pos) / MathF.Max(dt, 1e-3f);
            Pos = HoldTarget;
            Grounded = false;
            _petT = 0;
            return;
        }
        if (Grounded)
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
        else
        {
            float py = Pos.Y;
            Vel.Y = MathF.Min(Vel.Y + 1900 * S * dt, 3500 * S);
            Pos += Vel * dt;
            var (L, R, T) = env.BoundsAt(Pos.X);
            if (Pos.X < L + 6 * S) { Pos.X = L + 6 * S; Vel.X = MathF.Abs(Vel.X) * 0.3f; }
            if (Pos.X > R - 6 * S) { Pos.X = R - 6 * S; Vel.X = -MathF.Abs(Vel.X) * 0.3f; }
            if (Pos.Y - Height < T) { Pos.Y = T + Height; Vel.Y = MathF.Max(0, Vel.Y); }
            if (Vel.Y > 0 && env.FindLanding(Pos.X, py, Pos.Y) is { } p)
            {
                float impact = Vel.Y;
                Pos.Y = p.Y;
                Grounded = true;
                GroundHwnd = p.Hwnd;
                Vel = new Vector2(Vel.X * 0.25f, 0);
                if (impact > 600 * S) { w.Fx.Dust(Pos, S, 3, 0.3f, w.Rng); World.Play(Sfx.Land, Pos, 0.3f, 1.6f); }
                if (impact > 1800 * S && Kind == PetKind.Dog) { Say(w); UserBond -= 0.05f; }
            }
        }
        if (Mouth != null)
        {
            if (!w.Props.Contains(Mouth)) Mouth = null;
            else { Mouth.Pos = HeadPos + new Vector2(Facing * 5 * S, 3 * S); Mouth.Vel = Vel; }
        }
        Think(w, dt);
    }

    Vector2 HeadPos => Pos + new Vector2(Facing * Length * 0.55f, -Height * (_st == State.Sleep ? 0.35f : 0.85f));
    public Vector2 Centre => Pos + new Vector2(0, -Height * 0.5f);

    // ---------------- mind ----------------

    void Think(World w, float dt)
    {
        _ownerCheck -= dt;
        if (_ownerCheck <= 0) { _ownerCheck = 1; Bonding(w, 1); }
        if (!Grounded) return;
        var cur = w.Cursor;
        // Being stroked by you.
        float cv = w.CursorVel.Length();
        bool stroked = Vector2.Distance(cur, Centre) < Length * 0.7f && cv > 15 * S && cv < 260 * S;
        if (stroked)
        {
            _petT += dt;
            if (_st != State.Petted && _petT > 0.3f) { Go(State.Petted, 2.5f); }
            UserBond = M.Clamp01(UserBond + dt * 0.03f);
        }
        switch (_st)
        {
            case State.Petted:
                Vel.X = 0;
                if (stroked) _dur = MathF.Max(_dur, _t + 1);
                if (_soundCd <= 0) { _soundCd = Kind == PetKind.Cat ? 1.1f : 2.5f; World.Play(Kind == PetKind.Cat ? Sfx.Purr : Sfx.Bark, Pos, Kind == PetKind.Cat ? 0.35f : 0.15f, Kind == PetKind.Cat ? 1 : 1.4f, 0.5); }
                if (_t > _dur) Go(State.Idle, 1);
                return;
            case State.Sleep:
                Vel.X = 0;
                _sleepZ += dt;
                if (_t > _dur || (_energy > 0.98f && _t > 10)) Go(State.Idle, 1.5f);
                return;
            case State.Sit:
                Vel.X = 0;
                if (_t > _dur) Go(State.Idle, 0.5f);
                return;
            case State.Chase:
            {
                if (_ball == null || !w.Props.Contains(_ball) || _ball.Holder != null || Mouth != null) { Go(State.Idle, 1); return; }
                if (Vector2.Distance(_ball.Pos, HeadPos) < 12 * S + _ball.Radius && _ball.Vel.Length() < 500 * S)
                {
                    Mouth = _ball;
                    _ball.Pinned = false;
                    World.Play(Sfx.Bark, Pos, 0.25f, 1.3f, 0.5);
                    Go(State.Fetch, 20);
                    return;
                }
                MoveTo(w, _ball.Pos with { Y = _ball.OnGround ? _ball.Pos.Y + _ball.Radius : _ball.Pos.Y }, 1.6f);
                if (_t > _dur) Go(State.Idle, 1);
                return;
            }
            case State.Fetch:
            {
                // Bring it to whoever it loves most (you, if you're its favourite).
                Vector2 to = Owner != null && (UserBond < BondWith(Owner) || UserBond < 0.4f) ? Owner.Base : cur;
                if (Mouth == null) { Go(State.Idle, 1); return; }
                if (MoveTo(w, to, 1.4f, 30 * S) || _t > _dur)
                {
                    var b = Mouth;
                    Mouth = null;
                    b.Vel = new Vector2(Facing * 60 * S, -100 * S);
                    if (Owner != null && Vector2.Distance(Owner.Base, Pos) < 80 * S) Owner.Brain.OnPetBroughtBall(this, b, w);
                    Go(State.Sit, 2.5f);
                }
                return;
            }
            case State.Pounce:
            {
                if (_t > _dur) { Go(State.Idle, 1); return; }
                float dx = cur.X - Pos.X;
                Facing = dx >= 0 ? 1 : -1;
                if (MathF.Abs(dx) > 70 * S) MoveTo(w, new Vector2(cur.X, Pos.Y), 1.3f, 50 * S);
                else if (_t > 0.6f && cur.Y < Pos.Y && cur.Y > Pos.Y - 200 * S)
                {
                    // Pounce!
                    Vel = new Vector2(dx * 2.4f, -MathF.Sqrt(2 * 1900 * S * MathF.Max(30 * S, Pos.Y - cur.Y + 10 * S)));
                    Grounded = false;
                    _t = 0;
                    _dur = 2;
                }
                else Vel.X = 0;
                return;
            }
            case State.Follow:
            {
                if (Owner == null || Owner.Dead) { Go(State.Idle, 1); return; }
                float gap = MathF.Abs(Owner.Base.X - Pos.X) + MathF.Abs(Owner.Base.Y - Pos.Y);
                if (gap > 90 * S) MoveTo(w, Owner.Base + new Vector2(-Owner.Facing * 30 * S, 0), gap > 300 * S ? 1.7f : 1.1f, 25 * S);
                else Vel.X = 0;
                if (_t > _dur) Go(State.Idle, 1);
                return;
            }
            case State.Wander:
                if (_target is Vector2 tw && MoveTo(w, tw, 0.7f)) Go(State.Idle, _rng.Range(1, 3));
                if (_t > _dur) Go(State.Idle, 1);
                return;
            default:
                Vel.X = 0;
                if (_t > _dur) Decide(w);
                return;
        }
    }

    void Go(State s, float dur)
    {
        _st = s;
        _t = 0;
        _dur = dur;
        _route = null;
        _target = null;
        if (s != State.Petted) _petT = 0;
    }

    float BondWith(Figure f) => Bond.TryGetValue(f.Id, out var b) ? b : 0;

    void Bonding(World w, float dt)
    {
        foreach (var f in w.Figures)
        {
            if (f.Dead) continue;
            float d = Vector2.Distance(f.Base, Pos);
            if (d < 220 * S) Bond[f.Id] = M.Clamp01(BondWith(f) + dt * 0.004f * (1 + f.Tastes.Of(Thing.Pets)));
        }
        var best = w.Figures.Where(f => !f.Dead).OrderByDescending(BondWith).FirstOrDefault();
        if (best != null && BondWith(best) > 0.25f && best != Owner)
        {
            Owner = best;
            best.Brain.OnAdoptedBy(this);
        }
    }

    void Decide(World w)
    {
        var cur = w.Cursor;
        bool cat = Kind == PetKind.Cat;
        // Tired: nap (cats nap a lot, preferably somewhere soft).
        if (_energy < (cat ? 0.6f : 0.35f) && _rng.NextDouble() < 0.6)
        {
            var soft = w.Items.FirstOrDefault(i => i.OnGround && i.Free && (i.Def.Verbs.Contains(Verb.Lie) || i.Def.Verbs.Contains(Verb.Sit)) && Vector2.Distance(i.Pos, Pos) < 900 * S);
            if (cat && soft != null && _rng.NextDouble() < 0.7)
            {
                Go(State.Wander, 15);
                _target = new Vector2(soft.Pos.X, soft.Pos.Y - (soft.Def.SeatY > 0 ? soft.Def.SeatY : soft.Def.H) * soft.Sc);
                _dur = 15;
                return;
            }
            Go(State.Sleep, _rng.Range(20, 60));
            return;
        }
        // A loose ball: dogs go for it.
        if (!cat && Mouth == null)
        {
            var ball = w.Props.Where(p => p.Holder == null && !w.Matches.Any(m => m.Ball == p) && Vector2.Distance(p.Pos, Pos) < 1000 * S && p.SizeMul < 2.2f)
                              .OrderBy(p => Vector2.Distance(p.Pos, Pos)).FirstOrDefault();
            if (ball != null && _rng.NextDouble() < 0.55) { Go(State.Chase, 12); _ball = ball; return; }
        }
        // Cats can't resist a moving cursor.
        if (cat && w.CursorVel.Length() > 200 * S && Vector2.Distance(cur, Pos) < 450 * S && _rng.NextDouble() < 0.5) { Go(State.Pounce, 5); return; }
        if (Owner != null && _rng.NextDouble() < (cat ? 0.35 : 0.65)) { Go(State.Follow, _rng.Range(8, 20)); return; }
        if (_rng.NextDouble() < 0.3) { Go(State.Sit, _rng.Range(2, 6)); if (_rng.NextDouble() < 0.3) Say(w); return; }
        var seg = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
        if (seg != null)
        {
            Go(State.Wander, 8);
            _target = new Vector2(M.ClampIn(Pos.X + _rng.Range(-300, 300) * S, seg.X1 + 10 * S, seg.X2 - 10 * S), seg.Y);
        }
    }

    void Say(World w)
    {
        if (_soundCd > 0) return;
        _soundCd = 3;
        World.Play(Kind == PetKind.Cat ? Sfx.Meow : Sfx.Bark, Pos, 0.3f, Kind == PetKind.Cat ? 1 / MathF.Sqrt(SizeMul) : 1.2f / MathF.Sqrt(SizeMul), 1);
    }

    /// <summary>Head for a point (on this surface or another, via the route map). True once there.</summary>
    bool MoveTo(World w, Vector2 t, float pace, float within = 8)
    {
        var env = w.Env;
        float speed = (Kind == PetKind.Cat ? 70 : 85) * S * pace;
        var seg = env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
        if (seg == null) return false;
        var tp = env.SupportAt(t.X, t.Y, IntPtr.Zero) ?? env.Below(t.X, t.Y - 4 * S);
        if (tp == null || (tp.Hwnd == seg.Hwnd && MathF.Abs(tp.Y - seg.Y) < 2 && tp.X1 < seg.X2 && tp.X2 > seg.X1))
        {
            _route = null;
            float dx = t.X - Pos.X;
            if (MathF.Abs(dx) <= within) { Vel.X = 0; return true; }
            Vel.X = MathF.Sign(dx) * speed;
            return false;
        }
        if (_route == null || _routeStep >= _route.Count || NavGraph.Key(seg) != _route[_routeStep].From)
        {
            if (_t < _replanAt) { Vel.X = 0; return false; }
            _replanAt = _t + 0.5f;
            _route = w.Nav.FindPath(seg, Pos.X, tp, t.X, MyMover, e => e.Kind == MoveKind.Climb ? 1000 : 0, 300);
            _routeStep = 0;
            if (_route == null || _route.Count == 0 || _route.Any(e => e.Kind == MoveKind.Climb)) { _route = null; Vel.X = 0; return false; }
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

    // ---------------- you ----------------

    public bool HitTest(Vector2 p) => MathF.Abs(p.X - Pos.X) < Length * 0.6f && p.Y < Pos.Y + 2 && p.Y > Pos.Y - Height * 1.1f;

    public void Grab() { Held = true; Mouth = null; Go(State.Idle, 1); }

    public void Release(Vector2 v, World w)
    {
        Held = false;
        Vel = v;
        Grounded = false;
        float k = v.Length() / (2500 * S);
        if (k > 0.4f) { UserBond -= 0.08f * k; Say(w); }
    }

    public void CallTo(Vector2 where) { Go(State.Wander, 10); _target = where; }

    /// <summary>A figure is petting it.</summary>
    public void PettedBy(Figure f)
    {
        if (_st is State.Fetch or State.Chase) return;
        Go(State.Petted, 2.5f);
        Bond[f.Id] = M.Clamp01(BondWith(f) + 0.05f);
    }

    // ---------------- drawing ----------------

    public System.Drawing.RectangleF Bounds() =>
        System.Drawing.RectangleF.FromLTRB(Pos.X - Length, Pos.Y - Height * 2 - 30 * S, Pos.X + Length, Pos.Y + 4 * S);

    public void Draw(Renderer r)
    {
        float s = S, f = Facing;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.95f);
        var dark = new Color4(Color.R * 0.7f, Color.G * 0.7f, Color.B * 0.7f, 1);
        bool cat = Kind == PetKind.Cat;
        float L = Length, H = Height;
        Vector2 P(float x, float y) => Pos + new Vector2(x * f, y);
        if (_st == State.Sleep && Grounded && !Held)
        {
            // Curled up: a round body, head tucked in, tail wrapped round.
            var c = P(0, -H * 0.32f);
            r.Oval(c, L * 0.46f + 1.2f * s, H * 0.34f + 1.2f * s, ink);
            r.Oval(c, L * 0.46f, H * 0.34f, Color);
            r.Disc(P(L * 0.3f, -H * 0.3f), H * 0.26f + 1.1f * s, ink);
            r.Disc(P(L * 0.3f, -H * 0.3f), H * 0.26f, Color);
            r.Line(P(L * 0.33f, -H * 0.32f), P(L * 0.4f, -H * 0.32f), ink, 0.9f * s);   // closed eye
            if (cat) r.Line(P(-L * 0.45f, -H * 0.2f), P(-L * 0.1f, -H * 0.02f), dark, 2.4f * s);
            float z = (_sleepZ * 0.5f) % 1;
            r.Text("z", P(L * 0.35f, -H * 0.9f - z * 14 * s), (6 + z * 4) * s, Ui.Ink.A(1 - z), true);
            return;
        }
        bool sit = _st is State.Sit or State.Petted && Grounded;
        float step = MathF.Sin(_walk), step2 = MathF.Sin(_walk + MathF.PI);
        float moving = Grounded && MathF.Abs(Vel.X) > 4 * s ? 1 : 0;
        float bodyY = -H * (sit ? 0.5f : 0.62f), hip = -L * 0.32f, chest = L * 0.3f;
        // Legs (far pair darker, behind the body).
        float leg = H * 0.6f;
        void Leg(float x, float ph, bool far, bool hind)
        {
            Vector2 top = P(x, bodyY + H * 0.12f);
            Vector2 foot = sit && hind ? P(x + L * 0.12f, -0.5f * s) : P(x + MathF.Sin(ph) * 4 * s * moving, -0.5f * s - MathF.Max(0, MathF.Cos(ph)) * 3 * s * moving);
            if (!Grounded) foot = P(x + (hind ? -4 : 4) * s, bodyY + leg * 0.9f);
            r.Line(top, foot, far ? dark : Color, (cat ? 2.6f : 3.2f) * s);
        }
        Leg(hip + 2 * s, _walk + 1.6f, true, true);
        Leg(chest - 2 * s, _walk + 0.4f, true, false);
        // Tail: cats a long swishing curve, dogs a short wag (fast when happy).
        float wag = MathF.Sin(_tail * (cat ? 2.2f : (_st is State.Petted or State.Fetch or State.Follow ? 16 : 7)));
        Vector2 t0 = P(hip - 2 * s, bodyY - H * 0.08f);
        if (cat)
        {
            // A soft S-curve that swishes from the tip.
            Vector2 t1 = t0 + new Vector2(-f * 6 * s, -2 * s + wag * 1.5f * s), t2 = t1 + new Vector2(-f * 3 * s, -6 * s + wag * 2.5f * s),
                    t3 = t2 + new Vector2(f * 1.5f * s + wag * 3 * s * f, -4 * s);
            r.Line(t0, t1, ink, 3.6f * s); r.Line(t1, t2, ink, 3.4f * s); r.Line(t2, t3, ink, 3.2f * s);
            r.Line(t0, t1, Color, 2.4f * s); r.Line(t1, t2, Color, 2.2f * s); r.Line(t2, t3, Color, 2f * s);
        }
        else
        {
            Vector2 t1 = t0 + new Vector2(-f * 7 * s, -7 * s) + new Vector2(wag * 3 * s * f, 0);
            r.Line(t0, t1, ink, 3.8f * s); r.Line(t0, t1, Color, 2.6f * s);
        }
        // Body.
        var bc = P((hip + chest) / 2, bodyY);
        float tilt = sit ? -0.35f : 0;
        r.Oval(bc, L * 0.42f + 1.2f * s, H * (cat ? 0.24f : 0.3f) + 1.2f * s, ink);
        r.Oval(bc, L * 0.42f, H * (cat ? 0.24f : 0.3f), Color);
        if (sit) r.Oval(P(hip + 3 * s, bodyY + H * 0.12f), L * 0.18f, H * 0.2f, Color);
        Leg(hip, _walk, false, true);
        Leg(chest, _walk + MathF.PI, false, false);
        // Head.
        var head = HeadPos + new Vector2(0, tilt * 4 * s);
        float hr = H * (cat ? 0.3f : 0.33f);
        if (cat)
        {
            r.FillPolygon(stackalloc Vector2[] { head + new Vector2(-f * hr * 0.75f, -hr * 0.4f), head + new Vector2(-f * hr * 0.55f, -hr * 1.55f), head + new Vector2(-f * hr * 0.05f, -hr * 0.8f) }, ink);
            r.FillPolygon(stackalloc Vector2[] { head + new Vector2(f * hr * 0.05f, -hr * 0.8f), head + new Vector2(f * hr * 0.55f, -hr * 1.55f), head + new Vector2(f * hr * 0.75f, -hr * 0.4f) }, ink);
        }
        r.Disc(head, hr + 1.2f * s, ink);
        r.Disc(head, hr, Color);
        if (!cat)
        {
            // Snout and a floppy ear.
            r.Oval(head + new Vector2(f * hr * 0.75f, hr * 0.25f), hr * 0.5f + 1 * s, hr * 0.38f + 1 * s, ink);
            r.Oval(head + new Vector2(f * hr * 0.75f, hr * 0.25f), hr * 0.5f, hr * 0.38f, Color);
            r.Oval(head + new Vector2(-f * hr * 0.2f, hr * 0.15f), hr * 0.32f, hr * 0.65f, dark);
        }
        r.Disc(head + new Vector2(f * hr * (cat ? 0.85f : 1.2f), hr * (cat ? 0.15f : 0.2f)), 1.3f * s, ink);   // nose
        if (_st == State.Petted) r.Line(head + new Vector2(f * hr * 0.25f, -hr * 0.15f), head + new Vector2(f * hr * 0.55f, -hr * 0.15f), ink, 1 * s);   // happy, eyes shut
        else r.Disc(head + new Vector2(f * hr * 0.4f, -hr * 0.15f), 1.3f * s, ink);
        if (_st == State.Petted && ((int)(_t * 2) % 2 == 0)) r.Text("♥", head + new Vector2(0, -hr * 2.2f), 7 * s, new Color4(0.9f, 0.3f, 0.45f, 0.9f), true);
    }
}
