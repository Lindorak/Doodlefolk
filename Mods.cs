using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Mods: JSON files in %APPDATA%\Doodlefolk\mods that add objects (drawn with shapes or a simple SVG),
/// hats, names for new figures and pets, and jokes. See docs/MODDING.md. A broken file is skipped and its problem
/// is shown in the Studio; nothing in a mod can run code.</summary>
static class Mods
{
    public static string Dir => Path.Combine(Path.GetDirectoryName(Settings.FilePath)!, "mods");
    public static readonly List<string> Loaded = new(), Errors = new();
    public static readonly List<string> Jokes = new(), FigureNames = new();
    public static readonly Dictionary<PetKind, List<string>> PetNames = new();
    public static int ItemCount, HatCount;

    public static void Load() => Load(Array.Empty<string>());

    /// <summary>The mods folder, plus any extra folders (subscribed Steam Workshop items).</summary>
    public static void Load(IReadOnlyList<string> extraDirs)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string example = Path.Combine(Dir, "example-mod.json.txt");
            if (!File.Exists(example)) File.WriteAllText(example, ExampleMod);
        }
        catch (Exception e) { Errors.Add("Couldn't open the mods folder: " + e.Message); return; }
        var items = new List<ItemDef>();
        var hats = new List<LookPart>();
        var files = Directory.GetFiles(Dir, "*.json").OrderBy(f => f).Select(f => (f, workshop: false))
            .Concat(extraDirs.Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.json").Select(f => (f, workshop: true))));
        foreach (var (file, workshop) in files)
        {
            string name = (workshop ? "Workshop: " : "") + Path.GetFileName(file);
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                var root = doc.RootElement;
                int before = items.Count + hats.Count;
                if (root.TryGetProperty("items", out var its)) foreach (var it in its.EnumerateArray()) items.Add(ItemFrom(it, Path.GetDirectoryName(file)!));
                if (root.TryGetProperty("hats", out var hs)) foreach (var h in hs.EnumerateArray()) hats.Add(HatFrom(h, Path.GetDirectoryName(file)!));
                if (root.TryGetProperty("jokes", out var js)) foreach (var j in js.EnumerateArray()) if (j.GetString() is { Length: > 0 and <= 200 } s) Jokes.Add(s);
                if (root.TryGetProperty("names", out var ns))
                    foreach (var prop in ns.EnumerateObject())
                    {
                        var list = prop.Value.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length is > 0 and <= 24).ToList();
                        if (prop.Name.Equals("figures", StringComparison.OrdinalIgnoreCase)) FigureNames.AddRange(list);
                        else if (Enum.TryParse<PetKind>(prop.Name.TrimEnd('s'), true, out var kind)) { if (!PetNames.TryGetValue(kind, out var l)) PetNames[kind] = l = new(); l.AddRange(list); }
                        else throw new FormatException($"unknown name list \"{prop.Name}\" (use figures, cats, dogs, parrots, rabbits or hamsters)");
                    }
                Loaded.Add(name);
            }
            catch (Exception e) { Errors.Add($"{name}: {e.Message}"); }
        }
        // Keys must be unique (a mod can't replace a built-in object or hat).
        var newItems = items.Where(d => ItemCatalog.Find(d.Key) == null).GroupBy(d => d.Key).Select(g => g.First()).ToList();
        foreach (var d in items.Except(newItems)) Errors.Add($"object \"{d.Key}\" skipped: that name's taken");
        ItemCatalog.AddMods(newItems);
        var newHats = hats.Where(h => Look.Find(Look.Hats, h.Key) == null).GroupBy(h => h.Key).Select(g => g.First()).ToList();
        Look.AddHats(newHats);
        ItemCount = newItems.Count; HatCount = newHats.Count;
        if (Loaded.Count > 0 || Errors.Count > 0) World.Log($"mods: {string.Join(", ", Loaded)}; {ItemCount} objects, {HatCount} hats, {Jokes.Count} jokes; errors: {string.Join(" | ", Errors)}");
    }

    // ---------------- objects ----------------

    static ItemDef ItemFrom(JsonElement e, string dir)
    {
        string key = Req(e, "key").ToLowerInvariant();
        if (!Regex.IsMatch(key, "^[a-z0-9_-]{1,32}$")) throw new FormatException($"object key \"{key}\" should be lowercase letters, digits, - or _");
        var d = new ItemDef
        {
            Key = key,
            Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? key : key,
            Color = e.TryGetProperty("colour", out var c) || e.TryGetProperty("color", out c) ? Hex(c.GetString()) : M.Hex(0x9E9E9E),
            W = Num(e, "w", 0), H = Num(e, "h", 0),
        };
        d.Words = (e.TryGetProperty("words", out var ws) ? ws.EnumerateArray().Select(w => (w.GetString() ?? "").ToLowerInvariant()).Where(w => w.Length > 0) : Enumerable.Empty<string>())
                  .Prepend(d.Name.ToLowerInvariant()).Distinct().ToArray();
        if (e.TryGetProperty("svg", out var svg))
        {
            string text = svg.GetString() ?? "";
            if (!text.TrimStart().StartsWith("<")) text = File.ReadAllText(SafePath(dir, text));
            var (shapes, vw, vh) = FromSvg(text, d.Color, item: true);
            d.Shapes = shapes;
            if (d.W <= 0) d.W = vw;
            if (d.H <= 0) d.H = vh;
        }
        else if (e.TryGetProperty("shapes", out var ss)) d.Shapes = ss.EnumerateArray().Select(ShapeFrom).ToArray();
        else throw new FormatException($"object \"{key}\" needs \"shapes\" or \"svg\"");
        if (d.W <= 0 || d.H <= 0 || d.W > 400 || d.H > 400) throw new FormatException($"object \"{key}\" needs a size (w and h, up to 400)");
        if (d.Shapes.Length > 400) throw new FormatException($"object \"{key}\" has too many shapes (400 at most)");
        if (e.TryGetProperty("verbs", out var vs))
            d.Verbs = vs.EnumerateArray().Select(v => Enum.TryParse<Verb>(v.GetString(), true, out var vb) && vb is not (Verb.Create or Verb.Lasso) ? vb : throw new FormatException($"unknown verb \"{v.GetString()}\"")).Distinct().ToArray();
        if (e.TryGetProperty("likes", out var ls))
            d.Likes = ls.EnumerateArray().Select(v => Enum.TryParse<Thing>(v.GetString(), true, out var t) ? t : throw new FormatException($"unknown thing \"{v.GetString()}\"")).ToArray();
        if (e.TryGetProperty("surface", out var sf)) { d.Surface = sf.GetSingle(); d.SurfX1 = -d.W / 2; d.SurfX2 = d.W / 2; }
        if (e.TryGetProperty("seats", out var seats)) { d.Seats = seats.EnumerateArray().Select(x => x.GetSingle()).ToArray(); d.SeatY = Num(e, "seatY", d.H * 0.45f); }
        d.Comfort = Num(e, "comfort", 0.5f);
        d.Bounce = Num(e, "bounce", 0);
        d.Mass = Num(e, "mass", 1);
        d.Carry = e.TryGetProperty("carry", out var ca) && ca.GetBoolean();
        if (d.Verbs.Contains(Verb.Eat)) { d.Carry = true; d.Bites = (int)Num(e, "bites", 4); }
        if (d.Verbs.Contains(Verb.Sit) && d.Seats.Length == 0) { d.Seats = new[] { 0f }; d.SeatY = d.H * 0.45f; }
        if (d.Verbs.Contains(Verb.Stand) && d.Surface < 0) { d.Surface = d.H; d.SurfX1 = -d.W / 2; d.SurfX2 = d.W / 2; }
        if (d.Verbs.Contains(Verb.Wield)) { d.Reach = Num(e, "reach", d.H); d.Damage = Num(e, "damage", 1); }
        return d;
    }

    static Shape ShapeFrom(JsonElement s)
    {
        string k = Req(s, "k");
        if (k.Length != 1 || !"roepl c".Contains(k[0])) throw new FormatException($"unknown shape kind \"{k}\" (r, o, e, p, l or c)");
        var p = s.GetProperty("p").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        int col = s.TryGetProperty("c", out var c) ? c.ValueKind == JsonValueKind.String ? ItemDef.AddColour(Hex(c.GetString())) : c.GetInt32() : 0;
        return new Shape(k[0], p, col, s.TryGetProperty("w", out var w) ? w.GetSingle() : 1.4f, Over: s.TryGetProperty("over", out var o) && o.GetBoolean());
    }

    // ---------------- hats ----------------

    static LookPart HatFrom(JsonElement e, string dir)
    {
        string key = "mod-" + Req(e, "key").ToLowerInvariant();
        string name = e.TryGetProperty("name", out var n) ? n.GetString() ?? key : key;
        Shape[] front, back = Array.Empty<Shape>();
        if (e.TryGetProperty("svg", out var svg))
        {
            string text = svg.GetString() ?? "";
            if (!text.TrimStart().StartsWith("<")) text = File.ReadAllText(SafePath(dir, text));
            front = FromSvg(text, M.Hex(0xE53935), item: false).shapes;
        }
        else
        {
            front = e.GetProperty("front").EnumerateArray().Select(ShapeFrom).ToArray();
            if (e.TryGetProperty("back", out var b)) back = b.EnumerateArray().Select(ShapeFrom).ToArray();
        }
        return new LookPart(key, name, front, back.Length > 0 ? back : null);
    }

    // ---------------- SVG ----------------

    /// <summary>The simple parts of SVG: rect, circle, ellipse, line, polyline, polygon and straight-line paths, with
    /// fill and stroke colours. Objects: one SVG unit is one object unit and the bottom centre of the viewBox sits on
    /// the floor. Hats: 10 SVG units are one head radius, with (0, 0) at the centre of the head.</summary>
    static (Shape[] shapes, float w, float h) FromSvg(string text, Color4 main, bool item)
    {
        var doc = XDocument.Parse(text);
        var root = doc.Root ?? throw new FormatException("empty SVG");
        float vx = 0, vy = 0, vw = 100, vh = 100;
        if (root.Attribute("viewBox")?.Value is { } vb)
        {
            var v = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(F).ToArray();
            if (v.Length == 4) (vx, vy, vw, vh) = (v[0], v[1], v[2], v[3]);
        }
        else { vw = F(root.Attribute("width")?.Value ?? "100"); vh = F(root.Attribute("height")?.Value ?? "100"); }
        float cx = vx + vw / 2, by = vy + vh;
        (float, float) Map(float x, float y) => item ? (x - cx, by - y) : (x / 10, -y / 10);
        float Len(float l) => item ? l : l / 10;
        var shapes = new List<Shape>();
        foreach (var el in root.Descendants())
        {
            string tag = el.Name.LocalName;
            string? Attr(string a)
            {
                if (el.Attribute(a)?.Value is { } v) return v;
                if (el.Attribute("style")?.Value is { } style)
                    foreach (var part in style.Split(';'))
                    {
                        var kv = part.Split(':', 2);
                        if (kv.Length == 2 && kv[0].Trim() == a) return kv[1].Trim();
                    }
                // Inherit from a parent group.
                for (var p = el.Parent; p != null; p = p.Parent) if (p.Attribute(a)?.Value is { } pv) return pv;
                return null;
            }
            string fill = Attr("fill") ?? "#000000", stroke = Attr("stroke") ?? "none";
            float sw = Len(F(Attr("stroke-width") ?? "1"));
            int Col(string css) => ColourIndex(css, main);
            bool filled = fill != "none", stroked = stroke != "none";
            float A(string a) => F(el.Attribute(a)?.Value ?? "0");
            switch (tag)
            {
                case "rect":
                {
                    var (x0, y0) = Map(A("x"), A("y") + A("height"));
                    var (x1, y1) = Map(A("x") + A("width"), A("y"));
                    float rx = Len(A("rx"));
                    if (filled) shapes.Add(rx > 0 ? new Shape('o', new[] { Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1), rx }, Col(fill)) : new Shape('r', new[] { x0, y0, x1, y1 }, Col(fill)));
                    else if (stroked) shapes.Add(new Shape('c', new[] { x0, y0, x1, y0, x1, y1, x0, y1, x0, y0 }, Col(stroke), sw));
                    break;
                }
                case "circle":
                case "ellipse":
                {
                    var (ex, ey) = Map(A("cx"), A("cy"));
                    float rx = Len(tag == "circle" ? A("r") : A("rx")), ry = Len(tag == "circle" ? A("r") : A("ry"));
                    shapes.Add(new Shape('e', new[] { ex, ey, rx, ry }, Col(filled ? fill : stroke)));
                    break;
                }
                case "line":
                {
                    var (x0, y0) = Map(A("x1"), A("y1"));
                    var (x1, y1) = Map(A("x2"), A("y2"));
                    shapes.Add(new Shape('l', new[] { x0, y0, x1, y1 }, Col(stroked ? stroke : fill), sw));
                    break;
                }
                case "polygon":
                case "polyline":
                case "path":
                {
                    bool closed = false;
                    var pts = tag == "path" ? PathPoints(el.Attribute("d")?.Value ?? "", out closed) : Pairs(el.Attribute("points")?.Value ?? "");
                    if (tag == "polygon") closed = true; else if (tag == "polyline") closed = false; else closed = closed || filled;
                    if (pts.Count < 2) break;
                    var flat = pts.SelectMany(p => { var (mx, my) = Map(p.x, p.y); return new[] { mx, my }; }).ToArray();
                    if (closed && filled && pts.Count >= 3) shapes.Add(new Shape('p', flat, Col(fill)));
                    else shapes.Add(new Shape('c', flat, Col(stroked ? stroke : fill), sw));
                    break;
                }
            }
            if (shapes.Count > 400) throw new FormatException("the SVG has too many shapes (400 at most)");
        }
        return (shapes.ToArray(), vw, vh);
    }

    static List<(float x, float y)> Pairs(string s)
    {
        var n = Regex.Matches(s, @"-?\d*\.?\d+(?:e-?\d+)?").Select(m => F(m.Value)).ToArray();
        var list = new List<(float, float)>();
        for (int i = 0; i + 1 < n.Length; i += 2) list.Add((n[i], n[i + 1]));
        return list;
    }

    /// <summary>Straight-line paths (M, L, H, V, Z, upper or lower case); curves are followed to their end points.</summary>
    static List<(float x, float y)> PathPoints(string d, out bool closed)
    {
        closed = false;
        var pts = new List<(float x, float y)>();
        float x = 0, y = 0;
        foreach (System.Text.RegularExpressions.Match m in Regex.Matches(d, @"([MLHVZCSQTAmlhvzcsqta])([^MLHVZCSQTAmlhvzcsqta]*)"))
        {
            char cmd = m.Groups[1].Value[0];
            var n = Regex.Matches(m.Groups[2].Value, @"-?\d*\.?\d+(?:e-?\d+)?").Select(v => F(v.Value)).ToArray();
            bool rel = char.IsLower(cmd);
            int step = char.ToUpper(cmd) switch { 'M' or 'L' or 'T' => 2, 'H' or 'V' => 1, 'C' => 6, 'S' or 'Q' => 4, 'A' => 7, _ => 0 };
            if (char.ToUpper(cmd) == 'Z') { closed = true; continue; }
            for (int i = 0; step > 0 && i + step <= n.Length; i += step)
            {
                switch (char.ToUpper(cmd))
                {
                    case 'H': x = rel ? x + n[i] : n[i]; break;
                    case 'V': y = rel ? y + n[i] : n[i]; break;
                    default: { float nx = n[i + step - 2], ny = n[i + step - 1]; x = rel ? x + nx : nx; y = rel ? y + ny : ny; break; }
                }
                pts.Add((x, y));
            }
        }
        return pts;
    }

    static int ColourIndex(string css, Color4 main)
    {
        css = css.Trim().ToLowerInvariant();
        if (css is "currentcolor" or "main") return 0;
        if (css == "dark") return 1;
        if (css == "light") return 2;
        var c = Hex(NamedColour(css));
        if (MathF.Abs(c.R - main.R) + MathF.Abs(c.G - main.G) + MathF.Abs(c.B - main.B) < 0.02f) return 0;
        return ItemDef.AddColour(c);
    }

    static string NamedColour(string css) => css switch
    {
        "black" => "#000000", "white" => "#ffffff", "red" => "#e53935", "green" => "#43a047", "blue" => "#1e88e5", "yellow" => "#fdd835",
        "orange" => "#fb8c00", "purple" => "#8e24aa", "pink" => "#ec407a", "brown" => "#795548", "grey" or "gray" => "#9e9e9e",
        _ when css.Length == 4 && css[0] == '#' => $"#{css[1]}{css[1]}{css[2]}{css[2]}{css[3]}{css[3]}",
        _ => css,
    };

    static Color4 Hex(string? s) => s is { Length: 7 } && s[0] == '#' && int.TryParse(s[1..], NumberStyles.HexNumber, null, out _) ? Settings.ParseHex(s) : M.Hex(0x9E9E9E);
    static float F(string s) => float.TryParse(s.Trim().TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    static float Num(JsonElement e, string name, float fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
    static string Req(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.GetString() is { Length: > 0 } s ? s : throw new FormatException($"missing \"{name}\"");

    /// <summary>An SVG file named in a mod must sit in the mods folder.</summary>
    static string SafePath(string dir, string file)
    {
        string full = Path.GetFullPath(Path.Combine(dir, file));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new FormatException($"\"{file}\" must be inside the mods folder");
        return full;
    }

    const string ExampleMod = """
        {
          // An example mod. Rename this file to end in .json (and restart Doodlefolk) to try it.
          // Full guide: https://github.com/Lindorak/Doodlefolk/blob/main/docs/MODDING.md
          "items": [
            {
              "key": "lavalamp", "name": "Lava lamp", "words": ["lava lamp"], "colour": "#E040FB",
              "w": 12, "h": 28, "verbs": ["Dance"], "likes": ["Dancing"],
              "svg": "<svg viewBox='0 0 12 28'><rect x='3' y='24' width='6' height='4' fill='#5D6670'/><path d='M4 24 L2 8 L6 2 L10 8 L8 24 Z' fill='#E040FB'/><circle cx='6' cy='12' r='1.6' fill='#FFD54F'/><rect x='3' y='0' width='6' height='2' fill='#5D6670'/></svg>"
            }
          ],
          "hats": [
            { "key": "tiara", "name": "Tiara", "svg": "<svg viewBox='-12 -16 24 8'><path d='M-8 -9 L-5 -14 L-2 -10 L0 -15 L2 -10 L5 -14 L8 -9 Z' fill='#F2C14E'/><circle cx='0' cy='-12' r='1.2' fill='#EC407A'/></svg>" }
          ],
          "names": { "figures": ["Doodle", "Scribble"], "cats": ["Sir Pounce"] },
          "jokes": ["what do you call a stick figure with no stick? a figure of speech!"]
        }
        """;
}
