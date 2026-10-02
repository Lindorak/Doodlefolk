using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum WeatherKind { Clear, Rain, Storm, Snow }

/// <summary>Weather on the desktop: now and then a shower, a thunderstorm or (in the colder months) snow. Not real
/// weather, nothing is looked up online; how often it happens is a setting. Raindrops splash on window tops and patter;
/// snow settles in layers on whatever they can stand on and melts away afterwards; storms flash and rumble.</summary>
sealed class Weather
{
    public WeatherKind Kind { get; private set; }
    /// <summary>0..1, easing in when it starts and out when it stops.</summary>
    public float Intensity;
    public float Wind;
    public float Flash;
    public bool Raining => Kind is WeatherKind.Rain or WeatherKind.Storm && Intensity > 0.15f;
    public bool Snowing => Kind == WeatherKind.Snow && Intensity > 0.15f;
    public bool Active => Intensity > 0.01f || Snow.Count > 0 || Flash > 0.01f || _splashes.Count > 0;

    /// <summary>Snow lying on surfaces, by surface: depth in pixels.</summary>
    public readonly Dictionary<NavKey, float> Snow = new();
    public float SnowOn(Platform p) => Snow.TryGetValue(NavGraph.Key(p), out var d) ? d : 0;

    struct Drop { public Vector2 Pos, Prev; public float Size, Phase; }
    readonly List<Drop> _drops = new();
    readonly List<(Vector2 at, float t)> _splashes = new();
    double _until, _nextAt = -1, _thunderAt, _rumbleAt = -1;
    float _target;

    /// <summary>Start some weather now (for its natural length), or Clear to stop it.</summary>
    public void Start(WeatherKind k, double now, Random rng, World w)
    {
        var was = Kind;
        Kind = k;
        _target = k == WeatherKind.Clear ? 0 : 1;
        _until = now + (k == WeatherKind.Storm ? rng.Range(100, 220) : rng.Range(120, 360));
        Wind = rng.Range(-260, 260) * w.Scale;
        _thunderAt = now + rng.Range(4, 10);
        if (k != was) w.OnWeather(k);
    }

    void Schedule(double now, string mode, Random rng)
    {
        double gap = mode switch { "often" => rng.Range(300, 900), "rare" => rng.Range(2400, 5400), _ => rng.Range(900, 2400) };
        _nextAt = now + gap;
    }

    static WeatherKind Natural(Random rng)
    {
        int m = DateTime.Now.Month;
        double snow = m is 12 or 1 or 2 ? 0.65 : m is 11 or 3 ? 0.25 : 0;
        if (rng.NextDouble() < snow) return WeatherKind.Snow;
        return rng.NextDouble() < 0.3 ? WeatherKind.Storm : WeatherKind.Rain;
    }

    public void Step(World w, float dt, double now, string mode)
    {
        var rng = w.Rng;
        float S = w.Scale;
        // The sky's own schedule.
        if (_nextAt < 0) Schedule(now, mode, rng);
        if (Kind == WeatherKind.Clear && mode != "off" && now > _nextAt) { Start(Natural(rng), now, rng, w); Schedule(now, mode, rng); }
        if (Kind != WeatherKind.Clear && (now > _until || (mode == "off" && _target > 0 && now > _until))) { _target = 0; }
        if (_target == 0 && Kind != WeatherKind.Clear && Intensity < 0.02f) { Kind = WeatherKind.Clear; w.OnWeather(WeatherKind.Clear); }
        Intensity = M.MoveTowards(Intensity, _target, dt * 0.12f);

        // Thunder: a flash, the rumble a moment later.
        Flash = MathF.Max(0, Flash - dt * 5);
        if (Kind == WeatherKind.Storm && Intensity > 0.5f && now > _thunderAt)
        {
            _thunderAt = now + rng.Range(7, 20);
            Flash = 1;
            _rumbleAt = now + rng.Range(0.3f, 1.6f);
            w.OnThunder();
        }
        if (_rumbleAt > 0 && now > _rumbleAt) { _rumbleAt = -1; World.Play(Sfx.Thunder, new Vector2(w.Env.Virtual.Left + w.Env.Virtual.Width / 2f, 0), 0.8f); }

        // Drops and flakes.
        var env = w.Env;
        var v = env.Virtual;
        bool snow = Kind == WeatherKind.Snow;
        int want = (int)(Intensity * (snow ? 170 : 260) * v.Width / 3840f);
        while (_drops.Count < want)
        {
            float x = rng.Range(v.Left - 100, v.Right + 100);
            var (_, _, top) = env.BoundsAt(M.ClampIn(x, v.Left, v.Right - 1));
            var p = new Vector2(x, top - rng.Range(0, snow ? 600 : 300) * S);
            _drops.Add(new Drop { Pos = p, Prev = p, Size = rng.Range(0.7f, 1.3f), Phase = rng.Range(0, 6.3f) });
        }
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Prev = d.Pos;
            Vector2 vel = snow
                ? new Vector2(Wind * 0.3f + MathF.Sin((float)now * 1.3f + d.Phase) * 30 * S, (80 + 50 * d.Size) * S)
                : new Vector2(Wind, (1300 + 300 * d.Size) * S);
            d.Pos += vel * dt;
            bool gone = d.Pos.Y > v.Bottom + 20 || (i >= want && rng.NextDouble() < dt * 2);
            if (!gone && d.Pos.Y > d.Prev.Y && env.FindLanding(d.Pos.X, d.Prev.Y, d.Pos.Y) is { } hit)
            {
                gone = true;
                if (snow)
                {
                    var k = NavGraph.Key(hit);
                    Snow[k] = MathF.Min(9 * S, (Snow.TryGetValue(k, out var dd) ? dd : 0) + 18 * S * S / MathF.Max(hit.X2 - hit.X1, 60 * S) * d.Size);
                }
                else if (_splashes.Count < 120) _splashes.Add((new Vector2(d.Pos.X, hit.Y), 0));
            }
            if (gone) _drops.RemoveAt(i); else _drops[i] = d;
        }
        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            var (at, t) = _splashes[i];
            t += dt;
            if (t > 0.25f) _splashes.RemoveAt(i); else _splashes[i] = (at, t);
        }
        // Snow melts once it stops.
        if (!Snowing && Snow.Count > 0)
            foreach (var k in Snow.Keys.ToList())
            {
                float dd = Snow[k] - dt * 0.035f * S;
                if (dd <= 0.1f) Snow.Remove(k); else Snow[k] = dd;
            }
        w.Sound?.Rain(Raining ? Intensity * (Kind == WeatherKind.Storm ? 1.3f : 1) : 0);
    }

    /// <summary>Snow lying on surfaces (under figures and things).</summary>
    public void DrawCover(Renderer r, Env env, float S)
    {
        if (Snow.Count == 0) return;
        var white = new Color4(0.97f, 0.98f, 1, 0.95f);
        var edge = new Color4(0.75f, 0.8f, 0.9f, 0.7f);
        foreach (var p in env.Platforms)
        {
            if (!Snow.TryGetValue(NavGraph.Key(p), out var d) || d < 0.4f) continue;
            float y = p.Y - d / 2;
            r.Line(new Vector2(p.X1 + 2, y + 0.4f), new Vector2(p.X2 - 2, y + 0.4f), edge, d + 1.2f);
            r.Line(new Vector2(p.X1 + 2, y), new Vector2(p.X2 - 2, y), white, d);
            // A few soft lumps.
            for (float x = p.X1 + 14 * S; x < p.X2 - 14 * S; x += 37 * S)
                r.Oval(new Vector2(x + (MathF.Sin(x) * 6 * S), p.Y - d), d * 0.9f, d * 0.45f, white);
        }
    }

    /// <summary>Falling rain or snow, splashes and lightning (in front of everything).</summary>
    public void DrawSky(Renderer r, Env env, float S)
    {
        bool snow = Kind == WeatherKind.Snow;
        var rain = new Color4(0.62f, 0.72f, 0.9f, 0.55f);
        foreach (var d in _drops)
        {
            if (snow) { r.Disc(d.Pos, (1.4f + d.Size) * S, new Color4(0.6f, 0.65f, 0.75f, 0.35f)); r.Disc(d.Pos, (1.0f + d.Size * 0.9f) * S, new Color4(1, 1, 1, 0.92f)); }
            else
            {
                Vector2 dir = Vector2.Normalize(new Vector2(Wind, 1500 * S));
                r.Line(d.Pos - dir * (14 + 10 * d.Size) * S, d.Pos, rain, 1.1f * S);
            }
        }
        foreach (var (at, t) in _splashes)
        {
            float k = t / 0.25f, a = 1 - k;
            var c = new Color4(0.62f, 0.72f, 0.9f, 0.6f * a);
            r.Line(at, at + new Vector2(-5 * S * k - 1, -5 * S * (1 - k) - 1), c, S);
            r.Line(at, at + new Vector2(5 * S * k + 1, -5 * S * (1 - k) - 1), c, S);
        }
        if (Flash > 0.01f)
        {
            var v = env.Virtual;
            r.FillPolygon(stackalloc Vector2[] { new(v.Left, v.Top), new(v.Right, v.Top), new(v.Right, v.Bottom), new(v.Left, v.Bottom) }, new Color4(1, 1, 1, 0.1f * Flash));
        }
    }
}

sealed partial class World
{
    public readonly Weather Weather = new();
    /// <summary>0 in the day, 1 deep in the night (from the real clock), easing through dusk and dawn.</summary>
    public float Night;
    public bool DayNight = true;

    public void UpdateClock()
    {
        if (!DayNight) { Night = 0; return; }
        var t = DateTime.Now;
        float h = t.Hour + t.Minute / 60f;
        // 21:00 → 23:00 getting dark, 05:30 → 07:30 getting light.
        Night = h >= 21 ? M.Clamp01((h - 21) / 2) : h < 7.5f ? M.Clamp01((7.5f - h) / 2) : 0;
    }

    public void OnWeather(WeatherKind k)
    {
        World.Log($"weather: {k}");
        foreach (var f in Figures.ToArray()) f.Brain.OnWeatherChanged(k, this);
    }

    public void OnThunder()
    {
        foreach (var f in Figures.ToArray()) f.Brain.OnThunder(this);
    }
}
