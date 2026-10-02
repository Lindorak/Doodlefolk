using System.Text.Json;
using System.Text.RegularExpressions;

namespace StickFight;

/// <summary>Bridge between the live world and the Studio page: pushes state a few times a second
/// and applies the user's edits as they make them.</summary>
sealed partial class App
{
    StudioWindow? _studio, _quick;
    double _nextStudioPush;
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Open (or bring forward) the Studio, optionally on a page / figure.</summary>
    void OpenStudio(string? page = null, int id = 0)
    {
        if (_studio == null || _studio.IsDisposed)
        {
            _studio = new StudioWindow(OnStudioMessage, _debug);
            _studio.FormClosed += (_, _) => _studio = null;
            _studio.Show();
        }
        else
        {
            if (_studio.WindowState == FormWindowState.Minimized) _studio.WindowState = FormWindowState.Normal;
            _studio.Activate();
        }
        if (page != null) _studio.Post(JsonSerializer.Serialize(new { t = "go", page, id }, Json));
    }

    /// <summary>Tray right-click: the little quick panel (toggle).</summary>
    void ToggleQuick()
    {
        if (_quick != null && !_quick.IsDisposed) { _quick.Close(); return; }
        _quick = new StudioWindow(OnStudioMessage, _debug, quick: true);
        _quick.FormClosed += (_, _) => _quick = null;
        _quick.Show();
        _quick.Activate();
    }

    void PushStudio(double now)
    {
        bool studio = _studio != null && _studio.PageReady && _studio.WindowState != FormWindowState.Minimized;
        bool quick = _quick != null && _quick.PageReady;
        if ((!studio && !quick) || now < _nextStudioPush) return;
        _nextStudioPush = now + 0.16;
        string json = JsonSerializer.Serialize(StudioState(), Json);
        if (studio) _studio!.Post(json);
        if (quick) _quick!.Post(json);
    }

    static string Words(string s) => Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
    static object Opts<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new { v = Convert.ToInt32(v), label = Convert.ToInt32(v) == 0 && typeof(T) != typeof(Gear) ? "Auto" : Words(v.ToString()) }).ToArray();

    static object TraitsJson(Personality p) => new { p.Energy, p.Curiosity, p.Bravery, p.Playfulness, p.Aggression, p.Sociability };

    static string ThingGroup(Thing t) => t switch
    {
        Thing.HighPlaces or Thing.Taskbar or Thing.Ledges => "Places",
        Thing.SoccerBalls or Thing.Basketballs or Thing.BeachBalls => "Toys",
        Thing.YourCursor or Thing.BeingPickedUp or Thing.BeingThrown => "You",
        _ => "Things to do",
    };

    object StudioInit()
    {
        var fps = new List<object> { new { label = $"Match monitor ({_refresh} Hz)", value = Settings.MatchMonitor } };
        var caps = new SortedSet<int> { 30, 60, 90, 120, 144, 165, 240 };
        if (_refresh / 2 >= 30) caps.Add(_refresh / 2);
        foreach (int n in caps) if (n < _refresh) fps.Add(new { label = $"{n}", value = n });
        fps.Add(new { label = "Unlimited", value = Settings.Unlimited });
        return new
        {
            t = "init",
            version = typeof(App).Assembly.GetName().Version?.ToString(3),
            palette = Palette.All.Select(p => new { name = p.Name, hex = Settings.Hex(p.Color) }),
            presets = Personality.Presets.Select(p => new { name = p.Name, blurb = p.Blurb, traits = TraitsJson(p.Traits) }),
            things = Enum.GetValues<Thing>().Select(t => new { key = t.ToString(), name = Tastes.Name(t), group = ThingGroup(t) }),
            styles = new
            {
                walk = Opts<WalkStyle>(), run = Opts<RunStyle>(), idle = Opts<IdleHabit>(), climb = Opts<ClimbStyle>(),
                jump = Opts<JumpStyle>(), fight = Opts<FightStyle>(), celebrate = Opts<CelebrateStyle>(), rope = Opts<RopeStyle>(),
            },
            gear = Enum.GetValues<Gear>().Select(g => new { v = (int)g, label = GearInfo.Describe(g) }),
            relations = Enum.GetValues<Relation>().Select(r => new { key = r.ToString(), label = FightSettings.Describe(r) }),
            deathRules = Enum.GetValues<DeathRule>().Select(d => new { key = d.ToString(), label = d switch
            {
                DeathRule.KnockOut => "Knocked out, then gets back up",
                DeathRule.Permanent => "Dies for good",
                _ => "Just knocked down",
            } }),
            propKinds = Enum.GetValues<PropKind>().Select(k => new { key = k.ToString(), name = Prop.KindName(k) }),
            fps,
            refresh = _refresh,
        };
    }

    object StudioState() => new
    {
        t = "state",
        figures = _w.Figures.Select(FigureJson),
        props = _w.Props.Select(p => new { id = p.Id, kind = p.Kind.ToString(), name = Prop.KindName(p.Kind), size = p.SizeMul, bounce = p.Bounce, hex = Settings.Hex(p.Color), held = p.Holder?.Name }),
        library = _settings.Library.Select(s => new
        {
            name = s.Name, hex = s.Color, size = s.Size, traits = TraitsJson(s.Traits), describe = s.Traits.Describe(),
            likes = s.Tastes?.Describe(),
        }),
        fight = _w.Fight,
        settings = new
        {
            fps = _settings.FpsCap, remember = _settings.RememberCast, platforms = _showPlatforms, hidden = _paused, theme = _settings.Theme,
        },
        fpsNow = _fps,
    };

    object FigureJson(Figure f)
    {
        var b = f.Brain;
        var c = f.StyleChoice;
        var st = f.Style;
        static float R(float v) => MathF.Round(v, 2);
        return new
        {
            id = f.Id,
            name = f.Name,
            hex = Settings.Hex(f.Color),
            team = f.Team,
            size = f.SizeMul,
            gear = (int)f.Gear,
            activity = b.Activity,
            feels = b.FeelingsAboutYou(),
            fond = R(b.UserFondness),
            hunter = f.Hunter,
            trust = R(b.CursorTrust),
            memories = b.Memories.AsEnumerable().Reverse().Select(m => new { what = m.What, delta = R(m.Delta), ago = MathF.Round(b.Age - m.At) }),
            hp = R(f.HP),
            ko = f.KO,
            dead = f.Dead,
            facing = f.Facing,
            pose = f.Jt.Select(j => new[] { R((j.X - f.Base.X) / f.S), R((j.Y - f.Base.Y) / f.S) }),
            mood = new
            {
                stamina = R(b.Stamina), joy = R(b.Joy), sadness = R(b.Sadness), fear = R(b.Fear),
                annoyance = R(b.Annoyance), boredom = R(b.Boredom), loneliness = R(b.Loneliness),
            },
            traits = TraitsJson(f.Traits),
            describe = f.Traits.Describe(),
            tastes = new
            {
                opinions = Enum.GetValues<Thing>().ToDictionary(t => t.ToString(), t => R(f.Tastes.Of(t))),
                fav = f.Tastes.FavoriteColour,
                hate = f.Tastes.DislikedColour,
                describe = f.Tastes.Describe(),
            },
            style = new
            {
                choice = new { walk = (int)c.Walk, run = (int)c.Run, idle = (int)c.Idle, climb = (int)c.Climb, jump = (int)c.Jump, fight = (int)c.Fight, celebrate = (int)c.Celebrate, rope = (int)c.Rope },
                resolved = new { walk = Words(st.Walk.ToString()), run = Words(st.Run.ToString()), idle = Words(st.Idle.ToString()), climb = Words(st.Climb.ToString()), jump = Words(st.Jump.ToString()), fight = Words(st.Fight.ToString()), celebrate = Words(st.Celebrate.ToString()), rope = Words(st.Rope.ToString()) },
                describe = st.Describe(),
            },
            rels = _w.Figures.Where(o => o != f).Select(o => new
            {
                id = o.Id,
                name = o.Name,
                hex = Settings.Hex(o.Color),
                mine = R(b.AffinityWith(o)),
                theirs = R(o.Brain.AffinityWith(f)),
                relation = _w.Fight.Between(f, o).ToString(),
                similarity = R(f.Tastes.Similarity(o.Tastes)),
                shared = f.Tastes.SharedLikes(o.Tastes).Select(Tastes.Name),
            }),
        };
    }

    /// <summary>Turn a figure into (or out of) a cursor hunter.</summary>
    void MakeHunter(Figure f, bool on)
    {
        f.Hunter = on;
        if (on)
        {
            f.Brain.UserFondness = -1;
            f.Tastes.Set(Thing.YourCursor, -1);
            f.Traits.Aggression = MathF.Max(f.Traits.Aggression, 0.85f);
            f.Traits.Bravery = MathF.Max(f.Traits.Bravery, 0.8f);
            f.Traits.Energy = MathF.Max(f.Traits.Energy, 0.7f);
            f.Emote("#@!", 1.4f);
        }
    }

    // ---------------- edits from the page ----------------

    void OnStudioMessage(JsonElement m)
    {
        string t = m.GetProperty("t").GetString() ?? "";
        try
        {
            switch (t)
            {
                case "ready":
                    foreach (var win in new[] { _studio, _quick })
                    {
                        if (win == null || !win.PageReady) continue;
                        win.Post(JsonSerializer.Serialize(StudioInit(), Json));
                        win.Post(JsonSerializer.Serialize(StudioState(), Json));
                    }
                    break;
                case "studio":
                    _quick?.Close();
                    OpenStudio();
                    break;
                case "fig": FigureEdit(m); break;
                case "spawn":
                {
                    Personality? traits = m.TryGetProperty("preset", out var pr) && pr.GetInt32() is int pi && pi >= 0 && pi < Personality.Presets.Length
                        ? Personality.Presets[pi].Traits.Clone() : null;
                    int? ci = m.TryGetProperty("color", out var co) && co.GetInt32() is int cidx && cidx >= 0 ? cidx : null;
                    var f = Spawn(ci, traits);
                    if (f != null && m.TryGetProperty("hunter", out var hu) && hu.ValueKind == JsonValueKind.True) MakeHunter(f, true);
                    bool quiet = m.TryGetProperty("quiet", out var q) && q.ValueKind == JsonValueKind.True;
                    if (f != null && !quiet) _studio?.Post(JsonSerializer.Serialize(new { t = "spawned", id = f.Id }, Json));
                    break;
                }
                case "lib":
                {
                    string name = Str(m, "name");
                    var saved = _settings.Library.FirstOrDefault(s => s.Name == name);
                    if (saved == null) break;
                    if (Str(m, "op") == "delete") { _settings.Library.Remove(saved); _settings.Save(); }
                    else SpawnFromLibrary(saved);
                    break;
                }
                case "prop": PropEdit(m); break;
                case "fight": FightEdit(m); break;
                case "setting": SettingEdit(m); break;
                case "clear":
                    EndPress();
                    if (Str(m, "what") == "balls") foreach (var p in _w.Props.ToArray()) _w.RemoveProp(p);
                    else foreach (var f in _w.Figures.ToArray()) _w.RemoveFigure(f);
                    break;
                case "quit": ExitThread(); break;
            }
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            World.Log($"Studio: bad message {m}: {e.Message}");
        }
        _nextStudioPush = 0;   // reflect the change right away
    }

    static string Str(JsonElement m, string k) => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static float Num(JsonElement m, string k) => m.GetProperty(k).GetSingle();

    void FigureEdit(JsonElement m)
    {
        int id = m.GetProperty("id").GetInt32();
        var f = _w.Figures.FirstOrDefault(x => x.Id == id);
        if (f == null) return;
        string op = Str(m, "op");
        switch (op)
        {
            case "rename":
                string name = Str(m, "name").Trim();
                if (name.Length > 0) f.Name = UniqueName(name.Length > 40 ? name[..40] : name, f);
                break;
            case "color": f.Color = Settings.ParseHex(Str(m, "hex")); break;
            case "size": ResizeFigure(f, Math.Clamp(Num(m, "v"), 0.4f, 3f)); break;
            case "gear": f.Gear = (Gear)m.GetProperty("v").GetInt32(); break;
            case "trait":
            {
                float v = Math.Clamp(Num(m, "v"), 0, 1);
                var p = f.Traits;
                switch (Str(m, "key"))
                {
                    case "energy": p.Energy = v; break;
                    case "curiosity": p.Curiosity = v; break;
                    case "bravery": p.Bravery = v; break;
                    case "playfulness": p.Playfulness = v; break;
                    case "aggression": p.Aggression = v; break;
                    case "sociability": p.Sociability = v; break;
                }
                break;
            }
            case "preset":
            {
                int i = m.GetProperty("v").GetInt32();
                if (i >= 0 && i < Personality.Presets.Length) f.Traits.CopyFrom(Personality.Presets[i].Traits);
                break;
            }
            case "dice": f.Traits.CopyFrom(Personality.Random(_w.Rng)); break;
            case "taste":
                if (Enum.TryParse<Thing>(Str(m, "key"), out var th)) f.Tastes.Set(th, Math.Clamp(Num(m, "v"), -1, 1));
                break;
            case "fav": f.Tastes.FavoriteColour = Str(m, "v"); break;
            case "hate": f.Tastes.DislikedColour = Str(m, "v"); break;
            case "rollTastes": f.Tastes = Tastes.Generate(f.Traits, _w.Rng); break;
            case "style":
            {
                int v = m.GetProperty("v").GetInt32();
                var c = f.StyleChoice;
                switch (Str(m, "key"))
                {
                    case "walk": c.Walk = (WalkStyle)v; break;
                    case "run": c.Run = (RunStyle)v; break;
                    case "idle": c.Idle = (IdleHabit)v; break;
                    case "climb": c.Climb = (ClimbStyle)v; break;
                    case "jump": c.Jump = (JumpStyle)v; break;
                    case "fight": c.Fight = (FightStyle)v; break;
                    case "celebrate": c.Celebrate = (CelebrateStyle)v; break;
                    case "rope": c.Rope = (RopeStyle)v; break;
                }
                break;
            }
            case "quirks": f.StyleChoice.Seed = _w.Rng.Next(); break;
            case "affinity":
                if (_w.Figures.FirstOrDefault(x => x.Id == m.GetProperty("other").GetInt32()) is { } o) f.Brain.SetAffinity(o, Num(m, "v"));
                break;
            case "fond": f.Brain.UserFondness = Num(m, "v"); break;
            case "trust": f.Brain.CursorTrust = Math.Clamp(Num(m, "v"), 0, 1); break;
            case "save": SaveToLibrary(f); break;
            case "remove": _w.RemoveFigure(f); break;
            case "heal": f.HP = 100; break;
            case "hunter": MakeHunter(f, m.GetProperty("v").GetBoolean()); break;
            case "call": _studio?.Post(JsonSerializer.Serialize(new { t = "toast", text = f.Brain.CalledByUser(_w) }, Json)); break;
        }
    }

    void PropEdit(JsonElement m)
    {
        string op = Str(m, "op");
        if (op == "add")
        {
            if (Enum.TryParse<PropKind>(Str(m, "kind"), out var k)) SpawnProp(k);
            return;
        }
        var p = _w.Props.FirstOrDefault(x => x.Id == m.GetProperty("id").GetInt32());
        if (p == null) return;
        switch (op)
        {
            case "remove": _w.RemoveProp(p); break;
            case "size": p.SizeMul = Math.Clamp(Num(m, "v"), 0.3f, 5f); break;
            case "bounce": p.Bounce = Math.Clamp(Num(m, "v"), 0, 0.95f); break;
            case "color": p.Color = Settings.ParseHex(Str(m, "hex")); break;
        }
    }

    void FightEdit(JsonElement m)
    {
        var fs = _w.Fight;
        string key = Str(m, "key");
        var v = m.GetProperty("v");
        switch (key)
        {
            case "enabled": fs.Enabled = v.GetBoolean(); break;
            case "punchCursor": fs.PunchCursor = v.GetBoolean(); break;
            case "healthBars": fs.HealthBars = v.GetBoolean(); break;
            case "frequency": fs.Frequency = Math.Clamp(v.GetSingle(), 0, 2); break;
            case "strength": fs.Strength = Math.Clamp(v.GetSingle(), 0.25f, 3); break;
            case "reviveSeconds": fs.ReviveSeconds = Math.Clamp(v.GetSingle(), 3, 120); break;
            case "onZeroHealth": if (Enum.TryParse<DeathRule>(v.GetString(), out var dr)) fs.OnZeroHealth = dr; break;
            case "sameColour": if (Enum.TryParse<Relation>(v.GetString(), out var r1)) fs.SameColour = r1; break;
            case "differentColour": if (Enum.TryParse<Relation>(v.GetString(), out var r2)) fs.DifferentColour = r2; break;
            case "pair":
            {
                string pk = FightSettings.PairKey(Str(m, "a"), Str(m, "b"));
                if (Enum.TryParse<Relation>(Str(m, "rel"), out var r3) && r3 != Relation.Default) fs.Pairs[pk] = r3;
                else fs.Pairs.Remove(pk);
                break;
            }
        }
        _settings.Save();
    }

    void SettingEdit(JsonElement m)
    {
        var v = m.GetProperty("v");
        switch (Str(m, "key"))
        {
            case "fps": _settings.FpsCap = v.GetInt32(); ApplyFps(); break;
            case "remember": _settings.RememberCast = v.GetBoolean(); break;
            case "platforms": _showPlatforms = v.GetBoolean(); break;
            case "hidden": _paused = v.GetBoolean(); break;
            case "theme": _settings.Theme = v.GetString() ?? "auto"; break;
        }
        _settings.Save();
    }
}
