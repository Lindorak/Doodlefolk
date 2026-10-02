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
        Net("Goal (close)", "goal", 0.6f, 0.45f, new(6, 14), (g, _) => Ball(PropKind.SoccerBall, g.Local(-22, 14), new(1600, -60)));
        Net("Goal (low)", "goal", 1.2f, 1.6f, new(4, 16), (g, _) => Ball(PropKind.SoccerBall, g.Local(-40, 5), new(1700, 0)));
        Net("Goal (rolled in)", "goal", 2.0f, 1.6f, new(4, 16), (g, _) => Ball(PropKind.SoccerBall, g.Local(-30, 5), new(500, 0)));
        Net("Walk into it", "goal", 2.4f, 1.6f, new(4, 16), (g, f) => { f.PlaceAt(_w.Env.Platforms.First(p => p.Hwnd == IntPtr.Zero), g.Local(-24, 0).X); f.Facing = 1; f.DesiredVX = f.WalkSpeed; });
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
        if (_animJob < 0 || (_animFrame >= Shots && t > _animNextShot))
        {
            _animJob++;
            if (_animJob >= _animJobs.Count) { FinishSheet(); World.Log($"anim sheet: done → {_trailerPath}"); SelfTestExitCode = 0; ExitThread(); return; }
            StartAnimJob(_animJobs[_animJob], t);
            return;
        }
        var job = _animJobs[_animJob];
        if (!_animStarted && t >= _animJobStart - 0.1) { _animStarted = true; job.Start(_animFig, _animPet); _animJobStart = t; _animNextShot = t + 0.05; }
        if (!_animStarted || t < _animNextShot || _animFrame >= Shots) return;
        _animNextShot = _animJobStart + 0.05 + job.Duration * (_animFrame + 1) / Shots;
        var c = job.Focus(_animFig, _animPet);
        float span = (job.Pet != null ? 70 : 80) * _w.Scale * job.Size;
        var area = new RectangleF(c.X - span / 2, c.Y - span * 0.62f, span, span);
        var px = new byte[Tile * Tile * 4];
        _r.Capture(area, a =>
        {
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
