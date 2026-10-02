using System.Text.Json;

namespace StickFight;

/// <summary>One saved cast: everyone, their things, their pets and their balls.</summary>
sealed class SavedCast
{
    public string Name { get; set; } = "";
    public DateTime Saved { get; set; }
    public List<SavedFigure> Figures { get; set; } = new();
    public List<SavedItem> Items { get; set; } = new();
    public List<SavedPet> Pets { get; set; } = new();
    public List<SavedProp> Props { get; set; } = new();
    public List<SavedClub> Clubs { get; set; } = new();
}

/// <summary>Several casts side by side (save slots): keep the current cast under a name, start a fresh one, and
/// switch between them. The current cast lives in settings.json as before; the others are files in
/// %APPDATA%\StickFight\casts.</summary>
sealed partial class App
{
    static string CastDir => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "casts");

    static string CastFile(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return Path.Combine(CastDir, (safe.Length == 0 ? "cast" : safe) + ".json");
    }

    object CastsJson()
    {
        var list = new List<object>();
        try
        {
            if (Directory.Exists(CastDir))
                foreach (var file in Directory.GetFiles(CastDir, "*.json").OrderByDescending(File.GetLastWriteTime))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        var r = doc.RootElement;
                        string name = r.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : Path.GetFileNameWithoutExtension(file);
                        if (name == _settings.CastName) continue;
                        list.Add(new
                        {
                            name,
                            saved = r.TryGetProperty("Saved", out var sv) && sv.TryGetDateTime(out var dt) ? dt.ToString("d MMM, HH:mm") : "",
                            figures = r.TryGetProperty("Figures", out var fs) ? fs.EnumerateArray().Select(x => x.TryGetProperty("Name", out var fn) ? fn.GetString() : "").Take(8).ToArray() : Array.Empty<string?>(),
                            pets = r.TryGetProperty("Pets", out var ps) ? ps.GetArrayLength() : 0,
                        });
                    }
                    catch { }
                }
        }
        catch { }
        return new { current = _settings.CastName, others = list };
    }

    SavedCast CurrentCast()
    {
        SaveCast();
        return new SavedCast
        {
            Name = _settings.CastName, Saved = DateTime.Now,
            Figures = _settings.Figures.ToList(), Items = _settings.Items.ToList(), Pets = _settings.Pets.ToList(), Props = _settings.Props.ToList(), Clubs = _settings.Clubs.ToList(),
        };
    }

    void WriteCast(SavedCast c)
    {
        Directory.CreateDirectory(CastDir);
        File.WriteAllText(CastFile(c.Name), JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Keep a copy of the current cast under a new name (and carry on with it under that name).</summary>
    string SaveCastAs(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 40) return "Give it a name (up to 40 letters).";
        if (PetMode) return "Switch off pet-only mode first.";
        if (name != _settings.CastName && File.Exists(CastFile(name))) return $"There's already a cast called \"{name}\". Pick another name.";
        var c = CurrentCast();
        c.Name = name;
        _settings.CastName = name;
        WriteCast(c);
        _settings.Save();
        return $"Saved as \"{name}\".";
    }

    /// <summary>Put the current cast away (saved under its name) and bring out another, or a brand new empty one.</summary>
    string SwitchCast(string name, bool fresh)
    {
        name = name.Trim();
        if (name.Length is 0 or > 40) return "Give it a name (up to 40 letters).";
        if (PetMode) return "Switch off pet-only mode first.";
        if (name == _settings.CastName) return "That's the cast you're on.";
        if (fresh && File.Exists(CastFile(name))) return $"There's already a cast called \"{name}\". Pick another name, or switch to it.";
        SavedCast? next = null;
        if (!fresh)
        {
            try { next = JsonSerializer.Deserialize<SavedCast>(File.ReadAllText(CastFile(name))); }
            catch (Exception e) { return "Couldn't open that cast: " + e.Message; }
            if (next == null) return "That cast is empty.";
        }
        if (_w.Happening is { } h) EndHappening(h, false);
        StopGame(false);
        // Put this one away.
        WriteCast(CurrentCast());
        // Clear the stage.
        EndPress();
        foreach (var f in _w.Figures.ToArray()) _w.RemoveFigure(f);
        foreach (var it in _w.Items.ToArray()) _w.RemoveItem(it);
        foreach (var p in _w.Props.ToArray()) _w.RemoveProp(p);
        _w.Pets.Clear();
        _w.Clubs.Clear();
        // Bring out the other.
        _settings.CastName = name;
        _settings.Figures = next?.Figures ?? new();
        _settings.Items = next?.Items ?? new();
        _settings.Pets = next?.Pets ?? new();
        _settings.Props = next?.Props ?? new();
        _settings.Clubs = next?.Clubs ?? new();
        if (fresh) for (int i = 0; i < 3; i++) Spawn(null, null);
        else RestoreCast();
        _settings.Save();
        World.Log($"cast: switched to {name}{(fresh ? " (new)" : "")}");
        return fresh ? $"A fresh start: \"{name}\"." : $"Welcome back, \"{name}\".";
    }

    string DeleteCast(string name)
    {
        if (name == _settings.CastName) return "That's the cast you're on.";
        try { File.Delete(CastFile(name)); return $"Deleted \"{name}\"."; }
        catch (Exception e) { return "Couldn't delete it: " + e.Message; }
    }
}
