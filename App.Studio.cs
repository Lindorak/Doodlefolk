using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StickFight;

/// <summary>Bridge between the live world and the Studio page: pushes state a few times a second
/// and applies the user's edits as they make them.</summary>
sealed partial class App
{
    StudioWindow? _studio, _quick, _pop;
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

    /// <summary>Right-click on something: a small menu of quick changes for just that thing, right where you clicked.</summary>
    void ShowPop(string kind, int id)
    {
        if (_pop == null || _pop.IsDisposed)
        {
            _pop = new StudioWindow(OnStudioMessage, _debug, pop: true);
            _pop.FormClosed += (_, _) => _pop = null;
            _pop.Show();
        }
        _pop.PopAt(Cursor.Position);
        _pop.Post(JsonSerializer.Serialize(StudioState(), Json));
        _pop.Post(JsonSerializer.Serialize(new { t = "pop", kind, id }, Json));
    }

    /// <summary>A message for every open Studio window.</summary>
    void PostAll(object msg)
    {
        string json = JsonSerializer.Serialize(msg, Json);
        foreach (var win in new[] { _studio, _quick, _pop }) if (win != null && !win.IsDisposed && win.PageReady) win.Post(json);
    }

    void PushStudio(double now)
    {
        bool studio = _studio != null && _studio.PageReady && _studio.WindowState != FormWindowState.Minimized;
        bool quick = _quick != null && _quick.PageReady;
        bool pop = _pop != null && _pop.PageReady && _pop.Visible;
        if ((!studio && !quick && !pop) || now < _nextStudioPush) return;
        _nextStudioPush = now + 0.16;
        string json = JsonSerializer.Serialize(StudioState(), Json);
        if (studio) _studio!.Post(json);
        if (quick) _quick!.Post(json);
        if (pop) _pop!.Post(json);
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
            fixedColours = ItemDef.Fixed.Select(c => Settings.Hex(c)),
            lookParts = new
            {
                hat = LookJson(Look.Hats), hair = LookJson(Look.Hairs), beard = LookJson(Look.Beards), glasses = LookJson(Look.GlassesParts), shoes = LookJson(Look.ShoeParts),
                body = Look.BodyParts.Select(b => new { slot = b.Slot, key = b.Key, name = b.Name }),
            },
            catalog = ItemCatalog.All.Select(d => new
            {
                key = d.Key, name = d.Name, words = d.Words.Take(3), verbs = d.Verbs.Select(v => v.ToString()), w = d.W, h = d.H, hex = Settings.Hex(d.Color),
                shapes = d.Shapes.Select(s => new { k = s.Kind.ToString(), p = s.P, c = s.Col, w = s.W, over = s.Over, used = s.WhenUsed }),
            }),
        };
    }

    static object LookJson(LookPart[] parts) => parts.Select(p => new
    {
        key = p.Key, name = p.Name,
        front = p.Front.Select(s => new { k = s.Kind.ToString(), p = s.P, c = s.Col, w = s.W }),
        back = (p.Back ?? Array.Empty<Shape>()).Select(s => new { k = s.Kind.ToString(), p = s.P, c = s.Col, w = s.W }),
    });

    double _castsAt;
    object? _castsCache;
    object Casts()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_castsCache == null || now > _castsAt) { _castsCache = CastsJson(); _castsAt = now + 5; }
        return _castsCache;
    }

    object StudioState() => new
    {
        t = "state",
        casts = Casts(),
        game = _w.Game is { } gm ? new { kind = gm.Kind.ToString(), players = gm.Players.Count, found = gm.Found.Count, streak = gm.Streak, best = gm.Best } : null,
        figures = _w.Figures.Select(FigureJson),
        items = _w.Items.Select(i => new
        {
            id = i.Id, key = i.Def.Key, name = i.Def.Name, hex = Settings.Hex(i.Color), size = i.SizeMul, flip = i.Flip,
            tilt = MathF.Round(i.RestAngle * 180 / MathF.PI), playing = i.Playing, owner = i.OwnerId, homeable = Brain.HomeKind(i),
            users = i.Seated.Where(s => s != null).Select(s => s!.Name).Concat(i.User != null ? new[] { i.User.Name } : Array.Empty<string>()).Concat(i.Holder != null ? new[] { i.Holder.Name } : Array.Empty<string>()).Distinct(),
        }),
        pets = _w.Pets.Select(PetJson),
        sprayTool = _sprayTool,
        paper = Paper(),
        stickers = StickerBook(),
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
            sound = _settings.SoundOn, volume = _settings.SoundVolume, voices = _settings.Voices, smartFps = _settings.SmartFps, gfx = _settings.Gfx,
            weather = _settings.WeatherMode, dayNight = _settings.DayNight, celebrations = _settings.Celebrations, babies = _settings.Babies,
            petMode = _settings.PetMode, petCare = _settings.PetCare, stamina = _settings.StaminaOn, weight = _settings.WeightOn, petHelp = _settings.PetHelp, petBreeding = _settings.PetBreeding, lassoCursor = _settings.LassoCursor, jobs = _settings.Jobs, lifePace = _settings.LifePace, events = _settings.Events, calm = _settings.Calm, noticeDownloads = _settings.NoticeDownloads, noticeFrustration = _settings.NoticeFrustration, reminders = _settings.Reminders.Where(r => !r.Done).OrderBy(r => r.When).Select(r => new { id = r.Id, text = r.Text, when = r.When.ToString("ddd d MMM, HH:mm"), repeat = r.Repeat }), colourBlind = _settings.ColourBlind, pauseSchedule = _settings.PauseSchedule, pauseFrom = _settings.PauseFrom, pauseTo = _settings.PauseTo, pauseDays = _settings.PauseDays, weatherPlace = _settings.WeatherPlace, weatherStatus = RealWeatherStatus, tempC = _w.TempC, happening = _w.Happening?.Title, sky = _w.Weather.Kind.ToString(),
            noticeTyping = _settings.NoticeTyping, notifications = _settings.Notifications, wishes = _settings.Wishes, romance = _settings.Romance, screenTerrain = _settings.ScreenTerrain, screenReact = _settings.ScreenReact, screenLinks = _settings.ScreenLinks, screenMedia = _settings.ScreenMedia,
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
            family = FamilyLine(f), weightWord = f.WeightWord,
            job = (int)b.Job >= 0 ? b.Job.ToString() : "None", coins = b.Coins, stage = b.LifeStage, ageYears = World.LifePace > 0 || b.ParentIds.Count > 0 ? (int)b.AgeYears : 0, retired = b.IsElder,
            skills = Enum.GetValues<SkillKind>().Select(k => new { name = k == SkillKind.Ball ? "Ball games" : k.ToString(), v = MathF.Round(b.Sk(k), 2) }),
            birthday = b.Born.ToString("d MMMM"),
            thoughts = b.Thoughts.Select(t => new { label = t.label, share = R(t.share) }),
            diary = b.Diary.AsEnumerable().Reverse().Take(80).Select(d => new { at = d.At.ToString("yyyy-MM-ddTHH:mm:ss"), text = d.Text, mood = d.Mood }),
            decision = b.LastDecision, decidedAgo = R(b.DecidedAgo), route = b.RoutePlan,
            gender = f.Gender.ToString(),
            attraction = Enum.GetValues<Attraction>().Where(a => a is Attraction.Girls or Attraction.Boys or Attraction.Nonbinary && f.Attraction.HasFlag(a)).Select(a => a.ToString()),
            attractionText = Romance.Describe(f.Attraction),
            sweetheart = b.Sweetheart(_w)?.Name,
            crush = b.Sweetheart(_w) == null ? b.Crush(_w)?.Name : null,
            look = f.Look,
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
                annoyance = R(b.Annoyance), boredom = R(b.Boredom), loneliness = R(b.Loneliness), frustration = R(b.Frustration), weight = R(f.Weight),
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
                love = R(b.LoveFor(o)), theirLove = R(o.Brain.LoveFor(f)), attracted = b.AttractedTo(o), dating = b.Dating(o),
                shared = f.Tastes.SharedLikes(o.Tastes).Select(Tastes.Name),
                rival = b.IsRival(o), won = b.Record.GetValueOrDefault(o.Id).Won, lost = b.Record.GetValueOrDefault(o.Id).Lost,
                club = _w.ClubOf(f) is { } fc && fc.Members.Contains(o.Id),
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

    /// <summary>Home, trophies and family in a line, for the Studio.</summary>
    string FamilyLine(Figure f)
    {
        var b = f.Brain;
        var bits = new List<string>();
        if (b.Home(_w) is { } home) bits.Add($"Lives in the {home.Def.Name.ToLowerInvariant()}");
        if (b.Trophies > 0) bits.Add(b.Trophies == 1 ? "1 trophy" : $"{b.Trophies} trophies");
        var parents = _w.Figures.Where(o => b.ParentIds.Contains(o.Id)).Select(o => o.Name).ToList();
        if (parents.Count > 0) bits.Add($"Child of {string.Join(" & ", parents)}");
        var kids = _w.Figures.Where(o => o.Brain.ParentIds.Contains(f.Id)).Select(o => o.Name).ToList();
        if (kids.Count > 0) bits.Add($"Parent of {string.Join(" & ", kids)}");
        if (b.Baby) bits.Add($"{b.Grown * 100:0}% grown up");
        if (_w.ClubOf(f) is { } club) bits.Add($"Member of {club.Name}");
        if ((int)b.Job > 0) bits.Add($"Works as a {Brain.JobName(b.Job)}");
        if (b.Hobby != Hobby.None) bits.Add($"Loves {Brain.HobbyName(b.Hobby)}");
        if (b.Collection.Count > 0) bits.Add($"Collection: {b.Collection.Count} treasure{(b.Collection.Count == 1 ? "" : "s")}");
        if (b.Gifts.Count > 0) bits.Add($"Your gifts: {string.Join(", ", b.Gifts.AsEnumerable().Reverse().Select(g => g.Name.ToLowerInvariant()).Distinct().Take(4))}");
        return string.Join(" · ", bits);
    }

    void OnStudioMessage(JsonElement m)
    {
        string t = m.GetProperty("t").GetString() ?? "";
        try
        {
            switch (t)
            {
                case "ready":
                    foreach (var win in new[] { _studio, _quick, _pop })
                    {
                        if (win == null || !win.PageReady) continue;
                        win.Post(JsonSerializer.Serialize(StudioInit(), Json));
                        win.Post(JsonSerializer.Serialize(StudioState(), Json));
                    }
                    break;
                case "studio":
                    _quick?.Close();
                    _pop?.Hide();
                    if (m.TryGetProperty("page", out var sp)) OpenStudio(sp.GetString(), m.TryGetProperty("id", out var sid) ? sid.GetInt32() : 0);
                    else OpenStudio();
                    break;
                case "fig": FigureEdit(m); break;
                case "pet": PetEdit(m); break;
                case "cast":
                {
                    string cn = Str(m, "name");
                    string res = Str(m, "op") switch { "saveas" => SaveCastAs(cn), "new" => SwitchCast(cn, true), "load" => SwitchCast(cn, false), "delete" => DeleteCast(cn), _ => "" };
                    _castsAt = 0;
                    PostAll(new { t = "toast", text = res });
                    break;
                }
                case "reminder":
                {
                    string res = "";
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    var local = System.Globalization.DateTimeStyles.AssumeLocal;
                    switch (Str(m, "op"))
                    {
                        case "add":
                            res = DateTime.TryParse(Str(m, "when"), ci, local, out var when)
                                ? AddReminder(Str(m, "text"), when, Str(m, "repeat") is { Length: > 0 } rp ? rp : "none") : "Pick a date and time.";
                            break;
                        case "delete": _settings.Reminders.RemoveAll(r => r.Id == m.GetProperty("id").GetInt32()); _settings.Save(); res = "Removed."; break;
                        case "import":
                        {
                            int n = 0;
                            foreach (var e in m.GetProperty("items").EnumerateArray())
                            {
                                string et = e.GetProperty("text").GetString() ?? "";
                                if (DateTime.TryParse(e.GetProperty("when").GetString(), ci, local, out var ew)
                                    && !_settings.Reminders.Any(r => r.When == ew && r.Text == et)
                                    && AddReminder(et, ew, "none").StartsWith("Reminder set")) n++;
                            }
                            res = n == 0 ? "No upcoming events found in that file." : $"Added {n} reminder{(n == 1 ? "" : "s")} from your calendar.";
                            break;
                        }
                    }
                    PostAll(new { t = "toast", text = res });
                    break;
                }
                case "weatherPlace": SetWeatherPlace(Str(m, "v")); break;
                case "happening": World.Log("happening: " + (Str(m, "kind") == "stop" ? (_w.Happening is { } hp ? "stopped" : "none") : StartHappening(Str(m, "kind")))); if (Str(m, "kind") == "stop" && _w.Happening is { } hs) EndHappening(hs, false); break;
                case "sky":
                    if (Enum.TryParse<WeatherKind>(Str(m, "kind"), true, out var sk)) _w.Weather.Start(sk, _clock.Elapsed.TotalSeconds, _w.Rng, _w);
                    break;
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
                case "summon":
                    _studio?.Post(JsonSerializer.Serialize(new { t = "toast", text = Summon(Str(m, "text")) }, Json));
                    _quick?.Post(JsonSerializer.Serialize(new { t = "toast", text = Summon2Last }, Json));
                    break;
                case "item": ItemEdit(m); break;
                case "fight": FightEdit(m); break;
                case "setting": SettingEdit(m); break;
                case "clear":
                    EndPress();
                    if (Str(m, "what") == "balls") foreach (var p in _w.Props.ToArray()) _w.RemoveProp(p);
                    else if (Str(m, "what") == "items") foreach (var it in _w.Items.ToArray()) _w.RemoveItem(it);
                    else foreach (var f in _w.Figures.ToArray()) _w.RemoveFigure(f);
                    break;
                case "game":
                    if (Str(m, "kind") == "stop") StopGame();
                    else if (Enum.TryParse<GameKind>(Str(m, "kind"), true, out var gk))
                        PostAll(new { t = "toast", text = StartGame(gk, m.TryGetProperty("id", out var gid) ? _w.Figures.FirstOrDefault(x => x.Id == gid.GetInt32()) : null) });
                    break;
                case "photo": _quick?.Close(); _pop?.Hide(); TakePhoto(); break;
                case "record": _quick?.Close(); _pop?.Hide(); PostAll(new { t = "toast", text = StartRecording(m.TryGetProperty("seconds", out var rsec) ? rsec.GetInt32() : 10) }); break;
                case "sticker": if (Str(m, "key") == "paper") _w.Sticker("paper"); break;
                case "tourney": PostAll(new { t = "toast", text = StartTourney() }); break;
                case "adopt":
                    if (Enum.TryParse<PetKind>(Str(m, "kind"), true, out var ak)) { var np = AdoptPet(ak, m.TryGetProperty("young", out var yg) && yg.ValueKind == JsonValueKind.True); PostAll(new { t = "toast", text = $"Meet {np.Name} the {np.Species()}!" }); }
                    break;
                case "supply": if (ItemCatalog.Find(Str(m, "key")) is { } sdef) SpawnItem(sdef); break;
                case "spray": PickUpSpray(!_sprayTool); _quick?.Close(); break;
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
            case "look":
            {
                var l = f.Look;
                string v = Str(m, "v");
                switch (Str(m, "slot"))
                {
                    case "hat": l.Hat = v; break; case "hair": l.Hair = v; break; case "beard": l.Beard = v; break; case "glasses": l.Glasses = v; break;
                    case "top": l.Top = v; break; case "neck": l.Neck = v; break; case "waist": l.Waist = v; break; case "back": l.Back = v; break; case "shoes": l.Shoes = v; break;
                    case "hatColour": l.HatColour = v; break; case "hairColour": l.HairColour = v; break; case "topColour": l.TopColour = v; break;
                    case "neckColour": l.NeckColour = v; break; case "waistColour": l.WaistColour = v; break; case "backColour": l.BackColour = v; break;
                    case "shoeColour": l.ShoeColour = v; break;
                }
                break;
            }
            case "rollLook": f.Look = Look.Generate(f.Traits, _w.Rng.Next(), f.Gender); break;
            case "gender": if (Enum.TryParse<Gender>(Str(m, "v"), out var gnd)) f.Gender = gnd; break;
            case "attraction":
                f.Attraction = Attraction.None;
                foreach (var a in m.GetProperty("v").EnumerateArray()) if (Enum.TryParse<Attraction>(a.GetString(), out var at)) f.Attraction |= at;
                break;
            case "love":
                if (_w.Figures.FirstOrDefault(x => x.Id == m.GetProperty("other").GetInt32()) is { } lo) f.Brain.Love[lo.Id] = Math.Clamp(Num(m, "v"), 0, 1);
                break;
            case "party": f.Brain.ThrowParty(_w); break;
            case "breakup":
                if (f.Brain.Sweetheart(_w) is { } exs) { f.Brain.Love[exs.Id] = 0.1f; }
                break;
            case "plainLook": f.Look = new Look(); break;
            case "hunter": MakeHunter(f, m.GetProperty("v").GetBoolean()); break;
            case "job": if (Enum.TryParse<Job>(Str(m, "v"), out var job)) { f.Brain.Job = job; f.Brain.JobChanged(); } break;
            case "coins": f.Brain.Coins += (int)Num(m, "v"); f.Brain.GotCoins((int)Num(m, "v")); break;
            case "call": PostAll(new { t = "toast", text = f.Brain.CalledByUser(_w) }); break;
            case "talk": PostAll(new { t = "said", id = f.Id, text = f.Brain.Talk(Str(m, "v"), _w) }); break;
            case "dance": if (f.Mode == Mode.Control && f.Grounded) f.StartFidget(Fidget.Groove); break;
        }
    }

    void ItemEdit(JsonElement m)
    {
        string op = Str(m, "op");
        if (op == "add")
        {
            if (ItemCatalog.Find(Str(m, "key")) is { } def) SpawnItem(def, 1, null, _w.Rng.NextDouble() < 0.5);
            return;
        }
        var it = _w.Items.FirstOrDefault(x => x.Id == m.GetProperty("id").GetInt32());
        if (it == null) return;
        switch (op)
        {
            case "remove": _w.RemoveItem(it); break;
            case "size": it.SizeMul = Math.Clamp(Num(m, "v"), 0.3f, 3.5f); break;
            case "color": it.Color = Settings.ParseHex(Str(m, "hex")); break;
            case "flip": it.Flip = !it.Flip; break;
            case "tilt":
                it.RestAngle = Math.Clamp(Num(m, "v"), -90, 90) * MathF.PI / 180;
                if (MathF.Abs(it.RestAngle) > 0.05f) foreach (var f in _w.Figures) f.Brain.OnItemGone(it);   // tipped over: nobody can stay on it
                break;
            case "music": it.Playing = !it.Playing; break;
            case "owner":
            {
                int fid = m.GetProperty("v").GetInt32();
                if (_w.Figures.FirstOrDefault(x => x.Id == fid) is { } ow) ow.Brain.ClaimHome(it, _w);
                else it.OwnerId = 0;
                break;
            }
        }
    }

    void PetEdit(JsonElement m)
    {
        var p = _w.Pets.FirstOrDefault(x => x.Id == m.GetProperty("id").GetInt32());
        if (p == null) return;
        switch (Str(m, "op"))
        {
            case "remove": _w.Pets.Remove(p); break;
            case "size": p.SizeMul = Math.Clamp(Num(m, "v"), 0.5f, 2.5f); break;
            case "color": p.Color = Settings.ParseHex(Str(m, "hex")); break;
            case "rename": { var n = Str(m, "v").Trim(); if (n.Length is > 0 and <= 24) p.Name = n; break; }
            case "call": p.CallTo(_w.Cursor); break;
            default: PetCareEdit(p, Str(m, "op"), m); break;
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
            case "kick":
                // Give it a boot: up and off to a random side.
                if (p.Holder != null) { p.Holder.Carrying = null; p.Holder = null; }
                p.Vel = new Vector2(_w.Rng.Range(-500, 500), -_w.Rng.Range(900, 1300)) * _w.Scale;
                p.OnGround = false;
                World.Play(Sfx.Kick, p.Pos, 0.6f);
                break;
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
            case "theme": _settings.Theme = v.GetString() ?? "auto"; Ui.Update(_settings.Theme); break;
            case "gfxPreset": _settings.Gfx = GfxSettings.For(v.GetString() ?? "high", _settings.Gfx.Faces); Gfx.Q = _settings.Gfx; ForceFullRedraw(); break;
            case "gfx":
            {
                var g = _settings.Gfx;
                switch (Str(m, "part"))
                {
                    case "shadows": g.Shadows = v.GetInt32(); break;
                    case "drop": g.DropShadows = v.GetBoolean(); break;
                    case "shading": g.Shading = v.GetBoolean(); break;
                    case "faces": g.Faces = v.GetInt32(); break;
                    case "trails": g.Trails = v.GetBoolean(); break;
                    case "detail": g.DetailedArt = v.GetBoolean(); break;
                }
                if (Str(m, "part") != "faces") g.Preset = "custom";
                Gfx.Q = g;
                ForceFullRedraw();
                break;
            }
            case "smartFps": _settings.SmartFps = v.GetBoolean(); if (!_settings.SmartFps) ApplyFps(); break;
            case "voices": _settings.Voices = World.Voices = v.GetBoolean(); break;
            case "sound": _settings.SoundOn = v.GetBoolean(); if (_w.Sound != null) _w.Sound.Enabled = _settings.SoundOn; break;
            case "romance": _settings.Romance = v.GetBoolean(); _w.Romance = _settings.Romance; break;
            case "weather": _settings.WeatherMode = v.GetString() ?? "sometimes"; break;
            case "celebrations": _settings.Celebrations = v.GetBoolean(); break;
            case "babies": _settings.Babies = v.GetBoolean(); break;
            case "petMode": SetPetMode(v.GetBoolean()); break;
            case "petCare": _settings.PetCare = v.GetString() ?? "normal"; break;
            case "stamina": _settings.StaminaOn = v.GetBoolean(); break;
            case "weight": _settings.WeightOn = v.GetBoolean(); break;
            case "petHelp": _settings.PetHelp = v.GetBoolean(); break;
            case "petBreeding": _settings.PetBreeding = v.GetBoolean(); break;
            case "lassoCursor": _settings.LassoCursor = v.GetBoolean(); break;
            case "jobs": _settings.Jobs = v.GetBoolean(); break;
            case "lifePace": _settings.LifePace = v.GetString() ?? "off"; break;
            case "events": _settings.Events = v.GetBoolean(); break;
            case "noticeDownloads": _settings.NoticeDownloads = v.GetBoolean(); break;
            case "noticeFrustration": _settings.NoticeFrustration = v.GetBoolean(); break;
            case "calm": _settings.Calm = World.Calm = v.GetBoolean(); if (World.Calm) { foreach (var f in _w.Figures) if (f.Brain.InFight) f.Brain.CalmDown(); } break;
            case "colourBlind": _settings.ColourBlind = World.ColourBlind = v.GetBoolean(); ForceFullRedraw(); break;
            case "pauseSchedule": _settings.PauseSchedule = v.GetBoolean(); _quietCheckAt = 0; break;
            case "pauseFrom": if (v.GetString() is { Length: 5 } pf) _settings.PauseFrom = pf; _quietCheckAt = 0; break;
            case "pauseTo": if (v.GetString() is { Length: 5 } pt) _settings.PauseTo = pt; _quietCheckAt = 0; break;
            case "pauseDays": _settings.PauseDays = v.EnumerateArray().Select(x => x.GetInt32()).Where(d => d is >= 0 and <= 6).Distinct().ToList(); _quietCheckAt = 0; break;
            case "dayNight": _settings.DayNight = v.GetBoolean(); break;
            case "noticeTyping": _settings.NoticeTyping = v.GetBoolean(); break;
            case "notifications": _settings.Notifications = v.GetBoolean(); break;
            case "wishes": _settings.Wishes = v.GetBoolean(); if (!_settings.Wishes) _w.Wish = null; break;
            case "screenTerrain": _settings.ScreenTerrain = v.GetBoolean(); ApplyScreenSettings(); break;
            case "screenReact": _settings.ScreenReact = v.GetBoolean(); ApplyScreenSettings(); break;
            case "screenLinks": _settings.ScreenLinks = v.GetBoolean(); ApplyScreenSettings(); break;
            case "screenMedia": _settings.ScreenMedia = v.GetBoolean(); ApplyScreenSettings(); break;
            case "volume": _settings.SoundVolume = Math.Clamp(v.GetSingle(), 0, 1); if (_w.Sound != null) _w.Sound.Volume = _settings.SoundVolume; break;
        }
        _settings.Save();
    }
}
