using System.Numerics;

namespace Doodlefolk;

/// <summary>Using what mods add beyond objects and hats (see Mods.Content): characters in the library, storytellers
/// as town moods, festivals of their own, scenarios to start a town from, songs to sing.</summary>
sealed partial class App
{
    /// <summary>The town mood's numbers: built-in ones, or a mod's storyteller.</summary>
    (float drama, float events, float visitors) MoodNumbers(string mood)
    {
        if (Mods.Storytellers.FirstOrDefault(s => s.Key == mood) is { } st) return (st.Drama, st.Events, st.Visitors);
        return mood switch { "cozy" => (0.35f, 1, 1), "chaos" => (2f, 1, 1), _ => (1, 1, 1) };
    }

    string SpawnModCharacter(int index)
    {
        if (index < 0 || index >= Mods.Characters.Count) return "No such character.";
        var (s, from) = Mods.Characters[index];
        var f = SpawnFromLibrary(s);
        return f != null ? $"{f.Name} (from {from}) is on the way." : "There's no room for anyone else.";
    }

    /// <summary>Start a fresh cast from a scenario: its characters (or stand-ins), its things, its animals, its mood.</summary>
    string StartScenario(string key)
    {
        var sc = Mods.Scenarios.FirstOrDefault(x => x.Key == key);
        if (sc == null) return "No such scenario.";
        string made = SwitchCast(UniqueCastName(sc.Name), true);
        foreach (var name in sc.Characters)
        {
            var c = Mods.Characters.FirstOrDefault(x => x.fig.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (c.fig != null) SpawnFromLibrary(c.fig); else Spawn(null);
        }
        var v = _w.Env.Virtual;
        foreach (var (k, x) in sc.Items)
            if (ItemCatalog.Find(k) is { } d && SpawnItem(d) is { } it)
            {
                float px = v.Left + 40 + (v.Width - 80) * x;
                var (_, _, top) = _w.Env.BoundsAt(px);
                it.Pos = new Vector2(px, top + 40 * _w.Scale); it.Vel = Vector2.Zero; it.OnGround = false;
            }
        foreach (var p in sc.Pets) AdoptPet(p, false);
        if (sc.Mood.Length > 0) _settings.TownMood = sc.Mood;
        _settings.Save();
        World.Log($"scenario: {sc.Name} ({made})");
        return $"Welcome to {sc.Name}!";
    }

    string UniqueCastName(string name)
    {
        bool Taken(string n) => SameCast(n, _settings.CastName) || File.Exists(CastFile(n));
        if (!Taken(name)) return name;
        for (int i = 2; ; i++) if (!Taken($"{name} {i}")) return $"{name} {i}";
    }

    object ModContentState() => new
    {
        characters = Mods.Characters.Select((c, i) => new { index = i, name = c.fig.Name, hex = c.fig.Color, from = c.from, describe = c.fig.Traits.Describe(), likes = c.fig.Tastes?.Describe() }),
        storytellers = Mods.Storytellers.Select(s => new { key = s.Key, name = s.Name, blurb = s.Blurb }),
        scenarios = Mods.Scenarios.Select(s => new { key = s.Key, name = s.Name, blurb = s.Blurb, size = s.Characters.Count }),
        events = Mods.Events.Select(e => new { key = e.Key, title = e.Title }),
        songs = Mods.Songs.Count,
        behaviours = Mods.Behaviours.Select(b => b.Name),
    };
}
