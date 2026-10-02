using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>A meme template we know how to caption: where each line of text goes (fractions of the picture).</summary>
sealed record MemeTemplate(string Name, string Url, RectangleF[] Boxes, bool Dark = false);

/// <summary>One meme the town has made (or found).</summary>
sealed class MemeEntry
{
    public string File { get; set; } = "";
    public DateTime When { get; set; }
    public string Template { get; set; } = "";
    public string Text { get; set; } = "";
    public string Why { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Shown { get; set; }
}

/// <summary>What's going on, for picking a meme that fits (prank mode).</summary>
sealed record MemeContext(HashSet<string> Tags, string Figure, string Other, string Pet, string Weather, string Detail);

/// <summary>Memes for prank mode, chosen for the moment rather than at random: Monday mornings, Friday afternoons,
/// 2 a.m., rain, a long stretch of typing, a failed build, a festival, a hungry cat, someone's new sweetheart. The
/// pictures are well-known blank templates from Imgflip (fetched once and kept); the captions are written here (or by
/// the AI, if you've set that up) from what's actually happening, with names from your town. Optionally, real posts
/// from r/wholesomememes whose titles match the moment (never NSFW or spoilers). Only pictures are downloaded, only
/// from known picture hosts, and nothing about you is sent anywhere.</summary>
static class Memes
{
    static RectangleF R(float x, float y, float w, float h) => new(x, y, w, h);
    static readonly RectangleF Top = R(0.03f, 0.02f, 0.94f, 0.22f), Bottom = R(0.03f, 0.76f, 0.94f, 0.22f);

    public static readonly MemeTemplate[] Templates =
    {
        new("Drake Hotline Bling", "https://i.imgflip.com/30b1gx.jpg", new[] { R(0.52f, 0.05f, 0.46f, 0.4f), R(0.52f, 0.55f, 0.46f, 0.4f) }, Dark: true),
        new("Two Buttons", "https://i.imgflip.com/1g8my4.jpg", new[] { R(0.06f, 0.1f, 0.36f, 0.12f), R(0.46f, 0.06f, 0.38f, 0.12f), R(0.05f, 0.83f, 0.9f, 0.15f) }),
        new("Distracted Boyfriend", "https://i.imgflip.com/1ur9b0.jpg", new[] { R(0.1f, 0.62f, 0.3f, 0.2f), R(0.43f, 0.42f, 0.27f, 0.2f), R(0.68f, 0.55f, 0.3f, 0.2f) }),
        new("Woman Yelling At Cat", "https://i.imgflip.com/345v97.jpg", new[] { R(0.02f, 0.02f, 0.46f, 0.25f), R(0.52f, 0.02f, 0.46f, 0.25f) }),
        new("This Is Fine", "https://i.imgflip.com/wxica.jpg", new[] { R(0.03f, 0.03f, 0.55f, 0.3f), R(0.6f, 0.03f, 0.38f, 0.3f) }),
        new("Waiting Skeleton", "https://i.imgflip.com/2fm6x.jpg", new[] { Top, Bottom }),
        new("Hide the Pain Harold", "https://i.imgflip.com/gk5el.jpg", new[] { Top, Bottom }),
        new("Roll Safe Think About It", "https://i.imgflip.com/1h7in3.jpg", new[] { Top, Bottom }),
        new("Disaster Girl", "https://i.imgflip.com/23ls.jpg", new[] { Top, Bottom }),
        new("One Does Not Simply", "https://i.imgflip.com/1bij.jpg", new[] { Top, Bottom }),
        new("Change My Mind", "https://i.imgflip.com/24y43o.jpg", new[] { R(0.33f, 0.62f, 0.42f, 0.2f) }, Dark: true),
        new("Batman Slapping Robin", "https://i.imgflip.com/9ehk.jpg", new[] { R(0.03f, 0.02f, 0.44f, 0.2f), R(0.53f, 0.02f, 0.44f, 0.2f) }, Dark: true),
        new("Expanding Brain", "https://i.imgflip.com/1jwhww.jpg", new[] { R(0.02f, 0.02f, 0.46f, 0.21f), R(0.02f, 0.27f, 0.46f, 0.21f), R(0.02f, 0.52f, 0.46f, 0.21f), R(0.02f, 0.77f, 0.46f, 0.21f) }, Dark: true),
        new("UNO Draw 25 Cards", "https://i.imgflip.com/3lmzyx.jpg", new[] { R(0.1f, 0.25f, 0.33f, 0.24f), R(0.5f, 0.78f, 0.48f, 0.2f) }),
        new("Gru's Plan", "https://i.imgflip.com/26jxvz.jpg", new[] { R(0.23f, 0.08f, 0.22f, 0.32f), R(0.73f, 0.08f, 0.22f, 0.32f), R(0.23f, 0.58f, 0.22f, 0.32f), R(0.73f, 0.58f, 0.22f, 0.32f) }, Dark: true),
        new("Is This A Pigeon", "https://i.imgflip.com/1o00in.jpg", new[] { R(0.15f, 0.55f, 0.35f, 0.15f), R(0.6f, 0.12f, 0.35f, 0.15f), R(0.03f, 0.82f, 0.94f, 0.16f) }),
        new("Evil Kermit", "https://i.imgflip.com/1e7ql7.jpg", new[] { Top, Bottom }),
        new("Monkey Puppet", "https://i.imgflip.com/2gnnjh.jpg", new[] { Top, Bottom }),
        new("Bernie I Am Once Again Asking For Your Support", "https://i.imgflip.com/3oevdk.jpg", new[] { Bottom }),
        new("Epic Handshake", "https://i.imgflip.com/28j0te.jpg", new[] { R(0.03f, 0.55f, 0.32f, 0.2f), R(0.65f, 0.55f, 0.32f, 0.2f), R(0.33f, 0.12f, 0.34f, 0.2f) }),
    };

    static MemeTemplate T(string name) => Templates.First(t => t.Name == name);

    /// <summary>The local caption book: for each situation, a template and its lines ({fig}, {other}, {pet}, {day},
    /// {weather} are filled in from the town).</summary>
    static readonly (string tag, string template, string[] lines)[] Book =
    {
        ("monday", "Waiting Skeleton", new[] { "ME WAITING FOR", "FRIDAY" }),
        ("monday", "This Is Fine", new[] { "MONDAY MORNING", "this is fine" }),
        ("monday", "Hide the Pain Harold", new[] { "WHEN SOMEONE ASKS", "HOW YOUR MONDAY'S GOING" }),
        ("friday", "Drake Hotline Bling", new[] { "starting something new at 4pm on a friday", "starting the weekend early" }),
        ("friday", "Roll Safe Think About It", new[] { "CAN'T HAVE MONDAY PROBLEMS", "IF IT'S STILL FRIDAY" }),
        ("weekend", "Evil Kermit", new[] { "ME: I'LL REST THIS WEEKEND", "ALSO ME: JUST ONE MORE THING" }),
        ("latenight", "Two Buttons", new[] { "go to sleep", "one more thing", "ME AT {time}" }),
        ("latenight", "Waiting Skeleton", new[] { "{fig} WAITING FOR YOU", "TO GO TO BED" }),
        ("morning", "Drake Hotline Bling", new[] { "a slow, calm morning", "a coffee immediately" }),
        ("rain", "Hide the Pain Harold", new[] { "IT'S RAINING AGAIN", "AND {fig} FORGOT THE UMBRELLA" }),
        ("rain", "Disaster Girl", new[] { "THE PICNIC WAS TODAY", "THE RAIN HAD OTHER PLANS" }),
        ("snow", "One Does Not Simply", new[] { "ONE DOES NOT SIMPLY", "BUILD JUST ONE SNOWMAN" }),
        ("storm", "This Is Fine", new[] { "THE STORM OUTSIDE", "{fig}: this is fine" }),
        ("hot", "Monkey Puppet", new[] { "WHEN IT'S {weather}", "AND SOMEONE SAYS 'HOT DRINK?'" }),
        ("typing", "Drake Hotline Bling", new[] { "taking a break", "typing for three more hours" }),
        ("typing", "Expanding Brain", new[] { "typing", "typing fast", "typing very fast", "the keyboard is now on fire" }),
        ("typing", "Is This A Pigeon", new[] { "{fig}", "YOU, TYPING", "IS THIS A MARATHON?" }),
        ("buildfail", "This Is Fine", new[] { "THE BUILD IS RED", "this is fine" }),
        ("buildfail", "Batman Slapping Robin", new[] { "it works on my machine", "IT DOESN'T" }),
        ("buildpass", "Drake Hotline Bling", new[] { "reading why it failed", "it passed first try" }),
        ("buildpass", "Roll Safe Think About It", new[] { "CAN'T HAVE FAILING TESTS", "IF THEY ALL PASS" }),
        ("focus", "Epic Handshake", new[] { "YOU", "{fig}", "GETTING THINGS DONE" }),
        ("back", "Waiting Skeleton", new[] { "THE TOWN WAITING", "FOR YOU TO COME BACK" }),
        ("music", "Drake Hotline Bling", new[] { "silence", "{fig} dancing to your music" }),
        ("festival", "Two Buttons", new[] { "dance", "eat everything", "{fig} AT THE FESTIVAL" }),
        ("race", "Gru's Plan", new[] { "enter the race", "run really fast", "trip over {other}", "trip over {other}" }),
        ("fight", "Batman Slapping Robin", new[] { "let's settle this calmly", "NO" }),
        ("love", "Distracted Boyfriend", new[] { "{other}", "{fig}", "everyone else" }),
        ("love", "Epic Handshake", new[] { "{fig}", "{other}", "BEING ADORABLE" }),
        ("pet", "Woman Yelling At Cat", new[] { "WHO ATE THE PIZZA?!", "{pet}: wasn't me" }),
        ("pet", "Bernie I Am Once Again Asking For Your Support", new[] { "{pet} IS ONCE AGAIN ASKING FOR DINNER" }),
        ("pet", "Change My Mind", new[] { "{pet} deserves a second dinner" }),
        ("fish", "One Does Not Simply", new[] { "ONE DOES NOT SIMPLY", "CATCH THE GOLDEN KOI" }),
        ("visitor", "Is This A Pigeon", new[] { "{fig}", "{other}", "IS THIS A TOURIST?" }),
        ("baby", "Expanding Brain", new[] { "one figure", "two figures", "two figures in love", "a whole tiny {fig}" }),
        ("any", "UNO Draw 25 Cards", new[] { "share your snacks or draw 25", "{fig}" }),
        ("any", "Change My Mind", new[] { "the desktop is better with us on it" }),
        ("any", "Distracted Boyfriend", new[] { "a nap", "{fig}", "the job" }),
        ("any", "Two Buttons", new[] { "climb the window", "sit on the window", "{fig}" }),
    };

    public static string Dir => Path.Combine(AppPaths.DataDir, "memes");
    static string Index => Path.Combine(Dir, "memes.json");
    static readonly object Gate = new();
    static List<MemeEntry>? _list;
    public static List<MemeEntry> List
    {
        get
        {
            lock (Gate)
            {
                if (_list != null) return _list;
                try { _list = File.Exists(Index) ? JsonSerializer.Deserialize<List<MemeEntry>>(File.ReadAllText(Index)) ?? new() : new(); } catch { _list = new(); }
                _list.RemoveAll(m => !File.Exists(Path.Combine(Dir, m.File)));
                return _list;
            }
        }
    }
    static void Save() { lock (Gate) { try { Directory.CreateDirectory(Dir); File.WriteAllText(Index, JsonSerializer.Serialize(List)); } catch { } } }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static readonly string[] PictureHosts = { "i.imgflip.com", "i.redd.it", "preview.redd.it", "i.imgur.com" };
    static bool Allowed(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && PictureHosts.Contains(u.Host);

    /// <summary>Fill in names and the like.</summary>
    static string Fill(string line, MemeContext c) => line.Replace("{fig}", c.Figure).Replace("{other}", c.Other.Length > 0 ? c.Other : "someone")
        .Replace("{pet}", c.Pet.Length > 0 ? c.Pet : "the cat").Replace("{weather}", c.Weather).Replace("{day}", DateTime.Now.DayOfWeek.ToString())
        .Replace("{time}", DateTime.Now.ToString("h:mm tt").ToLowerInvariant());

    /// <summary>Choose what to make: the most specific situation that applies (never "any" if something better does).</summary>
    public static (MemeTemplate t, string[] lines, string why)? Choose(MemeContext c, Random rng)
    {
        var recent = List.TakeLast(8).Select(m => m.Text).ToHashSet();
        var fits = Book.Where(b => c.Tags.Contains(b.tag) && (!b.lines.Any(l => l.Contains("{pet}")) || c.Pet.Length > 0)
                                   && (!b.lines.Any(l => l.Contains("{other}")) || c.Other.Length > 0)).ToList();
        if (fits.Count == 0) fits = Book.Where(b => b.tag == "any").ToList();
        fits = fits.Where(b => !recent.Contains(string.Join(" / ", b.lines.Select(l => Fill(l, c))))).ToList();
        if (fits.Count == 0) return null;
        var (tag, template, lines) = fits[rng.Next(fits.Count)];
        return (T(template), lines.Select(l => Fill(l, c)).ToArray(), tag);
    }

    /// <summary>Make one (on a worker thread): template picture (fetched once), our caption on it, saved to the memes
    /// folder. Returns the entry, or null.</summary>
    public static async Task<MemeEntry?> Make(MemeTemplate t, string[] lines, string why, string source)
    {
        var pic = await Template(t);
        if (pic == null) return null;
        using var img = new Bitmap(pic);
        Caption(img, t, lines);
        string file = $"meme {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png";
        using (var small = Fit(img, 800)) small.Save(Path.Combine(Dir, file), ImageFormat.Png);
        var e = new MemeEntry { File = file, When = DateTime.Now, Template = t.Name, Text = string.Join(" / ", lines), Why = why, Source = source };
        lock (Gate) { List.Add(e); while (List.Count > 60) { try { File.Delete(Path.Combine(Dir, List[0].File)); } catch { } List.RemoveAt(0); } }
        Save();
        return e;
    }

    static async Task<string?> Template(MemeTemplate t)
    {
        string dir = Path.Combine(Dir, "templates");
        string path = Path.Combine(dir, Path.GetFileName(new Uri(t.Url).AbsolutePath));
        if (File.Exists(path)) return path;
        if (!Allowed(t.Url)) return null;
        try
        {
            Directory.CreateDirectory(dir);
            var bytes = await Http.GetByteArrayAsync(t.Url);
            if (bytes.Length > 8_000_000) return null;
            await File.WriteAllBytesAsync(path, bytes);
            return path;
        }
        catch (Exception e) { World.Log($"meme template download failed: {e.Message}"); return null; }
    }

    static Bitmap Fit(Image img, int max)
    {
        float k = Math.Min(1, max / (float)Math.Max(img.Width, img.Height));
        var b = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(b);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(img, 0, 0, b.Width, b.Height);
        return b;
    }

    static readonly string Font = new[] { "Impact", "Arial Black", "Segoe UI Black", "Arial" }.First(n => FontFamily.Families.Any(f => f.Name == n));

    /// <summary>Classic meme lettering (or dark lettering on the light templates' panels): each line shrunk to fit its box.</summary>
    public static void Caption(Bitmap img, MemeTemplate t, string[] lines)
    {
        using var g = Graphics.FromImage(img);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var fam = new FontFamily(Font);
        for (int i = 0; i < Math.Min(lines.Length, t.Boxes.Length); i++)
        {
            string text = t.Dark ? lines[i] : lines[i].ToUpperInvariant();
            var b = t.Boxes[i];
            var box = new RectangleF(b.X * img.Width, b.Y * img.Height, b.Width * img.Width, b.Height * img.Height);
            var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = (b.Y > 0.6f ? StringAlignment.Far : b.Y < 0.1f ? StringAlignment.Near : StringAlignment.Center) };
            float size = box.Height * 0.5f;
            using var path = new GraphicsPath();
            for (; size > 8; size *= 0.9f)
            {
                path.Reset();
                path.AddString(text, fam, (int)FontStyle.Regular, size, box, fmt);
                var r = path.GetBounds();
                if (r.Width <= box.Width * 1.02f && r.Height <= box.Height * 1.02f) break;
            }
            if (t.Dark)
            {
                using var fill = new SolidBrush(Color.FromArgb(30, 30, 30));
                g.FillPath(fill, path);
            }
            else
            {
                using var pen = new Pen(Color.Black, Math.Max(2, size / 7)) { LineJoin = LineJoin.Round };
                g.DrawPath(pen, path);
                g.FillPath(Brushes.White, path);
            }
        }
    }

    // ---------------- real memes (optional) ----------------

    sealed record Post(string title, string url, bool nsfw, bool spoiler, string author, string postLink, int ups);
    sealed record Batch(List<Post> memes);

    /// <summary>A real post from r/wholesomememes whose title fits the moment (keywords from the context), or null.</summary>
    public static async Task<MemeEntry?> FindReal(MemeContext c)
    {
        var words = c.Tags.SelectMany(KeyWords).Concat(new[] { c.Pet.Length > 0 ? "cat" : "" }).Where(w => w.Length > 0).ToHashSet();
        if (words.Count == 0) return null;
        try
        {
            var json = await Http.GetStringAsync("https://meme-api.com/gimme/wholesomememes/30");
            var batch = JsonSerializer.Deserialize<Batch>(json);
            var seen = List.Select(m => m.Source).ToHashSet();
            var post = batch?.memes.Where(p => !p.nsfw && !p.spoiler && p.ups >= 50 && Allowed(p.url) && !seen.Contains(p.postLink)
                                              && words.Any(w => p.title.Contains(w, StringComparison.OrdinalIgnoreCase)))
                                   .OrderByDescending(p => p.ups).FirstOrDefault();
            if (post == null) return null;
            var bytes = await Http.GetByteArrayAsync(post.url);
            if (bytes.Length > 8_000_000) return null;
            using var ms = new MemoryStream(bytes);
            using var img = Image.FromStream(ms);
            using var small = Fit(img, 800);
            Directory.CreateDirectory(Dir);
            string file = $"meme {DateTime.Now:yyyy-MM-dd HH.mm.ss} found.png";
            small.Save(Path.Combine(Dir, file), ImageFormat.Png);
            var e = new MemeEntry { File = file, When = DateTime.Now, Template = "r/wholesomememes", Text = post.title, Why = "found", Source = post.postLink };
            lock (Gate) List.Add(e);
            Save();
            return e;
        }
        catch (Exception e) { World.Log($"meme search failed: {e.Message}"); return null; }
    }

    static IEnumerable<string> KeyWords(string tag) => tag switch
    {
        "monday" => new[] { "monday" }, "friday" => new[] { "friday", "weekend" }, "weekend" => new[] { "weekend", "saturday", "sunday" },
        "latenight" => new[] { "sleep", "bed", "night" }, "morning" => new[] { "coffee", "morning" }, "rain" => new[] { "rain" }, "snow" => new[] { "snow", "winter" },
        "typing" => new[] { "work", "typing", "computer" }, "buildfail" => new[] { "code", "bug", "programmer" }, "buildpass" => new[] { "code", "programmer" },
        "pet" => new[] { "cat", "dog", "pet", "bunny", "hamster" }, "love" => new[] { "love", "friend", "partner" }, "festival" => new[] { "party", "dance" },
        "focus" => new[] { "proud", "productive" }, "back" => new[] { "miss", "back" }, _ => Array.Empty<string>(),
    };

    /// <summary>Optional: the AI writes the caption lines for a template, given what's happening. Null if it can't.</summary>
    public static string[]? CleanAiLines(string? reply, int boxes)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var lines = reply.Split('\n').Select(l => l.Trim().Trim('"', '-', '*', ' ')).Where(l => l.Length > 0 && l.Length <= 70).Take(boxes).ToArray();
        return lines.Length == boxes ? lines : null;
    }
}
