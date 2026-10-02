using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

enum VisitorKind { None, Bard, MailCarrier, Knight, Artist, Ghost, Explorer, Chef, Gardener, Viewer, Guest }

/// <summary>An entry in the Doodledex: who (or what) you've met, when first, how often.</summary>
sealed class DexEntry
{
    public DateTime First { get; set; }
    public int Count { get; set; }
}

/// <summary>Visitors (an idea from Neko Atsume's rare cats and Animal Crossing's travellers): every so often someone
/// from elsewhere wanders in for a few minutes: a bard who plays and draws a crowd, the mail carrier with a gift crate,
/// a knight who salutes everyone, an artist who paints a portrait, a ghost on dark nights, an explorer with tall tales,
/// a travelling chef with a feast, a gardener who plants flowers. Each leaves something behind, and goes in the
/// Doodledex. Visitors aren't kept between runs; what they leave is.</summary>
sealed partial class App
{
    double _visitorAt = 900, _crateEarnedAt;

    public static readonly (VisitorKind kind, string name, string title, string hat, string colour, string blurb)[] Visitors =
    {
        (VisitorKind.Bard, "Lyra", "a travelling bard", "jester", "#8E24AA", "Plays for a crowd, and leaves a lute behind."),
        (VisitorKind.MailCarrier, "Pip", "the mail carrier", "cap", "#1E88E5", "Brings gift crates with rare hats inside."),
        (VisitorKind.Knight, "Sir Doodle", "a wandering knight", "helmet", "#9AA3AD", "Salutes everyone, and plants a pennant."),
        (VisitorKind.Artist, "Mona", "an artist", "beanie", "#FB8C00", "Paints someone's portrait to keep."),
        (VisitorKind.Ghost, "Boo", "a friendly ghost", "", "#E3F2FD", "Only on dark nights. Says boo. Leaves a lantern."),
        (VisitorKind.Explorer, "Marco", "an explorer", "cowboy", "#6E4B26", "Tells tales of far-off desktops; leaves a map."),
        (VisitorKind.Chef, "Gusto", "a travelling chef", "chef", "#F7F5EF", "Cooks everyone a feast."),
        (VisitorKind.Gardener, "Fern", "a gardener", "flowercrown", "#43A047", "Plants flowers wherever she goes."),
    };

    void VisitorFrame(double now)
    {
        // Gift crates are earned by time spent together: one every two hours (up to two waiting for the mail carrier).
        if (_crateEarnedAt == 0) _crateEarnedAt = now;
        if (now - _crateEarnedAt > 7200 && _settings.CratesWaiting < 2) { _settings.CratesWaiting++; _crateEarnedAt = now; }
        if (now < _visitorAt || !_settings.Visitors || PetMode || World.Focus || _w.Happening != null || _w.Figures.Any(f => f.Visitor != VisitorKind.None)) return;
        _visitorAt = now + _w.Rng.Range(1500, 4200) / MathF.Max(0.25f, World.VisitorRate);
        if (World.VisitorRate <= 0) return;
        if (_w.Figures.Count(f => f.Mode == Mode.Control) < 1 || _w.Figures.Count >= World.MaxFigures) return;
        // Who: the mail carrier when a crate's waiting; the ghost only after dark; otherwise anyone (rarer ones rarer).
        VisitorKind kind;
        if (_settings.CratesWaiting > 0 && _w.Rng.NextDouble() < 0.6) kind = VisitorKind.MailCarrier;
        else if (_w.Night > 0.6f && _w.Rng.NextDouble() < 0.35) kind = VisitorKind.Ghost;
        else
        {
            var pool = new[] { VisitorKind.Bard, VisitorKind.Bard, VisitorKind.Knight, VisitorKind.Artist, VisitorKind.Explorer, VisitorKind.Explorer, VisitorKind.Chef, VisitorKind.Gardener, VisitorKind.Gardener };
            kind = pool[_w.Rng.Next(pool.Length)];
        }
        Visit(kind);
    }

    public string Visit(VisitorKind kind)
    {
        var info = Visitors.FirstOrDefault(v => v.kind == kind);
        if (info.kind == VisitorKind.None) return "nobody like that";
        var ground = HappeningGround(200 * _w.Scale);
        if (ground == null) return "nowhere to arrive";
        var f = new Figure(Settings.ParseHex(info.colour), UniqueName(info.name), _w.Scale, Personality.Random(_w.Rng), _w.Rng) { Visitor = kind };
        f.Look.Hat = info.hat; f.Look.HatColour = kind == VisitorKind.MailCarrier ? "#1565C0" : info.hat == "jester" ? "#E53935" : "#5D6670";
        // In from whichever end is nearer the others.
        float mid = _w.Figures.Count > 0 ? _w.Figures.Average(o => o.Base.X) : (ground.X1 + ground.X2) / 2;
        float x = mid < (ground.X1 + ground.X2) / 2 ? ground.X2 - 40 * _w.Scale : ground.X1 + 40 * _w.Scale;
        f.PlaceAt(ground, x);
        _w.Figures.Add(f);
        f.Brain.StartVisit(kind, _w, mid);
        var dex = _settings.Dex.TryGetValue("visitor:" + kind, out var d) ? d : _settings.Dex["visitor:" + kind] = new DexEntry { First = DateTime.Now };
        dex.Count++;
        _w.News("visitors", $"{f.Name}, {info.title}, came to visit", 2, f);
        _w.Sticker("visitor");
        World.Log($"visitor: {kind} ({f.Name})");
        return $"{f.Name}, {info.title}, is visiting.";
    }

    void RareSeen(string coat)
    {
        if (!_settings.Dex.TryGetValue("pet:" + coat, out var d)) _settings.Dex["pet:" + coat] = d = new DexEntry { First = DateTime.Now };
        d.Count++;
        _w.Sticker("rarepet");
        _w.News("pets", $"A {coat} coat! One in forty animals has one.", 2);
    }

    /// <summary>A visitor has said goodbye: off it goes.</summary>
    void VisitorsLeave()
    {
        foreach (var f in _w.Figures.Where(f => f.Visitor != VisitorKind.None && f.Brain.VisitOver).ToList())
        {
            _w.Fx.Dust(f.Base, _w.Scale, 8, 1, _w.Rng);
            _w.RemoveFigure(f);
        }
    }

    /// <summary>A crate is opened: a rare hat, if there are any left to find (or a pile of coins if not).</summary>
    public string OpenCrate(Item crate, Figure? opener)
    {
        _w.RemoveItem(crate);
        var locked = Look.RareHats.Where(h => !_settings.UnlockedHats.Contains(h)).ToList();
        _w.Fx.Spark(crate.Pos, _w.Scale, _w.Rng, 1.6f);
        World.Play(Sfx.TaDa, crate.Pos, 0.5f, 1.1f);
        if (locked.Count == 0)
        {
            foreach (var f in _w.Figures) f.Brain.Coins += 5;
            return "The crate was full of coins: 5 for everyone!";
        }
        string hat = locked[_w.Rng.Next(locked.Count)];
        _settings.UnlockedHats.Add(hat);
        _settings.Dex["hat:" + hat] = new DexEntry { First = DateTime.Now, Count = 1 };
        string name = Look.Find(Look.Hats, hat)?.Name ?? hat;
        if (opener != null)
        {
            opener.Look.Hat = hat;
            opener.Emote(World.Gestures ? "🎁" : $"a {name.ToLowerInvariant()}!", 2.2f);
            opener.Brain.Cheered(0.4f);
        }
        _w.News("visitors", $"A gift crate held a rare {name.ToLowerInvariant()}{(opener != null ? $", and {opener.Name} is wearing it" : "")}!", 3, opener);
        _w.Sticker("rarehat");
        _settings.Save();
        return $"A rare hat: the {name.ToLowerInvariant()}! (Now in the Look tab for everyone.)";
    }
}
