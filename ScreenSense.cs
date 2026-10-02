using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using NAudio.CoreAudioApi;
using static StickFight.Native;

namespace StickFight;

enum SeenKind { Line, Word, Link, Image }

/// <summary>Something read off the screen (screen pixels, as of the scan that found it).</summary>
sealed class Seen
{
    public SeenKind Kind;
    public RectangleF Rect;
    /// <summary>The word, link text or image description.</summary>
    public string Text = "";
    /// <summary>Links only: where it goes (https only; anything else is dropped).</summary>
    public string? Url;
    /// <summary>Words only: which of the figures' tastes it's about, or a feeling everyone shares.</summary>
    public Thing? About;
    public Lexicon.Feeling? Feeling;
}

/// <summary>One window's worth of reading: text lines, interesting words, links and pictures.</summary>
sealed class ScreenSnap
{
    public IntPtr Hwnd;
    public RECT Win;
    public double At;
    public readonly List<Seen> Things = new();
    /// <summary>Debug: how much text was read and how many known words were in it.</summary>
    public int TextLen, Known;
}

/// <summary>What's playing: music (something to dance to) or a video in a window (something to watch).</summary>
sealed record MediaNow(bool Music, bool Video, IntPtr VideoHwnd, float Level, double BeatAt)
{
    public static readonly MediaNow Quiet = new(false, false, IntPtr.Zero, 0, 0);
}

/// <summary>Words the figures recognise. Everything here is matched on this computer only; nothing read
/// off the screen is stored or sent anywhere.</summary>
static class Lexicon
{
    public sealed record Feeling(string Emote, float Joy, float Fear);

    public static readonly Dictionary<string, Thing> Topics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dance"] = Thing.Dancing, ["dancing"] = Thing.Dancing, ["music"] = Thing.Dancing, ["song"] = Thing.Dancing,
        ["party"] = Thing.Dancing, ["concert"] = Thing.Dancing, ["playlist"] = Thing.Dancing,
        ["soccer"] = Thing.SoccerBalls, ["football"] = Thing.SoccerBalls, ["goal"] = Thing.SoccerBalls,
        ["basketball"] = Thing.Basketballs, ["nba"] = Thing.Basketballs, ["beach"] = Thing.BeachBalls,
        ["ball"] = Thing.PlayingBall, ["sports"] = Thing.PlayingBall, ["game"] = Thing.PlayingBall, ["games"] = Thing.PlayingBall,
        ["juggle"] = Thing.Juggling, ["juggling"] = Thing.Juggling,
        ["pizza"] = Thing.Eating, ["burger"] = Thing.Eating, ["cake"] = Thing.Eating, ["cookie"] = Thing.Eating, ["cookies"] = Thing.Eating,
        ["food"] = Thing.Eating, ["recipe"] = Thing.Eating, ["lunch"] = Thing.Eating, ["dinner"] = Thing.Eating, ["breakfast"] = Thing.Eating,
        ["snack"] = Thing.Eating, ["coffee"] = Thing.Eating, ["chocolate"] = Thing.Eating, ["taco"] = Thing.Eating, ["sushi"] = Thing.Eating,
        ["sleep"] = Thing.Napping, ["nap"] = Thing.Napping, ["bed"] = Thing.Napping, ["tired"] = Thing.Napping,
        ["fight"] = Thing.Fighting, ["battle"] = Thing.Fighting, ["boxing"] = Thing.Fighting, ["karate"] = Thing.Fighting, ["ninja"] = Thing.Fighting,
        ["book"] = Thing.Reading, ["books"] = Thing.Reading, ["read"] = Thing.Reading, ["story"] = Thing.Reading, ["novel"] = Thing.Reading,
        ["climb"] = Thing.Climbing, ["climbing"] = Thing.Climbing, ["mountain"] = Thing.Climbing,
        ["travel"] = Thing.Exploring, ["explore"] = Thing.Exploring, ["adventure"] = Thing.Exploring, ["map"] = Thing.Exploring,
        ["chat"] = Thing.Chatting, ["message"] = Thing.Chatting, ["friends"] = Thing.Chatting,
        ["trick"] = Thing.Tricks, ["tricks"] = Thing.Tricks, ["skate"] = Thing.Tricks, ["parkour"] = Thing.Tricks,
        ["tower"] = Thing.HighPlaces, ["sky"] = Thing.HighPlaces, ["skyscraper"] = Thing.HighPlaces,
        ["chair"] = Thing.Sitting, ["sofa"] = Thing.Sitting, ["couch"] = Thing.Sitting,
        ["cursor"] = Thing.YourCursor, ["mouse"] = Thing.YourCursor,
    };

    public static readonly Dictionary<string, Feeling> Feelings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stickfight"] = new("that's us!", 0.6f, 0), ["animator"] = new("!!", 0.4f, 0), ["stickman"] = new("hey, us!", 0.5f, 0),
        ["cat"] = new("aww", 0.3f, 0), ["cats"] = new("aww", 0.3f, 0), ["kitten"] = new("aww ♥", 0.4f, 0),
        ["puppy"] = new("aww ♥", 0.4f, 0), ["dog"] = new("aww", 0.3f, 0), ["dogs"] = new("aww", 0.3f, 0),
        ["spider"] = new("eek!", -0.1f, 0.5f), ["spiders"] = new("eek!", -0.1f, 0.5f), ["snake"] = new("eek!", -0.1f, 0.45f),
        ["ghost"] = new("eek!", 0, 0.4f), ["monster"] = new("eek!", 0, 0.35f), ["zombie"] = new("eek!", 0, 0.4f),
        ["error"] = new("uh oh", -0.1f, 0.1f), ["crash"] = new("uh oh", -0.1f, 0.15f), ["failed"] = new("uh oh", -0.15f, 0),
        ["bug"] = new("a bug?!", 0, 0.2f), ["virus"] = new("eek!", -0.1f, 0.3f), ["delete"] = new("!!", 0, 0.2f),
        ["free"] = new("ooh", 0.2f, 0), ["winner"] = new("yay!", 0.3f, 0), ["birthday"] = new("yay!", 0.4f, 0),
        ["love"] = new("♥", 0.25f, 0), ["hello"] = new("hi!", 0.2f, 0), ["sad"] = new("aw…", -0.2f, 0),
    };

    static readonly string[] VideoTitles = { "YouTube", "Netflix", "Twitch", "Prime Video", "Disney+", "Vimeo", "Hulu", "Crunchyroll",
        "Plex", "VLC media player", "Media Player", "Movies & TV", "mpv", "PotPlayer", "TikTok", "Dailymotion", "Paramount+", "Apple TV" };
    static readonly string[] MusicTitles = { "YouTube Music", "Spotify", "SoundCloud", "Apple Music", "Deezer", "Tidal", "Bandcamp" };
    static readonly string[] VideoExes = { "vlc", "mpv", "mpc-hc64", "mpc-hc", "potplayermini64", "potplayer", "video.ui", "microsoft.media.player" };
    static readonly string[] Browsers = { "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "msedgewebview2" };

    public static bool VideoTitle(string title) =>
        !MusicTitles.Any(m => title.Contains(m, StringComparison.OrdinalIgnoreCase)) &&
        VideoTitles.Any(v => title.Contains(v, StringComparison.OrdinalIgnoreCase));
    public static bool VideoExe(string exe) => VideoExes.Contains(exe, StringComparer.OrdinalIgnoreCase);
    public static bool Browser(string exe) => Browsers.Contains(exe, StringComparer.OrdinalIgnoreCase);
}

/// <summary>The figures' eyes and ears on the rest of the desktop, running on its own background thread so a slow
/// app can never stall them. Reads the window you're using through UI Automation (text lines become ledges to stand
/// on; words, links and pictures become things to react to) and listens to which apps are playing sound (music to
/// dance to, videos to watch). Nothing leaves this computer and nothing is saved.</summary>
sealed class ScreenSense : IDisposable
{
    public volatile bool ReadText = true, Listen = true;
    /// <summary>Recently read windows (the one in use is re-read every ~0.6 s; others keep their last reading).</summary>
    public IReadOnlyList<ScreenSnap> Snaps => _snaps;
    public MediaNow Media => Fake != null && _clock.Elapsed.TotalSeconds < _fakeUntil ? Fake with { BeatAt = Math.Floor(_clock.Elapsed.TotalSeconds * 2) / 2 } : _media;
    /// <summary>Debug: pretend something's playing for a while.</summary>
    public MediaNow? Fake;
    double _fakeUntil;
    public void Pretend(MediaNow m, double seconds) { Fake = m; _fakeUntil = _clock.Elapsed.TotalSeconds + seconds; }
    public double ScanMs => _scanMs;
    public string Timing = "", EarsInfo = "";

    volatile ScreenSnap[] _snaps = Array.Empty<ScreenSnap>();
    volatile MediaNow _media = MediaNow.Quiet;
    volatile bool _stop;
    double _scanMs;
    readonly Thread _eyes, _ears;
    readonly int _pid = Environment.ProcessId;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public ScreenSense()
    {
        _eyes = new Thread(EyesLoop) { IsBackground = true, Name = "StickFight eyes", Priority = ThreadPriority.BelowNormal };
        _ears = new Thread(EarsLoop) { IsBackground = true, Name = "StickFight ears", Priority = ThreadPriority.BelowNormal };
        _eyes.SetApartmentState(ApartmentState.MTA);
        _ears.SetApartmentState(ApartmentState.MTA);
        _eyes.Start();
        _ears.Start();
    }

    public void Dispose() => _stop = true;

    uint PidOf(IntPtr h) { GetWindowThreadProcessId(h, out uint pid); return pid; }

    static string TitleOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // ---------------- eyes ----------------

    /// <summary>What we remember about reading one window (so the slow parts aren't redone every time).</summary>
    sealed class WinState
    {
        public AutomationElement? Doc;
        public double DocAt = -100, ExtrasAt = -100, ExtrasMs, ReadAt = -100;
        public List<Seen> Extras = new(), Words = new();
        public string TextKey = "";
        /// <summary>The app exposes its text as separate elements (browsers): words are found from those instead.</summary>
        public bool TextElems;
    }

    readonly Dictionary<IntPtr, WinState> _state = new();
    readonly List<IntPtr> _visible = new();
    readonly Dictionary<IntPtr, ScreenSnap> _read = new();
    double _linesMs, _wordsMs, _tVis, _tRects;
    int _rangeCount, _turn;

    void EyesLoop()
    {
        while (!_stop)
        {
            // Back off on slow apps so reading never becomes a burden on them.
            Thread.Sleep((int)Math.Clamp(_scanMs * 2.5, 600, 3000));
            if (!ReadText) { if (_snaps.Length > 0) { _snaps = Array.Empty<ScreenSnap>(); _read.Clear(); } continue; }
            try { Look(); }
            catch (Exception) { }
        }
    }

    /// <summary>Read the window you're using every time, plus one other visible window in turn (each of those
    /// gets a fresh look every few seconds).</summary>
    void Look()
    {
        double now = _clock.Elapsed.TotalSeconds;
        IntPtr fg = GetForegroundWindow();
        _visible.Clear();
        EnumWindows((h, _) =>
        {
            if (_visible.Count < 8 && IsWindowVisible(h) && !IsIconic(h) && Readable(h)) _visible.Add(h);
            return true;
        }, IntPtr.Zero);
        // Forget windows that are gone, hidden or reshaped.
        foreach (var h in _read.Keys.ToList())
            if (!_visible.Contains(h) || !Frame(h, out var r) || r.Width != _read[h].Win.Width || r.Height != _read[h].Win.Height) _read.Remove(h);
        foreach (var h in _state.Keys.ToList()) if (!IsWindowVisible(h)) _state.Remove(h);

        long t0 = Stopwatch.GetTimestamp();
        if (_visible.Contains(fg)) Read(fg, now);
        // One background window per pass, oldest reading first.
        var other = _visible.Where(h => h != fg).OrderBy(h => _state.TryGetValue(h, out var st) ? st.ReadAt : -100).FirstOrDefault();
        if (other != IntPtr.Zero && (!_state.TryGetValue(other, out var os) || now - os.ReadAt > 3)) Read(other, now);
        _scanMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        Timing = $"lines {_linesMs:0}/{_rangeCount}r (vis {_tVis:0} rects {_tRects:0}) words {_wordsMs:0}";
        // Front-most windows first (that's the order ledges are worked out in).
        _snaps = _visible.Where(_read.ContainsKey).Select(h => _read[h]).Where(sn => sn.Things.Count > 0).ToArray();
    }

    bool Readable(IntPtr h)
    {
        if (PidOf(h) == _pid || Private(h)) return false;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        if ((ex & (WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW)) != 0) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        return Frame(h, out var r) && r.Width >= 200 && r.Height >= 150;
    }

    static bool Frame(IntPtr h, out RECT r) => DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, 16) == 0 || GetWindowRect(h, out r);

    void Read(IntPtr h, double now)
    {
        if (!Frame(h, out var win)) return;
        if (!_state.TryGetValue(h, out var st)) _state[h] = st = new WinState();
        st.ReadAt = now;
        try
        {
            // Finding the text area means walking the window's whole tree: only for a new window, after a failed
            // read (not too often), or now and then (pages change).
            if ((st.Doc == null && now - st.DocAt > 6) || now - st.DocAt > 20)
            {
                st.DocAt = now;
                st.Doc = FindDocument(h);
                st.ExtrasAt = -100;
                st.TextKey = "";
            }
            var snap = new ScreenSnap { Hwnd = h, Win = win, At = now };
            if (st.Doc != null)
            {
                ReadLines(st, snap, st.Doc, win, snap.Things);
                if (snap.Things.Count == 0) st.Doc = null;
                // Links and pictures take longer to find; refresh them less often.
                if (st.Doc != null && now - st.ExtrasAt > Math.Max(3, st.ExtrasMs / 100))
                {
                    long t1 = Stopwatch.GetTimestamp();
                    st.Extras = FindExtras(st.Doc, win, out st.TextElems);
                    st.ExtrasAt = now;
                    st.ExtrasMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
                }
                snap.Things.AddRange(st.Extras);
            }
            _read[h] = snap;
        }
        catch (Exception e)
        {
            World.Log($"screen read failed: {e.GetType().Name}");
            st.Doc = null;   // the window closed or the app went away mid-read: try again later
            _read.Remove(h);
        }
    }

    // Never read password managers, credential prompts or private browsing windows, even locally.
    static readonly string[] PrivateExes = { "1password", "keepass", "keepassxc", "bitwarden", "lastpass", "dashlane", "keeper", "enpass",
        "roboform", "nordpass", "proton pass", "credentialuibroker", "consent", "logonui", "lockapp" };
    static readonly string[] PrivateTitles = { "InPrivate", "Incognito", "Private Browsing", "password", "1Password", "Bitwarden", "KeePass", "Sign in" };
    readonly Dictionary<uint, bool> _private = new();

    bool Private(IntPtr h)
    {
        uint pid = PidOf(h);
        if (!_private.TryGetValue(pid, out bool p))
        {
            string exe = "";
            try { exe = Process.GetProcessById((int)pid).ProcessName; } catch { }
            _private[pid] = p = PrivateExes.Any(x => exe.Equals(x, StringComparison.OrdinalIgnoreCase));
            if (_private.Count > 200) _private.Clear();
        }
        string title = TitleOf(h);
        return p || PrivateTitles.Any(t => title.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    static readonly Condition DocCond = new AndCondition(
        new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
        new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)));

    static AutomationElement? FindDocument(IntPtr h)
    {
        var root = AutomationElement.FromHandle(h);
        if (root == null) return null;
        // Prefer the biggest text area that's on screen (a web page, the editor of a text app).
        var all = root.FindAll(TreeScope.Descendants, DocCond);
        AutomationElement? best = null;
        double bestArea = 0;
        foreach (AutomationElement e in all)
        {
            var r = e.Current.BoundingRectangle;
            if (r.IsEmpty || e.Current.IsOffscreen) continue;
            double a = r.Width * r.Height;
            if (a > bestArea) { bestArea = a; best = e; }
        }
        return bestArea >= 120 * 80 ? best : null;
    }

    void ReadLines(WinState st, ScreenSnap snap, AutomationElement doc, RECT win, List<Seen> into)
    {
        long t0 = Stopwatch.GetTimestamp();
        if (!doc.TryGetCurrentPattern(TextPattern.Pattern, out object p)) return;
        var tp = (TextPattern)p;
        // Chromium-based apps take a long time to work out their "visible ranges"; asking what's under the top-left and
        // bottom-right corners of the text area and taking everything between is much quicker.
        TextPatternRange[] ranges;
        try
        {
            var b = doc.Current.BoundingRectangle;
            double L = Math.Max(b.Left, win.Left) + 6, T = Math.Max(b.Top, win.Top) + 6, R = Math.Min(b.Right, win.Right) - 6, B = Math.Min(b.Bottom, win.Bottom) - 6;
            // Probe along the top and bottom edges (the corners may be margins or scroll bars) and take the
            // earliest start and the latest end.
            TextPatternRange? a = null, z = null;
            for (int i = 0; i < 4; i++)
            {
                double x = L + (R - L) * (i / 3.0);
                try
                {
                    var top = tp.RangeFromPoint(new System.Windows.Point(x, T));
                    if (a == null || top.CompareEndpoints(TextPatternRangeEndpoint.Start, a, TextPatternRangeEndpoint.Start) < 0) a = top;
                }
                catch (Exception) { }
                try
                {
                    var bot = tp.RangeFromPoint(new System.Windows.Point(x, B));
                    if (z == null || bot.CompareEndpoints(TextPatternRangeEndpoint.End, z, TextPatternRangeEndpoint.End) > 0) z = bot;
                }
                catch (Exception) { }
            }
            if (a == null || z == null) throw new InvalidOperationException();
            a = a.Clone();
            a.MoveEndpointByRange(TextPatternRangeEndpoint.End, z, TextPatternRangeEndpoint.End);
            ranges = new[] { a };
            _rangeCount = 0;
        }
        catch (Exception)
        {
            ranges = tp.GetVisibleRanges();
            _rangeCount = ranges.Length;
        }
        _tVis = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        var text = new StringBuilder();
        int lines = 0;
        foreach (var range in ranges)
        {
            foreach (var r in range.GetBoundingRectangles())
            {
                if (lines >= 160) break;
                if (r.Width < 24 || r.Height < 7 || r.Height > 90) continue;
                if (r.Bottom < win.Top + 20 || r.Top > win.Bottom - 4 || r.Right < win.Left || r.Left > win.Right) continue;
                float x1 = (float)Math.Max(r.Left, win.Left), x2 = (float)Math.Min(r.Right, win.Right);
                into.Add(new Seen { Kind = SeenKind.Line, Rect = RectangleF.FromLTRB(x1, (float)r.Top, x2, (float)r.Bottom) });
                lines++;
            }
            _tRects = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            if (text.Length < 6000) text.Append(range.GetText(6000 - text.Length)).Append(' ');
        }
        _linesMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        // Words the figures know: find where each one is (only the first few, only whole words). Unchanged text in an
        // unmoved window means they're where they were.
        string key = $"{win.Left},{win.Top},{win.Width},{win.Height}:{text.Length}:{text.ToString().GetHashCode()}";
        snap.TextLen = text.Length;
        if (st.TextElems) return;   // words come with the links and pictures
        if (key == st.TextKey) { into.AddRange(st.Words); return; }
        long t1 = Stopwatch.GetTimestamp();
        int before = into.Count;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int found = 0;
        foreach (var word in Words(text.ToString()))
        {
            if (found >= 8) break;
            bool topic = Lexicon.Topics.TryGetValue(word, out var about);
            bool feel = Lexicon.Feelings.TryGetValue(word, out var feeling);
            if ((!topic && !feel) || !seen.Add(word)) continue;
            snap.Known++;
            foreach (var range in ranges)
                if (Locate(range, word) is { } rect && rect.Top >= win.Top && rect.Bottom <= win.Bottom)
                {
                    into.Add(new Seen { Kind = SeenKind.Word, Rect = rect, Text = word.ToLowerInvariant(), About = topic ? about : null, Feeling = feel ? feeling : null });
                    found++;
                    break;
                }
        }
        st.TextKey = key;
        st.Words = into.GetRange(before, into.Count - before);
        _wordsMs = Stopwatch.GetElapsedTime(t1).TotalMilliseconds;
    }

    static IEnumerable<string> Words(string s)
    {
        int i = 0;
        while (i < s.Length)
        {
            while (i < s.Length && !char.IsLetter(s[i])) i++;
            int st = i;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i - st >= 2 && i - st <= 14) yield return s.Substring(st, i - st);
        }
    }

    /// <summary>Where a whole word appears in a range (skips matches inside longer words).</summary>
    static RectangleF? Locate(TextPatternRange range, string word)
    {
        var search = range.Clone();
        for (int tries = 0; tries < 6; tries++)
        {
            var hit = search.FindText(word, false, true);
            if (hit == null) return null;
            // Whole words only. (Chromium's "expand to word" is unreliable, so look at the letters on either side instead.)
            bool whole = hit.GetText(word.Length + 1).Trim().Equals(word, StringComparison.OrdinalIgnoreCase);
            var before = hit.Clone();
            if (whole && before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == -1)
            {
                string bt = before.GetText(word.Length + 2);
                if (bt.Length > 0 && char.IsLetter(bt[0])) whole = false;
            }
            var after = hit.Clone();
            if (whole && after.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) == 1)
            {
                string at = after.GetText(word.Length + 2);
                if (at.Length > word.Length && char.IsLetter(at[^1])) whole = false;
            }
            if (whole)
            {
                var rs = hit.GetBoundingRectangles();
                if (rs.Length > 0 && rs[0].Width > 2) return new RectangleF((float)rs[0].Left, (float)rs[0].Top, (float)rs[0].Width, (float)rs[0].Height);
            }
            search.MoveEndpointByRange(TextPatternRangeEndpoint.Start, hit, TextPatternRangeEndpoint.End);
        }
        return null;
    }

    static readonly Condition LinkOrImage = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

    static List<Seen> FindExtras(AutomationElement doc, RECT win, out bool textElems)
    {
        textElems = false;
        var wordsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int words = 0;
        var list = new List<Seen>();
        var cr = new CacheRequest();
        cr.Add(AutomationElement.NameProperty);
        cr.Add(AutomationElement.BoundingRectangleProperty);
        cr.Add(AutomationElement.ControlTypeProperty);
        cr.Add(AutomationElement.IsOffscreenProperty);
        cr.Add(ValuePattern.ValueProperty);
        cr.AutomationElementMode = AutomationElementMode.None;
        AutomationElementCollection found;
        using (cr.Activate()) found = doc.FindAll(TreeScope.Descendants, LinkOrImage);
        int links = 0, images = 0;
        foreach (AutomationElement e in found)
        {
            var c = e.Cached;
            if (c.IsOffscreen) continue;
            var r = c.BoundingRectangle;
            if (r.IsEmpty || r.Top < win.Top + 20 || r.Bottom > win.Bottom || r.Left < win.Left || r.Right > win.Right) continue;
            var rect = new RectangleF((float)r.Left, (float)r.Top, (float)r.Width, (float)r.Height);
            string name = (c.Name ?? "").Trim();
            if (c.ControlType == ControlType.Text)
            {
                textElems = true;
                if (words < 8 && name.Length > 1) AddWords(list, name, rect, wordsSeen, ref words);
                continue;
            }
            if (words < 8 && name.Length > 1) AddWords(list, name, rect, wordsSeen, ref words);
            if (c.ControlType == ControlType.Hyperlink)
            {
                if (links >= 40 || name.Length < 2 || r.Width < 16) continue;
                string url = e.GetCachedPropertyValue(ValuePattern.ValueProperty) as string ?? "";
                // Only plain web links: no javascript:, file:, mailto: or anything else that could run something.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) continue;
                list.Add(new Seen { Kind = SeenKind.Link, Rect = rect, Text = Shorten(CleanName(name), 28), Url = u.AbsoluteUri });
                links++;
            }
            else if (images < 16 && r.Width >= 70 && r.Height >= 45)
            {
                list.Add(new Seen { Kind = SeenKind.Image, Rect = rect, Text = Shorten(name, 40) });
                images++;
            }
        }
        return list;
    }

    /// <summary>Known words in a piece of text on screen. Where exactly each one sits is estimated from its place in
    /// the text (exact for one-line labels and titles, close enough on the first line of a paragraph).</summary>
    static void AddWords(List<Seen> list, string text, RectangleF r, HashSet<string> seen, ref int count)
    {
        int i = 0;
        bool oneLine = r.Height < 34;
        float charW = oneLine ? r.Width / Math.Max(1, text.Length) : 7.5f;
        while (i < text.Length && count < 8)
        {
            while (i < text.Length && !char.IsLetter(text[i])) i++;
            int st = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            if (i - st < 2 || i - st > 14) continue;
            string word = text.Substring(st, i - st);
            bool topic = Lexicon.Topics.TryGetValue(word, out var about), feel = Lexicon.Feelings.TryGetValue(word, out var feeling);
            if (!topic && !feel || !seen.Add(word)) continue;
            float x = r.Left + st * charW, w = (i - st) * charW;
            if (x + w > r.Right) { if (oneLine) continue; x = r.Left; }   // past the first line: just mark the start of the block
            list.Add(new Seen { Kind = SeenKind.Word, Rect = new RectangleF(x, r.Top, w, oneLine ? r.Height : 18), Text = word.ToLowerInvariant(),
                                About = topic ? about : null, Feeling = feel ? feeling : null });
            count++;
        }
    }

    /// <summary>Accessibility names carry extras meant for screen readers ("unread, general (text channel)"); keep the gist.</summary>
    static string CleanName(string s)
    {
        foreach (var pre in new[] { "unread, ", "unread mentions, ", "link, ", "visited, " })
            if (s.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) s = s[pre.Length..];
        int paren = s.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 3 && s.EndsWith(')')) s = s[..paren];
        return s.Trim();
    }

    static string Shorten(string s, int n)
    {
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    // ---------------- ears ----------------

    sealed record Session(AudioSessionControl Ctl, uint Pid, string Exe);

    void EarsLoop()
    {
        MMDeviceEnumerator? devices = null;
        List<Session> sessions = new();
        double listedAt = -10, beatAt = 0;
        float avg = 0;
        var exeOf = new Dictionary<uint, string>();
        while (!_stop)
        {
            Thread.Sleep(80);
            if (!Listen) { _media = MediaNow.Quiet; continue; }
            double now = _clock.Elapsed.TotalSeconds;
            try
            {
                if (now - listedAt > 2)
                {
                    listedAt = now;
                    devices ??= new MMDeviceEnumerator();
                    using var dev = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    var mgr = dev.AudioSessionManager;
                    mgr.RefreshSessions();
                    sessions.Clear();
                    var all = mgr.Sessions;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var s = all[i];
                        uint pid = s.GetProcessID;
                        if (pid == 0 || pid == _pid) continue;
                        if (!exeOf.TryGetValue(pid, out var exe))
                        {
                            try { exe = Process.GetProcessById((int)pid).ProcessName; } catch { exe = ""; }
                            exeOf[pid] = exe;
                        }
                        sessions.Add(new Session(s, pid, exe));
                    }
                    if (exeOf.Count > 300) exeOf.Clear();
                }
                // The loudest app decides what kind of thing is playing.
                Session? loud = null;
                float peak = 0;
                foreach (var s in sessions)
                {
                    if (s.Ctl.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive) continue;
                    float v = s.Ctl.AudioMeterInformation?.MasterPeakValue ?? 0;
                    if (v > peak) { peak = v; loud = s; }
                }
                EarsInfo = $"{sessions.Count} sessions: " + string.Join(", ", sessions.Select(x => $"{x.Exe}({x.Ctl.State.ToString().Replace("AudioSessionState", "")}:{x.Ctl.AudioMeterInformation?.MasterPeakValue ?? -1:0.00})"));
                if (loud == null || peak < 0.015f) { _media = _media with { Music = false, Video = false, Level = 0 }; continue; }
                avg += (peak - avg) * 0.08f;
                if (peak > avg * 1.35f && peak > 0.06f && now - beatAt > 0.28) beatAt = now;
                IntPtr video = FindVideoWindow(loud, exeOf);
                _media = new MediaNow(video == IntPtr.Zero, video != IntPtr.Zero, video, peak, beatAt);
            }
            catch (Exception)
            {
                sessions.Clear();
                listedAt = now;   // device changed or went away; list again in a moment
                _media = MediaNow.Quiet;
            }
        }
    }

    readonly List<IntPtr> _tops = new();

    /// <summary>The on-screen window that's showing the video this sound belongs to, if it looks like one.</summary>
    IntPtr FindVideoWindow(Session s, Dictionary<uint, string> exeOf)
    {
        _tops.Clear();
        EnumWindows((h, _) => { if (IsWindowVisible(h) && !IsIconic(h)) _tops.Add(h); return true; }, IntPtr.Zero);
        bool browser = Lexicon.Browser(s.Exe), player = Lexicon.VideoExe(s.Exe);
        foreach (var h in _tops)
        {
            uint pid = PidOf(h);
            if (pid == _pid) continue;
            if (!exeOf.TryGetValue(pid, out var exe))
            {
                try { exe = Process.GetProcessById((int)pid).ProcessName; } catch { exe = ""; }
                exeOf[pid] = exe;
            }
            string title = TitleOf(h);
            if (title.Length == 0) continue;
            // Browsers play sound from a helper process, so match by app rather than by process.
            bool sameApp = pid == s.Pid || (browser && exe.Equals(s.Exe, StringComparison.OrdinalIgnoreCase));
            if (sameApp && (player || Lexicon.VideoTitle(title))) return h;
            // Store apps (Netflix, Movies & TV) live inside a frame host window.
            if (!sameApp && exe.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) && Lexicon.VideoTitle(title)) return h;
        }
        return IntPtr.Zero;
    }
}
