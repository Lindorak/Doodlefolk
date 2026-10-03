using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using Vortice.Mathematics;
using Color = System.Drawing.Color;

namespace Doodlefolk;

/// <summary>Animation contact sheets, for polishing (Doodlefolk.exe --animsheet folder [filter]): every fidget, dance,
/// celebration, walk, run and talk for figures, and every pose for each kind of animal, each played on its own on a
/// plain floor and drawn as a strip of frames. Runs like the trailer: hidden, its own data, its own clock.</summary>
sealed partial class App
{
    bool _animSheet;
    string _animFilter = "";
    sealed record AnimJob(string Group, string Label, float Duration, Action<Figure?, Pet?> Start, Func<Figure?, Pet?, Vector2> Focus, PetKind? Pet = null, float Size = 1);
    readonly List<AnimJob> _animJobs = new();
    int _animJob = -1, _animFrame;
    readonly List<(double at, Action act)> _animLater = new();
    bool _animStarted;
    double _animJobStart, _animNextShot;
    Figure? _animFig;
    Pet? _animPet;
    Bitmap? _sheet;
    Graphics? _sheetG;
    int _sheetRow, _sheetNo;
    string _sheetGroup = "";
    const int Tile = 180, Shots = 8, SheetRows = 10, LabelW = 190;

    void AnimSheetStage()
    {
        Directory.CreateDirectory(_trailerPath);
        _w.Scale = 2f;
        _w.Env.MinHeadroom = 75 * _w.Scale;
        _w.Env.StageScreen(new Rectangle(0, 0, 3000, 1100), 48);
        _w.Env.Staged!.Clear();
        _fakeCursor = new Vector2(-5000, -5000);
        _settings.Gfx = GfxSettings.For("high");
        BuildAnimJobs();
        if (_animFilter.Length > 0) _animJobs.RemoveAll(j => !(j.Group + " " + j.Label).Contains(_animFilter, StringComparison.OrdinalIgnoreCase));
    }

    void BuildAnimJobs()
    {
        Vector2 Body(Figure? f, Pet? p) => f != null ? (f.Base + f.Jt[J.Head]) / 2 : p!.Centre;
        void Fig(string group, string label, float dur, Action<Figure> start, float size = 1) => _animJobs.Add(new(group, label, dur, (f, _) => start(f!), Body, null, size));
        void Animal(PetKind kind, string label, float dur, Action<Pet> start) => _animJobs.Add(new(kind.ToString(), label, dur, (_, p) => start(p!), Body, kind));

        foreach (var k in Enum.GetValues<Fidget>())
            Fig("Fidgets", k.ToString(), Figure.FidgetLength(k), f => f.StartFidget(k));
        string[] moves = { "Sway", "Raise the roof", "Disco point", "Robot", "Twist", "Arms overhead", "Bounce and clap", "Shimmy" };
        for (int m = 0; m < moves.Length; m++) { int mm = m; Fig("Dance", moves[m], 3, f => { f.ForceDanceMove = mm; f.StartFidget(Fidget.Groove); f.FidgetDur = 999; }); }
        foreach (var c in Enum.GetValues<CelebrateStyle>().Where(c => c != CelebrateStyle.Auto))
            Fig("Celebrate", c.ToString(), 2, f => { f.StyleChoice.Celebrate = c; f.SetAction(Act.Cheer); });
        foreach (var h in Enum.GetValues<IdleHabit>().Where(h => h != IdleHabit.Auto))
            Fig("Idle", h.ToString(), 4, f => { f.StyleChoice.Idle = h; f.SetAction(Act.Stand); });
        foreach (var ws in Enum.GetValues<WalkStyle>().Where(s => s != WalkStyle.Auto))
            Fig("Walk", ws.ToString(), 1.6f, f => { f.StyleChoice.Walk = ws; f.DesiredVX = f.Facing * f.WalkSpeed; });
        foreach (var rs in Enum.GetValues<RunStyle>().Where(s => s != RunStyle.Auto))
            Fig("Run", rs.ToString(), 1.2f, f => { f.StyleChoice.Run = rs; f.DesiredVX = f.Facing * f.RunSpeed; });
        Fig("Talk", "Talking (8 s)", 8, f => f.SetAction(Act.Talk));
        foreach (var (label, act) in new[] { ("Talk", Act.Talk), ("Wave", Act.Wave), ("High five", Act.HighFive), ("Sit (edge)", Act.SitEdge), ("Sit (floor)", Act.SitFloor), ("Lie", Act.Lie), ("Curl", Act.Curl) })
            Fig("Actions", label, 2.5f, f => f.SetAction(act));
        // Nets: a ball through the hoop, shots into the goal (high, low, a soft one), and someone walking into it.
        Item? gear = null;
        void Net(string label, string key, float dur, float size, Vector2 focus, Action<Item, Figure> start) =>
            _animJobs.Add(new("Nets", label, dur, (f, _) =>
            {
                foreach (var p in _w.Props.ToList()) _w.RemoveProp(p);
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                gear = SpawnItem(ItemCatalog.Find(key)!);
                gear!.Pos = new Vector2(1300, floor.Y); gear.Vel = Vector2.Zero; gear.OnGround = true;
                f!.PlaceAt(floor, 600);
                start(gear, f);
            }, (_, _) => gear?.Local(focus.X, focus.Y) ?? Vector2.Zero, null, size));
        Prop Ball(PropKind k, Vector2 at, Vector2 vel) { var b = SpawnProp(k); b.Pos = at; b.Vel = vel * _w.Scale; return b; }
        Net("Swish", "hoop", 1.0f, 1.3f, new(-4, 66), (h, _) => Ball(PropKind.Basketball, h.Local(-4, 100), new(0, 0)));
        Net("Swish (close)", "hoop", 0.4f, 0.75f, new(-4, 64), (h, _) => Ball(PropKind.Basketball, h.Local(-4, 84), new(0, 300)));
        Net("Off the rim", "hoop", 1.2f, 1.3f, new(-4, 66), (h, _) => Ball(PropKind.Basketball, h.Local(-14, 100), new(40, 0)));
        Net("Goal (high)", "goal", 1.2f, 1.6f, new(4, 16), (g, _) => Ball(PropKind.SoccerBall, g.Local(-40, 22), new(1500, -120)));
        Net("Goal (close)", "goal", 0.6f, 0.9f, new(10, 9), (g, _) => Ball(PropKind.SoccerBall, g.Local(-22, 14), new(1600, -60)));
        Net("Goal (low)", "goal", 1.2f, 1.6f, new(4, 16), (g, _) => Ball(PropKind.SoccerBall, g.Local(-40, 5), new(1700, 0)));
        Net("Goal (rolled in)", "goal", 2.0f, 1.6f, new(4, 16), (g, _) => Ball(PropKind.SoccerBall, g.Local(-30, 5), new(500, 0)));
        Net("Walk into it", "goal", 2.4f, 1.6f, new(4, 16), (g, f) =>
        {
            f.PlaceAt(_w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero), g.Local(-24, 0).X);
            f.SpawnT = 0.999f;
            var walker = f;
            _animLater.Add((_tFrame / (double)TFps + 0.15, () =>
            {
                walker.Brain.Puppet(99); walker.Facing = 1; walker.DesiredVX = walker.WalkSpeed;
            }));
        });
        // Water: level with the window's edge, its depth below; swimmers in it up to the shoulders, a dive out of sight.
        foreach (var (label, key, swim, dive, wide) in new[] { ("Pond", "pond", false, false, 1f), ("Pond (swim)", "pond", true, false, 1f), ("Pond (dive)", "pond", true, true, 1f),
                                                              ("Pool (swim)", "pool", true, false, 1f), ("Pool (dive)", "pool", true, true, 1f), ("Pond, wider", "pond", false, false, 1.8f) })
        {
            Item? water = null;
            _animJobs.Add(new("Water", label, 7, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                water = SpawnItem(ItemCatalog.Find(key)!);
                water!.ScaleX = wide;
                // Under the figure (moving them would start them arriving all over again).
                water.Pos = new Vector2(swim ? f!.Base.X : 1400, floor.Y); water.Vel = Vector2.Zero; water.OnGround = true;
                if (swim) f!.Brain.SwimNow(water, _w, dive);
            }, (f, _) => swim && f != null ? new Vector2(f.Base.X, f.Base.Y - 10 * _w.Scale) : water?.Local(0, 8) ?? Vector2.Zero, null, swim ? 1.1f : 1.4f * wide));
        }
        // Fetching a ball: lying still, rolling away, rolling towards them (slow down, trap it, bend and scoop it up).
        foreach (var (label, dx, vx, kind) in new[] { ("Still ball", 160f, 0f, PropKind.SoccerBall), ("Rolling away", 120f, 520f, PropKind.SoccerBall), ("Rolling away fast", 90f, 1100f, PropKind.SoccerBall),
                                                     ("Rolling towards", 520f, -420f, PropKind.SoccerBall), ("Rolling towards fast", 600f, -900f, PropKind.SoccerBall), ("Bouncing away", 100f, 450f, PropKind.Basketball),
                                                     ("Tennis ball rolling away", 80f, 600f, PropKind.TennisBall), ("Beach ball drifting", 120f, 300f, PropKind.BeachBall) })
        {
            _animJobs.Add(new("Fetch", label, 8f, (f, _) =>
            {
                foreach (var p in _w.Props.ToList()) _w.RemoveProp(p);
                var ball = SpawnProp(kind);
                ball.Pos = new Vector2(f!.Base.X + dx * _w.Scale, f.Base.Y - ball.Radius - (kind == PropKind.Basketball ? 120 * _w.Scale : 1)); ball.Vel = new Vector2(vx * _w.Scale, 0);
                f.Brain.FetchNow(ball, _w);
                float start = MathF.Abs(ball.Pos.X - f.Base.X);
                int got0 = Brain.Fetched;
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 7.9, () => Console.WriteLine($"{label}: {(Brain.Fetched > got0 ? "picked up" : "NOT picked up")}, {Brain.ScoopTries} scoops so far")));
            }, (f, _) => f != null ? new Vector2(f.Base.X + 160 * _w.Scale, f.Base.Y - 50 * _w.Scale) : Vector2.Zero, null, 7f));
        }
        // Manga symbols, one at a time (and a couple of pairs).
        foreach (var (label, m) in new[] { ("Anger vein", Manpu.Vein), ("Steam", Manpu.Steam), ("Vein + steam", Manpu.Vein | Manpu.Steam), ("Sweat drop", Manpu.SweatDrop), ("Gloom", Manpu.Gloom),
                                          ("Sparkles", Manpu.Sparkles), ("Shock", Manpu.Shock), ("Dizzy", Manpu.Dizzy), ("Blush + hearts", Manpu.Blush | Manpu.Hearts), ("Nervous", Manpu.Nervous) })
            Fig("Symbols", label, 2.4f, f => { foreach (var k in Enum.GetValues<Manpu>()) if (k != Manpu.None && m.HasFlag(k)) f.Brain.Flash(k, 99); }, 1.05f);
        // Moving furniture: a chair overhead, a couch between two.
        foreach (var (label, key, two) in new[] { ("Chair overhead", "chair", false), ("Lamp overhead", "lamp", false), ("Couch, two of them", "couch", true), ("Bed, two of them", "bed", true) })
        {
            Item? thing = null;
            _animJobs.Add(new("Moving", label, 9, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                thing = SpawnItem(ItemCatalog.Find(key)!);
                thing!.Pos = new Vector2(f!.Base.X + 90 * _w.Scale, floor.Y); thing.Vel = Vector2.Zero; thing.OnGround = true;
                if (two)
                {
                    var mate = new Figure(Palette.All[(_animJob + 3) % Palette.All.Length].Color, "Mate", _w.Scale, Personality.Random(new Random(9)), new Random(9)) { SizeMul = 1 };
                    mate.Traits.Sociability = 1; mate.Traits.Energy = 1;
                    mate.PlaceAt(floor, thing.Pos.X + 120 * _w.Scale);
                    mate.SpawnT = 0.999f;
                    _w.Figures.Add(mate);
                    mate.Brain.AddAffinity(f, 1);
                    mate.Brain.Reset();
                }
                var fig = f; var t = thing;
                _animLater.Add((_tFrame / (double)TFps + 0.6, () => fig.Brain.HaulNow(t, t.Pos.X + 260 * _w.Scale, _w)));
            }, (f, _) => thing != null ? new Vector2(thing.Pos.X, thing.Pos.Y - 40 * _w.Scale) : Vector2.Zero, null, 3.4f));
        }
        // Skipping rope: a beginner, an expert (double-unders), and long rope with two turners.
        foreach (var (label, skill, longRope) in new[] { ("Beginner", 0.15f, false), ("Expert", 0.9f, false), ("Long rope", 0.3f, true), ("Double Dutch", 0.8f, true) })
        {
            Item? rope = null;
            _animJobs.Add(new("Skipping", label, longRope ? 6 : 4, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                f!.Brain.Skills[SkillKind.Skipping] = skill;
                rope = SpawnItem(ItemCatalog.Find(longRope ? "longrope" : "jumprope")!);
                rope!.Pos = new Vector2(f.Base.X, floor.Y); rope.Vel = Vector2.Zero; rope.OnGround = true;
                if (longRope)
                    for (int i = 0; i < 2; i++)
                    {
                        var m = new Figure(Palette.All[(_animJob + 3 + i) % Palette.All.Length].Color, "Turner" + i, _w.Scale, Personality.Random(new Random(9 + i)), new Random(9 + i)) { SizeMul = 1 };
                        m.Traits.Sociability = 1; m.Traits.Energy = 1;
                        m.PlaceAt(floor, f.Base.X + (i == 0 ? -70 : 70) * _w.Scale);
                        m.SpawnT = 0.999f;
                        _w.Figures.Add(m);
                        m.Brain.AddAffinity(f, 1); f.Brain.AddAffinity(m, 1);
                        m.Brain.Skills[SkillKind.Skipping] = 0.1f;
                        m.Brain.Reset();
                    }
                var fig = f; var it = rope;
                _animLater.Add((_tFrame / (double)TFps + (longRope ? 1.6 : 0.1), () => fig.Brain.UseNow(it, Verb.Skip, _w)));
            }, (f, _) => f != null ? new Vector2(f.Base.X, f.Base.Y - 30 * _w.Scale) : Vector2.Zero, null, 1.6f));
        }
        // Hide-and-seek behind a window: tucked into the corner where one window's top goes behind another.
        foreach (var (label, peek) in new[] { ("Behind a window", false), ("Peeking round a window", true) })
        {
            _animJobs.Add(new("Hiding", label, 3, (f, _) =>
            {
                _w.Env.Staged!.Clear();
                _w.Env.Staged.Add(((IntPtr)0x7F000301, new Native.RECT { Left = 1150, Top = 600, Right = 1700, Bottom = 1050 }));   // in front
                _w.Env.Staged.Add(((IntPtr)0x7F000302, new Native.RECT { Left = 600, Top = 800, Right = 1300, Bottom = 1050 }));   // behind
                _w.Env.Refresh(_overlay.Handle);
                var top = _w.Env.Platforms.First(p => p.Hwnd == (IntPtr)0x7F000302);
                f!.PlaceAt(top, top.X2 - 1);
                f.SpawnT = 0.999f;
                f.BehindWindow = new RectangleF(1150, 600, 550, 450);
                f.Facing = peek ? -1 : 1; f.KeepFacing = true;
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 0.6, () => { fig.Brain.Puppet(99); fig.SetAction(peek ? Act.Stand : Act.Curl); }));
            }, (f, _) => f != null ? new Vector2(f.Base.X, f.Base.Y - 30 * _w.Scale) : Vector2.Zero, null, 2.2f));
        }
        // Sports: a rally over the net (both played for real), seen whole.
        foreach (var (label, key) in new[] { ("Tennis rally", "tennisnet"), ("Badminton rally", "badmintonnet") })
        {
            Item? net = null;
            _animJobs.Add(new("Sports", label, 8, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                net = SpawnItem(ItemCatalog.Find(key)!);
                net!.Pos = new Vector2(1300, floor.Y); net.Vel = Vector2.Zero; net.OnGround = true;
                var mate = new Figure(Palette.All[(_animJob + 3) % Palette.All.Length].Color, "Mate", _w.Scale, Personality.Random(new Random(7)), new Random(7)) { SizeMul = 1 };
                mate.PlaceAt(floor, 1300 + 200 * _w.Scale);
                mate.SpawnT = 0.999f;
                _w.Figures.Add(mate);
                f!.PlaceAt(floor, 1300 - 200 * _w.Scale);
                f.SpawnT = 0.999f;
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 0.2, () => fig.Brain.StartMatchWith(net, new List<Figure> { fig, mate }, _w)));
            }, (_, _) => net != null ? net.Pos + new Vector2(0, -60 * _w.Scale) : Vector2.Zero, null, 4.2f));
        }
        // A basketball game (three of them) and a three-a-side football match, seen whole.
        foreach (var (label, sport) in new[] { ("Basketball game", "basketball"), ("Football game", "soccer") })
        {
            Item? court = null;
            _animJobs.Add(new("Sports", label, 12, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                var roster = new List<Figure> { f! };
                int n = sport == "soccer" ? 6 : 3;
                for (int i = 1; i < n; i++)
                {
                    var mate = new Figure(Palette.All[(_animJob + i * 2) % Palette.All.Length].Color, "Mate" + i, _w.Scale, Personality.Random(new Random(i * 11)), new Random(i * 11)) { SizeMul = 1 };
                    mate.SpawnT = 0.999f;
                    _w.Figures.Add(mate);
                    roster.Add(mate);
                }
                List<Item> gear = new();
                if (sport == "soccer")
                {
                    foreach (var (x, flip) in new[] { (900f, true), (1700f, false) })
                    {
                        var g = SpawnItem(ItemCatalog.Find("goal")!)!;
                        g.Pos = new Vector2(x, floor.Y); g.Vel = Vector2.Zero; g.OnGround = true; g.Flip = flip; gear.Add(g);
                    }
                }
                else
                {
                    var h = SpawnItem(ItemCatalog.Find("hoop")!)!;
                    h.Pos = new Vector2(1500, floor.Y); h.Vel = Vector2.Zero; h.OnGround = true; gear.Add(h);
                }
                court = gear[0];
                for (int i = 0; i < roster.Count; i++) { roster[i].PlaceAt(floor, 1000 + i * 110); roster[i].SpawnT = 0.999f; }
                var fig = f!;
                _animLater.Add((_tFrame / (double)TFps + 0.2, () => fig.Brain.StartMatchWith(gear[0], roster, _w)));
            }, (_, _) => court != null ? (sport == "soccer" ? new Vector2(1300, court.Pos.Y - 60 * _w.Scale) : court.Pos + new Vector2(-150 * _w.Scale, -80 * _w.Scale)) : Vector2.Zero, null, sport == "soccer" ? 5f : 3.2f));
        }
        // Every hairstyle (and the afro face on, sitting), close up.
        foreach (var hair in Look.Hairs)
            Fig("Hair", hair.Name, 1.5f, f => { f.Look.Hair = hair.Key; f.Look.HairColour = "#2B1D16"; f.SetAction(Act.Talk); }, 1.0f);
        Fig("Hair", "Afro, face on", 1.5f, f => { f.Look.Hair = "afro"; f.Look.HairColour = "#2B1D16"; f.SetAction(Act.SitFront); }, 1.0f);
        // Juggling with their hands: three, four (a fountain) and five, face on.
        foreach (var (label, skill) in new[] { ("Three", 0.3f), ("Four (fountain)", 0.7f), ("Five", 0.95f) })
            Fig("Juggling", label, 2.2f, f => { f.Brain.Skills[SkillKind.Juggling] = skill; f.Brain.StartHandJuggle(_w, 60); f.JugCatch = () => true; }, 2.1f);
        Fig("Juggling", "A fumble", 4f, f => { f.Brain.Skills[SkillKind.Juggling] = 0.2f; f.Brain.StartHandJuggle(_w, 60); int n = 0; f.JugCatch = () => ++n < 4; }, 1.0f);
        // Jumping at the cursor: hung in the air at different heights above them (and a little to one side); counts the hits.
        foreach (var (label, upH, off) in new[] { ("Cursor just overhead", 0.25f, 0f), ("Cursor high", 0.8f, 0f), ("Cursor very high", 1.35f, 0f), ("Cursor high, to the side", 0.8f, 30f), ("Cursor high, drifting", 0.8f, -1f), ("Hunter, cursor high", 0.8f, -2f) })
        {
            _animJobs.Add(new("CursorJump", label, 8, (f, _) =>
            {
                float S = _w.Scale;
                var fig = f!;
                fig.Traits.Aggression = 1; fig.Traits.Bravery = 1;
                _fakeCursor = new Vector2(fig.Base.X + MathF.Max(0, off) * S, fig.Jt[J.Neck].Y - fig.Arm - upH * fig.Height);
                int hits0 = Figure.CursorHits, swings0 = Figure.CursorSwings;
                if (off == -1)   // drifting slowly back and forth over their head
                    for (int k = 1; k < 78; k++) { int kk = k; float x0 = fig.Base.X; _animLater.Add((_tFrame / (double)TFps + k * 0.1, () => _fakeCursor = new Vector2(x0 + MathF.Sin(kk * 0.1f * 1.3f) * 70 * S, _fakeCursor!.Value.Y))); }
                if (off == -2) { fig.Hunter = true; _fakeCursor = new Vector2(fig.Base.X + 200 * S, _fakeCursor!.Value.Y); }
                else fig.Brain.BoxCursorNow();
                _animLater.Add((_tFrame / (double)TFps + 7.9, () => Console.WriteLine($"{label}: {Figure.CursorHits - hits0} hits of {Figure.CursorSwings - swings0} jumps")));
            }, (f, _) => f != null ? new Vector2(f.Base.X, f.Base.Y - f.Height) : Vector2.Zero, null, 2.2f));
        }
        // Snow: lying in drifts on a window top (rounded off at the ends), footprints as someone walks through, melting by a fire.
        foreach (var (label, walk, fire) in new[] { ("Lying on a window", false, false), ("Walking through", true, false), ("Melting by a fire", false, true) })
        {
            _animJobs.Add(new("Snow", label, fire ? 8 : 3.5f, (f, _) =>
            {
                float S = _w.Scale;
                _w.Weather.ClearCover();
                _w.Env.Staged!.Clear();
                _w.Env.Staged.Add(((IntPtr)0x7F000311, new Native.RECT { Left = 700, Top = 800, Right = 1500, Bottom = 1050 }));
                _w.Env.Refresh(_overlay.Handle);
                var top = _w.Env.Platforms.First(p => p.Hwnd == (IntPtr)0x7F000311);
                f!.PlaceAt(top, walk ? top.X1 + 120 * S : top.X1 + 260 * S);
                f.SpawnT = 0.999f;
                _w.Weather.Dust(top, top.X1, top.X2, 130, _w.Rng, S);
                if (fire)
                {
                    var c = SpawnItem(ItemCatalog.Find("campfire")!);
                    c!.Pos = new Vector2(top.X1 + 200 * S, top.Y); c.Vel = Vector2.Zero; c.OnGround = true;
                }
                if (walk) { f.Facing = 1; f.DesiredVX = f.WalkSpeed; }
            }, (f, _) => f != null ? new Vector2(walk ? f.Base.X : 1100, f.Base.Y - 20 * _w.Scale) : Vector2.Zero, null, walk ? 1.2f : 2.4f));
        }
        // Hiding: in the box (peeking over the rim now and then), the barrel, the tent (out of sight).
        foreach (var key in new[] { "box", "barrel", "tent" })
        {
            Item? place = null;
            _animJobs.Add(new("Hiding", "In the " + key, 6, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                place = SpawnItem(ItemCatalog.Find(key)!);
                place!.Pos = new Vector2(1000, floor.Y); place.Vel = Vector2.Zero; place.OnGround = true;
                f!.PlaceAt(floor, 960);
                f.SpawnT = 0.999f;
                var hf = f; var hp = place;
                _animLater.Add((_tFrame / (double)TFps + 0.15, () => hf.Brain.UseNow(hp!, Verb.Hide, _w)));   // once they're fully drawn
            }, (_, _) => place?.Local(0, 18) ?? Vector2.Zero, null, 1.1f));
        }
        // Asleep in the tent (out of sight, the z's showing), and someone walking past a couch with someone on it.
        {
            Item? tent = null;
            _animJobs.Add(new("Hiding", "Asleep in the tent", 5, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                tent = SpawnItem(ItemCatalog.Find("tent")!);
                tent!.Pos = new Vector2(1000, floor.Y); tent.Vel = Vector2.Zero; tent.OnGround = true;
                f!.PlaceAt(floor, 1000); f.SpawnT = 0.999f;
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 0.15, () => fig.Brain.UseNow(tent, Verb.Lie, _w)));
            }, (_, _) => tent?.Local(0, 30) ?? Vector2.Zero, null, 1.6f));
            Item? couch = null;
            _animJobs.Add(new("Hiding", "Walking past the couch", 4, (f, _) =>
            {
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                couch = SpawnItem(ItemCatalog.Find("couch")!);
                couch!.Pos = new Vector2(1000, floor.Y); couch.Vel = Vector2.Zero; couch.OnGround = true;
                f!.PlaceAt(floor, 1000); f.SpawnT = 0.999f;
                var walker = new Figure(Palette.All[(_animJob + 4) % Palette.All.Length].Color, "Walker", _w.Scale, Personality.Random(new Random(5)), new Random(5)) { SizeMul = 1 };
                walker.PlaceAt(floor, 1000 - 200 * _w.Scale); walker.SpawnT = 0.999f;
                _w.Figures.Add(walker);
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 0.15, () => { fig.Brain.UseNow(couch, Verb.Sit, _w); walker.Brain.Puppet(99); walker.Facing = 1; walker.DesiredVX = walker.WalkSpeed; }));
            }, (_, _) => couch?.Local(0, 14) ?? Vector2.Zero, null, 1.6f));
        }
        // Sleep: a nap (nodding off sitting up, a little z) against the night (lying down in bed, big Z's).
        foreach (var (label, night, with) in new[] { ("Nap on the couch", false, "couch"), ("Nap on the floor", false, ""), ("Night in bed", true, "bed"), ("Night on the floor", true, "") })
        {
            Item? thing = null;
            _animJobs.Add(new("Sleep", label, 6, (f, _) =>
            {
                Life.HourOverride = night ? 23.5f : 14f;
                var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
                if (with.Length > 0) { thing = SpawnItem(ItemCatalog.Find(with)!); thing!.Pos = new Vector2(1060, floor.Y); thing.Vel = Vector2.Zero; thing.OnGround = true; }
                f!.PlaceAt(floor, 1000); f.SpawnT = 0.999f;
                var fig = f;
                _animLater.Add((_tFrame / (double)TFps + 0.15, () => { if (night) fig.Brain.BedNow(_w); else fig.Brain.NapNow(_w); }));
            }, (f, _) => thing?.Local(0, 14) ?? (f != null ? new Vector2(f.Base.X, f.Base.Y - 25 * _w.Scale) : Vector2.Zero), null, 1.3f));
        }
        foreach (var kind in Enum.GetValues<PetKind>())
        {
            Animal(kind, "Walk", 1.6f, p => p.PuppetWalk(1));
            Animal(kind, "Run", 1.2f, p => p.PuppetWalk(2.4f));
            foreach (var pose in Pet.PuppetPoses(kind)) Animal(kind, pose, 2.4f, p => p.PuppetAs(pose));
        }
    }

    /// <summary>Each frame of the run: start the next animation, take its shots, move on.</summary>
    void AnimSheetFrame()
    {
        double t = _tFrame / (double)TFps;
        _tFrame++;
        if (_animFilter.Equals("catalog", StringComparison.OrdinalIgnoreCase)) { CatalogSheets(); SelfTestExitCode = 0; ExitThread(); return; }
        if (_animJob < 0 || (_animFrame >= Shots && t > _animNextShot))
        {
            _animJob++;
            if (_animJob >= _animJobs.Count) { FinishSheet(); World.Log($"anim sheet: done → {_trailerPath}"); SelfTestExitCode = 0; ExitThread(); return; }
            StartAnimJob(_animJobs[_animJob], t);
            return;
        }
        for (int i = _animLater.Count - 1; i >= 0; i--) if (t >= _animLater[i].at) { var a = _animLater[i].act; _animLater.RemoveAt(i); a(); }
        var job = _animJobs[_animJob];
        if (!_animStarted && t >= _animJobStart - 0.1) { _animStarted = true; _animFig?.Brain.Puppet(9999); job.Start(_animFig, _animPet); _animJobStart = t; _animNextShot = t + 0.05; }
        if (!_animStarted || t < _animNextShot || _animFrame >= Shots) return;
        _animNextShot = _animJobStart + 0.05 + job.Duration * (_animFrame + 1) / Shots;
        var c = job.Focus(_animFig, _animPet);
        float span = (job.Pet != null ? 70 : 80) * _w.Scale * job.Size;
        var area = new RectangleF(c.X - span / 2, c.Y - span * 0.62f, span, span);
        var px = new byte[Tile * Tile * 4];
        _r.Capture(area, a =>
        {
            // Pretend windows (back to front), so things behind them can be judged.
            if (_w.Env.Staged is { Count: > 0 } wins)
                for (int k = wins.Count - 1; k >= 0; k--)
                {
                    var rc = wins[k].Item2;
                    var wr = new RectangleF(rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);
                    _r.FillPolygon(new[] { new Vector2(wr.Left, wr.Top), new Vector2(wr.Right, wr.Top), new Vector2(wr.Right, wr.Bottom), new Vector2(wr.Left, wr.Bottom) }, new Color4(0.93f, 0.95f, 0.98f, 1));
                    _r.FillPolygon(new[] { new Vector2(wr.Left, wr.Top), new Vector2(wr.Right, wr.Top), new Vector2(wr.Right, wr.Top + 26 * _w.Scale), new Vector2(wr.Left, wr.Top + 26 * _w.Scale) }, new Color4(0.78f, 0.84f, 0.93f, 1));
                    _r.Line(new Vector2(wr.Left, wr.Top), new Vector2(wr.Right, wr.Top), Ui.Pencil.A(0.8f), 1.5f * _w.Scale);
                    _r.Line(new Vector2(wr.Left, wr.Top), new Vector2(wr.Left, wr.Bottom), Ui.Pencil.A(0.8f), 1.5f * _w.Scale);
                    _r.Line(new Vector2(wr.Right, wr.Top), new Vector2(wr.Right, wr.Bottom), Ui.Pencil.A(0.8f), 1.5f * _w.Scale);
                }
            foreach (var pl in _w.Env.Platforms) _r.Line(new Vector2(pl.X1, pl.Y + 1), new Vector2(pl.X2, pl.Y + 1), Ui.Pencil.A(0.7f), 1.5f * _w.Scale);
            DrawScene(a);
        }, new Color4(0.985f, 0.975f, 0.94f, 1), Tile / span, px, Tile, Tile);
        using var tile = ToBitmap(px, Tile, Tile);
        _sheetG!.DrawImage(tile, LabelW + _animFrame * (Tile + 4), 30 + _sheetRow * (Tile + 8));
        _animFrame++;
        if (_animFrame >= Shots) _sheetRow++;
    }

    void StartAnimJob(AnimJob job, double t)
    {
        if (_sheet == null || _sheetRow >= SheetRows || job.Group != _sheetGroup) NewSheet(job.Group);
        foreach (var f in _w.Figures.ToList()) _w.RemoveFigure(f);
        _w.Pets.Clear();
        foreach (var it in _w.Items.ToList()) _w.RemoveItem(it);
        _animFig = null; _animPet = null;
        var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
        if (job.Pet is { } kind)
        {
            var p = SpawnPet(kind, quiet: true);
            p.Pos = new Vector2(900, floor.Y - 2); p.Vel = Vector2.Zero; p.Facing = 1;
            _animPet = p;
        }
        else
        {
            var f = new Figure(Palette.All[_animJob % Palette.All.Length].Color, "Anim", _w.Scale, Personality.Random(new Random(7)), new Random(7)) { SizeMul = 1 };
            f.PlaceAt(floor, 900);
            f.SpawnT = 0.999f;
            f.Facing = 1;
            _w.Figures.Add(f);
            f.Brain.Puppet(9999);
            _animFig = f;
        }
        _animStarted = false;
        _animJobStart = t + 0.5;   // a moment to settle (and finish being drawn), then the animation and its shots
        _animNextShot = double.MaxValue;
        _animFrame = 0;
        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        _sheetG!.DrawString(job.Label, font, Brushes.Black, 8, 30 + _sheetRow * (Tile + 8) + Tile / 2 - 10);
    }

    /// <summary>Every object in the catalogue, each drawn on its own in a labelled grid (for the art pass).</summary>
    void CatalogSheets()
    {
        const int cell = 200, cols = 6, rows = 6;
        var defs = ItemCatalog.All.ToList();
        var floor = _w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero);
        using var font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        for (int page = 0; page * cols * rows < defs.Count; page++)
        {
            using var sheet = new Bitmap(cols * cell, rows * (cell + 22), PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(sheet);
            g.Clear(Color.White);
            for (int i = 0; i < cols * rows && page * cols * rows + i < defs.Count; i++)
            {
                var d = defs[page * cols * rows + i];
                var it = new Item(d, _w.Scale) { Pos = new Vector2(1500, floor.Y), OnGround = true };
                float size = MathF.Max(d.W, d.H) * it.Sc;
                float span = MathF.Max(size * 1.35f, 50 * _w.Scale);
                var area = new RectangleF(it.Pos.X - span / 2, it.Pos.Y - d.H * it.Sc / 2 - span / 2, span, span);
                var px = new byte[cell * cell * 4];
                _w.Items.Add(it);
                _r.Capture(area, a =>
                {
                    _r.Line(new Vector2(area.Left, floor.Y + 1), new Vector2(area.Right, floor.Y + 1), Ui.Pencil.A(0.6f), 1.5f * _w.Scale);
                    it.Draw(_r, false, 1.0); it.Draw(_r, true, 1.0);
                }, new Color4(0.985f, 0.975f, 0.94f, 1), cell / span, px, cell, cell);
                _w.Items.Remove(it);
                using var tile = ToBitmap(px, cell, cell);
                int x = (i % cols) * cell, y = (i / cols) * (cell + 22);
                g.DrawImage(tile, x, y);
                g.DrawString(d.Key, font, Brushes.Black, x + 4, y + cell + 2);
            }
            sheet.Save(Path.Combine(_trailerPath, $"catalog-{page + 1:00}.png"), ImageFormat.Png);
        }
        World.Log($"catalog: {defs.Count} things → {_trailerPath}");
    }

    void NewSheet(string group)
    {
        FinishSheet();
        _sheetGroup = group;
        _sheet = new Bitmap(LabelW + Shots * (Tile + 4), 30 + SheetRows * (Tile + 8), PixelFormat.Format32bppArgb);
        _sheetG = Graphics.FromImage(_sheet);
        _sheetG.Clear(Color.White);
        using var font = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
        _sheetG.DrawString(group, font, Brushes.DarkRed, 8, 4);
        _sheetRow = 0;
    }

    void FinishSheet()
    {
        if (_sheet == null) return;
        _sheetG?.Dispose();
        _sheetNo++;
        _sheet.Save(Path.Combine(_trailerPath, $"{_sheetNo:00}-{_sheetGroup.ToLowerInvariant().Replace(' ', '-')}.png"), ImageFormat.Png);
        _sheet.Dispose();
        _sheet = null;
    }
}
