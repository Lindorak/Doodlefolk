using System.Text.Json;
using System.Text.RegularExpressions;

namespace Doodlefolk;

/// <summary>A town mood from a mod (like Cozy, Classic, Chaos).</summary>
sealed record Storyteller(string Key, string Name, string Blurb, float Drama, float Events, float Visitors);

/// <summary>A town to start from: who's in it, what's out, which animals, and the mood.</summary>
sealed record Scenario(string Key, string Name, string Blurb, List<string> Characters, List<(string key, float x)> Items, List<PetKind> Pets, string Mood);

/// <summary>A festival of the mod's own: its title, decorations, food, and whether there are fireworks.</summary>
sealed record ModEvent(string Key, string Title, string News, List<string> Decor, List<string> Food, bool Fireworks);

/// <summary>More things a mod can add (see docs/MODDING.md): whole characters (the same shape as a saved figure),
/// storytellers (town moods), scenarios (a town to start from), festivals, and songs. Checked as they load, and the
/// Studio can check a file before you share it.</summary>
static partial class Mods
{
    public static readonly List<(SavedFigure fig, string from)> Characters = new();
    public static readonly List<Storyteller> Storytellers = new();
    public static readonly List<Scenario> Scenarios = new();
    public static readonly List<ModEvent> Events = new();
    public static readonly List<Song> Songs = new();
    static readonly HashSet<string> ModItemKeys = new();

    static readonly JsonSerializerOptions Loose = new() { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    static string Key(JsonElement e, string what) { string k = Req(e, "key").ToLowerInvariant(); return Regex.IsMatch(k, "^[a-z0-9_-]{1,32}$") ? k : throw new FormatException($"{what} key \"{k}\" should be lowercase letters, digits, - or _"); }
    static string Text(JsonElement e, string name, string fallback, int max) => e.TryGetProperty(name, out var v) && v.GetString() is { } s ? (s.Length > max ? s[..max] : s) : fallback;
    static List<string> Strings(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList() : new();

    /// <summary>The newer sections of one mod file. Warnings are things that will work but probably aren't meant.</summary>
    static void ContentFrom(JsonElement root, string modName, List<string> warnings, bool register)
    {
        if (root.TryGetProperty("characters", out var cs))
            foreach (var c in cs.EnumerateArray())
            {
                var s = c.Deserialize<SavedFigure>(Loose) ?? throw new FormatException("a character couldn't be read");
                if (s.Name.Length is 0 or > 24) throw new FormatException("a character needs a name (up to 24 letters)");
                if (!Regex.IsMatch(s.Color ?? "", "^#[0-9a-fA-F]{6}$")) throw new FormatException($"character \"{s.Name}\": colour should look like #E53935");
                // A fresh start: no memories, feelings or history come with a character from a mod.
                s.Diary.Clear(); s.Love.Clear(); s.Sweetheart = null; s.Parents.Clear(); s.Record.Clear(); s.Gifts.Clear(); s.Fondness = null; s.Trust = null; s.Hunter = false;
                s.Size = Math.Clamp(s.Size, 0.5f, 2f);
                if (s.Look?.Hat is { Length: > 0 } hat && Look.Find(Look.Hats, hat) == null) warnings.Add($"character \"{s.Name}\": no hat called \"{hat}\" (it'll be bare-headed)");
                if (register) Characters.Add((s, modName));
            }
        if (root.TryGetProperty("storytellers", out var ts))
            foreach (var t in ts.EnumerateArray())
            {
                var st = new Storyteller(Key(t, "storyteller"), Text(t, "name", "A storyteller", 30), Text(t, "blurb", "", 160),
                                         Math.Clamp(Num(t, "drama", 1), 0.1f, 3f), Math.Clamp(Num(t, "events", 1), 0, 4), Math.Clamp(Num(t, "visitors", 1), 0, 4));
                if (st.Key is "cozy" or "classic" or "chaos") throw new FormatException($"storyteller \"{st.Key}\" is a built-in name");
                if (register) Storytellers.Add(st);
            }
        if (root.TryGetProperty("events", out var es))
            foreach (var e in es.EnumerateArray())
            {
                var ev = new ModEvent(Key(e, "event"), Text(e, "title", "A festival", 40), Text(e, "news", "", 120), Strings(e, "decor"), Strings(e, "food"), e.TryGetProperty("fireworks", out var fw) && fw.ValueKind == JsonValueKind.True);
                foreach (var k in ev.Decor.Concat(ev.Food)) if (ItemCatalog.Find(k) == null) warnings.Add($"event \"{ev.Key}\": no object called \"{k}\" (it'll be left out)");
                if (ev.Decor.Count + ev.Food.Count > 12) throw new FormatException($"event \"{ev.Key}\": twelve decorations and dishes at most");
                if (register) Events.Add(ev);
            }
        if (root.TryGetProperty("scenarios", out var ss))
            foreach (var s in ss.EnumerateArray())
            {
                var items = s.TryGetProperty("items", out var its) ? its.EnumerateArray().Select(i => (Req(i, "key"), Math.Clamp(Num(i, "x", 0.5f), 0, 1))).ToList() : new();
                var pets = Strings(s, "pets").Select(p => Enum.TryParse<PetKind>(p, true, out var k) ? k : throw new FormatException($"scenario: unknown animal \"{p}\"")).ToList();
                var sc = new Scenario(Key(s, "scenario"), Text(s, "name", "A town", 40), Text(s, "blurb", "", 200), Strings(s, "characters"), items, pets, Text(s, "mood", "classic", 32));
                if (sc.Characters.Count == 0) throw new FormatException($"scenario \"{sc.Key}\" needs some characters");
                if (sc.Characters.Count > 12 || sc.Items.Count > 30 || sc.Pets.Count > 8) throw new FormatException($"scenario \"{sc.Key}\": up to 12 characters, 30 objects and 8 animals");
                foreach (var (k, _) in sc.Items) if (ItemCatalog.Find(k) == null) warnings.Add($"scenario \"{sc.Key}\": no object called \"{k}\"");
                if (register) Scenarios.Add(sc);
            }
        if (root.TryGetProperty("behaviours", out var bs) || root.TryGetProperty("behaviors", out bs))
            foreach (var b in bs.EnumerateArray())
            {
                var mb = BehaviourFrom(b, warnings);
                if (register) { Behaviours.RemoveAll(x => x.Key == mb.Key); Behaviours.Add(mb); }
            }
        if (root.TryGetProperty("songs", out var sg))
            foreach (var s in sg.EnumerateArray())
            {
                var song = new Song { Id = 100000 + Songs.Count, Title = Text(s, "title", "A song", 60), Lyrics = Text(s, "lyrics", "", 3000), Singer = "" };
                if (song.Lines.Length == 0) throw new FormatException($"song \"{song.Title}\" has no words");
                if (register) Songs.Add(song);
            }
    }

    /// <summary>Scenario characters must be in the same file (or already loaded).</summary>
    static void CrossCheck(JsonElement root, List<string> warnings)
    {
        if (!root.TryGetProperty("scenarios", out var ss)) return;
        var names = (root.TryGetProperty("characters", out var cs) ? cs.EnumerateArray().Select(c => Text(c, "name", "", 24)) : Enumerable.Empty<string>())
                    .Concat(Characters.Select(c => c.fig.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in ss.EnumerateArray())
            foreach (var n in Strings(s, "characters"))
                if (!names.Contains(n)) warnings.Add($"scenario \"{Text(s, "key", "", 32)}\": no character called \"{n}\" (a random one will stand in)");
    }

    /// <summary>Check a mod file without loading it: what it adds, what's wrong, what's odd.</summary>
    public static (bool ok, List<string> problems, List<string> warnings, string summary) Check(string file)
    {
        var problems = new List<string>();
        var warnings = new List<string>();
        string summary = "";
        try
        {
            var info = new FileInfo(file);
            if (info.Length > 2_000_000) problems.Add("the file is over 2 MB");
            using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string dir = Path.GetDirectoryName(file)!;
            int items = 0, hats = 0;
            if (root.TryGetProperty("items", out var its)) foreach (var it in its.EnumerateArray()) { var d = ItemFrom(it, dir); items++; if (ItemCatalog.Find(d.Key) != null && !ModItemKeys.Contains(d.Key)) problems.Add($"object \"{d.Key}\": that name's taken by a built-in object"); }
            if (root.TryGetProperty("hats", out var hs)) foreach (var h in hs.EnumerateArray()) { HatFrom(h, dir); hats++; }
            ContentFrom(root, Path.GetFileName(file), warnings, register: false);
            CrossCheck(root, warnings);
            var known = new HashSet<string> { "items", "hats", "jokes", "names", "characters", "storytellers", "events", "scenarios", "songs", "behaviours", "behaviors", "name", "description", "author", "version" };
            foreach (var p in root.EnumerateObject()) if (!known.Contains(p.Name)) warnings.Add($"\"{p.Name}\" isn't something mods can have (it's ignored)");
            int Count(string k) => root.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.GetArrayLength() : 0;
            var parts = new List<string>();
            void Part(int n, string one, string many) { if (n > 0) parts.Add($"{n} {(n == 1 ? one : many)}"); }
            Part(items, "object", "objects"); Part(hats, "hat", "hats"); Part(Count("jokes"), "joke", "jokes"); Part(Count("characters"), "character", "characters");
            Part(Count("storytellers"), "storyteller", "storytellers"); Part(Count("events"), "festival", "festivals"); Part(Count("scenarios"), "scenario", "scenarios"); Part(Count("songs"), "song", "songs"); Part(Count("behaviours") + Count("behaviors"), "behaviour", "behaviours");
            summary = parts.Count > 0 ? "Adds " + string.Join(", ", parts) + "." : "It doesn't add anything yet.";
            if (parts.Count == 0) warnings.Add("nothing in it yet");
        }
        catch (JsonException e) { problems.Add($"not valid JSON: {e.Message}"); }
        catch (Exception e) { problems.Add(e.Message); }
        return (problems.Count == 0, problems, warnings, summary);
    }
}
