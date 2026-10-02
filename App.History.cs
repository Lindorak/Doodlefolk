using System.Net;
using System.Text;

namespace Doodlefolk;

/// <summary>Something that happened, for the town's history.</summary>
sealed class HistoryEvent
{
    public DateTime When { get; set; }
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> Who { get; set; } = new();
    public int Weight { get; set; }
}

/// <summary>Town history (an idea from Dwarf Fortress's legends): everything notable that's ever happened, kept for
/// good (the newspaper only keeps three weeks), and the family tree, living and remembered, in the Studio's History
/// tab. Both can be exported: the tree as a picture, the history as a little web page.</summary>
sealed partial class App
{
    void OnNewsForHistory(string kind, string text, int weight, Figure[] who)
    {
        if (weight < 2 || kind is "app" || _selfTest && kind == "requests") return;
        var h = _settings.History;
        h.Add(new HistoryEvent { When = DateTime.Now, Kind = kind, Text = text, Who = who.Select(f => f.Name).ToList(), Weight = weight });
        if (h.Count > 3000) h.RemoveRange(0, h.Count - 3000);
    }

    /// <summary>Everyone, living and remembered, with parents and partners (by name).</summary>
    object FamilyState() => _w.Figures.Where(f => f.Visitor == VisitorKind.None).Select(f => new
    {
        name = f.Name, colour = Settings.Hex(f.Color), alive = true, age = (int)f.Brain.AgeYears,
        parents = f.Brain.ParentNames, partner = f.Brain.Sweetheart(_w)?.Name ?? "", died = "",
    }).Concat(_settings.Memorials.Select(m => new
    {
        name = m.Name, colour = m.Colour, alive = false, age = m.Age, parents = m.Parents, partner = m.Partner, died = m.Died.ToString("yyyy"),
    })).ToList();

    object HistoryMessage() => new
    {
        t = "history",
        family = FamilyState(),
        events = _settings.History.AsEnumerable().Reverse().Take(1500).Select(e => new { when = e.When.ToString("d MMM yyyy, HH:mm"), month = e.When.ToString("MMMM yyyy"), kind = e.Kind, text = e.Text, weight = e.Weight }),
        since = _settings.History.Count > 0 ? _settings.History[0].When.ToString("d MMMM yyyy") : "",
    };

    /// <summary>Save the family tree picture the Studio drew (PNG as base64, or SVG text) to Pictures\Doodlefolk.</summary>
    string ExportTree(string kind, string data)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.PicturesDir);
            string file = Path.Combine(AppPaths.PicturesDir, $"Doodlefolk family tree {DateTime.Now:yyyy-MM-dd HH.mm}.{(kind == "svg" ? "svg" : "png")}");
            if (kind == "svg")
            {
                if (!data.TrimStart().StartsWith("<svg") || data.Contains("<script", StringComparison.OrdinalIgnoreCase)) return "That didn't look like a picture.";
                File.WriteAllText(file, data);
            }
            else
            {
                var bytes = Convert.FromBase64String(data.Contains(',') ? data[(data.IndexOf(',') + 1)..] : data);
                if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != (byte)'P') return "That didn't look like a picture.";
                File.WriteAllBytes(file, bytes);
            }
            _w.Sticker("history");
            return $"Saved to Pictures\\Doodlefolk\\{Path.GetFileName(file)}";
        }
        catch (Exception e) { return "Couldn't save it: " + e.Message; }
    }

    /// <summary>The whole history as one self-contained web page (paper style), saved to Pictures\Doodlefolk.</summary>
    string ExportHistory()
    {
        var sb = new StringBuilder();
        string E(string s) => WebUtility.HtmlEncode(s);
        sb.Append("<!doctype html><meta charset=utf-8><title>A history of the town</title><style>body{font:16px/1.5 'Segoe Print','Comic Sans MS',cursive;background:#fbf8ef;color:#262420;max-width:760px;margin:40px auto;padding:0 20px}h1{font-size:34px;margin:0}h2{margin:28px 0 6px;border-bottom:1.5px dashed #ccc5b5}li{margin:3px 0}.w4{font-weight:bold}.w5{font-weight:bold;color:#c62828}.when{color:#8a8478;font-size:13px}.gone{color:#8a8478}</style>");
        sb.Append($"<h1>A history of the town</h1><p class=when>From {E(_settings.History.FirstOrDefault()?.When.ToString("d MMMM yyyy") ?? DateTime.Now.ToString("d MMMM yyyy"))} to {DateTime.Now:d MMMM yyyy}. Written by Doodlefolk.</p>");
        sb.Append("<h2>The townsfolk</h2><ul>");
        foreach (var f in _w.Figures.Where(f => f.Visitor == VisitorKind.None))
            sb.Append($"<li><b style='color:{Settings.Hex(f.Color)}'>●</b> {E(f.Name)}, {(int)f.Brain.AgeYears}{(f.Brain.ParentNames.Count > 0 ? $", child of {E(string.Join(" and ", f.Brain.ParentNames))}" : "")}{(f.Brain.Sweetheart(_w) is { } sw ? $", sweetheart of {E(sw.Name)}" : "")}</li>");
        foreach (var m in _settings.Memorials)
            sb.Append($"<li class=gone>🕯 {E(m.Name)}, {m.Age}, died {E(m.Died.ToString("d MMMM yyyy"))} ({E(m.Cause)}). <i>“{E(m.Epitaph)}”</i></li>");
        sb.Append("</ul>");
        foreach (var month in _settings.History.GroupBy(e => e.When.ToString("MMMM yyyy")))
        {
            sb.Append($"<h2>{E(month.Key)}</h2><ul>");
            foreach (var e in month) sb.Append($"<li class=w{Math.Min(5, e.Weight)}><span class=when>{e.When:d MMM HH:mm}</span> {E(e.Text)}</li>");
            sb.Append("</ul>");
        }
        try
        {
            Directory.CreateDirectory(AppPaths.PicturesDir);
            string file = Path.Combine(AppPaths.PicturesDir, $"Doodlefolk town history {DateTime.Now:yyyy-MM-dd}.html");
            File.WriteAllText(file, sb.ToString());
            _w.Sticker("history");
            return $"Saved to Pictures\\Doodlefolk\\{Path.GetFileName(file)}";
        }
        catch (Exception e) { return "Couldn't save it: " + e.Message; }
    }
}
