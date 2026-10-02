using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace Doodlefolk;

/// <summary>Steam community items, drawn by the town itself (Doodlefolk.exe --cardart folder): ten trading cards
/// (1920×1080 JPG under 350 KB, and 206×184 PNG), three profile backgrounds (faded to black at the sides and bottom, as
/// Steam asks), six badges (80×80), five emoticons (18×18 and 54×54) and the 206×44 logo, plus a text file with every
/// title and description to paste into Steamworks. Runs like the trailer: a staged desktop, hidden, its own data.</summary>
sealed partial class App
{
    bool _cardArt;
    string _cardDir = "";
    readonly List<(double at, string file, string title, string desc, Func<RectangleF> area, bool background)> _shots = new();
    int _shotAt;
    readonly List<string> _cardNotes = new();

    void CardArtStage()
    {
        _cardDir = _trailerPath;
        Directory.CreateDirectory(_cardDir);
        _w.Scale = 1.5f;
        _w.Env.MinHeadroom = 75 * _w.Scale;
        _w.Env.StageScreen(new Rectangle(0, 0, TW, TH), 48);
        _tWindows.Add(("Notes", new RectangleF(110, 560, 600, 300), M.Hex(0xFDD835)));
        _tWindows.Add(("Photos", new RectangleF(1010, 410, 760, 430), M.Hex(0x1E88E5)));
        _tWindows.Add(("Music", new RectangleF(560, 250, 460, 230), M.Hex(0xE53935)));
        SyncStagedWindows();
        _fakeCursor = new Vector2(-5000, -5000);
        BuildCardScript();
    }

    /// <summary>A 16:9 frame round these points (feet), the ground low in the picture, at least minW wide.</summary>
    static RectangleF Frame(IEnumerable<Vector2> points, float minW = 760, float lift = 0)
    {
        var pts = points.ToList();
        if (pts.Count == 0) return new RectangleF(0, TH - 620, 1102, 620);
        float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        float w = Math.Clamp(Math.Max((x1 - x0) * 1.35f + 260, (y1 - y0 + 260) * 16 / 9f), minW, TW), h = w * 9 / 16;
        float cx = (x0 + x1) / 2, bottom = y1 + 70 + lift;
        return new RectangleF(Math.Clamp(cx - w / 2, 0, TW - w), Math.Clamp(bottom - h, 0, TH - h), w, h);
    }

    RectangleF Around(params string[] names)
    {
        var pts = names.Select(n => TF(n)).Where(f => f != null).Select(f => f!.Base).ToList();
        if (pts.Count == 0) return new RectangleF(0, 0, TW, TH);
        float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y1 = pts.Max(p => p.Y);
        float w = Math.Clamp((x1 - x0) * 1.6f + 420, 700, TW), h = w * 9 / 16;
        float cx = (x0 + x1) / 2, top = y1 + 30 - h * 0.85f;
        return new RectangleF(Math.Clamp(cx - w / 2, 0, TW - w), Math.Clamp(top, 0, TH - h), w, h);
    }

    void BuildCardScript()
    {
        void At(double t, Action a) => _tScript.Add((t, $"{t:0.0}", a));
        void Card(double t, string file, string title, string desc, Func<RectangleF> area) => _shots.Add((t, file, title, desc, area, false));
        void Background(double t, string file, string title, string desc, Func<RectangleF> area) => _shots.Add((t, file, title, desc, area, true));
        void Cmd(string line) => RunCommand(line);

        At(0.0, () =>
        {
            foreach (var f in _w.Figures.ToList()) _w.RemoveFigure(f);
            TPut("couch", 300); TPut("radio", 470); TPut("campfire", 1650); TPut("lamp", 1820); TPut("fairylights", 1460);
            TPut("pond", 900); TPut("foodbowl", 1180); TPut("waterbowl", 1240);
        });
        At(0.2, () => TCast("Mo", "#E53935", 420, 1031, "cap"));
        At(0.3, () => TCast("Bea", "#1E88E5", 560, 1031, "bow"));
        At(0.4, () => TCast("Rex", "#43A047", 1240, 409, "crown"));
        At(0.5, () => TCast("Lou", "#FB8C00", 760, 249, "beanie"));
        At(0.6, () => TCast("Juni", "#8E24AA", 1400, 1031, "flowercrown"));
        At(0.8, () =>
        {
            foreach (var (k, x) in new[] { (PetKind.Cat, 1100f), (PetKind.Dog, 1250f), (PetKind.Rabbit, 640f) })
            {
                var p = SpawnPet(k, quiet: true);
                p.Pos = new Vector2(x, 1000);
            }
        });
        At(3, () => { float x = 330; foreach (var n in new[] { "Mo", "Bea", "Rex", "Lou", "Juni" }) if (TF(n) is { } f && _w.Env.Below(x, 1020) is { } fl) { f.PlaceAt(fl, x); f.SpawnT = 0.999f; x += 95; } float px = 380; foreach (var p in _w.Pets) { p.Pos = new Vector2(px + 380, 1000); px += 70; } });
        Card(5, "card01-town", "Doodlefolk", "Little people who live on your desktop.", () => Frame(_w.Figures.Select(f => f.Base).Concat(_w.Pets.Select(p => p.Pos)), 760));
        Background(5.1, "background01-desktop", "A whole town", "They live on your windows.", () => new RectangleF(0, 0, TW, TH));

        At(6, () => { foreach (var (n, x) in new[] { ("Mo", 420f), ("Bea", 520f) }) if (TF(n) is { } f && _w.Env.Below(x, 1020) is { } fl) { f.PlaceAt(fl, x); f.SpawnT = 0.999f; } Cmd("love Mo Bea 0.95"); Cmd("love Bea Mo 0.95"); Cmd("confess Mo"); });
        Card(10, "card02-sweethearts", "Sweethearts", "Crushes, confessions, dates, and sometimes a little one.", () => Frame(new[] { TF("Mo")!.Base, TF("Bea")!.Base }, 520));

        At(11, () => { foreach (var p in _w.Pets) { p.Hunger = 0.95f; p.Attention = 0.9f; } });
        Card(16, "card03-pets", "Pets with Real Needs", "Feed them, walk them, brush them, teach them tricks.", () => Frame(_w.Pets.Where(p => p.Kind is PetKind.Cat or PetKind.Dog).Select(p => p.Pos), 520));

        At(17, () => { if (TF("Juni") is { } j && _w.Env.Below(700, 1020) is { } fl) { j.PlaceAt(fl, 700); j.SpawnT = 0.999f; } });
        At(19, () => { if (TF("Juni") is { } j) { j.Brain.DebugTown(_w, "job", "None"); j.Brain.Stamina = 1; World.Log("card art fishing: " + j.Brain.DebugTown(_w, "fish", "")); } });
        Card(25, "card04-fishing", "Gone Fishing", "What's biting changes with the month, the hour and the rain.", () => Frame(new[] { TF("Juni")?.Base ?? new Vector2(900, 1031), _w.Items.FirstOrDefault(i => i.Def.Key == "pond")?.Pos ?? new Vector2(900, 1031) }, 600));
        Background(25.1, "background02-pond", "The pond", "Ducks, ripples, and something golden under the surface.", () => new RectangleF(300, TH - 900, 1600, 900));

        At(26, () => World.Log("card art: " + Visit(VisitorKind.Bard)));
        Card(36, "card05-bard", "The Travelling Bard", "Visitors drop by now and then, and leave something behind.", () =>
        {
            var b = _w.Figures.FirstOrDefault(f => f.Visitor == VisitorKind.Bard);
            if (b == null) { World.Log("card art: no bard!"); return Frame(_w.Figures.Select(f => f.Base)); }
            return Frame(_w.Figures.OrderBy(f => Vector2.Distance(f.Base, b.Base)).Take(3).Select(f => f.Base), 600);
        });

        At(37, () => { SetGravity("moon"); foreach (var f in _w.Figures.Where(f => f.Mode == Mode.Control)) f.GoRagdoll(new Vector2(_w.Rng.Range(-200, 200), -_w.Rng.Range(500, 800)) * _w.Scale); });
        Card(38.2, "card06-moon", "Moon Gravity", "The toybox: moon gravity, giant balls, earthquakes, slow motion.", () => Frame(_w.Figures.Select(f => f.Jt[J.Pelvis]).Append(new Vector2(TW / 2f, 1031)), 1200));
        At(40, () => SetGravity("normal"));

        At(44, () => { foreach (var f in _w.Figures.Where(f => f.Visitor != VisitorKind.None).ToList()) _w.RemoveFigure(f); StartHappening("race"); });
        Card(57, "card07-race", "Race Day", "Festivals, talent shows and races, whenever the town feels like it.", () => { var who = (_w.Happening?.Who ?? new List<Figure>()).Where(f => _w.Figures.Contains(f)).ToList(); World.Log($"card art race: {who.Count} racers, phase {_w.Happening?.Phase}"); return Frame(who.Select(f => f.Base), 800); });

        At(62, () => { if (_w.Happening is { } rh) EndHappening(rh, false); _w.NightOverride = 0.9f; _tNight = 0.9f; StartHappening("festival"); });
        At(66, () => _fireworks = true);
        Card(70, "card08-festival", "Festival Night", "Lights, lanterns, dancing, and fireworks after dark.", () => { var fire = _w.Items.FirstOrDefault(i => i.Def.Key == "campfire")?.Pos ?? new Vector2(TW / 2f, 1031); var crowd = (_w.Happening?.Crowd ?? new List<Figure>()).Where(f => _w.Figures.Contains(f)).Select(f => f.Base).ToList(); if (crowd.Count == 0) crowd.Add(fire); var mid = new Vector2(crowd.Average(c => c.X), crowd.Max(c => c.Y)); return Frame(crowd.Where(c => MathF.Abs(c.X - mid.X) < 420), 760); });
        Background(70.2, "background03-night", "Festival night", "The town, lit up.", () => new RectangleF(0, 0, TW, TH));

        At(72, () => { _fireworks = false; if (_w.Happening is { } fh) EndHappening(fh, false); if (TF("Lou") is { } l) { l.Brain.Force("sleep", Array.Empty<string>(), _w); l.Brain.DebugTown(_w, "showdream", ""); } });
        Card(78, "card09-dreams", "Sweet Dreams", "Late at night they get sleepy, and they dream.", () => Frame(new[] { TF("Lou")!.Jt[J.Head] + new Vector2(0, 40), TF("Lou")!.Base }, 520));

        At(79, () => { _w.NightOverride = null; _tNight = 0; foreach (var it in _w.Items.Where(i => i.Temporary).ToList()) _w.RemoveItem(it); });
        At(80, () => World.Log("card art: " + Visit(VisitorKind.MailCarrier)));
        Card(96, "card10-crate", "Special Delivery", "The mail carrier brings gift crates with rare hats inside.", () =>
        {
            var crate = _w.Items.FirstOrDefault(i => i.Def.Key == "giftcrate");
            var mail = _w.Figures.FirstOrDefault(f => f.Visitor == VisitorKind.MailCarrier);
            var pts = new List<Vector2>(); if (crate != null) pts.Add(crate.Pos); if (mail != null) pts.Add(mail.Base);
            return Frame(pts.Count > 0 ? pts : _w.Figures.Select(f => f.Base), 560);
        });
        _tScript.Sort((a, b) => a.at.CompareTo(b.at));
    }

    bool _fireworks;
    double _cardFireAt;

    /// <summary>Each frame of the card-art run: step the town; when a shot's due, take it.</summary>
    void CardArtFrame()
    {
        double t = _tFrame / (double)TFps;
        _tFrame++;
        if (_fireworks && t > _cardFireAt)
        {
            _cardFireAt = t + 0.35;
            var at = new Vector2(_w.Rng.Range(200, TW - 200), _w.Rng.Range(150, 500));
            var col = new[] { new Color4(1, 0.3f, 0.3f, 1), new Color4(0.3f, 0.8f, 1, 1), new Color4(1, 0.85f, 0.2f, 1), new Color4(0.7f, 0.4f, 1, 1) }[_w.Rng.Next(4)];
            for (int i = 0; i < 10; i++) { float ang = i * MathF.Tau / 10; _w.Fx.Spark(at + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * 70 * _w.Scale, _w.Scale * 0.9f, _w.Rng, 1.6f, col); }
        }
        while (_shotAt < _shots.Count && t >= _shots[_shotAt].at)
        {
            var s = _shots[_shotAt++];
            try { Shoot(s.file, s.title, s.desc, s.area(), s.background); }
            catch (Exception e) { World.Log($"card art {s.file}: {e.Message}"); }
        }
        if (_shotAt >= _shots.Count)
        {
            try { DrawBadgesAndEmoticons(); } catch (Exception e) { World.Log($"badges: {e.Message}"); }
            File.WriteAllLines(Path.Combine(_cardDir, "steam-community-items.txt"), _cardNotes);
            World.Log($"card art: done → {_cardDir}");
            SelfTestExitCode = 0;
            ExitThread();
        }
    }

    void Shoot(string file, string title, string desc, RectangleF area, bool background)
    {
        const int W = 1920, H = 1080;
        var px = new byte[W * H * 4];
        float zoom = W / area.Width;
        _r.Capture(area, a => { DrawDesktop(a); DrawScene(a); }, new Color4(0.86f, 0.9f, 0.96f, 1), zoom, px, W, H);
        using var bmp = ToBitmap(px, W, H);
        if (background) FadeToBlack(bmp);
        SaveJpegUnder(bmp, Path.Combine(_cardDir, file + ".jpg"), 350_000);
        if (!background)
        {
            // The small card: the middle of the picture at 206×184.
            float k = Math.Max(206f / W, 184f / H);
            int cw = (int)(206 / k), ch = (int)(184 / k);
            using var small = new Bitmap(206, 184, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, new Rectangle(0, 0, 206, 184), new Rectangle((W - cw) / 2, H - ch - (H - ch) / 4, cw, ch), GraphicsUnit.Pixel);
            }
            small.Save(Path.Combine(_cardDir, file + "-small.png"), ImageFormat.Png);
        }
        _cardNotes.Add($"{(background ? "Profile background" : "Trading card")}: {file}\n  Title: {title}\n  Description: {desc}\n");
        World.Log($"card art: {file}");
    }

    static Bitmap ToBitmap(byte[] px, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(px, y * w * 4, d.Scan0 + y * d.Stride, w * 4); }
        finally { bmp.UnlockBits(d); }
        return bmp;
    }

    /// <summary>Steam's rule for profile backgrounds: the sides and bottom fade to black, so there are no edges.</summary>
    static void FadeToBlack(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, row.Length);
                float vy = Math.Clamp((h - 1 - y) / (h * 0.28f), 0, 1);
                for (int x = 0; x < w; x++)
                {
                    float vx = Math.Clamp(Math.Min(x, w - 1 - x) / (w * 0.18f), 0, 1);
                    float k = vx * vy; k = k * k * (3 - 2 * k);
                    for (int c = 0; c < 3; c++) row[x * 4 + c] = (byte)(row[x * 4 + c] * k);
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, d.Scan0 + y * d.Stride, row.Length);
            }
        }
        finally { bmp.UnlockBits(d); }
    }

    static void SaveJpegUnder(Bitmap bmp, string path, long maxBytes)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        for (long q = 92; q >= 40; q -= 6)
        {
            using var ps = new EncoderParameters(1);
            ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, q);
            using var ms = new MemoryStream();
            bmp.Save(ms, codec, ps);
            if (ms.Length <= maxBytes || q <= 46) { File.WriteAllBytes(path, ms.ToArray()); return; }
        }
    }

    // ---------------- badges, emoticons, logo ----------------

    static readonly Color Ink = Color.FromArgb(38, 36, 31);

    /// <summary>A stick figure in the house style: round head, body, arms and legs, with a colour.</summary>
    static void Stick(Graphics g, float x, float y, float s, Color c, bool cheer = false, string hat = "")
    {
        using var pen = new Pen(c, Math.Max(1.2f, s * 0.12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float hr = s * 0.22f;
        var head = new PointF(x, y - s * 0.95f);
        var neck = new PointF(x, y - s * 0.72f);
        var hip = new PointF(x, y - s * 0.32f);
        g.DrawLine(pen, neck, hip);
        g.DrawLine(pen, hip, new PointF(x - s * 0.18f, y));
        g.DrawLine(pen, hip, new PointF(x + s * 0.18f, y));
        if (cheer) { g.DrawLine(pen, neck, new PointF(x - s * 0.3f, y - s * 1.05f)); g.DrawLine(pen, neck, new PointF(x + s * 0.3f, y - s * 1.05f)); }
        else { g.DrawLine(pen, neck, new PointF(x - s * 0.25f, y - s * 0.4f)); g.DrawLine(pen, neck, new PointF(x + s * 0.25f, y - s * 0.4f)); }
        using var fill = new SolidBrush(c);
        g.FillEllipse(fill, head.X - hr, head.Y - hr, hr * 2, hr * 2);
        if (hat == "crown")
        {
            using var gold = new SolidBrush(Color.FromArgb(242, 193, 78));
            float top = head.Y - hr * 0.8f;
            g.FillPolygon(gold, new[] { new PointF(head.X - hr, top), new PointF(head.X - hr, top - hr * 0.9f), new PointF(head.X - hr * 0.4f, top - hr * 0.4f), new PointF(head.X, top - hr * 1.1f), new PointF(head.X + hr * 0.4f, top - hr * 0.4f), new PointF(head.X + hr, top - hr * 0.9f), new PointF(head.X + hr, top) });
        }
    }

    static Bitmap Canvas(int w, int h, out Graphics g)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        return b;
    }

    static void Badge(Graphics g, Color ring, Color fill)
    {
        using var f = new SolidBrush(fill);
        using var p = new Pen(ring, 4);
        g.FillEllipse(f, 4, 4, 72, 72);
        g.DrawEllipse(p, 4, 4, 72, 72);
    }

    void DrawBadgesAndEmoticons()
    {
        var paper = Color.FromArgb(251, 248, 239);
        var levels = new (string title, Action<Graphics> draw)[]
        {
            ("Doodler", g => { Badge(g, Color.FromArgb(138, 132, 120), paper); using var pen = new Pen(Color.FromArgb(242, 193, 78), 7) { EndCap = LineCap.Triangle }; g.DrawLine(pen, 24, 56, 54, 26); using var tip = new Pen(Ink, 3); g.DrawLine(tip, 22, 58, 26, 54); }),
            ("Sketcher", g => { Badge(g, Color.FromArgb(229, 57, 53), paper); Stick(g, 40, 64, 46, Color.FromArgb(229, 57, 53)); }),
            ("Town Founder", g => { Badge(g, Color.FromArgb(30, 136, 229), paper); Stick(g, 30, 64, 40, Color.FromArgb(229, 57, 53)); Stick(g, 50, 64, 40, Color.FromArgb(30, 136, 229)); }),
            ("Neighbourhood", g => { Badge(g, Color.FromArgb(67, 160, 71), paper); Stick(g, 24, 62, 30, Color.FromArgb(229, 57, 53), cheer: true); Stick(g, 40, 62, 34, Color.FromArgb(30, 136, 229)); Stick(g, 56, 62, 30, Color.FromArgb(142, 36, 170), cheer: true); }),
            ("Mayor of the Desktop", g => { Badge(g, Color.FromArgb(142, 36, 170), paper); Stick(g, 40, 64, 46, Color.FromArgb(67, 160, 71), cheer: true, hat: "crown"); }),
            ("Golden Doodle", g => { Badge(g, Color.FromArgb(242, 193, 78), Color.FromArgb(255, 236, 179)); Stick(g, 40, 64, 46, Color.FromArgb(200, 150, 30), cheer: true, hat: "crown"); using var sp = new SolidBrush(Color.White); g.FillEllipse(sp, 14, 18, 6, 6); g.FillEllipse(sp, 60, 22, 5, 5); }),
        };
        for (int i = 0; i < levels.Length; i++)
        {
            using var b = Canvas(80, 80, out var g);
            levels[i].draw(g);
            g.Dispose();
            string file = i < 5 ? $"badge-level{i + 1}.png" : "badge-foil.png";
            b.Save(Path.Combine(_cardDir, file), ImageFormat.Png);
            _cardNotes.Add($"Badge {(i < 5 ? $"level {i + 1}" : "foil")}: {file}\n  Title: {levels[i].title}\n");
        }
        var emotes = new (string name, Action<Graphics, float> draw)[]
        {
            ("doodleheart", (g, s) => { using var b = new SolidBrush(Color.FromArgb(229, 57, 53)); var path = new GraphicsPath(); path.AddBezier(s * 0.5f, s * 0.88f, s * 0.05f, s * 0.55f, s * 0.05f, s * 0.12f, s * 0.5f, s * 0.32f); path.AddBezier(s * 0.5f, s * 0.32f, s * 0.95f, s * 0.12f, s * 0.95f, s * 0.55f, s * 0.5f, s * 0.88f); g.FillPath(b, path); using var p = new Pen(Ink, Math.Max(1, s / 18)); g.DrawPath(p, path); }),
            ("doodlefolk", (g, s) => Stick(g, s * 0.5f, s * 0.97f, s * 0.92f, Color.FromArgb(229, 57, 53), cheer: true)),
            ("doodlepencil", (g, s) => { using var p = new Pen(Color.FromArgb(242, 193, 78), s * 0.2f) { EndCap = LineCap.Triangle }; g.DrawLine(p, s * 0.2f, s * 0.8f, s * 0.82f, s * 0.18f); using var t = new SolidBrush(Ink); g.FillEllipse(t, s * 0.1f, s * 0.78f, s * 0.14f, s * 0.14f); }),
            ("goldenkoi", (g, s) => { using var b = new SolidBrush(Color.FromArgb(255, 213, 79)); g.FillEllipse(b, s * 0.1f, s * 0.3f, s * 0.62f, s * 0.4f); g.FillPolygon(b, new[] { new PointF(s * 0.66f, s * 0.5f), new PointF(s * 0.95f, s * 0.25f), new PointF(s * 0.95f, s * 0.75f) }); using var p = new Pen(Ink, Math.Max(1, s / 20)); g.DrawEllipse(p, s * 0.1f, s * 0.3f, s * 0.62f, s * 0.4f); using var e = new SolidBrush(Ink); g.FillEllipse(e, s * 0.22f, s * 0.42f, s * 0.08f, s * 0.08f); }),
            ("doodleghost", (g, s) => { using var b = new SolidBrush(Color.FromArgb(227, 242, 253)); var path = new GraphicsPath(); path.AddArc(s * 0.15f, s * 0.08f, s * 0.7f, s * 0.7f, 180, 180); path.AddLine(s * 0.85f, s * 0.43f, s * 0.85f, s * 0.92f); path.AddLine(s * 0.85f, s * 0.92f, s * 0.68f, s * 0.8f); path.AddLine(s * 0.68f, s * 0.8f, s * 0.5f, s * 0.92f); path.AddLine(s * 0.5f, s * 0.92f, s * 0.32f, s * 0.8f); path.AddLine(s * 0.32f, s * 0.8f, s * 0.15f, s * 0.92f); path.CloseFigure(); g.FillPath(b, path); using var p = new Pen(Ink, Math.Max(1, s / 18)); g.DrawPath(p, path); using var e = new SolidBrush(Ink); g.FillEllipse(e, s * 0.36f, s * 0.36f, s * 0.1f, s * 0.12f); g.FillEllipse(e, s * 0.56f, s * 0.36f, s * 0.1f, s * 0.12f); }),
        };
        foreach (var (name, draw) in emotes)
        {
            foreach (int size in new[] { 54, 18 })
            {
                using var b = Canvas(size, size, out var g);
                draw(g, size);
                g.Dispose();
                b.Save(Path.Combine(_cardDir, $"emoticon-{name}-{size}.png"), ImageFormat.Png);
            }
            _cardNotes.Add($"Emoticon: emoticon-{name}-18.png / -54.png\n  Name: {name}\n");
        }
        // The logo: dark handwriting with a white edge, so it reads on dark and light.
        using (var b = Canvas(206, 44, out var g))
        {
            var font = FontFamily.Families.Any(f => f.Name == "Segoe Print") ? "Segoe Print" : "Segoe UI";
            using var path = new GraphicsPath();
            path.AddString("Doodlefolk", new FontFamily(font), (int)FontStyle.Bold, 30, new RectangleF(0, -2, 206, 48), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            using var edge = new Pen(Color.White, 4) { LineJoin = LineJoin.Round };
            g.DrawPath(edge, path);
            using var ink = new SolidBrush(Ink);
            g.FillPath(ink, path);
            g.Dispose();
            b.Save(Path.Combine(_cardDir, "logo-206x44.png"), ImageFormat.Png);
        }
        _cardNotes.Add("Logo: logo-206x44.png\nCard border colour: #E53935\n");
    }
}
