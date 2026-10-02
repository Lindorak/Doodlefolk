using System.Numerics;

namespace Doodlefolk;

/// <summary>Reacting to what's on your screen: walking over to words they know (and loving, hating or being scared
/// of them), pointing out links (which only open if you click the bubble), grooving when music plays and sitting
/// down to watch when a video's on.</summary>
sealed partial class Brain
{
    readonly Dictionary<string, float> _wordSeen = new();
    SeenNow? _look;
    Vector2 _lookAt;
    bool _screenReacted, _scared;
    float _quietT;
    IntPtr _videoHwnd;
    double _lastBeat;

    /// <summary>What there is to do on screen right now, as choices for <see cref="Choose"/>.</summary>
    void ScreenOptions(World w, OptionList opts)
    {
        float E = P.Energy, tired = 1 - Stamina;
        var media = w.Media;
        if (media.Music && Stamina > 0.25f)
            opts.Add((MathF.Max(0, f.Tastes.Of(Thing.Dancing) + 0.4f) * 1.4f + P.Playfulness * 0.3f) * (0.5f + Joy) * (0.6f + E * 0.6f),
                      () => Go(G.Groove, rng.Range(8, 22)), "Dance to the music");
        opts.Category = "Watch the video";
        if (media.Video && WatchSpot(w, media.VideoHwnd) is { } spot)
            opts.Add(((0.45f + Boredom * 0.9f + tired * 0.5f) * Taste(Thing.Sitting) * (1.2f - E * 0.4f), () =>
            {
                _videoHwnd = media.VideoHwnd;
                _itemPending = false;
                if (_item != null) LeaveItem();
                Navigate(() => spot.Resolve(w.Env), 6 * S, false, () => Go(G.WatchScreen, rng.Range(20, 60)), WalkPurpose.Watch);
            }));
        if (w.ScreenReact && Stamina > 0.2f && PickWord(w) is { ledge: { } ledge } pw)
            opts.Add((0.25f + P.Curiosity * 0.9f) * (0.4f + Boredom) * pw.interest * Taste(Thing.Reading) * 0.9f, () => GoLookAt(w, pw.word, ledge), $"Look at \"{pw.word.Seen.Text}\" on screen");
        if (w.ScreenLinks && w.Offer == null && World.Now > w.NextOfferAt && Stamina > 0.3f && PickLink(w) is { ledge: { } lledge } pl)
            opts.Add(P.Curiosity * (0.3f + P.Sociability * 0.5f) * (0.4f + Boredom) * 0.7f * MathF.Max(0.2f, 0.6f + UserFondness), () => GoLookAt(w, pl.link, lledge), "Show you a link");
    }

    /// <summary>Debug: do one of the screen things now (word, link, watch, groove). Returns what happened.</summary>
    public string ForceScreen(World w, string what)
    {
        _wordSeen.Clear();
        TraceUntil = _t0 + 30;
        switch (what)
        {
            case "word":
                if (PickWord(w) is { ledge: { } l } pw) { GoLookAt(w, pw.word, l); return $"going to '{pw.word.Seen.Text}' at {pw.word.Centre} ledge y={l.Y}"; }
                return "no reachable word";
            case "link":
                w.NextOfferAt = 0;
                if (PickLink(w) is { ledge: { } ll } pl) { GoLookAt(w, pl.link, ll); return $"going to a link at {pl.link.Centre} ledge y={ll.Y}"; }
                return "no reachable link";
            case "watch":
                if (w.Media.Video && WatchSpot(w, w.Media.VideoHwnd) is { } spot)
                {
                    _videoHwnd = w.Media.VideoHwnd;
                    Navigate(() => spot.Resolve(w.Env), 6 * S, false, () => Go(G.WatchScreen, 30), WalkPurpose.Watch);
                    return "going to watch";
                }
                return "no video";
            case "groove": Go(G.Groove, 15); return "grooving";
        }
        return "?";
    }

    // ---------------- words & links ----------------

    (SeenNow word, float interest, Platform? ledge)? PickWord(World w)
    {
        SeenNow? best = null;
        float bestI = 0;
        foreach (var it in w.OnScreen(SeenKind.Word))
        {
            if (_wordSeen.TryGetValue(it.Seen.Text, out float at) && _t0 - at < 150) continue;
            float i = it.Seen.About is Thing t ? 0.2f + MathF.Abs(f.Tastes.Of(t)) * 1.4f : 0.3f + (it.Seen.Feeling?.Fear ?? 0) + MathF.Abs(it.Seen.Feeling?.Joy ?? 0);
            float d = Vector2.Distance(it.Centre, f.Base);
            i *= 1f / (1 + d / (900 * S));
            if (i > bestI) { bestI = i; best = it; }
        }
        return best is { } b ? (b, bestI, w.LedgeFor(b)) : null;
    }

    (SeenNow link, Platform? ledge)? PickLink(World w)
    {
        var links = w.OnScreen(SeenKind.Link).Where(l => !_wordSeen.ContainsKey(l.Seen.Url!) && Vector2.Distance(l.Centre, f.Base) < 1200 * S).ToList();
        if (links.Count == 0) return null;
        var pick = links[rng.Next(links.Count)];
        return (pick, w.LedgeFor(pick));
    }

    void GoLookAt(World w, SeenNow it, Platform ledge)
    {
        _wordSeen[it.Seen.Url ?? it.Seen.Text] = _t0;
        if (_wordSeen.Count > 200) _wordSeen.Clear();
        // Stand just beside it so it's in view, not underfoot.
        float side = f.Base.X < it.Centre.X ? -1 : 1;
        float x = M.ClampIn(it.Centre.X + side * (it.Rect.Width / 2 + 10 * S), ledge.X1 + 6 * S, ledge.X2 - 6 * S);
        var spot = Anchor.On(w.Env, ledge, x);
        var seen = it;
        _itemPending = false;
        if (_item != null) LeaveItem();
        Navigate(() => spot.Resolve(w.Env), 8 * S, false, () =>
        {
            _look = seen;
            _lookAt = seen.Centre;
            _screenReacted = _scared = false;
            Go(G.LookAtScreen, seen.Seen.Kind == SeenKind.Link ? 9 : rng.Range(2.5f, 4.5f));
        }, WalkPurpose.Look);
    }

    void DoLookAtScreen(World w)
    {
        f.DesiredVX = 0;
        if (_look is not { } it) { Go(G.Idle, 1); return; }
        // Follow it if the window moved; if it scrolled away, look puzzled.
        var now = w.OnScreen(it.Seen.Kind).FirstOrDefault(o => ReferenceEquals(o.Seen, it.Seen) || (o.Seen.Text == it.Seen.Text && o.Seen.Url == it.Seen.Url && Vector2.Distance(o.Centre, _lookAt) < 300 * S));
        if (now.Seen != null) _lookAt = now.Centre;
        else if (_t > 0.6f && !_screenReacted) { f.Emote("?", 1.2f); _screenReacted = true; _dur = MathF.Min(_dur, _t + 1.5f); }
        f.LookAt = _lookAt;
        FaceTo(_lookAt.X);
        if (_t > 0.5f && !_screenReacted) { _screenReacted = true; React(w, it); }
        if (it.Seen.Kind == SeenKind.Link)
        {
            // Point at it while the bubble's up.
            if (w.Offer?.By == f && w.Offer.Until > World.Now) { f.SetAction(_t % 3 < 1.2f ? Act.Wave : Act.Stand); _dur = MathF.Max(_dur, _t + 0.2f); }
            else if (_screenReacted && w.Offer?.By != f && _t > 1.5f) _dur = MathF.Min(_dur, _t + 0.5f);
        }
        if (_scared)
        {
            // Back away from it.
            float away = MathF.Sign(f.Base.X - _lookAt.X + 0.01f);
            f.DesiredVX = away * f.WalkSpeed * 1.4f;
            f.KeepFacing = true;
        }
        if (_t > _dur) { _look = null; f.KeepFacing = false; Go(G.Idle, rng.Range(0.8f, 2)); }
    }

    void React(World w, SeenNow it)
    {
        string word = it.Seen.Text;
        if (it.Seen.Kind == SeenKind.Link)
        {
            if (w.Offer != null) { f.Emote("hm", 1); return; }
            w.Offer = new LinkOffer { By = f, Url = it.Seen.Url!, Text = it.Seen.Text, Until = World.Now + 9 };
            w.NextOfferAt = World.Now + rng.Range(60, 150);
            f.Emote(rng.NextDouble() < 0.5 ? "ooh, look!" : "this one!", 1.6f);
            return;
        }
        if (it.Seen.Feeling is { } feel)
        {
            DiaryWord(word, feel.Joy - feel.Fear);
            f.Emote(feel.Emote, 1.8f);
            if (feel.Joy > 0) { Cheered(feel.Joy * 0.5f); if (feel.Joy >= 0.4f) f.SetAction(Act.Cheer); }
            else Saddened(-feel.Joy * 0.4f);
            if (feel.Fear > 0.25f * (0.5f + P.Bravery)) { Fear = M.Clamp01(Fear + feel.Fear); _scared = true; _dur = _t + 1.4f; }
            return;
        }
        if (it.Seen.About is not Thing t) return;
        float o = f.Tastes.Of(t);
        DiaryWord(word, o);
        if (o > 0.35f)
        {
            f.Emote(rng.NextDouble() < 0.5 ? $"♥ {word}" : $"{word}!!", 1.8f);
            Cheered(0.1f + o * 0.15f);
            Boredom = MathF.Max(0, Boredom - 0.15f);
            if (t == Thing.Dancing) f.StartFidget(Fidget.Groove);
            else if (P.Playfulness > 0.6f && Stamina > 0.4f) f.RequestFlip(55 * S);
            else f.SetAction(Act.Cheer);
        }
        else if (o < -0.35f)
        {
            f.Emote(rng.NextDouble() < 0.5 ? $"ew, {word}" : $"ugh, {word}", 1.8f);
            Annoyance = M.Clamp01(Annoyance + 0.1f);
            f.SetAction(Act.HandsHips);
        }
        else f.Emote(rng.NextDouble() < 0.6 ? $"{word}?" : "hm", 1.3f);
        // Anyone nearby wonders what the fuss is about.
        foreach (var o2 in w.Figures)
            if (o2 != f && !o2.Dead && o2.Mode == Mode.Control && Vector2.Distance(o2.Base, f.Base) < 160 * S && o2.Brain._g == G.Idle)
            {
                o2.LookAt = _lookAt;
                if (rng.NextDouble() < 0.5) o2.Emote("?", 1);
            }
    }

    /// <summary>The user clicked the bubble and opened the link.</summary>
    public void LinkTaken()
    {
        DiaryLinkOpened();
        f.Emote("yay!", 1.4f);
        Cheered(0.2f);
        FeelUser(0.04f, "Opened a link I found");
        f.SetAction(Act.Cheer);
    }

    // ---------------- music ----------------

    void DoGroove(World w)
    {
        f.DesiredVX = 0;
        var m = w.Media;
        _quietT = m.Music || (_rainDance && w.Weather.Raining) ? 0 : _quietT + World.Dt;
        if (_quietT > 2.5f) { f.Emote("aw", 1); Go(G.Idle, rng.Range(1, 2)); return; }
        if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove);
        if (m.BeatAt != _lastBeat)
        {
            _lastBeat = m.BeatAt;
            // Big beats: the odd hop, spin or note.
            if (m.Level > 0.45f && f.Grounded && rng.NextDouble() < 0.12 * (0.5f + P.Playfulness)) f.RequestJump(new Vector2(0, -260 * S), 0.08f);
            else if (rng.NextDouble() < 0.08) f.Facing = -f.Facing;
            if (rng.NextDouble() < 0.06) f.Emote(rng.NextDouble() < 0.5 ? "♪" : "♫", 1);
        }
        Cheered(World.Dt * 0.03f);
        Boredom = MathF.Max(0, Boredom - World.Dt * 0.02f);
        Stamina = MathF.Max(0, Stamina - World.Dt * 0.004f);
        if (_t > 10 && _t - World.Dt <= 10) DiaryDanced();
        if (_t > _dur || Stamina < 0.15f) Go(G.Idle, rng.Range(1, 2.5f));
    }

    // ---------------- videos ----------------

    /// <summary>Somewhere to sit and watch a window: the floor (or a ledge) just under it.</summary>
    Anchor? WatchSpot(World w, IntPtr hwnd)
    {
        if (w.Env.RectOf(hwnd) is not { } r) return null;
        if (w.Env.Visible(hwnd, RectangleF.FromLTRB(r.Left, r.Top + r.Height * 0.5f, r.Right, r.Top + r.Height * 0.5f + 1)) < 0.6f) return null;
        for (int tries = 0; tries < 4; tries++)
        {
            float x = rng.Range(r.Left + r.Width * 0.15f, r.Right - r.Width * 0.15f);
            if (w.Env.Below(x, r.Bottom - 6) is { } p && p.Y < r.Bottom + 260 * S && p.Seen == null)
                return Anchor.On(w.Env, p, x);
        }
        return null;
    }

    void DoWatchScreen(World w)
    {
        f.DesiredVX = 0;
        if (!f.Grounded) { Go(G.Idle, 0.5f); return; }
        var m = w.Media;
        _quietT = m.Video && m.VideoHwnd == _videoHwnd ? 0 : _quietT + World.Dt;
        if (_quietT > 4 || w.Env.RectOf(_videoHwnd) is not { } r) { f.Emote(rng.NextDouble() < 0.5 ? "aw" : "the end?", 1.3f); Go(G.Idle, rng.Range(1, 2)); return; }
        if (_t < 1.2f && KeepSpace(w)) { f.SetAction(Act.Stand); return; }
        f.FloorSit = true;
        f.SetAction(Act.SitBack);
        f.LookAt = new Vector2((r.Left + r.Right) / 2f, (r.Top + r.Bottom) / 2f);
        if (m.BeatAt != _lastBeat)
        {
            _lastBeat = m.BeatAt;
            if (m.Level > 0.55f && rng.NextDouble() < 0.06) f.Emote(rng.NextDouble() < 0.5 ? "!" : "whoa", 1);
        }
        if (rng.NextDouble() < World.Dt / 14)
            f.Emote(Joy > 0.5f || P.Playfulness > 0.6f ? (rng.NextDouble() < 0.6 ? "ha" : "♥") : rng.NextDouble() < 0.5 ? "ooh" : "hm", 1.2f);
        Cheered(World.Dt * 0.01f);
        Boredom = MathF.Max(0, Boredom - World.Dt * 0.03f);
        if (_t > 20 && _t - World.Dt <= 20) DiaryVideo();
        Loneliness = MathF.Max(0, Loneliness - World.Dt * 0.01f * w.Figures.Count(o => o != f && o.Brain._g == G.WatchScreen));
        Stamina = MathF.Min(1, Stamina + World.Dt * 0.01f);
        if (_t > _dur) Go(G.Idle, rng.Range(1, 2));
    }
}
