using System.Numerics;

namespace Doodlefolk;

sealed partial class Brain
{
    string _mourning = "";
    float _mournUntil, _flowersCd;

    /// <summary>Someone it cared about has died.</summary>
    public void Grieve(string name, float care, World w)
    {
        Saddened(0.35f + care * 0.45f);
        _mourning = name;
        _mournUntil = _t0 + 1800 * care;
        _flowersCd = _t0 + rng.Range(60, 240);
        f.Emote(Gestures ? "💔" : V($"{name}…", $"NO! {name.ToUpperInvariant()}!", $"…{name}.", $"oh, {name}…", $"goodbye, {name}"), 3);
        Write("grief:" + name, V($"We lost {name} today. I'll miss them.", $"{name.ToUpperInvariant()} IS GONE. I can't believe it.", $"{name}'s gone. Didn't think it'd hit this hard.", $"{name} is gone… I keep looking for them.", $"{name} has gone on ahead. The desktop is quieter."), "💔", 0);
    }

    /// <summary>Saw the ghost of someone it knew.</summary>
    public void SawGhostOf(string name, World w)
    {
        f.Emote(Gestures ? "👻" : V($"{name}?!", $"{name.ToUpperInvariant()}?!", $"…{name}? no way.", $"{name}…? is that you?", $"{name}, you came back"), 2.4f);
        Write("ghost:" + name, V($"I saw {name}'s ghost last night. They waved.", $"{name.ToUpperInvariant()}'S GHOST WAVED AT ME!!!", $"Saw {name}'s ghost. Not scared. Much.", $"I think {name} came back to see us… just for a moment.", $"{name} came back on the night air."), "👻", 3600);
    }

    /// <summary>While mourning: now and then, go and lay flowers at the headstone.</summary>
    void MemorialOptions(World w, OptionList opts)
    {
        if (_mourning.Length == 0) return;
        if (_t0 > _mournUntil) { _mourning = ""; return; }
        if (_t0 < _flowersCd) return;
        var stone = w.Items.FirstOrDefault(i => i.Def.Key == "memorial" && i.Label == _mourning && !Unreachable(i));
        if (stone == null) return;
        opts.Add(0.8f + P.Sociability * 0.4f, () =>
        {
            _flowersCd = _t0 + rng.Range(600, 1500);
            float side = MathF.Sign(f.Base.X - stone.Pos.X); if (side == 0) side = 1;
            Navigate(() => w.Items.Contains(stone) ? stone.Pos + new Vector2(side * 22 * S, 0) : null, 10 * S, false, () =>
            {
                FaceTo(stone.Pos.X);
                f.SetAction(Act.Tap);
                if (w.MakeItem?.Invoke("tulip") is { } bloom && w.Items.Count(i => i.Label == "flowers:" + _mourning) < 3)
                {
                    bloom.Pos = stone.Pos + new Vector2(side * rng.Range(9, 15) * S, -1); bloom.Vel = Vector2.Zero; bloom.OnGround = false;
                    bloom.Label = "flowers:" + _mourning; bloom.SizeMul = 0.6f;
                }
                f.Emote(Gestures ? "💐" : V("for you", "WE MISS YOU!", "…here.", "I brought flowers…", "rest well"), 2);
                Write("flowers:" + _mourning, V($"Took flowers to {_mourning}'s stone.", $"FLOWERS FOR {_mourning.ToUpperInvariant()}!", $"Left some flowers for {_mourning}.", $"I left flowers for {_mourning}… and sat a while.", $"Flowers for {_mourning}, by the stone."), "♥", 1800);
                Go(G.Idle, rng.Range(4, 8));
            }, WalkPurpose.Other);
        }, $"Lay flowers for {_mourning}");
    }
}
