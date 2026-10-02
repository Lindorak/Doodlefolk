using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Reacting to your work (an idea from OpenPets; off by default): your tools can tell the town how things are
/// going, and they react: a wince and a "this is fine" when the build breaks, cheers and confetti when the tests pass,
/// "ship it!" when you push. Tools talk to a tiny listener on this computer only (127.0.0.1, never the network):
///   curl -X POST http://127.0.0.1:47321/event/build-failed
/// or run  Doodlefolk.exe --notify build-failed  from a git hook, an npm script or a VS Code task (docs/DEVHOOKS.md).
/// Only a fixed list of events is understood; anything else is ignored. A few a minute at most.</summary>
sealed partial class App
{
    public const int DevHookPort = 47321;
    public static readonly string[] DevEvents = { "build-passed", "build-failed", "tests-passed", "tests-failed", "commit", "push", "deploy", "error", "done", "started" };
    TcpListener? _hookListener;
    readonly Queue<(string kind, string text)> _hookQueue = new();
    double _hookAt;
    public string DevHookStatus = "";

    void StartDevHooks()
    {
        StopDevHooks();
        if (!_settings.DevHooks || _trailer) { DevHookStatus = ""; return; }
        try
        {
            _hookListener = new TcpListener(IPAddress.Loopback, DevHookPort);
            _hookListener.Start();
            DevHookStatus = $"Listening on 127.0.0.1:{DevHookPort} (this computer only).";
            _ = Task.Run(() => AcceptHooks(_hookListener));
        }
        catch (Exception e) { DevHookStatus = $"Couldn't listen on port {DevHookPort}: {e.Message}"; _hookListener = null; }
        World.Log("dev hooks: " + DevHookStatus);
    }

    void StopDevHooks()
    {
        try { _hookListener?.Stop(); } catch { }
        _hookListener = null;
    }

    async Task AcceptHooks(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(); }
            catch { return; }   // stopped
            _ = Task.Run(() => ServeHook(client));
        }
    }

    /// <summary>A minimal HTTP exchange: POST (or GET) /event/&lt;kind&gt;[?text=…]. Nothing else is read or answered.</summary>
    void ServeHook(TcpClient client)
    {
        using (client)
        {
            try
            {
                client.ReceiveTimeout = 2000;
                using var stream = client.GetStream();
                var buf = new byte[2048];
                int n = stream.Read(buf, 0, buf.Length);
                string head = Encoding.ASCII.GetString(buf, 0, n);
                string line = head.Split("\r\n")[0];
                var parts = line.Split(' ');
                string status = "404 Not Found", body = "unknown event\n";
                if (parts.Length >= 2 && parts[1].StartsWith("/event/"))
                {
                    var path = parts[1][7..];
                    string kind = path.Split('?')[0].ToLowerInvariant(), text = "";
                    int q = path.IndexOf("text=", StringComparison.Ordinal);
                    if (q >= 0) text = WebUtility.UrlDecode(path[(q + 5)..].Split('&')[0]);
                    if (DevEvents.Contains(kind))
                    {
                        text = new string(text.Where(c => !char.IsControl(c)).Take(60).ToArray());
                        lock (_hookQueue) if (_hookQueue.Count < 5) _hookQueue.Enqueue((kind, text));
                        status = "200 OK"; body = "ok\n";
                    }
                }
                var resp = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}");
                stream.Write(resp, 0, resp.Length);
            }
            catch { }
        }
    }

    /// <summary>Send an event to the running Doodlefolk (the --notify command line).</summary>
    public static int Notify(string kind, string text)
    {
        try
        {
            using var c = new TcpClient();
            c.Connect(IPAddress.Loopback, DevHookPort);
            using var s = c.GetStream();
            var req = Encoding.ASCII.GetBytes($"POST /event/{Uri.EscapeDataString(kind)}{(text.Length > 0 ? "?text=" + Uri.EscapeDataString(text) : "")} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 0\r\n\r\n");
            s.Write(req, 0, req.Length);
            var buf = new byte[256];
            int n = s.Read(buf, 0, buf.Length);
            return Encoding.ASCII.GetString(buf, 0, n).Contains(" 200 ") ? 0 : 1;
        }
        catch { return 2; }   // not running, or reactions are off
    }

    void DevHookFrame(double now)
    {
        (string kind, string text) ev;
        lock (_hookQueue) { if (_hookQueue.Count == 0 || now < _hookAt) return; ev = _hookQueue.Dequeue(); }
        _hookAt = now + 4;
        DevReact(ev.kind, ev.text);
    }

    /// <summary>How the town takes the news.</summary>
    public string DevReact(string kind, string text)
    {
        var folk = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep && f.Visitor == VisitorKind.None).ToList();
        if (folk.Count == 0) return "nobody's awake";
        var rng = _w.Rng;
        double now = _clock.Elapsed.TotalSeconds;
        string Pick(params string[] s) => s[rng.Next(s.Length)];
        bool good = kind is "build-passed" or "tests-passed" or "deploy" or "done";
        bool bad = kind is "build-failed" or "tests-failed" or "error";
        if (kind.StartsWith("build") || kind.StartsWith("tests")) { _lastBuild = good ? "pass" : "fail"; _lastBuildAt = now; }
        foreach (var f in folk)
        {
            if (rng.NextDouble() > (bad || good ? 0.8 : 0.4)) continue;
            if (bad)
            {
                f.Emote(World.Gestures ? "😬" : Pick("oof", "uh oh…", "noooo", "this is fine", "it'll be ok!", "red again?"), 1.8f);
                f.Brain.Saddened(0.05f);
                if (f.Traits.Sociability > 0.65f && rng.NextDouble() < 0.5) f.Brain.Force("wave", Array.Empty<string>(), _w);
            }
            else if (good)
            {
                f.Emote(World.Gestures ? "🎉" : kind == "deploy" ? Pick("it's live!", "SHIPPED!", "to the moon!") : Pick("green!", "yay!", "it works!!", "first try!", "woo!"), 1.8f);
                f.Brain.Cheered(0.15f);
                f.Brain.Force("cheer", Array.Empty<string>(), _w);
            }
            else if (kind is "commit") f.Emote(Pick("nice commit!", "saved!", "good one", "📝"), 1.4f);
            else if (kind is "push") f.Emote(Pick("ship it!", "off it goes!", "pushed!", "🚀"), 1.4f);
            else if (kind is "started") f.Emote(Pick("go go go!", "here we go", "building…"), 1.2f);
        }
        if (good && kind != "done") Confetti();
        if (text.Length > 0 && folk.OrderBy(_ => rng.Next()).First() is { } speaker) speaker.Emote(text, 2.5f);
        _w.Sticker(bad ? "redbuild" : good ? "greenbuild" : "devhooks");
        World.Log($"dev event: {kind} {text}");
        return $"The town heard: {kind}";
    }
}
