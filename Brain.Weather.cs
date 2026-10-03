using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Weather and time of day. Out in the rain they get wet: most look for shelter (a box or tent, an umbrella,
/// or under a ledge), the playful dance in it. Snow means snowball fights and snowmen. Thunder scares the timid. Late
/// at night they get sleepy; in the morning they greet the day.</summary>
sealed partial class Brain
{
    /// <summary>How soaked it is (0..1).</summary>
    public float Wet;
    float _shelterCheck, _snowmanCd = 60;
    int _snowballs;
    bool _sheltered, _rainDance;
    int _greetedDay = -1;
    Figure? _snowTarget;

    bool LovesRain => (P.Playfulness > 0.62f && P.Energy > 0.45f) || f.Tastes.Of(Thing.Dancing) > 0.6f;
    bool HasUmbrella => f.Weapon?.Def.Key == "umbrella";

    void UpdateWeather(float dt, World w)
    {
        var sky = w.Weather;
        _shelterCheck -= dt;
        if (_shelterCheck <= 0) { _shelterCheck = 0.5f; _sheltered = Sheltered(w); }
        if (sky.Raining && !_sheltered && f.Mode == Mode.Control) Wet = M.Clamp01(Wet + dt * 0.06f * sky.Intensity);
        else Wet = MathF.Max(0, Wet - dt * (sky.Raining ? 0.004f : 0.02f));
        if (Wet > 0.3f && !LovesRain) Sadness = M.Clamp01(Sadness + dt * 0.004f);
        if (f.Weapon is { Def.Key: "umbrella" } u) u.Open = sky.Raining || sky.Snowing;
        // The day's first look around in the morning.
        var now = DateTime.Now;
        if (w.DayNight && now.Hour is >= 6 and < 11 && _greetedDay != now.DayOfYear && f.Mode == Mode.Control && f.Grounded && _g is G.Idle or G.Walk)
        {
            _greetedDay = now.DayOfYear;
            if (_t0 > 20)
            {
                f.Emote(rng.NextDouble() < 0.5 ? "good morning!" : "*yawn*", 1.6f);
                f.StartFidget(Fidget.Stretch);
                Write("morning", V("Good morning!", "GOOD MORNING, WORLD!", "Morning. Already?", "Morning... quietly.", "Morning. The light came back."), "☀", 36000);
            }
        }
        else if (_greetedDay < 0) _greetedDay = now.Hour < 11 ? now.DayOfYear : -2;
    }

    /// <summary>Under something, holding an umbrella, or tucked inside something.</summary>
    bool Sheltered(World w)
    {
        if (HasUmbrella || (_item != null && _verb == Verb.Hide)) return true;
        foreach (var p in w.Env.Platforms)
            if (p.Y < f.Base.Y - f.Height * 0.7f && p.Y > f.Base.Y - 520 * S && f.Base.X > p.X1 + 4 * S && f.Base.X < p.X2 - 4 * S) return true;
        return false;
    }

    void WeatherOptions(World w, OptionList opts)
    {
        var sky = w.Weather;
        // Night: sleepy.
        if (w.Night > 0.4f && Stamina < 0.85f)
            opts.Add(w.Night * (1.4f - P.Energy) * 2.2f, () => Go(G.Sleep, rng.Range(30, 90)), "Sleep (it's late)");
        if (sky.Raining)
        {
            if (LovesRain && Stamina > 0.3f)
                opts.Add((1 + P.Playfulness) * 1.2f, () => { _rainDance = true; Go(G.Groove, rng.Range(8, 16)); f.Emote("♪ rain!", 1.4f); }, "Dance in the rain");
            else if (!_sheltered)
                opts.Add((1.2f + Wet * 3) * (1.1f - P.Bravery * 0.3f), () => GoShelter(w), "Get out of the rain");
        }
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        float snowHere = seg != null ? sky.SnowOn(seg) : 0;
        if (snowHere > 1.5f * S && Stamina > 0.3f)
        {
            var target = w.Figures.Where(o => o != f && !o.Dead && o.Mode == Mode.Control && Vector2.Distance(o.Base, f.Base) < 650 * S && (AffinityWith(o) > -0.2f || P.Aggression > 0.6f))
                                  .OrderBy(_ => rng.Next()).FirstOrDefault();
            if (target != null)
                opts.Add(P.Playfulness * (1 + Joy) * 1.4f, () => BeginSnowball(target), $"Throw a snowball at {target.Name}");
            if (snowHere > 3 * S && _t0 > _snowmanCd && w.Items.Count < 50)
                opts.Add((P.Curiosity * 0.6f + P.Playfulness * 0.6f) * 1.1f, () => { _snowmanCd = _t0 + rng.Range(240, 480); Go(G.Snowman, 6); f.Emote("☃?", 1.2f); }, "Build a snowman");
        }
    }

    /// <summary>Somewhere out of the rain: a box or tent, an umbrella, under a ledge; or just huddle up.</summary>
    void GoShelter(World w)
    {
        var hide = w.Items.Where(i => i.Free && i.OnGround && (i.Def.Verbs.Contains(Verb.Hide) || i.Def.Verbs.Contains(Verb.Shelter)) && !Unreachable(i))
                          .OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
        if (hide != null && Vector2.Distance(hide.Pos, f.Base) < 1500 * S)
        {
            f.Emote(hide.Def.Verbs.Contains(Verb.Shelter) ? "umbrella!" : "!", 1);
            UseItem(hide, hide.Def.Verbs.Contains(Verb.Shelter) ? Verb.Shelter : Verb.Hide, w);
            return;
        }
        // Under a ledge: a spot with something overhead.
        var env = w.Env;
        (Platform p, float x, float d)? best = null;
        foreach (var p in env.Platforms)
        {
            if (p.X2 - p.X1 < 30 * S) continue;
            foreach (var q in env.Platforms)
            {
                if (q == p || q.Y > p.Y - f.Height * 0.8f || q.Y < p.Y - 480 * S) continue;
                float x1 = MathF.Max(p.X1, q.X1) + 10 * S, x2 = MathF.Min(p.X2, q.X2) - 10 * S;
                if (x2 <= x1) continue;
                float x = M.ClampIn(f.Base.X, x1, x2);
                float d = MathF.Abs(x - f.Base.X) + MathF.Abs(p.Y - f.Base.Y) * 1.5f;
                if (d < 1400 * S && (best == null || d < best.Value.d)) best = (p, x, d);
            }
        }
        if (best is { } b)
        {
            var a = Anchor.On(env, b.p, b.x);
            Navigate(() => a.Resolve(env), 6 * S, true, () => { Go(G.SitFloor, rng.Range(10, 25)); f.Emote("phew", 1); }, WalkPurpose.Other);
            return;
        }
        f.Emote("brr", 1.2f);
        f.DuckT = 1;
        Go(G.SitFloor, rng.Range(6, 12));
    }

    public void OnWeatherChanged(WeatherKind k, World w)
    {
        if (f.Mode != Mode.Control || _g == G.Sleep) return;
        switch (k)
        {
            case WeatherKind.Rain:
            case WeatherKind.Storm:
                f.Emote(LovesRain ? "rain! ♪" : rng.NextDouble() < 0.5 ? "uh oh" : "!", 1.3f);
                Write("rain", LovesRain ? V("It rained and I danced in it!", "It RAINED and I danced the whole time!", "Rain. Danced anyway.")
                                        : V("It rained today.", "Rain! Puddles everywhere!", "Rain. Great. Just great.", "It rained. I hid until it stopped.", "It rained. Every drop had its own little splash."), "☂", 3600);
                break;
            case WeatherKind.Snow:
                f.Emote(P.Playfulness > 0.4f ? "snow!!" : "brr", 1.4f);
                Write("snow", V("It snowed today!", "SNOW!!! Everything's white!", "Snow. Cold. Wet. Snow.", "It snowed. Everything went quiet.", "It snowed. Every flake was different."), "❄", 3600);
                break;
            case WeatherKind.Clear:
                if (Wet > 0.3f) { f.Emote("brrr", 1.2f); f.StartFidget(Fidget.Shrug); }
                else if (rng.NextDouble() < 0.4) f.Emote("☀", 1);
                break;
        }
    }

    public void OnThunder(World w)
    {
        w.Sticker("storm");
        if (f.Mode != Mode.Control || _g == G.Sleep) return;
        if (P.Bravery < 0.45f)
        {
            Fear = M.Clamp01(Fear + 0.35f);
            f.Emote("!!", 1);
            f.DuckT = 0.7f;
            Write("thunder", V("Thunder! So loud.", "THUNDER! My heart!", "Thunder. Didn't flinch. Much.", "Thunder. I hid. I'm still hiding."), "⚡", 3600);
        }
        else if (rng.NextDouble() < 0.4) f.Emote(P.Playfulness > 0.6f ? "whoa!" : "!", 1);
    }

    // ---------------- snow ----------------

    float _snowHitAt = -99;

    void BeginSnowball(Figure t)
    {
        _snowTarget = t;
        _snowballs = rng.Next(1, 4);
        Go(G.Snowball, 8);
    }

    void DoSnowball(World w)
    {
        var t = _snowTarget;
        if (t == null || !w.Figures.Contains(t) || t.Dead || !f.Grounded) { Go(G.Idle, 1); return; }
        f.DesiredVX = 0;
        FaceTo(t.Base.X);
        f.LookAt = t.Jt[J.Head];
        float c = _t % 1.6f, pc = (_t - World.Dt) % 1.6f;
        if (c < 0.7f) { f.DuckT = 0.15f; f.SetAction(Act.Stand); }   // scoop up some snow
        else f.SetAction(Act.Throw);
        if (c >= 0.95f && pc < 0.95f)
        {
            Vector2 from = f.Jt[J.HandN], to = t.Jt[J.Head] + t.Vel * 0.3f;
            float dist = Vector2.Distance(from, to);
            float time = MathF.Max(0.35f, dist / (850 * S));
            float g = 900 * S;
            Vector2 v = (to - from) / time - new Vector2(0, 0.5f * g * time) + new Vector2(0, rng.Range(-60, 60) * S);
            w.Projectiles.Add(new Projectile(Ammo.Snow, from, v, f, S, false));
            World.Play(Sfx.Swish, from, 0.3f, 1.3f);
            _snowballs--;
        }
        if (_snowballs <= 0 && c > 1.4f)
        {
            // While it's still flying both ways, it's one snowball fight: a breather, then more.
            if (_t0 - _snowHitAt < 6 && Stamina > 0.25f && rng.NextDouble() < 0.75) _snowballs = rng.Next(1, 3);
            else Go(G.Idle, rng.Range(0.8f, 2));
        }
    }

    /// <summary>Hit by a snowball.</summary>
    public void OnSnowballed(Figure? by, World w)
    {
        if (f.Mode != Mode.Control) return;
        f.Emote(P.Playfulness > 0.5f ? "ha!" : "hey!", 1);
        if (by == null) return;
        if (P.Playfulness > 0.45f) { AddAffinity(by, 0.02f); Cheered(0.05f); }
        else { AddAffinity(by, -0.03f); Annoyance = M.Clamp01(Annoyance + 0.08f); }
        Write("snowball:" + by.Name, V($"Snowball fight with {by.Name}!", $"Epic snowball fight with {by.Name}!!", $"{by.Name} threw a snowball at me. War.", $"{by.Name} hit me with a snowball. It went down my back."), "❄", 1200);
        _snowHitAt = _t0;
        // Throw one back (already at it with them: just one more on the pile).
        if (_g == G.Snowball && _snowTarget == by) { _snowballs = Math.Min(_snowballs + 1, 4); return; }
        if (P.Playfulness > 0.4f && Stamina > 0.3f && f.Grounded && _g is G.Idle or G.Walk or G.SitFloor or G.Snowball && rng.NextDouble() < 0.65)
            BeginSnowball(by);
    }

    void DoSnowman(World w)
    {
        f.DesiredVX = 0;
        if (!f.Grounded) { Go(G.Idle, 1); return; }
        f.DuckT = 0.2f;
        f.SetAction((int)(_t * 3) % 2 == 0 ? Act.Tap : Act.Stand);
        if ((int)(_t * 4) != (int)((_t - World.Dt) * 4)) w.Fx.Dust(f.Base + new Vector2(f.Facing * 14 * S, 0), S * 0.6f, 2, 0.2f, w.Rng);
        if (_t < _dur) return;
        if (w.MakeItem?.Invoke("snowman") is { } man)
        {
            man.Pos = f.Base + new Vector2(f.Facing * 26 * S, -2 * S);
            man.Vel = Vector2.Zero;
            man.OnGround = false;
            f.Emote("☃ ta-da!", 1.5f);
            Cheered(0.3f);
            w.Sticker("snowman");
            w.News("snowman", $"{f.Name} built a snowman", 1, f);
            Write("snowman", V("Built a snowman!", "Built the BEST snowman!", "Built a snowman. He's judging me.", "Built a little snowman. I named him.", "Built a snowman. He'll melt. Everything does."), "☃", 1800);
        }
        Go(G.Idle, rng.Range(1, 2));
    }
}
