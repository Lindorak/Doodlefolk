using System.Numerics;

namespace Doodlefolk;

/// <summary>A present you gave a figure (something it asked for), remembered and brought up again later.</summary>
sealed class Gift
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime When { get; set; } = DateTime.Now;
}

/// <summary>Rivalries (the same two keep meeting in fights, so there's always a rematch to be had) and memories of
/// what you gave them.</summary>
sealed partial class Brain
{
    // ---------------- rivals ----------------

    /// <summary>Head-to-head record against each figure: bouts won and lost.</summary>
    public readonly Dictionary<int, (int Won, int Lost)> Record = new();
    float _rematchCd = 60;

    public bool IsRival(Figure o) => Record.TryGetValue(o.Id, out var r) && (r.Lost >= 2 || (r.Won >= 1 && r.Lost >= 1 && r.Won + r.Lost >= 3));

    /// <summary>A fight or spar ended between us. A close, repeated contest (or a string of defeats) makes a rival.</summary>
    public void RecordBout(Figure o, bool won)
    {
        bool was = IsRival(o);
        var r = Record.GetValueOrDefault(o.Id);
        Record[o.Id] = won ? (r.Won + 1, r.Lost) : (r.Won, r.Lost + 1);
        if (!was && IsRival(o))
        {
            var rec = Record[o.Id];
            Write("rival:" + o.Name, V($"{o.Name} is my rival now. {rec.Won}–{rec.Lost}.", $"{o.Name} is my RIVAL!!! {rec.Won}–{rec.Lost}, but not for long!", $"{o.Name}. Rival. {rec.Won}–{rec.Lost}. I'll fix that.", $"I think {o.Name} and I are rivals now… {rec.Won}–{rec.Lost}", $"{o.Name} and I are bound by rivalry. {rec.Won}–{rec.Lost}."), "⚔", 1e9f);
            World.Current?.News("rival", $"A rivalry is born: {f.Name} vs {o.Name}", 2, f, o);
            World.Current?.Sticker("rivals");
        }
    }

    /// <summary>Rivals look for a rematch, more so when they're behind.</summary>
    void RivalOptions(World w, OptionList opts)
    {
        if (!Rules.Enabled || Baby || f.Hunter || _rematchCd > _t0 || Stamina < 0.5f || f.HP < 70) return;
        foreach (var o in w.Figures)
        {
            if (o == f || !IsRival(o) || o.Mode != Mode.Control || o.Brain.Asleep || o.Brain.InFight || !o.Grounded || o.Brain.Baby) continue;
            float d = Vector2.Distance(o.Base, f.Base);
            if (d > 1400 * S) continue;
            var r = Record[o.Id];
            float behind = r.Lost > r.Won ? 1.5f : r.Lost == r.Won ? 1.1f : 0.7f;
            opts.Add((0.3f + P.Aggression * 0.5f + P.Bravery * 0.3f) * behind * Taste(Thing.Sparring), () =>
            {
                _rematchCd = _t0 + 180;
                f.Emote(V($"rematch, {o.Name}!", $"REMATCH!! {o.Name.ToUpperInvariant()}!!", $"{o.Name}. again.", $"um, {o.Name}… rematch?", $"{o.Name}, once more"), 1.4f);
                Engage(o, true, w);
            }, $"Rematch with their rival {o.Name}");
            return;
        }
    }

    // ---------------- gifts from you ----------------

    public readonly List<Gift> Gifts = new();
    float _giftTalkAt;

    void RememberGift(string key, string name)
    {
        Gifts.Add(new Gift { Key = key, Name = name, When = DateTime.Now });
        if (Gifts.Count > 30) Gifts.RemoveAt(0);
    }

    /// <summary>Using something you gave it: now and then it says thanks again, long after.</summary>
    void UsingGift(Item it)
    {
        if (_t0 < _giftTalkAt || Gifts.FirstOrDefault(g => g.Key == it.Def.Key) is not { } g || (DateTime.Now - g.When).TotalMinutes < 5) return;
        if (rng.NextDouble() > 0.35) return;
        _giftTalkAt = _t0 + 600;
        string thing = g.Name.ToLowerInvariant();
        var ago = DateTime.Now - g.When;
        string when = ago.TotalDays >= 2 ? $"{ago.TotalDays:0} days ago" : ago.TotalHours >= 2 ? $"{ago.TotalHours:0} hours ago" : "earlier";
        f.Emote(V($"thanks again for the {thing}!", $"I LOVE the {thing} you gave me!!", $"the {thing}'s still fine. thanks.", $"I still have the {thing} you gave me ♥", $"the {thing}, a gift from you"), 1.8f);
        FeelUser(0.01f, $"Remembered the {thing} you gave them");
        Write("gift:" + g.Key, V($"Used the {thing} you gave me {when}. Still love it.", $"The {thing} from {when} is THE BEST.", $"The {thing} from {when} is holding up.", $"I used the {thing} you gave me {when}. It made me think of you.", $"The {thing}, from {when}. Some gifts stay warm."), "♥", 3600);
    }

    string GiftsLine() => Gifts.Count == 0 ? "" : string.Join(", ", Gifts.AsEnumerable().Reverse().Select(g => g.Name.ToLowerInvariant()).Distinct().Take(5));
}
