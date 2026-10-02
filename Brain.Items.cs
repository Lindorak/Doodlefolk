using System.Numerics;

namespace StickFight;

/// <summary>Using objects. Figures don't know "a bed", they know verbs (sit, lie, bounce, eat, hide...). Any object
/// offering a verb is a candidate whenever the figure's needs, mood and tastes call for that verb.</summary>
sealed partial class Brain
{
    public float Hunger = 0.2f;
    Item? _item;
    Verb _verb;
    int _seat = -1;
    int _bounces;
    bool _itemPending;
    float _nextBite;

    float ItemLike(Item it)
    {
        var likes = it.Def.Likes;
        if (likes.Length == 0) return 0;
        float s = 0;
        foreach (var t in likes) s += f.Tastes.Of(t);
        return s / likes.Length;
    }

    int FreeSeat(Item it)
    {
        int best = -1;
        float bd = float.MaxValue;
        for (int i = 0; i < it.Def.Seats.Length; i++)
        {
            if (it.Seated[i] != null && it.Seated[i] != f) continue;
            float d = MathF.Abs(it.SeatX(i) - f.Base.X);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    bool HunterAround(World w) => w.Figures.Any(o => o != f && o.Hunter && Vector2.Distance(o.Base, f.Base) < 700 * S) ||
                                 w.Figures.Any(o => o != f && o.Brain.InFight && o.Brain.Foe == f);

    (float, Action)? ItemOption(World w)
    {
        if (w.Items.Count == 0) return null;
        float E = P.Energy, tired = 1 - Stamina;
        (float score, Item it, Verb v)? best = null;
        foreach (var it in w.Items)
        {
            if (!it.Free || !it.OnGround) continue;
            float dist = Vector2.Distance(it.Pos, f.Base);
            if (dist > 2200 * S) continue;
            float near = 1 / (1 + dist / (700 * S));
            float like = MathF.Max(0.05f, 1 + ItemLike(it) * 1.5f);
            foreach (var v in it.Def.Verbs)
            {
                float wgt = v switch
                {
                    Verb.Sit => FreeSeat(it) >= 0 ? (tired * 0.9f + (1 - E) * 0.4f + 0.15f) * (0.6f + it.Def.Comfort) * Taste(Thing.Sitting) : 0,
                    Verb.Lie => it.User == null && it.Seated.All(s => s == null)
                        ? (Stamina < 0.45f || f.Tastes.Likes(Thing.Napping) ? (0.65f - Stamina) * 4 + 0.3f : 0.04f) * (0.5f + it.Def.Comfort) * Taste(Thing.Napping) : 0,
                    Verb.Hammock => it.User == null ? (tired * 1.2f + (1 - E) * 0.5f + P.Playfulness * 0.2f) * it.Def.Comfort : 0,
                    Verb.Bounce => Stamina > 0.4f ? (P.Playfulness * 0.8f + E * 0.6f) * Taste(Thing.Tricks) : 0,
                    Verb.Eat => it.Holder == null ? (Hunger > 0.35f ? Hunger * 3 : Hunger * 0.4f) * Taste(Thing.Eating) : 0,
                    Verb.Hide => it.User == null ? (Fear > 0.4f || HunterAround(w) ? 3 + Fear * 3 : P.Playfulness * 0.12f) : 0,
                    Verb.Dance => it.Playing ? MathF.Max(0, f.Tastes.Of(Thing.Dancing) + 0.35f) * 1.3f * (0.5f + Joy) : 0,
                    Verb.Read => it.Holder == null ? (P.Curiosity * (1 - E) * 0.7f + 0.05f) * Taste(Thing.Reading) : 0,
                    Verb.Warm => (P.Sociability * 0.5f + (1 - E) * 0.3f + 0.1f) * (w.Figures.Count(o => o.Brain._item == it) > 0 ? 1.6f : 1),
                    Verb.Stand => P.Playfulness * 0.06f,
                    Verb.Wield or Verb.Shoot => WeaponWant(it, v),
                    _ => 0,
                };
                float score = wgt * like * near;
                if (score > 0.01f && (best == null || score > best.Value.score)) best = (score, it, v);
            }
        }
        if (best is not { } b) return null;
        return (b.score * 1.6f, () => UseItem(b.it, b.v, w));
    }

    public void ForceUse(Item it, Verb v, World w) { if (f.Mode == Mode.Control && f.Grounded) UseItem(it, v, w); }

    /// <summary>Head to an object and start using it with <paramref name="v"/>.</summary>
    void UseItem(Item it, Verb v, World w)
    {
        var env = w.Env;
        _item = it;
        _verb = v;
        _seat = v == Verb.Sit ? FreeSeat(it) : -1;
        if (v == Verb.Sit && _seat < 0) { _item = null; Go(G.Idle, 1); return; }
        if (_seat >= 0) it.Seated[_seat] = f;
        if (v is Verb.Lie or Verb.Hammock or Verb.Hide) it.User = f;
        var item = it;
        Vector2? Spot()
        {
            if (!w.Items.Contains(item) || !item.Free) return null;
            float x = _verb switch
            {
                Verb.Sit => item.SeatX(_seat),
                Verb.Warm => item.Pos.X + MathF.Sign(f.Base.X - item.Pos.X == 0 ? 1 : f.Base.X - item.Pos.X) * (item.Def.W * item.Sc * 0.5f + 12 * S),
                Verb.Dance => item.Pos.X + MathF.Sign(f.Base.X - item.Pos.X == 0 ? 1 : f.Base.X - item.Pos.X) * (item.Def.W * item.Sc * 0.5f + 20 * S),
                _ => item.Pos.X,
            };
            // Trampolines and tables: go up onto the surface. Everything else: the ground beside/under it.
            if (_verb is Verb.Bounce or Verb.Stand)
                foreach (var p in env.Platforms)
                    if (p.Item == item && p.Y < item.Pos.Y - 2) return new Vector2(M.ClampIn(x, p.X1 + 4 * S, p.X2 - 4 * S), p.Y);
            return new Vector2(x, item.Pos.Y);
        }
        _itemPending = true;
        Navigate(Spot, 6 * S, Hunger > 0.7f || (v == Verb.Hide && Fear > 0.4f), () => BeginUse(w), WalkPurpose.Other);
        _dur = 30;
    }

    void BeginUse(World w)
    {
        var it = _item;
        if (it == null || !w.Items.Contains(it) || !it.Free) { LeaveItem(); Go(G.Idle, 1); return; }
        if (_verb is Verb.Wield or Verb.Shoot) { Equipped(it); return; }
        if (_verb is Verb.Lie or Verb.Hammock or Verb.Eat or Verb.Read or Verb.Hide or Verb.Bounce) f.DropWeapon(Vector2.Zero);
        float use = _verb switch
        {
            Verb.Sit => rng.Range(8, 25) * (0.6f + it.Def.Comfort),
            Verb.Lie or Verb.Hammock => rng.Range(18, 45),
            Verb.Bounce => rng.Range(6, 14),
            Verb.Hide => rng.Range(6, 15) + Fear * 10,
            Verb.Dance => rng.Range(8, 18),
            Verb.Read => rng.Range(12, 30),
            Verb.Warm => rng.Range(12, 30),
            Verb.Stand => rng.Range(3, 8),
            _ => 30,
        };
        Go(G.UseItem, use);
        if (_seat >= 0 && _seat < it.Seated.Length) it.Seated[_seat] = f;
        if (_verb is Verb.Lie or Verb.Hammock or Verb.Hide) it.User = f;
        _bounces = 0;
        _nextBite = 0.8f;
        var env = w.Env;
        switch (_verb)
        {
            case Verb.Sit:
            {
                float sx = it.SeatX(_seat), sy = it.Pos.Y - it.Def.SeatY * it.Sc;
                f.Mount(new Vector2(sx, sy), it.Handle);
                if (it.Def.Facing == SeatFacing.Side)
                {
                    // Facing away from the chair back.
                    f.Facing = it.Flip ? 1 : -1;
                    f.SetAction(Act.SitEdge);
                }
                else f.SetAction(it.Def.Facing == SeatFacing.In ? Act.SitBack : Act.SitFront);
                break;
            }
            case Verb.Lie:
            {
                var surf = env.Platforms.FirstOrDefault(p => p.Item == it);
                float y = surf?.Y ?? it.Pos.Y;
                f.Mount(new Vector2(it.Pos.X, y), surf != null ? it.Handle : it.GroundHwnd);
                f.Facing = it.Flip ? 1 : -1;    // head toward the pillow end
                f.SetAction(Act.Lie);
                break;
            }
            case Verb.Hammock:
            {
                var (c, _) = it.HammockCentre();
                f.Mount(c, it.Handle);
                f.Facing = rng.NextDouble() < 0.5 ? 1 : -1;
                f.SetAction(Act.Lie);
                break;
            }
            case Verb.Eat:
            case Verb.Read:
                it.Holder = f;
                f.CarryingItem = it;
                if (_verb == Verb.Read) it.Open = true;
                f.SetAction(_verb == Verb.Eat ? Act.Eat : Act.Read);
                break;
            case Verb.Hide:
                f.SetAction(Act.Curl);
                f.Emote(Fear > 0.4f ? "!" : "shh", 0.9f);
                break;
            case Verb.Warm:
                FaceTo(it.Pos.X);
                f.SetAction(Act.Warm);
                break;
        }
    }

    void DoUseItem(World w)
    {
        var it = _item;
        if (it == null || !w.Items.Contains(it)) { LeaveItem(); Go(G.Idle, 1); return; }
        f.DesiredVX = 0;
        bool done = _t > _dur;
        switch (_verb)
        {
            case Verb.Sit:
                if (Stamina < 0.98f) Stamina = MathF.Min(1, Stamina + World.Dt * 0.01f * (0.5f + it.Def.Comfort));
                if (it.Def.Facing != SeatFacing.Side) f.SetAction(it.Def.Facing == SeatFacing.In ? Act.SitBack : Act.SitFront);
                else f.SetAction(Act.SitEdge);
                if (it.Def.Comfort > 0.7f && Stamina < 0.3f && _t > 5 && rng.NextDouble() < World.Dt * 0.05) { f.Emote("z", 2); }
                break;
            case Verb.Lie:
            case Verb.Hammock:
                f.SetAction(Act.Lie);
                Stamina = MathF.Min(1, Stamina + World.Dt * 0.025f * (0.5f + it.Def.Comfort));
                if (_t > 1.5f && f.CurrentEmote != "z") f.Emote("z", 3);
                if (_verb == Verb.Hammock)
                {
                    // Keep our spot as the hammock swings (the net is a platform; ApplyCarry rides it).
                    var (c, _) = it.HammockCentre();
                    if (MathF.Abs(f.Base.X - c.X) > 6 * S) f.Mount(c, it.Handle);
                }
                if (Stamina > 0.98f && _t > 10) done = true;
                break;
            case Verb.Bounce:
                if (f.Grounded && !f.JumpPending)
                {
                    bool onIt = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd)?.Item == it;
                    if (!onIt || _bounces > 12 || Stamina < 0.2f) { done = true; break; }
                    _bounces++;
                    float h = f.Height * (0.6f + MathF.Min(_bounces, 6) * 0.35f) * (0.8f + P.Energy * 0.4f);
                    // Straight up, nudged back toward the middle so we come down on the mat again.
                    float vy = MathF.Sqrt(2 * f.Gravity * h), air = 2 * vy / f.Gravity;
                    float toMid = (it.Pos.X - f.Base.X) / air + rng.Range(-8, 8) * S;
                    f.RequestJump(new Vector2(toMid, -vy), 0.04f);
                    if (_bounces > 3 && rng.NextDouble() < P.Playfulness * 0.4f) f.RequestFlip(h * 0.5f);
                    if (_bounces == 4) f.Emote(rng.NextDouble() < 0.5 ? "!!" : "♪", 1);
                    Stamina = MathF.Max(0, Stamina - 0.01f);
                    Cheered(0.04f);
                }
                break;
            case Verb.Eat:
                f.SetAction(Act.Eat);
                _nextBite -= World.Dt;
                if (_nextBite <= 0 && f.ActionT % 1.4f > 0.25f && f.ActionT % 1.4f < 0.4f)
                {
                    _nextBite = 1.2f;
                    it.BitesLeft--;
                    Hunger = MathF.Max(0, Hunger - 0.35f / it.Def.Bites * 2);
                    Cheered(0.05f + MathF.Max(0, f.Tastes.Of(Thing.Eating)) * 0.05f);
                    w.Fx.Dust(f.Jt[J.Head] + new Vector2(f.Facing * 3 * S, 2 * S), S * 0.4f, 2, 0.2f, w.Rng);
                    if (it.BitesLeft <= 0)
                    {
                        f.CarryingItem = null;
                        it.Holder = null;
                        w.RemoveItem(it);
                        f.Emote(f.Tastes.Likes(Thing.Eating) ? "♥" : "mmm", 1.2f);
                        _item = null;
                        Go(G.Idle, 1.5f);
                        return;
                    }
                }
                break;
            case Verb.Read:
                f.SetAction(Act.Read);
                if (rng.NextDouble() < World.Dt * 0.12) f.Emote(rng.NextDouble() < 0.5 ? "!" : "hm", 0.8f);
                Boredom = MathF.Max(0, Boredom - World.Dt * 0.02f);
                break;
            case Verb.Hide:
                // Curled up; now and then peek out over the edge.
                bool peek = (_t % 4.5f) > 3.6f;
                f.SetAction(peek ? Act.SitFloor : Act.Curl);
                if (peek) f.LookAt = w.Cursor;
                if (Fear > 0.4f || HunterAround(w)) _dur = MathF.Max(_dur, _t + 3);
                break;
            case Verb.Dance:
                FaceTo(it.Pos.X + (f.Base.X > it.Pos.X ? 400 : -400) * S);
                if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove);
                Cheered(World.Dt * 0.03f);
                if (!it.Playing) done = true;
                break;
            case Verb.Warm:
                FaceTo(it.Pos.X);
                f.SetAction(Act.Warm);
                Stamina = MathF.Min(1, Stamina + World.Dt * 0.008f);
                Sadness = MathF.Max(0, Sadness - World.Dt * 0.01f);
                break;
            case Verb.Stand:
                f.SetAction(Act.Stand);
                break;
        }
        if (done) { LeaveItem(); Go(G.Idle, rng.Range(0.8f, 2)); }
    }

    /// <summary>Stop using the object: get up, put things down, free the seat.</summary>
    void LeaveItem()
    {
        var it = _item;
        _item = null;
        if (it == null) return;
        for (int i = 0; i < it.Seated.Length; i++) if (it.Seated[i] == f) it.Seated[i] = null;
        if (it.User == f) it.User = null;
        if (f.CarryingItem == it)
        {
            f.CarryingItem = null;
            it.Holder = null;
            it.Open = false;
            it.Vel = new Vector2(f.Facing * 60 * S, -150 * S);
            it.OnGround = false;
        }
        if (f.Action is Act.SitFront or Act.SitBack or Act.SitEdge or Act.Lie or Act.Curl or Act.Eat or Act.Read or Act.Warm) f.SetAction(Act.Stand);
        // Get down off seats and beds onto the floor below.
        if (f.Grounded && (long)f.GroundHwnd < 0) f.HopOff();
    }

    /// <summary>An object we're using was grabbed, eaten by someone else, or removed.</summary>
    public void OnItemGone(Item it)
    {
        if (f.CarryingItem == it) { f.CarryingItem = null; it.Holder = null; it.Open = false; }
        if (f.Weapon == it) { f.Weapon = null; f.AimAt = null; it.Holder = null; }
        if (_item != it) return;
        LeaveItem();
        if (_g == G.UseItem) { f.Emote("?", 1); Go(G.Idle, 1); }
    }
}
