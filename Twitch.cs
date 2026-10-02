using System.Net.WebSockets;
using System.Text;

namespace Doodlefolk;

/// <summary>A chat message from Twitch.</summary>
sealed record ChatLine(string User, string Name, string Colour, string Text, bool Mod, bool Broadcaster);

/// <summary>Reads a Twitch channel's chat, read-only and anonymously (Twitch's "justinfan" guest login: no account, no
/// keys, nothing sent but the channel's name). Reconnects by itself. Only chat lines come through.</summary>
sealed class TwitchChat : IDisposable
{
    readonly string _channel;
    readonly Action<ChatLine> _onLine;
    readonly CancellationTokenSource _stop = new();
    public string Status { get; private set; } = "Connecting…";

    public TwitchChat(string channel, Action<ChatLine> onLine)
    {
        _channel = new string(channel.Trim().TrimStart('#').ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || c == '_').Take(25).ToArray());
        _onLine = onLine;
        if (_channel.Length > 0) _ = Task.Run(Run); else Status = "No channel set.";
    }

    async Task Run()
    {
        int backoff = 2;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri("wss://irc-ws.chat.twitch.tv:443"), _stop.Token);
                async Task Send(string s) => await ws.SendAsync(Encoding.UTF8.GetBytes(s + "\r\n"), WebSocketMessageType.Text, true, _stop.Token);
                await Send("CAP REQ :twitch.tv/tags");
                await Send("PASS SCHMOOPIIE");
                await Send($"NICK justinfan{Random.Shared.Next(10000, 99999)}");
                await Send($"JOIN #{_channel}");
                Status = $"Reading #{_channel}'s chat.";
                backoff = 2;
                var buf = new byte[16384];
                var pending = new StringBuilder();
                while (ws.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    var r = await ws.ReceiveAsync(buf, _stop.Token);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    pending.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    if (!r.EndOfMessage) continue;
                    foreach (var raw in pending.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (raw.StartsWith("PING")) { await Send("PONG" + raw[4..]); continue; }
                        if (Parse(raw) is { } line) _onLine(line);
                    }
                    pending.Clear();
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Status = $"Twitch: {e.Message} (trying again)"; }
            try { await Task.Delay(TimeSpan.FromSeconds(backoff), _stop.Token); } catch { return; }
            backoff = Math.Min(backoff * 2, 60);
        }
    }

    /// <summary>One IRC line with tags → a chat line (PRIVMSG only).</summary>
    public static ChatLine? Parse(string raw)
    {
        string tags = "";
        string rest = raw;
        if (rest.StartsWith('@')) { int sp = rest.IndexOf(' '); if (sp < 0) return null; tags = rest[1..sp]; rest = rest[(sp + 1)..]; }
        // :user!user@user.tmi.twitch.tv PRIVMSG #channel :message
        if (!rest.StartsWith(':')) return null;
        int bang = rest.IndexOf('!'), cmd = rest.IndexOf(" PRIVMSG ", StringComparison.Ordinal);
        if (bang < 0 || cmd < 0) return null;
        int colon = rest.IndexOf(" :", cmd + 9, StringComparison.Ordinal);
        if (colon < 0) return null;
        string user = rest[1..bang], text = rest[(colon + 2)..];
        var t = tags.Split(';').Select(kv => kv.Split('=', 2)).Where(p => p.Length == 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.First()[1]);
        string name = t.TryGetValue("display-name", out var dn) && dn.Length > 0 ? dn.Replace("\\s", " ") : user;
        string colour = t.TryGetValue("color", out var c) && c.Length == 7 ? c : "";
        string badges = t.GetValueOrDefault("badges", "");
        // "/me" actions arrive wrapped in \x01ACTION … \x01.
        if (text.StartsWith("\u0001ACTION ") && text.EndsWith('\u0001')) text = text[8..^1];
        return new ChatLine(user, name, colour, text, badges.Contains("moderator/") || t.GetValueOrDefault("mod") == "1", badges.Contains("broadcaster/"));
    }

    public void Dispose() { _stop.Cancel(); }
}
