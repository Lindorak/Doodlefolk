namespace Doodlefolk;

/// <summary>The pond's catch log (see Fishes): new kinds and personal bests, and the Doodledex's fish page.</summary>
sealed partial class App
{
    string OnFishCaught(Figure by, FishKind kind, float cm)
    {
        var log = _settings.FishLog;
        bool first = !log.TryGetValue(kind.Key, out var rec);
        if (rec == null) log[kind.Key] = rec = new FishRecord { First = DateTime.Now };
        rec.Count++;
        bool best = cm > rec.BestCm;
        float old = rec.BestCm;
        if (best) { rec.BestCm = cm; rec.BestBy = by.Name; }
        _settings.FishTotal++;
        string what = kind.Name.ToLowerInvariant();
        string note = "";
        if (first)
        {
            note = " A new kind for the Doodledex!";
            int real = Fishes.Kinds.Count(k => !k.Junk && log.ContainsKey(k.Key));
            _w.News("fish", kind.Rarity >= FishRarity.Rare ? $"{by.Name} caught a {what}! A rare one: only the lucky see it" : $"{by.Name} caught the town's first {what}", kind.Rarity >= FishRarity.Rare ? 3 : 1, by);
            if (real >= 10) _w.Sticker("fishdex");
            if (Fishes.Kinds.All(k => log.ContainsKey(k.Key))) { _w.Sticker("allfish"); _w.News("fish", "Every kind of thing in the pond has been caught. A master angler town!", 4); }
        }
        else if (best && !kind.Junk && cm >= kind.MinCm + (kind.MaxCm - kind.MinCm) * 0.8f)
        {
            note = $" Biggest {what} yet (beat {old:0.#} cm)!";
            _w.News("fish", $"{by.Name} lands a whopper: a {cm:0.#} cm {what}!", 2, by);
        }
        if (kind.Rarity == FishRarity.Legendary) _w.Sticker("legendfish");
        Contribute("FISH_CAUGHT", 1);
        return note;
    }

    object FishDex()
    {
        bool raining = _w.Weather.Raining;
        var biting = Fishes.Biting(DateTime.Now, raining).Select(k => k.Key).ToHashSet();
        return Fishes.Kinds.Select(k =>
        {
            _settings.FishLog.TryGetValue(k.Key, out var r);
            return new
            {
                key = k.Key, name = k.Name, hint = k.Hint, months = Fishes.Months(k), rarity = k.Rarity.ToString(), junk = k.Junk,
                colour = Settings.Hex(k.Col), now = biting.Contains(k.Key),
                count = r?.Count ?? 0, best = r?.BestCm ?? 0, bestBy = r?.BestBy ?? "", first = r?.First.ToString("d MMM yyyy") ?? "",
            };
        });
    }
}
