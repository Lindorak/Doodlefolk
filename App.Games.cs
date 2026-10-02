using System.Drawing;
using Color = System.Drawing.Color;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Games with you (hide-and-seek, tag, catch) and photo mode.</summary>
sealed partial class App
{
    // ---------------- games ----------------

    /// <summary>Start a game. <paramref name="with"/> (from a figure's menu) always plays; everyone else who likes you
    /// well enough joins in (catch is just the two of you).</summary>
    string StartGame(GameKind kind, Figure? with)
    {
        StopGame(false);
        var g = new UserGame { Kind = kind };
        if (with != null && !with.Brain.WillPlay) return $"{with.Name} doesn't want to play right now";
        if (kind == GameKind.Catch)
        {
            var who = with ?? _w.Figures.Where(f => f.Brain.WillPlay).OrderByDescending(f => f.Brain.UserFondness).FirstOrDefault();
            if (who == null) return "Nobody wants to play right now";
            g.Players.Add(who);
            g.Thrower = who;
            g.Ball = _w.Props.Where(p => p.Holder == null && !p.Pinned && p.SizeMul <= 1.8f).OrderBy(p => Vector2.Distance(p.Pos, who.Base)).FirstOrDefault();
            if (g.Ball == null || Vector2.Distance(g.Ball.Pos, who.Base) > 1500 * _w.Scale)
            {
                g.Ball = new Prop(PropKind.Ball, _w.Scale);
                g.Ball.Pos = who.Base + new Vector2(who.Facing * 40, -who.Height * 1.4f);
                _w.Props.Add(g.Ball);
            }
        }
        else
        {
            foreach (var f in _w.Figures)
                if (f == with || f.Brain.WillPlay) g.Players.Add(f);
                else if (f.Mode == Mode.Control && !f.Dead && !f.Hunter && f.Brain.UserFondness <= -0.3f) f.Emote("hmph", 1.2f);
            if (g.Players.Count == 0) return "Nobody wants to play right now";
            if (kind == GameKind.HideSeek) g.Count = 9;
        }
        _w.Game = g;
        foreach (var f in g.Players) f.Brain.JoinGame(g, _w);
        World.Log($"game {kind} with {string.Join(", ", g.Players.Select(p => p.Name))}");
        return kind switch
        {
            GameKind.HideSeek => "Close your eyes and count! Then click the hiders when you spot them.",
            GameKind.Tag => "You're it! Touch one of them with the cursor, then run before they tag you back.",
            _ => $"Playing catch with {g.Players[0].Name}: touch the ball with the cursor to catch it and send it back.",
        };
    }

    void StopGame(bool announce = true)
    {
        if (_w.Game is not { } g) return;
        _w.Game = null;
        g.Over = true;
        foreach (var f in g.Players.ToArray()) if (_w.Figures.Contains(f)) f.Brain.GameOver(g, false);
        if (g.Kind == GameKind.HideSeek) ForceFullRedraw();
        if (announce) World.Log($"game {g.Kind} over");
    }

    double _gameLast;

    void GameFrame(double now)
    {
        float dt = (float)Math.Clamp(now - _gameLast, 0, 0.1);
        _gameLast = now;
        if (_w.Game is not { } g) return;
        g.T += dt;
        g.Grace = MathF.Max(0, g.Grace - dt);
        g.Players.RemoveAll(f => !_w.Figures.Contains(f) || f.Dead);
        if (g.Over || g.Players.Count == 0) { StopGame(); return; }
        switch (g.Kind)
        {
            case GameKind.HideSeek:
                if (g.Count > 0)
                {
                    g.Count -= dt;
                    ForceFullRedraw();     // the counting curtain covers everything
                    if (g.Count <= 0) foreach (var f in g.Players) if (f.Brain.WillPlay) { f.Emote("ready!", 1); break; }
                }
                if (g.Found.Count >= g.Players.Count) { _w.Sticker("hideseek"); StopGame(); break; }
                if (g.T > g.Count + 160) StopGame();       // you gave up: they win
                break;
            case GameKind.Tag:
                // You tag someone by touching them with the cursor.
                if (g.It == null && g.Grace <= 0)
                    foreach (var f in g.Players)
                        if (f.Mode == Mode.Control && f.DistanceTo(_w.Cursor, out _) <= 4 * f.S)
                        {
                            g.It = f;
                            _w.Sticker("tag");
                            g.Grace = 1.5f;
                            f.Brain.Tagged(g);
                            break;
                        }
                if (g.It != null && !g.Players.Contains(g.It)) g.It = null;
                if (g.T > 150) StopGame();
                break;
            case GameKind.Catch:
                CatchFrame(g, dt);
                break;
        }
    }

    void CatchFrame(UserGame g, float dt)
    {
        if (g.Ball is not { } b || !_w.Props.Contains(b)) { StopGame(); return; }
        var who = g.Thrower!;
        g.Quiet += dt;
        if (b.Holder != null || b.Pinned) g.Quiet = b.Holder == who ? 0 : g.Quiet;
        // Flying your way after they threw it.
        if (b.Holder == null && !b.Pinned && !b.OnGround && b.LastTouch == who && g.Flight == 0) g.Flight = 1;
        if (g.Flight == 1 && !b.Pinned && b.Holder == null)
        {
            if (Vector2.Distance(b.Pos, _w.Cursor) < b.Radius + 36 * _w.Scale)
            {
                // Caught it: straight back to them in a friendly lob.
                g.Streak++;
                g.Best = Math.Max(g.Best, g.Streak);
                if (g.Streak >= 10) _w.Sticker("catch10");
                g.Quiet = 0;
                Vector2 to = who.Base - new Vector2(0, who.Height * 0.75f);
                float t = Math.Clamp(Vector2.Distance(to, b.Pos) / (900 * _w.Scale), 0.55f, 1.3f);
                b.Vel = new Vector2((to.X - b.Pos.X) / t, (to.Y - b.Pos.Y) / t - 0.5f * b.Grav * t);
                b.LastTouch = null;
                b.ThrownByUser = true;
                b.OnGround = false;
                g.Flight = 2;
                World.Play(Sfx.BounceBall, b.Pos, 0.5f, 1.2f);
                who.Emote(g.Streak >= 3 ? $"x{g.Streak}!" : _w.Rng.NextDouble() < 0.5 ? "nice catch!" : "yay!", 1);
            }
            else if (b.OnGround)
            {
                g.Flight = 0;
                if (g.Streak > 0) who.Emote(g.Streak >= 3 ? $"aww, {g.Streak} in a row!" : "oops!", 1.2f);
                g.Streak = 0;
            }
        }
        // You threw it back by hand.
        if (b.ThrownByUser && !b.OnGround && g.Flight != 2) { g.Flight = 2; g.Quiet = 0; }
        if (g.Flight == 2 && b.OnGround && b.Holder == null)
        {
            g.Flight = 0;
            if (g.Streak >= 3) who.Emote($"so close! {g.Streak}!", 1.2f);
            g.Streak = 0;
        }
        if (g.T > 200 || g.Quiet > 45) StopGame();
    }

    /// <summary>Clicked during hide-and-seek: found someone?</summary>
    bool GameClick(Figure fig)
    {
        if (_w.Game is not { Kind: GameKind.HideSeek } g || g.Count > 0 || !g.Hidden(fig)) return false;
        g.Found.Add(fig);
        fig.Brain.FoundByUser(_w, g);
        return true;
    }

    void DrawGameCurtain()
    {
        if (_w.Game is not { Kind: GameKind.HideSeek, Count: > 0 } g) return;
        var b = _r.Bounds;
        float a = M.Clamp01(MathF.Min(g.Count, 9 - g.Count) / 0.4f);
        var board = new Color4(0.12f, 0.15f, 0.14f, 0.96f * a);
        _r.FillPolygon(stackalloc Vector2[] { new(b.Left, b.Top), new(b.Right, b.Top), new(b.Right, b.Bottom), new(b.Left, b.Bottom) }, board);
        var primary = Screen.PrimaryScreen?.Bounds ?? b;
        Vector2 c = new(primary.Left + primary.Width / 2f, primary.Top + primary.Height / 2f);
        float s = _w.Scale;
        var chalk = new Color4(0.93f, 0.92f, 0.89f, a);
        _r.Text($"{MathF.Ceiling(g.Count):0}", c + new Vector2(0, -40 * s), 120 * s, chalk, true);
        _r.Text("counting… no peeking!", c + new Vector2(0, 50 * s), 30 * s, chalk.A(0.85f), true);
        _r.Text($"{g.Players.Count} hiding", c + new Vector2(0, 92 * s), 18 * s, new Color4(0.6f, 0.62f, 0.58f, a), true);
    }

    // ---------------- photo mode ----------------

    double _photoAt = -1, _flashAt = -1;

    Vector2 _photoSpot;

    /// <summary>Photo: everyone near the biggest group squeezes in (a few seconds), then the shot is framed on them.</summary>
    void TakePhoto()
    {
        if (_photoAt > 0) return;
        var figs = _w.Figures.Where(f => f.Mode != Mode.Spawning && !f.Dead).ToList();
        Figure? best = null;
        int bestN = -1;
        foreach (var f in figs)
        {
            int n = figs.Count(o => Vector2.Distance(o.Base, f.Base) < 650 * f.S);
            if (n > bestN || (n == bestN && best != null && Vector2.Distance(f.Base, _w.Cursor) < Vector2.Distance(best.Base, _w.Cursor))) { bestN = n; best = f; }
        }
        _photoSpot = best != null ? best.Base : _w.Cursor;
        _photoAt = _clock.Elapsed.TotalSeconds + 4.5;
        bool first = true;
        int i = 0;
        foreach (var f in figs.OrderBy(f => Vector2.Distance(f.Base, _photoSpot)))
        {
            if (f.Mode != Mode.Control || Vector2.Distance(f.Base, _photoSpot) > 1500 * f.S) continue;
            // Side by side: alternate left and right of the middle.
            float off = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 46 * f.S;
            f.Brain.PosePhoto(first, _w, _photoSpot + new Vector2(off, 0), World.Now + 4.5);
            first = false;
            i++;
        }
    }

    void PhotoFrame(double now)
    {
        if (_flashAt > 0)
        {
            ForceFullRedraw();
            if (now > _flashAt + 0.45) { _flashAt = -1; ForceFullRedraw(); }
        }
        if (_photoAt < 0 || now < _photoAt) return;
        _photoAt = -1;
        string? path = null;
        try { path = SnapPhoto(); }
        catch (Exception e) { World.Log($"photo failed: {e.Message}"); }
        _flashAt = now;
        World.Play(Sfx.Swish, _w.Cursor, 0.4f, 1.6f);
        World.Play(Sfx.Pip, _w.Cursor, 0.3f, 0.6f);
        foreach (var f in _w.Figures) if (f.Mode == Mode.Control && !f.Dead) f.Brain.PhotoTaken();
        _w.Sticker("photo");
        if (path != null) PostAll(new { t = "toast", text = $"Saved to Pictures\\StickFight\\{Path.GetFileName(path)}" });
    }

    void DrawFlash()
    {
        if (_flashAt < 0) return;
        float a = M.Clamp01(1 - (float)(_clock.Elapsed.TotalSeconds - _flashAt) / 0.45f) * 0.75f;
        var b = _r.Bounds;
        _r.FillPolygon(stackalloc Vector2[] { new(b.Left, b.Top), new(b.Right, b.Top), new(b.Right, b.Bottom), new(b.Left, b.Bottom) }, new Color4(1, 1, 1, a));
    }

    /// <summary>A snapshot of the figures (and whatever's behind them), framed like an instant photo, saved to
    /// Pictures\StickFight. Stays on this PC.</summary>
    string? SnapPhoto()
    {
        var v = _w.Env.Virtual;
        RectangleF area = RectangleF.Empty;
        var names = new List<string>();
        foreach (var f in _w.Figures)
        {
            if (f.Mode == Mode.Spawning || f.Dead || Vector2.Distance(f.Base, _photoSpot) > 700 * f.S) continue;
            var r = (RectangleF)FigureRect(f);
            area = area.IsEmpty ? r : RectangleF.Union(area, r);
            names.Add(f.Name);
        }
        if (area.IsEmpty) area = new RectangleF(_photoSpot.X - 300 * _w.Scale, _photoSpot.Y - 300 * _w.Scale, 600 * _w.Scale, 340 * _w.Scale);
        area.Inflate(90 * _w.Scale, 70 * _w.Scale);
        // A landscape frame around the group: 3:2, never bigger than needed.
        float w = MathF.Max(area.Width, 560 * _w.Scale), h = MathF.Max(area.Height, w / 1.5f);
        w = MathF.Max(w, h * 1.5f);
        float cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        var shot = Rectangle.Round(RectangleF.FromLTRB(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2));
        shot.Intersect(new Rectangle(v.Left, v.Top, v.Width, v.Height));
        var snap = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(snap)) g.CopyFromScreen(shot.Left, shot.Top, 0, 0, shot.Size, CopyPixelOperation.SourceCopy);
        string who = names.Count switch { 0 => "the desktop", 1 => names[0], _ => string.Join(", ", names.Take(names.Count - 1)) + " & " + names[^1] };
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "StickFight");
        string file = Path.Combine(dir, $"StickFight {DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");
        // Framing and saving (PNG encoding is slow for big shots) happen off the main thread.
        _ = Task.Run(() =>
        {
            try { Frame(snap, who, dir, file); }
            catch (Exception e) { World.Log($"photo save failed: {e.Message}"); }
            finally { snap.Dispose(); }
        });
        return file;
    }

    static void Frame(Bitmap snap, string who, string dir, string file)
    {
        var shot = new Rectangle(0, 0, snap.Width, snap.Height);

        int pad = Math.Max(18, shot.Width / 36), bottom = pad * 4;
        using var card = new Bitmap(shot.Width + pad * 2, shot.Height + pad + bottom, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(card))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.FromArgb(255, 253, 247));
            g.DrawImage(snap, pad, pad);
            using var edge = new Pen(Color.FromArgb(40, 0, 0, 0), 1);
            g.DrawRectangle(edge, pad, pad, shot.Width - 1, shot.Height - 1);
            using var hand = new Font(FontFamily.Families.Any(ff => ff.Name == "Segoe Print") ? "Segoe Print" : "Segoe UI", pad * 0.95f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var small = new Font(hand.FontFamily, pad * 0.62f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.FromArgb(38, 36, 31));
            using var pencil = new SolidBrush(Color.FromArgb(138, 132, 120));
            g.DrawString(who, hand, ink, pad * 1.2f, shot.Height + pad + pad * 0.6f);
            g.DrawString(DateTime.Now.ToString("d MMMM yyyy · HH:mm"), small, pencil, pad * 1.25f, shot.Height + pad + pad * 2.1f);
        }
        Directory.CreateDirectory(dir);
        card.Save(file, ImageFormat.Png);
        World.Log($"photo saved: {file}");
    }
}
