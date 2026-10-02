using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using System.Text.Json;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace Doodlefolk;

/// <summary>One photo in the album.</summary>
sealed class AlbumEntry
{
    public string File { get; set; } = "";
    public DateTime When { get; set; }
    public string Kind { get; set; } = "";
    public string Caption { get; set; } = "";
    public List<string> Who { get; set; } = new();
    public bool Starred { get; set; }
}

/// <summary>The photo album (an idea from Neko Atsume's photo book): when something big happens (a first date, a
/// baby, a race won, a treehouse finished, a new pet) the town's own camera takes a picture of the moment and pastes it
/// into the album with a handwritten caption. Only the town is in the picture, drawn on sketchbook paper (your windows'
/// edges become pencil lines), never your screen, so nothing private ends up in it. The Studio's Album page shows them.</summary>
sealed partial class App
{
    public static string AlbumDir => Path.Combine(AppPaths.DataDir, "album");
    static string AlbumIndex => Path.Combine(AlbumDir, "album.json");
    List<AlbumEntry>? _album;
    (double at, string kind, string caption, Figure[] who, Pet[] pets, int weight)? _albumPending;
    double _albumLast = -1e9;
    const int AlbumW = 1200, AlbumH = 800;

    public List<AlbumEntry> Album
    {
        get
        {
            if (_album != null) return _album;
            try { _album = File.Exists(AlbumIndex) ? JsonSerializer.Deserialize<List<AlbumEntry>>(File.ReadAllText(AlbumIndex)) ?? new() : new(); }
            catch (Exception e) { World.Log($"album index unreadable: {e.Message}"); _album = new(); }
            _album.RemoveAll(a => !File.Exists(Path.Combine(AlbumDir, a.File)));
            return _album;
        }
    }

    void SaveAlbum()
    {
        try { Directory.CreateDirectory(AlbumDir); File.WriteAllText(AlbumIndex, JsonSerializer.Serialize(Album, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { World.Log($"album save failed: {e.Message}"); }
    }

    /// <summary>Something album-worthy just happened (a big news item): take the picture a moment later, once the
    /// moment is on screen (a hug, a cheer). One at a time; quieter moments wait a few minutes after the last.</summary>
    void OnMilestone(string kind, string text, int weight, Figure[] who)
    {
        if (!_settings.AutoAlbum || _selfTest || _trailer || kind is "app" or "club" or "memorial") return;
        double now = _clock.Elapsed.TotalSeconds;
        if (weight < 5 && now - _albumLast < 240) return;
        if (_albumPending is { } p && p.weight >= weight) return;
        // Who's in it: anyone named, plus anyone (figure or animal) the caption mentions.
        var figs = who.Where(f => _w.Figures.Contains(f)).ToList();
        foreach (var f in _w.Figures) if (!figs.Contains(f) && System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(f.Name)}\b")) figs.Add(f);
        var pets = _w.Pets.Where(pt => System.Text.RegularExpressions.Regex.IsMatch(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(pt.Name)}\b")).ToList();
        _albumPending = (now + 1.4, kind, text, figs.ToArray(), pets.ToArray(), weight);
    }

    /// <summary>Called each frame after drawing: take a pending photo when its moment comes.</summary>
    void AlbumFrame(double now)
    {
        if (_albumPending is not { } p || now < p.at) return;
        _albumPending = null;
        _albumLast = now;
        try { TakeAlbumPhoto(p.kind, p.caption, p.who, p.pets); }
        catch (Exception e) { World.Log($"album photo failed: {e.Message}"); }
    }

    public string TakeAlbumPhoto(string kind, string caption, IReadOnlyList<Figure> who, IReadOnlyList<Pet> pets)
    {
        float S = _w.Scale;
        var v = _w.Env.Virtual;
        // Frame the subjects (or, with nobody named, everyone): where they're scattered about, the biggest group of
        // them, so the picture is of people rather than of a whole screen with specks on it.
        var subjects = who.Where(f => _w.Figures.Contains(f)).Select(f => (at: f.Base, top: f.Jt[J.Head] - new Vector2(0, f.HeadR + 30 * f.S)))
                          .Concat(pets.Where(pt => _w.Pets.Contains(pt)).Select(pt => (at: pt.Pos, top: pt.Pos - new Vector2(0, 40 * pt.S)))).ToList();
        if (subjects.Count == 0) subjects = _w.Figures.Select(f => (at: f.Base, top: f.Jt[J.Head] - new Vector2(0, f.HeadR + 30 * f.S))).ToList();
        if (subjects.Count == 0) return "";
        float near = 360 * S;
        bool Close((Vector2 at, Vector2 top) a, (Vector2 at, Vector2 top) b) => MathF.Abs(a.at.X - b.at.X) < near && MathF.Abs(a.at.Y - b.at.Y) < near * 0.6f;
        var hub = subjects.OrderByDescending(s => subjects.Count(o => Close(s, o))).First();
        var group = subjects.Where(s => Close(hub, s)).ToList();
        var pts = group.SelectMany(s => new[] { s.at, s.top }).ToList();
        float x0 = pts.Min(q => q.X), x1 = pts.Max(q => q.X), y0 = pts.Min(q => q.Y), y1 = pts.Max(q => q.Y);
        float w = MathF.Max(MathF.Max((x1 - x0) + 260 * S, (y1 - y0 + 200 * S) * AlbumW / AlbumH), 520 * S);
        float h = w * AlbumH / AlbumW;
        var area = new RectangleF((x0 + x1) / 2 - w / 2, y1 + 40 * S - h * 0.86f, w, h);
        if (y0 - 60 * S < area.Top) area.Y = (y0 + y1) / 2 - h / 2;
        area.X = Math.Clamp(area.X, v.Left, Math.Max(v.Left, v.Right - w));
        area.Y = Math.Clamp(area.Y, v.Top, Math.Max(v.Top, v.Bottom - h));

        var px = new byte[AlbumW * AlbumH * 4];
        var paper = Ui.Chalk ? new Color4(0.16f, 0.2f, 0.18f, 1) : new Color4(0.985f, 0.975f, 0.94f, 1);
        _r.Capture(area, a =>
        {
            // Your windows' top edges as pencil lines (nothing inside them).
            foreach (var pl in _w.Env.Platforms)
            {
                if (pl.Item != null || pl.Seen != null || pl.Y < a.Top || pl.Y > a.Bottom + 4 || pl.X2 < a.Left || pl.X1 > a.Right) continue;
                _r.Line(new Vector2(pl.X1, pl.Y + 1), new Vector2(pl.X2, pl.Y + 1), Ui.Pencil.A(0.8f), 1.6f * S);
            }
            DrawScene(a);
        }, paper, AlbumW / area.Width, px, AlbumW, AlbumH);
        _r.EndCapture();

        string stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss");
        string file = $"{stamp} {kind}.png";
        var entry = new AlbumEntry { File = file, When = DateTime.Now, Kind = kind, Caption = caption, Who = who.Select(f => f.Name).Concat(pets.Select(pt => pt.Name)).ToList() };
        Album.Add(entry);
        // Over 300 photos: the oldest unstarred go.
        while (Album.Count > 300 && Album.FirstOrDefault(a => !a.Starred) is { } old)
        {
            Album.Remove(old);
            try { File.Delete(Path.Combine(AlbumDir, old.File)); } catch { }
        }
        bool chalk = Ui.Chalk;
        _ = Task.Run(() =>
        {
            try { SaveAlbumCard(px, caption, Path.Combine(AlbumDir, file), chalk); }
            catch (Exception e) { World.Log($"album photo save failed: {e.Message}"); }
        });
        SaveAlbum();
        _w.Sticker("album");
        World.Log($"album: {caption}");
        return file;
    }

    /// <summary>The album for the Studio (sent on request, not with every state push).</summary>
    public object AlbumMessage() => new
    {
        t = "album",
        auto = _settings.AutoAlbum,
        items = Album.AsEnumerable().Reverse().Select(a => new { file = a.File, when = a.When.ToString("d MMM yyyy, HH:mm"), month = a.When.ToString("MMMM yyyy"), a.Kind, caption = a.Caption, who = a.Who, starred = a.Starred }),
    };

    /// <summary>The picture, mounted on a page with photo corners and the caption written underneath.</summary>
    static void SaveAlbumCard(byte[] px, string caption, string file, bool chalk)
    {
        using var shot = new Bitmap(AlbumW, AlbumH, PixelFormat.Format32bppPArgb);
        var bits = shot.LockBits(new Rectangle(0, 0, AlbumW, AlbumH), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < AlbumH; y++) System.Runtime.InteropServices.Marshal.Copy(px, y * AlbumW * 4, bits.Scan0 + y * bits.Stride, AlbumW * 4); }
        finally { shot.UnlockBits(bits); }
        int pad = 36, bottom = 120;
        using var card = new Bitmap(AlbumW + pad * 2, AlbumH + pad + bottom, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(card))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(chalk ? Color.FromArgb(44, 52, 48) : Color.FromArgb(255, 253, 247));
            g.DrawImage(shot, pad, pad);
            using var edge = new Pen(Color.FromArgb(50, 0, 0, 0), 1.5f);
            g.DrawRectangle(edge, pad, pad, AlbumW - 1, AlbumH - 1);
            // Photo corners.
            using var corner = new SolidBrush(chalk ? Color.FromArgb(200, 230, 230, 220) : Color.FromArgb(200, 60, 55, 50));
            foreach (var (cx, cy, sx, sy) in new[] { (pad, pad, 1, 1), (pad + AlbumW, pad, -1, 1), (pad, pad + AlbumH, 1, -1), (pad + AlbumW, pad + AlbumH, -1, -1) })
                g.FillPolygon(corner, new[] { new PointF(cx - sx * 8, cy - sy * 8), new PointF(cx + sx * 38, cy - sy * 8), new PointF(cx - sx * 8, cy + sy * 38) });
            string font = FontFamily.Families.Any(ff => ff.Name == "Segoe Print") ? "Segoe Print" : "Segoe UI";
            using var hand = new Font(font, 30, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small = new Font(font, 19, FontStyle.Regular, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(chalk ? Color.FromArgb(236, 235, 228) : Color.FromArgb(38, 36, 31));
            using var pencil = new SolidBrush(chalk ? Color.FromArgb(150, 154, 144) : Color.FromArgb(138, 132, 120));
            var fmt = new StringFormat { Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(caption, hand, ink, new RectangleF(pad + 4, AlbumH + pad + 18, AlbumW - 8, 46), fmt);
            g.DrawString(DateTime.Now.ToString("d MMMM yyyy · HH:mm"), small, pencil, pad + 6, AlbumH + pad + 70);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        card.Save(file, ImageFormat.Png);
    }
}
