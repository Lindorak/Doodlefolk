using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Animals and each other (and the figures): cats and dogs start out wary, cats see parrots as prey,
/// time spent together peacefully makes friends (who then play-chase and curl up together), and squabbles over food,
/// toys and beds can turn into a real scrap. Parrots perch, talk and pick up phrases; dogs (and very patient cats)
/// go for walks on a leash.</summary>
sealed partial class Pet
{
    readonly Dictionary<int, float> _petBond = new();
    readonly Dictionary<int, float> _fear = new();
    public Pet? _scuffleWith;

    /// <summary>How it feels about another animal, -1..1 (cats and dogs start wary; a parrot is a cat's dinner).</summary>
    public float PetBond(Pet o)
    {
        if (_petBond.TryGetValue(o.Id, out var b)) return b;
        b = (Kind, o.Kind) switch
        {
            (PetKind.Cat, PetKind.Dog) or (PetKind.Dog, PetKind.Cat) => -0.25f,
            (PetKind.Cat, PetKind.Parrot) or (PetKind.Parrot, PetKind.Cat) => -0.3f,
            (PetKind.Dog, PetKind.Dog) => 0.2f,
            _ => 0.05f,
        };
        // The young get along with anyone.
        if (Young || o.Young) b += 0.2f;
        _petBond[o.Id] = b;
        return b;
    }

    public void SetPetBond(Pet o, float v) => _petBond[o.Id] = Math.Clamp(v, -1, 1);
    void PetBond(Pet o, float delta) => _petBond[o.Id] = Math.Clamp(PetBond(o) + delta, -1, 1);
    float Fear(Pet o) => _fear.GetValueOrDefault(o.Id);
    void Afraid(Pet o, float d) => _fear[o.Id] = M.Clamp01(Fear(o) + d);

    /// <summary>Once a second: getting used to each other.</summary>
    void SocialTick(World w, float dt)
    {
        foreach (var o in w.Pets)
        {
            if (o == this) continue;
            float d = Vector2.Distance(o.Pos, Pos);
            if (d > 450 * _s) continue;
            bool calm = _st is not (State.Scuffle or State.Flee or State.Stalk or State.ChasePet) && o._st is not (State.Scuffle or State.Flee or State.Stalk or State.ChasePet);
            if (calm) { PetBond(o, dt * 0.0015f * (Young || o.Young ? 2 : 1) * Tm(Temperament.Affectionate, 1.4f) * Tm(Temperament.Shy, 0.6f)); _fear[o.Id] = MathF.Max(0, Fear(o) - dt * 0.002f); }
        }
    }

    void Bonding(World w, float dt)
    {
        if (Owner != null && !w.Figures.Contains(Owner)) Owner = null;
        foreach (var f in w.Figures)
        {
            if (f.Dead) continue;
            float d = Vector2.Distance(f.Base, Pos);
            if (d < 220 * _s) Bond[f.Id] = M.Clamp01(BondWith(f) + dt * 0.004f * (1 + f.Tastes.Of(Thing.Pets)) * Tm(Temperament.Affectionate, 1.5f) * Tm(Temperament.Shy, 0.6f));
        }
        var best = w.Figures.Where(f => !f.Dead).OrderByDescending(BondWith).FirstOrDefault();
        if (best != null && BondWith(best) > 0.25f && best != Owner)
        {
            Owner = best;
            best.Brain.OnAdoptedBy(this);
            Log($"Picked {best.Name} as its favourite");
        }
    }

    float BondWith(Figure f) => Bond.TryGetValue(f.Id, out var b) ? b : 0;

    /// <summary>A figure is petting it.</summary>
    public void PettedBy(Figure f)
    {
        if (_st is State.Fetch or State.Chase or State.Scuffle or State.Eat or State.Potty || Leashed) return;
        Go(State.Petted, 2.5f);
        Bond[f.Id] = M.Clamp01(BondWith(f) + 0.05f);
        Attention = MathF.Max(0, Attention - 0.2f);
    }

    public void CallTo(Vector2 where) => Command("come", World.Current);

    // ---------------- hunting birds ----------------

    void BeginStalk(Pet bird, World w)
    {
        _other = bird;
        Go(State.Stalk, 20);
        _other = bird;
        Misdeed(Habit.ChaseBirds, w);
        Log($"Started stalking {bird.Name}");
    }

    void DoStalk(World w, float dt)
    {
        var b = _other;
        if (b == null || !w.Pets.Contains(b) || b.Flying || b.OnCursor || b.Held || _t > _dur) { Disappointed(w); return; }
        float dx = b.Pos.X - Pos.X, d = Vector2.Distance(b.Pos, Pos);
        Facing = dx >= 0 ? 1 : -1;
        _pose = Pose.Crouch;
        if (_t % 3 < 0.05f) Misdeed(Habit.ChaseBirds, w);   // still at it: a spray now still counts
        if (d < 110 * S && Grounded && MathF.Abs(b.Pos.Y - Pos.Y) < 160 * S)
        {
            // Butt wiggle… then pounce.
            Vel.X = 0;
            if (_t > _dur - 18.8f || _pounceWiggle > 0.7f)
            {
                Go(State.PounceBird, 2);
                _other = b;
                if (NavGraph.Lob(Pos, b.Pos, 1900 * S, S, out var v)) Vel = v; else Vel = new Vector2(dx * 3, -500 * S);
                Grounded = false;
                Misdeed(Habit.ChaseBirds, w);
                return;
            }
            _pounceWiggle += dt;
            return;
        }
        _pounceWiggle = 0;
        MoveTo(w, b.Pos, 0.4f, 60 * S);
    }

    float _pounceWiggle;

    void DoPounceBird(World w)
    {
        var b = _other;
        if (Grounded && _t > 0.2f)
        {
            if (b != null && w.Pets.Contains(b) && !b.Flying && Vector2.Distance(b.Pos, Pos) < 30 * S)
            {
                // Got it… but it wriggles free (feathers everywhere).
                w.Fx.Spark(b.Centre, b.S, w.Rng, 0.8f, b.Color);
                w.Fx.Spark(b.Centre, b.S, w.Rng, 0.8f, b.Accent);
                b.Afraid(this, 0.5f);
                b.Stress = M.Clamp01(b.Stress + 0.5f);
                b.Log($"Caught by {Name}! Escaped with a few feathers missing");
                b.FleeFrom(this, w);
                b.Squawk(w);
                Log($"Caught {b.Name} (it got away)");
                w.News("pets", b.Kind == PetKind.Parrot ? $"{Name} pounced on {b.Name}! Feathers flew, but {b.Name} escaped" : $"{Name} pounced on {b.Name}, who got away", 2);
                Misdeed(Habit.ChaseBirds, w);
                Go(State.Sit, 2);
                return;
            }
            Disappointed(w);
        }
        _pose = Pose.Air;
    }

    void Disappointed(World w)
    {
        if (Kind == PetKind.Cat) { World.Play(Sfx.Chirp, Pos, 0.15f, 0.7f, 1); Shout("ekekek"); }
        Go(State.Sit, 2);
    }

    // ---------------- chasing, fleeing, fighting ----------------

    void BeginChase(Pet o, World w, bool play = false)
    {
        Go(State.ChasePet, play ? _rng.Range(5, 9) : _rng.Range(6, 12));
        _other = o;
        _playful = play;
        if (!play) { Misdeed(Habit.ChasePets, w); Log($"Chased {o.Name}"); Shout("woof!"); }
        else if (o._st is State.Idle or State.Sit or State.Wander) { o.Go(State.ChasePet, _dur); o._other = this; o._playful = true; o._chaseAway = true; }
    }

    bool _playful, _chaseAway;

    void DoChasePet(World w, float dt)
    {
        var o = _other;
        if (o == null || !w.Pets.Contains(o) || o.Held || _t > _dur) { if (_playful && o != null) PetBond(o, 0.04f); GoIdle(1); return; }
        if (_playful && _chaseAway)
        {
            // The one being chased in play: run off, and swap now and then.
            var seg = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
            if (seg != null) MoveTo(w, new Vector2(o.Pos.X < Pos.X ? seg.X2 - 10 * S : seg.X1 + 10 * S, Pos.Y), 1.8f, 10 * S);
            if (_rng.NextDouble() < dt * 0.4) { _chaseAway = false; o._chaseAway = true; }
            Boredom = MathF.Max(0, Boredom - dt * 0.05f);
            return;
        }
        if (!_playful && _t % 2 < dt) Misdeed(Habit.ChasePets, w);
        MoveTo(w, o.Pos, 2, 20 * S);
        Boredom = MathF.Max(0, Boredom - dt * 0.04f);
        if (!_playful && Vector2.Distance(o.Pos, Pos) < 26 * S && o._st != State.Flee && o._st != State.Scuffle) o.DefendOrFlee(this, w);
    }

    /// <summary>Something's coming at it: run (cats go up), or, if cornered or feisty, stand and fight.</summary>
    void DefendOrFlee(Pet from, World w)
    {
        if (_st is State.Scuffle) return;
        float brave = (Kind == PetKind.Dog ? 0.5f : Small ? 0.05f : 0.3f) * Tm(Temperament.Bold, 1.6f) * Tm(Temperament.Shy, 0.4f);
        bool cornered = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd) is { } seg && (Pos.X - seg.X1 < 40 * S || seg.X2 - Pos.X < 40 * S);
        if (Kind != PetKind.Parrot && (cornered || _rng.NextDouble() < brave * (1 - Fear(from))) && Tempted(Habit.FightPets, 0.7f))
        {
            StartScuffle(from, w);
            return;
        }
        if (Kind == PetKind.Cat && _rng.NextDouble() < 0.5) { _pose = Pose.Arch; Shout("hsss!"); World.Play(Sfx.Hiss, Pos, 0.3f, 1, 0.5); }
        Afraid(from, 0.1f);
        if (Kind == PetKind.Rabbit) { Shout("*thump*"); World.Play(Sfx.Thud, Pos, 0.25f, 1.6f, 0.5); }
        if (Kind == PetKind.Hamster) World.Play(Sfx.Squeak, Pos, 0.2f, 2.2f, 0.5);
        FleeFrom(from, w);
    }

    void FleeFrom(Pet o, World w)
    {
        _other = o;
        Go(State.Flee, Kind == PetKind.Parrot ? 6 : 4);
        _other = o;
        Stress = M.Clamp01(Stress + 0.12f);
        if (Kind == PetKind.Parrot) { Squawk(w); PerchSomewhere(w, high: true, away: o.Pos); _st = State.Flee; _dur = 6; return; }
        // Cats escape upward if there's a ledge to jump to.
        if (Kind == PetKind.Cat && w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd) is { } seg)
        {
            var up = w.Env.Platforms.Where(p => p.Y < seg.Y - 40 * S && p.Y > seg.Y - 260 * S && p.X2 - p.X1 > 30 * S && MathF.Abs((p.X1 + p.X2) / 2 - Pos.X) < 500 * S)
                                    .OrderBy(p => MathF.Abs((p.X1 + p.X2) / 2 - Pos.X)).FirstOrDefault();
            if (up != null) _target = new Vector2(M.ClampIn(Pos.X, up.X1 + 10 * S, up.X2 - 10 * S), up.Y);
        }
    }

    void DoFlee(World w)
    {
        var o = _other;
        if (_t > _dur || o == null || !w.Pets.Contains(o)) { GoIdle(1); if (Stress > 0.5f) GoHide(w); return; }
        if (Kind == PetKind.Parrot) { if (!Flying && _t > 1) GoIdle(1); return; }
        if (_target is Vector2 t) { if (MoveTo(w, t, 2, 8 * S)) { _target = null; Go(State.Sit, 3); _pose = Pose.Arch; } return; }
        var seg = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd);
        if (seg == null) return;
        float away = Pos.X >= o.Pos.X ? 1 : -1;
        MoveTo(w, new Vector2(M.ClampIn(Pos.X + away * 300 * S, seg.X1 + 8 * S, seg.X2 - 8 * S), Pos.Y), 2.2f, 6 * S);
    }

    /// <summary>Two at one bowl: the eater guards it. The other backs off, or…</summary>
    bool FoodSquabble(Pet eater, World w)
    {
        float tension = 1 - (PetBond(eater) + 1) / 2;
        if (eater.Kind == PetKind.Dog) eater.Shout("grrr");
        if (Tempted(Habit.FightPets, tension * 0.7f)) { StartScuffle(eater, w, "over the food bowl"); return true; }
        if (PetBond(eater) > 0.3f) return false;   // friends share
        Go(State.Sit, 4);
        return true;
    }

    double _calmUntil;

    void StartScuffle(Pet o, World w, string why = "")
    {
        // After a scrap, both need a good while before they'd fight again.
        if (World.Calm || World.Now < _calmUntil || World.Now < o._calmUntil) { FleeFrom(o, w); return; }
        _calmUntil = o._calmUntil = World.Now + _rng.Range(480, 900);
        if (o._st == State.Scuffle || o.Held || Held || Kind == PetKind.Parrot && o.Kind == PetKind.Parrot && _rng.NextDouble() < 0.5) return;
        foreach (var p in new[] { this, o })
        {
            p.Go(State.Scuffle, _rng.Range(2.5f, 4));
            p.Misdeed(Habit.FightPets, w);
            p.Stress = M.Clamp01(p.Stress + 0.3f);
        }
        _scuffleWith = o;
        o._scuffleWith = this;
        o._dur = _dur;
        PetBond(o, -0.15f); o.PetBond(this, -0.15f);
        Log($"Got into a fight with {o.Name} {why}".Trim());
        o.Log($"Got into a fight with {Name} {why}".Trim());
        w.News("pets", $"{Name} and {o.Name} got into a scrap{(why.Length > 0 ? " " + why : "")}", 2);
    }

    void DoScuffle(World w, float dt)
    {
        var o = _scuffleWith;
        if (o == null || !w.Pets.Contains(o) || o._scuffleWith != this) { GoIdle(1); return; }
        // Roll toward each other into one big cartoon dust-up.
        Vector2 mid = (Pos + o.Pos) / 2;
        if (Grounded) Vel.X = MathF.Sign(mid.X - Pos.X) * MathF.Min(120 * S, MathF.Abs(mid.X - Pos.X) * 8);
        _pose = Pose.Arch;
        if (_soundCd <= 0) { _soundCd = 0.45f; World.Play(Kind switch { PetKind.Cat => _rng.NextDouble() < 0.5 ? Sfx.Hiss : Sfx.Yowl, PetKind.Dog => Sfx.Growl, _ => Sfx.Squawk }, Pos, 0.3f, 1, 0.2); }
        if (_t > _dur && Id < o.Id)
        {
            // Over: the less determined one runs for it.
            bool iLose = Stress + _rng.Range(0, 0.6f) > o.Stress + _rng.Range(0, 0.6f);
            var (loser, winner) = iLose ? (this, o) : (o, this);
            _scuffleWith = null; o._scuffleWith = null;
            winner.GoIdle(1.5f);
            loser.Afraid(winner, 0.25f);
            loser.MaybeHurt(w, 0.6f);
            loser.FleeFrom(winner, w);
        }
    }

    public Vector2? ScuffleCentre => _scuffleWith != null && Id < _scuffleWith.Id ? (Pos + _scuffleWith.Pos) / 2 + new Vector2(0, -Height * 0.6f) : null;

    // ---------------- parrots: perching and talking ----------------

    public readonly List<string> Vocabulary = new() { "hello!", "pretty bird!" };
    readonly Dictionary<string, int> _heard = new();
    string? _say;
    double _sayUntil;

    static Vector2 PerchTop(Item perch) => new(perch.Pos.X, perch.Pos.Y - perch.Def.H * perch.Sc * 0.92f);

    void PerchSomewhere(World w, bool high = false, Vector2? away = null)
    {
        Vector2? spot = null;
        var perch = Nearest(w, "perch");
        // Its favourite figure's head is the best seat in the house.
        if (!high && Owner != null && w.Figures.Contains(Owner) && Owner.Mode == Mode.Control && _rng.NextDouble() < 0.35 + BondWith(Owner) * 0.4) { PerchOn(Owner, w); return; }
        if (!high && perch != null && _rng.NextDouble() < 0.55) spot = PerchTop(perch);
        else
        {
            var plats = w.Env.Platforms.Where(p => p.X2 - p.X1 > 30 * S && (away == null || Vector2.Distance(new Vector2((p.X1 + p.X2) / 2, p.Y), away.Value) > 400 * _s))
                                       .OrderBy(p => high ? p.Y : _rng.Next()).Take(high ? 3 : 8).ToList();
            if (plats.Count > 0) { var p = plats[_rng.Next(plats.Count)]; spot = new Vector2(_rng.Range(p.X1 + 10 * S, p.X2 - 10 * S), p.Y); }
        }
        if (spot is not Vector2 s) { Go(State.Perch, 8); return; }
        Travel(() => s, 1, 4 * S, 20, () => Go(State.Perch, _rng.Range(10, 30)));
    }

    void PerchOn(Figure f, World w)
    {
        _perchFig = f;
        Go(State.Perch, _rng.Range(12, 30));
        _perchFig = f;
        Flying = true;
        Grounded = false;
    }

    void DoPerch(World w, float dt)
    {
        _pose = Pose.Perch;
        if (_perchFig is { } f)
        {
            if (!w.Figures.Contains(f) || f.Mode != Mode.Control || f.Brain.InFight || _t > _dur) { _perchFig = null; Flying = true; _flyTo = null; GoIdle(1); return; }
            Vector2 seat = f.Jt[J.Head] + new Vector2(0, -f.HeadR - 1 * S);
            if (Flying)
            {
                _flyTo = seat;
                if (Vector2.Distance(Pos, seat) < 8 * S) Flying = false;
                return;
            }
            Pos = seat; Vel = Vector2.Zero; Grounded = false;
            Facing = f.Facing;
        }
        else Vel.X = 0;
        // Bob along to music; chatter now and then.
        if (w.Items.Any(i => i.Playing && Vector2.Distance(i.Pos, Pos) < 600 * _s)) _pose = Pose.Fluff;
        if (_rng.NextDouble() < dt * 0.08) Chatter(w);
        Attention = MathF.Max(0, Attention - dt * (_perchFig != null ? 0.01f : 0));
        if (_t > _dur) GoIdle(0.5f);
    }

    /// <summary>Parrots say something they know (with their own little voice).</summary>
    void Chatter(World w)
    {
        if (Kind != PetKind.Parrot) return;
        var line = _rng.NextDouble() < 0.25 ? $"{Name.ToLowerInvariant()}!" : Vocabulary[_rng.Next(Vocabulary.Count)];
        Speak(w, line);
        Boredom = MathF.Max(0, Boredom - 0.06f);
        if (_st is State.Idle) Go(State.Perch, _rng.Range(4, 10));
    }

    public void Speak(World w, string text)
    {
        _say = text;
        _sayUntil = World.Now + 2 + text.Length * 0.06;
        World.Babble(text, Pos, 1.9f, 0.28f, 1.35f);
    }

    /// <summary>Parrots listen. Hear something often enough (or from you, directly) and they'll start saying it.</summary>
    public void Hear(string text, bool direct, World w)
    {
        if (Kind != PetKind.Parrot) return;
        text = text.Trim().ToLowerInvariant();
        if (text.Length is < 2 or > 32 || Vocabulary.Contains(text)) return;
        int n = _heard[text] = _heard.GetValueOrDefault(text) + (direct ? 2 : 1);
        float need = 4 - Sk(PetSkill.Talk) * 3;
        if (n < need) { if (direct) { Facing = w.Cursor.X >= Pos.X ? 1 : -1; Chirp(w); } return; }
        Vocabulary.Add(text);
        if (Vocabulary.Count > 24) Vocabulary.RemoveAt(2);
        Skills[PetSkill.Talk] = MathF.Min(1, Sk(PetSkill.Talk) + 0.05f);
        _goodDeed = (PetSkill.Talk, World.Now);
        Log($"Learned to say \"{text}\"");
        Speak(w, text);
    }

    // ---------------- walks ----------------

    float _walked, _sniffAt;

    void DoWalk(World w, float dt)
    {
        if (!Leashed) { GoIdle(1); return; }
        var cur = w.Cursor;
        float dx = cur.X - Pos.X;
        // Cats on a leash: mostly a protest.
        if (Kind == PetKind.Cat && _rng.NextDouble() < dt * (0.5f - UserBond * 0.3f)) { _pose = Pose.Belly; Vel.X = 0; Shout("…"); return; }
        if (Kind == PetKind.Parrot) { Flying = true; _flyTo = cur + new Vector2(0, 30 * S); return; }
        if (_t > _sniffAt && _rng.NextDouble() < dt * 0.25)
        {
            _sniffAt = _t + _rng.Range(2, 4);
            _pose = Pose.HeadDown;
        }
        if (_t < _sniffAt && _pose == Pose.HeadDown && _leashTug <= 0) { Vel.X = 0; return; }
        // Business, outside where it belongs.
        if (MathF.Max(Bladder, Bowel) > 0.35f && Grounded && _rng.NextDouble() < dt * 0.4) { Go(State.Potty, 3); return; }
        float lead = 50 * S;
        if (MathF.Abs(dx) > lead && Grounded)
        {
            float pace = M.Clamp01((MathF.Abs(dx) - lead) / (LeashLength * 0.7f)) * 1.6f + 0.5f;
            var under = w.Env.Below(cur.X, cur.Y - 2);
            MoveTo(w, new Vector2(cur.X, under?.Y ?? Pos.Y), pace, lead);
            _walked += MathF.Abs(Vel.X) * dt;
            _pose = Pose.Stand;
        }
        else { Vel.X = 0; _pose = Pose.Stand; Facing = dx >= 0 ? 1 : -1; }
        if (_walked > 4000 * _s)
        {
            _walked = 0;
            Boredom = MathF.Max(0, Boredom - 0.4f);
            Attention = MathF.Max(0, Attention - 0.4f);
            UserBond = MathF.Min(1, UserBond + 0.05f);
            Weight = MathF.Max(0, Weight - 0.01f);
            Log("Had a lovely walk");
            Happy(w);
            w.Sticker("walkies");
        }
        Attention = MathF.Max(0, Attention - dt * 0.004f);
    }
}
