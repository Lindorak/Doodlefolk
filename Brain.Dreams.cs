namespace StickFight;

/// <summary>Dreams: while asleep a figure dreams (a little cloud over its head) about its own life: what it loves, its
/// crush, its pet, a party, you; a rival or a bad fall turns it into a nightmare, and it wakes with a start. In the
/// morning it writes the dream down, and sometimes tells a friend.</summary>
sealed partial class Brain
{
    float _dreamAt = 10;
    string? _lastDream;
    bool _lastNightmare, _wasAsleep;

    void UpdateDreams(World w)
    {
        bool asleep = Asleep;
        if (!asleep)
        {
            if (_wasAsleep && _lastDream != null) Woke(w);
            _wasAsleep = false;
            if (f.Dream != null && _t0 > f.DreamUntil) f.Dream = null;
            return;
        }
        _wasAsleep = true;
        if (f.Dream != null && _t0 > f.DreamUntil) f.Dream = null;
        if (_t0 < _dreamAt) return;
        _dreamAt = _t0 + rng.Range(12, 22);
        var (text, nightmare) = PickDream(w);
        f.Dream = text;
        f.DreamUntil = _t0 + 6;
        f.Nightmare = nightmare;
        _lastDream = text;
        _lastNightmare = nightmare;
        w.Sticker("dream");
        if (nightmare && rng.NextDouble() < 0.35) { f.Dream = null; Fear = M.Clamp01(Fear + 0.3f); f.Emote("!", 1); Go(G.Idle, 1.5f); }
    }

    (string, bool) PickDream(World w)
    {
        var options = new List<(string, bool)>();
        var loves = Enum.GetValues<Thing>().OrderByDescending(t => f.Tastes.Of(t)).First();
        options.Add((ThingWords(loves), false));
        if (Crush(w) is { } c) options.Add(($"♥ {c.Name}", false));
        if (Sweetheart(w) is { } sw) options.Add(($"♥ {sw.Name}", false));
        if (w.Pets.FirstOrDefault(p => p.Owner == f) is { } pet) options.Add((pet.Name, false));
        if (Hunger > 0.5f) options.Add((rng.NextDouble() < 0.5 ? "pizza…" : "cake…", false));
        if (UserFondness > 0.5f) options.Add(("you ♥", false));
        if (Coins > 20) options.Add(("coins!", false));
        foreach (var o in w.Figures) if (o != f && IsRival(o)) { options.Add(($"{o.Name}!", true)); break; }
        if (Fear > 0.3f || Memories.Any(m => m.What.Contains("Threw") && _t0 - m.At < 600)) options.Add(("falling…", true));
        if (P.Curiosity > 0.6f) options.Add(("the stars", false));
        return options[rng.Next(options.Count)];
    }

    void Woke(World w)
    {
        string d = _lastDream!;
        Write("dream", _lastNightmare
            ? V($"Had a nightmare about {d.TrimEnd('!', '…')}.", $"NIGHTMARE! {d.TrimEnd('!', '…').ToUpperInvariant()}!!", $"Bad dream. {d.TrimEnd('!', '…')}. Whatever.", $"I had a scary dream about {d.TrimEnd('!', '…')}…", $"A dark dream: {d.TrimEnd('!', '…')}.")
            : V($"Dreamt about {d.TrimEnd('…', '!')}.", $"I DREAMT ABOUT {d.TrimEnd('…', '!').ToUpperInvariant()}!!", $"Dreamt about {d.TrimEnd('…', '!')}. Odd.", $"I had the nicest dream about {d.TrimEnd('…', '!')}…", $"In my dream: {d.TrimEnd('…', '!')}."), _lastNightmare ? "…" : "♪", 1200);
        // Tell a friend nearby.
        if (rng.NextDouble() < 0.4 && w.Figures.Any(o => o != f && System.Numerics.Vector2.Distance(o.Base, f.Base) < 300 * S && AffinityWith(o) > 0.2f))
            f.Emote(_lastNightmare ? $"I had a bad dream…" : $"I dreamt about {d.TrimEnd('…', '!')}!", 2);
        _lastDream = null;
    }
}
