using System.Numerics;
using System.Text;
using System.Text.Json;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>A figure of ours off visiting a friend.</summary>
sealed class AwayRecord
{
    public string VisitId { get; set; } = "";
    public ulong FriendId { get; set; }
    public string FriendName { get; set; } = "";
    public DateTime Left { get; set; }
    public SavedFigure Snapshot { get; set; } = new();
}

/// <summary>Friend visits (an idea from Desktop Gremlins and Weyrdlets): send one of your figures to a Steam friend's
/// desktop (they need Doodlefolk running through Steam too). It walks off the edge of your screen and turns up on
/// theirs with a gift, mingles, joins whatever's on, and after a while comes home with a postcard and stories for its
/// diary. Only friends, only figures (a name, a look, a personality), and visitors can be turned off.</summary>
sealed partial class App
{
    const int TripMinutes = 25;
    readonly List<(Figure fig, AwayRecord rec)> _away = new();
    sealed class GuestInfo { public string VisitId = "", Owner = "", Town = ""; public ulong From; public double Arrived, Leave, NoteAt; public readonly HashSet<string> Met = new(); public string Event = ""; public bool Rain; }
    readonly Dictionary<Figure, GuestInfo> _guests = new();
    Func<ulong, byte[], bool> _p2pSend = SteamHub.SendTo;
    Func<List<(ulong from, byte[] data)>> _p2pReceive = SteamHub.Receive;
    static readonly JsonSerializerOptions VisitJson = new() { PropertyNameCaseInsensitive = true };
    static readonly string[] Gifts = { "cake", "cookie", "book", "tulip", "lantern", "pizza", "radio" };

    /// <summary>Snapshot of who a figure is (no feelings about others, no diary: those stay home).</summary>
    SavedFigure Travelling(Figure f) => new()
    {
        Name = f.Name, Color = Settings.Hex(f.Color), Size = f.SizeMul, Look = f.Look.Clone(), Traits = f.Traits.Clone(), Tastes = f.Tastes.Clone(),
        Gender = f.Gender, Style = f.StyleChoice.Clone(),
    };

    public string SendToVisit(Figure f, ulong friend, string friendName)
    {
        if (f.Visitor != VisitorKind.None || f.Mode != Mode.Control) return $"{f.Name} can't go just now.";
        if (_away.Count >= 3) return "Three are away already.";
        var rec = new AwayRecord { VisitId = Guid.NewGuid().ToString("N")[..12], FriendId = friend, FriendName = friendName, Left = DateTime.Now, Snapshot = Travelling(f) };
        var packet = new { t = "visit", id = rec.VisitId, figure = rec.Snapshot, gift = Gifts[_w.Rng.Next(Gifts.Length)], owner = SteamHub.Ready ? Steamworks.SteamFriends.GetPersonaName() : "a friend", town = _settings.CastName };
        if (!_p2pSend(friend, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet)))) return $"Couldn't reach {friendName} (are they running Doodlefolk through Steam?).";
        f.Emote(World.Gestures ? "🧳" : $"off to {friendName}'s!", 2);
        f.Brain.Diarise($"Off to visit {friendName}'s desktop!", "★");
        _w.Fx.Dust(f.Base, _w.Scale, 10, 1, _w.Rng);
        _w.RemoveFigure(f);
        _away.Add((f, rec));
        SaveAway();
        _w.News("trip", $"{f.Name} has gone to visit {friendName}'s town", 2, f);
        return $"{f.Name} is on the way to {friendName}'s desktop.";
    }

    void SaveAway() { _settings.Away = _away.Select(a => a.rec).ToList(); _settings.Save(); }

    /// <summary>After loading: those who were away are still away (or home by now).</summary>
    void RestoreAway()
    {
        foreach (var rec in _settings.Away)
        {
            var s = rec.Snapshot;
            var f = new Figure(Settings.ParseHex(s.Color), UniqueName(s.Name), _w.Scale * Math.Clamp(s.Size, 0.4f, 3f), s.Traits.Clone(), _w.Rng) { SizeMul = Math.Clamp(s.Size, 0.4f, 3f) };
            if (s.Look != null) f.Look = s.Look.Clone();
            if (s.Tastes != null) f.Tastes = s.Tastes.Clone();
            _away.Add((f, rec));
        }
    }

    void FriendVisitFrame(double now)
    {
        // Messages from friends.
        List<(ulong from, byte[] data)> inbox;
        try { inbox = _p2pReceive(); } catch { inbox = new(); }
        foreach (var (from, data) in inbox.Take(8))
        {
            try
            {
                using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data));
                var r = doc.RootElement;
                switch (r.GetProperty("t").GetString())
                {
                    case "visit": GuestArrives(from, r, now); break;
                    case "report": World.Log("friend visit: report received"); TravellerReturns(r.GetProperty("id").GetString() ?? "", r); break;
                }
            }
            catch (Exception e) { World.Log($"friend visit message: {e.Message}"); }
        }
        // Guests: notice things, then go home with a report.
        foreach (var (g, info) in _guests.ToList())
        {
            if (!_w.Figures.Contains(g)) { World.Log($"friend visit: guest {g.Name} vanished"); _guests.Remove(g); continue; }
            if (now > info.NoteAt)
            {
                info.NoteAt = now + 15;
                foreach (var o in _w.Figures.Where(o => o != g && o.Visitor == VisitorKind.None && Vector2.Distance(o.Base, g.Base) < 300 * _w.Scale)) info.Met.Add(o.Name);
                if (_w.Happening is { } h) info.Event = h.Title;
                if (_w.Weather.Raining) info.Rain = true;
            }
            if (now > info.Leave) GuestLeaves(g, info);
        }
        // Travellers whose friend never wrote back come home anyway.
        foreach (var (f, rec) in _away.ToList())
            if ((DateTime.Now - rec.Left).TotalMinutes > TripMinutes + 10) TravellerReturns(rec.VisitId, null);
    }

    void GuestArrives(ulong from, JsonElement r, double now)
    {
        if (!_settings.FriendVisits || _guests.Count >= 3 || _w.Figures.Count >= World.MaxFigures) return;
        var s = r.GetProperty("figure").Deserialize<SavedFigure>(VisitJson);
        if (s == null || s.Name.Length is 0 or > 24) return;
        var ground = HappeningGround(150 * _w.Scale) ?? _w.Env.Platforms.FirstOrDefault(p => p.Hwnd == IntPtr.Zero);
        if (ground == null) return;
        string owner = Text(r, "owner", 32), town = Text(r, "town", 40), giftKey = Text(r, "gift", 20);
        var g = new Figure(Settings.ParseHex(s.Color), UniqueName(s.Name), _w.Scale * Math.Clamp(s.Size, 0.5f, 2f), s.Traits.Clone(), _w.Rng) { SizeMul = Math.Clamp(s.Size, 0.5f, 2f), Visitor = VisitorKind.Guest };
        if (s.Look != null) g.Look = s.Look.Clone();
        if (s.Tastes != null) g.Tastes = s.Tastes.Clone();
        bool left = _w.Rng.NextDouble() < 0.5;
        g.PlaceAt(ground, left ? ground.X1 + 30 * _w.Scale : ground.X2 - 30 * _w.Scale);
        _w.Figures.Add(g);
        var info = new GuestInfo { VisitId = Text(r, "id", 20), From = from, Owner = owner, Town = town, Arrived = now, Leave = now + _w.Rng.Range(480, 720) * (_selfTest ? 0.01 : 1), NoteAt = now + 5 };
        _guests[g] = info;
        g.Emote(World.Gestures ? "👋" : $"hi! I'm from {owner}'s town!", 3);
        if (ItemCatalog.Find(giftKey) is { } gd && SpawnItem(gd) is { } gift) { gift.Pos = g.Base + new Vector2(left ? 30 : -30, -10) * _w.Scale; gift.Vel = Vector2.Zero; gift.OnGround = false; }
        _w.News("trip", $"{g.Name} came over from {owner}'s desktop{(ItemCatalog.Find(giftKey) is { } gd2 ? $", with a {gd2.Name.ToLowerInvariant()}" : "")}", 3, g);
        _w.Sticker("guest");
        World.Log($"friend visit: {g.Name} from {owner}");
    }

    static string Text(JsonElement r, string name, int max) => r.TryGetProperty(name, out var v) && v.GetString() is { } s ? new string(s.Where(c => !char.IsControl(c)).Take(max).ToArray()) : "";

    void GuestLeaves(Figure g, GuestInfo info)
    {
        var lines = new List<string>();
        string host = SteamHub.Ready ? Steamworks.SteamFriends.GetPersonaName() : "a friend";
        var met = info.Met.Take(4).ToList();
        lines.Add(met.Count > 0 ? $"Visited {host}'s desktop and met {string.Join(", ", met.Take(met.Count - 1))}{(met.Count > 1 ? " and " : "")}{met.Last()}." : $"Visited {host}'s desktop. Quiet place.");
        if (info.Event.Length > 0) lines.Add($"There was {info.Event} at {host}'s! I joined in.");
        if (info.Rain) lines.Add($"It rained at {host}'s place. I got soaked.");
        var souvenir = new[] { "postcard" }[0];
        var report = new { t = "report", id = info.VisitId, lines, souvenir, from = host, town = _settings.CastName };
        bool sent = _p2pSend(info.From, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report)));
        World.Log($"friend visit: {g.Name} goes home (report {(sent ? "sent" : "not sent")})");
        g.Emote(World.Gestures ? "👋" : "bye! thanks for having me!", 1.5f);
        _w.Fx.Dust(g.Base, _w.Scale, 8, 1, _w.Rng);
        _w.RemoveFigure(g);
        _guests.Remove(g);
    }

    void TravellerReturns(string visitId, JsonElement? report)
    {
        int i = _away.FindIndex(a => a.rec.VisitId == visitId);
        if (i < 0) return;
        var (f, rec) = _away[i];
        _away.RemoveAt(i);
        SaveAway();
        var ground = _w.Env.Platforms.Where(p => p.Hwnd == IntPtr.Zero).OrderBy(_ => _w.Rng.Next()).FirstOrDefault() ?? HappeningGround(100 * _w.Scale);
        if (ground == null) return;
        f.PlaceAt(ground, _w.Rng.NextDouble() < 0.5 ? ground.X1 + 30 * _w.Scale : ground.X2 - 30 * _w.Scale);
        f.SpawnT = 0.999f;
        _w.Figures.Add(f);
        if (report is { } r && r.TryGetProperty("lines", out var ls))
        {
            foreach (var l in ls.EnumerateArray().Take(4))
                if (l.GetString() is { Length: > 0 and <= 160 } line) f.Brain.Diarise(line, "★");
        }
        else f.Brain.Diarise($"Back from {rec.FriendName}'s. Had a lovely time.", "★");
        if (ItemCatalog.Find("postcard") is { } pd && SpawnItem(pd) is { } card)
        {
            card.Pos = f.Base + new Vector2(20 * _w.Scale, -10 * _w.Scale); card.Vel = Vector2.Zero; card.OnGround = false;
            card.Label = report is { } rr ? $"from {Text(rr, "from", 20)}" : $"from {rec.FriendName}";
        }
        f.Emote(World.Gestures ? "🏡" : "I'm home!", 2.2f);
        f.Brain.Cheered(0.4f);
        _w.News("trip", $"{f.Name} is back from {rec.FriendName}'s desktop with a postcard", 3, f);
        _w.Sticker("traveller");
    }

    object FriendState() => new
    {
        friends = SteamHub.FriendsPlaying().Select(p => new { id = p.id.ToString(), name = p.name }),
        away = _away.Select(a => new { name = a.fig.Name, friend = a.rec.FriendName, since = a.rec.Left.ToString("HH:mm") }),
        guests = _guests.Select(g => new { name = g.Key.Name, owner = g.Value.Owner }),
        allow = _settings.FriendVisits,
    };
}
