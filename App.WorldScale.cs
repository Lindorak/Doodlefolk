using System.Numerics;

namespace Doodlefolk;

/// <summary>"Size of everything": one slider that makes the whole town (figures, things, animals, balls, bubbles,
/// effects) bigger or smaller together. Figures are rebuilt keeping who they are (same identity, memories and
/// friendships, same place); things, animals and balls are put back where they were at their new size.</summary>
sealed partial class App
{
    float _baseScale = 1;
    double _rescaleAt = -1;

    float TargetScale => _baseScale * Math.Clamp(_settings.WorldScale, 0.5f, 2f);

    /// <summary>The slider moved: rebuild once it settles.</summary>
    void QueueRescale() => _rescaleAt = _clock.Elapsed.TotalSeconds + 0.6;

    void RescaleFrame(double now)
    {
        if (_rescaleAt < 0 || now < _rescaleAt) return;
        _rescaleAt = -1;
        Rescale(TargetScale);
    }

    public void Rescale(float target)
    {
        if (MathF.Abs(target - _w.Scale) < 0.001f) return;
        EndPress();
        StopGame(false);
        if (_w.Happening is { } h) EndHappening(h, false);
        // Where everything is, and what it is.
        var keepItems = _w.Items.Where(i => !i.Temporary).ToList();
        var itemPos = keepItems.Select(i => (i.Pos, i.BitesLeft)).ToList();
        var savedItems = SaveItems(evenEaten: true);
        var savedPets = SavePets();
        var petPos = _w.Pets.Select(p => p.Pos).ToList();
        var props = _w.Props.Where(p => p.Holder == null).Select(p => (p.Kind, p.SizeMul, p.Bounce, p.Color, p.Pos)).ToList();
        foreach (var it in _w.Items.ToList()) _w.RemoveItem(it);
        foreach (var p in _w.Props.ToList()) _w.RemoveProp(p);
        _w.Pets.Clear();

        _w.Scale = target;
        _w.Env.MinHeadroom = 75 * target;
        if (_settings.TownLayout == "strip") _w.Env.Strip = Math.Clamp(_settings.StripHeight, 110, 400) * target;
        _w.Env.Refresh(_overlay.Handle);

        // Figures: rebuilt in place, the same people.
        foreach (var f in _w.Figures.ToList())
        {
            var nf = ResizeFigure(f, f.SizeMul, force: true);
            nf.Visitor = f.Visitor; nf.Spirit = f.Spirit;
            foreach (var k in _viewers.Where(v => v.Value.fig == f).Select(v => v.Key).ToList()) _viewers[k] = (nf, _viewers[k].lastSeen);
            if (_guests.Remove(f, out var info)) _guests[nf] = info;
        }
        // Things, animals, balls: back where they were.
        int before = _w.Items.Count;
        RestoreItems(savedItems);
        for (int i = before, k = 0; i < _w.Items.Count && k < itemPos.Count; i++, k++) { var it = _w.Items[i]; it.Pos = itemPos[k].Pos; it.BitesLeft = itemPos[k].BitesLeft; it.Vel = Vector2.Zero; it.OnGround = false; }
        _settings.Pets = savedPets;
        RestorePets();
        for (int i = 0; i < _w.Pets.Count && i < petPos.Count; i++) { _w.Pets[i].Pos = petPos[i]; _w.Pets[i].Vel = Vector2.Zero; _w.Pets[i].Grounded = false; }
        foreach (var (kind, size, bounce, colour, pos) in props)
        {
            var p = SpawnProp(kind);
            p.SizeMul = size; p.Bounce = bounce; p.Color = colour; p.Pos = pos; p.Vel = Vector2.Zero;
        }
        if (_settings.RememberCast) SaveCast();
        World.Log($"world scale: {target:0.00} ({_settings.WorldScale:0.00}×)");
    }
}
