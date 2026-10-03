using System.Numerics;

namespace Doodlefolk;

/// <summary>Out and about: riding bikes, skateboards and go-karts up and down; swimming in the pond or the pool
/// (especially on a hot day, when the real weather's on); and sitting patiently at the pond's edge with a rod. Cold
/// days send them to the campfire.</summary>
sealed partial class Brain
{
    public Item? WaterItem => f.Swimming ? _water : null;
    float _vehicleCd = 40, _swimCd = 60, _fishCd = 60;
    Item? _vehicle, _water;
    float _swerveT, _rideDir = 1, _swimDir = 1, _swimPause, _biteAt, _biteEnd;
    int _caught;

    /// <summary>How hot / cold it is out (0..1), from the real weather if it's on.</summary>
    static float Hot(World w) => w.TempC is float c ? M.Clamp01((c - 23) / 9) : 0;
    static float Cold(World w) => w.TempC is float c ? M.Clamp01((8 - c) / 12) : 0;

    void OutdoorOptions(World w, OptionList opts)
    {
        if (f.Hunter) return;
        float hot = Hot(w), cold = Cold(w);
        // A ride.
        if (_vehicleCd < _t0 && Stamina > 0.3f && !IsElder)
        {
            var v = w.Items.Where(i => i.IsVehicle && i.Rider == null && i.Free && i.OnGround && !Unreachable(i) && Vector2.Distance(i.Pos, f.Base) < 1800 * S
                           && w.Env.SupportAt(i.Pos.X, i.Pos.Y, i.GroundHwnd) is { Item: null } vs && vs.X2 - vs.X1 >= 160 * S)
                           .OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
            if (v != null && !(Baby && v.Def.Key == "gokart") && (!World.StaminaOn || Stamina > 0.25f))
                opts.Add((0.25f + P.Energy * 0.45f + P.Playfulness * 0.35f) * (0.4f + Boredom) * (w.Weather.Raining ? 0.3f : 1) * (1 + ItemLike(v)), () => GoRide(v, w), $"Ride the {v.Def.Name.ToLowerInvariant()}");
        }
        // A swim.
        if (_swimCd < _t0 && Stamina > 0.25f && w.Items.Where(i => i.IsWater && i.Free && !Unreachable(i) && Vector2.Distance(i.Pos, f.Base) < 2000 * S).OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault() is { } pool)
            opts.Add((0.18f + P.Playfulness * 0.3f + Boredom * 0.25f + hot * 1.8f) * (1 - cold * 0.85f) * (1 - w.Night * 0.6f) * (w.Weather.Snowing ? 0.1f : 1), () => GoSwim(pool, w), $"Go for a swim in the {pool.Def.Name.ToLowerInvariant()}");
        // Fishing.
        if (_fishCd < _t0 && !Baby && w.Items.FirstOrDefault(i => i.Def.Verbs.Contains(Verb.Fish) && i.Free && !Unreachable(i) && Vector2.Distance(i.Pos, f.Base) < 2000 * S
                                                                && w.Figures.Count(o => o.FishingIn == i) < 2) is { } pond)
            opts.Add((0.12f + (1 - P.Energy) * 0.35f + P.Curiosity * 0.15f + (Hobby == Hobby.Collecting ? 0.1f : 0)) * (w.Weather.Raining ? 0.6f : 1), () => GoFish(pond, w), "Go fishing");
        // Too cold: grumble (the campfire scores higher, see ItemOption).
        if (cold > 0.4f && rng.NextDouble() < 0.15 && f.Warmth < 0.1f)
            f.Emote(V("brr…", "IT'S FREEZING!", "cold. great.", "s-so cold…", "the cold bites"), 1.3f);
        if (hot > 0.5f && rng.NextDouble() < 0.1)
            f.Emote(V("phew, hot!", "SO HOT!!", "too hot.", "it's so warm…", "the sun presses down"), 1.3f);
    }

    /// <summary>Off the bike, out of the water, rod away (knocked down, picked up, removed).</summary>
    public void LeaveOutdoors()
    {
        if (f.Riding != null) Dismount();
        if (f.Swimming) StopSwim();
        if (f.FishingIn != null) StopFish();
    }

    /// <summary>Leave whatever outdoors activity it was in the middle of, if the goal has moved on.</summary>
    void TidyOutdoors(World w)
    {
        bool control = f.Mode == Mode.Control;
        if (f.Riding != null && (_g != G.Ride || !control)) Dismount();
        if (f.Swimming && (_g != G.Swim || !control)) StopSwim();
        if (f.FishingIn != null && (_g != G.Fish || !control)) StopFish();
    }

    // ---------------- riding ----------------

    void GoRide(Item v, World w)
    {
        _vehicleCd = _t0 + rng.Range(150, 360);
        _vehicle = v;
        Navigate(() => w.Items.Contains(v) && v.Rider == null && v.Free ? v.Pos : null, 8 * S, false, () => Mount(v, w), WalkPurpose.Other);
    }

    void Mount(Item v, World w)
    {
        if (v.Rider != null || !v.Free || !w.Items.Contains(v)) { World.Log($"{f.Name} can't ride the {v.Def.Key}: taken"); Go(G.Idle, 1); return; }
        if (World.StaminaOn && Stamina < 0.15f) { f.Emote(V("too tired to ride…", "TOO… TIRED…", "too tired.", "m-maybe later…", "my legs are sleeping"), 1.4f); Go(G.Idle, 1); return; }
        if (w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is not { } floor || floor.Item != null || floor.X2 - floor.X1 < 160 * S) { World.Log($"{f.Name} can't ride the {v.Def.Key}: no room here"); f.Emote(V("no room to ride…", "NOWHERE TO RIDE!", "no room.", "it's too small here…", "no road here"), 1.2f); Go(G.Idle, 1); return; }
        v.Rider = f; f.Riding = v;
        _vehicle = v;
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        _rideDir = seg == null ? f.Facing : f.Base.X - seg.X1 > seg.X2 - f.Base.X ? -1 : 1;
        Go(G.Ride, rng.Range(18, 45));
        string what = v.Def.Name.ToLowerInvariant();
        f.Emote(V("wheee!", "WHEEEE!!!", "fine. a ride.", "h-here I go…", "away on the wind"), 1.3f);
        Write("ride:" + v.Def.Key, V($"Went for a ride on the {what}.", $"RODE THE {what.ToUpperInvariant()}!!! SO FAST!", $"Rode the {what}. It was alright.", $"I rode the {what}… I didn't fall off!", $"I rode the {what} and the world blurred."), "♪", 900);
        w.Sticker("ride");
        Cheered(0.2f); Boredom = MathF.Max(0, Boredom - 0.3f);
    }

    void DoRide(World w)
    {
        var v = _vehicle;
        if (v == null || f.Riding != v || !w.Items.Contains(v) || v.Pinned || v.Holder != null || _t > _dur || (World.StaminaOn && Stamina < 0.08f))
        {
            Dismount();
            Go(G.Idle, 1);
            return;
        }
        float speed = v.Def.Key switch { "bike" => f.RunSpeed * 1.5f, "gokart" => f.RunSpeed * 1.9f, _ => f.RunSpeed * 1.15f };
        if (f.Grounded && w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { } seg)
        {
            float brake = MathF.Max(40 * S, f.Vel.X * f.Vel.X / (2 * 1600 * S));
            if (_rideDir > 0 && f.Base.X > seg.X2 - brake - 6 * S) _rideDir = -1;
            else if (_rideDir < 0 && f.Base.X < seg.X1 + brake + 6 * S) _rideDir = 1;
        }
        // Too little room to ride here (a bed, a shelf): hop off.
        if (f.Grounded && w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { } here && (here.X2 - here.X1 < 160 * S || here.Item != null)) { Dismount(); Go(G.Idle, 1); return; }
        // Someone in the way: swerve round (turn back), with a ring of the bell.
        foreach (var o in w.Figures)
        {
            if (_t < _swerveT) break;
            if (o == f || o.Mode != Mode.Control || MathF.Abs(o.Base.Y - f.Base.Y) > 20 * S || o.Brain.Asleep || o.Riding != null) continue;
            float ahead = (o.Base.X - f.Base.X) * _rideDir;
            if (ahead > 0 && ahead < 40 * S + MathF.Abs(f.Vel.X) * 0.25f)
            {
                _rideDir = -_rideDir;
                _swerveT = _t + 2;
                if (rng.NextDouble() < 0.5) f.Emote(v.Def.Key == "bike" ? "ring ring!" : v.Def.Key == "gokart" ? "beep beep!" : "coming through!", 1);
                break;
            }
        }
        FaceTo(f.Base.X + _rideDir * 100);
        f.DesiredVX = _rideDir * speed;
        f.SetAction(Act.Stand);
        if (World.StaminaOn) Stamina = MathF.Max(0, Stamina - World.Dt * (v.Def.Key == "bike" ? 0.006f : v.Def.Key == "gokart" ? 0.001f : 0.004f));
        if (rng.NextDouble() < World.Dt * 0.06) f.Emote(V("wheee!", "FASTER!!", "…", "eek!", "the wind!"), 1);
    }

    void Dismount()
    {
        if (f.Riding is { } v) { v.Rider = null; v.Vel = Vector2.Zero; v.OnGround = false; v.Angle = 0; }
        f.Riding = null;
        f.DesiredVX = 0;
        _vehicle = null;
    }

    // ---------------- swimming ----------------

    void GoSwim(Item water, World w)
    {
        _swimCd = _t0 + rng.Range(240, 600);
        if (f.Swimming) StopSwim();
        _water = water;
        float half = water.Def.W * water.Sc * 0.5f * water.ScaleX;
        float off = rng.Range(-1, 1) * MathF.Max(0, half - (MathF.Max(f.Leg, f.Torso + f.HeadR * 2) + 2 * S));
        Navigate(() => w.Items.Contains(water) && water.Free ? new Vector2(water.Pos.X + off, water.Pos.Y) : null, 10 * S, false, () => EnterWater(water, w), WalkPurpose.Other);
    }

    void EnterWater(Item water, World w)
    {
        {
            if (!w.Items.Contains(water)) { Go(G.Idle, 1); return; }
            f.Swimming = true; water.Swimmers++;
            _swimDir = rng.NextDouble() < 0.5 ? -1 : 1; _swimPause = 0;
            Go(G.Swim, rng.Range(20, 50));
            World.Play(Sfx.Squirt, f.Base, 0.4f, 0.6f);
            w.Fx.Splash(f.Base + new Vector2(0, -4 * S), S, rng, 1.6f);
            f.Emote(V("splash!", "CANNONBALL!!!", "cold. fine.", "it's a bit cold…", "into the water"), 1.3f);
            Write("swim", V($"Went for a swim in the {water.Def.Name.ToLowerInvariant()}.", "SWIMMING!!! SPLASH SPLASH!", "Swam a bit. Wet now.", $"I swam in the {water.Def.Name.ToLowerInvariant()}. I can do it!", "The water held me."), "♪", 900);
            w.Sticker("swim");
        }
    }

    void DoSwim(World w)
    {
        var water = _water;
        if (water == null || !f.Swimming || !w.Items.Contains(water) || !water.Free || _t > _dur || (World.StaminaOn && Stamina < 0.1f)) { StopSwim(); Go(G.Idle, 1.5f); return; }
        // Keep the whole body in the water (lying flat, it reaches a leg back and a torso and head forward).
        float half = MathF.Max(4 * S, water.Def.W * water.Sc * 0.5f * water.ScaleX - (MathF.Max(f.Leg, f.Torso + f.HeadR * 2) + 2 * S));
        // Laps, with a rest now and then.
        if (_swimPause > 0) { _swimPause -= World.Dt; f.DesiredVX = 0; }
        else
        {
            if (f.Base.X > water.Pos.X + half) _swimDir = -1;
            else if (f.Base.X < water.Pos.X - half) _swimDir = 1;
            FaceTo(f.Base.X + _swimDir * 100);
            f.DesiredVX = _swimDir * f.WalkSpeed * (0.5f + Sk(SkillKind.Swimming) * 0.6f);
            if (rng.NextDouble() < World.Dt * 0.15) _swimPause = rng.Range(2, 5);
        }
        f.SetAction(Act.Stand);
        // Now and then a dive: down out of sight, along a bit under the water, and up again with a splash.
        if (_diveT > 0)
        {
            _diveT -= World.Dt;
            f.Diving = M.MoveTowards(f.Diving, _diveT > 0.5f ? 1 : 0, World.Dt * 2.2f);
            if (_diveT <= 0) { f.Diving = 0; w.Fx.Splash(f.Base + new Vector2(0, -2 * S), S, rng, 1.1f); f.Emote(V("pah!", "WOO!", "…", "phew!", "up for air"), 0.9f); }
        }
        else if (_diveSoon && _t > 1.5f || _swimPause <= 0 && rng.NextDouble() < World.Dt * 0.05 * (0.3f + P.Bravery * 0.6f + Sk(SkillKind.Swimming)))
        {
            _diveSoon = false;
            _diveT = rng.Range(2.2f, 4.5f);
            w.Fx.Splash(f.Base + new Vector2(0, -2 * S), S, rng, 1.2f);
            World.Play(Sfx.Squirt, f.Base, 0.3f, 0.7f);
        }
        Wet = 1;
        Practice(SkillKind.Swimming, World.Dt * 0.0025f, true);
        Boredom = MathF.Max(0, Boredom - World.Dt * 0.01f);
        Cheered(World.Dt * 0.01f * (1 + Hot(w) * 2));
        if (World.StaminaOn) Stamina = MathF.Max(0, Stamina - World.Dt * 0.004f);
        if (MathF.Abs(f.Vel.X) > 10 * S && rng.NextDouble() < World.Dt * 2) w.Fx.Splash(f.Jt[J.HandN], S, rng, 0.5f);
    }

    float _diveT;
    bool _diveSoon;

    /// <summary>For the contact sheets and tests: go for a swim now (and dive once in).</summary>
    public void SwimNow(Item water, World w, bool dive = false) { _water = water; EnterWater(water, w); _diveSoon = dive; }

    void StopSwim()
    {
        _diveT = 0; f.Diving = 0;
        if (_water is { } water) water.Swimmers = Math.Max(0, water.Swimmers - 1);
        f.Swimming = false;
        f.DesiredVX = 0;
        _water = null;
    }

    // ---------------- fishing ----------------

    void GoFish(Item pond, World w)
    {
        _fishCd = _t0 + rng.Range(300, 700);
        float half = pond.Def.W * pond.Sc * 0.5f * pond.ScaleX;
        float side = MathF.Sign(f.Base.X - pond.Pos.X); if (side == 0) side = 1;
        Navigate(() => w.Items.Contains(pond) ? new Vector2(pond.Pos.X + side * (half + 8 * S), pond.Pos.Y) : null, 12 * S, false, () =>
        {
            if (!w.Items.Contains(pond)) { Go(G.Idle, 1); return; }
            FaceTo(pond.Pos.X);
            f.FishingIn = pond;
            f.Bobber = new Vector2(f.Base.X + f.Facing * 34 * S, pond.SurfaceY);
            f.Bite = false;
            _caught = 0;
            _biteAt = _t + rng.Range(8, 25);
            Go(G.Fish, rng.Range(40, 90));
            f.Emote(V("gone fishing", "BIG ONE TODAY!", "fishing. shh.", "um, fish? please?", "the line goes in"), 1.4f);
        }, WalkPurpose.Other);
    }

    void DoFish(World w)
    {
        var pond = f.FishingIn;
        if (pond == null || !w.Items.Contains(pond) || !pond.Free || _t > _dur)
        {
            if (pond != null && _caught == 0 && _t > _dur) Write("fish:none", V("Went fishing. Caught nothing.", "NO FISH?! THE FISH ARE HIDING!", "Fished. Nothing. Typical.", "I fished for ages… nothing bit.", "The water kept its secrets."), "…", 900);
            StopFish(); Go(G.Idle, 1); return;
        }
        f.DesiredVX = 0;
        FaceTo(pond.Pos.X);
        f.SetAction(Act.SitFloor);
        if (!f.Bite && _t > _biteAt)
        {
            f.Bite = true; _biteEnd = _t + rng.Range(0.8f, 1.6f);
            f.Emote("!", 0.8f);
            World.Play(Sfx.Squirt, f.Bobber, 0.15f, 1.6f);
        }
        if (f.Bite && _t > _biteEnd)
        {
            f.Bite = false;
            _biteAt = _t + rng.Range(10, 30) * (1.15f - Sk(SkillKind.Fishing) * 0.5f);
            if (rng.NextDouble() < 0.35 + P.Bravery * 0.08 + Sk(SkillKind.Fishing) * 0.4f)
            {
                _caught++;
                Practice(SkillKind.Fishing, 0.04f);
                w.Fx.Splash(f.Bobber, S, rng, 1.2f);
                var (kind, cm) = Fishes.Catch(rng, DateTime.Now, w.Weather.Raining, Hobby == Hobby.Collecting ? 0.5f : 0);
                string what = kind.Name.ToLowerInvariant(), size = Fishes.Size(kind, cm);
                var note = w.FishCaught?.Invoke(f, kind, cm) ?? "";
                if (kind.Junk)
                {
                    // Junk: a laugh, and (a bottle) a little message.
                    f.Emote(V($"an {what}?!", $"AN {what.ToUpperInvariant()}?!", $"…an {what}. great.", $"oh… an {what}…", $"the pond offers: an {what}"), 1.8f);
                    Write("fish:junk", kind.Key == "bottle" ? $"Fished a message in a bottle out of the pond! It said: \"{V("hello from the other side of the screen", "WHOEVER FINDS THIS: HI!!!", "help, I'm stuck in a pond", "keep fishing. it gets better.", "the sea remembers")}\"" : $"Fished an {what} out of the pond. Tidied it away.", "…", 300);
                }
                else
                {
                    bool keep = rng.NextDouble() < 0.4 && w.Items.Count < 55 && kind.Rarity < FishRarity.Rare;
                    if (keep && w.MakeItem?.Invoke("fish") is { } fish)
                    {
                        fish.Pos = f.Base + new Vector2(-f.Facing * 14 * S, -6 * S); fish.Vel = new Vector2(-f.Facing * 60 * S, -220 * S); fish.OnGround = false;
                        fish.SizeMul = Math.Clamp(cm / 30f, 0.5f, 1.9f);
                        fish.Color = kind.Col;
                    }
                    f.Emote(Gestures ? "🐟" : V($"a {size} {what}!", $"A {size.ToUpperInvariant()} {what.ToUpperInvariant()}!!!", $"{size} {what}. whatever.", $"a {what}! I c-caught one!", $"a {size} {what} rises"), 1.6f);
                    Cheered(kind.Rarity >= FishRarity.Rare ? 0.6f : 0.25f);
                    Write("fish:caught", V($"Caught a {size} {what} ({cm:0.#} cm){(keep ? " and kept it for supper" : " and let it go")}.{note}", $"CAUGHT A {size.ToUpperInvariant()} {what.ToUpperInvariant()}!!! {cm:0.#} CM!!!{note}", $"Caught a {what}. {cm:0.#} cm. {(keep ? "Kept it." : "Threw it back.")}{note}", $"I caught a {what}… {cm:0.#} cm… {(keep ? "I'll cook it." : "I let it swim away.")}{note}", $"A {what}, {cm:0.#} cm, {(keep ? "kept" : "returned to the deep")}.{note}"), "★", 300);
                }
                w.Sticker("fish");
            }
            else f.Emote(V("it got away…", "NOOO, IT GOT AWAY!", "missed.", "oh… it got away…", "it slipped the hook"), 1.3f);
        }
    }

    void StopFish()
    {
        f.FishingIn = null;
        f.Bite = false;
    }
}
