using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>The toybox (an idea from Interactive Buddy): moon gravity, a giant ball, a gust of wind, an earthquake,
/// slow motion, confetti. Just for fun, all temporary: gravity drifts back to normal after a few minutes (or stays, if
/// you'd rather), slow motion after twenty seconds.</summary>
sealed partial class App
{
    public static readonly (string key, string name, float mul)[] GravityModes =
    {
        ("normal", "Normal", 1), ("moon", "Moon", 0.3f), ("low", "Low", 0.6f), ("heavy", "Heavy", 1.7f), ("floaty", "Floaty", 0.12f),
    };
    string _gravityMode = "normal";
    double _gravityUntil, _slowUntil;
    double _baseClockScale = 1;

    public string SetGravity(string mode)
    {
        var g = GravityModes.FirstOrDefault(x => x.key == mode);
        if (g.key == null) return "no such gravity";
        _gravityMode = g.key;
        World.GravityMul = g.mul;
        _gravityUntil = g.key == "normal" || _settings.ToyboxMinutes <= 0 ? 0 : _clock.Elapsed.TotalSeconds + _settings.ToyboxMinutes * 60;
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep))
            if (_w.Rng.NextDouble() < 0.6) f.Emote(World.Gestures ? (g.mul < 1 ? "🌙" : "😮") : g.key switch
            {
                "moon" => new[] { "I'm so light!", "boing!", "one small step…", "WHEEE!" }[_w.Rng.Next(4)],
                "floaty" => new[] { "I'm floating!", "whoaaa…", "up we go!", "help, I'm drifting!" }[_w.Rng.Next(4)],
                "heavy" => new[] { "so… heavy…", "oof!", "my legs!", "is it me or…" }[_w.Rng.Next(4)],
                "low" => new[] { "bouncy!", "light on my feet!", "huh, springy" }[_w.Rng.Next(3)],
                _ => new[] { "back to normal", "solid ground!", "phew" }[_w.Rng.Next(3)],
            }, 1.6f);
        if (g.key != "normal") _w.Sticker("toybox");
        return g.key == "normal" ? "Gravity's back to normal." : $"{g.name} gravity{(_gravityUntil > 0 ? $" for {_settings.ToyboxMinutes} minutes" : "")}.";
    }

    public string GiantBall()
    {
        if (_w.Props.Count(p => p.SizeMul >= 3) >= 2) return "There are already two giant balls out.";
        var b = SpawnProp(PropKind.BeachBall);
        b.SizeMul = 4.5f;
        b.Vel = new Vector2(_w.Rng.Range(-200, 200) * _w.Scale, 0);
        _w.Sticker("toybox");
        return "A giant ball!";
    }

    public string Gust()
    {
        float dir = _w.Rng.NextDouble() < 0.5 ? -1 : 1, S = _w.Scale;
        foreach (var p in _w.Props.Where(p => p.Holder == null && !p.Pinned)) { p.Vel += new Vector2(dir * _w.Rng.Range(500, 900) * S / MathF.Max(1, p.SizeMul * 0.6f), -_w.Rng.Range(150, 400) * S); p.OnGround = false; }
        foreach (var it in _w.Items.Where(i => i.Holder == null && i.Def.Mass <= 1 && !i.IsPlant)) { it.Vel += new Vector2(dir * _w.Rng.Range(300, 600) * S, -_w.Rng.Range(100, 300) * S); it.OnGround = false; }
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && f.Grounded))
        {
            if (_w.Rng.NextDouble() < 0.35 + f.SizeMul * -0.1) f.GoRagdoll(new Vector2(dir * _w.Rng.Range(250, 500), -_w.Rng.Range(150, 300)) * S);
            else f.Emote(World.Gestures ? "🌬" : new[] { "whoa!", "windy!", "my hat!", "hold on!" }[_w.Rng.Next(4)], 1.2f);
        }
        _w.Seasons.Gust(_clock.Elapsed.TotalSeconds);
        return "Whoosh!";
    }

    public string Earthquake()
    {
        float S = _w.Scale;
        _w.HitStop = 0;
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && f.Grounded && f.Riding == null && !f.Brain.Asleep))
            if (_w.Rng.NextDouble() < 0.7) f.GoRagdoll(new Vector2(_w.Rng.Range(-260, 260), -_w.Rng.Range(200, 420)) * S);
        foreach (var p in _w.Props.Where(p => p.Holder == null && !p.Pinned)) { p.Vel += new Vector2(_w.Rng.Range(-200, 200), -_w.Rng.Range(300, 600)) * S; p.OnGround = false; }
        foreach (var it in _w.Items.Where(i => i.Holder == null && i.Def.Mass <= 2 && !i.IsPlant)) { it.Vel += new Vector2(_w.Rng.Range(-120, 120), -_w.Rng.Range(150, 350)) * S; it.OnGround = false; }
        foreach (var pet in _w.Pets.Where(pt => pt.Grounded)) { pet.Vel += new Vector2(_w.Rng.Range(-100, 100), -_w.Rng.Range(250, 400)) * S; pet.Grounded = false; }
        World.Play(Sfx.Thunder, _w.Cursor, 0.5f, 0.5f);
        return "The ground shakes!";
    }

    public string SlowMotion()
    {
        if (_slowUntil == 0) _baseClockScale = _clock.Scale;
        _clock.Scale = _baseClockScale * 0.35;
        _slowUntil = _clock.Elapsed.TotalSeconds + 20 * 0.35;   // twenty real seconds, in slowed time
        return "Sloooow mooootion…";
    }

    public string Confetti()
    {
        var v = _w.Env.Virtual;
        var cols = new[] { M.Hex(0xE53935), M.Hex(0xFDD835), M.Hex(0x43A047), M.Hex(0x1E88E5), M.Hex(0x8E24AA), M.Hex(0xFB8C00) };
        for (int i = 0; i < 40; i++)
            _w.Fx.Spark(new Vector2(_w.Rng.Range(v.Left + 60, v.Right - 60), _w.Rng.Range(v.Top + 80, v.Top + v.Height * 0.5f)), _w.Scale * 1.2f, _w.Rng, 1.6f, cols[_w.Rng.Next(cols.Length)]);
        foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep)) { f.Brain.Cheered(0.2f); if (_w.Rng.NextDouble() < 0.5) f.Emote(World.Gestures ? "🎉" : new[] { "yay!", "woo!", "party!", "confetti!!" }[_w.Rng.Next(4)], 1.4f); }
        World.Play(Sfx.TaDa, _w.Cursor, 0.4f, 1.2f);
        return "Confetti!";
    }

    void ToyboxFrame(double now)
    {
        if (_gravityUntil > 0 && now > _gravityUntil) { _gravityUntil = 0; SetGravity("normal"); }
        if (_slowUntil > 0 && now > _slowUntil) { _slowUntil = 0; _clock.Scale = _baseClockScale; }
    }

    object ToyboxState() => new { gravity = _gravityMode, gravityLeft = _gravityUntil > 0 ? (int)(_gravityUntil - _clock.Elapsed.TotalSeconds) : 0, minutes = _settings.ToyboxMinutes, slow = _slowUntil > 0 };

    string Toybox(string op) => op switch
    {
        "ball" => GiantBall(), "gust" => Gust(), "quake" => Earthquake(), "slow" => SlowMotion(), "confetti" => Confetti(),
        _ when op.StartsWith("gravity:") => SetGravity(op[8..]),
        _ => "",
    };
}
