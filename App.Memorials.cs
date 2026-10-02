using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Someone the town has lost, remembered.</summary>
sealed class Memorial
{
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
    public string Hat { get; set; } = "";
    public DateTime Died { get; set; }
    public int Age { get; set; }
    public string Cause { get; set; } = "";
    public string Epitaph { get; set; } = "";
    public string Partner { get; set; } = "";
    public List<string> Friends { get; set; } = new();
    public List<string> Children { get; set; } = new();
    public List<string> Parents { get; set; } = new();
    public List<string> Diary { get; set; } = new();
    public int GhostVisits { get; set; }
}

/// <summary>Memorials (an idea from The Sims): when someone dies (in a fight, with "dies for good" on, or, if you
/// choose, peacefully of old age), a little headstone goes up with their name, friends and family grieve and bring
/// flowers, and the Studio keeps a page for them: age, who they loved, a line about them and a few lines from their
/// diary. On dark nights their ghost may come back for a minute to wave at old friends. Nobody dies of old age unless
/// you turn it on.</summary>
sealed partial class App
{
    double _ghostAt = 1800;

    void OnFigureDied(Figure f)
    {
        if (f.Visitor != VisitorKind.None || _selfTest && f.Name.StartsWith("QA")) return;
        var b = f.Brain;
        int age = (int)b.AgeYears;
        string cause = f.Peaceful ? $"peacefully{(age >= 80 ? $", at the grand old age of {age}" : "")}"
                     : f.KilledBy is { } k && k != f ? $"in a fight with {k.Name}" : "in a fight";
        var friends = _w.Figures.Where(o => o != f && !o.Dead && o.Visitor == VisitorKind.None && b.AffinityWith(o) > 0.25f)
                                .OrderByDescending(o => b.AffinityWith(o)).Take(4).Select(o => o.Name).ToList();
        var kids = _w.Figures.Where(o => o.Brain.ParentIds.Contains(f.Id)).Select(o => o.Name).ToList();
        var m = new Memorial
        {
            Name = f.Name, Colour = Settings.Hex(f.Color), Hat = f.Look.Hat, Died = DateTime.Now, Age = age, Cause = cause,
            Partner = b.Sweetheart(_w)?.Name ?? "", Friends = friends, Children = kids, Epitaph = Epitaph(f), Parents = b.ParentNames.ToList(),
            Diary = b.Diary.Where(d => d.Mood is "★" or "♥").TakeLast(3).Concat(b.Diary.TakeLast(1)).Distinct().Select(d => $"{d.At:d MMM}: {d.Text}").ToList(),
        };
        _settings.Memorials.Add(m);
        // A headstone where they fell (on the ground below).
        if (ItemCatalog.Find("memorial") is { } def && _w.Env.Below(f.Base.X, f.Base.Y - 5) is { } floor && SpawnItem(def) is { } stone)
        {
            stone.Pos = new Vector2(Math.Clamp(f.Base.X, floor.X1 + 20 * _w.Scale, floor.X2 - 20 * _w.Scale), floor.Y - 2);
            stone.Vel = Vector2.Zero; stone.OnGround = false;
            stone.Label = f.Name; stone.OwnerColour = f.Color;
        }
        // Everyone who cared, grieves.
        foreach (var o in _w.Figures.Where(o => o != f && !o.Dead && o.Mode == Mode.Control && o.Visitor == VisitorKind.None))
        {
            float care = MathF.Max(o.Brain.AffinityWith(f), o.Brain.ParentIds.Contains(f.Id) || b.ParentIds.Contains(o.Id) || o.Brain.SweetheartId == f.Id ? 1 : 0);
            if (care > 0.2f) o.Brain.Grieve(f.Name, care, _w);
        }
        _w.News("memorial", $"In memory of {f.Name}, {age}, who died {cause}.{(friends.Count > 0 ? $" Loved by {string.Join(", ", friends)}." : "")}", 4, f);
        _w.Sticker("memorial");
        _settings.Save();
        World.Log($"memorial: {f.Name} ({cause})");
    }

    string Epitaph(Figure f)
    {
        var likes = Enum.GetValues<Thing>().OrderByDescending(t => f.Tastes.Of(t)).Take(2).Where(t => f.Tastes.Of(t) > 0.2f).Select(t => Tastes.Name(t).ToLowerInvariant()).ToList();
        var p = f.Traits;
        string nature = p.Sociability > 0.7f ? "a friend to everyone" : p.Bravery > 0.7f ? "brave to the end" : p.Curiosity > 0.7f ? "always off exploring" : p.Playfulness > 0.7f ? "the life of the party" : p.Aggression > 0.7f ? "fierce, and fiercely loyal" : p.Energy < 0.3f ? "fond of a good nap" : "one of a kind";
        return likes.Count > 0 ? $"{char.ToUpper(nature[0])}{nature[1..]}. Loved {string.Join(" and ", likes)}." : $"{char.ToUpper(nature[0])}{nature[1..]}.";
    }

    /// <summary>On a dark night, now and then, someone remembered comes back for a minute.</summary>
    void GhostFrame(double now)
    {
        if (now < _ghostAt) return;
        _ghostAt = now + _w.Rng.Range(2400, 5400);
        if (!_settings.Ghosts || _w.Night < 0.6f || Focusing || _settings.Memorials.Count == 0 || _w.Happening != null || _w.Figures.Any(f => f.Visitor != VisitorKind.None)) return;
        var m = _settings.Memorials[_w.Rng.Next(_settings.Memorials.Count)];
        GhostOf(m);
    }

    public string GhostOf(Memorial m)
    {
        var stone = _w.Items.FirstOrDefault(i => i.Def.Key == "memorial" && i.Label == m.Name);
        var ground = stone != null ? _w.Env.Below(stone.Pos.X, stone.Pos.Y - 5) : HappeningGround(200 * _w.Scale);
        if (ground == null) return "nowhere to appear";
        var f = new Figure(Color4.Lerp(Settings.ParseHex(m.Colour), new Color4(0.9f, 0.95f, 1, 1), 0.45f), m.Name, _w.Scale, Personality.Random(_w.Rng), _w.Rng) { Visitor = VisitorKind.Ghost, Spirit = true };
        f.Look.Hat = m.Hat;
        float x = stone?.Pos.X ?? (ground.X1 + ground.X2) / 2;
        f.PlaceAt(ground, Math.Clamp(x + 30 * _w.Scale, ground.X1 + 10, ground.X2 - 10));
        _w.Figures.Add(f);
        f.Brain.StartVisit(VisitorKind.Ghost, _w, x);
        m.GhostVisits++;
        // Old friends notice: not frightened, wistful.
        foreach (var o in _w.Figures.Where(o => o != f && (m.Friends.Contains(o.Name) || m.Partner == o.Name || m.Children.Contains(o.Name))))
            o.Brain.SawGhostOf(m.Name, _w);
        _w.News("memorial", $"On a dark night, {m.Name}'s ghost came by to say hello", 2, f);
        World.Log($"ghost: {m.Name}");
        return $"{m.Name}'s ghost is visiting.";
    }

    object MemorialState() => _settings.Memorials.AsEnumerable().Reverse().Select(m => new
    {
        name = m.Name, colour = m.Colour, hat = m.Hat, died = m.Died.ToString("d MMMM yyyy"), age = m.Age, cause = m.Cause, epitaph = m.Epitaph,
        partner = m.Partner, friends = m.Friends, children = m.Children, diary = m.Diary, ghost = m.GhostVisits,
    });
}
