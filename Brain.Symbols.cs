namespace Doodlefolk;

/// <summary>Which manga symbols show (Figure.Manpu.cs draws them): read off how they feel right now (anger,
/// frustration, fear, sadness, joy, a blush) plus short flashes for moments (a win, a loss, a jealous pang, an
/// awkward rejection, a startle).</summary>
sealed partial class Brain
{
    readonly Dictionary<Manpu, float> _flash = new();

    /// <summary>Show a symbol for a moment.</summary>
    public void Flash(Manpu m, float seconds) => _flash[m] = MathF.Max(_flash.GetValueOrDefault(m), _t0 + seconds);

    void UpdateSymbols(World w)
    {
        if (!World.Manga || f.Mode == Mode.Spawning) { f.Symbols = Manpu.None; return; }
        var s = Manpu.None;
        foreach (var (k, until) in _flash) if (_t0 < until) s |= k;
        var md = f.Mood;
        if (f.KO || f.Mode == Mode.GetUp) s |= Manpu.Dizzy;
        if (_g == G.Startled) s |= Manpu.Shock;
        if (_g == G.Annoyed) s |= Manpu.Vein;
        if (!Asleep)
        {
            float angry = MathF.Max(md.Angry, Annoyance);
            if (angry > 0.55f || (InFight && P.Aggression > 0.55f)) s |= Manpu.Vein;
            if (Frustration > 0.6f || angry > 0.82f) s |= Manpu.Steam;
            if (Fear > 0.62f && !s.HasFlag(Manpu.Shock)) s |= Manpu.Nervous;
            if (MathF.Max(Sadness, md.Sad) > 0.62f) s |= Manpu.Gloom;
            if (f.Blush > 0.35f) s |= Manpu.Blush;
            if (f.Blush > 0.6f && A != Archetype.Kuudere) s |= Manpu.Hearts;
            if (Joy > 0.85f && !s.HasFlag(Manpu.Gloom)) s |= Manpu.Sparkles;
        }
        // Two at most, the strongest feelings first (so it reads at a glance).
        int shown = 0;
        var keep = Manpu.None;
        foreach (var m in Order)
        {
            if (!s.HasFlag(m)) continue;
            keep |= m;
            if (++shown >= 2) break;
        }
        f.Symbols = keep;
    }

    static readonly Manpu[] Order = { Manpu.Dizzy, Manpu.Shock, Manpu.Vein, Manpu.Steam, Manpu.SweatDrop, Manpu.Nervous, Manpu.Gloom, Manpu.Hearts, Manpu.Blush, Manpu.Sparkles };
}
