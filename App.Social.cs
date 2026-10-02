using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Clubs and the news log saved between runs, figure-lit campfires put out in the morning, and stickers.</summary>
sealed partial class App
{
    double _tidyAt;

    /// <summary>Campfires the figures lit for the night are gone by morning (once nobody's sitting by them).</summary>
    void TidyTemporary()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now < _tidyAt) return;
        _tidyAt = now + 10;
        if (_w.Night > 0) return;
        foreach (var it in _w.Items.Where(i => i.Temporary && i.Def.Key == "campfire").ToList())
            if (_w.Figures.All(f => f.Brain.UsingItem != it)) _w.RemoveItem(it);
    }

    void SaveSocial()
    {
        _settings.Clubs = _w.Clubs.Select(c => new SavedClub
        {
            Name = c.Name, Colour = Settings.Hex(c.Colour), Thing = c.Thing.ToString(), Founded = c.Founded,
            Members = c.People(_w).Select(f => f.Name).ToList(),
        }).ToList();
        _settings.News = _w.NewsLog.Where(n => (DateTime.Now - n.When).TotalDays < 21).ToList();
    }

    void RestoreClubs()
    {
        foreach (var sc in _settings.Clubs)
        {
            var c = new Club { Name = sc.Name, Colour = Settings.ParseHex(sc.Colour), Founded = sc.Founded, Thing = Enum.TryParse<Thing>(sc.Thing, out var t) ? t : Thing.Chatting };
            foreach (var n in sc.Members) if (_w.Figures.FirstOrDefault(f => f.Name == n) is { } f) c.Members.Add(f.Id);
            if (c.Members.Count >= 2) _w.Clubs.Add(c);
        }
    }

    void InitSocial()
    {
        _w.NewsLog.AddRange(_settings.News);
        _w.OnSticker = Sticker;
    }

    // ---------------- stickers ----------------

    string? _stickerToast;
    double _stickerUntil;

    void Sticker(string key)
    {
        if (Stickers.Find(key) is not { } def) return;
        SteamHub.Achieve(key);   // also catches up stickers earned before Steam
        if (_settings.Stickers.ContainsKey(key)) return;
        _settings.Stickers[key] = DateTime.Now;
        _stickerToast = def.Title;
        _stickerUntil = _clock.Elapsed.TotalSeconds + 5;
        World.Play(Sfx.Chime, _w.Cursor, 0.4f, 1.2f);
        World.Log($"sticker: {key}");
        PostAll(new { t = "toast", text = $"New sticker: {def.Art} {def.Title}" });
    }

    RectangleF? StickerRect()
    {
        if (_stickerToast == null) return null;
        if (_clock.Elapsed.TotalSeconds > _stickerUntil + 0.1) { _stickerToast = null; return null; }
        var p = Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1040);
        float s = _w.Scale, w = (120 + _stickerToast.Length * 7) * s, h = 44 * s;
        return new RectangleF(p.Right - w - 24 * s, p.Bottom - h - 18 * s, w, h);
    }

    /// <summary>A little paper card in the corner: you got a sticker.</summary>
    void DrawStickerToast()
    {
        if (_stickerToast == null || StickerRect() is not RectangleF b || !Dirty(b)) return;
        float s = _w.Scale;
        float a = M.Clamp01((float)(_stickerUntil - _clock.Elapsed.TotalSeconds) / 0.5f);
        Vector2 c = new(b.Left + b.Width / 2, b.Top + b.Height / 2);
        Ui.Card(_r, c, b.Width - 4 * s, b.Height - 4 * s, 10 * s, s, a, Ui.Accent, 1.6f);
        _r.Text("★ New sticker", c + new Vector2(0, -8 * s), 10 * s, Ui.Pencil.A(a), true);
        _r.Text(_stickerToast, c + new Vector2(0, 8 * s), 15 * s, Ui.Ink.A(a), true);
    }
}
