using System.Diagnostics;
using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Screen awareness on the app side: starts the background reader, turns text into ledges every frame, and
/// shows the link bubble a figure raises. A link only ever opens when you click its bubble.</summary>
sealed partial class App
{
    void InitScreen()
    {
        try { _w.Screen = new ScreenSense(); }
        catch (Exception e) { World.Log($"screen sense failed: {e.Message}"); }
        ApplyScreenSettings();
    }

    void ApplyScreenSettings()
    {
        _w.ScreenTerrain = _settings.ScreenTerrain;
        _w.ScreenReact = _settings.ScreenReact;
        _w.ScreenLinks = _settings.ScreenLinks;
        _w.ScreenMedia = _settings.ScreenMedia;
        if (_w.Screen != null)
        {
            _w.Screen.ReadText = _settings.ScreenTerrain || _settings.ScreenReact || _settings.ScreenLinks;
            _w.Screen.Listen = _settings.ScreenMedia;
        }
        if (!_settings.ScreenLinks) _w.Offer = null;
    }

    void ScreenFrame()
    {
        if (_w.Screen != null && _w.ScreenTerrain) _w.Env.AddScreenSurfaces(_w.Screen.Snaps);
        if (_w.Offer is { } o && (World.Now > o.Until || o.By.Dead || o.By.Mode != Mode.Control || !_w.Figures.Contains(o.By)))
        {
            if (o.By.Mode == Mode.Control && !o.By.Dead) o.By.Emote(o.By.Traits.Sociability > 0.6f ? "aw, ok" : "fine", 1.2f);
            _w.Offer = null;
        }
    }

    const float OfferText = 9.5f, OfferHost = 7.5f;

    RectangleF? OfferRect()
    {
        if (_w.Offer is not { } o) return null;
        var f = o.By;
        float s = MathF.Max(1, f.S);
        Vector2 head = f.Jt[J.Head];
        float w = MathF.Max((o.Text.Length + 2) * OfferText * 0.62f, o.Host.Length * OfferHost * 0.58f) * s + 20 * s, h = 30 * s;
        var c = head + new Vector2(0, -(f.HeadR + 34 * f.S + h / 2));
        // Keep it on the screen it's on.
        var (L, R, T) = _w.Env.BoundsAt(head.X);
        c.X = M.ClampIn(c.X, L + w / 2 + 4, R - w / 2 - 4);
        c.Y = MathF.Max(c.Y, T + h / 2 + 4);
        o.Bubble = new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
        return RectangleF.FromLTRB(o.Bubble.Left - 4, o.Bubble.Top - 4, o.Bubble.Right + 4, MathF.Max(o.Bubble.Bottom + 10 * s, head.Y));
    }

    bool OfferHit(Vector2 c) => _w.Offer is { } o && o.Bubble.Width > 0 &&
                                c.X >= o.Bubble.Left && c.X <= o.Bubble.Right && c.Y >= o.Bubble.Top && c.Y <= o.Bubble.Bottom;

    void DrawOffer()
    {
        if (_w.Offer is not { } o || OfferRect() is not RectangleF all || !Dirty(all)) return;
        var f = o.By;
        float s = MathF.Max(1, f.S);
        var b = o.Bubble;
        Vector2 c = new(b.Left + b.Width / 2, b.Top + b.Height / 2);
        float fade = M.Clamp01((float)(o.Until - World.Now) / 0.4f);
        var ink = Ui.Ink.A(fade);
        Vector2 head = f.Jt[J.Head];
        // Pointing down at whoever's suggesting it; outlined in the accent colour while you hover it.
        Ui.Bubble(_r, c, b.Width, b.Height, head + new Vector2(0, -f.HeadR - 4 * f.S), s, fade, o.Hot ? Ui.Accent : null, o.Hot ? 2 : 1.3f);
        _r.Text("↗ " + o.Text, c + new Vector2(0, -5.5f * s), OfferText * s, o.Hot ? Ui.Accent.A(fade) : ink, true);
        _r.Text(o.Host + (o.Hot ? "  · click to open" : ""), c + new Vector2(0, 7.5f * s), OfferHost * s, Ui.Pencil.A(fade), true);
    }

    void TakeOffer()
    {
        if (_w.Offer is not { } o) return;
        _w.Offer = null;
        // Belt and braces: only ever hand plain https web addresses to the browser.
        if (!Uri.TryCreate(o.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) { World.Log($"open link failed: {e.Message}"); return; }
        o.By.Brain.LinkTaken();
    }

    /// <summary>Debug: what the eyes and ears currently see (counts and words only).</summary>
    string ScreenReport()
    {
        if (_w.Screen is not { } s) return "screen: off";
        var m = _w.Media;
        var parts = s.Snaps.Select(sn => $"[{sn.Hwnd}: {sn.Things.Count(t => t.Kind == SeenKind.Line)} lines, text {sn.TextLen} known {sn.Known}, words=" +
            string.Join(",", sn.Things.Where(t => t.Kind == SeenKind.Word).Select(t => t.Text)) +
            $", {sn.Things.Count(t => t.Kind == SeenKind.Link)} links, {sn.Things.Count(t => t.Kind == SeenKind.Image)} images]");
        int ledges = _w.Env.Platforms.Count(p => p.Seen != null);
        return $"screen: scan {s.ScanMs:0}ms ({s.Timing}), ledges {ledges}, {string.Join(" ", parts)}; media music={m.Music} video={m.Video} level={m.Level:0.00} ears: {s.EarsInfo}";
    }
}
