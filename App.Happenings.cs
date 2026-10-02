using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>A town event in progress: a festival, a talent show or a race day.</summary>
sealed class Happening
{
    public string Kind = "";
    /// <summary>A festival from a mod: its own title, decorations, food and fireworks.</summary>
    public ModEvent? Custom;
    public int Phase;
    public float T, PhaseT;
    public float Left, Right, Y;
    public IntPtr Hwnd;
    public readonly List<Figure> Who = new();
    public readonly List<Figure> Crowd = new();
    public readonly Dictionary<int, float> Score = new();
    public readonly List<Figure> Finished = new();
    public readonly List<Item> Temp = new();
    public Item? Stage, Flag;
    public int Turn = -1;
    /// <summary>The last countdown number called, and whether the results are in.</summary>
    public int Called = -1;
    public bool Wrapped;
    public float StartX, FinishX;
    public Figure? Winner;
    public float Centre => (Left + Right) / 2;
    public string Title => Custom != null ? Custom.Title : Kind switch { "festival" => "the festival", "talent" => "the talent show", _ => "race day" };
}

sealed partial class World
{
    public Happening? Happening;
}

/// <summary>Town events. Every so often (a setting; or whenever you start one from the Studio) the town holds a
/// festival (fairy lights and lanterns go up, everyone dances and chats, fireworks after dark), a talent show (acts
/// take turns on the stage, the crowd cheers, the best act wins a trophy) or a race day (racers line up, a countdown,
/// a sprint to the chequered flag).</summary>
sealed partial class App
{
    double _happeningAt = 1800;

    void HappeningFrame(float dt, double now)
    {
        var h = _w.Happening;
        if (h == null)
        {
            if (!_settings.Events || PetMode || now < _happeningAt || _w.Tourney != null || _w.Game != null) return;
            _happeningAt = now + _w.Rng.Range(2400, 6000) / MathF.Max(0.5f, World.Drama * World.EventRate);
            if (_w.Figures.Count(f => f.Mode == Mode.Control) < 3 || World.EventRate <= 0) return;
            var kinds = new List<string> { "festival", "talent", "race" };
            kinds.AddRange(Mods.Events.Select(e => "mod:" + e.Key));
            StartHappening(kinds[_w.Rng.Next(kinds.Count)]);
            return;
        }
        h.T += dt; h.PhaseT += dt;
        // Keep everyone taking part on task (they may have been distracted, or knocked about).
        if ((int)(h.T * 2) != (int)((h.T - dt) * 2))
            foreach (var f in h.Who.Concat(h.Crowd).ToList())
            {
                if (!_w.Figures.Contains(f) || f.Brain.HapRole.Length == 0) { LeftHappening(h, f); continue; }
                f.Brain.KeepInHappening(_w);
            }
        // Everyone gone (removed, or wandered off for good): call it off.
        if (h.Who.Count + h.Crowd.Count == 0 || (h.Kind != "festival" && (h.Who.Count == 0 || (h.Kind == "talent" && h.Phase == 0 && h.Who.Count < 2))))
        {
            EndHappening(h, false);
            return;
        }
        switch (h.Kind)
        {
            case "festival": FestivalStep(h, now); break;
            case "talent": TalentStep(h); break;
            case "race": RaceStep(h); break;
        }
    }

    /// <summary>The widest stretch of floor where most of them are.</summary>
    Platform? HappeningGround(float minWidth)
    {
        var counts = new Dictionary<Platform, int>();
        foreach (var f in _w.Figures)
            if (f.Mode == Mode.Control && _w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { Item: null } seg && seg.X2 - seg.X1 >= minWidth)
                counts[seg] = counts.GetValueOrDefault(seg) + 1;
        if (counts.Count == 0)
            return _w.Env.Platforms.Where(p => p.Item == null && p.X2 - p.X1 >= minWidth).OrderByDescending(p => p.X2 - p.X1).FirstOrDefault();
        return counts.OrderByDescending(kv => kv.Value * 1000 + (kv.Key.X2 - kv.Key.X1) / 10).First().Key;
    }

    public string StartHappening(string kind)
    {
        if (_w.Happening != null) return "something's already on";
        ModEvent? custom = null;
        if (kind.StartsWith("mod:"))
        {
            custom = Mods.Events.FirstOrDefault(e => e.Key == kind[4..]);
            if (custom == null) return "no such event";
            kind = "festival";
        }
        float s = _w.Scale;
        var ground = HappeningGround(kind == "race" ? 700 * s : 450 * s);
        if (ground == null) return "nowhere wide enough";
        var h = new Happening { Kind = kind, Y = ground.Y, Hwnd = ground.Hwnd, Custom = custom };
        Contribute("FESTIVALS_HELD", 1);
        float width = MathF.Min(ground.X2 - ground.X1 - 60 * s, kind == "race" ? 1500 * s : 900 * s);
        float mid = Math.Clamp(_w.Figures.Where(f => f.Mode == Mode.Control).Select(f => f.Base.X).DefaultIfEmpty((ground.X1 + ground.X2) / 2).Average(), ground.X1 + width / 2 + 30 * s, ground.X2 - width / 2 - 30 * s);
        h.Left = mid - width / 2; h.Right = mid + width / 2;
        var people = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Hunter && !f.Brain.InFight && f.Visitor == VisitorKind.None).OrderBy(_ => _w.Rng.Next()).ToList();
        if (people.Count < 2) return "not enough figures";
        Item? Put(string key, float x)
        {
            if (ItemCatalog.Find(key) is not { } def || SpawnItem(def) is not { } it) return null;
            it.Pos = new Vector2(x, h.Y - 2); it.Vel = Vector2.Zero; it.OnGround = false; it.Temporary = true;
            h.Temp.Add(it);
            return it;
        }
        switch (kind)
        {
            case "festival" when custom != null:
                var things = custom.Decor.Concat(custom.Food).Where(k => ItemCatalog.Find(k) != null).ToList();
                for (int i = 0; i < things.Count; i++) Put(things[i], h.Left + width * (i + 0.5f) / things.Count);
                foreach (var f in people) { h.Crowd.Add(f); f.Brain.JoinHappening("reveller", _w); }
                _w.News("town", custom.News.Length > 0 ? custom.News : $"{custom.Title} is on!", 3);
                break;
            case "festival":
                Put("fairylights", h.Left + width * 0.25f);
                Put("fairylights", h.Right - width * 0.25f);
                for (int i = 0; i < 3; i++) Put("lantern", h.Left + width * (0.12f + i * 0.38f));
                foreach (var f in people) { h.Crowd.Add(f); f.Brain.JoinHappening("reveller", _w); }
                _w.News("town", "The town festival is on! Music, lights and dancing", 3);
                break;
            case "talent":
                h.Stage = _w.Items.FirstOrDefault(i => i.Def.Key == "stage" && i.Free && i.OnGround && MathF.Abs(i.Pos.Y - h.Y) < 4 && i.Pos.X > h.Left && i.Pos.X < h.Right)
                          ?? Put("stage", mid);
                if (h.Stage == null) return "couldn't set up a stage";
                foreach (var f in people.Where(f => !f.Brain.Baby).Take(4)) { h.Who.Add(f); f.Brain.JoinHappening("act", _w); }
                foreach (var f in people.Where(f => !h.Who.Contains(f))) { h.Crowd.Add(f); f.Brain.JoinHappening("audience", _w); }
                if (h.Who.Count < 2) { EndHappening(h, false); return "not enough acts"; }
                _w.News("town", $"Talent show tonight! Acts: {string.Join(", ", h.Who.Select(f => f.Name))}", 3);
                break;
            default:
                bool right = _w.Rng.NextDouble() < 0.5;
                h.StartX = right ? h.Left + 30 * s : h.Right - 30 * s;
                h.FinishX = right ? h.Right - 30 * s : h.Left + 30 * s;
                h.Flag = Put("finishflag", h.FinishX + (right ? 12 : -12) * s);
                foreach (var f in people.Where(f => !f.Brain.IsElder).Take(5)) { h.Who.Add(f); f.Brain.JoinHappening("racer", _w); }
                foreach (var f in people.Where(f => !h.Who.Contains(f))) { h.Crowd.Add(f); f.Brain.JoinHappening("fan", _w); }
                if (h.Who.Count < 2) { EndHappening(h, false); return "not enough racers"; }
                _w.News("town", $"Race day! On the starting line: {string.Join(", ", h.Who.Select(f => f.Name))}", 3);
                break;
        }
        _w.Happening = h;
        World.Log($"happening: {kind} with {string.Join(", ", h.Who.Concat(h.Crowd).Select(f => f.Name))}");
        return "started " + h.Title;
    }

    /// <summary>Someone's no longer taking part: keep the running order straight.</summary>
    void LeftHappening(Happening h, Figure f)
    {
        int i = h.Who.IndexOf(f);
        if (i >= 0)
        {
            h.Who.RemoveAt(i);
            if (h.Kind == "talent" && h.Phase == 1)
            {
                if (i < h.Turn) h.Turn--;
                else if (i == h.Turn) h.PhaseT = 0;
            }
        }
        h.Crowd.Remove(f);
        if (_w.Figures.Contains(f)) f.Brain.LeaveHappening();
    }

    /// <summary>A figure was rebuilt (resized): it keeps its place in the event.</summary>
    void SwapInHappening(Figure old, Figure f)
    {
        if (_w.Happening is not { } h) return;
        int i = h.Who.IndexOf(old); if (i >= 0) h.Who[i] = f;
        int j = h.Crowd.IndexOf(old); if (j >= 0) h.Crowd[j] = f;
        if (h.Winner == old) h.Winner = f;
        int k = h.Finished.IndexOf(old); if (k >= 0) h.Finished[k] = f;
    }

    void EndHappening(Happening h, bool done)
    {
        foreach (var f in h.Who.Concat(h.Crowd)) f.Brain.LeaveHappening();
        foreach (var it in h.Temp) if (_w.Items.Contains(it) && (it.Def.Key != "stage" || it.Holder == null)) _w.RemoveItem(it);
        _w.Happening = null;
        _happeningAt = Math.Max(_happeningAt, _clock.Elapsed.TotalSeconds + 1200);
        if (done) World.Log($"happening over: {h.Kind}{(h.Winner != null ? ", won by " + h.Winner.Name : "")}");
    }

    // ---------------- festival ----------------

    double _festFireAt;

    void FestivalStep(Happening h, double now)
    {
        if (h.T > 180) { _w.Sticker("festival"); foreach (var f in h.Crowd) f.Brain.Remember("festival", _w); EndHappening(h, true); return; }
        // Fireworks after dark.
        if ((h.Custom == null || h.Custom.Fireworks) && _w.Night > 0.35f && now > _festFireAt)
        {
            _festFireAt = now + _w.Rng.Range(0.6f, 1.6f);
            var at = new Vector2(_w.Rng.Range(h.Left, h.Right), h.Y - _w.Rng.Range(260, 520) * _w.Scale);
            var col = new[] { new Color4(1, 0.3f, 0.3f, 1), new Color4(0.3f, 0.8f, 1, 1), new Color4(1, 0.85f, 0.2f, 1), new Color4(0.7f, 0.4f, 1, 1), new Color4(0.4f, 1, 0.5f, 1) }[_w.Rng.Next(5)];
            for (int i = 0; i < 9; i++) _w.Fx.Spark(at + new Vector2(_w.Rng.Range(-30, 30), _w.Rng.Range(-30, 30)) * _w.Scale, _w.Scale * 1.4f, _w.Rng, 1.2f, col);
            World.Play(Sfx.Thud, at, 0.3f, 1.8f);
        }
    }

    // ---------------- talent show ----------------

    void TalentStep(Happening h)
    {
        if (h.Stage == null || !_w.Items.Contains(h.Stage)) { EndHappening(h, false); return; }
        switch (h.Phase)
        {
            case 0: // gathering
                if (h.PhaseT > 14) { h.Phase = 1; h.PhaseT = 0; h.Turn = 0; }
                break;
            case 1: // acts
            {
                if (h.Turn >= h.Who.Count) { h.Phase = 2; h.PhaseT = 0; break; }
                var act = h.Who[h.Turn];
                if (h.PhaseT > 13)
                {
                    float skill = new[] { SkillKind.Dancing, SkillKind.Juggling, SkillKind.Drawing, SkillKind.Climbing }.Max(k => act.Brain.Sk(k));
                    float score = 1.5f + skill * 4.5f + act.Traits.Playfulness * 1.5f + act.Traits.Sociability * 1f + (float)_w.Rng.NextDouble() * 2;
                    h.Score[act.Id] = MathF.Min(10, score);
                    foreach (var c in h.Crowd) if (_w.Rng.NextDouble() < 0.6) c.Emote(score > 7.5f ? c.Brain.V("bravo!", "ENCORE!!!", "not bad.", "that was lovely…", "a triumph") : score > 5.5f ? "*clap clap*" : c.Brain.V("hmm.", "BOOO!", "meh.", "um… nice?", "…"), 1.4f);
                    World.Log($"talent: {act.Name} scores {score:0.0}");
                    h.Turn++; h.PhaseT = 0;
                }
                break;
            }
            case 2: // results
                if (h.PhaseT < 0.1f && h.Winner == null && h.Score.Count > 0)
                {
                    int best = h.Score.OrderByDescending(kv => kv.Value).First().Key;
                    h.Winner = h.Who.FirstOrDefault(f => f.Id == best);
                    if (h.Winner != null)
                    {
                        h.Winner.Brain.WonHappening(h, _w);
                        _w.News("town", $"{h.Winner.Name} wins the talent show ({h.Score[best]:0.0}/10)!", 4, h.Winner);
                        _w.Sticker("talent");
                    }
                    foreach (var f in h.Who.Concat(h.Crowd)) if (f != h.Winner) f.Brain.Remember("talent", _w);
                }
                if (h.PhaseT > 8) EndHappening(h, true);
                break;
        }
    }

    // ---------------- race day ----------------

    void RaceStep(Happening h)
    {
        switch (h.Phase)
        {
            case 0: // to the starting line
                if (h.PhaseT > 16 || (h.PhaseT > 5 && h.Who.All(f => MathF.Abs(f.Base.X - h.StartX) < 50 * f.S && f.Grounded))) { h.Phase = 1; h.PhaseT = 0; }
                break;
            case 1: // countdown
            {
                var starter = h.Crowd.FirstOrDefault() ?? h.Who.FirstOrDefault();
                if (starter == null) break;
                int n = Math.Min(3, (int)h.PhaseT);
                if (n > h.Called)
                {
                    h.Called = n;
                    starter.Emote(n < 3 ? (3 - n).ToString() : "GO!", 0.9f);
                    World.Play(Sfx.Pip, starter.Base, 0.4f, n < 3 ? 1 : 1.6f);
                }
                if (h.PhaseT > 3) { h.Phase = 2; h.PhaseT = 0; }
                break;
            }
            case 2: // racing
                foreach (var f in h.Who)
                    if (!h.Finished.Contains(f) && (f.Base.X - h.FinishX) * MathF.Sign(h.FinishX - h.StartX) >= 0 && MathF.Abs(f.Base.Y - h.Y) < 30 * f.S)
                    {
                        h.Finished.Add(f);
                        if (h.Finished.Count == 1)
                        {
                            h.Winner = f;
                            f.Brain.WonHappening(h, _w);
                            _w.News("town", $"{f.Name} wins the race!", 4, f);
                        }
                    }
                if (h.Finished.Count == h.Who.Count || h.PhaseT > 40) { h.Phase = 3; h.PhaseT = 0; }
                break;
            case 3:
                if (!h.Wrapped)
                {
                    h.Wrapped = true;
                    _w.Sticker("race");
                    foreach (var f in h.Who.Concat(h.Crowd)) if (f != h.Winner) f.Brain.Remember("race", _w);
                    if (h.Finished.Count > 1) World.Log("race: " + string.Join(", ", h.Finished.Select((f, i) => $"{i + 1}. {f.Name}")));
                }
                if (h.PhaseT > 6) EndHappening(h, true);
                break;
        }
    }
}
