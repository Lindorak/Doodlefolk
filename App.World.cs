using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>The world's slow rhythms: trinkets turning up for collectors, plants growing (and drying out), and the
/// seasons (falling leaves, blossom, fireflies, pumpkins at Halloween, a tree at Christmas).</summary>
sealed partial class App
{
    double _trinketAt = 60, _gardenAt, _decorAt = 5, _seasonLast;
    static readonly string[] TrinketKeys = { "marble", "button", "seashell", "feather", "bottlecap", "coin", "gem" };
    static readonly string[] PlantStages = { "seedpatch", "sprout", "bud" };
    const float StageSeconds = 900;

    /// <summary>Who's lit by a campfire (and, at night, dimmed away from one).</summary>
    void LightFrame(double now)
    {
        var fires = _w.Items.Where(i => i.Def.Key == "campfire" && i.Holder == null).ToList();
        float night = _settings.DayNight ? _w.Night : 0;
        // Lamps, fairy lights, lanterns and lit windows (only after dark).
        var lamps = new List<(Vector2 at, float reach, float strength)>();
        foreach (var it in _w.Items) if (it.Light() is { } l) lamps.Add(l);
        float Warm(Vector2 at, float s)
        {
            float best = 0;
            foreach (var fi in fires)
            {
                float d = Vector2.Distance(at, fi.Pos), reach = (180 + 120 * night) * s;
                if (d < reach) best = MathF.Max(best, MathF.Pow(1 - d / reach, 1.6f) * (0.45f + 0.55f * night) * Item.Flicker(now, fi.Id));
            }
            foreach (var (lat, reach, strength) in lamps)
            {
                float d = Vector2.Distance(at, lat);
                if (d < reach) best = MathF.Max(best, MathF.Pow(1 - d / reach, 1.4f) * strength);
            }
            return best;
        }
        foreach (var f in _w.Figures) { f.Warmth = Warm(f.Jt[J.Pelvis], f.S); f.NightDim = night * 0.3f * (1 - MathF.Min(1, f.Warmth * 2.5f)); }
        foreach (var p in _w.Pets) { p.Warmth = Warm(p.Centre, p.Scale); p.NightDim = night * 0.3f * (1 - MathF.Min(1, p.Warmth * 2.5f)); }
        // A crackle now and then.
        foreach (var fi in fires) if (_w.Rng.NextDouble() < 0.04) World.Play(Sfx.Crackle, fi.Pos, 0.18f, (float)_w.Rng.Range(0.8f, 1.3f), 0.05);
    }

    void WorldFrame(double now)
    {
        LightFrame(now);
        float dt = (float)Math.Clamp(now - _seasonLast, 0, 0.1);
        _seasonLast = now;
        _w.Seasons.Step(_w, dt, now);

        if (now > _trinketAt)
        {
            _trinketAt = now + _w.Rng.Range(240, 540);
            if (_w.Items.Count(i => i.Def.Verbs.Contains(Verb.Collect)) < 3 && _w.Items.Count < 55 && !PetMode
                && ItemCatalog.Find(TrinketKeys[_w.Rng.Next(TrinketKeys.Length)]) is { } tdef)
                SpawnItem(tdef);
        }
        if (now > _gardenAt) { _gardenAt = now + 5; GardenTick(5); }
        if (now > _decorAt) { _decorAt = now + 60; Decorate(); }
    }

    /// <summary>Plants drink their water and, while they have some, grow a stage every quarter of an hour or so.</summary>
    void GardenTick(float dt)
    {
        // The fish get peckish over a few hours.
        foreach (var tank in _w.Items.Where(i => i.Def.Key == "fishtank")) tank.Fill = MathF.Max(0, tank.Fill - dt / (5 * 3600));
        foreach (var it in _w.Items.Where(i => i.IsPlant).ToList())
        {
            if (_w.Weather.Raining) it.Fill = 1;
            it.Fill = MathF.Max(0, it.Fill - dt / 1800);
            if (it.Fill > 0.05f) it.Growth += dt / StageSeconds;
            int stage = Array.IndexOf(PlantStages, it.Def.Key);
            if (stage < 0 || it.Growth < 1) continue;
            string next = stage + 1 < PlantStages.Length ? PlantStages[stage + 1] : (it.PlantKind.Length > 0 ? it.PlantKind : "tulip");
            if (ItemCatalog.Find(next) is not { } def || SpawnItem(def, it.SizeMul, next == "tulip" ? Palette.All[_w.Rng.Next(Palette.All.Length)].Color : null, it.Flip) is not { } grown) continue;
            grown.Pos = it.Pos; grown.Vel = Vector2.Zero; grown.OnGround = false;
            grown.Fill = it.Fill; grown.PlantKind = it.PlantKind; grown.PlanterId = it.PlanterId; grown.Growth = 0;
            _w.RemoveItem(it);
            if (stage + 1 >= PlantStages.Length)
            {
                _w.Sticker("bloom");
                var planter = _w.Figures.FirstOrDefault(f => f.Id == grown.PlanterId);
                planter?.Brain.PlantBloomed(grown, _w);
                _w.News("garden", planter != null ? $"{planter.Name}'s {grown.Def.Name.ToLowerInvariant()} is in bloom" : $"A {grown.Def.Name.ToLowerInvariant()} bloomed", 1);
            }
        }
    }

    /// <summary>Holiday decorations come out (and go away again afterwards).</summary>
    void Decorate()
    {
        bool halloween = _w.Celebrations && _w.Holiday == Holiday.Halloween, xmas = _w.Celebrations && _w.Holiday == Holiday.Christmas;
        foreach (var it in _w.Items.Where(i => i.Temporary && (i.Def.Key == "pumpkin" && !halloween || i.Def.Key == "xmastree" && !xmas)).ToList()) _w.RemoveItem(it);
        void Put(string key, int count)
        {
            if (_w.Items.Count(i => i.Def.Key == key) >= count || ItemCatalog.Find(key) is not { } def) return;
            // Beside someone's home if there is one, otherwise anywhere on the floor.
            var homes = _w.Items.Where(i => i.OwnerId != 0).ToList();
            if (SpawnItem(def) is not { } it) return;
            it.Temporary = true;
            if (homes.Count > 0) { var h = homes[_w.Rng.Next(homes.Count)]; it.Pos = h.Pos + new Vector2((h.Def.W * h.Sc * 0.5f + 20 * _w.Scale) * (_w.Rng.NextDouble() < 0.5 ? -1 : 1), -4); it.Vel = Vector2.Zero; it.OnGround = false; }
        }
        if (halloween) Put("pumpkin", 2);
        if (xmas) Put("xmastree", 1);
    }
}
