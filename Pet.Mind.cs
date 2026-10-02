using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Decisions and behaviour. Every few seconds a pet weighs what it could do (eat, drink, find the litter
/// box or ask for a walk, sleep, ask you for attention, play, groom, sniff, perch, follow its favourite figure, stalk a
/// bird, chase the cat, knock something off a ledge…) from its needs, its species and age, and its training, and then
/// does it properly: walking there by the route map, using the thing, and asking you (with a thought bubble and the
/// right noises) when what it needs isn't there.</summary>
sealed partial class Pet
{
    enum State
    {
        Idle, Wander, Travel, Sit, Sleep, Petted, Eat, Drink, Potty, Accident, Guilty, Ask, Play, Chase, Fetch, Pounce, Zoomies,
        Groom, Stretch, Sniff, Follow, Stalk, PounceBird, ChasePet, Flee, Hide, Scuffle, Startled, Bath, Perch, Knock, Scratch, Noise, Trick, Walk, Wheel,
    }

    enum Pose { Stand, Sit, Lie, Curl, Crouch, Air, HeadDown, Squat, LegLift, Groom, Stretch, PlayBow, Belly, Arch, Cower, Upright, Shake, Perch, Fluff }

    State _st = State.Idle;
    Pose _pose = Pose.Stand;
    float _t, _dur = 1, _petT, _sleepZ, _senseT;
    Vector2? _target;
    Prop? _ball;
    Item? _thing;
    Pet? _other;
    Figure? _perchFig;
    Func<Vector2?>? _travelTo;
    Action? _onArrive;
    float _travelPace = 1, _travelWithin = 8;

    public bool Asleep => _st == State.Sleep;
    public string Activity => Held ? "Being held by you" : OnCursor ? "Riding on your cursor" : _st switch
    {
        State.Sleep => _other != null ? $"Curled up with {_other.Name}" : "Napping", State.Follow => Owner != null ? $"Following {Owner.Name}" : "Following someone",
        State.Chase => "Chasing a ball", State.Fetch => "Bringing the ball back", State.Pounce => "Pouncing at your cursor", State.Petted => "Being petted",
        State.Sit => "Sitting", State.Wander => "Wandering", State.Eat => "Eating", State.Drink => "Drinking", State.Potty => "Busy (bathroom)",
        State.Accident => "Having an accident", State.Guilty => "Looking guilty", State.Ask => Want switch
        {
            PetNeed.Food => "Asking for food", PetNeed.Water => "Asking for water", PetNeed.Potty => "Needs a clean litter box", PetNeed.Walk => "Needs to go out (leash!)",
            PetNeed.Attention => "Wants your attention", PetNeed.Play => "Wants to play", _ => "Asking for something",
        },
        State.Play => "Playing", State.Zoomies => "Has the zoomies!", State.Groom => "Grooming", State.Stretch => "Stretching", State.Sniff => "Sniffing around",
        State.Stalk => _other != null ? $"Stalking {_other.Name}…" : "Stalking", State.PounceBird => "Pouncing!", State.ChasePet => _other != null ? $"Chasing {_other.Name}" : "Chasing",
        State.Flee => "Running away", State.Hide => "Hiding", State.Scuffle => _scuffleWith != null ? $"Fighting with {_scuffleWith.Name}!" : "Fighting!",
        State.Startled => "Startled", State.Bath => "Having a bath", State.Perch => _perchFig != null ? $"Perched on {_perchFig.Name}" : "Perched",
        State.Knock => "Knocking something off a ledge…", State.Scratch => _thing?.Def.Key == "scratchpost" ? "Using the scratching post" : $"Scratching the {_thing?.Def.Name.ToLowerInvariant()}",
        State.Noise => Kind switch { PetKind.Dog => "Barking", PetKind.Cat => "Yowling", _ => "Screeching" }, State.Trick => _trick.Length > 0 ? $"Doing a trick: {_trick}" : "Doing a trick", State.Walk => "On a walk", State.Wheel => "Running on the wheel",
        State.Travel => "On the way", _ => "Hanging out",
    };

    void InitMind() => InitTraining();

    void Go(State s, float dur)
    {
        if (_st == State.Sleep && s != State.Sleep && s != State.Stretch && Energy > 0.4f && _rng.NextDouble() < 0.5) { _st = State.Stretch; _t = 0; _dur = 1.2f; _after = (s, dur); return; }
        _st = s;
        _t = 0;
        _dur = dur;
        _route = null;
        _target = null;
        _pose = Pose.Stand;
        if (s != State.Petted) _petT = 0;
        if (s != State.Ask) Want = PetNeed.None;
        if (s != State.Perch) _perchFig = null;
        if (s != State.Scuffle) _scuffleWith = null;
        if (s != State.Sleep && s != State.Stalk && s != State.ChasePet && s != State.Flee) _other = null;
    }

    (State s, float d)? _after;

    void GoIdle(float dur) => Go(State.Idle, dur);

    /// <summary>Go somewhere (re-aiming as the target moves), then do something.</summary>
    void Travel(Func<Vector2?> to, float pace, float within, float timeout, Action then)
    {
        Go(State.Travel, timeout);
        _travelTo = to;
        _travelPace = pace;
        _travelWithin = within;
        _onArrive = then;
    }

    void Think(World w, float dt)
    {
        if (PuppetStep(w, dt)) return;
        _t += dt;
        _t0 += dt;
        _leashTug = MathF.Max(0, _leashTug - dt);
        _ownerCheck -= dt;
        if (_ownerCheck <= 0) { _ownerCheck = 1; Bonding(w, 1); SocialTick(w, 1); }
        if (OnCursor) { _pose = Pose.Perch; if (_t > _dur) { _t = 0; _dur = _rng.Range(4, 9); if (_rng.NextDouble() < 0.5) Chatter(w); } UserBond = MathF.Min(1, UserBond + dt * 0.002f); Attention = MathF.Max(0, Attention - dt * 0.01f); return; }
        if (!Grounded && !Flying) { _pose = Pose.Air; return; }
        _senseT -= dt;
        if (_senseT <= 0) { _senseT = 0.3f; Senses(w); }
        var cur = w.Cursor;
        // Being stroked by you.
        float cv = w.CursorVel.Length();
        bool stroked = Grounded && Vector2.Distance(cur, Centre) < MathF.Max(Length, 12 * S) * 0.7f && cv > 15 * S && cv < 260 * S;
        if (stroked && _st is not (State.Scuffle or State.Potty or State.Accident or State.Eat))
        {
            _petT += dt;
            if (_st != State.Petted && _petT > 0.3f) { Go(State.Petted, 2.5f); }
            UserBond = M.Clamp01(UserBond + dt * 0.03f);
            Attention = MathF.Max(0, Attention - dt * 0.06f);
            Stress = MathF.Max(0, Stress - dt * 0.03f);
        }
        if (Leashed && _st is State.Idle or State.Wander or State.Follow or State.Sit or State.Travel or State.Ask or State.Chase or State.Play or State.Zoomies or State.Perch)
            Go(State.Walk, 9999);
        switch (_st)
        {
            case State.Petted:
                Vel.X = 0;
                _pose = Kind == PetKind.Dog && UserBond > 0.5f && _t > 1 ? Pose.Belly : Kind == PetKind.Parrot ? Pose.Fluff : Pose.Sit;
                if (stroked) _dur = MathF.Max(_dur, _t + 1);
                if (_soundCd <= 0)
                {
                    _soundCd = Kind == PetKind.Cat ? 1.1f : 2.5f;
                    World.Play(Kind switch { PetKind.Cat => Sfx.Purr, PetKind.Dog => Sfx.Bark, _ => Sfx.Chirp }, Pos, Kind == PetKind.Cat ? 0.35f : 0.12f, Kind == PetKind.Dog ? 1.5f : 1, 0.5);
                }
                if (_t > _dur) GoIdle(1);
                return;
            case State.Sleep:
                Vel.X = 0;
                _pose = Kind == PetKind.Parrot ? Pose.Fluff : Pose.Curl;
                _sleepZ += dt;
                if (_other != null && (!w.Pets.Contains(_other) || _other._st != State.Sleep)) _other = null;
                if (_t > _dur || (Energy > 0.98f && _t > 10)) GoIdle(1.5f);
                return;
            case State.Stretch:
                Vel.X = 0;
                _pose = Pose.Stretch;
                if (_t > _dur) { if (_after is { } a) { _after = null; Go(a.s, a.d); } else GoIdle(0.5f); }
                return;
            case State.Sit:
                Vel.X = 0;
                _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Sit;
                if (_t > _dur) GoIdle(0.5f);
                return;
            case State.Travel:
            {
                var to = _travelTo?.Invoke();
                if (to == null || _t > _dur) { GoIdle(1); return; }
                if (MoveTo(w, to.Value, _travelPace, _travelWithin)) { var then = _onArrive; _onArrive = null; then?.Invoke(); if (_st == State.Travel) GoIdle(1); }
                return;
            }
            case State.Wander:
                if (_target is Vector2 tw && MoveTo(w, tw, Kind == PetKind.Parrot ? 1 : 0.7f)) GoIdle(_rng.Range(1, 3));
                if (_t > _dur) GoIdle(1);
                return;
            case State.Eat: DoEat(w, dt); return;
            case State.Drink: DoDrink(w, dt); return;
            case State.Potty: DoPotty(w); return;
            case State.Accident:
                Vel.X = 0;
                if (_t > _dur) { if (Kind == PetKind.Dog) { Go(State.Guilty, 4); } else GoIdle(1); }
                return;
            case State.Guilty:
                Vel.X = 0;
                _pose = Pose.Cower;
                Facing = cur.X >= Pos.X ? 1 : -1;
                if (_t > _dur) GoIdle(1);
                return;
            case State.Ask: DoAsk(w, dt); return;
            case State.Play: DoPlay(w, dt); return;
            case State.Chase:
            {
                if (_ball == null || !w.Props.Contains(_ball) || _ball.Holder != null || Mouth != null) { GoIdle(1); return; }
                if (Vector2.Distance(_ball.Pos, HeadPos) < 12 * S + _ball.Radius && _ball.Vel.Length() < 500 * S)
                {
                    Mouth = _ball;
                    _ball.Pinned = false;
                    World.Play(Sfx.Bark, Pos, 0.25f, 1.3f, 0.5);
                    Go(State.Fetch, 20);
                    return;
                }
                MoveTo(w, _ball.Pos with { Y = _ball.OnGround ? _ball.Pos.Y + _ball.Radius : _ball.Pos.Y }, 1.6f);
                Boredom = MathF.Max(0, Boredom - dt * 0.02f);
                if (_t > _dur) GoIdle(1);
                return;
            }
            case State.Fetch:
            {
                Vector2 to = Owner != null && w.Figures.Contains(Owner) && (UserBond < BondWith(Owner) || UserBond < 0.4f) ? Owner.Base : cur;
                if (Mouth == null) { GoIdle(1); return; }
                if (MoveTo(w, to, 1.4f, 30 * S) || _t > _dur)
                {
                    var b = Mouth;
                    Mouth = null;
                    b.Vel = new Vector2(Facing * 60 * S, -100 * S);
                    if (Owner != null && w.Figures.Contains(Owner) && Vector2.Distance(Owner.Base, Pos) < 80 * S) Owner.Brain.OnPetBroughtBall(this, b, w);
                    Go(State.Sit, 2.5f);
                    Boredom = MathF.Max(0, Boredom - 0.15f);
                }
                return;
            }
            case State.Pounce:
            {
                if (_t > _dur) { GoIdle(1); return; }
                float dx = cur.X - Pos.X;
                Facing = dx >= 0 ? 1 : -1;
                _pose = Pose.Crouch;
                if (MathF.Abs(dx) > 70 * S) MoveTo(w, new Vector2(cur.X, Pos.Y), 1.3f, 50 * S);
                else if (_t > 0.6f && Grounded && cur.Y < Pos.Y && cur.Y > Pos.Y - 200 * S)
                {
                    Vel = new Vector2(dx * 2.4f, -MathF.Sqrt(2 * PetG * MathF.Max(30 * S, Pos.Y - cur.Y + 10 * S)));
                    Grounded = false;
                    _t = 0;
                    _dur = 2;
                    Boredom = MathF.Max(0, Boredom - 0.08f);
                }
                else Vel.X = 0;
                return;
            }
            case State.Zoomies:
            {
                var seg = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
                if (seg == null || _t > _dur) { GoIdle(1); return; }
                _target ??= new Vector2(Facing > 0 ? seg.X2 - 10 * S : seg.X1 + 10 * S, seg.Y);
                if (MoveTo(w, _target.Value, 2.4f, 12 * S)) _target = new Vector2(Pos.X < (seg.X1 + seg.X2) / 2 ? seg.X2 - 10 * S : seg.X1 + 10 * S, seg.Y);
                if (_rng.NextDouble() < dt * 0.6 && Grounded) { Vel.Y = -380 * S; Grounded = false; }
                Boredom = MathF.Max(0, Boredom - dt * 0.05f);
                return;
            }
            case State.Groom:
                Vel.X = 0;
                _pose = Kind == PetKind.Parrot ? Pose.Fluff : Pose.Groom;
                Stress = MathF.Max(0, Stress - dt * 0.01f);
                if (_t > _dur) GoIdle(0.5f);
                return;
            case State.Sniff:
                Vel.X = 0;
                _pose = Pose.HeadDown;
                Boredom = MathF.Max(0, Boredom - dt * 0.01f);
                if (_t > _dur) GoIdle(0.3f);
                return;
            case State.Follow:
            {
                if (Owner == null || Owner.Dead || !w.Figures.Contains(Owner)) { GoIdle(1); return; }
                if (Kind == PetKind.Parrot) { PerchOn(Owner, w); return; }
                float gap = MathF.Abs(Owner.Base.X - Pos.X) + MathF.Abs(Owner.Base.Y - Pos.Y);
                if (gap > 90 * S) MoveTo(w, Owner.Base + new Vector2(-Owner.Facing * 30 * S, 0), gap > 300 * S ? 1.7f : 1.1f, 25 * S);
                else Vel.X = 0;
                Attention = MathF.Max(0, Attention - dt * 0.004f);
                if (_t > _dur) GoIdle(1);
                return;
            }
            case State.Perch: DoPerch(w, dt); return;
            case State.Stalk: DoStalk(w, dt); return;
            case State.PounceBird: DoPounceBird(w); return;
            case State.ChasePet: DoChasePet(w, dt); return;
            case State.Flee: DoFlee(w); return;
            case State.Hide:
                Vel.X = 0;
                _pose = Pose.Cower;
                if (_t > _dur && Stress < 0.5f) GoIdle(1);
                else if (_t > _dur + 20) GoIdle(1);
                return;
            case State.Scuffle: DoScuffle(w, dt); return;
            case State.Startled:
                if (Grounded && _t < 0.1f) { Vel = new Vector2(-Facing * 200 * S, -420 * S); Grounded = false; }
                _pose = _t > 0.5f ? Pose.Shake : Pose.Air;
                if (Grounded) Vel.X = 0;
                if (_t > _dur) { if (Kind == PetKind.Cat && _rng.NextDouble() < 0.5) { Go(State.Groom, 3); } else GoIdle(1); }
                return;
            case State.Bath:
                Vel.X = 0;
                _pose = Pose.Fluff;
                if (_t > _dur) { Chirp(w); GoIdle(1); }
                return;
            case State.Knock: DoKnock(w, dt); return;
            case State.Scratch:
                Vel.X = 0;
                _pose = Pose.Upright;
                if (_thing != null) Facing = _thing.Pos.X >= Pos.X ? 1 : -1;
                if ((int)(_t * 3) != (int)((_t - dt) * 3)) World.Play(Sfx.Scribble, Pos, 0.15f, 1.6f, 0.2);
                Boredom = MathF.Max(0, Boredom - dt * 0.03f);
                Stress = MathF.Max(0, Stress - dt * 0.02f);
                if (_t > _dur) GoIdle(1);
                return;
            case State.Noise:
                Vel.X = 0;
                _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Stand;
                if (_soundCd <= 0) { _soundCd = Kind == PetKind.Dog ? 0.55f : 1.2f; Say(w, true); Shout(Kind switch { PetKind.Dog => "woof!", PetKind.Cat => "mrrraow!", _ => "SQUAWK!" }); }
                if (_t > _dur) GoIdle(1);
                return;
            case State.Trick:
                DoTrickPose(dt);
                if (_t > _dur) GoIdle(0.5f);
                return;
            case State.Wheel: DoWheel(w, dt); return;
            case State.Walk: DoWalk(w, dt); return;
            default:
                Vel.X = 0;
                _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Stand;
                if (_t > _dur) Decide(w);
                return;
        }
    }

    /// <summary>Things it reacts to right away: a cat creeping up on a parrot, a dog bearing down on a cat, thunder.</summary>
    void Senses(World w)
    {
        if (_st is State.Scuffle or State.Startled or State.Flee) return;
        foreach (var o in w.Pets)
        {
            if (o == this || o.Held) continue;
            float d = Vector2.Distance(o.Pos, Pos);
            // A parrot spots a cat coming (an alert one sooner).
            if (Kind == PetKind.Parrot && o.Kind == PetKind.Cat && !Flying && d < (o._st == State.Stalk ? 260 : 150) * _s && o._st != State.Sleep && _rng.NextDouble() < 0.5 + Fear(o) * 0.5)
            {
                Afraid(o, 0.08f);
                FleeFrom(o, w);
                return;
            }
            // Small and furry: a cat creeping up is reason enough to bolt.
            if (Small && o.Kind == PetKind.Cat && o._st is State.Stalk or State.PounceBird && o._other == this && d < 200 * _s && _rng.NextDouble() < 0.6) { DefendOrFlee(o, w); return; }
            // Being chased.
            if (o._st == State.ChasePet && o._other == this && !o._playful && d < 260 * _s) { DefendOrFlee(o, w); return; }
        }
    }

    void Decide(World w)
    {
        var opts = new List<(float W, string Label, Action Do)>();
        void Add(float wgt, string label, Action a) { if (wgt > 0.01f) opts.Add((wgt, label, a)); }
        bool cat = Kind == PetKind.Cat, dog = Kind == PetKind.Dog, bird = Kind == PetKind.Parrot;
        if (Small) SmallOptions(w, (wt, l, a) => Add(wt, l, a));
        // The young stay close to their mother.
        if (Young && Mother(w) is { } mum && Vector2.Distance(mum.Pos, Pos) > 120 * _s && !mum.Held)
            Add(1.6f * (1 - Age), "follow mum", () => Travel(() => w.Pets.Contains(mum) ? mum.Pos + new Vector2(-mum.Facing * mum.Length * 0.7f, 0) : null, 1.3f, 14 * _s, 15, () => Go(State.Sit, _rng.Range(2, 5))));
        float pot = MathF.Max(Bladder, Bowel);
        if (!bird && pot > 0.5f) Add(pot * pot * 7, "bathroom", () => GoPotty(w));
        if (Thirst > 0.45f) Add(Thirst * Thirst * 5, "drink", () => GoDrink(w));
        if (Hunger > 0.5f) Add(Hunger * Hunger * 5, "eat", () => GoEat(w));
        // Free-feeding: a full bowl is hard to resist (especially for dogs), hungry or not.
        else if (Hunger > 0.15f && w.Items.Any(i => i.Def.Key == "foodbowl" && i.Fill > 0.5f && Vector2.Distance(i.Pos, Pos) < 1500 * _s)) Add(dog ? 0.7f : 0.2f, "graze", () => GoEat(w));
        float tired = 1 - Energy;
        if (Energy < (cat ? 0.75f : 0.5f)) Add(tired * tired * 4 * (cat ? 1.5f : 1) * (Young ? 1.6f : 1) * (Kind == PetKind.Hamster ? 1 : 1 + w.Night) * Tm(Temperament.Lazy, 1.6f), "sleep", () => GoSleep(w));
        if (Attention > 0.4f) Add(Attention * Attention * 3.5f * Tm(Temperament.Affectionate, 1.5f) * Tm(Temperament.Shy, 0.6f), "attention", () => SeekAttention(w));
        if (Boredom > 0.25f && !Small) Add(Boredom * 1.8f * (0.3f + Energy) * (Young ? 1.6f : 1) * Tm(Temperament.Playful, 1.6f) * Tm(Temperament.Lazy, 0.6f), "play", () => GoPlay(w));
        if (Stress > 0.5f) Add(Stress * 2.5f, "hide", () => GoHide(w));
        if (cat) Add(0.3f, "groom", () => Go(State.Groom, _rng.Range(3, 7)));
        if (cat && Nearest(w, "fishtank") is { } tank) Add(0.25f + Boredom * 0.9f, "watch the fish", () => Travel(() => w.Items.Contains(tank) ? Beside(tank) : null, 1, 6 * S, 20, () => { Go(State.Sit, _rng.Range(8, 20)); _thing = tank; Boredom = MathF.Max(0, Boredom - 0.3f); Log("Watched the fish, intently"); }));
        if (dog) Add(0.35f, "sniff", () => Go(State.Sniff, _rng.Range(1.5f, 3.5f)));
        if (bird) { Add(0.9f, "perch", () => PerchSomewhere(w)); Add(0.25f + Boredom * 0.6f, "chatter", () => Chatter(w)); Add(0.2f, "preen", () => Go(State.Groom, _rng.Range(3, 6))); }
        if (Energy > 0.75f && !bird && Kind != PetKind.Hamster) Add((Young ? 0.5f : 0.12f) * Energy * Tm(Temperament.Playful, 2) * Tm(Temperament.Lazy, 0.3f), "zoomies", () => { Go(State.Zoomies, _rng.Range(4, 8)); Shout(cat ? "!!" : "woof!"); });
        // Temptations (training holds them back).
        // (A hunt takes it out of them: a while between tries.)
        if ((cat || dog) && _t0 > _huntAgain) foreach (var bp in w.Pets.Where(p => (p.Kind == PetKind.Parrot || (cat && p.Kind == PetKind.Hamster)) && p != this && !p.Flying && !p.OnCursor && !p.Held && Vector2.Distance(p.Pos, Pos) < 700 * _s))
            { if (Tempted(Habit.ChaseBirds, cat ? 0.45f : 0.2f)) Add(cat ? 1.3f : 0.6f, "stalk", () => { _huntAgain = _t0 + _rng.Range(90, 240); BeginStalk(bp, w); }); break; }
        if (dog) foreach (var cp in w.Pets.Where(p => p.Kind is PetKind.Cat or PetKind.Rabbit && Vector2.Distance(p.Pos, Pos) < 600 * _s && PetBond(p) < 0.4f && !p.Held))
            { if (Tempted(Habit.ChasePets, 0.4f * Energy)) Add(1.4f, "chase cat", () => BeginChase(cp, w)); break; }
        if (cat && Boredom > 0.45f && Attention > 0.4f && Tempted(Habit.KnockingThings, 0.5f)) Add(1.2f, "knock", () => BeginKnock(w));
        if (cat && Tempted(Habit.Scratching, 0.15f + Boredom * 0.25f) || cat && w.Items.Any(i => i.Def.Key == "scratchpost") && _rng.NextDouble() < 0.15) Add(0.7f, "scratch", () => BeginScratch(w));
        if (!cat && Attention > 0.6f && Boredom > 0.5f && Tempted(Habit.Noise, 0.35f)) Add(0.9f, "noise", () => { Go(State.Noise, _rng.Range(2, 4)); Misdeed(Habit.Noise, w); Log(dog ? "Barked and barked" : "Screeched for ages"); });
        if (cat && w.Night > 0.6f && Attention > 0.7f && Tempted(Habit.Noise, 0.25f)) Add(0.6f, "yowl", () => { Go(State.Noise, 3); Misdeed(Habit.Noise, w); Log("Yowled in the night"); });
        // Friends.
        if (Owner != null && w.Figures.Contains(Owner) && !Owner.Dead) Add((cat ? 0.35f : bird ? 0.5f : 0.8f) * (0.4f + BondWith(Owner)), "follow", () => Go(State.Follow, _rng.Range(8, 20)));
        foreach (var o in w.Pets) if (o != this && PetBond(o) > 0.45f && !o.Held && Vector2.Distance(o.Pos, Pos) < 900 * _s && !bird && o.Kind != PetKind.Parrot)
            { Add(0.4f * PetBond(o) * (0.4f + Boredom), "play-chase", () => BeginChase(o, w, play: true)); break; }
        Add(0.45f, "sit", () => Go(State.Sit, _rng.Range(2, 6)));
        Add(bird ? 0.2f : 0.6f, "wander", () => WanderNear(w));
        if (opts.Count == 0) { GoIdle(1); return; }
        float total = 0;
        foreach (var o in opts) total += MathF.Pow(o.W, 1.3f);
        float roll = (float)_rng.NextDouble() * total;
        foreach (var o in opts) { roll -= MathF.Pow(o.W, 1.3f); if (roll <= 0) { LastChoice = o.Label; o.Do(); return; } }
        opts[^1].Do();
    }

    public string LastChoice = "";
    float _t0, _huntAgain = 30;

    void WanderNear(World w)
    {
        if (Kind == PetKind.Parrot) { PerchSomewhere(w); return; }
        var seg = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
        if (seg == null) { GoIdle(1); return; }
        Go(State.Wander, 8);
        _target = new Vector2(M.ClampIn(Pos.X + _rng.Range(-300, 300) * S, seg.X1 + 10 * S, seg.X2 - 10 * S), seg.Y);
    }

    // ---------------- food and water ----------------

    void GoEat(World w)
    {
        var bowl = Nearest(w, "foodbowl");
        if (bowl == null) { AskFor(PetNeed.Food, w, null); return; }
        Travel(() => w.Items.Contains(bowl) ? Beside(bowl) : null, Hunger > 0.8f ? 1.5f : 1, 6 * S, 30, () =>
        {
            _thing = bowl;
            if (bowl.Fill < 0.03f) { AskFor(PetNeed.Food, w, bowl); return; }
            // Someone else is eating here.
            var rival = w.Pets.FirstOrDefault(o => o != this && o._st == State.Eat && o._thing == bowl);
            if (rival != null && FoodSquabble(rival, w)) return;
            Go(State.Eat, 12);
        });
    }

    /// <summary>Wanting a seat or a bed and finding none: grumpy.</summary>
    public void Frustrated(float amount, string why) { Frustration = MathF.Min(1, Frustration + amount); Log(why); }

    void DoEat(World w, float dt)
    {
        var bowl = _thing;
        if (bowl == null || !w.Items.Contains(bowl) || bowl.Holder != null) { GoIdle(1); return; }
        Vel.X = 0;
        Facing = bowl.Pos.X >= Pos.X ? 1 : -1;
        _pose = Kind == PetKind.Parrot ? Pose.HeadDown : Pose.HeadDown;
        if (bowl.Fill <= 0.01f) { if (Hunger > 0.35f) AskFor(PetNeed.Food, w, bowl); else GoIdle(1); return; }
        float bite = dt * 0.09f;
        Ate(bite);
        Hunger = MathF.Max(0, Hunger - bite);
        bowl.Fill = MathF.Max(0, bowl.Fill - bite * 0.35f * (Kind == PetKind.Parrot ? 0.4f : 1));
        if ((int)(_t * 1.3f) != (int)((_t - dt) * 1.3f)) World.Play(Kind == PetKind.Parrot ? Sfx.Chirp : Sfx.Munch, HeadPos, 0.18f, 1.8f / MathF.Sqrt(S / _s), 0.3);
        if (Hunger < 0.03f || _t > _dur)
        {
            Log(Hunger < 0.1f ? "Ate a full meal" : "Had a snack");
            if (Kind == PetKind.Hamster) _pouch = 1;
            Thirst = MathF.Min(1, Thirst + 0.08f);
            GoIdle(1);
            if (Kind == PetKind.Cat && _rng.NextDouble() < 0.6) Go(State.Groom, 4);
        }
    }

    void GoDrink(World w)
    {
        var bowl = Nearest(w, "waterbowl");
        if (bowl == null) { AskFor(PetNeed.Water, w, null); return; }
        Travel(() => w.Items.Contains(bowl) ? Beside(bowl) : null, Thirst > 0.8f ? 1.5f : 1, 6 * S, 30, () =>
        {
            _thing = bowl;
            if (bowl.Fill < 0.03f) { AskFor(PetNeed.Water, w, bowl); return; }
            Go(State.Drink, 8);
        });
    }

    void DoDrink(World w, float dt)
    {
        var bowl = _thing;
        if (bowl == null || !w.Items.Contains(bowl) || bowl.Holder != null) { GoIdle(1); return; }
        Vel.X = 0;
        Facing = bowl.Pos.X >= Pos.X ? 1 : -1;
        _pose = Pose.HeadDown;
        if (bowl.Fill <= 0.01f) { if (Thirst > 0.35f) AskFor(PetNeed.Water, w, bowl); else GoIdle(1); return; }
        float sip = dt * 0.14f;
        Thirst = MathF.Max(0, Thirst - sip);
        bowl.Fill = MathF.Max(0, bowl.Fill - sip * 0.18f);
        if ((int)(_t * 3) != (int)((_t - dt) * 3)) World.Play(Sfx.Lap, HeadPos, 0.12f, 1.2f, 0.2);
        if (Thirst < 0.03f || _t > _dur) { Log("Had a drink"); Bladder = MathF.Min(1, Bladder + 0.08f); GoIdle(1); }
    }

    // ---------------- the bathroom ----------------

    void GoPotty(World w)
    {
        float pot = MathF.Max(Bladder, Bowel);
        // Not house-trained yet: it just goes, wherever it is.
        if (_rng.NextDouble() < (1 - HouseScore) * 0.6f && pot > 0.55f && Accidents) { Accident(w); return; }
        if (Kind is PetKind.Cat or PetKind.Rabbit)
        {
            var box = Nearest(w, "litterbox");
            if (box == null) { AskFor(PetNeed.Potty, w, null); return; }
            Travel(() => w.Items.Contains(box) ? box.Pos : null, pot > 0.85f ? 1.6f : 1.1f, 5 * S, 30, () =>
            {
                // Cats are fussy: a dirty box gets turned down (the dirtier, the likelier).
                if (box.Dirt >= 3 && _rng.NextDouble() < (box.Dirt - 2) / 3f) { Log("Refused to use the dirty litter box"); AskFor(PetNeed.Potty, w, box); return; }
                _thing = box;
                Go(State.Potty, 4.5f);
            });
            return;
        }
        // Dogs: a pee pad if there is one, otherwise they need you to take them out.
        if (Leashed) { _thing = null; Go(State.Potty, 3); return; }
        var pad = Nearest(w, "peepad", i => i.Dirt < 4);
        if (pad != null && (pot > 0.8f || Young || _rng.NextDouble() < 0.5))
        {
            Travel(() => w.Items.Contains(pad) ? pad.Pos : null, pot > 0.85f ? 1.6f : 1.1f, 6 * S, 30, () => { _thing = pad; Go(State.Potty, 3); });
            return;
        }
        AskFor(PetNeed.Walk, w, null);
    }

    void DoPotty(World w)
    {
        Vel.X = 0;
        bool pee = Bladder >= Bowel || Kind == PetKind.Cat && Bladder > 0.3f;
        _pose = Kind == PetKind.Dog && pee && Age > 0.5f ? Pose.LegLift : Pose.Squat;
        if (_t < _dur) return;
        bool both = Kind == PetKind.Cat;
        if (pee || both) Bladder = 0;
        if (!pee || both || Bowel > 0.5f) Bowel = 0;
        string where;
        if (_thing is { } box && w.Items.Contains(box))
        {
            box.Dirt++;
            if (box.Def.Key == "litterbox") { w.Fx.Dust(box.Pos, S * 0.6f, 4, 0.3f, w.Rng); World.Play(Sfx.Scribble, box.Pos, 0.15f, 1.4f); }
            where = box.Def.Key == "litterbox" ? "Used the litter box" : "Used the pee pad";
        }
        else
        {
            where = "Did its business on a walk";
            // On a walk, poop is left behind: pick it up!
            if (!pee && w.MakeItem?.Invoke("poop") is { } mess) { mess.Pos = new Vector2(Pos.X - Facing * Length * 0.3f, Pos.Y); mess.Vel = Vector2.Zero; mess.OnGround = false; where += " (pick it up!)"; }
        }
        Log(where);
        GoodDeed(PetSkill.Housetrained);
        GoIdle(1);
    }

    // ---------------- asking you ----------------

    float _askBeat;

    void AskFor(PetNeed need, World w, Item? at)
    {
        Want = need;
        if (at != null)
        {
            Go(State.Ask, 25);
            Want = need;
            _thing = at;
            return;
        }
        // Come and find you.
        var under = w.Env.Below(w.Cursor.X, w.Cursor.Y - 2 * S);
        Vector2 spot = under != null ? new Vector2(M.ClampIn(w.Cursor.X + (Pos.X < w.Cursor.X ? -40 : 40) * S, under.X1 + 10 * S, under.X2 - 10 * S), under.Y) : Pos;
        if (Kind == PetKind.Parrot && under != null) spot = new Vector2(w.Cursor.X + 40 * S, under.Y);
        Travel(() => spot, 1.2f, 20 * S, 20, () => { Go(State.Ask, 25); Want = need; _thing = null; });
        Want = need;
    }

    void DoAsk(World w, float dt)
    {
        Vel.X = 0;
        _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Sit;
        Facing = (_thing != null ? _thing.Pos.X : w.Cursor.X) >= Pos.X ? 1 : -1;
        // Fixed? Go and use it.
        bool sorted = Want switch
        {
            PetNeed.Food => Hunger < 0.2f || w.Items.Any(i => i.Def.Key == "foodbowl" && i.Fill > 0.1f),
            PetNeed.Water => Thirst < 0.2f || w.Items.Any(i => i.Def.Key == "waterbowl" && i.Fill > 0.1f),
            PetNeed.Potty => MathF.Max(Bladder, Bowel) < 0.3f || w.Items.Any(i => i.Def.Key == "litterbox" && i.Dirt < 3),
            PetNeed.Walk => MathF.Max(Bladder, Bowel) < 0.3f || Leashed,
            PetNeed.Attention => Attention < 0.15f,
            _ => _t > 6,
        };
        if (sorted && _t > 0.5f)
        {
            var need = Want;
            GoIdle(0.3f);
            if (need == PetNeed.Food && Hunger > 0.2f) GoEat(w);
            else if (need == PetNeed.Water && Thirst > 0.2f) GoDrink(w);
            else if (need is PetNeed.Potty or PetNeed.Walk && MathF.Max(Bladder, Bowel) > 0.3f) GoPotty(w);
            return;
        }
        _askBeat -= dt;
        if (_askBeat <= 0)
        {
            _askBeat = _rng.Range(2.5f, 5);
            if (Kind == PetKind.Dog && Want == PetNeed.Walk) World.Play(Sfx.Whine, Pos, 0.25f, 1.2f / MathF.Sqrt(S / _s));
            else Say(w, false);
        }
        if (_t > _dur) GoIdle(_rng.Range(2, 5));
    }

    void SeekAttention(World w)
    {
        if (Owner != null && w.Figures.Contains(Owner) && BondWith(Owner) > UserBond && _rng.NextDouble() < 0.6) { Go(State.Follow, 15); return; }
        AskFor(PetNeed.Attention, w, null);
    }

    // ---------------- sleep ----------------

    void GoSleep(World w)
    {
        // Snuggle up with a friend who's already asleep?
        var buddy = w.Pets.FirstOrDefault(o => o != this && o._st == State.Sleep && PetBond(o) > 0.45f && o.Kind != PetKind.Parrot && Kind != PetKind.Parrot && Vector2.Distance(o.Pos, Pos) < 700 * _s);
        if (buddy != null)
        {
            Travel(() => w.Pets.Contains(buddy) ? buddy.Pos + new Vector2((Pos.X < buddy.Pos.X ? -1 : 1) * buddy.Length * 0.7f, 0) : null, 0.8f, 6 * S, 20, () =>
            { Go(State.Sleep, _rng.Range(40, 100)); _other = buddy; PetBond(buddy, 0.05f); Log($"Curled up with {buddy.Name}"); });
            return;
        }
        if (Kind == PetKind.Parrot)
        {
            var perch = Nearest(w, "perch");
            if (perch != null) { FlyTo(w, PerchTop(perch), 4 * S); Travel(() => PerchTop(perch), 1, 5 * S, 20, () => Go(State.Sleep, _rng.Range(40, 120))); return; }
            Go(State.Sleep, _rng.Range(30, 90));
            return;
        }
        var bed = Nearest(w, "petbed", i => !w.Pets.Any(o => o != this && o._st == State.Sleep && o._thing == i));
        Item? soft = bed ?? (Kind == PetKind.Cat ? w.Items.FirstOrDefault(i => i.OnGround && i.Free && (i.Def.Verbs.Contains(Verb.Lie) || i.Def.Verbs.Contains(Verb.Sit)) && Vector2.Distance(i.Pos, Pos) < 900 * _s) : null);
        if (soft != null && _rng.NextDouble() < 0.85)
        {
            Vector2 Top() => soft.Def.Key == "petbed" ? soft.Pos : new Vector2(soft.Pos.X, soft.Pos.Y - (soft.Def.SeatY > 0 ? soft.Def.SeatY : soft.Def.H) * soft.Sc);
            Travel(() => w.Items.Contains(soft) ? Top() : null, 0.9f, 6 * S, 25, () =>
            {
                _thing = soft;
                // Cats knead first; dogs turn round a couple of times.
                if (Kind == PetKind.Cat) { Go(State.Groom, 0); _pose = Pose.Upright; }
                Go(State.Sleep, _rng.Range(40, 120) * (Young ? 1.3f : 1));
                _thing = soft;
                if (Kind == PetKind.Dog) Facing = -Facing;
            });
            return;
        }
        // Nowhere comfy: it'll make do with the floor, but it isn't happy about it.
        Frustration = MathF.Min(1, Frustration + 0.12f);
        if (Kind == PetKind.Dog || Kind == PetKind.Cat) Log("No bed to sleep in, so it slept on the floor");
        _thing = null;
        Go(State.Sleep, _rng.Range(30, 80));
    }

    void GoHide(World w)
    {
        if (Kind == PetKind.Parrot) { PerchSomewhere(w, high: true); return; }
        var spot = w.Items.Where(i => i.OnGround && i.Free && (i.Def.Verbs.Contains(Verb.Hide) || i.Def.Verbs.Contains(Verb.Lie) || i.Def.Key == "petbed") && Vector2.Distance(i.Pos, Pos) < 1200 * _s)
                          .OrderBy(i => Vector2.Distance(i.Pos, Pos)).FirstOrDefault();
        if (spot == null) { Go(State.Hide, 8); return; }
        Travel(() => w.Items.Contains(spot) ? spot.Pos : null, 1.5f, 6 * S, 15, () => Go(State.Hide, _rng.Range(8, 16)));
    }

    // ---------------- play ----------------

    void GoPlay(World w)
    {
        bool cat = Kind == PetKind.Cat;
        // A dog and a loose ball.
        if (Kind == PetKind.Dog && Mouth == null)
        {
            var ball = w.Props.Where(p => p.Holder == null && !w.Matches.Any(m => m.Ball == p) && Vector2.Distance(p.Pos, Pos) < 1000 * _s && p.SizeMul < 2.2f).OrderBy(p => Vector2.Distance(p.Pos, Pos)).FirstOrDefault();
            if (ball != null && _rng.NextDouble() < 0.6) { Go(State.Chase, 12); _ball = ball; return; }
        }
        if (cat && w.CursorVel.Length() > 150 * _s && Vector2.Distance(w.Cursor, Pos) < 500 * _s && _rng.NextDouble() < 0.6) { Go(State.Pounce, 6); return; }
        var toy = Nearest(w, Kind == PetKind.Cat ? "yarn" : "chewtoy") ?? Nearest(w, "yarn") ?? Nearest(w, "chewtoy");
        if (toy != null)
        {
            Travel(() => w.Items.Contains(toy) ? Beside(toy) : null, 1.3f, 8 * S, 15, () => { _thing = toy; Go(State.Play, _rng.Range(6, 12)); });
            return;
        }
        if (Kind == PetKind.Parrot) { Chatter(w); return; }
        // Nothing to play with: ask you (and, failing that, the zoomies).
        if (_rng.NextDouble() < 0.5) AskFor(PetNeed.Play, w, null);
        else Go(State.Zoomies, _rng.Range(3, 6));
    }

    float _batAt;

    void DoPlay(World w, float dt)
    {
        var toy = _thing;
        if (toy == null || !w.Items.Contains(toy) || toy.Holder != null || _t > _dur) { GoIdle(1); return; }
        Boredom = MathF.Max(0, Boredom - dt * 0.05f);
        if (Kind == PetKind.Parrot)
        {
            _pose = Pose.HeadDown;
            if ((int)(_t * 2) != (int)((_t - dt) * 2)) World.Play(Sfx.Chirp, Pos, 0.1f, 1.6f, 0.2);
            return;
        }
        float dx = toy.Pos.X - Pos.X;
        Facing = dx >= 0 ? 1 : -1;
        if (MathF.Abs(dx) > toy.Def.W * toy.Sc * 0.5f + Length * 0.5f) { MoveTo(w, Beside(toy), 1.5f, 6 * S); _pose = Pose.Crouch; return; }
        Vel.X = 0;
        _pose = Pose.PlayBow;
        if (_t > _batAt)
        {
            _batAt = _t + _rng.Range(0.8f, 1.6f);
            toy.Vel = new Vector2(Facing * _rng.Range(120, 320) * S, -_rng.Range(80, 260) * S);
            toy.OnGround = false;
            if (Grounded && _rng.NextDouble() < 0.5) { Vel = new Vector2(Facing * 120 * S, -300 * S); Grounded = false; }
            World.Play(Kind == PetKind.Dog ? Sfx.Squeak : Sfx.Pip, toy.Pos, 0.15f, Kind == PetKind.Dog ? 1 : 2.2f, 0.2);
        }
    }

    // ---------------- cat mischief ----------------

    void BeginKnock(World w)
    {
        // Something small, sitting on a window ledge (not the floor), within reach.
        var target = w.Items.Where(i => i.OnGround && i.Holder == null && (i.Def.Carry || i.Def.W * i.Sc < 30 * _s) && !Mess(i)
                                        && w.Env.Below(i.Pos.X, i.Pos.Y - 4) is { Solid: false } && Vector2.Distance(i.Pos, Pos) < 1500 * _s)
                            .OrderBy(i => Vector2.Distance(i.Pos, Pos)).FirstOrDefault();
        if (target == null) { Go(State.Groom, 3); return; }
        Travel(() => w.Items.Contains(target) && target.OnGround ? Beside(target) : null, 1, 4 * S, 25, () => { _thing = target; Go(State.Knock, 10); });
    }

    void DoKnock(World w, float dt)
    {
        var it = _thing;
        if (it == null || !w.Items.Contains(it) || it.Holder != null || _t > _dur) { GoIdle(1); return; }
        Vel.X = 0;
        if (!it.OnGround || it.Pos.Y > Pos.Y + 20 * S)
        {
            // It fell. Watch it go. Satisfying.
            _pose = Pose.Sit;
            if (_t < _dur - 2.5f) { _dur = _t + 2.5f; Log($"Knocked the {it.Def.Name.ToLowerInvariant()} off the ledge"); }
            return;
        }
        Facing = it.Pos.X >= Pos.X ? 1 : -1;
        _pose = Pose.Upright;
        if ((int)(_t / 0.8f) != (int)((_t - dt) / 0.8f) && _t > 0.8f)
        {
            if (_t < 1.7f) Misdeed(Habit.KnockingThings, w);
            // Edge it toward the drop (away from the cat).
            it.Pos.X += Facing * 5 * S;
            it.Vel = new Vector2(Facing * 90 * S, 0);
        }
    }

    void BeginScratch(World w)
    {
        var post = Nearest(w, "scratchpost");
        bool usePost = post != null && _rng.NextDouble() < 0.25f + 0.75f * MathF.Max(R(Habit.Scratching), Sk(PetSkill.ScratchPost));
        var target = usePost ? post : w.Items.Where(i => i.OnGround && i.Free && (i.Def.Verbs.Contains(Verb.Sit) || i.Def.Verbs.Contains(Verb.Lie)) && i.Def.Key != "petbed" && Vector2.Distance(i.Pos, Pos) < 1500 * _s)
                                             .OrderBy(i => Vector2.Distance(i.Pos, Pos)).FirstOrDefault() ?? post;
        if (target == null) { Go(State.Groom, 3); return; }
        bool good = target.Def.Key == "scratchpost";
        if (!good && !Tempted(Habit.Scratching, 1)) { Go(State.Groom, 3); return; }
        Travel(() => w.Items.Contains(target) ? Beside(target) : null, 1, 4 * S, 20, () =>
        {
            _thing = target;
            Go(State.Scratch, _rng.Range(3, 6));
            if (good) { GoodDeed(PetSkill.ScratchPost); Log("Used the scratching post"); }
            else { Misdeed(Habit.Scratching, w); Log($"Scratched the {target.Def.Name.ToLowerInvariant()}"); }
        });
    }

    // ---------------- reactions ----------------

    void ThinkHeld(World w, float dt)
    {
        _t += dt;
        _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Air;
        // Cats tolerate it for a bit; those who trust you relax.
        if (Kind == PetKind.Cat && UserBond < 0.3f && _t > 3 && _rng.NextDouble() < dt * 0.4) { Say(w, true); Stress = MathF.Min(1, Stress + 0.02f); }
    }

    void OnLanded(World w, float impact)
    {
        if (impact > 1800 * S) { Say(w, true); UserBond -= 0.05f; Stress = M.Clamp01(Stress + 0.1f); MaybeHurt(w, M.Clamp01(impact / (3000 * S))); }
    }

    void Startle(World w, Habit? caughtDoing)
    {
        // Whatever it was up to, it stops.
        if (_scuffleWith != null) { var o = _scuffleWith; o._scuffleWith = null; if (o._st == State.Scuffle) o.GoIdle(1); }
        Mouth = null;
        Go(State.Startled, 1.4f);
        Say(w, true);
    }

    void Happy(World w)
    {
        if (Kind == PetKind.Cat) World.Play(Sfx.Purr, Pos, 0.25f, 1, 0.5);
        else if (Kind == PetKind.Dog) World.Play(Sfx.Bark, Pos, 0.15f, 1.6f, 0.5);
        else Chirp(w);
    }

    /// <summary>Its usual noise (or an unhappy one).</summary>
    void Say(World w, bool upset)
    {
        if (_soundCd > 0) return;
        _soundCd = 2;
        float p = 1 / MathF.Sqrt(S / _s);
        switch (Kind)
        {
            case PetKind.Cat: World.Play(upset ? Sfx.Hiss : Sfx.Meow, Pos, upset ? 0.25f : 0.3f, p, 1); break;
            case PetKind.Dog: World.Play(upset ? Sfx.Whine : Sfx.Bark, Pos, 0.28f, 1.2f * p, 1); break;
            default: if (upset) Squawk(w); else Chirp(w); break;
        }
    }

    void Chirp(World w) => World.Play(Sfx.Chirp, Pos, 0.25f, 1 + (float)_rng.NextDouble() * 0.3f, 0.3);
    void Squawk(World w) => World.Play(Sfx.Squawk, Pos, 0.3f, 1, 0.6);

    // ---------------- words (bubbles) ----------------

    string? _shout;
    float _shoutUntil;

    void Shout(string text) { _shout = text; _shoutUntil = _t + 1.4f; _shoutAt = World.Now; }
    double _shoutAt;

    /// <summary>Figures nearby notice (a bark, a hiss, a squawk).</summary>
    public string? Bubble => _shout != null && World.Now - _shoutAt < 1.6 ? _shout : _say != null && World.Now < _sayUntil ? _say : null;
}
