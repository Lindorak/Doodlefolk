using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

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

    /// <summary>Snow lying on surfaces, by surface: its average depth in pixels (the shape of it is in _drift).</summary>
    public readonly Dictionary<NavKey, float> Snow = new();
    public float SnowOn(Platform p) => Snow.TryGetValue(NavGraph.Key(p), out var d) ? d : 0;

    /// <summary>Whether snow (and leaves) stay where they land. Off: it still falls, but melts as it touches down.</summary>
    public static bool Piles = true;

    /// <summary>The shape of the snow on each surface: its depth every few pixels along it. Flakes pile where they land
    /// and slump to a natural slope; it thins off the ends, takes footprints, and melts patchily (thin spots first,
    /// and fastest beside a fire).</summary>
    readonly Dictionary<NavKey, float[]> _drift = new();
    const float BinPx = 4;
    double _nextSettle;

    float[] Profile(Platform p, float S)
    {
        float bin = BinPx * S;
        int n = Math.Max(3, (int)MathF.Ceiling((p.X2 - p.X1) / bin) + 1);
        var k = NavGraph.Key(p);
        if (_drift.TryGetValue(k, out var a) && a.Length == n) return a;
        var b = new float[n];
        if (a != null) for (int i = 0; i < n; i++) b[i] = a[Math.Min(a.Length - 1, i * a.Length / n)];   // the surface was resized
        return _drift[k] = b;
    }

    void Land(Platform p, float x, float size, float S)
    {
        var a = Profile(p, S);
        float bin = BinPx * S;
        int c = (int)MathF.Round((x - p.X1) / bin);
        float depth = 18 * S * S * size / bin;   // the same amount of snow per flake as ever, now where it fell
        ReadOnlySpan<float> spread = stackalloc float[] { 0.12f, 0.22f, 0.32f, 0.22f, 0.12f };
        for (int j = 0; j < 5; j++)
        {
            int i = c + j - 2;
            if (i >= 0 && i < a.Length) a[i] = MathF.Min(16 * S, a[i] + depth * spread[j]);
        }
    }

    /// <summary>A foot coming down on snow: a print pressed in, the snow pushed up a little round it. Returns how deep
    /// the snow was there (for the crunch).</summary>
    public float Footprint(Env env, Vector2 foot, float S)
    {
        if (_drift.Count == 0 || env.SupportAt(foot.X, foot.Y, IntPtr.Zero) is not { } p || !_drift.TryGetValue(NavGraph.Key(p), out var a)) return 0;
        float bin = BinPx * S;
        int c = (int)MathF.Round((foot.X - p.X1) / bin);
        if (c < 0 || c >= a.Length) return 0;
        float was = a[c], pushed = 0;
        for (int i = c - 1; i <= c + 1; i++)
        {
            if (i < 0 || i >= a.Length) continue;
            float keep = i == c ? 0.3f : 0.6f;
            float d = a[i] * (1 - keep);
            a[i] -= d; pushed += d;
        }
        if (c - 2 >= 0) a[c - 2] += pushed * 0.2f;
        if (c + 2 < a.Length) a[c + 2] += pushed * 0.2f;
        return was;
    }

    /// <summary>Snow laid down along a surface as if it had been snowing a while (contact sheets, tests).</summary>
    public void Dust(Platform p, float x1, float x2, int flakes, Random rng, float S)
    {
        for (int i = 0; i < flakes; i++) Land(p, rng.Range(x1, x2), rng.Range(0.7f, 1.3f), S);
        _nextSettle = 0;
        Snow[NavGraph.Key(p)] = 1;
    }

    /// <summary>All the lying snow gone at once (Studio: "Clear away snow and leaves").</summary>
    public void ClearCover() { Snow.Clear(); _drift.Clear(); }

    /// <summary>Stop now: no fading out (weather switched off).</summary>
    public void StopNow(World w)
    {
        _target = 0; Intensity = 0; Flash = 0; _drops.Clear(); _splashes.Clear();
        if (Kind != WeatherKind.Clear) { Kind = WeatherKind.Clear; w.OnWeather(WeatherKind.Clear); }
    }

    /// <summary>Ten times a second: snow slumps to its natural slope, falls off the ends, and melts.</summary>
    void Settle(World w, float dt, float S)
    {
        float bin = BinPx * S, maxStep = 0.55f * bin;
        bool melting = !Snowing || !Piles;
        float melt = dt * 0.035f * S * (Piles ? 1 : 12);
        var fires = w.Items.Where(i => i.Def.Verbs.Contains(Verb.Warm) && i.Holder == null).ToList();
        foreach (var p in w.Env.Platforms)
        {
            var k = NavGraph.Key(p);
            if (!_drift.TryGetValue(k, out var a)) continue;
            if (a.Length != Math.Max(3, (int)MathF.Ceiling((p.X2 - p.X1) / bin) + 1)) a = Profile(p, S);
            int n = a.Length;
            for (int pass = 0; pass < 2; pass++)
                for (int j = 0; j < n - 1; j++)
                {
                    int i = pass == 0 ? j : n - 2 - j;
                    float diff = a[i] - a[i + 1];
                    if (MathF.Abs(diff) <= maxStep) continue;
                    float move = (MathF.Abs(diff) - maxStep) * 0.5f;
                    if (diff > 0) { a[i] -= move; a[i + 1] += move; } else { a[i] += move; a[i + 1] -= move; }
                }
            // Nothing holds snow at the very ends: it rounds off there.
            a[0] = MathF.Min(a[0], a[1] * 0.55f); a[n - 1] = MathF.Min(a[n - 1], a[n - 2] * 0.55f);
            float sum = 0;
            for (int i = 0; i < n; i++)
            {
                if (melting) a[i] = MathF.Max(0, a[i] - melt * (0.55f + 0.9f * (((i * 7919 + k.X1) & 255) / 255f)));   // patchy
                foreach (var f in fires)
                {
                    if (MathF.Abs(f.Pos.Y - p.Y) > 60 * S) continue;
                    float dx = MathF.Abs(p.X1 + i * bin - f.Pos.X);
                    if (dx < 130 * S) a[i] = MathF.Max(0, a[i] - dt * 1.2f * S * (1 - dx / (130 * S)));
                }
                sum += a[i];
            }
            if (sum / n < 0.08f && a.Max() < 0.4f) { _drift.Remove(k); Snow.Remove(k); }
            else Snow[k] = sum / n;
        }
        // Surfaces that have gone (a window closed or moved) take their snow with them.
        if (_drift.Count > 0)
        {
            var live = new HashSet<NavKey>(w.Env.Platforms.Select(NavGraph.Key));
            foreach (var k in _drift.Keys.Where(k => !live.Contains(k)).ToList()) { _drift.Remove(k); Snow.Remove(k); }
        }
    }

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

    /// <summary>Keep to this weather (the real weather outside) until told otherwise.</summary>
    public void Hold(WeatherKind k, double now, Random rng, World w, float windKmh)
    {
        if (k != Kind || (k != WeatherKind.Clear && _target == 0)) Start(k, now, rng, w);
        _until = now + 3600;
        Wind = MathF.Sign(Wind == 0 ? 1 : Wind) * Math.Clamp(windKmh * 6, 20, 320) * w.Scale;
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
        return rng.NextDouble() < 0.3 * World.Drama && !World.Calm ? WeatherKind.Storm : WeatherKind.Rain;
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
            Flash = World.Calm ? 0 : 1;
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
                if (snow && Piles) Land(hit, d.Pos.X, d.Size, S);
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
        if (_drift.Count > 0 && now >= _nextSettle) { Settle(w, (float)Math.Min(0.5, now - _nextSettle + 0.1), S); _nextSettle = now + 0.1; }
        w.Sound?.Rain(Raining ? Intensity * (Kind == WeatherKind.Storm ? 1.3f : 1) : 0);
    }

    /// <summary>Snow lying on surfaces (under figures and things).</summary>
    public void DrawCover(Renderer r, Env env, float S)
    {
        if (_drift.Count == 0) return;
        var white = new Color4(0.97f, 0.98f, 1, 0.97f);
        var shade = new Color4(0.7f, 0.77f, 0.9f, 0.85f);
        var cool = new Color4(0.86f, 0.9f, 0.98f, 0.9f);
        var line = new Color4(0.5f, 0.58f, 0.76f, 0.9f);
        float bin = BinPx * S;
        double t = Environment.TickCount64 / 1000.0;
        Span<Vector2> top = stackalloc Vector2[132];
        Span<Vector2> poly = stackalloc Vector2[140];
        foreach (var p in env.Platforms)
        {
            if (!_drift.TryGetValue(NavGraph.Key(p), out var a)) continue;
            int n = a.Length, step = Math.Max(1, (n + 127) / 128);
            // The surface of the snow, smoothed a touch, sampled along the ledge.
            int m = 0;
            float peak = 0;
            for (int i = 0; i < n; i += step)
            {
                float d = (a[Math.Max(0, i - 1)] + a[i] * 2 + a[Math.Min(n - 1, i + 1)]) / 4;
                peak = MathF.Max(peak, d);
                top[m++] = new Vector2(MathF.Min(p.X2, p.X1 + i * bin), p.Y - d);
            }
            if (top[m - 1].X < p.X2 - 0.5f) top[m++] = new Vector2(p.X2, p.Y - a[n - 1]);
            if (peak < 0.5f) continue;
            // Rounded caps that droop just over each end, like snow on a real ledge.
            float lip0 = MathF.Min(a[Math.Min(2, n - 1)] * 0.45f, 5 * S), lip1 = MathF.Min(a[Math.Max(0, n - 3)] * 0.45f, 5 * S);
            int q = 0;
            poly[q++] = new Vector2(p.X1 - lip0 * 0.6f, p.Y + lip0 * 0.35f);
            poly[q++] = new Vector2(p.X1 - lip0 * 0.75f, p.Y - lip0 * 0.4f);
            for (int i = 0; i < m; i++) poly[q++] = top[i];
            poly[q++] = new Vector2(p.X2 + lip1 * 0.75f, p.Y - lip1 * 0.4f);
            poly[q++] = new Vector2(p.X2 + lip1 * 0.6f, p.Y + lip1 * 0.35f);
            var body = poly[..q];
            // A blue-grey shadow underneath and round the edge, the white body, and a cooler band low down for depth.
            for (int i = 0; i < q; i++) poly[i] += new Vector2(0, 0.9f * S);
            r.FillPolygon(body, shade);
            for (int i = 0; i < q; i++) poly[i] -= new Vector2(0, 0.9f * S);
            r.FillPolygon(body, white);
            r.Line(new Vector2(p.X1 + 1, p.Y - 0.6f * S), new Vector2(p.X2 - 1, p.Y - 0.6f * S), cool, 1.4f * S);
            // Drawn in, like everything else here: a soft blue-grey line along the top (it shows on any background).
            r.Polyline(poly[..q], line, 0.9f * S);
            // The odd glint where it's deep enough (the low ones are hidden by people walking through).
            if (Gfx.Q.DetailedArt)
                for (int i = 3; i < n - 3; i += 9)
                {
                    int h = (i * 2654435761u + (uint)p.X1).GetHashCode() & 1023;
                    if (a[i] < 3 * S || h > 300) continue;
                    float tw = MathF.Max(0, MathF.Sin((float)t * (1.3f + (h & 7) * 0.2f) + h));
                    if (tw < 0.55f) continue;
                    var at = new Vector2(p.X1 + i * bin, p.Y - a[i] * 0.85f);
                    float sz = (tw - 0.55f) * 3.2f * S;
                    var g = new Color4(1, 1, 1, (tw - 0.55f) * 2);
                    r.Line(at - new Vector2(sz, 0), at + new Vector2(sz, 0), g, 0.7f * S);
                    r.Line(at - new Vector2(0, sz), at + new Vector2(0, sz), g, 0.7f * S);
                }
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

    /// <summary>Debug: pretend it's this dark (null: follow the clock).</summary>
    public float? NightOverride;

    public void UpdateClock()
    {
        if (NightOverride is float no) { Night = no; return; }
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
