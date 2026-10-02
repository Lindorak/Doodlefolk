using System.Collections.Concurrent;
using System.Numerics;
using System.Text.RegularExpressions;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Stream mode (an idea from Stream Avatars and Desktop Gremlins): your Twitch chat can join the town.
/// "!join" brings a little figure in the chatter's colour, and what they type turns up in its bubbles; "!wave",
/// "!dance", "!jump", "!hat crown" make it do things; "!weather rain" and "!race" (if you allow them, with a
/// cooldown) change the town. Chat is read with Twitch's anonymous guest login: no account, no keys. Channel-point
/// rewards work through any bot that posts a command in chat. The stream view is a clean window of just the town on
/// a key colour, for OBS.</summary>
sealed partial class App
{
    TwitchChat? _twitch;
    readonly ConcurrentQueue<ChatLine> _chat = new();
    readonly Dictionary<string, (Figure fig, double lastSeen)> _viewers = new();
    double _streamCdWeather, _streamCdEvent;
    StreamView? _streamView;
    double _streamFrameAt;
    byte[]? _streamPx;
    public string StreamStatus => _twitch?.Status ?? (_settings.StreamOn ? "Set your channel name." : "");

    void StartStream()
    {
        _twitch?.Dispose(); _twitch = null;
        if (_settings.StreamOn && _settings.StreamChannel.Length > 0 && !_selfTest && !_trailer)
            _twitch = new TwitchChat(_settings.StreamChannel, l => { if (_chat.Count < 200) _chat.Enqueue(l); });
        if (!_settings.StreamOn) foreach (var v in _viewers.Values.ToList()) ViewerLeaves(v.fig, quiet: true);
    }

    void StreamFrame(double now)
    {
        for (int i = 0; i < 6 && _chat.TryDequeue(out var line); i++)
        {
            try { OnChat(line, now); } catch (Exception e) { World.Log($"stream: {e.Message}"); }
        }
        // Viewers who've gone quiet for a while wander off.
        foreach (var (user, v) in _viewers.ToList())
            if (!_w.Figures.Contains(v.fig)) _viewers.Remove(user);
            else if (now - v.lastSeen > _settings.StreamIdleMinutes * 60) ViewerLeaves(v.fig);
        // The stream view (for OBS): just the town on a key colour.
        if (_streamView is { IsDisposed: false, Visible: true } sv && now > _streamFrameAt)
        {
            _streamFrameAt = now + 1.0 / 30;
            var v = _w.Env.Virtual;
            var size = sv.ClientSize;
            if (size.Width > 16 && size.Height > 16)
            {
                int w = Math.Min(size.Width, 1920), h = Math.Min(size.Height, 1080);
                var area = new RectangleF(v.Left, v.Top, v.Width, v.Width * h / (float)w);
                if (area.Height < v.Height) area = new RectangleF(v.Left, v.Bottom - area.Height, area.Width, area.Height);   // keep the floor in view
                if (_streamPx == null || _streamPx.Length != w * h * 4) _streamPx = new byte[w * h * 4];
                _r.Capture(area, a => DrawScene(a), StreamKey(), w / area.Width, _streamPx, w, h);
                sv.Show(_streamPx, w, h);
            }
        }
    }

    Color4 StreamKey() => _settings.StreamKey switch { "magenta" => new Color4(1, 0, 1, 1), "blue" => new Color4(0, 0, 1, 1), _ => new Color4(0, 1, 0, 1) };

    static string Clean(string text)
    {
        text = Regex.Replace(text, @"https?://\S+|www\.\S+", "[link]");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 60 ? text[..58] + "…" : text;
    }

    void OnChat(ChatLine l, double now)
    {
        string text = l.Text.Trim();
        _viewers.TryGetValue(l.User, out var me);
        bool joined = me.fig != null && _w.Figures.Contains(me.fig);
        if (joined) _viewers[l.User] = (me.fig, now);
        if (!text.StartsWith('!'))
        {
            if (joined && _settings.StreamBubbles && text.Length > 0) me.fig.Emote(Clean(text), Math.Clamp(1.5f + text.Length * 0.06f, 2, 6));
            return;
        }
        var parts = text[1..].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string cmd = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
        bool Allowed(string group) => _settings.StreamCommands.Contains(group);
        switch (cmd)
        {
            case "join" when Allowed("join") && !joined:
                ViewerJoins(l, now);
                break;
            case "leave" when joined:
                ViewerLeaves(me.fig);
                break;
            case "wave" or "cheer" when joined && Allowed("moves"): me.fig.Brain.Force(cmd, Array.Empty<string>(), _w); break;
            case "dance" when joined && Allowed("moves"): me.fig.StartFidget(Fidget.Groove); me.fig.Emote("♪", 1.5f); break;
            case "jump" when joined && Allowed("moves") && me.fig.Grounded: me.fig.RequestJump(new Vector2(0, -MathF.Sqrt(2 * me.fig.Gravity * 120 * _w.Scale)), 0.05f); break;
            case "hat" when joined && Allowed("moves"):
                if (arg is "none" or "off") me.fig.Look.Hat = "";
                else if (Look.Hats.FirstOrDefault(h => (h.Key == arg || h.Name.Equals(arg, StringComparison.OrdinalIgnoreCase)) && (!Look.RareHats.Contains(h.Key) || _settings.UnlockedHats.Contains(h.Key))) is { } hat)
                    me.fig.Look.Hat = hat.Key;
                break;
            case "weather" when Allowed("weather") && now > _streamCdWeather:
                var kind = arg switch { "rain" => WeatherKind.Rain, "snow" => WeatherKind.Snow, "storm" => WeatherKind.Storm, "clear" or "sun" or "sunny" => WeatherKind.Clear, _ => (WeatherKind?)null };
                if (kind is { } k && _settings.WeatherMode != "real") { _w.Weather.Start(k, now, _w.Rng, _w); _streamCdWeather = now + _settings.StreamCooldown; _w.News("stream", $"{l.Name} from chat made it {arg}", 1); }
                break;
            case "race" or "festival" or "talent" when Allowed("events") && now > _streamCdEvent && _w.Happening == null:
                if (StartHappening(cmd).StartsWith("started")) { _streamCdEvent = now + _settings.StreamCooldown * 3; _w.News("stream", $"{l.Name} from chat called for {_w.Happening?.Title}", 2); }
                break;
        }
    }

    void ViewerJoins(ChatLine l, double now)
    {
        if (_viewers.Count >= _settings.StreamMaxViewers || _w.Figures.Count >= World.MaxFigures) return;
        var ground = HappeningGround(150 * _w.Scale) ?? _w.Env.Platforms.Where(p => p.Hwnd == IntPtr.Zero).FirstOrDefault();
        if (ground == null) return;
        var col = l.Colour.Length == 7 ? Settings.ParseHex(l.Colour) : Palette.All[(int)((uint)l.User.GetHashCode() % (uint)Palette.All.Length)].Color;
        string name = l.Name.Length > 18 ? l.Name[..18] : l.Name;
        var f = new Figure(col, UniqueName(name), _w.Scale * 0.9f, Personality.Random(_w.Rng), _w.Rng) { Visitor = VisitorKind.Viewer, SizeMul = 0.9f };
        f.PlaceAt(ground, _w.Rng.Range(ground.X1 + 30 * _w.Scale, ground.X2 - 30 * _w.Scale));
        _w.Figures.Add(f);
        _viewers[l.User] = (f, now);
        f.Emote(World.Gestures ? "👋" : new[] { "hi chat!", "hello!", "I'm in!", "o/" }[_w.Rng.Next(4)], 2);
        _w.Sticker("stream");
        World.Log($"stream: {name} joined");
    }

    void ViewerLeaves(Figure f, bool quiet = false)
    {
        if (!quiet) f.Emote(World.Gestures ? "👋" : "bye!", 1.2f);
        _w.Fx.Dust(f.Base, _w.Scale, 8, 1, _w.Rng);
        _w.RemoveFigure(f);
        foreach (var k in _viewers.Where(v => v.Value.fig == f).Select(v => v.Key).ToList()) _viewers.Remove(k);
    }

    void ToggleStreamView()
    {
        if (_streamView is { IsDisposed: false }) { _streamView.Close(); _streamView = null; return; }
        _streamView = new StreamView();
        _streamView.Show();
    }

    object StreamState() => new
    {
        on = _settings.StreamOn, channel = _settings.StreamChannel, status = StreamStatus, viewers = _viewers.Values.Select(v => v.fig.Name),
        commands = _settings.StreamCommands, bubbles = _settings.StreamBubbles, max = _settings.StreamMaxViewers, cooldown = _settings.StreamCooldown,
        key = _settings.StreamKey, view = _streamView is { IsDisposed: false },
    };
}

/// <summary>A plain window with just the town in it, on a key colour, for OBS to capture (add a chroma key filter).</summary>
sealed class StreamView : Form
{
    Bitmap? _bmp;

    public StreamView()
    {
        Text = "Doodlefolk stream view";
        ClientSize = new System.Drawing.Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = System.Drawing.Color.Lime;
        DoubleBuffered = true;
        ShowInTaskbar = true;
        Icon = App.AppIcon;
    }

    public void Show(byte[] bgra, int w, int h)
    {
        if (_bmp == null || _bmp.Width != w || _bmp.Height != h) { _bmp?.Dispose(); _bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); }
        var data = _bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4); }
        finally { _bmp.UnlockBits(data); }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_bmp == null) return;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.DrawImage(_bmp, 0, 0, ClientSize.Width, ClientSize.Height);
    }

    protected override void OnFormClosed(FormClosedEventArgs e) { _bmp?.Dispose(); base.OnFormClosed(e); }
}
